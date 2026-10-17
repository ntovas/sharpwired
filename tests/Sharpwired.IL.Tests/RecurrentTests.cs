// No Handwired source. Handwired has no translator. The tests of loop form 3 (plan, task 6, and
// section "Decisions", question 5, the rows of form 3): TranslateRecurrent splits a method with one
// loop with no maximum into a prefix, a step program and a suffix. RecurrentRunner evaluates the
// prefix, the step until done is 1 or until the step limit, and then the suffix. Each value test
// compares the runner with the plain method over seeded argument tables (plan decision 8) and over
// arguments that need many steps.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class RecurrentTests
{
    private const string RecurrentFile = "RecurrentInputs.cs";

    private const int StepLimit = 100_000;

    private const string NoMaximumReason =
        "the loop count has no declared maximum; declare [Range] on the count or [MaxIterations] on the method, or translate the loop as a step program (form 3)";

    private static RecurrentFunction Translate(string name) => HandwiredTranslator.TranslateRecurrent(Input(typeof(RecurrentInputs), name));

    // The row "while (x > 1f) x *= 0.5f; with [Range(0, 1e6)] float x".

    [Fact]
    public void The_runner_of_a_halving_loop_equals_the_plain_method_and_counts_the_plain_iterations()
    {
        var function = Translate(nameof(RecurrentInputs.Halve));
        var runner = new RecurrentRunner(function);
        foreach (var source in Arguments(function, 1201, [1000000f], [1f], [0f], [-0f], [1.5f], [999999.94f]))
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            Assert.Equal(HalveCount(source[0]), result.Steps);
            AssertSameValue(function.EvaluatePlain(source)[0], result.Outputs[0], ZeroSign.Exact, $"x = {source[0]:R}");
        }
    }

    [Fact]
    public void The_runner_stops_at_its_step_limit_and_reports_that_the_loop_did_not_complete()
    {
        var runner = new RecurrentRunner(Translate(nameof(RecurrentInputs.Halve)));
        var result = runner.Run([1000000f], 3);
        Assert.False(result.Completed);
        Assert.Equal(3, result.Steps);

        // The outputs come from the last state: 1000000 halved three times.
        Assert.Equal(125000f, result.Outputs[0]);
    }

    [Fact]
    public void The_runner_completes_when_the_step_limit_holds_the_last_evaluation()
    {
        var runner = new RecurrentRunner(Translate(nameof(RecurrentInputs.Halve)));

        // 8 needs 3 iterations, and the fourth evaluation gives done = 1.
        Assert.False(runner.Run([8f], 3).Completed);
        var result = runner.Run([8f], 4);
        Assert.True(result.Completed);
        Assert.Equal(3, result.Steps);
        Assert.Equal(1f, result.Outputs[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run([8f], -1));
    }

    // The row "int count = 0; while (n > 0) { n -= 3; count++; } return count;".

    [Fact]
    public void The_runner_of_a_count_down_loop_equals_the_plain_method()
    {
        var function = Translate(nameof(RecurrentInputs.CountDown));
        var runner = new RecurrentRunner(function);
        foreach (var source in Arguments(function, 1202, [100f], [0f], [1f], [3f], [99f]))
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            Assert.Equal(function.EvaluatePlain(source)[0], result.Outputs[0]);
            Assert.Equal(result.Outputs[0], result.Steps);
        }
    }

    // n starts in 0..100. The body runs only when n > 0, so it gives n - 3 in -2..97, and the fixed
    // point is -2..100. count grows by 1 in each round, so it has no fixed point in 8 rounds and gets
    // the wide int range.
    [Fact]
    public void The_state_ranges_come_from_the_fixed_point_or_from_the_wide_range()
    {
        var step = Translate(nameof(RecurrentInputs.CountDown)).Step;
        var circuit = step.Table.Circuit;
        Assert.Equal(2, step.State.Length);
        var n = step.State.Single(cell => cell.Path == "n").Wire;
        var count = step.State.Single(cell => cell.Path != "n").Wire;
        Assert.Equal(new NumericRange(-2, 100), circuit.Ranges[n.Position]);
        Assert.Equal(1, circuit.Grids[n.Position]);
        Assert.Equal(new NumericRange(-8_388_608, 8_388_608), circuit.Ranges[count.Position]);
        Assert.Equal(1, circuit.Grids[count.Position]);
    }

    // After the widening of c, m changes, so m also gets the wide range. The runner then equals the
    // plain method, and the steps are n.
    [Fact]
    public void A_cell_that_changes_after_a_widening_also_widens()
    {
        var function = Translate(nameof(RecurrentInputs.Q1Lag));
        var runner = new RecurrentRunner(function);
        foreach (var source in Arguments(function, 1206, [43f], [100f], [21f], [0f]))
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            Assert.Equal((int)source[0], result.Steps);
            Assert.Equal(function.EvaluatePlain(source)[0], result.Outputs[0]);
        }

        Assert.Equal(22f, runner.Run([43f], StepLimit).Outputs[0]);
    }

    [Fact]
    public void A_widening_that_forces_a_second_widening_gives_each_cell_the_wide_range()
    {
        var function = Translate(nameof(RecurrentInputs.ChainLag));
        var circuit = function.Step.Table.Circuit;
        Assert.Equal(4, function.Step.State.Length);
        var wide = new NumericRange(-8_388_608, 8_388_608);
        Assert.Equal(new NumericRange(0, 100), circuit.Ranges[function.Step.State.Single(cell => cell.Path == "local0").Wire.Position]);
        Assert.Equal(3, function.Step.State.Count(cell => circuit.Ranges[cell.Wire.Position] == wide));
        var runner = new RecurrentRunner(function);
        foreach (var source in Arguments(function, 1207, [100f], [27f], [26f], [0f]))
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            Assert.Equal((int)source[0], result.Steps);
            Assert.Equal(function.EvaluatePlain(source)[0], result.Outputs[0]);
        }
    }

    // The step table.

    [Fact]
    public void The_step_table_has_one_state_cell_for_each_loop_variable_and_one_input_cell_for_each_invariant()
    {
        var step = Translate(nameof(RecurrentInputs.Invariant)).Step;

        // The loop variables x and count, and the invariant limit = scale * scale.
        Assert.Equal(2, step.State.Length);
        Assert.Contains(step.State, cell => cell.Path == "x");
        Assert.Single(step.Invariants);
        var validator = step.Table.Validator;
        Assert.Equal(3, validator.CellCount);
        var policies = Enumerable.Range(0, 3).Select(position => step.Table.NextSource[position] == step.Table.Circuit.Source(position)).ToArray();

        // A state cell holds its next value, and an input cell holds itself.
        Assert.Equal([false, false, true], policies);
        Assert.Equal([.. step.State.Select(cell => cell.Wire.Position), step.Invariants[0].Wire.Position], [0, 1, 2]);

        var halve = Translate(nameof(RecurrentInputs.Halve)).Step;
        Assert.Equal(["x"], halve.State.Select(cell => cell.Path));
        Assert.Empty(halve.Invariants);
    }

    [Fact]
    public void A_lane_that_is_done_keeps_its_state_for_three_more_evaluations()
    {
        var function = Translate(nameof(RecurrentInputs.Invariant));
        var prefix = function.Prefix.CreateEvaluator();
        var step = ProgramEvaluator.Create<IReadOnlyList<Wire>>(function.Step.Table, [function.Step.Done]);
        float[] source = [.. prefix.Evaluate([40f, 2f]).Outputs];
        var evaluations = 0;
        while (step.Evaluate(source).Outputs[0] == 0)
        {
            source = [.. step.Evaluate(source).NextValues];
            evaluations++;
            Assert.True(evaluations < 100, "the loop did not end");
        }

        // 40 > 4, 20 > 4, 10 > 4, 5 > 4, and then 2.5 is done.
        Assert.Equal(4, evaluations);
        for (var more = 0; more < 3; more++)
        {
            var evaluation = step.Evaluate(source);
            Assert.Equal(1f, evaluation.Outputs[0]);
            Assert.Equal(source, evaluation.NextValues);
            source = [.. evaluation.NextValues];
        }
    }

    // The values.

    [Fact]
    public void A_value_of_the_code_before_the_loop_is_an_invariant_of_the_step_and_the_suffix()
    {
        var function = Translate(nameof(RecurrentInputs.Invariant));
        AssertRunnerMatchesPlain(function, Arguments(function, 1203, [1000f, 1f], [1000f, 4f], [0f, 1f], [4f, 2f]), ZeroSign.MayDiffer);
    }

    // The step evaluates one pass of the loop from its header. The pass of a do ... while loop holds
    // the body and then the test, so Steps counts the back edges: one less than the body count.
    [Fact]
    public void A_do_while_loop_gives_the_plain_values()
    {
        var function = Translate(nameof(RecurrentInputs.HalveAtLeastOnce));
        var runner = new RecurrentRunner(function);
        foreach (var source in Arguments(function, 1204, [1000000f], [2f], [1f], [0f]))
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            Assert.Equal(DoWhileCount(source[0]) - 1, result.Steps);
            AssertSameValue(function.EvaluatePlain(source)[0], result.Outputs[0], ZeroSign.MayDiffer, $"x = {source[0]:R}");
        }
    }

    [Fact]
    public void A_loop_that_needs_many_steps_gives_the_plain_values()
    {
        var function = Translate(nameof(RecurrentInputs.SlowDecay));
        var result = new RecurrentRunner(function).Run([1000000f], StepLimit);
        Assert.True(result.Completed);
        Assert.True(result.Steps > 2000, $"{result.Steps} steps");
        AssertRunnerMatchesPlain(function, Arguments(function, 1205, 20, [1000000f], [0.001f], [0f]), ZeroSign.MayDiffer);
    }

    // The refusals.

    [Fact]
    public void TranslateFunction_refuses_a_loop_with_no_maximum_at_its_line() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve))),
            RecurrentFile, "halve", "a loop with no maximum", NoMaximumReason);

    [Fact]
    public void Two_loops_with_no_maximum_are_refused() =>
        AssertRefused(() => Translate(nameof(RecurrentInputs.TwoLoops)), RecurrentFile, "second-loop", "a second loop with no maximum",
            "a step program holds one loop with no maximum; declare a maximum for each other loop");

    [Fact]
    public void A_loop_with_no_maximum_inside_a_branch_is_refused() =>
        AssertRefused(() => Translate(nameof(RecurrentInputs.LoopInBranch)), RecurrentFile, "loop-in-branch", "a loop with no maximum inside a branch",
            "the loop of a step program must run on each call; move the loop out of the branch");

    [Fact]
    public void A_loop_with_no_maximum_inside_a_loop_is_refused() =>
        AssertRefused(() => Translate(nameof(RecurrentInputs.LoopInLoop)), RecurrentFile, "loop-in-loop", "a loop with no maximum inside a loop",
            "the loop of a step program must be at the top level of the method; declare a maximum for the inner loop");

    // x *= 2 has no fixed point in 8 rounds, because the float state has no refinement by the continue
    // bit. No widened float cell can translate: over -float.MaxValue..float.MaxValue the range of the
    // values of the step, for example Select(c, state, back), is not finite, and decision 11 refuses it.
    [Fact]
    public void A_state_with_no_fixed_point_whose_body_passes_the_fp32_range_is_refused() =>
        AssertRefused(() => Translate(nameof(RecurrentInputs.Grow)), RecurrentFile, "grow", "a value past the FP32 range",
            "the value can overflow FP32; declare smaller ranges");

    [Fact]
    public void A_method_with_no_loop_of_form_3_is_refused_as_an_argument()
    {
        var error = Assert.Throws<ArgumentException>(() => Translate(nameof(RecurrentInputs.NoLoop)));
        Assert.IsNotType<TranslationException>(error);
        Assert.Equal("the method RecurrentInputs.NoLoop has no loop with no maximum; use TranslateFunction", error.Message);
    }

    private static void AssertRunnerMatchesPlain(RecurrentFunction function, IEnumerable<float[]> sources, ZeroSign zeroSign)
    {
        var runner = new RecurrentRunner(function);
        foreach (var source in sources)
        {
            var result = runner.Run(source, StepLimit);
            Assert.True(result.Completed);
            var plain = function.EvaluatePlain(source);
            Assert.Equal(plain.Length, result.Outputs.Length);
            for (var leaf = 0; leaf < plain.Length; leaf++)
            {
                AssertSameValue(plain[leaf], result.Outputs[leaf], zeroSign, $"output {leaf} for [{string.Join(", ", source)}]");
            }
        }
    }

    // The seeded argument tables of the prefix, then the extra tables.
    private static IEnumerable<float[]> Arguments(RecurrentFunction function, int seed, params float[][] extra) =>
        Arguments(function, seed, SeededTableCount, extra);

    private static IEnumerable<float[]> Arguments(RecurrentFunction function, int seed, int count, params float[][] extra) =>
        SeededSources.Create(function.Prefix.Table, count, seed).Concat(extra);

    private static int HalveCount(float x)
    {
        var count = 0;
        while (x > 1f)
        {
            x *= 0.5f;
            count++;
        }

        return count;
    }

    private static int DoWhileCount(float x)
    {
        var count = 0;
        do
        {
            x *= 0.5f;
            count++;
        }
        while (x > 1f);
        return count;
    }
}
