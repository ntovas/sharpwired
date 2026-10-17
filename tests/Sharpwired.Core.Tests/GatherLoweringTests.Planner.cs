// Handwired source: src/runtime/numeric-ui-core.ts (planGatherLowering, attemptGatherMatch,
// resolveAffineSide, hasFiniteRange). No Handwired test reaches these planner rules one by one.
// Each test builds a circuit by hand so that one rule decides the result. Each test asserts the
// gather statistics, and compares the values with the reference evaluator (expectExact).

namespace Sharpwired.Core.Tests;

public partial class GatherLoweringTests
{
    // planGatherLowering tries the groups of the gate wire of the first product first, and stops at
    // the first group that matches. Here each product a_k × b_k matches both groups: group B through
    // the gate wire and group A through the left wire. So the plan must read group B.
    [Fact]
    public void A_sum_that_matches_two_groups_reads_the_first_group_of_the_gate_wire()
    {
        // Cells 0-1: group A. Cells 2-3: group B. Cells 4-5: the values of a second read of group A.
        var circuit = new Circuit(6);
        Wire[] groupA = [circuit.Source(0), circuit.Source(1)];
        Wire[] groupB = [circuit.Source(2), circuit.Source(3)];
        Wire[] values = [circuit.Source(4), circuit.Source(5)];
        circuit.AssumeOneHot(groupA);
        circuit.AssumeOneHot(groupB);
        foreach (var value in values) circuit.AssumeRange(value, 0, 100);
        // Multiply(a, b) stores the macro with left a and right b, so the gate wire of each product is b.
        var crossRead = circuit.Affine(MaskedTerms(circuit, groupA, groupB));
        var valueRead = circuit.Affine(MaskedTerms(circuit, groupA, values));
        var (lowered, _) = CompileBoth(circuit, [crossRead, valueRead]);

        var statistics = TensorChecks.GatherStatistics(lowered);
        Assert.Equal(2, statistics.ReadCount);
        // The cross read resolves group B and the value read resolves group A. With group A for both
        // reads, the program has one compute only.
        Assert.Equal(2, statistics.ComputeCount);
        var crossCompute = Assert.Single(lowered.Layers.SelectMany(layer => layer.GatherComputes), compute => compute.BitPositions.Contains(2));
        Assert.Equal(new[] { 2, 3 }, crossCompute.BitPositions);

        foreach (var (slotA, slotB) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) })
        {
            var source = new float[6];
            source[slotA] = 1;
            source[2 + slotB] = 1;
            source[4] = 17.5f;
            source[5] = 23.25f;
            var reference = ExpectExact(circuit, lowered, source);
            Js.Is(slotA == slotB ? 1 : 0, reference[crossRead.Position]);
            Js.Is(source[4 + slotA], reference[valueRead.Position]);
        }
    }

    // attemptGatherMatch refuses a negative arm that no positive arm uses. The extra term
    // -f(b0 × max(0, -w)) is a negative arm of bit b0 with no partner. It is not zero for a negative
    // w, so the sum must stay dense.
    [Fact]
    public void A_negative_arm_with_no_positive_arm_keeps_the_sum_dense()
    {
        // Cells 0-1: the group. Cells 2-3: the values. Cell 4: the wire w behind the negated gate.
        var circuit = new Circuit(5);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] values = [circuit.Source(2), circuit.Source(3)];
        var other = circuit.Source(4);
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, 0, 100);
        circuit.AssumeRange(other, -10, 10);
        var unusedArm = circuit.Reglu([(bits[0], 1)], 0, [(other, -1)], 0);
        var read = circuit.Affine([.. MaskedTerms(circuit, bits, values), new Term(unusedArm, -1)]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, 7, -3]);
        // f(5) + f(0) - f(1 × max(0, 3)) = 2. A gather of the selected value gives 5.
        Js.Is(2, reference[read.Position]);
        ExpectExact(circuit, lowered, [0, 1, 5, 7, -3]);
    }

    // The key of a negative arm is the pair (left wire, gate wire). Here the positive arm
    // f(b0 × max(0, v)) and the negative arm f(b0 × max(0, -u)) have the same bit and different
    // values. So they are not a pair, and the sum must stay dense. A key of the left wire only makes
    // them a pair.
    [Fact]
    public void A_positive_arm_and_a_negative_arm_of_different_values_are_not_a_pair()
    {
        // Cells 0-1: the group. Cell 2: v. Cell 3: u. Cell 4: the value of bit 1.
        var circuit = new Circuit(5);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        var positiveValue = circuit.Source(2);
        var negativeValue = circuit.Source(3);
        var plainValue = circuit.Source(4);
        circuit.AssumeOneHot(bits);
        circuit.AssumeRange(positiveValue, -10, 10);
        circuit.AssumeRange(negativeValue, -10, 10);
        circuit.AssumeRange(plainValue, 0, 100);
        var positiveArm = circuit.Reglu([(bits[0], 1)], 0, [(positiveValue, 1)], 0);
        var negativeArm = circuit.Reglu([(bits[0], 1)], 0, [(negativeValue, -1)], 0);
        var read = circuit.Affine([(positiveArm, 1), (negativeArm, -1), (circuit.Multiply(bits[1], plainValue), 1)]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, -3, 7]);
        // f(1 × max(0, 5)) - f(1 × max(0, 3)) + f(0) = 2. A gather of v gives 5.
        Js.Is(2, reference[read.Position]);
        ExpectExact(circuit, lowered, [0, 1, 5, -3, 7]);
    }

    // A term whose bit can be on either side is flexible. attemptGatherMatch places the fixed terms
    // first. Then it gives each flexible term the first open position of its options. The product
    // b0 × b1 of two members has the options (position 1, b0) and (position 0, b1). The fixed term
    // b1 × v takes position 1, so the flexible term must take position 0.
    [Fact]
    public void A_flexible_term_takes_the_position_that_the_fixed_terms_leave_open()
    {
        // Cells 0-1: the group. Cell 2: the value.
        var circuit = new Circuit(3);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        var value = circuit.Source(2);
        circuit.AssumeOneHot(bits);
        circuit.AssumeRange(value, 0, 100);
        var memberProduct = circuit.Multiply(bits[0], bits[1]);
        var valueProduct = circuit.Multiply(bits[1], value);
        var read = circuit.Affine([(memberProduct, 1), (valueProduct, 1)]);
        Assert.Equal(new[] { memberProduct, valueProduct }, circuit.Operations[read.Position - circuit.SourceSize].LeftTerms.Select(term => term.Wire));
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        var gatherRead = Assert.Single(lowered.Layers.SelectMany(layer => layer.GatherReads));
        // The candidates are b1 at position 0 and v at position 1, at their packed positions 1 and 2.
        Assert.Equal(new[] { 1, 2 }, gatherRead.CandidatePositions);

        var reference = ExpectExact(circuit, lowered, [1, 0, 42]);
        Js.Is(0, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 42]);
        Js.Is(42, reference[read.Position]);
    }

    // The bit in the gate reads the left value only when the declared range of the value is finite.
    // Multiply(value, bit) puts the bit in the gate. For an unknown range, a product that the group
    // does not select can be f(Infinity × 0) = NaN, so the sum must stay dense.
    [Fact]
    public void The_bit_in_the_gate_needs_a_finite_range_on_the_value()
    {
        // Cells 0-1: the group. Cells 2-3: the values, with unknown ranges.
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] values = [circuit.Source(2), circuit.Source(3)];
        circuit.AssumeOneHot(bits);
        var read = circuit.Affine([.. values.Select((value, slot) => new Term(circuit.Multiply(value, bits[slot]), 1))]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, float.PositiveInfinity]);
        Js.Is(double.NaN, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, -7]);
        Js.Is(-7, reference[read.Position]);
    }

    // The positive arm of a mixed-sign pair reads the value only when the declared range of the
    // value is finite. For an unknown range, an arm that the group does not select can be
    // f(0 × Infinity) = NaN, so the sum must stay dense.
    [Fact]
    public void The_mixed_sign_pair_needs_a_finite_range_on_the_value()
    {
        // Cells 0-1: the group. Cells 2-3: the values, with unknown ranges.
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] values = [circuit.Source(2), circuit.Source(3)];
        circuit.AssumeOneHot(bits);
        var read = circuit.Affine([.. HandWrittenPairs(circuit, bits, values)]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, float.PositiveInfinity]);
        Js.Is(double.NaN, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, -7]);
        Js.Is(-7, reference[read.Position]);
    }

    // The same hand-written pairs with finite ranges lower. This proves that the finite range is
    // the only rule that keeps the sum of the test above dense.
    [Fact]
    public void The_mixed_sign_pair_lowers_for_finite_ranges()
    {
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] values = [circuit.Source(2), circuit.Source(3)];
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, -100, 100);
        var read = circuit.Affine([.. HandWrittenPairs(circuit, bits, values)]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, -7]);
        Js.Is(5, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, -7]);
        Js.Is(-7, reference[read.Position]);
    }

    // HasFiniteRange rounds each bound to FP32 before the test. The bound 3.5e38 is finite in
    // double, but it rounds to Infinity in FP32, so the range admits an FP32 value of Infinity. Here
    // each value is f(s + 2^104). For s at the largest FP32 value, the sum rounds to Infinity. The
    // declared range 0..3.5e38 admits that sum in real numbers. The masked sum must stay dense.
    [Fact]
    public void A_range_bound_that_rounds_to_infinity_in_FP32_is_not_finite()
    {
        // Cells 0-1: the group. Cells 2-3: the inputs of the values.
        const double LargeBound = 3.5e38;
        // 2^104 is above the half step of the FP32 values near the largest value (2^103).
        var offset = Math.Pow(2, 104);
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] inputs = [circuit.Source(2), circuit.Source(3)];
        circuit.AssumeOneHot(bits);
        foreach (var input in inputs) circuit.AssumeRange(input, 0, float.MaxValue);
        var values = inputs.Select(input => circuit.RoundAffine([(input, 1)], offset)).ToArray();
        foreach (var value in values) circuit.AssumeRange(value, 0, LargeBound);
        Assert.Equal(LargeBound, circuit.Ranges[values[0].Position].Maximum);
        Assert.True(double.IsFinite(circuit.Ranges[values[0].Position].Maximum));
        Assert.Equal(float.PositiveInfinity, (float)circuit.Ranges[values[0].Position].Maximum);
        // Multiply(value, bit) puts the bit in the gate, so the finite range of the value decides.
        var read = circuit.Affine([.. values.Select((value, slot) => new Term(circuit.Multiply(value, bits[slot]), 1))]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, float.MaxValue]);
        Js.Is(float.PositiveInfinity, reference[values[1].Position]);
        Js.Is(double.NaN, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, 7]);
        Js.Is(Js.F(7 + offset), reference[read.Position]);
    }

    // ResolveAffineSide finds a multi-term wire through the expression cache. The builder copies the
    // two-term affine w = s0 + s1 into each ReGLU that reads it. So the bit-in-gate product
    // f(w × max(0, bit)) stores the terms of w. The direct look-up gives w back.
    [Fact]
    public void The_expression_cache_gives_back_a_copied_multi_term_value()
    {
        // Cells 0-1: the group. Cells 2-5: the terms of the two values.
        var circuit = new Circuit(6);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        for (var cell = 2; cell < 6; cell++) circuit.AssumeRange(circuit.Source(cell), -100, 100);
        circuit.AssumeOneHot(bits);
        Wire[] values =
        [
            circuit.Affine([(circuit.Source(2), 1), (circuit.Source(3), 1)]),
            circuit.Affine([(circuit.Source(4), 1), (circuit.Source(5), 1)]),
        ];
        var products = values.Select((value, slot) => circuit.Reglu([(value, 1)], 0, [(bits[slot], 1)], 0)).ToArray();
        // The product stores the two terms of the value, not the value wire.
        Assert.Equal(2, circuit.Operations[products[0].Position - circuit.SourceSize].LeftTerms.Length);
        var read = circuit.Affine([.. products.Select(product => new Term(product, 1))]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        var gatherRead = Assert.Single(lowered.Layers.SelectMany(layer => layer.GatherReads));
        Assert.Equal(values.Select(value => IndexOf(lowered.WireOrder, value.Position)), gatherRead.CandidatePositions);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, -2, 7, 1]);
        Js.Is(3, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, -2, 7, 1]);
        Js.Is(8, reference[read.Position]);
    }

    // ResolveAffineSide finds the negation of a multi-term wire through the expression cache. Take
    // the hand-written pair of the value w = s0 + s1. Its negative arm f(bit × max(0, -w)) stores
    // the gate terms -s0 - s1. The negated look-up gives w back with sign -1.
    [Fact]
    public void The_expression_cache_gives_back_a_negated_copied_multi_term_value()
    {
        // Cells 0-1: the group. Cells 2-5: the terms of the two values.
        var circuit = new Circuit(6);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        for (var cell = 2; cell < 6; cell++) circuit.AssumeRange(circuit.Source(cell), -100, 100);
        circuit.AssumeOneHot(bits);
        Wire[] values =
        [
            circuit.Affine([(circuit.Source(2), 1), (circuit.Source(3), 1)]),
            circuit.Affine([(circuit.Source(4), 1), (circuit.Source(5), 1)]),
        ];
        var terms = HandWrittenPairs(circuit, bits, values);
        // The negative arm stores the negated terms of the value.
        var negativeArm = circuit.Operations[terms[1].Wire.Position - circuit.SourceSize];
        Assert.Equal(new[] { -1.0, -1.0 }, negativeArm.GateTerms.Select(term => term.Coefficient));
        var read = circuit.Affine(terms);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        var reference = ExpectExact(circuit, lowered, [1, 0, 5, -12, 7, 1]);
        Js.Is(-7, reference[read.Position]);
        reference = ExpectExact(circuit, lowered, [0, 1, 5, -12, 7, 1]);
        Js.Is(8, reference[read.Position]);
    }

    // The mixed-sign pair of each slot, written by hand: f(bit × max(0, value)) - f(bit × max(0, -value)).
    // Multiply stores its macro on the difference, so the planner reads it as one exact product. The
    // hand-written arms have no macro, so the planner must match each pair through its negative arm.
    private static Term[] HandWrittenPairs(Circuit circuit, IReadOnlyList<Wire> bits, IReadOnlyList<Wire> values) =>
    [
        .. bits.SelectMany((bit, slot) => new[]
        {
            new Term(circuit.Reglu([(bit, 1)], 0, [(values[slot], 1)], 0), 1),
            new Term(circuit.Reglu([(bit, 1)], 0, [(values[slot], -1)], 0), -1),
        }),
    ];

    private static int IndexOf(UnsignedIndices wireOrder, int wire) => wireOrder.ToList().IndexOf(wire);
}
