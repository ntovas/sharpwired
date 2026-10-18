// Handwired source: tests/json-search/search.test.ts and lab/json-search/gpu (the loop shaders on the device
// equal the CPU runner). Task 3 of the phase 6 plan, on each adapter and with both writers: each loop input over
// 10,000 seeded documents equals the CPU runner (Object.is for the f32 writer, sameNumericValue for the integer
// writer), with the faults of the CPU runner; a chained run keeps a position group on the GPU for a second
// runner; a chunk limit of 1,000 documents gives the same values for 2,500 documents; an invalid lane gives the
// fault of the CPU runner and keeps the other lanes. The create time of each shader and the run time over
// 100,000 documents go to the test output. The subnormal rule (phase 5 decision 4) excludes a document with a
// subnormal value on a device that flushes them, and the test writes the count.

using System.Diagnostics;
using Sharpwired.Core;
using Xunit.Abstractions;
using static Sharpwired.Gpu.Tests.LoopInputPrograms;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class GpuLoopRunnerTests(GpuFixture fixture, ITestOutputHelper output)
{
    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    // The GPU run of a program over the documents, with each document in the window and a target for each
    // position group. The targets come back to the host.
    private static (LoopRun Run, float[][] Targets, LoopWgslModule Module) RunGpu(
        GpuDevice device, GpuWriter writer, LoopProgram program, float[] shared, float[] documents, int count, int? chunk = null)
    {
        using var backend = new GpuLoopBackend(device, new GpuLoopOptions(writer)) { MaxDocumentsPerChunk = chunk };
        using var runner = backend.CreateRunner(program);
        runner.WriteShared(shared);
        var inputs = program.Buffers.Select(_ => backend.Upload(documents, count, program.Buffers[0].CellsPerDocument)).ToArray();
        var groups = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        var targets = Enumerable.Range(0, groups).Select(_ => backend.Allocate(count, program.Positions, 7)).ToArray();
        var run = runner.Run(new LoopRequest(inputs, count, targets, 0, count));
        var values = targets.Select(target => backend.Read(target, 0, count)).ToArray();
        var module = backend.Module(runner);
        foreach (var buffer in inputs.Concat(targets)) buffer.Dispose();
        return (run, values, module);
    }

    // Compare a GPU run with a CPU run: the faults, then each value of each valid document. Return the count of
    // documents that the subnormal rule excludes.
    private static int Compare(GpuDevice device, GpuWriter writer, LoopProgram program, float[] documents, int count, LoopRun expected, float[][] expectedTargets, LoopRun actual, float[][] actualTargets)
    {
        Assert.Equal(expected.Faults.ToArray(), actual.Faults.ToArray());
        Assert.Equal(count, actual.DocumentsDone);
        var faults = expected.Faults.Select(fault => fault.Document).ToHashSet();
        var documentValues = expected.DocumentValues.Length / Math.Max(1, count);
        var cells = program.Buffers[0].CellsPerDocument;
        var excluded = 0;
        for (var document = 0; document < count; document++)
        {
            var values = Enumerable.Range(0, documentValues).Select(group => expected.DocumentValues[(group * count) + document])
                .Concat(Enumerable.Range(0, expectedTargets.Length).SelectMany(group => expectedTargets[group].Skip(document * program.Positions).Take(program.Positions)))
                .Concat(documents.Skip(document * cells).Take(cells));
            if (device.FlushesSubnormals && values.Any(float.IsSubnormal))
            {
                excluded++;
                continue;
            }
            for (var group = 0; group < documentValues; group++)
            {
                var index = (group * count) + document;
                Same(writer, faults.Contains(document), expected.DocumentValues[index], actual.DocumentValues[index], $"document {document} value {group}");
            }
            for (var group = 0; group < expectedTargets.Length; group++)
            {
                for (var position = 0; position < program.Positions; position++)
                {
                    var window = (((group * count) + document) * program.Positions) + position;
                    Same(writer, faults.Contains(document), expected.WindowPositionValues[window], actual.WindowPositionValues[window], $"document {document} group {group} position {position}");
                    var at = (document * program.Positions) + position;
                    Same(writer, faults.Contains(document), expectedTargets[group][at], actualTargets[group][at], $"document {document} target {group} position {position}");
                }
            }
        }
        return excluded;
    }

    private static void Same(GpuWriter writer, bool fault, float expected, float actual, string what)
    {
        // A document with a fault holds NaN. An i32 has no -0, so the integer writer can give +0 where the CPU
        // runner gives -0 (question 3 of the phase 5 plan).
        var agrees = fault ? float.IsNaN(actual) : writer == GpuWriter.Integer ? Js.SameNumericValue(expected, actual) : Js.ObjectIs(expected, actual);
        if (!agrees) Assert.Fail($"{what}: expected {expected:R}, got {actual:R}");
    }

    public static TheoryData<string, GpuWriter> ProgramsAndWriters() => LoopWriterTests.ProgramsAndWriters();

    [Theory]
    [MemberData(nameof(ProgramsAndWriters))]
    public void Each_loop_input_over_10000_documents_equals_the_cpu_runner(string name, GpuWriter writer)
    {
        const int Count = 10_000;
        var (program, shared, documents) = LoopCases.Case(name, Count, 0x6b00 + name.Length);
        var (expected, expectedTargets) = LoopCases.RunCpu(program, shared, documents, Count);
        fixture.ForEachDevice(output, device =>
        {
            var (actual, actualTargets, module) = RunGpu(device, writer, program, shared, documents, Count);
            var excluded = Compare(device, writer, program, documents, Count, expected, expectedTargets, actual, actualTargets);
            output.WriteLine($"[gpu loop] {name} {writer}: {Count} documents, {actual.Faults.Length} faults, {excluded} with a subnormal value not compared; {module.Statistics.Statements} statements; run {actual.Times.TotalMs:F1} ms");
        });
    }

    // A chained run: Leaders writes its position group "leaders" into a document buffer on the GPU, and Sum over 31
    // positions reads that buffer as its documents (31 items and the length at column 31). No values come back
    // between the two runs. The result equals the same chain on the CPU.
    [Theory]
    [MemberData(nameof(Writers))]
    public void A_chained_run_reads_a_position_group_that_stays_on_the_gpu(GpuWriter writer)
    {
        const int Count = 3_000;
        const int Positions = LoopCases.Positions;
        var first = Leaders(Positions);
        var second = Sum(Positions - 1);
        var documents = CreateDocuments(Count, Positions, 0xc4a1);

        LoopRun Chain(ILoopBackend backend)
        {
            using var firstRunner = backend.CreateRunner(first);
            using var secondRunner = backend.CreateRunner(second);
            using var input = backend.Upload(documents, Count, Positions + 1);
            using var leaders = backend.Allocate(Count, Positions, 0);
            var firstRun = firstRunner.Run(new LoopRequest([input], Count, [leaders, null], 0, 0));
            Assert.Empty(firstRun.Faults);
            return secondRunner.Run(new LoopRequest([leaders], Count, [], 0, 0));
        }

        using var cpu = new CpuLoopBackend(new CpuLoopOptions(StopAtLength: false));
        var expected = Chain(cpu);
        fixture.ForEachDevice(output, device =>
        {
            using var gpu = new GpuLoopBackend(device, new GpuLoopOptions(writer));
            var actual = Chain(gpu);
            Assert.Empty(actual.Faults);
            for (var document = 0; document < Count; document++) Same(writer, false, expected.DocumentValues[document], actual.DocumentValues[document], $"document {document}");
            Assert.Contains(actual.DocumentValues, value => value > 0);
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void A_chunk_limit_of_1000_documents_gives_the_cpu_values_for_2500_documents(GpuWriter writer)
    {
        const int Count = 2_500;
        var (program, shared, documents) = LoopCases.Case("Search8", Count, 0xc4c4);
        var (expected, expectedTargets) = LoopCases.RunCpu(program, shared, documents, Count);
        fixture.ForEachDevice(output, device =>
        {
            var (actual, actualTargets, _) = RunGpu(device, writer, program, shared, documents, Count, chunk: 1_000);
            Compare(device, writer, program, documents, Count, expected, expectedTargets, actual, actualTargets);
        });
    }

    // Overflow: document 7 passes the declared range of its register. LeadersBits: many documents write a value
    // that is not a bit into a field of bits. Each fault names its document and holds the message of the CPU
    // runner, its outputs are NaN, and the other documents equal the CPU runner.
    [Theory]
    [InlineData("Overflow", GpuWriter.Float32)]
    [InlineData("Overflow", GpuWriter.Integer)]
    [InlineData("LeadersBits", GpuWriter.Float32)]
    [InlineData("LeadersBits", GpuWriter.Integer)]
    public void An_invalid_lane_gives_the_cpu_fault_and_the_other_lanes_keep_their_values(string name, GpuWriter writer)
    {
        const int Count = 1_000;
        var (program, shared, documents) = LoopCases.Case(name, Count, 0x0f10);
        var (expected, expectedTargets) = LoopCases.RunCpu(program, shared, documents, Count);
        if (name == "Overflow") Assert.Equal(new LaneFault(7, "stage Step, position 4: count must be finite and within 0..50, got 100"), Assert.Single(expected.Faults));
        else Assert.True(expected.Faults.Length > Count / 8, $"{expected.Faults.Length} faults");
        fixture.ForEachDevice(output, device =>
        {
            var (actual, actualTargets, _) = RunGpu(device, writer, program, shared, documents, Count);
            Compare(device, writer, program, documents, Count, expected, expectedTargets, actual, actualTargets);
            output.WriteLine($"[gpu loop] {name} {writer}: {actual.Faults.Length} faults replayed on the CPU in {actual.Times.ReadMs:F1} ms");
        });
    }

    // The create time of each shader (the shader module and the pipeline) and the run time over 100,000
    // documents, the second run of each runner. The test asserts no time.
    [Theory]
    [MemberData(nameof(Writers))]
    public void The_create_times_and_the_run_times_over_100000_documents_go_to_the_output(GpuWriter writer)
    {
        const int Count = 100_000;
        fixture.ForEachDevice(output, device =>
        {
            foreach (var name in new[] { "Leaders", "Switches", "Mixed", "Search8" })
            {
                var (program, shared, documents) = LoopCases.Case(name, Count, 0x100000);
                using var backend = new GpuLoopBackend(device, new GpuLoopOptions(writer, Salt: (uint)Environment.TickCount));
                var watch = Stopwatch.StartNew();
                using var runner = backend.CreateRunner(program);
                var create = watch.Elapsed.TotalMilliseconds;
                runner.WriteShared(shared);
                using var input = backend.Upload(documents, Count, program.Buffers[0].CellsPerDocument);
                var request = new LoopRequest([input], Count, [], 0, 0);
                runner.Run(request);
                var run = runner.Run(request);
                var module = backend.Module(runner);
                output.WriteLine($"[gpu loop time] {name} {writer}: create {create:F1} ms ({module.Statistics.Statements} statements); "
                    + $"{Count} documents in {run.Times.TotalMs:F1} ms (write {run.Times.WriteMs:F1}, submit {run.Times.SubmitMs:F1}, wait {run.Times.WaitMs:F1}, read {run.Times.ReadMs:F1}); {run.Faults.Length} faults");
            }
        });
    }

    [Fact]
    public void The_backend_checks_the_request_and_a_call_after_Dispose_throws()
    {
        fixture.ForEachDevice(output, device =>
        {
            var program = Lookup(LoopCases.Positions);
            var cells = CreateDocuments(10, LoopCases.Positions, 0xc4ed);
            var backend = new GpuLoopBackend(device);
            using var other = new CpuLoopBackend();
            var runner = backend.CreateRunner(program);
            var input = backend.Upload(cells, 10, LoopCases.Positions + 1);
            using var foreign = other.Upload(cells, 10, LoopCases.Positions + 1);

            Js.Throws(() => runner.Run(new LoopRequest([input], 10, [], 0, 0)), "WriteShared");
            float[] shared = [.. LookupShared()];
            shared[0] = 2000;
            Js.Throws(() => runner.WriteShared(shared), "stage Step: threshold must be finite and within 0..1000, got 2000");
            runner.WriteShared(LookupShared());
            Js.Throws(() => runner.Run(new LoopRequest([foreign], 10, [], 0, 0)), "another backend");
            Js.Throws(() => runner.Run(new LoopRequest([input], 11, [], 0, 0)), "holds 10 documents, and the request runs 11");
            Js.Throws(() => runner.Run(new LoopRequest([input], 10, [], 8, 3)), "the window 8..10 must lie inside the 10 documents");
            Assert.Equal(cells.AsSpan(2 * (LoopCases.Positions + 1), 3 * (LoopCases.Positions + 1)).ToArray(), backend.Read(input, 2, 3));
            using (var filled = backend.Allocate(2, 3, 7.5f)) Assert.All(backend.Read(filled, 0, 2), value => Assert.Equal(7.5f, value));
            Assert.Equal(LoopIntegerWriter.Write(program).Code, backend.Module(runner).Code);
            Assert.Throws<ArgumentException>(() => backend.Module(new CpuLoopBackend().CreateRunner(program)));
            var empty = runner.Run(new LoopRequest([input], 0, [], 0, 0));
            Assert.Empty(empty.DocumentValues);

            runner.Dispose();
            Assert.Throws<ObjectDisposedException>(() => runner.Run(new LoopRequest([input], 10, [], 0, 0)));
            input.Dispose();
            Assert.Throws<ObjectDisposedException>(() => backend.Read(input, 0, 1));
            backend.Dispose();
            Assert.Throws<ObjectDisposedException>(() => backend.Upload(cells, 10, LoopCases.Positions + 1));
            Assert.Throws<ObjectDisposedException>(() => backend.CreateRunner(program));
        });
    }
}
