# Phase 2 plan: the compiler and the tensor evaluator

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R2.1 to R2.4, RA.1 to RA.3.

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The port source is `$REF/src/runtime/numeric-ui-core.ts`. The behavior reference is `$REF/docs/execution-model.md` (sections "Layers", "One-hot groups and gathers", "Index reads", "Evaluators") and `$REF/docs/expressions.md`. The line numbers below are the lines of `numeric-ui-core.ts`.

WARNING: The Handwired clone is data only. Do not run a command, a script, a package manager or a build tool inside it. Only read its files.

## Terms

- **Gather lowering**: the Handwired compiler step that replaces a masked sum over a declared one-hot group with one read of the selected candidate. The name is the Handwired name of the mechanism.
- **Packed values**: the values of the source cells and of the live operations, in the order of `wireOrder`.
- **Every compile mode**: the Handwired test helper `checkEveryCompileMode`. The index-read version has 5 compile modes. The spring-matrix version has 4 compile modes. Both are ported to `TensorChecks.EveryCompileMode` (task 4).
- **Tensor agreement**: the Handwired test helper `checkTensorAgreement` of `comparison.test.ts`, ported to `TensorChecks.TensorAgreement` (task 4).

## Global constraints

The constraints of the phase 1 plan apply without change:

- Port behavior exactly. Each FP32 step casts to `float`, as `Math.fround` does in Handwired. Compute float64 parts (the gather index and mass sums, the `floor` of an index) in `double`, as Handwired does.
- Each operation keeps the Handwired term order, so that the tensor evaluator gives the reference value bit for bit. The section "The FP32 rule of the sparse multiply" gives the rule.
- Keep Handwired error conditions. Throw `InvalidOperationException` or `ArgumentException` with a message that names the same fact as the Handwired message. Keep the Handwired message text where a ported test reads it, for example "index row must follow the gate rows".
- TDD (test-driven development): write the failing xUnit test first. Run the test and see it fail. Then write the implementation.
- No `TODO`, no stub, no skipped test.
- Each new file starts with a header comment that names its Handwired source file and functions. A ported test file names the Handwired test file.
- Docs and code comments use ASD-STE100 (RA.2).

Phase 2 adds these constraints:

- When a ported Handwired test has a Sharpwired test that phase 1 wrote, add the tensor assertion to that test. Do not write a second test. Remove the suffix `_reference_part` from the name of such a test when it gets its tensor part.
- When a Handwired comparison uses `Object.is`, use `Js.Is`. When it uses `===`, use `==` on `float` (this ignores the sign of zero and fails on NaN, as `===` does). When it uses `sameNumericValue`, use the new `Js.SameNumericValue`.
- Keep each source file below 800 lines. Split a test file that passes 800 lines into partial class files.
- Commit at the end of each task. Do not push.

## The FP32 rule of the sparse multiply

The tensor evaluator must give the value of the reference evaluator on each live wire, bit for bit. The rule has four parts.

1. **Stored constants round once, as the reference rounds them.** The layer stores the bias and the weights in `Float32Array`, so the store rounds each value to FP32:

   ```ts
   bias[row] = operation.leftBias                         // :899
   bias[projectionRow] = operation.gateBias               // :911
   weights[storedWeight] = coefficient                    // :987
   ```

   The reference evaluator rounds the same numbers at use (`evaluateTerms`, :1888-1900):

   ```ts
   let value = Math.fround(bias)
   for (const [wire, coefficient] of terms) {
     value = Math.fround(
       value + Math.fround(Math.fround(coefficient) * values[wire]!),
     )
   }
   ```

   C# rule: `Bias[row] = (float)operation.LeftBias` and `Weights[k] = (float)term.Coefficient`. Both are round-to-nearest-even conversions from `double`, as `Float32Array` stores are.

2. **Each row adds its terms in stored term order.** `projectionTerms[row] = operation.leftTerms` (:908) keeps the order of the operation. The columns map a wire to its input column, and they do not change the order. `sparseMatmulAddBias` (:1870-1886):

   ```ts
   let value = bias[row]!
   for (let index = rowOffsets[row]!; index < rowOffsets[row + 1]!; index++) {
     const product = Math.fround(weights[index]! * input[columns[index]!]!)
     value = Math.fround(value + product)
   }
   ```

   C# rule, one statement for each step, with an explicit cast on each step:

   ```csharp
   var value = bias[row];
   for (var index = rowOffsets[row]; index < rowOffsets[row + 1]; index++)
   {
       var product = (float)(weights[index] * input[columns[index]]);
       value = (float)(value + product);
   }
   ```

   The start value, the product and the sum are the same three FP32 steps as `EvaluateTerms` in `Circuit.Reference.cs`. A C# `float` operation can run at a higher precision by the language rules. The `(float)` cast forces the round to FP32. Phase 1 test `Fp32SemanticsTests` proves that RyuJIT does not contract the multiply and the add into an FMA.

3. **The gate and the boolean atom use the same FP32 step as the reference.**

   ```ts
   projection[outputRow] = Math.fround(
     projection[outputRow]! * Math.max(0, projection[outputSize + gateRow]!),   // :1764-1766
   )
   projection[booleanRow] = evaluateBoolean(projection[booleanRow]!)            // :1770
   ```

   C# rule: `(float)(projection[row] * MathF.Max(0f, projection[gate]))`, as `EvaluateOperationStream` does. `MathF.Max(0f, NaN)` is NaN and `MathF.Max(0f, -0f)` is `+0`, as `Math.max` gives. The boolean row calls `Fp32.EvaluateBoolean`.

4. **A gather read adds one contribution to the row bias.** `applyGathers` (:1784-1813):

   ```ts
   index += position * bit          // :1798, float64
   mass += bit                      // float64
   gatherIndices[compute.slot] = index   // :1801, Int32Array store
   gatherMasses[compute.slot] = mass     // :1802, Float32Array store
   ...
   if (read.masked) contribution = Math.fround(gatherMasses[read.slot]! * contribution)   // :1810
   projection[read.row] = Math.fround(projection[read.row]! + contribution)              // :1811
   ```

   C# rule: sum `index` and `mass` in `double`. Store the index with `JsToInt32(index)` and the mass with `(float)mass`. Then `contribution = (float)(mass * contribution)` for a masked read, and `projection[row] = (float)(projection[row] + contribution)`. For an exact one-hot this gives `f(f(bias) + selected)`, which is the reference value. The one known difference is the sign of an exactly zero result (execution-model.md, "One-hot groups and gathers"): the dense sum also adds the zeros of the products that the group does not select. The Handwired standard for this case is `===`.

An index read copies the packed value of the selected candidate, or writes `+0` or NaN (`applyIndexReads`, :1816-1842). It does no arithmetic. The index row is a projection row with the rule of part 2, and `ResolveIndexPosition` floors it in `double`.

For `+`, `-` and `*` on two FP32 values, the `double` result rounded once to FP32 equals the FP32 result. So the C# `float` statements equal the Handwired `Math.fround` statements on `double` values.

## Public API

The .NET names follow the phase 1 conventions: a `Circuit` method for each Handwired `Circuit` method, an extension method in `Validation` for each Handwired validator, and a `bool` parameter for each Handwired option object field (as `allowExistingReaders` in phase 1).

```csharp
public sealed partial class Circuit
{
    // Handwired Circuit.tensorProgram. AssumeOneHot, Append and IndexReadAffine set it to null.
    public TensorProgram? TensorProgram { get; set; }

    // Handwired Circuit.compileTensorProgram(roots, lowerGathers, {recomputeDepths}).
    // roots null: every operation is a root.
    public TensorProgram CompileTensorProgram(
        IReadOnlyList<Wire>? roots = null, bool lowerGathers = true, bool recomputeDepths = false);

    // Handwired Circuit.evaluateTensor. A new array of Width values, 0 for each removed wire.
    public float[] EvaluateTensor(ReadOnlySpan<float> source, TensorProgram? program = null);

    // Handwired Circuit.createTensorEvaluator. It compiles with lowerGathers true.
    public TensorEvaluator CreateTensorEvaluator(IReadOnlyList<Wire> roots, bool recomputeDepths = false);

    // Handwired Circuit.createTensorProgramEvaluator.
    public TensorEvaluator CreateTensorProgramEvaluator(TensorProgram program);
}

// Handwired: the function that createTensorProgramEvaluator returns, with its packedValues property.
public sealed class TensorEvaluator
{
    public TensorProgram Program { get; }
    // The array that the evaluator computes in. The next call overwrites it.
    public float[] PackedValues { get; }
    // The values of Program.Roots, in root order. Each call returns the same array and overwrites it.
    public float[] Evaluate(ReadOnlySpan<float> source);
}

public static partial class Validation
{
    // Handwired validateTensorProgram(circuit, program).
    public static void ValidateTensorProgram(this Circuit circuit, TensorProgram program);
    // Handwired validateTensorEvaluation(source, circuit, tensorValues).
    public static void ValidateTensorEvaluation(this Circuit circuit, ReadOnlySpan<float> source, float[] tensorValues);
}

// Handwired UnsignedIndexArray (Uint8Array | Uint16Array | Uint32Array) and compactUnsignedIndices.
public sealed class UnsignedIndices : IReadOnlyList<int>
{
    public static UnsignedIndices Empty { get; }            // new Uint8Array(0)
    public static UnsignedIndices From(ReadOnlySpan<int> values);  // compactUnsignedIndices
    public int ElementBits { get; }                          // 8, 16 or 32
    public int ByteLength { get; }                           // Count × ElementBits / 8
    public int Count { get; }
    public int this[int index] { get; }
}

// Handwired TensorGatherCompute, TensorGatherRead, TensorIndexRead, TensorLayer, TensorProgram.
public sealed record TensorGatherCompute(int Slot, UnsignedIndices BitPositions);
public sealed record TensorGatherRead(int Row, int Slot, bool Masked, UnsignedIndices CandidatePositions);
public sealed record TensorIndexRead(
    int Row, int IndexRow, bool BelowZero, bool AboveZero,
    int CandidateCount, int CandidateBase, int CandidateStride, UnsignedIndices CandidatePositions);

public sealed record TensorLayer
{
    public required UnsignedIndices InputIndices { get; init; }
    public required int InputSize { get; init; }
    public required int OutputSize { get; init; }
    public required ImmutableArray<float> Bias { get; init; }
    public required UnsignedIndices GateRows { get; init; }
    public required UnsignedIndices BooleanRows { get; init; }
    // The sparse matrix in CSR (compressed sparse row) form: RowOffsets, Columns, Weights.
    public required UnsignedIndices RowOffsets { get; init; }
    public required UnsignedIndices Columns { get; init; }
    public required ImmutableArray<float> Weights { get; init; }
    public required ImmutableArray<TensorGatherCompute> GatherComputes { get; init; }
    public required ImmutableArray<TensorGatherRead> GatherReads { get; init; }
    public required ImmutableArray<TensorIndexRead> IndexReads { get; init; }
}

public sealed record TensorProgram
{
    public required ImmutableArray<TensorLayer> Layers { get; init; }
    public required UnsignedIndices Roots { get; init; }
    public required int SourceSize { get; init; }
    public required int ValueCount { get; init; }
    public required UnsignedIndices WireOrder { get; init; }
    public required int Width { get; init; }
    public required int GatherSlotCount { get; init; }
}
```

The sparse matrix has no separate type. Handwired keeps `rowOffsets`, `columns` and `weights` on the layer, and `validateTensorProgram` checks them with the other layer fields. The port does the same.

| Sharpwired | Handwired |
| --- | --- |
| `Circuit.CompileTensorProgram` | `Circuit.compileTensorProgram` (:796), `findLiveOperations` (:1164), `computeCompiledDepths` (:1210), `packCandidateTable` (:1318), `compactUnsignedIndices` (:1922) |
| gather plans (internal `GatherPlan`, `MaskedProduct`) | `planGatherLowering` (:1411), `attemptGatherMatch` (:1481), `parseMaskedProduct` (:1391), `resolveAffineSide` (:1360), `hasFiniteRange` (:1471) |
| `TensorProgram.WireOrder`, `ValueCount`, `SourceSize`, `Width`, `GatherSlotCount`, `Roots` | `TensorProgram` (:182) |
| `TensorLayer` | `TensorLayer` (:167) |
| `TensorGatherCompute`, `TensorGatherRead` | `TensorGatherCompute` (:134), `TensorGatherRead` (:143) |
| `TensorIndexRead` with `CandidateBase`, `CandidateStride`, `CandidatePositions` | `TensorIndexRead` (:156), `PackedCandidateTable` (:208) |
| the `recomputeDepths` parameter | `CompileOptions.recomputeDepths` (:215) |
| `Circuit.TensorProgram` | `Circuit.tensorProgram` (:259) and its resets (:334, :553, :645) |
| `Circuit.EvaluateTensor` | `Circuit.evaluateTensor` (:751), `evaluateTensorProgram` (:1704) |
| `TensorEvaluator`, `Circuit.CreateTensorEvaluator`, `Circuit.CreateTensorProgramEvaluator`, `TensorEvaluator.PackedValues` | `createTensorEvaluator` (:757), `createTensorProgramEvaluator` (:769) |
| internal `TensorWorkspace`, `ExecuteTensorProgram`, `SparseMatmulAddBias`, `ApplyGathers`, `ApplyIndexReads` | `TensorWorkspace` (:192), `createTensorWorkspace` (:1717), `executeTensorProgram` (:1733), `sparseMatmulAddBias` (:1870), `applyGathers` (:1784), `applyIndexReads` (:1816) |
| `Validation.ValidateTensorProgram` | `validateTensorProgram` (:3039) |
| `Validation.ValidateTensorEvaluation` | `validateTensorEvaluation` (:3367) |

## Files

New files:

```txt
src/Sharpwired.Core/TensorProgram.cs            UnsignedIndices, TensorGatherCompute, TensorGatherRead, TensorIndexRead, TensorLayer, TensorProgram
src/Sharpwired.Core/Circuit.Compile.cs          CompileTensorProgram, FindLiveOperations, ComputeCompiledDepths, PackCandidateTable
src/Sharpwired.Core/Circuit.GatherLowering.cs   GatherPlan, MaskedProduct, PlanGatherLowering, AttemptGatherMatch, ParseMaskedProduct, ResolveAffineSide, HasFiniteRange
src/Sharpwired.Core/Circuit.Tensor.cs           the TensorProgram property, EvaluateTensor, CreateTensorEvaluator, CreateTensorProgramEvaluator
src/Sharpwired.Core/TensorEvaluator.cs          TensorEvaluator, TensorWorkspace, ExecuteTensorProgram, SparseMatmulAddBias, ApplyGathers, ApplyIndexReads, EvaluateTensorProgram, JsToInt32
src/Sharpwired.Core/Validation.Tensor.cs        ValidateTensorProgram, ValidateTensorEvaluation
tests/Sharpwired.Core.Tests/TensorChecks.cs                    EveryCompileMode (index-read modes and spring-matrix modes), TensorAgreement, ProgramCounts, Cost, GatherStatistics, LoweredWires, AssertSameProgram
tests/Sharpwired.Core.Tests/TensorCompilerTests.cs             the compiler tests with no Sharpwired test from phase 1
tests/Sharpwired.Core.Tests/TensorEvaluatorTests.cs            the evaluator tests with no Sharpwired test from phase 1, and the cache reset tests
tests/Sharpwired.Core.Tests/TensorValidationTests.cs           ValidateTensorProgram and ValidateTensorEvaluation refusals
tests/Sharpwired.Core.Tests/GatherLoweringTests.Magnitudes.cs  gather-lowering.test.ts "adversarial value magnitudes" and "library circuits"
tests/Sharpwired.Core.Tests/GatherLoweringTests.Seeded.cs      gather-lowering.test.ts "seeded circuits"
tests/Sharpwired.Core.Tests/TensorDifferentialTests.cs         R2.4: seeded random programs in the reference and the tensor evaluator
```

Changed files:

```txt
src/Sharpwired.Core/Circuit.cs              AssumeOneHot and Append set TensorProgram to null
src/Sharpwired.Core/Circuit.IndexRead.cs    IndexReadAffine sets TensorProgram to null after it adds the operation
src/Sharpwired.Core/Circuit.Reference.cs    EvaluateOperationStream becomes internal (ValidateTensorEvaluation calls it)
src/Sharpwired.Core/Validation.cs           the class becomes partial
tests/Sharpwired.Core.Tests/Js.cs           SameNumericValue
tests/Sharpwired.Core.Tests/*Tests.cs       the tensor assertions of PHASE2-TENSOR-ASSERTIONS.md
tests/Sharpwired.Core.Tests/ComparisonTests.cs   Cost and LiveOperations are removed (task 4)
tests/Sharpwired.Core.Tests/GatherLoweringTests.cs   the class becomes partial, and the recognition tests are added
tests/Sharpwired.Core.Tests/ApiShapeTests.cs    the program types are read-only
tests/Sharpwired.Core.Tests/PHASE2-TENSOR-ASSERTIONS.md   deleted in task 6
```

Size estimates: `Circuit.Compile.cs` about 350 lines, `Circuit.GatherLowering.cs` about 260, `TensorEvaluator.cs` about 250, `TensorProgram.cs` about 250, `Validation.Tensor.cs` about 230, `Circuit.Tensor.cs` about 90. `GatherLoweringTests` is split in three files because the Handwired file has 1,243 lines.

## Handwired behavior with no direct C# form

| Handwired behavior | C# decision |
| --- | --- |
| `compactUnsignedIndices` returns a `Uint8Array`, `Uint16Array` or `Uint32Array`, and tests check the type with `instanceof` and read `byteLength`. | `UnsignedIndices` holds one `byte[]`, `ushort[]` or `uint[]`. `ElementBits` gives 8, 16 or 32, and `ByteLength` gives the byte count. The tests check `ElementBits` where Handwired checks `instanceof`. The indexer returns `int`. |
| A `Float32Array` store rounds a `double` to FP32 without a visible cast. | Each store into a `float[]` or into the bias and weight builders has an explicit `(float)` cast. `Bias` and `Weights` are `ImmutableArray<float>`, so the test "compiled tensor constants use native FP32 storage" checks the element type `float`. |
| An `Int32Array` store of the gather index applies ECMAScript ToInt32: NaN and infinities give 0, other values truncate and wrap modulo 2^32. The .NET `(int)` cast saturates instead. | A private `JsToInt32(double)` in `TensorEvaluator.cs` does ToInt32. A test declares a one-hot group over a computed member that evaluates to +Infinity (a broken contract). ToInt32 gives the index 0, so the read selects the first candidate. A saturating cast gives `int.MaxValue`, and the clamp then selects the last candidate. |
| `packedValues` and the output of a reusable evaluator are the same `Float32Array` objects after each call (`assert.equal(first, second)`). | `TensorEvaluator.PackedValues` and the result of `Evaluate` are the same `float[]` instances on each call. The tests use `Assert.Same`. The arrays are writable, as in Handwired. |
| `wireOrder.indexOf(root)` gives -1 for a root that the program does not hold, and `packedValues[-1]` reads `undefined`, which a `Float32Array` stores as NaN. | `CreateTensorProgramEvaluator` keeps -1 for such a root, and `Evaluate` writes `float.NaN` for it. Only a hand-made program can have such a root, because the compiler marks every root live. |
| Tests copy a program with `structuredClone` and change one field. | The program types are records. A test makes a changed copy with `with` expressions and `ImmutableArray.SetItem` or `RemoveAt`. |
| `toEqual` compares two programs field by field, with the typed array type. | `TensorChecks.AssertSameProgram` compares each field, each `UnsignedIndices` (`ElementBits` and values), and each `float` with `Js.Is`. Record equality is not used, because it compares arrays by reference. |
| Reads of the same candidate table share one positions array, and `validateTensorProgram` checks a shared array once with a `Set` of arrays. A test checks that two reads hold the same array. | The compiler keys the packed tables by the `CandidateTable` reference and stores the same `UnsignedIndices` instance in each read. The validator uses `HashSet<UnsignedIndices>(ReferenceEqualityComparer.Instance)`. The test uses `Assert.Same`. |
| `Map` and `Set` iterate in insertion order: `slotByGroup` gives the slot numbers, and `candidateGroups` (the groups of the gate wire, then the groups of the left wire) gives the order in which `planGatherLowering` tries groups. | The C# code keeps an ordered `List` next to each `HashSet` or `Dictionary` whose order matters. It does not enumerate a `HashSet` or a `Dictionary` where order changes the result. |
| `resolveAffineSide` finds a wire by the key of `serializeExpression`, which writes numbers with JavaScript `String(number)`. | Call the phase 1 `Circuit.SerializeExpression`, which uses `Fp32.Format`. A negated coefficient of `-0` writes "0", as JavaScript does. |
| `evaluateTensor(source, program?)` and `tensorProgram ??= compileTensorProgram()`. | An optional `TensorProgram? program = null` parameter and `TensorProgram ??= CompileTensorProgram()`. |
| A JavaScript NaN is one value. | .NET NaN values can have different bits. `Js.Is` and `Js.SameNumericValue` treat every NaN as equal, as `Object.is` does. No test compares NaN bits. |
| `console.log` of the seeded gather test counts. | Write the counts with xUnit `ITestOutputHelper`. |

## Tasks

The tasks run in order. Each task builds on the previous task. Each task ends with `dotnet build -warnaserror` and `dotnet test` green, and a commit.

### Task 1: the program types and the dense compiler

- Files: `TensorProgram.cs`, `Circuit.Compile.cs`, `Validation.Tensor.cs` (`ValidateTensorProgram` only), `Validation.cs` (partial), `TensorChecks.cs` (`ProgramCounts`, `AssertSameProgram`), `TensorCompilerTests.cs`, `TensorValidationTests.cs`, and the phase 1 test files that the rows name (`CircuitTests.cs`, `IndexReadTests.cs`).
- Port: the types `TensorIndexRead`, `TensorLayer`, `TensorProgram`, `PackedCandidateTable`; `compactUnsignedIndices`; `findLiveOperations`; `computeCompiledDepths`; `packCandidateTable`; `compileTensorProgram` for the four instructions; `validateTensorProgram` without its gather checks. In this task the signature is `CompileTensorProgram(IReadOnlyList<Wire>? roots = null, bool recomputeDepths = false)`. Task 3 adds `lowerGathers`.
- Order rules to keep:
  - The layer of an operation is its depth minus 1: the build-time depth, or the compiled depth with `recomputeDepths`. The compiler skips a depth with no live operation.
  - In a layer, the rows follow operation order. The input columns follow packed position.
  - The projection rows are the outputs, then one gate row for each ReGLU in row order, then one index row for each index read in row order.
  - An index read output row stores no weights and the bias 0. A candidate table packs at its first read. Evenly spaced positions store `CandidateBase`, `CandidateStride` and `UnsignedIndices.Empty`.
  - `FindLiveOperations` pops a stack and adds the candidates of a table once.
- Handwired tests to port (into `TensorCompilerTests.cs` unless the PHASE2 row names a phase 1 test):
  - numeric-ui-core.test.ts: "compiled tensor constants use native FP32 storage", "compiled indices use the smallest safe unsigned width", "tensor layers gather only the values they read", and the compile parts of "identity affine operations return an alias without allocating a wire" and "a sum shared by many consumers stays one materialized wire".
  - index-read.test.ts: "recomputing depths without lowered gathers reproduces the build-time program" (with `AssertSameProgram`), and the `ValidateTensorProgram` part of "validators reject malformed index read programs and circuits".
  - Sharpwired cases with no Handwired test: each `ValidateTensorProgram` message that a corrupted copy can reach without gathers, for example "tensor width mismatch", "tensor wire must be written exactly once", "sparse row offsets must be ordered", "every tensor root must be present".
- PHASE2 rows closed: plan task 2 "compiled tensor constants use native FP32 storage", "compiled indices use the smallest safe unsigned width", "tensor layers gather only the values they read", "a sum shared by many consumers stays one materialized wire", "identity affine operations return an alias without allocating a wire"; plan task 3 "recomputing depths without lowered gathers reproduces the build-time program", "validators reject malformed index read programs and circuits".

### Task 2: the tensor evaluator and the reusable evaluators

- Files: `TensorEvaluator.cs`, `Circuit.Tensor.cs`, `Validation.Tensor.cs` (`ValidateTensorEvaluation`), `Circuit.cs`, `Circuit.IndexRead.cs`, `Circuit.Reference.cs`, `TensorEvaluatorTests.cs`, `TensorValidationTests.cs`, and the phase 1 test files that the rows name.
- Port: `TensorWorkspace`, `createTensorWorkspace`, `executeTensorProgram`, `sparseMatmulAddBias`, `applyIndexReads`, `evaluateTensorProgram`, `Circuit.evaluateTensor`, `Circuit.createTensorEvaluator`, `Circuit.createTensorProgramEvaluator`, `validateTensorEvaluation`, and the `tensorProgram` field with its resets.
- The cache reset. Handwired sets `tensorProgram = null` at three places only:
  - `assumeOneHot` (:334), after it adds the group.
  - `indexRead` (:553), after it adds the operation.
  - `append` (:645), after it adds the operation.

  An expression that the builder finds in its cache returns before the reset. `assumeRange`, `assumeGrid`, `assumeBit` and `markRoundingPoint` do not reset the cache, and `inlineCircuit` resets it through `append`, `indexRead` and `assumeOneHot`. Port the same three resets: `AssumeOneHot`, `Append` and `IndexReadAffine`. Do not add a reset to the other declarations. The compiler reads no grid, no exact bit and no rounding point record. A range that narrows later can only allow more gather lowering, and the cached dense program stays exact.
- `ValidateTensorEvaluation` compares with `==` on `float`, because Handwired uses `===`. It calls `EvaluateOperationStream`, which does not check the source ranges, and it reads `TensorProgram ??= CompileTensorProgram()`.
- Handwired tests to port:
  - numeric-ui-core.test.ts: "a program's evaluator exposes the packed values of its last evaluation: the source cells, then every live operation in wireOrder" (new test, with `Assert.Same` on `PackedValues`), and the tensor assertions of "tensor layers append only their new values", "the reference and tensor evaluators round every arithmetic step", "tensor and reference evaluators both reject a wrong-sized source table", "weighted sums normalize duplicate, zero, and unordered terms", "duplicate-wire terms merge into one exact FP32 coefficient", "constant terms and constant gates fold before compilation", "tensor roots remove operations that cannot affect an output", "affine outputs do not allocate fake gate rows", "boolean outputs make complete decisions with an explicit tie rule".
  - numeric-semantics.test.ts: the tensor assertions of "explicit rounding survives consumers, duplicate terms, and circuit inlining", "explicit term order survives source permutation during inlining", "selection preserves finite extreme operands and its intended derivative".
  - macro-ranges.test.ts: the tensor assertion in each helper (`evaluateMaximum`, `evaluateMinimum`, `evaluateSelect`, `evaluateMultiply`, `evaluateClampZeroOne`), and "a proven-nonnegative operand lowers emitMultiply to one reglu".
  - Sharpwired cases with no Handwired test: the cache resets above, each with a test that the cache is null after the reset point and stays set after an expression cache hit and after `AssumeRange`, `AssumeGrid`, `AssumeBit` and `MarkRoundingPoint`; `ValidateTensorEvaluation` refuses a changed tensor value with "does not match the reference FP32 atom program" and a wrong width with "tensor result width mismatch".
- PHASE2 rows closed: every plan task 2 row that task 1 and task 3 do not close.

### Task 3: gather lowering

- Files: `Circuit.GatherLowering.cs`, `Circuit.Compile.cs`, `TensorEvaluator.cs`, `Validation.Tensor.cs`, `TensorChecks.cs` (`GatherStatistics` with `gatherBytes`, `LoweredWires`), `Js.cs` (`SameNumericValue`), `GatherLoweringTests.cs`, `GatherLoweringTests.Magnitudes.cs`, `GatherLoweringTests.Seeded.cs`, `NumericSemanticsTests.cs`.
- Port: `GatherPlan`, `MaskedProduct`, `planGatherLowering`, `attemptGatherMatch`, `parseMaskedProduct`, `resolveAffineSide`, `hasFiniteRange`, the gather branch of `findLiveOperations` and of `computeCompiledDepths`, the gather rows and the slot allocation of `compileTensorProgram`, `TensorGatherCompute`, `TensorGatherRead`, `applyGathers`, the gather checks of `validateTensorProgram`. Add the `lowerGathers` parameter: `CompileTensorProgram(roots, lowerGathers = true, recomputeDepths = false)`.
- Rules to keep:
  - `attemptGatherMatch` places fixed terms first, then flexible terms in term order.
  - The negative arm map is keyed by the pair (left wire, gate wire).
  - `planGatherLowering` stops at the first group that matches, in the order of `candidateGroups`.
  - The first read of a group in a layer gets the next slot number. The executor resolves each slot once, before the reads of the layer.
- Handwired tests to port: all of gather-lowering.test.ts except the HandwiredOS test, that is:
  - recognition: "a one-hot-masked sum lowers to one gather and drops its products", "a bias on the masked sum survives lowering exactly", "signed values lower through the mixed-sign product pair", "the bit-in-gate orientation lowers for values that may be negative", "sums that are not pure masked reads stay dense and stay exact", "a sum mixing products of two different groups stays dense", "a hand-written bit-in-left product over a signed value is rejected", "a size-1 group lowers its biased read and aliases its bare read", "many reads against one group share a single index resolution", "identical reads deduplicate to one wire and one gather", "a gather output can be the candidate of another gather", "an all-zero-allowed group multiplies the gathered value by its mass", "candidates with unbounded declared ranges are refused in every orientation", "an ignorable filler term over an unbounded value blocks lowering", "a result that is exactly zero may carry the other sign of zero", "lowering can be turned off per compile", "gather outputs feed downstream macros exactly";
  - the tensor parts of the 4 phase 1 tests: "recognition survives the affine inlining of argmax selection wires", "fractional first-active selections never lower a masked sum", "inlineCircuit carries declared groups into the target circuit", "a violated one-hot contract diverges and validation catches it";
  - "adversarial value magnitudes / extreme candidates gather bit-for-bit";
  - "library circuits": "stored weights per read drop from 3N to zero under lowering", "lowered and dense programs agree with the reference across selections";
  - "seeded circuits with gathers match the reference on every wire", with the same seed, 3,000 circuits and the same coverage floors.
  - numeric-semantics.test.ts: "reusable tensor evaluation returns only roots and refreshes every call" (new test, with `Assert.Same` on the result), and the tensor assertions of "overflowing unselected candidates do not lower to gather" and "min and max ranges describe their arithmetic, not mathematical bounds".
  - Sharpwired cases with no Handwired test: each gather message of `ValidateTensorProgram` on a corrupted copy ("gather slot must be resolved exactly once", "gather read must follow its slot resolution", "gather output row must store no weights", "every gather slot must be resolved"); `JsToInt32` on a group member that evaluates to +Infinity (see the table of Handwired behavior with no direct C# form).
- Not ported: gather-lowering.test.ts "HandwiredOS / window-manager declares its stacking order as 6 one-hot groups, and none of its sums lowers". It builds `src/handwiredos/window-manager.ts`, which is not part of `src/runtime` and has no Sharpwired port. See open question 2.
- PHASE2 rows closed: plan task 6 gather-lowering rows (the 4 named rows and "every other test of the file"); plan task 2 "overflowing unselected candidates do not lower to gather", "reusable tensor evaluation returns only roots and refreshes every call"; plan task 4 "min and max ranges describe their arithmetic, not mathematical bounds".

### Task 4: index reads, comparisons and the real cost numbers

- Files: `TensorChecks.cs` (`EveryCompileMode`, `TensorAgreement`, `Cost`), `IndexReadTests.cs`, `IndexReadTests.Selection.cs`, `IndexReadTests.Backward.cs`, `ComparisonTests.cs`, `RoundingPointTests.cs`, `NumericSemanticsTests.cs`, `MacroRangesTests.cs`, `SelectionTests.cs`, `SpringMatrixTests.cs`. Production code changes only when a ported test fails.
- Port the helpers:
  - `EveryCompileMode(circuit, roots, sources, CompileModes.IndexRead)` gives the 5 modes of index-read.test.ts and both reusable evaluators (with and without `recomputeDepths`). `CompileModes.SpringMatrix` gives the 4 modes of spring-matrix.test.ts and the reusable evaluator without `recomputeDepths`. Each mode calls `ValidateCircuit`, `ValidateTensorProgram`, sets `circuit.TensorProgram`, and compares each wire of `WireOrder` with `Js.Is`. The helper puts back the previous `TensorProgram` and returns the reference values. It replaces `IndexReadTests.EvaluateAll`.
  - `TensorAgreement(circuit, roots, sources)` compiles with `lowerGathers` true and false, validates, and compares each wire of `WireOrder` with `Js.Is`. At the end it sets `circuit.TensorProgram` to null.
  - `Cost(circuit, root)` is `(ValueCount - SourceSize, Layers.Length)` of `CompileTensorProgram([root])`.
- Remove the phase 1 stand-in. Delete `ComparisonTests.Cost` and `ComparisonTests.LiveOperations` (`ComparisonTests.cs:109-142`). Change each caller to `TensorChecks.Cost` or `TensorChecks.ProgramCounts`: 13 calls in `ComparisonTests.cs`, 1 in `MacroRangesTests.cs`, 2 in `SelectionTests.cs`, 2 in `SpringMatrixTests.cs`. The test at `ComparisonTests.cs:665` reads `WireOrder[SourceSize..ValueCount)` of `CompileTensorProgram([bit])` in place of `LiveOperations`. Each cost number stays the number that Handwired asserts.
- Handwired tests to port: the tensor assertions of each index-read.test.ts and comparison.test.ts row of the PHASE2 list, and index-read.test.ts "recomputed depths: a lowered gather read stops waiting for the products it replaced" (new test, which also checks that `recomputeDepths: false` gives the same program with `AssertSameProgram`).
- PHASE2 rows closed: every plan task 3 row that task 1 did not close; every plan task 4 row that task 3 did not close.

### Task 5: springs, selection, inlining and the hardened random tests

- Files: `SpringMatrixTests.cs`, `MacroRangesTests.cs`, `SelectionTests.cs`, `InlineTests.cs`. Production code changes only when a ported test fails.
- Handwired tests to port (the tensor assertions of the PHASE2 rows):
  - spring-matrix.test.ts: `ProgramCounts` with `storedWeights` (14 operations, 3 layers, 28 weights for `emitApplySpringMatrix`; 54, 57, 102 and 264 weights for the animated objects); every compile mode (`CompileModes.SpringMatrix`) for the first 40 and the first 30 sources; each sample evaluates with `CreateTensorEvaluator(roots)` where Handwired does.
  - macro-ranges.test.ts: the 240-frame check of the two settled-branch tests with `EvaluateTensor`; `CompileTensorProgram([position, velocity, active])` has 46 live operations in 33 layers.
  - numeric-ui-core.test.ts: "inlined circuits become one graph and share matching expressions", "first-active selection resolves ordered candidates through a balanced tree", "argmax resolves continuous scores through a balanced tree", "argmax breaks exact ties toward the lower-indexed candidate".
  - random-hardened.test.ts: "non-dyadic macro circuits preserve every live FP32 wire" (rename the Sharpwired test to match, because it now checks each wire of `WireOrder` with `Js.Is` and calls `ValidateTensorEvaluation`), "boolean rounds adjacent-ulp scores identically in both evaluators", and the three "argmax sub-margin and exact ties" tests.
  - The row "spring-matrix.test.ts / inlining keeps the values and the rounding points" has no tensor assertion. Mark it done with that reason.
- PHASE2 rows closed: every plan task 5 row; every plan task 6 row that task 3 did not close.

### Task 6: the differential test (R2.4) and the close of the PHASE2 list

- Files: `TensorDifferentialTests.cs`, `ApiShapeTests.cs`, `PHASE2-TENSOR-ASSERTIONS.md` (deleted).
- The differential test `Seeded_random_programs_give_the_reference_value_on_every_live_wire_in_every_compile_mode`:
  - Seed: a fixed `Js.CreateRandom` seed. Build 600 circuits of 4 to 12 source cells and 20 to 60 builder calls.
  - Source declarations: finite ranges, ranges with no bound, grids, exact bits, and one-hot groups (with and without `allowAllZero`, declared before and after their readers).
  - Builder calls, chosen at random: `Affine` with 1 to 6 terms (duplicate wires, zero coefficients, the coefficients 1/3, -0.7, 2^-20, 12,345.6789), `RoundAffine`, `Constant`, `Reglu`, `Boolean`, `Multiply`, `Select`, `Minimum`, `Maximum`, `MinimumExact`, `MaximumExact`, `Compare` (grid and sign cascade, with and without `endWithBoolean`), `IndexRead` and `IndexReadAffine` with both out-of-range rules, `Argmax` and `FirstActive` with masked sums over their selections, masked sums over the declared groups in each product orientation, and `InlineCircuit` of a small random child.
  - Sources: 4 tables for each circuit. A cell gets a random value in its range, an end of its range, `±0`, the smallest subnormal or the largest FP32 value in its range. A cell with no bound can also get `±Infinity`. A group gets a valid one-hot, or all zeros where the group allows it.
  - Roots: 1 to 5 random operation wires.
  - Check, for each circuit: `ValidateCircuit`; for each of the 5 index-read compile modes, `ValidateTensorProgram`, then `EvaluateTensor(source, program)` on each source, compared with `EvaluateReference(source)` on each wire of `WireOrder`. The comparison is `Js.Is` for the modes without gather lowering, and `Js.SameNumericValue` for the modes with gather lowering (open question 1). Also check both reusable evaluators on the roots, and `ValidateTensorEvaluation` when no reference value on `WireOrder` is NaN.
  - Coverage floors: assert a minimum count of compared wires, gather reads, index reads, NaN wires, signed-zero wires and of each operation kind. Run the test once, then set each floor to half of the measured count.
- `ApiShapeTests`: the public program types expose no writable array (`ImmutableArray<float>` and `UnsignedIndices` only), and `UnsignedIndices` has no public setter.
- Check each row of `PHASE2-TENSOR-ASSERTIONS.md` against the tasks above. Each row must map to a test that now holds its tensor assertion, or to the reason in task 3 or task 5. Then delete the file.
- Run the full verification below.

## Map of the PHASE2 list

| PHASE2 section | Rows | Task |
| --- | --- | --- |
| Plan task 2 | constant storage, index widths, gather only the values they read, a shared sum, identity alias | 1 |
| Plan task 2 | overflowing unselected candidates, reusable tensor evaluation | 3 |
| Plan task 2 | every other row | 2 |
| Plan task 3 | recomputing depths without lowered gathers, validators reject malformed index read programs | 1 |
| Plan task 3 | every other row | 4 |
| Plan task 4 | min and max ranges | 3 |
| Plan task 4 | every other row | 4 |
| Plan task 5 | every row | 5 |
| Plan task 6 | the 4 gather-lowering rows and "every other test of the file" | 3 |
| Plan task 6 | every other row | 5 |

The Handwired tests of `cells.test.ts` and `prefix-networks.test.ts` also call the compiler and the reusable evaluator. They belong to phase 3 (R3.1, R3.3), which ports them with the phase 2 API. The spring-matrix snap test needs `advanceFrame` and is on the PHASE3 list.

## Verification

- `dotnet build -warnaserror` passes.
- `dotnet test` passes.
- `dotnet test -c Release` passes.
- `grep -rn "TODO\|Skip =" src tests` gives no result.
- `tests/Sharpwired.Core.Tests/PHASE2-TENSOR-ASSERTIONS.md` does not exist.
- Each new source file is below 800 lines.
- Each R2 item maps to at least one test:

| Item | Tests |
| --- | --- |
| R2.1 layers of sparse matrices, dead operation removal, `wireOrder`, `valueCount`, `sourceSize` | `TensorCompilerTests` (constant storage, index widths, gather only the values they read), `CircuitTests.Tensor_layers_append_only_their_new_values`, `CircuitTests.Tensor_roots_remove_operations_that_cannot_affect_an_output`, `CircuitTests.Affine_outputs_do_not_allocate_fake_gate_rows` |
| R2.2 gather lowering, index read tables with base and stride, `recomputeDepths` | `GatherLoweringTests` (all files), `IndexReadTests.Evenly_spaced_and_scattered_tables_read_the_selected_candidate`, the two index-read `recomputeDepths` tests |
| R2.3 `TensorEvaluator`, `CreateTensorEvaluator`, `CreateTensorProgramEvaluator` with `PackedValues`, `ValidateTensorEvaluation`, `ValidateTensorProgram` | `TensorEvaluatorTests` (packed values, reusable evaluation, cache resets), `TensorValidationTests`, `IndexReadTests.Validators_reject_malformed_index_read_circuits` |
| R2.4 differential tests and the recorded phase 1 assertions | `TensorDifferentialTests`, `GatherLoweringTests.Seeded_circuits_with_gathers_match_the_reference_on_every_wire`, `SelectionTests.Non_dyadic_macro_circuits_preserve_every_live_FP32_wire`, `IndexReadTests.Random_circuits_mixing_index_reads_products_selects_and_gathers_read_the_plain_definition`, and the deleted PHASE2 list |
| RA.1 | `NOTICE` keeps the Handwired MIT notice (no change) |
| RA.2 | Each new comment and doc in ASD-STE100 |
| RA.3 | The green `dotnet test` run at the end of each task |

## Open questions

1. Gather lowering can change the sign of an exactly zero result (execution-model.md, "One-hot groups and gathers", and the Handwired test "a result that is exactly zero may carry the other sign of zero"). A strict `Js.Is` check on a gather-lowered program fails on such a wire. The plan uses `Js.Is` for the modes without gather lowering, and `Js.SameNumericValue` (`==`, or both NaN) for the modes with gather lowering, as the Handwired seeded gather test does. The controller must approve this exception to R2.4.
2. The HandwiredOS test of gather-lowering.test.ts needs `src/handwiredos/window-manager.ts`. Sharpwired has no port of HandwiredOS. The plan does not port the test. The controller must approve this exclusion, or name a replacement test.
3. `TensorEvaluator.Evaluate` and `PackedValues` return writable `float[]` instances that the next call overwrites, as Handwired does. A read-only type (`ReadOnlySpan<float>` or `ReadOnlyMemory<float>`) cannot show the same-instance property that the Handwired tests check. The plan uses `float[]`. The controller can choose a read-only type instead, and the two Handwired identity assertions then change.
