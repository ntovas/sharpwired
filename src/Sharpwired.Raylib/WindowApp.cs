// Handwired source: template/toggle.ts (mountToggle, lines 141-170) and template/index.html. The browser
// gives the host DOM events, requestAnimationFrame and CSS. A Sharpwired window host reads one
// WindowInput in each frame and draws the DrawCommand list that the app returns.
// This file makes no Raylib call. The command-line options of the window have no Handwired source.
// Phase 6 task 8 adds the keyboard input (the browser gives the page input and keydown events, demo.ts), the
// pixel scale, four draw commands for text, glyphs and pixels (demo.ts writes text nodes and one canvas
// ImageData), and the font members of an app.

using System.Collections.Immutable;
using System.Globalization;

namespace Sharpwired.Raylib;

/// <summary>The keys that a window reads. On macOS, Command counts as Control.</summary>
public enum Key
{
    Backspace,
    Delete,
    Left,
    Right,
    Home,
    End,
    Enter,
    Digit1,
    Digit2,
    Digit3,
    B,
}

/// <summary>One key press, or one repeat of a key that the person holds, with the state of Shift and Control.</summary>
public readonly record struct KeyPress(Key Key, bool Shift, bool Control);

/// <summary>
/// What the window read in one frame. It holds the pointer position in screen pixels and the left
/// button edges. It also holds the focus loss edge and the screen size in pixels. The init members hold
/// the keyboard input of the frame and the pixel scale. The phase 3 constructor leaves them empty.
/// </summary>
public readonly record struct WindowInput(
    double PointerX, double PointerY, bool PointerPressed, bool PointerReleased, bool FocusLost,
    int ViewportWidth, int ViewportHeight)
{
    private readonly string? text;
    private readonly ImmutableArray<KeyPress> keys;
    private readonly double pixelScale;

    /// <summary>The characters that the person typed in the frame, in order. Null reads as "".</summary>
    public string Text
    {
        get => text ?? "";
        init => text = value;
    }

    /// <summary>The key presses and repeats of the frame, in order. A default array reads as empty.</summary>
    public ImmutableArray<KeyPress> Keys
    {
        get => keys.IsDefault ? [] : keys;
        init => keys = value;
    }

    /// <summary>
    /// The device pixels for each screen pixel, for example 2 on a display with two device pixels for each
    /// screen pixel. A value of 0 or less reads as 1.
    /// </summary>
    public double PixelScale
    {
        get => pixelScale > 0 ? pixelScale : 1;
        init => pixelScale = value;
    }
}

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

/// <summary>A filled rectangle with square corners, in screen pixels.</summary>
public sealed record FillRectangle(double X, double Y, double Width, double Height, Rgba Color) : DrawCommand;

/// <summary>
/// A label: the text with its top left corner at (X, Y), Size pixels high, in the first font that has each
/// code point.
/// </summary>
public sealed record DrawText(double X, double Y, string Text, double Size, Rgba Color) : DrawCommand;

/// <summary>One code point in the cell at (Column, Row) of a glyph grid.</summary>
public readonly record struct Glyph(int CodePoint, int Column, int Row, Rgba Color);

/// <summary>
/// Glyphs on a fixed grid: the cell at (column, row) has its top left corner at (X + column × CellWidth,
/// Y + row × LineHeight). The grid does not depend on the font, so an app can place text with no font
/// metrics. Size is the height of a glyph in pixels.
/// </summary>
public sealed record DrawGlyphs : DrawCommand
{
    /// <summary>Make the command. A default glyph array is empty.</summary>
    /// <exception cref="ArgumentException">The cell width, the line height or the size is not positive.</exception>
    public DrawGlyphs(double x, double y, double cellWidth, double lineHeight, double size, ImmutableArray<Glyph> glyphs)
    {
        if (!(cellWidth > 0) || !(lineHeight > 0) || !(size > 0))
        {
            throw new ArgumentException($"a glyph grid needs a positive cell, got {cellWidth} × {lineHeight} and size {size}");
        }
        this.X = x;
        this.Y = y;
        this.CellWidth = cellWidth;
        this.LineHeight = lineHeight;
        this.Size = size;
        this.Glyphs = glyphs.IsDefault ? [] : glyphs;
    }

    public double X { get; }

    public double Y { get; }

    public double CellWidth { get; }

    public double LineHeight { get; }

    public double Size { get; }

    public ImmutableArray<Glyph> Glyphs { get; }
}

/// <summary>
/// An image of Width × Height pixels, row after row, each pixel a <see cref="Word"/>. The window keeps one
/// texture for each DrawPixels command of a frame, by its place in the command list, and copies the pixels
/// into the texture only when Version changes. The image covers Width / Scale × Height / Scale screen pixels
/// from (X, Y), so that a Scale equal to the pixel scale puts each pixel on one device pixel.
/// </summary>
public sealed record DrawPixels : DrawCommand
{
    /// <summary>Make the command.</summary>
    /// <exception cref="ArgumentException">The size or the scale is not positive, or the pixel count is not Width × Height.</exception>
    public DrawPixels(double x, double y, int width, int height, ImmutableArray<uint> pixels, int version, double scale = 1)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"an image needs a positive size, got {width} × {height}");
        if (!(scale > 0)) throw new ArgumentException($"an image needs a positive scale, got {scale}");
        if (pixels.IsDefault || pixels.Length != (long)width * height)
        {
            throw new ArgumentException($"an image of {width} × {height} needs {(long)width * height} pixels, got {(pixels.IsDefault ? 0 : pixels.Length)}");
        }
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
        this.Pixels = pixels;
        this.Version = version;
        this.Scale = scale;
    }

    public double X { get; }

    public double Y { get; }

    public int Width { get; }

    public int Height { get; }

    public ImmutableArray<uint> Pixels { get; }

    public int Version { get; }

    public double Scale { get; }

    /// <summary>The width of the image on the screen, in screen pixels.</summary>
    public double DrawnWidth => Width / Scale;

    /// <summary>The height of the image on the screen, in screen pixels.</summary>
    public double DrawnHeight => Height / Scale;

    /// <summary>The pixel word of a color: the bytes R, G, B, A in memory order (R | G &lt;&lt; 8 | B &lt;&lt; 16 | A &lt;&lt; 24).</summary>
    public static uint Word(Rgba color) => color.R | ((uint)color.G << 8) | ((uint)color.B << 16) | ((uint)color.A << 24);
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

    /// <summary>
    /// The font files for the text and the glyphs, in order of preference. The window loads each file that
    /// exists, and draws each code point with the first font that has it. With no file, the Raylib default
    /// font draws ASCII.
    /// </summary>
    IReadOnlyList<string> FontPaths => [];

    /// <summary>The code points that the app draws past ASCII 32..126. The window loads them from the fonts.</summary>
    IReadOnlyList<int> FontCodePoints => [];
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
