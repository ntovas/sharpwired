// No Handwired source. R5.4: the recurrent step on the GPU (phase 5 plan, task 5, and question 6,
// "Recurrent tests" and "Wide-cell test"). Each test runs on each adapter with both writers, and compares
// each lane with RecurrentRunner.Run on the CPU: the same steps, the same Completed, and outputs with
// Js.ObjectIs (f32 writer) or Js.SameNumericValue (integer writer). The GPU and the CPU run the same
// programs, so the f32 writer gives the same sign of zero.
//
// The subnormal rule (decision 4): on a device that flushes subnormal values, a lane that differs from
// the CPU passes only when the CPU run of the lane holds a nonzero value of magnitude below 2^-126. The
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
    // RecurrentTests. Grow is refused by the translator.
    public static TheoryData<string, GpuWriter> Inputs()
    {
        var data = new TheoryData<string, GpuWriter>();
        foreach (var name in EdgeValues.Keys)
        {
            foreach (var writer in Writers) data.Add(name, writer);
        }
        return data;
    }

    private static readonly Dictionary<string, float[][]> EdgeValues = new()
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
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
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
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
            var batch = runner.Run(Flatten(lanes), lanes.Count, 3);
            Assert.Contains(false, batch.Completed);
            Assert.Contains(true, batch.Completed);
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
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
            var watch = Stopwatch.StartNew();
            var batch = runner.Run(Flatten(lanes), lanes.Count, limit);
            watch.Stop();
            output.WriteLine($"CountUp, {writer}: {lanes.Count} lanes, limit {limit}, {watch.Elapsed.TotalMilliseconds:F1} ms");
            Assert.True(limit > 2 * GpuRecurrentRunner.StepsPerDispatch);
            Assert.Equal(limit - 1, batch.Steps[limit - 1]);
            Assert.True(batch.Completed[limit - 1]);
            Assert.Equal(limit, batch.Steps[limit]);
            Assert.False(batch.Completed[limit]);
            AssertLanesEqualCpu(device, function, writer, lanes, batch, limit);
        });
    }

    // The wide-cell test of the plan (the phase 4 carried item). x has the wide int range -2^23..2^23.
    // For n = 30, the 24th doubling gives 2^24, and the cell check stops the lane. The GPU runner replays
    // the lane on the CPU and throws its message. The other lanes are valid.
    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_wide_cell_past_its_range_throws_the_CPU_message_for_its_lane(GpuWriter writer)
    {
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.DoubleUntil));
        var x = function.Step.State.Single(cell => cell.Path != "n" && function.Step.Table.Circuit.Ranges[cell.Wire.Position].Maximum == 8_388_608);
        Assert.Equal(-8_388_608, function.Step.Table.Circuit.Ranges[x.Wire.Position].Minimum);
        var expected = Assert.Throws<InvalidOperationException>(() => new RecurrentRunner(function).Run([30f], StepLimit));
        output.WriteLine($"CPU: {expected.Message}");

        var valid = Enumerable.Range(0, 24).Select(n => new[] { (float)n }).ToList();
        var lanes = valid.Take(12).Append([30f]).Concat(valid.Skip(12)).ToList();
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
            var error = Assert.Throws<InvalidOperationException>(() => runner.Run(Flatten(lanes), lanes.Count, StepLimit));
            output.WriteLine($"GPU: {error.Message}");
            Assert.Equal($"lane 12: {expected.Message}", error.Message);
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal(expected.Message, error.InnerException!.Message);

            // Without the lane of n = 30, each lane equals the CPU, and the largest value is 2^23.
            var batch = runner.Run(Flatten(valid), valid.Count, StepLimit);
            Assert.Equal(8_388_608f, batch.Lane(23).Outputs[0]);
            AssertLanesEqualCpu(device, function, writer, valid, batch, StepLimit);
        });
    }

    [Theory]
    [MemberData(nameof(WriterData))]
    public void A_bit_state_cell_gives_the_CPU_values(GpuWriter writer)
    {
        var function = Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.FlipUntil));
        Assert.True(function.Step.Table.Circuit.ExactBits[function.Step.State.Single(cell => function.Step.Table.Circuit.ExactBits[cell.Wire.Position]).Wire.Position]);
        var lanes = Enumerable.Range(0, 41).Select(n => new[] { (float)n }).ToList();
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
            AssertLanesEqualCpu(device, function, writer, lanes, runner.Run(Flatten(lanes), lanes.Count, StepLimit), StepLimit);
        });
    }

    [Theory]
    [MemberData(nameof(WriterData))]
    public void Run_of_one_lane_equals_the_CPU_runner_and_a_step_limit_of_0_gives_no_step(GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var cpu = new RecurrentRunner(function);
        fixture.ForEachDevice(output, device =>
        {
            using var runner = GpuRecurrentRunner.Create(device, function, writer);
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
            using var runner = GpuRecurrentRunner.Create(device, function, writer, maxLanesPerChunk: 64);
            Assert.Equal(64, runner.LanesPerChunk);
            AssertLanesEqualCpu(device, function, writer, lanes, runner.Run(Flatten(lanes), lanes.Count, StepLimit), StepLimit);
        });
    }

    public static TheoryData<GpuWriter> WriterData => [.. Writers];

    private void AssertLanesEqualCpu(GpuDevice device, RecurrentFunction function, GpuWriter writer, IReadOnlyList<float[]> lanes, GpuRecurrentBatch batch, int stepLimit)
    {
        Assert.Equal(lanes.Count, batch.Steps.Length);
        Assert.Equal(lanes.Count, batch.Completed.Length);
        Assert.Equal(lanes.Count * function.Suffix.Outputs.Length, batch.Outputs.Length);
        var cpu = new RecurrentRunner(function);
        var excluded = 0;
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            var expected = cpu.Run(lanes[lane], stepLimit);
            var actual = batch.Lane(lane);
            if (LaneEquals(expected, actual, writer)) continue;
            if (device.FlushesSubnormals && SubnormalReplay.Holds(function, lanes[lane], stepLimit))
            {
                excluded++;
                continue;
            }

            AssertLaneEquals(expected, actual, writer, $"lane {lane} [{string.Join(", ", lanes[lane].Select(value => value.ToString("R")))}]");
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
