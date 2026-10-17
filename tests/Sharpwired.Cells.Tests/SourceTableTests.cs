// Handwired source: tests/runtime/cells.test.ts (smallTable, counterCells, buildCounter, and the tests
// of declaration, createCircuit, next, finish, the validator and components).
// Each test name holds the Handwired test name. A test with no Handwired twin says so in its comment.
// Handwired declares a nested object literal with table.declare. Here each test declares the cells
// with ICellScope calls and keeps the wires in a C# record of the same shape.

using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Cells.Tests;

public class SourceTableTests
{
    internal sealed record ChildCells(Wire Velocity);

    internal sealed record SmallCells(Wire Margin, Wire Width, Wire Click, Wire Snap, Wire Count, Wire Open, Wire[] Choice, Wire[][] Order);

    // Every state cell except count.
    internal sealed record KeepCells(Wire Open, Wire[] Choice, Wire[][] Order, ChildCells Child);

    internal sealed record SmallTable(SourceTable Table, SmallCells Cells, KeepCells Keep);

    // Handwired smallTable: one cell of every kind, a one-hot group, a permutation, and a cell declared
    // in a second call under a name.
    internal static SmallTable CreateSmallTable()
    {
        var table = new SourceTable();
        var cells = new SmallCells(
            table.Declare("margin", Cell.Constant(12, new NumericRange(0, 64))),
            table.Declare("width", Cell.Input(new NumericRange(0, 100), initial: 40, grid: 1)),
            table.Declare("click", Cell.Event()),
            table.Declare("snap", Cell.InputBit()),
            table.Declare("count", Cell.State(new NumericRange(0, 10), initial: 2)),
            table.Declare("open", Cell.StateBit(1)),
            table.Declare("choice", Cell.OneHot(Cell.List(3, index => Cell.StateBit(index == 1 ? 1 : 0)))),
            table.Declare("order", Cell.Permutation([2, 0, 1])));
        var child = table.Declare("child", scope => new ChildCells(scope.Declare("velocity", Cell.State(Cell.AnyNumber))));
        // Every state cell except count, each one keeping its value.
        var keep = new KeepCells(cells.Open, cells.Choice, cells.Order, child);
        return new SmallTable(table, cells, keep);
    }

    private static int[] Positions(IEnumerable<Wire> wires) => [.. wires.Select(wire => wire.Position)];

    [Fact]
    public void Positions_follow_declaration_order_and_a_name_prefixes_the_cells_of_a_later_call()
    {
        var (table, cells, keep) = CreateSmallTable();
        Assert.Equal([0, 1, 2, 3, 4, 5], Positions([cells.Margin, cells.Width, cells.Click, cells.Snap, cells.Count, cells.Open]));
        Assert.Equal([6, 7, 8], Positions(cells.Choice));
        Assert.Equal([9, 10, 11], Positions(cells.Order[0]));
        Assert.Equal([12, 13, 14], Positions(cells.Order[1]));
        Assert.Equal([15, 16, 17], Positions(cells.Order[2]));
        Assert.Equal(18, keep.Child.Velocity.Position);
        Assert.Equal(19, table.Size);
        Assert.Equal("child.velocity", table.NameOf(keep.Child.Velocity));
        Assert.Equal("order[1][2]", table.NameOf(cells.Order[1][2]));
        Assert.Equal(
            [CellPolicy.Constant, CellPolicy.Input, CellPolicy.Event, CellPolicy.State, CellPolicy.State],
            new[] { cells.Margin, cells.Width, cells.Click, cells.Count, keep.Child.Velocity }.Select(table.PolicyOf));
        var refused = Js.Throws(() => Cell.Permutation([0, 0, 2]), "0, 0, 2 is not a permutation of 0..2");
        Assert.IsType<ArgumentException>(refused);
    }

    [Fact]
    public void CreateCircuit_declares_ranges_grids_and_one_hot_groups_and_finish_derives_next_values_and_the_initial_table()
    {
        var (table, cells, keep) = CreateSmallTable();
        var circuit = table.CreateCircuit();
        Assert.Equal(new NumericRange(0, 100), circuit.Ranges[cells.Width.Position]);
        Assert.Equal(1, circuit.Grids[cells.Width.Position]);
        // The choice group, then the permutation's three rank groups and three item groups.
        Assert.Equal(7, circuit.OneHotGroups.Count);
        var nextCount = circuit.Affine([(cells.Count, 1), (cells.Click, 1)]);
        table.Next(cells.Count, nextCount);
        table.Next(keep, keep);
        var finished = table.Finish();
        var nextSource = finished.NextSource;
        // A constant or an input holds itself, an event holds the constant 0, and a state cell holds its next value.
        Assert.Equal(
            [cells.Margin, cells.Width, cells.Snap, nextCount],
            new[] { nextSource[cells.Margin.Position], nextSource[cells.Width.Position], nextSource[cells.Snap.Position], nextSource[cells.Count.Position] });
        var zero = circuit.Operations[nextSource[cells.Click.Position].Position - circuit.SourceSize];
        Assert.Equal(OperationKind.Affine, zero.Kind);
        Assert.Empty(zero.LeftTerms);
        Js.Is(0, zero.LeftBias);
        Js.AllIs(
            [12, 40, 0, 0, 2, 1, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0],
            finished.InitialSource.Select(value => (double)value));
        Assert.Same(circuit, finished.Circuit);
        Assert.Equal(19, finished.Validator.CellCount);
    }

    [Fact]
    public void A_state_cell_without_a_next_value_or_with_two_fails_while_building_and_so_does_a_call_out_of_order()
    {
        var missing = CreateSmallTable();
        missing.Table.CreateCircuit();
        missing.Table.Next(missing.Keep, missing.Keep);
        Assert.IsType<InvalidOperationException>(Js.Throws(() => missing.Table.Finish(), "count never received a next value"));
        var (table, cells, _) = CreateSmallTable();
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Next(cells.Count, cells.Width), "call createCircuit before table.next"));
        table.CreateCircuit();
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Declare("late", Cell.Event()), "declare every cell before createCircuit"));
        table.Next(cells.Count, cells.Width);
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Next(cells.Count, cells.Width), "count already has a next value"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next(cells.Width, cells.Count), "width is not a state cell"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next(cells.Open, Wire.FromPosition(10_000)), "the next value of open is not a wire of the circuit"));
    }

    // Sharpwired case with no Handwired test: the order errors that the Handwired test does not reach.
    [Fact]
    public void Calls_out_of_order_name_the_order_that_a_table_needs()
    {
        var (table, cells, keep) = CreateSmallTable();
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Finish(), "call createCircuit before finish"));
        table.CreateCircuit();
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.AtMostOne("late", [cells.Click, cells.Snap]), "add rules before createCircuit"));
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.CreateCircuit(), "createCircuit may only run once"));
        table.Next(cells.Count, cells.Count);
        table.Next(keep, keep);
        table.Finish();
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Next(cells.Count, cells.Count), "table.next after finish has no effect"));
        Assert.IsType<InvalidOperationException>(Js.Throws(() => table.Finish(), "finish may only run once"));
    }

    // Sharpwired case with no Handwired test: the refusals of the cell functions and of atMostOne.
    [Fact]
    public void The_cell_functions_and_atMostOne_refuse_a_declaration_that_breaks_their_rules()
    {
        Assert.IsType<ArgumentException>(Js.Throws(() => Cell.Input(new NumericRange(5, 1)), "a cell's range is inverted: 5..1"));
        Assert.IsType<ArgumentException>(Js.Throws(() => Cell.State(new NumericRange(0, double.NaN)), "a cell's range is inverted: 0..NaN"));
        Assert.IsType<ArgumentException>(Js.Throws(
            () => Cell.OneHot([Cell.StateBit(), Cell.State(Cell.UnitInterval)]), "one-hot members must be exact bits, e.g. stateBit()"));
        Assert.IsType<ArgumentException>(Js.Throws(() => Cell.Event(grid: 0.5), "a bit's grid can only be 1"));
        Assert.IsType<ArgumentException>(Js.Throws(() => Cell.StateBit(grid: 2), "a bit's grid can only be 1"));
        Assert.IsType<ArgumentException>(Js.Throws(() => Cell.Permutation([1, 0], grid: 0.25), "a bit's grid can only be 1"));
        Assert.Equal(1, Cell.Event(grid: 1).Grid);
        Assert.Null(Cell.StateBit().Grid);
        Assert.Equal(new NumericRange(double.NegativeInfinity, double.PositiveInfinity), Cell.AnyNumber);
        Assert.Equal(new NumericRange(0, 1), Cell.UnitInterval);
        var table = new SourceTable();
        var width = table.Declare("width", Cell.Input(new NumericRange(0, 1)));
        var click = table.Declare("click", Cell.Event());
        Assert.IsType<ArgumentException>(Js.Throws(() => table.AtMostOne("pointer", [click, width]), "pointer: members must be exact bits"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.AtMostOne("outside", [click, Wire.FromPosition(5)]), "outside: members must be exact bits"));
        Assert.Equal("wire 5", table.NameOf(Wire.FromPosition(5)));
        Assert.Null(table.PolicyOf(Wire.FromPosition(5)));
    }

    internal sealed record SpringCells(Wire Velocity, Wire Position);

    internal sealed record PairCells(Wire[] Pair, Wire Flag);

    [Fact]
    public void Next_pairs_state_with_wires_by_field_name_and_a_different_shape_pairs_nothing()
    {
        var table = new SourceTable();
        // The record declares Velocity first, and the named arguments declare position first.
        var spring = table.Declare("spring", scope => new SpringCells(
            Position: scope.Declare("position", Cell.State(new NumericRange(0, 100), initial: 10)),
            Velocity: scope.Declare("velocity", Cell.State(new NumericRange(-100, 100)))));
        var circuit = table.CreateCircuit();
        Assert.Equal((0, 1), (spring.Position.Position, spring.Velocity.Position));
        var nextPosition = circuit.Affine([(spring.Position, 1), (spring.Velocity, 0.5)]);
        var nextVelocity = circuit.Affine([(spring.Velocity, 0.9)]);
        // A different field set does not compile with one type on both sides. With object as the type, the
        // runtime check of Handwired refuses it.
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next<object>(spring, new { Position = nextPosition, Speed = nextVelocity }),
            "next state has fields position, speed but the stored struct has position, velocity"));
        table.Next(spring, new SpringCells(nextVelocity, nextPosition));
        Assert.Equal<Wire>([nextPosition, nextVelocity], table.Finish().NextSource);
    }

    // Sharpwired case with no Handwired test: a list of the wrong length pairs nothing, also for the
    // fields that the walk reached before the list.
    [Fact]
    public void Next_refuses_a_list_of_the_wrong_length_before_it_pairs_any_cell()
    {
        var table = new SourceTable();
        var cells = table.Declare("", scope => new PairCells(scope.Declare("pair", Cell.List(2, _ => Cell.StateBit())), scope.Declare("flag", Cell.StateBit())));
        table.CreateCircuit();
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next(cells.Pair, [cells.Flag]), "next state must be a list of 2"));
        // The walk visits flag before pair, in name order, and still pairs nothing.
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next(cells, new PairCells([cells.Flag], cells.Flag)), "next state at pair must be a list of 2"));
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next<object>(cells, new { flag = cells.Flag, pair = cells.Flag }),
            "next state at pair must be a list of 2"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>(cells.Flag, cells.Pair), "next state must be a wire"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>(cells, cells.Pair), "next state must be an object"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>(1.5f, cells.Flag), "stored state holds wires, lists and objects"));
        table.Next(cells, cells);
        Assert.Equal<Wire>([cells.Pair[0], cells.Pair[1], cells.Flag], table.Finish().NextSource);
    }

    [Fact]
    public void The_validator_names_the_cell_or_rule_that_a_table_breaks()
    {
        var (table, cells, keep) = CreateSmallTable();
        table.AtMostOne("click or snap", [cells.Click, cells.Snap]);
        table.CreateCircuit();
        var all = new { cells.Count, keep.Open, keep.Choice, keep.Order, keep.Child };
        table.Next(all, all);
        var finished = table.Finish();
        Action Broken(Action<float[]> edit) => () =>
        {
            var source = finished.InitialSource.ToArray();
            edit(source);
            finished.Validator.Validate(source);
        };
        var width = Assert.IsType<CellValueException>(Js.Throws(Broken(source => source[cells.Width.Position] = 101), "width must be finite and within 0..100, got 101"));
        Assert.Equal(cells.Width.Position, width.Position);
        Js.Throws(Broken(source => source[cells.Width.Position] = 40.5f), "width must be a multiple of 1, got 40.5");
        Js.Throws(Broken(source => source[keep.Child.Velocity.Position] = float.PositiveInfinity), "child.velocity must be finite");
        Js.Throws(Broken(source => source[cells.Click.Position] = 0.5f), "click must be exactly 0 or 1, got 0.5");
        var rule = Js.Throws(Broken(source => source[cells.Choice[0].Position] = 1), "choice breaks its oneHot rule with mass 2");
        Assert.IsType<ArgumentException>(rule);
        Js.Throws(Broken(source => source[cells.Order[0][2].Position] = 0), "order rank 0 breaks its oneHot rule with mass 0");
        Js.Throws(Broken(source => source[cells.Click.Position] = source[cells.Snap.Position] = 1), "click or snap breaks its atMostOne rule with mass 2");
        Assert.IsType<ArgumentException>(Js.Throws(() => finished.Validator.Validate(new float[3]), "the table holds 3 numbers, but 19 cells are declared"));
    }

    // Handwired counterCells: the cells of a component.
    internal sealed record CounterCells(Wire Count);

    // The output of the component.
    internal sealed record CounterOutput(Wire Count);

    internal static CounterCells DeclareCounter(ICellScope scope) => new(scope.Declare("count", Cell.State(new NumericRange(0, 1000))));

    // Handwired buildCounter: a component builds with its own wires and a wire of its parent.
    internal static CounterOutput BuildCounter(Circuit circuit, SourceTable table, CounterCells counter, Wire press)
    {
        var count = circuit.Affine([(counter.Count, 1), (press, 1)]);
        table.Next(counter, new CounterCells(count));
        return new CounterOutput(count);
    }

    [Fact]
    public void A_component_is_declared_nested_in_its_parent_or_in_a_second_call_and_built_with_the_parents_wires()
    {
        var table = new SourceTable();
        var press = table.Declare("press", Cell.Event());
        var counters = table.DeclareList("counters", 2, (scope, _) => DeclareCounter(scope));
        var extra = table.Declare("extra", DeclareCounter);
        var circuit = table.CreateCircuit();
        CounterOutput[] outputs = [.. counters.Append(extra).Select(counter => BuildCounter(circuit, table, counter, press))];
        Assert.Equal(["counters[1].count", "extra.count"], new[] { table.NameOf(counters[1].Count), table.NameOf(extra.Count) });
        var evaluator = ProgramEvaluator.Create(table.Finish(), outputs);
        Assert.Equal<string>(["[0].count", "[1].count", "[2].count"], evaluator.OutputPaths);
        Js.AllIs([6, 1, 8], evaluator.Evaluate([1, 5, 0, 7]).Outputs.Select(value => (double)value));
    }
}
