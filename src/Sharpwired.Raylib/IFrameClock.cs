// Handwired source: template/toggle.ts (mountToggle). The browser gives each frame its time through
// requestAnimationFrame. A Sharpwired host reads the time of a frame from an IFrameClock.
// This file makes no Raylib call.

using System.Diagnostics;

namespace Sharpwired.Raylib;

/// <summary>The time source of a host, in milliseconds. Tests use a fake clock.</summary>
public interface IFrameClock
{
    /// <summary>The time now, in milliseconds.</summary>
    double NowMs { get; }
}

/// <summary>The time since the construction of the clock, from <see cref="Stopwatch"/>. It makes no Raylib call.</summary>
public sealed class StopwatchFrameClock : IFrameClock
{
    private readonly long start = Stopwatch.GetTimestamp();

    /// <summary>The milliseconds since the construction of the clock.</summary>
    public double NowMs => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
