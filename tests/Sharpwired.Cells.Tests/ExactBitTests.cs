// Handwired source: tests/runtime/cells.test.ts:67, :84 and :106 (the exact bit tests).
// Each test name holds the Handwired test name.

using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Cells.Tests;

public class ExactBitTests
{
    private static int[] DeclaredBits(Circuit circuit) =>
        [.. circuit.ExactBits.Select((isBit, wire) => (isBit, wire)).Where(entry => entry.isBit).Select(entry => entry.wire)];

    [Fact]
    public void CreateCircuit_declares_exact_bits_and_an_operation_that_reads_one_is_built_as_before()
    {
        var (table, cells, _) = SourceTableTests.CreateSmallTable();
        var circuit = table.CreateCircuit();
        // click, snap, open, the three choice cells and the permutation's nine cells.
        Assert.Equal(
            new[] { cells.Click, cells.Snap, cells.Open }.Concat(cells.Choice).Concat(cells.Order.SelectMany(row => row)).Select(wire => wire.Position),
            DeclaredBits(circuit));
        // A bit gets no grid from the declaration, so a sum that reads it has none, and Compare would still build a
        // sign cascade for the sum: AssumeGrid(wire, 1) is the declaration that changes that.
        Assert.Null(circuit.Grids[cells.Snap.Position]);
        var sum = circuit.Affine([(cells.Width, 1), (cells.Snap, 1)]);
        Assert.Null(circuit.Grids[sum.Position]);
        Assert.False(circuit.ExactBits[sum.Position]);
        Assert.Equal(new NumericRange(0, 101), circuit.Ranges[sum.Position]);
        // The declaration narrows the range to 0..1, and a range outside 0..1 contradicts it.
        circuit.AssumeBit(cells.Margin);
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[cells.Margin.Position]);
        Assert.True(circuit.ExactBits[cells.Margin.Position]);
        Js.Throws(() => circuit.AssumeBit(circuit.Affine([(cells.Count, 1)], 5)), "numeric range contradicts known bounds");
    }

    private sealed record BuiltProgram(Circuit Circuit, TensorProgram Program);

    [Fact]
    public void A_program_built_over_exact_bits_has_the_operations_and_the_compiled_layers_of_the_same_program_built_over_cells_within_0_to_1()
    {
        // The same builder code twice: once with the five bits declared with InputBit, once as plain cells within 0..1.
        static BuiltProgram Build(Func<CellSpec> bit)
        {
            var table = new SourceTable();
            var enabled = table.Declare("enabled", bit());
            var flags = table.Declare("flags", Cell.List(4, _ => bit()));
            var row = table.Declare("row", Cell.Input(new NumericRange(0, 3), grid: 1));
            var width = table.Declare("width", Cell.Input(new NumericRange(0, 100), grid: 1.0 / 64));
            var limit = table.Declare("limit", Cell.Input(new NumericRange(0, 100)));
            var circuit = table.CreateCircuit();
            var flag = circuit.IndexRead(row, flags);
            // A comparison whose operand reads a bit: with a grid on the bit, the builder would write integer bits here.
            var scaled = circuit.RoundAffine([(width, 1), (enabled, 8)]);
            var over = circuit.Compare(scaled, Relation.Greater, limit);
            Wire[] output =
            [
                circuit.Reglu([(width, 1)], 0, [(flag, 1), (enabled, 1)], -1),
                over,
                circuit.Multiply(enabled, width),
                circuit.Select(flag, width, limit),
            ];
            return new BuiltProgram(circuit, circuit.CompileTensorProgram(output));
        }
        var withBits = Build(() => Cell.InputBit());
        var without = Build(() => Cell.Input(new NumericRange(0, 1)));
        Assert.Equal((5, 0), (withBits.Circuit.ExactBits.Count(isBit => isBit), without.Circuit.ExactBits.Count(isBit => isBit)));
        Assert.True(withBits.Circuit.Operations.Count > 20, $"{withBits.Circuit.Operations.Count} operations");
        TensorChecks.AssertSameOperations(without.Circuit.Operations, withBits.Circuit.Operations);
        Assert.Equal(without.Circuit.Grids, withBits.Circuit.Grids);
        TensorChecks.AssertSameProgram(without.Program, withBits.Program);
    }

    private sealed record InlinedProgram(Circuit Circuit, Wire Flag, TensorProgram Program);

    // The PHASE3 review item cells.test.ts:106.
    [Fact]
    public void InlineCircuit_repeats_an_exact_bit_on_the_wire_that_the_bit_lands_on_and_builds_the_operations_that_it_builds_without_the_declaration()
    {
        // The same inner program twice, with its `enabled` cell declared with InputBit and as a cell within 0..1, inlined
        // into an outer circuit whose own cells are not bits: C# for the inner program, enabled ? width : 0.
        static InlinedProgram Build(Func<CellSpec> bit)
        {
            var innerTable = new SourceTable();
            var enabled = innerTable.Declare("enabled", bit());
            var width = innerTable.Declare("width", Cell.Input(new NumericRange(0, 100), grid: 1));
            var innerCircuit = innerTable.CreateCircuit();
            innerCircuit.Multiply(enabled, width);
            var outerTable = new SourceTable();
            var flag = outerTable.Declare("flag", Cell.Input(new NumericRange(0, 1)));
            var size = outerTable.Declare("size", Cell.Input(new NumericRange(0, 100), grid: 1));
            outerTable.Declare("other", Cell.Input(new NumericRange(0, 1)));
            var circuit = outerTable.CreateCircuit();
            var wires = circuit.InlineCircuit(innerCircuit, [flag, size]);
            return new InlinedProgram(circuit, flag, circuit.CompileTensorProgram([wires[^1]]));
        }
        var withBit = Build(() => Cell.InputBit());
        var without = Build(() => Cell.Input(new NumericRange(0, 1)));
        // Only the cell that the inner bit was mapped to is an exact bit of the outer circuit.
        Assert.Equal([withBit.Flag.Position], DeclaredBits(withBit.Circuit));
        Assert.DoesNotContain(true, without.Circuit.ExactBits);
        Assert.NotEmpty(withBit.Circuit.Operations);
        TensorChecks.AssertSameOperations(without.Circuit.Operations, withBit.Circuit.Operations);
        Assert.Equal(without.Circuit.Ranges, withBit.Circuit.Ranges);
        Assert.Equal(without.Circuit.Grids, withBit.Circuit.Grids);
        TensorChecks.AssertSameProgram(without.Program, withBit.Program);
    }
}
