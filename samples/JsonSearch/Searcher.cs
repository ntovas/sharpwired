// Handwired source: src/json-search/searcher.ts (createGpuSearcher and createCpuSearcher: the cells of each document,
// the highlighter one time at creation with the display kinds that stay on the backend, one search program for each
// capacity, resolvePattern, summarize, validationOf, and the gate that gives a result only to the newest request).
// One searcher for both backends: the runners come from an ILoopBackend.
// Differences from Handwired:
// - Search is synchronous. One evaluation runs at a time. A request that a newer request replaced, before or while
//   it ran, returns null. A request that needs no search (a syntax error, a pattern that is too large) also
//   replaces the requests before it.
// - The documents are in slices of documents, each with its own document buffer and kinds buffer. On the GPU one
//   slice holds all documents. On the CPU a slice holds 16,384 documents, so that a search reports the counts of
//   each slice as progress and stops at a cancellation between slices (and between blocks inside a slice).
// - The times of a search are one LoopTimes. WriteMs is the pattern compile, the write of the shared cells, and the
//   write time of each run. SubmitMs, WaitMs and ReadMs are the sums over the slices. TotalMs is the pattern compile
//   plus the time from the lock to the copy of the last count. The wait for the evaluation lock is not in a time. On
//   the CPU, WaitMs is the sum of the block loops.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Sharpwired.Gpu;

namespace JsonSearch;

/// <summary>A search: the pattern, the scope, and the documents whose marks come back.</summary>
public sealed record SearchRequest(string Pattern, SearchScope Scope, int WindowFirst, int WindowCount);

/// <summary>The result of one search request.</summary>
public abstract record SearchResult
{
    private SearchResult()
    {
    }

    /// <summary>
    /// The search ran. MatchCounts holds one count for each document; a document that the search did not reach holds
    /// 0. WindowMarks holds 512 marks for each window document (0 outside a match, 1 inside, 2 at the first code
    /// point of a match).
    /// </summary>
    public sealed record Searched(
        int States,
        ImmutableArray<float> MatchCounts,
        int DocumentsDone,
        int DocumentsWithMatch,
        long Matches,
        ImmutableArray<float> WindowMarks,
        LoopTimes Times) : SearchResult;

    /// <summary>The pattern is outside the language. Position is the code point position of the error.</summary>
    public sealed record SyntaxError(string Message, int Position) : SearchResult;

    /// <summary>The pattern needs more than the largest program holds: one error for each part.</summary>
    public sealed record TooLarge(ImmutableArray<CapacityError> Errors) : SearchResult;
}

/// <summary>
/// Documents that the highlighter validated one time, and the search of a pattern over them on one backend.
/// </summary>
public sealed class Searcher : IDisposable
{
    private const int Positions = JsonSearch.Kinds.Capacity;
    private const int SummaryValues = 4;
    private const int CpuSliceSize = 16_384;
    // The largest number of documents of one read of the kinds.
    private const int ReadChunk = 8_192;

    private static readonly Lazy<LoopProgram> HighlighterLoop = new(HighlighterProgram.Build);

    private static readonly ImmutableArray<Lazy<SearchProgram>> SearchPrograms =
        [.. SearchCapacity.All.Select(capacity => new Lazy<SearchProgram>(() => SearchProgram.Build(capacity)))];

    private readonly ILoopBackend backend;
    private readonly Slice[] slices;
    private readonly ILoopRunner highlighter;
    private readonly ILoopRunner[] searchRunners;
    private readonly object evaluation = new();
    private long latest;
    private int searchRuns;
    private bool disposed;

    private Searcher(ILoopBackend backend, Slice[] slices, ILoopRunner highlighter, ILoopRunner[] searchRunners, ImmutableArray<DocumentSummary> validation)
    {
        this.backend = backend;
        this.slices = slices;
        this.highlighter = highlighter;
        this.searchRunners = searchRunners;
        Validation = validation;
        DocumentCount = slices.Sum(slice => slice.Count);
    }

    /// <summary>The number of documents.</summary>
    public int DocumentCount { get; }

    /// <summary>The search program of the capacity at this index of SearchCapacity.All, which each searcher shares.</summary>
    internal static SearchProgram ProgramAt(int index) => SearchPrograms[index].Value;

    /// <summary>What the highlighter says about each document, in document order.</summary>
    public ImmutableArray<DocumentSummary> Validation { get; }

    /// <summary>The number of search runs on the backend, one for each slice of each search.</summary>
    internal int SearchRuns => Volatile.Read(ref searchRuns);

    /// <summary>The number of highlighter runs of the build: one for each slice, or 0 for a searcher over given kinds.</summary>
    internal int HighlighterRuns { get; private init; }

    /// <summary>The number of requests so far, with the requests that needed no search.</summary>
    internal long Requests => Interlocked.Read(ref latest);

    /// <summary>
    /// Write the cells of the texts, upload them to the backend, make the highlighter runner and the three search
    /// runners, and run the highlighter one time. The display kinds stay on the backend. The searcher does not own
    /// the backend.
    /// </summary>
    /// <exception cref="ArgumentException">No text.</exception>
    public static Searcher Create(IReadOnlyList<string> texts, ILoopBackend backend) => Create(texts, backend, null);

    /// <summary>Create with a chosen number of documents in each slice, for tests.</summary>
    internal static Searcher Create(IReadOnlyList<string> texts, ILoopBackend backend, int? documentsPerSlice) =>
        Build(texts, backend, documentsPerSlice, null, default, CancellationToken.None);

    /// <summary>
    /// Make a searcher over the display kinds and the validation of another searcher of the same texts. It runs no
    /// highlighter. The window service uses it for its CPU searcher when the GPU searcher holds the kinds. A
    /// cancellation stops the build between slices.
    /// </summary>
    /// <exception cref="ArgumentException">No text, a kinds count or a validation count that is not the text count, or a document with other than 512 kinds.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    internal static Searcher CreateFromKinds(
        IReadOnlyList<string> texts, ILoopBackend backend, IReadOnlyList<ImmutableArray<byte>> kinds, ImmutableArray<DocumentSummary> validation, int? documentsPerSlice, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Count != texts.Count || validation.IsDefault || validation.Length != texts.Count)
        {
            throw new ArgumentException($"the kinds and the validation must hold one entry for each of the {texts.Count} texts", nameof(kinds));
        }
        for (var row = 0; row < kinds.Count; row++)
        {
            if (kinds[row].Length != Positions) throw new ArgumentException($"the kinds of document {row} hold {kinds[row].Length} values, and a document has {Positions}", nameof(kinds));
        }
        return Build(texts, backend, documentsPerSlice, kinds, validation, cancel);
    }

    // Make the slices and the runners. With no kinds, run the highlighter over each slice. With kinds, upload them.
    private static Searcher Build(
        IReadOnlyList<string> texts, ILoopBackend backend, int? documentsPerSlice, IReadOnlyList<ImmutableArray<byte>>? givenKinds, ImmutableArray<DocumentSummary> givenValidation, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(backend);
        if (texts.Count == 0) throw new ArgumentException("a searcher holds at least one document", nameof(texts));
        var sliceSize = documentsPerSlice ?? (backend is CpuLoopBackend ? CpuSliceSize : texts.Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(sliceSize, 1, nameof(documentsPerSlice));

        var slices = new List<Slice>();
        var runners = new List<ILoopRunner>();
        var parallel = new ParallelOptions { CancellationToken = cancel };
        try
        {
            var highlighter = backend.CreateRunner(HighlighterLoop.Value);
            runners.Add(highlighter);
            foreach (var program in SearchPrograms) runners.Add(backend.CreateRunner(program.Value.Loop));
            var summaries = new float[texts.Count * SummaryValues];
            var highlighterRuns = 0;
            for (var first = 0; first < texts.Count; first += sliceSize)
            {
                cancel.ThrowIfCancellationRequested();
                var count = Math.Min(sliceSize, texts.Count - first);
                var cells = new float[count * DocumentCells.CellsPerDocument];
                Parallel.For(0, count, parallel, row => DocumentCells.Write(cells.AsSpan(row * DocumentCells.CellsPerDocument, DocumentCells.CellsPerDocument), texts[first + row]));
                var documents = backend.Upload(cells, count, DocumentCells.CellsPerDocument);
                IDocumentBuffer kinds;
                try
                {
                    kinds = givenKinds is null ? backend.Allocate(count, Positions, (float)DisplayKind.PastEnd) : UploadKinds(backend, givenKinds, first, count, parallel);
                }
                catch
                {
                    documents.Dispose();
                    throw;
                }
                slices.Add(new Slice(first, count, documents, kinds));
                if (givenKinds is not null) continue;
                var run = highlighter.Run(new LoopRequest([documents], count, [kinds], 0, 0));
                highlighterRuns++;
                if (!run.Faults.IsEmpty) throw new InvalidOperationException($"the highlighter failed on document {first + run.Faults[0].Document}: {run.Faults[0].Message}");
                run.DocumentValues.CopyTo(summaries.AsSpan(first * SummaryValues));
            }
            var validation = givenKinds is not null ? givenValidation : Enumerable.Range(0, texts.Count).Select(row => new DocumentSummary(
                summaries[row * SummaryValues] == 1,
                (int)summaries[(row * SummaryValues) + 1],
                (ErrorKind)(int)summaries[(row * SummaryValues) + 3],
                summaries[(row * SummaryValues) + 2] == 1)).ToImmutableArray();
            return new Searcher(backend, [.. slices], highlighter, [.. runners.Skip(1)], validation) { HighlighterRuns = highlighterRuns };
        }
        catch
        {
            foreach (var slice in slices) slice.Dispose();
            foreach (var runner in runners) runner.Dispose();
            throw;
        }
    }

    // The kinds of the documents first..first + count - 1 as one buffer of 512 cells for each document.
    private static IDocumentBuffer UploadKinds(ILoopBackend backend, IReadOnlyList<ImmutableArray<byte>> kinds, int first, int count, ParallelOptions parallel)
    {
        var cells = new float[count * Positions];
        Parallel.For(0, count, parallel, row =>
        {
            var source = kinds[first + row];
            var target = cells.AsSpan(row * Positions, Positions);
            for (var position = 0; position < Positions; position++) target[position] = source[position];
        });
        return backend.Upload(cells, count, Positions);
    }

    /// <summary>
    /// Search the pattern over each document. Returns null when a newer request replaced this one, before or while
    /// it ran. The progress gets a Searched result after each slice, with the documents done so far. A cancellation
    /// stops the search and returns the documents done.
    /// </summary>
    /// <exception cref="ArgumentException">The window is not inside the documents.</exception>
    public SearchResult? Search(SearchRequest request, IProgress<SearchResult>? progress = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Pattern, nameof(request));
        ThrowIfDisposed();
        if (request.WindowFirst < 0 || request.WindowCount < 0 || (long)request.WindowFirst + request.WindowCount > DocumentCount)
        {
            throw new ArgumentException($"the window {request.WindowFirst}, {request.WindowCount} documents must lie inside the {DocumentCount} documents", nameof(request));
        }
        var compile = Stopwatch.StartNew();
        var resolved = Resolve(request.Pattern);
        if (resolved.Result is { } early)
        {
            Interlocked.Increment(ref latest);
            return early;
        }
        var compileMs = compile.Elapsed.TotalMilliseconds;
        var token = Interlocked.Increment(ref latest);
        lock (evaluation)
        {
            ThrowIfDisposed();
            if (token != Interlocked.Read(ref latest)) return null;
            // The time of the search starts here, after the wait for the lock, and adds the time of the pattern compile.
            var total = Stopwatch.StartNew();
            var result = Evaluate(resolved.Compiled!, resolved.Index, request, compileMs, total, () => token == Interlocked.Read(ref latest), progress, cancel);
            return token == Interlocked.Read(ref latest) ? result : null;
        }
    }

    /// <summary>The display kinds of the documents, 512 for each document, document after document.</summary>
    /// <exception cref="ArgumentException">The range is not inside the documents.</exception>
    public ImmutableArray<byte> Kinds(int firstDocument, int count)
    {
        ThrowIfDisposed();
        if (firstDocument < 0 || count < 0 || (long)firstDocument + count > DocumentCount)
        {
            throw new ArgumentException($"the documents {firstDocument}, {count} documents must lie inside the {DocumentCount} documents", nameof(firstDocument));
        }
        var kinds = new byte[count * Positions];
        lock (evaluation)
        {
            ThrowIfDisposed();
            foreach (var slice in slices)
            {
                var start = Math.Max(firstDocument, slice.First);
                var end = Math.Min(firstDocument + count, slice.First + slice.Count);
                for (var chunk = start; chunk < end; chunk += ReadChunk)
                {
                    var size = Math.Min(ReadChunk, end - chunk);
                    var values = backend.Read(slice.Kinds, chunk - slice.First, size);
                    var offset = (chunk - firstDocument) * Positions;
                    for (var index = 0; index < values.Length; index++) kinds[offset + index] = (byte)values[index];
                }
            }
        }
        return ImmutableCollectionsMarshal.AsImmutableArray(kinds);
    }

    public void Dispose()
    {
        lock (evaluation)
        {
            if (disposed) return;
            disposed = true;
            foreach (var runner in searchRunners) runner.Dispose();
            highlighter.Dispose();
            foreach (var slice in slices) slice.Dispose();
        }
    }

    /// <summary>The compiled pattern and the index of the smallest capacity that holds it, or the result that says why there is none.</summary>
    internal static (CompiledPattern? Compiled, int Index, SearchResult? Result) Resolve(string pattern)
    {
        CompiledPattern compiled;
        try
        {
            compiled = PatternCompiler.Compile(pattern);
        }
        catch (PatternSyntaxException error)
        {
            return (null, -1, new SearchResult.SyntaxError(error.Message, error.Position));
        }
        for (var index = 0; index < SearchCapacity.All.Length; index++)
        {
            if (PatternCompiler.CapacityErrors(compiled, SearchCapacity.All[index]).IsEmpty) return (compiled, index, null);
        }
        return (null, -1, new SearchResult.TooLarge(PatternCompiler.CapacityErrors(compiled, SearchCapacity.All[^1])));
    }

    private SearchResult.Searched Evaluate(
        CompiledPattern compiled, int capacityIndex, SearchRequest request, double compileMs, Stopwatch total, Func<bool> current, IProgress<SearchResult>? progress, CancellationToken cancel)
    {
        var program = SearchPrograms[capacityIndex].Value;
        var runner = searchRunners[capacityIndex];
        var shared = program.InitialShared();
        program.WritePattern(shared, compiled);
        program.WriteScope(shared, request.Scope);
        runner.WriteShared(shared);
        double writeMs = compileMs + total.Elapsed.TotalMilliseconds, submitMs = 0, waitMs = 0, readMs = 0;
        var counts = new float[DocumentCount];
        var marks = new float[request.WindowCount * Positions];
        var done = 0;
        foreach (var slice in slices)
        {
            if (cancel.IsCancellationRequested) break;
            var windowStart = Math.Max(request.WindowFirst, slice.First);
            var windowEnd = Math.Min(request.WindowFirst + request.WindowCount, slice.First + slice.Count);
            var windowCount = Math.Max(0, windowEnd - windowStart);
            var windowFirst = windowCount > 0 ? windowStart - slice.First : 0;
            Interlocked.Increment(ref searchRuns);
            var run = runner.Run(new LoopRequest([slice.Documents, slice.Kinds], slice.Count, [null], windowFirst, windowCount), null, cancel);
            if (!run.Faults.IsEmpty) throw new InvalidOperationException($"the search failed on document {slice.First + run.Faults[0].Document}: {run.Faults[0].Message}");
            run.DocumentValues.AsSpan(0, run.DocumentsDone).CopyTo(counts.AsSpan(slice.First));
            if (windowCount > 0) run.WindowPositionValues.CopyTo(marks.AsSpan((windowStart - request.WindowFirst) * Positions));
            writeMs += run.Times.WriteMs;
            submitMs += run.Times.SubmitMs;
            waitMs += run.Times.WaitMs;
            readMs += run.Times.ReadMs;
            done = slice.First + run.DocumentsDone;
            if (run.DocumentsDone < slice.Count) break;
            if (progress is not null && done < DocumentCount && current())
            {
                progress.Report(Result(program, counts, done, marks, new LoopTimes(writeMs, submitMs, waitMs, readMs, compileMs + total.Elapsed.TotalMilliseconds)));
            }
        }
        var result = Result(program, counts, done, marks, new LoopTimes(writeMs, submitMs, waitMs, readMs, compileMs + total.Elapsed.TotalMilliseconds));
        if (progress is not null && done == DocumentCount && current()) progress.Report(result);
        return result;
    }

    private static SearchResult.Searched Result(SearchProgram program, float[] counts, int done, float[] marks, LoopTimes times)
    {
        var matches = 0L;
        var withMatch = 0;
        for (var row = 0; row < done; row++)
        {
            matches += (long)counts[row];
            if (counts[row] > 0) withMatch++;
        }
        return new SearchResult.Searched(program.States, [.. counts], done, withMatch, matches, [.. marks], times);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    // A range of documents with its cells and its display kinds on the backend.
    private sealed class Slice(int first, int count, IDocumentBuffer documents, IDocumentBuffer kinds) : IDisposable
    {
        public int First { get; } = first;

        public int Count { get; } = count;

        public IDocumentBuffer Documents { get; } = documents;

        public IDocumentBuffer Kinds { get; } = kinds;

        public void Dispose()
        {
            Documents.Dispose();
            Kinds.Dispose();
        }
    }
}
