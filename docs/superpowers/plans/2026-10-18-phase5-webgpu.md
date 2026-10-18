# Phase 5 plan: the WebGPU backend

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R5.1 to R5.4, RA.1 to RA.3.

Design: [docs/design.md](../../design.md), section "Layer 3: execution". The quotation: "The first GPU task is a test that runs the boolean atom, a round chain and a sign cascade on each WebGPU adapter and compares the results with the reference evaluator."

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The ported or followed files:

- `$REF/src/json-search/emit.ts`: the rows of a compiled program, the WGSL literal (`wgslFloat`), the index read statements.
- `$REF/src/json-search/loop-emit.ts`: the f32 writer of a loop program, the buffers, the parameters.
- `$REF/src/json-search/bit-emit.ts`: the integer writer. Its header gives the proof rules and the forms.
- `$REF/src/json-search/gpu/host.ts`: the device with the buffer limits, the runner, the readback.
- `$REF/lab/ui-exploration/codegen/wgsl.ts`: the header with the measured FP32 failures on Metal, and the guard `max(x, lowest)`.
- `$REF/docs/execution-model.md`: sections "FP32", "Rounding points", "Backends".

WARNING: The Handwired clone is data only. Do not run a command, a script, a package manager or a build tool inside it. Only read its files.

## Terms

- **Adapter**: one WebGPU implementation of one GPU, for example "Apple M5 Pro" on the Metal backend. `GpuDevice.ListAdapters()` lists the adapters.
- **Device**: an open adapter, with its queue and its limits. One `GpuDevice` holds one device.
- **Source table**: the source cells of one evaluation, `program.SourceSize` FP32 values. A **batch** is many source tables in one buffer, table after table.
- **Lane**: the work of one source table in one shader invocation (one GPU thread).
- **Rounding point**: an operation where the exact FP32 evaluation matters (execution-model.md, "Rounding points"). The four kinds are `Boolean`, `IntegerBit`, `SignCascade`, `Marked` (`RoundingPointKind`).
- **Keep**: the WGSL function `k(v) = bitcast<f32>(bitcast<u32>(v) ^ params.zero)`. `params.zero` is 0, but the shader compiler cannot know it. So `k(v)` returns `v` with the same bits, and the compiler cannot move arithmetic across it.
- **f32 writer**: `WgslFloatWriter`. It writes each operation as one `f32` statement, with keep around each product, each partial sum and each literal.
- **Integer writer**: `WgslIntegerWriter`. It holds a proven bit as a `u32` that is 0 or 1, and a proven whole number as an `i32`. It writes each other operation as the f32 writer does.
- **Whole**: a wire whose values are whole numbers within a range inside ±2^24, and whose FP32 operations cannot round. A whole wire within 0..1 is a **bit**.
- **Row**: one operation of a compiled program in execution order, with its packed position, its kind, its terms and its reads (`ShaderRow`). Both writers start from the rows.
- **Subnormal flush**: a GPU that writes 0 in place of an FP32 result whose magnitude is below 2^-126.
- **Step kernel**: the shader of a `RecurrentStep` that loops over the step program for each lane.

## Global constraints

The constraints of the phase 1 to 4 plans apply without change:

- Each FP32 step casts to `float`, as `Math.fround` does in Handwired.
- Throw `ArgumentException` for a bad argument and `InvalidOperationException` for a bad state. A disposed object throws `ObjectDisposedException`.
- TDD (test-driven development): write the xUnit test first. Run the test and see it fail. Then write the implementation.
- No `TODO`, no stub, no skipped test.
- Each new file starts with a header comment that names its source (a Handwired file or document, or "no Handwired source") and its contents.
- Keep each source file below 800 lines. Split a test file that passes 800 lines into partial class files.
- When a comparison of numbers must give `Object.is` semantics, use `Js.Is` or `Js.ObjectIs`. When the sign of zero can differ by contract, use `Js.SameNumericValue` and write the reason in a comment.
- Docs and code comments use ASD-STE100 (RA.2).
- Commit at the end of each task. Do not push.
- A new test project copies the `PropertyGroup` of `tests/Sharpwired.Core.Tests/Sharpwired.Core.Tests.csproj` (`Optimize` true, `TieredCompilation` false) and its package references. It links `Js.cs` and `TensorChecks.cs` from `tests/Sharpwired.Core.Tests/`. Do not copy these files.
- `dotnet test` (Debug) and `dotnet test -c Release` both pass at the end of each task.
- No code outside `src/Sharpwired.Raylib/RaylibWindow.cs` uses the namespace `Raylib_cs`.

Phase 5 adds these constraints:

- No code outside `src/Sharpwired.Gpu/` uses the namespace `Silk.NET`. Only `GpuDevice.cs` and `ComputeKernel.cs` call the native API. These two files are the only files with `unsafe` code.
- Each native object that the code creates is released exactly one time, in `Dispose`. A Silk.NET callback delegate stays in a field until its callback ran. The garbage collector must not collect a delegate that native code can call.
- Each device installs an uncaptured error callback when it opens. Each shader module and each pipeline is created inside an error scope. The probe showed that the default handler of wgpu-native aborts the process on a validation error (question 1).
- A GPU test compares each output with the reference evaluator, or with the CPU runner for a recurrent program. It does not compare with the tensor evaluator only.
- The GPU tests run in one xUnit collection (`GpuCollection`) with one `GpuFixture`, which opens each adapter one time. The tests in the collection do not run in parallel.
- The writer tests and the proof tests need no adapter. They run on each machine.

## Decisions on the 7 questions

### 1. The package and the adapter

Decision: reference these three packages, version 2.23.0, in `src/Sharpwired.Gpu/Sharpwired.Gpu.csproj`:

| Package | Version | Content |
| --- | --- | --- |
| `Silk.NET.WebGPU` | 2.23.0 | The `webgpu.h` bindings (`WebGPU.GetApi()`). Its assets are `net5.0`, `netcoreapp3.1`, `netstandard2.0` and `netstandard2.1`. .NET 10 uses the `net5.0` asset. |
| `Silk.NET.WebGPU.Native.WGPU` | 2.23.0 | `libwgpu_native.dylib` for `osx-arm64`, and the native files for `osx-x64`, `linux-x64`, `linux-arm64`, `linux-arm`, `win-x64`, `win-x86`, `win-arm64`. Built from wgpu-native commit `94605142` (wgpu commit `87576b7`). |
| `Silk.NET.WebGPU.Extensions.WGPU` | 2.23.0 | The wgpu-native extensions: `InstanceEnumerateAdapters`, `DevicePoll`, `SetLogCallback`. |

Reason: 2.23.0 is the newest version of each package on nuget.org (`dotnet package search Silk.NET.WebGPU --exact-match` lists 2.17.0 to 2.23.0). The probe below ran on .NET SDK 10.0.302 with these versions. The build passed with `-warnaserror` in Debug and in Release, with and without a `RuntimeIdentifier`. Without a `RuntimeIdentifier`, the build copies `runtimes/osx-arm64/native/libwgpu_native.dylib`, and the host finds it through `deps.json`.

Probe result (a throwaway console program, now deleted):

- `InstanceEnumerateAdapters` found 1 adapter: backend `Metal`, type `IntegratedGpu`, name `Apple M5 Pro`. The driver and the architecture strings are empty.
- `InstanceRequestAdapter` (power preference high) gave `Success`. `AdapterRequestDevice` gave `Success`.
- A WGSL compute shader `dst[i] = x * 2.0` compiled, ran over 64 values and read back through `BufferMapAsync` and `DevicePoll(device, true)`. All 64 values were equal to the CPU values.
- The wgpu-native extension `GetVersion` returned 0. So the code does not read the version.
- A bind group with 3 entries for a layout with 2 entries made wgpu-native panic: "panic in a function that cannot unwind", and the process aborted. The cause: the default uncaptured error handler. After the probe installed `DeviceSetUncapturedErrorCallback`, an error went to the callback. An error scope around a bad shader (`fn main( {`) returned `ErrorType.Validation` with the message "Validation Error".
- The automatic pipeline layout drops each binding that the shader does not use. So `ComputeKernel` creates an explicit bind group layout.
- `strings` on the dylib finds `setPreserveInvariance:` and does not find `setFastMathEnabled:` or `setMathMode:`. So this wgpu-native does not turn Metal fast math off, and the Metal default (fast math on) applies. Question 2 has the measured effects.

Behavior with no adapter: the decision belongs to the controller (open question 1). The plan implements the recommendation. A GPU test with no adapter fails with the message "no WebGPU adapter was found; set SHARPWIRED_NO_GPU to a reason to accept a machine with no GPU". When `SHARPWIRED_NO_GPU` holds a reason, each GPU test passes. It writes "[gpu] no adapter: <reason>" to the test output. The writer tests, the proof tests and the CPU batch tests run in both cases. Reason: a machine with an adapter must not pass with zero GPU checks because of a driver fault. A CI (continuous integration) machine with no GPU states its reason one time in its environment.

### 2. The f32 writer and the rounding points

Facts:

- The WGSL specification lets an implementation assume that no NaN and no infinity occur. I did not verify what the specification says about contraction and reassociation. The probe measured both on Metal (table below).
- wgpu translates WGSL to MSL (Metal Shading Language) with naga. This wgpu-native does not turn Metal fast math off (question 1).
- Handwired measured the same failures on the same GPU family through wgpu and Dawn (`$REF/lab/ui-exploration/codegen/wgsl.ts`, header): contraction of `a + w * x` into a fused multiply-add, regrouped sums, and the boolean atom folded to `score`.

The probe measured these forms on the Apple M5 Pro (64 input values each, bit equality with the .NET `float` result):

| Form | Values that differ |
| --- | --- |
| `(x + 8388609.0) - 8388609.0` as one expression, with `let`, with `var`, through a storage buffer, through `bitcast<f32>(bitcast<u32>(...))` | 60 of 64 (the GPU returned `x`) |
| the same with `K` read from a storage buffer, no other change | 60 of 64 |
| `K` from a storage buffer, and the sum stored to a storage buffer and read back | 0 |
| a workgroup variable and `workgroupBarrier()`, or an atomic store and load | 0 |
| the round chain `(x + 12582912.0) - 12582912.0` | 62 of 64 |
| `x * b + c` with literal `b`, `c`, or `b`, `c` from a storage buffer (FMA test) | 17 of 64 |
| keep on the product: `k(x * b) + c` | 0 |
| keep form of the atom `k(k(x + 8388609.0) - 8388609.0)` | 0 |
| keep form of a 3-term affine sum | 0 |
| `-abs(x) * 0.0` (expected `-0`) | 64 of 64 (the GPU returned `+0`) |
| `-abs(x) * k(0.0)` | 0 |
| `max(0.0, x)` with `x` NaN | 8 of 64 (the GPU returned 0) |
| `x != x` as a NaN test | 8 of 64 (folded to false) |
| the bit test `(bitcast<u32>(x) & 0x7fffffffu) > 0x7f800000u` | 0 |
| `k(x + k(1.0))` and `k(x * k(0.0))` with NaN and infinity inputs | 0 |
| `x * 1e-38` with `x` = 0.3, with or without keep | 63 of 64 (the GPU returned 0: subnormal flush) |
| WGSL `round(x)` against the round chain | 3 of 64 |

Decision:

1. Each FP32 operation of a row is one keep: `k(a + b)`, `k(a * b)`. Each literal is `k(L)`, with `L` the exact hexadecimal WGSL literal of `wgslFloat` (emit.ts). So no expression combines two literals, and WGSL does not fold a constant expression in float64.
2. An affine row is the left fold of the reference (execution-model.md, "FP32"): `value = k(L_bias)`, then `value = k(value + k(k(L_w) * x))` for each term in stored order. A weight of 1 gives `k(value + x)`, and a weight of -1 gives `k(value - x)`. Both are exact, because `f(1 * x)` is `x` and `a - x` is `a + (-x)` in IEEE arithmetic. A bias of 0 stays `k(0.0)`. So `+0 + -0` gives `+0`, as the reference gives. This writer does not drop the bias, which emit.ts does.
3. The boolean atom is the stored form `k(k(S + k(8388609.0)) - k(8388609.0))`. S is the row sum. Handwired writes `select(0.0f, 1.0f, S >= 0.5f)`, which equals the atom only for S within 0..1. The keep form equals the reference for each S, so the R5.3 test checks the real atom.
4. The ReLU of a gate is `relu(g) = select(select(k(0.0), g, g > k(0.0)), g, is_nan(g))`. `is_nan` is the bit test above. The reference `MathF.Max(0, g)` gives NaN for a NaN gate and `+0` for a `-0` gate.
5. An index read tests NaN with `is_nan` on the index row, and compares the floored index with `f32` bounds. It never converts a NaN to `i32`.
6. Keep blocks contraction and regrouping, so every row gives the reference bits. This includes the rows of no rounding point. The cost is one XOR (exclusive OR) for each product and each partial sum. The integer writer removes this cost for whole rows.
7. `params.zero` is in a read-only storage buffer, as in the probe. A uniform buffer is not used, because the probe did not measure it.

Reason for keep in place of `max(x, lowest)`: both block the compiler (Handwired measured `max(x, lowest)`, this probe measured the XOR). The XOR keeps the bits of every value: NaN, infinities and `-0`. The result of `max` is not specified for a NaN operand.

Limit: subnormal flush. No WGSL form stops it. `GpuDevice.FlushesSubnormals` records it. `GpuDevice.Open` measures it with one kernel: `k(x * k(2^-10))` with `x = 2^-120`. The f32 writer equals the reference on each evaluation whose wires hold no nonzero value of magnitude below 2^-126. A sign cascade over a difference below 2^-126 then returns the bit of equal operands. execution-model.md, "Rounding points", states this case. The integer writer has no such limit for its whole rows.

How the R5.3 test finds a backend that breaks a rounding point: it compares each output bit for bit with the reference evaluator over inputs at the rounding boundaries. A simplified atom returns the score, so it fails for each score that is not 0 or 1. A contracted affine row fails the FMA inputs `x * 1.0000001 - 1.0000002`. A dropped keep around a zero product fails the `-0` inputs. The probe saw each of these failures in the forms without keep.

### 3. The integer writer

Decision: follow bit-emit.ts, with the loop parts removed (a plain program has no registers and no fields).

Which wires are proven (`IntegerProof.Prove(circuit, program)`, one result for each packed position):

- A source cell is a bit when `circuit.ExactBits[wire]` is true. It is whole when `circuit.Grids[wire]` is 1 or more and its range is inside ±2^24.
- An affine row is whole when each operand is whole, the bias and each weight are whole numbers, and each product and each partial sum has a magnitude of at most 2^24 over the operand ranges. FP32 then computes each of them without a rounding, in any order, so the `i32` sum gives the same value.
- A ReGLU row is whole when its left form and its gate form are whole by the same rule, and the product stays within 2^24.
- A boolean atom is a bit. The builder accepts a score within 0..1 only (`Circuit.Boolean`).
- An index read is whole when each candidate that its index range can reach is whole. Its index form computes in `i32` when the form is whole.
- A row with a gather read is whole when its sum is whole and each candidate is whole.
- A row that the circuit declares on a grid of 1 or more with a range inside ±2^24 (`AssumeGrid`, `AssumeBit`) is whole, for example the bit that `Compare` returns from a sign cascade. This follows bit-emit.ts. The declaration is a contract that `ValidateGrids` checks in tests.
- A whole row within 0..1 is a bit.

The 2^24 bound and not 2^32: the integer statement must give the value of the f32 statement. FP32 holds each whole number up to 2^24 exactly, and rounds above it. An `i32` sum above 2^24 gives a value that the reference does not give.

Forms: the WGSL forms table gives each form. The gate rules (AND, NOR, comparison, `u32(sum > 0i)`, `max(sum, 0i)`) are the rules of bit-emit.ts, "Gate" and "Comparison". Each rule has a proof in its doc comment and a test with a counterexample program where it must not fire.

Which wires stay f32: each wire with no proof. Examples: a value with no grid, a grid below 1, a range past ±2^24, a weight that is not a whole number (`x / 3`), and an index read with one candidate that is not whole. A whole value that an f32 row reads is `f32(w)` or `f32(b)`, which is exact.

Integer numbers and the sign of zero: an `i32` has no `-0`. A whole output can give `+0` where the reference gives `-0`. Handwired allows this ("zeros may differ in sign, which never changes a nonzero value of these instructions", emit.ts). The tests compare the outputs of the integer writer with `Js.SameNumericValue`.

Not in phase 5: the lanes and the packed reads of bit-emit.ts (bits of one `u32` word, `packWords`). They need the loop form and the shared tables of the JSON search. Open question 2 asks to defer them to phase 6.

### 4. The batch runner

Decision:

- Buffers, in group 0:
  - binding 0, `sources: array<f32>`, read-only storage. Table `t` cell `c` is at `t * SourceSize + c`.
  - binding 1, `outputs: array<f32>`, read-write storage. Table `t` root `r` is at `t * RootCount + r`.
  - binding 2, `params: Params`, read-only storage: `first` (the first table of this dispatch), `count` (the table count of this dispatch), `zero` (always 0).
- One invocation evaluates one source table. Workgroup size 64 (the Handwired value). The dispatch has `ceil(count / 64)` workgroups in x. An invocation with `t >= count` returns.
- A batch splits into chunks. A chunk holds at most `min(maxStorageBufferBindingSize / (4 * SourceSize), maxStorageBufferBindingSize / (4 * RootCount), 64 * maxComputeWorkgroupsPerDimension)` tables. `GpuDevice.Open` requests the adapter maximum of `maxStorageBufferBindingSize` and `maxBufferSize`, as `requestBatchDevice` in host.ts does.
- One `Evaluate` call: validate the length of `sources` (`tableCount * SourceSize`), validate each source table with `circuit.ValidateSourceRanges` on the CPU, as `TensorEvaluator` does, then for each chunk: `QueueWriteBuffer`, encode one compute pass, copy `outputs` to a map-read buffer, submit, `BufferMapAsync`, `DevicePoll(wait: true)`, copy the mapped range into the caller span, unmap.
- The evaluator keeps its buffers and grows them when a larger chunk comes. It creates the shader module and the pipeline one time, in `Create`.
- A root that the program does not hold gives NaN, as `TensorEvaluator` does. The writer writes the NaN bits `0x7fc00000u` for it.
- CPU fallback: `CpuBatchEvaluator` runs the same `IBatchEvaluator` contract with `Parallel.For` over blocks of tables. Each worker has its own `TensorEvaluator` from one shared program (TensorProgram remarks). `BatchEvaluators.Create(circuit, program, device)` returns a `GpuEvaluator` when `device` is not null, and a `CpuBatchEvaluator` otherwise. Phase 6 (R6.3) needs both paths.
- `GpuEvaluator` also implements `IRootEvaluator.Evaluate(ReadOnlySpan<float> source)` for one table. `ProgramEvaluator` uses this for the toggle on the GPU (task 4).
- Thread safety: each `GpuDevice` holds one lock. Each call that uses the device (buffer writes, submit, map, poll) takes this lock. So all GPU calls on one device run one at a time, and two evaluators on one device can be called from two threads. Reason: I did not verify the thread rules of wgpu-native through Silk.NET, and a lock does not depend on them. The evaluators of one device share the GPU queue, so the lock costs little. The test `Two_evaluators_on_one_device_run_on_two_threads` checks the result.
- Disposal: `GpuEvaluator.Dispose` releases its buffers, bind group, pipeline, layouts and shader module. `GpuDevice.Dispose` releases the queue, the device, the adapter and the instance. Dispose the evaluators before their device. A call after `Dispose` throws `ObjectDisposedException`.
- Device loss: the device-lost callback sets a flag. The next call throws `InvalidOperationException` with the reason.

Reason: the layout and the dispatch follow host.ts and loop-emit.ts. The source validation keeps the contract of `TensorEvaluator`: a source table that breaks a declaration throws, and is never a wrong value.

### 5. The recurrent step on the GPU (R5.4)

Decision:

- `GpuRecurrentRunner.Create(device, function, writer)` builds three GPU programs from one `RecurrentFunction`:
  1. The prefix, a `GpuEvaluator` over `function.Prefix.Table` with the roots `function.Prefix.Outputs` and then `function.Prefix.LoopOverflowBits`.
  2. The step kernel over `function.Step.Table`. Its roots are `Table.NextSource` (one for each cell) and then `Done`.
  3. The suffix, a `GpuEvaluator` over `function.Suffix.Table` with the roots `function.Suffix.Outputs` and then its overflow bits.
- The three passes run in one command buffer. The prefix writes its outputs into the `state` buffer. The order of the prefix outputs is the order of the step cells (Recurrent.cs header), so the step kernel reads the same buffer as its source tables. The suffix cells are the step cells, so the suffix reads the same buffer. One readback returns the suffix outputs, the overflow bits and the lane status.
- The step kernel holds each state cell in a `var` and each invariant in a `let`. For each lane, it repeats the CPU order of `RecurrentRunner.Run`:
  1. Stop with status `Limit` when the evaluation count equals `stepLimit`.
  2. At the first evaluation, check the cells of the source table against the cell rules (see below). Stop with status `Invalid` and the cell position when a check fails. A later source table holds the next values that step 4 checked.
  3. Evaluate the step rows.
  4. Check each next value against the rules of its cell. Stop with status `Invalid` on a failure. `ProgramEvaluator.Evaluate` also checks the next values before the done test, so a lane with `done` = 1 and a bad next value fails on the CPU and on the GPU.
  5. Check that `done` is finite. When `done` is not 0, stop with status `Completed`.
  6. Copy the next values into the state variables. Add 1 to the step count.
- The check of the cell rules, in WGSL: finite (`is_finite` as a bit test), within the declared range of the cell, an exact bit (0, -0 or 1) for a bit cell, a multiple of the grid for a grid cell (`floor(v / grid) * grid == v`, with a power-of-two grid this is exact), and each one-hot and at-most-one rule of `circuit.OneHotGroups`. The rules come from the declarations of the source wires in `function.Step.Table.Circuit` (`Ranges`, `Grids`, `ExactBits`, `OneHotGroups`). `SourceTable.CreateCircuit` declares each cell spec there. So no new API on `CellValidator` is necessary.
- The phase 4 carried item: this check covers the wide form-3 cells. A wide `int` cell has the range ±2^23. A next value past the range stops the lane with `Invalid`. So a value past the cell range is never a silent wrong value. The runner also has the step limit.
- On a lane with `Invalid`, the host runs `RecurrentRunner.Run` on the CPU for that lane with `stepLimit` equal to the failed evaluation plus 1. The CPU runner throws its own exception. The GPU runner throws `InvalidOperationException` with "lane <n>: " and the CPU message, and the CPU exception as the inner exception. So the message names the same cell and value as the CPU runner. When the CPU runner does not throw, the GPU runner throws `InvalidOperationException` "lane <n>: the GPU check failed at evaluation <e> and the CPU check passed", which is a backend fault.
- A nonzero overflow bit throws the message of `TranslatedEvaluator` for the first lane with the bit. A non-finite suffix output throws as `ProgramEvaluator` does.
- Steps in dispatches: one dispatch runs at most `StepsPerDispatch = 1024` evaluations for each lane. The lane state, the step count and the status stay in buffers between dispatches. The host submits more dispatches while a lane has status `Running` and its count is below `stepLimit`. An atomic counter of running lanes is read back after each dispatch. Reason: macOS stops a GPU command that runs too long, and `stepLimit` has no upper bound. A lane that is done stays idle until the other lanes of its workgroup are done, as the design says.
- The result: `GpuRecurrentBatch(float[] Outputs, int[] Steps, bool[] Completed)`, lane after lane, and `Lane(int)` gives a `RecurrentResult`. `Run(arguments, stepLimit)` for one lane returns a `RecurrentResult`, equal to the CPU runner result.
- The argument tables are validated on the CPU with `function.Prefix.Table.Validator` before the upload.

### 6. Test strategy

Decision:

- Each GPU test runs for each adapter of `GpuFixture.Adapters`, and for both writers where the test names "both writers". The test output gives the adapter name and backend.
- The comparison rule:
  - f32 writer, program without gather lowering: `Js.ObjectIs` on each value (bits, `-0`, NaN).
  - f32 writer, program with gather lowering: `Js.SameNumericValue` (a gather can change the sign of an exact zero, TensorDifferentialTests header).
  - integer writer: `Js.SameNumericValue` (question 3).
  - When `device.FlushesSubnormals` is true, an evaluation is compared only when its reference holds no source cell and no wire with a nonzero magnitude below 2^-126. The test counts the evaluations that it does not compare, and writes the count. When `FlushesSubnormals` is false, each evaluation is compared.
- R5.3 test (`RoundingPointTests`): three programs, each on each adapter and with both writers, with source tables at the rounding boundaries:
  - The boolean atom: `Boolean(RoundAffine([(x, 0.3)], 0.1))` and `Boolean(u)` with `u` a unit cell. Scores 0, -0, 1, 0.5, 0.49999997, 0.50000006, 0.25, 0.75, and 256 seeded scores.
  - A round chain: `t = RoundAffine([(x, 1)], 12582912)`, `r = RoundAffine([(t, 1)], -12582912)`, both with `MarkRoundingPoint`. `x` within ±2^22: the ties 0.5, 1.5, 2.5, -0.5, -1.5, values 1 ulp (unit in the last place) from each tie, and 256 seeded values.
  - A sign cascade: `Compare(a, Relation.Greater, b)` and `Compare(a, Relation.Equal, b)` with `a`, `b` in ±1e6 and no grid, so that `Compare` builds a sign cascade (the test asserts `RoundingPointKind.SignCascade` in `RoundingPoints()`). Inputs: equal values, `0` and `-0`, values 1 ulp apart at 1, 1000 and 1e6, differences of 2^-126 and 2^-100, and 256 seeded pairs. One more input has a difference of 2^-140. Its expected value is the reference when `FlushesSubnormals` is false, and the bit of equal operands when it is true.
  - One FMA program: `RoundAffine([(x, 1.0000001)], -1.0000002)` over the 64 probe inputs.
  - Each output equals the reference with `Js.ObjectIs` (f32 writer) or `Js.SameNumericValue` (integer writer).
- Differential test (`GpuDifferentialTests`): the phase 2 generator moves from `TensorDifferentialTests` into `tests/Sharpwired.Core.Tests/RandomPrograms.cs` (`internal static`). The phase 2 test calls it and keeps its seed and its coverage floors, so its counts do not change. The GPU test links the file. It runs 200 seeded circuits, in the modes "every wire as a root" (gather lowering) and "no gather lowering, every wire as a root", with both writers, over 64 source tables each. Each live wire is compared. Coverage floors: half of the counts of the first run, as in phase 2. The task records the run time on the M5 Pro. If the run takes more than 60 s, the task lowers the circuit count and records the new count.
- Toggle test (`GpuToggleTests`): a `ToggleMachine` on `ProgramEvaluator.Create(table, output, (circuit, program) => GpuEvaluator.Create(device, circuit, program, writer))` and a `ToggleMachine` on the CPU evaluator run the scripted trace of `ToggleTrace` (linked from `tests/Toggle.Builder.Tests/`). Each frame gives the same 6 outputs with `Js.Is` and the same `Clock.IsRunning`, for both writers. The two machines run the same circuit, so the sign of zero must be equal too.
- Recurrent tests (`GpuRecurrentTests`): each method of `RecurrentInputs` that `RecurrentTests` translates (`Halve`, `CountDown`, `SlowDecay`, `Invariant`, `HalveAtLeastOnce`, `Q1Lag`, `ChainLag`, `LaterHalf`, `Fall`) runs on the GPU over 1,024 seeded argument tables and the edge values of `RecurrentTests`. `Grow` is refused by the translator, so it has no GPU test. Each lane equals `RecurrentRunner.Run`: the same steps, the same `Completed`, and outputs with `Js.ObjectIs` (f32 writer) or `Js.SameNumericValue` (integer writer). The GPU and the CPU run the same circuit, so the f32 writer must give the same sign of zero. Plus: a step limit below the needed count, a step limit of 2,500 with lanes that need 0 to 2,500 steps (more than two dispatches), and the wide-cell test below.
- Wide-cell test: a new input `DoubleUntil([Range(0, 40)] int n)` (`int x = 1; int k = 0; while (k < n) { x = x * 2; k++; } return x;`). `x` gets the wide range ±2^23. For `n` = 30, the CPU runner throws at the evaluation where `x` passes 2^23. The GPU runner must throw `InvalidOperationException` that holds the CPU message, for the lane with `n` = 30, in a batch where the other lanes are valid.
- No test is skipped. The zero-adapter behavior is question 1.

### 7. The project layout

Decision:

- `src/Sharpwired.Gpu/Sharpwired.Gpu.csproj`: references `Sharpwired.Core`, `Sharpwired.Cells` and `Sharpwired.IL` (for `RecurrentFunction`), and the three Silk.NET packages. `AllowUnsafeBlocks` is true.
- `tests/Sharpwired.Gpu.Tests/Sharpwired.Gpu.Tests.csproj`: references `Sharpwired.Gpu`, `Sharpwired.IL`, `Sharpwired.Annotations`, `Toggle.Builder`. It links `Js.cs`, `TensorChecks.cs`, `RandomPrograms.cs` from `tests/Sharpwired.Core.Tests/`, `Inputs/RecurrentInputs.cs` from `tests/Sharpwired.IL.Tests/`, and `ToggleTrace.cs` and `ToggleReference.cs` from `tests/Toggle.Builder.Tests/`.
- `Sharpwired.slnx` gets both projects.
- Core gets `IRootEvaluator` (public interface). `TensorEvaluator` implements it. Cells gets one overload `ProgramEvaluator.Create(program, output, Func<Circuit, TensorProgram, IRootEvaluator> backend)`. Both are additions. No current signature changes.
- The writers read `TensorProgram` through its public API: `UnsignedIndices` indexer, `ImmutableArray` weights and biases. A writer runs one time for each program, so the indexer cost does not matter. The phase 2 parked finding 3 (raw array access) therefore needs no Core change in phase 5.

## Public API

### Sharpwired.Core (addition)

```csharp
/// An evaluator of one source table that returns the values of the program roots.
public interface IRootEvaluator
{
    TensorProgram Program { get; }
    float[] Evaluate(ReadOnlySpan<float> source);
}

public sealed class TensorEvaluator : IRootEvaluator { /* no other change */ }
```

### Sharpwired.Cells (addition)

```csharp
public static class ProgramEvaluator
{
    public static ProgramEvaluator<TOutput> Create<TOutput>(FinishedTable program, TOutput output);
    // New: the backend makes the root evaluator from the circuit and the compiled program.
    public static ProgramEvaluator<TOutput> Create<TOutput>(
        FinishedTable program, TOutput output, Func<Circuit, TensorProgram, IRootEvaluator> backend);
}
```

### Sharpwired.Gpu

```csharp
public enum GpuBackend { Metal, Vulkan, D3D12, OpenGL, OpenGLES, Other }
public enum GpuAdapterType { DiscreteGpu, IntegratedGpu, Cpu, Other }
public sealed record GpuAdapterInfo(int Index, string Name, GpuBackend Backend, GpuAdapterType Type);

public sealed class GpuDevice : IDisposable
{
    public static IReadOnlyList<GpuAdapterInfo> ListAdapters();
    // Opens the adapter, or the high-performance adapter when adapter is null.
    // Throws InvalidOperationException when no adapter opens.
    public static GpuDevice Open(GpuAdapterInfo? adapter = null);
    public GpuAdapterInfo Adapter { get; }
    public long MaxStorageBufferBindingSize { get; }
    public int MaxWorkgroupsPerDimension { get; }
    public bool FlushesSubnormals { get; }
    public void Dispose();
}

public enum GpuWriter { Float32, Integer }

public sealed record WgslStatistics(int Statements, int FloatRows, int WholeRows, int BitRows, int Keeps);
public sealed record WgslModule(string Code, int SourceSize, int RootCount, int WorkgroupSize, WgslStatistics Statistics);

public static class WgslFloatWriter
{
    public static WgslModule Write(Circuit circuit, TensorProgram program);
}

public static class WgslIntegerWriter
{
    public static WgslModule Write(Circuit circuit, TensorProgram program);
}

public enum WholeKind { Float, Whole, Bit }
public readonly record struct WholeProof(WholeKind Kind, long Minimum, long Maximum);
public static class IntegerProof
{
    // One proof for each packed position of the program.
    public static ImmutableArray<WholeProof> Prove(Circuit circuit, TensorProgram program);
}

public interface IBatchEvaluator : IDisposable
{
    TensorProgram Program { get; }
    int RootCount { get; }
    // outputs.Length must be tableCount * RootCount. Table t root r is at t * RootCount + r.
    void Evaluate(ReadOnlySpan<float> sources, int tableCount, Span<float> outputs);
}

public sealed class GpuEvaluator : IBatchEvaluator, IRootEvaluator
{
    public static GpuEvaluator Create(GpuDevice device, Circuit circuit, TensorProgram program, GpuWriter writer = GpuWriter.Integer);
    public WgslModule Module { get; }
    public float[] Evaluate(ReadOnlySpan<float> sources, int tableCount);
}

public sealed class CpuBatchEvaluator : IBatchEvaluator
{
    public CpuBatchEvaluator(Circuit circuit, TensorProgram program, int? maxDegreeOfParallelism = null);
}

public static class BatchEvaluators
{
    public static IBatchEvaluator Create(Circuit circuit, TensorProgram program, GpuDevice? device, GpuWriter writer = GpuWriter.Integer);
}

public sealed record GpuRecurrentBatch(ImmutableArray<float> Outputs, ImmutableArray<int> Steps, ImmutableArray<bool> Completed, int OutputCount)
{
    public RecurrentResult Lane(int lane);
}

public sealed class GpuRecurrentRunner : IDisposable
{
    public const int StepsPerDispatch = 1024;
    public static GpuRecurrentRunner Create(GpuDevice device, RecurrentFunction function, GpuWriter writer = GpuWriter.Integer);
    public GpuRecurrentBatch Run(ReadOnlySpan<float> arguments, int laneCount, int stepLimit);
    public RecurrentResult Run(ReadOnlySpan<float> arguments, int stepLimit);
}
```

Exceptions: a wrong span length, a negative count, a negative step limit, or a program of another circuit throws `ArgumentException` (or `ArgumentOutOfRangeException`). A source table that breaks a declaration throws as `TensorEvaluator` does. No adapter, a shader or pipeline error (with the wgpu message), a lost device, and a failed map throw `InvalidOperationException`.

## Files

| File | Content | Source |
| --- | --- | --- |
| `src/Sharpwired.Gpu/Sharpwired.Gpu.csproj` | the project, three packages | no Handwired source |
| `src/Sharpwired.Gpu/GpuDevice.cs` | adapters, open, limits, error callback, error scopes, device loss, subnormal probe | gpu/host.ts (`requestBatchDevice`) |
| `src/Sharpwired.Gpu/ComputeKernel.cs` | internal: one shader, explicit bind group layout, buffers, dispatch, readback | gpu/host.ts (`createProgramRunner`) |
| `src/Sharpwired.Gpu/WgslText.cs` | `wgslFloat`, `wgslInt`, the prelude (`Params`, `k`, `is_nan`, `is_finite`, `relu`), names | emit.ts, codegen/wgsl.ts |
| `src/Sharpwired.Gpu/ShaderRows.cs` | `ShaderRow` records and `FlattenProgram` with gathers and index reads of all candidate forms | emit.ts (`flattenProgram`) |
| `src/Sharpwired.Gpu/WgslFloatWriter.cs` | the f32 writer | emit.ts (`createRowWriter`), loop-emit.ts |
| `src/Sharpwired.Gpu/IntegerProof.cs` | the proof of whole wires and bits | bit-emit.ts (`declaredWhole`, `sumRange`, `proveStage`) |
| `src/Sharpwired.Gpu/WgslIntegerWriter.cs` | the integer writer, gate rules | bit-emit.ts |
| `src/Sharpwired.Gpu/Batch.cs` | `IBatchEvaluator`, `CpuBatchEvaluator`, `BatchEvaluators` | no Handwired source |
| `src/Sharpwired.Gpu/GpuEvaluator.cs` | the GPU batch evaluator, chunks | gpu/host.ts (`evaluate`) |
| `src/Sharpwired.Gpu/RecurrentKernelWriter.cs` | the step kernel WGSL and the cell checks | loop-emit.ts (the `for` loop, registers as `var`) |
| `src/Sharpwired.Gpu/GpuRecurrentRunner.cs` | the three passes, dispatch rounds, status, CPU replay of a failed lane | no Handwired source |
| `src/Sharpwired.Core/IRootEvaluator.cs` | the interface | no Handwired source |
| `src/Sharpwired.Core/TensorEvaluator.cs` | implements `IRootEvaluator` | unchanged source |
| `src/Sharpwired.Cells/ProgramEvaluator.cs` | the backend overload | unchanged source |
| `tests/Sharpwired.Core.Tests/RandomPrograms.cs` | the phase 2 generator, moved | unchanged source |
| `tests/Sharpwired.Core.Tests/TensorDifferentialTests.cs` | calls `RandomPrograms` | unchanged source |
| `tests/Sharpwired.Gpu.Tests/Sharpwired.Gpu.Tests.csproj` | the test project | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/GpuFixture.cs` | `GpuFixture`, `GpuCollection`, the zero-adapter policy | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/DeviceTests.cs` | adapters, open, a trivial kernel, error scope, dispose | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/WgslFormTests.cs` | hand-written WGSL forms of question 2 on each adapter | codegen/wgsl.ts header |
| `tests/Sharpwired.Gpu.Tests/RowInterpreter.cs` | a CPU interpreter of `ShaderRow` lists in both writer forms | json-search tests (wgsl-interpreter.ts idea) |
| `tests/Sharpwired.Gpu.Tests/WriterTests.cs` | WGSL text of each row kind, literals, statement counts | json-search.test.ts |
| `tests/Sharpwired.Gpu.Tests/IntegerProofTests.cs` | each proof rule and each counterexample | bit-emit.test.ts, writer-probes.test.ts |
| `tests/Sharpwired.Gpu.Tests/RoundingPointTests.cs` | R5.3 | execution-model.md, "Rounding points" |
| `tests/Sharpwired.Gpu.Tests/BatchEvaluatorTests.cs` | chunks, 100,000 tables, CPU fallback, threads, errors | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/GpuDifferentialTests.cs` | seeded programs on the GPU | TensorDifferentialTests |
| `tests/Sharpwired.Gpu.Tests/GpuToggleTests.cs` | the toggle trace on the GPU | toggle.test.ts |
| `tests/Sharpwired.Gpu.Tests/GpuRecurrentInputs.cs` | `DoubleUntil` and other new `[Handwired]` inputs | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/GpuRecurrentTests.cs` | R5.4 | no Handwired source |
| `Sharpwired.slnx` | the two projects | |
| `docs/design.md` | Layer 3: the probe facts, keep, the subnormal limit | |
| `docs/superpowers/specs/2026-10-16-sharpwired-design.md` | R5.1 to R5.4 checked | |

## WGSL forms table

Notation: `S`, `L`, `G`, `I` are the row sums of the stored form (bias then terms in stored order). In the f32 column each `+` and `*` is inside `k(...)` and each literal is `k(lit)`. In the integer column, `w` is an `i32` whole value, `b` is a `u32` bit, and `x` is an f32 value. `base = t * SourceSize`.

| Operation | f32 writer | Integer writer |
| --- | --- | --- |
| Prelude | `struct Params { first: u32, count: u32, zero: u32 }`; `fn k(v: f32) -> f32 { return bitcast<f32>(bitcast<u32>(v) ^ params.zero); }`; `fn is_nan(v: f32) -> bool { return (bitcast<u32>(v) & 0x7fffffffu) > 0x7f800000u; }`; `fn is_finite(v: f32) -> bool { return (bitcast<u32>(v) & 0x7f800000u) != 0x7f800000u; }`; `fn relu(g: f32) -> f32 { return select(select(k(0.0f), g, g > k(0.0f)), g, is_nan(g)); }` | the same prelude |
| Source cell `c` | `let s3: f32 = sources[base + 3u];` | bit: `let b3: u32 = select(0u, 1u, sources[base + 3u] > 0.5f);` whole: `let w3: i32 = i32(sources[base + 3u]);` other: as f32 |
| Affine row | `let v9: f32 = k(k(k(2.5f) + s3) - k(k(0x1.555556p-2f) * v7));` for `2.5 + s3 - (1/3) v7` (left fold, one `k` per step, at most 32 terms per statement, then `let v9_1` continues) | whole: `let w9: i32 = 2i + w3 - 3i * w7;` bit (`1 - b`): `let b9: u32 = b3 ^ 1u;` other: f32 form, a whole operand as `f32(w3)` |
| ReGLU row | `let v9: f32 = k(L * relu(G));`; with `L` the constant 1: `let v9: f32 = relu(G);` | gate in 0..1 with bits of positive weight and nonnegative values of negative weight: `b_a & (b_b ^ 1u)`, `(b_a \| b_b \| b_c) ^ 1u`; comparison pair `relu(1 - relu(B - x))`: `u32(B <= x)`; other gate in 0..1: `u32(G > 0i)`; other whole gate: `max(G, 0i)`; whole row: `let w9: i32 = L * max(G, 0i);` other: f32 form |
| Boolean atom | `let v9: f32 = k(k(S + k(8388609.0f)) - k(8388609.0f));` | whole score: `let b9: u32 = u32(S);` f32 score: `let b9: u32 = u32(k(k(S + k(8388609.0f)) - k(8388609.0f)));` |
| Index read | `let p9: f32 = floor(I);` `let c9: i32 = select(i32(clamp(p9, 0.0f, 12.0f)), 0i, is_nan(I));` `var v9: f32 = <candidate at c9>;` then `if (p9 < 0.0f) { v9 = 0.0f; }` for a `zero` side below, `if (p9 > 12.0f) { v9 = 0.0f; }` for a `zero` side above, `if (is_nan(I)) { v9 = bitcast<f32>(0x7fc00000u); }` | whole index: `let c9: i32 = clamp(I, 0i, 12i);` and the zero sides as `select(..., 0, I < 0i)`, `select(..., 0, I > 12i)`; a clamp or a select that the index range rules out is not written; whole candidates give `w9`, other candidates give `v9` |
| Candidate at `c` | source cells at `base0 + c * stride`: `sources[base + u32(7i + c9 * 2i)]`; source cells in a list: `const t4 = array<u32, 5>(...);` and `sources[base + t4[c9]]`; computed values: `var t9 = array<f32, 13>(v20, v21, ...);` and `t9[c9]` | the same, with `i32(...)` around a whole source read and `array<i32, N>` for whole computed values |
| Gather compute (slot `g`) | `let gi2: i32 = i32(0.0f * s4 + 1.0f * s5 + 2.0f * s6);` `let gm2: f32 = s4 + s5 + s6;` (whole numbers, exact in any order for one-hot bits, so no `k`) | `let gi2: i32 = i32(b5) + 2i * i32(b6);` `let gm2: i32 = i32(b4 + b5 + b6);` |
| Gather read | row sum `r`, then `k(r + cand)`; masked: `k(r + k(gm2 * cand))`; `cand` is the candidate at `clamp(gi2, 0i, last)` | whole row and candidates: `r + cand`, masked `r + gm2 * cand` in `i32`; other: f32 form |
| Output root `r` | `outputs[t * RootCount + 2u] = v9;`; a root that the program does not hold: `bitcast<f32>(0x7fc00000u)` | `f32(w9)` or `f32(b9)` for a whole or bit root |
| Step kernel cell check | `ok = ok && is_finite(v) && v >= k(min) && v <= k(max)`, with no comparison for an infinite bound; bit: `v == 0.0f \|\| v == 1.0f`; grid `g`: `floor(k(v * k(1/g))) == k(v * k(1/g))`; one-hot: the members are bits and their sum is 1 (at-most-one: at most 1) | whole and bit state cells check in `i32` and `u32` |

## Tasks

The tasks run in this order. Each task ends with a green `dotnet test` and `dotnet test -c Release`, and a commit.

### Task 1: the project, the device and the WGSL form test

- Files: `src/Sharpwired.Gpu/Sharpwired.Gpu.csproj`, `GpuDevice.cs`, `ComputeKernel.cs`, `WgslText.cs` (the prelude only); `tests/Sharpwired.Gpu.Tests/Sharpwired.Gpu.Tests.csproj`, `GpuFixture.cs`, `DeviceTests.cs`, `WgslFormTests.cs`; `Sharpwired.slnx`.
- `GpuDevice` as in question 1 and question 4: `ListAdapters`, `Open`, the uncaptured error callback, error scopes, the device-lost flag, `FlushesSubnormals`. `ComputeKernel` creates an explicit bind group layout from a list of buffer kinds, so a binding that the shader does not use causes no error.
- Tests (`DeviceTests`):
  - `ListAdapters` gives each adapter with a name and a backend. The fixture writes them to the output.
  - Each adapter opens, and a kernel `dst[i] = src[i] * 2.0` over 1,000 values reads back the CPU values.
  - A bad shader throws `InvalidOperationException` that holds the wgpu message, and the process continues. A second kernel on the same device then runs.
  - `Dispose` two times does not throw. A call after `Dispose` throws `ObjectDisposedException`.
  - With no adapter: the fixture policy of question 1. A unit test of the policy function checks both cases with a fake adapter list.
- Tests (`WgslFormTests`, the first GPU task of the design): each form of the question 2 table without keep and with keep runs on each adapter. The forms with keep give the CPU bits on all inputs (assert). The forms without keep are written to the output with their counts of differences, with no assertion, because a backend without fast math gives no difference. The `FlushesSubnormals` value is asserted against the result of `x * 2^-10` at `x = 2^-120`.
- R items: R5.2 (the wgpu-native path), R5.3 (part: the forms of the rounding points).

### Task 2: the f32 writer and the batch evaluator

- Files: `ShaderRows.cs`, `WgslText.cs`, `WgslFloatWriter.cs`, `Batch.cs`, `GpuEvaluator.cs`; tests `RowInterpreter.cs`, `WriterTests.cs`, `RoundingPointTests.cs` (f32 writer), `BatchEvaluatorTests.cs`.
- `ShaderRows.FlattenProgram` gives the rows of each layer in the order of `TensorExecution`: sums, gathers, index reads, gates, booleans. It accepts each program that `ValidateTensorProgram` accepts. emit.ts refuses gathers and computed candidates. This writer accepts them (forms table).
- Tests (no adapter):
  - `WgslFloatWriter` text for one row of each kind, with a bias of 0, a weight of ±1, 33 terms (a continued statement), `-0` and subnormal literals, index reads with each out-of-range rule and each candidate form, masked and unmasked gathers.
  - `wgslFloat` gives the exact bits for 10,000 seeded FP32 values: a C# parser of the hexadecimal literal reads them back.
  - `RowInterpreter` runs the rows of the f32 form in C# `float` arithmetic, and equals the reference with `Js.ObjectIs` (or `Js.SameNumericValue` with gathers) over the seeded programs of `RandomPrograms`. This checks the rows without a GPU.
- Tests (each adapter):
  - R5.3 with the f32 writer: the boolean atom, the round chain, the sign cascade and the FMA program of question 6.
  - `Evaluate` over 100,000 tables of a 20-operation program equals the reference on each table.
  - A chunk test: a device limit lowered by an internal option to 1,000 tables per chunk, 2,500 tables, equal to the reference.
  - A root that the program does not hold gives NaN.
  - Wrong `sources` length, a negative count and a short output span throw `ArgumentException`. A source table past a declared range throws the `TensorEvaluator` exception.
  - Two evaluators on one device run on two threads, and each equals the reference.
  - `CpuBatchEvaluator` equals the reference over the same 100,000 tables. `BatchEvaluators.Create` with a null device gives the CPU evaluator.
- R items: R5.1 (f32 writer), R5.2, R5.3 (f32 writer).

### Task 3: the integer writer

- Files: `IntegerProof.cs`, `WgslIntegerWriter.cs`; tests `IntegerProofTests.cs`, `WriterTests.cs`, `RowInterpreter.cs` (integer form), `RoundingPointTests.cs` (integer writer).
- Tests (no adapter):
  - Each proof rule of question 3 gives `Whole` or `Bit` on its smallest program, and each counterexample gives `Float`: a weight of 0.5, a partial sum of 2^24 + 1, a source grid of 0.5, an index read with one candidate that is not whole, a gate whose positive-weight value is not a bit.
  - Each gate rule fires on its program and does not fire on its counterexample. The rule and the counterexample come from bit-emit.ts and writer-probes.test.ts.
  - `RowInterpreter` runs the integer form in C# `int` and `uint` arithmetic, and equals the reference with `Js.SameNumericValue` over the seeded programs of `RandomPrograms`.
  - The `Compare` integer bit (`RoundingPointKind.IntegerBit`) becomes one `u32(B <= x)` statement.
- Tests (each adapter): R5.3 with the integer writer, and the batch tests of task 2 with the integer writer.
- R items: R5.1 (integer writer), R5.3 (integer writer).

### Task 4: the differential test and the toggle on the GPU

- Files: `src/Sharpwired.Core/IRootEvaluator.cs`, `src/Sharpwired.Core/TensorEvaluator.cs`, `src/Sharpwired.Cells/ProgramEvaluator.cs`, `tests/Sharpwired.Core.Tests/RandomPrograms.cs`, `tests/Sharpwired.Core.Tests/TensorDifferentialTests.cs`, tests `GpuDifferentialTests.cs`, `GpuToggleTests.cs`.
- Move the generator first. Run the phase 2 differential test and compare its output counts with the counts in its comment. They must be equal.
- Tests:
  - `ProgramEvaluator.Create` with a backend calls the backend one time with the compiled program, and gives the same `Evaluate` results as the overload without a backend (CPU backend test, no adapter).
  - `GpuDifferentialTests` (question 6) on each adapter, both writers.
  - `GpuToggleTests` (question 6) on each adapter, both writers.
- R items: R5.2, R5.3 (on seeded programs and on the toggle).

### Task 5: the recurrent step on the GPU

- Files: `RecurrentKernelWriter.cs`, `GpuRecurrentRunner.cs`; tests `GpuRecurrentInputs.cs`, `GpuRecurrentTests.cs`.
- Tests (no adapter): the step kernel text has one `var` for each state cell and one `let` for each invariant, the cell checks of the forms table, and the status codes.
- Tests (each adapter, both writers): question 6, "Recurrent tests" and "Wide-cell test". Also: `Run(arguments, stepLimit)` for one lane equals `RecurrentRunner.Run`. A negative step limit throws `ArgumentOutOfRangeException`. A step limit of 0 gives `Completed` false and 0 steps, as the CPU runner gives.
- R items: R5.4, and the phase 4 carried item (the GPU runner checks the wide cells).

### Task 6: the close of the phase

- Files: `docs/design.md` (Layer 3: the package versions, the probe facts, keep, the subnormal limit, the two writers; the open item "GPU exactness under Metal fast math" gets the result), the spec (R5.1 to R5.4 checked), `.superpowers/sdd/phase5/progress.md` (the ledger).
- Run the verification list. Record the run times of the GPU tests.
- R items: RA.1, RA.2, RA.3.

## Verification

- `dotnet build -warnaserror` passes.
- `dotnet test` passes (Debug). `dotnet test -c Release` passes.
- `grep -rn "TODO\|Skip =" src tests samples` gives no result.
- `grep -rln "Silk.NET" src samples tests --include=*.cs` gives only files in `src/Sharpwired.Gpu/`.
- `grep -rln "unsafe" src --include='*.cs'` gives only `src/Sharpwired.Gpu/GpuDevice.cs`, `src/Sharpwired.Gpu/ComputeKernel.cs`, and `src/Sharpwired.IL/IlDecoder.cs`. Phase 4 added the unsafe method of `IlDecoder.cs`. The phase 5 rule covers the files of `src/Sharpwired.Gpu/` only.
- `grep -rln "Raylib_cs" src samples tests` gives only `src/Sharpwired.Raylib/RaylibWindow.cs`.
- Each new source file is below 800 lines.
- The test output names each adapter, its backend, and `FlushesSubnormals`.
- The phase 2 differential test gives the counts of its comment after the generator moved.
- Each R5 item maps to at least one test:

| Item | Tests |
| --- | --- |
| R5.1 the f32 writer and the integer writer | `WriterTests`, `IntegerProofTests`, the `RowInterpreter` differential tests, `RoundingPointTests`, `GpuDifferentialTests` |
| R5.2 the batch runner through wgpu-native | `DeviceTests`, `BatchEvaluatorTests`, `GpuDifferentialTests`, `GpuToggleTests` |
| R5.3 the boolean atom, a round chain and a sign cascade on each adapter, against the reference | `RoundingPointTests`, `WgslFormTests` |
| R5.4 the recurrent step on the GPU | `GpuRecurrentTests` |
| RA.1 | `NOTICE` keeps the Handwired MIT notice (no change) |
| RA.2 | Each new comment and doc in ASD-STE100 |
| RA.3 | The green `dotnet test` run at the end of each task |

## Open questions

1. A machine with no adapter. R5.3 says "on each available adapter". With zero adapters, the GPU tests either fail, or pass with a recorded reason. Recommendation: fail by default, and pass with the reason in the output when the environment variable `SHARPWIRED_NO_GPU` holds a reason (question 1). This machine has one adapter (Apple M5 Pro, Metal), so the default applies here. Do you accept this policy?
2. Packed words. bit-emit.ts also holds lists of bits as lanes of one `u32` word, and packs table reads into words (`packWords`). The design says "as `u32` values or as bits of a word". These rules need the loop form and the shared tables of the JSON search. Recommendation: phase 5 holds each bit as its own `u32`. Phase 6 adds the lanes and the packed reads with the loop form. Do you agree to defer them?
3. `i32` for whole numbers. R5.1 says "holds proven integers and bits as `u32` values". A whole value can be negative, for example the difference `a - b` of `Compare`. bit-emit.ts holds whole numbers as `i32` and bits as `u32`. Recommendation: `i32` for whole numbers and `u32` for bits, and a change of the R5.1 text to "as integer values". Do you accept?
4. The subnormal limit of the f32 writer. Metal flushes subnormal results on this machine, and no WGSL form stops it. Recommendation: accept the limit, record it as `GpuDevice.FlushesSubnormals`, and compare only the evaluations with no subnormal value on a device that flushes (question 6). The alternative is an exact software path for each operation near 2^-126, which costs a branch for each product and each sum. Do you accept the limit?
5. The default writer. Recommendation: `GpuWriter.Integer` is the default, because it gives the same values as the f32 writer and removes the keep cost on whole rows. Do you agree?

## Decisions

The controller decided the open questions on 2026-10-18.

1. Zero adapters. The GPU tests fail when no adapter opens. They pass with the reason in the test output when the environment variable `SHARPWIRED_NO_GPU` holds a reason. The writer, proof and CPU tests always run. No test is skipped.
2. Packed words (the lanes and `packWords` of bit-emit.ts) move to phase 6, where the JSON search loop and its shared tables exist. Phase 5 holds each bit as its own `u32`.
3. Integer types. Whole numbers use `i32`, and bits use `u32`, as bit-emit.ts does. The spec item R5.1 says "u32"; the plan reads it as "integer values", because a whole number can be negative.
4. Subnormal values. The f32 writer cannot stop Metal from flushing subnormal results to 0. `GpuDevice.FlushesSubnormals` reports it. On such a device, the tests compare the evaluations with no subnormal value and count the others. The design docs and the XML comments state that the f32 writer is not exact for subnormal values on such a device. Handwired execution-model.md states the same effect for the sign cascade below 2^-126.
5. The default writer is `GpuWriter.Integer`.
