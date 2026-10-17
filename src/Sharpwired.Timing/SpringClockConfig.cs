// Handwired source: src/runtime/host-timing.ts (SpringClockConfig, defaultSpringClockConfig).
// HostTiming.ValidateSpringClockConfig checks a configuration.

namespace Sharpwired.Timing;

/// <summary>
/// The clock of the springs that a host steps between evaluations.
/// </summary>
/// <param name="TimeGrid">The host rounds times to multiples of 1 / TimeGrid ms, for example 64 for a 1/64 ms grid.</param>
/// <param name="StepMs">One spring step simulates this many milliseconds.</param>
/// <param name="MaxStepsPerFrame">
/// The most steps that the evaluations of one frame run together, for example 300 steps (1,200 ms),
/// as chenguini's ui.md. When a frame needs more, each animated object snaps onto its destination.
/// </param>
/// <param name="SettleCheckSteps">
/// The host checks the settle state every this many steps of simulated time, from the time at which
/// the clock started.
/// </param>
public sealed record SpringClockConfig(int TimeGrid, double StepMs, int MaxStepsPerFrame, int SettleCheckSteps)
{
    /// <summary>Handwired defaultSpringClockConfig: a 1/64 ms grid, 4 ms steps, 300 steps per frame, a settle check every 16 steps.</summary>
    public static SpringClockConfig Default { get; } = new(64, 4, 300, 16);
}
