// Handwired source: src/runtime/numeric-ui-core.ts (evaluateReference, validateSourceRanges,
// evaluateOperationStream, evaluateTerms, evaluateBoolean, resolveIndexPosition, readIndexedValue).
// The index read tests add an index operation by hand, as Handwired tests change
// circuit.operations by hand. The IndexRead port (plan task 3) builds index reads.

using System.Collections.Immutable;

namespace Sharpwired.Core.Tests;

public class ReferenceEvaluatorTests
{
    [Fact]
    public void Affine_terms_run_in_stored_order_from_the_rounded_bias()
    {
        var circuit = new Circuit(2);
        // f(0.1) + 2^24 + 1 - 2^24 in FP32, term by term.
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(0), -1)], 0.1);
        var values = circuit.EvaluateReference([16_777_216, 1]);
        float expected = (float)0.1;
        expected += 16_777_216f;
        expected += 1f;
        expected -= 16_777_216f;
        Js.Is(expected, values[sum.Position]);
    }

    [Fact]
    public void A_reglu_with_a_NaN_gate_is_NaN_and_a_negative_zero_gate_counts_as_positive_zero()
    {
        var circuit = new Circuit(2);
        var nan = circuit.RoundAffine([(circuit.Source(1), 1), (circuit.Source(1), -1)]);
        var nanProduct = circuit.Reglu([(circuit.Source(0), 1)], 0, [(nan, 1)], 0);
        var zeroGate = circuit.Append(OperationKind.Reglu, [(circuit.Source(0), 1)], 0, [], -0.0, preserveRounding: true);
        var negativeLeft = circuit.Reglu([(circuit.Source(0), -1)], 0, [(circuit.Source(1), -1)], 0);

        var values = circuit.EvaluateReference([3, float.PositiveInfinity]);
        Assert.True(float.IsNaN(values[nanProduct.Position]));
        Js.Is(0, values[zeroGate.Position]);
        Js.Is(-0.0, values[negativeLeft.Position]);
    }

    [Fact]
    public void The_source_range_check_names_the_first_wire_outside_its_range_and_refuses_NaN()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(1), 0, 1);
        Js.Throws(() => circuit.EvaluateReference([5, 2]), "source wire 1 is outside its assumed range");
        Js.Throws(() => circuit.ValidateSourceRanges([float.NaN, 0]), "source wire 0 is outside its assumed range");
        Js.Throws(() => circuit.ValidateSourceRanges([0]), "source table has the wrong length");
        circuit.ValidateSourceRanges([float.NegativeInfinity, 1]);
    }

    [Fact]
    public void A_boolean_atom_reads_a_composed_form()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 0.5);
        circuit.AssumeRange(circuit.Source(1), 0, 0.5);
        var sum = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var bit = circuit.Boolean(sum);
        Assert.Equal<Term>([new Term(circuit.Source(0), 1), new Term(circuit.Source(1), 1)], circuit.Operations[bit.Position - circuit.SourceSize].LeftTerms);
        Js.Is(1, circuit.EvaluateReference([0.25f, 0.25f])[bit.Position]);
        Js.Is(0, circuit.EvaluateReference([0.25f, 0.2f])[bit.Position]);
    }

    [Fact]
    public void A_boolean_input_must_be_within_zero_and_one()
    {
        var circuit = new Circuit(1);
        Js.Throws(() => circuit.Boolean(circuit.Source(0)), "boolean input must be within zero and one");
    }

    [Fact]
    public void An_index_read_copies_the_selected_candidate_and_follows_the_rules_outside_the_table()
    {
        var circuit = new Circuit(3);
        var index = circuit.Source(0);
        var table = new CandidateTable(0, [circuit.Source(1), circuit.Source(2)], 0);
        AddIndexOperation(circuit, table, [new Term(index, 1)], OutOfRange.Clamp, OutOfRange.Zero);
        AddIndexOperation(circuit, table, [new Term(index, 1), new Term(index, -1)], OutOfRange.Clamp, OutOfRange.Zero);
        var read = circuit.SourceSize;
        var nanRead = circuit.SourceSize + 1;

        Js.Is(-0.0, circuit.EvaluateReference([-1, -0f, 7])[read]);
        Js.Is(-0.0, circuit.EvaluateReference([0.999f, -0f, 7])[read]);
        Js.Is(7, circuit.EvaluateReference([1.999f, -0f, 7])[read]);
        Js.Is(0, circuit.EvaluateReference([2, -0f, 7])[read]);
        Js.Is(float.PositiveInfinity, circuit.EvaluateReference([1, 3, float.PositiveInfinity])[read]);
        Assert.True(float.IsNaN(circuit.EvaluateReference([float.PositiveInfinity, 3, 7])[nanRead]));
    }

    [Fact]
    public void Resolve_index_position_floors_in_float64_and_applies_the_side_rules()
    {
        Assert.Equal(-2, Circuit.ResolveIndexPosition(double.NaN, 4, false, false));
        Assert.Equal(0, Circuit.ResolveIndexPosition(-0.5, 4, false, false));
        Assert.Equal(-1, Circuit.ResolveIndexPosition(-0.5, 4, true, false));
        Assert.Equal(3, Circuit.ResolveIndexPosition((float)3.999, 4, false, false));
        Assert.Equal(3, Circuit.ResolveIndexPosition(double.PositiveInfinity, 4, false, false));
        Assert.Equal(-1, Circuit.ResolveIndexPosition(4, 4, false, true));
        Assert.Equal(0, Circuit.ResolveIndexPosition(double.NegativeInfinity, 4, false, true));
    }

    internal static void AddIndexOperation(
        Circuit circuit,
        CandidateTable table,
        ImmutableArray<Term> indexTerms,
        OutOfRange below,
        OutOfRange above)
    {
        AppendOperation(circuit, new Operation
        {
            Kind = OperationKind.Index,
            LeftTerms = [],
            LeftBias = 0,
            GateTerms = indexTerms,
            GateBias = 0,
            Depth = 1,
            IndexRead = new IndexReadInstruction(table, below, above, new NumericRange(-1, 2)),
        });
    }

    // Append an operation that the builder does not make, for example an index read with a NaN
    // index form, and one entry to each list that has one entry for each wire. The new wire has
    // the depth of the operation, no constant value, an open range, no grid and no exact bit. The
    // wires that the operation reads get a reader.
    internal static void AppendOperation(Circuit circuit, Operation operation)
    {
        circuit.OperationList.Add(operation);
        circuit.DepthList.Add(operation.Depth);
        circuit.ConstantList.Add(null);
        circuit.RangeList.Add(NumericRange.Unknown);
        circuit.GridList.Add(null);
        circuit.ExactBitList.Add(false);
        circuit.HasReaderList.Add(false);
        IEnumerable<Wire> readWires = [
            .. operation.LeftTerms.Select(term => term.Wire),
            .. operation.GateTerms.Select(term => term.Wire),
            .. operation.IndexRead?.Table.Candidates ?? []];
        foreach (var wire in readWires.Where(wire => wire.Position < circuit.Width)) circuit.HasReaderList[wire.Position] = true;
        var width = circuit.Width;
        Assert.All(
            [circuit.DepthList.Count, circuit.ConstantList.Count, circuit.RangeList.Count, circuit.GridList.Count, circuit.ExactBitList.Count, circuit.HasReaderList.Count],
            count => Assert.Equal(width, count));
    }
}
