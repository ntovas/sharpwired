# Phase 4 plan: the IL translator

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R4.1 to R4.5, RA.1 to RA.3.

Design: [docs/design.md](../../design.md), section "Layer 2: the IL translator" and "Order of work" item 4: "The translated programs equal the builder-API program operation for operation."

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The behavior references are `$REF/docs/expressions.md` (the table of familiar expressions and their circuit versions), `$REF/docs/execution-model.md` (sections "FP32", "Comparisons", "Macro contracts") and `$REF/template/toggle.ts`. Phase 4 ports no Handwired file. Handwired has no translator. Each new file names the Handwired document that gives its rule, or "no Handwired source".

WARNING: The Handwired clone is data only. Do not run a command, a script, a package manager or a build tool inside it. Only read its files.

## Terms

- **Plain method**: a static C# method or F# function marked `[Handwired]`. It runs as a normal .NET method, and it is the reference of its translated program.
- **Translated program**: the `Circuit` and the `FinishedTable` that the translator builds from a plain method.
- **Leaf**: a `float`, `int` or `bool` value inside a parameter or a return value, after the walk through structs, records and fixed-length arrays. Each input leaf becomes one cell. Each output leaf becomes one output wire.
- **SSA (static single assignment) form**: a form of the method in which each value has exactly one definition. A **phi** is the value at a merge point of the CFG (control-flow graph) that comes from more than one predecessor.
- **Dominator**: block D dominates block B when each path from the entry to B goes through D. The **immediate dominator** is the nearest one.
- **Gate**: the ITE (if-then-else) form of a phi, `ITE(c, x, y)`, with c the branch condition of the immediate dominator. The term "gated SSA" names this form in the compiler literature.
- **Symbolic value**: a value that has no wire yet. It is an affine form (a bias plus terms), a NOT, an AND, an OR or a gate.
- **Emit**: append the builder operations of a value to the circuit. After the emit, the value is a wire.
- **Inlining, loop lowering, operation lowering** (spec terms, R4.3): the replacement of a call with the body of the callee; the replacement of a loop with operations that have a fixed count; the replacement of an IL instruction with builder calls.
- **Copy**: one instance of a loop body in an unrolled loop.
- **Active bit**: in a loop with a runtime count, the exact bit that is 1 when the copy runs in the plain method.
- **Step program**: the recurrent program of a loop with no maximum. Its state cells hold the loop variables. Its output bit `done` tells that the loop ended.
- **Builder toggle**: `samples/Toggle.Builder/ToggleProgram.cs`, 20 cells, 69 operations, 21 layers.

## Global constraints

The constraints of the phase 1 to 3 plans apply without change:

- Each FP32 step casts to `float`, as `Math.fround` does in Handwired.
- Throw `ArgumentException` for a bad argument and `InvalidOperationException` for a bad state. A refused construct in a plain method is a bad argument (the method). The new `TranslationException` derives from `ArgumentException`.
- TDD (test-driven development): write the xUnit test first. Run the test and see it fail. Then write the implementation.
- No `TODO`, no stub, no skipped test.
- Each new file starts with a header comment that names its source (a Handwired document, or "no Handwired source") and its contents.
- Keep each source file below 800 lines. Split a test file that passes 800 lines into partial class files.
- When a comparison of numbers must give `Object.is` semantics, use `Js.Is` or `Js.ObjectIs`. When the sign of zero can differ by contract (section "Behavior with no direct form"), use `==` on the value and write the reason in a comment.
- Docs and code comments use ASD-STE100 (RA.2).
- Commit at the end of each task. Do not push.
- A new test project copies the `PropertyGroup` of `tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj` (`Optimize` true, `TieredCompilation` false) and its package references. It links `Js.cs` and `TensorChecks.cs` from `tests/Sharpwired.Core.Tests/` when it needs them. Do not copy these files.
- No code outside `src/Sharpwired.Raylib/RaylibWindow.cs` uses the namespace `Raylib_cs`.

Phase 4 adds these constraints:

- Compare operation lists with `TensorChecks.AssertSameOperations`. Compare compiled programs with `TensorChecks.AssertSameProgram`. Do not use `Operation` record equality. It compares `ImmutableArray` fields by reference (carried item of `.superpowers/sdd/phase3/progress.md`, task 6).
- Each translation test runs in Debug and in Release. The C# and F# test inputs compile in the configuration of the test run. So `dotnet test` checks the Debug IL, and `dotnet test -c Release` checks the Release IL. A test must not depend on the configuration. A per-construct test gives the same expected circuit in both configurations.
- A translation test that checks values compares the translated program with the plain method over seeded source tables (`SeededSources`, task 3), and it compares each output leaf.
- F# code goes only in `samples/Toggle.FSharp/` and in `tests/Sharpwired.IL.FSharpInputs/`.
- `src/Sharpwired.Annotations/` has no project reference and no package reference. A plain method runs without the translator.

## Decisions on the 8 questions

### 1. Operation-for-operation equality with the builder toggle

Decision: "equal operation for operation" means all of these, for the C# toggle and the F# toggle, in Debug and in Release:

1. `AssertSameOperations(builder.Circuit.Operations, translated.Circuit.Operations)`.
2. `AssertSameProgram` on the two `TensorProgram` values of `ProgramEvaluator.Create`.
3. The same cell names in the same positions, the same `NextSource` and the same `InitialSource`.
4. Over the scripted trace, the translated program and the plain method give the same 6 outputs with `Js.Is` in each frame, and the same clock state.
5. Over the scripted trace, the translated program passes the 6-step comparison rule of phase 3 against `ToggleReference`.

The builder side of items 1 to 3 is the builder toggle with its cells in the order of the translated table. The design input contract puts the state parameter first (question 2), so the translated table starts with the 5 state cells. The builder toggle declares them at positions 9, 10, 17, 18, 19. The cell positions are in each wire number and in the term order of each ordinary operation, so the two lists cannot be equal with different cell orders. `ToggleProgram` gets an overload `Build(Func<ICellScope, ToggleCells> declare)`. The test declares the 20 cells in the translated order and builds the same behavior. The current `Build()` calls the overload with `Declare`, so the phase 3 tests do not change.

The plain toggle has one statement for each step of `ToggleProgram.Build`, in the same order. `Hw.AnimatedObject` replaces the macro, which has no plain form. The C# form:

```csharp
[Handwired]
public static (ToggleState Next, ToggleOutput Output) Step(ToggleState state, ToggleInput input)
{
    float trackLeft = input.ViewportWidth * 0.5f - TrackWidth / 2;
    float trackTop = input.ViewportHeight * 0.5f - TrackHeight / 2;
    bool armedAfterDown = input.Down.Active ? InsideTrack(input.Down, trackLeft, trackTop) : state.Armed;
    bool on = input.Up.Active && armedAfterDown && InsideTrack(input.Up, trackLeft, trackTop) ? !state.On : state.On;
    bool armed = armedAfterDown && !input.Up.Active && !input.Cancel;
    float destination = on ? Travel : 0f;
    var thumb = Hw.AnimatedObject(input.SpringMatrix, state.Thumb.Displacement, state.Thumb.Velocity, destination,
        state.Thumb.Destination, input.SettleCheck, input.Snap);
    return (new ToggleState(on, armed, new ThumbState(thumb.Displacement, thumb.Velocity, destination)),
        new ToggleOutput(trackLeft, trackTop, thumb.Position, on, armed, thumb.Active));
}

[Handwired]
private static bool InsideTrack(Pointer point, float trackLeft, float trackTop) =>
    point.X >= trackLeft && point.X < trackLeft + TrackWidth && point.Y >= trackTop && point.Y < trackTop + TrackHeight;
```

`TrackWidth`, `TrackHeight` and `Travel` are `const float` (52, 32, 20). Roslyn folds `TrackWidth / 2` into `ldc.r4 26`.

Evidence from the probes (Roslyn 10.0.302 and F# 10.1.302, Debug and Release). Each line gives the IL and the builder call that the decisions below give:

| Statement | IL (Release, C#) | Builder call | Builder toggle call |
| --- | --- | --- | --- |
| `trackLeft` | `get_ViewportWidth; ldc.r4 0.5; mul; ldc.r4 26; sub; stloc.0` | one exact affine form `[(vw, 0.5)] - 26`, emitted at its definition (3 uses) | `Affine([(ViewportWidth, 0.5)], -26)` |
| `InsideTrack` | `get_X; ldarg.1; blt.un` (falls through on `x >= left`), `ldc.r4 52; add; bge.un`, `blt.un`, `clt; ret`, `ldc.i4.0; ret` | 4 `Compare` calls in IL order, the affine `left + 52` emitted at the second compare, then `AND(c1..c4)` emitted at the call as `Reglu([], 1, [c1..c4], -3)` | `All(Compare(x, >=, left), Compare(x, <, Affine(left + 52)), ...)` |
| `armedAfterDown` | `get_Active; brtrue T; get_Armed; br M; T: call InsideTrack; M:` | `ITE(down.active, inside, armed)` gives `Select(down.active, armed, inside)` | `Select(Down.Active, Armed, InsideTrack(Down))` |
| `on` | `get_Active; ldloc.2; and; brfalse F; call InsideTrack; brtrue T; F: get_On; br M; T: get_On; ldc.i4.0; ceq; M:` | `ITE(AND(up, aad), ITE(insideUp, NOT on, on), on)` gives `ITE(AND(up, aad, insideUp), NOT on, on)`, then `All`, `Not(on)`, `Select` | `Select(All(Up.Active, armedAfterDown, InsideTrack(Up)), On, Not(On))` |
| `armed` | `ldloc.2; brfalse F; get_Active; brtrue F; get_Cancel; ldc.i4.0; ceq; br M; F: ldc.i4.0; M:` | `ITE(aad, ITE(up, 0, NOT cancel), 0)` gives `AND(aad, NOT up, NOT cancel)`, then `Not(up)`, `Not(cancel)`, `All` | `All(armedAfterDown, Not(Up.Active), Not(Cancel))` |
| `destination` | `ldloc.3; brtrue T; ldc.r4 0; br M; T: ldc.r4 20; M:` | `ITE(on, 20, 0)` gives `Affine([(on, 20)])` | `Affine([(on, Travel)])` |
| `thumb` | `call Hw::AnimatedObject(...)` | `circuit.AnimatedObject` with the same spring and options | the same call |

Roslyn writes `input.Up.Active && armedAfterDown` as the IL `and`, because the right operand is a local with no side effect. This occurs in Debug and in Release. The gate rules below give the same `All` for `and` and for a branch chain. The F# compiler writes the same statement as three `brfalse` instructions. The F# compiler writes `x < r` inside `&&` as `clt; brfalse`, where Roslyn writes `bge.un`. Both give `Compare(x, Less, r)`.

The trace check (item 4) is an equality with `Js.Is`, because the translator emits only operations that equal the C# operation on FP32 values (question 2), and `Hw.AnimatedObject` has a plain body that equals the macro bit for bit (task 1). If a frame shows a difference only in the sign of zero, report it to the controller. Do not change the check to `==` without a decision.

No construct of the toggle needs an open question. Open question 1 asks if the controller accepts the builder overload for the cell order.

### 2. The input contract

Decision:

- `TranslateStep` takes `static (TState Next, TOutput Output) Step(TState state, TInput input)`. The return type is `ValueTuple<TState, TOutput>`. An F# function returns `struct (next, output)`.
- The translator declares cells in a walk: the parameters in order, then the public instance properties of each type in `MetadataToken` order, which is the declaration order. The probe confirms this order for C# positional records and for F# records. A fixed-length array gives one cell for each element, with the names `springMatrix[0]` to `springMatrix[3]`. A cell name is the path of property names with the first letter in lower case, as the builder toggle names them (`down.x`, `thumb.displacement`).
- A leaf is a state cell when its property, a property that holds it, or its parameter has `[State]`. The first parameter of a `TranslateStep` method is a state parameter without the attribute. A leaf with `[Event]` is an event cell. Each other leaf is an input cell.
- `float` gives `Cell.State(range)` or `Cell.Input(range)`, with the range from `[Range]` (`Cell.AnyNumber` when there is no `[Range]`) and the grid from `[Grid]`. `int` gives the same with grid 1, and it needs a `[Range]`. `bool` gives `Cell.StateBit()`, `Cell.InputBit()` or `Cell.Event()`. `[Initial(value)]` sets the initial value. `[OneHot]` on a `bool[]` gives `Cell.OneHot` of its members.
- The translator reads an attribute from the property, from the backing field, or from the primary constructor parameter with the same name (ordinal, ignore case). A C# positional record can then write `[Range(0, 1)] float X` or `[property: Range(0, 1)] float X`.
- `Next` pairs with the state cells by path. The type of `Next` is the state type, so each state cell gets exactly one next value. The translator calls `table.Next` for each state cell in walk order, then `table.Finish()`.
- The output leaves of `Output` are the outputs, in walk order.
- `TranslateFunction` takes a static method with any number of parameters and any return type. Each parameter leaf is an input cell (or an event cell with `[Event]`). There is no state cell.
- The walk refuses: a type with public instance fields (except `ValueTuple` as the top return type and as the result of an `Hw` method), a type with a base type other than `object` or `ValueType`, a `float[]` without `[FixedLength]`, and a leaf type other than `float`, `int` and `bool`. A record with a base record has a property order that depends on the assembly (phase 3 ledger, task 4), so the walk refuses it.

The plain method as the reference:

- The plain method computes in FP32: each `float` IL operation rounds to FP32. RyuJIT computes `float` operations in single precision. It does not contract `a * b + c` into an FMA (fused multiply-add). The R1.13 tests check this in the test process (`Fp32SemanticsTests.A_multiply_and_an_add_round_separately_and_do_not_contract_into_an_fma`).
- FMA risk: a later JIT can contract a multiply and an add, for example on ARM64. Then a plain method and its program differ in the last bit. Three checks find it: the R1.13 tests, the seeded value tests of task 3, and the trace check of task 7. The translator refuses `MathF.FusedMultiplyAdd` and `Math.FusedMultiplyAdd`.
- The translator refuses `double` arithmetic (question 6), so a plain method has no float64 step.
- The rule for each emitted operation: its FP32 value equals the value of the C# operation for each input within the declared ranges, up to the sign of zero where a Handwired macro contract says so. The section "Emission rules" gives the rule for affine forms.

### 3. The IL front end

Decision:

- IL bytes: `MethodBase.GetMethodBody().GetILAsByteArray()`, the local types from `MethodBody.LocalVariables`, and tokens through `Module.ResolveMethod`, `ResolveField`, `ResolveType` with the generic context of the method. The decoder reads the bytes with `System.Reflection.Metadata.BlobReader` and the `ILOpCode` enum. A method with an exception clause (`MethodBody.ExceptionHandlingClauses` not empty) is refused. `System.Reflection.Metadata` is in the .NET 10 shared framework, so the project needs no package.
- Sequence points: `PEReader` on `Assembly.Location`, then `TryOpenAssociatedPortablePdb`, which finds a separate portable PDB next to the assembly or an embedded portable PDB. `MetadataTokens.MethodDebugInformationHandle(method.MetadataToken)` gives the sequence points. The location of an IL offset is the last sequence point at or before the offset that is not hidden. The F# compiler writes many hidden points (`nop // hidden` in the probe), so the "not hidden" rule is necessary. The C# test inputs use a separate PDB (the default). `tests/Sharpwired.IL.FSharpInputs` sets `<DebugType>embedded</DebugType>`, so the tests cover both PDB forms.
- CFG: a block starts at offset 0, at each branch target, and after each branch, `ret` and `switch`. Edges come from branches, fall-through and `switch` targets.
- Stack to SSA: an abstract stack per block. The stack depth and the slot types at a block entry must agree on all predecessors. Locals, arguments and stack slots at a merge get a phi. The algorithm is Braun et al., "Simple and Efficient Construction of Static Single Assignment Form" (2013), on the CFG. It needs no dominance frontiers.
- Types: each SSA value has a type: `Float`, `Int`, `Bit`, `Aggregate(Type)`, `Array(element, length)`, `Address(of a local, an argument or a field path)`. The IL stack has no `bool` type, so `Bit` comes from a `bool` field, local, parameter or return type, from a comparison, from `and`, `or`, `xor` of bits, and from a phi whose inputs are bits or the constants 0 and 1.
- The section "IL opcode table" lists the accepted opcodes. Each other opcode is refused with its name.

How the constructs of question 3 appear in IL (probe output) and how each one is lowered:

| Construct | C# IL | F# IL | Lowering |
| --- | --- | --- | --- |
| `c ? a : b` | `brtrue T; <b>; br M; T: <a>; M:`. The else side comes first in IL. | `brfalse E; <a>; br M; E: <b>; M:`. The then side comes first. | phi at M, gate `ITE(c, a, b)`, question 4 |
| `a && b` (value) | Release and Debug: the IL `and` when `b` is a local or argument; otherwise `brfalse F; <b>; br M; F: ldc.i4.0; M:` | `brfalse F; <b>; br M; F: ldc.i4.0`. Debug: each partial result is a phi, and the next test branches on it. | `AND`, merged into one node |
| `a \|\| b` (value) | the IL `or` for locals and arguments; otherwise `brtrue T` chains | `brtrue T` chains | `OR` |
| `x >= y`, `x <= y` on `float` | value: `clt.un; ldc.i4.0; ceq`, `cgt.un; ldc.i4.0; ceq`. Branch: `blt.un`, `bgt.un` to the false side. | the same | `Compare(x, GreaterOrEqual, y)`, `Compare(x, LessOrEqual, y)` |
| `x != y` on `float` | `ceq; ldc.i4.0; ceq`, or `beq` to the false side | the same | `Not(Compare(x, Equal, y))` |
| `!b` | `ldc.i4.0; ceq` | `not b`: `ldc.i4.0; ceq` | `NOT b` |
| `Math.Min/Max/Clamp/Abs` (`float`) | `call Math::Min(Single,Single)` | F# `min`, `max`, `abs` on `float32`: `call Math::Min(Single,Single)` | `MinimumExact`, `MaximumExact`, `ClampExact`, `Absolute` |
| record property read | `ldarga.s; call get_X()`; a nested struct copies into a temporary: `call get_Down(); stloc.s 8; ldloca.s 8; call get_Active()` | `ldfld X@`; a nested struct: `ldflda Down@; ldfld Active@` | the leaf of the aggregate |
| `ldfld`, `ldflda`, `stfld` | `stfld` on a struct argument: `ldarga.s 0; <value>; stfld Plain::A` | the same | read or replace one field of an aggregate value |
| `newobj` record or struct | `newobj State::.ctor(Boolean,Boolean,Thumb)` | the same; an F# record is a class | a new aggregate value |
| tuple | `newobj ValueTuple'2::.ctor`; `ldfld ValueTuple'4::Item2` | `struct (a, b)`: `newobj ValueTuple'4`, then `stloc; ldloca; ldfld Item3`. `(a, b)`: `newobj Tuple'2` | a new aggregate value; a field read |
| call of a `[Handwired]` method | `call Probe::InsideTrack(Pointer,Single,Single)` | `call Probe::insideTrack(...)`. Release can inline a small function: the probe function `animatedObject` is a call in Debug and inline code in Release. | inlining, question 3 decision below |
| constant loop | `br C; body; C: ldloc i; ldc.i4.4; blt body` (Debug: `clt; stloc; ldloc; brtrue`) | `for i in 0 .. 3`: the same shape | loop form 1 |
| runtime loop | `br C; body; C: ldloc i; ldarg n; blt body` | `for i in 0 .. n - 1`: a guard `n - 1 < i` to the exit, then the body, then `bne.un body` | loop form 2 |

Inlining decision: the translator inlines each call to a static, non-generic method with `[Handwired]`, and to the known methods of the lowering table. The value of the call is emitted at the call. So the helper result becomes one wire, as a builder local function returns one wire. A call to another method is refused. Recursion is refused. The F# optimizer can inline a small F# function in Release and keep the call in Debug. Then the values stay equal, but the operation lists differ. An F# `[<Handwired>]` helper therefore has `[<MethodImpl(MethodImplOptions.NoInlining)>]`. The probe confirms that the F# compiler keeps the call with this attribute in Release.

### 4. Branch to select

Decision:

- A bool is an exact bit wire, 0 or 1 in FP32. A `bool` cell is a bit cell (`StateBit`, `InputBit`, `Event`). `Compare` returns an exact bit. NOT, AND, OR, XOR and `Select` of exact bits return exact bits. The translator does not call `AssumeBit`, because the builder toggle does not call it.
- Each comparison opcode emits its `Compare` at its own position in IL order. A branch on a comparison (`blt.un`, `bge`, and so on) emits the same `Compare` as the value form. The relation map:

| IL | Compare relation | Literal on the edge |
| --- | --- | --- |
| `cgt`, `clt`, `ceq` | `Greater`, `Less`, `Equal` | the bit |
| `cgt.un; ldc.i4.0; ceq`, `clt.un; ldc.i4.0; ceq` | `LessOrEqual`, `GreaterOrEqual` | the bit |
| `bgt`, `bge`, `blt`, `ble`, `beq` | `Greater`, `GreaterOrEqual`, `Less`, `LessOrEqual`, `Equal` | the bit on the jump edge, NOT bit on the next edge |
| `bgt.un`, `bge.un`, `blt.un`, `ble.un`, `bne.un` on `float` | `LessOrEqual`, `Less`, `GreaterOrEqual`, `Greater`, `Equal` | NOT bit on the jump edge, the bit on the next edge |
| `bne.un` on `int` | `Equal` | NOT bit on the jump edge |
| `brtrue`, `brfalse` on a bit value | (no new operation) | the value on the jump edge or its NOT |

The `.un` rows hold because the declared ranges are finite, so no operand is NaN. Then `!(x < y)` is `x >= y`. For an `int` branch, Roslyn writes `bge` for the source `i < n`. The `Compare` then has the relation `GreaterOrEqual` and the other edge reads its NOT. The values are equal, and the operation list names the IL relation.

- Each phi becomes a gate. The gate of the phi at merge block M is `value(D)`, with D the immediate dominator of M. `value(B)` for a block B in the region from D to M is: the phi operand when B jumps to M; `ITE(c, value(T), value(F))` when B branches on literal c to T and F. The translator stores `value(B)` for each block, so a shared block such as the `ldc.i4.0` block of an `&&` chain is computed once.
- The gate rules, applied from the inner gate outward:
  1. `ITE(1, x, y) = x`, `ITE(0, x, y) = y`, `ITE(c, x, x) = x`.
  2. For bits: `ITE(c, 1, 0) = c`, `ITE(c, 0, 1) = NOT c`, `ITE(c, x, 0) = AND(c, x)`, `ITE(c, 0, x) = AND(NOT c, x)`, `ITE(c, 1, x) = OR(c, x)`, `ITE(c, x, 1) = OR(NOT c, x)`.
  3. `ITE(c1, ITE(c2, x, y), y) = ITE(AND(c1, c2), x, y)` and `ITE(c1, x, ITE(c2, x, y)) = ITE(OR(c1, c2), x, y)`.
  4. An AND that reads an AND with no wire becomes one AND with the literals in order. The same holds for OR.
  5. `ITE(c, k1, k0)` with `float` or `int` constants gives the affine form `[(c, k1 - k0)] + k0`, when `k1 - k0` is an FP32 value and `f(k0 + (k1 - k0)) == k1`. Otherwise it gives `Select` of two constant wires.
  6. `ITE(c, x, 0)` with a `float` or `int` wire x gives `Multiply(c, x)`. This is the Handwired form of `condition ? value : 0`.
  7. Each other gate gives `Select(c, y, x)`: the gate is c, the off value is y, the on value is x.
- The emit order of a gate: the condition, then the off value, then the on value, then the gate operations. This is the order of the arguments of `circuit.Select(gate, off, on)` in the builder toggle.
- AND of literals l1..ln emits `Reglu([], 1, [(l1, 1), ..., (ln, 1)], 1 - n)`, as `All` in the builder toggle. A NOT literal emits `Affine([(b, -1)], 1)` first, in literal order. OR emits `Affine([(Reglu([], 1, [(l1, -1), ..., (ln, -1)], 1), -1)], 1)`, which is `1 - relu(1 - sum)` (expressions.md, row "NOT, AND and OR"). XOR emits `Affine([(Reglu([], 1, [(a, 1), (b, -1)], 0), 1), (Reglu([], 1, [(b, 1), (a, -1)], 0), 1)])`.
- The `switch` opcode (F# `match` on constants) on an `int` with a declared range: the edge of case k reads `Compare(v, Equal, k)`. The default edge reads the AND of the NOTs.

### 5. Loops

Decision: the translator finds natural loops with the dominator tree. A back edge is an edge whose target dominates its source. Each loop must be reducible, with one header and one exit edge. A loop with two exits (`break`, `return` inside the body) is refused. The translator unrolls each loop by interpretation of its blocks, one copy at a time. In copy k the header phis read the values after copy k - 1.

- **Form 1, constant trip count**: the loop condition is a constant in each copy, because the loop variables are constants. The translator follows the branch and emits no operation for the condition. The unroll stops when the condition exits the loop. A loop that needs more than 65,536 copies is refused. This rule covers C# `for`, F# `for ... in`, `while` with constants, nested loops, and a loop over `array.Length` of a fixed-length array (`ldlen` gives the constant length).
- **Form 2, runtime trip count with a maximum**: the loop condition is a runtime bit. Copy k runs under the active bit `r_k`. The entry gives `r_0`: the path condition of the loop entry (for the F# guard `n - 1 < i`, this is its NOT). After the condition c of copy k, `r_(k+1) = AND(r_k, c)`. Each loop variable v after copy k is `Select(r_k, v_before, v_after)`. The copy count N comes from one of two sources:
  - Range refinement: on the path of `r_k`, the translator keeps an interval for each `int` wire that a loop condition compares with a constant. A condition `±w + b R constant` shrinks the interval of w. For example, `k < n` on the active path gives `n >= k + 1`, and `k + 1 != n` at the lower end of the interval removes that end. When the interval is empty, `r_k` is the constant 0 and the unroll stops. This gives N = 8 for `for (i = 0; i < n; i++)` with `[Range(0, 8)] int n`, and N = 8 for the F# `for i in 0 .. n - 1` shape, which tests `i + 1 != n` at the bottom.
  - `[MaxIterations(N)]` on the method: when the range refinement cannot empty the interval, the translator makes N copies. The active bit after copy N is a loop overflow bit. `TranslatedFunction.LoopOverflowBits` lists it, and `CreateEvaluator` checks that it is 0 after each evaluation.
- **Form 3, no maximum**: the condition is a runtime bit, the range refinement cannot stop the unroll, and there is no `[MaxIterations]`. `TranslateFunction` and `TranslateStep` refuse the loop. `TranslateRecurrent` gives a step program. Limits of the first version: one such loop in a method, at the top level (its entry path condition is the constant 1), with no loop of form 3 inside it.

The step program and its CPU runner:

- `TranslateRecurrent` splits the method into three programs. The **prefix** computes, from the parameters, the initial loop variables and each value that the loop or the code after it reads (the invariants). The **step** has a state cell for each loop variable and an input cell for each invariant. The **suffix** computes the return value from the final loop variables and the invariants.
- The step computes `done = NOT c(state)`, with c the loop condition, and `next = Select(c, state, body(state))` for each loop variable. A lane that is done keeps its state, which the GPU runner of phase 5 (R5.4) needs.
- A bottom-tested loop (C# `do ... while`) runs its body one time before the first test. The prefix holds this first copy, and the step program is then a top-tested loop.
- The range of a state cell is the least fixed point of: the range of the initial value, joined with the range of `body(state)` with the state range refined by c. The translator tries at most 8 rounds. With no fixed point, an `int` state cell gets `[-2^23, 2^23]` with grid 1, and a `float` state cell gets `[-float.MaxValue, float.MaxValue]`. The `int` range is 2^23 and not 2^24, because with 2^24 the body form `count + 1` passes 2^24, and the exactness rule of `int` forms refuses it (task 6). After the widening, more rounds run: a cell that is not wide and whose range changes also gets the wide range, until a round changes no range. That round is the fixed-point check: the next-state range of each cell that is not wide lies inside its cell range. When the ranges still change after one round for each cell, the loop is refused (review of task 6, round 1). The cell validator then refuses a next value of a wide cell that leaves the range, and the runner does not return a wrong value without an error.
- `RecurrentRunner.Run(arguments, stepLimit)` evaluates the prefix, writes the step source table, evaluates the step until `done` is 1 or until `stepLimit` evaluations, then evaluates the suffix. The result has the outputs, the count of evaluations with `done` 0 (the plain iteration count), and `Completed`. When the limit stops the loop, `Completed` is false and the outputs come from the last state.

Test cases:

| Form | Plain method (C# unless F# is named) | Checks |
| --- | --- | --- |
| 1 | `for (int i = 0; i < 4; i++) s += x * w[i];` with `[FixedLength(4)] float[] w` | `AssertSameOperations` against a hand-built circuit of 4 copies; values over 200 seeded tables |
| 1 | F# `for i in 0 .. 3 do s <- s + x` | the same circuit as the C# loop `s += x`, 4 times |
| 1 | two nested loops of 3 and 2 copies; `i += 2` up to 8; `i < a.Length` | values; operation count |
| 1 | `for (int i = 0; i < 70000; i++)` | refused: "a loop with more than 65536 copies" |
| 2 | `for (int i = 0; i < n; i++) s += x;` with `[Range(0, 8)] int n` | 8 copies; values over seeded tables with n in 0..8 |
| 2 | F# `for i in 0 .. n - 1 do s <- s + x` with `[<Range(0., 8.)>] n` | 8 copies (refinement through `!=`); values |
| 2 | `while (x > 1f) x *= 0.5f;` with `[MaxIterations(12)]` and `[Range(0, 4096)] float x` | values; the overflow bit is 0 over the range |
| 2 | the same method with `[Range(0, 1048576)] float x` | the evaluator reports the overflow bit for x = 1048576 |
| 3 | `while (x > 1f) x *= 0.5f;` with `[Range(0, 1e6)] float x`, through `TranslateRecurrent` | `RecurrentRunner` equals the plain method with `Js.Is`; `Steps` equals the plain iteration count; `stepLimit` 3 gives `Completed` false |
| 3 | `int count = 0; while (n > 0) { n -= 3; count++; } return count;` with `[Range(0, 100)] int n` | values; the state range of n is the fixed point `[-2, 100]`; count gets `[-2^23, 2^23]` |
| 3 | a value computed before the loop and read after it; a `do ... while` loop | values (invariants; the first copy in the prefix) |
| 3 | two loops with no maximum; a loop with no maximum inside an `if` | refused |
| all | a loop with `break` | refused: "a loop with two exits" |

### 6. Operation lowering

Decision: the section "Lowering table" gives the map. Its source is the table "Familiar expressions" of `$REF/docs/expressions.md`. The constructs that the table marks as not exact (dynamic division, square root, sine) are refused. The section "Error message rules" gives the form `<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>`.

### 7. The project layout

Decision:

- `src/Sharpwired.Annotations/`: the attributes, `Hw`, and the `Hw` result types. No reference. User code, `samples/Toggle.FSharp` and the C# test inputs reference only this assembly for the plain methods. A plain method then runs without the translator, and `Hw` has a plain body in FP32 that needs no `Circuit`.
- `src/Sharpwired.IL/`: the translator. It references `Sharpwired.Core`, `Sharpwired.Cells` and `Sharpwired.Annotations`.
- `samples/Toggle.CSharp/` stays the executable of phase 3. It gets the plain C# toggle (`ToggleStep.cs`) and `CSharpToggle.cs`, which translates it and returns a `BuiltToggle`. `Program.cs` opens the window with the translated C# toggle. So the sample matches the design layout: "the toggle switch as a plain C# method, and the program that opens its Raylib window".
- `samples/Toggle.FSharp/`: an F# class library with the plain F# toggle. It references only `Sharpwired.Annotations`.
- Test projects: `tests/Sharpwired.Annotations.Tests`, `tests/Sharpwired.IL.Tests`, `tests/Sharpwired.IL.FSharpInputs` (F# library, not a test project), `tests/Toggle.CSharp.Tests`, `tests/Toggle.FSharp.Tests`.
- Evidence for F#: in the probe directory, `dotnet new classlib -lang F#` restored and built with SDK 10.0.302 and FSharp.Core 10.1.302 from the local NuGet cache. It also built with the repository `Directory.Build.props` (`Nullable` enable, `TreatWarningsAsErrors` true, `LangVersion` latest), with no warning.

### 8. The F# notes

Decision:

- Records, struct records, struct tuples, `System.Tuple`, `if ... then ... else`, `match` on constants, `&&`, `||`, `not`, `min`, `max` and `abs` on `float32` compile to IL that the steps above accept. The probe shows each of them.
- `inline` functions are already expanded in the IL: the probe function `useInline` has `ldc.r4 2; mul; ldc.r4 1; add` and no call.
- Closures are refused. In Debug, a closure is `newobj f@34::.ctor` and `callvirt FSharpFunc'2::Invoke`, and the translator refuses `newobj` of a type that is not a data constructor. In Release, the F# optimizer removed the probe closure `let f = fun y -> y + x in f 1.0f`, and the IL is `ldc.r4 1; ldarg.0; add`. The translator accepts this IL, because it has no closure. The refusal test therefore uses a closure that a `NoInlining` function receives, which stays a closure in both configurations.
- `seq` is refused: the IL has `newobj seqSum@35`, `callvirt IEnumerator::MoveNext` and a `try`/`finally` clause.
- F# library functions that FSharp.Core declares `inline` can expand into code with `box`, `throw` and `newarr` of a runtime length. The probe `Array.map` in Debug shows all three. The translator refuses these instructions.
- The plain F# toggle (task 8):

```fsharp
[<Handwired; MethodImpl(MethodImplOptions.NoInlining)>]
let insideTrack (point: Pointer) (trackLeft: float32) (trackTop: float32) =
    point.X >= trackLeft && point.X < trackLeft + trackWidth && point.Y >= trackTop && point.Y < trackTop + trackHeight

[<Handwired>]
let step (state: ToggleState) (input: ToggleInput) =
    let trackLeft = input.ViewportWidth * 0.5f - trackWidth / 2.0f
    let trackTop = input.ViewportHeight * 0.5f - trackHeight / 2.0f
    let armedAfterDown = if input.Down.Active then insideTrack input.Down trackLeft trackTop else state.Armed
    let on = if input.Up.Active && armedAfterDown && insideTrack input.Up trackLeft trackTop then not state.On else state.On
    let armed = armedAfterDown && not input.Up.Active && not input.Cancel
    let destination = if on then travel else 0.0f
    let thumb = Hw.AnimatedObject(input.SpringMatrix, state.Thumb.Displacement, state.Thumb.Velocity, destination,
                                  state.Thumb.Destination, input.SettleCheck, input.Snap)
    struct ({ On = on; Armed = armed; Thumb = { Displacement = thumb.Displacement; Velocity = thumb.Velocity; Destination = destination } },
            { TrackLeft = trackLeft; TrackTop = trackTop; ThumbOffset = thumb.Position; On = on; Armed = armed; Active = thumb.Active })
```

`trackWidth`, `trackHeight` and `travel` are `[<Literal>]` values, so the F# compiler writes `ldc.r4 26` as Roslyn does. The probe shows the same gates for this function as for the C# toggle (section 1).

## Public API

### Sharpwired.Annotations

```csharp
namespace Sharpwired.Annotations;

[AttributeUsage(AttributeTargets.Method)]
public sealed class HandwiredAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RangeAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
}

// The same targets for these five attributes.
public sealed class GridAttribute(double step) : Attribute { public double Step { get; } = step; }
public sealed class EventAttribute : Attribute;
public sealed class StateAttribute : Attribute;
public sealed class InitialAttribute(double value) : Attribute { public double Value { get; } = value; }
public sealed class FixedLengthAttribute(int length) : Attribute { public int Length { get; } = length; }
public sealed class OneHotAttribute : Attribute { public bool AllowAllZero { get; init; } }

[AttributeUsage(AttributeTargets.Method)]
public sealed class MaxIterationsAttribute(int count) : Attribute { public int Count { get; } = count; }

public readonly record struct AnimatedValue(float Position, float Displacement, float Velocity, bool Active);
public readonly record struct SpringValue(float Displacement, float Velocity);

// The operations with no plain C# form. Each body computes in FP32, operation for operation as the
// macro in Sharpwired.Core, so the plain method gives the value of the program bit for bit.
public static class Hw
{
    // Macros.AnimatedObject with one spring, the default thresholds 0.01 and 0.01, and a snap bit.
    // matrix has length 4: displacement from displacement, displacement from velocity, velocity from
    // displacement, velocity from velocity.
    public static AnimatedValue AnimatedObject(float[] matrix, float displacement, float velocity, float destination,
        float destinationDuringSteps, bool settleCheck, bool snap);

    // Macros.ApplySpringMatrix.
    public static SpringValue ApplySpringMatrix(float[] matrix, float displacement, float velocity);

    // Macros.Relu: f(max(0, value)) in one operation. MathF.Max(0, value) costs a Compare.
    public static float Relu(float value);

    // Circuit.Boolean, the boolean atom: score within 0..1, 0 below 0.5, otherwise 1.
    public static bool Boolean(float score);

    // Macros.FirstActive: the first candidate that is 1, gated by active, as a one-hot (or all zero) array.
    public static bool[] FirstActive(bool active, bool[] candidates);
}
```

`[Initial]` and `[FixedLength]` are not in the R4.2 list. The builder toggle needs `[Initial]` (the viewport cells start at 1280 and 800), and a `float[]` property has no length in its type. Open question 5 asks the controller to accept them.

### Sharpwired.IL

```csharp
namespace Sharpwired.IL;

public static class HandwiredTranslator
{
    // Each parameter leaf is an input or event cell; each return leaf is an output.
    public static TranslatedFunction TranslateFunction(MethodInfo method);

    // (TState Next, TOutput Output) Step(TState state, TInput input), section "Decisions", question 2.
    public static TranslatedStep TranslateStep(MethodInfo method);

    // A method with one loop of form 3.
    public static RecurrentFunction TranslateRecurrent(MethodInfo method);
}

public readonly record struct NamedWire(string Path, Wire Wire);

public sealed class TranslatedFunction
{
    public FinishedTable Table { get; }
    public ImmutableArray<NamedWire> Inputs { get; }
    public ImmutableArray<NamedWire> Outputs { get; }
    public ImmutableArray<Wire> LoopOverflowBits { get; }
    public ProgramEvaluator<IReadOnlyList<Wire>> CreateEvaluator();
    // Run the plain method by reflection on the leaves of the source table, and return the output leaves.
    public float[] EvaluatePlain(ReadOnlySpan<float> source);
}

public sealed class TranslatedStep
{
    public FinishedTable Table { get; }
    public ImmutableArray<NamedWire> Cells { get; }
    public ImmutableArray<NamedWire> Outputs { get; }
    public Wire Cell(string path);
    public Wire Output(string path);
    public ProgramEvaluator<IReadOnlyList<Wire>> CreateEvaluator();
    // An evaluator that runs the plain method: next values of the state cells, 0 for events, inputs unchanged.
    public ISourceTableEvaluator CreatePlainEvaluator();
}

public sealed record RecurrentStep(FinishedTable Table, ImmutableArray<NamedWire> State, ImmutableArray<NamedWire> Invariants, Wire Done);

public sealed class RecurrentFunction
{
    public TranslatedFunction Prefix { get; }
    public RecurrentStep Step { get; }
    public TranslatedFunction Suffix { get; }
}

public sealed record RecurrentResult(ImmutableArray<float> Outputs, int Steps, bool Completed);

// The CPU runner. Phase 5 adds the GPU runner of the same step table (R5.4).
public sealed class RecurrentRunner(RecurrentFunction function)
{
    public RecurrentResult Run(ReadOnlySpan<float> arguments, int stepLimit);
}

public sealed class TranslationException : ArgumentException
{
    public string? SourceFile { get; }
    public int? Line { get; }
    public int IlOffset { get; }
    public string Construct { get; }
    public string Reason { get; }
}
```

The translator caches nothing between calls. Each call translates the method again.

### Changes to current projects

- `src/Sharpwired.Core/Circuit.Preview.cs` (new): `public (ImmutableArray<Term> Terms, double Bias) PreviewAffine(IReadOnlyList<Term> terms, double bias)` gives the form that `Affine` stores (normalized, constants folded, small producers composed) and appends nothing. `public double? FormGrid(IReadOnlyList<Term> terms, double bias)` gives the grid of `InferFormGrid`, or null. The translator needs both for the emission rules, and they are internal today.
- `src/Sharpwired.Cells/`: the new interfaces `ISourceTableEvaluator` (`ISourceTableEvaluation Evaluate(ReadOnlySpan<float> source)`) and `ISourceTableEvaluation` (`float[] NextValues`, `float Read(Wire output)`). `ProgramEvaluator<TOutput>` and `ProgramEvaluation` implement them with no change of behavior.
- `samples/Toggle.Builder/ToggleProgram.cs`: `Build(Func<ICellScope, ToggleCells> declare)`. `Build()` calls it with `Declare`.
- `samples/Toggle.Builder/ToggleMachine.cs`: `ToggleMachine(BuiltToggle built, ISourceTableEvaluator evaluator, WriteBack writeBack = WriteBack.EventsAndSettleChecks)`. The current constructor builds the builder toggle and calls it with `ProgramEvaluator.Create(built.Table, built.Output)`.
- `tests/Toggle.Builder.Tests/ToggleTests.cs`: move `Frame` and `ScriptedTrace` into the new file `tests/Toggle.Builder.Tests/ToggleTrace.cs` (`internal static class ToggleTrace`), so that the two new toggle test projects link it.

## Emission rules

The translator emits operations in IL order of the blocks, after loop unroll. Every branch in an acyclic region goes forward, so IL order is a topological order. A backward branch that is not a back edge of a natural loop is refused.

1. These operations emit at their IL instruction: `Compare`, `Multiply`, a known call (`Math.Min` and the others), an `Hw` call, an index read, and the result of an inlined `[Handwired]` call.
2. An affine form (from `add`, `sub`, `neg`, `mul` by a constant, `div` by a power of two, `conv.r4`) and a NOT have no wire. An affine form with two or more uses emits at its definition. An affine form with one use emits when an operation of rule 1 or 3 reads it, or at the return.
3. A gate, an AND and an OR emit at their merge block, in the order of question 4.
4. An output leaf or a next value with no wire emits at the return, in walk order.

The rule for an affine form F (C#-equal rule):

1. Let P be `circuit.PreviewAffine(F)`, the form that `Affine` stores.
2. F is exact when each coefficient of P is an FP32 value and `circuit.FormGrid(P)` is not null. Then each partial sum in any order is an exact FP32 value, and so is each intermediate value of the C# expression. Emit `circuit.Affine(F)`.
3. F is one C# operation when it came from one IL instruction, P equals F (no producer was composed), and P has at most two elements (terms and a nonzero bias). FP32 addition is commutative, so the order of P gives the C# value. Emit `circuit.Affine(F)`.
4. Otherwise emit the C# expression tree of F as `RoundAffine` operations, one for each IL instruction, with the operands in C# order. A `RoundAffine` operation is never composed into a reader.
5. An `int` form must be exact (step 2). Otherwise the translator refuses it, because C# `int` arithmetic is exact up to 2^31 and FP32 only up to 2^24.

`trackLeft` is exact by step 2: `ViewportWidth` has grid 1 and range 0..16,384, so `0.5 × vw - 26` stays on the grid 0.5 within 2^24 steps. The rule emits `Affine([(vw, 0.5)], -26)`, as the builder toggle does.

## Files

New projects, each added to `Sharpwired.slnx`:

| Project | Kind | References |
| --- | --- | --- |
| `src/Sharpwired.Annotations/Sharpwired.Annotations.csproj` | library | none |
| `src/Sharpwired.IL/Sharpwired.IL.csproj` | library | `Sharpwired.Core`, `Sharpwired.Cells`, `Sharpwired.Annotations` |
| `samples/Toggle.FSharp/Toggle.FSharp.fsproj` | F# library | `Sharpwired.Annotations` |
| `tests/Sharpwired.Annotations.Tests/Sharpwired.Annotations.Tests.csproj` | xUnit | `Sharpwired.Annotations`, `Sharpwired.Core`, `Sharpwired.Timing`; links `Js.cs` |
| `tests/Sharpwired.IL.FSharpInputs/Sharpwired.IL.FSharpInputs.fsproj` | F# library, `DebugType` embedded | `Sharpwired.Annotations` |
| `tests/Sharpwired.IL.Tests/Sharpwired.IL.Tests.csproj` | xUnit | `Sharpwired.IL` and its references, `Sharpwired.IL.FSharpInputs`; links `Js.cs`, `TensorChecks.cs` |
| `tests/Toggle.CSharp.Tests/Toggle.CSharp.Tests.csproj` | xUnit | `Toggle.CSharp`, `Toggle.Builder`, `Sharpwired.IL` and their references; links `Js.cs`, `TensorChecks.cs`, `ToggleTrace.cs`, `ToggleReference.cs` |
| `tests/Toggle.FSharp.Tests/Toggle.FSharp.Tests.csproj` | xUnit | `Toggle.FSharp`, `Toggle.Builder`, `Sharpwired.IL` and their references; links the same 4 files |

Changed project: `samples/Toggle.CSharp/Toggle.CSharp.csproj` gets references to `Sharpwired.Annotations` and `Sharpwired.IL`.

Files of `src/Sharpwired.IL/` (each below 800 lines):

| File | Contents |
| --- | --- |
| `TranslationException.cs` | the exception and the message form |
| `SourceLocations.cs` | the PDB reader: sequence points and the location of an IL offset |
| `IlDecoder.cs` | the opcode table, the decoder, the token resolver |
| `ControlFlowGraph.cs` | blocks, edges, dominators, natural loops |
| `SsaBuilder.cs` | stack and locals to SSA (Braun et al.), phis, types |
| `CellLayout.cs` | the walk of parameter and return types, attributes, cell declarations, `Next` pairs |
| `Values.cs` | the value kinds: wire, constant, affine form, NOT, AND, OR, gate, aggregate, array, address |
| `Gates.cs` | phi to gate, the gate rules, the emit of a gate |
| `Emitter.cs` | the emission rules and the C#-equal rule of affine forms |
| `OperationLowering.cs` | the map from IL instructions and known calls to builder calls |
| `Intrinsics.cs` | the map from `Hw` methods to macros |
| `LoopForms.cs` | the three loop forms and the range refinement |
| `Inliner.cs` | calls of `[Handwired]` methods, data constructors, property getters |
| `HandwiredTranslator.cs` | the entry points and `TranslatedFunction`, `TranslatedStep` |
| `Recurrent.cs` | `RecurrentFunction`, `RecurrentStep`, `RecurrentRunner`, the state range fixed point |
| `PlainInvoker.cs` | the reflection runner of a plain method on a source table |

Files of the samples and tests:

| File | Contents |
| --- | --- |
| `src/Sharpwired.Annotations/Attributes.cs`, `Hw.cs`, `HwValues.cs` | the API above |
| `samples/Toggle.CSharp/ToggleStep.cs` | `ToggleState`, `ThumbState`, `Pointer`, `ToggleInput`, `ToggleOutput`, `Step`, `InsideTrack` |
| `samples/Toggle.CSharp/CSharpToggle.cs` | `Build()`: translate `Step`, map the cell paths and output paths to a `BuiltToggle` |
| `samples/Toggle.FSharp/Toggle.fs` | the F# types and functions of question 8 |
| `tests/Sharpwired.Annotations.Tests/HwTests.cs` | each `Hw` body against its macro |
| `tests/Sharpwired.IL.Tests/Inputs/*.cs` | C# test inputs, one file per construct group |
| `tests/Sharpwired.IL.FSharpInputs/Inputs.fs` | F# test inputs |
| `tests/Sharpwired.IL.Tests/SeededSources.cs` | random source tables within the declared ranges and grids |
| `tests/Sharpwired.IL.Tests/*Tests.cs` | the tests of tasks 2 to 6 |
| `tests/Toggle.CSharp.Tests/CSharpToggleTests.cs` | the toggle checks of question 1 |
| `tests/Toggle.FSharp.Tests/FSharpToggleTests.cs` | the same checks for the F# toggle |

The toggle cells in the plain types:

| Cell | C# property | Attributes |
| --- | --- | --- |
| `on`, `armed` | `ToggleState.On`, `Armed` (`bool`) | none (state parameter) |
| `thumb.displacement`, `thumb.velocity`, `thumb.destination` | `ThumbState` (`float`) | `[Range(-40, 40)]`, `[Range(-1280, 1280)]`, `[Range(0, 20)]` |
| `viewportWidth`, `viewportHeight` | `ToggleInput` (`float`) | `[Range(0, 16384), Grid(1), Initial(1280)]`, `[..., Initial(800)]` |
| `down.active`, `up.active`, `cancel` | `Pointer.Active`, `ToggleInput.Cancel` (`bool`) | `[Event]` |
| `down.x`, `down.y`, `up.x`, `up.y` | `Pointer.X`, `Y` (`float`) | `[Range(-16384, 16384), Grid(1.0 / 64)]` |
| `springMatrix[0..3]` | `ToggleInput.SpringMatrix` (`float[]`) | `[FixedLength(4), Range(-MatrixBound, MatrixBound)]` |
| `settleCheck`, `snap` | `ToggleInput` (`bool`) | none |

`MatrixBound` is the constant `10.360516548156738`, because an attribute argument must be a constant. The probe computed it with `SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45))`. A test checks that the constant equals `SpringMatrixBound(ToggleLayout.Spring)`. F# writes `0.015625` for the grid, because an F# attribute argument must be a literal.

## IL opcode table

Accepted opcodes. The short forms (`.s`) follow their long forms. Each other opcode is refused with the message construct "the IL instruction <name>".

| Opcodes | Condition | Lowering |
| --- | --- | --- |
| `nop` | none | none |
| `ldarg`, `ldarg.0` to `ldarg.3`, `starg` | none | SSA variable |
| `ldloc`, `ldloc.0` to `ldloc.3`, `stloc`, `stloc.0` to `stloc.3` | none | SSA variable |
| `ldarga`, `ldloca` | the address goes only to a known call, `ldfld`, `ldflda`, `stfld`, `initobj` or `ldobj` | address value |
| `ldc.i4.m1`, `ldc.i4.0` to `ldc.i4.8`, `ldc.i4`, `ldc.r4` | none | constant |
| `dup`, `pop` | none | stack only |
| `add`, `sub`, `neg` | `float` or `int` | affine form |
| `mul` | a constant operand: affine form. Two wires: `Multiply` | |
| `div` | `float` by a power-of-two constant | affine form, coefficient `1 / c` |
| `and`, `or`, `xor` | bits | AND, OR, XOR |
| `ceq`, `cgt`, `cgt.un`, `clt`, `clt.un` | `float`, `int` or bits; finite ranges | `Compare`, question 4 |
| `br`, `brtrue`, `brfalse`, `beq`, `bge`, `bgt`, `ble`, `blt`, `bne.un`, `bge.un`, `bgt.un`, `ble.un`, `blt.un` | forward, or a back edge of a natural loop | CFG edges, question 4 |
| `switch` | `int` with a finite range | CFG edges, question 4 |
| `ret` | none | return value |
| `conv.r4` | from `int` (exact within 2^24) or `float` | the same value |
| `conv.i4` | from a `float` with grid 1 | the same value |
| `call` | a known method of the lowering table, a property getter, a `[Handwired]` method | inlining |
| `callvirt` | the target is not virtual (a getter of a sealed record class) | as `call` |
| `newobj` | a data constructor: its body only stores its parameters into fields of the type and calls the constructor of `object` or `ValueType`; or `ValueTuple`, `Tuple` | aggregate |
| `initobj` | a struct of leaves | aggregate of zero constants |
| `ldobj` | a struct from an address of a local, argument or field | copy |
| `ldfld`, `ldflda`, `stfld` | `stfld` only into a local or argument aggregate | aggregate field |
| `ldsfld` | an `initonly` static field of type `float`, `int`, `bool`, `float[]` or `int[]` | constants, read by reflection at translation |
| `ldelem.r4`, `ldelem.i4`, `ldelem.u1`, `ldelem.i1`, `ldelem` | constant index: the element. Computed index: `IndexRead`, with an index range within 0..length - 1 | |
| `ldelema` | constant index, then `ldfld` | element field |
| `newarr` | constant length | aggregate array |
| `stelem.r4`, `stelem.i4`, `stelem.i1`, `stelem` | an array from `newarr` of this method; constant index | array element |
| `ldlen` | a fixed-length array | constant |
| `tail.` prefix | before a `call` of a `[Handwired]` method | ignored |

Refused opcodes include: `ldc.r8`, `conv.r8`, `conv.r.un` and each other float64 operation; `ldc.i8` and each 64-bit conversion; `rem`, `rem.un`, `div.un`, `shl`, `shr`, `shr.un`, `not`; `ldnull`, `ldstr`, `box`, `unbox`, `unbox.any`, `isinst`, `castclass`; `throw`, `rethrow`, `leave`, `endfinally`, `endfilter`; `calli`, `ldftn`, `ldvirtftn`, `jmp`; `stsfld`, `ldsflda`; `ldind.*`, `stind.*`, `localloc`, `cpblk`, `initblk`; `sizeof`, `mkrefany`, `refanyval`, `refanytype`, `arglist`, `ldtoken`, `ckfinite`; the prefixes `constrained.`, `volatile.`, `unaligned.`, `readonly.`, `no.`; and each `.ovf` conversion and arithmetic opcode.

## Lowering table

The source column names the row of `$REF/docs/expressions.md`.

| Plain expression | Types | Builder call | Exactness | Source |
| --- | --- | --- | --- | --- |
| `a + b`, `a - b`, `-a`, `a * c`, `a / 2^k` | `float`, `int` | `Affine` or a `RoundAffine` chain, section "Emission rules" | equals C# | execution-model.md "FP32" |
| `a * b` | `float` | `Multiply` | one FP32 product, for finite operands | "Macro contracts" |
| `a * b` | `int` | `Multiply`; the result range must stay within 2^24 | exact | |
| `a < b` and the other relations | `float`, `int` | `Compare` | exact for finite declared ranges | "a >= b ..." |
| `a == b` | bits | `Compare(a, Equal, b)` | exact | |
| `!a`, `a && b`, `a & b`, `a \|\| b`, `a \| b`, `a ^ b` | bits | NOT, AND, OR, XOR, question 4 | exact for exact bits | "NOT, AND and OR" |
| `c ? x : y`, `if (c) v = x;` | any | the gate rules, question 4 | exact at a bit c, up to the sign of zero | "condition ? on : off" |
| `Math.Min`, `Math.Max`, `MathF.Min`, `MathF.Max` | `float`, `int` | `MinimumExact`, `MaximumExact` | one operand, up to the sign of zero; finite ranges | "Math.min(a, b) ..." |
| `Math.Clamp(v, lo, hi)` | `float`, `int` | `ClampExact(v, lo, hi)`; needs `max(lo) <= min(hi)` from the ranges | one operand | "Math.min(a, b) ..." |
| `Math.Abs`, `MathF.Abs` | `float` | `Absolute` | exact | "Math.min(a, b) ..." |
| `array[i]` | constant i | the element wire | exact | |
| `array[i]` | computed `int` i | `IndexRead(i, elements, OutOfRange.Clamp)`; the range of i must lie in 0..length - 1 | exact (a copy) | "table[index] ..." |
| `(float)i` | `int` to `float` | the same wire | exact within 2^24 | |
| `(int)x` | `float` with grid 1 | the same wire | exact | |
| `Hw.AnimatedObject` | | `AnimatedObject([new AnimatedSpring(matrix, displacement, velocity, destination) { DestinationDuringSteps = during }], new AnimatedObjectOptions(settleCheck) { Snap = snap })` | the macro contract | execution-model.md "Springs and time" |
| `Hw.ApplySpringMatrix` | | `ApplySpringMatrix` | the macro contract | |
| `Hw.Relu` | | `Relu` | exact | |
| `Hw.Boolean` | | `Boolean` | FP32 atom | "Decision bits" |
| `Hw.FirstActive` | | `FirstActive` | the macro contract | |
| property getter | | the backing field of the aggregate | exact | |
| record and struct constructor, tuple | | an aggregate | exact | |

The macros that need a unit wire (`Spring`, `Argmax`, `ClampZeroOne`) have no `Hw` method in phase 4. Open question 4 asks about them.

## Error message rules

1. The form is `<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>`. `TranslationException.Message` is this text, with no parameter name.
2. `<file>` is the document path in the PDB. `<line>` is the start line of the last sequence point that is not hidden, at or before the IL offset.
3. When the method has no PDB, the location is `<type>.<method>+IL_<offset as 4 hex digits>`.
4. For an inlined method, the location is in the callee. Each call site follows, innermost first, as ` (called from <file>:<line>)`.
5. `<construct>` names the source construct: "the IL instruction box", "double arithmetic", "a call to System.Console.WriteLine(String)", "int division", "float division by 3", "a closure", "a loop with two exits", "a loop with no maximum", "a comparison", "recursion", "a public field in ToggleInput".
6. `<reason>` is one sentence in ASD-STE100 that states the rule, for example "the program computes in FP32; write the expression in float".
7. A macro refusal becomes a translation error. The translator catches the `ArgumentException` of the macro and uses its message as the reason, for example `Tests.cs:41: a comparison is not supported in a [Handwired] method: comparison operands need finite declared ranges`.
8. Errors of the method itself, before the IL, are plain `ArgumentException` values: "the method Probe.F has no [Handwired] attribute", "a [Handwired] method must be static", "a [Handwired] method must not be generic", "TranslateStep needs two parameters and a (Next, Output) tuple".

## Behavior with no direct form

| C# or F# behavior | Sharpwired behavior |
| --- | --- |
| `double` arithmetic, for example `(float)(x * 0.1)` (IL `conv.r8; ldc.r8 0.1; mul; conv.r4`) | Refused. The program computes in FP32, and a float64 step gives other values. Write `x * 0.1f`. |
| `const double` in a `float` expression | Refused when it promotes the expression to `double`. Declare the constant as `const float`. |
| `int` division and remainder (`div`, `rem`) | Refused. C# rounds toward zero, and the runtime has no exact macro for it. A threshold count or an index read table (expressions.md) is the Handwired construction. |
| `float` division by a constant that is not a power of two (`x / 3f`) | Refused. C# rounds `x / 3` once. `x * f(1/3)` gives another value for some x. Write `x * (1f / 3)` in the plain method, and then the plain method and the program agree. |
| division by a runtime value, `Math.Sqrt`, `MathF.Sin`, `Math.Floor`, `Math.Round` | Refused. Handwired has only inexact library constructions for them. |
| `Math.Clamp(v, lo, hi)` when lo can be greater than hi | Refused. .NET throws, and `ClampExact` returns hi. |
| `Math.Min(a, b)` and `Math.Max(a, b)` with a == b | The program returns a. The value is equal, and the sign of zero can differ. |
| `c ? x : y` on `float` | `Select` returns the chosen operand plus a signed zero, so -0 can become +0. The values are equal. |
| an unchecked `int` overflow | Refused at translation: an `int` result range past 2^24 has no exact FP32 form. |
| an array index out of range | Refused at translation: the index range must lie in 0..length - 1. C# throws, and `IndexRead` clamps. |
| NaN and infinity | Not part of the contract. The declared ranges are finite, and `ProgramEvaluator` refuses a non-finite output. A plain method that overflows to infinity has no equal program. |
| `&&` and `\|\|` skip the right side | The program computes both sides. A plain method has no side effect, so the values are equal. |
| exceptions, `checked` arithmetic, `try` | Refused. |
| virtual and interface calls, delegates, lambdas, local functions that capture | Refused. A C# lambda is a `newobj` of a closure class and an `ldftn`. |
| a `static readonly` field | Read one time at translation. A later change of a `static readonly` array does not change the program. |
| an attribute argument that is not a constant | Not possible in C# or F#. The toggle writes `MatrixBound` as a literal, and a test checks it. |
| `ValueTuple` as a cell type | Refused, as the Cells walker refuses public fields. It is accepted as the top return type and as an intermediate value. |
| the F# optimizer in Release | It can inline a small F# function and remove a closure. `[<MethodImpl(MethodImplOptions.NoInlining)>]` keeps a `[<Handwired>]` helper as a call. |
| `Hw.AnimatedObject` with `destinationDuringSteps` equal to `destination` as the same value | The macro skips the rebase when the two arguments are the same wire. The plain body always rebases, which can change -0 to +0. The toggle passes two different cells. |

## Tasks

The tasks run in order. Each task ends with `dotnet build -warnaserror`, `dotnet test` and `dotnet test -c Release` green, and a commit.

### Task 1: annotations, Hw, and the Core and Cells additions

- Files: `src/Sharpwired.Annotations/` (`Sharpwired.Annotations.csproj`, `Attributes.cs`, `Hw.cs`, `HwValues.cs`), `tests/Sharpwired.Annotations.Tests/` (`Sharpwired.Annotations.Tests.csproj`, `HwTests.cs`, `AttributeTests.cs`), `src/Sharpwired.Core/Circuit.Preview.cs`, `tests/Sharpwired.Core.Tests/PreviewTests.cs`, `src/Sharpwired.Cells/ProgramEvaluator.cs` (the interfaces), `tests/Sharpwired.Cells.Tests/ProgramEvaluatorTests.cs`, `Sharpwired.slnx`.
- Write each `Hw` body as the macro operations in FP32, one statement per operation, with the same term order. For example, a `Multiply` of two operands with no proven sign is `f(f(a × max(0, b)) - f(a × max(0, -b)))`, as the three operations of `Circuit.Multiply`.
- Tests:
  - `HwTests`: each `Hw` method against its macro, evaluated with `Circuit.EvaluateReference`, over 2,000 seeded inputs within the macro ranges plus the edge values (0, -0, the range ends, the settle thresholds ± one FP32 step). Compare with `Js.Is`. For `AnimatedObject`, use the toggle ranges and the matrices `SpringMatrixPower(step, n)` for n = 0, 1, 4, 300.
  - `AttributeTests`: each attribute keeps its arguments; `AttributeUsage` targets.
  - `PreviewTests`: `PreviewAffine` equals the stored form of `Affine` for 10 seeded forms, and it appends no operation; `FormGrid` equals the grid of the stored operation.
  - `ProgramEvaluatorTests`: `ProgramEvaluator<T>` works through `ISourceTableEvaluator`.
- R items: R4.2 (attributes), R4.3 (`Hw` intrinsics, part).

### Task 2: the IL front end

- Files: `src/Sharpwired.IL/` (`Sharpwired.IL.csproj`, `TranslationException.cs`, `SourceLocations.cs`, `IlDecoder.cs`, `ControlFlowGraph.cs`, `SsaBuilder.cs`), `tests/Sharpwired.IL.Tests/` (`Sharpwired.IL.Tests.csproj`, `Inputs/FrontEndInputs.cs`, `DecoderTests.cs`, `SourceLocationTests.cs`, `ControlFlowTests.cs`, `SsaTests.cs`), `tests/Sharpwired.IL.FSharpInputs/` (`Sharpwired.IL.FSharpInputs.fsproj`, `Inputs.fs`), `Sharpwired.slnx`.
- Tests:
  - The decoder gives the opcode list of 6 input methods. The expected lists come from the probe output above. Each test accepts the Debug and the Release list, because `nop` and the return form differ. The test removes `nop` and compares the rest.
  - The location of an IL offset: for a C# input (separate PDB) and an F# input (embedded PDB), the line of a marker comment in the source file. The test finds the marker line by a read of the source file, so an edit of the file does not break the test. A hidden F# sequence point gives the line of the visible point before it.
  - A method with no PDB (a method of `System.Math`) gives the location form `Math.Max+IL_0000`.
  - CFG: the block count and edges of `c ? a : b`, of an `&&` chain, of a C# `for` loop and of the F# `for ... in` loop. The dominator tree and the natural loop of each loop.
  - SSA: the phis of the C# `?:` (one stack phi), of the Debug F# `&&` chain (a phi that a branch reads), and of a loop (header phis). The types: a `bool` local gives `Bit`; a phi of `ldc.i4.0` and a comparison gives `Bit`.
  - Refusals with file and line: an exception clause, and 5 refused opcodes (`box`, `ldstr`, `conv.r8`, `rem`, `ldftn`).
- R items: R4.3 (IL decode, CFG, SSA), R4.4 (part).

### Task 3: cells and operation lowering for straight-line code

- Files: `src/Sharpwired.IL/` (`CellLayout.cs`, `Values.cs`, `Emitter.cs`, `OperationLowering.cs`, `Intrinsics.cs`, `HandwiredTranslator.cs`, `PlainInvoker.cs`), `tests/Sharpwired.IL.Tests/` (`SeededSources.cs`, `Inputs/ArithmeticInputs.cs`, `Inputs/AggregateInputs.cs`, `Inputs/RefusedInputs.cs`, `CellLayoutTests.cs`, `ArithmeticTests.cs`, `AggregateTests.cs`, `IntrinsicTests.cs`, `RefusalTests.cs`).
- Straight-line code only. A branch is refused until task 4 with "a branch".
- Each per-construct test builds the expected circuit by hand in the test, translates the input method, and checks `AssertSameOperations`. It then compares `CreateEvaluator` with `EvaluatePlain` over 200 seeded tables from `SeededSources` (bits 0 or 1; `int` values within the range; `float` values on the grid, or random FP32 values in the range with the range ends, 0 and -0).
- Per-construct tests:
  - An exact affine chain (`x * 0.5f - 26f` on a grid) gives one `Affine`. An inexact chain (`a + b + c` with no grid) gives two `RoundAffine` operations in C# order. `x * 0.3f` with no grid gives one ordinary `Affine` (one C# operation). `x / 4f` gives the coefficient 0.25.
  - `a * b` on `float` gives `Multiply`. `a * b` on `int` within 2^24 gives `Multiply`.
  - The 6 relations in value form on `float` and on `int` give `Compare` with the relations of question 4. `!=` gives `Not(Compare(Equal))`.
  - NOT, `&`, `|`, `^` on bits.
  - `Math.Min`, `Math.Max`, `Math.Clamp`, `Math.Abs`, `MathF.Min`, `MathF.Max` on `float`; `Math.Min` on `int`.
  - Aggregates: a C# record struct getter, a C# record class getter (`callvirt`), `stfld` into a struct argument, `newobj` of a record and a `ValueTuple` return, `initobj`.
  - Arrays: a constant index, a computed index (`IndexRead`), `ldlen`, `newarr` with constant stores.
  - `static readonly` float and table.
  - `conv.r4` from `int`.
  - `Hw.Relu`, `Hw.Boolean`, `Hw.ApplySpringMatrix`, `Hw.FirstActive`, `Hw.AnimatedObject` give the macro operations.
  - `CellLayoutTests`: the cells of the toggle types in walk order with the names, policies, ranges, grids and initial values of the table in section "Files"; `[OneHot]`; the attribute on a primary constructor parameter; a `[State]` property in a `TranslateFunction` parameter; the refusals of the walk (question 2).
- Refusal tests, each with the file and the line of its marker: double arithmetic, `int` division, float division by 3, `Math.Sqrt`, a call to `Console.WriteLine`, an `int` range past 2^24, a computed index with a range past the array, a `Compare` of a value with no range (a macro refusal), `Math.Clamp` with lo that can pass hi, `MathF.FusedMultiplyAdd`, a public field type as a parameter, an instance method, a generic method, a method with no `[Handwired]`.
- R items: R4.1 (C#), R4.2, R4.3 (operation lowering, intrinsics), R4.4.

### Task 4: branch to select and inlining

- Files: `src/Sharpwired.IL/` (`Gates.cs`, `Inliner.cs`, changes in `Emitter.cs`), `tests/Sharpwired.IL.Tests/` (`Inputs/BranchInputs.cs`, `Inputs/CallInputs.cs`, `BranchTests.cs`, `InlineTests.cs`, `FSharpInputTests.cs`, `RefusalTests.cs`), `tests/Sharpwired.IL.FSharpInputs/Inputs.fs`.
- Per-construct tests (hand-built circuit and `AssertSameOperations`, then seeded values):
  - `c ? x : y` on `float` gives `Select(c, y, x)`. On constants gives the affine of rule 5. `c ? x : 0f` gives `Multiply(c, x)`.
  - `if (c) y = x * 2f;` gives `Select(c, x, Affine(2x))`.
  - `a && b && c` as a value and as a branch condition gives one `Reglu` AND of 3 literals. `a && !b` gives `Not(b)` then the AND. `a || b` gives the OR form.
  - A gate with both sides that emit operations: the C# order (the else side first in IL) and the F# order (the then side first). The expected circuits differ only in the operation order.
  - A helper `[Handwired]` call with two `ret` instructions: its AND result emits at the call, and an outer AND reads it as one literal.
  - The `switch` of an F# `match` on 0, 1 and other.
  - F# inputs: a class record, a struct record, a struct tuple, a `System.Tuple`, `if`, `match`, `&&`, `not`, `min`, `max`, `abs`, an `inline` function, a `NoInlining` helper.
  - Each test above passes in Debug and in Release with the same expected circuit.
- Refusal tests: recursion, a C# lambda, an F# closure that a `NoInlining` function receives, an F# `seq`, a call to a method without `[Handwired]`, a backward branch outside a natural loop (an F# input cannot make one; use a C# `goto` input).
- R items: R4.1 (F#), R4.3 (inlining, branch to select).

### Task 5: loop forms 1 and 2

- Files: `src/Sharpwired.IL/LoopForms.cs`, changes in `ControlFlowGraph.cs` and `Emitter.cs`, `tests/Sharpwired.IL.Tests/` (`Inputs/LoopInputs.cs`, `LoopTests.cs`), `tests/Sharpwired.IL.FSharpInputs/Inputs.fs`.
- Tests: the rows of forms 1 and 2 and the row "all" of the table in question 5. Each value test compares `CreateEvaluator` with `EvaluatePlain` over 200 seeded tables. Each operation test builds the unrolled circuit by hand for the small loops (4 copies of `s += x * w[i]`; 8 copies of `s += x` with active bits).
- R items: R4.3 (loop lowering, forms 1 and 2).

### Task 6: loop form 3, the step program and the CPU runner

- Files: `src/Sharpwired.IL/Recurrent.cs`, changes in `LoopForms.cs` and `HandwiredTranslator.cs`, `tests/Sharpwired.IL.Tests/` (`Inputs/RecurrentInputs.cs`, `RecurrentTests.cs`).
- Tests: the rows of form 3 of the table in question 5. Also: the step table has one state cell for each loop variable and one input cell for each invariant; a lane that is done keeps its state for 3 more evaluations; `TranslateFunction` on a form 3 method gives the refusal "a loop with no maximum" with its file and line.
- R items: R4.3 (loop lowering, form 3).

### Task 7: the C# toggle

- Files: `samples/Toggle.CSharp/` (`Toggle.CSharp.csproj`, `ToggleStep.cs`, `CSharpToggle.cs`, `Program.cs`), `samples/Toggle.Builder/ToggleProgram.cs`, `samples/Toggle.Builder/ToggleMachine.cs`, `tests/Toggle.Builder.Tests/ToggleTrace.cs` (new, moved from `ToggleTests.cs`), `tests/Toggle.Builder.Tests/ToggleTests.cs`, `tests/Toggle.CSharp.Tests/` (`Toggle.CSharp.Tests.csproj`, `CSharpToggleTests.cs`), `Sharpwired.slnx`.
- `CSharpToggle.Build()` translates `ToggleStep.Step` and returns `new BuiltToggle(cells, output, step.Table)`, with each wire from `step.Cell(path)` and `step.Output(path)`.
- Tests (`CSharpToggleTests`):
  - The translated table has the 20 cell names in walk order: `on`, `armed`, `thumb.displacement`, `thumb.velocity`, `thumb.destination`, `viewportWidth`, `viewportHeight`, `down.active`, `down.x`, `down.y`, `up.active`, `up.x`, `up.y`, `cancel`, `springMatrix[0]` to `springMatrix[3]`, `settleCheck`, `snap`.
  - Items 1 to 3 of question 1 against `ToggleProgram.Build(StateFirst)`, where the test function `StateFirst` declares the builder cells in the translated order. The operation count is 69 and the layer count is 21.
  - Item 4: a `ToggleMachine` on the translated program and a `ToggleMachine` on `step.CreatePlainEvaluator()` run the scripted trace. Each frame gives the same 6 outputs with `Js.Is` and the same `Clock.IsRunning`.
  - Item 5: the 6-step comparison rule of phase 3 (`ToggleTests`) for the translated machine against `ToggleReference`, with 38 frames in which the reference animates, and a thumb error below 1e-5 px.
  - The 4 same-timestamp cases of `ToggleTests` for the translated machine.
  - `MatrixBound` equals `SpringMatrix.SpringMatrixBound(ToggleLayout.Spring)`.
- Run the window: `dotnet run --project samples/Toggle.CSharp -- --frames 120 --screenshot /private/tmp/claude-502/toggle-cs.png`. Look at the screenshot: the track and the thumb are at the center.
- R items: R4.1, R4.5 (C#).

### Task 8: the F# toggle and the close of the phase

- Files: `samples/Toggle.FSharp/` (`Toggle.FSharp.fsproj`, `Toggle.fs`), `tests/Toggle.FSharp.Tests/` (`Toggle.FSharp.Tests.csproj`, `FSharpToggleTests.cs`), `docs/design.md`, `docs/superpowers/specs/2026-10-16-sharpwired-design.md`, `Sharpwired.slnx`.
- Tests (`FSharpToggleTests`): the checks of task 7 for `Toggle.FSharp.step`, with the same `StateFirst` builder toggle. Also: the F# translated operation list equals the C# translated operation list with `AssertSameOperations`.
- Add `src/Sharpwired.Annotations/` to the solution layout of `docs/design.md`. Check R4.1 to R4.5 in the spec checklist.
- Run the full verification below.
- R items: R4.1 (F#), R4.5 (F#), RA.1 to RA.3.

## Verification

- `dotnet build -warnaserror` passes.
- `dotnet test` passes (Debug).
- `dotnet test -c Release` passes.
- `grep -rn "TODO\|Skip =" src tests samples` gives no result.
- `grep -rln "Raylib_cs" src samples tests` gives only `src/Sharpwired.Raylib/RaylibWindow.cs`.
- `src/Sharpwired.Annotations/Sharpwired.Annotations.csproj` has no `ProjectReference` and no `PackageReference`.
- `samples/Toggle.FSharp/Toggle.FSharp.fsproj` references only `Sharpwired.Annotations`.
- Each new source file is below 800 lines.
- The window of task 7 shows the toggle in the screenshot.
- Each R4 item maps to at least one test:

| Item | Tests |
| --- | --- |
| R4.1 `[Handwired]` methods in C# and F# translate to a `Circuit` | `ArithmeticTests`, `BranchTests`, `FSharpInputTests`, `CSharpToggleTests`, `FSharpToggleTests` |
| R4.2 types and attributes | `CellLayoutTests` (`float`, `int`, `bool`, structs, records, fixed-length arrays, `[Range]`, `[Grid]`, `[Event]`, `[State]`, `[OneHot]`, `[Initial]`, `[FixedLength]`), `LoopTests` (`[MaxIterations]`), `AttributeTests` |
| R4.3 IL decode, CFG, SSA, inlining, three loop forms, branch to select, operation lowering, `Hw` intrinsics | `DecoderTests`, `ControlFlowTests`, `SsaTests`, `InlineTests`, `LoopTests`, `RecurrentTests`, `BranchTests`, `ArithmeticTests`, `IntrinsicTests`, `HwTests` |
| R4.4 a refused construct gives the source file and line | `RefusalTests` (C# with a separate PDB, F# with an embedded PDB), `SourceLocationTests` |
| R4.5 the toggle in plain C# and plain F#, equal to the plain methods over the scripted trace | `CSharpToggleTests`, `FSharpToggleTests` |
| RA.1 | `NOTICE` keeps the Handwired MIT notice (no change) |
| RA.2 | Each new comment and doc in ASD-STE100 |
| RA.3 | The green `dotnet test` run at the end of each task |

## Open questions

1. Cell order. The design contract puts the state parameter first, so the translated toggle declares its 5 state cells first. The plan adds `ToggleProgram.Build(Func<ICellScope, ToggleCells> declare)` and compares with the builder toggle declared in that order. Exact `AssertSameOperations` and `AssertSameProgram` then hold. The alternative is a one-parameter form, `Step(ToggleCells cells)` with `[State]` properties in the Handwired order, which needs no builder change but does not match the design signature. Do you accept the builder overload?
2. Pattern recognition. The design says that the translator "also recognizes the handwired patterns" (a prefix network for prefix sums, a threshold count for `floor(x / step)`). R4.3 names only the three loop forms. The plan does not recognize these patterns in phase 4. Do you agree to defer them?
3. Window program. The plan changes `samples/Toggle.CSharp/Program.cs` to open the translated C# toggle, as the design layout says. The builder toggle then has no window program, and its manual check of phase 3 runs through the translated program. Do you want an option that opens the builder toggle too?
4. `Hw` scope. Phase 4 has `AnimatedObject`, `ApplySpringMatrix`, `Relu`, `Boolean` and `FirstActive`. `Spring`, `Argmax` and `ClampZeroOne` need a unit wire, which needs a new constant cell in the table. Do you agree to add them in phase 6 only if the JSON search needs them?
5. Attributes outside R4.2. The plan adds `[Initial]` (the builder toggle starts the viewport at 1280 × 800) and `[FixedLength]` (a `float[]` has no length in its type). Do you accept the two attributes?
6. The recurrent form limits. One loop with no maximum per method, at the top level. The state cell range comes from a fixed point of 8 rounds, with a wide fallback range. Do you accept these limits for phase 4?
7. The sign of zero. The per-construct value tests use `==` on outputs that come from `Select`, `MinimumExact` or `MaximumExact`, because their contracts allow a different sign of zero. The toggle trace uses `Js.Is`. Do you agree?

## Decisions

The controller decided these open questions on 2026-10-17. Question 2 waits for the user.

1. Accepted: the state-first `ToggleProgram.Build(declare)` overload. The phase 3 `Build()` stays and keeps its cell order.
3. Accepted: `Toggle.CSharp` opens the translated C# toggle. The option `--builder` opens the builder toggle.
4. Accepted: `Spring`, `Argmax` and `ClampZeroOne` join `Hw` in phase 6 only if the JSON search needs them.
5. Accepted: `[Initial]` and `[FixedLength]`. The translator needs an initial state value and a fixed array length.
6. Accepted: the form 3 limits. One loop with no maximum per method, at the top level. The state range comes from a fixed point of 8 rounds, with a wide fallback range.
7. Accepted: the per-construct value tests compare with `==` where Handwired allows a different sign of zero (`Select`, `MinimumExact`, `MaximumExact`, execution-model.md "up to the sign of zero"). The toggle trace compares with `Js.Is`.

The user decided question 2 on 2026-10-17: add the pattern recognition to phase 4. Task 6b holds it and runs after Task 6 and before Task 7.

### Task 6b: pattern recognition (running totals and threshold counts)

- Source: docs/design.md, Layer 2, step 4 ("The translator also recognizes the handwired patterns where they cost less than an unrolled loop, for example a prefix network for running totals and a threshold count for `floor(x / step)`"). Handwired: `$REF/docs/expressions.md` rows 13 to 15 and line 48, `$REF/lab/ui-exploration/guide/patterns/threshold-counts.md`, `$REF/lab/ui-exploration/guide/sections/loops.md`, and `src/runtime/prefix-networks.ts`.
- Running totals. A loop of form 1 or 2 whose body is `acc[i] = combine(acc[i-1], x[i])` (or a scalar accumulator whose value at each step is stored to an array element) with an associative combine (`+`, `Math.Min`, `Math.Max`, `&&`, `||`, or a `[Handwired]` combine method marked associative with a new `[Associative]` attribute) lowers to `PrefixNetworks.Sklansky` (depth first). Add `[Associative]` to `Sharpwired.Annotations`. The translator must prove the shape from the IL; it must not guess. A loop that does not match keeps its normal form.
- Threshold counts. `(int)MathF.Floor(x / step)` and `x / step` integer division, and `i % count`, where `x` has a declared grid and a finite range that gives K possible results with K at most a limit (for example 64), lower to a threshold count: `1 + [x >= t1] + ...` with `Compare` bits, as threshold-counts.md describes. A value that depends only on the count is one sum over the count's one-hot. Larger K with an integer `x` lowers to an index read over a table of results (expressions.md row 15). Other cases keep the plain lowering or are refused, as the lowering table says.
- Tests:
  - For each pattern: the translated values equal the plain method over seeded inputs (`Js.Is`, or `==` where Handwired allows the sign of zero).
  - The operation count and the layer count of the recognized form are below those of the normal form (compile both: once with recognition, once with a translator option that turns it off).
  - The expected counts follow Handwired: K − 1 comparisons for K results in O(1) layers (expressions.md row 14); the prefix network counts follow `PrefixNetworkTests`.
  - A loop that looks like a running total but has a non-associative combine (for example `-`) keeps its normal form.
- Closes: the design.md step 4 sentence on patterns.

## Decisions after task 3

8. Decision 7 widens. An affine operation starts its sum at f(bias) = +0, so `-x`, `x * c` and `a + b` give +0 where C# gives -0. Handwired defines exactness as "equality to the stated FP32 calculation, ignoring the sign of zero" (execution-model.md, "Exactness"). The value tests therefore compare float outputs of affine, `Multiply`, `Select`, `MinimumExact` and `MaximumExact` with `==`. Bit outputs, index reads, `Hw` outputs and constants compare with `Js.Is`. For the toggle (tasks 7 and 8): the translated program against the builder program uses `Js.Is` on every output, because both are circuits with the same operations. The translated program against the plain method uses `Js.Is` on the bit outputs and `==` on the float outputs.
9. A cell with no `[Initial]` starts at the value of its range and grid nearest to 0.
10. A `break` to the end of a loop translates. Its IL is the same as a loop test with two blocks, which Release emits for `while (a && b)`. Exit edges that reach one exit block with the same phi values count as one exit. A `return` inside a loop stays refused.
11. The translator refuses an emitted value whose inferred range is not finite while the wires that it reads have finite ranges ("the value can overflow FP32; declare smaller ranges"). The rule does not look inside a macro, because `AnimatedObject` has a saturating `Reglu` by design.
12. Running totals in form 1 lower to the Sklansky network for its depth: log2(k) layers in place of k − 1, with (k/2)·log2(k) combines in place of k − 1. "Cost less" in the design means fewer layers for these loops. In form 2 the network also has fewer operations, because it removes the Select of each copy.
