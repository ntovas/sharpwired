// Handwired source: $REF/lab/ui-exploration/guide/sections/loops.md, section 3 ("Prefix sums three
// ways") and section "The shapes of cost", and $REF/docs/expressions.md, line 48. The tests of plan
// task 6b for running totals: a loop of form 1 or 2 whose accumulator takes an associative combine
// in each step lowers to PrefixNetworks.Sklansky. Each test translates the input two times: with the
// pattern recognition (the default) and with TranslationOptions.RecognizePatterns false (the normal
// form). The prefix network counts follow PrefixNetworkTests: k items take (k / 2) × log2(k) combines
// in log2(k) layers when k is a power of two.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class RunningTotalTests
{
    private const string PatternFile = "PatternInputs.cs";

    private static CellSpec Thousand(double? grid) => Cell.Input(new NumericRange(-1000, 1000), grid: grid);

    // Form 1.

    // 64 cells and a store of each prefix: the Sklansky network of the cells, one RoundAffine for each
    // combine. The value 0 before the loop is the identity of +, so the network starts at x[0].
    [Fact]
    public void A_running_sum_with_a_store_in_each_step_gives_the_Sklansky_network_of_its_items()
    {
        var expected = new ExpectedProgram([.. Enumerable.Repeat(Thousand(0.25), 64)]);
        var circuit = expected.Circuit;
        PrefixNetworks.Sklansky(expected.Cells, (earlier, later) => circuit.RoundAffine([(earlier, 1), (later, 1)]));
        var translated = PatternSupport.Translate(nameof(PatternInputs.RunningSum));
        expected.AssertSameOperations(translated);
        Assert.Equal((32 * 6, 6), PatternSupport.Size(translated));
        AssertMatchesPlain(translated, 6201, ZeroSign.MayDiffer);
    }

    // The chain of the normal form has 63 sums (the builder composes the small ones), so the network
    // has more operations. It has fewer layers: 6 against the depth of the chain.
    [Fact]
    public void A_running_sum_in_form_1_has_fewer_layers_than_its_normal_form()
    {
        var recognized = PatternSupport.Size(PatternSupport.Translate(nameof(PatternInputs.RunningSum)));
        var normal = PatternSupport.Translate(nameof(PatternInputs.RunningSum), recognize: false);
        Assert.True(recognized.Layers < PatternSupport.Size(normal).Layers, $"recognized {recognized}, normal {PatternSupport.Size(normal)}");
        AssertMatchesPlain(normal, 6201, ZeroSign.MayDiffer);
    }

    // minimums[i] = Math.Min(minimums[i - 1], x[i]). The value of minimums[0] before the loop, x[0],
    // is the first item: 8 items give 12 MinimumExact combines in 3 levels.
    [Fact]
    public void An_array_element_that_reads_the_element_of_the_step_before_gives_the_network()
    {
        var expected = new ExpectedProgram([.. Enumerable.Repeat(Thousand(1), 8)]);
        var circuit = expected.Circuit;
        PrefixNetworks.Sklansky(expected.Cells, circuit.MinimumExact);
        var translated = PatternSupport.Translate(nameof(PatternInputs.RunningMinimumInArray));
        expected.AssertSameOperations(translated);
        var normal = PatternSupport.Size(PatternSupport.Translate(nameof(PatternInputs.RunningMinimumInArray), recognize: false));
        Assert.True(PatternSupport.Size(translated).Layers < normal.Layers, $"recognized {PatternSupport.Size(translated)}, normal {normal}");
        AssertMatchesPlain(translated, 6202, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData(nameof(PatternInputs.RowTops), true)]
    [InlineData(nameof(PatternInputs.RunningAll), true)]
    [InlineData(nameof(PatternInputs.RunningMinimum), false)]
    [InlineData(nameof(PatternInputs.RunningMaximumFirst), false)]
    [InlineData(nameof(PatternInputs.RunningLarger), false)]
    public void Each_associative_combine_gives_fewer_layers_than_its_normal_form_and_the_plain_values(string name, bool exactZero)
    {
        var zeroSign = exactZero ? ZeroSign.Exact : ZeroSign.MayDiffer;
        var recognized = PatternSupport.Translate(name);
        var normal = PatternSupport.Translate(name, recognize: false);
        Assert.True(PatternSupport.Size(recognized).Layers < PatternSupport.Size(normal).Layers,
            $"recognized {PatternSupport.Size(recognized)}, normal {PatternSupport.Size(normal)}");
        AssertMatchesPlain(recognized, 6203, zeroSign);
    }

    // Math.Min of 16 values on a grid: the network has 32 MinimumExact combines in 4 levels. The
    // value 1000 before the loop is at or above the range of each item, so it is the identity of Min.
    [Fact]
    public void A_running_minimum_gives_the_Sklansky_network_of_MinimumExact()
    {
        var expected = new ExpectedProgram([.. Enumerable.Repeat(Thousand(1), 16)]);
        var circuit = expected.Circuit;
        PrefixNetworks.Sklansky(expected.Cells, circuit.MinimumExact);
        expected.AssertSameOperations(PatternSupport.Translate(nameof(PatternInputs.RunningMinimum)));
    }

    // A loop that looks like a running total keeps its normal form: the same operations with and
    // without the pattern recognition.
    [Theory]
    [InlineData(nameof(PatternInputs.RunningDifference), false)]
    [InlineData(nameof(PatternInputs.RunningSumInexact), false)]
    [InlineData(nameof(PatternInputs.RunningDouble), false)]
    [InlineData(nameof(PatternInputs.RunningSumInArray), false)]
    [InlineData(nameof(PatternInputs.RunningSumSkip), false)]
    [InlineData(nameof(PatternInputs.RunningMinimumSkip), false)]
    [InlineData(nameof(PatternInputs.RunningSmallerUnmarked), false)]
    [InlineData(nameof(PatternInputs.CountedLarger), false)]
    public void A_loop_with_no_running_total_pattern_keeps_its_normal_form(string name, bool exactZero)
    {
        var zeroSign = exactZero ? ZeroSign.Exact : ZeroSign.MayDiffer;
        var recognized = PatternSupport.Translate(name);
        var normal = PatternSupport.Translate(name, recognize: false);
        TensorChecks.AssertSameOperations(normal.Table.Circuit.Operations, recognized.Table.Circuit.Operations);
        AssertMatchesPlain(recognized, 6204, zeroSign);
    }

    // Form 2.

    // The normal form keeps the sum with a Select in each copy. The network adds the items gated by
    // the active bits: Multiply(active, x[i]) gives x[i] or 0, and 0 is the identity of +.
    [Theory]
    [InlineData(nameof(PatternInputs.CountedTotal), false)]
    [InlineData(nameof(PatternInputs.CountedMaximum), false)]
    [InlineData(nameof(PatternInputs.CountedAny), true)]
    [InlineData(nameof(PatternInputs.CountedAll), true)]
    public void A_running_total_in_form_2_gives_the_plain_values(string name, bool exactZero)
    {
        var zeroSign = exactZero ? ZeroSign.Exact : ZeroSign.MayDiffer;
        var recognized = PatternSupport.Translate(name);
        Assert.True(PatternSupport.Size(recognized).Layers < PatternSupport.Size(PatternSupport.Translate(name, recognize: false)).Layers);
        AssertMatchesPlain(recognized, 6205, zeroSign);
        var n = recognized.Table.Circuit.Ranges[0];
        for (var count = (int)n.Minimum; count <= n.Maximum; count++)
        {
            var source = SeededSources.Create(recognized.Table, 1, 6206 + count)[0];
            source[0] = count;
            AssertMatchesPlainAt(recognized, source, zeroSign);
        }
    }

    [Fact]
    public void A_running_sum_in_form_2_has_fewer_operations_and_layers_than_its_normal_form()
    {
        var recognized = PatternSupport.Size(PatternSupport.Translate(nameof(PatternInputs.CountedTotal)));
        var normal = PatternSupport.Size(PatternSupport.Translate(nameof(PatternInputs.CountedTotal), recognize: false));
        Assert.True(recognized.Operations < normal.Operations, $"recognized {recognized}, normal {normal}");
        Assert.True(recognized.Layers < normal.Layers, $"recognized {recognized}, normal {normal}");
    }

    // 200 copies with a store under the active bit of each copy. The normal form refuses the store.
    [Fact]
    public void A_running_sum_of_200_elements_with_a_runtime_count_gives_the_plain_values()
    {
        var translated = PatternSupport.Translate(nameof(PatternInputs.RunningSumCounted));
        AssertMatchesPlain(translated, 6207, ZeroSign.MayDiffer);
        var source = SeededSources.Create(translated.Table, 1, 6208)[0];
        foreach (var count in new[] { 0, 1, 127, 128, 199, 200 })
        {
            source[0] = count;
            AssertMatchesPlainAt(translated, source, ZeroSign.MayDiffer);
        }

        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.RunningSumCounted), recognize: false), PatternFile, "running-counted-store",
            "an array store under a runtime condition in a loop", "the program changes an array in place, so a store in a loop must run in each copy of the loop");
    }
}

/// <summary>The translation with and without the pattern recognition, and the size of a compiled program.</summary>
internal static class PatternSupport
{
    public static TranslatedFunction Translate(string name, bool recognize = true) =>
        HandwiredTranslator.TranslateFunction(Input(typeof(PatternInputs), name), new TranslationOptions { RecognizePatterns = recognize });

    /// <summary>The live operations (the operations that an output needs) and the layers of the compiled program.</summary>
    public static (int Operations, int Layers) Size(TranslatedFunction translated)
    {
        var program = translated.CreateEvaluator().Program;
        return (program.ValueCount - program.SourceSize, program.Layers.Length);
    }
}
