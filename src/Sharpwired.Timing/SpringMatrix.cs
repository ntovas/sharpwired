// Handwired source: src/runtime/host-timing.ts (SpringConstants, SpringMatrix,
// identitySpringMatrix, springStepMatrix, multiplySpringMatrices, springMatrixPower,
// springMatrixBound(constants, stepMs, maxStepsPerFrame), springMatrixBound(constants, config),
// and the stepMs and maxStepsPerFrame values of defaultSpringClockConfig).
// This file holds float64 host arithmetic only. It builds no operations. Phase 1 put it in
// Sharpwired.Core. Phase 3 moved it to Sharpwired.Timing with the rest of host-timing.ts.

namespace Sharpwired.Timing;

/// <summary>The stiffness and the damping of a spring. Both must be finite and nonnegative.</summary>
public readonly record struct SpringConstants(double Stiffness, double Damping);

/// <summary>
/// (displacement, velocity) after a number of steps, as
/// displacement' = m[0] × displacement + m[1] × velocity and
/// velocity' = m[2] × displacement + m[3] × velocity. A host writes the 4 entries into the
/// matrix cells of Macros.ApplySpringMatrix before each evaluation.
/// </summary>
public readonly record struct SpringMatrix(
    double DisplacementFromDisplacement,
    double DisplacementFromVelocity,
    double VelocityFromDisplacement,
    double VelocityFromVelocity)
{
    /// <summary>One spring step simulates this many milliseconds, as the default Handwired clock.</summary>
    public const double DefaultStepMs = 4;

    /// <summary>The most steps that the evaluations of one frame run together, as the default Handwired clock.</summary>
    public const int DefaultMaxStepsPerFrame = 300;

    /// <summary>The matrix of zero steps.</summary>
    public static SpringMatrix Identity { get; } = new(1, 0, 0, 1);

    /// <summary>The entry m[index], in the order of the constructor.</summary>
    public double this[int index] => index switch
    {
        0 => DisplacementFromDisplacement,
        1 => DisplacementFromVelocity,
        2 => VelocityFromDisplacement,
        3 => VelocityFromVelocity,
        _ => throw new ArgumentOutOfRangeException(nameof(index), index, "a spring matrix has 4 entries"),
    };

    /// <summary>The 4 entries, in the order of the constructor.</summary>
    public double[] ToArray() => [DisplacementFromDisplacement, DisplacementFromVelocity, VelocityFromDisplacement, VelocityFromVelocity];

    /// <summary>
    /// One semi-implicit Euler step of index.ts stepSpringTo as a matrix:
    /// velocity' = velocity + (-stiffness × displacement - damping × velocity) × dt, then
    /// displacement' = displacement + velocity' × dt.
    /// </summary>
    public static SpringMatrix SpringStepMatrix(SpringConstants constants, double stepMs = DefaultStepMs)
    {
        if (!(double.IsFinite(constants.Stiffness) && constants.Stiffness >= 0
            && double.IsFinite(constants.Damping) && constants.Damping >= 0))
        {
            throw new ArgumentException("stiffness and damping must be finite and nonnegative");
        }
        var dt = stepMs / 1000;
        var velocityKeep = 1 - constants.Damping * dt;
        return new SpringMatrix(1 - constants.Stiffness * dt * dt, dt * velocityKeep, -constants.Stiffness * dt, velocityKeep);
    }

    /// <summary>The matrix product left × right.</summary>
    public static SpringMatrix MultiplySpringMatrices(SpringMatrix left, SpringMatrix right) => new(
        left[0] * right[0] + left[1] * right[2],
        left[0] * right[1] + left[1] * right[3],
        left[2] * right[0] + left[3] * right[2],
        left[2] * right[1] + left[3] * right[3]);

    /// <summary>
    /// matrix^count. The function squares the matrix again and again, and multiplies the squares
    /// that the bits of count select. For example, the spring matrix of 4 steps is
    /// SpringMatrixPower(SpringStepMatrix(constants), 4). Every power of one matrix commutes, so
    /// the result is the same real matrix as count single steps, within float64 rounding.
    /// </summary>
    public static SpringMatrix SpringMatrixPower(SpringMatrix matrix, int count)
    {
        if (count < 0) throw new ArgumentException($"a step count must be a nonnegative integer, got {count}");
        var result = Identity;
        var square = matrix;
        var remaining = count;
        while (remaining > 0)
        {
            if (remaining % 2 == 1) result = MultiplySpringMatrices(result, square);
            remaining /= 2;
            if (remaining > 0) square = MultiplySpringMatrices(square, square);
        }
        return result;
    }

    /// <summary>
    /// The magnitude to declare on the 4 matrix cells before the build, for example 10.36 for
    /// stiffness 650 and damping 45. It is the largest |entry| of M^n for n = 0..maxStepsPerFrame,
    /// with headroom. So the FP32 value of each entry that the host writes stays within it. Throws
    /// for a spring whose powers overflow, for example a stiffness far above 250,000 with 4 ms steps.
    /// </summary>
    public static double SpringMatrixBound(SpringConstants constants, double stepMs = DefaultStepMs, int maxStepsPerFrame = DefaultMaxStepsPerFrame)
    {
        var step = SpringStepMatrix(constants, stepMs);
        var power = Identity;
        var bound = 1.0;
        for (var steps = 1; steps <= maxStepsPerFrame; steps++)
        {
            power = MultiplySpringMatrices(power, step);
            foreach (var entry in power.ToArray()) bound = Math.Max(bound, Math.Abs(entry));
        }
        // Rounding is monotone, so f(entry) <= f(bound) for every |entry| <= bound.
        // The cast to float is Math.fround: it rounds to the nearest FP32 value, ties to even.
        var declared = (double)(float)(bound * (1 + 1e-6));
        if (!double.IsFinite(declared)) throw new ArgumentException("the spring matrix overflows FP32 within maxStepsPerFrame steps");
        return declared;
    }

    /// <summary>
    /// Handwired springMatrixBound(constants, config): the bound of
    /// SpringMatrixBound(constants, stepMs, maxStepsPerFrame) with config.StepMs and
    /// config.MaxStepsPerFrame. The function does not validate the configuration, as Handwired.
    /// </summary>
    public static double SpringMatrixBound(SpringConstants constants, SpringClockConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return SpringMatrixBound(constants, config.StepMs, config.MaxStepsPerFrame);
    }
}
