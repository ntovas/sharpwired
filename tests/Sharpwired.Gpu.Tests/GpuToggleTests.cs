// Handwired source: template/toggle.test.ts (scriptedTrace and its frame checks). The builder toggle
// runs the scripted trace of ToggleTrace on the GPU, through ProgramEvaluator.Create with a GpuEvaluator
// backend, and on the CPU, through ProgramEvaluator.Create without a backend (question 6 of the phase 5
// plan). The two machines run the same circuit. So each frame gives the same outputs and the same
// stored cells with Object.is, and the same state of the spring clock.

using Sharpwired.Cells;
using Sharpwired.Core;
using Toggle.Builder;
using Toggle.Builder.Tests;
using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class GpuToggleTests(GpuFixture fixture, ITestOutputHelper output)
{
    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    // R5.2, R5.3 on the toggle. On a device that flushes subnormal values, a frame with an evaluation
    // that holds a subnormal value is not compared (decision 4). The test counts these frames, and then
    // copies the stored cells of the CPU machine into the GPU machine.
    [Theory]
    [MemberData(nameof(Writers))]
    public void The_toggle_on_the_GPU_gives_the_outputs_and_the_stored_cells_of_the_CPU_machine_on_each_frame(GpuWriter writer)
    {
        var built = ToggleProgram.Build();
        var trace = ToggleTrace.ScriptedTrace();
        fixture.ForEachDevice(output, device =>
        {
            GpuEvaluator? gpu = null;
            try
            {
                RecordingEvaluator? gpuRoots = null;
                RecordingEvaluator? cpuRoots = null;
                var gpuMachine = new ToggleMachine(built, ProgramEvaluator.Create(built.Table, built.Output, (circuit, program) =>
                    gpuRoots = new RecordingEvaluator(gpu = GpuEvaluator.Create(device, circuit, program, writer), device.FlushesSubnormals)));
                var cpuMachine = new ToggleMachine(built, ProgramEvaluator.Create(built.Table, built.Output, (circuit, program) =>
                    cpuRoots = new RecordingEvaluator(circuit.CreateTensorProgramEvaluator(program), recordsSubnormals: false)));
                var comparedFrames = 0;
                var excludedFrames = 0;
                var animatingFrames = 0;
                double[] times = [.. trace.Select(frame => frame.TimeMs), 5000];
                for (var frameIndex = 0; frameIndex < times.Length; frameIndex++)
                {
                    var events = frameIndex < trace.Count ? trace[frameIndex].Events : [];
                    foreach (var item in events)
                    {
                        gpuMachine.Queue(item);
                        cpuMachine.Queue(item);
                    }
                    gpuRoots!.SawSubnormal = false;
                    var actual = gpuMachine.Step(1280, 800, times[frameIndex]);
                    var expected = cpuMachine.Step(1280, 800, times[frameIndex]);
                    if (gpuRoots.SawSubnormal)
                    {
                        excludedFrames++;
                        cpuMachine.Source.CopyTo(gpuMachine.Source, 0);
                        continue;
                    }
                    var where = $"frame {frameIndex} at {times[frameIndex]} ms, {writer}";
                    Assert.True(
                        Same(expected, actual),
                        $"{where}: outputs {actual}, CPU outputs {expected}");
                    for (var cell = 0; cell < cpuMachine.Source.Length; cell++)
                    {
                        Assert.True(
                            Js.ObjectIs(cpuMachine.Source[cell], gpuMachine.Source[cell]),
                            $"{where}: stored cell {cell} is {gpuMachine.Source[cell]:R}, CPU {cpuMachine.Source[cell]:R}");
                    }
                    Assert.Equal(cpuMachine.Clock.IsRunning, gpuMachine.Clock.IsRunning);
                    Assert.Equal(cpuMachine.Clock.AnimatedUntilMs, gpuMachine.Clock.AnimatedUntilMs);
                    if (frameIndex < trace.Count && trace[frameIndex].Expected is { } decisions)
                    {
                        Js.AllIs([decisions.On, decisions.Armed], [actual.On, actual.Armed]);
                    }
                    if (gpuMachine.Clock.IsRunning) animatingFrames++;
                    comparedFrames++;
                }
                var statistics = gpu!.Module.Statistics;
                output.WriteLine(
                    $"[gpu toggle] {writer}: {comparedFrames} frames compared, {excludedFrames} frames with a subnormal value not compared, "
                    + $"{animatingFrames} animating frames, {gpuRoots!.Calls} GPU evaluations, {cpuRoots!.Calls} CPU evaluations; "
                    + $"rows: {statistics.FloatRows} f32, {statistics.WholeRows} i32, {statistics.BitRows} u32; {statistics.Keeps} keeps");
                // Each evaluation of the GPU machine ran on the GPU, one for each evaluation of the CPU machine.
                Assert.Equal(cpuRoots.Calls, gpuRoots.Calls);
                Assert.True(gpuRoots.Calls >= times.Length, $"{gpuRoots.Calls} GPU evaluations for {times.Length} frames");
                Assert.True(comparedFrames >= times.Length / 2, $"{comparedFrames} of {times.Length} frames compared");
                // The trace animates: 38 frames in ToggleTests, and the frame at 5000 ms is at rest.
                Assert.True(animatingFrames > 0, "no frame animates");
                Js.Is(20, gpuMachine.Step(1280, 800, 6000).ThumbOffset);
            }
            finally
            {
                gpu?.Dispose();
            }
        });
    }

    private static bool Same(ToggleOutput expected, ToggleOutput actual) =>
        Js.ObjectIs(expected.TrackLeft, actual.TrackLeft)
        && Js.ObjectIs(expected.TrackTop, actual.TrackTop)
        && Js.ObjectIs(expected.ThumbOffset, actual.ThumbOffset)
        && Js.ObjectIs(expected.On, actual.On)
        && Js.ObjectIs(expected.Armed, actual.Armed)
        && Js.ObjectIs(expected.Active, actual.Active);

    // A root evaluator that counts its calls, and on a device that flushes subnormal values records
    // whether an evaluation holds a subnormal value in the f32 form of the rows (RowInterpreter).
    private sealed class RecordingEvaluator(IRootEvaluator inner, bool recordsSubnormals) : IRootEvaluator
    {
        private readonly System.Collections.Immutable.ImmutableArray<ShaderRow> rows = ShaderRows.FlattenProgram(inner.Program);

        public int Calls { get; private set; }

        public bool SawSubnormal { get; set; }

        public TensorProgram Program => inner.Program;

        public float[] Evaluate(ReadOnlySpan<float> source)
        {
            Calls++;
            if (recordsSubnormals)
            {
                RowInterpreter.Run(inner.Program, rows, source, out var sawSubnormal);
                SawSubnormal |= sawSubnormal;
            }
            return inner.Evaluate(source);
        }
    }
}
