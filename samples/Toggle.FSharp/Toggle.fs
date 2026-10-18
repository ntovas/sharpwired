// Handwired source: template/toggle.ts, lines 9-84 (layout, matrixBound, coordinate, toggleCells and
// buildToggle with its local function insideTrack).
//
// The toggle switch as a plain F# function. ToggleStep.step runs as a normal .NET method. The IL
// translator (Sharpwired.IL) translates it into the program of the builder toggle
// (Toggle.Builder.ToggleProgram), operation for operation. It does the same for the plain C# toggle
// (Toggle.CSharp.ToggleStep.Step). The records declare the 20 cells: their fields give the cell
// names, and the attributes give the ranges, grids and initial values of toggleCells. step has one
// binding for each step of ToggleProgram.Build, in the same order. Hw.AnimatedObject replaces the
// animation macro, which has no plain F# form.

namespace Toggle.FSharp

open System.Runtime.CompilerServices
open Sharpwired.Annotations

/// The literals of the toggle. An attribute argument must be a literal.
module ToggleLiterals =
    /// SpringMatrix.SpringMatrixBound(ToggleLayout.Spring), as a literal. A test checks that the two
    /// values are equal.
    [<Literal>]
    let MatrixBound = 10.360516548156738

    /// The grid of the pointer coordinates: 1/64 px.
    [<Literal>]
    let PointerGrid = 0.015625

    [<Literal>]
    let TrackWidth = 52.0f

    [<Literal>]
    let TrackHeight = 32.0f

    [<Literal>]
    let Travel = 20.0f

open ToggleLiterals

/// The spring of the thumb in px from the off side: its displacement from the destination, its
/// velocity, and the destination. The bounds scale with the travel, and leave space for a click
/// while the thumb moves.
[<Struct>]
type ThumbState =
    { [<Range(-40.0, 40.0)>] Displacement: float32
      [<Range(-1280.0, 1280.0)>] Velocity: float32
      [<Range(0.0, 20.0)>] Destination: float32 }

/// The state cells: the switch position, the armed bit, and the spring of the thumb.
type ToggleState = { On: bool; Armed: bool; Thumb: ThumbState }

/// One pointer event: the event bit and the coordinates. The host puts coordinates on a 1/64 px grid.
[<Struct>]
type Pointer =
    { [<Event>] Active: bool
      [<Range(-16384.0, 16384.0); Grid(PointerGrid)>] X: float32
      [<Range(-16384.0, 16384.0); Grid(PointerGrid)>] Y: float32 }

/// The input and event cells. The host writes the 2 × 2 matrix of the spring steps of an evaluation
/// into SpringMatrix, each entry within ±MatrixBound. SettleCheck tells whether the spring can come to
/// rest, and Snap tells whether the thumb snaps onto its destination.
type ToggleInput =
    { [<Range(0.0, 16384.0); Grid(1.0); Initial(1280.0)>] ViewportWidth: float32
      [<Range(0.0, 16384.0); Grid(1.0); Initial(800.0)>] ViewportHeight: float32
      Down: Pointer
      Up: Pointer
      [<Event>] Cancel: bool
      [<FixedLength(4); Range(-MatrixBound, MatrixBound)>] SpringMatrix: float32[]
      SettleCheck: bool
      Snap: bool }

/// The outputs, in the order of the Handwired output object.
type ToggleOutput =
    { TrackLeft: float32
      TrackTop: float32
      ThumbOffset: float32
      On: bool
      Armed: bool
      Active: bool }

/// Handwired buildToggle as a plain F# function.
module ToggleStep =
    /// Handwired insideTrack. NoInlining keeps the call in Release. Thus the translator inlines it at
    /// the call, and its AND gives one wire, as the builder local function does.
    [<Handwired; MethodImpl(MethodImplOptions.NoInlining)>]
    let insideTrack (point: Pointer) (trackLeft: float32) (trackTop: float32) =
        point.X >= trackLeft && point.X < trackLeft + TrackWidth && point.Y >= trackTop && point.Y < trackTop + TrackHeight

    /// One evaluation of the toggle. As a native switch, it acts on release. A press on the track arms
    /// it, and a release on the track while armed flips it. A release off the track or a cancel
    /// disarms it. The inputs apply in a fixed order: down, then up, then cancel. The thumb springs to
    /// its new side.
    [<Handwired>]
    let step (state: ToggleState) (input: ToggleInput) =
        let trackLeft = input.ViewportWidth * 0.5f - TrackWidth / 2.0f
        let trackTop = input.ViewportHeight * 0.5f - TrackHeight / 2.0f
        let armedAfterDown = if input.Down.Active then insideTrack input.Down trackLeft trackTop else state.Armed
        let on = if input.Up.Active && armedAfterDown && insideTrack input.Up trackLeft trackTop then not state.On else state.On
        let armed = armedAfterDown && not input.Up.Active && not input.Cancel
        let destination = if on then Travel else 0.0f
        // The steps simulate time before the events of this evaluation, so they pull toward the
        // previous destination, state.Thumb.Destination.
        let thumb = Hw.AnimatedObject(input.SpringMatrix, state.Thumb.Displacement, state.Thumb.Velocity, destination,
                                      state.Thumb.Destination, input.SettleCheck, input.Snap)
        struct ({ ToggleState.On = on; Armed = armed; Thumb = { Displacement = thumb.Displacement; Velocity = thumb.Velocity; Destination = destination } },
                { TrackLeft = trackLeft; TrackTop = trackTop; ThumbOffset = thumb.Position; On = on; Armed = armed; Active = thumb.Active })
