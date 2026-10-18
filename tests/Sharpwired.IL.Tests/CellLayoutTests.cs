// No Handwired source. Handwired has no translator. The tests check the cell walk of the plan,
// section "Decisions", question 2: the cells of the toggle types in walk order, with the names,
// policies, ranges, grids and initial values of the plan, section "Files"; [OneHot]; an attribute
// on a primary constructor parameter, on a property and on a backing field; [State] in a
// TranslateFunction parameter; the refusals of the walk; and the checks of the attribute arguments.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class CellLayoutTests
{
    private const string Refused = "RefusedInputs.cs";

    private static readonly string[] ToggleCellNames =
    [
        "on", "armed", "thumb.displacement", "thumb.velocity", "thumb.destination", "viewportWidth", "viewportHeight",
        "down.active", "down.x", "down.y", "up.active", "up.x", "up.y", "cancel",
        "springMatrix[0]", "springMatrix[1]", "springMatrix[2]", "springMatrix[3]", "settleCheck", "snap",
    ];

    // The toggle cells of the plan, section "Files", in the walk order: the state parameter first.
    private static FinishedTable ExpectedLayoutStep()
    {
        var table = new SourceTable();
        var on = table.Declare("on", Cell.StateBit());
        var armed = table.Declare("armed", Cell.StateBit());
        var displacement = table.Declare("thumb.displacement", Cell.State(new NumericRange(-40, 40)));
        var velocity = table.Declare("thumb.velocity", Cell.State(new NumericRange(-1280, 1280)));
        var destination = table.Declare("thumb.destination", Cell.State(new NumericRange(0, 20)));
        var viewportWidth = table.Declare("viewportWidth", Cell.Input(new NumericRange(0, 16384), initial: 1280, grid: 1));
        var viewportHeight = table.Declare("viewportHeight", Cell.Input(new NumericRange(0, 16384), initial: 800, grid: 1));
        foreach (var pointer in new[] { "down", "up" })
        {
            table.Declare(pointer + ".active", Cell.Event());
            table.Declare(pointer + ".x", Cell.Input(new NumericRange(-16384, 16384), grid: 1.0 / 64));
            table.Declare(pointer + ".y", Cell.Input(new NumericRange(-16384, 16384), grid: 1.0 / 64));
        }

        table.Declare("cancel", Cell.Event());
        table.Declare("springMatrix", Enumerable.Range(0, 4).Select(_ => Cell.Input(new NumericRange(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound))).ToArray());
        var settleCheck = table.Declare("settleCheck", Cell.InputBit());
        table.Declare("snap", Cell.InputBit());
        var circuit = table.CreateCircuit();

        // The next values in walk order emit at the return, then the outputs.
        var nextDisplacement = circuit.Affine([(displacement, 0.5)]);
        var nextVelocity = circuit.Affine([(velocity, 0.5)]);
        var nextDestination = circuit.Affine([(destination, 0.5)]);
        circuit.Affine([(viewportWidth, 0.5)], -26);
        circuit.Affine([(viewportHeight, 0.5)], -16);
        circuit.Affine([(destination, 1), (displacement, 1)]);
        _ = settleCheck;
        table.Next(on, armed);
        table.Next(armed, on);
        table.Next(displacement, nextDisplacement);
        table.Next(velocity, nextVelocity);
        table.Next(destination, nextDestination);
        return table.Finish();
    }

    [Fact]
    public void The_toggle_types_give_the_toggle_cells_in_walk_order()
    {
        var step = HandwiredTranslator.TranslateStep(Input(typeof(AggregateInputs), nameof(AggregateInputs.LayoutStep)));
        var expected = ExpectedLayoutStep();
        var actual = step.Table;

        Assert.Equal(ToggleCellNames, step.Cells.Select(cell => cell.Path));
        Assert.Equal(Enumerable.Range(0, 20).Select(Wire.FromPosition), step.Cells.Select(cell => cell.Wire));
        for (var cell = 0; cell < 20; cell++)
        {
            Assert.Equal(expected.Circuit.Ranges[cell], actual.Circuit.Ranges[cell]);
            Assert.Equal(expected.Circuit.Grids[cell], actual.Circuit.Grids[cell]);
            Assert.Equal(expected.Circuit.ExactBits[cell], actual.Circuit.ExactBits[cell]);
        }

        // The next sources tell the policies: a state cell holds its next value, an input cell holds
        // itself, and an event holds the wire of Constant(0).
        Assert.Equal<Wire>(expected.NextSource, actual.NextSource);
        Assert.Equal<float>(expected.InitialSource, actual.InitialSource);
        TensorChecks.AssertSameOperations(expected.Circuit.Operations, actual.Circuit.Operations);
        Assert.Equal(["trackLeft", "trackTop", "thumbOffset", "on", "armed", "active"], step.Outputs.Select(output => output.Path));
        Assert.Equal(step.Outputs[0].Wire, step.Output("trackLeft"));
        Assert.Equal(Wire.FromPosition(8), step.Cell("down.x"));
        Assert.IsType<ArgumentException>(Js.Throws(() => step.Cell("down.z"), "down.z"));
    }

    [Fact]
    public void The_translated_step_equals_the_plain_step_over_seeded_tables()
    {
        var step = HandwiredTranslator.TranslateStep(Input(typeof(AggregateInputs), nameof(AggregateInputs.LayoutStep)));
        var program = step.CreateEvaluator();
        var plain = step.CreatePlainEvaluator();
        foreach (var source in SeededSources.Create(step.Table, SeededTableCount, 301))
        {
            var programResult = program.Evaluate(source);
            var plainResult = plain.Evaluate(source);
            for (var cell = 0; cell < step.Cells.Length; cell++)
            {
                AssertSameValue(plainResult.NextValues[cell], programResult.NextValues[cell], ZeroSign.MayDiffer, $"next value of {step.Cells[cell].Path}");
            }

            foreach (var output in step.Outputs)
            {
                AssertSameValue(plainResult.Read(output.Wire), programResult.Read(output.Wire), ZeroSign.MayDiffer, $"output {output.Path}");
            }
        }
    }

    [Fact]
    public void A_one_hot_array_gives_a_declared_one_hot_group_of_input_bits()
    {
        var translated = HandwiredTranslator.TranslateFunction(Input(typeof(AggregateInputs), nameof(AggregateInputs.OneHotInput)));
        Assert.Equal(["tabs[0]", "tabs[1]", "tabs[2]", "x"], translated.Inputs.Select(input => input.Path));
        var circuit = translated.Table.Circuit;
        var group = Assert.Single(circuit.OneHotGroups);
        Assert.Equal<Wire>([Wire.FromPosition(0), Wire.FromPosition(1), Wire.FromPosition(2)], group.Members);
        Assert.True(group.AllowAllZero);
        Assert.All(Enumerable.Range(0, 3), cell => Assert.True(circuit.ExactBits[cell]));
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[3]);
        Assert.Contains("oneHot", Assert.ThrowsAny<ArgumentException>(() => translated.Table.Validator.Validate([1, 1, 0, 0.5f])).Message);
        AssertMatchesPlain(translated, 302, ZeroSign.Exact);
    }

    [Fact]
    public void An_attribute_on_a_backing_field_declares_the_cell()
    {
        var translated = HandwiredTranslator.TranslateFunction(Input(typeof(AggregateInputs), nameof(AggregateInputs.AddToArgument)));
        Assert.Equal(["total", "x"], translated.Inputs.Select(input => input.Path));
        Assert.Equal(new NumericRange(-100, 100), translated.Table.Circuit.Ranges[0]);
        Assert.Equal(0.25, translated.Table.Circuit.Grids[0]);
    }

    [Fact]
    public void A_state_property_in_a_TranslateFunction_parameter_is_refused()
    {
        var error = Assert.Throws<TranslationException>(() =>
            HandwiredTranslator.TranslateFunction(Input(typeof(AggregateInputs), nameof(AggregateInputs.StateInFunction))));
        var path = InputPath("AggregateInputs.cs");
        var line = File.ReadAllLines(path).Select((text, index) => (text, index)).Single(entry => entry.text.Contains("record struct WithStateProperty", StringComparison.Ordinal)).index + 1;
        Assert.Equal(
            $"{path}:{line}: a [State] leaf in a TranslateFunction parameter is not supported in a [Handwired] method: "
            + "TranslateFunction declares no state cells; use TranslateStep",
            error.Message);
    }

    [Theory]
    [InlineData(nameof(RefusedInputs.PublicFieldParameter), "public-field", "a public field in MutablePair",
        "a cell type holds its values in properties; make each public field a property")]
    [InlineData(nameof(RefusedInputs.BaseRecord), "base-record", "a base type of DerivedShape",
        "a cell type derives only from object or ValueType, because the property order of a base type is not defined")]
    [InlineData(nameof(RefusedInputs.NoLength), "no-length", "an array without [FixedLength]",
        "an array type has no length; add [FixedLength]")]
    [InlineData(nameof(RefusedInputs.ByteLeaf), "byte-leaf", "a cell of type Byte",
        "a cell holds a float, an int or a bool")]
    [InlineData(nameof(RefusedInputs.IntWithoutRange), "int-without-range", "an int cell without [Range]",
        "an int cell needs a finite [Range] within -2^24..2^24")]
    [InlineData(nameof(RefusedInputs.ZeroLength), "zero-length", "[FixedLength(0)]",
        "the length of an array must be at least 1")]
    [InlineData(nameof(RefusedInputs.NegativeIterations), "negative-iterations", "[MaxIterations(-1)]",
        "the maximum number of iterations must be at least 1")]
    public void The_walk_refuses_a_parameter_with_the_file_and_line(string method, string marker, string construct, string reason) =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), method)), Refused, marker, construct, reason);

    [Theory]
    [InlineData(nameof(RefusedInputs.InvertedRangeProperty), "inverted-range", "[Range(1, 0)]",
        "the minimum of a range must not be greater than the maximum")]
    [InlineData(nameof(RefusedInputs.BadGridProperty), "bad-grid", "[Grid(0.3)]",
        "a grid must be a power of two from 2^-149 to 2^127")]
    [InlineData(nameof(RefusedInputs.OutsideInitialProperty), "outside-initial", "[Initial(2)]",
        "the initial value must lie in the range 0..1")]
    public void The_walk_refuses_a_bad_attribute_on_a_property_with_the_file_and_line(string method, string marker, string construct, string reason) =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), method)), Refused, marker, construct, reason);

    // Plan decision 9: a cell with no [Initial] starts at the value of its range and grid nearest to
    // 0. With no grid, the value is the FP32 value inside the range nearest to 0 (final fix wave,
    // item H). (float)0.7 is below 0.7, and (float)-0.7 is above -0.7.
    [Theory]
    [InlineData(2.5, 10, 1.0, 3)]
    [InlineData(-10, -2.5, 1.0, -3)]
    [InlineData(0.75, 10, 0.5, 1)]
    [InlineData(-10, -0.75, 0.5, -1)]
    [InlineData(0.1, 1, null, (double)(float)0.1)]
    [InlineData(-1, -0.1, null, (double)(float)-0.1)]
    [InlineData(-5, 5, 1.0, 0)]
    public void The_initial_value_is_the_value_nearest_to_zero(double minimum, double maximum, double? grid, double expected) =>
        Assert.Equal(expected, CellLayout.NearestToZero(new NumericRange(minimum, maximum), grid));

    [Fact]
    public void The_initial_value_with_no_grid_is_an_FP32_value_inside_the_range()
    {
        Assert.Equal((double)MathF.BitIncrement((float)0.7), CellLayout.NearestToZero(new NumericRange(0.7, 1), null));
        Assert.Equal((double)MathF.BitDecrement((float)-0.7), CellLayout.NearestToZero(new NumericRange(-1, -0.7), null));
    }
}
