// No Handwired source. Handwired has no translator. The loop tests of task 5 (plan, section
// "Decisions", question 5, the rows of forms 1 and 2 and the row "all"): a constant trip count
// unrolls the body (form 1); a runtime trip count with a maximum from [Range] or [MaxIterations]
// unrolls the maximum number of copies, each copy under its active bit (form 2). Each operation test
// builds the unrolled circuit by hand and checks AssertSameOperations. Each value test compares the
// translated program with the plain method over 200 seeded source tables. The C# loops have the
// condition at the bottom of the IL (the C# for, while and do ... while). The F# loops are for ...
// in, for ... to and while. The IL of each loop below gives the same relation in Debug and in
// Release (clt and blt give Less), so the same expected circuit holds for both configurations.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class LoopTests
{
    private const string LoopFile = "LoopInputs.cs";

    private const string NoMaximumReason =
        "the loop count has no declared maximum; declare [Range] on the count or [MaxIterations] on the method, or translate the loop as a step program (form 3)";

    private static CellSpec Open => Cell.Input(Cell.AnyNumber);

    private static CellSpec Hundred => Cell.Input(new NumericRange(-100, 100));

    private static CellSpec Count(int maximum) => Cell.Input(new NumericRange(0, maximum), grid: 1);

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(LoopInputs), name));

    private static TranslatedFunction TranslateFSharp(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(Sharpwired.IL.FSharpInputs.Loops), name));

    // Form 1.

    // Copy k reads w[k] at a constant index. The sum of copy k emits at the start of copy k + 1 (the
    // loop header is a merge block). 0 + m0 is the wire m0. The last operation of a Multiply keeps its
    // rounding, so m0 + m1 is one ordinary affine. The next sum reads that ordinary affine, which the
    // builder composes, so it is a round affine, and so on.
    [Fact]
    public void A_constant_loop_unrolls_four_copies_of_the_body()
    {
        var expected = new ExpectedProgram(Hundred, Hundred, Hundred, Hundred, Hundred);
        var x = expected.Cells[0];
        ProductChain(expected.Circuit, x, expected.Cells[1..]);
        var translated = Translate(nameof(LoopInputs.WeightedSum));
        expected.AssertSameOperations(translated);
        Assert.Empty(translated.LoopOverflowBits);
        AssertMatchesPlain(translated, 1101, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData(nameof(LoopInputs.RepeatAdd))]
    [InlineData(nameof(LoopInputs.RepeatAddWhile))]
    [InlineData(nameof(LoopInputs.RepeatAddDoWhile))]
    public void Four_copies_of_s_plus_x_in_CSharp_give_the_hand_built_circuit(string name)
    {
        var translated = Translate(name);
        RepeatAddCircuit().AssertSameOperations(translated);
        AssertMatchesPlain(translated, 1102, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData("repeatAddForIn")]
    [InlineData("repeatAddForTo")]
    [InlineData("repeatAddWhile")]
    public void Four_copies_of_s_plus_x_in_FSharp_give_the_circuit_of_the_CSharp_loop(string name)
    {
        var translated = TranslateFSharp(name);
        RepeatAddCircuit().AssertSameOperations(translated);
        TensorChecks.AssertSameOperations(Translate(nameof(LoopInputs.RepeatAdd)).Table.Circuit.Operations, translated.Table.Circuit.Operations);
        AssertMatchesPlain(translated, 1103, ZeroSign.MayDiffer);
    }

    [Fact]
    public void Two_nested_loops_of_3_and_2_copies_give_6_copies_of_the_inner_body()
    {
        var expected = new ExpectedProgram([Hundred, .. Enumerable.Repeat(Hundred, 6)]);
        ProductChain(expected.Circuit, expected.Cells[0], expected.Cells[1..]);
        var translated = Translate(nameof(LoopInputs.Nested));
        expected.AssertSameOperations(translated);
        Assert.Equal(6 * 3 + 5, translated.Table.Circuit.Operations.Count);
        AssertMatchesPlain(translated, 1104, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_loop_with_a_step_of_two_reads_each_second_element()
    {
        var expected = new ExpectedProgram([Hundred, .. Enumerable.Repeat(Hundred, 8)]);
        var w = expected.Cells[1..];
        ProductChain(expected.Circuit, expected.Cells[0], [w[0], w[2], w[4], w[6]]);
        var translated = Translate(nameof(LoopInputs.StepTwo));
        expected.AssertSameOperations(translated);
        Assert.Equal(4 * 3 + 3, translated.Table.Circuit.Operations.Count);
        AssertMatchesPlain(translated, 1105, ZeroSign.MayDiffer);
    }

    // w.Length is the constant 5 (ldlen of a fixed-length array). The cells have no affine producer,
    // so the sums alternate: an ordinary affine of two cells, then a round affine that reads it.
    [Fact]
    public void A_loop_to_the_length_of_a_fixed_length_array_unrolls_one_copy_for_each_element()
    {
        var expected = new ExpectedProgram(Enumerable.Repeat(Hundred, 5).ToArray());
        var w = expected.Cells;
        var circuit = expected.Circuit;
        var a1 = circuit.Affine([(w[0], 1), (w[1], 1)]);
        var r2 = circuit.RoundAffine([(a1, 1), (w[2], 1)]);
        var a3 = circuit.Affine([(r2, 1), (w[3], 1)]);
        circuit.RoundAffine([(a3, 1), (w[4], 1)]);
        var translated = Translate(nameof(LoopInputs.OverLength));
        expected.AssertSameOperations(translated);
        Assert.Equal(4, translated.Table.Circuit.Operations.Count);
        AssertMatchesPlain(translated, 1106, ZeroSign.MayDiffer);
    }

    // The loop is inside a branch. The value after the branch is the gate of the loop result and the
    // value before the branch. The last operation of a Select keeps its rounding, so the difference
    // after it is one ordinary affine.
    [Fact]
    public void A_loop_inside_a_branch_gives_the_gate_of_its_result()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred);
        var (c, x) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        var e1 = circuit.Affine([(x, 1)], 0.5);
        var q1 = circuit.RoundAffine([(e1, 0.5)]);
        var e2 = circuit.RoundAffine([(q1, 1), (x, 1)]);
        var q2 = circuit.RoundAffine([(e2, 0.5)]);
        var e3 = circuit.RoundAffine([(q2, 1), (x, 1)]);
        var gate = circuit.Select(c, circuit.Constant(1), e3);
        circuit.Affine([(gate, 1), (x, -1)]);
        var translated = Translate(nameof(LoopInputs.LoopUnderBranch));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 1107, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_loop_with_more_than_65536_copies_is_refused() =>
        AssertRefused(() => Translate(nameof(LoopInputs.TooMany)), LoopFile, "too-many", "a loop with more than 65536 copies",
            "the program unrolls a loop into at most 65536 copies of its body");

    // Form 2.

    // Copy k computes the bit k < n. The active bit of copy k + 1 is AND(r_k, k < n). The loop
    // counter stays a constant in each copy, because a copy matters only when it is active. The
    // range refinement gives n >= k + 1 on the active path, so in copy 8 the bit 8 < n is the
    // constant 0 and the unroll stops after 8 copies of the body. The result after copy k is
    // Select(r_k, result before, s_k); the first one is Multiply(r_1, x), because the result before
    // copy 1 is the constant 0.
    [Fact]
    public void A_counted_loop_unrolls_eight_copies_with_active_bits()
    {
        var expected = new ExpectedProgram(Count(8), Open);
        var (n, x) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        var c0 = circuit.Compare(circuit.Constant(0), Relation.Less, n);
        var c1 = circuit.Compare(circuit.Constant(1), Relation.Less, n);
        var held = circuit.Multiply(c0, x);
        var active = circuit.Reglu([], 1, [(c0, 1), (c1, 1)], -1);
        var sum = circuit.Affine([(x, 1), (x, 1)]);
        for (var k = 2; k <= 8; k++)
        {
            if (k > 2)
            {
                sum = k % 2 == 1 ? circuit.RoundAffine([(sum, 1), (x, 1)]) : circuit.Affine([(sum, 1), (x, 1)]);
            }

            if (k < 8)
            {
                var condition = circuit.Compare(circuit.Constant(k), Relation.Less, n);
                held = circuit.Select(active, held, sum);
                active = circuit.Reglu([], 1, [(active, 1), (condition, 1)], -1);
            }
            else
            {
                circuit.Select(active, held, sum);
            }
        }

        var translated = Translate(nameof(LoopInputs.CountedSum));
        expected.AssertSameOperations(translated);
        Assert.Empty(translated.LoopOverflowBits);
        AssertMatchesPlain(translated, 1108, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_counted_loop_with_an_array_read_gives_the_plain_values() =>
        AssertMatchesPlain(Translate(nameof(LoopInputs.CountedWeights)), 1109, ZeroSign.MayDiffer);

    // The F# for ... in 0 .. n - 1 loop has a guard n - 1 < 0 before the loop and tests i + 1 != n
    // at the bottom. The NOT of the guard gives n - 1 >= 0 on the active path, and each test removes
    // the lower end of the interval. In the eighth copy the test i + 1 == n is the constant 1, so the
    // loop stops after 8 copies (refinement through !=).
    // The sum s + x has two uses (the header phi and the phi after the loop), so it emits at its
    // instruction, before the test of the copy.
    [Fact]
    public void An_FSharp_for_in_loop_to_n_minus_1_unrolls_eight_copies()
    {
        var expected = new ExpectedProgram(Count(8), Open);
        var (n, x) = (expected.Cells[0], expected.Cells[1]);
        var circuit = expected.Circuit;
        var limit = circuit.Affine([(n, 1)], -1);
        var zero = circuit.Constant(0);
        var guard = circuit.Compare(limit, Relation.Less, zero);
        var e1 = circuit.Compare(circuit.Constant(1), Relation.Equal, n);
        var active = circuit.Affine([(e1, -1)], 1);
        Wire held = x;
        Wire sum = x;
        for (var k = 1; k <= 7; k++)
        {
            sum = k == 1 ? circuit.Affine([(x, 1), (x, 1)]) : k % 2 == 0 ? circuit.RoundAffine([(sum, 1), (x, 1)]) : circuit.Affine([(sum, 1), (x, 1)]);
            Wire? condition = k < 7 ? circuit.Compare(circuit.Constant(k + 1), Relation.Equal, n) : null;
            held = circuit.Select(active, held, sum);
            if (condition is { } bit)
            {
                var notBit = circuit.Affine([(bit, -1)], 1);
                active = circuit.Reglu([], 1, [(active, 1), (notBit, 1)], -1);
            }
        }

        circuit.Select(guard, held, zero);
        var translated = TranslateFSharp("countedSum");
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 1110, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData("countedSumTo")]
    [InlineData("countedSumWhile")]
    public void The_FSharp_counted_loops_give_the_plain_values(string name)
    {
        var translated = TranslateFSharp(name);
        Assert.Empty(translated.LoopOverflowBits);
        AssertMatchesPlain(translated, 1111, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_loop_with_MaxIterations_gives_the_plain_values_and_an_overflow_bit_that_is_0_over_the_range()
    {
        foreach (var translated in new[] { Translate(nameof(LoopInputs.Halve)), TranslateFSharp("halve") })
        {
            var bit = Assert.Single(translated.LoopOverflowBits);
            var probe = ProgramEvaluator.Create<IReadOnlyList<Wire>>(translated.Table, [bit]);
            foreach (var source in SeededSources.Create(translated.Table, SeededTableCount, 1112))
            {
                Assert.Equal(0f, probe.Evaluate(source).Outputs[0]);
            }

            AssertMatchesPlain(translated, 1112, ZeroSign.MayDiffer);
        }
    }

    // [MaxIterations(12)]: 12 copies of the body. For x = 2^20 the loop needs 20 copies. The overflow
    // bit is 1, and the result is the state after 12 copies, 2^20 / 2^12 = 256. The evaluator of the
    // function refuses the evaluation.
    [Fact]
    public void A_count_above_the_maximum_gives_the_overflow_bit_and_the_state_after_the_maximum_copies()
    {
        var translated = Translate(nameof(LoopInputs.HalveWide));
        var bit = Assert.Single(translated.LoopOverflowBits);
        var probe = ProgramEvaluator.Create<IReadOnlyList<Wire>>(translated.Table, [translated.Outputs[0].Wire, bit]);
        Assert.Equal([256f, 1f], probe.Evaluate([1048576f]).Outputs);
        Assert.Equal(1f, probe.Evaluate([4097f]).Outputs[1]);
        foreach (var x in new[] { 0f, 1f, 2f, 3000.5f, 4096f })
        {
            Assert.Equal(0f, probe.Evaluate([x]).Outputs[1]);
            AssertMatchesPlainAt(translated, [x], ZeroSign.MayDiffer);
        }

        var error = Assert.Throws<InvalidOperationException>(() => translated.CreateEvaluator().Evaluate([1048576f]));
        Assert.Equal($"{Site(InputPath(LoopFile), "halve-wide")}: the loop needs more than 12 copies of its body; the overflow bit is 1", error.Message);
    }

    // The loop is in a [Handwired] method that the caller runs only when c is 1. The overflow bit
    // is AND(c, the bit of the loop), so it is 0 when the caller does not run the call.
    [Fact]
    public void The_overflow_bit_of_a_loop_in_an_inlined_method_is_0_when_the_caller_does_not_run_the_call()
    {
        var translated = Translate(nameof(LoopInputs.HalveWhen));
        var bit = Assert.Single(translated.LoopOverflowBits);
        var probe = ProgramEvaluator.Create<IReadOnlyList<Wire>>(translated.Table, [bit]);
        Assert.Equal(0f, probe.Evaluate([0f, 100f]).Outputs[0]);
        Assert.Equal(1f, probe.Evaluate([1f, 100f]).Outputs[0]);
        Assert.Equal(0f, probe.Evaluate([1f, 4f]).Outputs[0]);
        AssertMatchesPlainAt(translated, [0f, 100f], ZeroSign.MayDiffer);
        AssertMatchesPlainAt(translated, [1f, 3f], ZeroSign.MayDiffer);
        var error = Assert.Throws<InvalidOperationException>(() => translated.CreateEvaluator().Evaluate([1f, 100f]));
        Assert.Equal($"{Site(InputPath(LoopFile), "halve-twice")}: the loop needs more than 2 copies of its body; the overflow bit is 1", error.Message);
    }

    // Each copy of a constant loop runs on each call, so a store into a local array in the copy
    // changes the array in place.
    [Fact]
    public void An_array_store_in_a_constant_loop_gives_the_plain_values() =>
        AssertMatchesPlain(Translate(nameof(LoopInputs.StoreInConstantLoop)), 1113, ZeroSign.MayDiffer);

    [Fact]
    public void An_array_store_in_a_loop_with_a_runtime_count_is_refused() =>
        AssertRefused(() => Translate(nameof(LoopInputs.StoreInCountedLoop)), LoopFile, "store-counted",
            "an array store under a runtime condition in a loop", "the program changes an array in place, so a store in a loop must run in each copy of the loop");

    [Fact]
    public void A_step_method_with_a_loop_overflow_bit_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateStep(Input(typeof(LoopInputs), nameof(LoopInputs.HalveStep))), LoopFile, "halve-step",
            "a loop with an overflow bit in a step method", "a step program does not check a loop overflow bit; declare [Range] on the loop count");

    // The row "all" and the loops with no maximum.

    [Fact]
    public void A_loop_with_a_break_is_refused() =>
        AssertRefused(() => Translate(nameof(LoopInputs.Break)), LoopFile, "break", "a loop with two exits",
            "a loop must have one exit edge; remove the break or the return in the loop");

    [Fact]
    public void A_loop_with_no_exit_is_refused() =>
        AssertRefused(() => Translate(nameof(LoopInputs.NoExit)), LoopFile, "no-exit", "a loop with no exit",
            "the program has a fixed size, so each loop must end");

    [Fact]
    public void A_loop_with_no_maximum_is_refused() =>
        AssertRefused(() => Translate(nameof(LoopInputs.NoMaximum)), LoopFile, "no-maximum", "a loop with no maximum", NoMaximumReason);

    [Fact]
    public void An_FSharp_loop_with_no_maximum_is_refused_at_its_line() =>
        AssertRefusedWith(() => TranslateFSharp("noMaximum"),
            RefusalMessage(FrontEndSupport.FSharpInputPath(), "fs-no-maximum", "a loop with no maximum", NoMaximumReason),
            "a loop with no maximum", NoMaximumReason);

    // m_k = Multiply(x, w_k) for each copy; the sums: m0 (no operation), then an ordinary affine and a
    // round affine in turn.
    private static void ProductChain(Circuit circuit, Wire x, Wire[] weights)
    {
        var sum = circuit.Multiply(x, weights[0]);
        for (var k = 1; k < weights.Length; k++)
        {
            var product = circuit.Multiply(x, weights[k]);
            sum = k % 2 == 1 ? circuit.Affine([(sum, 1), (product, 1)]) : circuit.RoundAffine([(sum, 1), (product, 1)]);
        }
    }

    // Four copies of s += x: 0 + x is the wire x; x + x is the exact one-instruction form 2x; the sum
    // that reads 2x composes it, so it is a round affine; the last sum reads a round affine, so it is
    // an ordinary affine of two terms.
    private static ExpectedProgram RepeatAddCircuit()
    {
        var expected = new ExpectedProgram(Open);
        var x = expected.Cells[0];
        var circuit = expected.Circuit;
        var two = circuit.Affine([(x, 1), (x, 1)]);
        var three = circuit.RoundAffine([(two, 1), (x, 1)]);
        circuit.Affine([(three, 1), (x, 1)]);
        return expected;
    }
}
