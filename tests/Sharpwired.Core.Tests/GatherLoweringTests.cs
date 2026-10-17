// Handwired source: tests/runtime/gather-lowering.test.ts, the 'recognition' block. Each test name
// holds the Handwired test name. GatherLoweringTests.Magnitudes.cs holds 'adversarial value
// magnitudes', 'library circuits' and the replacement of the 'HandwiredOS' test.
// GatherLoweringTests.Seeded.cs holds 'seeded circuits'.

namespace Sharpwired.Core.Tests;

public partial class GatherLoweringTests
{
    // Handwired compileBoth: compile with and without gather lowering, and validate both programs.
    private static (TensorProgram Lowered, TensorProgram Plain) CompileBoth(Circuit circuit, IReadOnlyList<Wire>? roots = null)
    {
        var lowered = circuit.CompileTensorProgram(roots, lowerGathers: true);
        var plain = circuit.CompileTensorProgram(roots, lowerGathers: false);
        circuit.ValidateTensorProgram(lowered);
        circuit.ValidateTensorProgram(plain);
        return (lowered, plain);
    }

    // Handwired expectExact: run the program with EvaluateTensor, and compare each wire of WireOrder
    // with the reference value under the agreement standard sameNumericValue. Return the reference values.
    private static float[] ExpectExact(Circuit circuit, TensorProgram program, float[] source)
    {
        var reference = circuit.EvaluateReference(source);
        circuit.TensorProgram = program;
        var tensor = circuit.EvaluateTensor(source);
        foreach (var wire in program.WireOrder)
        {
            Assert.True(
                Js.SameNumericValue(tensor[wire], reference[wire]),
                $"wire {wire}: tensor {tensor[wire]:R} does not match reference {reference[wire]:R}");
        }
        return reference;
    }

    // Handwired buildBasicRead: slotCount bits declared one-hot, slotCount nonnegative values, and one
    // masked sum that reads the selected value.
    private static (Circuit Circuit, Wire[] Bits, Wire[] Values, Wire Read) BuildBasicRead(int slotCount, double bias = 0)
    {
        var circuit = new Circuit(slotCount * 2);
        var bits = Enumerable.Range(0, slotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(slotCount, slotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
        var read = circuit.Affine([.. bits.Select((bit, slot) => new Term(circuit.Multiply(bit, values[slot]), 1))], bias);
        return (circuit, bits, values, read);
    }

    // Handwired basicSource: the selected bit (none for -1), then the values f(10.5 + slot × 3.25).
    private static float[] BasicSource(int slotCount, int selectedSlot)
    {
        var source = new float[slotCount * 2];
        if (selectedSlot >= 0) source[selectedSlot] = 1;
        for (var slot = 0; slot < slotCount; slot++) source[slotCount + slot] = (float)(10.5 + slot * 3.25);
        return source;
    }

    private static Term[] MaskedTerms(Circuit circuit, IReadOnlyList<Wire> bits, IReadOnlyList<Wire> values) =>
        [.. bits.Select((bit, slot) => new Term(circuit.Multiply(bit, values[slot]), 1))];

    // 'recognition' / 'a one-hot-masked sum lowers to one gather and drops its products'.
    [Fact]
    public void A_one_hot_masked_sum_lowers_to_one_gather_and_drops_its_products()
    {
        const int SlotCount = 8;
        var (circuit, _, _, read) = BuildBasicRead(SlotCount);
        var (lowered, plain) = CompileBoth(circuit, [read]);

        var loweredStats = TensorChecks.GatherStatistics(lowered);
        var plainStats = TensorChecks.GatherStatistics(plain);
        Assert.Equal(1, loweredStats.ReadCount);
        Assert.Equal(1, loweredStats.SlotCount);
        // The dense form stores 2 weights for each product and 1 for each term of the sum.
        Assert.Equal(3 * SlotCount, plainStats.StoredWeightCount);
        // The gather row stores no weights, and the products leave the live set: only the sum
        // stays after the source cells.
        Assert.Equal(0, loweredStats.StoredWeightCount);
        Assert.Equal(circuit.SourceSize + 1, lowered.ValueCount);

        for (var selectedSlot = 0; selectedSlot < SlotCount; selectedSlot++)
        {
            var source = BasicSource(SlotCount, selectedSlot);
            var reference = ExpectExact(circuit, lowered, source);
            ExpectExact(circuit, plain, source);
            Js.Is(source[SlotCount + selectedSlot], reference[read.Position]);
        }
    }

    // 'recognition' / 'a bias on the masked sum survives lowering exactly'.
    [Fact]
    public void A_bias_on_the_masked_sum_survives_lowering_exactly()
    {
        var (circuit, _, _, read) = BuildBasicRead(4, 0.1);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        for (var selectedSlot = 0; selectedSlot < 4; selectedSlot++)
        {
            var source = BasicSource(4, selectedSlot);
            var reference = ExpectExact(circuit, lowered, source);
            Js.Is(Js.F(Js.F(0.1) + source[4 + selectedSlot]), reference[read.Position]);
        }
    }

    // 'recognition' / 'signed values lower through the mixed-sign product pair'.
    [Fact]
    public void Signed_values_lower_through_the_mixed_sign_product_pair()
    {
        // The group is declared after the products, so the ranges of the bits are unknown when
        // Multiply runs. So Multiply builds its mixed-sign form: two ReGLU arms and their difference.
        const int SlotCount = 6;
        var circuit = new Circuit(SlotCount * 2);
        var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(SlotCount, SlotCount).Select(circuit.Source).ToArray();
        foreach (var value in values) circuit.AssumeRange(value, -4096, 4096);
        var read = circuit.Affine(MaskedTerms(circuit, bits, values));
        circuit.AssumeOneHot(bits, allowAllZero: false, allowExistingReaders: true);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        for (var selectedSlot = 0; selectedSlot < SlotCount; selectedSlot++)
        {
            var source = new float[SlotCount * 2];
            source[selectedSlot] = 1;
            for (var slot = 0; slot < SlotCount; slot++) source[SlotCount + slot] = (float)((slot % 2 == 0 ? -1 : 1) * (20.75 + slot));
            var reference = ExpectExact(circuit, lowered, source);
            Js.Is(source[SlotCount + selectedSlot], reference[read.Position]);
        }
    }

    // 'recognition' / 'the bit-in-gate orientation lowers for values that may be negative'.
    [Fact]
    public void The_bit_in_gate_orientation_lowers_for_values_that_may_be_negative()
    {
        // The bits are declared before the products, so for a signed value Multiply puts the bit in
        // the gate: f(value × max(0, bit)).
        const int SlotCount = 5;
        var circuit = new Circuit(SlotCount * 2);
        var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(SlotCount, SlotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, -1e6, 1e6);
        var read = circuit.Affine(MaskedTerms(circuit, bits, values));
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        var source = new float[SlotCount * 2];
        source[2] = 1;
        for (var slot = 0; slot < SlotCount; slot++) source[SlotCount + slot] = (float)(-1000.5 + slot * 7.75);
        var reference = ExpectExact(circuit, lowered, source);
        Js.Is(source[SlotCount + 2], reference[read.Position]);
    }

    // 'recognition' / 'sums that are not pure masked reads stay dense and stay exact'.
    [Fact]
    public void Sums_that_are_not_pure_masked_reads_stay_dense_and_stay_exact()
    {
        const int SlotCount = 4;
        (Circuit Circuit, Wire Read) Build(Action<Circuit, List<Term>> mutate)
        {
            var circuit = new Circuit(SlotCount * 2 + 1);
            var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
            var values = Enumerable.Range(SlotCount, SlotCount).Select(circuit.Source).ToArray();
            circuit.AssumeOneHot(bits);
            foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
            circuit.AssumeRange(circuit.Source(SlotCount * 2), 0, 4096);
            var terms = MaskedTerms(circuit, bits, values).ToList();
            mutate(circuit, terms);
            return (circuit, circuit.Affine(terms));
        }

        (string Label, Action<Circuit, List<Term>> Mutate)[] cases =
        [
            ("a scaled term", (_, terms) => terms[1] = terms[1] with { Coefficient = 2 }),
            ("a missing member product", (_, terms) => terms.RemoveAt(terms.Count - 1)),
            ("an extra unrelated term", (circuit, terms) => terms.Add(new Term(circuit.Affine([(circuit.Source(SlotCount * 2), 1)], 5), 1))),
        ];
        foreach (var (label, mutate) in cases)
        {
            var (circuit, read) = Build(mutate);
            var (lowered, _) = CompileBoth(circuit, [read]);
            Assert.True(TensorChecks.GatherStatistics(lowered).ReadCount == 0, label);
            var source = new float[SlotCount * 2 + 1];
            source[1] = 1;
            for (var slot = 0; slot < SlotCount; slot++) source[SlotCount + slot] = 3 + slot;
            source[SlotCount * 2] = 11;
            ExpectExact(circuit, lowered, source);
        }
    }

    // 'recognition' / 'a sum mixing products of two different groups stays dense'.
    [Fact]
    public void A_sum_mixing_products_of_two_different_groups_stays_dense()
    {
        var circuit = new Circuit(8);
        circuit.AssumeOneHot([circuit.Source(0), circuit.Source(1)]);
        circuit.AssumeOneHot([circuit.Source(2), circuit.Source(3)]);
        for (var value = 4; value < 8; value++) circuit.AssumeRange(circuit.Source(value), 0, 100);
        // Bit 0 reads value 4, bit 1 value 5, bit 2 value 6 and bit 3 value 7.
        var read = circuit.Affine([.. new[] { 0, 1, 2, 3 }.Select(bit => new Term(circuit.Multiply(circuit.Source(bit), circuit.Source(4 + bit)), 1))]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);
        ExpectExact(circuit, lowered, [1, 0, 0, 1, 5, 6, 7, 8]);
    }

    // 'recognition' / 'a hand-written bit-in-left product over a signed value is rejected'.
    [Fact]
    public void A_hand_written_bit_in_left_product_over_a_signed_value_is_rejected()
    {
        // f(bit × max(0, value)) reads the value only when the ReLU is the identity. For a value that
        // can be negative the sum must stay dense.
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        Wire[] values = [circuit.Source(2), circuit.Source(3)];
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, -100, 100);
        var read = circuit.Affine([.. bits.Select((bit, slot) => new Term(circuit.Reglu([(bit, 1)], 0, [(values[slot], 1)], 0), 1))]);
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);
        ExpectExact(circuit, lowered, [0, 1, 7, -9]);
    }

    // 'recognition' / 'a size-1 group lowers its biased read and aliases its bare read'.
    [Fact]
    public void A_size_1_group_lowers_its_biased_read_and_aliases_its_bare_read()
    {
        var circuit = new Circuit(2);
        var bit = circuit.Source(0);
        var value = circuit.Source(1);
        circuit.AssumeOneHot([bit]);
        circuit.AssumeRange(value, 0, 100);
        var product = circuit.Multiply(bit, value);
        // The bare sum is an alias of the product, so it has no sum operation to lower. A bias
        // makes an operation.
        var bareRead = circuit.Affine([(product, 1)]);
        Assert.Equal(product, bareRead);
        var biasedRead = circuit.Affine([(product, 1)], 2);
        var (lowered, _) = CompileBoth(circuit, [bareRead, biasedRead]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        var reference = ExpectExact(circuit, lowered, [1, 42]);
        Js.Is(44, reference[biasedRead.Position]);
    }

    // 'recognition' / 'many reads against one group share a single index resolution'.
    [Fact]
    public void Many_reads_against_one_group_share_a_single_index_resolution()
    {
        const int SlotCount = 8;
        const int ReadCount = 5;
        var circuit = new Circuit(SlotCount + SlotCount * ReadCount);
        var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        var reads = new List<Wire>();
        for (var field = 0; field < ReadCount; field++)
        {
            var values = Enumerable.Range(SlotCount + field * SlotCount, SlotCount).Select(circuit.Source).ToArray();
            foreach (var value in values) circuit.AssumeRange(value, 0, 1e6);
            reads.Add(circuit.Affine(MaskedTerms(circuit, bits, values)));
        }
        var (lowered, _) = CompileBoth(circuit, reads);
        var statistics = TensorChecks.GatherStatistics(lowered);
        Assert.Equal(ReadCount, statistics.ReadCount);
        Assert.Equal(1, statistics.ComputeCount);
        Assert.Equal(1, statistics.SlotCount);

        var source = new float[circuit.SourceSize];
        source[3] = 1;
        for (var cell = SlotCount; cell < circuit.SourceSize; cell++) source[cell] = (float)(cell * 1.5);
        ExpectExact(circuit, lowered, source);
    }

    // 'recognition' / 'identical reads deduplicate to one wire and one gather'.
    [Fact]
    public void Identical_reads_deduplicate_to_one_wire_and_one_gather()
    {
        var (circuit, bits, values, read) = BuildBasicRead(4);
        var duplicate = circuit.Affine(MaskedTerms(circuit, bits, values));
        Assert.Equal(read, duplicate);
        var (lowered, _) = CompileBoth(circuit, [read, duplicate]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
    }

    // 'recognition' / 'a gather output can be the candidate of another gather'.
    [Fact]
    public void A_gather_output_can_be_the_candidate_of_another_gather()
    {
        // The first gather picks a value, and that value is a candidate of the second group. The
        // first gather output equals its sum bit for bit, so the chain stays exact.
        var circuit = new Circuit(4 + 4 + 2 + 1);
        var firstBits = new[] { 0, 1, 2, 3 }.Select(circuit.Source).ToArray();
        var firstValues = new[] { 4, 5, 6, 7 }.Select(circuit.Source).ToArray();
        Wire[] secondBits = [circuit.Source(8), circuit.Source(9)];
        var plainValue = circuit.Source(10);
        circuit.AssumeOneHot(firstBits);
        circuit.AssumeOneHot(secondBits);
        foreach (var value in firstValues) circuit.AssumeRange(value, 0, 1000);
        circuit.AssumeRange(plainValue, 0, 1000);
        var firstRead = circuit.Affine(MaskedTerms(circuit, firstBits, firstValues));
        var secondRead = circuit.Affine([
            (circuit.Multiply(secondBits[0], firstRead), 1),
            (circuit.Multiply(secondBits[1], plainValue), 1),
        ]);
        var (lowered, _) = CompileBoth(circuit, [secondRead]);
        Assert.Equal(2, TensorChecks.GatherStatistics(lowered).ReadCount);

        foreach (var (firstSlot, secondSlot) in new[] { (0, 0), (2, 0), (3, 1) })
        {
            var source = new float[circuit.SourceSize];
            source[firstSlot] = 1;
            source[8 + secondSlot] = 1;
            for (var slot = 0; slot < 4; slot++) source[4 + slot] = (float)(100.5 + slot);
            source[plainValue.Position] = 999;
            var reference = ExpectExact(circuit, lowered, source);
            Js.Is(secondSlot == 0 ? source[4 + firstSlot] : 999, reference[secondRead.Position]);
        }
    }

    // 'recognition' / 'an all-zero-allowed group multiplies the gathered value by its mass'.
    [Fact]
    public void An_all_zero_allowed_group_multiplies_the_gathered_value_by_its_mass()
    {
        const int SlotCount = 4;
        var circuit = new Circuit(SlotCount * 2);
        var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(SlotCount, SlotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits, allowAllZero: true);
        foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
        var read = circuit.Affine(MaskedTerms(circuit, bits, values));
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        // Each one-hot state, and the state with all bits at 0.
        for (var selectedSlot = -1; selectedSlot < SlotCount; selectedSlot++)
        {
            var source = BasicSource(SlotCount, selectedSlot);
            var reference = ExpectExact(circuit, lowered, source);
            Js.Is(selectedSlot < 0 ? 0 : source[SlotCount + selectedSlot], reference[read.Position]);
        }
    }

    // 'recognition' / 'recognition survives the affine inlining of argmax selection wires'.
    [Fact]
    public void Recognition_survives_the_affine_inlining_of_argmax_selection_wires()
    {
        // Some selection wires of Argmax are two-term affines that the builder copies into each
        // consumer. So the product ReGLUs store the expanded definition of the selection wire, and
        // the recognition finds the wire again through the expression cache.
        const int ScoreCount = 3;
        var circuit = new Circuit(ScoreCount + ScoreCount + 1);
        var scores = Enumerable.Range(0, ScoreCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(ScoreCount, ScoreCount).Select(circuit.Source).ToArray();
        var unit = circuit.Source(ScoreCount + ScoreCount);
        foreach (var score in scores) circuit.AssumeRange(score, 0, 1);
        foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
        circuit.AssumeRange(unit, 1, 1);
        var winner = circuit.Argmax(scores, unit);
        var read = circuit.Affine(MaskedTerms(circuit, winner.Selected, values));
        var (lowered, _) = CompileBoth(circuit, [read, .. winner.Selected]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        foreach (var scoreValues in new[] { new[] { 0.2f, 0.9f, 0.5f }, [0.9f, 0.2f, 0.5f], [0.5f, 0.5f, 0.5f], [0.1f, 0.2f, 0.9f] })
        {
            float[] source = [.. scoreValues, 17.5f, 23.25f, 31.125f, 1];
            var reference = ExpectExact(circuit, lowered, source);
            var winnerIndex = winner.Selected.ToList().FindIndex(bit => reference[bit.Position] == 1);
            Js.Is(source[ScoreCount + winnerIndex], reference[read.Position]);
        }
    }

    // 'recognition' / 'emitArgmax declares its group; emitFirstActive only for proven bits'.
    [Fact]
    public void Argmax_declares_its_group_and_first_active_only_for_proven_bits()
    {
        var circuit = new Circuit(5);
        Wire[] scores = [circuit.Source(0), circuit.Source(1), circuit.Source(2)];
        var gate = circuit.Source(3);
        var unit = circuit.Source(4);
        foreach (var wire in scores.Append(gate)) circuit.AssumeRange(wire, 0, 1);
        circuit.AssumeRange(unit, 1, 1);
        // Argmax declares its group without a condition: fractional scores only feed the boolean
        // comparisons, and the tree still routes the unit down one path.
        var winner = circuit.Argmax(scores, unit);
        Assert.Single(circuit.OneHotGroups);
        Assert.Equal(winner.Selected, circuit.OneHotGroups[0].Members);
        Assert.False(circuit.OneHotGroups[0].AllowAllZero);
        // A 0..1 range admits fractional values, so this selection is not a proven one-hot and stays undeclared.
        circuit.FirstActive(gate, scores);
        Assert.Single(circuit.OneHotGroups);
        // Boolean atoms give exact bits, and the range of the unit wire keeps it at 1. So this
        // selection is a proven one-hot or all zeros, and FirstActive declares it.
        var bits = scores.Select(circuit.Boolean).ToArray();
        var selected = circuit.FirstActive(unit, bits);
        Assert.Equal(2, circuit.OneHotGroups.Count);
        Assert.Equal(selected, circuit.OneHotGroups[1].Members);
        Assert.True(circuit.OneHotGroups[1].AllowAllZero);
        circuit.ValidateOneHotGroups(circuit.EvaluateReference([0.5f, 0.75f, 0.25f, 0, 1]));
    }

    // 'recognition' / 'fractional first-active selections never lower a masked sum'.
    [Fact]
    public void Fractional_first_active_selections_never_lower_a_masked_sum()
    {
        // The preconditions of FirstActive only bound its inputs to 0..1, and fractional scores are
        // legal. Candidates [0.5, 0] send the mass 0.25 to each output, so the selection is not a
        // one-hot. In Handwired, a declaration without a condition once lowered this read to a gather
        // that returned 50, while the reference evaluator computes 75.
        var circuit = new Circuit(5);
        var gate = circuit.Source(0);
        Wire[] candidates = [circuit.Source(1), circuit.Source(2)];
        Wire[] values = [circuit.Source(3), circuit.Source(4)];
        foreach (var wire in candidates.Prepend(gate)) circuit.AssumeRange(wire, 0, 1);
        foreach (var value in values) circuit.AssumeRange(value, 0, 1000);
        var selected = circuit.FirstActive(gate, candidates);
        var read = circuit.Affine(MaskedTerms(circuit, selected, values));
        Assert.Empty(circuit.OneHotGroups);
        var (lowered, plain) = CompileBoth(circuit, [read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(lowered).ReadCount);

        float[] source = [1, 0.5f, 0, 100, 200];
        var reference = ExpectExact(circuit, lowered, source);
        ExpectExact(circuit, plain, source);
        Js.Is(75, reference[read.Position]);
    }

    // 'recognition' / 'emitFirstActive accepts candidates that fold to one shared constant'.
    [Fact]
    public void First_active_accepts_candidates_that_fold_to_one_shared_constant()
    {
        // The candidates share a constant, but each multiplication keeps its own routing operation.
        // Their group that allows all zeros is legal, and the validator can check it.
        var circuit = new Circuit(1);
        var unit = circuit.Source(0);
        circuit.AssumeRange(unit, 1, 1);
        var zero = circuit.Constant(0);
        var selected = circuit.FirstActive(unit, [zero, zero]);
        Assert.Equal(2, selected.Length);
        Assert.Single(circuit.OneHotGroups);
        var values = circuit.EvaluateReference([1]);
        Assert.Equal([0.0, 0.0], selected.Select(wire => (double)values[wire.Position]));
        circuit.ValidateOneHotGroups(values);
    }

    // 'recognition' / 'candidates with unbounded declared ranges are refused in every orientation'.
    [Fact]
    public void Candidates_with_unbounded_declared_ranges_are_refused_in_every_orientation()
    {
        // A range that admits Infinity breaks the exactness argument. Take an infinite value with its
        // bit at 0. The reference computes f(Infinity × 0) = NaN, and the whole sum is NaN. A gather
        // skips that product and returns the selected value.
        const double Infinity = double.PositiveInfinity;
        (Circuit Circuit, Wire Read) BuildRead(bool declareBitsFirst, double minimum, double maximum)
        {
            var circuit = new Circuit(4);
            Wire[] bits = [circuit.Source(0), circuit.Source(1)];
            Wire[] values = [circuit.Source(2), circuit.Source(3)];
            if (declareBitsFirst) circuit.AssumeOneHot(bits);
            foreach (var value in values) circuit.AssumeRange(value, minimum, maximum);
            var read = circuit.Affine(MaskedTerms(circuit, bits, values));
            if (!declareBitsFirst) circuit.AssumeOneHot(bits, allowAllZero: false, allowExistingReaders: true);
            return (circuit, read);
        }

        (string Label, bool DeclareBitsFirst, double Minimum, double Maximum)[] cases =
        [
            // Bits declared first, nonnegative values with no upper bound: Multiply puts the bit on
            // the left, and the maximum of the value is not finite.
            ("bit on the left", true, 0, Infinity),
            // Bits declared first, values with no bound: Multiply puts the bit in the gate.
            ("bit in the gate", true, -Infinity, Infinity),
            // Bits declared after the reads: Multiply builds the mixed-sign pair, whose arms read the
            // same value with no bound.
            ("mixed-sign pair", false, -Infinity, Infinity),
        ];
        foreach (var (label, declareBitsFirst, minimum, maximum) in cases)
        {
            var (circuit, read) = BuildRead(declareBitsFirst, minimum, maximum);
            var (lowered, _) = CompileBoth(circuit, [read]);
            Assert.True(TensorChecks.GatherStatistics(lowered).ReadCount == 0, label);
            // The selected slot holds 5 and the other slot holds Infinity: both evaluators must give
            // the NaN of the dense sum.
            float[] source = [1, 0, 5, float.PositiveInfinity];
            var reference = circuit.EvaluateReference(source);
            Assert.True(float.IsNaN(reference[read.Position]), label);
            ExpectExact(circuit, lowered, source);
        }
    }

    // 'recognition' / 'an ignorable filler term over an unbounded value blocks lowering'.
    [Fact]
    public void An_ignorable_filler_term_over_an_unbounded_value_blocks_lowering()
    {
        (Circuit Circuit, Wire Read) BuildRead(double fillerMaximum)
        {
            var circuit = new Circuit(5);
            Wire[] bits = [circuit.Source(0), circuit.Source(1)];
            Wire[] values = [circuit.Source(2), circuit.Source(3)];
            var fillerValue = circuit.Source(4);
            circuit.AssumeOneHot(bits);
            foreach (var value in values) circuit.AssumeRange(value, 0, 100);
            circuit.AssumeRange(fillerValue, 0, fillerMaximum);
            var read = circuit.Affine([
                (circuit.Multiply(bits[0], values[0]), 1),
                (circuit.Multiply(bits[1], values[1]), 1),
                // The filler form f(x × max(0, -bit)): a signed zero for exact bits, but NaN when x is infinite.
                (circuit.Reglu([(fillerValue, 1)], 0, [(bits[0], -1)], 0), 1),
            ]);
            return (circuit, read);
        }

        var finite = BuildRead(100);
        var (finiteLowered, _) = CompileBoth(finite.Circuit, [finite.Read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(finiteLowered).ReadCount);
        ExpectExact(finite.Circuit, finiteLowered, [0, 1, 7, 9, 50]);

        var unbounded = BuildRead(double.PositiveInfinity);
        var (unboundedLowered, _) = CompileBoth(unbounded.Circuit, [unbounded.Read]);
        Assert.Equal(0, TensorChecks.GatherStatistics(unboundedLowered).ReadCount);
        float[] source = [1, 0, 7, 9, float.PositiveInfinity];
        var reference = unbounded.Circuit.EvaluateReference(source);
        Assert.True(float.IsNaN(reference[unbounded.Read.Position]));
        ExpectExact(unbounded.Circuit, unboundedLowered, source);
    }

    // 'recognition' / 'a result that is exactly zero may carry the other sign of zero'.
    [Fact]
    public void A_result_that_is_exactly_zero_may_carry_the_other_sign_of_zero()
    {
        // The agreement standard of the lowering is ===, which ValidateTensorEvaluation also uses,
        // and === treats +0 and -0 as equal. The reference adds the signed zeros of each product in
        // wire order. The gather adds only the selected contribution. So an exactly zero result can
        // be +0 on one side and -0 on the other.
        (Circuit Circuit, Wire Read) BuildRead(bool allowAllZero)
        {
            var circuit = new Circuit(4);
            Wire[] bits = [circuit.Source(0), circuit.Source(1)];
            var signedValue = circuit.Source(2);
            var nonnegativeValue = circuit.Source(3);
            circuit.AssumeOneHot(bits, allowAllZero);
            circuit.AssumeRange(signedValue, -10, 10);
            circuit.AssumeRange(nonnegativeValue, 0, 10);
            var read = circuit.Affine([
                (circuit.Multiply(bits[0], signedValue), 1),
                (circuit.Multiply(bits[1], nonnegativeValue), 1),
            ], -0.0);
            return (circuit, read);
        }

        // Case A: a group that allows all zeros, with no active bit. The reference adds a -0 product
        // and then a +0 product to the -0 bias and gives +0. The gather adds f(0 × -5) = -0 to the
        // -0 bias and gives -0.
        // Case B: a strict one-hot that selects the value -0, and a positive candidate later in
        // wire order brings the reference back to +0.
        foreach (var (allowAllZero, source) in new[] { (true, new float[] { 0, 0, -5, 7 }), (false, [1, 0, -0f, 7]) })
        {
            var (circuit, read) = BuildRead(allowAllZero);
            var (lowered, _) = CompileBoth(circuit, [read]);
            Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
            var reference = circuit.EvaluateReference(source);
            circuit.TensorProgram = lowered;
            var tensor = circuit.EvaluateTensor(source);
            Assert.True(tensor[read.Position] == reference[read.Position]);
            Js.Is(0, reference[read.Position]);
            Js.Is(-0.0, tensor[read.Position]);
        }
    }

    // 'recognition' / 'inlineCircuit carries declared groups into the target circuit'.
    [Fact]
    public void Inline_circuit_carries_declared_groups_into_the_target_circuit()
    {
        var child = new Circuit(4);
        Wire[] childBits = [child.Source(0), child.Source(1)];
        Wire[] childValues = [child.Source(2), child.Source(3)];
        child.AssumeOneHot(childBits, allowAllZero: true);
        foreach (var value in childValues) child.AssumeRange(value, 0, 100);
        var childRead = child.Affine(MaskedTerms(child, childBits, childValues));
        var parent = new Circuit(5);
        Wire[] parentBits = [parent.Source(1), parent.Source(2)];
        Wire[] parentValues = [parent.Source(3), parent.Source(4)];
        foreach (var wire in parentBits) parent.AssumeRange(wire, 0, 1);
        foreach (var wire in parentValues) parent.AssumeRange(wire, 0, 100);
        var wireMap = parent.InlineCircuit(child, [.. parentBits, .. parentValues]);

        var group = Assert.Single(parent.OneHotGroups);
        Assert.Equal<Wire>(parentBits, group.Members);
        Assert.True(group.AllowAllZero);
        var read = wireMap[childRead.Position];
        var (lowered, _) = CompileBoth(parent, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);
        var values = ExpectExact(parent, lowered, [9, 0, 1, 5, 7]);
        // Sharpwired addition: the value of the read, and the declared group holds on the values.
        Js.Is(7, values[read.Position]);
        parent.ValidateOneHotGroups(values);
    }

    // 'recognition' / 'lowering can be turned off per compile'.
    [Fact]
    public void Lowering_can_be_turned_off_per_compile()
    {
        var (circuit, _, _, read) = BuildBasicRead(4);
        var plain = circuit.CompileTensorProgram([read], lowerGathers: false);
        Assert.Equal(0, TensorChecks.GatherStatistics(plain).ReadCount);
        Assert.Equal(12, TensorChecks.GatherStatistics(plain).StoredWeightCount);
        ExpectExact(circuit, plain, BasicSource(4, 2));
    }

    // 'recognition' / 'gather outputs feed downstream macros exactly'.
    [Fact]
    public void Gather_outputs_feed_downstream_macros_exactly()
    {
        const int SlotCount = 6;
        var (circuit, bits, values, read) = BuildBasicRead(SlotCount);
        var other = circuit.Affine([.. bits.Select((bit, slot) => new Term(circuit.Multiply(bit, values[(slot + 1) % SlotCount]), 1))]);
        var largest = circuit.Maximum(read, other);
        var scaled = circuit.Multiply(read, other);
        var (lowered, _) = CompileBoth(circuit, [largest, scaled]);
        Assert.Equal(2, TensorChecks.GatherStatistics(lowered).ReadCount);
        for (var selectedSlot = 0; selectedSlot < SlotCount; selectedSlot++)
        {
            ExpectExact(circuit, lowered, BasicSource(SlotCount, selectedSlot));
        }
    }

    // 'recognition' / 'a violated one-hot contract diverges and validation catches it'.
    [Fact]
    public void A_violated_one_hot_contract_diverges_and_validation_catches_it()
    {
        // Two bits set at once: the reference adds both values, while the gather reads the slot of
        // the corrupted index. The lowering contract is the declared invariant, so
        // ValidateOneHotGroups is the check that a trainer must run before it writes bits back.
        const int SlotCount = 4;
        var (circuit, _, _, read) = BuildBasicRead(SlotCount);
        var (lowered, _) = CompileBoth(circuit, [read]);
        var source = BasicSource(SlotCount, 0);
        circuit.ValidateOneHotGroups(circuit.EvaluateReference(source));
        // A second active bit breaks the declaration.
        source[2] = 1;
        var reference = circuit.EvaluateReference(source);
        circuit.TensorProgram = lowered;
        var tensor = circuit.EvaluateTensor(source);
        Assert.False(Js.ObjectIs(reference[read.Position], tensor[read.Position]));
        // Sharpwired addition: the reference value is the sum of both selected values.
        Js.Is(Js.F(10.5 + 17), reference[read.Position]);
        Js.Throws(() => circuit.ValidateOneHotGroups(reference), "one-hot group");
    }
}
