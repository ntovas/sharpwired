// No Handwired source. R5.4: the recurrent step on the GPU (phase 5 plan, task 5, and question 6,
// "Recurrent tests" and "Wide-cell test"). Each test runs on each adapter with both writers, and on
// CpuRecurrentRunner. Each runner runs through IRecurrentBatchRunner (phase 6 plan, task 7). The test
// compares each lane with RecurrentRunner.Run on the CPU. The steps and Completed must be the same. The outputs
// must agree by Js.ObjectIs (f32 writer) or Js.SameNumericValue (integer writer). The GPU and the CPU run
// the same programs, so the f32 writer gives the same sign of zero.
//
// The subnormal rule (decision 4) applies on a device that flushes subnormal values. There, a lane that
// differs from the CPU passes only when its CPU run holds a nonzero value of magnitude below 2^-126. The
// test writes the count of such lanes. A lane that equals the CPU needs no such check.

using System.Diagnostics;
using Sharpwired.Cells;
using Sharpwired.IL;
using Sharpwired.IL.Tests;
using Sharpwired.IL.Tests.Inputs;
using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed partial class GpuRecurrentTests(GpuFixture fixture, ITestOutputHelper output)
{
    private const int StepLimit = 100_000;

    private const int SeededLanes = 1024;

    private static readonly GpuWriter[] Writers = [GpuWriter.Float32, GpuWriter.Integer];

    // The methods of RecurrentInputs that RecurrentTests translates, with the edge values of
    // RecurrentTests. The translator refuses Grow.
    public static TheoryData<string, GpuWriter> Inputs()
    {
        var data = new TheoryData<string, GpuWriter>();
        foreach (var name in EdgeValues.Keys)
        {
            foreach (var writer in Writers) data.Add(name, writer);
        }
        return data;
    }

    internal static readonly Dictionary<string, float[][]> EdgeValues = new()
    {
        [nameof(RecurrentInputs.Halve)] = [[1000000f], [1f], [0f], [-0f], [1.5f], [999999.94f]],
        [nameof(RecurrentInputs.CountDown)] = [[100f], [0f], [1f], [3f], [99f]],
        [nameof(RecurrentInputs.SlowDecay)] = [[1000000f], [0.001f], [0f]],
        [nameof(RecurrentInputs.Invariant)] = [[1000f, 1f], [1000f, 4f], [0f, 1f], [4f, 2f], [40f, 2f]],
        [nameof(RecurrentInputs.HalveAtLeastOnce)] = [[1000000f], [2f], [1f], [0f]],
        [nameof(RecurrentInputs.Q1Lag)] = [[43f], [100f], [21f], [0f]],
        [nameof(RecurrentInputs.ChainLag)] = [[100f], [27f], [26f], [0f]],
        [nameof(RecurrentInputs.LaterHalf)] = [[1000000f], [0f], [17f]],
        [nameof(RecurrentInputs.Fall)] = [[100f], [0f], [3f]],
    };

    private static RecurrentFunction Translate(Type type, string name) => RecurrentKernelWriterTests.Translate(type, name);

    private static float[] Flatten(IReadOnlyList<float[]> lanes) => [.. lanes.SelectMany(lane => lane)];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Each_lane_of_1024_seeded_tables_and_the_edge_values_equals_the_CPU_runner(string name, GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), name);
        var lanes = SeededSources.Create(function.Prefix.Table, SeededLanes, 5000 + name.Length).Concat(EdgeValues[name]).ToList();
        ForEachRunner(function, writer, (runner, device) =>
        {
            var watch = Stopwatch.StartNew();
            var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);
            watch.Stop();
            output.WriteLine($"{name}, {writer}: {lanes.Count} lanes, at most {batch.Steps.Max()} steps, {watch.Elapsed.TotalMilliseconds:F1} ms");
            AssertLanesEqualCpu(device, function, writer, lanes, batch, StepLimit);
        });
    }

    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_step_limit_below_the_needed_count_stops_each_lane_as_the_CPU_runner_does(GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve));
        var lanes = SeededSources.Create(function.Prefix.Table, 256, 5101).Concat(EdgeValues[nameof(RecurrentInputs.Halve)]).ToList();
        ForEachRunner(function, writer, (runner, device) =>
        {
            var batch = runner.Run(Flatten(lanes), lanes.Count, 3);
            Assert.Contains(RecurrentLaneStatus.Limit, batch.Status);
            Assert.Contains(RecurrentLaneStatus.Completed, batch.Status);
            Assert.Equal(3, batch.Steps.Max());
            AssertLanesEqualCpu(device, function, writer, lanes, batch, 3);
        });
    }

    // CountUp needs n + 1 evaluations. The lanes need 0 to 2,500 steps, so the run needs three
    // dispatches of 1,024 evaluations. The lane with n = 2,500 needs 2,501 evaluations, so the limit
    // stops it.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_step_limit_of_2500_runs_lanes_of_0_to_2500_steps_over_three_dispatches(GpuWriter writer)
    {
        const int limit = 2500;
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.CountUp));
        var lanes = Enumerable.Range(0, limit + 1).Select(n => new[] { (float)n }).ToList();
        ForEachRunner(function, writer, (runner, device) =>
        {
            var watch = Stopwatch.StartNew();
            var batch = runner.Run(Flatten(lanes), lanes.Count, limit);
            watch.Stop();
            output.WriteLine($"CountUp, {writer}: {lanes.Count} lanes, limit {limit}, {watch.Elapsed.TotalMilliseconds:F1} ms");
            if (runner is GpuRecurrentRunner gpu) Assert.True(limit > 2 * gpu.StepsPerDispatch);
            Assert.Equal(limit - 1, batch.Steps[limit - 1]);
            Assert.Equal(RecurrentLaneStatus.Completed, batch.Status[limit - 1]);
            Assert.Equal(limit, batch.Steps[limit]);
            Assert.Equal(RecurrentLaneStatus.Limit, batch.Status[limit]);
            AssertLanesEqualCpu(device, function, writer, lanes, batch, limit);
        });
    }

    // The wide-cell test of the plan (the phase 4 carried item), and the invalid lane of the phase 6 plan.
    // The value x has the wide int range -2^23..2^23. For n = 30 in lane 5, x is 2^24 after it doubles 24 times, and
    // the cell check stops the lane. The GPU runner replays the lane on the CPU and keeps its message. The
    // other lanes are valid and keep their results. ThrowIfInvalid throws the message of phase 5.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_wide_cell_past_its_range_gives_the_status_Invalid_and_the_CPU_message_for_its_lane(GpuWriter writer)
    {
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.DoubleUntil));
        var x = function.Step.State.Single(cell => cell.Path != "n" && function.Step.Table.Circuit.Ranges[cell.Wire.Position].Maximum == 8_388_608);
        Assert.Equal(-8_388_608, function.Step.Table.Circuit.Ranges[x.Wire.Position].Minimum);
        var expected = Assert.Throws<InvalidOperationException>(() => new RecurrentRunner(function).Run([30f], StepLimit));
        output.WriteLine($"CPU: {expected.Message}");

        var lanes = InvalidLaneTables();
        ForEachRunner(function, writer, (runner, device) =>
        {
            var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);
            Assert.Equal(RecurrentLaneStatus.Invalid, batch.Status[5]);
            Assert.Equal(expected.Message, batch.Messages[5]);
            // An invalid lane has NaN outputs and 0 steps.
            Assert.All(batch.Outputs.AsSpan(5 * batch.OutputCount, batch.OutputCount).ToArray(), value => Assert.True(float.IsNaN(value), $"output {value:R}"));
            Assert.Equal(0, batch.Steps[5]);
            Assert.Equal(1, batch.Status.Count(status => status == RecurrentLaneStatus.Invalid));
            var error = Assert.Throws<InvalidOperationException>(batch.ThrowIfInvalid);
            output.WriteLine($"{runner.GetType().Name}: {error.Message}");
            Assert.Equal($"lane 5: {expected.Message}", error.Message);
            Assert.Equal(8_388_608f, batch.Lane(24).Outputs[0]);
            AssertLanesEqualCpu(device, function, writer, lanes, batch, StepLimit);
        });
    }

    /// <summary>The lanes of DoubleUntil for n = 0 to 23, with n = 30 at lane 5.</summary>
    internal static List<float[]> InvalidLaneTables()
    {
        var valid = Enumerable.Range(0, 24).Select(n => new[] { (float)n }).ToList();
        return [.. valid.Take(5), [30f], .. valid.Skip(5)];
    }

    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_bit_state_cell_gives_the_CPU_values(GpuWriter writer)
    {
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.FlipUntil));
        Assert.True(function.Step.Table.Circuit.ExactBits[function.Step.State.Single(cell => function.Step.Table.Circuit.ExactBits[cell.Wire.Position]).Wire.Position]);
        var lanes = Enumerable.Range(0, 41).Select(n => new[] { (float)n }).ToList();
        ForEachRunner(function, writer, (runner, device) =>
            AssertLanesEqualCpu(device, function, writer, lanes, runner.Run(Flatten(lanes), lanes.Count, StepLimit), StepLimit));
    }

    [Theory]
    [MemberData(nameof(WriterData))]
    public void Run_of_one_lane_equals_the_CPU_runner_and_a_step_limit_of_0_gives_no_step(GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var cpu = new RecurrentRunner(function);
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(Writer: writer));
            foreach (var arguments in EdgeValues[nameof(RecurrentInputs.Invariant)])
            {
                AssertLaneEquals(cpu.Run(arguments, StepLimit), runner.Run(arguments, StepLimit), writer, $"[{string.Join(", ", arguments)}]");
                var none = runner.Run(arguments, 0);
                Assert.False(none.Completed);
                Assert.Equal(0, none.Steps);
                AssertLaneEquals(cpu.Run(arguments, 0), none, writer, $"limit 0, [{string.Join(", ", arguments)}]");
            }

            var empty = runner.Run([], 0, StepLimit);
            Assert.Empty(empty.Steps);
            Assert.Equal(0, runner.ReplayPasses);
        });
    }

    [Fact]
    public void Run_checks_its_arguments_and_Dispose_stops_the_runner()
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve));
        fixture.ForEachDevice(output, device =>
        {
            var runner = GpuRecurrentRunner.Create(device, function);
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run([8f], -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run([8f], -1, StepLimit));
            Assert.Throws<ArgumentException>(() => runner.Run([8f, 4f], 1, StepLimit));
            Assert.Throws<ArgumentException>(() => runner.Run([8f, 4f], StepLimit));

            // An argument past its declared range throws the exception of the validator of the prefix.
            var expected = Assert.Throws<CellValueException>(() => function.Prefix.Table.Validator.Validate([-1f]));
            var error = Assert.Throws<CellValueException>(() => runner.Run([8f, -1f], 2, StepLimit));
            Assert.Equal(expected.Message, error.Message);
            Assert.Throws<ArgumentNullException>(() => GpuRecurrentRunner.Create(device, null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuRecurrentRunner.Create(device, function, (GpuWriter)7));
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(Writer: (GpuWriter)7)));
            Assert.Same(function, runner.Function);
            Assert.Equal(1024, runner.StepsPerDispatch);
            using (var created = RecurrentRunners.Create(function, device, new GpuRecurrentOptions(7)))
            {
                Assert.Equal(7, Assert.IsType<GpuRecurrentRunner>(created).StepsPerDispatch);
            }

            runner.Dispose();
            runner.Dispose();
            Assert.Throws<ObjectDisposedException>(() => runner.Run([8f], StepLimit));
        });
    }

    // A lower chunk limit splits a batch into chunks. Each chunk runs its own dispatches.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_batch_in_chunks_equals_the_CPU_runner(GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var lanes = SeededSources.Create(function.Prefix.Table, 250, 5202).ToList();
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, new GpuRecurrentOptions(Writer: writer), maxLanesPerChunk: 64);
            Assert.Equal(64, runner.LanesPerChunk);
            AssertLanesEqualCpu(device, function, writer, lanes, runner.Run(Flatten(lanes), lanes.Count, StepLimit), StepLimit);
            AssertNoReplayPass(runner, device);
        });
    }

    public static TheoryData<GpuWriter> WriterData => [.. Writers];

    // Run the test on CpuRecurrentRunner, then on the GPU runner of the writer on each device. The device
    // is null for the CPU runner.
    private void ForEachRunner(RecurrentFunction function, GpuWriter writer, Action<IRecurrentBatchRunner, GpuDevice?> test, GpuRecurrentOptions? options = null)
    {
        using (IRecurrentBatchRunner cpu = new CpuRecurrentRunner(function))
        {
            output.WriteLine("[cpu] CpuRecurrentRunner");
            test(cpu, null);
        }

        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, (options ?? new GpuRecurrentOptions()) with { Writer = writer });
            test(runner, device);
            AssertNoReplayPass(runner, device);
        });
    }

    // The lanes of the last GPU compare, for AssertNoReplayPass.
    private (RecurrentFunction Function, IReadOnlyList<float[]> Lanes, int StepLimit)? lastCompare;

    // A lane that the kernel marked failed and whose CPU replay passed gets the CPU result. Thus the compare of
    // the lanes cannot see a false kernel check. The replay passes of the runner must be 0, apart from the lanes
    // of the last compare that the subnormal rule excludes.
    private void AssertNoReplayPass(GpuRecurrentRunner runner, GpuDevice device)
    {
        output.WriteLine($"replay passes: {runner.ReplayPasses}");
        if (runner.ReplayPasses == 0) return;
        Assert.True(device.FlushesSubnormals && lastCompare is not null, $"{runner.ReplayPasses} lanes failed a kernel check and passed on the CPU");
        var (function, lanes, stepLimit) = lastCompare!.Value;
        var subnormal = lanes.Count(lane => SubnormalReplay.Holds(function, lane, stepLimit));
        Assert.True(runner.ReplayPasses <= subnormal, $"{runner.ReplayPasses} lanes failed a kernel check and passed on the CPU, and the subnormal rule excludes {subnormal}");
    }

    // The CPU runner equals RecurrentRunner.Run exactly. A GPU lane equals it with the rules of the writer
    // and the subnormal rule. A lane that RecurrentRunner.Run refuses has the status Invalid and the CPU
    // message.
    private void AssertLanesEqualCpu(GpuDevice? device, RecurrentFunction function, GpuWriter writer, IReadOnlyList<float[]> lanes, RecurrentBatch batch, int stepLimit)
    {
        if (device is null)
        {
            CpuRecurrentRunnerTests.AssertLanesEqualRecurrentRunner(function, lanes, batch, stepLimit);
            return;
        }

        lastCompare = (function, lanes, stepLimit);
        Assert.Equal(lanes.Count, batch.Steps.Length);
        Assert.Equal(lanes.Count, batch.Status.Length);
        Assert.Equal(lanes.Count * function.Suffix.Outputs.Length, batch.Outputs.Length);
        var cpu = new RecurrentRunner(function);
        var excluded = 0;
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            var context = $"lane {lane} [{string.Join(", ", lanes[lane].Select(value => value.ToString("R")))}]";
            RecurrentResult expected;
            try
            {
                expected = cpu.Run(lanes[lane], stepLimit);
            }
            catch (InvalidOperationException error)
            {
                Assert.True(batch.Status[lane] == RecurrentLaneStatus.Invalid, $"{context}: status {batch.Status[lane]}, CPU {error.Message}");
                Assert.Equal(error.Message, batch.Messages[lane]);
                continue;
            }

            Assert.True(batch.Status[lane] != RecurrentLaneStatus.Invalid, $"{context}: status Invalid ({batch.Messages[lane]}), and the CPU run passed");
            var actual = batch.Lane(lane);
            if (LaneEquals(expected, actual, writer)) continue;
            if (device.FlushesSubnormals && SubnormalReplay.Holds(function, lanes[lane], stepLimit))
            {
                excluded++;
                continue;
            }

            AssertLaneEquals(expected, actual, writer, context);
        }

        output.WriteLine($"compared {lanes.Count - excluded} lanes, excluded by the subnormal rule {excluded}");
    }

    private static bool LaneEquals(RecurrentResult expected, RecurrentResult actual, GpuWriter writer) =>
        expected.Steps == actual.Steps && expected.Completed == actual.Completed && expected.Outputs.Length == actual.Outputs.Length
        && expected.Outputs.Zip(actual.Outputs).All(pair => writer == GpuWriter.Float32 ? Js.ObjectIs(pair.First, pair.Second) : Js.SameNumericValue(pair.First, pair.Second));

    private static void AssertLaneEquals(RecurrentResult expected, RecurrentResult actual, GpuWriter writer, string context)
    {
        Assert.True(expected.Steps == actual.Steps, $"{context}: steps {actual.Steps}, CPU {expected.Steps}");
        Assert.True(expected.Completed == actual.Completed, $"{context}: Completed {actual.Completed}, CPU {expected.Completed}");
        Assert.Equal(expected.Outputs.Length, actual.Outputs.Length);
        for (var leaf = 0; leaf < expected.Outputs.Length; leaf++)
        {
            var same = writer == GpuWriter.Float32 ? Js.ObjectIs(expected.Outputs[leaf], actual.Outputs[leaf]) : Js.SameNumericValue(expected.Outputs[leaf], actual.Outputs[leaf]);
            Assert.True(same, $"{context}: output {leaf} is {actual.Outputs[leaf]:R}, CPU {expected.Outputs[leaf]:R}");
        }
    }
}
