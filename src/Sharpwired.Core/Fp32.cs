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
    /// A number as JavaScript String(number) writes it: the shortest digits that give the same
    /// value, in decimal form for 1e-6 &lt;= |value| &lt; 1e21 and in exponent form otherwise, for
    /// example "0.000001", "123456789012345680000", "1e-7" and "1.5e+21". Both zeros give "0".
    /// Each other value gets a different text, so the text can be part of a cache key.
    /// </summary>
    public static string Format(double value)
    {
        if (value == 0) return "0";
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        // "R" gives the shortest round-trip digits, for example "1.2345678901234568E+20" or "0.0001".
        var roundTrip = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = roundTrip.IndexOf('E', StringComparison.Ordinal);
        var mantissa = exponentIndex < 0 ? roundTrip : roundTrip[..exponentIndex];
        var exponent = exponentIndex < 0 ? 0 : int.Parse(roundTrip[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var pointIndex = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = pointIndex < 0 ? mantissa : mantissa.Remove(pointIndex, 1);
        // The value is 0.digits × 10^decimalExponent, as in the ECMAScript Number::toString steps.
        var decimalExponent = (pointIndex < 0 ? mantissa.Length : pointIndex) + exponent;
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits.Trim('0');
        decimalExponent -= leadingZeros;
        var count = digits.Length;
        string text;
        if (count <= decimalExponent && decimalExponent <= 21) text = digits + new string('0', decimalExponent - count);
        else if (0 < decimalExponent && decimalExponent <= 21) text = digits[..decimalExponent] + "." + digits[decimalExponent..];
        else if (-6 < decimalExponent && decimalExponent <= 0) text = "0." + new string('0', -decimalExponent) + digits;
        else
        {
            var scientificExponent = decimalExponent - 1;
            var sign = scientificExponent < 0 ? "-" : "+";
            var fraction = count == 1 ? "" : "." + digits[1..];
            text = $"{digits[0]}{fraction}e{sign}{Math.Abs(scientificExponent).ToString(CultureInfo.InvariantCulture)}";
        }
        return value < 0 ? "-" + text : text;
    }
}
