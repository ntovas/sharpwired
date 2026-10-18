// Handwired source: tests/json-search/bit-emit.test.ts and tests/json-search/writer-probes.test.ts (the near
// misses: programs that miss one precondition of a rule, so that the rule must not fire). These tests check
// each proof rule of IntegerProof on its smallest program, and each counterexample of question 3 of the
// phase 5 plan. They need no adapter.

using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed class IntegerProofTests
{
    private const double Limit = 16_777_216;

    // The proof of a wire in the program of every wire.
    private static WholeProof ProofOf(Circuit circuit, Wire wire, bool lowerGathers = true)
    {
        var program = circuit.CompileTensorProgram(lowerGathers: lowerGathers);
        var proofs = IntegerProof.Prove(circuit, program);
        Assert.Equal(program.ValueCount, proofs.Length);
        return proofs[PositionOf(program, wire)];
    }

    private static int PositionOf(TensorProgram program, Wire wire)
    {
        for (var position = 0; position < program.WireOrder.Count; position++)
        {
            if (program.WireOrder[position] == wire.Position) return position;
        }
        throw new InvalidOperationException($"wire {wire.Position} is not in the program");
    }

    private static void IsFloat(WholeProof proof) => Assert.Equal(new WholeProof(WholeKind.Float, 0, 0), proof);

    private static void IsWhole(WholeProof proof, long minimum, long maximum) =>
        Assert.Equal(new WholeProof(WholeKind.Whole, minimum, maximum), proof);

    private static void IsBit(WholeProof proof, long minimum = 0, long maximum = 1) =>
        Assert.Equal(new WholeProof(WholeKind.Bit, minimum, maximum), proof);

    // A source cell on a grid of 1 within the range.
    private static Wire Whole(Circuit circuit, int cell, double minimum, double maximum)
    {
        var wire = circuit.Source(cell);
        circuit.AssumeRange(wire, minimum, maximum);
        circuit.AssumeGrid(wire, 1);
        return wire;
    }

    private static Wire Bit(Circuit circuit, int cell)
    {
        var wire = circuit.Source(cell);
        circuit.AssumeBit(wire);
        return wire;
    }

    [Fact]
    public void A_source_cell_is_a_bit_or_whole_from_its_declarations()
    {
        var circuit = new Circuit(9);
        var bit = Bit(circuit, 0);
        var whole = Whole(circuit, 1, -12, 12);
        var coarse = circuit.Source(2);
        circuit.AssumeRange(coarse, -3.5, 7);
        circuit.AssumeGrid(coarse, 2);
        var fine = circuit.Source(3);
        circuit.AssumeRange(fine, -12, 12);
        circuit.AssumeGrid(fine, 0.5);
        var wide = Whole(circuit, 4, -2 * Limit, 2 * Limit);
        var plain = circuit.Source(5);
        circuit.AssumeRange(plain, -12, 12);
        // A range of one value: each source table that passes the range check holds that value.
        var one = circuit.Source(6);
        circuit.AssumeRange(one, 1, 1);
        var three = circuit.Source(7);
        circuit.AssumeRange(three, 3, 3);
        var half = circuit.Source(8);
        circuit.AssumeRange(half, 2.5, 2.5);
        circuit.RoundAffine([.. Enumerable.Range(0, 9).Select(cell => new Term(circuit.Source(cell), 1))]);

        IsBit(ProofOf(circuit, bit));
        IsWhole(ProofOf(circuit, whole), -12, 12);
        // A grid of 2 is a grid of whole numbers. The range narrows to the whole numbers in it.
        IsWhole(ProofOf(circuit, coarse), -3, 7);
        IsFloat(ProofOf(circuit, fine));
        IsFloat(ProofOf(circuit, wide));
        IsFloat(ProofOf(circuit, plain));
        IsBit(ProofOf(circuit, one), 1, 1);
        IsWhole(ProofOf(circuit, three), 3, 3);
        IsFloat(ProofOf(circuit, half));
    }

    [Fact]
    public void An_affine_row_of_whole_operands_and_whole_weights_is_whole()
    {
        var circuit = new Circuit(3);
        var (a, b, bit) = (Whole(circuit, 0, -10, 10), Whole(circuit, 1, 0, 5), Bit(circuit, 2));
        var sum = circuit.RoundAffine([(a, 1), (b, -3)], 2);
        var negated = circuit.RoundAffine([(bit, -1)], 1);
        var constant = circuit.RoundAffine([], 7);
        var twoBits = circuit.RoundAffine([(bit, 1), (negated, 1)]);

        IsWhole(ProofOf(circuit, sum), -23, 12);
        IsBit(ProofOf(circuit, negated));
        IsWhole(ProofOf(circuit, constant), 7, 7);
        // bit + (1 - bit) is always 1, but the range arithmetic gives 0..2.
        IsWhole(ProofOf(circuit, twoBits), 0, 2);
    }

    // The counterexamples of an affine row. They are a weight of 0.5, a bias of 0.5, an operand that is
    // not whole, and a partial sum of 2^24 + 1.
    [Fact]
    public void An_affine_row_with_a_fraction_or_a_large_partial_sum_stays_f32()
    {
        var circuit = new Circuit(3);
        var (a, large, plain) = (Whole(circuit, 0, -10, 10), Whole(circuit, 1, 0, Limit / 2), circuit.Source(2));
        circuit.AssumeRange(plain, -10, 10);
        var halfWeight = circuit.RoundAffine([(a, 0.5)]);
        var halfBias = circuit.RoundAffine([(a, 1)], 0.5);
        var floatOperand = circuit.RoundAffine([(a, 1), (plain, 1)]);
        var atLimit = circuit.RoundAffine([(large, 1), (large, 1)]);
        var pastLimit = circuit.RoundAffine([(large, 1), (large, 1)], 1);
        var pastLimitFirst = circuit.RoundAffine([(large, 1), (large, 1), (large, -1)]);

        IsFloat(ProofOf(circuit, halfWeight));
        IsFloat(ProofOf(circuit, halfBias));
        IsFloat(ProofOf(circuit, floatOperand));
        IsWhole(ProofOf(circuit, atLimit), 0, (long)Limit);
        IsFloat(ProofOf(circuit, pastLimit));
        // The final value is within 0..2^23, but the partial sum large + large can be 2^24. The bound of
        // every product and partial sum is 3 × 2^23.
        IsFloat(ProofOf(circuit, pastLimitFirst));
    }

    [Fact]
    public void A_reglu_row_is_whole_when_its_forms_are_whole_and_the_product_stays_within_2_to_the_24()
    {
        var circuit = new Circuit(4);
        var (a, b, small, large) = (Whole(circuit, 0, -10, 10), Whole(circuit, 1, 0, 5), Whole(circuit, 2, 0, 4096), Whole(circuit, 3, 0, 8192));
        var product = circuit.Multiply(a, b);
        var gate = circuit.Reglu([(a, 2)], 1, [(b, 1), (a, 1)], -3);
        var relu = circuit.Relu(a);
        var tooLarge = circuit.Multiply(small, large);
        var halfGate = circuit.Reglu([(a, 1)], 0, [(b, 0.5)], 0);

        IsWhole(ProofOf(circuit, product), -50, 50);
        // left -19..21, relu(gate) 0..12.
        IsWhole(ProofOf(circuit, gate), -228, 252);
        IsWhole(ProofOf(circuit, relu), 0, 10);
        IsFloat(ProofOf(circuit, tooLarge));
        IsFloat(ProofOf(circuit, halfGate));
    }

    [Fact]
    public void A_boolean_atom_is_a_bit_for_a_whole_score_and_for_an_f32_score()
    {
        var circuit = new Circuit(2);
        var (bit, unit) = (Bit(circuit, 0), circuit.Source(1));
        circuit.AssumeRange(unit, 0, 1);
        IsBit(ProofOf(circuit, circuit.Boolean(bit)));
        IsBit(ProofOf(circuit, circuit.Boolean(unit)));
        IsFloat(ProofOf(circuit, unit));
    }

    [Fact]
    public void An_index_read_is_whole_when_each_candidate_that_its_index_can_reach_is_whole()
    {
        var circuit = new Circuit(6);
        var index = Whole(circuit, 0, 0, 1);
        var (a, b) = (Whole(circuit, 1, -4, 4), Whole(circuit, 2, 3, 9));
        var plain = circuit.Source(3);
        circuit.AssumeRange(plain, -10, 10);
        var wideIndex = Whole(circuit, 4, -3, 5);
        var fractionIndex = circuit.Source(5);
        circuit.AssumeRange(fractionIndex, 0, 2);
        var whole = circuit.IndexRead(wideIndex, [a, b, a]);
        var oneFloat = circuit.IndexRead(wideIndex, [a, plain, b]);
        // The index reaches candidates 0 and 1 only, so the f32 candidate 2 does not count.
        var reachable = circuit.IndexReadAffine([(index, 1)], 0, [a, b, plain], OutOfRange.Clamp, OutOfRange.Clamp);
        // A side that reads zero adds 0 to the range.
        var zeroSide = circuit.IndexRead(wideIndex, [b, b], OutOfRange.Zero);
        // An f32 index form reaches each candidate.
        var floatIndex = circuit.IndexRead(fractionIndex, [a, b, a]);
        var floatIndexOneFloat = circuit.IndexRead(fractionIndex, [a, b, plain]);

        IsWhole(ProofOf(circuit, whole), -4, 9);
        IsFloat(ProofOf(circuit, oneFloat));
        IsWhole(ProofOf(circuit, reachable), -4, 9);
        IsWhole(ProofOf(circuit, zeroSide), 0, 9);
        IsWhole(ProofOf(circuit, floatIndex), -4, 9);
        IsFloat(ProofOf(circuit, floatIndexOneFloat));
    }

    // Final review of phase 5. A member of an AssumeOneHot group is not a proven bit, on an operation
    // or on a source cell without AssumeBit. A finite cone does not make it a bit, so a masked gather
    // over it keeps the row f32.
    [Fact]
    public void A_masked_gather_needs_a_proven_bit_for_each_member()
    {
        var (circuit, sum) = IntegerWriterGpuTests.BrokenOneHotGather();
        var program = circuit.CompileTensorProgram([sum]);
        Assert.Contains(program.Layers, layer => layer.GatherReads.Any(read => read.Masked));
        IsFloat(IntegerProof.Prove(circuit, program)[PositionOf(program, sum)]);

        var sources = new Circuit(6);
        var cells = Enumerable.Range(0, 6).Select(sources.Source).ToArray();
        sources.AssumeOneHot(cells[..3], allowAllZero: true);
        foreach (var cell in new[] { 3, 4, 5 }) Whole(sources, cell, -5, 5);
        var masked = sources.Affine([
            (sources.Multiply(cells[0], cells[3]), 1),
            (sources.Multiply(cells[1], cells[4]), 1),
            (sources.Multiply(cells[2], cells[5]), 1),
        ], 2);
        var maskedProgram = sources.CompileTensorProgram([masked]);
        Assert.Contains(maskedProgram.Layers, layer => layer.GatherReads.Any(read => read.Masked));
        IsFloat(IntegerProof.Prove(sources, maskedProgram)[PositionOf(maskedProgram, masked)]);
    }

    // One-hot source bits select a whole value (a gather read of the lowered program). A candidate
    // that is not whole keeps the row f32. A group that allows all zeros gives a masked read, which
    // needs a proven bit for each member. The evaluators do not check a one-hot group.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void A_row_with_a_gather_read_is_whole_when_its_sum_and_each_candidate_are_whole(bool allowAllZero, bool floatCandidate, bool bits)
    {
        var circuit = new Circuit(6);
        var cells = Enumerable.Range(0, 6).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(cells[..3], allowAllZero);
        if (bits)
        {
            foreach (var cell in cells[..3]) circuit.AssumeBit(cell);
        }
        Whole(circuit, 3, -5, 5);
        Whole(circuit, 4, 0, 7);
        if (floatCandidate) circuit.AssumeRange(cells[5], -1, 1);
        else Whole(circuit, 5, -1, 1);
        var sum = circuit.Affine([
            (circuit.Multiply(cells[0], cells[3]), 1),
            (circuit.Multiply(cells[1], cells[4]), 1),
            (circuit.Multiply(cells[2], cells[5]), 1),
        ], 2);
        var program = circuit.CompileTensorProgram([sum]);
        Assert.Equal(1, TensorChecks.ProgramCounts(program).GatherReads);
        var proof = IntegerProof.Prove(circuit, program)[PositionOf(program, sum)];
        // 2 plus one candidate within -5..7, or plus 0 for a group of zeros.
        if (floatCandidate) IsFloat(proof);
        else IsWhole(proof, -3, 9);
    }

    // The bit that Compare returns. On grid operands, the range arithmetic proves each row. With the
    // sign cascade, the rows inside stay f32, and the circuit declares the last row a bit.
    [Fact]
    public void The_bit_of_Compare_is_a_bit_on_grids_and_after_a_sign_cascade()
    {
        var circuit = new Circuit(4);
        var (a, b) = (Whole(circuit, 0, -100, 100), Whole(circuit, 1, -100, 100));
        var (x, y) = (circuit.Source(2), circuit.Source(3));
        circuit.AssumeRange(x, -1e6, 1e6);
        circuit.AssumeRange(y, -1e6, 1e6);
        var onGrids = circuit.Compare(a, Relation.GreaterOrEqual, b);
        var cascade = circuit.Compare(x, Relation.Greater, y);
        Assert.Contains(circuit.RoundingPoints(), point => point.Wire == onGrids && point.Kind == RoundingPointKind.IntegerBit);
        Assert.Contains(circuit.RoundingPoints(), point => point.Wire == cascade && point.Kind == RoundingPointKind.SignCascade);

        IsBit(ProofOf(circuit, onGrids));
        IsBit(ProofOf(circuit, cascade));
        // The first row of the cascade reads the f32 difference.
        var first = circuit.RoundingPoints().First(point => point.Kind == RoundingPointKind.SignCascade).Wire;
        IsFloat(ProofOf(circuit, first));
    }

    // The rules that convert an f32 value need a cone with no infinite wire. Here p is within 0..1 by
    // range inference, but p is NaN for s0 = s1 = +Infinity, because the cells have no range.
    [Fact]
    public void A_converted_value_whose_cone_can_be_infinite_stays_f32()
    {
        var circuit = new Circuit(5);
        var (s0, s1, u, c0, c1) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4));
        circuit.AssumeRange(u, 0, 1);
        Whole(circuit, 3, -10, 10);
        Whole(circuit, 4, -10, 10);
        var ramp = circuit.Reglu([], 1, [(circuit.Affine([(s0, 1), (s1, -1)]), 1)], 0);
        var p = circuit.Reglu([(u, 1)], 0, [(circuit.Affine([(ramp, -1)], 1), 1)], 0);
        var atom = circuit.Boolean(p);
        var bit = circuit.Compare(p, Relation.Greater, circuit.Constant(0.5));
        var read = circuit.IndexRead(p, [c0, c1]);
        IsFloat(ProofOf(circuit, atom));
        IsFloat(ProofOf(circuit, bit));
        IsFloat(ProofOf(circuit, read));

        // The same program over cells with finite ranges proves each value.
        var bounded = new Circuit(5);
        bounded.AssumeRange(bounded.Source(0), -100, 100);
        bounded.AssumeRange(bounded.Source(1), -100, 100);
        bounded.AssumeRange(bounded.Source(2), 0, 1);
        Whole(bounded, 3, -10, 10);
        Whole(bounded, 4, -10, 10);
        var boundedRamp = bounded.Reglu([], 1, [(bounded.Affine([(bounded.Source(0), 1), (bounded.Source(1), -1)]), 1)], 0);
        var q = bounded.Reglu([(bounded.Source(2), 1)], 0, [(bounded.Affine([(boundedRamp, -1)], 1), 1)], 0);
        IsBit(ProofOf(bounded, bounded.Boolean(q)));
        IsBit(ProofOf(bounded, bounded.Compare(q, Relation.Greater, bounded.Constant(0.5))));
        IsWhole(ProofOf(bounded, bounded.IndexRead(q, [bounded.Source(3), bounded.Source(4)])), -10, 10);
    }

    // A grid or a bit that the caller declares on an operation is not a proof: the evaluators do not
    // check it. Here 0.5 × a holds 0.5 for a = 1, against the declared grid of 1.
    [Fact]
    public void A_grid_that_the_caller_declares_on_an_operation_proves_nothing()
    {
        var circuit = new Circuit(1);
        var a = Whole(circuit, 0, 0, 10);
        var half = circuit.RoundAffine([(a, 0.5)]);
        circuit.AssumeGrid(half, 1);
        var halfBit = circuit.RoundAffine([(a, 0.5)], 0.25);
        circuit.AssumeBit(halfBit, allowExistingReaders: true);
        Assert.Equal(1, circuit.Grids[half.Position]);
        IsFloat(ProofOf(circuit, half));
        IsFloat(ProofOf(circuit, halfBit));
    }

    // The integer evaluator checks the declarations that the proof and Compare use. Each cell on a grid
    // holds a multiple of the grid, and each exact bit holds 0, -0 or 1.
    [Fact]
    public void The_source_check_refuses_a_cell_off_its_grid_and_a_bit_that_is_not_0_or_1()
    {
        var circuit = new Circuit(3);
        Bit(circuit, 0);
        Whole(circuit, 1, -12, 12);
        var fine = circuit.Source(2);
        circuit.AssumeRange(fine, -12, 12);
        circuit.AssumeGrid(fine, 0.25);

        IntegerProof.ValidateSourceDeclarations(circuit, [-0f, -12f, 0.75f]);
        IntegerProof.ValidateSourceDeclarations(circuit, [1f, 7f, -11.25f]);
        Assert.Equal("source wire 0 is declared a bit and must be 0 or 1, got 0.5",
            Assert.Throws<ArgumentException>(() => IntegerProof.ValidateSourceDeclarations(circuit, [0.5f, 0f, 0f])).Message);
        Assert.Equal("source wire 1 is declared on the grid 1 and must be a multiple of it, got 2.5",
            Assert.Throws<ArgumentException>(() => IntegerProof.ValidateSourceDeclarations(circuit, [0f, 2.5f, 0f])).Message);
        Assert.Equal("source wire 2 is declared on the grid 0.25 and must be a multiple of it, got 0.375",
            Assert.Throws<ArgumentException>(() => IntegerProof.ValidateSourceDeclarations(circuit, [0f, 0f, 0.375f])).Message);
        Assert.Throws<ArgumentException>(() => IntegerProof.ValidateSourceDeclarations(circuit, [0f, 0f]));
    }

    [Fact]
    public void Prove_refuses_a_program_of_another_circuit()
    {
        var circuit = new Circuit(2);
        circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        Assert.Throws<ArgumentException>(() => IntegerProof.Prove(new Circuit(3), circuit.CompileTensorProgram()));
    }
}
