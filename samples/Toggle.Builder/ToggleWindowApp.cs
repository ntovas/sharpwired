// Handwired source: template/toggle.ts (mountToggle, lines 141-170) and the CSS of template/index.html
// (lines 9-14). The browser host queues pointer events and steps the machine in requestAnimationFrame.
// This window app gets the input of each frame from a window host and returns draw commands.
// This file makes no Raylib call.

using Sharpwired.Raylib;
using Sharpwired.Timing;

namespace Toggle.Builder;

/// <summary>
/// Handwired mountToggle for a window host. Each frame queues the pointer events of the frame at the
/// frame time. It steps the machine when the browser host would request a frame. It returns the
/// track and the thumb as two rounded rectangles.
/// </summary>
public sealed class ToggleWindowApp(ToggleMachine machine) : IWindowApp
{
    private const double TrackRadius = 16;
    private const double ThumbSize = 24;
    private const double ThumbRadius = 12;
    // The CSS colors of index.html: the green of a filled track, the gray of an empty track, the thumb and the body.
    private static readonly Rgba FilledTrack = new(0x34, 0xc7, 0x59, 255);
    private static readonly Rgba EmptyTrack = new(0xd1, 0xd1, 0xd6, 255);
    private static readonly Rgba Thumb = new(255, 255, 255, 255);

    private IReadOnlyList<DrawCommand>? lastCommands;
    private int lastWidth;
    private int lastHeight;

    /// <summary>The title of index.html.</summary>
    public string Title => "Toggle switch";

    /// <summary>The width of the first window. The scripted trace of toggle.test.ts uses 1280 × 800.</summary>
    public int InitialWidth => 1280;

    /// <summary>The height of the first window.</summary>
    public int InitialHeight => 800;

    /// <summary>The body background of index.html, #f5f5f7.</summary>
    public Rgba Background { get; } = new(0xf5, 0xf5, 0xf7, 255);

    /// <summary>
    /// Queue a down event for a press, an up event for a release and a cancel event for a focus loss,
    /// each at frameTimeMs. Raylib gives no event timestamp, so an event applies at the time of the
    /// frame that reads it. Then step the machine and return the draw commands. In one case, return
    /// the commands of the last step. In this case, no event is queued and the clock is stopped. Also,
    /// the viewport did not change and a step exists. The browser host requests no frame in this case.
    /// </summary>
    public IReadOnlyList<DrawCommand> Frame(WindowInput input, double frameTimeMs)
    {
        var x = QuantizeCoordinate(input.PointerX);
        var y = QuantizeCoordinate(input.PointerY);
        if (input.PointerPressed) machine.Queue(new ToggleEvent(ToggleEventKind.Down, x, y, frameTimeMs));
        if (input.PointerReleased) machine.Queue(new ToggleEvent(ToggleEventKind.Up, x, y, frameTimeMs));
        if (input.FocusLost) machine.Queue(new ToggleEvent(ToggleEventKind.Cancel, 0, 0, frameTimeMs));
        var queued = input.PointerPressed || input.PointerReleased || input.FocusLost;
        var resized = input.ViewportWidth != lastWidth || input.ViewportHeight != lastHeight;
        if (lastCommands is not null && !queued && !resized && !machine.Clock.IsRunning) return lastCommands;
        lastWidth = input.ViewportWidth;
        lastHeight = input.ViewportHeight;
        lastCommands = Draw(machine.Step(input.ViewportWidth, input.ViewportHeight, frameTimeMs));
        return lastCommands;
    }

    /// <summary>
    /// Handwired quantize (toggle.ts:161): put a coordinate on the declared 1/64 px grid with JavaScript
    /// Math.round, and clamp it to ±16,384 px.
    /// </summary>
    public static double QuantizeCoordinate(double coordinate) =>
        Math.Min(16_384, Math.Max(-16_384, JsMath.Round(coordinate * 64) / 64));

    /// <summary>
    /// The DOM writes of toggle.ts:153-157 with the CSS of index.html. The track is at (trackLeft,
    /// trackTop), 52 × 32 px with radius 16. Its color is color-mix(in srgb, #34c759 fill, #d1d1d6) with
    /// fill = min(1, max(0, thumbOffset / travel)). The thumb is a child of the track, at (4 +
    /// thumbOffset, 4) in the track, 24 × 24 px with radius 12, white. The filter brightness(1 - armed ×
    /// 0.1) of the track applies to the track and to its child, the thumb. Each channel rounds half up to
    /// a byte. The box shadow of the thumb and aria-checked have no window form.
    /// </summary>
    public static IReadOnlyList<DrawCommand> Draw(ToggleOutput output)
    {
        var fill = Math.Min(1, Math.Max(0, output.ThumbOffset / ToggleLayout.Travel));
        var brightness = 1 - output.Armed * 0.1;
        var track = new Rgba(
            Channel((FilledTrack.R * fill + EmptyTrack.R * (1 - fill)) * brightness),
            Channel((FilledTrack.G * fill + EmptyTrack.G * (1 - fill)) * brightness),
            Channel((FilledTrack.B * fill + EmptyTrack.B * (1 - fill)) * brightness),
            255);
        var thumb = new Rgba(Channel(Thumb.R * brightness), Channel(Thumb.G * brightness), Channel(Thumb.B * brightness), 255);
        return
        [
            new FillRoundedRectangle(output.TrackLeft, output.TrackTop, ToggleLayout.TrackWidth, ToggleLayout.TrackHeight, TrackRadius, track),
            new FillRoundedRectangle(
                output.TrackLeft + ToggleLayout.ThumbInset + output.ThumbOffset,
                output.TrackTop + ToggleLayout.ThumbInset,
                ThumbSize,
                ThumbSize,
                ThumbRadius,
                thumb),
        ];
    }

    // A channel value in 0..255, rounded half up, as an 8-bit channel.
    private static byte Channel(double value) => (byte)Math.Min(255, Math.Max(0, JsMath.Round(value)));
}
