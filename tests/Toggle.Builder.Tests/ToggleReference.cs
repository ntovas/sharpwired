// Handwired source: template/toggle-reference.ts (createToggleReference, with its local functions
// destination, withinThresholds, stepTo and render).
//
// The toggle switch of ToggleProgram in plain C#, in float64 (double), with the timing that
// AdvanceFrame gives the program. The spring takes fixed 4 ms steps from the time the clock started.
// It steps to the timestamp of each event before the event applies. It settles only at a check every
// 64 ms of simulated time. Events that share a timestamp apply together, in the order that the
// program handles them in one evaluation. This order is the last down, then the last up, then any
// cancel. This reference
// never snaps the thumb onto its destination when a frame needs more than 300 steps. So a trace with a
// stall of more than about 1,140 ms while the thumb moves gives other values than the program.

using Sharpwired.Timing;

namespace Toggle.Builder.Tests;

/// <summary>The values of one Render call. Handwired returns the same fields.</summary>
internal readonly record struct ReferenceOutput(double TrackLeft, double TrackTop, double ThumbOffset, double On, double Armed, bool Animating);

/// <summary>Handwired createToggleReference.</summary>
internal sealed class ToggleReference
{
    // The default settle thresholds of AnimatedObject, 0.01 px and 0.01 px/s, as FP32 values.
    private static readonly double Threshold = (double)(float)0.01;

    // Handwired switchState.
    public double On { get; private set; }
    public double Armed { get; private set; }
    public double ThumbOffset { get; private set; }
    public double Velocity { get; private set; }
    public double? AnimatedUntilMs { get; private set; }
    public double CheckOriginMs { get; private set; }

    private double Destination() => On * ToggleLayout.Travel;

    private bool WithinThresholds() =>
        Math.Abs(ThumbOffset - Destination()) <= Threshold && Math.Abs(Velocity) <= Threshold;

    private void StepTo(double timeMs)
    {
        while (AnimatedUntilMs is { } until && until + 4 <= timeMs)
        {
            Velocity += (-ToggleLayout.Spring.Stiffness * (ThumbOffset - Destination()) - ToggleLayout.Spring.Damping * Velocity) * 0.004;
            ThumbOffset += Velocity * 0.004;
            AnimatedUntilMs = until + 4;
            // The double remainder, as the JavaScript % operator.
            if ((AnimatedUntilMs.Value - CheckOriginMs) % 64 == 0 && WithinThresholds())
            {
                ThumbOffset = Destination();
                Velocity = 0;
                AnimatedUntilMs = null;
            }
        }
    }

    /// <summary>Handwired render: apply the events of one frame, then step to the time of the frame.</summary>
    public ReferenceOutput Render(double viewportWidth, double viewportHeight, double frameTime, IReadOnlyList<ToggleEvent> events)
    {
        var trackLeft = viewportWidth / 2 - ToggleLayout.TrackWidth / 2;
        var trackTop = viewportHeight / 2 - ToggleLayout.TrackHeight / 2;
        bool InsideTrack(ToggleEvent item) =>
            item.X >= trackLeft && item.X < trackLeft + ToggleLayout.TrackWidth && item.Y >= trackTop && item.Y < trackTop + ToggleLayout.TrackHeight;
        for (var start = 0; start < events.Count;)
        {
            var eventMs = HostTiming.QuantizeTimeMs(events[start].TimeStamp);
            var end = start + 1;
            while (end < events.Count && HostTiming.QuantizeTimeMs(events[end].TimeStamp) == eventMs) end++;
            ToggleEvent? down = null;
            ToggleEvent? up = null;
            var cancel = false;
            // Handwired findLast and some over the events with the same time.
            for (var index = start; index < end; index++)
            {
                switch (events[index].Kind)
                {
                    case ToggleEventKind.Down:
                        down = events[index];
                        break;
                    case ToggleEventKind.Up:
                        up = events[index];
                        break;
                    case ToggleEventKind.Cancel:
                        cancel = true;
                        break;
                }
            }
            start = end;
            StepTo(eventMs);
            if (down is { } pressed) Armed = InsideTrack(pressed) ? 1 : 0;
            if (up is { } released && Armed == 1 && InsideTrack(released)) On = 1 - On;
            if (up is not null || cancel) Armed = 0;
            // A stopped clock starts at an event that gives the thumb a place to go.
            if (AnimatedUntilMs is null && !WithinThresholds())
            {
                AnimatedUntilMs = eventMs;
                CheckOriginMs = eventMs;
            }
        }
        StepTo(HostTiming.QuantizeTimeMs(frameTime));
        return new ReferenceOutput(trackLeft, trackTop, ThumbOffset, On, Armed, AnimatedUntilMs is not null);
    }
}
