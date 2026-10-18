// Handwired source: tests/json-search/searcher.test.ts (the searcher on the CPU and on the GPU). That file covers the
// validation, the kinds, a search for each capacity, the syntax error and the pattern that is too large. It also
// covers the gate that gives a result only to the newest request. The searcher over 100,000 generated documents on
// the CPU, with the example patterns of the page in their scopes and in All. The checks are the capacity of each
// pattern, the totals, and the window marks of the first 64 documents. The test compares these marks against the
// automaton over the display kinds of JsonReference. The GPU part is in
// GpuSearcherTests below. The times of the 100,000-document runs go to the test output.

using System.Collections.Immutable;
using System.Diagnostics;
using Sharpwired.Gpu;
using Sharpwired.Gpu.Tests;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

/// <summary>The 100,000 documents and the CPU searches over them, which the CPU, the GPU and the baseline tests share.</summary>
internal static class LargeRun
{
    public const int Count = 100_000;
    public const int WindowCount = 64;

    /// <summary>The four example patterns in their scopes and in All, and the capacity that holds each.</summary>
    public static readonly ImmutableArray<(string Pattern, SearchScope Scope, int States)> Searches =
    [
        ("[a-z0-9.]+@[a-z.]+", SearchScope.StringValues, 8),
        ("[a-z0-9.]+@[a-z.]+", SearchScope.All, 8),
        ("[a-z]+Id", SearchScope.Keys, 8),
        ("[a-z]+Id", SearchScope.All, 8),
        (@"\d\d\d\d-\d\d-\d\d", SearchScope.All, 16),
        ("([a-z]+)+x", SearchScope.All, 8),
    ];

    public static readonly Lazy<string[]> Texts = new(() => [.. Documents.Generate(Count).Select(document => document.Text)]);

    /// <summary>The results of the CPU searcher, its validation, its kinds, and its create time.</summary>
    public static readonly Lazy<CpuResults> Cpu = new(() =>
    {
        var watch = Stopwatch.StartNew();
        using var backend = new CpuLoopBackend();
        using var searcher = Searcher.Create(Texts.Value, backend);
        var createMs = watch.Elapsed.TotalMilliseconds;
        var results = Searches.Select(search =>
            Assert.IsType<SearchResult.Searched>(searcher.Search(new SearchRequest(search.Pattern, search.Scope, 0, WindowCount)))).ToImmutableArray();
        var flat = searcher.Kinds(0, Count);
        var kinds = Enumerable.Range(0, Count).Select(row => ImmutableArray.Create(flat.AsSpan(row * Kinds.Capacity, Kinds.Capacity))).ToImmutableArray();
        return new CpuResults(results, searcher.Validation, flat, kinds, createMs);
    });

    internal sealed record CpuResults(
        ImmutableArray<SearchResult.Searched> Results,
        ImmutableArray<DocumentSummary> Validation,
        ImmutableArray<byte> FlatKinds,
        ImmutableArray<ImmutableArray<byte>> Kinds,
        double CreateMs);
}

public sealed class SearcherTests(ITestOutputHelper output)
{
    private static readonly string[] SmallTexts = [.. Documents.Generate(300).Select(document => document.Text)];

    [Fact]
    public void The_cpu_searcher_over_100000_documents_finds_the_matches_of_the_examples_at_their_capacity()
    {
        var cpu = LargeRun.Cpu.Value;
        var texts = LargeRun.Texts.Value;
        output.WriteLine($"[cpu searcher] create over {LargeRun.Count} documents (cells, highlighter, programs): {cpu.CreateMs:F0} ms");
        var windowTexts = texts.Take(LargeRun.WindowCount).ToArray();
        var windowReferences = windowTexts.Select(JsonReference.Highlight).ToArray();
        for (var index = 0; index < LargeRun.Searches.Length; index++)
        {
            var (pattern, scope, states) = LargeRun.Searches[index];
            var result = cpu.Results[index];
            Assert.Equal(states, result.States);
            Assert.Equal(LargeRun.Count, result.DocumentsDone);
            Assert.Equal(LargeRun.Count, result.MatchCounts.Length);
            Assert.Equal((long)result.MatchCounts.Sum(count => (double)count), result.Matches);
            Assert.Equal(result.MatchCounts.Count(count => count > 0), result.DocumentsWithMatch);
            Assert.True(result.DocumentsWithMatch > 100, $"{pattern} in {scope}: {result.DocumentsWithMatch} documents with a match");
            var reference = SearchTests.ReferenceSearch(windowTexts, windowReferences, PatternCompiler.Compile(pattern), SearchProgram.KindsOf(scope));
            Assert.Equal(reference.Marks, result.WindowMarks.ToArray());
            Assert.Equal(reference.Counts, result.MatchCounts.Take(LargeRun.WindowCount).ToArray());
            output.WriteLine(
                $"[cpu search] {pattern} in {scope}, {result.States} states: {result.DocumentsWithMatch} of {LargeRun.Count} documents match, {result.Matches} matches, "
                + $"{result.Times.TotalMs:F0} ms");
        }
    }

    [Fact]
    public void The_validation_and_the_kinds_equal_the_highlighter_runner()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var searcher = Searcher.Create(SmallTexts, backend, documentsPerSlice: 70);
        var (summary, kinds) = HighlighterTests.RunCpu(SmallTexts, CpuStageBackend.Compiled);
        Assert.Equal(SmallTexts.Length, searcher.Validation.Length);
        for (var row = 0; row < SmallTexts.Length; row++)
        {
            var expected = new DocumentSummary(summary[row * 4] == 1, (int)summary[(row * 4) + 1], (ErrorKind)(int)summary[(row * 4) + 3], summary[(row * 4) + 2] == 1);
            Assert.Equal(expected, searcher.Validation[row]);
        }
        Assert.Contains(searcher.Validation, summaryOf => !summaryOf.Valid);
        // A range that crosses the slices.
        var read = searcher.Kinds(65, 80);
        Assert.Equal(80 * Kinds.Capacity, read.Length);
        for (var index = 0; index < read.Length; index++) Assert.Equal(kinds[(65 * Kinds.Capacity) + index], read[index]);
        Assert.Throws<ArgumentException>(() => searcher.Kinds(290, 11));
    }

    [Fact]
    public void A_searcher_over_the_kinds_of_another_searcher_runs_no_highlighter_and_gives_its_results()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var whole = Searcher.Create(SmallTexts, backend);
        Assert.Equal(1, whole.HighlighterRuns);
        var flat = whole.Kinds(0, SmallTexts.Length);
        var kinds = Enumerable.Range(0, SmallTexts.Length).Select(row => ImmutableArray.Create(flat.AsSpan(row * Kinds.Capacity, Kinds.Capacity))).ToArray();
        using var reused = Searcher.CreateFromKinds(SmallTexts, backend, kinds, whole.Validation, 70, CancellationToken.None);
        Assert.Equal(0, reused.HighlighterRuns);
        Assert.Equal(whole.Validation, reused.Validation);
        Assert.Equal(flat.ToArray(), reused.Kinds(0, SmallTexts.Length).ToArray());
        var request = new SearchRequest("[a-z]+", SearchScope.StringValues, 60, 20);
        var left = Assert.IsType<SearchResult.Searched>(reused.Search(request));
        var right = Assert.IsType<SearchResult.Searched>(whole.Search(request));
        Assert.Equal(right.MatchCounts, left.MatchCounts.ToArray());
        Assert.Equal(right.WindowMarks, left.WindowMarks.ToArray());

        // A cancelled token stops the build, and a count that is not the text count throws.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Searcher.CreateFromKinds(SmallTexts, backend, kinds, whole.Validation, 70, cancelled.Token));
        Assert.Throws<ArgumentException>(() => Searcher.CreateFromKinds(SmallTexts, backend, kinds[1..], whole.Validation, 70, CancellationToken.None));
    }

    [Fact]
    public void A_search_over_slices_equals_one_slice_and_the_window_crosses_the_slices()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var sliced = Searcher.Create(SmallTexts, backend, documentsPerSlice: 70);
        using var whole = Searcher.Create(SmallTexts, backend);
        var request = new SearchRequest("[a-z]+", SearchScope.StringValues, 60, 20);
        var left = Assert.IsType<SearchResult.Searched>(sliced.Search(request));
        var right = Assert.IsType<SearchResult.Searched>(whole.Search(request));
        Assert.Equal(right.MatchCounts, left.MatchCounts.ToArray());
        Assert.Equal(right.WindowMarks, left.WindowMarks.ToArray());
        Assert.Equal(20 * Kinds.Capacity, left.WindowMarks.Length);
        Assert.True(left.Matches > 0);
        var reference = SearchTests.ReferenceSearch(
            SmallTexts[60..80], [.. SmallTexts[60..80].Select(JsonReference.Highlight)], PatternCompiler.Compile("[a-z]+"), SearchProgram.KindsOf(SearchScope.StringValues));
        Assert.Equal(reference.Marks, left.WindowMarks.ToArray());
    }

    [Fact]
    public void A_syntax_error_and_a_pattern_of_33_states_return_their_results_and_run_no_search()
    {
        using var backend = new CpuLoopBackend();
        using var searcher = Searcher.Create(SmallTexts, backend);
        var runs = searcher.SearchRuns;

        var syntax = Assert.IsType<SearchResult.SyntaxError>(searcher.Search(new SearchRequest("ab(c", SearchScope.All, 0, 0)));
        var thrown = Assert.Throws<PatternSyntaxException>(() => PatternCompiler.Compile("ab(c"));
        Assert.Equal(thrown.Message, syntax.Message);
        Assert.Equal(thrown.Position, syntax.Position);

        var tooLarge = Assert.IsType<SearchResult.TooLarge>(searcher.Search(new SearchRequest(new string('a', 33), SearchScope.All, 0, 0)));
        var error = Assert.Single(tooLarge.Errors);
        Assert.Equal("the pattern needs 34 states, and the program holds 32", error.Message);
        Assert.Equal(runs, searcher.SearchRuns);

        Assert.IsType<SearchResult.Searched>(searcher.Search(new SearchRequest("abc", SearchScope.All, 0, 0)));
        Assert.Equal(runs + 1, searcher.SearchRuns);
    }

    [Fact]
    public async Task A_request_that_a_newer_request_replaces_returns_no_result()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var searcher = Searcher.Create(SmallTexts, backend, documentsPerSlice: 50);

        // A newer request that needs no search replaces the request in progress.
        SearchResult? newer = null;
        var replaced = searcher.Search(
            new SearchRequest("[a-z]+", SearchScope.All, 0, 0),
            new Callback(_ => newer ??= searcher.Search(new SearchRequest("(", SearchScope.All, 0, 0))));
        Assert.Null(replaced);
        Assert.IsType<SearchResult.SyntaxError>(newer);

        // A newer request that waits for the request in progress gets the result, and the older one gets none.
        Task<SearchResult?>? waiting = null;
        var older = searcher.Search(
            new SearchRequest("[a-z]+", SearchScope.All, 0, 0),
            new Callback(_ =>
            {
                if (waiting is not null) return;
                var before = searcher.Requests;
                waiting = Task.Run(() => searcher.Search(new SearchRequest("[0-9]+", SearchScope.All, 0, 0)));
                Assert.True(SpinWait.SpinUntil(() => searcher.Requests > before, TimeSpan.FromSeconds(30)), "the newer request did not start");
            }));
        Assert.Null(older);
        var result = Assert.IsType<SearchResult.Searched>(await waiting!);
        Assert.Equal(SmallTexts.Length, result.DocumentsDone);
        Assert.True(result.Matches > 0);
    }

    [Fact]
    public async Task The_times_of_a_search_start_after_the_wait_for_the_search_in_progress()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var searcher = Searcher.Create(SmallTexts, backend, documentsPerSlice: 50);
        // The first progress report of the older search holds the evaluation lock for 800 ms after the newer request starts.
        Task<SearchResult?>? waiting = null;
        searcher.Search(
            new SearchRequest("[a-z]+", SearchScope.All, 0, 0),
            new Callback(_ =>
            {
                if (waiting is not null) return;
                var before = searcher.Requests;
                waiting = Task.Run(() => searcher.Search(new SearchRequest("[0-9]+", SearchScope.All, 0, 0)));
                Assert.True(SpinWait.SpinUntil(() => searcher.Requests > before, TimeSpan.FromSeconds(30)), "the newer request did not start");
                Thread.Sleep(800);
            }));
        var result = Assert.IsType<SearchResult.Searched>(await waiting!);
        Assert.True(result.Times.TotalMs < 400, $"TotalMs {result.Times.TotalMs}");
        Assert.True(result.Times.WriteMs <= result.Times.TotalMs);
    }

    [Fact]
    public void The_cpu_search_reports_its_progress_and_stops_at_a_cancellation()
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var searcher = Searcher.Create(SmallTexts, backend, documentsPerSlice: 50);
        var reports = new List<SearchResult.Searched>();
        var full = Assert.IsType<SearchResult.Searched>(searcher.Search(
            new SearchRequest("[a-z]+", SearchScope.Keys, 0, 0),
            new Callback(report => reports.Add(Assert.IsType<SearchResult.Searched>(report)))));
        Assert.Equal([50, 100, 150, 200, 250, 300], reports.Select(report => report.DocumentsDone));
        foreach (var report in reports)
        {
            Assert.Equal(full.MatchCounts.Take(report.DocumentsDone), report.MatchCounts.Take(report.DocumentsDone));
            Assert.Equal(full.MatchCounts.Take(report.DocumentsDone).Count(count => count > 0), report.DocumentsWithMatch);
        }

        using var cancel = new CancellationTokenSource();
        var stopped = Assert.IsType<SearchResult.Searched>(searcher.Search(
            new SearchRequest("[a-z]+", SearchScope.Keys, 0, 0),
            new Callback(report =>
            {
                if (((SearchResult.Searched)report).DocumentsDone >= 100) cancel.Cancel();
            }),
            cancel.Token));
        Assert.Equal(100, stopped.DocumentsDone);
        Assert.Equal(full.MatchCounts.Take(100), stopped.MatchCounts.Take(100));
    }

    [Fact]
    public void A_bad_request_throws_and_a_disposed_searcher_throws()
    {
        using var backend = new CpuLoopBackend();
        var searcher = Searcher.Create(SmallTexts, backend);
        Assert.Throws<ArgumentException>(() => searcher.Search(new SearchRequest("a", SearchScope.All, 290, 11)));
        Assert.Throws<ArgumentException>(() => searcher.Search(new SearchRequest("a", SearchScope.All, -1, 1)));
        Assert.Throws<ArgumentException>(() => Searcher.Create([], backend));
        searcher.Dispose();
        Assert.Throws<ObjectDisposedException>(() => searcher.Search(new SearchRequest("a", SearchScope.All, 0, 0)));
        Assert.Throws<ObjectDisposedException>(() => searcher.Kinds(0, 1));
    }

    private sealed class Callback(Action<SearchResult> action) : IProgress<SearchResult>
    {
        public void Report(SearchResult value) => action(value);
    }
}

[Collection(GpuCollection.Name)]
public sealed class GpuSearcherTests(GpuFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void The_gpu_searcher_over_100000_documents_gives_the_cpu_counts_on_each_document()
    {
        var cpu = LargeRun.Cpu.Value;
        var texts = LargeRun.Texts.Value;
        fixture.ForEachDevice(output, device =>
        {
            var watch = Stopwatch.StartNew();
            using var gpu = new GpuLoopBackend(device);
            using var searcher = Searcher.Create(texts, gpu);
            output.WriteLine($"[gpu searcher] create over {LargeRun.Count} documents (cells, upload, highlighter, four shaders): {watch.Elapsed.TotalMilliseconds:F0} ms");
            Assert.Equal(cpu.Validation.ToArray(), searcher.Validation.ToArray());
            Assert.Equal(cpu.FlatKinds.Take(LargeRun.WindowCount * Kinds.Capacity), searcher.Kinds(0, LargeRun.WindowCount));
            for (var index = 0; index < LargeRun.Searches.Length; index++)
            {
                var (pattern, scope, states) = LargeRun.Searches[index];
                var expected = cpu.Results[index];
                var request = new SearchRequest(pattern, scope, 0, LargeRun.WindowCount);
                var first = Assert.IsType<SearchResult.Searched>(searcher.Search(request));
                var result = Assert.IsType<SearchResult.Searched>(searcher.Search(request));
                Assert.Equal(states, result.States);
                Assert.Equal(LargeRun.Count, result.DocumentsDone);
                for (var row = 0; row < LargeRun.Count; row++)
                {
                    if (expected.MatchCounts[row] != result.MatchCounts[row])
                    {
                        Assert.Fail($"{pattern} in {scope}: document {row}: the CPU gives {expected.MatchCounts[row]}, the GPU {result.MatchCounts[row]}");
                    }
                }
                Assert.Equal(expected.WindowMarks, result.WindowMarks.ToArray());
                Assert.Equal(expected.Matches, result.Matches);
                Assert.Equal(expected.DocumentsWithMatch, result.DocumentsWithMatch);
                Assert.Equal(first.MatchCounts, result.MatchCounts.ToArray());
                output.WriteLine(
                    $"[gpu search] {pattern} in {scope}, {result.States} states: {result.DocumentsWithMatch} of {LargeRun.Count} documents match, {result.Matches} matches; "
                    + $"first search {first.Times.TotalMs:F1} ms, second {result.Times.TotalMs:F1} ms; the CPU {expected.Times.TotalMs:F0} ms");
            }
            // A document that failed a kernel check and passed on the CPU gets the CPU values. No value is subnormal, so there must be none.
            Assert.Equal(0, gpu.ReplayPasses);
        });
    }
}
