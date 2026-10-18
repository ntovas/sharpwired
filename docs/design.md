# Sharpwired design

Sharpwired converts C# and F# methods into numeric programs. A numeric program is a fixed list of FP32 (32-bit float) operations: weighted sums, products with a ReLU, rounding to a bit, and table reads. It is a neural net with weights that are written, not trained. The design follows [chenglou/handwired](https://github.com/chenglou/handwired) (MIT license), with one difference: handwired has no tool that converts source code, and the user writes builder code by hand. Sharpwired adds an IL (Intermediate Language) translator, so a plain C# or F# method becomes a program.

## Decisions

| Topic | Decision |
| --- | --- |
| Frontend | An IL translator for methods marked `[Handwired]`. C# and F# both compile to IL, so one translator serves both languages. |
| Core | A C# port of handwired's `src/runtime`: circuit, macros, range and grid inference, compiler, evaluators, backpropagation, cells, host timing, prefix networks. |
| Host UI | Raylib-cs. |
| GPU | A WebGPU backend (wgpu-native through Silk.NET.WebGPU). Sharpwired writes WGSL (WebGPU Shading Language) from a compiled program and runs it over many source tables in one pass. WebGPU runs on Metal (macOS), Vulkan and Direct3D 12. |
| First scope | The runtime, the toggle template in C# and in F#, and the JSON search demo. |

## Solution layout

```txt
src/Sharpwired.Core/        Wire, Circuit, instructions, macros, inference, compiler, evaluators, backprop
src/Sharpwired.Cells/       SourceTable and cell declarations (constant, input, event, state, oneHot, list)
src/Sharpwired.Timing/      host timing: frame planning, spring matrices, settle checks (float64, no operations)
src/Sharpwired.Annotations/ the attributes ([Handwired], [Range], ...) and Hw, with no reference: a plain method runs without the translator
src/Sharpwired.IL/          the IL translator: [Handwired] methods to Circuit
src/Sharpwired.Gpu/         the WebGPU backend: WGSL writers and the batch runner
src/Sharpwired.Raylib/      the Raylib host loop: input queue, frame clock, output to draw calls
samples/Toggle.Builder/     the toggle switch with the builder API, its machine and its window app (a library)
samples/Toggle.CSharp/      the toggle switch as a plain C# method, and the program that opens its Raylib window
samples/Toggle.FSharp/      the same switch as a plain F# function (a library that references only Sharpwired.Annotations)
samples/JsonSearch/         the JSON highlighter and the regex search over 100,000 documents, and the window app
samples/JsonSearch.App/     the program that opens the JSON search window, or writes the bench table
tests/                      xUnit tests: ports of handwired's tests, differential and seeded random tests
```

Target framework: .NET 10 (SDK 10.0.302 is installed).

## Layer 1: the core runtime

The core is a direct port of `numeric-ui-core.ts`, `cells.ts`, `host-timing.ts` and `prefix-networks.ts`. The port keeps the four instructions and their FP32 rules:

```txt
affine(left)       = left
reglu(left, gate)  = f(left * max(0, gate))
boolean(score)     = f(f(f(score) + 8_388_609) - 8_388_609)
index(form, ...)   = values[candidates[floor(form)]], with a rule outside the table
```

`f(x)` is a cast to `float`. RyuJIT computes `float` operations in IEEE single precision with SSE, and it does not contract `a * b + c` into an FMA (fused multiply-add) operation. The reference evaluator therefore uses ordinary `float` arithmetic. A test suite checks this property, because the exactness of every macro depends on it.

`Wire` is a `readonly record struct` that holds an `int`. A plain `int` does not convert to a `Wire`, so the type system prevents the error that handwired describes (a number where a wire is expected).

The macros keep handwired's names and contracts in .NET style: `Circuit.Compare`, `Circuit.Select`, `Circuit.Multiply`, `Circuit.MinimumExact`, `Circuit.FirstActive`, `Circuit.Argmax`, `Circuit.ApplySpringMatrix`, `Circuit.AnimatedObject`. Each macro has the same exactness contract as in handwired's `docs/execution-model.md`. Each contract has a ported test.

## Layer 2: the IL translator

The translator reads the IL of a method marked `[Handwired]` and emits builder calls on a `Circuit`. The plain method stays a normal .NET method. It runs directly as the reference implementation, so each program has a reference without extra work.

### Input contract

```csharp
[Handwired]
public static (ToggleState Next, ToggleOutput Output) Step(ToggleState state, ToggleInput input)
```

- Parameter and field types: `float`, `int`, `bool`, structs and records of these types, and fixed-length arrays.
- `float` maps to a wire. `int` maps to a wire with grid 1 and a declared range. `bool` maps to an exact bit.
- Attributes declare ranges and grids: `[Range(0, 16384)]`, `[Grid(1.0 / 64)]`, `[Event]`, `[State]`, `[OneHot]`. `[Initial(1280)]` sets the initial value of a cell. A cell with no `[Initial]` starts at the value of its range and grid nearest to 0. `[FixedLength(4)]` gives the length of a `float[]`, because the type has no length. `[MaxIterations(N)]` on a method gives the maximum of a loop. `[Associative]` on a `[Handwired]` combine method lets the translator use it in a prefix network. The translator refuses a value with no finite range where a macro needs one, for example a comparison operand.
- The translator reads an attribute from the property, from its backing field, or from the primary constructor parameter with the same name. So a C# positional record and an F# record field both take attributes.
- The first parameter is the state. The method returns the next state and the output as a `ValueTuple`. An F# function returns `struct (next, output)`. This maps to handwired's cells: state fields become state cells, input fields become input cells or event cells.
- The cells come in a walk: the parameters in order, then the public instance properties of each type in declaration order. A cell name is the path of property names with a lower-case first letter, for example `down.x`, `thumb.displacement` or `springMatrix[0]`. The output leaves are the outputs, in walk order.

The entry points of `HandwiredTranslator`:

- `TranslateStep(method)`: the input contract above. The result has the `FinishedTable` and the cells and outputs by path. It also has an evaluator of the program, and an evaluator that runs the plain method on the same source table.
- `TranslateFunction(method)`: a static method with any parameters. Each parameter leaf is an input or event cell. Each return leaf is an output.
- `TranslateRecurrent(method)`: a method with one loop with no maximum (step 4). The result has a prefix program, a step program and a suffix program. `RecurrentRunner.Run(arguments, stepLimit)` evaluates them on the CPU.

### Translation steps

1. Decode the IL with `System.Reflection.Metadata`. Read sequence points from the PDB (program database) file for error messages.
2. Build the control-flow graph and convert the evaluation stack to SSA (static single assignment) form.
3. Inline calls to other `[Handwired]` methods and to known pure methods. The callee must be static and not generic. The translator refuses recursion. The value of an inlined call emits at the call. So a helper result becomes one wire, as a builder local function returns one wire.
4. Lower each loop. A network has a fixed number of operations in one evaluation and cannot stop early. The translator therefore uses one of three forms:
   - **Constant trip count:** unroll the body.
   - **Runtime trip count with a maximum:** the count `n` has a declared maximum `N`, from `[Range]` or `[MaxIterations]`. Unroll the body `N` times. Each copy computes the bit `i < n`, and a select keeps the old loop state when the bit is 0. The cost is always `N` copies of the body. The translator finds `N` from the declared range of `n` by a refinement of its interval on the active path. With `[MaxIterations]`, the active bit after the last copy is an overflow bit, and the evaluator checks that it is 0.
   - **Runtime trip count with no maximum:** the loop body becomes a separate step program, a recurrent net. The loop state is the step's state cells, and the loop condition is a `done` output bit. The runner evaluates the step until `done` is 1 or until a configured limit. On the GPU, the kernel loops over the step. A lane that is done stays idle until all lanes in its group are done. `TranslateRecurrent` splits the method into three programs. The prefix computes the initial loop state and the values that the loop reads. The step follows, and the suffix computes the return value. The first version accepts one such loop in a method, at the top level. The range of each state cell is a fixed point of at most 8 rounds, with a wide range when the rounds do not stop.

   The translator refuses a loop with two exits (a `return` in the body). It accepts a `break` to the end of the loop. The translator also recognizes the handwired patterns where they cost less than an unrolled loop. A running total with an associative combine gives a Sklansky prefix network, which has fewer layers. The combine is `+`, `Math.Min`, `Math.Max`, `&&`, `||`, or an `[Associative]` method. `floor(x / step)`, an `int` division and an `int` remainder with at most 64 results give a threshold count. With more results and an integer `x`, they give an index read over a table of results. `TranslationOptions.RecognizePatterns = false` turns the recognition off.
5. Convert branches to selects: each phi node becomes a gate `ITE(c, on, off)` on the branch condition of its immediate dominator. Both sides of each branch are computed. The gate rules simplify a gate before the emit:
   - A bit gate with a constant side becomes an AND, an OR or a NOT. Nested AND and OR values become one AND or OR with all the literals, so `a && b && c` gives one operation.
   - A gate of two constants gives one affine form when the form gives both constants exactly. `c ? x : 0` gives `Multiply(c, x)`.
   - Each other gate gives `Circuit.Select(c, off, on)`.
   - A gate returns exactly one side, up to the sign of zero. So the translator declares the union of the side ranges on the result, and the grid of both sides. Without the declaration, the inferred range of `Select` grows in each loop copy.
6. Lower each operation:
   - `+`, `-`, constant `*`, and `/` by a power of two: `affine`. An exact affine form (FP32 coefficients and a grid) is one operation. Otherwise each IL instruction gives one rounded affine operation, so the program rounds where C# rounds.
   - `*` of two wires: `Multiply`.
   - `<`, `<=`, `>`, `>=`, `==`: `Compare`, which uses the declared ranges and grids. `int != 0` gives `NOT(Compare(x, Equal, 0))`, for the value form and for the branch form.
   - `&&`, `||`, `!` on bits: the AND, OR and NOT bit constructions.
   - `Math.Min`, `Math.Max`, `Math.Clamp`: the exact versions.
   - `array[i]` with a computed `i`: `indexRead`.
   - Calls to `Hw.*` intrinsics, for example `Hw.AnimatedObject`: the matching macro.
7. Refuse every other instruction with a message that names the source line, for example a heap allocation, a virtual call, or an exception. The form is `<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>`. `TranslationException` derives from `ArgumentException`. The program computes in FP32 and must give the value of the plain method. So the translator also refuses `double` arithmetic, `int` division with no threshold count, a division by a runtime value, and `MathF.FusedMultiplyAdd`.

The emit order follows the IL order of the blocks:

- A `Compare`, a `Multiply`, a known call, an `Hw` call, an index read and the result of an inlined call emit at their IL instruction.
- An affine form with two or more uses emits at its definition. An affine form with one use emits at its reader.
- A gate emits at its merge block when an IL operation reads it, or when it has two or more readers. A gate with one reader that is a branch or a phi emits at that reader. That gate is a partial result of an `&&` or `||` chain, and the reader merges it into one AND or OR.

With these rules, the C# toggle and the F# toggle give the operation list of the builder toggle in Debug and in Release. The operation equality with a builder program needs two conditions. The plain method has its statements in the order of the builder statements. Each `&&` chain has its operands in the order of the builder `All` call.

### The rules of phase 6

Phase 6 adds five rules for the step methods of the JSON search. Rules 2 to 5 follow the reads of handwired's `json-step.ts` and `data-program.ts`. `TranslationOptions.RecognizePatterns = false` turns rules 2 to 5 off, and then each construct has its phase 4 form.

1. A `bool` that the program reads as a number, for example `b ? 1 : 0` in an `int` sum, is the bit wire with no operation. The emitter declares the grid 1 and the bit on the wire. An `int` form that reads the bit stays exact. A bit is also an index: `t[b ? 1 : 0]` is one index read at the bit. Rule 1 is not under `RecognizePatterns`, because the phase 4 form of an `int` form that reads such a bit was a refusal.
2. `t[Math.Clamp(i, 0, t.Length - 1)]` is one index read at `i` with the clamp rule on both sides. A clamp to other bounds, or a clamp result that a second reader reads, keeps `ClampExact` and the read of its result.
3. `Hw.ReadOrZero(t, i)` is one index read with the zero rule on both sides. An index outside the table gives `+0`.
4. `bit && t[i]`, with `t` a `bool[]` and `i` proven inside the table, is one index read at `i + n × (1 - bit)` with the zero rule above the table. An `int[]` table keeps the gate, because a whole value is no bit.
5. An index read registers only the candidates that the range of its index form reaches, and the form gets the offset of the first candidate as its bias. A read at `k × 17 + c` has a candidate table of 17 wires.

Limit: a local array cannot be the state of a step program of `TranslateRecurrent`. The translator refuses a store into a local array in the step pass, and a choice between two local arrays. No program of phase 6 needs such a state.

### F# notes

F# compiles `if`, `match` on constants, records and tuples to IL that the steps above accept. F# closures and `seq` expressions compile to classes and state machines. The translator refuses them in the first version. F# inlined functions (`inline`) are already expanded in the IL.

The F# compiler can inline a small function in Release and keep the call in Debug. Then the values stay equal, but the operation lists differ. An F# `[<Handwired>]` helper therefore has `[<MethodImpl(MethodImplOptions.NoInlining)>]`. The F# IL differs from the C# IL for the same statements. F# puts the then side of an `if` first. It writes `&&` as branch chains where Roslyn writes the IL `and`, and it reads a `bool` local with a branch. The gate rules and the emit order above give the same operations from both forms.

### Intrinsics

The `Hw` static class holds the operations that have no plain C# form. Examples are a spring step with a host-written matrix, a settle check, and a one-hot selection. Each `Hw` method has a plain .NET body with the same FP32 semantics, so the method still runs as the reference. The first version has `Hw.AnimatedObject`, `Hw.ApplySpringMatrix`, `Hw.Relu`, `Hw.Boolean` and `Hw.FirstActive`. `Hw` and the attributes are in `Sharpwired.Annotations`, which has no reference, so a plain method needs no translator to run.

## Layer 3: execution

- `ReferenceEvaluator`: one operation at a time, in wire order. It defines what a program computes.
- `TensorEvaluator`: the compiled program in layers of sparse matrices on the CPU, with gather lowering and index reads.
- `GpuEvaluator` (WebGPU): a WGSL compute shader written from the compiled program, over a batch of source tables. Each thread evaluates one source table. Two writers, as in handwired's JSON search: one writes each operation as one `f32` statement, and one holds proven whole numbers as `i32` values and proven bits as `u32` values.
- `CpuBatchEvaluator`: the same batch contract (`IBatchEvaluator`) on the CPU, with one `TensorEvaluator` for each worker of a parallel loop. `BatchEvaluators.Create` gives the GPU evaluator when the caller gives a device, and the CPU evaluator otherwise.
- `GpuRecurrentRunner`: the recurrent step form of phase 4 on the GPU (see below).

Why not ILGPU: ILGPU has CUDA, OpenCL and CPU accelerators. The development machine is an Apple M5 Pro, which has no CUDA, and Apple deprecated OpenCL on macOS. ILGPU would run on the CPU accelerator there. I did not verify the current ILGPU backend list.

### The WebGPU packages

`src/Sharpwired.Gpu` references three packages, version 2.23.0 (the newest version on nuget.org at the start of phase 5):

| Package | Content |
| --- | --- |
| `Silk.NET.WebGPU` | The `webgpu.h` bindings. .NET 10 uses the `net5.0` asset. |
| `Silk.NET.WebGPU.Native.WGPU` | The wgpu-native library for macOS, Linux and Windows. The build is wgpu-native commit `94605142`. |
| `Silk.NET.WebGPU.Extensions.WGPU` | The wgpu-native extensions: `InstanceEnumerateAdapters`, `DevicePoll`, `SetLogCallback`. |

Only `GpuDevice.cs` and `ComputeKernel.cs` call the native API. No code outside `src/Sharpwired.Gpu` uses the namespace `Silk.NET`.

### Probe facts

A probe program and the device tests measured these facts on the Apple M5 Pro (Metal backend) with this wgpu-native:

- The default uncaptured error handler of wgpu-native aborts the process on a validation error. So `GpuDevice` installs its own callback as the first call on a new device, and creates each shader module, pipeline, bind group and buffer inside an error scope. The scope message holds the wgpu detail ("Caused by: ...").
- An error in `QueueSubmit` aborts the process. For example, a submit that uses a buffer with a pending map aborts. So a map request that does not complete marks the device unusable, and each later call throws.
- The automatic pipeline layout drops each binding that the shader does not use. So `ComputeKernel` creates an explicit bind group layout.
- wgpu-native gives the address of a released buffer to the next new buffer. So a kernel compares a generation number for each storage buffer to decide if its bind group is out of date. A compare of the native address gave the values of a released buffer after two grows.
- This wgpu-native does not turn Metal fast math off (the library has `setPreserveInvariance:` and no `setFastMathEnabled:` or `setMathMode:`). Without keep, the GPU folded `(x + 8388609) - 8388609` to `x` (60 of 64 values differ), contracted `x * b + c` to a fused multiply-add (17 of 64), gave `+0` for `-abs(x) * 0` (64 of 64), gave 0 for `max(0, NaN)`, and folded `x != x` to false.
- The GPU writes 0 in place of an FP32 result whose magnitude is below 2^-126 (subnormal flush). No WGSL form stops it.

### The keep function

Each shader starts with the keep function `k(v) = bitcast<f32>(bitcast<u32>(v) ^ params.zero)`. `params.zero` is 0 in a read-only storage buffer, and the shader compiler cannot know the value. So `k(v)` returns `v` with the same bits, and the compiler cannot move arithmetic across it. The f32 writer puts keep around each product, each partial sum and each literal. An affine row is the left fold of the reference: `value = k(bias)`, then `value = k(value + k(k(w) * x))` for each term. The boolean atom is `k(k(S + k(8388609.0)) - k(8388609.0))`. NaN tests use the bits, and `relu` keeps NaN and `+0` for a `-0` gate.

Limit: WGSL §15.7.5 blocks the reorder and the fusion of operations across `k`, so the keep form gives the stored FP32 order on each implementation. WGSL §15.7.2 lets an implementation assume that no NaN and no infinity occur, and does not guarantee `-0`. So `WgslFormTests` and `RoundingPointTests` measure the results with NaN, infinities and `-0` on each adapter. The specification does not guarantee them.

### The subnormal limit

`GpuDevice.FlushesSubnormals` reports the subnormal flush. `GpuDevice.Open` measures it with one kernel: `k(x * k(2^-10))` with `x = 2^-120`. On a device that flushes, the f32 writer is not exact for an evaluation that holds a nonzero value of magnitude below 2^-126 in an f32 row. A sign cascade over a difference below 2^-126 then returns the bit of equal operands, as handwired's execution-model.md states. On such a device, the tests compare each evaluation with no subnormal value, and count the other evaluations. The Apple M5 Pro flushes.

### The two writers

`WgslFloatWriter` writes each operation as one `f32` statement with keep. With it, `GpuEvaluator` gives the reference bits (`Object.is`), apart from the subnormal limit. A program with gather lowering can give the other sign of an exact zero. A source table that breaks a one-hot declaration is outside the contract: a gather then reads one candidate, as `TensorEvaluator` does, and the value can differ from the reference evaluator.

`WgslIntegerWriter`, the default, holds a proven bit as a `u32` that is 0 or 1, and a proven whole number as an `i32`. It writes each other operation as the f32 writer does, and a whole value that an f32 row reads is `k(f32(w))`. `IntegerProof.Prove` gives one proof for each packed position:

- A source cell is a bit when the circuit declares it an exact bit. It is whole on a grid of 1 or more with a range inside ±2^24. The integer evaluator checks the grid and the bit declarations of each source table.
- An affine row or a ReGLU row is whole when its operands, its bias and its weights are whole, and the bound of each product and partial sum is at most 2^24. FP32 computes such a value with no rounding, in any order, so the `i32` statement gives the value of the `f32` statement.
- A boolean atom, an index read with an `f32` index, and the bit of `Compare` are proven only when the cone of the row is finite: each wire of the cone has a finite magnitude bound. Range inference does not follow NaN, and NaN has no integer. This is the finite-cone rule.
- A masked gather read is whole only when each member of its one-hot group is a proven bit. A one-hot declaration is not a proof, because no evaluator checks it. A finite cone alone is not enough: a member can hold 0.5.
- A grid or a bit that the caller declares on an operation is not a proof. The bit of `Compare` is the one exception: the rule trusts the declarations that `Compare` used.

An `i32` has no `-0`, so the integer writer can give `+0` where the reference gives `-0` (sameNumericValue). The packed words of handwired's bit-emit.ts (bits of one `u32`, `packWords`) are in the loop writers of phase 6 (see "Loop programs").

### The batch runner

One invocation evaluates one source table. Workgroup size 64. A batch splits into chunks that fit the buffer limits of the device. `GpuDevice.Open` requests the adapter maximum of the buffer limits. `GpuEvaluator` checks each source table on the CPU, as `TensorEvaluator` does, before it takes the lock of the device. All GPU calls of one device run one at a time under this lock. A root that the program does not hold gives NaN.

### The recurrent step on the GPU

`GpuRecurrentRunner` runs a `RecurrentFunction` of `TranslateRecurrent`. A batch splits into chunks that fit the buffer limits. For each chunk, the prefix and the first step dispatch run in one command buffer. The step kernel holds each state cell in a `var` and loops over the step program for each lane. It runs at most `GpuRecurrentOptions.StepsPerDispatch` evaluations of each lane in one dispatch (default 1,024, at least 1), because some systems stop a GPU command that runs too long. No test on this machine showed such a stop. The host reads the status of each lane after each dispatch, and submits one more step dispatch while a lane runs. The suffix then runs one time for each chunk, after the last step dispatch. An internal counter of the suffix dispatches checks this.

The step kernel checks each cell in the kernel with the declarations of the step table: finite, within the range, an exact bit, a multiple of the grid, and the one-hot rules. It checks the cells of the first source table, and each next value before the done test. So a value past a cell range, also the range ±2^23 of a wide `int` cell, stops the lane. It is never a silent wrong value. For a lane that fails, the host replays the lane on the CPU with `RecurrentRunner`. A CPU check that fails gives the lane the status `Invalid`, NaN outputs, 0 steps and the CPU message, and the other lanes keep their results. `RecurrentBatch.ThrowIfInvalid` throws the message of phase 5 for the first invalid lane. A CPU replay that passes gives the lane the CPU result. An internal counter `ReplayPasses` counts such lanes, and each GPU test asserts 0, apart from the lanes that the subnormal rule excludes. So a kernel check that fails falsely cannot hide behind the replay. Each lane equals `RecurrentRunner.Run`: the same steps, the same `Completed`, and the same outputs, with the writer limits above.

`CpuRecurrentRunner` runs the same batch contract (`IRecurrentBatchRunner`) on the CPU, with blocks of lanes in a parallel loop. `RecurrentRunners.Create` gives the GPU runner for a device and the CPU runner otherwise. The status of each lane is `Completed`, `Limit` or `Invalid`.

### Loop programs

Phase 6 adds the loop program of handwired's `loop-program.ts`. A `LoopProgram` runs translated step methods over the positions of each document. Each stage is one `TranslateFunction` result, and it runs one time, forward over the positions, or backward. For each input cell, the stage names what the cell reads: the item of a buffer at the position, one fixed document cell, a register, a component of a field, or a shared cell. For each output, it names what it writes: a register, a field component, a document output, or a position output. A register carries a value from one position to the next. A field holds values for each position, which one stage writes and a later stage reads. `LoopProgram.Create` refuses a bad program with `ArgumentException`, for example a field read before a stage writes it, or a stage that writes some but not all components of a field of bits. `LoopProgram.EvaluatePlain` runs the plain step methods in a C# loop, as the reference of the translation.

An `ILoopBackend` keeps document buffers and makes runners. A runner gets the shared cells one time, and then runs a request: the input buffers, the document count, a target buffer for each position group, and a window of documents whose position values come back. A document that fails a cell check gets a `LaneFault` with the message of the check, and NaN outputs. The other documents keep their values. A backend refuses one buffer as the target of two position groups.

- `CpuLoopBackend` runs blocks of documents in a parallel loop, and stops at a cancellation between blocks. Its default stage backend is `CompiledStage`: the rows of a stage as .NET delegates (System.Linq.Expressions), at most 2,000 operations in one delegate. Each value is equal by Object.is to the value of `TensorEvaluator`. In Release on the Apple M5 Pro, one evaluation was 1.9 to 7.5 times faster than `TensorEvaluator` less its range check (Reverse32 1.9, Forward32 2.8, Reverse8 3.6). `TensorEvaluator` stays a test option, and a failed range check of it also gives a `LaneFault`.
- `GpuLoopBackend` keeps the document buffers on the device, so a position group that one runner writes can be the input of a second runner with no readback. One invocation runs one document: the WGSL has a `for` loop for each looped stage, the registers in `var` values, a field in a function array, and a field of bits in one `u32` word for each position. The constant tables go into the shared storage buffer. A run splits into chunks of at most 65,535 workgroups of 64 invocations. The kernel checks each cell as the CPU runner does and writes a status for each document. The host replays each document with status 1 on the CPU runner, which gives the fault message, or the values when it finds no fault. An internal counter `ReplayPasses` counts the replays that pass, and the GPU tests assert 0. A cancelled run writes NaN for each document that it did not reach, with one fill for each output.
- `LoopFloatWriter` writes each row as `f32` statements with keep, as `WgslFloatWriter` does. `LoopIntegerWriter` ports the rules of handwired's `bit-emit.ts`: sums and gates of proven bits and whole numbers as `u32` and `i32` statements, comparisons, reads, packed reads (gated reads of a bit table as bits of `u32` words, which `PackedWords.Pack` writes one time for each `WriteShared`), and lanes (a list of bits stored together, for example the registers of bits that the same stages write, as one word, so that one word operation computes the same gate for each bit). A register value that reads another register goes into a `let` before the first register write, so that it reads the value of the previous position.

### Result

The rounding-point test (`RoundingPointTests`) runs the boolean atom, a round chain, a sign cascade and a fused multiply-add program on each adapter with both writers. Each output equals the reference evaluator, apart from the subnormal limit. Metal fast math stays on, and the keep form gives the reference bits. On the Apple M5 Pro, the 173 tests of `Sharpwired.Gpu.Tests` ran in 36 s in Debug and in 19 s in Release (2026-10-18).

## Layer 4: the hosts and samples

`Sharpwired.Raylib` holds the frame loop. Input handlers queue events with timestamps. Each frame calls `AdvanceFrame`, which plans the evaluations: one per event timestamp, a settle check each 64 ms of simulated time, and one at the frame time. The host then draws from the output numbers only.

- **Toggle**: the template switch, written once as a C# method and once as an F# function. The test compares the translated program with the plain method over a scripted trace of events, as handwired's `toggle.test.ts` does.
- **JSON search**: the highlighter and the regex search as `[Handwired]` step methods over one position of a document, which a `LoopProgram` runs over the positions (phase 6 decision 1). The GPU backend runs them over 100,000 generated documents. The Raylib window shows the pattern field, the match map and one document. The CPU path runs the same loop programs in parallel over document blocks.

### The JSON search

`samples/JsonSearch` is a library, and `samples/JsonSearch.App` opens the window or writes the bench table (`--bench`).

- **Documents.** `Documents.Generate` ports handwired's `documents.ts`: the hash `lowbias32`, 7 shapes, 4 styles and 16 defects, at most 512 code points. The texts are equal to the handwired texts: the `lowbias32` hash of the UTF-16 code units of the first 2,000 texts is 3,798,448,091, the value that handwired's `json-search.test.ts` pins. `DocumentCells` writes the first 512 code points and the length of each text as the cells of one document.
- **Highlighter.** `HighlighterStep.Step` runs at the positions 0 to 511 and gives the display kind of each code point. `HighlighterStep.End` runs one time for position 512 and gives whether the text is valid, the position and the kind of the first error, and whether the text is too long. Step has 40 live operations that compute, with 12 index reads, and End has 18, with 6 index reads (handwired: 37 and 17). Each error-state bit is a comparison of 2 operations (handwired: 1 ReGLU), and the first error kind is 1 operation more.
- **Search.** `PatternParser`, `Automaton` and `PatternCompiler` turn a pattern into the cells of an automaton. The search programs hold 8, 16 and 32 states. `SearchStart`, `Reverse` and `Forward` are step methods for each capacity, and the classes differ only in the constant of the states. Reverse has 151, 359 and 1,159 live operations that compute, and Forward 96, 312 and 1,128. The integer writer gives 229, 301 and 445 statements (handwired: 147, 171 and 219), with 128, 512 and 2,048 packed reads.
- **Searcher.** `Searcher.Create` writes the cells, uploads them, makes the highlighter runner and the three search runners, and runs the highlighter one time. The display kinds stay on the backend. `Search` compiles the pattern, picks the smallest capacity that holds it, writes the shared cells and runs the search. Only the newest request gets a result. On the CPU, the documents are in slices of 16,384, so a search reports its progress and stops at a cancellation. The times start after the wait for the evaluation lock.
- **.NET baseline.** `DotNetBaseline` translates the pattern for .NET `Regex` (code points as surrogate pairs) and counts the matches on one thread, with a limit of 10 s.
- **Window.** `JsonSearchApp` (1,280 × 860 px, "JSON search") draws the pattern field, the scope toggle (All, Keys, String values), the backend toggle (GPU, CPU), the four examples, the line of counts and times, the match map and one document with the colors of the display kinds and a mark under each match. It makes no Raylib call. `SearchService` has three worker threads: the search, the document, and the .NET `Regex` timing. Each thread runs only the newest job, and each answer carries the ID of its request. The window loads Monaco and Arial Unicode on macOS, reads the character map of each font file (`FontCoverage`), and draws `?` for a code point that no font has.

The bench on the Apple M5 Pro (Metal), 100,000 documents, Release, 2026-10-19: the GPU search took 12.9 to 14.8 ms, the CPU search on all cores 850 to 2,231 ms, and .NET `Regex` on one thread 12.5 to 302 ms. Each of the 24 rows has equal totals for the program and .NET `Regex`, and the tests compare the count of each document. The GPU searcher of 100,000 documents took 2,177 ms to make, and the CPU searcher 1,336 ms.

## Order of work

Each phase ends with passing tests.

1. Core runtime: `Wire`, `Circuit`, the four instructions, the reference evaluator, range and grid inference, the macros, and their contract tests.
2. Compiler and `TensorEvaluator`, with differential tests against the reference evaluator (seeded random programs).
3. Cells, host timing, and the toggle written with the builder API. A Raylib window shows it.
4. The IL translator. Toggle in plain C# and F#. The translated programs equal the builder-API program operation for operation.
5. The WebGPU backend, and the rounding-point test on each adapter.
6. The JSON search demo.

## Open items

- License: handwired is MIT. Code ported from it must keep its copyright notice. Add a `NOTICE` file with the handwired notice before the first port.
- GPU exactness under Metal fast math: resolved in phase 5. This wgpu-native keeps Metal fast math on. The keep function gives the reference bits for each rounding point on the Apple M5 Pro, with both writers. The subnormal flush stays a documented limit (Layer 3, "The subnormal limit").
- Range attributes on nested structs: the first version takes ranges only from attributes. Range inference across inlined calls comes from the core inference.
