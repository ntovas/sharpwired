// Handwired source: src/runtime/cells.ts (pairLeaves, collectLeaves). No Handwired test file.
// Handwired walks plain JavaScript objects. The C# walker reads public properties by reflection,
// so these tests fix how it treats C# shapes that JavaScript does not have.

using System.Collections.Immutable;
using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Cells.Tests;

public class WireStructTests
{
    private struct FieldStruct
    {
        public Wire On;
    }

    private record BaseOutput(Wire First);

    private sealed record DerivedOutput(Wire Zeta, Wire Alpha, Wire First) : BaseOutput(First)
    {
        public Wire Body { get; init; }
    }

    private sealed record Built(SourceTable Table, Wire On, Wire Other);

    private static Built CreateTable()
    {
        var table = new SourceTable();
        var on = table.Declare("on", Cell.StateBit());
        var other = table.Declare("other", Cell.StateBit());
        table.CreateCircuit();
        return new Built(table, on, other);
    }

    // A struct with public fields, or a ValueTuple, has no public properties. The walker would find
    // no leaf, so it refuses the type.
    [Fact]
    public void Next_and_ProgramEvaluator_refuse_a_type_with_public_fields()
    {
        var (table, on, other) = CreateTable();
        var fields = new FieldStruct { On = on };
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>(fields, fields), "FieldStruct holds public fields; use properties"));
        Assert.IsType<ArgumentException>(Js.Throws(() => table.Next<object>((on, other), (on, other)), "ValueTuple`2 holds public fields; use properties"));
        table.Next(new { on, other }, new { on, other });
        var finished = table.Finish();
        Assert.IsType<ArgumentException>(Js.Throws(() => ProgramEvaluator.Create(finished, fields), "FieldStruct holds public fields; use properties"));
        Assert.IsType<ArgumentException>(Js.Throws(() => ProgramEvaluator.Create(finished, (on, other)), "ValueTuple`2 holds public fields; use properties"));
    }

    // A default ImmutableArray holds no array. The walker refuses it with its own message.
    [Fact]
    public void Next_and_ProgramEvaluator_refuse_a_default_ImmutableArray()
    {
        var (table, on, other) = CreateTable();
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next(default(ImmutableArray<Wire>), [on]), "the list is a default ImmutableArray; use an empty array"));
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next(ImmutableArray.Create(on), default), "the list is a default ImmutableArray; use an empty array"));
        Assert.IsType<ArgumentException>(Js.Throws(
            () => table.Next<object>(new { list = ImmutableArray.Create(on) }, new { list = default(ImmutableArray<Wire>) }),
            "the list at list is a default ImmutableArray; use an empty array"));
        table.Next(ImmutableArray.Create(on, other), ImmutableArray.Create(on, other));
        var finished = table.Finish();
        Assert.IsType<ArgumentException>(Js.Throws(
            () => ProgramEvaluator.Create(finished, new { shown = default(ImmutableArray<Wire>) }),
            "the list at shown is a default ImmutableArray; use an empty array"));
    }

    // The outputs of ProgramEvaluator go in MetadataToken order: the properties of the base record
    // first, then the positional properties, then the properties of the record body.
    [Fact]
    public void ProgramEvaluator_orders_the_outputs_of_a_derived_record_base_first_then_positional_then_body()
    {
        var table = new SourceTable();
        var cells = table.Declare("cells", Cell.List(4, _ => Cell.StateBit()));
        table.CreateCircuit();
        table.Next(cells, cells);
        var output = new DerivedOutput(cells[0], cells[1], cells[2]) { Body = cells[3] };
        var evaluator = ProgramEvaluator.Create(table.Finish(), output);
        Assert.Equal<string>(["first", "zeta", "alpha", "body"], evaluator.OutputPaths);
        // first reads cells[2], zeta cells[0], alpha cells[1] and body cells[3].
        Js.AllIs([0, 1, 1, 0], evaluator.Evaluate([1, 1, 0, 0]).Outputs.Select(value => (double)value));
    }
}
