// Handwired source: tests/runtime/cells.test.ts (the createEvaluator tests, the NaN message tests, and
// the runtime part of the plain number test).
// Each test name holds the Handwired test name. A test with no Handwired twin says so in its comment.
// Handwired returns the output in the shape of the output object. Here ProgramEvaluation.Outputs holds
// the values in OutputPaths order, and Read(wire) gives the value of one output wire.

using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Cells.Tests;

public class ProgramEvaluatorTests
{
    private sealed record SpeedOutput(Wire Doubled);

    private sealed record OnSpeedOutput(Wire On, SpeedOutput Speed);

    [Fact]
    public void CreateEvaluator_returns_next_values_in_cell_order_and_outputs_in_the_output_shape_and_checks_both_tables()
    {
        var table = new SourceTable();
        var press = table.Declare("press", Cell.Event());
        var onCell = table.Declare("on", Cell.StateBit());
        var speedCell = table.Declare("speed", Cell.State(new NumericRange(0, 8), initial: 2));
        var circuit = table.CreateCircuit();
        // C#: if (press) on = !on; speed *= 2
        var on = circuit.Select(press, onCell, circuit.Affine([(onCell, -1)], 1));
        var speed = circuit.Affine([(speedCell, 2)]);
        table.Next(onCell, on);
        table.Next(speedCell, speed);
        var evaluator = ProgramEvaluator.Create(table.Finish(), new OnSpeedOutput(on, new SpeedOutput(speed)));
        Assert.Equal<string>(["on", "speed.doubled"], evaluator.OutputPaths);
        float[] source = [1, 0, 2];
        var first = evaluator.Evaluate(source);
        Js.AllIs([0, 1, 4], first.NextValues.Select(value => (double)value));
        Js.AllIs([1, 4], first.Outputs.Select(value => (double)value));
        Js.Is(1, first.Read(on));
        Js.Is(4, first.Read(speed));
        first.NextValues.CopyTo(source, 0);
        evaluator.Evaluate(source).NextValues.CopyTo(source, 0);
        // The third evaluation doubles 8 to 16, past the declared range.
        Assert.IsType<InvalidOperationException>(Js.Throws(() => evaluator.Evaluate(source), "speed must be finite and within 0..8, got 16"));
        // The table is checked before the program runs: a press of 0.5 throws, although the next value of the press cell is 0.
        Assert.IsType<CellValueException>(Js.Throws(() => evaluator.Evaluate([0.5f, 0, 2]), "press must be exactly 0 or 1, got 0.5"));
    }

    [Fact]
    public void CreateEvaluator_checks_that_outputs_are_finite_and_names_the_operation_that_produced_a_NaN()
    {
        var table = new SourceTable();
        var pressed = table.Declare("pressed", Cell.StateBit(1));
        var width = table.Declare("width", Cell.Input(new NumericRange(0, 3.4e38), initial: 3));
        var circuit = table.CreateCircuit();
        table.Next(pressed, pressed);
        // C#: pressed ? 7 : width * 2. Both sides of a select always compute, and width * 2 overflows to Infinity at 3e38.
        var doubledWidth = circuit.RoundAffine([(width, 2)]);
        var shown = circuit.Select(pressed, doubledWidth, circuit.Constant(7));
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { shown });
        Js.AllIs([7], evaluator.Evaluate([1, 3]).Outputs.Select(value => (double)value));
        var failure = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate([1, 3e38f]));
        Assert.Matches(@"^output shown must be finite, got NaN\. wire \d+ is the first NaN: .*, in select\(gate wire 0 = 1", failure.Message);
    }

    [Fact]
    public void A_failed_check_gets_no_NaN_message_when_no_checked_value_is_NaN_even_when_an_unused_operation_is_NaN()
    {
        var table = new SourceTable();
        var pressed = table.Declare("pressed", Cell.StateBit(1));
        var width = table.Declare("width", Cell.Input(new NumericRange(0, 3.4e38), initial: 3));
        var speedCell = table.Declare("speed", Cell.State(new NumericRange(0, 8), initial: 2));
        var circuit = table.CreateCircuit();
        table.Next(pressed, pressed);
        // Read by no next value or output, for example left over from an earlier version of the builder. It is NaN at width 3e38.
        circuit.Select(pressed, circuit.RoundAffine([(width, 2)]), circuit.Constant(7));
        var speed = circuit.Affine([(speedCell, 2)]);
        table.Next(speedCell, speed);
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { speed });
        // speed doubles from 8 to 16, past its range.
        var failure = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate([1, 3e38f, 8]));
        Assert.Matches(@"^speed must be finite and within 0\.\.8, got 16$", failure.Message);
    }

    [Fact]
    public void A_failed_check_gets_a_NaN_message_when_the_value_that_failed_is_NaN_and_not_for_a_NaN_value_that_the_check_did_not_reach()
    {
        var table = new SourceTable();
        var pressed = table.Declare("pressed", Cell.StateBit(1));
        var width = table.Declare("width", Cell.Input(new NumericRange(0, 3.4e38), initial: 3));
        var speedCell = table.Declare("speed", Cell.State(new NumericRange(0, 8), initial: 2));
        var shownCell = table.Declare("shown", Cell.State(Cell.AnyNumber));
        var circuit = table.CreateCircuit();
        table.Next(pressed, pressed);
        // C#: speed *= 2
        table.Next(speedCell, circuit.Affine([(speedCell, 2)]));
        // C#: shown = pressed ? 7 : width * 2. Both sides of a select always compute, so the select is NaN at width 3e38.
        var shown = circuit.Select(pressed, circuit.RoundAffine([(width, 2)]), circuit.Constant(7));
        table.Next(shownCell, shown);
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { shown });
        // speed doubles from 8 to 16, past its range, and the check throws there. The check does not reach the NaN next
        // value of shown and the NaN output.
        var speedFailure = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate([1, 3e38f, 8, 0]));
        Assert.Matches(@"^speed must be finite and within 0\.\.8, got 16$", speedFailure.Message);
        var shownFailure = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate([1, 3e38f, 2, 0]));
        Assert.Matches(
            $@"^shown must be finite and within -Infinity\.\.Infinity, got NaN\. wire \d+ is the first NaN: .*, in select\(gate wire {pressed} = 1",
            shownFailure.Message);
    }

    [Fact]
    public void The_NaN_message_names_the_first_NaN_that_the_NaN_output_reads_and_a_NaN_operand_of_a_select_gets_no_select_context()
    {
        var table = new SourceTable();
        var pressed = table.Declare("pressed", Cell.StateBit(1));
        var width = table.Declare("width", Cell.Input(new NumericRange(0, 3.4e38), initial: 3));
        var circuit = table.CreateCircuit();
        table.Next(pressed, pressed);
        // Read by no output, and NaN at width 3e38.
        circuit.Select(pressed, circuit.RoundAffine([(width, 2)]), circuit.Constant(7));
        // C#: pressed ? width * 2 + width * -2 : 7. At width 3e38, the side that the gate picks is Infinity - Infinity.
        var plus = circuit.RoundAffine([(width, 2)]);
        var minus = circuit.RoundAffine([(width, -2)]);
        var difference = circuit.RoundAffine([(plus, 1), (minus, 1)]);
        var shown = circuit.Select(pressed, circuit.Constant(7), difference);
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { shown });
        var failure = Assert.Throws<InvalidOperationException>(() => evaluator.Evaluate([1, 3e38f]));
        Assert.Matches(
            $@"^output shown must be finite, got NaN\. wire {difference} is the first NaN: a sum of wire {plus} = Infinity, wire {minus} = -Infinity$",
            failure.Message);
    }

    private sealed record PressSpring(Wire Displacement, Wire Velocity);

    // Handwired refuses the plain numbers in the type checker only. Wire has no conversion from int, so
    // these calls do not compile here. The runtime part stays: wires made with Wire.FromPosition at
    // positions counted by hand build without an error.
    [Fact]
    public void Table_next_and_createEvaluator_refuse_plain_numbers_where_wires_are_expected_in_the_type_checker()
    {
        var table = new SourceTable();
        var press = table.Declare("press", Cell.Event());
        var on = table.Declare("on", Cell.StateBit());
        var spring = table.Declare("spring", scope => new PressSpring(
            scope.Declare("displacement", Cell.State(new NumericRange(-40, 40))),
            scope.Declare("velocity", Cell.State(new NumericRange(-1280, 1280)))));
        var pair = table.Declare("pair", Cell.List(2, _ => Cell.StateBit()));
        table.Declare("count", Cell.State(new NumericRange(0, 10)));
        table.Declare("flag", Cell.StateBit());
        table.CreateCircuit();
        // Each call writes a position counted by hand where a wire is meant. For example, 0 is the press cell, and 6 is
        // the count cell. At runtime, a next value is only checked to be a wire of the circuit, and a stored position to
        // be a state cell.
        table.Next(on, Wire.FromPosition(0));
        table.Next(spring, new PressSpring(Wire.FromPosition(0), spring.Velocity));
        table.Next(pair, [Wire.FromPosition(0), Wire.FromPosition(0)]);
        table.Next(Wire.FromPosition(6), press);
        table.Next(new { Flag = Wire.FromPosition(7) }, new { Flag = press });
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { Typo = Wire.FromPosition(1) });
        var result = evaluator.Evaluate([1, 0, 5, 7, 0, 0, 3, 0]);
        // on, the displacement of the spring, both pair cells, count and flag took the 1 of the press cell. The output read
        // the on cell.
        Js.AllIs([0, 1, 1, 7, 1, 1, 1, 1], result.NextValues.Select(value => (double)value));
        Assert.Equal<string>(["typo"], evaluator.OutputPaths);
        Js.AllIs([0], result.Outputs.Select(value => (double)value));
    }

    // Sharpwired case with no Handwired test: the phase 4 interfaces. A host that holds an
    // ISourceTableEvaluator gets the same values and the same result object as the concrete evaluator.
    [Fact]
    public void ProgramEvaluator_works_through_ISourceTableEvaluator()
    {
        var table = new SourceTable();
        var press = table.Declare("press", Cell.Event());
        var countCell = table.Declare("count", Cell.State(new NumericRange(0, 8)));
        var circuit = table.CreateCircuit();
        // C#: count += press ? 1 : 0; doubled = count * 2
        var count = circuit.Affine([(countCell, 1), (press, 1)]);
        var doubled = circuit.Affine([(count, 2)]);
        table.Next(countCell, count);
        var concrete = ProgramEvaluator.Create(table.Finish(), new { count, doubled });
        ISourceTableEvaluator evaluator = concrete;
        var evaluation = evaluator.Evaluate([1, 3]);
        // Read the values before the next call, because the next call overwrites the arrays.
        Js.AllIs([0, 4], evaluation.NextValues.Select(value => (double)value));
        Js.Is(4, evaluation.Read(count));
        Js.Is(8, evaluation.Read(doubled));
        Assert.IsType<ArgumentException>(Js.Throws(() => evaluation.Read(press), $"wire {press} is not an output"));
        Assert.Same(concrete.Evaluate([0, 5]), evaluation);
        Js.AllIs([0, 5], evaluation.NextValues.Select(value => (double)value));
        Js.Is(10, evaluation.Read(doubled));
        Assert.IsType<CellValueException>(Js.Throws(() => evaluator.Evaluate([0.5f, 3]), "press must be exactly 0 or 1, got 0.5"));
    }

    private sealed record NumberOutput(float Speed);

    // Sharpwired case with no Handwired test: an output leaf that is not a wire, and the paths of nested
    // records, lists and anonymous objects.
    [Fact]
    public void An_output_holds_wires_lists_and_objects_and_its_paths_follow_the_declaration_order_of_the_properties()
    {
        var table = new SourceTable();
        var speedCell = table.Declare("speed", Cell.State(new NumericRange(0, 8)));
        table.CreateCircuit();
        table.Next(speedCell, speedCell);
        var finished = table.Finish();
        Assert.IsType<ArgumentException>(Js.Throws(() => ProgramEvaluator.Create(finished, new NumberOutput(1)), "an output holds wires, lists and objects"));
        Assert.IsType<ArgumentException>(Js.Throws(() => ProgramEvaluator.Create<object>(finished, 2.5f), "an output holds wires, lists and objects"));
        var evaluator = ProgramEvaluator.Create(finished, new { Zebra = speedCell, Apple = new[] { new OnSpeedOutput(speedCell, new SpeedOutput(speedCell)) } });
        Assert.Equal<string>(["zebra", "apple[0].on", "apple[0].speed.doubled"], evaluator.OutputPaths);
        var single = ProgramEvaluator.Create(finished, speedCell);
        Assert.Equal<string>([""], single.OutputPaths);
        Js.AllIs([3], single.Evaluate([3]).Outputs.Select(value => (double)value));
        Assert.IsType<ArgumentException>(Js.Throws(() => single.Evaluate([3]).Read(Wire.FromPosition(7)), "wire 7 is not an output"));
    }

    // Sharpwired case with no Handwired test: the reflection walk runs once in Create. Evaluate returns
    // the same result object each time, with arrays that the next call overwrites, and allocates nothing.
    [Fact]
    public void Evaluate_reuses_its_result_and_allocates_nothing()
    {
        var table = new SourceTable();
        var press = table.Declare("press", Cell.Event());
        var countCell = table.Declare("count", Cell.State(new NumericRange(0, 1_000_000)));
        var circuit = table.CreateCircuit();
        var count = circuit.Affine([(countCell, 1), (press, 1)]);
        table.Next(countCell, count);
        var evaluator = ProgramEvaluator.Create(table.Finish(), new { count, pressed = new[] { press } });
        float[] source = [1, 0];
        var first = evaluator.Evaluate(source);
        for (var call = 0; call < 10; call++) evaluator.Evaluate(source);
        var before = GC.GetAllocatedBytesForCurrentThread();
        ProgramEvaluation last = first;
        for (var call = 0; call < 1_000; call++) last = evaluator.Evaluate(source);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(first, last);
        Js.AllIs([0, 1], last.NextValues.Select(value => (double)value));
        Js.AllIs([1, 1], last.Outputs.Select(value => (double)value));
    }
}
