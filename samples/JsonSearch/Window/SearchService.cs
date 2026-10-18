// Handwired source: src/json-search/demo.ts (searchOnGpu, documentOnGpu, the CPU worker and the RegExp worker that the
// page asks for work, and the answers that arrive between two frames) and demo-worker.ts. The search service of the
// window: three worker threads, so that the window thread never waits for a search.
// - The search thread runs the newest search on the GPU searcher or on the CPU searcher. A newer search cancels the
//   token of the older one. When there is a GPU, the first CPU search makes the CPU searcher over the display kinds
//   that the service holds, so it runs no highlighter. A cancellation also stops this build.
// - The document thread gives the display kinds of one document, which the service read one time from the
//   searcher, and its marks from the search program over that one document on a CPU backend of its own (the
//   Handwired CPU searcher with documents: 'window'). The Searcher runs each search over each document, so a window
//   search of one document on the CPU searcher takes as long as a full search.
// - The .NET Regex thread times the newest pattern and scope with the limit of 10 s. A newer timing cancels it.
// Each answer carries the ID of its request, and the window drops the answers of the requests that it replaced. The
// progress of a search goes into the queue of answers and does not call the window thread, so no thread waits for
// another one while it holds the evaluation lock of the searcher. Each exception of a request becomes a Failed
// answer with a sentence.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Sharpwired.Gpu;

namespace JsonSearch;

/// <summary>An answer of the search service. Id is the ID of the request.</summary>
public abstract record SearchAnswer(int Id)
{
    /// <summary>The counts of a search: a progress answer, or the answer for each document.</summary>
    public sealed record Found(int Id, SearchResult.Searched Result) : SearchAnswer(Id);

    /// <summary>The display kinds of one document, and its marks (default with no search).</summary>
    public sealed record Document(int Id, int Index, ImmutableArray<byte> Kinds, ImmutableArray<float> Marks) : SearchAnswer(Id);

    /// <summary>The .NET Regex time of a pattern and a scope.</summary>
    public sealed record DotNetTime(int Id, JsonSearch.DotNetTime Time) : SearchAnswer(Id);

    /// <summary>The request stopped with an error. Message is a sentence for the line.</summary>
    public sealed record Failed(int Id, string Message) : SearchAnswer(Id);
}

/// <summary>The work that the window asks for. Each call returns at once, and the answers arrive through Poll.</summary>
public interface ISearchService
{
    /// <summary>The number of documents.</summary>
    int DocumentCount { get; }

    /// <summary>True when the GPU backend exists.</summary>
    bool HasGpu { get; }

    /// <summary>Search the pattern over each document. A newer search replaces this one.</summary>
    void Search(int id, string pattern, SearchScope scope, SearchBackend backend);

    /// <summary>Stop the search in progress, for a pattern that the window does not search.</summary>
    void CancelSearch();

    /// <summary>The display kinds of one document, and its marks for the pattern, or no marks for a null pattern.</summary>
    void ShowDocument(int id, int index, string? pattern, SearchScope scope);

    /// <summary>The .NET Regex time of the pattern and the scope. A newer timing replaces this one.</summary>
    void TimeDotNet(int id, string pattern, SearchScope scope);

    /// <summary>Stop the .NET Regex timing in progress, for a pattern that the window does not time.</summary>
    void CancelDotNet();

    /// <summary>The answers that arrived since the last call, in the order of arrival.</summary>
    IReadOnlyList<SearchAnswer> Poll();
}

/// <summary>The search service of the window over one GPU searcher, one CPU searcher, or both.</summary>
public sealed class SearchService : ISearchService, IDisposable
{
    /// <summary>The limit of a .NET Regex timing.</summary>
    public static readonly TimeSpan DotNetLimit = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<string> texts;
    private readonly ImmutableArray<ImmutableArray<byte>> kinds;
    private readonly Searcher? gpuSearcher;
    private readonly ILoopBackend cpuBackend;
    private readonly int? documentsPerSlice;
    private readonly ConcurrentQueue<SearchAnswer> answers = new();
    private readonly DocumentMarks marks = new();
    private readonly LatestJobWorker<SearchJob> searchWorker;
    private readonly LatestJobWorker<DocumentJob> documentWorker;
    private readonly LatestJobWorker<SearchJob> dotNetWorker;
    private Searcher? cpuSearcher;
    private int disposed;

    private SearchService(IReadOnlyList<string> texts, ImmutableArray<ImmutableArray<byte>> kinds, Searcher? gpuSearcher, Searcher? cpuSearcher, ILoopBackend cpuBackend, int? documentsPerSlice)
    {
        this.texts = texts;
        this.kinds = kinds;
        this.gpuSearcher = gpuSearcher;
        this.cpuSearcher = cpuSearcher;
        this.cpuBackend = cpuBackend;
        this.documentsPerSlice = documentsPerSlice;
        searchWorker = new LatestJobWorker<SearchJob>("json search", RunSearch);
        documentWorker = new LatestJobWorker<DocumentJob>("json document", RunDocument);
        dotNetWorker = new LatestJobWorker<SearchJob>("json .NET Regex", RunDotNet);
    }

    public int DocumentCount => texts.Count;

    public bool HasGpu => gpuSearcher is not null;

    /// <summary>The highlighter runs of the CPU searcher, or null before it exists, for tests.</summary>
    internal int? CpuHighlighterRuns => Volatile.Read(ref cpuSearcher)?.HighlighterRuns;

    /// <summary>
    /// Make the searcher of the GPU backend, or of the CPU backend when there is no GPU, which runs the highlighter
    /// over each document. Then read the display kinds of each document, and start the workers. The service does not
    /// own the backends.
    /// </summary>
    public static SearchService Create(IReadOnlyList<string> texts, ILoopBackend? gpu, ILoopBackend cpu) => Create(texts, gpu, cpu, null);

    /// <summary>Create with a chosen number of documents in each slice of the CPU searcher, for tests.</summary>
    internal static SearchService Create(IReadOnlyList<string> texts, ILoopBackend? gpu, ILoopBackend cpu, int? documentsPerSlice)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(cpu);
        var first = gpu is null ? Searcher.Create(texts, cpu, documentsPerSlice) : Searcher.Create(texts, gpu);
        try
        {
            var flat = first.Kinds(0, texts.Count);
            var kinds = Enumerable.Range(0, texts.Count)
                .Select(row => ImmutableArray.Create(flat.AsSpan(row * Kinds.Capacity, Kinds.Capacity)))
                .ToImmutableArray();
            return gpu is null
                ? new SearchService(texts, kinds, null, first, cpu, documentsPerSlice)
                : new SearchService(texts, kinds, first, null, cpu, documentsPerSlice);
        }
        catch
        {
            first.Dispose();
            throw;
        }
    }

    public void Search(int id, string pattern, SearchScope scope, SearchBackend backend)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ThrowIfDisposed();
        searchWorker.Post(new SearchJob(id, pattern, scope, backend));
    }

    public void CancelSearch()
    {
        ThrowIfDisposed();
        searchWorker.Cancel();
    }

    public void ShowDocument(int id, int index, string? pattern, SearchScope scope)
    {
        ThrowIfDisposed();
        documentWorker.Post(new DocumentJob(id, index, pattern, scope));
    }

    public void TimeDotNet(int id, string pattern, SearchScope scope)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ThrowIfDisposed();
        dotNetWorker.Post(new SearchJob(id, pattern, scope, SearchBackend.Cpu));
    }

    public void CancelDotNet()
    {
        ThrowIfDisposed();
        dotNetWorker.Cancel();
    }

    public IReadOnlyList<SearchAnswer> Poll()
    {
        ThrowIfDisposed();
        var arrived = new List<SearchAnswer>();
        while (answers.TryDequeue(out var answer)) arrived.Add(answer);
        return arrived;
    }

    /// <summary>
    /// Cancel the work in progress, wait for the three threads to stop, and then release the searchers. The searchers
    /// wait for their evaluation lock, so they go only after the threads.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        searchWorker.Dispose();
        documentWorker.Dispose();
        dotNetWorker.Dispose();
        cpuSearcher?.Dispose();
        gpuSearcher?.Dispose();
        marks.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) == 1, this);

    private void RunSearch(SearchJob job, CancellationToken cancel)
    {
        Guard(job.Id, () =>
        {
            var searcher = job.Backend == SearchBackend.Gpu
                ? gpuSearcher ?? throw new InvalidOperationException("this window has no GPU")
                : cpuSearcher ??= Searcher.CreateFromKinds(texts, cpuBackend, kinds, gpuSearcher!.Validation, documentsPerSlice, cancel);
            // The progress of the last slice equals the result that Search returns, so only the result goes in the queue.
            var progress = new QueueProgress(result =>
            {
                if (result is SearchResult.Searched searched && searched.DocumentsDone < texts.Count) answers.Enqueue(new SearchAnswer.Found(job.Id, searched));
            });
            switch (searcher.Search(new SearchRequest(job.Pattern, job.Scope, 0, 0), progress, cancel))
            {
                case SearchResult.Searched searched when !cancel.IsCancellationRequested:
                    answers.Enqueue(new SearchAnswer.Found(job.Id, searched));
                    break;
                case SearchResult.SyntaxError error:
                    answers.Enqueue(new SearchAnswer.Failed(job.Id, Sentences.Syntax(error.Message, error.Position)));
                    break;
                case SearchResult.TooLarge large:
                    answers.Enqueue(new SearchAnswer.Failed(job.Id, string.Join(" ", large.Errors.Select(Sentences.Limit))));
                    break;
            }
        });
    }

    private void RunDocument(DocumentJob job, CancellationToken cancel)
    {
        Guard(job.Id, () =>
        {
            if (job.Index < 0 || job.Index >= texts.Count) throw new ArgumentException($"document {job.Index} is not one of the {texts.Count} documents");
            var documentKinds = kinds[job.Index];
            var documentMarks = job.Pattern is null ? default : marks.Of(texts[job.Index], documentKinds, job.Pattern, job.Scope);
            if (!cancel.IsCancellationRequested) answers.Enqueue(new SearchAnswer.Document(job.Id, job.Index, documentKinds, documentMarks));
        });
    }

    private void RunDotNet(SearchJob job, CancellationToken cancel)
    {
        Guard(job.Id, () =>
        {
            var result = DotNetBaseline.Count(texts, kinds, job.Pattern, job.Scope, DotNetLimit, cancel);
            answers.Enqueue(new SearchAnswer.DotNetTime(job.Id, new DotNetTime(result.OverLimit ? DotNetLimit.TotalMilliseconds : result.Milliseconds, result.OverLimit)));
        });
    }

    // Run the work of a request, and give each exception as a Failed answer. A cancellation gives no answer.
    private void Guard(int id, Action work)
    {
        try
        {
            work();
        }
        catch (OperationCanceledException)
        {
        }
        catch (PatternSyntaxException error)
        {
            answers.Enqueue(new SearchAnswer.Failed(id, Sentences.Syntax(error.Message, error.Position)));
        }
        catch (Exception error)
        {
            answers.Enqueue(new SearchAnswer.Failed(id, Sentences.Failure(error.Message.TrimEnd('.'))));
        }
    }

    private sealed record SearchJob(int Id, string Pattern, SearchScope Scope, SearchBackend Backend);

    private sealed record DocumentJob(int Id, int Index, string? Pattern, SearchScope Scope);

    // A progress that puts each report in the queue at once, on the thread of the search.
    private sealed class QueueProgress(Action<SearchResult> report) : IProgress<SearchResult>
    {
        public void Report(SearchResult value) => report(value);
    }
}

/// <summary>
/// The marks of one document: the search program at the smallest capacity that holds the pattern, over that one
/// document, on a CPU backend of its own. The runners are made at the first use of each capacity.
/// </summary>
internal sealed class DocumentMarks : IDisposable
{
    private readonly CpuLoopBackend backend = new();
    private readonly ILoopRunner?[] runners = new ILoopRunner?[SearchCapacity.All.Length];

    /// <summary>The 512 marks of the text for the pattern and the scope.</summary>
    /// <exception cref="PatternSyntaxException">The pattern is outside the language.</exception>
    /// <exception cref="ArgumentException">No program holds the pattern.</exception>
    public ImmutableArray<float> Of(string text, ImmutableArray<byte> kinds, string pattern, SearchScope scope)
    {
        var (compiled, index, early) = Searcher.Resolve(pattern);
        switch (early)
        {
            case SearchResult.SyntaxError error:
                throw new PatternSyntaxException(error.Message, error.Position);
            case SearchResult.TooLarge large:
                throw new ArgumentException(string.Join(" ", large.Errors.Select(Sentences.Limit)));
        }
        var program = Searcher.ProgramAt(index);
        var runner = runners[index] ??= backend.CreateRunner(program.Loop);
        var shared = program.InitialShared();
        program.WritePattern(shared, compiled!);
        program.WriteScope(shared, scope);
        runner.WriteShared(shared);
        var cells = new float[DocumentCells.CellsPerDocument];
        DocumentCells.Write(cells, text);
        var kindCells = new float[Kinds.Capacity];
        for (var position = 0; position < kindCells.Length; position++) kindCells[position] = kinds[position];
        using var documents = backend.Upload(cells, 1, DocumentCells.CellsPerDocument);
        using var kindBuffer = backend.Upload(kindCells, 1, Kinds.Capacity);
        var run = runner.Run(new LoopRequest([documents, kindBuffer], 1, [null], 0, 1));
        if (!run.Faults.IsEmpty) throw new InvalidOperationException($"the search of the document failed: {run.Faults[0].Message}");
        return run.WindowPositionValues;
    }

    public void Dispose()
    {
        foreach (var runner in runners) runner?.Dispose();
        backend.Dispose();
    }
}
