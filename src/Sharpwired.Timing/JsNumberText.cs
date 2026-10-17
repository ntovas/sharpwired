// Handwired source: the JavaScript String(number) rule (ECMAScript Number::toString), which the
// messages of src/runtime/host-timing.ts use. The body is a copy of Sharpwired.Core Fp32.Format,
// because Sharpwired.Timing has no project reference.

using System.Globalization;

namespace Sharpwired.Timing;

/// <summary>The JavaScript text of a number, for the Timing messages.</summary>
internal static class JsNumberText
{
    /// <summary>
    /// A number as JavaScript String(number) writes it: the shortest digits that give the same
    /// value, in decimal form for 1e-6 &lt;= |value| &lt; 1e21 and in exponent form otherwise, for
    /// example "0.000001", "123456789012345680000", "1e-7" and "1.5e+21". Both zeros give "0".
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
