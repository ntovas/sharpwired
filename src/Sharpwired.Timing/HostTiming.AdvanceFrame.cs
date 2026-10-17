// Handwired source: src/runtime/host-timing.ts (SpringClock, stoppedSpringClock, EvaluationKind,
// WriteBack, writeBackPolicies, PlannedEvaluation, TimedEvent, advanceFrame with its local
// functions run, readAnimatedUntilMs and stepTo).
// This file holds float64 host arithmetic only. It builds no operations.

using System.Globalization;

namespace Sharpwired.Timing;

/// <summary>
/// Handwired SpringClock: the animated time of the springs, plus what the planner needs between
/// frames.
/// </summary>
/// <param name="AnimatedUntilMs">
/// How far the springs stepped, on the time grid. Null while the clock is stopped, because the last
/// evaluation reported that nothing animates.
/// </param>
/// <param name="CheckOriginMs">
/// Settle checks run at CheckOriginMs + k × SettleCheckSteps × StepMs. CheckOriginMs is the time at
/// which the clock last started.
/// </param>
/// <param name="LastEvaluationMs">The time of the last evaluation that wrote state back, or null after idle.</param>
public readonly record struct SpringClock(double? AnimatedUntilMs, double CheckOriginMs, double? LastEvaluationMs)
{
    /// <summary>Handwired stoppedSpringClock.</summary>
    public static SpringClock Stopped { get; } = new(null, 0, null);

    /// <summary>
    /// True when AnimatedUntilMs is not null. The host requests frames while this is true.
    /// </summary>
    public bool IsRunning => AnimatedUntilMs is not null;
}

/// <summary>Handwired EvaluationKind.</summary>
public enum EvaluationKind
{
    /// <summary>
    /// Handwired 'settle check': steps up to a fixed simulated time and checks the settle state
    /// there, with no events.
    /// </summary>
    SettleCheck,

    /// <summary>
    /// Handwired 'events': steps up to the time of the events, then applies each event with that
    /// time.
    /// </summary>
    Events,

    /// <summary>Handwired 'frame': steps up to the time of the frame, for the display.</summary>
    Frame,

    /// <summary>
    /// Handwired 'snap': runs no steps and puts each animated object on its destination with zero
    /// velocity, because the steps up to the next evaluation pass what is left of the step budget
    /// of the frame.
    /// </summary>
    Snap,
}

/// <summary>Handwired WriteBack: the two write-back policies.</summary>
public enum WriteBack
{
    /// <summary>Handwired 'every evaluation': each evaluation writes the next state back.</summary>
    EveryEvaluation,

    /// <summary>
    /// Handwired 'events and settle checks': only the events, settle check and snap evaluations
    /// write state back. A frame evaluates a copy of the table, so the stored state does not
    /// depend on which frames ran.
    /// </summary>
    EventsAndSettleChecks,
}

/// <summary>Handwired TimedEvent: an event with a timestamp in milliseconds.</summary>
public interface ITimedEvent
{
    /// <summary>The timestamp of the event, on the time origin of the frame times.</summary>
    double TimeStamp { get; }
}

/// <summary>Handwired PlannedEvaluation: one evaluation that AdvanceFrame plans.</summary>
/// <param name="Kind">The kind of the evaluation.</param>
/// <param name="TimeMs">The time of the evaluation, on the time grid.</param>
/// <param name="SimulatedTimeMs">
/// How far the springs stepped after this evaluation. It equals TimeMs for a settle check, a snap,
/// or a stopped clock. Otherwise it is up to one step before TimeMs.
/// </param>
/// <param name="DurationMs">
/// The time since the last evaluation that wrote state back, or 0 after idle, on the time grid.
/// It has no clamp, for example 2,000 ms for the snap after a 2 s stall. FP32 holds it exactly
/// only within 2^24 grid steps, for example 262,144 ms on the 1/64 ms grid.
/// </param>
/// <param name="Steps">
/// The host writes SpringMatrixPower(SpringStepMatrix(constants), Steps) into the 4 matrix cells of
/// each stiffness and damping pair.
/// </param>
/// <param name="SettleCheck">The host writes 1 into the settle check cell when true, and 0 when false.</param>
/// <param name="Snap">The host writes 1 into the snap cells when true, and 0 when false. True only for a snap.</param>
/// <param name="Events">The events that this evaluation applies, all at TimeMs, in delivery order.</param>
/// <param name="WritesBack">False for a frame with WriteBack.EventsAndSettleChecks: the host evaluates a copy of the table.</param>
public sealed record PlannedEvaluation<TEvent>(
    EvaluationKind Kind,
    double TimeMs,
    double SimulatedTimeMs,
    double DurationMs,
    int Steps,
    bool SettleCheck,
    bool Snap,
    IReadOnlyList<TEvent> Events,
    bool WritesBack);

public static partial class HostTiming
{
    /// <summary>
    /// Handwired advanceFrame: plans and runs the evaluations of one frame. evaluate writes the
    /// matrices, the settle check bit, the snap bit and the events of the evaluation into the
    /// table, runs the program, writes the next state back when WritesBack is true, and returns
    /// true while something animates. The return value is the clock after the frame. A null
    /// configuration is SpringClockConfig.Default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order rule: before the host applies an event with the timestamp T, the springs step to
    /// T toward the destinations that were in effect before T. So a new destination starts to pull
    /// within one step of T at each frame rate.
    /// </para>
    /// <para>
    /// Only settle check evaluations check the settle state, at the simulated times
    /// CheckOriginMs + 64 k ms by default. An evaluation while the clock is stopped checks it too,
    /// and starts the clock at its own time. A settle check at the simulated time c runs before
    /// the events with timestamps at or after c, so each frame rate checks the same state against
    /// the same destinations.
    /// </para>
    /// <para>
    /// The evaluations of one frame run at most MaxStepsPerFrame steps together. Before each events
    /// or frame evaluation, when the steps up to its time pass what is left of that budget, a snap
    /// evaluation puts each animated object on its destination and stops the clock. This drops
    /// the backlog. The steps count from AnimatedUntilMs, where the springs last stepped in state
    /// that was written back. An event before the last evaluation that wrote state back applies at
    /// the time of that evaluation, and an event after the frame applies at the time of the frame.
    /// </para>
    /// </remarks>
    public static SpringClock AdvanceFrame<TEvent>(
        SpringClock clock,
        double frameTimeStamp,
        IReadOnlyList<TEvent> events,
        Func<PlannedEvaluation<TEvent>, bool> evaluate,
        WriteBack writeBack = WriteBack.EveryEvaluation,
        SpringClockConfig? config = null)
        where TEvent : ITimedEvent
    {
        config ??= SpringClockConfig.Default;
        ValidateSpringClockConfig(config);
        // Handwired writeBackPolicies: a C# enum can hold an undefined value, as a JavaScript caller
        // can pass any string.
        if (writeBack is not (WriteBack.EveryEvaluation or WriteBack.EventsAndSettleChecks))
        {
            throw new ArgumentException(
                $"writeBack must be 'every evaluation' or 'events and settle checks', got {((int)writeBack).ToString(CultureInfo.InvariantCulture)}");
        }
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(evaluate);
        var checkIntervalMs = config.StepMs * config.SettleCheckSteps;
        var animatedUntilMs = clock.AnimatedUntilMs;
        var checkOriginMs = clock.CheckOriginMs;
        var lastEvaluationMs = clock.LastEvaluationMs;
        var stepBudget = config.MaxStepsPerFrame;
        var earliestMs = lastEvaluationMs ?? double.NegativeInfinity;
        var frameTimeMs = Math.Max(QuantizeTimeMs(frameTimeStamp, config), earliestMs);

        void Run(EvaluationKind kind, double timeMs, int steps, bool settleCheck, IReadOnlyList<TEvent> eventsHere)
        {
            var writesBack = kind != EvaluationKind.Frame || writeBack == WriteBack.EveryEvaluation;
            var simulatedTimeMs = animatedUntilMs is { } until ? until + steps * config.StepMs : timeMs;
            var animating = evaluate(new PlannedEvaluation<TEvent>(
                kind,
                timeMs,
                simulatedTimeMs,
                lastEvaluationMs is { } last ? timeMs - last : 0,
                steps,
                settleCheck,
                kind == EvaluationKind.Snap,
                eventsHere,
                writesBack));
            // A frame on a copy runs at most what is left of the budget, and changes neither the
            // budget nor the clock.
            if (!writesBack) return;
            stepBudget -= steps;
            // A stopped clock counts no time. An evaluation that finds something animating starts
            // the clock at its own time.
            if (animatedUntilMs is null && animating) checkOriginMs = timeMs;
            animatedUntilMs = animating ? simulatedTimeMs : null;
            // Idle starts as soon as an evaluation reports that nothing animates, for example at a
            // settle check in the middle of a 10 fps frame. So the duration of the next evaluation
            // is 0 in each frame.
            lastEvaluationMs = animating ? timeMs : null;
        }

        // Handwired reads the clock through a function, because Run can stop the clock in a loop
        // that started with the clock running.
        double? ReadAnimatedUntilMs() => animatedUntilMs;

        void StepTo(double targetMs, EvaluationKind kind, IReadOnlyList<TEvent> eventsHere)
        {
            if (animatedUntilMs is { } from && CountSpringSteps(targetMs - from, stepBudget, config).Snap)
            {
                // Past the budget: stop the clock, which drops the backlog, and snap. As each
                // evaluation while the clock is stopped, the snap checks the settle state, and
                // starts the clock at its own time if something still animates.
                animatedUntilMs = null;
                Run(EvaluationKind.Snap, targetMs, 0, true, []);
            }
            if (animatedUntilMs is not null)
            {
                // Settle checks at each fixed simulated time up to the target, and one that lands
                // exactly where the target evaluation ends. Together with the steps of the target,
                // they fit what is left of the budget, because the steps up to the target did.
                for (var untilMs = ReadAnimatedUntilMs(); untilMs is { } until; untilMs = ReadAnimatedUntilMs())
                {
                    var reachableMs = until + CountSpringSteps(targetMs - until, stepBudget, config).Steps * config.StepMs;
                    var nextCheckMs = checkOriginMs
                        + (Math.Floor((until - checkOriginMs) / checkIntervalMs) + 1) * checkIntervalMs;
                    if (nextCheckMs > reachableMs) break;
                    Run(EvaluationKind.SettleCheck, Math.Max(nextCheckMs, lastEvaluationMs ?? nextCheckMs), WholeSteps((nextCheckMs - until) / config.StepMs), true, []);
                }
            }
            if (animatedUntilMs is not { } animated)
            {
                Run(kind, targetMs, 0, true, eventsHere);
                return;
            }
            Run(kind, targetMs, CountSpringSteps(targetMs - animated, stepBudget, config).Steps, false, eventsHere);
        }

        // A stable sort by time, then by arrival index, as the Handwired comparator.
        var timedEvents = events
            .Select((item, index) => (Event: item, Index: index, TimeMs: Math.Min(frameTimeMs, Math.Max(earliestMs, QuantizeTimeMs(item.TimeStamp, config)))))
            .OrderBy(item => item.TimeMs)
            .ThenBy(item => item.Index)
            .ToArray();
        for (var start = 0; start < timedEvents.Length;)
        {
            var timeMs = timedEvents[start].TimeMs;
            var end = start;
            while (end < timedEvents.Length && timedEvents[end].TimeMs == timeMs) end++;
            var group = new TEvent[end - start];
            for (var index = start; index < end; index++) group[index - start] = timedEvents[index].Event;
            StepTo(timeMs, EvaluationKind.Events, group);
            start = end;
        }
        StepTo(frameTimeMs, EvaluationKind.Frame, []);
        return new SpringClock(animatedUntilMs, checkOriginMs, lastEvaluationMs);
    }

    // Handwired passes (nextCheckMs - untilMs) / stepMs as a JavaScript number. It is a whole
    // number, because the clock steps from its origin in whole steps. The C# step count is an int,
    // so this conversion checks that fact.
    private static int WholeSteps(double steps)
    {
        if (!(IsInteger(steps) && steps >= 0 && steps <= int.MaxValue))
        {
            throw new InvalidOperationException("a settle check must be a whole number of steps");
        }
        return (int)steps;
    }
}
