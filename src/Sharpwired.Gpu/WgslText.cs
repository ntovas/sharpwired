// Handwired source: src/json-search/emit.ts (the header of a shader) and the header of
// lab/ui-exploration/codegen/wgsl.ts (the measured FP32 failures on Metal). The WGSL prelude of each
// shader: the Params struct, the keep function k, the bit tests is_nan and is_finite, and relu.

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
}
