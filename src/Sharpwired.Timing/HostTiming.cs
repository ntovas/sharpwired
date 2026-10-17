// Handwired source: src/runtime/host-timing.ts (validateSpringClockConfig, quantizeTimeMs,
// FrameTiming, quantizeFrameTiming, StepCount, countSpringSteps).
// This file holds float64 host arithmetic only. It builds no operations.

using System.Collections.Immutable;
using System.Globalization;

namespace Sharpwired.Timing;

/// <summary>
/// Handwired FrameTiming: the time cells of one frame.
/// </summary>
/// <param name="FrameTimeMs">The timestamp of the frame on the grid, never before the previous frame.</param>
/// <param name="DurationMs">
/// The time since the previous frame, or 0 for the first frame after idle. At most
/// maxStepsPerFrame × stepMs (1,200 ms by default), because the springs snap after that.
/// </param>
/// <param name="EventTimesMs">
/// The time of each event relative to the frame, for example -3.5 for a press 3.5 ms before the
/// frame. An event after the frame gets 0. An older event gets the later of the previous frame
/// and -maxStepsPerFrame × stepMs, or -maxStepsPerFrame × stepMs after idle.
/// </param>
public readonly record struct FrameTiming(double FrameTimeMs, double DurationMs, ImmutableArray<double> EventTimesMs);

/// <summary>
/// Handwired StepCount: the steps of one evaluation.
/// </summary>
/// <param name="Steps">floor(pendingMs / stepMs), or 0 for a snap.</param>
/// <param name="LeftoverMs">
/// The pending time that these steps do not simulate, less than one step. A later evaluation gets
/// it. 0 for a snap.
/// </param>
/// <param name="Snap">
/// True when more than stepBudget steps are pending. The host then puts each animated object on
/// its destination with zero velocity, and drops the pending time.
/// </param>
public readonly record struct StepCount(int Steps, double LeftoverMs, bool Snap);

/// <summary>
/// The float64 code that a host runs between two evaluations. It decides what the host writes
/// into the source table before each evaluation. It builds no operations.
/// </summary>
public static partial class HostTiming
{
    /// <summary>
    /// Handwired validateSpringClockConfig. The checks run in the Handwired order: the time grid,
    /// the step, maxStepsPerFrame, settleCheckSteps, and the span of one frame.
    /// </summary>
    public static void ValidateSpringClockConfig(SpringClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var timeGrid = config.TimeGrid;
        if (!(timeGrid >= 1 && IsInteger(Math.Log2(timeGrid))))
        {
            throw new ArgumentException($"the time grid must be a whole power of two, got {timeGrid.ToString(CultureInfo.InvariantCulture)}");
        }
        if (!(config.StepMs > 0 && IsInteger(config.StepMs * timeGrid)))
        {
            throw new ArgumentException("a spring step must be a positive multiple of the time grid");
        }
        if (!(config.MaxStepsPerFrame >= 1)) throw new ArgumentException("maxStepsPerFrame must be a positive integer");
        if (!(config.SettleCheckSteps >= 1)) throw new ArgumentException("settleCheckSteps must be a positive integer");
        // The durations and the event times of QuantizeFrameTiming are multiples of the grid within
        // maxStepsPerFrame × stepMs. FP32 holds each of them exactly while that span stays within
        // 2^24 grid steps. The product is a double, so 2^20 × 4 × 64 does not overflow.
        if (!(config.MaxStepsPerFrame * config.StepMs * timeGrid <= 16_777_216))
        {
            throw new ArgumentException("maxStepsPerFrame × stepMs must stay within 2^24 grid steps");
        }
    }

    /// <summary>
    /// Handwired quantizeTimeMs: a timestamp rounded to the time grid, for example 16.6667 ms gives
    /// 16.671875 ms on the 1/64 ms grid. The host rounds absolute timestamps before it subtracts
    /// them, so the durations of each split of a time span add up to the same number. The result
    /// is never -0. A null configuration is SpringClockConfig.Default.
    /// </summary>
    public static double QuantizeTimeMs(double timeStamp, SpringClockConfig? config = null)
    {
        config ??= SpringClockConfig.Default;
        if (!double.IsFinite(timeStamp)) throw new ArgumentException($"a timestamp must be finite, got {Format(timeStamp)}");
        return PositiveZero(JsMath.Round(timeStamp * config.TimeGrid) / config.TimeGrid);
    }

    /// <summary>
    /// Handwired quantizeFrameTiming: what a host writes into the time cells of a program for one
    /// frame. These are the duration of the frame and the times of its events relative to the
    /// frame, on the time grid. Absolute timestamps stay in float64. A null configuration is
    /// SpringClockConfig.Default.
    /// </summary>
    public static FrameTiming QuantizeFrameTiming(
        double frameTimeStamp,
        double? previousFrameTimeMs,
        IReadOnlyList<double> eventTimeStamps,
        SpringClockConfig? config = null)
    {
        config ??= SpringClockConfig.Default;
        ValidateSpringClockConfig(config);
        ArgumentNullException.ThrowIfNull(eventTimeStamps);
        var capMs = config.MaxStepsPerFrame * config.StepMs;
        var frameTimeMs = QuantizeTimeMs(frameTimeStamp, config);
        if (previousFrameTimeMs is { } previous) frameTimeMs = Math.Max(frameTimeMs, previous);
        var durationMs = previousFrameTimeMs is null ? 0 : Math.Min(capMs, frameTimeMs - previousFrameTimeMs.Value);
        var earliestEventMs = previousFrameTimeMs is null
            ? -capMs
            : Math.Max(-capMs, previousFrameTimeMs.Value - frameTimeMs);
        var eventTimesMs = ImmutableArray.CreateBuilder<double>(eventTimeStamps.Count);
        foreach (var timeStamp in eventTimeStamps)
        {
            eventTimesMs.Add(PositiveZero(Math.Min(0, Math.Max(earliestEventMs, QuantizeTimeMs(timeStamp, config) - frameTimeMs))));
        }
        return new FrameTiming(frameTimeMs, durationMs, eventTimesMs.MoveToImmutable());
    }

    /// <summary>
    /// Handwired countSpringSteps: the number of steps that an evaluation runs. pendingMs is the
    /// time since the springs last stepped, on the time grid. stepBudget is the part of
    /// maxStepsPerFrame that the frame did not use. For example, 17.5 ms pending runs 4 steps and
    /// carries 1.5 ms, and 1,300 ms pending with a budget of 300 snaps. A null configuration is
    /// SpringClockConfig.Default.
    /// </summary>
    public static StepCount CountSpringSteps(double pendingMs, int stepBudget, SpringClockConfig? config = null)
    {
        config ??= SpringClockConfig.Default;
        if (!(IsInteger(pendingMs * config.TimeGrid) && pendingMs >= 0))
        {
            throw new ArgumentException($"pending time must lie on the time grid and be nonnegative, got {Format(pendingMs)}");
        }
        if (!(stepBudget >= 0)) throw new ArgumentException("the step budget must be a nonnegative integer");
        var steps = Math.Floor(pendingMs / config.StepMs);
        if (steps > stepBudget) return new StepCount(0, 0, true);
        // steps <= stepBudget here, so the conversion to int is exact.
        return new StepCount((int)steps, pendingMs - steps * config.StepMs, false);
    }

    // JavaScript `x + 0`: -0 gives +0, and each other value stays the same.
    private static double PositiveZero(double value) => value == 0 ? 0.0 : value;

    // A number in a message. Sharpwired.Timing has no reference to Fp32.Format, so it writes the
    // shortest round-trip digits.
    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // JavaScript Number.isInteger: false for NaN and for the infinities.
    private static bool IsInteger(double value) => double.IsFinite(value) && Math.Floor(value) == value;
}
