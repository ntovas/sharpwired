// Handwired source: src/runtime/numeric-ui-core.ts (evaluateAffineRange, multiplyByReluRange,
// inferFormGrid, inferOperationGrid) and tests/runtime/comparison.test.ts.

namespace Sharpwired.Core.Tests;

public class InferenceTests
{
    // comparison.test.ts 'grids are inferred through sums and products that cannot round'.
    // This port keeps the assertions that use no emitCompare and no validateGrids.
    // The others belong to plan tasks 4 and 6.
    [Fact]
    public void Grids_are_inferred_through_sums_and_products_that_cannot_round()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 16_384);
        circuit.AssumeGrid(circuit.Source(0), 1.0 / 64);
        circuit.AssumeRange(circuit.Source(1), -8, 8);
        circuit.AssumeGrid(circuit.Source(1), 1.0 / 4);
        circuit.AssumeRange(circuit.Source(2), 0, 1);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        Assert.Equal(1.0 / 64, circuit.Grids[circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), -1)], 3).Position]);
        Assert.Equal(1.0 / 128, circuit.Grids[circuit.Affine([(circuit.Source(0), 0.5), (circuit.Source(1), 3)]).Position]);
        Assert.Equal(1.0 / 64, circuit.Grids[circuit.Reglu([], 1, [(circuit.Source(0), 1)], -100).Position]);
        // f(1/3) is 11184811 × 2^-25, so 16,384 / 3 needs more than 2^24 steps of 2^-31.
        Assert.Null(circuit.Grids[circuit.Affine([(circuit.Source(0), 1.0 / 3)]).Position]);
        // 8 × 16,384 = 2^17 isn't a multiple of 1/256 within 2^24 steps.
        Assert.Null(circuit.Grids[circuit.Multiply(circuit.Source(0), circuit.Source(1)).Position]);
        Assert.Null(circuit.Grids[circuit.Affine([(circuit.Source(0), 1), (circuit.Source(2), 1)]).Position]);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        Assert.Equal(1.0, circuit.Grids[circuit.Boolean(circuit.Source(3)).Position]);
        Assert.Equal(1.0 / 64, circuit.Grids[circuit.IndexRead(circuit.Affine([], 0), [circuit.Source(0), circuit.Source(1)]).Position]);
        // A coarser declaration is kept. Operations already read source 0, so the late declaration needs the opt-out.
        circuit.AssumeGrid(circuit.Source(0), 1, allowExistingReaders: true);
        circuit.AssumeGrid(circuit.Source(0), 1.0 / 64);
        Assert.Equal(1.0, circuit.Grids[0]);
        Js.Throws(() => circuit.AssumeGrid(circuit.Source(1), 0.3), "power of two");
        Js.Throws(() => circuit.AssumeGrid(circuit.Source(1), 0), "power of two");
    }

    [Fact]
    public void A_grid_step_must_be_a_power_of_two_from_2_pow_minus_149_to_2_pow_127()
    {
        var circuit = new Circuit(1);
        circuit.AssumeGrid(circuit.Source(0), Math.Pow(2, -149));
        circuit.AssumeGrid(circuit.Source(0), Math.Pow(2, 127));
        Assert.Equal(Math.Pow(2, 127), circuit.Grids[0]);
        foreach (var step in new[] { Math.Pow(2, -150), Math.Pow(2, 128), 3, -1, double.PositiveInfinity, double.NaN })
        {
            Js.Throws(() => new Circuit(1).AssumeGrid(Wire.FromPosition(0), step), "a grid step must be a power of two from 2^-149 to 2^127, got");
        }
    }

    [Fact]
    public void Grid_inference_refuses_open_ranges_and_sums_past_2_pow_24_grid_steps()
    {
        var circuit = new Circuit(3);
        circuit.AssumeGrid(circuit.Source(0), 1);
        circuit.AssumeRange(circuit.Source(1), 0, Math.Pow(2, 24));
        circuit.AssumeGrid(circuit.Source(1), 1);
        circuit.AssumeRange(circuit.Source(2), 0, 1);
        circuit.AssumeGrid(circuit.Source(2), 1);
        // An open range can hold Infinity, and f(0 × Infinity) is NaN.
        Assert.Null(circuit.Grids[circuit.Affine([(circuit.Source(0), 2)]).Position]);
        // |bias| + 2^24 × 1 is exactly 2^24 grid steps.
        Assert.Equal(1.0, circuit.Grids[circuit.RoundAffine([(circuit.Source(1), 1)]).Position]);
        Assert.Null(circuit.Grids[circuit.RoundAffine([(circuit.Source(1), 1)], 1).Position]);
        // The bias sets the grid when it is coarser: 0.5 makes the grid 1/2.
        Assert.Equal(0.5, circuit.Grids[circuit.Affine([(circuit.Source(2), 1)], 0.5).Position]);
        // The coefficient scales the grid, and a zero coefficient adds nothing.
        Assert.Equal(4.0, circuit.Grids[circuit.RoundAffine([(circuit.Source(2), 4), (circuit.Source(1), 0)]).Position]);
        // A constant gets the grid of its value. The constant 0 has no finite grid, so it gets none.
        Assert.Equal(0.25, circuit.Grids[circuit.Constant(0.75).Position]);
        Assert.Null(circuit.Grids[circuit.Constant(0).Position]);
    }

    [Fact]
    public void A_reglu_product_grid_is_the_product_of_both_form_grids_within_2_pow_24_steps()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -4, 4);
        circuit.AssumeGrid(circuit.Source(0), 0.5);
        circuit.AssumeRange(circuit.Source(1), 0, 8);
        circuit.AssumeGrid(circuit.Source(1), 0.25);
        Assert.Equal(0.125, circuit.Grids[circuit.Reglu([(circuit.Source(0), 1)], 0, [(circuit.Source(1), 1)], 0).Position]);
        // A form with an operand without a grid gives no grid.
        var noGrid = new Circuit(2);
        noGrid.AssumeRange(noGrid.Source(0), 0, 1);
        noGrid.AssumeGrid(noGrid.Source(0), 1);
        noGrid.AssumeRange(noGrid.Source(1), 0, 1);
        Assert.Null(noGrid.Grids[noGrid.Reglu([(noGrid.Source(0), 1)], 0, [(noGrid.Source(1), 1)], 0).Position]);
        Assert.Null(noGrid.Grids[noGrid.Reglu([(noGrid.Source(1), 1)], 0, [(noGrid.Source(0), 1)], 0).Position]);
    }

    [Fact]
    public void Affine_ranges_follow_the_rounded_coefficient_product_and_sum_at_each_step()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -2, 3);
        circuit.AssumeRange(circuit.Source(1), 0.1, 0.2);
        var negated = circuit.RoundAffine([(circuit.Source(0), -1)], 0.1);
        Assert.Equal(new NumericRange(Js.F(Js.F(0.1) - 3), Js.F(Js.F(0.1) + 2)), circuit.Ranges[negated.Position]);
        // The coefficient rounds to FP32 first, and each product rounds before the sum.
        var scaled = circuit.RoundAffine([(circuit.Source(1), 1.0 / 3)]);
        Assert.Equal(new NumericRange(Js.F(Js.F(1.0 / 3) * 0.1), Js.F(Js.F(1.0 / 3) * 0.2)), circuit.Ranges[scaled.Position]);
        // An unknown operand gives an unknown range.
        var open = new Circuit(1);
        var difference = open.RoundAffine([(open.Source(0), 1), (open.Source(0), -1)]);
        Assert.Equal(new NumericRange(double.NegativeInfinity, double.PositiveInfinity), open.Ranges[difference.Position]);
        // A NaN endpoint, here f(0 × Infinity), also gives an unknown range.
        var zeroTimesOpen = open.RoundAffine([(open.Source(0), 0)], 5);
        Assert.Equal(new NumericRange(double.NegativeInfinity, double.PositiveInfinity), open.Ranges[zeroTimesOpen.Position]);
    }

    [Fact]
    public void Reglu_and_boolean_ranges()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -3, 2);
        circuit.AssumeRange(circuit.Source(1), -1, 4);
        var product = circuit.Reglu([(circuit.Source(0), 1)], 0, [(circuit.Source(1), 1)], 0);
        Assert.Equal(new NumericRange(-12, 8), circuit.Ranges[product.Position]);
        circuit.AssumeRange(circuit.Source(1), 0, 1, allowExistingReaders: true);
        var bit = circuit.Boolean(circuit.Source(1));
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[bit.Position]);
        // A product of an unknown left side and a gate that may be 0 has an unknown range.
        var open = new Circuit(2);
        open.AssumeRange(open.Source(1), 0, 1);
        var openProduct = open.Reglu([(open.Source(0), 1)], 0, [(open.Source(1), 1)], 0);
        Assert.Equal(new NumericRange(double.NegativeInfinity, double.PositiveInfinity), open.Ranges[openProduct.Position]);
    }

    [Fact]
    public void Is_non_negative_reads_the_range_minimum()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 5);
        Assert.True(circuit.IsNonNegative(circuit.Source(0)));
        Assert.False(circuit.IsNonNegative(circuit.Source(1)));
    }
}
