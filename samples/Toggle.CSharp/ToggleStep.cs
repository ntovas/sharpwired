// Handwired source: template/toggle.ts, lines 9-84 (layout, matrixBound, coordinate, toggleCells and
// buildToggle with its local function insideTrack).
//
// The toggle switch as a plain C# method. Step runs as a normal .NET method, and the IL translator
// (Sharpwired.IL) translates it into the program of the builder toggle (Toggle.Builder.ToggleProgram),
// operation for operation. The plain types declare the 20 cells: their properties give the cell
// names, and the attributes give the ranges, grids and initial values of toggleCells. Step has one
// statement for each step of ToggleProgram.Build, in the same order. Hw.AnimatedObject replaces the
// animation macro, which has no plain C# form.

using Sharpwired.Annotations;

namespace Toggle.CSharp;

/// <summary>
/// The spring of the thumb in px from the off side: its displacement from the destination, its
/// velocity, and the destination. The bounds scale with the travel, and leave space for a click
/// while the thumb moves.
/// </summary>
public readonly record struct ThumbState(
    [Range(-40, 40)] float Displacement,
    [Range(-1280, 1280)] float Velocity,
    [Range(0, 20)] float Destination);

/// <summary>The state cells: the switch position, the armed bit, and the spring of the thumb.</summary>
public sealed record ToggleState(bool On, bool Armed, ThumbState Thumb);

/// <summary>One pointer event: the event bit and the coordinates. The host puts coordinates on a 1/64 px grid.</summary>
public readonly record struct Pointer(
    [Event] bool Active,
    [Range(-16384, 16384), Grid(1.0 / 64)] float X,
    [Range(-16384, 16384), Grid(1.0 / 64)] float Y);

/// <summary>
/// The input and event cells. The host writes the 2 × 2 matrix of the spring steps of an evaluation
/// into SpringMatrix, each entry within ±MatrixBound. SettleCheck tells whether the spring can come to
/// rest, and Snap tells whether the thumb snaps onto its destination.
/// </summary>
public sealed record ToggleInput(
    [Range(0, 16384), Grid(1), Initial(1280)] float ViewportWidth,
    [Range(0, 16384), Grid(1), Initial(800)] float ViewportHeight,
    Pointer Down,
    Pointer Up,
    [Event] bool Cancel,
    [FixedLength(4), Range(-ToggleStep.MatrixBound, ToggleStep.MatrixBound)] float[] SpringMatrix,
    bool SettleCheck,
    bool Snap);

/// <summary>The outputs, in the order of the Handwired output object.</summary>
public sealed record ToggleOutput(float TrackLeft, float TrackTop, float ThumbOffset, bool On, bool Armed, bool Active);

/// <summary>Handwired buildToggle as a plain C# method.</summary>
public static class ToggleStep
{
    /// <summary>
    /// SpringMatrix.SpringMatrixBound(ToggleLayout.Spring), as a literal, because an attribute argument
    /// must be a constant. A test checks that the two values are equal.
    /// </summary>
    public const double MatrixBound = 10.360516548156738;

    private const float TrackWidth = 52;
    private const float TrackHeight = 32;
    private const float Travel = 20;

    /// <summary>
    /// One evaluation of the toggle. As a native switch, it acts on release. A press on the track arms
    /// it, and a release on the track while armed flips it. A release off the track or a cancel
    /// disarms it. The inputs apply in a fixed order: down, then up, then cancel. The thumb springs to
    /// its new side.
    /// </summary>
    [Handwired]
    public static (ToggleState Next, ToggleOutput Output) Step(ToggleState state, ToggleInput input)
    {
        float trackLeft = input.ViewportWidth * 0.5f - TrackWidth / 2;
        float trackTop = input.ViewportHeight * 0.5f - TrackHeight / 2;
        bool armedAfterDown = input.Down.Active ? InsideTrack(input.Down, trackLeft, trackTop) : state.Armed;
        bool on = input.Up.Active && armedAfterDown && InsideTrack(input.Up, trackLeft, trackTop) ? !state.On : state.On;
        bool armed = armedAfterDown && !input.Up.Active && !input.Cancel;
        float destination = on ? Travel : 0f;
        // The steps simulate time before the events of this evaluation, so they pull toward the
        // previous destination, state.Thumb.Destination.
        var thumb = Hw.AnimatedObject(input.SpringMatrix, state.Thumb.Displacement, state.Thumb.Velocity, destination,
            state.Thumb.Destination, input.SettleCheck, input.Snap);
        return (new ToggleState(on, armed, new ThumbState(thumb.Displacement, thumb.Velocity, destination)),
            new ToggleOutput(trackLeft, trackTop, thumb.Position, on, armed, thumb.Active));
    }

    [Handwired]
    private static bool InsideTrack(Pointer point, float trackLeft, float trackTop) =>
        point.X >= trackLeft && point.X < trackLeft + TrackWidth && point.Y >= trackTop && point.Y < trackTop + TrackHeight;
}
