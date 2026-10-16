// Handwired source: src/runtime/numeric-ui-core.ts (markRoundingPoint, roundingPoints,
// recordRoundingPoint) and tests/runtime/comparison.test.ts ('marking, and inlining keeps
// records, grids and values').

namespace Sharpwired.Core.Tests;

public class RoundingPointTests
{
    // comparison.test.ts 'marking, and inlining keeps records, grids and values' (the marking part).
    [Fact]
    public void Marking_records_a_marked_rounding_point_and_refuses_a_source_cell()
    {
        var child = new Circuit(3);
        child.AssumeRange(child.Source(2), -100, 100);
        var roundChain = child.RoundAffine([(child.Source(2), 1)], Math.Pow(2, 23) + Math.Pow(2, 22));
        child.MarkRoundingPoint(roundChain);
        Js.Throws(() => child.MarkRoundingPoint(child.Source(2)), "not a source cell");
        Assert.Equal([new RoundingPoint(roundChain, RoundingPointKind.Marked)], child.RoundingPoints());
    }

    // comparison.test.ts 'marking, and inlining keeps records, grids and values' (reference part).
    // Handwired compares the child values with the inlined parent. This port also compares the child
    // values with the plain definitions.
    [Fact]
    public void Marking_and_inlining_keeps_records_grids_and_values()
    {
        var child = new Circuit(3);
        child.AssumeRange(child.Source(0), 0, 4096);
        child.AssumeRange(child.Source(1), 0, 4096);
        child.AssumeGrid(child.Source(0), 1.0 / 64);
        child.AssumeGrid(child.Source(1), 1.0 / 64);
        child.AssumeRange(child.Source(2), -100, 100);
        var roundChain = child.RoundAffine([(child.Source(2), 1)], Math.Pow(2, 23) + Math.Pow(2, 22));
        child.MarkRoundingPoint(roundChain);
        Js.Throws(() => child.MarkRoundingPoint(child.Source(2)), "not a source cell");
        Wire[] roots =
        [
            child.Compare(child.Source(0), Relation.GreaterOrEqual, child.Source(1)),
            child.Compare(child.Source(0), Relation.Equal, child.Source(1), new CompareOptions { EndWithBoolean = true }),
            child.Compare(child.Source(2), Relation.Less, child.Affine([], Js.F(0.3))),
            child.MinimumExact(child.Source(2), child.Affine([], 1.5)),
        ];
        var kinds = child.RoundingPoints().ToDictionary(point => point.Wire, point => point.Kind);
        Assert.Equal(RoundingPointKind.Marked, kinds[roundChain]);
        Assert.Contains(RoundingPointKind.IntegerBit, kinds.Values);
        Assert.Contains(RoundingPointKind.SignCascade, kinds.Values);
        Assert.Contains(RoundingPointKind.Boolean, kinds.Values);
        Assert.Equal([1.0 / 64, 1.0 / 64, 1.0], [child.Grids[0], child.Grids[1], child.Grids[roots[0].Position]]);
        var parent = new Circuit(3);
        var wireMap = parent.InlineCircuit(child, [parent.Source(1), parent.Source(0), parent.Source(2)]);
        var parentKinds = parent.RoundingPoints().ToDictionary(point => point.Wire, point => point.Kind);
        foreach (var (wire, kind) in child.RoundingPoints()) Assert.Equal(kind, parentKinds[wireMap[wire.Position]]);
        Assert.Equal(RoundingPointKind.Marked, parentKinds[wireMap[roundChain.Position]]);
        Assert.Equal([1.0 / 64, 1.0 / 64, 1.0], [parent.Grids[0], parent.Grids[1], parent.Grids[wireMap[roots[0].Position].Position]]);
        parent.ValidateCircuit();
        foreach (var (a, b, c) in new[] { (5.0, 5.0, 0.3), (5 + 1.0 / 64, 5.0, -0.3), (0.0, 4096.0, 100.0), (4096.0, 0.0, Js.F(0.3)) })
        {
            var values = child.EvaluateReference([(float)a, (float)b, (float)c]);
            var parentValues = parent.EvaluateReference([(float)b, (float)a, (float)c]);
            foreach (var root in roots) Js.Is(values[root.Position], parentValues[wireMap[root.Position].Position]);
            var f = Js.F(c);
            Js.Is(a >= b ? 1 : 0, values[roots[0].Position]);
            Js.Is(a == b ? 1 : 0, values[roots[1].Position]);
            Js.Is(f < Js.F(0.3) ? 1 : 0, values[roots[2].Position]);
            Js.Is(Math.Min(f, 1.5), values[roots[3].Position]);
        }
    }

    [Fact]
    public void Boolean_atoms_are_recorded_and_earlier_records_win()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var bit = circuit.Boolean(circuit.Source(0));
        circuit.MarkRoundingPoint(bit);
        circuit.MarkRoundingPoint(sum);
        circuit.MarkRoundingPoint(sum);
        Assert.Equal(
            [new RoundingPoint(sum, RoundingPointKind.Marked), new RoundingPoint(bit, RoundingPointKind.Boolean)],
            circuit.RoundingPoints());
    }

    [Fact]
    public void An_internal_record_keeps_the_first_kind()
    {
        var circuit = new Circuit(1);
        var stage = circuit.RoundAffine([(circuit.Source(0), 2)]);
        circuit.RecordRoundingPoint(stage, RoundingPointKind.SignCascade);
        circuit.RecordRoundingPoint(stage, RoundingPointKind.IntegerBit);
        circuit.MarkRoundingPoint(stage);
        Assert.Equal([new RoundingPoint(stage, RoundingPointKind.SignCascade)], circuit.RoundingPoints());
    }

    [Fact]
    public void Marking_refuses_a_wire_outside_the_circuit()
    {
        var circuit = new Circuit(1);
        Js.Throws(() => circuit.MarkRoundingPoint(Wire.FromPosition(1)), "wire is outside the compiled circuit");
    }
}
