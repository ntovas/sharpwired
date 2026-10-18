// Handwired source: tests/json-search/search.test.ts and tests/json-search/edge-cases.test.ts (the search on the
// GPU against the CPU and the references). On each adapter and with both writers: the highlighter writes the
// display kinds into a buffer that stays on the GPU, and the search of each capacity reads it. Over 10,000
// documents (the hand-written documents at the limits, then generated documents) with the patterns that fill the
// capacity, the empty pattern and [^] (a match on the last position), in each scope and in the scopes of
// edge-cases.ts (lists of kinds, and no kind), the match counts and the
// window marks of the first 64 documents equal the CPU runner, and the window marks equal the automaton over the
// display kinds of JsonReference. The CPU runner runs with StopAtLength false, as the GPU runs each position.
// Over 100,000 generated documents, one pattern at each capacity gives the counts of the CPU runner. The shader
// create times and the run times go to the test output. Each value is a whole number, so the subnormal rule
// excludes no document.

using System.Collections.Concurrent;
using System.Diagnostics;
using Sharpwired.Gpu;
using Sharpwired.Gpu.Tests;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

[Collection(GpuCollection.Name)]
public sealed class GpuSearchTests(GpuFixture fixture, ITestOutputHelper output)
{
    private const int Positions = SearchCells.Positions;
    private const int DocumentCount = 10_000;
    private const int WindowCount = 64;

    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    // The hand-written documents of more than one code point, then generated documents up to 10,000.
    private static readonly Lazy<string[]> Texts = new(() =>
    {
        var edges = SearchTests.LongerEdgeTexts;
        return [.. edges.Concat(Documents.Generate(DocumentCount - edges.Length).Select(document => document.Text))];
    });

    private static readonly Lazy<float[]> Cells = new(() => DocumentCells.WriteAll(Texts.Value));

    // The display kinds of the CPU highlighter, the input of the CPU search.
    private static readonly Lazy<float[]> CpuKinds = new(() => HighlighterTests.RunCpu(Texts.Value, CpuStageBackend.Compiled).Kinds);

    private static readonly Lazy<ReferenceResult[]> WindowReferences = new(() => [.. Texts.Value.Take(WindowCount).Select(JsonReference.Highlight)]);

    // The CPU results of each search, which both writers compare with: (states, pattern, scope) to the counts of
    // each document and the marks of the window.
    private static readonly ConcurrentDictionary<(int, string, string), (float[] Counts, float[] Marks)> CpuResults = new();

    // The searches of edge-cases.ts at the capacity (each pattern with all kinds and one more scope in turn,
    // among them lists of kinds), the empty pattern and [^] in each scope, and at 8 states each pattern in each
    // scope. The larger programs take more time on the CPU, so they run the searches of edge-cases.ts only.
    private static EdgeSearch[] Searches(int states)
    {
        EdgeSearch[] scopes =
        [
            new("", "everything", SearchProgram.KindsOf(SearchScope.All)),
            new("", "keys", SearchProgram.KindsOf(SearchScope.Keys)),
            new("", "strings", SearchProgram.KindsOf(SearchScope.StringValues)),
        ];
        var patterns = states == 8 ? EdgeCases.EdgePatterns(states).Concat(["[^]"]) : ["", "[^]"];
        return [.. EdgeCases.EdgeSearches(states)
            .Concat(patterns.SelectMany(pattern => scopes.Select(scope => scope with { Pattern = pattern })))
            .DistinctBy(search => (search.Pattern, search.ScopeName))];
    }

    private static (float[] Counts, float[] Marks) CpuResult(int states, string pattern, string scopeName, IReadOnlyCollection<DisplayKind> kinds) =>
        CpuResults.GetOrAdd((states, pattern, scopeName), _ =>
        {
            var program = SearchTests.Programs[states].Value;
            using var backend = new CpuLoopBackend(new CpuLoopOptions(CpuStageBackend.Compiled, StopAtLength: false));
            using var runner = backend.CreateRunner(program.Loop);
            runner.WriteShared(SearchTests.SharedOf(program, PatternCompiler.Compile(pattern), kinds));
            using var documents = backend.Upload(Cells.Value, DocumentCount, DocumentCells.CellsPerDocument);
            using var kindBuffer = backend.Upload(CpuKinds.Value, DocumentCount, Positions);
            var run = runner.Run(new LoopRequest([documents, kindBuffer], DocumentCount, [null], 0, WindowCount));
            Assert.Empty(run.Faults);
            return ([.. run.DocumentValues], [.. run.WindowPositionValues]);
        });

    private static void Same(GpuWriter writer, float[] expected, float[] actual, string what, int valuesPerDocument)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            // An i32 has no -0, so the integer writer can give +0 where the CPU gives -0. No value here is -0.
            var agrees = writer == GpuWriter.Integer ? Js.SameNumericValue(expected[index], actual[index]) : Js.ObjectIs(expected[index], actual[index]);
            if (!agrees) Assert.Fail($"{what}: document {index / valuesPerDocument} value {index % valuesPerDocument}: expected {expected[index]:R}, got {actual[index]:R}");
        }
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_search_of_each_capacity_over_10000_documents_equals_the_cpu_runner_and_the_window_equals_the_reference(GpuWriter writer)
    {
        var texts = Texts.Value;
        Assert.Equal(DocumentCount, texts.Length);
        Assert.DoesNotContain(Cells.Value, float.IsSubnormal);
        var watch = Stopwatch.StartNew();
        foreach (var capacity in SearchCapacity.All)
        {
            foreach (var search in Searches(capacity.States)) CpuResult(capacity.States, search.Pattern, search.ScopeName, search.Kinds);
        }
        output.WriteLine($"[cpu search] the expected values of {SearchCapacity.All.Sum(capacity => Searches(capacity.States).Length)} searches over {DocumentCount} documents in {watch.Elapsed.TotalSeconds:F1} s (compiled stages, all positions)");

        fixture.ForEachDevice(output, device =>
        {
            using var gpu = new GpuLoopBackend(device, new GpuLoopOptions(writer));
            using var highlighter = gpu.CreateRunner(HighlighterTests.Program.Value);
            using var documents = gpu.Upload(Cells.Value, DocumentCount, DocumentCells.CellsPerDocument);
            using var kinds = gpu.Allocate(DocumentCount, Positions, -9);
            var highlighted = highlighter.Run(new LoopRequest([documents], DocumentCount, [kinds], 0, 0));
            Assert.Empty(highlighted.Faults);
            output.WriteLine($"[gpu highlighter] {writer}: {DocumentCount} documents in {highlighted.Times.TotalMs:F1} ms");
            var marked = 0;
            foreach (var capacity in SearchCapacity.All)
            {
                var program = SearchTests.Programs[capacity.States].Value;
                watch.Restart();
                using var runner = gpu.CreateRunner(program.Loop);
                var createMs = watch.Elapsed.TotalMilliseconds;
                var statements = gpu.Module(runner).Statistics.Statements;
                var runMs = new List<double>();
                foreach (var (pattern, scopeName, searched) in Searches(capacity.States))
                {
                    var compiled = PatternCompiler.Compile(pattern);
                    runner.WriteShared(SearchTests.SharedOf(program, compiled, searched));
                    var run = runner.Run(new LoopRequest([documents, kinds], DocumentCount, [null], 0, WindowCount));
                    Assert.Empty(run.Faults);
                    Assert.Equal(DocumentCount, run.DocumentsDone);
                    runMs.Add(run.Times.TotalMs);
                    var what = $"{capacity.States} states, {pattern} in {scopeName}";
                    var expected = CpuResult(capacity.States, pattern, scopeName, searched);
                    Same(writer, expected.Counts, [.. run.DocumentValues], what + ", the counts", 1);
                    Same(writer, expected.Marks, [.. run.WindowPositionValues], what + ", the window marks", Positions);
                    // The window against the automaton over the display kinds of JsonReference.
                    var windowTexts = texts.Take(WindowCount).ToArray();
                    var reference = SearchTests.ReferenceSearch(windowTexts, WindowReferences.Value, compiled, searched);
                    Same(writer, reference.Marks, [.. run.WindowPositionValues], what + ", the window marks against the reference", Positions);
                    Same(writer, reference.Counts, [.. run.DocumentValues.Take(WindowCount)], what + ", the window counts against the reference", 1);
                    marked += reference.Marks.Count(mark => mark != 0);
                }
                output.WriteLine(
                    $"[gpu search] {writer}, {capacity.States} states: {statements} statements, create {createMs:F0} ms; {runMs.Count} searches over {DocumentCount} documents, "
                    + $"{runMs.Min():F1} to {runMs.Max():F1} ms each (median {runMs.Order().ElementAt(runMs.Count / 2):F1} ms)");
            }
            Assert.True(marked > 10_000, $"{marked} marked positions in the windows");
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void One_pattern_over_100000_documents_gives_the_cpu_counts_at_each_capacity(GpuWriter writer)
    {
        const int Count = 100_000;
        const string Pattern = "[a-z]+Id";
        var texts = Documents.Generate(Count).Select(document => document.Text).ToArray();
        var cells = DocumentCells.WriteAll(texts);
        var searched = SearchProgram.KindsOf(SearchScope.Keys);
        var compiled = PatternCompiler.Compile(Pattern);

        // The CPU counts at 8 states. The pattern fits each capacity, so each program gives the same counts.
        var watch = Stopwatch.StartNew();
        var (_, cpuKinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        var program8 = SearchTests.Programs[8].Value;
        float[] expected;
        using (var cpu = new SearchTests.CpuSearch(program8, stopAtLength: false))
        {
            expected = cpu.Run(SearchTests.SharedOf(program8, compiled, searched), cells, cpuKinds, Count).Counts;
        }
        output.WriteLine($"[cpu search] {Pattern} in Keys over {Count} documents in {watch.Elapsed.TotalSeconds:F1} s with the highlighter (compiled stages, all positions); {expected.Sum()} matches");
        Assert.True(expected.Sum() > 10_000, $"{expected.Sum()} matches");

        fixture.ForEachDevice(output, device =>
        {
            using var gpu = new GpuLoopBackend(device, new GpuLoopOptions(writer));
            using var highlighter = gpu.CreateRunner(HighlighterTests.Program.Value);
            using var documents = gpu.Upload(cells, Count, DocumentCells.CellsPerDocument);
            using var kinds = gpu.Allocate(Count, Positions, -9);
            var highlighted = highlighter.Run(new LoopRequest([documents], Count, [kinds], 0, 0));
            Assert.Empty(highlighted.Faults);
            foreach (var capacity in SearchCapacity.All)
            {
                var program = SearchTests.Programs[capacity.States].Value;
                using var runner = gpu.CreateRunner(program.Loop);
                runner.WriteShared(SearchTests.SharedOf(program, compiled, searched));
                var run = runner.Run(new LoopRequest([documents, kinds], Count, [null], 0, 0));
                Assert.Empty(run.Faults);
                Same(writer, expected, [.. run.DocumentValues], $"{capacity.States} states, the counts", 1);
                var times = run.Times;
                output.WriteLine(
                    $"[gpu search] {writer}, {capacity.States} states, {Pattern} in Keys: {Count} documents in {times.TotalMs:F1} ms "
                    + $"(write {times.WriteMs:F1}, submit {times.SubmitMs:F1}, wait {times.WaitMs:F1}, read {times.ReadMs:F1}); highlighter {highlighted.Times.TotalMs:F1} ms");
            }
        });
    }
}
