// Handwired source: tests/runtime/numeric-ui-core.test.ts ('inlined circuits become one graph and
// share matching expressions') and src/runtime/numeric-ui-core.ts (inlineCircuit). The other
// Handwired inlining cases are in NumericSemanticsTests, RoundingPointTests, IndexReadTests and
// SpringMatrixTests. The tests marked as Sharpwired additions isolate each declaration that
// inlineCircuit transfers.

namespace Sharpwired.Core.Tests;

public class InlineTests
{
    // 'inlined circuits become one graph and share matching expressions'.
    [Fact]
    public void Inlined_circuits_become_one_graph_and_share_matching_expressions()
    {
        var child = new Circuit(2);
        var childSum = child.Affine([(child.Source(0), 1), (child.Source(1), 1)]);
        var childProduct = child.Reglu([(childSum, 1)], 0, [(child.Source(0), 1)], 0);
        var parent = new Circuit(2);
        var existingSum = parent.Affine([(parent.Source(0), 1), (parent.Source(1), 1)]);
        var wireMap = parent.InlineCircuit(child, [parent.Source(0), parent.Source(1)]);

        Assert.Equal(existingSum, wireMap[childSum.Position]);
        var output = wireMap[childProduct.Position];
        var program = parent.CompileTensorProgram([output]);
        parent.TensorProgram = program;
        Js.Is(10, parent.EvaluateTensor([2, 3])[output.Position]);
    }

    // Sharpwired addition: the mapping covers the source cells and then each operation, and it must
    // have one wire for each source cell of the inlined circuit.
    [Fact]
    public void The_wire_map_starts_with_the_source_wires_and_the_mapping_must_cover_every_source_cell()
    {
        var child = new Circuit(2);
        var sum = child.RoundAffine([(child.Source(0), 1), (child.Source(1), 2)]);
        var parent = new Circuit(3);
        var wireMap = parent.InlineCircuit(child, [parent.Source(2), parent.Source(0)]);
        Assert.Equal([parent.Source(2), parent.Source(0), wireMap[sum.Position]], wireMap);
        Assert.Equal<Term>(
            [new Term(parent.Source(2), 1), new Term(parent.Source(0), 2)],
            parent.Operations[wireMap[sum.Position].Position - parent.SourceSize].LeftTerms);
        Js.Throws(() => parent.InlineCircuit(child, [parent.Source(0)]), "inlined source mapping has the wrong length");
        Js.Throws(() => parent.InlineCircuit(child, [parent.Source(0), Wire.FromPosition(9)]), "wire is outside the compiled circuit");
    }

    // Sharpwired addition: the ranges, grids and exact bits of the source cells, and the ranges and
    // grids of each operation, move to the mapped wires.
    [Fact]
    public void Inlining_transfers_ranges_grids_and_exact_bits()
    {
        var child = new Circuit(3);
        child.AssumeRange(child.Source(0), -4, 4);
        child.AssumeGrid(child.Source(0), 0.25);
        child.AssumeBit(child.Source(1));
        var scaled = child.RoundAffine([(child.Source(2), 3)]);
        child.AssumeRange(scaled, 0, 30);
        child.AssumeGrid(scaled, 2);
        var parent = new Circuit(3);
        var wireMap = parent.InlineCircuit(child, [parent.Source(0), parent.Source(1), parent.Source(2)]);
        Assert.Equal(new NumericRange(-4, 4), parent.Ranges[0]);
        Assert.Equal(0.25, parent.Grids[0]);
        Assert.Equal(new NumericRange(0, 1), parent.Ranges[1]);
        Assert.True(parent.ExactBits[1]);
        Assert.False(parent.ExactBits[0]);
        Assert.Equal(new NumericRange(0, 30), parent.Ranges[wireMap[scaled.Position].Position]);
        Assert.Equal(2, parent.Grids[wireMap[scaled.Position].Position]);
        // Source cell 2 has no declaration, so it stays open in the parent.
        Assert.Equal(NumericRange.Unknown, parent.Ranges[2]);
        Assert.Null(parent.Grids[2]);
    }

    // Sharpwired addition: a late grid that lands on a target wire that operations already read
    // throws, unless the caller opts in, as for a range.
    [Fact]
    public void A_grid_that_lands_on_a_wire_with_readers_throws_unless_the_caller_opts_in()
    {
        var child = new Circuit(1);
        child.AssumeGrid(child.Source(0), 1);
        var parent = new Circuit(1);
        parent.RoundAffine([(parent.Source(0), 2)]);
        Js.Throws(() => parent.InlineCircuit(child, [parent.Source(0)]), "already read wire 0 were built without a grid");
        parent.InlineCircuit(child, [parent.Source(0)], allowExistingReaders: true);
        Assert.Equal(1, parent.Grids[0]);
    }

    // Sharpwired addition: multiply and select records rebuild through Multiply and Select, so the
    // parent keeps the records, and the inner operations share the wires of the rebuilt macros.
    [Fact]
    public void Multiply_and_select_records_rebuild_as_macros()
    {
        var child = new Circuit(3);
        child.AssumeRange(child.Source(0), 0, 1);
        var product = child.Multiply(child.Source(1), child.Source(2));
        var selected = child.Select(child.Source(0), child.Source(1), product);
        var parent = new Circuit(3);
        var wireMap = parent.InlineCircuit(child, [parent.Source(0), parent.Source(1), parent.Source(2)]);
        Assert.Equal(child.Operations.Count, parent.Operations.Count);
        Assert.Equal(
            new MultiplyMacro(parent.Source(1), parent.Source(2)),
            parent.Operations[wireMap[product.Position].Position - parent.SourceSize].Macro);
        Assert.Equal(
            new SelectMacro(parent.Source(0), parent.Source(1), wireMap[product.Position]),
            parent.Operations[wireMap[selected.Position].Position - parent.SourceSize].Macro);
        foreach (var source in new float[][] { [0, -3, 5], [1, 7, -0.5f], [0.25f, 2, 3] })
        {
            var childValues = child.EvaluateReference(source);
            var parentValues = parent.EvaluateReference(source);
            Js.Is(childValues[selected.Position], parentValues[wireMap[selected.Position].Position]);
        }
    }

    // Sharpwired addition: comparison bits, rounding points, one-hot groups and candidate tables move
    // to the parent. A boolean atom with preserveRounding, from Compare on grid operands, reads its
    // score terms directly.
    [Fact]
    public void Inlining_transfers_comparison_bits_rounding_points_groups_and_shared_tables()
    {
        var child = new Circuit(4);
        child.AssumeRange(child.Source(0), 0, 64);
        child.AssumeGrid(child.Source(0), 1);
        child.AssumeRange(child.Source(1), 0, 64);
        child.AssumeGrid(child.Source(1), 1);
        child.AssumeRange(child.Source(2), 0, 1);
        child.AssumeRange(child.Source(3), 0, 3);
        var bit = child.Compare(child.Source(0), Relation.GreaterOrEqual, child.Source(1), new CompareOptions { EndWithBoolean = true });
        var atom = child.Operations[bit.Position - child.SourceSize];
        Assert.Equal(OperationKind.Boolean, atom.Kind);
        Assert.True(atom.PreserveRounding);
        var other = child.Boolean(child.Source(2));
        child.AssumeOneHot([bit, other], allowAllZero: true);
        Wire[] table = [child.Source(0), bit, other];
        var first = child.IndexRead(child.Source(3), table);
        var second = child.IndexRead(child.Source(3), table, OutOfRange.Zero);
        var marked = child.RoundAffine([(child.Source(2), 1)], 8_388_608);
        child.MarkRoundingPoint(marked);

        var parent = new Circuit(4);
        var wireMap = parent.InlineCircuit(child, [parent.Source(0), parent.Source(1), parent.Source(2), parent.Source(3)]);
        var parentAtom = parent.Operations[wireMap[bit.Position].Position - parent.SourceSize];
        Assert.Equal(OperationKind.Boolean, parentAtom.Kind);
        Assert.True(parentAtom.PreserveRounding);
        Assert.Equal<Term>(atom.LeftTerms, parentAtom.LeftTerms);
        Assert.Equal(atom.LeftBias, parentAtom.LeftBias);
        Assert.True(parentAtom.ComparisonBit);
        var group = Assert.Single(parent.OneHotGroups);
        Assert.Equal<Wire>([wireMap[bit.Position], wireMap[other.Position]], group.Members);
        Assert.True(group.AllowAllZero);
        Assert.Same(
            parent.Operations[wireMap[first.Position].Position - parent.SourceSize].IndexRead!.Table,
            parent.Operations[wireMap[second.Position].Position - parent.SourceSize].IndexRead!.Table);
        Assert.Equal(
            child.RoundingPoints().Select(point => (wireMap[point.Wire.Position], point.Kind)).OrderBy(point => point.Item1),
            parent.RoundingPoints().Select(point => (point.Wire, point.Kind)));
        foreach (var source in new float[][] { [3, 3, 0, 1], [3, 4, 1, 2], [0, 1, 0.25f, 0] })
        {
            var childValues = child.EvaluateReference(source);
            var parentValues = parent.EvaluateReference(source);
            for (var wire = 0; wire < child.Width; wire++) Js.Is(childValues[wire], parentValues[wireMap[wire].Position]);
            parent.ValidateOneHotGroups(parentValues);
        }
    }

    // Sharpwired addition: a marked affine can fold into a source cell of the parent, for example
    // when its terms map to one wire, and a source cell has no operation to record.
    [Fact]
    public void A_marked_affine_that_folds_into_a_source_cell_keeps_no_record()
    {
        var child = new Circuit(2);
        var half = child.Affine([(child.Source(0), 0.5), (child.Source(1), 0.5)]);
        child.MarkRoundingPoint(half);
        var parent = new Circuit(1);
        var wireMap = parent.InlineCircuit(child, [parent.Source(0), parent.Source(0)]);
        Assert.Equal(parent.Source(0), wireMap[half.Position]);
        Assert.Empty(parent.RoundingPoints());
        Assert.Empty(parent.Operations);
    }

    // Sharpwired addition: one-hot groups whose members map to one parent wire are refused, because a
    // group needs distinct members.
    [Fact]
    public void A_group_whose_members_map_to_one_wire_is_refused()
    {
        var child = new Circuit(2);
        child.AssumeOneHot([child.Source(0), child.Source(1)]);
        var parent = new Circuit(1);
        Js.Throws(() => parent.InlineCircuit(child, [parent.Source(0), parent.Source(0)]), "one-hot group members must be distinct wires");
    }
}
