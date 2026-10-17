// Handwired source: template/toggle.ts (mountToggle, lines 141-170) and template/index.html. The browser
// gives the page its pointer events, its frames and its drawing. This class gives the same to an
// IWindowApp with Raylib-cs. It is the only file that uses the namespace Raylib_cs.

using Raylib_cs;

namespace Sharpwired.Raylib;

/// <summary>The window host: it reads the input of each frame, calls the app, and draws the commands.</summary>
public static class RaylibWindow
{
    // A fixed segment count for each rounded corner. With the value 0, Raylib computes the count from
    // the radius in logical pixels, which gives a corner with visible edges on a 2 × display.
    private const int CornerSegments = 16;

    /// <summary>
    /// Open a window for the app and run it. The run stops when the person closes the window (the
    /// close button or the Esc key), or at the frame limit of the options. In each frame, read the
    /// pointer, the left button edges, the focus loss edge and the screen size. Then read the clock
    /// one time, call <see cref="IWindowApp.Frame"/>, and draw the commands on the background. With a
    /// screenshot path, draw the last commands again and save them as an image before the window
    /// closes.
    /// </summary>
    public static void Run(IWindowApp app, IFrameClock clock, WindowRunOptions? options = null)
    {
        options ??= WindowRunOptions.Default;
        Raylib_cs.Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.HighDpiWindow);
        Raylib_cs.Raylib.InitWindow(app.InitialWidth, app.InitialHeight, app.Title);
        // CloseWindow is for a window that opened. When InitWindow fails, no window is closed.
        if (!Raylib_cs.Raylib.IsWindowReady()) throw new InvalidOperationException("the Raylib window did not open");
        try
        {
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
                    Raylib_cs.Raylib.GetScreenHeight());
                var nowMs = clock.NowMs;
                commands = app.Frame(input, nowMs);
                Raylib_cs.Raylib.BeginDrawing();
                Draw(app.Background, commands);
                Raylib_cs.Raylib.EndDrawing();
                framesDrawn++;
            }
            if (options.ScreenshotPath is { } path) SaveScreenshot(app.Background, commands, path);
        }
        finally
        {
            Raylib_cs.Raylib.CloseWindow();
        }
    }

    private static void Draw(Rgba background, IReadOnlyList<DrawCommand> commands)
    {
        Raylib_cs.Raylib.ClearBackground(ToColor(background));
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
    private static void SaveScreenshot(Rgba background, IReadOnlyList<DrawCommand> commands, string path)
    {
        Raylib_cs.Raylib.BeginDrawing();
        Draw(background, commands);
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
}
