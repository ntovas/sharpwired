// No Handwired source. Handwired has no translator. The per-construct tests of task 3 for
// arithmetic, comparisons, bits and the Math methods. Each test builds the expected circuit by hand
// with the builder calls of the plan (sections "Emission rules", "Lowering table" and "Decisions",
// question 4), checks AssertSameOperations, and compares the translated program with the plain
// method over 200 seeded source tables. The same expected circuit holds for the Debug and the
// Release IL.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class ArithmeticTests
{
    private static CellSpec Open => Cell.Input(Cell.AnyNumber);

    private static CellSpec Hundred => Cell.Input(new NumericRange(-100, 100));

    private static CellSpec IntHundred => Cell.Input(new NumericRange(-100, 100), grid: 1);

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(ArithmeticInputs), name));

    [Fact]
    public void An_exact_affine_chain_gives_one_affine()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 16384), grid: 1));
        expected.Circuit.Affine([(expected.Cells[0], 0.5)], -26);
        var translated = Translate(nameof(ArithmeticInputs.ExactChain));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 401, ZeroSign.Exact);
    }

    [Fact]
    public void An_inexact_chain_gives_one_round_affine_for_each_instruction_in_CSharp_order()
    {
        var expected = new ExpectedProgram(Open, Open, Open);
        var (a, b, c) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var sum = expected.Circuit.RoundAffine([(a, 1), (b, 1)]);
        expected.Circuit.RoundAffine([(sum, 1), (c, 1)]);
        var translated = Translate(nameof(ArithmeticInputs.InexactChain));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 402, ZeroSign.MayDiffer);
    }

    [Fact]
    public void One_CSharp_operation_with_no_grid_gives_one_ordinary_affine()
    {
        var expected = new ExpectedProgram(Open);
        expected.Circuit.Affine([(expected.Cells[0], 0.3f)]);
        var translated = Translate(nameof(ArithmeticInputs.ScaleByPointThree));
        expected.AssertSameOperations(translated);
        Assert.False(translated.Table.Circuit.Operations[0].PreserveRounding);
        AssertMatchesPlain(translated, 403, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_division_by_four_gives_the_coefficient_one_quarter()
    {
        var expected = new ExpectedProgram(Open);
        expected.Circuit.Affine([(expected.Cells[0], 0.25)]);
        var translated = Translate(nameof(ArithmeticInputs.DivideByFour));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 404, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_negation_then_a_sum_with_no_grid_gives_two_round_affines()
    {
        var expected = new ExpectedProgram(Open);
        var negated = expected.Circuit.RoundAffine([(expected.Cells[0], -1)]);
        expected.Circuit.RoundAffine([(negated, 1)], 3);
        var translated = Translate(nameof(ArithmeticInputs.NegateAndShift));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 405, ZeroSign.Exact);
    }

    // The form x × 0.3 has two uses, so it emits at its definition, before the product x × y. The
    // builder would compose it into the sum that reads it, so the sum is a RoundAffine.
    [Fact]
    public void A_form_with_two_uses_emits_at_its_definition_and_a_composed_reader_rounds()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        var (x, y) = (expected.Cells[0], expected.Cells[1]);
        var shared = expected.Circuit.Affine([(x, 0.3f)]);
        var product = expected.Circuit.Multiply(x, y);
        var scaled = expected.Circuit.Multiply(product, shared);
        expected.Circuit.RoundAffine([(shared, 1), (scaled, 1)]);
        var translated = Translate(nameof(ArithmeticInputs.SharedForm));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 406, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_float_product_of_two_wires_gives_multiply()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        expected.Circuit.Multiply(expected.Cells[0], expected.Cells[1]);
        var translated = Translate(nameof(ArithmeticInputs.FloatProduct));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 407, ZeroSign.MayDiffer);
    }

    [Fact]
    public void An_int_product_within_2_to_the_24_gives_multiply()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-1000, 1000), grid: 1), Cell.Input(new NumericRange(0, 1000), grid: 1));
        expected.Circuit.Multiply(expected.Cells[0], expected.Cells[1]);
        var translated = Translate(nameof(ArithmeticInputs.IntProduct));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 408, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData(nameof(ArithmeticInputs.FloatRelations), false, 409)]
    [InlineData(nameof(ArithmeticInputs.IntRelations), true, 410)]
    public void The_six_relations_give_compare_and_not_equal_gives_not_of_equal(string name, bool integer, int seed)
    {
        var expected = new ExpectedProgram(integer ? IntHundred : Hundred, integer ? IntHundred : Hundred);
        var (a, b) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        circuit.Compare(a, Relation.Greater, b);
        circuit.Compare(a, Relation.GreaterOrEqual, b);
        circuit.Compare(a, Relation.Less, b);
        circuit.Compare(a, Relation.LessOrEqual, b);
        circuit.Compare(a, Relation.Equal, b);
        var equal = circuit.Compare(a, Relation.Equal, b);
        circuit.Affine([(equal, -1)], 1);
        var translated = Translate(name);
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, seed, ZeroSign.Exact);
    }

    // The comparison a == b emits at its instruction. NOT, AND, OR and XOR have no wire, so they emit
    // at the return in walk order.
    [Fact]
    public void Not_and_or_xor_and_equality_on_bits_give_the_bit_forms()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit());
        var (a, b) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        circuit.Compare(a, Relation.Equal, b);
        circuit.Affine([(a, -1)], 1);
        circuit.Reglu([], 1, [(a, 1), (b, 1)], -1);
        circuit.Affine([(circuit.Reglu([], 1, [(a, -1), (b, -1)], 1), -1)], 1);
        circuit.Affine([(circuit.Reglu([], 1, [(a, 1), (b, -1)], 0), 1), (circuit.Reglu([], 1, [(b, 1), (a, -1)], 0), 1)]);
        var translated = Translate(nameof(ArithmeticInputs.BitOperations));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 411, ZeroSign.Exact);
    }

    [Fact]
    public void An_and_of_an_and_gives_one_and_with_the_literals_in_order()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit(), Cell.InputBit());
        var (a, b, c) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var notB = expected.Circuit.Affine([(b, -1)], 1);
        expected.Circuit.Reglu([], 1, [(a, 1), (notB, 1), (c, 1)], -2);
        var translated = Translate(nameof(ArithmeticInputs.ThreeWayAnd));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 412, ZeroSign.Exact);
    }

    [Fact]
    public void The_Math_methods_give_the_exact_macros()
    {
        var expected = new ExpectedProgram(Hundred, Hundred, Cell.Input(new NumericRange(-10, 0)), Cell.Input(new NumericRange(1, 10), initial: 1));
        var (a, b, lo, hi) = (expected.Cells[0], expected.Cells[1], expected.Cells[2], expected.Cells[3]);
        var circuit = expected.Circuit;
        circuit.MinimumExact(a, b);
        circuit.MaximumExact(a, b);
        circuit.ClampExact(a, lo, hi);
        circuit.Absolute(a);
        circuit.MinimumExact(a, b);
        circuit.MaximumExact(a, b);
        var translated = Translate(nameof(ArithmeticInputs.MathCalls));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 413, ZeroSign.MayDiffer);
    }

    [Fact]
    public void Math_Min_on_int_gives_minimum_exact()
    {
        var expected = new ExpectedProgram(IntHundred, IntHundred);
        expected.Circuit.MinimumExact(expected.Cells[0], expected.Cells[1]);
        var translated = Translate(nameof(ArithmeticInputs.IntMinimum));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 414, ZeroSign.MayDiffer);
    }

    // Review of task 3: the stored form of (a + b) - b is the exact form a, but C# rounds a + b.
    [Fact]
    public void A_form_whose_terms_cancel_after_a_rounded_sum_gives_a_round_affine_chain()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 10), grid: 1), Cell.Input(new NumericRange(-1e9, 1e9)));
        var (a, b) = (expected.Cells[0], expected.Cells[1]);
        var sum = expected.Circuit.RoundAffine([(a, 1), (b, 1)]);
        expected.Circuit.RoundAffine([(sum, 1), (b, -1)]);
        var translated = Translate(nameof(ArithmeticInputs.CancelledSum));
        expected.AssertSameOperations(translated);
        AssertMatchesPlainAt(translated, [1f, 1e8f], ZeroSign.MayDiffer);
        AssertMatchesPlain(translated, 415, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_form_whose_constant_folds_after_a_rounded_sum_gives_a_round_affine_chain()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 1e6)));
        var x = expected.Cells[0];
        var sum = expected.Circuit.RoundAffine([(x, 1)], 0.1f);
        expected.Circuit.RoundAffine([(sum, 1), (x, -1)]);
        var translated = Translate(nameof(ArithmeticInputs.CancelledConstant));
        expected.AssertSameOperations(translated);
        AssertMatchesPlainAt(translated, [1000f], ZeroSign.MayDiffer);
        AssertMatchesPlain(translated, 416, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_form_with_a_product_past_2_to_the_24_steps_gives_a_round_affine_chain()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 16777216), grid: 1));
        var x = expected.Cells[0];
        var three = expected.Circuit.RoundAffine([(x, 3)]);
        var two = expected.Circuit.RoundAffine([(x, 2)]);
        expected.Circuit.RoundAffine([(three, 1), (two, -1)]);
        var translated = Translate(nameof(ArithmeticInputs.CancelledProducts));
        expected.AssertSameOperations(translated);
        AssertMatchesPlainAt(translated, [16777215f], ZeroSign.MayDiffer);
        AssertMatchesPlain(translated, 417, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_chain_with_exact_intermediate_values_gives_one_affine()
    {
        var grid = Cell.Input(new NumericRange(0, 1000), grid: 1);
        var expected = new ExpectedProgram(grid, grid);
        expected.Circuit.Affine([(expected.Cells[0], 0.5), (expected.Cells[1], 0.5)], -3);
        var translated = Translate(nameof(ArithmeticInputs.ExactTwoSteps));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 418, ZeroSign.MayDiffer);
    }
}
