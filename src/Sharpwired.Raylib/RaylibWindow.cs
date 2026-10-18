// Handwired source: template/toggle.ts (mountToggle, lines 141-170) and template/index.html. The browser
// gives the page its pointer events, its frames and its drawing. This class gives the same to an
// IWindowApp with Raylib-cs. It is the only file that uses the namespace Raylib_cs.
// Phase 6 task 8 (Handwired src/json-search/demo.ts, which reads input and keydown events, writes text nodes and
// puts one ImageData on a canvas): the typed text and the key presses of each frame, the pixel scale, the fonts
// of the app with the replacement glyph, the commands FillRectangle, DrawText, DrawGlyphs and DrawPixels, and one
// texture for each DrawPixels command, which the window releases before it closes.

using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Raylib_cs;

namespace Sharpwired.Raylib;

/// <summary>The window host: it reads the input of each frame, calls the app, and draws the commands.</summary>
public static class RaylibWindow
{
    // A fixed segment count for each rounded corner. With the value 0, Raylib computes the count from
    // the radius in logical pixels, which gives a corner with visible edges on a 2 × display.
    private const int CornerSegments = 16;

    // The pixel height at which the fonts load: more than twice the largest glyph of the apps, so that a glyph on
    // a display with two device pixels for each screen pixel stays sharp.
    private const int FontLoadSize = 36;

    private static readonly (Key Key, KeyboardKey[] RaylibKeys)[] KeyMap =
    [
        (Key.Backspace, [KeyboardKey.Backspace]),
        (Key.Delete, [KeyboardKey.Delete]),
        (Key.Left, [KeyboardKey.Left]),
        (Key.Right, [KeyboardKey.Right]),
        (Key.Home, [KeyboardKey.Home]),
        (Key.End, [KeyboardKey.End]),
        (Key.Enter, [KeyboardKey.Enter, KeyboardKey.KpEnter]),
        (Key.Digit1, [KeyboardKey.One]),
        (Key.Digit2, [KeyboardKey.Two]),
        (Key.Digit3, [KeyboardKey.Three]),
        (Key.B, [KeyboardKey.B]),
    ];

    /// <summary>
    /// Open a window for the app and run it. The run stops when the person closes the window (the
    /// close button or the Esc key), or at the frame limit of the options. In each frame, read the
    /// pointer, the left button edges, the focus loss edge, the screen size, the typed text, the key
    /// presses and the pixel scale. Then read the clock one time, call <see cref="IWindowApp.Frame"/>,
    /// and draw the commands on the background. With a screenshot path, draw the last commands again
    /// and save them as an image before the window closes.
    /// </summary>
    public static void Run(IWindowApp app, IFrameClock clock, WindowRunOptions? options = null)
    {
        options ??= WindowRunOptions.Default;
        Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.HighDpiWindow);
        Raylib_cs.Raylib.InitWindow(app.InitialWidth, app.InitialHeight, app.Title);
        // CloseWindow is for a window that opened. When InitWindow fails, no window is closed.
        if (!Raylib_cs.Raylib.IsWindowReady()) throw new InvalidOperationException("the Raylib window did not open");
        WindowResources? resources = null;
        try
        {
            resources = new WindowResources(app);
            Raylib_cs.Raylib.SetTargetFPS(60);
            var focus = new FocusEdge(Raylib_cs.Raylib.IsWindowFocused());
            IReadOnlyList<DrawCommand> commands = [];
            var framesDrawn = 0;
            while (!Raylib_cs.Raylib.WindowShouldClose() && !options.StopsAfter(framesDrawn))
            {
                // Limit: Raylib gives only the button state of each input poll, one poll for each
                // frame. A press and a release inside one poll give no edge, so the window loses a
                // click that is shorter than one frame. Raylib-cs gives no button event callback.
                // See decision 6 of the phase 3 plan.
                var pointer = Raylib_cs.Raylib.GetMousePosition();
                var input = new WindowInput(
                    pointer.X,
                    pointer.Y,
                    Raylib_cs.Raylib.IsMouseButtonPressed(MouseButton.Left),
                    Raylib_cs.Raylib.IsMouseButtonReleased(MouseButton.Left),
                    focus.Lost(Raylib_cs.Raylib.IsWindowFocused()),
                    Raylib_cs.Raylib.GetScreenWidth(),
                    Raylib_cs.Raylib.GetScreenHeight())
                {
                    Text = ReadText(),
                    Keys = ReadKeys(),
                    PixelScale = Raylib_cs.Raylib.GetWindowScaleDPI().X,
                };
                var nowMs = clock.NowMs;
                commands = app.Frame(input, nowMs);
                Raylib_cs.Raylib.BeginDrawing();
                Draw(app.Background, commands, resources);
                Raylib_cs.Raylib.EndDrawing();
                framesDrawn++;
            }
            if (options.ScreenshotPath is { } path) SaveScreenshot(app.Background, commands, path, resources);
        }
        finally
        {
            // The fonts and the textures need the window, so they go before CloseWindow.
            resources?.Dispose();
            Raylib_cs.Raylib.CloseWindow();
        }
    }

    // The characters of the frame. GetCharPressed gives a Unicode code point. A value that is not a Unicode scalar
    // value, for example a lone surrogate, is left out, so that the app gets only whole code points.
    private static string ReadText()
    {
        var text = new StringBuilder();
        for (var codePoint = Raylib_cs.Raylib.GetCharPressed(); codePoint != 0; codePoint = Raylib_cs.Raylib.GetCharPressed())
        {
            if (!Rune.IsValid(codePoint)) continue;
            text.Append(char.ConvertFromUtf32(codePoint));
        }
        return text.ToString();
    }

    // The presses and the repeats of the keys of the app. On macOS, Command counts as Control.
    private static ImmutableArray<KeyPress> ReadKeys()
    {
        var shift = Raylib_cs.Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib_cs.Raylib.IsKeyDown(KeyboardKey.RightShift);
        var control = Raylib_cs.Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib_cs.Raylib.IsKeyDown(KeyboardKey.RightControl)
            || Raylib_cs.Raylib.IsKeyDown(KeyboardKey.LeftSuper) || Raylib_cs.Raylib.IsKeyDown(KeyboardKey.RightSuper);
        var keys = ImmutableArray.CreateBuilder<KeyPress>();
        foreach (var (key, raylibKeys) in KeyMap)
        {
            foreach (var raylibKey in raylibKeys)
            {
                if (!Raylib_cs.Raylib.IsKeyPressed(raylibKey) && !Raylib_cs.Raylib.IsKeyPressedRepeat(raylibKey)) continue;
                keys.Add(new KeyPress(key, shift, control));
                break;
            }
        }
        return keys.ToImmutable();
    }

    private static void Draw(Rgba background, IReadOnlyList<DrawCommand> commands, WindowResources resources)
    {
        Raylib_cs.Raylib.ClearBackground(ToColor(background));
        var pixelPlace = 0;
        foreach (var command in commands)
        {
            switch (command)
            {
                case FillRoundedRectangle rectangle:
                    Raylib_cs.Raylib.DrawRectangleRounded(
                        new Rectangle((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height),
                        (float)rectangle.Roundness,
                        CornerSegments,
                        ToColor(rectangle.Color));
                    break;
                case FillRectangle rectangle:
                    Raylib_cs.Raylib.DrawRectangleRec(
                        new Rectangle((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height),
                        ToColor(rectangle.Color));
                    break;
                case DrawText text:
                    resources.DrawText(text);
                    break;
                case DrawGlyphs glyphs:
                    resources.DrawGlyphs(glyphs);
                    break;
                case DrawPixels pixels:
                    resources.DrawPixels(pixels, pixelPlace++);
                    break;
                default:
                    throw new InvalidOperationException($"the window cannot draw a {command.GetType().Name}");
            }
        }
    }

    // Draw the last commands again and read the frame buffer before EndDrawing swaps it. Raylib keeps
    // each shape in its render batch, and only ClearBackground writes to the frame buffer at once. So
    // DrawRenderBatchActive must draw the batch before LoadImageFromScreen reads the buffer. Without it,
    // the image holds only the background. Raylib TakeScreenshot keeps only the file name of a path, so
    // this method exports the image itself.
    private static void SaveScreenshot(Rgba background, IReadOnlyList<DrawCommand> commands, string path, WindowResources resources)
    {
        Raylib_cs.Raylib.BeginDrawing();
        Draw(background, commands, resources);
        Rlgl.DrawRenderBatchActive();
        var image = Raylib_cs.Raylib.LoadImageFromScreen();
        Raylib_cs.Raylib.EndDrawing();
        try
        {
            if (!Raylib_cs.Raylib.ExportImage(image, path)) throw new InvalidOperationException($"the screenshot could not be saved to {path}");
        }
        finally
        {
            Raylib_cs.Raylib.UnloadImage(image);
        }
    }

    private static Color ToColor(Rgba color) => new(color.R, color.G, color.B, color.A);

    // The fonts of the app and the textures of the DrawPixels commands. The fonts load when the window opens.
    private sealed class WindowResources : IDisposable
    {
        private readonly List<Font> loadedFonts = [];
        private readonly Font[] fonts;
        private readonly GlyphFonts choice;
        private readonly Dictionary<int, (Texture2D Texture, int Width, int Height, int Version)> textures = [];
        private bool disposed;

        // Load each font file of the app that exists, with the code points of the app that its character map has.
        // Write to the error output the glyph count of each font and the code points that no font has.
        public WindowResources(IWindowApp app)
        {
            var codePoints = GlyphFonts.CodePointsToLoad(app.FontCodePoints);
            var sets = new List<IReadOnlySet<int>>();
            var drawn = new List<Font>();
            foreach (var path in app.FontPaths)
            {
                if (!File.Exists(path)) continue;
                HashSet<int> covered;
                try
                {
                    covered = FontCoverage.Covered(File.ReadAllBytes(path), codePoints);
                }
                catch (ArgumentException error)
                {
                    Console.Error.WriteLine($"[window] the font {path} was not loaded: {error.Message}");
                    continue;
                }
                var own = codePoints.Where(covered.Contains).ToArray();
                if (own.Length == 0) continue;
                var font = Raylib_cs.Raylib.LoadFontEx(path, FontLoadSize, own, own.Length);
                if (!Raylib_cs.Raylib.IsFontValid(font))
                {
                    Console.Error.WriteLine($"[window] Raylib did not load the font {path}");
                    continue;
                }
                Raylib_cs.Raylib.SetTextureFilter(font.Texture, TextureFilter.Bilinear);
                loadedFonts.Add(font);
                drawn.Add(font);
                sets.Add(covered);
                Console.Error.WriteLine($"[window] the font {path} has {covered.Count} of the {codePoints.Length} code points, and Raylib loaded {font.GlyphCount} glyphs");
            }
            if (drawn.Count == 0)
            {
                drawn.Add(Raylib_cs.Raylib.GetFontDefault());
                choice = GlyphFonts.DefaultOnly();
                Console.Error.WriteLine("[window] no font file of the app exists, so the Raylib default font draws ASCII");
            }
            else
            {
                choice = new GlyphFonts(sets);
            }
            fonts = [.. drawn];
            var missing = choice.Missing(codePoints);
            Console.Error.WriteLine(missing.Count == 0
                ? $"[window] the fonts have each of the {codePoints.Length} code points of the app"
                : $"[window] no font has these {missing.Count} code points, so they show as '?': {string.Join(" ", missing.Take(40).Select(codePoint => $"U+{codePoint:X4}"))}");
        }

        public void DrawText(DrawText text)
        {
            var x = (float)text.X;
            foreach (var rune in text.Text.EnumerateRunes()) x += DrawCodePoint(rune.Value, x, (float)text.Y, (float)text.Size, text.Color);
        }

        public void DrawGlyphs(DrawGlyphs grid)
        {
            foreach (var glyph in grid.Glyphs)
            {
                var x = grid.X + (glyph.Column * grid.CellWidth);
                var y = grid.Y + (glyph.Row * grid.LineHeight) + ((grid.LineHeight - grid.Size) / 2);
                DrawCodePoint(glyph.CodePoint, (float)x, (float)y, (float)grid.Size, glyph.Color);
            }
        }

        // Keep one texture for each place, and copy the pixels only when the version or the size changes.
        public void DrawPixels(DrawPixels pixels, int place)
        {
            if (!textures.TryGetValue(place, out var entry) || entry.Width != pixels.Width || entry.Height != pixels.Height)
            {
                if (textures.ContainsKey(place)) Raylib_cs.Raylib.UnloadTexture(entry.Texture);
                var image = Raylib_cs.Raylib.GenImageColor(pixels.Width, pixels.Height, new Color(0, 0, 0, 0));
                var texture = Raylib_cs.Raylib.LoadTextureFromImage(image);
                Raylib_cs.Raylib.UnloadImage(image);
                Raylib_cs.Raylib.SetTextureFilter(texture, TextureFilter.Point);
                Raylib_cs.Raylib.UpdateTexture(texture, pixels.Pixels.AsSpan());
                entry = (texture, pixels.Width, pixels.Height, pixels.Version);
                textures[place] = entry;
            }
            else if (entry.Version != pixels.Version)
            {
                Raylib_cs.Raylib.UpdateTexture(entry.Texture, pixels.Pixels.AsSpan());
                entry = entry with { Version = pixels.Version };
                textures[place] = entry;
            }
            Raylib_cs.Raylib.DrawTexturePro(
                entry.Texture,
                new Rectangle(0, 0, pixels.Width, pixels.Height),
                new Rectangle((float)pixels.X, (float)pixels.Y, (float)pixels.DrawnWidth, (float)pixels.DrawnHeight),
                Vector2.Zero,
                0,
                Color.White);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in textures.Values) Raylib_cs.Raylib.UnloadTexture(entry.Texture);
            textures.Clear();
            foreach (var font in loadedFonts) Raylib_cs.Raylib.UnloadFont(font);
            loadedFonts.Clear();
        }

        // Draw one code point with the first font that has it, and return its advance in screen pixels.
        private float DrawCodePoint(int codePoint, float x, float y, float size, Rgba color)
        {
            var (index, drawn) = choice.Resolve(codePoint);
            var font = fonts[index];
            if (drawn != ' ') Raylib_cs.Raylib.DrawTextCodepoint(font, drawn, new Vector2(x, y), size, ToColor(color));
            // The Raylib default font gives an advance of 0, and then the width of the glyph in the atlas is the advance.
            var info = Raylib_cs.Raylib.GetGlyphInfo(font, drawn);
            var advance = info.AdvanceX > 0 ? info.AdvanceX : Raylib_cs.Raylib.GetGlyphAtlasRec(font, drawn).Width + 1;
            return advance * size / font.BaseSize;
        }
    }
}
