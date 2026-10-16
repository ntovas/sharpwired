// Handwired source: docs/execution-model.md (section "FP32") and
// src/runtime/numeric-ui-core.ts (evaluateTerms, evaluateBoolean).
// These tests check that .NET float arithmetic gives the Math.fround semantics
// that Handwired uses. The exactness of every macro depends on them.

namespace Sharpwired.Core.Tests;

public class Fp32SemanticsTests
{
    // The values come from an array, so that the C# compiler cannot fold them.
    private static readonly float[] FixedInputs = [1f + MathF.ScaleB(1f, -12), 1f + MathF.ScaleB(1f, -12), -(1f + MathF.ScaleB(1f, -11))];

    [Fact]
    public void A_multiply_and_an_add_round_separately_and_do_not_contract_into_an_fma()
    {
        float a = FixedInputs[0];
        float b = FixedInputs[1];
        float c = FixedInputs[2];

        float separate = (float)((float)(a * b) + c);
        float plain = a * b + c;
        float fused = MathF.FusedMultiplyAdd(a, b, c);

        // The exact product is 1 + 2^-11 + 2^-24. FP32 rounds it to 1 + 2^-11, so the sum is 0.
        Assert.Equal(0f, separate);
        Assert.Equal(MathF.ScaleB(1f, -24), fused);
        Assert.NotEqual(fused, separate);
        Assert.Equal(separate, plain);
    }

    [Fact]
    public void Seeded_inputs_where_an_fma_differs_give_the_separately_rounded_value()
    {
        var random = new Random(20261016);
        var found = 0;
        for (var sample = 0; sample < 200_000 && found < 1_000; sample++)
        {
            float a = (float)(random.NextDouble() * 2 - 1);
            float b = (float)(random.NextDouble() * 2 - 1);
            float c = (float)(random.NextDouble() * 2 - 1);
            float fused = MathF.FusedMultiplyAdd(a, b, c);
            float product = a * b;
            float separate = (float)((double)product + c);
            if (fused == separate) continue;
            found++;
            Assert.Equal(separate, a * b + c);
            Assert.Equal(separate, (float)((float)a * (float)b + (float)c));
        }

        Assert.True(found >= 100, $"the search found only {found} inputs where an FMA differs");
    }

    [Fact]
    public void The_boolean_atom_rounds_scores_in_zero_to_one_at_one_half()
    {
        // Handwired evaluateBoolean: f(f(f(score) + 8_388_609) - 8_388_609).
        float[] scores = [0f, 0.25f, 0.49f, BitConverter.Int32BitsToSingle(0x3effffff), 0.5f, 0.51f, 0.75f, 1f];
        float[] expected = [0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f];
        for (var index = 0; index < scores.Length; index++)
        {
            float shifted = scores[index] + 8388609f;
            float bit = shifted - 8388609f;
            Assert.Equal(expected[index], bit);
        }
    }

    [Fact]
    public void Subnormal_values_survive()
    {
        float[] values = [float.Epsilon, 1.4e-45f, 2f];
        Assert.True(values[0] > 0);
        Assert.Equal(2.8e-45f, values[1] * values[2]);
        Assert.Equal(BitConverter.Int32BitsToSingle(1), values[0]);
    }
}
