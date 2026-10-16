// Handwired source: tests/runtime/numeric-semantics.test.ts.
// Each test name holds the Handwired test name. A test that compares with the
// tensor evaluator keeps its reference-evaluator part here. PHASE2-TENSOR-ASSERTIONS.md
// lists the tensor assertions.

namespace Sharpwired.Core.Tests;

public class NumericSemanticsTests
{
    // 'explicit rounding survives consumers, duplicate terms, and circuit inlining' (reference part).
    [Fact]
    public void Explicit_rounding_survives_consumers_duplicate_terms_and_circuit_inlining()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var difference = circuit.Affine([(sum, 1), (circuit.Source(0), -1)]);
        var duplicate = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(0), 1), (circuit.Source(0), 1)]);
        var parent = new Circuit(2);
        var mapping = parent.InlineCircuit(circuit, [parent.Source(0), parent.Source(1)]);
        float[] source = [16_777_216, 1];

        var values = circuit.EvaluateReference(source);
        Js.Is(0, values[difference.Position]);
        Js.Is(Js.F(Js.F(source[0] + source[0]) + source[0]), values[duplicate.Position]);
        Assert.Equal(3, circuit.Operations[duplicate.Position - circuit.SourceSize].LeftTerms.Length);
        Js.Is(0, parent.EvaluateReference(source)[mapping[difference.Position].Position]);
    }

    // 'explicit term order survives source permutation during inlining' (reference part).
    [Fact]
    public void Explicit_term_order_survives_source_permutation_during_inlining()
    {
        var circuit = new Circuit(3);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1)]);
        var parent = new Circuit(3);
        var mapping = parent.InlineCircuit(circuit, [parent.Source(0), parent.Source(2), parent.Source(1)]);
        float[] original = [16_777_216, 1, -16_777_216];
        float[] permuted = [16_777_216, -16_777_216, 1];
        Js.Is(0, circuit.EvaluateReference(original)[sum.Position]);
        Js.Is(0, parent.EvaluateReference(permuted)[mapping[sum.Position].Position]);
        var reordered = parent.RoundAffine([(parent.Source(0), 1), (parent.Source(1), 1), (parent.Source(2), 1)]);
        Assert.NotEqual(mapping[sum.Position], reordered);
        Js.Is(1, parent.EvaluateReference(permuted)[reordered.Position]);
    }

    // 'range bounds include rounded endpoints and FP32 overflow'.
    [Fact]
    public void Range_bounds_include_rounded_endpoints_and_FP32_overflow()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1 - Math.Pow(2, -24));
        circuit.AssumeRange(circuit.Source(1), 0, Math.Pow(2, -25));
        circuit.AssumeRange(circuit.Source(2), 0, 3.4e38);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var overflow = circuit.RoundAffine([(circuit.Source(2), 2)]);
        var zero = circuit.RoundAffine([], 0);
        var poisoned = circuit.Append(OperationKind.Reglu, [(overflow, 1)], 0, [(zero, 1)], 0, preserveRounding: true);

        Js.Is(1, circuit.Ranges[sum.Position].Maximum);
        Js.Is(double.PositiveInfinity, circuit.Ranges[overflow.Position].Maximum);
        Js.Is(double.PositiveInfinity, circuit.Ranges[poisoned.Position].Maximum);
    }

    // 'overflowing unselected candidates do not lower to gather' (reference part).
    [Fact]
    public void Overflowing_unselected_candidates_give_NaN_in_the_reference_evaluator()
    {
        var circuit = new Circuit(4);
        circuit.AssumeOneHot([circuit.Source(0), circuit.Source(1)]);
        circuit.AssumeRange(circuit.Source(2), 5, 5);
        circuit.AssumeRange(circuit.Source(3), 0, 3.4e38);
        var twice = circuit.RoundAffine([(circuit.Source(3), 2)]);
        var output = circuit.Affine([
            (circuit.Multiply(circuit.Source(2), circuit.Source(0)), 1),
            (circuit.Multiply(twice, circuit.Source(1)), 1),
        ]);

        Assert.True(float.IsNaN(circuit.EvaluateReference([1, 0, 5, 3e38f])[output.Position]));
    }

    // 'min and max ranges describe their arithmetic, not mathematical bounds' (reference part).
    [Fact]
    public void Min_and_max_ranges_describe_their_arithmetic_not_mathematical_bounds()
    {
        foreach (var emit in new Func<Circuit, Wire, Wire, Wire>[] { Macros.Minimum, Macros.Maximum })
        {
            var circuit = new Circuit(5);
            circuit.AssumeOneHot([circuit.Source(0), circuit.Source(1)]);
            circuit.AssumeRange(circuit.Source(2), 5, 5);
            circuit.AssumeRange(circuit.Source(3), -3.4e38, 3.4e38);
            circuit.AssumeRange(circuit.Source(4), -3.4e38, 3.4e38);
            var extreme = emit(circuit, circuit.Source(3), circuit.Source(4));
            var output = circuit.RoundAffine([
                (circuit.Multiply(circuit.Source(2), circuit.Source(0)), 1),
                (circuit.Multiply(extreme, circuit.Source(1)), 1),
            ]);
            Assert.True(float.IsNaN(circuit.EvaluateReference([1, 0, 5, 3e38f, -3e38f])[output.Position]));
        }
        var unit = new Circuit(2);
        unit.AssumeRange(unit.Source(0), 1, 1);
        unit.AssumeRange(unit.Source(1), Math.Pow(2, -25), Math.Pow(2, -25));
        var minimum = unit.Minimum(unit.Source(0), unit.Source(1));
        var value = unit.EvaluateReference([1, MathF.Pow(2, -25)])[minimum.Position];
        Js.Is(0, value);
        Assert.True(unit.Ranges[minimum.Position].Minimum <= value);
    }

    // 'the weaker min and max bounds hold across unit-interval FP32 values'.
    [Fact]
    public void The_weaker_min_and_max_bounds_hold_across_unit_interval_FP32_values()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeRange(circuit.Source(1), 0, 1);
        var minimum = circuit.Minimum(circuit.Source(0), circuit.Source(1));
        var maximum = circuit.Maximum(circuit.Source(0), circuit.Source(1));
        uint[] edges = [0, 1, 0x007fffff, 0x00800000, 0x33000000, 0x3effffff, 0x3f000000, 0x3f000001, 0x3f7fffff, 0x3f800000];
        void Check(uint left, uint right)
        {
            var values = circuit.EvaluateReference([BitConverter.UInt32BitsToSingle(left), BitConverter.UInt32BitsToSingle(right)]);
            foreach (var wire in new[] { minimum, maximum })
            {
                Assert.True(values[wire.Position] >= circuit.Ranges[wire.Position].Minimum);
                Assert.True(values[wire.Position] <= circuit.Ranges[wire.Position].Maximum);
            }
        }
        foreach (var left in edges)
        {
            foreach (var right in edges) Check(left, right);
        }
        var seed = 317u;
        var words = new uint[2];
        for (var sample = 0; sample < 5_000; sample++)
        {
            for (var index = 0; index < 2; index++)
            {
                seed = unchecked(seed * 1_664_525 + 1_013_904_223);
                words[index] = seed % 0x3f800001;
            }
            Check(words[0], words[1]);
        }
    }

    // 'selection preserves finite extreme operands and its intended derivative' (reference values).
    [Fact]
    public void Selection_preserves_finite_extreme_operands()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var output = circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2));
        foreach (var gate in new float[] { 0, 1 })
        {
            float[] source = [gate, 3e38f, -3e38f];
            Js.Is(source[gate == 0 ? 1 : 2], circuit.EvaluateReference(source)[output.Position]);
        }
    }

    // 'source cells, constants and hand-counted positions become wires only through checked helpers'.
    // Handwired also refuses asWire(0.5). Wire.FromPosition takes an int, so the type refuses it.
    [Fact]
    public void Source_cells_constants_and_hand_counted_positions_become_wires_only_through_checked_helpers()
    {
        var circuit = new Circuit(2);
        Assert.Equal(1, circuit.Source(1).Position);
        Js.Throws(() => circuit.Source(2), "outside the source table");
        Js.Throws(() => circuit.Source(-1), "outside the source table");
        Js.Throws(() => Wire.FromPosition(-1), "nonnegative integer");
        Assert.Equal(circuit.Constant(3), circuit.Affine([], 3));
        Assert.Equal(3, circuit.Constants[circuit.Constant(3).Position]);
    }

    // 'a range declared after operations read the wire throws, unless the caller opts in'.
    [Fact]
    public void A_range_declared_after_operations_read_the_wire_throws_unless_the_caller_opts_in()
    {
        var circuit = new Circuit(2);
        var velocity = circuit.Source(0);
        var timestep = circuit.Source(1);
        circuit.AssumeRange(velocity, -100, 100);
        var distance = circuit.Multiply(velocity, timestep);
        // The product was built for a timestep that may be negative: 3 operations.
        Assert.Equal(3, circuit.Operations.Count);
        Js.Throws(() => circuit.AssumeRange(timestep, 0, 0.032), "already read wire 1");
        // Declaring the range it already has, or a wider one, changes nothing and doesn't throw.
        circuit.AssumeRange(velocity, -1000, 1000);
        circuit.AssumeRange(timestep, 0, 0.032, allowExistingReaders: true);
        Js.Is(0, circuit.Ranges[timestep.Position].Minimum);
        Assert.Equal(OperationKind.Affine, circuit.Operations[distance.Position - circuit.SourceSize].Kind);
        // A source circuit's declared ranges that land on target wires that already have readers.
        var child = new Circuit(1);
        child.AssumeRange(child.Source(0), 0, 1);
        var parent = new Circuit(1);
        parent.Multiply(parent.Source(0), parent.Source(0));
        Js.Throws(() => parent.InlineCircuit(child, [parent.Source(0)]), "already read wire 0");
        parent.InlineCircuit(child, [parent.Source(0)], allowExistingReaders: true);
        Js.Is(1, parent.Ranges[0].Maximum);
    }

    // 'a grid declared after operations read the wire throws, unless the caller opts in'.
    [Fact]
    public void A_grid_declared_after_operations_read_the_wire_throws_unless_the_caller_opts_in()
    {
        var circuit = new Circuit(2);
        var pointerX = circuit.Source(0);
        var windowLeft = circuit.Source(1);
        circuit.AssumeRange(pointerX, 0, 1024);
        circuit.AssumeRange(windowLeft, 0, 1024);
        circuit.AssumeGrid(windowLeft, 1);
        // pointerX has no grid, so Compare builds a sign cascade, and declaring the grid afterwards doesn't turn it into integer bits.
        circuit.Compare(pointerX, Relation.GreaterOrEqual, windowLeft);
        Js.Throws(() => circuit.AssumeGrid(pointerX, 1.0 / 64), "already read wire 0 were built without a grid");
        // A finer grid than the wire already has changes nothing and doesn't throw.
        circuit.AssumeGrid(windowLeft, 1.0 / 64);
        circuit.AssumeGrid(pointerX, 1.0 / 64, allowExistingReaders: true);
        Assert.Equal(1.0 / 64, circuit.Grids[pointerX.Position]);
        Assert.Equal(1.0, circuit.Grids[windowLeft.Position]);
    }

    // 'a declaration after operations that folded to constants or dropped a zero coefficient does not throw, since nothing reads the wire'.
    [Fact]
    public void A_declaration_after_operations_that_folded_to_constants_or_dropped_a_zero_coefficient_does_not_throw()
    {
        var circuit = new Circuit(3);
        var x = circuit.Source(0);
        var y = circuit.Source(1);
        var z = circuit.Source(2);
        // x * max(0, -1), which is 0 for every x, so the operation is the constant 0 and doesn't read x.
        circuit.Reglu([(x, 1)], 0, [], -1);
        // A term with a zero coefficient is dropped, so the operation is the constant 5.
        circuit.Affine([(y, 0)], 5);
        // z * 2 + y * 0: the zero coefficient is dropped next to a live term, so the operation only reads z.
        circuit.Affine([(z, 2), (y, 0)]);
        circuit.AssumeRange(x, 0, 1);
        circuit.AssumeGrid(x, 1);
        circuit.AssumeRange(y, 0, 1);
        circuit.Affine([(y, 1), (x, 2)]);
        Js.Throws(() => circuit.AssumeRange(x, 0, 0.5), "already read wire 0");
    }
    // 'validateNoNaN names the select whose unpicked side overflowed'.
    [Fact]
    public void Validate_no_NaN_names_the_select_whose_unpicked_side_overflowed()
    {
        var circuit = new Circuit(3);
        var (gate, off, on) = (circuit.Source(0), circuit.Source(1), circuit.Source(2));
        circuit.AssumeRange(gate, 0, 1);
        circuit.AssumeRange(off, 0, 3.4e38);
        circuit.AssumeRange(on, -100, 100);
        var doubledOff = circuit.RoundAffine([(off, 2)]);
        var output = circuit.Select(gate, doubledOff, on);
        circuit.ValidateNoNaN(circuit.EvaluateReference([1, 3, 7]));
        var overflowing = circuit.EvaluateReference([1, 3e38f, 7]);
        Assert.True(float.IsNaN(overflowing[output.Position]));
        var exception = Js.Throws(() => circuit.ValidateNoNaN(overflowing), "wire 6 is the first NaN");
        Assert.Matches(
            @"wire 6 is the first NaN: f\(left × max\(0, gate\)\) with left = 0 and gate = Infinity, in multiply\(wire 4 = 0, wire 3 = Infinity\), in select\(gate wire 0 = 1, off wire 3 = Infinity, on wire 2 = 7\)",
            exception.Message);
    }

    // 'validateNoNaN with roots checks only the wires they read, and a NaN operand of a select or multiply gets no context'.
    [Fact]
    public void Validate_no_NaN_with_roots_checks_only_the_wires_they_read_and_a_NaN_operand_of_a_select_or_multiply_gets_no_context()
    {
        var circuit = new Circuit(3);
        var (gate, width, scale) = (circuit.Source(0), circuit.Source(1), circuit.Source(2));
        circuit.AssumeRange(gate, 0, 1);
        circuit.AssumeRange(width, 0, 3.4e38);
        circuit.AssumeRange(scale, 0, 2);
        var doubled = circuit.RoundAffine([(width, 2)]);
        // The side the gate doesn't pick overflows, so this select is NaN, and the roots below don't read it.
        var unused = circuit.Select(gate, doubled, circuit.Constant(7));
        // JS: width * 2 + width * -2, which is Infinity - Infinity at width 3e38.
        var negated = circuit.RoundAffine([(width, -2)]);
        var difference = circuit.RoundAffine([(doubled, 1), (negated, 1)]);
        var picked = circuit.Select(gate, circuit.Constant(7), difference);
        var scaled = circuit.Multiply(difference, scale);
        var values = circuit.EvaluateReference([1, 3e38f, 1]);
        Assert.All(new[] { unused, picked, scaled }, wire => Assert.True(float.IsNaN(values[wire.Position])));
        var all = Js.Throws(() => circuit.ValidateNoNaN(values), "is the first NaN");
        Assert.Matches(@"with left = 0 and gate = Infinity, in multiply\(.*\), in select\(", all.Message);
        // The NaN comes from the sum on the side the gate picks, which is an operand of the select and of the multiply.
        var differenceMessage = $"^wire {difference} is the first NaN: a sum of wire {doubled} = Infinity, wire {negated} = -Infinity$";
        Assert.Matches(differenceMessage, Js.Throws(() => circuit.ValidateNoNaN(values, [picked]), "first NaN").Message);
        Assert.Matches(differenceMessage, Js.Throws(() => circuit.ValidateNoNaN(values, [scaled]), "first NaN").Message);
        circuit.ValidateNoNaN(values, [scale]);
    }
}
