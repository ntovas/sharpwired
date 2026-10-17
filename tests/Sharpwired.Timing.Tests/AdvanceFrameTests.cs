// Handwired source: tests/runtime/spring-matrix.test.ts, describe 'advanceFrame', with the helpers
// TestEvent, LoggedEvaluation, runPlanner, movesUntilSnap and frameTimesAt.
// Each test name holds the Handwired test name. A test with no Handwired twin says so in its comment.
// Handwired compares logs with toEqual. Here each PlannedEvaluation becomes a LoggedEvaluation
// record that holds the event ids as a comma-joined string, so record equality compares values.

using Sharpwired.Core.Tests;

namespace Sharpwired.Timing.Tests;

public class AdvanceFrameTests
{
    // Handwired TestEvent.
    private readonly record struct TestEvent(double TimeStamp, int Id) : ITimedEvent;

    // Handwired LoggedEvaluation: a PlannedEvaluation with the event ids in place of the events.
    private sealed record LoggedEvaluation(
        EvaluationKind Kind, double TimeMs, double SimulatedTimeMs, double DurationMs,
        int Steps, bool SettleCheck, bool Snap, bool WritesBack, string EventIds);

    private sealed record PlannerRun(List<LoggedEvaluation> Log, List<SpringClock> Clocks, SpringClock Clock, List<int> StepsPerFrame);

    private static string Ids(IReadOnlyList<TestEvent> events) => string.Join(",", events.Select(item => item.Id));

    private static LoggedEvaluation Logged(PlannedEvaluation<TestEvent> evaluation) => new(
        evaluation.Kind, evaluation.TimeMs, evaluation.SimulatedTimeMs, evaluation.DurationMs,
        evaluation.Steps, evaluation.SettleCheck, evaluation.Snap, evaluation.WritesBack, Ids(evaluation.Events));

    private static SpringClock Advance(
        SpringClock clock,
        double frameTimeStamp,
        TestEvent[] events,
        Func<PlannedEvaluation<TestEvent>, bool> evaluate,
        WriteBack writeBack = WriteBack.EveryEvaluation) =>
        HostTiming.AdvanceFrame(clock, frameTimeStamp, events, evaluate, writeBack);

    // Handwired runPlanner: runs the planner along frame times with an evaluate callback that only
    // logs, and reports animating while `animating` says so.
    private static PlannerRun RunPlanner(
        IReadOnlyList<double> frameTimes,
        IReadOnlyList<TestEvent> events,
        WriteBack writeBack,
        Func<PlannedEvaluation<TestEvent>, bool>? animating = null)
    {
        animating ??= _ => true;
        var clock = SpringClock.Stopped;
        var log = new List<LoggedEvaluation>();
        var clocks = new List<SpringClock>();
        var stepsPerFrame = new List<int>();
        var nextEvent = 0;
        foreach (var frameTime in frameTimes)
        {
            var arrived = new List<TestEvent>();
            while (nextEvent < events.Count && events[nextEvent].TimeStamp <= frameTime) arrived.Add(events[nextEvent++]);
            var steps = 0;
            clock = HostTiming.AdvanceFrame(clock, frameTime, arrived, evaluation =>
            {
                log.Add(Logged(evaluation));
                steps += evaluation.Steps;
                return animating(evaluation);
            }, writeBack);
            clocks.Add(clock);
            stepsPerFrame.Add(steps);
        }
        return new PlannerRun(log, clocks, clock, stepsPerFrame);
    }

    // Handwired movesUntilSnap: a stand-in for a program. Each event starts an animation that keeps
    // moving until a snap.
    private static Func<PlannedEvaluation<TestEvent>, bool> MovesUntilSnap()
    {
        var moving = false;
        return evaluation =>
        {
            if (evaluation.Snap) moving = false;
            if (evaluation.Events.Count > 0) moving = true;
            return moving;
        };
    }

    // Handwired frameTimesAt. SpringTraceApp and SpringTraceTests use it too.
    internal static List<double> FrameTimesAt(double hz, double durationMs, (double StartMs, double LengthMs)? stall = null)
    {
        var times = new List<double>();
        for (var frame = 0; ; frame++)
        {
            var time = frame * 1000.0 / hz;
            if (time > durationMs) break;
            if (stall is { } gap && time > gap.StartMs && time < gap.StartMs + gap.LengthMs) continue;
            times.Add(time);
        }
        return times;
    }

    private const EvaluationKind Events = EvaluationKind.Events;
    private const EvaluationKind Frame = EvaluationKind.Frame;
    private const EvaluationKind SettleCheck = EvaluationKind.SettleCheck;
    private const EvaluationKind Snap = EvaluationKind.Snap;

    // 'advanceFrame' / 'a stopped clock starts at an event, checks settling at fixed simulated
    // times, and after idle the duration restarts at 0'.
    [Fact]
    public void A_stopped_clock_starts_at_an_event_checks_settling_at_fixed_simulated_times_and_after_idle_the_duration_restarts_at_0()
    {
        // Something animates until the settle check at 159.296875 ms.
        var run = RunPlanner([100, 180, 250], [new TestEvent(95.3, 1)], WriteBack.EveryEvaluation, evaluation => evaluation.TimeMs < 159);
        LoggedEvaluation[] expected =
        [
            new(Events, 95.296875, 95.296875, 0, 0, true, false, true, "1"),
            new(Frame, 100, 99.296875, 4.703125, 1, false, false, true, ""),
            // 64 ms of simulated time after the event.
            new(SettleCheck, 159.296875, 159.296875, 59.296875, 15, true, false, true, ""),
            // The check reported that nothing animates: idle starts, and the frame starts no clock.
            new(Frame, 180, 180, 0, 0, true, false, true, ""),
            new(Frame, 250, 250, 0, 0, true, false, true, ""),
        ];
        Assert.Equal(expected, run.Log);
        Assert.Equal(new SpringClock(99.296875, 95.296875, 100), run.Clocks[0]);
        Assert.Equal(new SpringClock(null, 95.296875, null), run.Clocks[1]);
    }

    // 'advanceFrame' / 'evaluations that write state back are the same at 10, 30, 60 and 120 fps
    // and through a 500 ms stall, with presses between frames'.
    [Fact]
    public void Evaluations_that_write_state_back_are_the_same_at_10_30_60_and_120_fps_and_through_a_500_ms_stall_with_presses_between_frames()
    {
        var random = Js.CreateRandom(53);
        var events = new List<TestEvent>();
        var time = 100.0;
        for (var id = 0; id < 20; id++)
        {
            time += 20 + random() * 400;
            events.Add(new TestEvent(time, id));
        }
        var durationMs = time + 1000;
        // A stand-in for a program. An event starts an animation that settles at the first settle
        // check 300 ms or more after the latest event. An evaluation while the clock is stopped
        // (settleCheck with no steps behind it) reports no animation, unless it applies events.
        // Gaps above 364 ms stop the clock.
        bool StopsAfterEvents(PlannedEvaluation<TestEvent> evaluation)
        {
            if (evaluation.Kind == Events) return true;
            if (!evaluation.SettleCheck) return true;
            if (evaluation.Kind == Frame) return false;
            var latestEventMs = double.NegativeInfinity;
            foreach (var item in events)
            {
                var eventMs = HostTiming.QuantizeTimeMs(item.TimeStamp);
                if (eventMs <= evaluation.TimeMs) latestEventMs = Math.Max(latestEventMs, eventMs);
            }
            return evaluation.TimeMs - latestEventMs < 300;
        }
        static List<LoggedEvaluation> Writing(PlannerRun run) => run.Log.Where(evaluation => evaluation.WritesBack).ToList();
        // With every evaluation writing back, the settle checks are still the same.
        static List<(EvaluationKind, double, bool, string)> Checks(IEnumerable<LoggedEvaluation> log) =>
            log.Where(evaluation => evaluation.Kind != Frame)
                .Select(evaluation => (evaluation.Kind, evaluation.TimeMs, evaluation.SettleCheck, evaluation.EventIds))
                .ToList();

        var reference = Writing(RunPlanner(FrameTimesAt(120, durationMs), events, WriteBack.EventsAndSettleChecks, StopsAfterEvents));
        Assert.True(reference.Count(evaluation => evaluation.Kind == SettleCheck) > 20);
        foreach (var frameTimes in new[]
        {
            FrameTimesAt(10, durationMs),
            FrameTimesAt(30, durationMs),
            FrameTimesAt(60, durationMs),
            FrameTimesAt(60, durationMs, (1000, 500)),
        })
        {
            var run = RunPlanner(frameTimes, events, WriteBack.EventsAndSettleChecks, StopsAfterEvents);
            Assert.Equal(reference, Writing(run));
            Assert.True(run.Log.Where(evaluation => evaluation.Kind == Frame).All(evaluation => !evaluation.WritesBack));
            Assert.Equal(Checks(reference), Checks(RunPlanner(frameTimes, events, WriteBack.EveryEvaluation, StopsAfterEvents).Log));
        }
        foreach (var evaluation in reference)
        {
            Assert.True(evaluation.Steps <= (evaluation.Kind == SettleCheck ? 16 : 300));
            Assert.False(evaluation.Snap);
        }
    }

    // 'advanceFrame' / 'past the cap, a snap evaluation stops the clock and drops the backlog, so
    // no frame runs more than 300 steps'.
    [Fact]
    public void Past_the_cap_a_snap_evaluation_stops_the_clock_and_drops_the_backlog_so_no_frame_runs_more_than_300_steps()
    {
        var stall = RunPlanner([0, 1000.0 / 60, 2000 + 1000.0 / 60, 2000 + 2000.0 / 60], [new TestEvent(0, 0)], WriteBack.EveryEvaluation, MovesUntilSnap());
        (EvaluationKind, double, double, double, int, bool, bool)[] expected =
        [
            (Events, 0, 0, 0, 0, true, false),
            (Frame, 0, 0, 0, 0, false, false),
            (Frame, 16.671875, 16, 16.671875, 4, false, false),
            // 2,000.671875 ms pending would be 500 steps: snap, and the frame runs with the clock stopped.
            (Snap, 2016.671875, 2016.671875, 2000, 0, true, true),
            (Frame, 2016.671875, 2016.671875, 0, 0, true, false),
            (Frame, 2033.328125, 2033.328125, 0, 0, true, false),
        ];
        Assert.Equal(expected, stall.Log.Select(item => (item.Kind, item.TimeMs, item.SimulatedTimeMs, item.DurationMs, item.Steps, item.SettleCheck, item.Snap)));
        Assert.Null(stall.Clocks[2].AnimatedUntilMs);
        Assert.Equal([0, 4, 0, 0], stall.StepsPerFrame);
        // A snap that still reports something animating starts the clock at its own time.
        var stillMoving = RunPlanner([0, 1000.0 / 60, 2000 + 1000.0 / 60], [new TestEvent(0, 0)], WriteBack.EveryEvaluation);
        Assert.Equal(new SpringClock(2016.671875, 2016.671875, 2016.671875), stillMoving.Clock);
        // With 'events and settle checks', the snap writes state back, and the frame then evaluates a copy.
        var committed = Advance(SpringClock.Stopped, 10, [new TestEvent(10, 0)], _ => true, WriteBack.EventsAndSettleChecks);
        var snapLog = new List<PlannedEvaluation<TestEvent>>();
        var moving = MovesUntilSnap();
        committed = Advance(committed, 3000, [], evaluation =>
        {
            snapLog.Add(evaluation);
            return moving(evaluation);
        }, WriteBack.EventsAndSettleChecks);
        Assert.Equal(
            [(Snap, 0, true), (Frame, 0, false)],
            snapLog.Select(item => (item.Kind, item.Steps, item.WritesBack)));
        Assert.Equal(new SpringClock(null, 10, null), committed);
    }

    // 'advanceFrame' / 'an event during a long stall applies at its own timestamp after a snap, and
    // later steps share what is left of the budget'.
    [Fact]
    public void An_event_during_a_long_stall_applies_at_its_own_timestamp_after_a_snap_and_later_steps_share_what_is_left_of_the_budget()
    {
        var run = RunPlanner(
            [0, 1000.0 / 60, 3000 + 1000.0 / 60],
            [new TestEvent(0, 0), new TestEvent(1500, 1), new TestEvent(2950, 2)],
            WriteBack.EveryEvaluation,
            MovesUntilSnap());
        (EvaluationKind, double, double, int, bool, bool, string)[] expected =
        [
            // 1,484 ms from where the springs stepped to the press: snap onto the destinations before it, then apply it.
            (Snap, 1500, 1500, 0, true, true, ""),
            (Events, 1500, 1500, 0, true, false, "1"),
            // 1,450 ms more: snap onto the first press's destinations, and apply the second press.
            (Snap, 2950, 2950, 0, true, true, ""),
            (Events, 2950, 2950, 0, true, false, "2"),
            // The second press's animation starts at its timestamp and steps normally to the frame.
            (SettleCheck, 3014, 3014, 16, true, false, ""),
            (Frame, 3016.671875, 3014, 0, false, false, ""),
        ];
        Assert.Equal(expected, run.Log.Skip(3).Select(item => (item.Kind, item.TimeMs, item.SimulatedTimeMs, item.Steps, item.SettleCheck, item.Snap, item.EventIds)));
        // A press at 1,150 ms steps 283 times with its settle checks, and the 25 steps up to the
        // frame at 1,250 ms exceed the remaining 17, so the springs snap.
        var split = RunPlanner([0, 1000.0 / 60, 1250], [new TestEvent(0, 0), new TestEvent(1150, 1)], WriteBack.EveryEvaluation, MovesUntilSnap());
        Assert.Equal([0, 4, 283], split.StepsPerFrame);
        Assert.Equal(
            [(Events, 1150, 15), (Snap, 1250, 0), (Frame, 1250, 0)],
            split.Log.TakeLast(3).Select(item => (item.Kind, item.TimeMs, item.Steps)));
        Assert.Equal(
            Enumerable.Range(0, 17).Select(index => 64.0 * (index + 1)),
            split.Log.Where(item => item.Kind == SettleCheck).Select(item => item.TimeMs));
    }

    // 'advanceFrame' / 'late events apply at the last evaluation, events after the frame at the
    // frame, and events sharing a timestamp share one evaluation'.
    [Fact]
    public void Late_events_apply_at_the_last_evaluation_events_after_the_frame_at_the_frame_and_events_sharing_a_timestamp_share_one_evaluation()
    {
        var clock = Advance(SpringClock.Stopped, 100, [new TestEvent(100, 0)], _ => true);
        var log = new List<LoggedEvaluation>();
        clock = Advance(clock, 116.6667,
        [
            new TestEvent(90, 1),
            new TestEvent(105, 2),
            new TestEvent(130, 3),
            new TestEvent(105.001, 4),
        ], evaluation =>
        {
            log.Add(Logged(evaluation));
            return true;
        });
        Assert.Equal(
            [(Events, 100, 0, "1"), (Events, 105, 1, "2,4"), (Events, 116.671875, 3, "3"), (Frame, 116.671875, 0, "")],
            log.Select(item => (item.Kind, item.TimeMs, item.Steps, item.EventIds)));
        Assert.Equal(new SpringClock(116, 100, 116.671875), clock);
    }

    // 'advanceFrame' / "with 'events and settle checks', a frame evaluates a copy and leaves the
    // clock where the last commit put it".
    [Fact]
    public void With_events_and_settle_checks_a_frame_evaluates_a_copy_and_leaves_the_clock_where_the_last_commit_put_it()
    {
        var committed = Advance(SpringClock.Stopped, 10, [new TestEvent(10, 0)], _ => true, WriteBack.EventsAndSettleChecks);
        var log = new List<PlannedEvaluation<TestEvent>>();
        var after = Advance(committed, 40, [], evaluation =>
        {
            log.Add(evaluation);
            return true;
        }, WriteBack.EventsAndSettleChecks);
        Assert.Equal([(Frame, 7, false, 30.0)], log.Select(item => (item.Kind, item.Steps, item.WritesBack, item.DurationMs)));
        Assert.Equal(committed, after);
    }

    // 'advanceFrame' / "a snap counts steps from the last evaluation that wrote state back, so with
    // 'events and settle checks' a frame gap from just above 1,140 ms can snap, depending on where
    // the last settle check landed".
    [Fact]
    public void A_snap_counts_steps_from_the_last_evaluation_that_wrote_state_back_so_with_events_and_settle_checks_a_frame_gap_from_just_above_1140_ms_can_snap_depending_on_where_the_last_settle_check_landed()
    {
        // An event at 0 ms starts an animation that moves until a snap. One frame runs at
        // previousFrameMs, and the next one gapMs later. Returns whether that frame snaps.
        static bool SnapsAfterGap(double previousFrameMs, double gapMs, WriteBack writeBack)
        {
            var moving = MovesUntilSnap();
            var clock = Advance(SpringClock.Stopped, 0, [new TestEvent(0, 0)], moving, writeBack);
            clock = Advance(clock, previousFrameMs, [], moving, writeBack);
            var snapped = false;
            Advance(clock, previousFrameMs + gapMs, [], evaluation =>
            {
                if (evaluation.Snap) snapped = true;
                return moving(evaluation);
            }, writeBack);
            return snapped;
        }
        // The frame at 60 ms wrote back at simulated time 60 ms, so 1,204 ms later is 301 steps.
        Assert.False(SnapsAfterGap(60, 1203, WriteBack.EveryEvaluation));
        Assert.True(SnapsAfterGap(60, 1204, WriteBack.EveryEvaluation));
        // The frame at 60 ms evaluated a copy, and the first settle check is at 64 ms, so the steps
        // count from the event at 0 ms.
        Assert.False(SnapsAfterGap(60, 1143, WriteBack.EventsAndSettleChecks));
        Assert.True(SnapsAfterGap(60, 1144, WriteBack.EventsAndSettleChecks));
        // A frame at 64 ms runs the settle check at 64 ms, which writes back.
        Assert.False(SnapsAfterGap(64, 1203, WriteBack.EventsAndSettleChecks));
        Assert.True(SnapsAfterGap(64, 1204, WriteBack.EventsAndSettleChecks));
        // The last evaluation that wrote back is less than 64 ms before the previous frame. So the
        // smallest gap that snaps is a little more than 1,140 ms. For example, after a frame at
        // 63.984375 ms, a gap of 1,140.015625 ms snaps.
        Assert.False(SnapsAfterGap(63.984375, 1140, WriteBack.EventsAndSettleChecks));
        Assert.True(SnapsAfterGap(63.984375, 1140.015625, WriteBack.EventsAndSettleChecks));
    }

    // No Handwired twin. Handwired refuses a write-back string other than the two policies. The C#
    // enum can hold an undefined value, and AdvanceFrame refuses it with the Handwired message
    // before it calls evaluate.
    [Fact]
    public void AdvanceFrame_refuses_an_undefined_write_back_value_with_the_Handwired_message()
    {
        var calls = 0;
        var exception = Js.Throws(
            () => Advance(SpringClock.Stopped, 10, [new TestEvent(10, 0)], _ => { calls++; return true; }, (WriteBack)7),
            "writeBack must be 'every evaluation' or 'events and settle checks', got 7");
        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(0, calls);
    }

    // No Handwired twin. AdvanceFrame checks the configuration first, before the write-back value
    // and before it calls evaluate.
    [Fact]
    public void AdvanceFrame_refuses_a_bad_configuration_before_it_calls_evaluate()
    {
        var calls = 0;
        bool Count(PlannedEvaluation<TestEvent> evaluation)
        {
            calls++;
            return true;
        }
        var badGrid = SpringClockConfig.Default with { TimeGrid = 60 };
        var exception = Js.Throws(
            () => HostTiming.AdvanceFrame(SpringClock.Stopped, 10, [new TestEvent(10, 0)], Count, WriteBack.EveryEvaluation, badGrid),
            "the time grid must be a whole power of two, got 60");
        Assert.IsType<ArgumentException>(exception);
        // The configuration check runs before the write-back check.
        Js.Throws(
            () => HostTiming.AdvanceFrame(SpringClock.Stopped, 10, [new TestEvent(10, 0)], Count, (WriteBack)7, badGrid),
            "power of two");
        Assert.Equal(0, calls);
    }

    // No Handwired twin. The host requests frames while AnimatedUntilMs is not null. Handwired
    // reads clock.animatedUntilMs !== null; SpringClock.IsRunning gives the same fact.
    [Fact]
    public void The_stopped_clock_does_not_run_and_a_clock_with_an_animated_time_runs()
    {
        Assert.Equal(new SpringClock(null, 0, null), SpringClock.Stopped);
        Assert.False(SpringClock.Stopped.IsRunning);
        Assert.True(new SpringClock(0, 0, null).IsRunning);
        var clock = Advance(SpringClock.Stopped, 10, [new TestEvent(10, 0)], _ => true);
        Assert.True(clock.IsRunning);
        Assert.False(Advance(clock, 30, [], _ => false).IsRunning);
    }

    // No Handwired twin. A non-default configuration changes the settle check interval and the
    // step budget: with 2 ms steps, a settle check every 4 steps and 10 steps per frame, the checks
    // run every 8 ms from the event, and a 30 ms gap snaps.
    [Fact]
    public void AdvanceFrame_reads_the_step_the_settle_check_interval_and_the_step_budget_of_its_configuration()
    {
        var config = new SpringClockConfig(64, 2, 10, 4);
        var log = new List<LoggedEvaluation>();
        bool Record(PlannedEvaluation<TestEvent> evaluation)
        {
            log.Add(Logged(evaluation));
            return evaluation.Events.Count > 0 || !evaluation.Snap;
        }
        var clock = HostTiming.AdvanceFrame(SpringClock.Stopped, 1, new[] { new TestEvent(1, 0) }, Record, WriteBack.EveryEvaluation, config);
        clock = HostTiming.AdvanceFrame(clock, 18, Array.Empty<TestEvent>(), Record, WriteBack.EveryEvaluation, config);
        clock = HostTiming.AdvanceFrame(clock, 48, Array.Empty<TestEvent>(), Record, WriteBack.EveryEvaluation, config);
        LoggedEvaluation[] expected =
        [
            new(Events, 1, 1, 0, 0, true, false, true, "0"),
            new(Frame, 1, 1, 0, 0, false, false, true, ""),
            new(SettleCheck, 9, 9, 8, 4, true, false, true, ""),
            new(SettleCheck, 17, 17, 8, 4, true, false, true, ""),
            new(Frame, 18, 17, 1, 0, false, false, true, ""),
            // 31 ms from 17 ms is 15 steps of 2 ms, more than the budget of 10.
            new(Snap, 48, 48, 30, 0, true, true, true, ""),
            new(Frame, 48, 48, 0, 0, true, false, true, ""),
        ];
        Assert.Equal(expected, log);
        Assert.Equal(new SpringClock(48, 48, 48), clock);
    }
    // No Handwired twin. Handwired advanceFrame clamps the frame time to the last evaluation that
    // wrote state back (host-timing.ts:328). A frame timestamp before that evaluation, and an event
    // before it, run at the time of that evaluation with no steps.
    [Fact]
    public void A_frame_timestamp_before_the_last_evaluation_runs_at_the_time_of_that_evaluation()
    {
        var clock = Advance(SpringClock.Stopped, 100, [new TestEvent(100, 0)], _ => true);
        Assert.Equal(new SpringClock(100, 100, 100), clock);
        var log = new List<LoggedEvaluation>();
        clock = Advance(clock, 90, [new TestEvent(80, 1)], evaluation =>
        {
            log.Add(Logged(evaluation));
            return true;
        });
        LoggedEvaluation[] expected =
        [
            new(Events, 100, 100, 0, 0, false, false, true, "1"),
            new(Frame, 100, 100, 0, 0, false, false, true, ""),
        ];
        Assert.Equal(expected, log);
        Assert.Equal(new SpringClock(100, 100, 100), clock);
    }

    // No Handwired twin. The settle checks of a clock that AdvanceFrame made lie a whole number of
    // steps after AnimatedUntilMs. A caller can build a clock that breaks this fact. Here the first
    // settle check at 64 ms is 15.5 steps after 2 ms, so AdvanceFrame refuses the clock.
    [Fact]
    public void AdvanceFrame_refuses_a_clock_whose_settle_check_is_not_a_whole_number_of_steps_away()
    {
        var calls = 0;
        var exception = Js.Throws(
            () => Advance(new SpringClock(2, 0, 2), 100, [], _ => { calls++; return true; }),
            "a settle check must be a whole number of steps");
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(0, calls);
    }
}
