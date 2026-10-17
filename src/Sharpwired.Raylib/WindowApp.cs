// Handwired source: template/toggle.ts (mountToggle, lines 141-170) and template/index.html. The browser
// gives the host DOM events, requestAnimationFrame and CSS. A Sharpwired window host reads one
// WindowInput in each frame and draws the DrawCommand list that the app returns.
// This file makes no Raylib call. The command-line options of the window have no Handwired source.

using System.Globalization;

namespace Sharpwired.Raylib;

/// <summary>
/// What the window read in one frame. It holds the pointer position in screen pixels and the left
/// button edges. It also holds the focus loss edge and the screen size in pixels.
/// </summary>
public readonly record struct WindowInput(
    double PointerX, double PointerY, bool PointerPressed, bool PointerReleased, bool FocusLost,
    int ViewportWidth, int ViewportHeight);

/// <summary>A color with 8-bit channels.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>A command that the window draws in one frame.</summary>
public abstract record DrawCommand;

/// <summary>A filled rectangle with rounded corners, in screen pixels, as a CSS box with border-radius.</summary>
public sealed record FillRoundedRectangle(double X, double Y, double Width, double Height, double Radius, Rgba Color) : DrawCommand
{
    /// <summary>
    /// The Raylib roundness of the rectangle: 2 × radius / min(width, height). The value 1 gives a half
    /// circle at each end of the short side.
    /// </summary>
    public double Roundness => 2 * Radius / Math.Min(Width, Height);
}

/// <summary>
/// An app that a window runs. Frame gets the input of one frame and the time of the frame, and returns
/// the draw commands. It makes no Raylib call.
/// </summary>
public interface IWindowApp
{
    /// <summary>The window title.</summary>
    string Title { get; }

    /// <summary>The width of the window when it opens, in pixels.</summary>
    int InitialWidth { get; }

    /// <summary>The height of the window when it opens, in pixels.</summary>
    int InitialHeight { get; }

    /// <summary>The color that clears the window before the draw commands.</summary>
    Rgba Background { get; }

    /// <summary>Apply the input of one frame at frameTimeMs, and return the commands that draw the frame.</summary>
    IReadOnlyList<DrawCommand> Frame(WindowInput input, double frameTimeMs);
}

/// <summary>
/// The focus loss edge. Raylib has no pointercancel event, so a host reports a focus loss in the frame
/// in which the window changes from focused to not focused.
/// </summary>
public sealed class FocusEdge(bool focused)
{
    private bool wasFocused = focused;

    /// <summary>Record the focus of this frame. Return true when the window was focused in the frame before.</summary>
    public bool Lost(bool focused)
    {
        var lost = wasFocused && !focused;
        wasFocused = focused;
        return lost;
    }
}

/// <summary>
/// The command-line options of a window. <c>--frames N</c> closes the window after N frames.
/// <c>--screenshot path</c> saves the last frame as an image before the window closes. With no option,
/// the window runs until the person closes it.
/// </summary>
public sealed record WindowRunOptions(int? FrameLimit, string? ScreenshotPath)
{
    /// <summary>No frame limit and no screenshot.</summary>
    public static WindowRunOptions Default { get; } = new(null, null);

    /// <summary>True when the window closes after it has drawn framesDrawn frames.</summary>
    public bool StopsAfter(int framesDrawn) => FrameLimit is { } limit && framesDrawn >= limit;

    /// <summary>
    /// Read the options from the command line. Throw <see cref="ArgumentException"/> for an unknown
    /// option, an option with no value, or an option that is given two times. Also throw it for a
    /// frame count that is not a positive integer. A value that starts with "--" is the next option, so the option before
    /// it has no value.
    /// </summary>
    public static WindowRunOptions Parse(IReadOnlyList<string> args)
    {
        int? frameLimit = null;
        string? screenshotPath = null;
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (option != "--frames" && option != "--screenshot") throw new ArgumentException($"unknown option {option}");
            if (index + 1 >= args.Count || args[index + 1].Length == 0 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} needs a value");
            }
            var value = args[++index];
            if (option == "--frames")
            {
                if (frameLimit is not null) throw new ArgumentException("--frames is given two times");
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var frames) || frames <= 0)
                {
                    throw new ArgumentException($"--frames must be a positive integer, got {value}");
                }
                frameLimit = frames;
            }
            else
            {
                if (screenshotPath is not null) throw new ArgumentException("--screenshot is given two times");
                screenshotPath = value;
            }
        }
        return new WindowRunOptions(frameLimit, screenshotPath);
    }
}
