// No Handwired source. Handwired has no translator. The per-construct tests of task 3 for the Hw
// methods (plan, section "Lowering table", rows Hw.AnimatedObject to Hw.FirstActive): each call gives
// the operations of its macro. Each Hw body equals its macro bit for bit (HwTests), so the value
// comparison over 200 seeded source tables is Object.is.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class IntrinsicTests
{
    private static CellSpec MatrixEntry => Cell.Input(new NumericRange(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound));

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(ArithmeticInputs), name));

    [Fact]
    public void Hw_Relu_gives_the_relu_macro()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(-100, 100)));
        expected.Circuit.Relu(expected.Cells[0]);
        var translated = Translate(nameof(ArithmeticInputs.Relu));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 601, ZeroSign.Exact);
    }

    [Fact]
    public void Hw_Boolean_gives_the_boolean_atom()
    {
        var expected = new ExpectedProgram(Cell.Input(new NumericRange(0, 1)));
        expected.Circuit.Boolean(expected.Cells[0]);
        var translated = Translate(nameof(ArithmeticInputs.Boolean));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 602, ZeroSign.Exact);
    }

    [Fact]
    public void Hw_ApplySpringMatrix_gives_the_spring_matrix_macro()
    {
        var expected = new ExpectedProgram(MatrixEntry, MatrixEntry, MatrixEntry, MatrixEntry,
            Cell.Input(new NumericRange(-40, 40)), Cell.Input(new NumericRange(-1280, 1280)));
        var cells = expected.Cells;
        var state = expected.Circuit.ApplySpringMatrix(new SpringMatrixWires(cells[0], cells[1], cells[2], cells[3]), cells[4], cells[5]);
        var translated = Translate(nameof(ArithmeticInputs.ApplySpringMatrix));
        expected.AssertSameOperations(translated);
        Assert.Equal(["displacement", "velocity"], translated.Outputs.Select(output => output.Path));
        Assert.Equal<Wire>([state.Displacement, state.Velocity], translated.Outputs.Select(output => output.Wire));
        AssertMatchesPlain(translated, 603, ZeroSign.Exact);
    }

    [Fact]
    public void Hw_FirstActive_gives_the_first_active_macro()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Cell.InputBit(), Cell.InputBit(), Cell.InputBit());
        var cells = expected.Cells;
        var selected = expected.Circuit.FirstActive(cells[0], cells[1..]);
        var translated = Translate(nameof(ArithmeticInputs.FirstActive));
        expected.AssertSameOperations(translated);
        Assert.Equal<Wire>(selected, translated.Outputs.Select(output => output.Wire));
        AssertMatchesPlain(translated, 604, ZeroSign.Exact);
    }

    [Fact]
    public void Hw_AnimatedObject_gives_the_animated_object_macro_with_one_spring_and_the_snap_bit()
    {
        var expected = new ExpectedProgram(MatrixEntry, MatrixEntry, MatrixEntry, MatrixEntry,
            Cell.Input(new NumericRange(-40, 40)), Cell.Input(new NumericRange(-1280, 1280)), Cell.Input(new NumericRange(0, 20)),
            Cell.Input(new NumericRange(0, 20)), Cell.InputBit(), Cell.InputBit());
        var cells = expected.Cells;
        var spring = new AnimatedSpring(new SpringMatrixWires(cells[0], cells[1], cells[2], cells[3]), cells[4], cells[5], cells[6])
        {
            DestinationDuringSteps = cells[7],
        };
        var animated = expected.Circuit.AnimatedObject([spring], new AnimatedObjectOptions(cells[8]) { Snap = cells[9] });
        var translated = Translate(nameof(ArithmeticInputs.AnimatedObject));
        expected.AssertSameOperations(translated);
        Assert.Equal(["position", "displacement", "velocity", "active"], translated.Outputs.Select(output => output.Path));
        Assert.Equal<Wire>([animated.Positions[0], animated.Displacements[0], animated.Velocities[0], animated.Active],
            translated.Outputs.Select(output => output.Wire));
        AssertMatchesPlain(translated, 605, ZeroSign.Exact);
    }
}
