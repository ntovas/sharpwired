// No Handwired source. Handwired has no translator. The per-construct tests of task 3 for
// aggregates, arrays, static readonly fields and conv.r4 (plan, sections "IL opcode table" and
// "Lowering table"). Each test builds the expected circuit by hand, checks AssertSameOperations, and
// compares the translated program with the plain method over 200 seeded source tables.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class AggregateTests
{
    private static CellSpec Ten => Cell.Input(new NumericRange(-10, 10));

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(AggregateInputs), name));

    [Fact]
    public void A_record_struct_getter_reads_the_cell_of_its_backing_field()
    {
        var coordinate = Cell.Input(new NumericRange(-16384, 16384), grid: 1.0 / 64);
        var expected = new ExpectedProgram(Cell.Event(), coordinate, coordinate);
        expected.Circuit.Affine([(expected.Cells[1], 1), (expected.Cells[2], 1)]);
        var translated = Translate(nameof(AggregateInputs.PointerSum));
        Assert.Equal(["active", "x", "y"], translated.Inputs.Select(input => input.Path));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 501, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_record_class_getter_through_callvirt_reads_the_cell_of_its_backing_field()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-1000, 1000)), Cell.Input(new NumericRange(0, 1000)));
        expected.Circuit.Affine([(expected.Cells[0], 1), (expected.Cells[1], 1)]);
        var translated = Translate(nameof(AggregateInputs.RectRight));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 502, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_field_store_into_a_struct_argument_gives_a_new_value_of_the_argument()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-100, 100), grid: 0.25), Cell.Input(new NumericRange(0, 10), grid: 1));
        expected.Circuit.Affine([(expected.Cells[0], 1), (expected.Cells[1], 1)]);
        var translated = Translate(nameof(AggregateInputs.AddToArgument));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 503, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_record_constructor_and_a_tuple_return_give_output_leaves_in_walk_order()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-1000, 1000)), Cell.Input(new NumericRange(0, 1000)));
        var half = expected.Circuit.Affine([(expected.Cells[1], 0.5)]);
        var translated = Translate(nameof(AggregateInputs.MakeRect));
        expected.AssertSameOperations(translated);
        Assert.Equal(["item1.left", "item1.width", "item2"], translated.Outputs.Select(output => output.Path));
        Assert.Equal<Wire>([expected.Cells[0], half, expected.Cells[0]], translated.Outputs.Select(output => output.Wire));
        AssertMatchesPlain(translated, 504, ZeroSign.MayDiffer);
    }

    [Fact]
    public void Initobj_gives_zero_constants_for_each_field()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-100, 100), grid: 1));
        expected.Circuit.Affine([(expected.Cells[0], 0.5)], 1);
        var translated = Translate(nameof(AggregateInputs.ZeroLocal));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 505, ZeroSign.Exact);
    }

    [Fact]
    public void A_constant_index_reads_the_element()
    {
        var expected = new ExpectedProgram(Ten, Ten, Ten);
        expected.Circuit.Affine([(expected.Cells[0], 1), (expected.Cells[2], 1)]);
        var translated = Translate(nameof(AggregateInputs.ConstantIndex));
        Assert.Equal(["w[0]", "w[1]", "w[2]"], translated.Inputs.Select(input => input.Path));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 506, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_computed_index_gives_an_index_read_that_clamps()
    {
        var expected = new ExpectedProgram(Ten, Ten, Ten, Ten, Cell.Input(new NumericRange(0, 3), grid: 1));
        expected.Circuit.IndexRead(expected.Cells[4], expected.Cells[..4], OutOfRange.Clamp);
        var translated = Translate(nameof(AggregateInputs.ComputedIndex));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 507, ZeroSign.Exact);
    }

    // An index read infers no grid, and the elements of an int array are integers, so the translator
    // declares the grid 1 on the read. The int form t[i] + 1 is then exact.
    [Fact]
    public void A_computed_index_into_an_int_array_gives_a_read_on_the_grid_1()
    {
        var integer = Cell.Input(new NumericRange(-100, 100), grid: 1);
        var expected = new ExpectedProgram(integer, integer, integer, Cell.Input(new NumericRange(0, 2), grid: 1));
        var read = expected.Circuit.IndexRead(expected.Cells[3], expected.Cells[..3], OutOfRange.Clamp);
        expected.Circuit.AssumeGrid(read, 1);
        expected.Circuit.Affine([(read, 1)], 1);
        var translated = Translate(nameof(AggregateInputs.IntLookup));
        expected.AssertSameOperations(translated);
        Assert.Equal(1, translated.Table.Circuit.Grids[read.Position]);
        AssertMatchesPlain(translated, 513, ZeroSign.Exact);
    }

    [Fact]
    public void The_length_of_a_fixed_length_array_is_a_constant()
    {
        var expected = new ExpectedProgram(Ten, Ten, Ten, Ten, Ten);
        expected.Circuit.Affine([(expected.Cells[0], 1)], 5);
        var translated = Translate(nameof(AggregateInputs.PlusLength));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 508, ZeroSign.Exact);
    }

    [Fact]
    public void A_new_array_with_constant_stores_gives_its_elements_to_an_index_read()
    {
        var expected = new ExpectedProgram(Ten, Ten, Cell.Input(new NumericRange(0, 1), grid: 1));
        var doubled = expected.Circuit.Affine([(expected.Cells[1], 2)]);
        expected.Circuit.IndexRead(expected.Cells[2], [expected.Cells[0], doubled], OutOfRange.Clamp);
        var translated = Translate(nameof(AggregateInputs.PickBuilt));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 509, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_returned_new_array_gives_one_output_for_each_element()
    {
        var expected = new ExpectedProgram(Ten, Ten);
        var doubled = expected.Circuit.Affine([(expected.Cells[0], 2)]);
        var translated = Translate(nameof(AggregateInputs.BuildArray));
        expected.AssertSameOperations(translated);
        Assert.Equal(["[0]", "[1]"], translated.Outputs.Select(output => output.Path));
        Assert.Equal<Wire>([doubled, expected.Cells[1]], translated.Outputs.Select(output => output.Wire));
        AssertMatchesPlain(translated, 510, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_static_readonly_float_and_table_are_constants_of_the_translation()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 3), grid: 1), Ten);
        var (index, x) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        var read = circuit.IndexRead(index, [circuit.Constant(1), circuit.Constant(2), circuit.Constant(4), circuit.Constant(8)], OutOfRange.Clamp);
        var scaled = circuit.RoundAffine([(x, 0.75)]);
        circuit.RoundAffine([(read, 1), (scaled, 1)]);
        var translated = Translate(nameof(AggregateInputs.StaticLookup));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 511, ZeroSign.MayDiffer);
    }

    [Fact]
    public void Conv_r4_of_an_int_keeps_the_wire()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-100, 100), grid: 1));
        expected.Circuit.Affine([(expected.Cells[0], 0.5)]);
        var translated = Translate(nameof(AggregateInputs.FromInt));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 512, ZeroSign.Exact);
    }
}
