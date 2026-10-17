// Handwired source: template/toggle.ts (createToggleMachine, lines 92-139): the event queue, the
// spring clock and the advanceFrame call. The toggle-specific cell writes stay in ToggleMachine.
// This file makes no Raylib call.

using Sharpwired.Timing;

namespace Sharpwired.Raylib;

/// <summary>
/// The input queue, the spring clock and the <see cref="HostTiming.AdvanceFrame{TEvent}"/> calls of a
/// host. Event handlers only queue events. Each step plans the evaluations of one frame with
/// AdvanceFrame, which steps the springs to the timestamp of each event before the event applies.
/// With <see cref="WriteBack.EventsAndSettleChecks"/>, the frame evaluation runs on a copy of the
/// table. So the stored state depends only on the events, and not on the frame rate.
/// </summary>
public sealed class HostLoop<TEvent>
    where TEvent : ITimedEvent
{
    private readonly WriteBack writeBack;
    private readonly SpringClockConfig? config;
    private List<TEvent> queued = [];

    /// <summary>
    /// Make a host loop with a stopped clock and an empty queue. A null configuration is
    /// <see cref="SpringClockConfig.Default"/>. AdvanceFrame checks the policy and the configuration
    /// at each step, as Handwired does.
    /// </summary>
    public HostLoop(WriteBack writeBack = WriteBack.EventsAndSettleChecks, SpringClockConfig? config = null)
    {
        this.writeBack = writeBack;
        this.config = config;
    }

    /// <summary>The clock that the last step returned. The host requests frames while it runs.</summary>
    public SpringClock Clock { get; private set; } = SpringClock.Stopped;

    /// <summary>The number of events that wait for the next step.</summary>
    public int QueuedCount => queued.Count;

    /// <summary>Add an event to the queue. The next step applies it.</summary>
    public void Queue(TEvent queuedEvent) => queued.Add(queuedEvent);

    /// <summary>
    /// Call AdvanceFrame with the queued events in queue order, keep the clock that it returns, and
    /// then empty the queue. When AdvanceFrame throws, the clock and the queue do not change.
    /// </summary>
    public void Step(double frameTimeMs, Func<PlannedEvaluation<TEvent>, bool> evaluate)
    {
        Clock = HostTiming.AdvanceFrame(Clock, frameTimeMs, queued, evaluate, writeBack, config);
        // A new list, as Handwired assigns a new array. A caller that kept the old list keeps its events.
        queued = [];
    }
}
