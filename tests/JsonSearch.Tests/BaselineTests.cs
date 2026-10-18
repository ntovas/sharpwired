// Handwired source: src/json-search/plain-search.ts and src/json-search/README.md ("What the search finds": for the
// page's own patterns, the RegExp counts equal the program counts for every document; a|abab finds 2 matches in
// xabab where the program finds 1). The .NET Regex baseline: its counts equal the program counts for the four
// example patterns over 100,000 documents, its runs of keys and string values, its code points past U+FFFF, its
// limit, and the bench table of SearchBench and of JsonSearch.App --bench.

using System.Collections.Immutable;
using Sharpwired.Gpu;
using Sharpwired.Gpu.Tests;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

public sealed class BaselineTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static ImmutableArray<ImmutableArray<byte>> KindsOf(IReadOnlyList<string> texts)
    {
        var (_, kinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        return [.. Enumerable.Range(0, texts.Count).Select(row => kinds.AsSpan(row * Kinds.Capacity, Kinds.Capacity).ToArray().Select(kind => (byte)kind).ToImmutableArray())];
    }

    // The program counts of one pattern over the texts, from a CPU searcher.
    private static SearchResult.Searched ProgramCounts(IReadOnlyList<string> texts, string pattern, SearchScope scope)
    {
        using var backend = new CpuLoopBackend();
        using var searcher = Searcher.Create(texts, backend);
        return Assert.IsType<SearchResult.Searched>(searcher.Search(new SearchRequest(pattern, scope, 0, 0)));
    }

    [Fact]
    public void The_dotnet_counts_equal_the_program_counts_for_the_examples_over_100000_documents()
    {
        var cpu = LargeRun.Cpu.Value;
        var texts = LargeRun.Texts.Value;
        var compared = 0;
        for (var index = 0; index < LargeRun.Searches.Length; index++)
        {
            var (pattern, scope, _) = LargeRun.Searches[index];
            var program = cpu.Results[index];
            var baseline = DotNetBaseline.Count(texts, cpu.Kinds, pattern, scope, Limit);
            output.WriteLine(
                $"[dotnet regex] {pattern} in {scope}: {baseline.DocumentsWithMatch} documents with a match, {baseline.Matches} matches, {baseline.Milliseconds:F0} ms"
                + (baseline.OverLimit ? $", over the limit after {baseline.MatchCounts.Length} documents" : "")
                + $"; the program on the CPU {program.Times.TotalMs:F0} ms");
            if (baseline.OverLimit)
            {
                // Only ([a-z]+)+x can backtrack past the limit. The documents before the stop still agree.
                Assert.Equal("([a-z]+)+x", pattern);
            }
            else
            {
                Assert.Equal(LargeRun.Count, baseline.MatchCounts.Length);
                Assert.Equal(program.Matches, baseline.Matches);
                Assert.Equal(program.DocumentsWithMatch, baseline.DocumentsWithMatch);
            }
            for (var row = 0; row < baseline.MatchCounts.Length; row++)
            {
                if (baseline.MatchCounts[row] != program.MatchCounts[row])
                {
                    Assert.Fail($"{pattern} in {scope}: document {row}: .NET Regex finds {baseline.MatchCounts[row]}, the program {program.MatchCounts[row]}");
                }
                compared++;
            }
        }
        Assert.True(compared >= 5 * LargeRun.Count, $"{compared} document counts compared");
    }

    [Fact]
    public void A_pattern_where_the_first_alternative_wins_gives_the_documented_difference()
    {
        string[] texts = ["xabab", "xabx"];
        var kinds = KindsOf(texts);
        var program = ProgramCounts(texts, "a|abab", SearchScope.All);
        var baseline = DotNetBaseline.Count(texts, kinds, "a|abab", SearchScope.All, Limit);
        Assert.Equal([1f, 1f], program.MatchCounts.ToArray());
        Assert.Equal([2, 1], baseline.MatchCounts.ToArray());
        Assert.Equal(3, baseline.Matches);
        Assert.Equal(2, baseline.DocumentsWithMatch);
        Assert.False(baseline.OverLimit);
    }

    // Task 8: the window cancels the .NET Regex count of a pattern that the person left. The count stops between
    // documents and throws OperationCanceledException.
    [Fact]
    public void A_cancelled_count_throws_operation_cancelled()
    {
        string[] texts = ["[\"aId\"]", "[\"bId\"]"];
        var kinds = KindsOf(texts);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => DotNetBaseline.Count(texts, kinds, "[a-z]+Id", SearchScope.All, Limit, cancel.Token));
        Assert.Equal(2, DotNetBaseline.Count(texts, kinds, "[a-z]+Id", SearchScope.All, Limit, CancellationToken.None).Matches);
    }

    [Fact]
    public void A_scope_counts_inside_each_run_of_its_kinds()
    {
        string[] texts = ["{\"userId\": \"anId\", \"n\": [\"xId\", 7]}", "[\"aId\"]"];
        var kinds = KindsOf(texts);
        foreach (var (scope, expected) in new[] { (SearchScope.All, new[] { 3, 1 }), (SearchScope.Keys, new[] { 1, 0 }), (SearchScope.StringValues, new[] { 2, 1 }) })
        {
            var baseline = DotNetBaseline.Count(texts, kinds, "[a-z]+Id", scope, Limit);
            Assert.Equal(expected, baseline.MatchCounts.ToArray());
            Assert.Equal(expected.Select(count => (float)count), ProgramCounts(texts, "[a-z]+Id", scope).MatchCounts.ToArray());
        }
        // A key run ends at its closing quote, so a match cannot join a key and the next text.
        string[] joined = ["{\"ab\":\"cd\"}"];
        Assert.Equal([0], DotNetBaseline.Count(joined, KindsOf(joined), "b\":", SearchScope.Keys, Limit).MatchCounts.ToArray());
        Assert.Equal([1], DotNetBaseline.Count(joined, KindsOf(joined), "b\":", SearchScope.All, Limit).MatchCounts.ToArray());
    }

    [Fact]
    public void The_baseline_reads_code_points_and_the_first_512_of_each_document()
    {
        string[] texts = ["\"a😀b東京\"", "{\"😀\": \"😀x\"}", "\"" + new string('q', 600) + "\""];
        var kinds = KindsOf(texts);
        var dot = DotNetBaseline.Count(texts, kinds, ".", SearchScope.All, Limit);
        Assert.Equal([7, 11, 512], dot.MatchCounts.ToArray());
        Assert.Equal([1, 0, 0], DotNetBaseline.Count(texts, kinds, "😀b", SearchScope.StringValues, Limit).MatchCounts.ToArray());
        Assert.Equal([0, 1, 0], DotNetBaseline.Count(texts, kinds, "\"😀\"", SearchScope.Keys, Limit).MatchCounts.ToArray());
        Assert.Equal([0, 1, 0], DotNetBaseline.Count(texts, kinds, "[^a]x", SearchScope.StringValues, Limit).MatchCounts.ToArray());
        foreach (var pattern in new[] { ".", "😀b", "[^a]x", "q+" })
        {
            Assert.Equal(
                ProgramCounts(texts, pattern, SearchScope.All).MatchCounts.Select(count => (int)count),
                DotNetBaseline.Count(texts, kinds, pattern, SearchScope.All, Limit).MatchCounts.ToArray());
        }
    }

    [Fact]
    public void The_limit_stops_between_documents_and_reports_over_limit()
    {
        string[] texts = ["\"abc\"", "\"abd\""];
        var kinds = KindsOf(texts);
        var stopped = DotNetBaseline.Count(texts, kinds, "ab", SearchScope.All, TimeSpan.Zero);
        Assert.True(stopped.OverLimit);
        Assert.Empty(stopped.MatchCounts.ToArray());
        Assert.Equal(0, stopped.Matches);
        var counted = DotNetBaseline.Count(texts, kinds, "ab", SearchScope.All, Limit);
        Assert.False(counted.OverLimit);
        Assert.Equal([1, 1], counted.MatchCounts.ToArray());
        Assert.True(counted.Milliseconds >= 0);
        Assert.Throws<PatternSyntaxException>(() => DotNetBaseline.Count(texts, kinds, "a(", SearchScope.All, Limit));
        Assert.Throws<ArgumentException>(() => DotNetBaseline.Count(texts, [kinds[0]], "ab", SearchScope.All, Limit));
    }

    [Fact]
    public void The_bench_writes_one_row_for_each_pattern_scope_and_backend()
    {
        var texts = Documents.Generate(2_000).Select(document => document.Text).ToArray();
        using var backend = new CpuLoopBackend();
        var rows = SearchBench.Run(texts, [new BenchBackend("CPU", backend)], Limit);
        Assert.Equal(12, rows.Length);
        Assert.Equal(
            SearchBench.Examples.SelectMany(example => Enum.GetValues<SearchScope>().Select(scope => (example.Pattern, scope))),
            rows.Select(row => (row.Pattern, row.Scope)));
        foreach (var row in rows)
        {
            Assert.Equal("CPU", row.Backend);
            Assert.Equal(row.Pattern == @"\d\d\d\d-\d\d-\d\d" ? 16 : 8, row.States);
            Assert.True(row.ProgramMs > 0);
            if (!row.DotNetOverLimit) Assert.Equal(row.ProgramMatches, row.DotNetMatches);
        }
        var table = SearchBench.Format(rows, texts.Length);
        output.WriteLine(table);
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "| Pattern | Scope | Backend | States | Program ms | Program documents | Program matches | .NET Regex ms | .NET documents | .NET matches | Counts |",
            lines[1]);
        Assert.Equal(12, lines.Count(line => line.StartsWith("| `", StringComparison.Ordinal)));
        // 11 columns; no example pattern holds a pipe.
        Assert.All(lines.Where(line => line.StartsWith("| `", StringComparison.Ordinal)), line => Assert.Equal(12, line.Count(character => character == '|')));
    }

    [Fact]
    public void The_app_bench_mode_writes_the_table_and_exits_with_0()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = JsonSearchCommand.Run(["--bench", "--cpu", "--documents", "300"], output, error);
        Assert.Equal("", error.ToString());
        Assert.Equal(0, exit);
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(12, lines.Count(line => line.StartsWith("| `", StringComparison.Ordinal) && line.Contains("| CPU |", StringComparison.Ordinal)));
        Assert.Equal(2, JsonSearchCommand.Run(["--documents", "x"], new StringWriter(), error));
        // Task 8: a run without --bench opens the window, so the test only parses that case.
        Assert.Equal(2, JsonSearchCommand.Run(["--frames"], new StringWriter(), new StringWriter()));
    }

    // Task 8: the window options of the app. --frames and --screenshot go to WindowRunOptions, and the bench does not
    // take them.
    [Fact]
    public void The_app_options_without_bench_are_the_window_options()
    {
        var window = JsonSearchCommand.Parse(["--frames", "120", "--screenshot", "/tmp/a.png", "--documents", "50", "--cpu"], new StringWriter());
        Assert.NotNull(window);
        Assert.Equal((false, true, 50, 120, "/tmp/a.png"), (window.Bench, window.CpuOnly, window.Documents, window.Window.FrameLimit, window.Window.ScreenshotPath));
        var plain = JsonSearchCommand.Parse([], new StringWriter());
        Assert.NotNull(plain);
        Assert.Equal((false, 100_000, (int?)null), (plain.Bench, plain.Documents, plain.Window.FrameLimit));
        var error = new StringWriter();
        Assert.Null(JsonSearchCommand.Parse(["--bench", "--frames", "2"], error));
        Assert.Contains("options of the window", error.ToString(), StringComparison.Ordinal);
        Assert.Null(JsonSearchCommand.Parse(["--frames", "0"], new StringWriter()));
        Assert.Null(JsonSearchCommand.Parse(["--screenshot"], new StringWriter()));
    }
}

[Collection(GpuCollection.Name)]
public sealed class GpuBenchTests(GpuFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void The_bench_on_the_gpu_and_the_cpu_writes_two_rows_for_each_pattern_and_scope()
    {
        var texts = Documents.Generate(500).Select(document => document.Text).ToArray();
        fixture.ForEachDevice(output, device =>
        {
            using var gpu = new GpuLoopBackend(device);
            using var cpu = new CpuLoopBackend();
            var rows = SearchBench.Run(texts, [new BenchBackend("GPU", gpu), new BenchBackend("CPU", cpu)], TimeSpan.FromSeconds(10));
            Assert.Equal(24, rows.Length);
            foreach (var pair in rows.GroupBy(row => (row.Pattern, row.Scope)))
            {
                Assert.Equal(["GPU", "CPU"], pair.Select(row => row.Backend));
                Assert.Single(pair.Select(row => (row.ProgramDocuments, row.ProgramMatches)).Distinct());
            }
            output.WriteLine(SearchBench.Format(rows, texts.Length));
        });
    }
}
