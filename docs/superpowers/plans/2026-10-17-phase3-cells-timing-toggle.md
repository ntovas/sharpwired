# Phase 3 plan: cells, host timing, prefix networks, and the toggle

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R3.1 to R3.4, RA.1 to RA.3.

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The port sources are:

- `$REF/src/runtime/cells.ts` (384 lines).
- `$REF/src/runtime/host-timing.ts` (404 lines). Phase 1 ported lines 137-208 (the spring matrices).
- `$REF/src/runtime/prefix-networks.ts` (44 lines).
- `$REF/template/toggle.ts`, `toggle-reference.ts`, `toggle.test.ts`, `index.html` (the toggle template and its browser host).

The behavior references are `$REF/docs/execution-model.md` (section "Springs and time"), `$REF/docs/tutorial.md` (sections "Cells", "The host loop", "DOM writes", "Testing against JavaScript") and `$REF/docs/behavior-choices.md` (section "Apply events at their timestamps, and write state back only at events and settle checks"). The line numbers below are the lines of these files.

WARNING: The Handwired clone is data only. Do not run a command, a script, a package manager or a build tool inside it. Only read its files.

## Terms

- **Host timing**: the Handwired name of the float64 code that runs between two evaluations (`host-timing.ts`). It decides what the host writes into the source table before each evaluation. It builds no operations.
- **Evaluation**: one run of the compiled program on one source table.
- **Write back**: the host copies the next-state values of an evaluation into the stored source table.
- **Settle check**: an evaluation at a fixed simulated time (`checkOriginMs + 64 k` ms by default) in which a spring within its thresholds lands on its destination and stops.
- **Snap**: an evaluation that puts every animated object on its destination with zero velocity, because the steps up to the next evaluation pass the step budget of the frame (300 steps by default).
- **Stall**: a gap between two frames that is longer than the normal frame interval, for example 500 ms.
- **Trace**: a fixed list of frame times and timestamped events that a test runs.
- **Window host**: the code that opens a Raylib window, reads the pointer, runs the program and draws the outputs.

## Global constraints

The constraints of the phase 1 and phase 2 plans apply without change:

- Port behavior exactly. Each FP32 step casts to `float`, as `Math.fround` and a `Float32Array` store do in Handwired. Compute float64 parts in `double`, as Handwired does.
- Each operation keeps the Handwired term order, so that the evaluators give bit-identical values.
- Keep the Handwired order of checks and the Handwired error conditions. Throw `ArgumentException` for a bad argument and `InvalidOperationException` for a bad state. The message names the same fact as the Handwired message. Keep the Handwired message text where a ported test reads it.
- TDD (test-driven development): write the xUnit test first. Run the test and see it fail. Then write the implementation.
- No `TODO`, no stub, no skipped test.
- Each new file starts with a header comment that names its Handwired source file and functions. A ported test file names the Handwired test file.
- Keep each source file below 800 lines. Split a test file that passes 800 lines into partial class files.
- When a Handwired comparison uses `Object.is`, use `Js.Is` or `Js.ObjectIs`. When it uses `===`, use `==` on the value. When it uses `toEqual` on numbers, use `Js.Is` (`toEqual` uses `Object.is` for numbers).
- Docs and code comments use ASD-STE100 (RA.2).
- Commit at the end of each task. Do not push.

Phase 3 adds these constraints:

- Sharpwired.Timing has no project reference. This makes the design rule "float64, no operations" (design Layer 1) a compile-time fact: the project cannot call a `Circuit` method.
- A new test project copies the `PropertyGroup` of `tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj` (`Optimize` true, `TieredCompilation` false) and its package references. The FP32 behavior of the tests is then the same in each test project.
- A new test project that needs `Js` or `TensorChecks` links the file from `tests/Sharpwired.Core.Tests/` with `<Compile Include="..\Sharpwired.Core.Tests\Js.cs" Link="Js.cs" />`. Do not copy the file. Both classes are `internal`, so each linked copy compiles into its own test assembly.
- No code outside `src/Sharpwired.Raylib/RaylibWindow.cs` uses the namespace `Raylib_cs`. Tests never call a Raylib function.

## The float64 rules

Host timing runs in float64 (`double`) and builds no operations. This holds for `HostTiming`, `SpringMatrix`, `JsMath`, the toggle reference, and the host code that turns outputs into draw commands.

A host value becomes FP32 at one place only: the store into the source table, which is a `float[]`. Each store has an explicit `(float)` cast:

| Host value | Where it becomes FP32 | Exact? |
| --- | --- | --- |
| A spring matrix entry, `SpringMatrixPower(step, steps)[i]` | `values[cells.SpringMatrix[i]] = (float)matrix[i]` in `ToggleMachine` and in the trace app of task 3 | No. It rounds to nearest even, as a `Float32Array` store does. `SpringMatrixBound` declares a range that holds every rounded entry. |
| The settle check bit and the snap bit | `values[cell] = evaluation.SettleCheck ? 1f : 0f` | Yes. |
| A viewport width or height | `values[cells.ViewportWidth] = (float)viewportWidth` | Yes, for whole pixels within 0..16,384. |
| A pointer coordinate | `values[cells.Down.X] = (float)x` | Yes. The host first puts it on the 1/64 px grid within ±16,384 px (`JsMath.Round(x * 64) / 64`, then a clamp), and FP32 holds each such value. |
| A destination in the trace app of task 3 | `table[cell] = (float)(destination + offset)` | Yes up to 2^17 px. Near 2^20 px a 1/64 grid destination rounds, as in Handwired. |
| `PlannedEvaluation.DurationMs` | No phase 3 program has a duration cell. | FP32 holds it exactly within 2^24 grid steps (262,144 ms), as `host-timing.ts:251-255` says. |

Program outputs come back as `float` and the host reads them as `double`, which is exact. `SpringMatrixBound` computes in `double` and rounds the bound once with `(double)(float)(bound * (1 + 1e-6))`, as `host-timing.ts:205` does with `Math.fround`.

JavaScript number rules that the port must keep:

- `Math.round(x)` rounds half up (toward +Infinity) and gives -0 for x in [-0.5, -0]. The .NET `Math.Round(double)` rounds half to even. So do not use `Math.Round`. Use the new `JsMath.Round`, which has the body of the test helper `Js.Round` (`tests/Sharpwired.Core.Tests/Js.cs:37`).
- `x + 0` turns -0 into +0 (`host-timing.ts:63`, `:102`). Write this as the private helper `PositiveZero(x) => x == 0 ? 0.0 : x`, so that the intent does not depend on what the JIT does with `x + 0.0`.
- `Math.max` and `Math.min` treat +0 as larger than -0 and give NaN for a NaN operand. The .NET `Math.Max(double, double)` and `Math.Min(double, double)` follow the same rules (IEEE 754:2019 maximum and minimum) since .NET Core 3.0. Use them.
- `Number.isInteger(x)` is false for NaN and for infinities. Write it as the private helper `IsInteger(x) => double.IsFinite(x) && Math.Floor(x) == x`.

## The location of the spring matrices

Decision: move `src/Sharpwired.Core/SpringMatrix.cs` to `src/Sharpwired.Timing/SpringMatrix.cs`, with the namespace `Sharpwired.Timing`. Keep `docs/design.md:20` as it is.

Reasons:

1. The design puts all host timing in Sharpwired.Timing, and `SpringMatrix` is a part of `host-timing.ts` (lines 137-208). The `SpringClockConfig` overload of `SpringMatrixBound` (PHASE3 review item `host-timing.ts:196`) needs `SpringClockConfig`, which is a Timing type. In Core, that overload would pull the clock configuration into Core.
2. The move causes no circular project reference. No Core production code uses `SpringMatrix`: `grep` finds the name only in two XML comments in `Macros.Spring.cs` (lines 17 and 157), which are plain text. Core has no reference to Timing, and Timing has no project reference at all. The only use of `Fp32` in `SpringMatrix.cs` is `Fp32.Round`, which is the cast `(float)value`.
3. The spring macro tests in `tests/Sharpwired.Core.Tests/SpringMatrixTests.cs` need the float64 matrices as host values. `Sharpwired.Core.Tests` gets a project reference to `Sharpwired.Timing`. The references are then `Core.Tests -> Core`, `Core.Tests -> Timing` and `Cells -> Core`. Timing references no project. The graph has no cycle.

The spring macros (`Macros.Spring.cs`) stay in Core, because they build operations.

## Public API

### Sharpwired.Timing

```csharp
namespace Sharpwired.Timing;

// Handwired SpringClockConfig and defaultSpringClockConfig (host-timing.ts:21-40).
public sealed record SpringClockConfig(int TimeGrid, double StepMs, int MaxStepsPerFrame, int SettleCheckSteps)
{
    public static SpringClockConfig Default { get; } = new(64, 4, 300, 16);
}

// Handwired FrameTiming (:66).
public readonly record struct FrameTiming(double FrameTimeMs, double DurationMs, ImmutableArray<double> EventTimesMs);

// Handwired StepCount (:106).
public readonly record struct StepCount(int Steps, double LeftoverMs, bool Snap);

// Handwired SpringClock and stoppedSpringClock (:211-222).
public readonly record struct SpringClock(double? AnimatedUntilMs, double CheckOriginMs, double? LastEvaluationMs)
{
    public static SpringClock Stopped { get; } = new(null, 0, null);
    // The host requests frames while this is true (AnimatedUntilMs is not null).
    public bool IsRunning { get; }
}

// Handwired EvaluationKind (:231): 'settle check', 'events', 'frame', 'snap'.
public enum EvaluationKind { SettleCheck, Events, Frame, Snap }

// Handwired WriteBack (:237): 'every evaluation', 'events and settle checks'.
public enum WriteBack { EveryEvaluation, EventsAndSettleChecks }

// Handwired TimedEvent (:270).
public interface ITimedEvent { double TimeStamp { get; } }

// Handwired PlannedEvaluation<Event> (:243).
public sealed record PlannedEvaluation<TEvent>(
    EvaluationKind Kind, double TimeMs, double SimulatedTimeMs, double DurationMs,
    int Steps, bool SettleCheck, bool Snap, IReadOnlyList<TEvent> Events, bool WritesBack);

public static class HostTiming
{
    // Handwired validateSpringClockConfig (:42).
    public static void ValidateSpringClockConfig(SpringClockConfig config);
    // Handwired quantizeTimeMs (:61). config null: SpringClockConfig.Default.
    public static double QuantizeTimeMs(double timeStamp, SpringClockConfig? config = null);
    // Handwired quantizeFrameTiming (:87).
    public static FrameTiming QuantizeFrameTiming(
        double frameTimeStamp, double? previousFrameTimeMs, IReadOnlyList<double> eventTimeStamps, SpringClockConfig? config = null);
    // Handwired countSpringSteps (:125).
    public static StepCount CountSpringSteps(double pendingMs, int stepBudget, SpringClockConfig? config = null);
    // Handwired advanceFrame (:314).
    public static SpringClock AdvanceFrame<TEvent>(
        SpringClock clock, double frameTimeStamp, IReadOnlyList<TEvent> events,
        Func<PlannedEvaluation<TEvent>, bool> evaluate,
        WriteBack writeBack = WriteBack.EveryEvaluation, SpringClockConfig? config = null)
        where TEvent : ITimedEvent;
}

// JavaScript Math.round for host code: half up, and -0 for -0.5 <= x <= -0.
public static class JsMath
{
    public static double Round(double value);
}

// Moved from Sharpwired.Core without other change (phase 1), plus one overload.
public readonly record struct SpringConstants(double Stiffness, double Damping);
public readonly record struct SpringMatrix(double DisplacementFromDisplacement, double DisplacementFromVelocity,
                                           double VelocityFromDisplacement, double VelocityFromVelocity)
{
    public const double DefaultStepMs = 4;
    public const int DefaultMaxStepsPerFrame = 300;
    public static SpringMatrix Identity { get; }
    public static SpringMatrix SpringStepMatrix(SpringConstants constants, double stepMs = DefaultStepMs);
    public static SpringMatrix MultiplySpringMatrices(SpringMatrix left, SpringMatrix right);
    public static SpringMatrix SpringMatrixPower(SpringMatrix matrix, int count);
    public static double SpringMatrixBound(SpringConstants constants, double stepMs = DefaultStepMs, int maxStepsPerFrame = DefaultMaxStepsPerFrame);
    // New: Handwired springMatrixBound(constants, config) (:196). It reads config.StepMs and config.MaxStepsPerFrame.
    public static double SpringMatrixBound(SpringConstants constants, SpringClockConfig config);
}
```

The new overload has no default for `config`. So the call `SpringMatrixBound(constants)` still resolves to the phase 1 overload, and it is not ambiguous.

### Sharpwired.Cells

```csharp
namespace Sharpwired.Cells;

// Handwired CellPolicy (cells.ts:33).
public enum CellPolicy { Constant, Input, Event, State }

// Handwired Spec, CellSpec, OneHotSpec, PermutationSpec (:35, :72, :91, :108).
public abstract record Spec;
public sealed record CellSpec(CellPolicy Policy, NumericRange Range, double Initial, bool ExactBit, double? Grid) : Spec;
public sealed record OneHotSpec(ImmutableArray<CellSpec> Members, bool AllowAllZero, bool DeclareToCircuit) : Spec;
public sealed record PermutationSpec(ImmutableArray<ImmutableArray<CellSpec>> Cells) : Spec;

// Handwired constant, input, inputBit, event, state, stateBit, list, oneHot, permutation, anyNumber, unitInterval.
public static class Cell
{
    public static NumericRange AnyNumber { get; }        // anyNumber (:24)
    public static NumericRange UnitInterval { get; }     // unitInterval (:25)
    public static CellSpec Constant(double value, NumericRange range, double? grid = null);   // constant (:58)
    public static CellSpec Input(NumericRange range, double initial = 0, double? grid = null); // input (:60)
    public static CellSpec InputBit(double initial = 0);                                      // inputBit (:62)
    public static CellSpec Event(double? grid = null);                                        // event (:63), grid null or 1
    public static CellSpec State(NumericRange range, double initial = 0, double? grid = null); // state (:64)
    public static CellSpec StateBit(double initial = 0, double? grid = null);                 // stateBit (:66), grid null or 1
    public static T[] List<T>(int count, Func<int, T> make);                                  // list (:68)
    public static OneHotSpec OneHot(IReadOnlyList<CellSpec> members, bool allowAllZero = false, bool declareToCircuit = true); // oneHot (:83)
    public static PermutationSpec Permutation(IReadOnlyList<int> order, double? grid = null);  // permutation (:96)
}

// The declare methods of a table, or of a part of a table under a name prefix.
// Handwired table.declare(spec, name) (:163), with the recursion of declareSpec (:131).
public interface ICellScope
{
    Wire Declare(string name, CellSpec cell);                       // a cell: "prefix.name"
    Wire[] Declare(string name, IReadOnlyList<CellSpec> cells);     // a list: "name[0]", "name[1]", ...
    Wire[] Declare(string name, OneHotSpec group);                  // the members, and one oneHot rule "name"
    Wire[][] Declare(string name, PermutationSpec permutation);     // [rank][item], and the rules "name rank r", "name item i"
    T Declare<T>(string name, Func<ICellScope, T> declare);         // a struct or a component: "name.field"
    T[] DeclareList<T>(string name, int count, Func<ICellScope, int, T> declare); // "name[i].field"
}

// Handwired createSourceTable (:125).
public sealed class SourceTable : ICellScope
{
    public void AtMostOne(string name, IReadOnlyList<Wire> members);   // table.atMostOne (:169)
    public int Size { get; }                                           // table.size (:174)
    public string NameOf(Wire wire);                                   // table.nameOf (:177)
    public CellPolicy? PolicyOf(Wire wire);                            // table.policyOf (:180)
    public Circuit CreateCircuit();                                    // table.createCircuit (:181)
    public void Next<T>(T stored, T next) where T : notnull;           // table.next (:204), with pairLeaves (:319)
    public FinishedTable Finish();                                     // table.finish (:224)
}

// The return value of Handwired finish: {circuit, nextSource, initialSource, validate}.
public sealed record FinishedTable(Circuit Circuit, ImmutableArray<Wire> NextSource, ImmutableArray<float> InitialSource, CellValidator Validator);

// Handwired createValidator (:351) and the function that it returns.
public sealed class CellValidator
{
    public int CellCount { get; }
    public void Validate(ReadOnlySpan<float> source);
}

// Handwired CellValueError (:341).
public sealed class CellValueException : ArgumentException
{
    public int Position { get; }
}

// Handwired createEvaluator (:254), collectLeaves (:297), mapLeaves (:310).
public static class ProgramEvaluator
{
    // output: a Wire, a list of outputs, or an object whose public properties are outputs.
    public static ProgramEvaluator<TOutput> Create<TOutput>(FinishedTable program, TOutput output) where TOutput : notnull;
}

public sealed class ProgramEvaluator<TOutput> where TOutput : notnull
{
    public TensorProgram Program { get; }
    public ImmutableArray<string> OutputPaths { get; }            // for example "thumbOffset", "speed.doubled", "[1].count"
    public ProgramEvaluation Evaluate(ReadOnlySpan<float> source);
}

// The result of one call. The next call of the same evaluator overwrites both arrays, as in Handwired.
public sealed class ProgramEvaluation
{
    public float[] NextValues { get; }                            // one value for each cell, in cell order
    public float[] Outputs { get; }                               // one value for each output leaf, in OutputPaths order
    public float Read(Wire output);                               // the value of the first output leaf that holds this wire
}
```

`Next` and `ProgramEvaluator.Create` walk a value of type `T` by reflection, once, at build time:

- A `Wire` is one leaf.
- An `IReadOnlyList<T>` (this includes `Wire[]` and `Wire[][]`) is a list. The path of element `i` is `path[i]`.
- Any other class or struct is an object. Its public instance properties are its fields. The path of a field is `path.field`, where `field` is the property name with its first letter in lower case. So the C# property `ThumbOffset` gives the path `thumbOffset`, and the Handwired messages stay the same.
- `Next` visits the fields in ordinal order of their names, as `pairLeaves` sorts them (`:330`). `Create` visits them in declaration order (`MetadataToken` order), as `Object.entries` gives them in `collectLeaves` (`:304`).
- Any other type, for example `float`, throws `ArgumentException` "stored state holds wires, lists and objects" or "an output holds wires, lists and objects".

### Sharpwired.Core (new file)

```csharp
namespace Sharpwired.Core;

// Handwired sklanskyPrefix and brentKungPrefix (prefix-networks.ts:12, :30).
public static class PrefixNetworks
{
    public static T[] Sklansky<T>(IReadOnlyList<T> items, Func<T, T, T> combine);
    public static T[] BrentKung<T>(IReadOnlyList<T> items, Func<T, T, T> combine);
}
```

Prefix networks go in Core. They have no host dependency, builder code calls them with `Circuit` methods, and the phase 4 IL translator (which references Core) uses them for running totals (Handwired docs/patterns.md, "Running totals").

### Sharpwired.Raylib

```csharp
namespace Sharpwired.Raylib;

// The time source of a host, in milliseconds. Tests use a fake clock.
public interface IFrameClock { double NowMs { get; } }
// Stopwatch.GetElapsedTime from the time of construction. No Raylib call.
public sealed class StopwatchFrameClock : IFrameClock { }

// The input queue, the spring clock and the AdvanceFrame calls of a host.
// Handwired createToggleMachine (toggle.ts:92-139) without the toggle-specific cell writes.
public sealed class HostLoop<TEvent> where TEvent : ITimedEvent
{
    public HostLoop(WriteBack writeBack = WriteBack.EventsAndSettleChecks, SpringClockConfig? config = null);
    public SpringClock Clock { get; }
    public int QueuedCount { get; }
    public void Queue(TEvent queuedEvent);
    // Calls HostTiming.AdvanceFrame with the queued events, then clears the queue.
    public void Step(double frameTimeMs, Func<PlannedEvaluation<TEvent>, bool> evaluate);
}

// What the window read in one frame.
public readonly record struct WindowInput(
    double PointerX, double PointerY, bool PointerPressed, bool PointerReleased, bool FocusLost,
    int ViewportWidth, int ViewportHeight);

public readonly record struct Rgba(byte R, byte G, byte B, byte A);
public abstract record DrawCommand;
public sealed record FillRoundedRectangle(double X, double Y, double Width, double Height, double Radius, Rgba Color) : DrawCommand;

// An app that a window runs. Frame gets the input of one frame and the time of the frame,
// and returns the draw commands. It makes no Raylib call.
public interface IWindowApp
{
    string Title { get; }
    int InitialWidth { get; }
    int InitialHeight { get; }
    Rgba Background { get; }
    IReadOnlyList<DrawCommand> Frame(WindowInput input, double frameTimeMs);
}

// The only class that calls Raylib: it reads the input, calls the app, and draws.
public static class RaylibWindow
{
    public static void Run(IWindowApp app, IFrameClock clock);
}
```

### The toggle sample (`samples/Toggle.Builder`)

```csharp
namespace Toggle.Builder;

// Handwired layout and springConstants (toggle.ts:9-10).
public static class ToggleLayout { TrackWidth = 52, TrackHeight = 32, ThumbInset = 4, Travel = 20; SpringConstants Spring = new(650, 45); }

// The wires of the cells, in the shape of Handwired toggleCells (toggle.ts:18-38).
public sealed record PointerCells(Wire Active, Wire X, Wire Y);
public sealed record ThumbCells(Wire Displacement, Wire Velocity, Wire Destination);
public sealed record ToggleCells(Wire ViewportWidth, Wire ViewportHeight, PointerCells Down, PointerCells Up, Wire Cancel,
    Wire On, Wire Armed, Wire[] SpringMatrix, Wire SettleCheck, Wire Snap, ThumbCells Thumb);
public sealed record ToggleOutputWires(Wire TrackLeft, Wire TrackTop, Wire ThumbOffset, Wire On, Wire Armed, Wire Active);

// Handwired buildToggle (toggle.ts:40).
public static class ToggleProgram
{
    public static ToggleCells Declare(ICellScope scope);   // toggleCells
    public static BuiltToggle Build();
}
public sealed record BuiltToggle(ToggleCells Cells, ToggleOutputWires Output, FinishedTable Table);

// Handwired ToggleEvent and ToggleOutput (toggle.ts:86-87).
public enum ToggleEventKind { Down, Up, Cancel }
public readonly record struct ToggleEvent(ToggleEventKind Kind, double X, double Y, double TimeStamp) : ITimedEvent;
public readonly record struct ToggleOutput(double TrackLeft, double TrackTop, double ThumbOffset, double On, double Armed, double Active);

// Handwired createToggleMachine (toggle.ts:92).
public sealed class ToggleMachine
{
    public ToggleMachine(WriteBack writeBack = WriteBack.EventsAndSettleChecks);
    public BuiltToggle Built { get; }
    public float[] Source { get; }
    public SpringClock Clock { get; }
    public void Queue(ToggleEvent toggleEvent);
    public ToggleOutput Step(double viewportWidth, double viewportHeight, double frameTime);
}

// Handwired mountToggle and the CSS of index.html (toggle.ts:142-170, index.html:9-14), for a Raylib window.
public sealed class ToggleWindowApp : IWindowApp
{
    public ToggleWindowApp(ToggleMachine machine);
    public static double QuantizeCoordinate(double coordinate);          // toggle.ts:161
    public static IReadOnlyList<DrawCommand> Draw(ToggleOutput output);  // toggle.ts:153-157 and the CSS
}
```

## Map of names

| Sharpwired | Handwired |
| --- | --- |
| `SourceTable` | `createSourceTable` (cells.ts:125), `SourceTable` (:123) |
| `ICellScope.Declare` overloads, `DeclareList` | `table.declare` (:163), `declareSpec` (:131) |
| `Cell.Constant`, `Input`, `InputBit`, `Event`, `State`, `StateBit` | `constant` (:58), `input` (:60), `inputBit` (:62), `event` (:63), `state` (:64), `stateBit` (:66), `cell` (:53) |
| `Cell.List` | `list` (:68) |
| `Cell.OneHot` | `oneHot` (:83) |
| `Cell.Permutation` | `permutation` (:96) |
| `SourceTable.AtMostOne` | `table.atMostOne` (:169) |
| `SourceTable.Size`, `NameOf`, `PolicyOf` | `table.size`, `table.nameOf`, `table.policyOf` (:174-180) |
| `SourceTable.CreateCircuit` | `table.createCircuit` (:181) |
| `SourceTable.Next` | `table.next` (:204), `pairLeaves` (:319) |
| `SourceTable.Finish`, `FinishedTable` | `table.finish` (:224) |
| `CellValidator`, `CellValueException` | `createValidator` (:351), `CellValueError` (:341) |
| `ProgramEvaluator.Create`, `ProgramEvaluator<T>.Evaluate`, `ProgramEvaluation` | `createEvaluator` (:254), `withFirstNaN` (:269), `collectLeaves` (:297), `mapLeaves` (:310) |
| `SpringClockConfig`, `SpringClockConfig.Default` | `SpringClockConfig`, `defaultSpringClockConfig` (host-timing.ts:21-40) |
| `HostTiming.ValidateSpringClockConfig` | `validateSpringClockConfig` (:42) |
| `HostTiming.QuantizeTimeMs`, `JsMath.Round` | `quantizeTimeMs` (:61), JavaScript `Math.round` |
| `HostTiming.QuantizeFrameTiming`, `FrameTiming` | `quantizeFrameTiming` (:87), `FrameTiming` (:66) |
| `HostTiming.CountSpringSteps`, `StepCount` | `countSpringSteps` (:125), `StepCount` (:106) |
| `SpringMatrix.*` (moved), `SpringMatrixBound(constants, config)` | `springStepMatrix`, `multiplySpringMatrices`, `springMatrixPower`, `springMatrixBound` (:154-208) |
| `SpringClock`, `SpringClock.Stopped` | `SpringClock`, `stoppedSpringClock` (:211-222) |
| `EvaluationKind`, `WriteBack` (the two write-back policies) | `EvaluationKind` (:231), `WriteBack` (:237), `writeBackPolicies` (:241) |
| `PlannedEvaluation<TEvent>`, `ITimedEvent` | `PlannedEvaluation` (:243), `TimedEvent` (:270) |
| `HostTiming.AdvanceFrame` (the frame plan, settle checks, snaps) | `advanceFrame` (:314) with `run` (:330), `stepTo` (:364) |
| `PrefixNetworks.Sklansky`, `PrefixNetworks.BrentKung` | `sklanskyPrefix` (prefix-networks.ts:12), `brentKungPrefix` (:30) |
| `ToggleProgram.Build`, `ToggleProgram.Declare` | `buildToggle`, `toggleCells` (toggle.ts:18-84) |
| `ToggleMachine`, `HostLoop<TEvent>` | `createToggleMachine` (toggle.ts:92-139) |
| `ToggleWindowApp`, `RaylibWindow` | `mountToggle` (toggle.ts:142-170), `index.html` |
| `ToggleReference` (test project) | `createToggleReference` (toggle-reference.ts:10) |

## Files

New projects, each added to `Sharpwired.slnx`:

| Project | Kind | References |
| --- | --- | --- |
| `src/Sharpwired.Timing/Sharpwired.Timing.csproj` | library | none |
| `src/Sharpwired.Cells/Sharpwired.Cells.csproj` | library | `Sharpwired.Core` |
| `src/Sharpwired.Raylib/Sharpwired.Raylib.csproj` | library | `Sharpwired.Timing`, NuGet `Raylib-cs` 8.1.0 |
| `samples/Toggle.Builder/Toggle.Builder.csproj` | library | `Sharpwired.Core`, `Sharpwired.Cells`, `Sharpwired.Timing`, `Sharpwired.Raylib` |
| `samples/Toggle.CSharp/Toggle.CSharp.csproj` | executable (`OutputType` `Exe`) | `Toggle.Builder`, `Sharpwired.Raylib`, `Sharpwired.Timing` |
| `tests/Sharpwired.Timing.Tests/Sharpwired.Timing.Tests.csproj` | xUnit | `Sharpwired.Timing`, `Sharpwired.Core` (the trace app builds a circuit); links `Js.cs` |
| `tests/Sharpwired.Cells.Tests/Sharpwired.Cells.Tests.csproj` | xUnit | `Sharpwired.Cells`, `Sharpwired.Core`; links `Js.cs`, `TensorChecks.cs` |
| `tests/Sharpwired.Raylib.Tests/Sharpwired.Raylib.Tests.csproj` | xUnit | `Sharpwired.Raylib`, `Sharpwired.Timing` |
| `tests/Toggle.Builder.Tests/Toggle.Builder.Tests.csproj` | xUnit | `Toggle.Builder` and its references |

Changed project: `tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj` gets a project reference to `Sharpwired.Timing`.

`Sharpwired.slnx` gets a `/samples/` folder with the two sample projects, and the four test projects under `/tests/`.

Raylib-cs version: 8.1.0, published on nuget.org on 2026-09-05. `dotnet package search Raylib-cs --exact-match` lists it as the latest stable version. Its package has `lib/net10.0/Raylib-cs.dll` and native libraries for `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64` and `win-x86`. It has no package dependency.

Toggle project decision: the builder toggle goes in the class library `samples/Toggle.Builder`, and `samples/Toggle.CSharp` is the executable that opens the window. Reasons:

1. The person runs `dotnet run --project samples/Toggle.CSharp`, as the design names it.
2. Phase 4 adds the plain C# method to `samples/Toggle.CSharp` and the F# function to `samples/Toggle.FSharp`. The phase 4 tests compare each translated program with the builder program operation for operation (design "Order of work", step 4). They need the builder program as a library reference. A test project that references an executable project gets its `Main` and its Raylib start code too.
3. `Toggle.FSharp` can open the same window with the same `ToggleWindowApp` from the library.

`samples/Toggle.CSharp` holds only `Program.cs` (about 10 lines, no logic), so it has no test project in phase 3 (open question 3).

New source files:

```txt
src/Sharpwired.Timing/SpringMatrix.cs           moved from Core; namespace Sharpwired.Timing; the SpringClockConfig overload of SpringMatrixBound
src/Sharpwired.Timing/SpringClockConfig.cs      SpringClockConfig
src/Sharpwired.Timing/JsMath.cs                 JsMath.Round
src/Sharpwired.Timing/HostTiming.cs             ValidateSpringClockConfig, QuantizeTimeMs, QuantizeFrameTiming, CountSpringSteps, FrameTiming, StepCount
src/Sharpwired.Timing/HostTiming.AdvanceFrame.cs AdvanceFrame, SpringClock, EvaluationKind, WriteBack, ITimedEvent, PlannedEvaluation
src/Sharpwired.Cells/CellSpec.cs                CellPolicy, Spec, CellSpec, OneHotSpec, PermutationSpec, Cell
src/Sharpwired.Cells/SourceTable.cs             SourceTable, ICellScope, the scope class, FinishedTable, the rules
src/Sharpwired.Cells/WireStruct.cs              the reflection walker of Next and Create (pairLeaves, collectLeaves)
src/Sharpwired.Cells/CellValidator.cs           CellValidator, CellValueException
src/Sharpwired.Cells/ProgramEvaluator.cs        ProgramEvaluator, ProgramEvaluator<T>, ProgramEvaluation
src/Sharpwired.Core/PrefixNetworks.cs           PrefixNetworks.Sklansky, PrefixNetworks.BrentKung
src/Sharpwired.Raylib/IFrameClock.cs            IFrameClock, StopwatchFrameClock
src/Sharpwired.Raylib/HostLoop.cs               HostLoop<TEvent>
src/Sharpwired.Raylib/WindowApp.cs              WindowInput, Rgba, DrawCommand, FillRoundedRectangle, IWindowApp
src/Sharpwired.Raylib/RaylibWindow.cs           RaylibWindow.Run (the only file that uses Raylib_cs)
samples/Toggle.Builder/ToggleProgram.cs         ToggleLayout, the cell records, ToggleOutputWires, ToggleProgram, BuiltToggle
samples/Toggle.Builder/ToggleMachine.cs         ToggleEventKind, ToggleEvent, ToggleOutput, ToggleMachine
samples/Toggle.Builder/ToggleWindowApp.cs       ToggleWindowApp
samples/Toggle.CSharp/Program.cs                RaylibWindow.Run(new ToggleWindowApp(new ToggleMachine()), new StopwatchFrameClock())
```

New test files:

```txt
tests/Sharpwired.Timing.Tests/HostTimingTests.cs        the "host timing" describe block of spring-matrix.test.ts
tests/Sharpwired.Timing.Tests/AdvanceFrameTests.cs      the "advanceFrame" describe block, runPlanner, movesUntilSnap, frameTimesAt
tests/Sharpwired.Timing.Tests/SpringTraceApp.cs         createApp, runApp, the float64 reference, runUiMdOrder, appPresses, traceSchedules
tests/Sharpwired.Timing.Tests/SpringTraceTests.cs       the "springs along frame-time traces" describe block
tests/Sharpwired.Cells.Tests/SourceTableTests.cs        cells.test.ts: declaration, createCircuit, next, finish, validator, components
tests/Sharpwired.Cells.Tests/ProgramEvaluatorTests.cs   cells.test.ts: createEvaluator and the NaN messages
tests/Sharpwired.Cells.Tests/ExactBitTests.cs           cells.test.ts:67, :84, :106
tests/Sharpwired.Core.Tests/PrefixNetworkTests.cs       prefix-networks.test.ts
tests/Sharpwired.Raylib.Tests/HostLoopTests.cs          HostLoop with a fake clock and scripted events
tests/Sharpwired.Raylib.Tests/FakeFrameClock.cs         a clock that a test sets
tests/Toggle.Builder.Tests/ToggleReference.cs           toggle-reference.ts in C#
tests/Toggle.Builder.Tests/ToggleTests.cs               toggle.test.ts
tests/Toggle.Builder.Tests/ToggleWindowAppTests.cs      the window app without a window: input to events, the idle rule, draw commands
```

Changed files:

```txt
src/Sharpwired.Core/SpringMatrix.cs                    deleted (moved to Sharpwired.Timing)
src/Sharpwired.Core/Macros.Spring.cs                   the two XML comments name Sharpwired.Timing.SpringMatrix
tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj   reference to Sharpwired.Timing
tests/Sharpwired.Core.Tests/SpringMatrixTests.cs       using Sharpwired.Timing; the float64 matrix test moves to HostTimingTests
tests/Sharpwired.Core.Tests/TensorChecks.cs            AssertSameOperations (for cells.test.ts:84 and :106)
tests/Sharpwired.Core.Tests/PHASE3-TIMING-ASSERTIONS.md   deleted in task 7
Sharpwired.slnx                                        the new projects
docs/design.md                                         the solution layout lists samples/Toggle.Builder
docs/superpowers/specs/2026-10-16-sharpwired-design.md   R3.1 to R3.4 checked in task 7
```

Size estimates: `HostTiming.AdvanceFrame.cs` about 200 lines, `HostTiming.cs` about 150, `SourceTable.cs` about 300, `WireStruct.cs` about 150, `ProgramEvaluator.cs` about 130, `CellValidator.cs` about 90, `ToggleProgram.cs` about 150, `ToggleMachine.cs` about 120, `ToggleWindowApp.cs` about 130, `RaylibWindow.cs` about 80, `SpringTraceApp.cs` about 450, `AdvanceFrameTests.cs` about 400.

## Handwired behavior with no direct C# form

| Handwired behavior | C# decision |
| --- | --- |
| `table.declare(spec)` takes a nested object literal and returns the same shape with wires (the mapped type `CellWires<S>`). C# has no mapped types. | The caller writes a C# record for the wires and fills it with `ICellScope.Declare` calls, one for each field. `Declare<T>(name, scope => ...)` gives a name prefix for a nested struct or a component, and `DeclareList` gives `name[i].` prefixes. Cell names are strings that the caller writes, for example `"down.x"`, so the Handwired messages stay the same. |
| `table.next(stored, next)` pairs leaves by field name in any field order, and refuses "next state has fields position, speed but the stored struct has position, velocity". | `Next<T>(T stored, T next)` takes the same type on both sides, so a different field set does not compile. The walker visits fields in ordinal name order, as `pairLeaves` sorts them. The list length refusal "next state at x must be a list of 2" stays a runtime check. The ported test "next pairs state with wires by field name" checks a record whose constructor takes the fields in another order. |
| `cells.test.ts:271` refuses plain numbers with `@ts-expect-error`, and only the type checker refuses them. | `Wire` has no conversion from `int` (R1.1), so these calls do not compile. `ApiShapeTests.Wire_has_no_conversion_from_int_and_no_public_constructor` already proves it. The runtime part of the test is ported: wires made with `Wire.FromPosition` at hand-counted positions build without an error, and give the same next values `[0, 1, 1, 7, 1, 1, 1, 1]` and the output `typo` 0. |
| The output of `createEvaluator` has the shape of the output object (the type `WireValues<S>`). | `ProgramEvaluation.Read(wire)` returns the value of an output wire, and `Outputs` gives all values in `OutputPaths` order. `ToggleMachine.Step` copies the 6 values into the record `ToggleOutput`. |
| `BitOptions = {grid?: 1}` restricts a bit grid to 1 in the type checker. | `Cell.Event`, `Cell.StateBit` and `Cell.Permutation` take `double? grid` and throw `ArgumentException` "a bit's grid can only be 1" for another value. |
| `invariant` throws a plain `Error` for each refusal. | Argument errors throw `ArgumentException`: an inverted range, one-hot members that are not exact bits, a list that is not a permutation, at-most-one members that are not bits, a stored wire that is not a state cell, a next wire outside the circuit, a list of the wrong length, a bad clock configuration, a bad write-back value, a non-finite timestamp, a bad pending time or step budget, a source table that breaks a declaration (`CellValueException` or `ArgumentException` for a rule). Order errors throw `InvalidOperationException`: "declare every cell before createCircuit", "add rules before createCircuit", "createCircuit may only run once", "call createCircuit before table.next", "table.next after finish has no effect", "call createCircuit before finish", "finish may only run once", "... never received a next value", and the checks of the next values and outputs after an evaluation. |
| `CellValueError` carries the position of the cell, and `createEvaluator` reads it to add the first NaN. | `CellValueException : ArgumentException` has `Position`. When the check of the next values fails, the evaluator throws `InvalidOperationException` with the same message, plus ". " and `DescribeFirstNaN` when the value is NaN (`withFirstNaN`, `:269`). A rule failure has no position and gets no NaN text. |
| A `Float32Array` store rounds a `double` without a visible cast. | Each store into a source table has an explicit `(float)` cast (section "The float64 rules"). `FinishedTable.InitialSource` is `ImmutableArray<float>` from `(float)spec.Initial`. |
| `Number.isInteger(source[wire] / grid)` (`:375`) divides in float64. | `IsInteger((double)source[wire] / grid)`. |
| `let mass = 0; mass += source[member]` (`:379`) adds in float64. | `double mass`, with each term widened from `float`. |
| A message writes a number with JavaScript `String(number)`, for example `0..100` or `-Infinity..Infinity`. | `Fp32.Format` in Sharpwired.Cells. Sharpwired.Timing has no reference to Core, so its few messages use `double.ToString("R", CultureInfo.InvariantCulture)`. The ported Timing tests read only the substrings "power of two", "multiple of the time grid", "positive integer", "2^24 grid steps", "finite", "time grid", "nonnegative", "step budget", "overflows", "nonnegative integer". |
| `stepBudget` and `count` can be any JavaScript number, and a test passes 1.5 to see "the step budget must be a nonnegative integer". | `int` parameters. A fraction does not compile. The ported test passes -1, which reaches the same check and message. |
| `timeGrid`, `maxStepsPerFrame` and `settleCheckSteps` are numbers that `validateSpringClockConfig` checks with `Number.isInteger`. | `int` record fields. The checks that remain at runtime keep the Handwired order and messages. The product `maxStepsPerFrame × stepMs × timeGrid` is computed in `double`, so 2^20 × 4 × 64 does not overflow `int`. |
| `writeBack` is a string, and `advanceFrame` refuses other strings at runtime (`:323`). | `WriteBack` is an enum. `AdvanceFrame` throws `ArgumentException` "writeBack must be 'every evaluation' or 'events and settle checks', got 7" for an undefined value, for example `(WriteBack)7`. |
| `number \| null` for `animatedUntilMs`, `lastEvaluationMs` and `previousFrameTimeMs`. | `double?`. `earliestMs = lastEvaluationMs ?? double.NegativeInfinity`. |
| `(nextCheckMs - untilMs) / config.stepMs` (`:382`) is a JavaScript number that is a whole number by construction. | `Steps` is `int`. The planner computes the quotient in `double` and converts it with a check that it is a whole number. The check throws `InvalidOperationException` "a settle check must be a whole number of steps" if it fails. No trace reaches it. |
| `timedEvents.sort(...)` is a stable sort by time, then by arrival index. | `OrderBy(time).ThenBy(index)`, which is stable. |
| Tests compare logged evaluations and clocks with `toEqual`. | The tests project each `PlannedEvaluation` to a `LoggedEvaluation` record that holds the event ids as a comma-joined string, so record equality compares values. `SpringClock` is a record struct with `double?` fields, so `Assert.Equal` compares values. |
| `requestAnimationFrame` calls `render` only while the clock runs or after an event or a resize (`toggle.ts:147-168`). | Raylib draws every frame. `ToggleWindowApp.Frame` calls `ToggleMachine.Step` only when the clock runs, an event is queued, or the viewport size changed. In other frames it returns the draw commands of the last step. A test proves that this gives the same draw commands as a step in every frame. |
| A browser event has its own `timeStamp`, on the same time origin as the frame time. | Raylib reads input once per frame (`PollInputEvents` in `EndDrawing`) and gives no timestamp. `RaylibWindow` reads the clock once per frame, and `ToggleWindowApp` gives each event of that frame the frame time as its timestamp. So an event applies at the frame time, which is up to one frame after the physical press. Open question 1. |
| `pointercancel` (`toggle.ts:162`). | Raylib has no cancel event. `RaylibWindow` sets `FocusLost` in the frame in which `IsWindowFocused()` changes from true to false, and `ToggleWindowApp` queues a cancel event for it. Open question 2. |
| `event.clientX` and `event.clientY` in CSS pixels, and the DOM writes of `translate3d`, `--fill`, `--armed`, `aria-checked`. | `Raylib.GetMousePosition()` in screen coordinates. `ToggleWindowApp.Draw` returns two `FillRoundedRectangle` commands: the track at (trackLeft, trackTop), 52 × 32, radius 16, with the color of the CSS `color-mix` of #34c759 and #d1d1d6 by `fill = min(1, max(0, thumbOffset / travel))`, times the brightness `1 - armed × 0.1`; and the thumb at (trackLeft + 4 + thumbOffset, trackTop + 4), 24 × 24, radius 12, white. The background is #f5f5f7. `aria-checked` has no Raylib form. |
| The page is `index.html` and `main.ts`, started with `bun run start:template`. | `samples/Toggle.CSharp/Program.cs`, started with `dotnet run --project samples/Toggle.CSharp`. |
| `fieldIndices` (cells.ts:103) gives the index of each field of a flat struct. | Not ported. It serves TypeScript code that reads struct cells by index. A C# record has typed fields, and no phase 3 test or program calls it. |

## Tasks

The tasks run in order. Each task ends with `dotnet build -warnaserror` and `dotnet test` green, and a commit.

### Task 1: the Timing project, the spring matrix move, and the frame quantization

- Files: `src/Sharpwired.Timing/` (`Sharpwired.Timing.csproj`, `SpringMatrix.cs`, `SpringClockConfig.cs`, `JsMath.cs`, `HostTiming.cs`), `src/Sharpwired.Core/SpringMatrix.cs` (deleted), `src/Sharpwired.Core/Macros.Spring.cs` (comments), `tests/Sharpwired.Timing.Tests/` (`Sharpwired.Timing.Tests.csproj`, `HostTimingTests.cs`), `tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj`, `tests/Sharpwired.Core.Tests/SpringMatrixTests.cs`, `Sharpwired.slnx`.
- Move `SpringMatrix.cs` with `git mv`, so that the history follows it. Change the namespace to `Sharpwired.Timing`. Replace `Fp32.Round(x)` with `(double)(float)x`. Update the header comment: the file now names `springMatrixBound(constants, config)` too, and it no longer says that phase 3 ports the frame plan.
- Port: `SpringClockConfig`, `defaultSpringClockConfig`, `validateSpringClockConfig`, `quantizeTimeMs`, `FrameTiming`, `quantizeFrameTiming`, `StepCount`, `countSpringSteps`, `springMatrixBound(constants, config)`, and JavaScript `Math.round` as `JsMath.Round`.
- Rules to keep:
  - `validateSpringClockConfig` checks in this order: the time grid ("the time grid must be a whole power of two, got 60"), the step ("a spring step must be a positive multiple of the time grid"), `maxStepsPerFrame` ("maxStepsPerFrame must be a positive integer"), `settleCheckSteps` ("settleCheckSteps must be a positive integer"), the span ("maxStepsPerFrame × stepMs must stay within 2^24 grid steps"). The power-of-two test is `IsInteger(Math.Log2(timeGrid))`, as Handwired writes it.
  - `quantizeTimeMs` refuses a non-finite timestamp ("a timestamp must be finite, got NaN") before it rounds, and returns `PositiveZero(JsMath.Round(timeStamp * timeGrid) / timeGrid)`.
  - `quantizeFrameTiming` calls `validateSpringClockConfig` first. The event times are `PositiveZero(Math.Min(0, Math.Max(earliestEventMs, QuantizeTimeMs(t) - frameTimeMs)))`.
  - `countSpringSteps` checks the pending time ("pending time must lie on the time grid and be nonnegative, got ...") before the budget ("the step budget must be a nonnegative integer"). `steps = Math.Floor(pendingMs / stepMs)`. A snap returns `(0, 0, true)`.
- Handwired tests to port (spring-matrix.test.ts, describe "host timing"):
  - "frame durations over any split of a time span add up, and idle, late and early times clamp as documented" (:556). It uses `Js.CreateRandom(41)`, 50 traces of 60 frames, and `Js.F` for the FP32 check.
  - "countSpringSteps runs floor(pending / 4) steps and carries the leftover, or snaps when more steps are pending than the budget" (:608). The budget 1.5 case becomes the budget -1 case (see the table above).
  - "spring matrices: powers by squaring equal repeated Euler steps, and the declared bound covers every FP32 entry the host writes" (:639). Move the phase 1 test `SpringMatrixTests.Spring_matrices_powers_by_squaring_equal_repeated_Euler_steps_and_the_declared_bound_covers_every_FP32_entry` into `HostTimingTests` with the same name, and add the `validateSpringClockConfig` part.
  - Sharpwired cases with no Handwired test: `SpringMatrixBound(constants, SpringClockConfig.Default)` equals `SpringMatrixBound(constants)` for the 3 test springs, and a configuration with `MaxStepsPerFrame` 7 gives the bound of 7 steps; `JsMath.Round` gives 1 for 0.5, -0 for -0.5, -1 for -0.5000000000000001, and 0 for 0.49999999999999994; `ValidateSpringClockConfig` refuses `SettleCheckSteps` 0.
- PHASE3 rows closed: plan task 5 rows 1, 2 and 3 (the three "host timing" rows); review items `host-timing.ts:196` and `docs/design.md:20` (decision: section "The location of the spring matrices").

### Task 2: AdvanceFrame

- Files: `src/Sharpwired.Timing/HostTiming.AdvanceFrame.cs`, `tests/Sharpwired.Timing.Tests/AdvanceFrameTests.cs`.
- Port: `SpringClock`, `stoppedSpringClock`, `EvaluationKind`, `WriteBack`, `writeBackPolicies`, `PlannedEvaluation`, `TimedEvent`, `advanceFrame` with its local functions `run`, `readAnimatedUntilMs` and `stepTo`.
- Rules to keep:
  - `AdvanceFrame` validates the configuration, then the write-back value, before it plans an evaluation.
  - `run`: `writesBack = kind != Frame || writeBack == EveryEvaluation`. `simulatedTimeMs = animatedUntilMs is null ? timeMs : animatedUntilMs + steps × stepMs`. `durationMs = lastEvaluationMs is null ? 0 : timeMs - lastEvaluationMs`. An evaluation that does not write back changes neither the step budget nor the clock. Otherwise the budget drops by `steps`; a stopped clock that finds something animating sets `checkOriginMs = timeMs`; then `animatedUntilMs = animating ? simulatedTimeMs : null` and `lastEvaluationMs = animating ? timeMs : null`.
  - `stepTo`: first the snap test with `CountSpringSteps(targetMs - animatedUntilMs, stepBudget)`; a snap sets `animatedUntilMs = null` before `run(Snap, targetMs, 0, true, [])`. Then the settle checks loop, which reads the clock again after each `run`, because `run` can stop the clock. `nextCheckMs = checkOriginMs + (Math.Floor((untilMs - checkOriginMs) / checkIntervalMs) + 1) × checkIntervalMs`. The loop stops when `nextCheckMs > reachableMs`. The settle check runs at `Math.Max(nextCheckMs, lastEvaluationMs ?? nextCheckMs)`. Then the target evaluation: with a stopped clock, `run(kind, targetMs, 0, true, events)`; otherwise `run(kind, targetMs, steps, false, events)`.
  - Events: each event time is `Math.Min(frameTimeMs, Math.Max(earliestMs, QuantizeTimeMs(event.TimeStamp)))`. Sort by time, then by arrival index. Each group of equal times is one `Events` evaluation, with the events in delivery order. Then one `Frame` evaluation at `frameTimeMs`.
- Handwired tests to port (spring-matrix.test.ts, describe "advanceFrame", :725-911, with the helpers `runPlanner`, `movesUntilSnap`, `frameTimesAt`, `TestEvent`):
  - "a stopped clock starts at an event, checks settling at fixed simulated times, and after idle the duration restarts at 0".
  - "evaluations that write state back are the same at 10, 30, 60 and 120 fps and through a 500 ms stall, with presses between frames" (`Js.CreateRandom(53)`).
  - "past the cap, a snap evaluation stops the clock and drops the backlog, so no frame runs more than 300 steps".
  - "an event during a long stall applies at its own timestamp after a snap, and later steps share what is left of the budget".
  - "late events apply at the last evaluation, events after the frame at the frame, and events sharing a timestamp share one evaluation".
  - "with 'events and settle checks', a frame evaluates a copy and leaves the clock where the last commit put it".
  - "a snap counts steps from the last evaluation that wrote state back, so with 'events and settle checks' a frame gap from just above 1,140 ms can snap, depending on where the last settle check landed".
  - Sharpwired cases with no Handwired test: `AdvanceFrame` refuses `(WriteBack)7` with the Handwired message; `AdvanceFrame` refuses a bad configuration before it calls `evaluate` (the callback counts 0 calls).
- PHASE3 rows closed: plan task 5 rows 4 to 10 (the seven "advanceFrame" rows).

### Task 3: springs along frame-time traces

- Files: `tests/Sharpwired.Timing.Tests/SpringTraceApp.cs`, `tests/Sharpwired.Timing.Tests/SpringTraceTests.cs`. Production code changes only when a ported test fails.
- Port the helpers of spring-matrix.test.ts :914-1225: `Press`, the `app*` constants, `appPowers`, `createApp` (2 animated objects built with `Circuit.AnimatedObject`, evaluated with `CreateTensorEvaluator(roots)`), `AppRun`, `runApp`, `Float64Spring`, `stepFloat64Springs`, `settleFloat64Objects`, `runFloat64Reference`, `runUiMdOrder`, `appPresses`, `framePositions`, `frameDisplacements`, `maxFrameDifference`, `differingBits` (with `Js.ObjectIs`), `traceCases`, `traceSchedules`.
- In `runApp`, the table writes are FP32 stores: `table[cell] = (float)powers[steps][entry]`, `(float)(press.Destinations[index] + offset)`. `runApp` copies the next values with `outputs.AsSpan(0, size).CopyTo(state)` and copies each frame output, because the reusable evaluator overwrites its result array.
- The float64 reference uses `Js.F(0.01)` for the threshold and the `%` operator on `double`, as Handwired does.
- Handwired tests to port (describe "springs along frame-time traces, with presses between frames"):
  - "with 'events and settle checks', committed tables and displays are bit-identical at 10, 30, 60 and 120 fps and through 500 ms stalls, and settle like float64 springs, including jumps that stop the clock at exact rest".
  - "writing back every evaluation stays within 5e-4 px of float64 springs and of 120 fps, and settles like float64 springs".
  - "a 3 s stall snaps every object onto its destination once, no frame runs more than 300 steps, and the committed tables stay bit-identical across frame rates".
  - "moved to 65,536, 131,072 and 1,048,576 px, both policies settle like float64 springs and stop animating, and stored displacements stay within 5e-4 px of float64 up to 131,072 px".
  - "ui.md's order, applying presses and then catching up, differs across frame rates in float64".
- `run.settles` equals `reference.settles` element by element: equal times with `==`, and equal bits.
- PHASE3 rows closed: plan task 5 rows 11 to 15 (the five "springs along frame-time traces" rows).

### Task 4: cells

- Files: `src/Sharpwired.Cells/` (`Sharpwired.Cells.csproj`, `CellSpec.cs`, `SourceTable.cs`, `WireStruct.cs`, `CellValidator.cs`, `ProgramEvaluator.cs`), `tests/Sharpwired.Cells.Tests/` (`Sharpwired.Cells.Tests.csproj`, `SourceTableTests.cs`, `ProgramEvaluatorTests.cs`, `ExactBitTests.cs`), `tests/Sharpwired.Core.Tests/TensorChecks.cs` (`AssertSameOperations`), `Sharpwired.slnx`.
- Port: `Range`, `anyNumber`, `unitInterval`, `CellPolicy`, `CellSpec`, `cell`, `constant`, `input`, `inputBit`, `event`, `state`, `stateBit`, `list`, `OneHotSpec`, `oneHot`, `PermutationSpec`, `permutation`, `createSourceTable` (`declare`, `atMostOne`, `size`, `nameOf`, `policyOf`, `createCircuit`, `next`, `finish`), `declareSpec`, `pairLeaves`, `collectLeaves`, `mapLeaves`, `CellValueError`, `createValidator`, `createEvaluator`, `withFirstNaN`.
- Rules to keep:
  - Positions follow declaration order. A one-hot group declares its members, then adds its rule. A permutation declares its cells row by row (`name[rank][item]`), then adds the rank rules, then the item rules.
  - `CreateCircuit` declares, for each cell in order, `AssumeRange`, then `AssumeGrid` when the cell has a grid, then `AssumeBit` when it is an exact bit. Then it declares each rule with `declareToCircuit` true, in rule order, with `AssumeOneHot(members, allowAllZero)`.
  - `Next` checks the whole shape and each pair before it records any pair. The pair checks run in this order: a source cell, a state cell, no earlier next value, a wire of the circuit (`< SourceSize + Operations.Count`).
  - `Finish` builds `Constant(0)` only when a table has an event cell, and builds it after the check for state cells with no next value. A constant or an input holds itself, an event holds the zero wire, and a state cell holds its next value. `Finish` validates the initial table before it returns.
  - The validator checks in this order: the table length, then each cell for "finite and within", then each exact bit, then each grid, then each rule in rule order. A rule passes when `atMostOne` has `mass <= 1`, or when `oneHot` has `mass == 1` or (`allowAllZero` and `mass == 0`). The message is "`name` breaks its `oneHot` rule with mass `m`" or "... its `atMostOne` rule ...".
  - `ProgramEvaluator.Create` compiles `CompileTensorProgram(roots)` with `roots = [..NextSource, ..outputLeaves]` and builds `CreateTensorProgramEvaluator(program)`. `Evaluate` validates the source, evaluates, validates the next values, then checks that each output is finite ("output `path` must be finite, got NaN"). The NaN text comes from `DescribeFirstNaN(EvaluateTensor(source, program), [roots[index]])`.
- Handwired tests to port: every test of cells.test.ts, that is:
  - "positions follow declaration order, and a name prefixes the cells of a later call".
  - "createCircuit declares ranges, grids and one-hot groups, and finish derives next values and the initial table".
  - "createCircuit declares exact bits, and an operation that reads one is built as before".
  - "a program built over exact bits has the operations and the compiled layers of the same program built over cells within 0..1" (with `TensorChecks.AssertSameOperations` and `TensorChecks.AssertSameProgram`).
  - "inlineCircuit repeats an exact bit on the wire that the bit lands on, and builds the operations that it builds without the declaration" (cells.test.ts:106, the PHASE3 review item).
  - "a state cell without a next value, or with two, fails while building, and so does a call out of order".
  - "next pairs state with wires by field name, and a different shape pairs nothing" (see the table: the field-name refusal does not compile; the test keeps the pair in another field order and adds the list-length refusal).
  - "the validator names the cell or rule that a table breaks".
  - "createEvaluator returns next values in cell order and outputs in the output shape, and checks both tables".
  - "createEvaluator checks that outputs are finite, and names the operation that produced a NaN".
  - "a failed check gets no NaN message when no checked value is NaN, even when an unused operation is NaN".
  - "a failed check gets a NaN message when the value that failed is NaN, and not for a NaN value that the check did not reach".
  - "the NaN message names the first NaN that the NaN output reads, and a NaN operand of a select gets no select context".
  - "table.next and createEvaluator refuse plain numbers where wires are expected, in the type checker" (the runtime part, see the table).
  - "a component is declared nested in its parent or in a second call, and built with the parent's wires" (with a `CounterCells` record and a `BuildCounter` method).
- Sharpwired cases with no Handwired test: "a cell's range is inverted: 5..1"; "one-hot members must be exact bits, e.g. stateBit()"; "a bit's grid can only be 1"; "add rules before createCircuit"; "createCircuit may only run once"; "table.next after finish has no effect"; "finish may only run once"; "`name`: members must be exact bits" for `AtMostOne`; an output of type `float` is refused with "an output holds wires, lists and objects"; the output paths of a record with the properties `Speed` and `Doubled` are `speed` and `speed.doubled`.
- PHASE3 rows closed: review item `cells.test.ts:106`.

### Task 5: prefix networks

- Files: `src/Sharpwired.Core/PrefixNetworks.cs`, `tests/Sharpwired.Core.Tests/PrefixNetworkTests.cs`.
- Port: `sklanskyPrefix`, `brentKungPrefix`. Both copy the items into a new array and combine in place, in the loop order of Handwired. `BrentKung` uses an `int` span, and the downward loop starts at `span / 2` after the upward loop ends.
- Handwired tests to port (prefix-networks.test.ts):
  - "every prefix is the ordered combination of its items, for 1 to 70 items".
  - "combines and longest chains follow the formulas for powers of two".
  - "a state machine scan gives the loop's states, in S × (k + combines) + k live operations, or k + S × combines from a constant initial state". The random source is the test's own LCG: `seed = (uint)(seed * 1664525 + 1013904223)`, `seed / 4294967296.0`, with `unchecked` `uint` arithmetic. The scan uses `IndexReadAffine` with `OutOfRange.Clamp` on both sides, `IndexRead`, `CompileTensorProgram` and `CreateTensorEvaluator`.
- Sharpwired case with no Handwired test: both networks return an empty array for no items and call `combine` 0 times.
- PHASE3 rows closed: none. R3.3 has no PHASE3 row.

### Task 6: the builder toggle and its trace test

- Files: `samples/Toggle.Builder/` (`Toggle.Builder.csproj`, `ToggleProgram.cs`, `ToggleMachine.cs`), `src/Sharpwired.Raylib/` (`Sharpwired.Raylib.csproj`, `HostLoop.cs`, `IFrameClock.cs`), `tests/Toggle.Builder.Tests/` (`Toggle.Builder.Tests.csproj`, `ToggleReference.cs`, `ToggleTests.cs`), `tests/Sharpwired.Raylib.Tests/` (`Sharpwired.Raylib.Tests.csproj`, `HostLoopTests.cs`, `FakeFrameClock.cs`), `Sharpwired.slnx`.
- Port `toggle.ts:9-139`: `layout`, `springConstants`, `matrixBound = SpringMatrixBound(springConstants)`, `toggleCells` (20 cells in the Handwired order: viewport width and height, `down`, `up`, `cancel`, `on`, `armed`, `springMatrix[0..3]`, `settleCheck`, `snap`, `thumb`), `buildToggle` with its local `all`, `not` and `insideTrack`, and `createToggleMachine`. Keep each builder call and its order: the layout affines, the four `Compare` calls of `insideTrack` with `Relation.GreaterOrEqual` and `Relation.Less`, the two `Select` calls, the `AnimatedObject` call with `DestinationDuringSteps = cells.Thumb.Destination` and the options `(SettleCheck, Snap)`, then `Next` for `on`, `armed` and `thumb`, then `Finish`.
- `HostLoop<TEvent>` holds the queue, the `SpringClock` and the `AdvanceFrame` call of `createToggleMachine`. `ToggleMachine.Step` calls `HostLoop.Step` with the evaluate callback of `toggle.ts:111-133`: pick the stored table or a copy, write the 4 matrix entries with `(float)`, the two bits, the viewport, and the events (the last event of each kind wins in one evaluation), call `ProgramEvaluation Evaluate`, copy `NextValues` into the stored table when the evaluation writes back, keep the output, and return `output.Active > 0`. The matrices of 0 to 300 steps are computed once in the constructor, as `toggle.ts:97` does.
- Port `toggle-reference.ts` into `tests/Toggle.Builder.Tests/ToggleReference.cs` in `double`. It uses `HostTiming.QuantizeTimeMs`, `ToggleLayout` and `(double)(float)0.01` for the thresholds. `% 64 == 0` is the `double` remainder, as in JavaScript.
- Handwired tests to port (toggle.test.ts):
  - "the program and plain JavaScript agree on every frame, and every scripted step takes effect", with `scriptedTrace` (frames 16 ms apart from 1,000 ms, each event 3 ms before its frame, 12 pointer steps, a 500 ms stall while the thumb moves, then 60 idle frames).
  - "the table check rejects a pointer coordinate off the 1/64 px grid" ("down.x must be a multiple of 0.015625").
  - "events that share a timestamp apply together, the last down, then the last up, then any cancel, in both versions" (the 3 cases).
- The comparison rule of the trace test, for each frame, in this order:
  1. When the frame states `moving`, `machine.Clock.IsRunning` equals it before the step.
  2. `on`, `armed`, `trackLeft` and `trackTop` of the program equal the reference exactly (`==` on `double`). The decisions are exact, because `Compare` bits equal the C# comparisons for every declared value.
  3. When the frame states the expected `on` and `armed`, the program gives them.
  4. `machine.Clock.IsRunning` after the step equals `reference.Animating`.
  5. `|thumbOffset - reference.ThumbOffset| < 1e-5` px over the trace. The program steps the spring in FP32 and the reference in float64. Handwired measured at most 1.3e-6 px over the 20 px travel (tutorial.md:156).
  6. After the trace, the count of frames in which the reference animates is 38, and a step at 5,000 ms gives `thumbOffset` exactly 20 and a stopped clock.
- `HostLoopTests` (no Handwired test): `Step` passes the queued events in queue order to `AdvanceFrame` and clears the queue; `Clock` is the clock that `AdvanceFrame` returned; a `HostLoop` with `WriteBack.EveryEvaluation` passes that policy (a frame evaluation has `WritesBack` true); the events queued after a `Step` go to the next `Step`. The tests use a scripted event list and the frame times of a `FakeFrameClock`.
- PHASE3 rows closed: none. This task closes R3.4 except the window.

### Task 7: the Raylib window host, the sample, and the close of the PHASE3 list

- Files: `src/Sharpwired.Raylib/WindowApp.cs`, `src/Sharpwired.Raylib/RaylibWindow.cs`, `samples/Toggle.Builder/ToggleWindowApp.cs`, `samples/Toggle.CSharp/` (`Toggle.CSharp.csproj`, `Program.cs`), `tests/Toggle.Builder.Tests/ToggleWindowAppTests.cs`, `tests/Sharpwired.Core.Tests/PHASE3-TIMING-ASSERTIONS.md` (deleted), `docs/design.md`, `docs/superpowers/specs/2026-10-16-sharpwired-design.md`, `Sharpwired.slnx`.
- Before the code, read the Raylib-cs 8.1.0 API from `lib/net10.0/Raylib-cs.xml` in the NuGet cache. Use only these calls: `SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint)`, `InitWindow`, `SetTargetFPS(60)`, `WindowShouldClose`, `GetMousePosition`, `IsMouseButtonPressed(MouseButton.Left)`, `IsMouseButtonReleased(MouseButton.Left)`, `IsWindowFocused`, `GetScreenWidth`, `GetScreenHeight`, `BeginDrawing`, `ClearBackground`, `DrawRectangleRounded`, `EndDrawing`, `CloseWindow`. If a name differs in 8.1.0, use the 8.1.0 name and record it in the commit message.
- `RaylibWindow.Run(app, clock)`:
  1. Open the window with `app.Title`, `app.InitialWidth` and `app.InitialHeight`.
  2. In each frame, read the pointer position, the two button edges, the focus change and the screen size into one `WindowInput`.
  3. Read `clock.NowMs` one time.
  4. Call `app.Frame(input, nowMs)`.
  5. Clear with `app.Background` and draw each `FillRoundedRectangle`. The Raylib roundness is `2 × radius / min(width, height)`.
  6. Close the window when `WindowShouldClose` is true.
- `ToggleWindowApp.Frame(input, frameTimeMs)`:
  1. Queue a `Down` event for `PointerPressed`, an `Up` event for `PointerReleased`, and a `Cancel` event (x 0, y 0) for `FocusLost`, each with the timestamp `frameTimeMs` and the coordinates `QuantizeCoordinate(x)`, `QuantizeCoordinate(y)`.
  2. If no event is queued, the clock does not run, the viewport did not change, and a last step exists, return the last draw commands.
  3. Otherwise call `machine.Step(width, height, frameTimeMs)` and return `Draw(output)`.
- `QuantizeCoordinate(c) = Math.Min(16384, Math.Max(-16384, JsMath.Round(c × 64) / 64))`, as `toggle.ts:161`.
- `Program.cs`: `RaylibWindow.Run(new ToggleWindowApp(new ToggleMachine()), new StopwatchFrameClock());`.
- `ToggleWindowAppTests` (no Handwired test; the window logic without a window):
  - A scripted list of `WindowInput` values at the times of a `FakeFrameClock` (a click on the track at 1,000 ms, idle frames, a click while the thumb moves, a press and a focus loss, a resize from 1280 × 800 to 1000 × 700, 5 s of idle frames) gives at each frame the draw commands of `Draw(machine.Step(...))` for a second `ToggleMachine` that gets the same events with the same timestamps and steps in every frame. This proves that the idle rule gives the same pixels.
  - `QuantizeCoordinate`: 640.001 gives 640, 613.99 gives 613.984375, 20,000 gives 16,384, -0.001 gives -0.
  - `Draw` of an output with `thumbOffset` 0, 10 and 20 and `armed` 0 and 1 gives the track and thumb rectangles and colors of the table above (for example `thumbOffset` 20 and `armed` 0 give the track color #34c759).
  - The draw commands of a frame stay the same object while the app is idle, so `RaylibWindow` draws them again without a new step.
- Check each row of `PHASE3-TIMING-ASSERTIONS.md` against the map below. Each row must map to a test of task 1, 2, 3 or 4. Then delete the file.
- Add `samples/Toggle.Builder/` to the solution layout of `docs/design.md`. Check R3.1 to R3.4 in the spec checklist.
- Run the full verification below, with the manual window check.

## Map of the PHASE3 list

| PHASE3 section | Rows | Task |
| --- | --- | --- |
| Plan task 5 | rows 1-3: the "host timing" describe block (frame durations, countSpringSteps, the validateSpringClockConfig part of the spring matrix test) | 1 |
| Plan task 5 | rows 4-10: the "advanceFrame" describe block | 2 |
| Plan task 5 | rows 11-15: the "springs along frame-time traces" describe block, with ui.md's order | 3 |
| Phase 1 review items | `cells.test.ts:106` | 4 |
| Phase 1 review items | `host-timing.ts:196` (the `SpringClockConfig` overload) | 1 |
| Phase 1 review items | `docs/design.md:20` (the location of the spring matrices: moved to Sharpwired.Timing) | 1 |

## How to run the sample and check the window

Run from the worktree root:

```sh
dotnet run --project samples/Toggle.CSharp
```

The manual check, after task 7:

1. Run the command above. A window of 1280 × 800 px opens with the title "Toggle switch" and a light gray background (#f5f5f7).
2. Make sure that a gray track of 52 × 32 px is at the center of the window, with a white thumb at its left end.
3. Click on the track. Make sure that the thumb moves to the right with a spring motion, and that the track color changes from gray to green during the motion.
4. Click on the track again. Make sure that the thumb moves back to the left and the track becomes gray.
5. Click two times quickly while the thumb moves. Make sure that the thumb turns back toward the side of the last click.
6. Push the left button on the track and hold it. Make sure that the track becomes approximately 10% darker (the switch is armed).
7. Move the pointer off the track and release the button. Make sure that the switch does not change.
8. Push the button on the track, change to another app with Cmd+Tab, then come back and release the button. Make sure that the switch does not change (the focus loss cancels the press).
9. Click 1 px outside the right edge of the track. Make sure that the switch does not change.
10. Change the size of the window. Make sure that the track stays at the center.
11. Close the window with the close button or the Esc key. Make sure that the process stops without an error.

## Verification

- `dotnet build -warnaserror` passes.
- `dotnet test` passes.
- `dotnet test -c Release` passes.
- `grep -rn "TODO\|Skip =" src tests samples` gives no result.
- `grep -rln "Raylib_cs" src samples tests` gives only `src/Sharpwired.Raylib/RaylibWindow.cs`.
- `src/Sharpwired.Timing/Sharpwired.Timing.csproj` has no `ProjectReference`.
- `tests/Sharpwired.Core.Tests/PHASE3-TIMING-ASSERTIONS.md` does not exist.
- Each new source file is below 800 lines.
- The manual window check of the section above passes.
- Each R3 item maps to at least one test:

| Item | Tests |
| --- | --- |
| R3.1 `SourceTable`, the cell functions, `Next`, `Finish`, the validator | `SourceTableTests` (declaration order, createCircuit, next, finish, the validator, components), `ExactBitTests`, `ProgramEvaluatorTests` |
| R3.2 `AdvanceFrame`, the two write-back policies, settle checks, snaps, spring matrix functions, `QuantizeFrameTiming` | `HostTimingTests` (QuantizeFrameTiming, CountSpringSteps, the spring matrices and the configuration), `AdvanceFrameTests` (all), `SpringTraceTests` (both policies, the snap after a 3 s stall, the far offsets, ui.md's order) |
| R3.3 prefix networks | `PrefixNetworkTests` |
| R3.4 the toggle with the builder API, the Raylib-cs window host, the scripted trace test | `ToggleTests` (the trace against `ToggleReference`, the grid refusal, the same-timestamp orders), `ToggleWindowAppTests`, `HostLoopTests`, the manual window check |
| RA.1 | `NOTICE` keeps the Handwired MIT notice (no change) |
| RA.2 | Each new comment and doc in ASD-STE100 |
| RA.3 | The green `dotnet test` run at the end of each task |

## Open questions

1. Event timestamps in the Raylib host. Raylib-cs gives no timestamp for an input event, and it reads input once per frame. The plan gives each event the frame time, so an event applies up to one frame (about 16.7 ms at 60 fps) after the physical press. The browser host applies a press at its own timestamp. Is this resolution acceptable for phase 3? The alternative is a separate input thread with GLFW callbacks, which Raylib-cs does not expose.
2. Cancel. Raylib has no `pointercancel`. The plan maps a loss of window focus to a cancel event. Do you want this map from focus loss to cancel, or no cancel event in the window host?
3. Test projects for samples. The plan adds `tests/Toggle.Builder.Tests` for `samples/Toggle.Builder`, and no test project for `samples/Toggle.CSharp`, which holds only `Program.cs` in phase 3. Phase 4 adds the plain C# method to `samples/Toggle.CSharp`, and its test project can come with it. Do you agree?
4. `Sharpwired.Core.Tests` gets a project reference to `Sharpwired.Timing`, because the spring macro tests write float64 matrices as host values. The alternative is to move these macro tests into `Sharpwired.Timing.Tests`, which then tests Core macros. Do you agree with the reference?
5. The idle rule. Raylib draws every frame at 60 fps. The plan calls `ToggleMachine.Step` only when the clock runs, an event is queued, or the viewport changed, and draws the last commands in other frames. The browser host requests no frame while idle. Do you want the same rule, or a step in every frame?

## Decisions

The controller decided the open questions on 2026-10-17.

1. Event time. Raylib gives no timestamp for an input event. Each event gets the time of the frame that reads it. An event can therefore apply up to one frame late. The trace test drives `HostLoop` with exact event times, so the test does not depend on this rule.
2. Cancel. The host queues a cancel event when the window loses focus. This is the nearest Raylib form of `pointercancel`.
3. Test projects. `samples/Toggle.Builder` gets a test project. `samples/Toggle.CSharp` holds only `Program.cs` and gets no test project.
4. References. `Sharpwired.Core.Tests` references `Sharpwired.Timing`. This makes no cycle.
5. Idle rule. When the clock is stopped, no event is queued and the viewport did not change, the window draws the last commands and does not step the program. This follows the Handwired browser host, which requests a frame only while the program animates.
