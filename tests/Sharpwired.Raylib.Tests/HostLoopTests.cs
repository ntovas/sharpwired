// Handwired source: none. HostLoop holds the queue, the spring clock and the advanceFrame call of
// createToggleMachine (template/toggle.ts:92-139). Handwired tests this host only through
// template/toggle.test.ts. These tests compare HostLoop with direct calls of HostTiming.AdvanceFrame,
// with scripted events at the frame times of a FakeFrameClock.

using Sharpwired.Timing;

namespace Sharpwired.Raylib.Tests;

public class HostLoopTests
{
    private sealed record TestEvent(string Id, double TimeStamp) : ITimedEvent;

    // One planned evaluation as values. Events holds the event ids, joined with commas, so that
    // record equality compares the events too.
    private sealed record LoggedEvaluation(
        EvaluationKind Kind, double TimeMs, double SimulatedTimeMs, double DurationMs, int Steps,
        bool SettleCheck, bool Snap, string Events, bool WritesBack);

    private static LoggedEvaluation Log(PlannedEvaluation<TestEvent> evaluation) => new(
        evaluation.Kind, evaluation.TimeMs, evaluation.SimulatedTimeMs, evaluation.DurationMs, evaluation.Steps,
        evaluation.SettleCheck, evaluation.Snap, string.Join(",", evaluation.Events.Select(item => item.Id)), evaluation.WritesBack);

    // The scripted program: something animates from the first event until 1,200 ms of simulated time.
    private static bool Animating(PlannedEvaluation<TestEvent> evaluation) => evaluation.SimulatedTimeMs < 1200;

    // Frames 16 ms apart from 1,000 ms. Each frame has its own events, which arrive before the frame.
    private static readonly TestEvent[][] ScriptedFrames =
    [
        [],
        [new("a", 1010), new("b", 1005), new("c", 1010)],
        [],
        [new("d", 1043.5)],
        [],
        [new("e", 1070), new("f", 1070)],
        [],
        [],
    ];

    // The evaluations and the clocks of direct AdvanceFrame calls with the scripted events.
    private static (List<LoggedEvaluation> Log, List<SpringClock> Clocks) RunDirect(WriteBack writeBack, SpringClockConfig? config = null)
    {
        var log = new List<LoggedEvaluation>();
        var clocks = new List<SpringClock>();
        var clock = SpringClock.Stopped;
        var frameClock = new FakeFrameClock();
        frameClock.Set(1000);
        foreach (var events in ScriptedFrames)
        {
            clock = HostTiming.AdvanceFrame(clock, frameClock.NowMs, events, evaluation =>
            {
                log.Add(Log(evaluation));
                return Animating(evaluation);
            }, writeBack, config);
            clocks.Add(clock);
            frameClock.Advance(16);
        }
        return (log, clocks);
    }

    // The same frames through HostLoop.
    private static (List<LoggedEvaluation> Log, List<SpringClock> Clocks) RunLoop(HostLoop<TestEvent> loop)
    {
        var log = new List<LoggedEvaluation>();
        var clocks = new List<SpringClock>();
        var frameClock = new FakeFrameClock();
        frameClock.Set(1000);
        foreach (var events in ScriptedFrames)
        {
            foreach (var item in events) loop.Queue(item);
            Assert.Equal(events.Length, loop.QueuedCount);
            loop.Step(frameClock.NowMs, evaluation =>
            {
                log.Add(Log(evaluation));
                return Animating(evaluation);
            });
            Assert.Equal(0, loop.QueuedCount);
            clocks.Add(loop.Clock);
            frameClock.Advance(16);
        }
        return (log, clocks);
    }

    [Fact]
    public void Step_passes_the_queued_events_in_queue_order_to_AdvanceFrame_clears_the_queue_and_keeps_the_returned_clock()
    {
        var loop = new HostLoop<TestEvent>();
        Assert.Equal(SpringClock.Stopped, loop.Clock);
        var direct = RunDirect(WriteBack.EventsAndSettleChecks);
        var looped = RunLoop(loop);
        Assert.Equal(direct.Log, looped.Log);
        Assert.Equal(direct.Clocks, looped.Clocks);
        // The events "a" and "c" share a timestamp, so they apply in one evaluation in queue order.
        Assert.Contains(looped.Log, evaluation => evaluation.Events == "a,c");
        Assert.Contains(looped.Log, evaluation => evaluation.Events == "e,f");
        // The scripted program animates, so the trace is not trivial.
        Assert.Contains(looped.Clocks, clock => clock.IsRunning);
        Assert.Contains(looped.Log, evaluation => evaluation.Kind == EvaluationKind.SettleCheck);
    }

    [Fact]
    public void The_default_policy_is_events_and_settle_checks_and_EveryEvaluation_writes_back_each_frame()
    {
        var defaultLoop = RunLoop(new HostLoop<TestEvent>());
        Assert.Contains(defaultLoop.Log, evaluation => evaluation.Kind == EvaluationKind.Frame);
        Assert.All(defaultLoop.Log.Where(evaluation => evaluation.Kind == EvaluationKind.Frame), evaluation => Assert.False(evaluation.WritesBack));

        var everyEvaluation = RunLoop(new HostLoop<TestEvent>(WriteBack.EveryEvaluation));
        Assert.All(everyEvaluation.Log, evaluation => Assert.True(evaluation.WritesBack));
        var direct = RunDirect(WriteBack.EveryEvaluation);
        Assert.Equal(direct.Log, everyEvaluation.Log);
        Assert.Equal(direct.Clocks, everyEvaluation.Clocks);
    }

    [Fact]
    public void A_configuration_goes_to_AdvanceFrame()
    {
        var config = SpringClockConfig.Default with { SettleCheckSteps = 4 };
        var looped = RunLoop(new HostLoop<TestEvent>(WriteBack.EventsAndSettleChecks, config));
        var direct = RunDirect(WriteBack.EventsAndSettleChecks, config);
        Assert.Equal(direct.Log, looped.Log);
        Assert.NotEqual(RunDirect(WriteBack.EventsAndSettleChecks).Log, looped.Log);
    }

    [Fact]
    public void Events_queued_after_a_Step_go_to_the_next_Step()
    {
        var loop = new HostLoop<TestEvent>();
        var frames = new List<List<string>>();
        var frameClock = new FakeFrameClock();
        frameClock.Set(1000);
        void Step()
        {
            var ids = new List<string>();
            loop.Step(frameClock.NowMs, evaluation =>
            {
                ids.AddRange(evaluation.Events.Select(item => item.Id));
                return Animating(evaluation);
            });
            frames.Add(ids);
            frameClock.Advance(16);
        }
        loop.Queue(new TestEvent("first", 995));
        Step();
        loop.Queue(new TestEvent("second", 1010));
        loop.Queue(new TestEvent("third", 1011));
        Step();
        Step();
        Assert.Equal(["first", "second,third", ""], frames.Select(ids => string.Join(",", ids)));
    }

    [Fact]
    public void A_Step_that_throws_keeps_the_queue_and_the_clock()
    {
        // Handwired assigns the clock and clears the queue only after advanceFrame returns.
        var loop = new HostLoop<TestEvent>();
        loop.Queue(new TestEvent("a", 1000));
        loop.Step(1016, Animating);
        var clock = loop.Clock;
        Assert.True(clock.IsRunning);
        loop.Queue(new TestEvent("b", 1020));
        var error = Assert.Throws<InvalidOperationException>(() => loop.Step(1032, _ => throw new InvalidOperationException("scripted failure")));
        Assert.Equal("scripted failure", error.Message);
        Assert.Equal(1, loop.QueuedCount);
        Assert.Equal(clock, loop.Clock);
    }

    [Fact]
    public void A_bad_write_back_value_throws_before_an_evaluation()
    {
        var loop = new HostLoop<TestEvent>((WriteBack)7);
        var calls = 0;
        var error = Assert.Throws<ArgumentException>(() => loop.Step(1000, _ => ++calls > 0));
        Assert.Contains("writeBack must be 'every evaluation' or 'events and settle checks', got 7", error.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void The_stopwatch_clock_starts_near_0_and_does_not_go_back()
    {
        var clock = new StopwatchFrameClock();
        var first = clock.NowMs;
        var second = clock.NowMs;
        Assert.True(first >= 0);
        Assert.True(first < 60_000);
        Assert.True(second >= first);
    }
}
