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
src/Sharpwired.IL/          the IL translator: [Handwired] methods to Circuit
src/Sharpwired.Gpu/         the WebGPU backend: WGSL writers and the batch runner
src/Sharpwired.Raylib/      the Raylib host loop: input queue, frame clock, output to draw calls
samples/Toggle.Builder/     the toggle switch with the builder API, its machine and its window app (a library)
samples/Toggle.CSharp/      the toggle switch as a plain C# method, and the program that opens its Raylib window
samples/Toggle.FSharp/      the same switch as a plain F# function
samples/JsonSearch/         the JSON highlighter and the regex search over 100,000 documents
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
- Attributes declare ranges and grids: `[Range(0, 16384)]`, `[Grid(1.0 / 64)]`, `[Event]`, `[State]`, `[OneHot]`. The translator refuses a value with no finite range where a macro needs one, for example a comparison operand.
- The first parameter is the state. The method returns the next state and the output. This maps to handwired's cells: state fields become state cells, input fields become input cells or event cells.

### Translation steps

1. Decode the IL with `System.Reflection.Metadata`. Read sequence points from the PDB (program database) file for error messages.
2. Build the control-flow graph and convert the evaluation stack to SSA (static single assignment) form.
3. Inline calls to other `[Handwired]` methods and to known pure methods.
4. Lower each loop. A network has a fixed number of operations in one evaluation and cannot stop early. The translator therefore uses one of three forms:
   - **Constant trip count:** unroll the body.
   - **Runtime trip count with a maximum:** the count `n` has a declared maximum `N`, from `[Range]` or `[MaxIterations]`. Unroll the body `N` times. Each copy computes the bit `i < n`, and a select keeps the old loop state when the bit is 0. The cost is always `N` copies of the body.
   - **Runtime trip count with no maximum:** the loop body becomes a separate step program, a recurrent net. The loop state is the step's state cells, and the loop condition is a `done` output bit. The runner evaluates the step until `done` is 1 or until a configured limit. On the GPU, the kernel loops over the step. A lane that is done stays idle until all lanes in its group are done.

   The translator also recognizes the handwired patterns where they cost less than an unrolled loop, for example a prefix network for running totals and a threshold count for `floor(x / step)`.
5. Convert branches to selects: each phi node becomes `Circuit.Select` on the branch condition. Both sides of each branch are computed.
6. Lower each operation:
   - `+`, `-`, constant `*` and `/`: `affine`.
   - `*` of two wires: `Multiply`.
   - `<`, `<=`, `>`, `>=`, `==`: `Compare`, which uses the declared ranges and grids.
   - `&&`, `||`, `!` on bits: the AND, OR and NOT bit constructions.
   - `Math.Min`, `Math.Max`, `Math.Clamp`: the exact versions.
   - `array[i]` with a computed `i`: `indexRead`.
   - Calls to `Hw.*` intrinsics, for example `Hw.AnimatedObject`: the matching macro.
7. Refuse every other instruction with a message that names the source line, for example a heap allocation, a virtual call, or an exception.

### F# notes

F# compiles `if`, `match` on constants, records and tuples to IL that the steps above accept. F# closures and `seq` expressions compile to classes and state machines. The translator refuses them in the first version. F# inlined functions (`inline`) are already expanded in the IL.

### Intrinsics

The `Hw` static class holds the operations that have no plain C# form, for example a spring step with a host-written matrix, a settle check, and a one-hot selection. Each `Hw` method has a plain .NET body with the same FP32 semantics, so the method still runs as the reference.

## Layer 3: execution

- `ReferenceEvaluator`: one operation at a time, in wire order. It defines what a program computes.
- `TensorEvaluator`: the compiled program in layers of sparse matrices on the CPU, with gather lowering and index reads.
- `GpuEvaluator` (WebGPU): a WGSL compute shader written from the compiled program, over a batch of source tables. Each thread evaluates one source table. Two writers, as in handwired's JSON search: one writes each operation as one `f32` statement, and one holds proven integers and bits as `u32` values or as bits of a word.

Why not ILGPU: ILGPU has CUDA, OpenCL and CPU accelerators. The development machine is an Apple M5 Pro, which has no CUDA, and Apple deprecated OpenCL on macOS. ILGPU would run on the CPU accelerator there. I did not verify the current ILGPU backend list.

Known problem: a shader compiler can simplify `(x + K) - K` to `x`, which breaks the boolean atom and round chains. Metal can compile with fast math, which allows this. The `u32` writer is exact under any float mode, because integer arithmetic does not depend on it. The `f32` writer must keep the stored FP32 evaluation at each rounding point. I did not verify which math mode wgpu selects for Metal. The first GPU task is a test that runs the boolean atom, a round chain and a sign cascade on each WebGPU adapter and compares the results with the reference evaluator.

## Layer 4: the hosts and samples

`Sharpwired.Raylib` holds the frame loop. Input handlers queue events with timestamps. Each frame calls `AdvanceFrame`, which plans the evaluations: one per event timestamp, a settle check each 64 ms of simulated time, and one at the frame time. The host then draws from the output numbers only.

- **Toggle**: the template switch, written once as a C# method and once as an F# function. The test compares the translated program with the plain method over a scripted trace of events, as handwired's `toggle.test.ts` does.
- **JSON search**: the highlighter and the regex search as `[Handwired]` methods over the characters of a document. The GPU backend runs them over 100,000 generated documents. The Raylib window shows the pattern field, the match map and one document. The CPU path runs the compiled program in parallel over document blocks.

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
- GPU exactness under Metal fast math: see the known problem in Layer 3.
- Range attributes on nested structs: the first version takes ranges only from attributes. Range inference across inlined calls comes from the core inference.
