// Handwired source: template/toggle.ts, lines 1-84 (layout, springConstants, matrixBound,
// matrixEntry, coordinate, toggleCells, buildToggle with its local functions all, not and
// insideTrack).
//
// A toggle switch at the center of the viewport, as one numeric program. As a native switch, it acts
// on release. A press on the track arms it, and a release on the track while armed flips it. A
// release off the track or a cancel disarms it. The thumb springs to its new side.
//
// Handwired declares toggleCells as a nested object literal and gets the same shape with wires. C#
// has no mapped types, so ToggleCells is a record, and Declare fills it with ICellScope calls in the
// order of the Handwired object.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Timing;

namespace Toggle.Builder;

/// <summary>Handwired layout and springConstants.</summary>
public static class ToggleLayout
{
    public const double TrackWidth = 52;
    public const double TrackHeight = 32;
    public const double ThumbInset = 4;
    public const double Travel = 20;

    /// <summary>The spring of the thumb.</summary>
    public static SpringConstants Spring { get; } = new(650, 45);
}

/// <summary>The cells of one pointer event: the event bit and the coordinates.</summary>
public sealed record PointerCells(Wire Active, Wire X, Wire Y);

/// <summary>
/// The spring of the thumb in px from the off side: its displacement from the destination, its
/// velocity, and the destination.
/// </summary>
public sealed record ThumbCells(Wire Displacement, Wire Velocity, Wire Destination);

/// <summary>Handwired toggleCells: each number that the program reads, or keeps from one evaluation to the next.</summary>
public sealed record ToggleCells(
    Wire ViewportWidth,
    Wire ViewportHeight,
    PointerCells Down,
    PointerCells Up,
    Wire Cancel,
    Wire On,
    Wire Armed,
    Wire[] SpringMatrix,
    Wire SettleCheck,
    Wire Snap,
    ThumbCells Thumb);

/// <summary>The output wires of the toggle, in the order of the Handwired output object.</summary>
public sealed record ToggleOutputWires(Wire TrackLeft, Wire TrackTop, Wire ThumbOffset, Wire On, Wire Armed, Wire Active);

/// <summary>The return value of Handwired buildToggle: the cells, the outputs, and the finished table.</summary>
public sealed record BuiltToggle(ToggleCells Cells, ToggleOutputWires Output, FinishedTable Table);

/// <summary>Handwired buildToggle and toggleCells.</summary>
public static class ToggleProgram
{
    // The host writes the 2 × 2 matrix of the spring steps of an evaluation into 4 input cells, each
    // within ±MatrixBound.
    private static readonly double MatrixBound = SpringMatrix.SpringMatrixBound(ToggleLayout.Spring);

    private static CellSpec MatrixEntry() => Cell.Input(new NumericRange(-MatrixBound, MatrixBound));

    // The host puts pointer coordinates on a 1/64 px grid.
    private static CellSpec Coordinate() => Cell.Input(new NumericRange(-16_384, 16_384), grid: 1.0 / 64);

    private static PointerCells DeclarePointer(ICellScope scope) =>
        new(scope.Declare("active", Cell.Event()), scope.Declare("x", Coordinate()), scope.Declare("y", Coordinate()));

    /// <summary>Handwired toggleCells: declare the 20 cells of the toggle in the scope, in the Handwired order.</summary>
    public static ToggleCells Declare(ICellScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var viewportWidth = scope.Declare("viewportWidth", Cell.Input(new NumericRange(0, 16_384), initial: 1280, grid: 1));
        var viewportHeight = scope.Declare("viewportHeight", Cell.Input(new NumericRange(0, 16_384), initial: 800, grid: 1));
        var down = scope.Declare("down", DeclarePointer);
        var up = scope.Declare("up", DeclarePointer);
        var cancel = scope.Declare("cancel", Cell.Event());
        var on = scope.Declare("on", Cell.StateBit());
        var armed = scope.Declare("armed", Cell.StateBit());
        // What AdvanceFrame plans for each evaluation: the spring matrix of its steps, and whether the
        // spring can come to rest (a settle check). The snap cell tells whether the thumb snaps onto
        // its destination, because the frame needs too many steps.
        var springMatrix = scope.Declare("springMatrix", [MatrixEntry(), MatrixEntry(), MatrixEntry(), MatrixEntry()]);
        var settleCheck = scope.Declare("settleCheck", Cell.InputBit());
        var snap = scope.Declare("snap", Cell.InputBit());
        // The bounds scale with the travel, and leave space for a click while the thumb moves.
        var thumb = scope.Declare("thumb", thumbScope => new ThumbCells(
            thumbScope.Declare("displacement", Cell.State(new NumericRange(-2 * ToggleLayout.Travel, 2 * ToggleLayout.Travel))),
            thumbScope.Declare("velocity", Cell.State(new NumericRange(-64 * ToggleLayout.Travel, 64 * ToggleLayout.Travel))),
            thumbScope.Declare("destination", Cell.State(new NumericRange(0, ToggleLayout.Travel)))));
        return new ToggleCells(viewportWidth, viewportHeight, down, up, cancel, on, armed, springMatrix, settleCheck, snap, thumb);
    }

    /// <summary>Handwired buildToggle: declare the cells, build the behavior, pair the state cells, and finish.</summary>
    public static BuiltToggle Build() => Build(Declare);

    /// <summary>
    /// Handwired buildToggle with another declaration of the 20 cells. The behavior is the same. The
    /// order of the cells changes the wire numbers, so the phase 4 tests use this overload to build the
    /// toggle with the cells in the order of a translated plain method.
    /// </summary>
    public static BuiltToggle Build(Func<ICellScope, ToggleCells> declare)
    {
        ArgumentNullException.ThrowIfNull(declare);
        var table = new SourceTable();
        var cells = declare(table);
        var circuit = table.CreateCircuit();

        // Layout. C#: trackLeft = viewportWidth / 2 - trackWidth / 2
        var trackLeft = circuit.Affine([(cells.ViewportWidth, 0.5)], -ToggleLayout.TrackWidth / 2);
        var trackTop = circuit.Affine([(cells.ViewportHeight, 0.5)], -ToggleLayout.TrackHeight / 2);

        // C#: bits.All(bit => bit == 1), as relu(sum of bits - (count - 1)).
        Wire All(params Wire[] bits) => circuit.Reglu([], 1, [.. bits.Select(bit => new Term(bit, 1))], 1 - bits.Length);
        // C#: !bit
        Wire Not(Wire bit) => circuit.Affine([(bit, -1)], 1);
        // C#: x >= trackLeft && x < trackLeft + trackWidth && y >= trackTop && y < trackTop + trackHeight
        Wire InsideTrack(PointerCells point) => All(
            circuit.Compare(point.X, Relation.GreaterOrEqual, trackLeft),
            circuit.Compare(point.X, Relation.Less, circuit.Affine([(trackLeft, 1)], ToggleLayout.TrackWidth)),
            circuit.Compare(point.Y, Relation.GreaterOrEqual, trackTop),
            circuit.Compare(point.Y, Relation.Less, circuit.Affine([(trackTop, 1)], ToggleLayout.TrackHeight)));

        // Inputs in a fixed order: down, then up, then cancel. C#: if (down) armed = InsideTrack(down)
        var armedAfterDown = circuit.Select(cells.Down.Active, cells.Armed, InsideTrack(cells.Down));
        // C#: if (up && armed && InsideTrack(up)) on = !on
        var on = circuit.Select(All(cells.Up.Active, armedAfterDown, InsideTrack(cells.Up)), cells.On, Not(cells.On));
        // C#: if (up || cancel) armed = false
        var armed = All(armedAfterDown, Not(cells.Up.Active), Not(cells.Cancel));

        // Animation. C#: thumb.Destination = on ? travel : 0, then step the spring of the thumb toward it.
        var destination = circuit.Affine([(on, ToggleLayout.Travel)]);
        var matrix = cells.SpringMatrix;
        var thumb = circuit.AnimatedObject(
            [
                new AnimatedSpring(new SpringMatrixWires(matrix[0], matrix[1], matrix[2], matrix[3]), cells.Thumb.Displacement, cells.Thumb.Velocity, destination)
                {
                    // The steps simulate time before the events of this evaluation, so they pull toward the previous destination.
                    DestinationDuringSteps = cells.Thumb.Destination,
                },
            ],
            new AnimatedObjectOptions(cells.SettleCheck) { Snap = cells.Snap });

        // Commit the state: each state cell gets exactly one next value.
        table.Next(cells.On, on);
        table.Next(cells.Armed, armed);
        table.Next(cells.Thumb, new ThumbCells(thumb.Displacements[0], thumb.Velocities[0], destination));
        var output = new ToggleOutputWires(trackLeft, trackTop, thumb.Positions[0], on, armed, thumb.Active);
        return new BuiltToggle(cells, output, table.Finish());
    }
}
