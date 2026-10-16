# Phase 1 plan: the core runtime

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R1.1 to R1.14, RA.1 to RA.3.

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The port source is `$REF/src/runtime/numeric-ui-core.ts`. The behavior reference is `$REF/docs/execution-model.md` and `$REF/docs/expressions.md`.

## Global constraints

- Port behavior exactly. Each FP32 step casts to `float`, as `Math.fround` does in Handwired. Compute float64 parts (range bounds, `floor` of an index) in `double`, as Handwired does.
- Each operation keeps the Handwired term order, so that the reference evaluator gives bit-identical values.
- Keep Handwired error conditions. Throw `InvalidOperationException` or `ArgumentException` with a message that names the same fact as the Handwired message.
- TDD: write the failing xUnit test first, run it, see it fail, then implement.
- Do not implement the tensor compiler or the tensor evaluator in phase 1. A Handwired test that compares with the tensor evaluator is ported with the reference evaluator only. List each such test in `tests/Sharpwired.Core.Tests/PHASE2-TENSOR-ASSERTIONS.md` (file, test name, the tensor assertion that phase 2 adds).
- No `TODO`, no stub, no skipped test.

## Files

```txt
Sharpwired.slnx
Directory.Build.props                         net10.0, Nullable, TreatWarningsAsErrors, LangVersion latest
src/Sharpwired.Core/Sharpwired.Core.csproj
src/Sharpwired.Core/Wire.cs                   Wire, Term, NumericRange, OutOfRange, RoundingPointKind
src/Sharpwired.Core/Operation.cs              the operation records and macro records
src/Sharpwired.Core/Circuit.cs                declarations, builder methods, inference, rounding points
src/Sharpwired.Core/Circuit.IndexRead.cs      index reads and candidate tables
src/Sharpwired.Core/Circuit.Reference.cs      EvaluateReference, BackpropagateReference
src/Sharpwired.Core/Fp32.cs                   FP32 helpers: Round, boolean atom, power-of-two helpers
src/Sharpwired.Core/Macros.cs                 Relu, Multiply, Select, Minimum, Maximum
src/Sharpwired.Core/Macros.Compare.cs         Compare, integer bits, sign cascade, exact min, max, clamp
src/Sharpwired.Core/Macros.Spring.cs          Spring, ApplySpringMatrix, AnimatedObject
src/Sharpwired.Core/Macros.Selection.cs       Argmax, FirstActive
src/Sharpwired.Core/Inline.cs                 InlineCircuit
src/Sharpwired.Core/Validation.cs             ValidateCircuit and the value validators
src/Sharpwired.Core/SpringMatrix.cs           float64 SpringStepMatrix, SpringMatrixPower, SpringMatrixBound (from host-timing.ts), for tests and hosts
tests/Sharpwired.Core.Tests/...               one test file per area
```

A task can split a file if it passes 800 lines. Keep the names above as the entry points.

## Tasks

The tasks run in order. Each task builds on the previous task.

### Task 1: scaffold and FP32 semantics

- Create the solution, `Directory.Build.props`, the core project and the xUnit test project. Add `.claude/worktrees/` to `.gitignore`.
- Tests (`Fp32SemanticsTests.cs`):
  - `(float)((float)a * (float)b + (float)c)` equals the value with a separate rounding of the product, for inputs where an FMA gives a different result. Find such inputs with `Math.FusedMultiplyAdd` and assert that the two results differ, so that the test proves that no contraction happens.
  - `(score + 8388609f) - 8388609f` rounds scores in 0..1 to 0 or 1 at 0.5, as Handwired `evaluateBoolean` does.
  - Subnormal values survive: `float.Epsilon > 0` and `1.4e-45f * 2 == 2.8e-45f`.
- Done when `dotnet test` passes.

### Task 2: Wire, Circuit and the reference evaluator

- Port from `numeric-ui-core.ts`: `Wire`, `asWire`, `Terms`, the `Operation` types, `Circuit` fields and constructor, `Source`, `Constant`, `Affine`, `RoundAffine`, `Reglu`, `Boolean`, `Multiply`, `Select` (the circuit methods with macro records), the declarations, `MarkRoundingPoint`, `RoundingPoints`, `IsNonNegative`, range inference (`evaluateAffineRange`, `multiplyByReluRange`), grid inference (`inferFormGrid`, `inferOperationGrid`), affine composition (`composeAffineTerms`, `foldConstantTerms`, `normalizeTerms`, `identityWire`, the expression cache), `EvaluateReference`, `ValidateSourceRanges`, `ValidateCircuit`.
- Tests: port the matching cases of `$REF/tests/runtime/numeric-ui-core.test.ts`, `numeric-semantics.test.ts` and `macro-ranges.test.ts` (the cases that use only these parts).

### Task 3: index reads

- Port `indexRead`, `indexReadAffine`, `registerCandidateTable`, `indexReadRange`, `reachableCandidates`, the reference evaluation of index reads, and `ValidateIndexReads`.
- Arrays: Handwired keys candidate tables by array identity and freezes the array. In C#, take `IReadOnlyList<Wire>`, copy it into an immutable table, and key the cache by content. Keep the Handwired rule that identical tables share one table.
- Tests: port the reference-evaluator cases of `$REF/tests/runtime/index-read.test.ts`.

### Task 4: the basic macros and Compare

- Port `emitRelu`, `emitMultiply`, `emitMaximum`, `emitMinimum`, `emitSelect`, `emitCompare` with all its helpers (grid compare, integer bit, sign cascade plan, `endWithBoolean`), `emitMinimumExact`, `emitMaximumExact`, `emitClampExact`.
- Tests: port `$REF/tests/runtime/comparison.test.ts` and the macro cases of `macro-ranges.test.ts` and `numeric-semantics.test.ts`. Include the cost table of `execution-model.md#comparisons` as assertions on operation counts.

### Task 5: springs

- Port `SpringStepMatrix`, `SpringMatrixPower`, `SpringMatrixBound` from `$REF/src/runtime/host-timing.ts` into `SpringMatrix.cs` (float64). Port `emitSpring`, `emitApplySpringMatrix`, `emitAnimatedObject`, `emitAbsolute`, `emitClampZeroOne`.
- Tests: port the cases of `$REF/tests/runtime/spring-matrix.test.ts` that do not need `advanceFrame`. List the `advanceFrame` cases for phase 3 in `tests/Sharpwired.Core.Tests/PHASE3-TIMING-ASSERTIONS.md`.

### Task 6: selection, inlining and validators

- Port `emitArgmax`, `routeArgmax`, `emitFirstActive`, `isProvenBit`, `isBitByConstruction`, `routeWinner`, `inlineCircuit`, `validateOneHotGroups`, `validateGrids`, `validateNoNaN`, `describeFirstNaN`, and the `assert*` helpers.
- Tests: port the matching cases of `numeric-ui-core.test.ts`, `numeric-semantics.test.ts`, `builder-differential.test.ts` (reference parts) and `random-hardened.test.ts` (reference parts).

### Task 7: the reference backward pass

- Port `backpropagateReference` with the `booleanGradient` and `indexGradient` options, and the gradients through the multiply and select records.
- Tests: port each backward-pass case in `$REF/tests/runtime/` and add central-difference checks for `Multiply`, `Select` and `ApplySpringMatrix`.

## Verification

- `dotnet build -warnaserror` and `dotnet test` pass.
- Each checklist item R1.1 to R1.14 maps to at least one test.
