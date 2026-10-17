// Handwired source: src/runtime/cells.ts (createSourceTable with createCircuit, next and finish, and
// createValidator). No Handwired test file.
// Each test fixes one rule of the Handwired code that the ported tests of cells.test.ts do not fix.
// The comment of each test names the cells.ts lines of the rule.

using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Cells.Tests;

public class SourceTableRuleTests
{
    private static Action Broken(FinishedTable finished, Action<float[]> edit) => () =>
    {
        var source = finished.InitialSource.ToArray();
        edit(source);
        finished.Validator.Validate(source);
    };

    private static FinishedTable FinishSmallTable()
    {
        var (table, cells, keep) = SourceTableTests.CreateSmallTable();
        table.CreateCircuit();
        var all = new { cells.Count, keep.Open, keep.Choice, keep.Order, keep.Child };
        table.Next(all, all);
        return table.Finish();
    }

    // cells.ts:362-371. The range check of each cell runs before the exact bit check. The value 2 in
    // an event cell breaks both checks, and the range message names it.
    [Fact]
    public void The_validator_checks_the_range_of_a_cell_before_its_exact_bit()
    {
        var finished = FinishSmallTable();
        var click = SourceTableTests.CreateSmallTable().Cells.Click.Position;
        var error = Js.Throws(Broken(finished, source => source[click] = 2), "click must be finite and within 0..1, got 2");
        Assert.IsType<CellValueException>(error);
    }

    // cells.ts:231. Finish builds Constant(0) only when the table has an event cell.
    [Fact]
    public void Finish_builds_no_constant_for_a_table_with_no_event_cell()
    {
        var table = new SourceTable();
        var count = table.Declare("count", Cell.State(new NumericRange(0, 10), initial: 3));
        var circuit = table.CreateCircuit();
        table.Next(count, count);
        var finished = table.Finish();
        Assert.Empty(circuit.Operations);
        Assert.Equal<Wire>([count], finished.NextSource);
    }

    // cells.ts:211-219. Next checks every pair before it records a pair. Here the pair of a passes
    // its checks, and the later pair of w fails, so a gets no next value.
    [Fact]
    public void Next_records_no_pair_when_a_later_pair_fails_its_checks()
    {
        var table = new SourceTable();
        var a = table.Declare("a", Cell.StateBit());
        var w = table.Declare("w", Cell.Input(Cell.UnitInterval));
        table.CreateCircuit();
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>(new { a, w }, new { a, w }), "w is not a state cell"));
        table.Next(a, a);
        Assert.Equal<Wire>([a, w], table.Finish().NextSource);
    }

    // cells.ts:148-150. The item rules of a permutation hold the columns of the matrix. Here each row
    // holds one 1, and column 0 holds two.
    [Fact]
    public void The_item_rules_of_a_permutation_hold_its_columns()
    {
        var (table, cells, keep) = SourceTableTests.CreateSmallTable();
        var circuit = table.CreateCircuit();
        // The choice group, the three rank groups, then the three item groups.
        Assert.Equal<Wire>(cells.Order.Select(row => row[0]), circuit.OneHotGroups[4].Members);
        var all = new { cells.Count, keep.Open, keep.Choice, keep.Order, keep.Child };
        table.Next(all, all);
        var finished = table.Finish();
        // The initial order is [2, 0, 1]: rank 0 holds item 2. Move it to item 0.
        var error = Js.Throws(
            Broken(finished, source =>
            {
                source[cells.Order[0][2].Position] = 0;
                source[cells.Order[0][0].Position] = 1;
            }),
            "order item 0 breaks its oneHot rule with mass 2");
        Assert.IsType<ArgumentException>(error);
    }

    // cells.ts:235. Finish validates the initial table. A one-hot group with no member at 1 breaks
    // its rule.
    [Fact]
    public void Finish_validates_the_initial_table()
    {
        var table = new SourceTable();
        var choice = table.Declare("choice", Cell.OneHot(Cell.List(2, _ => Cell.StateBit())));
        table.CreateCircuit();
        table.Next(choice, choice);
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Finish(), "choice breaks its oneHot rule with mass 0"));
    }

    // cells.ts:216. The next value must be a wire below sourceSize + the operation count.
    [Fact]
    public void Next_refuses_the_first_wire_past_the_circuit()
    {
        var table = new SourceTable();
        var open = table.Declare("open", Cell.StateBit());
        var other = table.Declare("other", Cell.StateBit());
        var circuit = table.CreateCircuit();
        var flipped = circuit.Affine([(open, -1)], 1);
        var width = circuit.SourceSize + circuit.Operations.Count;
        Assert.Equal(flipped.Position, width - 1);
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next(open, Wire.FromPosition(width)), "the next value of open is not a wire of the circuit"));
        table.Next(open, flipped);
        table.Next(other, other);
        Assert.Equal<Wire>([flipped, other], table.Finish().NextSource);
    }

    // cells.ts:186-187. CreateCircuit declares the range of a cell before its grid. A caller can
    // build a CellSpec with an inverted range and a grid of 3, which breaks both declarations. The
    // range declaration throws first.
    [Fact]
    public void CreateCircuit_declares_the_range_of_a_cell_before_its_grid()
    {
        var table = new SourceTable();
        table.Declare("bad", Cell.Input(Cell.UnitInterval) with { Range = new NumericRange(5, 1), Grid = 3 });
        Assert.IsType<ArgumentException>(Js.Throws(() => table.CreateCircuit(), "numeric range is inverted"));
    }
}
