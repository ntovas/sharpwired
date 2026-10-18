// Handwired source: template/toggle.ts (buildToggle and toggleCells) and template/toggle.test.ts
// (scriptedTrace and its tests). Handwired has no translator, so these checks have no Handwired twin.
// The five checks of decision 1 in the phase 4 plan, with the comparison rules of decision 8, for a
// translated toggle step. CSharpToggleTests runs them on the plain C# toggle (Toggle.CSharp), and
// FSharpToggleTests links this file and runs them on the plain F# toggle (Toggle.FSharp). The two
// plain toggles have the same cell paths and output paths, so CSharpToggle.Build maps both of them to
// a BuiltToggle.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL;
using Sharpwired.Timing;
using Toggle.Builder;
using Toggle.Builder.Tests;
using Toggle.CSharp;
using Xunit.Abstractions;
using Frame = Toggle.Builder.Tests.ToggleTrace.Frame;

namespace Toggle.CSharp.Tests;

/// <summary>The five checks of decision 1 for the translated toggle step that <see cref="Translate"/> gives.</summary>
public abstract class TranslatedToggleChecks(ITestOutputHelper output)
{
    /// <summary>Translate the plain toggle step. Each call translates the method again.</summary>
    protected abstract TranslatedStep Translate();

    // The walk order of the plain types: the state parameter first, then the input parameter.
    internal static readonly string[] CellNames =
    [
        "on", "armed", "thumb.displacement", "thumb.velocity", "thumb.destination", "viewportWidth", "viewportHeight",
        "down.active", "down.x", "down.y", "up.active", "up.x", "up.y", "cancel",
        "springMatrix[0]", "springMatrix[1]", "springMatrix[2]", "springMatrix[3]", "settleCheck", "snap",
    ];

    // The cells of ToggleProgram.Declare with the same names and specs, in the walk order of the plain
    // types (plan, decision 1). ToggleProgram.Build(StateFirst) is the builder toggle with this order.
    internal static ToggleCells StateFirst(ICellScope scope)
    {
        var on = scope.Declare("on", Cell.StateBit());
        var armed = scope.Declare("armed", Cell.StateBit());
        var thumb = scope.Declare("thumb", thumbScope => new ThumbCells(
            thumbScope.Declare("displacement", Cell.State(new NumericRange(-2 * ToggleLayout.Travel, 2 * ToggleLayout.Travel))),
            thumbScope.Declare("velocity", Cell.State(new NumericRange(-64 * ToggleLayout.Travel, 64 * ToggleLayout.Travel))),
            thumbScope.Declare("destination", Cell.State(new NumericRange(0, ToggleLayout.Travel)))));
        var viewportWidth = scope.Declare("viewportWidth", Cell.Input(new NumericRange(0, 16_384), initial: 1280, grid: 1));
        var viewportHeight = scope.Declare("viewportHeight", Cell.Input(new NumericRange(0, 16_384), initial: 800, grid: 1));
        static PointerCells DeclarePointer(ICellScope pointer) => new(
            pointer.Declare("active", Cell.Event()),
            pointer.Declare("x", Cell.Input(new NumericRange(-16_384, 16_384), grid: 1.0 / 64)),
            pointer.Declare("y", Cell.Input(new NumericRange(-16_384, 16_384), grid: 1.0 / 64)));
        var down = scope.Declare("down", DeclarePointer);
        var up = scope.Declare("up", DeclarePointer);
        var cancel = scope.Declare("cancel", Cell.Event());
        var bound = SpringMatrix.SpringMatrixBound(ToggleLayout.Spring);
        var springMatrix = scope.Declare("springMatrix", [.. Enumerable.Range(0, 4).Select(_ => Cell.Input(new NumericRange(-bound, bound)))]);
        var settleCheck = scope.Declare("settleCheck", Cell.InputBit());
        var snap = scope.Declare("snap", Cell.InputBit());
        return new ToggleCells(viewportWidth, viewportHeight, down, up, cancel, on, armed, springMatrix, settleCheck, snap, thumb);
    }

    // StateFirst declares the cells of ToggleProgram.Declare in another order (final fix wave, item L).
    // The two declarations give the same names with the same specs, and only the order differs. So
    // an error in StateFirst and in the plain types at the same time does not hide: Declare is the
    // phase 3 source of the cells.
    [Fact]
    public void StateFirst_declares_the_cells_of_the_builder_toggle_in_the_walk_order()
    {
        var builderCells = CellRecorder.Record(ToggleProgram.Declare);
        var stateFirstCells = CellRecorder.Record(StateFirst);
        Assert.Equal(CellNames, stateFirstCells.Select(cell => cell.Name));
        Assert.NotEqual(CellNames, builderCells.Select(cell => cell.Name));
        Assert.Equal(builderCells.OrderBy(cell => cell.Name, StringComparer.Ordinal), stateFirstCells.OrderBy(cell => cell.Name, StringComparer.Ordinal));
    }

    private static Wire[] CellWires(ToggleCells cells) =>
    [
        cells.On, cells.Armed, cells.Thumb.Displacement, cells.Thumb.Velocity, cells.Thumb.Destination, cells.ViewportWidth,
        cells.ViewportHeight, cells.Down.Active, cells.Down.X, cells.Down.Y, cells.Up.Active, cells.Up.X, cells.Up.Y, cells.Cancel,
        .. cells.SpringMatrix, cells.SettleCheck, cells.Snap,
    ];

    private static Wire[] OutputWires(ToggleOutputWires wires) =>
        [wires.TrackLeft, wires.TrackTop, wires.ThumbOffset, wires.On, wires.Armed, wires.Active];

    private ToggleMachine TranslatedMachine() => TranslatedMachine(CSharpToggle.Build(Translate()));

    private static ToggleMachine TranslatedMachine(BuiltToggle built) => new(built, ProgramEvaluator.Create(built.Table, built.Output));

    [Fact]
    public void The_translated_table_has_the_20_cells_in_walk_order()
    {
        var step = Translate();
        Assert.Equal(CellNames, step.Cells.Select(cell => cell.Path));
        Assert.Equal(Enumerable.Range(0, 20).Select(Wire.FromPosition), step.Cells.Select(cell => cell.Wire));
        Assert.Equal(["trackLeft", "trackTop", "thumbOffset", "on", "armed", "active"], step.Outputs.Select(wire => wire.Path));
        var built = CSharpToggle.Build(step);
        Assert.Equal(Enumerable.Range(0, 20).Select(Wire.FromPosition), CellWires(built.Cells));
        Assert.Equal(step.Outputs.Select(wire => wire.Wire), OutputWires(built.Output));
        Js.AllIs([0, 0, 0, 0, 0, 1280, 800, .. Enumerable.Repeat(0.0, 13)], built.Table.InitialSource.Select(value => (double)value));
    }

    // Decision 1, items 1 to 3. The operation list and the compiled program equal those of the builder
    // toggle with its cells in the same order. The cell names, specs, next sources and initial values
    // also equal those of that toggle.
    [Fact]
    public void The_translated_toggle_equals_the_builder_toggle_operation_for_operation()
    {
        var step = Translate();
        var translated = CSharpToggle.Build(step);
        SourceTable? builderTable = null;
        var builder = ToggleProgram.Build(scope =>
        {
            builderTable = Assert.IsType<SourceTable>(scope);
            return StateFirst(scope);
        });

        // Item 1.
        TensorChecks.AssertSameOperations(builder.Table.Circuit.Operations, translated.Table.Circuit.Operations);
        Assert.Equal(69, translated.Table.Circuit.Operations.Count);

        // Item 2.
        var builderProgram = ProgramEvaluator.Create(builder.Table, builder.Output).Program;
        var translatedProgram = ProgramEvaluator.Create(translated.Table, translated.Output).Program;
        TensorChecks.AssertSameProgram(builderProgram, translatedProgram);
        TensorChecks.AssertSameProgram(builderProgram, step.CreateEvaluator().Program);
        Assert.Equal(21, translatedProgram.Layers.Length);

        // Item 3. The expected names come from the table of the builder toggle.
        Assert.Equal(Enumerable.Range(0, 20).Select(position => builderTable!.NameOf(Wire.FromPosition(position))), step.Cells.Select(cell => cell.Path));
        Assert.Equal(CellWires(builder.Cells), CellWires(translated.Cells));
        Assert.Equal(OutputWires(builder.Output), OutputWires(translated.Output));
        for (var cell = 0; cell < 20; cell++)
        {
            Assert.Equal(builder.Table.Circuit.Ranges[cell], translated.Table.Circuit.Ranges[cell]);
            Assert.Equal(builder.Table.Circuit.Grids[cell], translated.Table.Circuit.Grids[cell]);
            Assert.Equal(builder.Table.Circuit.ExactBits[cell], translated.Table.Circuit.ExactBits[cell]);
        }

        Assert.Equal<Wire>(builder.Table.NextSource, translated.Table.NextSource);
        Js.AllIs(builder.Table.InitialSource.Select(value => (double)value), translated.Table.InitialSource.Select(value => (double)value));
    }

    // Decision 1, item 4, with the comparison rules of decision 8. The translated program and the
    // builder program are circuits with the same operations, so each output and each stored cell
    // compares with Js.Is. The plain method compares with Js.Is on the bit outputs and with == on the
    // float outputs and the stored float cells. This is because an affine operation can give +0 where
    // C# gives -0.
    [Fact]
    public void Over_the_scripted_trace_the_translated_program_equals_the_builder_program_and_the_plain_method()
    {
        var step = Translate();
        var built = CSharpToggle.Build(step);
        var translatedMachine = TranslatedMachine(built);
        var plainMachine = new ToggleMachine(built, step.CreatePlainEvaluator());
        var builder = ToggleProgram.Build(StateFirst);
        var builderMachine = new ToggleMachine(builder, ProgramEvaluator.Create(builder.Table, builder.Output));
        var zeroSignDifferences = 0;
        var frames = 0;
        foreach (var frame in ToggleTrace.ScriptedTrace())
        {
            foreach (var item in frame.Events)
            {
                translatedMachine.Queue(item);
                plainMachine.Queue(item);
                builderMachine.Queue(item);
            }

            var translated = translatedMachine.Step(1280, 800, frame.TimeMs);
            var plain = plainMachine.Step(1280, 800, frame.TimeMs);
            var fromBuilder = builderMachine.Step(1280, 800, frame.TimeMs);
            double[] translatedOutputs = [translated.TrackLeft, translated.TrackTop, translated.ThumbOffset, translated.On, translated.Armed, translated.Active];
            Js.AllIs([fromBuilder.TrackLeft, fromBuilder.TrackTop, fromBuilder.ThumbOffset, fromBuilder.On, fromBuilder.Armed, fromBuilder.Active], translatedOutputs);
            Js.AllIs(builderMachine.Source.Select(value => (double)value), translatedMachine.Source.Select(value => (double)value));
            Assert.Equal(builderMachine.Clock.IsRunning, translatedMachine.Clock.IsRunning);

            Js.AllIs([plain.On, plain.Armed, plain.Active], [translated.On, translated.Armed, translated.Active]);
            double[] plainFloats = [plain.TrackLeft, plain.TrackTop, plain.ThumbOffset];
            double[] translatedFloats = [translated.TrackLeft, translated.TrackTop, translated.ThumbOffset];
            for (var index = 0; index < plainFloats.Length; index++)
            {
                Assert.True(plainFloats[index] == translatedFloats[index], $"frame {frames} at {frame.TimeMs} ms: output {index} is {translatedFloats[index]:R}, plain {plainFloats[index]:R}");
                if (!Js.ObjectIs(plainFloats[index], translatedFloats[index])) zeroSignDifferences++;
            }

            for (var cell = 0; cell < plainMachine.Source.Length; cell++)
            {
                var plainValue = plainMachine.Source[cell];
                var translatedValue = translatedMachine.Source[cell];
                Assert.True(plainValue == translatedValue, $"frame {frames} at {frame.TimeMs} ms: cell {CellNames[cell]} is {translatedValue:R}, plain {plainValue:R}");
                if (!Js.ObjectIs(plainValue, translatedValue)) zeroSignDifferences++;
            }

            Assert.Equal(plainMachine.Clock.IsRunning, translatedMachine.Clock.IsRunning);
            frames++;
        }

        output.WriteLine($"frames: {frames}, values that differ from the plain method only in the sign of zero: {zeroSignDifferences}");
        Assert.Equal(ToggleTrace.ScriptedTrace().Count, frames);
    }

    // Decision 1, item 5: the comparison rule of the phase 3 test
    // The_program_and_plain_CSharp_agree_on_every_frame_and_every_scripted_step_takes_effect, for the
    // translated program against ToggleReference.
    [Fact]
    public void The_translated_program_and_the_reference_agree_on_every_frame_and_every_scripted_step_takes_effect()
    {
        var machine = TranslatedMachine();
        var reference = new ToggleReference();
        double largestThumbError = 0;
        var animatingFrames = 0;
        foreach (var frame in ToggleTrace.ScriptedTrace())
        {
            if (frame.Moving is { } moving) Assert.Equal(moving, machine.Clock.IsRunning);
            foreach (var item in frame.Events) machine.Queue(item);
            var result = machine.Step(1280, 800, frame.TimeMs);
            var expected = reference.Render(1280, 800, frame.TimeMs, frame.Events);
            Js.AllIs(
                [expected.On, expected.Armed, expected.TrackLeft, expected.TrackTop],
                [result.On, result.Armed, result.TrackLeft, result.TrackTop]);
            if (frame.Expected is { } decisions) Js.AllIs([decisions.On, decisions.Armed], [result.On, result.Armed]);
            Assert.Equal(expected.Animating, machine.Clock.IsRunning);
            largestThumbError = Math.Max(largestThumbError, Math.Abs(result.ThumbOffset - expected.ThumbOffset));
            if (expected.Animating) animatingFrames++;
        }

        output.WriteLine($"largest thumb error: {largestThumbError:R} px, animating frames: {animatingFrames}");
        Assert.Equal(38, animatingFrames);
        Assert.True(largestThumbError < 1e-5, $"largest thumb error {largestThumbError:R} px");
        Js.Is(20, machine.Step(1280, 800, 5000).ThumbOffset);
        Assert.Null(machine.Clock.AnimatedUntilMs);
    }

    // The four cases of the phase 3 test
    // Events_that_share_a_timestamp_apply_together_the_last_down_then_the_last_up_then_any_cancel_in_both_versions,
    // for the translated program.
    [Fact]
    public void Events_that_share_a_timestamp_apply_together_in_the_translated_program()
    {
        static ToggleEvent At(ToggleEventKind kind, double timeStamp) => new(kind, 640, 400, timeStamp);
        (Frame[] Frames, double On, double Armed)[] cases =
        [
            ([new(1016, [At(ToggleEventKind.Down, 1013)]), new(1032, [At(ToggleEventKind.Up, 1029), At(ToggleEventKind.Down, 1029)])], 1, 0),
            ([new(1016, [At(ToggleEventKind.Cancel, 1013), At(ToggleEventKind.Down, 1013)])], 0, 0),
            ([new(1016, [At(ToggleEventKind.Down, 1013), At(ToggleEventKind.Cancel, 1013)])], 0, 0),
            ([new(1016, [At(ToggleEventKind.Down, 1013)]), new(1032, [At(ToggleEventKind.Up, 1029), At(ToggleEventKind.Down, 1029.015625)])], 1, 1),
        ];
        foreach (var (frames, on, armed) in cases)
        {
            var machine = TranslatedMachine();
            var reference = new ToggleReference();
            double[] decisions = [];
            foreach (var frame in frames)
            {
                foreach (var item in frame.Events) machine.Queue(item);
                var result = machine.Step(1280, 800, frame.TimeMs);
                var referenceOutput = reference.Render(1280, 800, frame.TimeMs, frame.Events);
                decisions = [result.On, result.Armed];
                Js.AllIs([referenceOutput.On, referenceOutput.Armed], decisions);
            }

            Js.AllIs([on, armed], decisions);
        }
    }
}

/// <summary>A cell scope that records the name and the spec of each cell in declaration order, and declares nothing.</summary>
internal sealed class CellRecorder(string prefix, List<(string Name, CellSpec Spec)> cells) : ICellScope
{
    private int count;

    /// <summary>The cells of a declaration, in declaration order.</summary>
    public static List<(string Name, CellSpec Spec)> Record(Func<ICellScope, ToggleCells> declare)
    {
        var cells = new List<(string Name, CellSpec Spec)>();
        declare(new CellRecorder("", cells));
        return cells;
    }

    public Wire Declare(string name, CellSpec cell)
    {
        cells.Add((Path(name), cell));
        return Wire.FromPosition(count++);
    }

    public Wire[] Declare(string name, IReadOnlyList<CellSpec> list) =>
        [.. list.Select((cell, index) => Declare($"{name}[{index}]", cell))];

    public Wire[] Declare(string name, OneHotSpec group) => throw new NotSupportedException("the toggle has no one-hot group");

    public Wire[][] Declare(string name, PermutationSpec permutation) => throw new NotSupportedException("the toggle has no permutation");

    public T Declare<T>(string name, Func<ICellScope, T> declare) => declare(new CellRecorder(Path(name), cells));

    public T[] DeclareList<T>(string name, int count, Func<ICellScope, int, T> declare) => throw new NotSupportedException("the toggle has no list");

    private string Path(string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";
}
