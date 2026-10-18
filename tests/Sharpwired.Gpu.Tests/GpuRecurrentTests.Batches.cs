// No Handwired source. The carried recurrent items of phase 5 (phase 6 plan, task 7): the setting
// StepsPerDispatch, one suffix dispatch for each chunk, and a step kernel with 600 cells.

using System.Diagnostics;
using Sharpwired.IL;
using Sharpwired.IL.Tests;
using Sharpwired.IL.Tests.Inputs;

namespace Sharpwired.Gpu.Tests;

public sealed partial class GpuRecurrentTests
{
    // StepsPerDispatch of 1, 7 and 1,024 give equal batches. The lanes of Invariant need at most 10 steps,
    // and the lanes of DoubleUntil need at most 23 steps and hold the invalid lane 5. With 7 evaluations
    // in one dispatch, a lane stops in the middle of a dispatch or at the end of one.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void StepsPerDispatch_of_1_7_and_1024_give_equal_batches(GpuWriter writer)
    {
        var invariant = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var invariantLanes = SeededSources.Create(invariant.Prefix.Table, 128, 5404).Concat(EdgeValues[nameof(RecurrentInputs.Invariant)]).ToList();
        var doubling = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.DoubleUntil));
        var doublingLanes = InvalidLaneTables();
        fixture.ForEachDevice(output, device =>
        {
            foreach (var (function, lanes) in new[] { (invariant, invariantLanes), (doubling, doublingLanes) })
            {
                var batches = new List<RecurrentBatch>();
                foreach (var steps in new[] { 1, 7, 1024 })
                {
                    using var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(steps, writer));
                    Assert.Equal(steps, runner.StepsPerDispatch);
                    var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);
                    output.WriteLine($"StepsPerDispatch {steps}: {runner.StepDispatches} step dispatches, {runner.SuffixDispatches} suffix dispatches, at most {batch.Steps.Max()} steps");
                    Assert.True(runner.StepDispatches >= ((batch.Steps.Max() + steps) / steps), $"{runner.StepDispatches} step dispatches");
                    batches.Add(batch);
                    lastCompare = (function, lanes, StepLimit);
                    AssertNoReplayPass(runner, device);
                }

                AssertLanesEqualCpu(device, function, writer, lanes, batches[^1], StepLimit);
                foreach (var batch in batches.Take(2)) AssertBatchesEqual(batches[^1], batch);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => GpuRecurrentRunner.Create(device, invariant, new GpuRecurrentOptions(0, writer)));
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuRecurrentRunner.Create(device, invariant, new GpuRecurrentOptions(-1, writer)));
        });
    }

    // The suffix runs one time for each chunk, after the last round of step dispatches. CountUp with the
    // lanes 0 to 2,500 needs three rounds. With chunks of 1,000 lanes, the batch has three chunks.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void The_suffix_runs_one_time_for_each_chunk_after_the_last_round(GpuWriter writer)
    {
        const int limit = 2500;
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.CountUp));
        var lanes = Enumerable.Range(0, limit + 1).Select(n => new[] { (float)n }).ToList();
        fixture.ForEachDevice(output, device =>
        {
            using (var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(Writer: writer)))
            {
                var batch = runner.Run(Flatten(lanes), lanes.Count, limit);
                output.WriteLine($"one chunk: {runner.StepDispatches} step dispatches, {runner.SuffixDispatches} suffix dispatches");
                Assert.Equal(3, runner.StepDispatches);
                Assert.Equal(1, runner.SuffixDispatches);
                AssertLanesEqualCpu(device, function, writer, lanes, batch, limit);
                AssertNoReplayPass(runner, device);
            }

            using (var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(Writer: writer), maxLanesPerChunk: 1000))
            {
                var batch = runner.Run(Flatten(lanes), lanes.Count, limit);
                output.WriteLine($"three chunks: {runner.StepDispatches} step dispatches, {runner.SuffixDispatches} suffix dispatches");
                Assert.Equal(1 + 2 + 3, runner.StepDispatches);
                Assert.Equal(3, runner.SuffixDispatches);
                AssertLanesEqualCpu(device, function, writer, lanes, batch, limit);
                AssertNoReplayPass(runner, device);
            }
        });
    }

    // The step kernel with 600 state cells: the struct of WideStateInputs.Rotate600. Each lane equals the
    // CPU runner, with 1,024 and with 7 evaluations in one dispatch.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_step_of_600_state_cells_equals_the_CPU_runner(GpuWriter writer)
    {
        var function = Translate(typeof(WideStateInputs), nameof(WideStateInputs.Rotate600));
        Assert.True(function.Step.State.Length >= 600, $"{function.Step.State.Length} state cells");
        Assert.True(function.Step.Table.Circuit.SourceSize > 512);
        var lanes = new[] { 0, 1, 2, 599, 600, 601, 1234, 2000 }.Select(n => new[] { (float)n }).Concat(SeededSources.Create(function.Prefix.Table, 56, 5505)).ToList();
        var cpu = new CpuRecurrentRunner(function);
        var expected = cpu.Run(Flatten(lanes), lanes.Count, StepLimit);
        Assert.Equal(599f, expected.Lane(0).Outputs[0]);
        Assert.Equal(1f, expected.Lane(1).Outputs[0]);
        Assert.Equal(599f, expected.Lane(4).Outputs[0]);
        AssertLanesEqualCpu(null, function, writer, lanes, expected, StepLimit);
        fixture.ForEachDevice(output, device =>
        {
            // With 7 evaluations in one dispatch, the 600 cells go back to the buffer and come in again many times.
            foreach (var steps in new[] { 1024, 7 })
            {
                var create = Stopwatch.StartNew();
                using var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(steps, writer));
                create.Stop();
                var watch = Stopwatch.StartNew();
                var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);
                watch.Stop();
                output.WriteLine($"Rotate600, {writer}, StepsPerDispatch {steps}: {function.Step.Table.Circuit.SourceSize} cells, create {create.Elapsed.TotalMilliseconds:F1} ms, {lanes.Count} lanes, at most {batch.Steps.Max()} steps, {runner.StepDispatches} step dispatches, run {watch.Elapsed.TotalMilliseconds:F1} ms");
                if (steps == 7) Assert.True(runner.StepDispatches > batch.Steps.Max() / 7, $"{runner.StepDispatches} step dispatches");
                AssertLanesEqualCpu(device, function, writer, lanes, batch, StepLimit);
                AssertNoReplayPass(runner, device);
            }
        });
    }

    // Two GPU batches are equal: the same status, steps and messages, and outputs by Js.ObjectIs.
    private static void AssertBatchesEqual(RecurrentBatch expected, RecurrentBatch actual)
    {
        Assert.Equal(expected.OutputCount, actual.OutputCount);
        Assert.Equal(expected.Status.ToArray(), actual.Status.ToArray());
        Assert.Equal(expected.Steps.ToArray(), actual.Steps.ToArray());
        Assert.Equal(expected.Messages.ToArray(), actual.Messages.ToArray());
        Assert.Equal(expected.Outputs.Length, actual.Outputs.Length);
        for (var index = 0; index < expected.Outputs.Length; index++)
        {
            Assert.True(Js.ObjectIs(expected.Outputs[index], actual.Outputs[index]), $"output {index}: {actual.Outputs[index]:R}, expected {expected.Outputs[index]:R}");
        }
    }
}
