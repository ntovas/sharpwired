// Handwired source: tests/runtime/spring-matrix.test.ts, describe 'springs along frame-time traces,
// with presses between frames'. The helpers are in SpringTraceApp.cs.
// Each test name holds the Handwired test name.
// Handwired compares the commit lists as `${kind}@${timeMs}` strings with toEqual. Here they are
// lists of (kind, time) tuples, which compare the same values.

using Sharpwired.Core;
using static Sharpwired.Timing.Tests.SpringTraceApp;

namespace Sharpwired.Timing.Tests;

public class SpringTraceTests
{
    // Handwired creates one evaluator for the describe block. xUnit makes a new instance of the
    // class for each test, so each test compiles its own evaluator of the same circuit.
    private readonly TensorEvaluator evaluator = CreateApp();

    private static List<(EvaluationKind Kind, double TimeMs)> CommitKinds(AppRun run) =>
        [.. run.Commits.Select(commit => (commit.Kind, commit.TimeMs))];

    [Fact]
    public void With_events_and_settle_checks_committed_tables_and_displays_are_bit_identical_at_10_30_60_and_120_fps_and_through_500_ms_stalls_and_settle_like_float64_springs_including_jumps_that_stop_the_clock_at_exact_rest()
    {
        var stopsAtJumps = 0;
        foreach (var (seed, jumpChance) in TraceCases)
        {
            var presses = AppPresses(seed, jumpChance);
            var (baseline, others) = TraceSchedules(presses);
            var baselineRun = RunApp(evaluator, presses, baseline, WriteBack.EventsAndSettleChecks);
            foreach (var frameTimes in (List<double>[])[baseline, .. others])
            {
                var run = frameTimes == baseline ? baselineRun : RunApp(evaluator, presses, frameTimes, WriteBack.EventsAndSettleChecks);
                Assert.Equal(CommitKinds(baselineRun), CommitKinds(run));
                for (var index = 0; index < run.Commits.Count; index++)
                {
                    Assert.Equal(0, DifferingBits(run.Commits[index].Table, baselineRun.Commits[index].Table));
                }
                var sharedDisplays = 0;
                foreach (var (time, frame) in run.Frames)
                {
                    if (!baselineRun.Frames.TryGetValue(time, out var other)) continue;
                    sharedDisplays++;
                    Assert.Equal(0, DifferingBits(frame.Outputs, other.Outputs));
                }
                Assert.True(sharedDisplays > 20, $"expected more than 20 shared displays, got {sharedDisplays}");
                var reference = RunFloat64Reference(presses, frameTimes);
                AssertSameSettles(reference.Settles, run.Settles);
                Assert.True(MaxFrameDifference(FramePositions(run), reference.Positions) < 5e-4);
                Assert.DoesNotContain(run.Commits, commit => commit.Kind == EvaluationKind.Snap);
            }
            // Without jumps, a running clock stops only at a settle check. A jump snaps its object,
            // and when the other object is exactly at rest, nothing animates and the clock stops
            // at the jump, which needs the rest bit.
            if (jumpChance == 0) Assert.Equal(0, baselineRun.StopsWithoutSettleCheck);
            else stopsAtJumps += baselineRun.StopsWithoutSettleCheck;
        }
        // Handwired measured 5 stops over the 4 traces with jumps.
        Assert.True(stopsAtJumps > 0);
    }

    [Fact]
    public void Writing_back_every_evaluation_stays_within_5e_4_px_of_float64_springs_and_of_120_fps_and_settles_like_float64_springs()
    {
        var runsWithDifferentSettles = 0;
        foreach (var (seed, jumpChance) in TraceCases)
        {
            var presses = AppPresses(seed, jumpChance);
            var (baseline, others) = TraceSchedules(presses);
            var baselineRun = RunApp(evaluator, presses, baseline, WriteBack.EveryEvaluation);
            foreach (var frameTimes in (List<double>[])[baseline, .. others])
            {
                var run = frameTimes == baseline ? baselineRun : RunApp(evaluator, presses, frameTimes, WriteBack.EveryEvaluation);
                var reference = RunFloat64Reference(presses, frameTimes);
                Assert.True(MaxFrameDifference(FramePositions(run), reference.Positions) < 5e-4);
                Assert.True(MaxFrameDifference(FramePositions(run), FramePositions(baselineRun)) < 5e-4);
                if (!SameSettlesAsJson(run.Settles, reference.Settles)) runsWithDifferentSettles++;
            }
        }
        // Each evaluation rounds the stored displacement at its own size, so the error becomes
        // smaller near rest. Handwired measured on seeds 1 to 8: up to 1.4e-4 px from float64, and
        // each run settles like float64. With absolute positions, which rounded at up to 2^-14 px
        // at 800 px, it was 7.3e-4 px, and 2 of 48 runs settled at a different check.
        Assert.Equal(0, runsWithDifferentSettles);
    }

    [Fact]
    public void A_3_s_stall_snaps_every_object_onto_its_destination_once_no_frame_runs_more_than_300_steps_and_the_committed_tables_stay_bit_identical_across_frame_rates()
    {
        Press[] presses =
        [
            new(100, 0, [700, 50, 1.5], false),
            new(250, 1, [20], false),
            // During the stall, 2.9 s before the next frame.
            new(500, 0, [120, 480, 0.75], false),
            new(4000, 1, [640], false),
            new(4100, 0, [300, 300, 1], false),
        ];
        var stall = (400.0, 3000.0);
        var runs = new[] { 120, 60, 30, 10 }
            .Select(hz => RunApp(evaluator, presses, AdvanceFrameTests.FrameTimesAt(hz, 6000, stall), WriteBack.EventsAndSettleChecks))
            .ToArray();
        foreach (var run in runs)
        {
            Assert.True(run.StepsPerFrame.Max() <= 300);
            var snaps = run.Commits.Where(commit => commit.Kind == EvaluationKind.Snap).ToList();
            Assert.Equal([3400.0], snaps.Select(commit => commit.TimeMs));
            // Right after the snap, each displacement and velocity is ±0.
            for (var spring = 0; spring < 4; spring++)
            {
                Assert.True(snaps[0].Table[SpringCell(spring, 0)] == 0);
                Assert.True(snaps[0].Table[SpringCell(spring, 1)] == 0);
            }
            Assert.Equal(CommitKinds(runs[0]), CommitKinds(run));
            for (var index = 0; index < run.Commits.Count; index++)
            {
                Assert.Equal(0, DifferingBits(run.Commits[index].Table, runs[0].Commits[index].Table));
            }
            Assert.Null(run.Clock.AnimatedUntilMs);
        }
        // Each frame rate continues at 3,400 ms, and shows the snapped objects there.
        Sharpwired.Core.Tests.Js.AllIs([120, 480, 0.75, 20], runs[3].Frames[3400].Positions);
    }

    [Fact]
    public void Moved_to_65536_131072_and_1048576_px_both_policies_settle_like_float64_springs_and_stop_animating_and_stored_displacements_stay_within_5e_4_px_of_float64_up_to_131072_px()
    {
        foreach (var offset in new[] { Math.Pow(2, 16), Math.Pow(2, 17), Math.Pow(2, 20) })
        {
            // FP32 values near the offset are this far apart, for example 1/128 px near 65,536 px.
            var spacing = Math.Pow(2, Math.Log2(offset) - 23);
            foreach (var writeBack in new[] { WriteBack.EveryEvaluation, WriteBack.EventsAndSettleChecks })
            {
                var worstDisplacement = 0.0;
                var worstPosition = 0.0;
                foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
                {
                    var presses = AppPresses(seed);
                    var (baseline, others) = TraceSchedules(presses);
                    foreach (var frameTimes in (List<double>[])[baseline, .. others])
                    {
                        var run = RunApp(evaluator, presses, frameTimes, writeBack, offset);
                        var reference = RunFloat64Reference(presses, frameTimes, offset);
                        AssertSameSettles(reference.Settles, run.Settles);
                        // Nothing animates 2 s after the last press.
                        Assert.Null(run.Clock.AnimatedUntilMs);
                        worstDisplacement = Math.Max(worstDisplacement, MaxFrameDifference(FrameDisplacements(run), reference.Displacements));
                        worstPosition = Math.Max(worstPosition, MaxFrameDifference(FramePositions(run), reference.Positions));
                    }
                }
                if (offset <= Math.Pow(2, 17))
                {
                    // Handwired measured 1.4e-4 px with 'every evaluation' and 7.4e-5 px with
                    // 'events and settle checks', as at 0 px.
                    Assert.True(worstDisplacement < 5e-4, $"worst displacement {worstDisplacement:R} at {offset:R} px with {writeBack}");
                    // The drawn position rounds one time at the magnitude of the destination.
                    // Handwired measured 3.9e-3 px near 65,536 px and 7.9e-3 px near 131,072 px.
                    Assert.True(worstPosition < spacing / 2 + 5e-4, $"worst position {worstPosition:R} at {offset:R} px with {writeBack}");
                }
                else
                {
                    // FP32 values near 1,048,576 px are 1/8 px apart, so the destinations of the
                    // scale spring, on a 1/64 grid, round before they reach the program. Handwired
                    // measured 0.10 px.
                    Assert.True(worstDisplacement < spacing, $"worst displacement {worstDisplacement:R} at {offset:R} px with {writeBack}");
                }
            }
        }
    }

    [Fact]
    public void Ui_md_order_applying_presses_and_then_catching_up_differs_across_frame_rates_in_float64()
    {
        var worst10 = 0.0;
        var worst30 = 0.0;
        var worst60 = 0.0;
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var presses = AppPresses(seed);
            var durationMs = presses[^1].TimeStamp + 2000;
            var baseline = RunUiMdOrder(presses, AdvanceFrameTests.FrameTimesAt(120, durationMs));
            worst10 = Math.Max(worst10, MaxFrameDifference(RunUiMdOrder(presses, AdvanceFrameTests.FrameTimesAt(10, durationMs)), baseline));
            worst30 = Math.Max(worst30, MaxFrameDifference(RunUiMdOrder(presses, AdvanceFrameTests.FrameTimesAt(30, durationMs)), baseline));
            worst60 = Math.Max(worst60, MaxFrameDifference(RunUiMdOrder(presses, AdvanceFrameTests.FrameTimesAt(60, durationMs)), baseline));
        }
        // Handwired measured 359, 124 and 53 px.
        Assert.True(worst10 > 100, $"worst at 10 fps {worst10:R}");
        Assert.True(worst30 > 50, $"worst at 30 fps {worst30:R}");
        Assert.True(worst60 > 10, $"worst at 60 fps {worst60:R}");
    }
}
