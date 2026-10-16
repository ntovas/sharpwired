// Handwired source: src/runtime/numeric-ui-core.ts (Math.fround, evaluateBoolean,
// lowestPowerOfTwo, countTrailingZeros, isPowerOfTwoGrid, exactGridSteps, smallestGrid,
// largestGrid), and the JavaScript number-to-string rule for keys and messages.

using System.Globalization;
using System.Numerics;

namespace Sharpwired.Core;

/// <summary>FP32 helpers that give the same values as the Handwired float64 code.</summary>
public static class Fp32
{
    /// <summary>
    /// FP32 holds every multiple of a power of two with at most 24 significant bits. A sum or
    /// product of values on grids does not round while it stays within this many grid steps.
    /// </summary>
    public const double ExactGridSteps = 16_777_216;

    /// <summary>2^-149, the smallest grid step.</summary>
    public static readonly double SmallestGrid = Math.ScaleB(1.0, -149);

    /// <summary>2^127, the largest grid step.</summary>
    public static readonly double LargestGrid = Math.ScaleB(1.0, 127);

    /// <summary>Math.fround: round a float64 value to the nearest FP32 value.</summary>
    public static double Round(double value) => (float)value;

    /// <summary>
    /// The boolean atom: f(f(f(score) + 8_388_609) - 8_388_609). FP32 rounds to whole numbers at
    /// this size, and the odd base makes 0.5 round up.
    /// </summary>
    public static float EvaluateBoolean(float score)
    {
        const float Base = 8_388_609f;
        float shifted = score + Base;
        return shifted - Base;
    }

    /// <summary>
    /// The largest power of two that divides a finite nonzero value, for example 4 for 12 and
    /// 0.25 for 0.75. For a non-finite value, the result is the value that the Handwired bit
    /// operations give.
    /// </summary>
    public static double LowestPowerOfTwo(double value)
    {
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        var high = (uint)(bits >> 32);
        var low = (uint)bits;
        var biasedExponent = (int)((high >> 20) & 0x7ff);
        // FP32 values are normal float64 values, so the implicit first mantissa bit is set.
        var exponent = biasedExponent - 1075;
        var trailingZeros = low != 0
            ? BitOperations.TrailingZeroCount(low)
            : 32 + BitOperations.TrailingZeroCount((high & 0xfffff) | 0x100000);
        return Math.ScaleB(1.0, exponent + trailingZeros);
    }

    /// <summary>True for a power of two from 2^-149 to 2^127.</summary>
    public static bool IsPowerOfTwoGrid(double step) =>
        double.IsFinite(step) && step >= SmallestGrid && step <= LargestGrid && LowestPowerOfTwo(step) == step;

    /// <summary>
    /// A number as JavaScript String(number) writes it in most cases: the shortest text that
    /// gives the same value. Both zeros give "0", as in JavaScript.
    /// </summary>
    public static string Format(double value)
    {
        if (value == 0) return "0";
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        return value.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);
    }
}
