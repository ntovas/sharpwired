// No Handwired source. These tests cover the parts of the window host that make no Raylib call.
// These parts are the roundness of a rounded rectangle, the focus loss edge, and the command-line
// options.

namespace Sharpwired.Raylib.Tests;

public class WindowAppTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    // No Handwired test. The CSS border-radius of a 52 x 32 track is 16 px, which is a full half circle
    // at each end. The Raylib roundness of that rectangle is 2 x 16 / 32 = 1.
    [Fact]
    public void The_roundness_of_a_rounded_rectangle_is_twice_the_radius_over_the_short_side()
    {
        Assert.Equal(1.0, new FillRoundedRectangle(0, 0, 52, 32, 16, White).Roundness);
        Assert.Equal(1.0, new FillRoundedRectangle(4, 4, 24, 24, 12, White).Roundness);
        Assert.Equal(0.25, new FillRoundedRectangle(0, 0, 40, 80, 5, White).Roundness);
        Assert.Equal(0.0, new FillRoundedRectangle(0, 0, 40, 80, 0, White).Roundness);
    }

    // No Handwired test. Raylib has no pointercancel. The host queues a cancel event in the frame in
    // which the window changes from focused to not focused, and in no other frame.
    [Fact]
    public void A_focus_loss_is_reported_only_in_the_frame_in_which_the_focus_changes_from_true_to_false()
    {
        var edge = new FocusEdge(focused: true);
        Assert.False(edge.Lost(focused: true));
        Assert.True(edge.Lost(focused: false));
        Assert.False(edge.Lost(focused: false));
        Assert.False(edge.Lost(focused: true));
        Assert.True(edge.Lost(focused: false));

        var unfocused = new FocusEdge(focused: false);
        Assert.False(unfocused.Lost(focused: false));
        Assert.False(unfocused.Lost(focused: true));
        Assert.True(unfocused.Lost(focused: false));
    }

    // No Handwired test. With no option, the window runs until the person closes it.
    [Fact]
    public void No_option_gives_no_frame_limit_and_no_screenshot()
    {
        var options = WindowRunOptions.Parse([]);
        Assert.Null(options.FrameLimit);
        Assert.Null(options.ScreenshotPath);
        Assert.False(options.StopsAfter(1_000_000));
    }

    // No Handwired test. --frames N closes the window after N frames, and --screenshot keeps a path.
    [Fact]
    public void Frames_and_screenshot_options_set_the_frame_limit_and_the_screenshot_path()
    {
        var options = WindowRunOptions.Parse(["--frames", "30", "--screenshot", "/tmp/toggle window.png"]);
        Assert.Equal(30, options.FrameLimit);
        Assert.Equal("/tmp/toggle window.png", options.ScreenshotPath);
        Assert.False(options.StopsAfter(29));
        Assert.True(options.StopsAfter(30));

        var reversed = WindowRunOptions.Parse(["--screenshot", "a.png", "--frames", "1"]);
        Assert.Equal(1, reversed.FrameLimit);
        Assert.Equal("a.png", reversed.ScreenshotPath);
        Assert.False(reversed.StopsAfter(0));
        Assert.True(reversed.StopsAfter(1));
    }

    // No Handwired test. A bad command line fails before the window opens, with a message that names the fault.
    [Theory]
    [InlineData(new[] { "--frames" }, "--frames needs a value")]
    [InlineData(new[] { "--frames", "0" }, "--frames must be a positive integer, got 0")]
    [InlineData(new[] { "--frames", "-3" }, "--frames must be a positive integer, got -3")]
    [InlineData(new[] { "--frames", "1.5" }, "--frames must be a positive integer, got 1.5")]
    [InlineData(new[] { "--screenshot" }, "--screenshot needs a value")]
    [InlineData(new[] { "--screenshot", "" }, "--screenshot needs a value")]
    // A value that starts with -- is the next option, so the option before it has no value.
    [InlineData(new[] { "--screenshot", "--frames", "3" }, "--screenshot needs a value")]
    [InlineData(new[] { "--frames", "--screenshot", "a.png" }, "--frames needs a value")]
    [InlineData(new[] { "--frames", "--3" }, "--frames needs a value")]
    [InlineData(new[] { "--frames", "2", "--frames", "3" }, "--frames is given two times")]
    [InlineData(new[] { "--screenshot", "a.png", "--screenshot", "b.png" }, "--screenshot is given two times")]
    [InlineData(new[] { "--fps", "60" }, "unknown option --fps")]
    public void A_bad_command_line_is_refused_with_a_message_that_names_the_fault(string[] args, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => WindowRunOptions.Parse(args));
        Assert.Equal(message, error.Message);
    }
}
