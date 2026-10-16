// Handwired source: docs/execution-model.md (section "FP32") and
// src/runtime/numeric-ui-core.ts (evaluateTerms, evaluateBoolean).
// These tests check that .NET float arithmetic gives the Math.fround semantics
// that Handwired uses. The exactness of every macro depends on them.

// The JIT does not optimize a method that it compiles at Tier 0 or with MinOpts, and that code
// never forms an FMA. So the arithmetic helpers below have AggressiveOptimization: the JIT skips
// Tier 0 and compiles them fully optimized at the first call. The project sets Optimize to true,
// so that the Debug build does not mark the assembly with DisableOptimizations, which forces
// unoptimized code. TieredCompilation is false in the project, so that the production evaluator
// in Sharpwired.Core also gets fully optimized code at the first call in a Release run.
// The Debug build of Sharpwired.Core is not optimized. So the Debug run checks the helpers below
// under full optimization, but it does not check Circuit.EvaluateTerms under full optimization.
// Only the Release run (dotnet test -c Release) checks Circuit.EvaluateTerms under full
// optimization.

using System.Runtime.CompilerServices;

namespace Sharpwired.Core.Tests;

public class Fp32SemanticsTests
{
    // The values come from an array, so that the C# compiler cannot fold them.
    // a = b = 1 + 2^-12 and c = -(1 + 2^-11). The exact product is 1 + 2^-11 + 2^-24.
    // FP32 rounds the product to 1 + 2^-11, so the separately rounded sum is 0. An FMA gives 2^-24.
    private static readonly float[] FixedInputs = [1f + MathF.ScaleB(1f, -12), 1f + MathF.ScaleB(1f, -12), -(1f + MathF.ScaleB(1f, -11))];

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static float PlainMultiplyAdd(float a, float b, float c) => a * b + c;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static float CastMultiplyAdd(float a, float b, float c) => (float)((float)a * (float)b + (float)c);

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static float AddThenMultiply(float a, float b, float c) => c + a * b;

    [Fact]
    public void A_multiply_and_an_add_round_separately_and_do_not_contract_into_an_fma()
    {
        float a = FixedInputs[0];
        float b = FixedInputs[1];
        float c = FixedInputs[2];

        float fused = MathF.FusedMultiplyAdd(a, b, c);
        Assert.Equal(MathF.ScaleB(1f, -24), fused);

        Assert.Equal(0f, PlainMultiplyAdd(a, b, c));
        Assert.Equal(0f, CastMultiplyAdd(a, b, c));
        Assert.Equal(0f, AddThenMultiply(a, b, c));
    }

    [Fact]
    public void The_reference_evaluator_rounds_a_multiply_and_an_add_separately()
    {
        float a = FixedInputs[0];
        float b = FixedInputs[1];
        float c = FixedInputs[2];
        Assert.NotEqual(0f, MathF.FusedMultiplyAdd(a, b, c));

        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 1, 2);
        circuit.AssumeRange(circuit.Source(1), 1, 2);
        circuit.AssumeRange(circuit.Source(2), -2, -1);
        // A Multiply and an affine sum: f(f(a × b) + c). Multiply stores the rounded product as its
        // own wire, so this case cannot detect an FMA. It checks the rounding across two operations.
        var product = circuit.Multiply(circuit.Source(0), circuit.Source(1));
        var sum = circuit.RoundAffine([(product, 1), (circuit.Source(2), 1)]);
        // One affine term with a coefficient: f(c + f(b × a)), where b is the coefficient.
        // EvaluateTerms computes value + coefficient × values[wire], the form that an FMA can replace.
        var term = circuit.RoundAffine([(circuit.Source(2), 1), (circuit.Source(0), (double)b)]);

        var values = circuit.EvaluateReference([a, b, c]);
        Assert.Equal(0f, values[sum.Position]);
        Assert.Equal(0f, values[term.Position]);
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
            Assert.Equal(separate, PlainMultiplyAdd(a, b, c));
            Assert.Equal(separate, CastMultiplyAdd(a, b, c));
            Assert.Equal(separate, AddThenMultiply(a, b, c));
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

        // The same scores through Circuit.Boolean and the reference evaluator.
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var atom = circuit.Boolean(circuit.Source(0));
        for (var index = 0; index < scores.Length; index++)
        {
            Js.Is(expected[index], circuit.EvaluateReference([scores[index]])[atom.Position]);
        }
        Js.Is(0, circuit.EvaluateReference([BitConverter.Int32BitsToSingle(0x3effffff)])[atom.Position]);
        Js.Is(1, circuit.EvaluateReference([0.5f])[atom.Position]);
    }

    [Fact]
    public void Subnormal_values_survive()
    {
        float[] values = [float.Epsilon, 1.4e-45f, 2f];
        Assert.True(values[0] > 0);
        Assert.Equal(2.8e-45f, values[1] * values[2]);
        Assert.Equal(BitConverter.Int32BitsToSingle(1), values[0]);

        // The same values through the reference evaluator: f(2 × 2^-149), f(2^-149 + 2^-149) and
        // f(2^-149 × 0.5), which rounds to +0 at the bottom of the subnormal range.
        var circuit = new Circuit(1);
        var doubled = circuit.RoundAffine([(circuit.Source(0), 2)]);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(0), 1)]);
        var halved = circuit.RoundAffine([(circuit.Source(0), 0.5)]);
        var evaluated = circuit.EvaluateReference([float.Epsilon]);
        Assert.Equal(2.8e-45f, evaluated[doubled.Position]);
        Assert.Equal(2.8e-45f, evaluated[sum.Position]);
        Js.Is(0, evaluated[halved.Position]);
    }
}
