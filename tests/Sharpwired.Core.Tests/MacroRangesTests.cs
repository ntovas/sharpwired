// Handwired source: tests/runtime/macro-ranges.test.ts.
// Handwired emitSelect and emitMultiply call circuit.select and circuit.multiply, so this port
// calls Circuit.Select and Circuit.Multiply. Macros.Relu, Macros.Maximum and Macros.Minimum port
// emitRelu, emitMaximum and emitMinimum, and Macros.Spring and Macros.ClampZeroOne port emitSpring
// and emitClampZeroOne.
// Each helper compares the tensor value with the reference value (toBe), as in Handwired.

namespace Sharpwired.Core.Tests;

public class MacroRangesTests
{
    // Widest finite range the circuit will accept as a source bound.
    private const double Big = 3.4e38;

    // The shared generator of the file, as the module-level random of macro-ranges.test.ts.
    // Only one test reads it, so the order of the tests does not change its values.
    private static readonly Func<double> SharedRandom = Js.CreateRandom(0x5afe9a);

    private static double SignedMagnitude(Func<double> next, double scale) => (next() * 2 - 1) * scale;

    // The reference value of the result. The compiled and literal evaluators must agree bit for bit
    // at each magnitude: the tensor value must be the reference value (toBe).
    private static float EvaluateBoth(Circuit circuit, float[] source, Wire result)
    {
        var reference = circuit.EvaluateReference(source);
        var tensor = circuit.EvaluateTensor(source);
        Js.Is(reference[result.Position], tensor[result.Position]);
        return reference[result.Position];
    }

    // max(a, b) expanded as b + relu(a - b), evaluated through the real circuit.
    private static float EvaluateMaximum(double a, double b)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -Big, Big);
        circuit.AssumeRange(circuit.Source(1), -Big, Big);
        var result = circuit.Maximum(circuit.Source(0), circuit.Source(1));
        return EvaluateBoth(circuit, [(float)a, (float)b], result);
    }

    // min(a, b) expanded as a - relu(a - b).
    private static float EvaluateMinimum(double a, double b)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -Big, Big);
        circuit.AssumeRange(circuit.Source(1), -Big, Big);
        var result = circuit.Minimum(circuit.Source(0), circuit.Source(1));
        return EvaluateBoth(circuit, [(float)a, (float)b], result);
    }

    // select(gate, off, on) expanded as gate * on + (1 - gate) * off, two masked products.
    private static float EvaluateSelect(double gate, double off, double on)
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeRange(circuit.Source(1), -Big, Big);
        circuit.AssumeRange(circuit.Source(2), -Big, Big);
        var result = circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2));
        return EvaluateBoth(circuit, [(float)gate, (float)off, (float)on], result);
    }

    // multiply(a, b); ranges chosen so each operand keeps its sign freedom.
    private static float EvaluateMultiply(double a, double b)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), a < 0 ? -Big : 0, Big);
        circuit.AssumeRange(circuit.Source(1), b < 0 ? -Big : 0, Big);
        var result = circuit.Multiply(circuit.Source(0), circuit.Source(1));
        return EvaluateBoth(circuit, [(float)a, (float)b], result);
    }

    // The clamp01 shape relu(v) - relu(v - 1), as Handwired emitClampZeroOne builds it, with unit pinned to 1.
    private static float EvaluateClampZeroOne(double v)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -Big, Big);
        circuit.AssumeRange(circuit.Source(1), 1, 1);
        var value = circuit.Source(0);
        var unit = circuit.Source(1);
        var positive = circuit.Relu(value);
        var aboveOne = circuit.Relu(circuit.Affine([(value, 1), (unit, -1)]));
        var result = circuit.Affine([(positive, 1), (aboveOne, -1)]);
        circuit.AssumeRange(result, 0, 1);
        var value01 = EvaluateBoth(circuit, [(float)v, 1], result);
        // Sharpwired: Handwired does not export emitClampZeroOne. Macros.ClampZeroOne is public, so
        // this helper also checks that the macro gives the same value as the mirror above.
        var macroCircuit = new Circuit(2);
        macroCircuit.AssumeRange(macroCircuit.Source(0), -Big, Big);
        macroCircuit.AssumeRange(macroCircuit.Source(1), 1, 1);
        var macroResult = macroCircuit.ClampZeroOne(macroCircuit.Source(0), macroCircuit.Source(1));
        Assert.Equal(new NumericRange(0, 1), macroCircuit.Ranges[macroResult.Position]);
        Js.Is(value01, macroCircuit.EvaluateReference([(float)v, 1])[macroResult.Position]);
        return value01;
    }

    // 'the known disparity failure: max(2**24, -1) loses one unit'.
    [Fact]
    public void The_known_disparity_failure_max_of_2_pow_24_and_minus_1_loses_one_unit()
    {
        Js.Is(16_777_215, EvaluateMaximum(Math.Pow(2, 24), -1));
        Js.Is(16_777_216, Math.Max(Math.Pow(2, 24), -1));
        Js.Is(Math.Max(Math.Pow(2, 24), -1) - 1, EvaluateMaximum(Math.Pow(2, 24), -1));
    }

    // 'the mirror disparity failure: min(2**24, -1) collapses to zero'.
    [Fact]
    public void The_mirror_disparity_failure_min_of_2_pow_24_and_minus_1_collapses_to_zero()
    {
        Js.Is(0, EvaluateMinimum(Math.Pow(2, 24), -1));
        Js.Is(-1, Math.Min(Math.Pow(2, 24), -1));
    }

    // 'the first diverging magnitude for max(a, -1) is the integer a = 2**24'.
    [Fact]
    public void The_first_diverging_magnitude_for_max_of_a_and_minus_1_is_the_integer_2_pow_24()
    {
        Js.Is(Math.Pow(2, 24) - 1, EvaluateMaximum(Math.Pow(2, 24) - 1, -1));
        Js.Is(16_777_215, EvaluateMaximum(Math.Pow(2, 24), -1));
        Assert.NotEqual(Math.Max(Math.Pow(2, 24), -1), EvaluateMaximum(Math.Pow(2, 24), -1));
        for (var a = Math.Pow(2, 24) - 64; a < Math.Pow(2, 24); a++)
        {
            Js.Is(a, EvaluateMaximum(a, -1));
        }
    }

    // 'max and min stay exact for integer operands with |x| < 2**23'.
    [Fact]
    public void Max_and_min_stay_exact_for_integer_operands_below_2_pow_23()
    {
        for (var trial = 0; trial < 3_000; trial++)
        {
            var a = Js.Round(SignedMagnitude(SharedRandom, Math.Pow(2, 23) - 1));
            var b = Js.Round(SignedMagnitude(SharedRandom, Math.Pow(2, 23) - 1));
            Js.Is(Math.Max(a, b), EvaluateMaximum(a, b));
            Js.Is(Math.Min(a, b), EvaluateMinimum(a, b));
        }
        Js.Is(Math.Pow(2, 23) - 1, EvaluateMaximum(Math.Pow(2, 23) - 1, -(Math.Pow(2, 23) - 1)));
        Js.Is(-(Math.Pow(2, 23) - 1), EvaluateMinimum(Math.Pow(2, 23) - 1, -(Math.Pow(2, 23) - 1)));
    }

    // 'max and min are NOT exact for fractional operands even at small scale'.
    [Fact]
    public void Max_and_min_are_not_exact_for_fractional_operands_even_at_small_scale()
    {
        var a = Js.F(422.882_418_613_880_9);
        var b = Js.F(-79.529_039_096_087_22);
        Assert.NotEqual(Math.Max(a, b), EvaluateMaximum(a, b));
        // toBeCloseTo(expected, 2): |expected - received| < 10^-2 / 2.
        Assert.True(Math.Abs(Math.Max(a, b) - EvaluateMaximum(a, b)) < 0.005);
    }

    // 'the winner being the small operand stays exact even at huge disparity'.
    [Fact]
    public void The_winner_being_the_small_operand_stays_exact_even_at_huge_disparity()
    {
        for (var exponent = 10; exponent <= 40; exponent++)
        {
            var large = Js.F(Math.Pow(2, exponent));
            Js.Is(large, EvaluateMaximum(-1, large));
        }
        Js.Is(0, EvaluateMinimum(Math.Pow(2, 24), -1));
        Js.Is(-1, EvaluateMinimum(Math.Pow(2, 23), -1));
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

    // emitSpring settled branch: the sources are position, destination, velocity, stiffness,
    // damping, timestep, settleDistance, settleSpeed, settleDistanceScale, settleSpeedScale, unit.
    private static Circuit BuildSpringCircuit(out SpringWires spring)
    {
        var circuit = new Circuit(11);
        circuit.AssumeRange(circuit.Source(0), -1e6, 1e6);
        circuit.AssumeRange(circuit.Source(1), -1e6, 1e6);
        circuit.AssumeRange(circuit.Source(2), -1e6, 1e6);
        circuit.AssumeRange(circuit.Source(3), 0, 1000);
        circuit.AssumeRange(circuit.Source(4), 0, 1000);
        circuit.AssumeRange(circuit.Source(5), 0, 1);
        circuit.AssumeRange(circuit.Source(6), 0, 1e6);
        circuit.AssumeRange(circuit.Source(7), 0, 1e6);
        circuit.AssumeRange(circuit.Source(8), 0, 1e6);
        circuit.AssumeRange(circuit.Source(9), 0, 1e6);
        circuit.AssumeRange(circuit.Source(10), 1, 1);
        var cells = Enumerable.Range(0, 11).Select(circuit.Source).ToArray();
        spring = circuit.Spring(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6], cells[7], cells[8], cells[9], cells[10]);
        return circuit;
    }

    private static (double Position, double Velocity, double Active) BuildSettledSpring(double initialPosition, double destination, double initialVelocity)
    {
        var circuit = BuildSpringCircuit(out var spring);
        var position = initialPosition;
        var velocity = initialVelocity;
        var active = 1.0;
        for (var frame = 0; frame < 240; frame++)
        {
            float[] source = [(float)position, (float)destination, (float)velocity, 200, 20, 0.05f, 0.5f, 0.5f, 1000, 1000, 1];
            var reference = circuit.EvaluateReference(source);
            var tensor = circuit.EvaluateTensor(source);
            // The reference and the compiled springs must step identically (toBe).
            Js.Is(reference[spring.Position.Position], tensor[spring.Position.Position]);
            Js.Is(reference[spring.Velocity.Position], tensor[spring.Velocity.Position]);
            Js.Is(reference[spring.Active.Position], tensor[spring.Active.Position]);
            position = reference[spring.Position.Position];
            velocity = reference[spring.Velocity.Position];
            active = reference[spring.Active.Position];
        }
        return (position, velocity, active);
    }

    // 'emitSpring settled branch' / 'a settled spring snaps position to destination and velocity to zero exactly'.
    [Fact]
    public void A_settled_spring_snaps_position_to_destination_and_velocity_to_zero_exactly()
    {
        foreach (var (start, destination) in new (double, double)[] { (100, 0), (5, 42), (-30, -7), (0, 250) })
        {
            var settled = BuildSettledSpring(start, destination, 0);
            // Settled means active rounds to 0, so position = select(0, destination, position) =
            // destination and velocity = multiply(0, velocity) = 0, both bit-exact.
            Js.Is(0, settled.Active);
            Js.Is(Js.F(destination), settled.Position);
            Js.Is(0, settled.Velocity);
        }
    }

    // 'emitSpring settled branch' / 'a spring released with initial velocity still settles exactly'.
    [Fact]
    public void A_spring_released_with_initial_velocity_still_settles_exactly()
    {
        var settled = BuildSettledSpring(5, 42, -3);
        Js.Is(0, settled.Active);
        Js.Is(42, settled.Position);
        Js.Is(0, settled.Velocity);
    }

    // Sharpwired: the cost that execution-model.md#in-the-program states for emitSpring, 46
    // operations in 33 layers for one spring, read from compileTensorProgram([position, velocity, active]).
    [Fact]
    public void Spring_costs_46_operations_in_33_layers()
    {
        var circuit = BuildSpringCircuit(out var spring);
        var counts = TensorChecks.ProgramCounts(circuit.CompileTensorProgram([spring.Position, spring.Velocity, spring.Active]));
        Assert.Equal((46, 33), (counts.LiveOperations, counts.Layers));
        circuit.ValidateCircuit();
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

    // 'a proven-nonnegative operand lowers emitMultiply to one reglu'.
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
        Js.Is(-6, circuit.EvaluateTensor([-3, 2])[product.Position]);
        Js.Throws(() => circuit.EvaluateTensor([-3, -2]), "outside its assumed range");
    }
}
