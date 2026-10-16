// Handwired source: tests/runtime/macro-ranges.test.ts.
// This file ports the cases that use only Circuit.Select, Circuit.Multiply and
// Circuit.Reglu. Handwired emitSelect and emitMultiply call circuit.select and
// circuit.multiply, and emitRelu(value) is circuit.reglu([], 1, [[value, 1]], 0).
// The emitMaximum, emitMinimum and emitSpring cases belong to plan tasks 4 and 5.
// Each helper also compares with the tensor evaluator in Handwired. PHASE2-TENSOR-ASSERTIONS.md
// lists those assertions.

namespace Sharpwired.Core.Tests;

public class MacroRangesTests
{
    // Widest finite range the circuit will accept as a source bound.
    private const double Big = 3.4e38;

    // select(gate, off, on) expanded as gate * on + (1 - gate) * off, two masked products.
    private static float EvaluateSelect(double gate, double off, double on)
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeRange(circuit.Source(1), -Big, Big);
        circuit.AssumeRange(circuit.Source(2), -Big, Big);
        var result = circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2));
        return circuit.EvaluateReference([(float)gate, (float)off, (float)on])[result.Position];
    }

    // multiply(a, b); ranges chosen so each operand keeps its sign freedom.
    private static float EvaluateMultiply(double a, double b)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), a < 0 ? -Big : 0, Big);
        circuit.AssumeRange(circuit.Source(1), b < 0 ? -Big : 0, Big);
        var result = circuit.Multiply(circuit.Source(0), circuit.Source(1));
        return circuit.EvaluateReference([(float)a, (float)b])[result.Position];
    }

    // The clamp01 shape relu(v) - relu(v - 1), as Handwired emitClampZeroOne builds it, with unit pinned to 1.
    private static float EvaluateClampZeroOne(double v)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -Big, Big);
        circuit.AssumeRange(circuit.Source(1), 1, 1);
        var value = circuit.Source(0);
        var unit = circuit.Source(1);
        var positive = circuit.Reglu([], 1, [(value, 1)], 0);
        var aboveOne = circuit.Reglu([], 1, [(circuit.Affine([(value, 1), (unit, -1)]), 1)], 0);
        var result = circuit.Affine([(positive, 1), (aboveOne, -1)]);
        circuit.AssumeRange(result, 0, 1);
        return circuit.EvaluateReference([(float)v, 1])[result.Position];
    }

    // 'clamp01 is exact through 2**24 then degrades at the binade boundaries'.
    [Fact]
    public void Clamp01_is_exact_through_2_pow_24_then_degrades_at_the_binade_boundaries()
    {
        Js.Is(0.5, EvaluateClampZeroOne(0.5));
        Js.Is(0, EvaluateClampZeroOne(-3));
        Js.Is(1, EvaluateClampZeroOne(2));
        // The plateau value of 1 survives up to the lower binade edge 2**24 itself.
        Js.Is(1, EvaluateClampZeroOne(Math.Pow(2, 24)));
        // Once the ulp reaches 2, v - 1 rounds to v - 2, so the result is 2.
        Js.Is(2, EvaluateClampZeroOne(Math.Pow(2, 24) + 2));
        // At 2**25 the ulp is 4, so v - 1 rounds back up to v, and the two relu arms cancel.
        Js.Is(0, EvaluateClampZeroOne(Math.Pow(2, 25)));
    }

    private static readonly double[] Magnitudes =
    [
        0, 1, -1, 0.5, -0.5, 3.141_59, -2.718_28,
        1e-20, 1e20, Math.Pow(2, 24), -Math.Pow(2, 24), 12_345.678, -9.99e30, 1e10,
    ];

    // 'gate exactly 0 returns off bit-exactly for every magnitude pair'.
    [Fact]
    public void Gate_exactly_0_returns_off_bit_exactly_for_every_magnitude_pair()
    {
        foreach (var off in Magnitudes)
        {
            foreach (var on in Magnitudes)
            {
                Js.Is(Js.F(off), EvaluateSelect(0, off, on));
            }
        }
    }

    // 'gate exactly 1 returns on bit-exactly for integer operands with |x| < 2**23'.
    [Fact]
    public void Gate_exactly_1_returns_on_bit_exactly_for_integer_operands_below_2_pow_23()
    {
        double[] integers = [0, 1, -1, 2, -2, 1000, -1000, 65_535, Math.Pow(2, 22), -Math.Pow(2, 22), Math.Pow(2, 23) - 1, -(Math.Pow(2, 23) - 1)];
        foreach (var on in integers)
        {
            foreach (var off in integers)
            {
                Js.Is(on, EvaluateSelect(1, off, on));
            }
        }
        // off = 0 gives on exactly at any magnitude or fractional value.
        foreach (var on in new[] { 3.141_59, -2.718_28, 1e-20, 1e20, -9.99e30, Math.Pow(2, 24) })
        {
            Js.Is(Js.F(on), EvaluateSelect(1, 0, on));
        }
    }

    // 'gate exactly 1 stays exact across extreme magnitude differences'.
    [Fact]
    public void Gate_exactly_1_stays_exact_across_extreme_magnitude_differences()
    {
        Js.Is(1, EvaluateSelect(1, 1e10, 1));
        Js.Is(16_777_216, EvaluateSelect(1, -9.99e30, 16_777_216));
        Js.Is(Js.F(3.141_59), EvaluateSelect(1, 1000, 3.141_59));
        Js.Is(Js.F(3.141_59), EvaluateSelect(1, 16_777_216, 3.141_59));
    }

    // 'emitMultiply equals fround(a * b) across signs and magnitudes'.
    [Fact]
    public void Multiply_equals_fround_of_the_product_across_signs_and_magnitudes()
    {
        double[] values =
        [
            0, 1, -1, 0.5, -0.5, 2, -2, 1e10, -1e10, 1e-10,
            3.141_59, -2.718_28, Math.Pow(2, 23), 1234.5, 65_535, -65_535, 0.1, -0.1,
        ];
        foreach (var a in values)
        {
            foreach (var b in values)
            {
                Js.Is(Js.F(Js.F(a) * Js.F(b)), EvaluateMultiply(a, b));
            }
        }
    }

    // 'emitMultiply tracks fround even into overflow'.
    [Fact]
    public void Multiply_tracks_fround_even_into_overflow()
    {
        Js.Is(Js.F(Js.F(1e20) * Js.F(1e20)), EvaluateMultiply(1e20, 1e20));
        Js.Is(Js.F(Js.F(-1e20) * Js.F(1e20)), EvaluateMultiply(-1e20, 1e20));
        Js.Is(double.PositiveInfinity, EvaluateMultiply(1e20, 1e20));
    }

    // 'a proven-nonnegative operand lowers emitMultiply to one reglu' (reference part).
    [Fact]
    public void A_proven_nonnegative_operand_lowers_multiply_to_one_reglu()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(1), 0, double.PositiveInfinity);
        var product = circuit.Multiply(circuit.Source(0), circuit.Source(1));
        Assert.Single(circuit.Operations);
        Assert.Equal(OperationKind.Reglu, circuit.Operations[0].Kind);
        Js.Is(-6, circuit.EvaluateReference([-3, 2])[product.Position]);
        Js.Throws(() => circuit.EvaluateReference([-3, -2]), "outside its assumed range");
    }
}
