// No Handwired source. Handwired has no translator. The per-construct tests of task 4 for branch to
// select (plan, section "Decisions", question 4): each phi becomes a gate, the gate rules simplify
// it, and the gate emits as an affine form, a Multiply, a Select, a NOT, an AND or an OR. Each test
// builds the expected circuit by hand, checks AssertSameOperations, and compares the translated
// program with the plain method over 200 seeded source tables. The same expected circuit holds for
// the Debug and the Release IL.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class BranchTests
{
    private static CellSpec Open => Cell.Input(Cell.AnyNumber);

    private static CellSpec Hundred => Cell.Input(new NumericRange(-100, 100));

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(BranchInputs), name));

    [Fact]
    public void A_conditional_on_floats_gives_select_with_the_off_value_first()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Open, Open);
        var (c, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        expected.Circuit.Select(c, y, x);
        var translated = Translate(nameof(BranchInputs.Choose));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 701, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData(nameof(BranchInputs.ChooseConstants), 20, 0)]
    [InlineData(nameof(BranchInputs.ChooseOffsetConstants), 1.5, 1.5)]
    public void A_conditional_on_two_constants_gives_one_affine(string name, double step, double bias)
    {
        var expected = new ExpectedProgram(Cell.InputBit());
        expected.Circuit.Affine([(expected.Cells[0], step)], bias);
        var translated = Translate(name);
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 702, ZeroSign.Exact);
    }

    [Fact]
    public void A_conditional_with_zero_off_gives_multiply()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred);
        expected.Circuit.Multiply(expected.Cells[0], expected.Cells[1]);
        var translated = Translate(nameof(BranchInputs.ChooseOrZero));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 703, ZeroSign.MayDiffer);
    }

    // The affine form 2x has one use, so it emits when the Select reads it, after the off value.
    [Fact]
    public void An_if_statement_gives_select_of_the_old_and_the_new_value()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Open);
        var (c, x) = (expected.Cells[0], expected.Cells[1]);
        expected.Circuit.Select(c, x, expected.Circuit.Affine([(x, 2)]));
        var translated = Translate(nameof(BranchInputs.DoubleWhen));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 704, ZeroSign.MayDiffer);
    }

    [Fact]
    public void An_and_chain_as_a_value_gives_one_and_of_three_literals()
    {
        var expected = new ExpectedProgram(Hundred, Hundred, Hundred);
        var (x, y, z) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var circuit = expected.Circuit;
        var gx = circuit.Compare(x, Relation.Greater, circuit.Constant(0));
        var gy = circuit.Compare(y, Relation.Greater, circuit.Constant(0));
        var gz = circuit.Compare(z, Relation.Greater, circuit.Constant(0));
        circuit.Reglu([], 1, [(gx, 1), (gy, 1), (gz, 1)], -2);
        var translated = Translate(nameof(BranchInputs.AllThree));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 705, ZeroSign.Exact);
    }

    [Fact]
    public void An_and_chain_as_a_branch_condition_gives_one_and_then_select()
    {
        var expected = new ExpectedProgram(Hundred, Hundred, Hundred, Open, Open);
        var (x, y, z, a, b) = (expected.Cells[0], expected.Cells[1], expected.Cells[2], expected.Cells[3], expected.Cells[4]);
        var circuit = expected.Circuit;
        var gx = circuit.Compare(x, Relation.Greater, circuit.Constant(0));
        var gy = circuit.Compare(y, Relation.Greater, circuit.Constant(0));
        var gz = circuit.Compare(z, Relation.Greater, circuit.Constant(0));
        var all = circuit.Reglu([], 1, [(gx, 1), (gy, 1), (gz, 1)], -2);
        circuit.Select(all, b, a);
        var translated = Translate(nameof(BranchInputs.AllThreeBranch));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 706, ZeroSign.MayDiffer);
    }

    [Fact]
    public void An_and_with_a_not_gives_the_not_then_the_and()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit());
        var (a, b) = (expected.Cells[0], expected.Cells[1]);
        var notB = expected.Circuit.Affine([(b, -1)], 1);
        expected.Circuit.Reglu([], 1, [(a, 1), (notB, 1)], -1);
        var translated = Translate(nameof(BranchInputs.AndNot));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 707, ZeroSign.Exact);
    }

    [Fact]
    public void An_or_chain_gives_the_or_form()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        var (x, y) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        var gx = circuit.Compare(x, Relation.Greater, circuit.Constant(0));
        var gy = circuit.Compare(y, Relation.Greater, circuit.Constant(0));
        circuit.Affine([(circuit.Reglu([], 1, [(gx, -1), (gy, -1)], 1), -1)], 1);
        var translated = Translate(nameof(BranchInputs.EitherAbove));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 708, ZeroSign.Exact);
    }

    // C# writes the else side first, so its maximum comes before the product of the then side.
    [Fact]
    public void A_gate_with_operations_on_both_sides_emits_the_sides_in_CSharp_IL_order()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Hundred);
        var (c, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var maximum = expected.Circuit.MaximumExact(x, y);
        var product = expected.Circuit.Multiply(x, y);
        expected.Circuit.Select(c, maximum, product);
        var translated = Translate(nameof(BranchInputs.BothSides));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 709, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_struct_that_a_branch_changes_gives_one_select_for_each_field()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Hundred);
        var (c, a, b) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        expected.Circuit.Select(c, a, b);
        expected.Circuit.Select(c, b, a);
        var translated = Translate(nameof(BranchInputs.SwapWhen));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 710, ZeroSign.MayDiffer);
    }

    // Release writes bne.un to the false side and Debug writes ceq; both give Compare(Equal).
    [Fact]
    public void An_int_equality_branch_gives_compare_equal_and_select()
    {
        var count = Cell.Input(new NumericRange(0, 8), grid: 1);
        var expected = new ExpectedProgram(count, count);
        var (i, n) = (expected.Cells[0], expected.Cells[1]);
        var equal = expected.Circuit.Compare(i, Relation.Equal, n);
        expected.Circuit.Select(equal, n, expected.Circuit.Affine([(i, 1)], 1));
        var translated = Translate(nameof(BranchInputs.IntEqual));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 711, ZeroSign.MayDiffer);
    }

    // The builder toggle calls of plan question 1: Select(Down.Active, Armed, InsideTrack(Down)),
    // Select(All(Up.Active, armedAfterDown, InsideTrack(Up)), On, Not(On)),
    // All(armedAfterDown, Not(Up.Active), Not(Cancel)) and Affine([(on, Travel)]).
    [Fact]
    public void The_toggle_statements_give_the_builder_toggle_calls()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Cell.InputBit(), Hundred, Cell.InputBit(), Cell.InputBit(), Cell.InputBit());
        var (down, downX, up, upX) = (expected.Cells[0], expected.Cells[1], expected.Cells[2], expected.Cells[3]);
        var (cancel, stateOn, stateArmed) = (expected.Cells[4], expected.Cells[5], expected.Cells[6]);
        var circuit = expected.Circuit;
        var armedAfterDown = circuit.Select(down, stateArmed, circuit.Compare(downX, Relation.Greater, circuit.Constant(0)));
        var insideUp = circuit.Compare(upX, Relation.Greater, circuit.Constant(0));
        var on = circuit.Select(circuit.Reglu([], 1, [(up, 1), (armedAfterDown, 1), (insideUp, 1)], -2), stateOn, circuit.Affine([(stateOn, -1)], 1));
        var notUp = circuit.Affine([(up, -1)], 1);
        var notCancel = circuit.Affine([(cancel, -1)], 1);
        circuit.Reglu([], 1, [(armedAfterDown, 1), (notUp, 1), (notCancel, 1)], -2);
        circuit.Affine([(on, 20)]);
        var translated = Translate(nameof(BranchInputs.ToggleGates));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 712, ZeroSign.Exact);
    }
}
