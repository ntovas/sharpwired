// Handwired source: src/runtime/numeric-ui-core.ts (markRoundingPoint, roundingPoints,
// recordRoundingPoint) and tests/runtime/comparison.test.ts ('marking, and inlining keeps
// records, grids and values'). The emitCompare and inlineCircuit parts of that test belong
// to plan tasks 4 and 6.

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
