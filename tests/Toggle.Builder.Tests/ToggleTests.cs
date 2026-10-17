// Handwired source: template/toggle.test.ts (scriptedTrace and its three tests).
// Each test name holds the Handwired test name. A test with no Handwired twin says so in its comment.
// Handwired compares the numbers with toEqual and toBe, which use Object.is. These tests use Js.Is.

using Sharpwired.Cells;
using Sharpwired.Core.Tests;
using Sharpwired.Timing;
using Xunit.Abstractions;

namespace Toggle.Builder.Tests;

public class ToggleTests(ITestOutputHelper output)
{
    // Moving tells whether the thumb still moves before the frame, so that a step that must land in the
    // middle of an animation does.
    private sealed record Frame(double TimeMs, ToggleEvent[] Events, (double On, double Armed)? Expected = null, bool? Moving = null);

    // Handwired scriptedTrace: frames 16 ms apart at 1280 × 800, where the track spans x 614..666 and
    // y 384..416. Each event arrives 3 ms before its frame.
    private static List<Frame> ScriptedTrace()
    {
        var frames = new List<Frame>();
        double timeMs = 1000;
        void Idle(int count, double gapMs = 16, bool? moving = null)
        {
            for (var index = 0; index < count; index++) frames.Add(new Frame(timeMs += gapMs, [], null, moving));
        }
        void Pointer(ToggleEventKind kind, double x, double y, double on, double armed, bool? moving = null)
        {
            timeMs += 16;
            frames.Add(new Frame(timeMs, [new ToggleEvent(kind, x, y, timeMs - 3)], (on, armed), moving));
        }
        Idle(2);
        Pointer(ToggleEventKind.Down, 640, 400, 0, 1, false); // a press on the track arms the switch
        Pointer(ToggleEventKind.Up, 640, 400, 1, 0); // a release on the track while armed flips it on
        Idle(3);
        Pointer(ToggleEventKind.Down, 630.25, 399.5, 1, 1, true);
        Pointer(ToggleEventKind.Up, 630.25, 399.5, 0, 0, true); // a click while the thumb moves flips it back
        Idle(60);
        Pointer(ToggleEventKind.Down, 613.984375, 400, 0, 0, false); // 1/64 px left of the track
        Pointer(ToggleEventKind.Down, 614, 384, 0, 1); // exactly on the left and top edges
        Pointer(ToggleEventKind.Up, 666, 400, 0, 0); // exactly on the right edge, which is outside, so it only disarms
        Pointer(ToggleEventKind.Down, 640, 400, 0, 1);
        Pointer(ToggleEventKind.Cancel, 0, 0, 0, 0); // a pointercancel disarms
        Pointer(ToggleEventKind.Up, 640, 400, 0, 0); // so this release does not flip
        Pointer(ToggleEventKind.Down, 665.984375, 415.984375, 0, 1);
        Pointer(ToggleEventKind.Up, 665.984375, 415.984375, 1, 0); // 1/64 px inside the bottom right corner
        Idle(1, 500, true); // a 500 ms stall while the thumb moves
        Idle(60);
        return frames;
    }

    [Fact]
    public void The_program_and_plain_CSharp_agree_on_every_frame_and_every_scripted_step_takes_effect()
    {
        var machine = new ToggleMachine();
        var reference = new ToggleReference();
        double largestThumbError = 0;
        var animatingFrames = 0;
        foreach (var frame in ScriptedTrace())
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
        // A copied test with another trace changes two numbers: this count of animating frames, and the rest position below.
        Assert.Equal(38, animatingFrames);
        Assert.True(largestThumbError < 1e-5, $"largest thumb error {largestThumbError:R} px");
        // At rest the thumb is exactly on its side, and the host stops the frame requests.
        Js.Is(20, machine.Step(1280, 800, 5000).ThumbOffset);
        Assert.Null(machine.Clock.AnimatedUntilMs);
    }

    [Fact]
    public void The_table_check_rejects_a_pointer_coordinate_off_the_1_64_px_grid()
    {
        var machine = new ToggleMachine();
        machine.Queue(new ToggleEvent(ToggleEventKind.Down, 640.001, 400, 1000));
        Js.Throws(() => machine.Step(1280, 800, 1016), "down.x must be a multiple of 0.015625");
    }

    [Fact]
    public void Events_that_share_a_timestamp_apply_together_the_last_down_then_the_last_up_then_any_cancel_in_both_versions()
    {
        static ToggleEvent At(ToggleEventKind kind, double timeStamp) => new(kind, 640, 400, timeStamp);
        (Frame[] Frames, double On, double Armed)[] cases =
        [
            // A press, then a release and a press at one timestamp: the press applies first, and the
            // release flips the switch and disarms it.
            ([new(1016, [At(ToggleEventKind.Down, 1013)]), new(1032, [At(ToggleEventKind.Up, 1029), At(ToggleEventKind.Down, 1029)])], 1, 0),
            // A cancel and a press at one timestamp: the cancel applies after the press, so the switch stays disarmed.
            ([new(1016, [At(ToggleEventKind.Cancel, 1013), At(ToggleEventKind.Down, 1013)])], 0, 0),
            // A press and then a cancel at one timestamp: the cancel applies after the press, so the switch stays disarmed.
            ([new(1016, [At(ToggleEventKind.Down, 1013), At(ToggleEventKind.Cancel, 1013)])], 0, 0),
            // The same release and press 1/64 ms apart apply one after the other, so the press arms the switch again.
            ([new(1016, [At(ToggleEventKind.Down, 1013)]), new(1032, [At(ToggleEventKind.Up, 1029), At(ToggleEventKind.Down, 1029.015625)])], 1, 1),
        ];
        foreach (var (frames, on, armed) in cases)
        {
            var machine = new ToggleMachine();
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

    // Sharpwired case with no Handwired test: of two events of one kind at one timestamp, the last one
    // writes the cells, in the program and in the reference. Handwired gets this from the loop of the
    // evaluate callback and from findLast.
    [Theory]
    [InlineData(640, 700, 0)]
    [InlineData(700, 640, 1)]
    public void Of_two_downs_at_one_timestamp_the_last_one_applies_in_both_versions(double firstX, double lastX, double armed)
    {
        var machine = new ToggleMachine();
        var reference = new ToggleReference();
        ToggleEvent[] events = [new(ToggleEventKind.Down, firstX, 400, 1013), new(ToggleEventKind.Down, lastX, 400, 1013)];
        foreach (var item in events) machine.Queue(item);
        var result = machine.Step(1280, 800, 1016);
        var referenceOutput = reference.Render(1280, 800, 1016, events);
        Js.AllIs([0, armed], [result.On, result.Armed]);
        Js.AllIs([referenceOutput.On, referenceOutput.Armed], [result.On, result.Armed]);
    }

    // Sharpwired case with no Handwired test: the 20 cells of toggleCells, in the Handwired order, with
    // the Handwired names and the initial table.
    [Fact]
    public void The_toggle_declares_20_cells_in_the_Handwired_order()
    {
        var built = ToggleProgram.Build();
        var table = new SourceTable();
        var cells = ToggleProgram.Declare(table);
        string[] names =
        [
            "viewportWidth", "viewportHeight", "down.active", "down.x", "down.y", "up.active", "up.x", "up.y", "cancel", "on",
            "armed", "springMatrix[0]", "springMatrix[1]", "springMatrix[2]", "springMatrix[3]", "settleCheck", "snap",
            "thumb.displacement", "thumb.velocity", "thumb.destination",
        ];
        Assert.Equal(20, table.Size);
        Assert.Equal(names, Enumerable.Range(0, 20).Select(position => table.NameOf(Sharpwired.Core.Wire.FromPosition(position))));
        Js.AllIs([1280, 800, .. Enumerable.Repeat(0.0, 18)], built.Table.InitialSource.Select(value => (double)value));
        static int[] Positions(ToggleCells cells) =>
        [
            .. new[] { cells.ViewportWidth, cells.ViewportHeight, cells.Down.Active, cells.Down.X, cells.Down.Y, cells.Up.Active, cells.Up.X, cells.Up.Y,
                cells.Cancel, cells.On, cells.Armed, cells.SpringMatrix[0], cells.SpringMatrix[1], cells.SpringMatrix[2], cells.SpringMatrix[3],
                cells.SettleCheck, cells.Snap, cells.Thumb.Displacement, cells.Thumb.Velocity, cells.Thumb.Destination }.Select(wire => wire.Position),
        ];
        Assert.Equal(Enumerable.Range(0, 20), Positions(cells));
        Assert.Equal(Enumerable.Range(0, 20), Positions(built.Cells));
        Assert.Equal(4, built.Cells.SpringMatrix.Length);
        // The matrix entries lie within ±SpringMatrixBound(springConstants).
        var bound = SpringMatrix.SpringMatrixBound(ToggleLayout.Spring);
        Assert.Equal(new Sharpwired.Core.NumericRange(-bound, bound), built.Table.Circuit.Ranges[cells.SpringMatrix[2].Position]);
    }

    // Sharpwired case with no Handwired test: the size of the toggle program. Handwired states no count
    // for the toggle. Phase 4 compares translated programs with this program operation for operation.
    [Fact]
    public void The_toggle_program_has_a_fixed_operation_count_and_layer_count()
    {
        var built = ToggleProgram.Build();
        var evaluator = ProgramEvaluator.Create(built.Table, built.Output);
        var operations = built.Table.Circuit.Operations.Count;
        var layers = evaluator.Program.Layers.Length;
        output.WriteLine($"operations: {operations}, layers: {layers}");
        Assert.Equal(["trackLeft", "trackTop", "thumbOffset", "on", "armed", "active"], evaluator.OutputPaths.ToArray());
        Assert.Equal(ExpectedOperations, operations);
        Assert.Equal(ExpectedLayers, layers);
    }

    // A regression record: the port measured these counts from the C# program. Handwired states no
    // operation count and no layer count for the toggle.
    private const int ExpectedOperations = 69;
    private const int ExpectedLayers = 21;

    // Sharpwired case with no Handwired test: the write-back policy goes to the host loop. With the
    // default policy a frame evaluation runs on a copy, so the stored thumb cells change only at events
    // and settle checks. With EveryEvaluation each frame writes them back.
    [Fact]
    public void The_machine_writes_frames_back_only_with_EveryEvaluation()
    {
        float[] ThumbAfterFrames(WriteBack writeBack)
        {
            var machine = new ToggleMachine(writeBack);
            machine.Queue(new ToggleEvent(ToggleEventKind.Down, 640, 400, 1000));
            machine.Queue(new ToggleEvent(ToggleEventKind.Up, 640, 400, 1000));
            machine.Step(1280, 800, 1000);
            var thumb = machine.Built.Cells.Thumb;
            var afterEvents = new[] { machine.Source[thumb.Displacement.Position], machine.Source[thumb.Velocity.Position] };
            // Frames at 1,016 and 1,032 ms run 4 and 8 steps, before the first settle check at 1,064 ms.
            machine.Step(1280, 800, 1016);
            machine.Step(1280, 800, 1032);
            Assert.True(machine.Clock.IsRunning);
            return [.. afterEvents, machine.Source[thumb.Displacement.Position], machine.Source[thumb.Velocity.Position]];
        }
        var kept = ThumbAfterFrames(WriteBack.EventsAndSettleChecks);
        Js.AllIs([kept[0], kept[1]], [kept[2], kept[3]]);
        Js.AllIs([-20, 0], [kept[0], kept[1]]);
        var written = ThumbAfterFrames(WriteBack.EveryEvaluation);
        Assert.NotEqual(written[0], written[2]);
        Assert.NotEqual(written[1], written[3]);
    }
}
