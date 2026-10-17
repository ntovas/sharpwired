// Handwired source: $REF/docs/expressions.md, rows 14 and 15 of the table "Familiar expressions"
// (Math.floor(x / step) and i % count with few results; Math.floor(i / 64) and i % 64 with many
// results) and line 48 (a division by a runtime value with few values), and
// $REF/lab/ui-exploration/guide/patterns/threshold-counts.md. The tests of plan task 6b for threshold
// counts: a floor, an int division or an int remainder of a value on a grid with K possible results
// gives K - 1 comparisons and one sum in 3 layers. With more than 64 results, an int value gives an
// index read over a table of the results. Each value test checks each grid point of the range.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class ThresholdCountTests
{
    private const string PatternFile = "PatternInputs.cs";

    private const string DivisionReason = "the program has no exact integer division or remainder";

    // floor(width / 244) for width in 244..1708 has the 7 results 1..7: 6 thresholds 488, 732, ...,
    // 1708. Each comparison with a constant on a grid is an integer bit of 2 operations, so the count
    // is 13 operations in 3 layers, as the column count of threshold-counts.md.
    [Fact]
    public void A_floor_of_a_quotient_with_7_results_gives_6_comparisons_and_one_sum()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(244, 1708), initial: 244, grid: 1));
        var circuit = expected.Circuit;
        var width = expected.Cells[0];
        var bits = Enumerable.Range(2, 6).Select(count => circuit.Compare(width, Relation.GreaterOrEqual, circuit.Constant(244 * count))).ToArray();
        circuit.Affine([.. bits.Select(bit => new Term(bit, 1))], 1);
        var translated = PatternSupport.Translate(nameof(PatternInputs.ColumnCount));
        expected.AssertSameOperations(translated);
        Assert.Equal((13, 3), PatternSupport.Size(translated));
        AssertEachGridPoint(translated, ZeroSign.Exact);
    }

    // (width - 24) / 244: the thresholds are on the width, 512, 756, ..., 1732, as in the gallery.
    [Fact]
    public void A_floor_of_a_one_input_expression_puts_the_thresholds_on_the_input()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(268, 1732), initial: 268, grid: 1));
        var circuit = expected.Circuit;
        var width = expected.Cells[0];
        var bits = Enumerable.Range(2, 6).Select(count => circuit.Compare(width, Relation.GreaterOrEqual, circuit.Constant(24 + 244 * count))).ToArray();
        circuit.Affine([.. bits.Select(bit => new Term(bit, 1))], 1);
        var translated = PatternSupport.Translate(nameof(PatternInputs.GalleryColumns));
        expected.AssertSameOperations(translated);
        AssertEachGridPoint(translated, ZeroSign.MayDiffer);
    }

    [Theory]
    [InlineData(nameof(PatternInputs.FloorQuarter), 17, false)]
    [InlineData(nameof(PatternInputs.Truncated), 17, true)]
    [InlineData(nameof(PatternInputs.Row), 8, true)]
    public void K_results_give_K_minus_1_comparisons_and_one_sum_in_3_layers(string name, int results, bool exactZero)
    {
        var zeroSign = exactZero ? ZeroSign.Exact : ZeroSign.MayDiffer;
        var translated = PatternSupport.Translate(name);
        Assert.Equal((2 * (results - 1) + 1, 3), PatternSupport.Size(translated));
        AssertEachGridPoint(translated, zeroSign);
    }

    // item % 8 is item - 8 × (item / 8): the same 7 comparisons and one sum.
    [Fact]
    public void An_int_remainder_is_the_value_minus_the_divisor_times_the_count()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 63), grid: 1));
        var circuit = expected.Circuit;
        var item = expected.Cells[0];
        var bits = Enumerable.Range(1, 7).Select(row => circuit.Compare(item, Relation.GreaterOrEqual, circuit.Constant(8 * row))).ToArray();
        var count = circuit.Affine([.. bits.Select(bit => new Term(bit, 1))]);
        circuit.Affine([(item, 1), (count, -8)]);
        var translated = PatternSupport.Translate(nameof(PatternInputs.Column));
        expected.AssertSameOperations(translated);
        AssertEachGridPoint(translated, ZeroSign.Exact);
    }

    [Theory]
    [InlineData(nameof(PatternInputs.NegativeRow))]
    [InlineData(nameof(PatternInputs.NegativeColumn))]
    [InlineData(nameof(PatternInputs.RowOfItem))]
    [InlineData(nameof(PatternInputs.HalvesInLoop))]
    public void Int_divisions_and_remainders_give_the_plain_values(string name) =>
        AssertEachGridPoint(PatternSupport.Translate(name), ZeroSign.Exact);

    // The count has fewer operations and layers than the same count written as a loop with no
    // division. The normal form of the division itself is a refusal.
    [Fact]
    public void A_threshold_count_has_fewer_operations_and_layers_than_a_counting_loop()
    {
        var recognized = PatternSupport.Size(PatternSupport.Translate(nameof(PatternInputs.Row)));
        var loop = PatternSupport.Translate(nameof(PatternInputs.RowByLoop), recognize: false);
        Assert.True(recognized.Operations < PatternSupport.Size(loop).Operations, $"recognized {recognized}, loop {PatternSupport.Size(loop)}");
        Assert.True(recognized.Layers < PatternSupport.Size(loop).Layers, $"recognized {recognized}, loop {PatternSupport.Size(loop)}");
        AssertEachGridPoint(loop, ZeroSign.Exact);
    }

    [Fact]
    public void With_no_pattern_recognition_the_division_and_the_remainder_are_refused()
    {
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.ColumnCount), recognize: false), PatternFile, "column-count",
            "float division by 244", "C# rounds x / 244 one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / 244)");
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.Row), recognize: false), PatternFile, "row",
            "int division", "C# rounds toward zero, and the program has no exact integer division");
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.Column), recognize: false), PatternFile, "column",
            "the IL instruction rem", DivisionReason);
    }

    // row / 64 for row in 0..9999 has 157 results: one index read over a table of 10000 results. As in
    // expressions.md, row 15, the read costs 1 operation plus one constant for each distinct result
    // (157). The remainder row - 64 × quotient and the sum compose into one affine operation. The
    // constants are in the first layer, the read in the second and the sum in the third.
    [Fact]
    public void More_than_64_results_of_an_int_give_an_index_read_over_a_table_of_the_results()
    {
        var translated = PatternSupport.Translate(nameof(PatternInputs.Slot));
        var program = translated.CreateEvaluator().Program;
        Assert.Single(program.Layers.SelectMany(layer => layer.IndexReads));
        Assert.Equal((157 + 1 + 1, 3), PatternSupport.Size(translated));
        AssertEachGridPoint(translated, ZeroSign.Exact);
    }

    [Fact]
    public void A_floor_with_too_many_results_or_no_grid_a_remainder_of_two_values_and_a_divisor_that_can_be_0_are_refused()
    {
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.FloorWide)), PatternFile, "floor-wide",
            "a call to System.MathF.Floor(Single)", "the lowering table has no exact construction for this method");
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.FloorNoGrid)), PatternFile, "floor-no-grid",
            "float division by 3", "C# rounds x / 3 one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / 3)");
        var remainder = AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.RemainderOfValues)), PatternFile, "rem-values",
            "the IL instruction rem", DivisionReason);
        Assert.Equal(2, remainder.IlOffset);
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.DivideByCount)), PatternFile, "divide-by-count",
            "an int division by a value that can be 0", "C# throws for a division by 0; declare a range of the divisor that does not hold 0");
    }

    // 600001 grid points are more than a scan reads, so the translator finds the change points by
    // bisection. floor(width / 3000) has 51 results: 50 comparisons and one sum.
    [Fact]
    public void A_range_with_more_grid_points_than_a_scan_reads_gives_the_change_points_by_bisection()
    {
        var translated = PatternSupport.Translate(nameof(PatternInputs.WideColumnCount));
        Assert.Equal((2 * 50 + 1, 3), PatternSupport.Size(translated));
        foreach (var source in SeededSources.Create(translated.Table, 2000, 6301))
        {
            AssertMatchesPlainAt(translated, source, ZeroSign.Exact);
        }

        for (var count = 1; count <= 50; count++)
        {
            // The first grid point at or above count × 3000 where C# gives count, and the point before it.
            var point = 3000f * count;
            while (MathF.Floor(point / 3000f) >= count) point -= 0.25f;
            AssertMatchesPlainAt(translated, [point], ZeroSign.Exact);
            AssertMatchesPlainAt(translated, [point + 0.25f], ZeroSign.Exact);
        }
    }

    [Fact]
    public void A_threshold_count_with_a_result_past_2_to_the_24_is_refused() =>
        AssertRefused(() => PatternSupport.Translate(nameof(PatternInputs.FloorPastExact)), PatternFile, "floor-past-exact",
            "a threshold count with a value past 2^24", "each step and each result of a threshold count must lie within -2^24..2^24; declare a smaller range");

    // Each grid point of the one input cell, compared with the plain method.
    private static void AssertEachGridPoint(TranslatedFunction translated, ZeroSign zeroSign)
    {
        var circuit = translated.Table.Circuit;
        Assert.Equal(1, circuit.SourceSize);
        var range = circuit.Ranges[0];
        var grid = circuit.Grids[0] ?? throw new InvalidOperationException("the input has no grid");
        for (var value = range.Minimum; value <= range.Maximum; value += grid)
        {
            AssertMatchesPlainAt(translated, [(float)value], zeroSign);
        }
    }
}
