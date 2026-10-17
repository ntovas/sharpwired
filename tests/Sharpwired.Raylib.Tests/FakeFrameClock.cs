// Handwired source: none. The browser host reads the frame time from requestAnimationFrame
// (template/toggle.ts, mountToggle). This clock gives the frame times that a test sets.

namespace Sharpwired.Raylib.Tests;

/// <summary>A frame clock that holds the time that a test sets.</summary>
internal sealed class FakeFrameClock : IFrameClock
{
    public double NowMs { get; private set; }

    /// <summary>Set the time to timeMs.</summary>
    public void Set(double timeMs) => NowMs = timeMs;

    /// <summary>Add milliseconds to the time.</summary>
    public void Advance(double milliseconds) => NowMs += milliseconds;
}
