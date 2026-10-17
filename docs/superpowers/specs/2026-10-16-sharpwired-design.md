# Sharpwired: spec

The approved design is [docs/design.md](../../design.md). The user approved it on 2026-10-16, with two changes that the design already holds:

- The GPU backend is WebGPU (wgpu-native through Silk.NET.WebGPU), because ILGPU has no Metal backend.
- Loops with a runtime trip count are supported: an unroll to a declared maximum, or a recurrent step program.

The reference source is Handwired at commit HEAD of `https://github.com/chenglou/handwired` (cloned on 2026-10-16). "Handwired" below means that source.

## Requirement checklist

This checklist is the definition of done. Each phase owns the items under it.

### Phase 1: core runtime (`src/Sharpwired.Core`)

- [x] R1.1 `Wire` is a distinct type. A plain `int` does not convert to a `Wire` implicitly.
- [x] R1.2 `Circuit` holds the four instructions (`affine`, `reglu`, `boolean`, `index`) with the FP32 (32-bit float) rules of Handwired `docs/execution-model.md`.
- [x] R1.3 The builder composes small affine producers (fewer than 4 terms), folds constants, normalizes term order, and reuses identical expressions, as Handwired does. `RoundAffine` keeps the term order and the operand wires.
- [x] R1.4 Range inference follows each FP32 rounding step. Grid inference follows Handwired rules.
- [x] R1.5 Declarations: `AssumeRange`, `AssumeGrid`, `AssumeOneHot`, `AssumeBit`, with the "existing readers" refusal and the `allowExistingReaders` option.
- [x] R1.6 Rounding points: the four kinds, `MarkRoundingPoint`, `RoundingPoints()`.
- [x] R1.7 Index reads: `IndexRead`, `IndexReadAffine`, the `clamp` and `zero` rules, candidate tables, the ±2^24 index range contract.
- [x] R1.8 The reference evaluator gives the same value as Handwired `evaluateReference` for each wire.
- [x] R1.9 Macros, each with its Handwired exactness contract: relu, multiply, select, minimum, maximum, compare (integer bits, sign cascade, `endWithBoolean`), the exact minimum, maximum and clamp, spring, spring matrix, animated object, argmax, first active.
- [x] R1.10 `InlineCircuit`, with the transfer of declarations, groups and rounding points.
- [x] R1.11 Validators: `ValidateCircuit`, `ValidateOneHotGroups`, `ValidateIndexReads`, `ValidateGrids`, `ValidateNoNaN`, `DescribeFirstNaN`.
- [x] R1.12 The reference backward pass, with the `booleanGradient` and `indexGradient` options and the macro records for multiply and select.
- [x] R1.13 A test proves that .NET `float` arithmetic matches `Math.fround` semantics and does not contract a multiply and an add into an FMA (fused multiply-add).
- [x] R1.14 The Handwired tests of `tests/runtime/` that cover the phase 1 parts are ported to xUnit. Each assertion that needs the tensor evaluator is recorded for phase 2.

### Phase 2: compiler and tensor evaluator

- [x] R2.1 The compiler builds layers of sparse matrices, removes dead operations, and reports `wireOrder`, `valueCount` and `sourceSize`.
- [x] R2.2 Gather lowering for declared one-hot groups, index read tables with base and stride, and the `recomputeDepths` option.
- [x] R2.3 `TensorEvaluator`, `CreateTensorEvaluator`, `CreateTensorProgramEvaluator` with `PackedValues`, and `ValidateTensorEvaluation`, `ValidateTensorProgram`.
- [x] R2.4 Differential tests: seeded random programs give equal values in the reference evaluator and the tensor evaluator. The recorded phase 1 assertions are ported.

### Phase 3: cells, timing, toggle with the builder API

- [x] R3.1 `SourceTable` and the cell functions (`Constant`, `Input`, `InputBit`, `Event`, `State`, `StateBit`, `List`, `OneHot`, `Permutation`, `AtMostOne`), `Next`, `Finish`, the validator.
- [x] R3.2 Host timing: frame planning (`AdvanceFrame`), the two write-back policies, settle checks, snaps, spring matrix functions, `QuantizeFrameTiming`.
- [x] R3.3 Prefix networks.
- [x] R3.4 The toggle switch with the builder API, a Raylib-cs window host, and a test against a plain C# reference over a scripted trace.

### Phase 4: the IL translator

- [x] R4.1 `[Handwired]` methods in C# and in F# translate to a `Circuit`.
- [x] R4.2 Types: `float`, `int`, `bool`, structs and records of these, fixed-length arrays. Attributes: `[Range]`, `[Grid]`, `[Event]`, `[State]`, `[OneHot]`, `[MaxIterations]`.
- [x] R4.3 Steps: IL decode, control-flow graph, SSA (static single assignment) form, inlining, loop lowering in three forms, branch to select conversion, operation lowering, `Hw` intrinsics.
- [x] R4.4 A refused construct gives an error with the source file and line from the PDB (program database).
- [x] R4.5 The toggle as a plain C# method and as a plain F# function. The translated programs equal the plain methods over the scripted trace.

### Phase 5: the WebGPU backend

- [ ] R5.1 A WGSL (WebGPU Shading Language) writer with one `f32` statement per operation, and a writer that holds proven integers and bits as `u32` values.
- [ ] R5.2 A batch runner over many source tables, through wgpu-native.
- [ ] R5.3 A test runs the boolean atom, a round chain and a sign cascade on each available adapter and compares with the reference evaluator.
- [ ] R5.4 The recurrent step form of phase 4 runs on the GPU.

### Phase 6: the JSON search demo

- [ ] R6.1 The document generator (seeded, 7 kinds, valid JSON of up to 512 code points).
- [ ] R6.2 The JSON highlighter program and the regex search program in sizes of 8, 16 and 32 states, as in Handwired `src/json-search`.
- [ ] R6.3 GPU and CPU paths over 100,000 documents, with timings next to .NET `Regex`.
- [ ] R6.4 A Raylib-cs window: the pattern field, the scope toggle (all, keys, string values), the backend toggle, the match map, and one document with colors and marks.
- [ ] R6.5 Tests that run without a GPU compare the programs with a plain C# reference.

### All phases

- [ ] RA.1 The `NOTICE` file keeps the Handwired MIT notice.
- [ ] RA.2 Docs and code comments use ASD-STE100.
- [ ] RA.3 Each phase ends with a green `dotnet test` run.

## Conventions

- .NET 10, C# with `Nullable` and `TreatWarningsAsErrors` on. F# only in F# samples and F# test inputs.
- xUnit for tests. One test project per source project, under `tests/`.
- Macros are extension methods on `Circuit` in the static class `Macros`, for example `circuit.Compare(a, Relation.GreaterOrEqual, b)`.
- Each ported file names its Handwired source file in a header comment.
