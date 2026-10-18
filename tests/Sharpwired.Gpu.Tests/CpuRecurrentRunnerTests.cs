// No Handwired source. The CPU batch runner of a RecurrentFunction (phase 6 plan, task 7, and question 4,
// the carried items of phase 5). Each lane of CpuRecurrentRunner must give the outputs, the steps and the
// Completed of RecurrentRunner.Run, on 1 and on 8 threads. The outputs agree by Js.ObjectIs: both runners
// evaluate the same programs on the CPU. A lane that fails a check gets the status Invalid and the CPU
// message, and the other lanes keep their results.

using Sharpwired.Cells;
using Sharpwired.IL;
using Sharpwired.IL.Tests;
using Sharpwired.IL.Tests.Inputs;

namespace Sharpwired.Gpu.Tests;

public sealed class CpuRecurrentRunnerTests
{
    private const int StepLimit = 100_000;

    private const int SeededLanes = 1024;

    public static TheoryData<string, int> Inputs()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in GpuRecurrentTests.EdgeValues.Keys)
        {
            foreach (var threads in new[] { 1, 8 }) data.Add(name, threads);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Each_lane_of_1024_seeded_tables_and_the_edge_values_equals_RecurrentRunner(string name, int threads)
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(RecurrentInputs), name);
        var lanes = SeededSources.Create(function.Prefix.Table, SeededLanes, 5000 + name.Length).Concat(GpuRecurrentTests.EdgeValues[name]).ToList();
        using var runner = new CpuRecurrentRunner(function, threads);
        Assert.Same(function, runner.Function);
        var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);
        Assert.Equal(function.Suffix.Outputs.Length, batch.OutputCount);
        AssertLanesEqualRecurrentRunner(function, lanes, batch, StepLimit);
    }

    // A step limit stops some lanes: their status is Limit, and Lane gives Completed false.
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void A_step_limit_gives_the_status_Limit_as_RecurrentRunner_gives_Completed_false(int threads)
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve));
        var lanes = SeededSources.Create(function.Prefix.Table, 256, 5101).ToList();
        using var runner = new CpuRecurrentRunner(function, threads);
        var batch = runner.Run(Flatten(lanes), lanes.Count, 3);
        Assert.Contains(RecurrentLaneStatus.Limit, batch.Status);
        Assert.Contains(RecurrentLaneStatus.Completed, batch.Status);
        AssertLanesEqualRecurrentRunner(function, lanes, batch, 3);
    }

    // The plan test: lane 5 of DoubleUntil has n = 30, and a next value of x passes the wide int range.
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void An_invalid_lane_gets_the_CPU_message_and_the_other_lanes_keep_their_results(int threads)
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.DoubleUntil));
        var expected = Assert.Throws<InvalidOperationException>(() => new RecurrentRunner(function).Run([30f], StepLimit));
        var lanes = GpuRecurrentTests.InvalidLaneTables();
        using var runner = new CpuRecurrentRunner(function, threads);
        var batch = runner.Run(Flatten(lanes), lanes.Count, StepLimit);

        Assert.Equal(RecurrentLaneStatus.Invalid, batch.Status[5]);
        Assert.Equal(expected.Message, batch.Messages[5]);
        Assert.Equal(0, batch.Steps[5]);
        Assert.True(float.IsNaN(batch.Outputs[5]));
        Assert.Equal(1, batch.Status.Count(status => status == RecurrentLaneStatus.Invalid));
        AssertLanesEqualRecurrentRunner(function, lanes, batch, StepLimit);

        var error = Assert.Throws<InvalidOperationException>(batch.ThrowIfInvalid);
        Assert.Equal($"lane 5: {expected.Message}", error.Message);
        var laneError = Assert.Throws<InvalidOperationException>(() => batch.Lane(5));
        Assert.Equal($"lane 5: {expected.Message}", laneError.Message);
        Assert.Equal(16f, batch.Lane(4).Outputs[0]);
    }

    [Fact]
    public void A_batch_with_no_invalid_lane_passes_ThrowIfInvalid()
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(GpuRecurrentInputs), nameof(GpuRecurrentInputs.DoubleUntil));
        using var runner = new CpuRecurrentRunner(function);
        var batch = runner.Run([0f, 23f], 2, StepLimit);
        batch.ThrowIfInvalid();
        Assert.All(batch.Messages, Assert.Null);
        Assert.Equal(8_388_608f, batch.Lane(1).Outputs[0]);
    }

    // Two calls on two threads at the same time: each worker has its own RecurrentRunner.
    [Fact]
    public void Two_runs_at_the_same_time_give_the_results_of_one_run()
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var lanes = SeededSources.Create(function.Prefix.Table, 512, 5303).ToList();
        using var runner = new CpuRecurrentRunner(function);
        var batches = new RecurrentBatch[2];
        Parallel.For(0, 2, index => batches[index] = runner.Run(Flatten(lanes), lanes.Count, StepLimit));
        foreach (var batch in batches) AssertLanesEqualRecurrentRunner(function, lanes, batch, StepLimit);
    }

    [Fact]
    public void Run_checks_its_arguments_and_Dispose_stops_the_runner()
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve));
        Assert.Throws<ArgumentNullException>(() => new CpuRecurrentRunner(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpuRecurrentRunner(function, 0));
        var runner = new CpuRecurrentRunner(function);
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run([8f], 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run([], -1, StepLimit));
        Assert.Throws<ArgumentException>(() => runner.Run([8f, 4f], 1, StepLimit));
        Assert.Empty(runner.Run([], 0, StepLimit).Steps);

        // An argument past its declared range throws the exception of the validator of the prefix, as the
        // GPU runner does.
        var expected = Assert.Throws<CellValueException>(() => function.Prefix.Table.Validator.Validate([-1f]));
        var error = Assert.Throws<CellValueException>(() => runner.Run([8f, -1f], 2, StepLimit));
        Assert.Equal(expected.Message, error.Message);

        runner.Dispose();
        runner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => runner.Run([8f], 1, StepLimit));
    }

    [Fact]
    public void RecurrentRunners_Create_with_no_device_gives_the_CPU_runner()
    {
        var function = RecurrentKernelWriterTests.Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve));
        using var runner = RecurrentRunners.Create(function, null);
        Assert.IsType<CpuRecurrentRunner>(runner);
        Assert.Same(function, runner.Function);
        Assert.Throws<ArgumentNullException>(() => RecurrentRunners.Create(null!, null));
    }

    // The checks of RecurrentBatch on a batch that a test builds.
    [Fact]
    public void RecurrentBatch_Lane_checks_the_lane_index()
    {
        var batch = new RecurrentBatch([1f, 2f], [3, 0], [RecurrentLaneStatus.Completed, RecurrentLaneStatus.Limit], [null, null], 1);
        var first = batch.Lane(0);
        Assert.Equal([1f], first.Outputs.ToArray());
        Assert.Equal(3, first.Steps);
        Assert.True(first.Completed);
        Assert.False(batch.Lane(1).Completed);
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Lane(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Lane(-1));
    }

    private static float[] Flatten(IReadOnlyList<float[]> lanes) => [.. lanes.SelectMany(lane => lane)];

    // Each lane equals RecurrentRunner.Run: the same outputs by Js.ObjectIs, the same steps, Completed for
    // the status Completed and Limit, and the CPU message for the status Invalid.
    internal static void AssertLanesEqualRecurrentRunner(RecurrentFunction function, IReadOnlyList<float[]> lanes, RecurrentBatch batch, int stepLimit)
    {
        Assert.Equal(lanes.Count, batch.Steps.Length);
        Assert.Equal(lanes.Count, batch.Status.Length);
        Assert.Equal(lanes.Count, batch.Messages.Length);
        Assert.Equal(lanes.Count * function.Suffix.Outputs.Length, batch.Outputs.Length);
        var cpu = new RecurrentRunner(function);
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

            Assert.True(batch.Status[lane] == (expected.Completed ? RecurrentLaneStatus.Completed : RecurrentLaneStatus.Limit), $"{context}: status {batch.Status[lane]}, CPU Completed {expected.Completed}");
            Assert.Null(batch.Messages[lane]);
            var actual = batch.Lane(lane);
            Assert.True(expected.Steps == actual.Steps, $"{context}: steps {actual.Steps}, CPU {expected.Steps}");
            Assert.Equal(expected.Completed, actual.Completed);
            for (var leaf = 0; leaf < expected.Outputs.Length; leaf++)
            {
                Assert.True(Js.ObjectIs(expected.Outputs[leaf], actual.Outputs[leaf]), $"{context}: output {leaf} is {actual.Outputs[leaf]:R}, CPU {expected.Outputs[leaf]:R}");
            }
        }
    }
}
