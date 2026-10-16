// Handwired source: tests/runtime/index-read.test.ts (the builder cases: candidate tables, constant
// indices, refusals, index ranges and read ranges), and one case of
// tests/runtime/numeric-semantics.test.ts. Each test name holds the Handwired test name. These tests
// read the builder results only, and compile no program.

namespace Sharpwired.Core.Tests;

public partial class IndexReadTests
{
    // 'candidate tables are registered once, frozen, and shared by arrays with the same content'.
    // Handwired freezes the caller's array, so a change after a read throws. Sharpwired copies the
    // candidates into an immutable table, so a change to the caller's list cannot reach a read.
    [Fact]
    public void Candidate_tables_are_registered_once_copied_and_shared_by_lists_with_the_same_content()
    {
        var circuit = new Circuit(6);
        circuit.AssumeRange(circuit.Source(0), 0, 3);
        var candidates = Wires(1, 2, 3, 4).ToList();
        var first = circuit.IndexRead(circuit.Source(0), candidates);
        Assert.Equal(first, circuit.IndexRead(circuit.Source(0), candidates));
        Assert.Equal(first, circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)]));
        var zeroRead = circuit.IndexRead(circuit.Source(0), candidates, OutOfRange.Zero);
        Assert.NotEqual(first, zeroRead);
        var otherContent = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(5)]);
        Assert.NotEqual(first, otherContent);
        var firstTable = circuit.Operations[first.Position - circuit.SourceSize].IndexRead!.Table;
        Assert.Same(firstTable, circuit.Operations[zeroRead.Position - circuit.SourceSize].IndexRead!.Table);
        Assert.NotEqual(firstTable.Id, circuit.Operations[otherContent.Position - circuit.SourceSize].IndexRead!.Table.Id);

        candidates[0] = circuit.Source(5);
        candidates.Add(circuit.Source(5));
        Assert.Equal<Wire>([circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)], firstTable.Candidates);
        Js.Is(10, circuit.EvaluateReference([0, 10, 20, 30, 40, 50])[first.Position]);
        // The changed list has new content, so it registers a new table.
        Assert.NotEqual(first, circuit.IndexRead(circuit.Source(0), candidates));
    }

    // No Handwired case. The table lookup hashes the content. Each read gets its own table, and
    // identical content still shares one table. This test does not measure time.
    [Fact]
    public void A_thousand_distinct_tables_each_read_their_own_table_and_identical_content_shares_it()
    {
        const int TableCount = 1_000;
        const int TableSize = 64;
        var circuit = new Circuit(1 + 2 * TableSize);
        circuit.AssumeRange(circuit.Source(0), 0, TableSize - 1);
        // Table t holds, at position k, cell 1 + 2k or cell 2 + 2k, as bit k of t selects. Each t
        // below 1,024 gives different content.
        Wire[] Content(int t) =>
            [.. Enumerable.Range(0, TableSize).Select(k => circuit.Source(1 + 2 * k + (k < 10 ? (t >> k) & 1 : 0)))];
        var tables = new CandidateTable[TableCount];
        for (var t = 0; t < TableCount; t++)
        {
            var read = circuit.IndexRead(circuit.Source(0), Content(t));
            tables[t] = circuit.Operations[read.Position - circuit.SourceSize].IndexRead!.Table;
            Assert.Equal<Wire>(Content(t), tables[t].Candidates);
        }
        Assert.Equal(TableCount, tables.Select(table => table.Id).Distinct().Count());
        for (var t = 0; t < TableCount; t++)
        {
            // A new list with the same content and the other side rule: a new operation on the same table.
            var read = circuit.IndexRead(circuit.Source(0), Content(t), OutOfRange.Zero);
            Assert.Same(tables[t], circuit.Operations[read.Position - circuit.SourceSize].IndexRead!.Table);
        }
    }

    // 'a constant index selects its candidate while building'.
    [Fact]
    public void A_constant_index_selects_its_candidate_while_building()
    {
        var circuit = new Circuit(5);
        var three = circuit.Affine([], 3);
        var nine = circuit.Affine([], 9);
        Wire[] table = [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)];
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(three, table));
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(nine, table));
        Assert.Equal(Wire.FromPosition(1), circuit.IndexReadAffine([(three, -1)], 1, table, OutOfRange.Clamp, OutOfRange.Zero));
        var beyond = circuit.IndexRead(nine, table, OutOfRange.Zero);
        Js.Is(0, circuit.Constants[beyond.Position]!.Value);
        var belowZero = circuit.IndexReadAffine([(three, 1)], -5, table, OutOfRange.Zero, OutOfRange.Clamp);
        Js.Is(0, circuit.Constants[belowZero.Position]!.Value);
    }

    // 'the builder refuses reads that break the contract'.
    [Fact]
    public void The_builder_refuses_reads_that_break_the_contract()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(1), 0, Limit - 1);
        circuit.AssumeRange(circuit.Source(2), -Math.Pow(2, 25), 0);
        // An index cell with no declared range, and one declared past -2^24.
        Js.Throws(() => circuit.IndexRead(circuit.Source(0), [circuit.Source(3)]), "index range");
        Js.Throws(() => circuit.IndexRead(circuit.Source(2), [circuit.Source(3)]), "index range");
        // Both endpoints must lie strictly inside ±2^24: 0..2^24 - 1 builds, and adding 1 reaches 2^24.
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(circuit.Source(1), [circuit.Source(3)]));
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), []), "at least one candidate");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [Wire.FromPosition(1000)]), "wire is outside");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [circuit.Source(3)], (OutOfRange)7), "clamp or zero");
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), double.NaN)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "finite");
        var positiveInfinity = circuit.Affine([], double.PositiveInfinity);
        var negativeInfinity = circuit.Affine([], double.NegativeInfinity);
        Js.Throws(
            () => circuit.IndexReadAffine([(positiveInfinity, 1), (negativeInfinity, 1)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "must not be NaN");
    }

    // 'an index range is accepted only strictly inside ±2^24, so an endpoint that FP32 rounded onto 2^24 is refused'.
    [Fact]
    public void An_index_range_is_accepted_only_strictly_inside_the_limit_so_an_endpoint_rounded_onto_the_limit_is_refused()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, Limit);
        circuit.AssumeRange(circuit.Source(1), -Limit, 0);
        circuit.AssumeRange(circuit.Source(2), 0, Limit - 2);
        // f(2^24 + 1) = 2^24, so the form's range ends on 2^24, and at cell value 2^24
        // the read would land one position early.
        Assert.Equal(Limit, Js.F(Limit + 1));
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(0), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(0), [circuit.Source(3)]), "strictly inside ±2^24");
        // The negative mirror: f(-2^24 - 1) = -2^24.
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), 1)], -1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [circuit.Source(3)]), "strictly inside ±2^24");
        // One position inside the limit on either side builds.
        var upper = circuit.IndexReadAffine([(circuit.Source(2), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(1, Limit - 1), circuit.Operations[upper.Position - circuit.SourceSize].IndexRead!.IndexRange);
        var lower = circuit.IndexReadAffine([(circuit.Source(2), -1)], -1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(-(Limit - 1), -1), circuit.Operations[lower.Position - circuit.SourceSize].IndexRead!.IndexRange);
        // A range declared on the form can't put an endpoint on the limit either.
        Js.Throws(
            () => circuit.IndexReadAffine(
                [(circuit.Source(0), 1)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp, new NumericRange(0, Limit)),
            "strictly inside ±2^24");
        circuit.ValidateCircuit();
        var operationIndex = upper.Position - circuit.SourceSize;
        var operation = circuit.OperationList[operationIndex];
        circuit.OperationList[operationIndex] = operation with { IndexRead = operation.IndexRead! with { IndexRange = new NumericRange(0, Limit) } };
        Js.Throws(() => circuit.ValidateCircuit(), "strictly inside ±2^24");
    }

    // 'the read range is the union of the reachable candidates, plus 0 where a zero side is reachable'.
    [Fact]
    public void The_read_range_is_the_union_of_the_reachable_candidates_plus_zero_where_a_zero_side_is_reachable()
    {
        var circuit = new Circuit(9);
        (double Minimum, double Maximum)[] candidateRanges = [(0.5, 1), (2, 3), (-5, -4), (10, 20)];
        for (var position = 0; position < candidateRanges.Length; position++)
        {
            circuit.AssumeRange(Wire.FromPosition(1 + position), candidateRanges[position].Minimum, candidateRanges[position].Maximum);
        }
        var candidates = Wires(1, 2, 3, 4);
        Wire IndexCell(Wire wire, double minimum, double maximum)
        {
            circuit.AssumeRange(wire, minimum, maximum);
            return wire;
        }
        var inside = circuit.IndexRead(IndexCell(circuit.Source(5), 1, 2), candidates);
        Assert.Equal(new NumericRange(-5, 3), circuit.Ranges[inside.Position]);
        var zeroBelow = circuit.IndexReadAffine([(IndexCell(circuit.Source(6), -3, 1), 1)], 0, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0, 3), circuit.Ranges[zeroBelow.Position]);
        var clampBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], 0, candidates, OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0.5, 3), circuit.Ranges[clampBelow.Position]);
        var zeroAbove = circuit.IndexReadAffine([(IndexCell(circuit.Source(7), 2, 9), 1)], 0, candidates, OutOfRange.Clamp, OutOfRange.Zero);
        Assert.Equal(new NumericRange(-5, 20), circuit.Ranges[zeroAbove.Position]);
        // Index ranges entirely below the table reach only the 'zero' side, whatever their distance,
        // and they share one cached union. Handwired also checks that each read gets its own range
        // object. NumericRange is a value type, so each wire holds its own copy.
        var justBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -2, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        var fartherBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -50, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0, 0), circuit.Ranges[justBelow.Position]);
        Assert.Equal(new NumericRange(0, 0), circuit.Ranges[fartherBelow.Position]);
        var clampedFartherBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -50, candidates, OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0.5, 1), circuit.Ranges[clampedFartherBelow.Position]);
        // Cell 8 has no declared range. It opens the read's range only where the index can reach it.
        var unreachableUnknown = circuit.IndexRead(circuit.Source(5), [circuit.Source(8), circuit.Source(1)]);
        Assert.Equal(new NumericRange(0.5, 1), circuit.Ranges[unreachableUnknown.Position]);
        var reachableUnknown = circuit.IndexRead(circuit.Source(5), [circuit.Source(1), circuit.Source(8)]);
        Assert.Equal(NumericRange.Unknown, circuit.Ranges[reachableUnknown.Position]);
    }

    // numeric-semantics.test.ts 'an index read reads the wires of its index form and its candidates, so a later declaration on them throws'.
    [Fact]
    public void An_index_read_reads_the_wires_of_its_index_form_and_its_candidates_so_a_later_declaration_on_them_throws()
    {
        var circuit = new Circuit(5);
        var (row, offset, first, second, third) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4));
        circuit.AssumeRange(row, 0, 2);
        // JS: [first, second, third][row + offset * 0]. The zero coefficient is dropped, so the read doesn't read offset.
        circuit.IndexReadAffine([(row, 1), (offset, 0)], 0, [first, second, third], OutOfRange.Clamp, OutOfRange.Clamp);
        Js.Throws(() => circuit.AssumeRange(row, 0, 1), "already read wire 0");
        Js.Throws(() => circuit.AssumeRange(second, 0, 1), "already read wire 3");
        Js.Throws(() => circuit.AssumeGrid(third, 1), "already read wire 4");
        circuit.AssumeRange(offset, 0, 1);
    }

    // Sharpwired addition. Handwired reachableCandidates has no direct test: the 'proven bits' test of
    // index-read.test.ts checks it through emitFirstActive (Emit_first_active_declares_a_one_hot_group_...
    // in IndexReadTests.Selection.cs). These cases are the reach cases of that test, with the candidates that each read can return.
    [Fact]
    public void Reachable_candidates_follow_the_accepted_index_range_and_the_side_rules()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 2);
        var (a, b) = (circuit.Source(1), circuit.Source(2));
        IReadOnlyList<Wire> Reach(double bias, OutOfRange below, OutOfRange above)
        {
            var read = circuit.IndexReadAffine([(circuit.Source(0), 1)], bias, [a, b], below, above);
            return Circuit.ReachableCandidates(circuit.Operations[read.Position - circuit.SourceSize].IndexRead!);
        }
        // Index -1..1 reaches both candidates directly, and the first one again through the clamp below.
        Assert.Equal<Wire>([a, b, a], Reach(-1, OutOfRange.Clamp, OutOfRange.Zero));
        // Index 2..4 reaches only past the end.
        Assert.Equal<Wire>([b], Reach(2, OutOfRange.Zero, OutOfRange.Clamp));
        Assert.Equal<Wire>([], Reach(2, OutOfRange.Clamp, OutOfRange.Zero));
        // Index -4..-2 reaches only before the start.
        Assert.Equal<Wire>([a], Reach(-4, OutOfRange.Clamp, OutOfRange.Zero));
        Assert.Equal<Wire>([], Reach(-4, OutOfRange.Zero, OutOfRange.Clamp));
        // Index 0..2 reaches both candidates and the last one again through the clamp above.
        Assert.Equal<Wire>([a, b, b], Reach(0, OutOfRange.Zero, OutOfRange.Clamp));
    }

    // Sharpwired addition, from indexReadAffine: the read is one deeper than its deepest candidate
    // and its deepest index operand, and it has no grid and no constant.
    [Fact]
    public void An_index_read_is_one_deeper_than_its_deepest_candidate_and_index_operand()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeGrid(circuit.Source(1), 1);
        var deepCandidate = circuit.RoundAffine([(circuit.RoundAffine([(circuit.Source(1), 2)]), 2)]);
        var shallowRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]);
        Assert.Equal(1, circuit.Depths[shallowRead.Position]);
        Assert.Null(circuit.Grids[shallowRead.Position]);
        Assert.Null(circuit.Constants[shallowRead.Position]);
        var deepRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), deepCandidate]);
        Assert.Equal(3, circuit.Depths[deepRead.Position]);
        var index = circuit.RoundAffine([(circuit.Source(0), 1)]);
        circuit.AssumeRange(index, 0, 1);
        var readOfRead = circuit.IndexRead(index, [deepRead]);
        Assert.Equal(4, circuit.Depths[readOfRead.Position]);
    }

    // Sharpwired addition: the Js.CreateRandom port gives the values of the JavaScript createRandom.
    // The expected values come from node with the Handwired function text.
    [Fact]
    public void Create_random_gives_the_values_of_the_Handwired_generator()
    {
        var random = Js.CreateRandom(0x1dea);
        Assert.Equal(0.022467014146968722, random());
        Assert.Equal(0.26093018613755703, random());
        Assert.Equal(0.6365113609936088, random());
    }
}
