// Handwired source: tests/json-search/edge-cases.test.ts and tests/json-search/searcher.test.ts (the highlighter of
// a searcher on the GPU against the CPU and against the reference). On each adapter and with both writers: the
// highlighter over the first 100,000 generated documents (invalid fraction 0) and over the 2,509 corpus texts
// equals the CPU runner, and a sample of 2,000 of the documents equals JsonReference. The display kinds stay on
// the GPU in a position target, and the test reads them back in chunks. The shader create time and the run
// times go to the test output. Each value is a whole number, so the subnormal rule excludes no document.

using System.Diagnostics;
using Sharpwired.Gpu;
using Sharpwired.Gpu.Tests;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

[Collection(GpuCollection.Name)]
public sealed class GpuHighlighterTests(GpuFixture fixture, ITestOutputHelper output)
{
    private const int Capacity = Kinds.Capacity;
    private const int SummaryValues = 4;
    private const int ReadChunk = 8192;

    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    private static readonly Lazy<string[]> ValidDocuments = new(() => [.. Documents.Generate(100_000, invalidFraction: 0).Select(document => document.Text)]);

    // A run of the program over the texts: the summaries, the display kinds and the run, from the backend.
    private static (float[] Summary, float[] Kinds, LoopRun Run) Run(ILoopBackend backend, ILoopRunner runner, string[] texts, float[] cells)
    {
        using var input = backend.Upload(cells, texts.Length, DocumentCells.CellsPerDocument);
        using var target = backend.Allocate(texts.Length, Capacity, -9);
        var run = runner.Run(new LoopRequest([input], texts.Length, [target], 0, 0));
        var kinds = new float[texts.Length * Capacity];
        for (var first = 0; first < texts.Length; first += ReadChunk)
        {
            var count = Math.Min(ReadChunk, texts.Length - first);
            backend.Read(target, first, count).CopyTo(kinds, first * Capacity);
        }
        return ([.. run.DocumentValues], kinds, run);
    }

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
    public void The_highlighter_over_100000_documents_and_the_corpus_equals_the_cpu_runner(GpuWriter writer)
    {
        var documents = ValidDocuments.Value;
        var corpus = Corpus.All.ToArray();
        var documentCells = DocumentCells.WriteAll(documents);
        var corpusCells = DocumentCells.WriteAll(corpus);
        Assert.DoesNotContain(documentCells, float.IsSubnormal);
        var program = HighlighterTests.Program.Value;

        using var cpu = new CpuLoopBackend(new CpuLoopOptions(CpuStageBackend.Compiled, StopAtLength: false));
        using var cpuRunner = cpu.CreateRunner(program);
        var watch = Stopwatch.StartNew();
        var expectedDocuments = Run(cpu, cpuRunner, documents, documentCells);
        var cpuMs = watch.Elapsed.TotalMilliseconds;
        var expectedCorpus = Run(cpu, cpuRunner, corpus, corpusCells);
        Assert.Empty(expectedDocuments.Run.Faults);
        Assert.Empty(expectedCorpus.Run.Faults);
        output.WriteLine($"[cpu highlighter] {documents.Length} documents in {cpuMs:F0} ms (compiled stages, all positions)");

        fixture.ForEachDevice(output, device =>
        {
            using var gpu = new GpuLoopBackend(device, new GpuLoopOptions(writer));
            watch.Restart();
            using var runner = gpu.CreateRunner(program);
            var createMs = watch.Elapsed.TotalMilliseconds;
            var module = gpu.Module(runner);

            var actualDocuments = Run(gpu, runner, documents, documentCells);
            Assert.Empty(actualDocuments.Run.Faults);
            Assert.Equal(documents.Length, actualDocuments.Run.DocumentsDone);
            Same(writer, expectedDocuments.Summary, actualDocuments.Summary, "the summaries of the documents", SummaryValues);
            Same(writer, expectedDocuments.Kinds, actualDocuments.Kinds, "the display kinds of the documents", Capacity);

            var actualCorpus = Run(gpu, runner, corpus, corpusCells);
            Assert.Empty(actualCorpus.Run.Faults);
            Same(writer, expectedCorpus.Summary, actualCorpus.Summary, "the summaries of the corpus", SummaryValues);
            Same(writer, expectedCorpus.Kinds, actualCorpus.Kinds, "the display kinds of the corpus", Capacity);

            // A sample of 2,000 documents, each 50th, against the reference.
            var sample = Enumerable.Range(0, 2000).Select(row => row * 50).ToArray();
            HighlighterTests.AssertReference(
                [.. sample.Select(row => documents[row])],
                [.. sample.SelectMany(row => actualDocuments.Summary.Skip(row * SummaryValues).Take(SummaryValues))],
                [.. sample.SelectMany(row => actualDocuments.Kinds.Skip(row * Capacity).Take(Capacity))],
                $"the GPU, {writer}");
            Assert.Equal(documents.Length, Enumerable.Range(0, documents.Length).Count(row => actualDocuments.Summary[row * SummaryValues] == 1));

            var times = actualDocuments.Run.Times;
            output.WriteLine(
                $"[gpu highlighter] {writer}: {module.Statistics.Statements} statements, create {createMs:F0} ms; {documents.Length} documents in {times.TotalMs:F1} ms "
                + $"(write {times.WriteMs:F1}, submit {times.SubmitMs:F1}, wait {times.WaitMs:F1}, read {times.ReadMs:F1}); corpus {corpus.Length} texts in {actualCorpus.Run.Times.TotalMs:F1} ms");
            // A document that failed a kernel check and passed on the CPU gets the CPU values. No value is subnormal, so there must be none.
            Assert.Equal(0, gpu.ReplayPasses);
        });
    }
}
