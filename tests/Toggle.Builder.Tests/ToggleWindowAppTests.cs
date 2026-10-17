// No Handwired test file. These tests cover the window app of template/toggle.ts (mountToggle, lines
// 141-170) and the CSS of template/index.html without a window. They cover the input to events, the
// idle rule, the coordinate grid and the draw commands.

using Sharpwired.Core.Tests;
using Sharpwired.Raylib;
using Sharpwired.Raylib.Tests;

namespace Toggle.Builder.Tests;

public class ToggleWindowAppTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Gray = new(0xd1, 0xd1, 0xd6, 255);
    private static readonly Rgba Green = new(0x34, 0xc7, 0x59, 255);

    // One scripted frame: the input that the window reads, and the time that the clock advances before it.
    private sealed record ScriptedFrame(double GapMs, WindowInput Input);

    private static WindowInput Idle(int width = 1280, int height = 800) =>
        new(0, 0, false, false, false, width, height);

    private static WindowInput Pointer(double x, double y, bool pressed, bool released, int width = 1280, int height = 800) =>
        new(x, y, pressed, released, false, width, height);

    // At 1280 x 800 the track spans x 614..666 and y 384..416. At 1000 x 700 it spans x 474..526 and y 334..366.
    private static List<ScriptedFrame> Script()
    {
        var frames = new List<ScriptedFrame>();
        void Add(WindowInput input, int count = 1, double gapMs = 16)
        {
            for (var index = 0; index < count; index++) frames.Add(new ScriptedFrame(gapMs, input));
        }
        Add(Idle(), 2); // the clock starts at 984 ms, so the click below is at 1,000 ms
        Add(Pointer(640.001, 400.3, pressed: true, released: false)); // a press on the track at 1,000 ms
        Add(Pointer(640.001, 400.3, pressed: false, released: true)); // the release flips the switch on
        Add(Idle(), 8); // the thumb moves
        Add(Pointer(630.25, 399.5, pressed: true, released: false)); // a click while the thumb moves
        Add(Pointer(630.25, 399.5, pressed: false, released: true));
        Add(Idle(), 80); // the thumb settles and the clock stops
        Add(Pointer(640, 400, pressed: true, released: false)); // a press on the track arms the switch
        Add(new WindowInput(640, 400, false, false, true, 1280, 800)); // a focus loss cancels the press
        Add(Idle(), 3);
        Add(Pointer(640, 400, pressed: false, released: true)); // so this release does not flip
        Add(Idle(), 3);
        Add(Idle(1000, 700)); // a resize moves the track to the new center
        Add(Idle(1000, 700), 3);
        Add(Pointer(500, 350, pressed: true, released: true, 1000, 700)); // a press and a release in one frame
        Add(Idle(1000, 700), 313); // 5 s of idle frames
        return frames;
    }

    // No Handwired test. The browser host requests a frame only while the clock runs, after an event,
    // or after a resize. The window app draws in every frame, but steps the program only in these frames.
    // A second machine gets the same events at the same timestamps and steps in every frame. It gives
    // the same draw commands at every frame, so the idle rule gives the same pixels.
    [Fact]
    public void The_idle_rule_gives_at_each_frame_the_draw_commands_of_a_machine_that_steps_in_every_frame()
    {
        var clock = new FakeFrameClock();
        clock.Set(968);
        var app = new ToggleWindowApp(new ToggleMachine());
        var reference = new ToggleMachine();
        IReadOnlyList<DrawCommand>? previous = null;
        var reusedFrames = 0;
        var runningFrames = 0;
        ToggleOutput last = default;
        foreach (var frame in Script())
        {
            clock.Advance(frame.GapMs);
            var input = frame.Input;
            var nowMs = clock.NowMs;
            var x = ToggleWindowApp.QuantizeCoordinate(input.PointerX);
            var y = ToggleWindowApp.QuantizeCoordinate(input.PointerY);
            if (input.PointerPressed) reference.Queue(new ToggleEvent(ToggleEventKind.Down, x, y, nowMs));
            if (input.PointerReleased) reference.Queue(new ToggleEvent(ToggleEventKind.Up, x, y, nowMs));
            if (input.FocusLost) reference.Queue(new ToggleEvent(ToggleEventKind.Cancel, 0, 0, nowMs));
            if (reference.Clock.IsRunning) runningFrames++;
            last = reference.Step(input.ViewportWidth, input.ViewportHeight, nowMs);

            var commands = app.Frame(input, nowMs);

            Assert.Equal(ToggleWindowApp.Draw(last), commands);
            if (ReferenceEquals(commands, previous)) reusedFrames++;
            previous = commands;
        }
        // The script reaches each state. The clock ran, and the app reused the commands of idle frames.
        // The last click (a press and a release in one frame) left the switch on at the end of its travel.
        Assert.True(runningFrames > 50, $"the clock ran in {runningFrames} frames");
        Assert.True(reusedFrames > 300, $"the app reused the commands in {reusedFrames} frames");
        Assert.Equal(1.0, last.On);
        Assert.Equal(0.0, last.Armed);
        Assert.Equal(20.0, last.ThumbOffset);
        Assert.Equal(474.0, last.TrackLeft);
        Assert.Equal(334.0, last.TrackTop);
        Assert.False(reference.Clock.IsRunning);
    }

    // No Handwired test. The focus loss of the script above must cancel the press: the switch stays off
    // after the release. This test runs the window app alone and reads the track color.
    [Fact]
    public void A_focus_loss_between_a_press_and_a_release_on_the_track_cancels_the_press()
    {
        var app = new ToggleWindowApp(new ToggleMachine());
        var timeMs = 1000.0;
        IReadOnlyList<DrawCommand> Frame(WindowInput input) => app.Frame(input, timeMs += 16);
        Frame(Idle());
        var armed = Frame(Pointer(640, 400, pressed: true, released: false));
        Assert.Equal(new Rgba(188, 188, 193, 255), ((FillRoundedRectangle)armed[0]).Color);
        var cancelled = Frame(new WindowInput(640, 400, false, false, true, 1280, 800));
        Assert.Equal(Gray, ((FillRoundedRectangle)cancelled[0]).Color);
        var released = Frame(Pointer(640, 400, pressed: false, released: true));
        for (var index = 0; index < 100; index++) released = Frame(Idle());
        Assert.Equal(ToggleWindowApp.Draw(new ToggleOutput(614, 384, 0, 0, 0, 0)), released);
    }

    // No Handwired test. toggle.ts:161 puts a coordinate on the declared 1/64 px grid with Math.round,
    // and clamps it to +-16,384 px.
    [Fact]
    public void QuantizeCoordinate_puts_a_coordinate_on_the_1_64_px_grid_and_clamps_it_to_16384_px()
    {
        Js.Is(640.0, ToggleWindowApp.QuantizeCoordinate(640.001));
        Js.Is(613.984375, ToggleWindowApp.QuantizeCoordinate(613.99));
        Js.Is(16384.0, ToggleWindowApp.QuantizeCoordinate(20000));
        Js.Is(-16384.0, ToggleWindowApp.QuantizeCoordinate(-20000));
        Js.Is(-0.0, ToggleWindowApp.QuantizeCoordinate(-0.001));
        // Math.round rounds a half up: 0.5 / 64 px goes up, and -0.5 / 64 px goes to -0.
        Js.Is(0.015625, ToggleWindowApp.QuantizeCoordinate(0.5 / 64));
        Js.Is(-0.0, ToggleWindowApp.QuantizeCoordinate(-0.5 / 64));
    }

    // No Handwired test. The CSS of index.html: the track is 52 x 32 px with radius 16 at (trackLeft,
    // trackTop). Its color is color-mix(in srgb, #34c759 fill, #d1d1d6) with fill = min(1, max(0,
    // thumbOffset / travel)), times brightness(1 - armed x 0.1). The thumb is a child of the track:
    // 24 x 24 px with radius 12 at (trackLeft + 4 + thumbOffset, trackTop + 4). It is white, and the
    // brightness filter of the track applies to it too. Each channel rounds half up to a byte.
    [Theory]
    [InlineData(0, 0, 209, 209, 214, 255)]
    [InlineData(10, 0, 131, 204, 152, 255)]
    [InlineData(20, 0, 52, 199, 89, 255)]
    [InlineData(0, 1, 188, 188, 193, 230)]
    [InlineData(10, 1, 117, 184, 136, 230)]
    [InlineData(20, 1, 47, 179, 80, 230)]
    [InlineData(-0.5, 0, 209, 209, 214, 255)]
    [InlineData(21, 0, 52, 199, 89, 255)]
    public void Draw_gives_the_track_and_the_thumb_with_the_colors_of_the_CSS(
        double thumbOffset, double armed, byte red, byte green, byte blue, byte thumb)
    {
        var commands = ToggleWindowApp.Draw(new ToggleOutput(614, 384, thumbOffset, 1, armed, 0));
        Assert.Equal(
            [
                new FillRoundedRectangle(614, 384, 52, 32, 16, new Rgba(red, green, blue, 255)),
                new FillRoundedRectangle(618 + thumbOffset, 388, 24, 24, 12, new Rgba(thumb, thumb, thumb, 255)),
            ],
            commands);
    }

    // No Handwired test. The two end colors are the CSS colors without a change.
    [Fact]
    public void Draw_gives_the_gray_track_at_the_left_end_and_the_green_track_at_the_right_end()
    {
        Assert.Equal(Gray, ((FillRoundedRectangle)ToggleWindowApp.Draw(new ToggleOutput(0, 0, 0, 0, 0, 0))[0]).Color);
        Assert.Equal(Green, ((FillRoundedRectangle)ToggleWindowApp.Draw(new ToggleOutput(0, 0, 20, 1, 0, 0))[0]).Color);
        Assert.Equal(White, ((FillRoundedRectangle)ToggleWindowApp.Draw(new ToggleOutput(0, 0, 20, 1, 0, 0))[1]).Color);
    }

    // No Handwired test. While the app is idle, a frame returns the same list object, so the window
    // draws it again without a new step. An event, a resize or a running clock gives a new list.
    [Fact]
    public void The_draw_commands_stay_the_same_object_while_the_app_is_idle()
    {
        var app = new ToggleWindowApp(new ToggleMachine());
        var first = app.Frame(Idle(), 1000);
        Assert.Same(first, app.Frame(Idle(), 1016));
        Assert.Same(first, app.Frame(Idle(), 1032));

        var resized = app.Frame(Idle(1000, 700), 1048);
        Assert.NotSame(first, resized);
        Assert.Same(resized, app.Frame(Idle(1000, 700), 1064));

        var pressed = app.Frame(Pointer(500, 350, pressed: true, released: false, 1000, 700), 1080);
        Assert.NotSame(resized, pressed);
        var released = app.Frame(Pointer(500, 350, pressed: false, released: true, 1000, 700), 1096);
        // The release flips the switch on and starts the clock, so each frame steps while the thumb moves.
        var moving = app.Frame(Idle(1000, 700), 1112);
        Assert.NotSame(released, moving);
        Assert.NotSame(moving, app.Frame(Idle(1000, 700), 1128));
    }

    // No Handwired test. The window of index.html: the title, the size of the first window and the
    // body background #f5f5f7.
    [Fact]
    public void The_window_has_the_title_the_size_and_the_background_of_the_page()
    {
        var app = new ToggleWindowApp(new ToggleMachine());
        Assert.Equal("Toggle switch", app.Title);
        Assert.Equal(1280, app.InitialWidth);
        Assert.Equal(800, app.InitialHeight);
        Assert.Equal(new Rgba(0xf5, 0xf5, 0xf7, 255), app.Background);
    }
}
