// Handwired source: template/toggle.test.ts (scriptedTrace).
// The scripted trace of the toggle tests. The builder toggle tests, the C# toggle tests and the F#
// toggle tests link this file, so that all of them run the same frames.

namespace Toggle.Builder.Tests;

/// <summary>Handwired scriptedTrace and the frame record that it returns.</summary>
internal static class ToggleTrace
{
    // Moving tells whether the thumb still moves before the frame, so that a step that must land in the
    // middle of an animation does.
    internal sealed record Frame(double TimeMs, ToggleEvent[] Events, (double On, double Armed)? Expected = null, bool? Moving = null);

    // Handwired scriptedTrace: frames 16 ms apart at 1280 × 800, where the track spans x 614..666 and
    // y 384..416. Each event arrives 3 ms before its frame.
    internal static List<Frame> ScriptedTrace()
    {
        var frames = new List<Frame>();
        double timeMs = 1000;
        void Idle(int count, double gapMs = 16, bool? moving = null)
        {
            for (var index = 0; index < count; index++) frames.Add(new Frame(timeMs += gapMs, [], null, moving));
        }
        void Pointer(ToggleEventKind kind, double x, double y, double on, double armed, bool? moving = null)
        {
            timeMs += 16;
            frames.Add(new Frame(timeMs, [new ToggleEvent(kind, x, y, timeMs - 3)], (on, armed), moving));
        }
        Idle(2);
        Pointer(ToggleEventKind.Down, 640, 400, 0, 1, false); // a press on the track arms the switch
        Pointer(ToggleEventKind.Up, 640, 400, 1, 0); // a release on the track while armed flips it on
        Idle(3);
        Pointer(ToggleEventKind.Down, 630.25, 399.5, 1, 1, true);
        Pointer(ToggleEventKind.Up, 630.25, 399.5, 0, 0, true); // a click while the thumb moves flips it back
        Idle(60);
        Pointer(ToggleEventKind.Down, 613.984375, 400, 0, 0, false); // 1/64 px left of the track
        Pointer(ToggleEventKind.Down, 614, 384, 0, 1); // exactly on the left and top edges
        Pointer(ToggleEventKind.Up, 666, 400, 0, 0); // exactly on the right edge, which is outside, so it only disarms
        Pointer(ToggleEventKind.Down, 640, 400, 0, 1);
        Pointer(ToggleEventKind.Cancel, 0, 0, 0, 0); // a pointercancel disarms
        Pointer(ToggleEventKind.Up, 640, 400, 0, 0); // so this release does not flip
        Pointer(ToggleEventKind.Down, 665.984375, 415.984375, 0, 1);
        Pointer(ToggleEventKind.Up, 665.984375, 415.984375, 1, 0); // 1/64 px inside the bottom right corner
        Idle(1, 500, true); // a 500 ms stall while the thumb moves
        Idle(60);
        return frames;
    }
}
