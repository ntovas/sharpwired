// Handwired source: tests/runtime/spring-matrix.test.ts, describe 'host timing'.
// Each test name holds the Handwired test name. A test with no Handwired twin says so in its comment.

using Sharpwired.Core.Tests;

namespace Sharpwired.Timing.Tests;

public class HostTimingTests
{
    private static double F(double value) => Js.F(value);

    private static readonly SpringConstants[] Constants =
    [
        new(290, 30),
        new(650, 45),
        new(120, 14),
    ];

    // Plain float64 semi-implicit Euler steps, as index.ts's stepSpringTo loop.
    private static (double Displacement, double Velocity) EulerSteps(SpringConstants constants, double displacement, double velocity, int steps)
    {
        var e = displacement;
        var v = velocity;
        for (var step = 0; step < steps; step++)
        {
            v += (-constants.Stiffness * e - constants.Damping * v) * 0.004;
            e += v * 0.004;
        }
        return (e, v);
    }

    // JavaScript Number.isInteger.
    private static bool IsInteger(double value) => double.IsFinite(value) && Math.Floor(value) == value;

    // toEqual on a FrameTiming: Object.is on each number, and the same event count.
    private static void FrameTimingIs(double frameTimeMs, double durationMs, double[] eventTimesMs, FrameTiming actual)
    {
        Js.Is(frameTimeMs, actual.FrameTimeMs);
        Js.Is(durationMs, actual.DurationMs);
        Js.AllIs(eventTimesMs, actual.EventTimesMs);
    }

    // 'host timing' / 'frame durations over any split of a time span add up, and idle, late and
    // early times clamp as documented'.
    [Fact]
    public void Frame_durations_over_any_split_of_a_time_span_add_up_and_idle_late_and_early_times_clamp_as_documented()
    {
        Js.Is(16.671875, HostTiming.QuantizeTimeMs(16.6667));
        Assert.True(Js.ObjectIs(HostTiming.QuantizeTimeMs(-0.001), 0));
        // Half-grid timestamps round up, as JavaScript Math.round does. The .NET Math.Round
        // rounds 0.5 and 2.5 grid steps down to the even neighbor.
        Js.Is(1.0 / 64, HostTiming.QuantizeTimeMs(1.0 / 128));
        Js.Is(2.0 / 64, HostTiming.QuantizeTimeMs(3.0 / 128));
        Js.Is(3.0 / 64, HostTiming.QuantizeTimeMs(5.0 / 128));
        var random = Js.CreateRandom(41);
        for (var trace = 0; trace < 50; trace++)
        {
            var start = random() * 1e7;
            double? previous = null;
            var totalMs = 0.0;
            var firstFrameMs = 0.0;
            var lastFrameMs = 0.0;
            var now = start;
            for (var frame = 0; frame < 60; frame++)
            {
                now += random() * 60;
                var eventTimeStamps = new double[3];
                for (var index = 0; index < 3; index++) eventTimeStamps[index] = now - random() * 50;
                var timing = HostTiming.QuantizeFrameTiming(now, previous, eventTimeStamps);
                foreach (var value in (double[])[timing.DurationMs, .. timing.EventTimesMs])
                {
                    Assert.True(IsInteger(value * 64));
                    Js.Is(value, F(value));
                    Assert.False(Js.ObjectIs(value, -0.0));
                }
                for (var index = 0; index < timing.EventTimesMs.Length; index++)
                {
                    var quantizedEvent = HostTiming.QuantizeTimeMs(eventTimeStamps[index]);
                    if (previous is null || quantizedEvent >= previous.Value) Js.Is(quantizedEvent, timing.FrameTimeMs + timing.EventTimesMs[index]);
                    else Js.Is(previous.Value, timing.FrameTimeMs + timing.EventTimesMs[index]);
                }
                totalMs += timing.DurationMs;
                if (previous is null) firstFrameMs = timing.FrameTimeMs;
                previous = timing.FrameTimeMs;
                lastFrameMs = timing.FrameTimeMs;
            }
            // Quantizing absolute timestamps first makes every split of the span add up exactly.
            Js.Is(lastFrameMs - firstFrameMs, totalMs);
        }
        // The first frame after idle has duration 0, and its events keep their relative times within the cap.
        FrameTimingIs(5000, 0, [-10, -1000], HostTiming.QuantizeFrameTiming(5000, null, [4990.004, 4000]));
        Js.AllIs([-9.984375], HostTiming.QuantizeFrameTiming(5000, null, [4990.01]).EventTimesMs);
        // A frame timestamp before the previous frame counts no time.
        FrameTimingIs(1000, 0, [], HostTiming.QuantizeFrameTiming(990, 1000, []));
        // A 100 s stall writes the cap, 1,200 ms. An event after the frame reads +0. An older
        // event reads the later of the previous frame and -1,200 ms.
        var stalled = HostTiming.QuantizeFrameTiming(101_000, 1000, [101_003, 500]);
        Js.Is(1200, stalled.DurationMs);
        Assert.True(Js.ObjectIs(stalled.EventTimesMs[0], 0));
        Js.Is(-1200, stalled.EventTimesMs[1]);
        // Past the cap, an event after the previous frame is clamped too, and so is an event 4 s before a frame after idle.
        Js.AllIs([-1200, -1200, -1000], HostTiming.QuantizeFrameTiming(200_000, 1000, [500, 1500, 199_000]).EventTimesMs);
        Js.AllIs([-1200], HostTiming.QuantizeFrameTiming(5000, null, [1000]).EventTimesMs);
        // Within the cap, an event before the previous frame reads the previous frame's time.
        Js.AllIs([-100], HostTiming.QuantizeFrameTiming(1100, 1000, [900]).EventTimesMs);
        Js.Throws(() => HostTiming.QuantizeTimeMs(double.NaN), "finite");
    }

    // No Handwired twin. JsMath.Round is JavaScript Math.round: half up, and -0 for a negative
    // value that rounds to 0. The .NET Math.Round rounds half to even.
    [Fact]
    public void JsMath_round_rounds_half_up_and_gives_negative_zero_as_JavaScript()
    {
        Js.Is(1, JsMath.Round(0.5));
        Js.Is(-0.0, JsMath.Round(-0.5));
        Js.Is(-1, JsMath.Round(-0.5000000000000001));
        Js.Is(0, JsMath.Round(0.49999999999999994));
        Js.Is(3, JsMath.Round(2.5));
        Js.Is(-2, JsMath.Round(-2.5));
    }

    // No Handwired twin. QuantizeFrameTiming validates the configuration before it reads a time,
    // and QuantizeTimeMs reads the time grid of its configuration.
    [Fact]
    public void Frame_quantization_checks_the_configuration_first_and_reads_its_time_grid()
    {
        Js.Throws(() => HostTiming.QuantizeFrameTiming(double.NaN, null, [], SpringClockConfig.Default with { TimeGrid = 60 }), "power of two");
        Js.Is(16.6875, HostTiming.QuantizeTimeMs(16.6667, SpringClockConfig.Default with { TimeGrid = 16 }));
        FrameTimingIs(16.6875, 0, [-8], HostTiming.QuantizeFrameTiming(16.6667, null, [8.7], SpringClockConfig.Default with { TimeGrid = 16 }));
    }

    // toEqual on a StepCount: Object.is on each number.
    private static void StepCountIs(int steps, double leftoverMs, bool snap, StepCount actual)
    {
        Assert.Equal(steps, actual.Steps);
        Js.Is(leftoverMs, actual.LeftoverMs);
        Assert.Equal(snap, actual.Snap);
    }

    // 'host timing' / 'countSpringSteps runs floor(pending / 4) steps and carries the leftover, or
    // snaps when more steps are pending than the budget'.
    [Fact]
    public void CountSpringSteps_runs_floor_pending_over_4_steps_and_carries_the_leftover_or_snaps_when_more_steps_are_pending_than_the_budget()
    {
        var checkedCount = 0;
        var snaps = 0;
        for (var steps = 0; steps <= 305; steps++)
        {
            foreach (var offset in (double[])[-1.0 / 64, 0, 1.0 / 64])
            {
                var pendingMs = 4 * steps + offset;
                if (pendingMs < 0) continue;
                foreach (var budget in (int[])[300, 7, 0])
                {
                    var count = HostTiming.CountSpringSteps(pendingMs, budget);
                    var pendingSteps = Math.Floor(pendingMs / 4);
                    if (pendingSteps > budget)
                    {
                        StepCountIs(0, 0, true, count);
                        snaps++;
                    }
                    else
                    {
                        StepCountIs((int)pendingSteps, pendingMs - 4 * pendingSteps, false, count);
                    }
                    checkedCount++;
                }
            }
        }
        Assert.True(checkedCount > 2700);
        Assert.True(snaps > 1500);
        // 1,203.984375 ms runs the whole cap and carries 3.984375 ms, and 1,204 ms snaps.
        StepCountIs(300, 3.984375, false, HostTiming.CountSpringSteps(1203.984375, 300));
        StepCountIs(0, 0, true, HostTiming.CountSpringSteps(1204, 300));
        StepCountIs(0, 0, true, HostTiming.CountSpringSteps(100_000.5, 300));
        Js.Throws(() => HostTiming.CountSpringSteps(1.0 / 128, 300), "time grid");
        Js.Throws(() => HostTiming.CountSpringSteps(-4, 300), "nonnegative");
        // Handwired passes the budget 1.5. A C# budget is an int, so this port passes -1.
        Js.Throws(() => HostTiming.CountSpringSteps(4, -1), "step budget");
    }

    // No Handwired twin. CountSpringSteps checks the pending time before the budget, refuses NaN,
    // and reads the step and the time grid of its configuration.
    [Fact]
    public void CountSpringSteps_checks_the_pending_time_before_the_budget_and_reads_its_configuration()
    {
        Js.Throws(() => HostTiming.CountSpringSteps(1.0 / 128, -1), "pending time must lie on the time grid and be nonnegative, got 0.0078125");
        Js.Throws(() => HostTiming.CountSpringSteps(double.NaN, 300), "got NaN");
        Js.Throws(() => HostTiming.CountSpringSteps(4, -1), "the step budget must be a nonnegative integer");
        var config = SpringClockConfig.Default with { TimeGrid = 128, StepMs = 2 };
        StepCountIs(3, 1.0 / 128, false, HostTiming.CountSpringSteps(6 + 1.0 / 128, 300, config));
        StepCountIs(0, 0, true, HostTiming.CountSpringSteps(6, 2, config));
        // Handwired writes the number with JavaScript String(number).
        Js.Throws(() => HostTiming.CountSpringSteps(1e-7, 300), "pending time must lie on the time grid and be nonnegative, got 1e-7");
        Js.Throws(() => HostTiming.CountSpringSteps(-1.5e20, 300), "got -150000000000000000000");
        Js.Throws(() => HostTiming.QuantizeTimeMs(double.NegativeInfinity), "a timestamp must be finite, got -Infinity");
    }

    // No Handwired twin. An unvalidated configuration with a step of 0 ms makes 0 / 0 steps for no
    // pending time. Handwired returns NaN steps. The C# step count is an int, so CountSpringSteps
    // refuses this case.
    [Fact]
    public void CountSpringSteps_refuses_a_step_count_that_is_NaN()
    {
        var exception = Js.Throws(
            () => HostTiming.CountSpringSteps(0, 300, SpringClockConfig.Default with { StepMs = 0 }),
            "the step count is NaN");
        Assert.IsType<ArgumentException>(exception);
        // A step of 0 ms with pending time gives infinite steps, which snap.
        StepCountIs(0, 0, true, HostTiming.CountSpringSteps(4, 300, SpringClockConfig.Default with { StepMs = 0 }));
    }

    // 'host timing' / 'spring matrices: powers by squaring equal repeated Euler steps, and the
    // declared bound covers every FP32 entry the host writes'. Phase 1 put the matrix part in
    // SpringMatrixTests. Phase 3 moved it here and added the validateSpringClockConfig part.
    [Fact]
    public void Spring_matrices_powers_by_squaring_equal_repeated_Euler_steps_and_the_declared_bound_covers_every_FP32_entry()
    {
        foreach (var constants in Constants)
        {
            var step = SpringMatrix.SpringStepMatrix(constants);
            var euler = EulerSteps(constants, 3, -7, 1);
            Assert.True(Math.Abs(step[0] * 3 + step[1] * -7 - euler.Displacement) < 1e-12);
            Assert.True(Math.Abs(step[2] * 3 + step[3] * -7 - euler.Velocity) < 1e-12);
            var bound = SpringMatrix.SpringMatrixBound(constants);
            var repeated = SpringMatrix.Identity;
            var largest = 1.0;
            for (var steps = 0; steps <= 300; steps++)
            {
                if (steps > 0) repeated = SpringMatrix.MultiplySpringMatrices(repeated, step);
                var power = SpringMatrix.SpringMatrixPower(step, steps);
                for (var index = 0; index < 4; index++)
                {
                    var entry = power[index];
                    Assert.True(Math.Abs(entry - repeated[index]) < 1e-12);
                    Assert.True(Math.Abs(F(entry)) <= bound);
                    largest = Math.Max(largest, Math.Abs(entry));
                }
            }
            Assert.True(bound < largest * (1 + 1e-5));
        }
        // toBeCloseTo(10.36, 2): |expected - received| < 10^-2 / 2.
        Assert.True(Math.Abs(SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45)) - 10.36) < 0.005);
        Js.Throws(() => SpringMatrix.SpringMatrixBound(new SpringConstants(1e7, 0)), "overflows");
        Js.Throws(() => SpringMatrix.SpringStepMatrix(new SpringConstants(-1, 0)), "nonnegative");
        // Handwired passes the count 1.5. A C# count is an int, so this port passes -1.
        Js.Throws(() => SpringMatrix.SpringMatrixPower(SpringMatrix.Identity, -1), "nonnegative integer");
        HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default);
        Js.Throws(() => HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default with { TimeGrid = 60 }), "power of two");
        Js.Throws(() => HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default with { StepMs = 1.0 / 128 }), "multiple of the time grid");
        Js.Throws(() => HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default with { MaxStepsPerFrame = 0 }), "positive integer");
        // 2^20 steps of 4 ms on a 1/64 ms grid are 2^28 grid steps.
        Js.Throws(() => HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default with { MaxStepsPerFrame = 1 << 20 }), "2^24 grid steps");
    }

    // No Handwired twin. Handwired springMatrixBound(constants, config) reads config.stepMs and
    // config.maxStepsPerFrame. The default configuration gives the bound of the phase 1 overload.
    [Fact]
    public void The_bound_of_a_clock_configuration_reads_its_step_and_its_step_cap()
    {
        Assert.Equal(new SpringClockConfig(64, 4, 300, 16), SpringClockConfig.Default);
        foreach (var constants in Constants)
        {
            Js.Is(SpringMatrix.SpringMatrixBound(constants), SpringMatrix.SpringMatrixBound(constants, SpringClockConfig.Default));
            var sevenSteps = SpringClockConfig.Default with { MaxStepsPerFrame = 7 };
            Js.Is(SpringMatrix.SpringMatrixBound(constants, 4, 7), SpringMatrix.SpringMatrixBound(constants, sevenSteps));
            var twoMsSteps = SpringClockConfig.Default with { StepMs = 2 };
            Js.Is(SpringMatrix.SpringMatrixBound(constants, 2, 300), SpringMatrix.SpringMatrixBound(constants, twoMsSteps));
        }
        // The 650/45 spring reaches its largest entry after more than 7 steps. So the bound of 7
        // steps is smaller than the bound of 300 steps.
        Assert.True(
            SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45), SpringClockConfig.Default with { MaxStepsPerFrame = 7 })
                < SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45)));
    }

    // No Handwired twin. validateSpringClockConfig refuses settleCheckSteps 0.
    [Fact]
    public void The_configuration_check_refuses_zero_settle_check_steps()
    {
        Js.Throws(() => HostTiming.ValidateSpringClockConfig(SpringClockConfig.Default with { SettleCheckSteps = 0 }), "settleCheckSteps must be a positive integer");
    }
}
