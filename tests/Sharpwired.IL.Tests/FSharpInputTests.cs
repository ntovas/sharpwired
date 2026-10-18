// No Handwired source. Handwired has no translator. The F# tests of task 4 (plan, section
// "Decisions", question 8). They cover records, struct records, struct tuples, System.Tuple, if,
// and match on constants (the switch opcode). They also cover &&, not, min, max, abs, an inline
// function and a NoInlining helper.
// Each test builds the expected circuit by hand, checks AssertSameOperations, and compares the
// translated program with the plain function over 200 seeded source tables. The F# inputs compile in
// the configuration of the test run, so the same expected circuit holds for the Debug and the
// Release IL.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.FSharpInputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class FSharpInputTests
{
    private static CellSpec Open => Cell.Input(Cell.AnyNumber);

    private static CellSpec Hundred => Cell.Input(new NumericRange(-100, 100));

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(Translation), name));

    [Fact]
    public void A_class_record_reads_the_cells_of_its_fields()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        expected.Circuit.Affine([(expected.Cells[0], 1), (expected.Cells[1], 1)]);
        var translated = Translate("recordSum");
        Assert.Equal(["x", "y"], translated.Inputs.Select(input => input.Path));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 901, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_struct_record_reads_the_cells_of_its_fields()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        expected.Circuit.Multiply(expected.Cells[0], expected.Cells[1]);
        var translated = Translate("structRecordProduct");
        Assert.Equal(["a", "b"], translated.Inputs.Select(input => input.Path));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 902, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_struct_tuple_gives_one_output_for_each_item()
    {
        var expected = new ExpectedProgram(Open, Open);
        var (x, y) = (expected.Cells[0], expected.Cells[1]);
        expected.Circuit.Affine([(x, 1), (y, 1)]);
        expected.Circuit.Affine([(x, 0.5)]);
        var translated = Translate("structTuple");
        Assert.Equal(2, translated.Outputs.Length);
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 903, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_System_Tuple_gives_one_output_for_each_item()
    {
        var expected = new ExpectedProgram(Open, Open);
        var (x, y) = (expected.Cells[0], expected.Cells[1]);
        expected.Circuit.Affine([(x, 1), (y, 1)]);
        expected.Circuit.Affine([(x, 1), (y, -1)]);
        var translated = Translate("systemTuple");
        Assert.Equal(2, translated.Outputs.Length);
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 912, ZeroSign.MayDiffer);
    }

    [Fact]
    public void An_if_expression_gives_the_select_of_the_CSharp_conditional()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Open, Open);
        var (c, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        expected.Circuit.Select(c, y, x);
        var translated = Translate("choose");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 904, ZeroSign.MayDiffer);
    }

    // The switch opcode: the edge of case k reads Compare(n, Equal, k), and the default edge is the
    // last off value.
    [Fact]
    public void A_match_on_constants_gives_one_compare_for_each_case_and_nested_selects()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 4), grid: 1), Open, Open, Open);
        var (n, a, b, c) = (expected.Cells[0], expected.Cells[1], expected.Cells[2], expected.Cells[3]);
        var circuit = expected.Circuit;
        var zero = circuit.Compare(n, Relation.Equal, circuit.Constant(0));
        var one = circuit.Compare(n, Relation.Equal, circuit.Constant(1));
        circuit.Select(zero, circuit.Select(one, c, b), a);
        var translated = Translate("pick");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 905, ZeroSign.MayDiffer);
    }

    // Release writes three brfalse instructions; Debug branches on each partial result. Both give
    // the circuit of the C# chain.
    [Fact]
    public void An_and_chain_gives_one_and_of_three_literals()
    {
        var expected = new ExpectedProgram(Hundred, Hundred, Hundred);
        var (x, y, z) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var circuit = expected.Circuit;
        var gx = circuit.Compare(x, Relation.Greater, circuit.Constant(0));
        var gy = circuit.Compare(y, Relation.Greater, circuit.Constant(0));
        var gz = circuit.Compare(z, Relation.Greater, circuit.Constant(0));
        circuit.Reglu([], 1, [(gx, 1), (gy, 1), (gz, 1)], -2);
        var translated = Translate("allThree");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 906, ZeroSign.Exact);
    }

    [Fact]
    public void An_and_with_not_gives_the_not_then_the_and()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit());
        var (a, b) = (expected.Cells[0], expected.Cells[1]);
        var notB = expected.Circuit.Affine([(b, -1)], 1);
        expected.Circuit.Reglu([], 1, [(a, 1), (notB, 1)], -1);
        var translated = Translate("andNot");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 907, ZeroSign.Exact);
    }

    [Fact]
    public void Min_max_and_abs_give_the_exact_macros()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        var (x, y) = (expected.Cells[0], expected.Cells[1]);
        expected.Circuit.MinimumExact(x, y);
        expected.Circuit.MaximumExact(x, y);
        expected.Circuit.Absolute(x);
        var translated = Translate("minMaxAbs");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 908, ZeroSign.MayDiffer);
    }

    // F# writes the then side first, so its product comes before the maximum of the else side. The
    // C# circuit of BranchTests has the same operations in the other order.
    [Fact]
    public void A_gate_with_operations_on_both_sides_emits_the_sides_in_FSharp_IL_order()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Hundred);
        var (c, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var product = expected.Circuit.Multiply(x, y);
        var maximum = expected.Circuit.MaximumExact(x, y);
        expected.Circuit.Select(c, maximum, product);
        var translated = Translate("bothSides");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 909, ZeroSign.MayDiffer);
    }

    // An inline function is already expanded in the IL. Two instructions with no grid give a
    // RoundAffine chain, as the C# expression x * 2f + 1f does.
    [Fact]
    public void An_inline_function_gives_the_operations_of_its_expanded_body()
    {
        var expected = new ExpectedProgram(Open);
        var doubled = expected.Circuit.RoundAffine([(expected.Cells[0], 2)]);
        expected.Circuit.RoundAffine([(doubled, 1)], 1);
        var translated = Translate("useInline");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 910, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_NoInlining_helper_gives_its_and_at_the_call()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Hundred);
        var (armed, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var circuit = expected.Circuit;
        var c1 = circuit.Compare(x, Relation.GreaterOrEqual, circuit.Constant(0));
        var c2 = circuit.Compare(x, Relation.Less, circuit.Constant(10));
        var c3 = circuit.Compare(y, Relation.GreaterOrEqual, circuit.Constant(0));
        var c4 = circuit.Compare(y, Relation.Less, circuit.Constant(10));
        var inside = circuit.Reglu([], 1, [(c1, 1), (c2, 1), (c3, 1), (c4, 1)], -3);
        circuit.Reglu([], 1, [(armed, 1), (inside, 1)], -1);
        var translated = Translate("armedInside");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 911, ZeroSign.Exact);
    }

    // A gate with two readers emits at its merge block, also when no IL operation reads it (only
    // branches and phis). So its Select comes before the operations of the next statement, as in
    // the C# IL, where the IL and reads the gate.
    [Fact]
    public void A_gate_with_two_readers_emits_at_its_merge_block_when_only_branches_read_it()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit(), Cell.InputBit(), Cell.InputBit(), Hundred, Hundred, Hundred);
        var (c, a, b, d) = (expected.Cells[0], expected.Cells[1], expected.Cells[2], expected.Cells[3]);
        var (x, y, z) = (expected.Cells[4], expected.Cells[5], expected.Cells[6]);
        var circuit = expected.Circuit;
        Wire InsideBox(Wire first, Wire second)
        {
            var c1 = circuit.Compare(first, Relation.GreaterOrEqual, circuit.Constant(0));
            var c2 = circuit.Compare(first, Relation.Less, circuit.Constant(10));
            var c3 = circuit.Compare(second, Relation.GreaterOrEqual, circuit.Constant(0));
            var c4 = circuit.Compare(second, Relation.Less, circuit.Constant(10));
            return circuit.Reglu([], 1, [(c1, 1), (c2, 1), (c3, 1), (c4, 1)], -3);
        }

        var g = circuit.Select(c, a, InsideBox(x, y));
        var inside = InsideBox(z, y);
        circuit.Reglu([], 1, [(b, 1), (g, 1), (inside, 1)], -2);
        var notD = circuit.Affine([(d, -1)], 1);
        circuit.Reglu([], 1, [(g, 1), (notD, 1)], -1);
        var translated = Translate("sharedGate");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 913, ZeroSign.Exact);
    }
}
