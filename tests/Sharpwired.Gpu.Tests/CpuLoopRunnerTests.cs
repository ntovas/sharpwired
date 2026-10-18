// Handwired source: tests/json-search/json-step.test.ts and search.test.ts (createLoopRunner on the
// executor and on generated JavaScript equals the plain reference). Task 2 of the phase 6 plan: the CPU
// runner with both stage backends equals LoopProgram.EvaluatePlain on 1,000 seeded documents for each
// input program. StopAtLength gives the same outputs. An invalid document gives a LaneFault with the
// message of the cell check, and the other documents keep their values (phase 5 carried item). A
// cancellation stops between blocks. A call after Dispose throws ObjectDisposedException.

using Sharpwired.Core;
using static Sharpwired.Gpu.Tests.LoopInputPrograms;

namespace Sharpwired.Gpu.Tests;

public sealed class CpuLoopRunnerTests
{
    private const int Positions = 32;
    private const int DocumentCount = 1_000;

    public static TheoryData<string, CpuStageBackend> Programs()
    {
        var data = new TheoryData<string, CpuStageBackend>();
        foreach (var name in new[] { "Sum", "Leaders", "Lookup", "Overflow" })
        {
            data.Add(name, CpuStageBackend.Compiled);
            data.Add(name, CpuStageBackend.Tensor);
        }
        return data;
    }

    public static TheoryData<CpuStageBackend> Backends => new() { CpuStageBackend.Compiled, CpuStageBackend.Tensor };

    [Theory]
    [MemberData(nameof(Programs))]
    public void The_runner_equals_EvaluatePlain_on_seeded_documents(string name, CpuStageBackend stages)
    {
        var (program, shared) = Program(name);
        var cells = CreateDocuments(DocumentCount, Positions, 0x10c0 + name.Length);
        using var backend = new CpuLoopBackend(new CpuLoopOptions(stages, BlockSize: 64));
        using var runner = backend.CreateRunner(program);
        runner.WriteShared(shared);
        using var input = backend.Upload(cells, DocumentCount, Positions + 1);
        var positionGroups = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        var targets = Enumerable.Range(0, positionGroups).Select(_ => backend.Allocate(DocumentCount, Positions, float.NaN)).ToArray();
        var run = runner.Run(new LoopRequest([input], DocumentCount, targets, 0, DocumentCount));

        // The plain method and the program can differ in the sign of zero by the contract of the
        // translator (ZeroSign.Loose of the IL tests: a sum with the bias 0 gives +0 where C# gives -0).
        // So this comparison uses sameNumericValue. The two stage backends give the same bits (next test).
        var (documentValues, positionValues) = Plain(program, cells, DocumentCount, shared);
        Assert.Empty(run.Faults);
        Assert.Equal(DocumentCount, run.DocumentsDone);
        AllSame(documentValues, run.DocumentValues);
        AllSame(positionValues, run.WindowPositionValues);
        for (var group = 0; group < positionGroups; group++)
        {
            AllIs(run.WindowPositionValues.AsSpan().Slice(group * DocumentCount * Positions, DocumentCount * Positions).ToArray(), backend.Read(targets[group], 0, DocumentCount));
            targets[group].Dispose();
        }
        Assert.True(run.Times.TotalMs >= run.Times.WaitMs);
    }

    [Theory]
    [InlineData("Sum")]
    [InlineData("Leaders")]
    [InlineData("Lookup")]
    [InlineData("Overflow")]
    public void The_two_stage_backends_give_the_same_bits(string name)
    {
        var (program, shared) = Program(name);
        var cells = CreateDocuments(DocumentCount, Positions, 0xb17);
        LoopRun Run(CpuStageBackend stages)
        {
            using var backend = new CpuLoopBackend(new CpuLoopOptions(stages));
            using var runner = backend.CreateRunner(program);
            runner.WriteShared(shared);
            using var input = backend.Upload(cells, DocumentCount, Positions + 1);
            return runner.Run(new LoopRequest([input], DocumentCount, [], 0, DocumentCount));
        }
        var compiled = Run(CpuStageBackend.Compiled);
        var tensor = Run(CpuStageBackend.Tensor);
        AllIs(tensor.DocumentValues.ToArray(), compiled.DocumentValues);
        AllIs(tensor.WindowPositionValues.ToArray(), compiled.WindowPositionValues);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void StopAtLength_gives_the_same_outputs(string name, CpuStageBackend stages)
    {
        var (program, shared) = Program(name);
        var cells = CreateDocuments(DocumentCount, Positions, 0x5a1d);
        LoopRun Run(bool stop)
        {
            using var backend = new CpuLoopBackend(new CpuLoopOptions(stages, StopAtLength: stop));
            using var runner = backend.CreateRunner(program);
            runner.WriteShared(shared);
            using var input = backend.Upload(cells, DocumentCount, Positions + 1);
            return runner.Run(new LoopRequest([input], DocumentCount, [], 100, 300));
        }
        var full = Run(false);
        var stopped = Run(true);
        Assert.Empty(full.Faults);
        Assert.Empty(stopped.Faults);
        AllIs(full.DocumentValues.ToArray(), stopped.DocumentValues);
        AllIs(full.WindowPositionValues.ToArray(), stopped.WindowPositionValues);
        Assert.Equal(program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position) * 300 * Positions, stopped.WindowPositionValues.Length);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void A_register_past_its_range_gives_a_fault_for_its_document_and_the_other_documents_keep_their_values(CpuStageBackend stages)
    {
        var program = Overflow(Positions);
        var cells = CreateDocuments(DocumentCount, Positions, 0x0f10, overflowAt: 7);
        using var backend = new CpuLoopBackend(new CpuLoopOptions(stages, BlockSize: 16));
        using var runner = backend.CreateRunner(program);
        using var input = backend.Upload(cells, DocumentCount, Positions + 1);
        var run = runner.Run(new LoopRequest([input], DocumentCount, [], 0, 0));

        var expectedMessage = "stage Step, position 4: count must be finite and within 0..50, got 100";
        var fault = Assert.Single(run.Faults);
        Assert.Equal(new LaneFault(7, expectedMessage), fault);
        var plainError = Assert.Throws<InvalidOperationException>(() => program.EvaluatePlain([cells.AsMemory(7 * (Positions + 1), Positions + 1)], []));
        Assert.Equal(expectedMessage, plainError.Message);
        Assert.True(float.IsNaN(run.DocumentValues[7]));
        for (var document = 0; document < DocumentCount; document++)
        {
            if (document == 7) continue;
            var expected = program.EvaluatePlain([cells.AsMemory(document * (Positions + 1), Positions + 1)], []);
            Assert.True(Js.SameNumericValue(expected.DocumentOutputs[0], run.DocumentValues[document]), $"document {document}");
        }
        Assert.Equal(DocumentCount, run.DocumentsDone);
    }

    // A field of bits that gets 523, and a length cell of 2.5.
    [Theory]
    [MemberData(nameof(Backends))]
    public void A_value_that_is_not_a_bit_in_a_field_of_bits_and_a_length_that_is_not_whole_give_faults(CpuStageBackend stages)
    {
        var program = LoopProgram.Create(Positions, [Documents(Positions)], LeadersRegisters(),
            [new LoopField("largest", 1, true), new LoopField("flags", 2, true)], LeadersGroups(Positions),
            [LeadersBackwardStage(), LeadersForwardStage(), LeadersTotalStage()], 0, new LengthCell(0, Positions));
        var cells = CreateDocuments(3, Positions, 0xb175);
        cells.AsSpan(0, Positions).Fill(-1);
        cells[Positions] = 0;
        cells[(2 * (Positions + 1)) + Positions] = 2.5f;
        cells.AsSpan(Positions + 1, Positions).Fill(3);
        cells[(Positions + 1) + Positions] = Positions;
        cells[(Positions + 1) + 9] = 523;
        using var backend = new CpuLoopBackend(new CpuLoopOptions(stages));
        using var runner = backend.CreateRunner(program);
        using var input = backend.Upload(cells, 3, Positions + 1);
        var run = runner.Run(new LoopRequest([input], 3, [], 0, 3));

        LaneFault[] expected =
        [
            new LaneFault(1, "stage Backward, position 31: field largest component 0 must be exactly 0 or 1, got 3"),
            new LaneFault(2, "the length cell holds 2.5, which is not a whole number of 0 or more"),
        ];
        Assert.Equal(expected, run.Faults.ToArray());
        var plainError = Assert.Throws<InvalidOperationException>(() => program.EvaluatePlain([cells.AsMemory(Positions + 1, Positions + 1)], []));
        Assert.Equal(run.Faults[0].Message, plainError.Message);
        Assert.False(float.IsNaN(run.DocumentValues[0]));
        Assert.True(float.IsNaN(run.DocumentValues[1]) && float.IsNaN(run.DocumentValues[2]));
        Assert.All(run.WindowPositionValues.Skip(Positions).Take(2 * Positions), value => Assert.True(float.IsNaN(value)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void A_cancellation_stops_between_blocks_and_the_progress_reports_increase(int threads)
    {
        const int BlockSize = 64;
        var program = Leaders(Positions);
        var cells = CreateDocuments(DocumentCount, Positions, 0xca7c);
        using var backend = new CpuLoopBackend(new CpuLoopOptions(MaxDegreeOfParallelism: threads, BlockSize: BlockSize));
        using var runner = backend.CreateRunner(program);
        using var input = backend.Upload(cells, DocumentCount, Positions + 1);
        var request = new LoopRequest([input], DocumentCount, [], 0, DocumentCount);
        var full = runner.Run(request);

        using var cancel = new CancellationTokenSource();
        var reports = new List<int>();
        var progress = new SynchronousProgress(done =>
        {
            reports.Add(done);
            if (done >= 2 * BlockSize) cancel.Cancel();
        });
        var run = runner.Run(request, progress, cancel.Token);

        Assert.True(run.DocumentsDone >= 2 * BlockSize && run.DocumentsDone < DocumentCount, $"done {run.DocumentsDone}");
        Assert.Equal(0, run.DocumentsDone % BlockSize);
        Assert.Equal(run.DocumentsDone, reports[^1]);
        Assert.All(reports.Zip(reports.Skip(1)), pair => Assert.True(pair.First < pair.Second, $"{pair.First} then {pair.Second}"));
        Assert.All(reports, done => Assert.Equal(0, done % BlockSize));
        AllIs(full.DocumentValues.Take(run.DocumentsDone).ToArray(), run.DocumentValues.Take(run.DocumentsDone).ToArray());
        Assert.All(run.DocumentValues.Skip(run.DocumentsDone), value => Assert.True(float.IsNaN(value)));

        var allReports = new List<int>();
        runner.Run(request, new SynchronousProgress(allReports.Add));
        Assert.Equal(DocumentCount, allReports[^1]);
        Assert.All(allReports.Zip(allReports.Skip(1)), pair => Assert.True(pair.First < pair.Second));
    }

    [Fact]
    public void The_runner_checks_the_shared_cells_and_the_request()
    {
        var program = Lookup(Positions);
        using var backend = new CpuLoopBackend();
        using var other = new CpuLoopBackend();
        using var runner = backend.CreateRunner(program);
        var cells = CreateDocuments(10, Positions, 0xc4ec);
        using var input = backend.Upload(cells, 10, Positions + 1);
        using var foreign = other.Upload(cells, 10, Positions + 1);
        using var small = backend.Upload(cells.AsSpan(0, 10 * Positions), 10, Positions);

        Js.Throws(() => runner.Run(new LoopRequest([input], 10, [], 0, 0)), "WriteShared");
        Js.Throws(() => runner.WriteShared([1, 2]), "the shared table holds 2 cells, and the program needs 9");
        float[] shared = [.. LookupShared()];
        shared[0] = 2000;
        Js.Throws(() => runner.WriteShared(shared), "stage Step: threshold must be finite and within 0..1000, got 2000");
        runner.WriteShared(LookupShared());

        Js.Throws(() => runner.Run(new LoopRequest([], 10, [], 0, 0)), "the request has 0 inputs, and the program has 1 buffers");
        Js.Throws(() => runner.Run(new LoopRequest([foreign], 10, [], 0, 0)), "another backend");
        Js.Throws(() => runner.Run(new LoopRequest([small], 10, [], 0, 0)), "holds 32 cells for each document, and buffer documents of the program needs 33");
        Js.Throws(() => runner.Run(new LoopRequest([input], 11, [], 0, 0)), "holds 10 documents, and the request runs 11");
        Js.Throws(() => runner.Run(new LoopRequest([input], 10, [], 8, 3)), "the window 8..10 must lie inside the 10 documents");
        Js.Throws(() => runner.Run(new LoopRequest([input], 10, [null, null], 0, 0)), "the request has 2 position targets, and the program has 1 position groups");
        Js.Throws(() => runner.Run(new LoopRequest([input], 10, [input], 0, 0)), "position target 0");
        // One buffer as the target of two position groups.
        using (var leadersRunner = backend.CreateRunner(Leaders(Positions)))
        using (var target = backend.Allocate(10, Positions, 0))
        {
            Js.Throws(() => leadersRunner.Run(new LoopRequest([input], 10, [target, target], 0, 0)), "position target 1 is also position target 0");
        }
        Js.Throws(() => backend.Upload(cells, 10, Positions), "cells holds");
        Js.Throws(() => backend.Read(input, 5, 6), "documents 5..10");
        Assert.Equal(cells.AsSpan(2 * (Positions + 1), 3 * (Positions + 1)).ToArray(), backend.Read(input, 2, 3));
        Assert.All(backend.Read(backend.Allocate(2, 3, 7.5f), 0, 2), value => Assert.Equal(7.5f, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpuLoopBackend(new CpuLoopOptions(BlockSize: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpuLoopBackend(new CpuLoopOptions(MaxDegreeOfParallelism: 0)));
    }

    // The Tensor stage backend checks the source table against the ranges of the circuit. A check that fails there
    // gives a LaneFault for its document, as a check of the runner does, and the run goes on. The test narrows
    // the circuit range of the item cell to 0..10 on a new translation, so the cell checks of the runner pass.
    [Fact]
    public void A_failed_range_check_of_the_Tensor_backend_gives_a_LaneFault()
    {
        var function = Sharpwired.IL.HandwiredTranslator.TranslateFunction(typeof(LoopInputs).GetMethod(nameof(LoopInputs.SumStep))!);
        var item = function.Inputs.Single(input => input.Path == "item").Wire;
        function.Table.Circuit.AssumeRange(item, 0, 10, allowExistingReaders: true);
        var stage = SumStage() with { Function = function };
        var program = LoopProgram.Create(Positions, [Documents(Positions)], [new LoopRegister("total", 0)], [],
            [new LoopOutputGroup("total", OutputGroupKind.Document, 1, 0)], [stage, SumTotalStage()], 0);
        var cells = CreateDocuments(20, Positions, 0x7e45);
        using var backend = new CpuLoopBackend(new CpuLoopOptions(CpuStageBackend.Tensor, BlockSize: 4));
        using var runner = backend.CreateRunner(program);
        runner.WriteShared([]);
        using var input = backend.Upload(cells, 20, Positions + 1);
        var run = runner.Run(new LoopRequest([input], 20, [], 0, 0));
        Assert.Equal(20, run.DocumentsDone);
        Assert.NotEmpty(run.Faults);
        Assert.All(run.Faults, fault => Assert.Contains("source wire", fault.Message, StringComparison.Ordinal));
        Assert.All(run.Faults, fault => Assert.True(float.IsNaN(run.DocumentValues[fault.Document])));
    }

    [Fact]
    public void A_call_after_Dispose_throws_ObjectDisposedException()
    {
        var program = Sum(Positions);
        var cells = CreateDocuments(4, Positions, 0xd15);
        var backend = new CpuLoopBackend();
        var runner = backend.CreateRunner(program);
        var input = backend.Upload(cells, 4, Positions + 1);
        var request = new LoopRequest([input], 4, [], 0, 0);
        runner.Run(request);

        input.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runner.Run(request));
        Assert.Throws<ObjectDisposedException>(() => backend.Read(input, 0, 1));
        input = backend.Upload(cells, 4, Positions + 1);
        request = request with { Inputs = [input] };
        runner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runner.Run(request));
        Assert.Throws<ObjectDisposedException>(() => runner.WriteShared([]));
        runner.Dispose();

        var second = backend.CreateRunner(program);
        backend.Dispose();
        Assert.Throws<ObjectDisposedException>(() => second.Run(request));
        Assert.Throws<ObjectDisposedException>(() => backend.Upload(cells, 4, Positions + 1));
        Assert.Throws<ObjectDisposedException>(() => backend.Allocate(1, 1, 0));
        Assert.Throws<ObjectDisposedException>(() => backend.Read(input, 0, 1));
        Assert.Throws<ObjectDisposedException>(() => backend.CreateRunner(program));
        backend.Dispose();
    }

    private static (LoopProgram Program, float[] Shared) Program(string name) => name switch
    {
        "Sum" => (Sum(Positions), []),
        "Leaders" => (Leaders(Positions), []),
        "Lookup" => (Lookup(Positions), LookupShared()),
        "Overflow" => (Overflow(Positions), []),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such program"),
    };

    // The values of EvaluatePlain in the layout of a LoopRun: the document groups, group after group and
    // document after document, then the position groups in the same order.
    private static (float[] DocumentValues, float[] PositionValues) Plain(LoopProgram program, float[] cells, int count, float[] shared)
    {
        var cellsPerDocument = program.Buffers[0].CellsPerDocument;
        var results = Enumerable.Range(0, count).Select(document => program.EvaluatePlain([cells.AsMemory(document * cellsPerDocument, cellsPerDocument)], shared)).ToArray();
        var documentValues = new List<float>();
        var positionValues = new List<float>();
        var (documentOffset, positionOffset) = (0, 0);
        foreach (var group in program.OutputGroups)
        {
            var size = group.ValuesPerDocument;
            if (group.Kind == OutputGroupKind.Document)
            {
                foreach (var result in results) documentValues.AddRange(result.DocumentOutputs.Skip(documentOffset).Take(size));
                documentOffset += size;
            }
            else
            {
                foreach (var result in results) positionValues.AddRange(result.PositionOutputs.Skip(positionOffset).Take(size));
                positionOffset += size;
            }
        }
        return ([.. documentValues], [.. positionValues]);
    }

    private static void AllIs(float[] expected, IReadOnlyList<float> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            if (!Js.ObjectIs(expected[index], actual[index])) Assert.Fail($"value {index}: expected {expected[index]:R}, got {actual[index]:R}");
        }
    }

    private static void AllSame(float[] expected, IReadOnlyList<float> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            if (!Js.SameNumericValue(expected[index], actual[index])) Assert.Fail($"value {index}: expected {expected[index]:R}, got {actual[index]:R}");
        }
    }

    // An IProgress that runs the action on the thread of the report, so the test sees each report in order.
    private sealed class SynchronousProgress(Action<int> action) : IProgress<int>
    {
        public void Report(int value) => action(value);
    }
}
