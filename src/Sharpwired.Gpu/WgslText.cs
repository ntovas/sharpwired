// Handwired source: src/json-search/emit.ts (the header of a shader) and the header of
// lab/ui-exploration/codegen/wgsl.ts (the measured FP32 failures on Metal). The WGSL prelude of each
// shader: the Params struct, the keep function k, the bit tests is_nan and is_finite, and relu. Also the
// literals of emit.ts: wgslFloat and wgslInt.

using System.Globalization;

namespace Sharpwired.Gpu;

/// <summary>The WGSL text that each shader of this backend starts with.</summary>
internal static class WgslText
{
    /// <summary>
    /// The prelude. A shader that includes it declares a read-only storage binding <c>params: Params</c>,
    /// and writes 0 to <c>params.zero</c>.
    /// <list type="bullet">
    /// <item><c>k(v)</c> is the keep function. It returns <c>v</c> with the same bits, because
    /// <c>params.zero</c> is 0. The shader compiler cannot know the value, so it cannot move arithmetic
    /// across <c>k</c>: no contraction to a fused multiply-add, no regrouped sum, no folded constant. The
    /// XOR also keeps NaN, the infinities and -0.</item>
    /// <item><c>is_nan</c> and <c>is_finite</c> test the bits. WGSL lets a compiler fold <c>v != v</c> to
    /// false.</item>
    /// <item><c>relu(g)</c> equals <c>MathF.Max(0, g)</c>: NaN for a NaN gate, +0 for a -0 gate.</item>
    /// </list>
    /// </summary>
    public const string Prelude = """
        struct Params {
            first: u32,
            count: u32,
            zero: u32,
        }

        fn k(v: f32) -> f32 { return bitcast<f32>(bitcast<u32>(v) ^ params.zero); }

        fn is_nan(v: f32) -> bool { return (bitcast<u32>(v) & 0x7fffffffu) > 0x7f800000u; }

        fn is_finite(v: f32) -> bool { return (bitcast<u32>(v) & 0x7f800000u) != 0x7f800000u; }

        fn relu(g: f32) -> f32 { return select(select(k(0.0f), g, g > k(0.0f)), g, is_nan(g)); }


        """;

    /// <summary>
    /// emit.ts wgslFloat: an FP32 value as one WGSL literal with the exact bits. A whole number of
    /// magnitude below 2^24 is a decimal literal, for example <c>3.0f</c>. Another value is a
    /// hexadecimal literal, for example <c>0x1.555556p-2f</c>, or <c>0x0.000400p-126f</c> for a
    /// subnormal value. A negative value is in parentheses, and -0 is <c>(-0.0f)</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is NaN or infinite.</exception>
    public static string Float(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentException($"a WGSL literal must be a finite FP32 value, got {value}", nameof(value));
        if (value == 0) return float.IsNegative(value) ? "(-0.0f)" : "0.0f";
        if (value == MathF.Truncate(value) && MathF.Abs(value) < 16_777_216f)
        {
            var whole = ((int)value).ToString(CultureInfo.InvariantCulture);
            return value < 0 ? $"({whole}.0f)" : $"{whole}.0f";
        }
        var bits = BitConverter.SingleToUInt32Bits(MathF.Abs(value));
        var exponentBits = (int)((bits >> 23) & 0xff);
        // The 23 fraction bits, shifted left by one, are 6 hexadecimal digits.
        var fraction = ((bits & 0x7fffff) << 1).ToString("x6", CultureInfo.InvariantCulture);
        var magnitude = exponentBits == 0
            ? $"0x0.{fraction}p-126f"
            : $"0x1.{fraction}p{(exponentBits - 127).ToString(CultureInfo.InvariantCulture)}f";
        return value < 0 ? $"(-{magnitude})" : magnitude;
    }

    /// <summary>emit.ts wgslInt: an i32 literal. A negative value is in parentheses, for example <c>(-1i)</c>.</summary>
    public static string Int(int value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return value < 0 ? $"({text}i)" : $"{text}i";
    }

    /// <summary>A u32 literal, for example <c>3u</c>.</summary>
    public static string Uint(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value.ToString(CultureInfo.InvariantCulture) + "u";
    }
}
