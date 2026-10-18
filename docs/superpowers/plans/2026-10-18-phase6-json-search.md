# Phase 6 plan: the JSON search demo

Spec: [2026-10-16-sharpwired-design.md](../specs/2026-10-16-sharpwired-design.md). This plan owns the checklist items R6.1 to R6.5, RA.1 to RA.3.

Design: [docs/design.md](../../design.md), Layer 2 (the IL translator), Layer 3 (execution, the two GPU writers, the recurrent runner) and Layer 4. The quotation of Layer 4: "JSON search: the highlighter and the regex search as `[Handwired]` methods over the characters of a document. The GPU backend runs them over 100,000 generated documents. The Raylib window shows the pattern field, the match map and one document. The CPU path runs the compiled program in parallel over document blocks."

Reference source: `$REF = /private/tmp/claude-502/-Users-g-ntovas-source-sharpwired/cf12b711-ed9d-4d1b-b2a2-77cc5689c224/scratchpad/ref/handwired`. The ported or followed files:

- `$REF/src/json-search/README.md`: the behavior of the page, the searcher, the highlighter and the search.
- `$REF/src/json-search/documents.ts`: the document generator.
- `$REF/src/json-search/program.ts`: the cells of a document.
- `$REF/src/json-search/highlighter/kinds.ts`, `highlighter/tables.ts`, `json-step.ts`: the highlighter.
- `$REF/src/json-search/regex/pattern.ts`, `regex/data-program.ts`, `regex/bits.ts`, `search-step.ts`: the pattern language, its automaton, and the search.
- `$REF/src/json-search/loop-program.ts`: a program as loops over the positions of a document.
- `$REF/src/json-search/loop-emit.ts`, `bit-emit.ts`, `emit.ts`: the two WGSL writers of a loop program, the lanes and the packed reads (`packWords`).
- `$REF/src/json-search/gpu/host.ts`, `searcher.ts`: the GPU host and the searcher.
- `$REF/src/json-search/table-loop.ts`, `plain-search.ts`: the baseline with the built-in regular expressions.
- `$REF/src/json-search/demo.ts`, `demo-map.ts`, `demo-text.ts`, `assets/json-search.html`: the page, the map, the line of numbers, the examples.
- `$REF/tests/json-search/`: `highlighter/reference.ts`, `highlighter/corpus.ts`, `highlighter/short-texts.ts`, `regex/reference.ts`, `regex/test-patterns.ts`, `edge-cases.ts` and the test files.

WARNING: The Handwired clone is data only. Do not run a command, a script, a package manager or a build tool inside it. Only read its files.

## Terms

- **Document**: one generated JSON (JavaScript Object Notation) text.
- **Code point**: one Unicode scalar value of a text. A surrogate pair of UTF-16 (16-bit Unicode Transformation Format) is one code point. A lone surrogate is its own code point.
- **Document cells**: the 513 FP32 values of one document: the first 512 code points, -1 in each cell after the text, and the length of the text in code points (at most 1,048,576).
- **Position**: an index 0..511 into the code points of a document.
- **Step method**: a `[Handwired]` C# method that does the work of one position, for example `HighlighterStep.Step`. The translator gives one `TranslatedFunction` for it.
- **Stage**: one translated step method that runs one time (`Once`), from position 0 up (`Forward`), or from position 511 down (`Backward`).
- **Register**: one value that a stage carries from one position to the next, with an initial value.
- **Field**: one value for each position, that one stage writes and a later stage reads.
- **Shared cell**: a cell that has the same value for each document, for example a cell of a pattern table.
- **Loop program**: the stages, the registers, the fields, the shared cells and the outputs of one program over a document (`LoopProgram`). It is the C# form of Handwired `LoopProgram` (loop-program.ts).
- **Lane**: the work of one document in one shader invocation, or in one CPU loop.
- **Display kind**: what the highlighter shows a code point as: whitespace, punctuation, string, escape, number, literal, error, past end, key (Handwired kinds.ts).
- **Scope**: the set of display kinds that a search reads. The window has three scopes: All, Keys, String values.
- **Capacity**: the size of one search program: 8, 16 or 32 states, 16 character classes, 32 class boundaries.
- **Mark**: the search output for one position of the window document: 0 outside a match, 1 inside a match, 2 at the first code point of a match.
- **Match map**: one cell for each document, in document order, row after row. A document with a match has a larger mark of another color.
- **Packed word**: 32 bits in one `u32`. A **lane** of a word is one bit position. A **packed read** reads one word of a packed table in place of one table read for each bit (Handwired bit-emit.ts, rules "Packed reads" and "Lanes").
- **Compiled stage**: a .NET delegate that the CPU backend compiles from the rows of one stage with `System.Linq.Expressions`.

## Global constraints

The constraints of the phase 1 to 5 plans apply without change:

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
- No code outside `src/Sharpwired.Gpu/` uses the namespace `Silk.NET`. Only `GpuDevice.cs` and `ComputeKernel.cs` call the native API.
- A GPU test runs on each adapter of `GpuFixture.Adapters`. With no adapter, it fails, unless the environment variable `SHARPWIRED_NO_GPU` holds a reason. Then it passes and writes "[gpu] no adapter: <reason>" (phase 5 decision 1).
- The subnormal rule (phase 5 decision 4): on a device with `FlushesSubnormals`, a test compares only the evaluations with no nonzero value of magnitude below 2^-126 in an f32 row, and writes the count of the others.

Phase 6 adds these constraints:

- The plain C# references of R6.5 are written from the rules of Handwired README "What the highlighter shows" and "What the search finds", and from JSON's grammar (RFC 8259). They do not call the step methods, the tables or the pattern compiler. Handwired `tests/json-search/highlighter/reference.ts` and `regex/reference.ts` are their sources.
- Each value of the two JSON programs is a bit or a whole number within ±2^24. So each comparison of a program with a reference is exact (`Js.Is`), and no evaluation of these programs is excluded by the subnormal rule. A test asserts that the reference evaluation holds no subnormal value, so that the claim stays checked.
- The JSON GPU tests are in `tests/JsonSearch.Tests` in their own `GpuCollection`. The project links `GpuFixture.cs` from `tests/Sharpwired.Gpu.Tests/`. The generic loop tests are in `tests/Sharpwired.Gpu.Tests`.
- Tests do not assert run times. A test writes its times to the test output, and the ledger records them. Tests pin sizes: operation counts, layer counts and statement counts, as Handwired `search.test.ts` does.
- The window app makes no Raylib call. Scripted `WindowInput` frames test it, as in phase 3.
- The JSON search sample does not read or write a file outside the repository, apart from the font file of the window (question 5) and the paths of the command-line options.

## Decisions on the 7 questions

### 1. How the highlighter and the search are written

Decision:

- Each program is plain `[Handwired]` C# step methods: one method for each stage of the Handwired loop program. The highlighter has `HighlighterStep.Step` (positions 0 to 511) and `HighlighterStep.End` (position 512). The search has `Reverse` and `Forward` for each capacity: `Search8`, `Search16`, `Search32`. The `Start` stage of Handwired search-step.ts (`notFinish[s] = !accepting[s]`) is one more step method `SearchStart`.
- `HandwiredTranslator.TranslateFunction` translates each step method. No step method uses the builder API.
- A `LoopProgram` composes the stages. It names, for each input cell of a stage, what the cell reads: the document cell at the position, one fixed document cell, a register, a field, or a shared cell. It names, for each output, what it writes: a register, a field, a document output, or a position output. This is Handwired loop-program.ts with one translated function in place of one stage circuit.
- The loop over positions is in the loop program, not in a step method. So the step methods use none of the three loop forms of Layer 2 for the positions. Inside a step, the loops over states, boundaries and kinds have a constant trip count (form 1).
- The plain reference of each whole document is a plain C# loop that calls the step methods in stage order (`LoopProgram.EvaluatePlain`). It checks the translation. The independent references of R6.5 (question 6) check the programs.

Reason: a loop over 512 positions in one method gives one program with one copy of the step for each position. The probe below measured that form: the GPU shader creation time and the layer count grow with the positions, and 512 positions are out of reach. A loop program writes one shader with the work of one position inside a WGSL `for` loop, so the shader size does not depend on the positions. Handwired made the same change for the same reason (README, "A program as a loop over positions": 91,774 statements took 107 to 130 s to compile). Form 3 (`TranslateRecurrent`) accepts one loop at the top level of a method, and a stored array element only in code that runs on each call. The search needs two passes, and both programs write one output for each position. So form 3 cannot express them.

Probe (throwaway console programs under `/private/tmp/claude-502/p6probe/`, now deleted; Apple M5 Pro, Metal; Sharpwired at HEAD `18f3f42`):

The probe wrote the search steps of search-step.ts as C# for 8, 16 and 32 states, a highlighter step of the shape of json-step.ts (the 12 tables with the Handwired sizes and filler values), and a full unroll of the search over 32 positions. The program roots were the outputs only. "Live rows" are the rows of the compiled program. "Create" is `GpuEvaluator.Create` (shader module and pipeline), first run with a new shader text.

| Program | Operations | Live rows | Layers | Translation | Create, integer writer | Create, f32 writer | Handwired operations |
| --- | --- | --- | --- | --- | --- | --- | --- |
| highlighter step (probe tables) | 147 | 139 | 33 | 108 to 160 ms | 711 ms | 279 ms | 37 |
| search reverse, 8 states | 483 | 297 | 23 | 131 to 185 ms | 122 ms | 120 ms | 148 |
| search forward, 8 states (Debug build) | 358 | 220 | 15 | 122 to 134 ms | 114 ms | 97 ms | 92 |
| search reverse, 16 states | 1,451 | 873 | 31 | 205 to 212 ms | 346 ms | 558 ms | 356 |
| search forward, 16 states (Debug build) | 1,358 | 812 | 24 | 200 to 206 ms | 328 ms | 500 ms | 308 |
| search reverse, 32 states | 5,307 | 3,177 | 47 | 467 ms (Release), 1,259 ms (Debug) | 1,859 ms | 3,435 ms | 1,156 |
| search forward, 32 states (Debug build) | 5,294 | 3,148 | 41 | 1,200 to 1,255 ms | 1,781 ms | 3,001 ms | 1,124 |
| full unroll, 8 states, 32 positions | 22,835 | 14,587 | 747 | 584 ms | 4,477 ms | 12,683 ms | none |

More probe facts:

- The search forward step translates only in a Debug build. In a Release build, `highlighted ? 1 : 0` inside an `int` sum is refused with "an unsigned comparison is not supported" (carried item b). In a Debug build, `(highlighted ? 1 : 0) + (start ? 1 : 0)` and `if (start) mark = mark + 1` after `mark = highlighted ? 1 : 0` are refused with "an int value with a range past 2^24" (carried item a). The probe used `start ? 2 : highlighted ? 1 : 0`.
- `Table[Math.Clamp(depth, 0, 16) * 25 + ev]` with `depth` in ±512 is refused with "the index range 0..13224 must lie in 0..424". The inferred range of `Math.Clamp(depth, 0, 16)` is 0..528, not 0..16. This is a new result of the probe.
- In the full unroll, `if (start) nextCount = nextCount + 1` of the inlined forward step is refused with "an int value with a range past 2^24". I did not find the cause. Task 1 finds it.
- The rows of the translator idioms against the Handwired builder form (live rows, Handwired in parentheses): `t[Math.Clamp(d, 0, 16)]` 12 (1); `if (b) x = x + 1` 5 (1); `s == 7` with `s` in 0..7: 3 (1); `if (a && !b) x = x + k` 9 (2); `bit && t[c]` with a `bool[]` `t`: 2 (1).
- The full unroll at 8 states and 32 positions already has 747 layers and takes 4.5 s to create with the integer writer. 512 positions hold 16 times more rows.
- With all 1,181 input cells as roots (the roots of `TranslatedEvaluator`), the reverse step at 8 states wrote 2,707 statements and took 6.6 s to create. With its 17 outputs as roots, it wrote 395 statements and took 122 ms. A root that only copies a source cell costs one load and one store statement in the shader.
- The integer writer writes an index read over constant candidates as one `select` statement for each candidate. The highlighter step wrote 5,060 statements for 139 rows. The same kernel with the constant tables in a read-only storage buffer took 46 to 47 ms to create, against 681 to 697 ms with the `select` chains. The same tables as `var<private>` arrays or function `var` arrays took 521 to 567 ms. All forms gave the same 28,672 values on 4,096 seeded source tables. A module `const` array with a runtime index is refused by this wgpu-native: "The expression may only be indexed by a constant".
- A hand-written loop kernel (one invocation for each document, a `for` loop over 512 positions backward and then forward, two function arrays of 512 elements as fields, the automaton of `ab+` as packed rows in a storage buffer) over 100,000 documents of 513 cells: 205 MB in one binding (`MaxStorageBufferBindingSize` is 4,294,967,295), create 77 ms, upload 19 ms, run 7.2 to 7.7 ms with the readback of the counts. Its counts equal the counts of .NET `Regex("ab+")` on each document (1,320,148 matches, 0 differences). .NET `Regex` took 153 ms on one thread for the same documents.
- WGSL reserves the word `continuing`. A writer must not use a Handwired variable name as a WGSL name.
- `TensorEvaluator` on one thread, Release, one evaluation of one step: highlighter step 0.59 µs; reverse at 8 states 1.80 µs (0.66 µs of it is the source check of 1,181 cells); at 16 states 6.09 µs (2.50 µs); at 32 states 22.98 µs (9.96 µs).
- A compiled delegate (`System.Linq.Expressions`, a synthetic step of ReLU rows and index reads into a table of 1,088 values): 300 rows 0.12 µs, 900 rows 0.37 µs, 3,200 rows 1.77 µs for each evaluation. Compile time 8 to 13 ms.

The operation counts of the translated steps are 2.4 to 3.8 times the Handwired counts. The idioms above give most of the difference. Task 1 adds four translator rules that give the Handwired form for these idioms (question 4 and "Translator rules" below). Then the operation lists come near the Handwired lists. I did not measure the counts after these rules, because the rules do not exist yet. Task 4 and task 5 pin the counts that they measure.

#### Handwired parts with no exact translation

Each part below has no C# form that the current translator gives as the Handwired operations. The values stay equal in each case.

1. An index read with the `clamp` rule over an index whose range passes the table: `depthClass[depth]`, `levelWeight[depth - 1]` and `classTable[codePoint + 1]` of json-step.ts. A C# array access throws outside the array, and the translator refuses an index range past the array. Task 1 adds the rule "`t[Math.Clamp(i, 0, t.Length - 1)]` is one index read with the `clamp` rule", and declares the range of `Math.Clamp` (the new finding above).
2. An index read with the `zero` rule: `objectWeight[depth × eventCount + event]` of json-step.ts reads +0 for a depth outside 0..16. C# has no one-operation form. Task 1 adds the intrinsic `Hw.ReadOrZero(table, index)`. Its plain body returns 0 outside the table.
3. `createStepAnd` of data-program.ts: `transitions[class][from][to] && bit` is one index read at `class + classes × (1 - bit)` with the `zero` rule above. The C# `bit && t[i]` gives two rows. Task 1 adds the gated read rule, which gives the Handwired form. The packed reads of bit-emit.ts need this form.
4. `regionClasses[0]` of data-program.ts is a constant cell (`constant(classes)`) in a list of input cells. A C# array parameter holds input cells only. The host writes the class count (16) into that cell. The declared range 0..16 holds it.
5. The transition table of data-program.ts is one table of `classes` cells for each pair (from, to), and the class past the last class reads +0 by the `zero` rule. In C#, the step reads one `bool[]` of `17 × S × S` cells in the order [from][to][class], with a 17th class whose cells the host writes 0. With the the candidate cut rule of task 1, each read has a table of the 17 cells of its pair, as in Handwired.
6. The stages of one Handwired loop program share one circuit, so the step and the end stage share their tables and their cells. Each Sharpwired stage is its own translated function with its own source table. The operations of a stage are equal or near; the shared tables are written one time for each stage.
7. The generated documents. documents.ts uses `JSON.stringify` and the JavaScript number text. The C# generator ports the structure: the hash `lowbias32`, the 7 shapes, the 4 styles, the 16 defects and the same choices for each counter. I did not verify that each text is equal to the Handwired text, because the Handwired code cannot run here. The C# tests pin a hash of their own first 2,000 texts.
8. The built-in regular expressions. The Handwired baseline is the JavaScript `RegExp` with the `u` flag. The C# baseline is .NET `Regex`. .NET `\d` and `\w` read Unicode classes, `.` excludes only `\n`, and a code point past U+FFFF is two UTF-16 units. `DotNetPattern.Translate` writes `[0-9]`, `[0-9A-Za-z_]`, `[^\n\r  ]` and surrogate pairs. Both `RegExp` and `Regex` find the leftmost match by the first alternative, so their counts can differ from the program count, as Handwired README says ("What the search finds").
9. The Web page parts: the workers, the `?salt=` and `?buffer=` options, the 20,000 documents for a narrow window, and the sentence for a buffer limit. The window uses threads in place of workers. A runner option `Salt` gives a cold shader compile for the bench. The runners split a batch into chunks, so no buffer limit sentence is necessary.

### 2. The pattern becomes cells, not a program

Decision: as Handwired.

- One search program for each capacity: 8, 16 and 32 states, each with 16 classes and 32 class boundaries. The automaton is data in shared cells: the boundaries, the region classes, the transition table and the accepting bits. A new pattern writes these cells and changes no program and no shader.
- The window makes the four GPU shaders (the highlighter and the three searches) when it starts. A pattern runs on the smallest capacity that holds it.
- `PatternCompiler.Compile(pattern)` ports regex/pattern.ts:
  - The language: literal code points; `.`, which matches each code point except `\n`, `\r`, U+2028 and U+2029; `\d` (0-9) and `\w` (0-9, A-Z, a-z, `_`); classes with ranges and negation, for example `[a-z_]` and `[^0-9]`; `[]` matches nothing and `[^]` matches each code point; `?`, `*` and `+` after a literal, a class or a group; `|` and `( )`. The syntax characters `^ $ \ . * + ? ( ) [ ] { } |` and `/` need a backslash. In a class, `\-` is a literal `-`, and a `-` first or last is literal.
  - These give a `PatternSyntaxException` with the code point position and the Handwired message: a lazy quantifier (`*?`), a quantifier with nothing to repeat, `(?`, `^` and `$`, `{`, `}` and `]` without a backslash, an unknown escape, a backslash at the end, a class or a group that is never closed, an out-of-order range, a range end that is `\d` or `\w`, and a `)` that closes no group.
  - The automaton: Glushkov's position automaton. State 0 is the initial state, and state p + 1 is the p-th atom. Each step into a state reads a code point of the set of that state, so the automaton has no empty steps.
  - The classes: the code points that each step reads the same way share a class. The partition lists the code points where the class changes, and the class of each region, or -1 for a region that no step reads.
  - `CapacityErrors(compiled, capacity)` gives one error for each of states, classes and boundaries that the pattern needs past the capacity, with the message "the pattern needs N states, and the program holds 32".
- `SearchProgram.WritePattern(shared, compiled)` writes the cells, as data-program.ts `writePattern` does: an unused boundary holds 0x110000, an unused region class holds 16, `transitions[from][to][class]` is 1 when the class reads into `to` and `to` is a target of `from`, and the 17th class is 0.
- `SearchProgram.WriteScope(shared, scope)` writes the 9 `searchable[kind]` cells.
- The integer writer packs the transition cells into words at each upload (`PackWords`, bit-emit.ts `packWords`), so a packed read reads one word for each (class, to) gate.

What the search finds (Handwired README): from a scan position, the longest non-empty match that starts there. The scan continues at its end. When no match starts there, the scan moves one code point on. The search finds each non-overlapping, non-empty match, leftmost and then longest. A masked search replaces a code point of a kind outside the scope with -1, so a match lies inside one run of searched characters.

Reason: the same cells serve each pattern, so no keystroke waits for a shader compile. The probe measured 0.1 to 1.9 s to create one search shader in the current writers. Handwired README gives the same reason.

### 3. The document batch

Decision:

- The encoding (`DocumentCells.Write`, program.ts `writeDocumentCells`): 512 code points, -1 after the text, then the length in code points, at most 1,048,576. 513 FP32 values, 2,052 bytes. 100,000 documents are 205.2 MB.
- The highlighter loop program: positions 512, buffer 0 the documents. Registers: lexer state, depth, container code, grammar state, error count, first error kind (each with the Handwired range, valid after any count of positions). The `Step` stage runs forward and writes the display kind of each position to the position output group `displayKinds` (fill: past end). The `End` stage runs once, reads the length cell (column 512), and writes `valid`, `firstErrorPosition`, `truncated`, `firstErrorKind` to the document group `summary`.
- The search loop program, for each capacity, masked: buffer 0 the documents, buffer 1 the display kinds. Registers: `notFinish[S]`, `reached[S]`, `matchCount`. Fields: `codePointClass` (width 1) and `notReach` (width S, bits). Stages `Start` (once), `Reverse` (backward), `Forward` (forward). Outputs: `matchCount` (document group) and `marks` (position group, for the window documents only).
- The GPU path (`GpuLoopBackend`, the integer writer by default):
  - The documents upload one time into a `DocumentBuffer` on the GPU. The highlighter writes its display kinds into a second `DocumentBuffer` that stays on the GPU. The search reads both. Only the match counts and the window marks come back.
  - One invocation for each document, workgroup size 64. The positions are a WGSL `for` loop. A register is a `var`. A field is a function array of 512 values, or 512 words for a field of bits.
  - The shared cells are in a read-only storage buffer. The constant candidate tables of a stage are in the same buffer, after the shared cells (the probe: 47 ms against 681 ms). A source cell has a statement only where a row reads it directly.
  - A batch splits into chunks that fit `MaxStorageBufferBindingSize` and `65,535 × 64` invocations, as `GpuEvaluator` does.
  - Each register and each field value is checked against the declaration of its cell before the next stage reads it, as the recurrent step kernel does. A lane that fails stops with status `Invalid`. The other lanes continue. The host replays the failed lane on the CPU and reports its message (carried item: no status for each lane).
- The CPU path (`CpuLoopBackend`):
  - `Parallel.For` over blocks of 256 documents. Each worker has its own compiled stages and its own register and field arrays.
  - The default stage backend is the compiled delegate. The `Tensor` option runs `TensorEvaluator` for each stage, for tests. The probe measured 1.77 µs for a compiled delegate of 3,200 rows, and 22.98 µs (13 µs without the source check) for `TensorEvaluator` at 3,177 rows.
  - `StopAtLength`: the CPU loops stop at the end of the text. The outputs are equal, because a cell past the end holds -1, which no step reads (Handwired `stopAtLength`). The GPU loops run each position, as in Handwired, where the stop was slower.
  - A block reports its progress, and a `CancellationToken` stops the search between blocks.
- The shared cells are checked one time for each upload, and the document cells one time when they are written (`DocumentCells.Write` checks each value). The runners do not check each source table in each step. This removes the CPU check cost of the phase 5 carried item.
- The .NET `Regex` comparison (`DotNetBaseline`):
  - `DotNetPattern.Translate(pattern)` gives the .NET pattern (question 1, item 8). A pattern outside the language throws the same `PatternSyntaxException` first.
  - `Count(texts, kinds, pattern, scope)` counts the matches of `Regex.Matches` in each document, or in each run of keys or string values. The runs come from the display kinds of the highlighter, and the search for the runs is not part of the time. One thread, `RegexOptions.CultureInvariant`, a match timeout of 10 s, and a total limit of 10 s that stops between documents. A pass under 100 ms runs two times, and the faster pass counts.
  - The window line: "10,559 of 100,000 documents match. This program: 13 ms on the GPU. .NET Regex: 21 ms." The GPU time runs from the pattern compile to the copy of the last count out of the GPU buffer. The CPU time is the sum of the block loops.
  - `JsonSearch.App --bench` writes a table: for each example pattern and each scope, the program time on the GPU and on the CPU at the capacity that holds it, the .NET `Regex` time, both document counts, and a column that marks a count difference.

Reason: the layout and the passes follow Handwired README "The GPU host" and "A document's cells". The hand-written loop kernel of the probe ran 100,000 documents of 205 MB in 7.2 to 7.7 ms from one storage buffer.

### 4. The carried items

Each item has a task:

| Item | Source | Task | Test |
| --- | --- | --- | --- |
| a. A bit gate gets `AssumeBit` but no grid 1, so `k + (b ? 1 : 0)` is refused with a wrong reason | phase 4 | 1 | `BitArithmeticTests`: `k + (b ? 1 : 0)`, `(a ? 1 : 0) + (b ? 1 : 0)`, `if (b) x = x + 1` after `x = c ? 1 : 0`, Debug and Release, values against the plain method |
| b. Release writes `b ? 1 : 0` as `cgt.un` of a bool against 0, refused as "unsigned comparison" | phase 4 | 1 | the same methods compiled in Release; a test reads the IL and asserts the `cgt.un` form is present, so the test covers the Release form |
| c. `brtrue` or `brfalse` on an int constant other than 0 or 1 refused in Release | phase 4 | 1 | `BranchOnConstantTests`: `const int Mode = 2; if (Mode != 0) ...` and a `while (k)` on a constant, Debug and Release |
| d. Tests for a constant gate side grid (0.75) and `AssumeBit` on a float gate in 0..1 (mutations B4, B5 not caught) | phase 4 | 1 | `GateRangeTests`: the grid of `c ? x : 0.75f` is 0.25 when `x` has grid 0.25 and 0.75 otherwise; `c ? u : v` with `u`, `v` float bits declares `AssumeBit`; each test fails with the mutation reverted in its code |
| Shader creation 5.6 to 7.6 s for a 19,179-operation program | phase 5 | 3 | `LoopWriterTests` pins the statement counts; `GpuLoopRunnerTests` writes the create times; a writer test asserts one statement for each constant table read and no statement for a source cell that no row reads directly |
| The integer evaluator CPU declaration checks cost time | phase 5 | 3 | `BatchEvaluatorTests`: the checks run in parallel and before the device lock; the loop runners check the shared cells one time (question 3); the test output gives the times before and after |
| No parallel CPU batch runner for a `RecurrentFunction` and no shared interface | phase 5 | 7 | `CpuRecurrentRunnerTests`, and the GPU recurrent tests run through `IRecurrentBatchRunner` |
| One invalid lane throws and the batch loses the other lanes | phase 5 | 3 (loop runners), 7 (recurrent runners) | a batch with one invalid lane returns the other lanes and the fault of the bad lane |
| `StepsPerDispatch` cannot be set | phase 5 | 7 | `GpuRecurrentOptions.StepsPerDispatch` of 1, 7 and 1,024 give equal results |
| Packed words (lanes and `packWords`) | phase 5 decision 2 | 3 | `PackedWordTests` and `LoopWriterTests`: each bit-emit.ts rule fires on its program and not on its counterexample |
| The step kernel with 513 or more cells is untested | phase 5 | 7 | a recurrent method with 600 state cells on the GPU equals the CPU runner |
| The suffix runs on all lanes in each round | phase 5 | 7 | the suffix runs one time after the last round; a test counts the suffix dispatches |
| `WindowInput` has no keyboard input | phase 3 | 8 | `WindowInputTests`, `JsonSearchAppTests` with scripted keys and text |

Translator rules of task 1 (Layer 2 additions, all on `TranslationOptions.RecognizePatterns`):

1. A bit (`bool`) used as an `int` (`b ? 1 : 0`, in the Debug and the Release IL forms) is the bit wire with grid 1 and range 0..1. No operation.
2. `t[Math.Clamp(i, 0, t.Length - 1)]` is one index read with the `clamp` rule. In other places, `Math.Clamp(x, lo, hi)` declares the range `max(lo, min(x))..min(hi, max(x))` on its result.
3. `Hw.ReadOrZero(t, i)` is one index read with the `zero` rule on both sides.
4. `bit && t[i]`, with `t` a `bool[]` and `i` proven in 0..t.Length - 1, is one index read at `i + t.Length × (1 - bit)` with the `zero` rule above (data-program.ts `createStepAnd`).
5. Candidate cut: an index read registers only the candidates that its index range reaches, and the index form gets the offset of the first one as bias. So `t[(from × S + to) × 17 + cls]` reads a table of the 17 cells of the pair (from, to).

### 5. The window

Decision:

- `JsonSearchApp : IWindowApp` in `samples/JsonSearch`. It makes no Raylib call. It holds the pattern field, the scope toggle, the backend toggle, the line of numbers, the match map, the four examples and one document.
- Layout of a window of 1,280 × 860 px (the title "JSON search"):
  - The pattern field at the top left, 640 × 36 px. The scope toggle "All | Keys | String values" and the backend toggle "GPU | CPU" at its right.
  - The examples of Handwired json-search.html under the field: `[a-z0-9.]+@[a-z.]+` (String values), `[a-z]+Id` (Keys), `\d\d\d\d-\d\d-\d\d` (All), `([a-z]+)+x` (All). A click sets the pattern and the scope.
  - The line of numbers under the examples (Handwired demo-text.ts sentences).
  - The match map at the left, 600 px wide, at most 420 px high (demo-map.ts `mapLayout`, `paintCells`, `cellAt`, `nearestMatch`, `pickedDocument`, `nextMatch`).
  - The document at the right: one code point in each cell of a fixed grid, 80 columns, the color of its display kind, and a mark rectangle under the code points of each match (alternate colors for neighbor matches, demo-text.ts). A line feed starts a new row. A code point of East Asian width "wide" takes two cells.
- Input:
  - `WindowInput` gets two new members, each with an empty default, so that the phase 3 code does not change: `Text` (the characters typed in the frame, from `GetCharPressed`) and `Keys` (an `ImmutableArray<KeyPress>` from `IsKeyPressed` and `IsKeyPressedRepeat`). `KeyPress(Key Key, bool Shift, bool Control)`. `Key` holds `Backspace`, `Delete`, `Left`, `Right`, `Home`, `End`, `Enter`, `Digit1`, `Digit2`, `Digit3`, `B`. On macOS, Command counts as `Control`.
  - The field has the focus after a click on it and when the window opens. Typed text goes into the field at the caret. `Backspace`, `Delete`, `Left`, `Right`, `Home` and `End` edit the field.
  - `Control+1`, `Control+2`, `Control+3` select All, Keys and String values. `Control+B` changes the backend. A click on a toggle segment does the same.
  - The pointer over the map picks a document (`pickedDocument`). A click keeps it. `Enter` and `Shift+Enter` go to the next and the previous document with a match (`nextMatch`).
- Search requests: when the pattern, the scope or the backend changes, the app asks the `ISearchService` for a new search. The search runs on a worker thread. A newer request cancels an older one. A result applies in the first frame after it arrives. The CPU search reports its progress, and the map fills from the top ("928 of the first 8,773 documents match. This program: 2.2 s on the CPU so far."). The .NET `Regex` time runs on a second worker thread with its limit of 10 s.
- New `DrawCommand` subtypes in `Sharpwired.Raylib`:
  - `FillRectangle(X, Y, Width, Height, Color)`.
  - `DrawText(X, Y, Text, Size, Color)` for labels.
  - `DrawGlyphs(X, Y, CellWidth, LineHeight, Size, ImmutableArray<Glyph>)` with `Glyph(int CodePoint, int Column, int Row, Rgba Color)`.
  - `DrawPixels(X, Y, Width, Height, ImmutableArray<uint> Pixels, int Version)`. `RaylibWindow` keeps one texture for each `DrawPixels` place and updates it when `Version` changes. The map draws 100,000 cells as one texture, not as 100,000 rectangles.
- Font: `IWindowApp` gets a member with a default, `IReadOnlyList<string> FontPaths => []`. `RaylibWindow` loads the first path that exists with the code points that the app names (`IWindowApp.FontCodePoints`, default ASCII). The JSON app names `/System/Library/Fonts/Supplemental/Arial Unicode.ttf`, `/System/Library/Fonts/Monaco.ttf` and two common Linux paths, and the code points of the generator word lists. With no font file, the Raylib default font draws ASCII and `?` for other code points. The glyph grid of the app does not depend on the font, so the layout and the tests do not need the font.
- `RaylibWindow.cs` stays the only Raylib file. Its new parts: the text and key reads, the four draw commands, the font load, the textures, and their release when the window closes.
- Testable: `JsonSearchAppTests` drive the app with scripted `WindowInput` frames and a fake `ISearchService` whose results the test releases at chosen frames. They assert the draw commands: the field text and caret, the selected segments, the line text, the map pixels of chosen documents, and the glyphs and marks of the picked document.

Reason: the layout and the behavior follow the Handwired page. One texture for the map follows demo-map.ts, which writes one `ImageData`. A fixed glyph grid keeps the app free of font metrics, which only Raylib can measure.

### 6. Tests without a GPU (R6.5)

Decision: `tests/JsonSearch.Tests` holds these tests. None needs an adapter.

- References (test code, written from the rules, question "Global constraints"):
  - `JsonReference`: a scanner and a parser with a real stack (Handwired highlighter/reference.ts). It gives the display kind of each code point and the summary, with the three rules past JSON's grammar: the first error position, the rest of the line after a tokenizer error, and a key only before the first error. Its tests: `System.Text.Json` (`JsonDocument.Parse` with no comments and no trailing commas) agrees on `valid` for the corpus texts of depth 16 or less and length 512 or less; display kinds written by hand for the 13 texts of reference.test.ts; each error kind has a text.
  - `RegexReference.AutomatonMatches`: runs the compiled automaton from each scan position until no state is left, and keeps the last position where an accepting state was reached (regex/reference.ts `automatonMatches`).
  - `RegexReference.DotNetMatches`: does not use the automaton. It tests each substring with `new Regex("^(?:" + DotNetPattern.Translate(p) + ")$")` (regex/reference.ts `regexpMatches`). It runs on texts of up to 40 code points.
- Program tests (CPU loop runner, both stage backends):
  - The highlighter equals `JsonReference` (display kinds and summary) on the corpus (the 2,509 texts of corpus.ts and json-step.test.ts, ported), on the edge cases (the empty text, 511 to 514 code points, depth 16 and 17, a surrogate pair at position 511), and on the first 2,000 generated documents with defects. The compiled-delegate backend also runs the 43,420 short texts of short-texts.ts.
  - The search at each capacity equals `AutomatonMatches` (counts and marks) for the 26 hand-written cases of regex-search.test.ts, for the seeded random patterns of test-patterns.ts that fit the capacity (more than 250 of 400 at 8 states), with 6 texts each, and for each scope over 300 generated documents.
  - `AutomatonMatches` equals `DotNetMatches` on the same random patterns and short texts. This checks the pattern compiler against .NET `Regex`.
  - The program counts equal the `DotNetBaseline` counts for the four example patterns over the first 2,000 documents, in each scope. Handwired README states that the counts are equal for the page's own patterns. A pattern where leftmost-longest and leftmost-first differ (`a|ab` on `xabx`) gives different counts, and the test asserts the documented difference.
  - Syntax errors: each construct of question 2 gives `PatternSyntaxException` with the Handwired position. A pattern of 33 states gives the capacity error message at 32.
  - `LoopProgram.EvaluatePlain` (the plain step methods in a loop) equals the CPU runner on the same documents, so the translation of each step is checked over whole documents.
- Generator tests (R6.1):
  - `DocumentAt(seed, index)` is a pure function: the same seed and index give the same text, in any order of calls and on two threads.
  - With `invalidFraction` 0, each of the first 100,000 documents parses with `System.Text.Json`, has at most 512 code points, and is one of the 7 shapes. Each shape occurs. Each of the 4 styles occurs.
  - With the default fraction (0.1), each of the 16 defects occurs in the first 2,000 documents, and each defect text fails `System.Text.Json` except `tooDeep` (valid JSON past depth 16) and `tooLong`. The reference reports `tooDeep` and `tooLong` for those.
  - The SHA-256 hash of the first 2,000 texts with the default seed is pinned.
- Cells: `DocumentCells.Write` gives -1 after the text, a surrogate pair as one code point, a lone surrogate as its own code point, and the length past 512.
- Window logic: `MatchMapTests` (each demo-map.ts function, with the values of demo.test.ts where it has them), `DocumentViewTests`, `PatternFieldTests`, `JsonSearchAppTests`.

Reason: the same test set as Handwired `tests/json-search`, with .NET `Regex` and `System.Text.Json` in place of `RegExp` and `JSON.parse`.

### 7. The project layout

Decision:

- `samples/JsonSearch/JsonSearch.csproj`: a library. It holds the generator, the cells, the highlighter tables and step methods, the pattern compiler, the search step methods, the loop programs, the searcher, the .NET baseline, the bench, and the window app logic (`JsonSearchApp`, `MatchMap`, `DocumentView`, `PatternField`). It references `Sharpwired.Annotations`, `Sharpwired.IL`, `Sharpwired.Gpu` and `Sharpwired.Raylib`.
- `samples/JsonSearch.App/JsonSearch.App.csproj`: the executable. `Program.cs` parses `--frames N`, `--screenshot path`, `--documents N` (default 100,000), `--cpu` (no GPU) and `--bench`, then opens the window or writes the bench table.
- This follows phase 3 and phase 4: `Toggle.Builder` is a library with the window app, and `Toggle.CSharp` is the executable.
- `tests/JsonSearch.Tests/JsonSearch.Tests.csproj`: references `JsonSearch`. It links `Js.cs` and `TensorChecks.cs` from `tests/Sharpwired.Core.Tests/` and `GpuFixture.cs` from `tests/Sharpwired.Gpu.Tests/`.
- The generic loop program code is in `src/Sharpwired.Gpu/` (the runners, the writers, the compiled stages). Its tests are in `tests/Sharpwired.Gpu.Tests/`. The translator rules are in `src/Sharpwired.IL/`, with tests in `tests/Sharpwired.IL.Tests/`.
- `Sharpwired.slnx` gets the three new projects.

Reason: the window app logic needs tests without a window, so it is in the library. The executable holds only the start code.

## Public API

### Sharpwired.Annotations (addition)

```csharp
public static class Hw
{
    // table[index], or 0 when index is outside 0..table.Length - 1. The translator gives one index read
    // with the zero rule on both sides.
    public static int ReadOrZero(int[] table, int index);
    public static float ReadOrZero(float[] table, int index);
}
```

### Sharpwired.IL

No new public type. The rules of question 4 change the operations that `TranslateFunction`, `TranslateStep` and `TranslateRecurrent` give, and they accept more methods. With `RecognizePatterns = false`, the rules 2 to 5 are off, and rule 1 stays on, because it fixes a refusal.

### Sharpwired.Gpu: loop programs

```csharp
public enum StageRepeat { Once, Forward, Backward }

// What one input cell of a stage reads.
public abstract record CellSource
{
    public sealed record Item(int Buffer) : CellSource;                       // the document cell at the position
    public sealed record DocumentCell(int Buffer, int Column) : CellSource;   // one fixed cell of the document
    public sealed record Register(int Index) : CellSource;
    public sealed record Field(int Index, int Component) : CellSource;
    public sealed record Shared(int Offset) : CellSource;                     // one value for all documents
}

// Where one output of a stage goes.
public abstract record StageWrite
{
    public sealed record Register(int Index) : StageWrite;
    public sealed record Field(int Index, int Component) : StageWrite;
    public sealed record Document(int Group, int Column) : StageWrite;
    public sealed record Position(int Group) : StageWrite;
}

public sealed record LoopBuffer(string Name, int CellsPerDocument);
public sealed record LoopRegister(string Name, float Initial);
public sealed record LoopField(string Name, int Width, bool Bits);
public enum OutputGroupKind { Document, Position }
public sealed record LoopOutputGroup(string Name, OutputGroupKind Kind, int ValuesPerDocument, float Fill);

// A cell path of the function maps to a source. An array path, for example "transitions", maps its
// elements to consecutive shared offsets or field components from the given start.
public sealed record LoopStage(
    string Name, StageRepeat Repeat, TranslatedFunction Function,
    ImmutableDictionary<string, CellSource> Cells, ImmutableDictionary<string, StageWrite> Writes);

public readonly record struct LengthCell(int Buffer, int Column);

public sealed class LoopProgram
{
    // Throws ArgumentException for a cell with no source, an output with no write, a register or a
    // field that no stage writes before a stage reads it, or a size mismatch.
    public static LoopProgram Create(
        int positions, IReadOnlyList<LoopBuffer> buffers, IReadOnlyList<LoopRegister> registers,
        IReadOnlyList<LoopField> fields, IReadOnlyList<LoopOutputGroup> outputGroups,
        IReadOnlyList<LoopStage> stages, int sharedSize, LengthCell? lengthCell = null);
    public int Positions { get; }
    public ImmutableArray<LoopBuffer> Buffers { get; }
    public ImmutableArray<LoopRegister> Registers { get; }
    public ImmutableArray<LoopField> Fields { get; }
    public ImmutableArray<LoopOutputGroup> OutputGroups { get; }
    public ImmutableArray<LoopStage> Stages { get; }
    public int SharedSize { get; }
    // The plain step methods in a loop over one document, by reflection. The reference of the translation.
    public LoopDocumentValues EvaluatePlain(IReadOnlyList<ReadOnlyMemory<float>> documentCells, ReadOnlySpan<float> shared);
}

public sealed record LoopDocumentValues(ImmutableArray<float> DocumentOutputs, ImmutableArray<float> PositionOutputs);

// Documents that a backend holds: on the GPU a storage buffer, on the CPU an array.
public interface IDocumentBuffer : IDisposable
{
    int DocumentCount { get; }
    int CellsPerDocument { get; }
}

public sealed record LaneFault(int Document, string Message);
public sealed record LoopTimes(double WriteMs, double SubmitMs, double WaitMs, double ReadMs, double TotalMs);

public sealed record LoopRequest(
    IReadOnlyList<IDocumentBuffer> Inputs,          // one for each LoopBuffer
    int DocumentCount,
    IReadOnlyList<IDocumentBuffer?> PositionTargets, // one for each position group: keep all its values there, or null
    int WindowFirst, int WindowCount);              // the documents whose position values come back

public sealed record LoopRun(
    ImmutableArray<float> DocumentValues,           // group after group, document after document
    ImmutableArray<float> WindowPositionValues,     // group after group, window document after window document
    ImmutableArray<LaneFault> Faults,
    int DocumentsDone,
    LoopTimes Times);

public interface ILoopRunner : IDisposable
{
    LoopProgram Program { get; }
    void WriteShared(ReadOnlySpan<float> shared);   // checked against the cell declarations
    LoopRun Run(LoopRequest request, IProgress<int>? progress = null, CancellationToken cancel = default);
}

public interface ILoopBackend : IDisposable
{
    IDocumentBuffer Upload(ReadOnlySpan<float> cells, int documentCount, int cellsPerDocument);
    IDocumentBuffer Allocate(int documentCount, int cellsPerDocument, float fill);
    float[] Read(IDocumentBuffer buffer, int firstDocument, int documentCount);
    ILoopRunner CreateRunner(LoopProgram program);
}

public enum CpuStageBackend { Compiled, Tensor }

public sealed record GpuLoopOptions(GpuWriter Writer = GpuWriter.Integer, uint Salt = 0);
public sealed class GpuLoopBackend : ILoopBackend
{
    public GpuLoopBackend(GpuDevice device, GpuLoopOptions? options = null);
    public LoopWgslModule Module(ILoopRunner runner);      // the shader text and its statistics
}

public sealed record CpuLoopOptions(CpuStageBackend Stages = CpuStageBackend.Compiled, bool StopAtLength = true,
    int? MaxDegreeOfParallelism = null, int BlockSize = 256);
public sealed class CpuLoopBackend : ILoopBackend
{
    public CpuLoopBackend(CpuLoopOptions? options = null);
}

public sealed record LoopWgslModule(string Code, LoopWgslStatistics Statistics, ImmutableArray<PackedCell> PackedCells);
public sealed record LoopWgslStatistics(int Statements, int FloatRows, int WholeRows, int BitRows, int PackedReads, int LaneOperations, int TableCells);
public readonly record struct PackedCell(int Word, int Lane, int SharedOffset);
public static class PackedWords
{
    // bit-emit.ts packWords: one u32 for each packed word, from the shared cells.
    public static void Pack(ImmutableArray<PackedCell> cells, ReadOnlySpan<float> shared, Span<uint> words);
}

// The compiled delegate of one stage, from its rows. Exact FP32: each row is the reference left fold.
public sealed class CompiledStage
{
    public static CompiledStage Compile(Circuit circuit, TensorProgram program);
    public void Evaluate(ReadOnlySpan<float> source, Span<float> roots);
}
```

### Sharpwired.Gpu: recurrent runners (changes for the carried items)

```csharp
public enum RecurrentLaneStatus { Completed, Limit, Invalid }

public sealed record RecurrentBatch(
    ImmutableArray<float> Outputs, ImmutableArray<int> Steps, ImmutableArray<RecurrentLaneStatus> Status,
    ImmutableArray<string?> Messages, int OutputCount)
{
    public RecurrentResult Lane(int lane);   // throws InvalidOperationException with the message for an Invalid lane
    public void ThrowIfInvalid();            // the behavior of phase 5: throws for the first Invalid lane
}

public interface IRecurrentBatchRunner : IDisposable
{
    RecurrentFunction Function { get; }
    RecurrentBatch Run(ReadOnlySpan<float> arguments, int laneCount, int stepLimit);
}

public sealed record GpuRecurrentOptions(int StepsPerDispatch = 1024, GpuWriter Writer = GpuWriter.Integer);
public sealed class GpuRecurrentRunner : IRecurrentBatchRunner
{
    public static GpuRecurrentRunner Create(GpuDevice device, RecurrentFunction function, GpuRecurrentOptions? options = null);
    public RecurrentResult Run(ReadOnlySpan<float> arguments, int stepLimit);   // one lane, unchanged
}

public sealed class CpuRecurrentRunner : IRecurrentBatchRunner
{
    public CpuRecurrentRunner(RecurrentFunction function, int? maxDegreeOfParallelism = null);
}

public static class RecurrentRunners
{
    public static IRecurrentBatchRunner Create(RecurrentFunction function, GpuDevice? device, GpuRecurrentOptions? options = null);
}
```

`GpuRecurrentBatch` becomes `RecurrentBatch`. `GpuRecurrentRunner.Create(device, function, writer)` stays as an overload. The phase 5 tests change from `Completed` to `Status`.

### Sharpwired.Raylib (additions)

```csharp
public enum Key { Backspace, Delete, Left, Right, Home, End, Enter, Digit1, Digit2, Digit3, B }
public readonly record struct KeyPress(Key Key, bool Shift, bool Control);

public readonly record struct WindowInput(
    double PointerX, double PointerY, bool PointerPressed, bool PointerReleased, bool FocusLost,
    int ViewportWidth, int ViewportHeight)
{
    public string Text { get; init; }                 // null reads as ""
    public ImmutableArray<KeyPress> Keys { get; init; } // default reads as empty
}

public sealed record FillRectangle(double X, double Y, double Width, double Height, Rgba Color) : DrawCommand;
public sealed record DrawText(double X, double Y, string Text, double Size, Rgba Color) : DrawCommand;
public readonly record struct Glyph(int CodePoint, int Column, int Row, Rgba Color);
public sealed record DrawGlyphs(double X, double Y, double CellWidth, double LineHeight, double Size, ImmutableArray<Glyph> Glyphs) : DrawCommand;
public sealed record DrawPixels(double X, double Y, int Width, int Height, ImmutableArray<uint> Pixels, int Version) : DrawCommand;

public interface IWindowApp
{
    // the phase 3 members do not change
    IReadOnlyList<string> FontPaths => [];
    IReadOnlyList<int> FontCodePoints => [];   // empty: ASCII 32..126
}
```

### JsonSearch (the sample library)

```csharp
namespace JsonSearch;

public enum DocumentShape { PackageManifest, UserRecord, LogEvent, Settings, Order, MapFeature, SensorReadings }
public enum DocumentDefect { None, MissingQuote, TrailingComma, BadEscape, TooDeep, TooLong, MissingColon, UnquotedKey,
    TruncatedText, SingleQuotes, BadLiteral, BadNumber, MissingComma, MismatchedBracket, ControlCharacter, Comment, ExtraText }
public sealed record GeneratedDocument(string Text, DocumentShape Shape, DocumentDefect Defect);
public static class Documents
{
    public const int DefaultSeed = 20261013;
    public const double DefaultInvalidFraction = 0.1;
    public static GeneratedDocument DocumentAt(int seed, int index, double invalidFraction = DefaultInvalidFraction);
    public static IReadOnlyList<GeneratedDocument> Generate(int count, int seed = DefaultSeed, double invalidFraction = DefaultInvalidFraction);
    public static int CodePointLength(string text);
}

public static class DocumentCells
{
    public const int Capacity = 512;
    public const int CellsPerDocument = 513;
    public const int LengthLimit = 1_048_576;
    public static void Write(Span<float> cells, string text);
    public static float[] WriteAll(IReadOnlyList<string> texts);
}

public enum DisplayKind { Whitespace, Punctuation, String, Escape, Number, Literal, Error, PastEnd, Key }
public enum ErrorKind { None, UnexpectedCharacter, ControlCharacterInString, BadEscape, BadUnicodeEscape, BadNumber,
    BadLiteral, ExpectedValue, ExpectedKey, ExpectedColon, ExpectedCommaOrClose, MismatchedBracket, ExtraAfterValue,
    TooDeep, UnexpectedEnd, TooLong }
public sealed record DocumentSummary(bool Valid, int FirstErrorPosition, ErrorKind FirstErrorKind, bool Truncated);

public readonly record struct HighlighterState(int LexerState, int Depth, int Code, int GrammarState, int ErrorCount, int FirstErrorKind);
public static class HighlighterStep
{
    [Handwired] public static (HighlighterState Next, int DisplayKind) Step(HighlighterState state, int codePoint);
    [Handwired] public static (int Valid, int FirstErrorPosition, int Truncated, int FirstErrorKind) End(HighlighterState state, int textLength);
}
public static class HighlighterProgram { public static LoopProgram Build(); }

public sealed class PatternSyntaxException(string message, int position) : ArgumentException(message) { public int Position { get; } }
public sealed record SearchCapacity(int States, int Classes = 16, int Boundaries = 32)
{
    public static ImmutableArray<SearchCapacity> All { get; }   // 8, 16, 32 states
}
public sealed record CapacityError(string Reason, int Needed, int Capacity, string Message);
public sealed class CompiledPattern { /* automaton and partition, read-only */ }
public static class PatternCompiler
{
    public static CompiledPattern Compile(string pattern);
    public static ImmutableArray<CapacityError> CapacityErrors(CompiledPattern compiled, SearchCapacity capacity);
}

public enum SearchScope { All, Keys, StringValues }
public static class Search8 { [Handwired] Reverse(...); [Handwired] Forward(...); }   // and Search16, Search32, SearchStart
public sealed class SearchProgram
{
    public static SearchProgram Build(SearchCapacity capacity);
    public LoopProgram Loop { get; }
    public void WritePattern(Span<float> shared, CompiledPattern compiled);
    public void WriteScope(Span<float> shared, SearchScope scope);
}

public sealed record SearchRequest(string Pattern, SearchScope Scope, int WindowFirst, int WindowCount);
public abstract record SearchResult
{
    public sealed record Searched(int States, ImmutableArray<float> MatchCounts, int DocumentsDone, int DocumentsWithMatch,
        long Matches, ImmutableArray<float> WindowMarks, LoopTimes Times) : SearchResult;
    public sealed record SyntaxError(string Message, int Position) : SearchResult;
    public sealed record TooLarge(ImmutableArray<CapacityError> Errors) : SearchResult;
}
public sealed class Searcher : IDisposable
{
    public static Searcher Create(IReadOnlyList<string> texts, ILoopBackend backend);
    public ImmutableArray<DocumentSummary> Validation { get; }
    public SearchResult Search(SearchRequest request, IProgress<SearchResult>? progress = null, CancellationToken cancel = default);
    public ImmutableArray<byte> Kinds(int firstDocument, int count);
}

public static class DotNetPattern { public static string Translate(string pattern); }
public sealed record BaselineResult(long Matches, int DocumentsWithMatch, double Milliseconds, bool OverLimit);
public static class DotNetBaseline
{
    public static BaselineResult Count(IReadOnlyList<string> texts, IReadOnlyList<ImmutableArray<byte>> kinds, string pattern, SearchScope scope, TimeSpan limit);
}

public interface ISearchService { /* Start(request, backend) and Poll() for the window app */ }
public sealed class JsonSearchApp : IWindowApp { public JsonSearchApp(ISearchService service, IReadOnlyList<string> texts); }
public static class MatchMap { /* mapLayout, cellAt, cellCenter, nearestMatch, pickedDocument, nextMatch, paintCells */ }
```

The step method parameters carry the Handwired ranges with `[Range]` and `[FixedLength]`, for example `[Range(-1, 0x10FFFF)] int codePoint` and `[FixedLength(17 * S * S)] bool[] transitions`.

## Files

| File | Content | Source |
| --- | --- | --- |
| `src/Sharpwired.Annotations/Hw.cs` | `ReadOrZero` | data-program.ts (`zero` rule), json-step.ts |
| `src/Sharpwired.IL/OperationLowering.cs` | rules 1 to 5 of question 4 | data-program.ts `createStepAnd`, json-step.ts `read` |
| `src/Sharpwired.IL/IndexReads.cs` | the clamp read, the gated read, the candidate cut (moved out of `OperationLowering.cs`, which is at 713 lines) | numeric-ui-core.ts `indexReadAffine` |
| `src/Sharpwired.IL/Gates.cs`, `Comparisons.cs`, `SsaBuilder.cs` | carried items a to c | no Handwired source |
| `src/Sharpwired.IL/Intrinsics.cs` | `Hw.ReadOrZero` | no Handwired source |
| `src/Sharpwired.Gpu/Loops/LoopProgram.cs` | the model, `Create`, `EvaluatePlain` | loop-program.ts |
| `src/Sharpwired.Gpu/Loops/LoopBackends.cs` | `ILoopBackend`, `ILoopRunner`, `IDocumentBuffer`, the records | gpu/host.ts |
| `src/Sharpwired.Gpu/Loops/CompiledStage.cs` | the compiled delegate | generated-js.ts |
| `src/Sharpwired.Gpu/Loops/CpuLoopBackend.cs` | the CPU runner, blocks, progress, cancel, faults | loop-program.ts `createLoopRunner` |
| `src/Sharpwired.Gpu/Loops/LoopFloatWriter.cs` | the f32 loop writer | loop-emit.ts |
| `src/Sharpwired.Gpu/Loops/LoopIntegerWriter.cs` | the integer loop writer | bit-emit.ts |
| `src/Sharpwired.Gpu/Loops/LoopLanes.cs` | the Lanes rule | bit-emit.ts ("Lanes") |
| `src/Sharpwired.Gpu/Loops/PackedReads.cs` | the Packed reads rule, `PackedWords` | bit-emit.ts ("Packed reads", `packWords`) |
| `src/Sharpwired.Gpu/Loops/LoopKernelText.cs` | the shared parts of both loop writers: buffers, parameters, the `for` loops, the cell checks, the names | loop-emit.ts |
| `src/Sharpwired.Gpu/Loops/GpuLoopBackend.cs` | the GPU runner, document buffers, chunks, readback, replay of a failed lane | gpu/host.ts |
| `src/Sharpwired.Gpu/ShaderRows.cs`, `WgslIntegerWriter.cs`, `WgslFloatWriter.cs` | constant tables in a storage buffer; a statement only for a source cell that a row reads directly | emit.ts |
| `src/Sharpwired.Gpu/Batch.cs` | parallel source checks before the lock | no Handwired source |
| `src/Sharpwired.Gpu/GpuRecurrentRunner.cs`, `RecurrentKernelWriter.cs`, `RecurrentRunners.cs` | the carried recurrent items | no Handwired source |
| `src/Sharpwired.Raylib/WindowApp.cs` | `Key`, `KeyPress`, the new `WindowInput` members, the draw commands, the font members | template/toggle.ts, demo.ts |
| `src/Sharpwired.Raylib/RaylibWindow.cs` | the text and key reads, the new commands, fonts, textures | demo.ts |
| `samples/JsonSearch/JsonSearch.csproj` | the library | no Handwired source |
| `samples/JsonSearch/Documents.cs`, `DocumentBuilders.cs`, `DocumentDefects.cs` | the generator | documents.ts |
| `samples/JsonSearch/DocumentCells.cs` | the cells | program.ts |
| `samples/JsonSearch/Highlighter/Kinds.cs`, `Tables.cs`, `HighlighterStep.cs`, `HighlighterProgram.cs` | the highlighter | highlighter/kinds.ts, highlighter/tables.ts, json-step.ts |
| `samples/JsonSearch/Regex/PatternCompiler.cs`, `PatternParser.cs`, `Automaton.cs` | the pattern language | regex/pattern.ts |
| `samples/JsonSearch/Regex/SearchSteps.cs` (`Search8`, `Search16`, `Search32`, `SearchStart`) | the search steps | search-step.ts, data-program.ts, bits.ts |
| `samples/JsonSearch/Regex/SearchProgram.cs` | the loop program, `WritePattern`, `WriteScope` | search-step.ts, data-program.ts |
| `samples/JsonSearch/Searcher.cs` | validation, search, kinds | searcher.ts |
| `samples/JsonSearch/DotNetBaseline.cs` | the .NET `Regex` baseline and `DotNetPattern` | plain-search.ts, table-loop.ts |
| `samples/JsonSearch/SearchBench.cs` | the bench table | lab/json-search (the tables only) |
| `samples/JsonSearch/Window/JsonSearchApp.cs`, `MatchMap.cs`, `DocumentView.cs`, `PatternField.cs`, `SearchService.cs`, `Sentences.cs` | the window logic | demo.ts, demo-map.ts, demo-text.ts, json-search.html |
| `samples/JsonSearch.App/JsonSearch.App.csproj`, `Program.cs` | the executable | no Handwired source |
| `tests/Sharpwired.IL.Tests/BitArithmeticTests.cs`, `BranchOnConstantTests.cs`, `IndexReadRuleTests.cs`, `GateRangeTests.cs`, `Inputs/BitArithmeticInputs.cs`, `Inputs/IndexReadInputs.cs` | task 1 | no Handwired source |
| `tests/Sharpwired.Gpu.Tests/LoopProgramTests.cs`, `CompiledStageTests.cs`, `CpuLoopRunnerTests.cs`, `LoopInputs.cs` | task 2 | loop-program.ts |
| `tests/Sharpwired.Gpu.Tests/LoopWriterTests.cs`, `PackedWordTests.cs`, `GpuLoopRunnerTests.cs`, `LoopRowInterpreter.cs` | task 3 | bit-emit.test.ts, writer-probes.test.ts, search.test.ts |
| `tests/Sharpwired.Gpu.Tests/CpuRecurrentRunnerTests.cs`, `GpuRecurrentTests.cs` | task 7 | no Handwired source |
| `tests/Sharpwired.Raylib.Tests/WindowInputTests.cs`, `DrawCommandTests.cs` | task 8 | no Handwired source |
| `tests/JsonSearch.Tests/JsonSearch.Tests.csproj` | the test project | no Handwired source |
| `tests/JsonSearch.Tests/References/JsonReference.cs`, `RegexReference.cs` | the plain C# references | highlighter/reference.ts, regex/reference.ts |
| `tests/JsonSearch.Tests/Data/Corpus.cs`, `ShortTexts.cs`, `TestPatterns.cs`, `EdgeCases.cs` | the test data | corpus.ts, short-texts.ts, test-patterns.ts, edge-cases.ts |
| `tests/JsonSearch.Tests/DocumentTests.cs`, `DocumentCellTests.cs` | R6.1 | json-search.test.ts |
| `tests/JsonSearch.Tests/JsonReferenceTests.cs`, `HighlighterTests.cs`, `GpuHighlighterTests.cs` | R6.2, R6.5 | reference.test.ts, json-step.test.ts |
| `tests/JsonSearch.Tests/PatternTests.cs`, `SearchTests.cs`, `GpuSearchTests.cs` | R6.2, R6.5 | regex-search.test.ts, search.test.ts, edge-cases.test.ts |
| `tests/JsonSearch.Tests/SearcherTests.cs`, `BaselineTests.cs` | R6.3 | searcher.test.ts |
| `tests/JsonSearch.Tests/MatchMapTests.cs`, `DocumentViewTests.cs`, `PatternFieldTests.cs`, `JsonSearchAppTests.cs` | R6.4 | demo.test.ts |
| `Sharpwired.slnx` | the three new projects | |
| `docs/design.md` | Layer 2 rules, Layer 3 loop programs, Layer 4 JSON search | |
| `docs/superpowers/specs/2026-10-16-sharpwired-design.md` | R6.1 to R6.5 checked | |

## Tasks

The tasks run in this order. Each task ends with a green `dotnet test` and `dotnet test -c Release`, and a commit.

### Task 1: the translator rules and the phase 4 carried items

- Files: `src/Sharpwired.IL/` (`OperationLowering.cs`, `IndexReads.cs`, `Gates.cs`, `Comparisons.cs`, `SsaBuilder.cs`, `Intrinsics.cs`), `src/Sharpwired.Annotations/Hw.cs`; tests `BitArithmeticTests.cs`, `BranchOnConstantTests.cs`, `IndexReadRuleTests.cs`, `GateRangeTests.cs`, `Inputs/BitArithmeticInputs.cs`, `Inputs/IndexReadInputs.cs`.
- First, write a test for each probe refusal of question 1: the C# methods of the probe (the search forward step at 8 states with `(highlighted ? 1 : 0) + (start ? 1 : 0)`, the highlighter line with `Math.Clamp`, and the unrolled call). Run them in Debug and in Release and see each refusal.
- Then the five rules of question 4 and the carried items a to d.
- Tests:
  - Carried items a to d: the table of question 4.
  - Each rule gives its operation count on its smallest method: `b ? 1 : 0` gives 0 operations; `t[Math.Clamp(i, 0, n - 1)]` gives 1 index read with `clamp`; `Hw.ReadOrZero` gives 1 index read with `zero` on both sides; `bit && t[i]` gives 1 index read at `i + n × (1 - bit)` with `zero` above; a read at `k × 17 + c` has a candidate table of 17 wires.
  - Each rule has a counterexample that it must not change: `Math.Clamp` with bounds that are not the table bounds, `bit && t[i]` with `i` not proven in range, an `int[]` table in the gated form (a whole value is no bit), and a `Math.Clamp` result that a second reader reads.
  - Each new method equals its plain method over 200 seeded source tables (`AssertMatchesPlain`), in Debug and in Release.
  - With `RecognizePatterns = false`, rules 2 to 5 give the operations of phase 4.
  - The phase 4 differential harness (4,800 generated methods) gives 0 mismatches after the change.
  - The toggle translations (C# and F#) keep their operation lists.
- R items: none closed; prepares R6.2. Carried: phase 4 a, b, c, d.

### Task 2: the loop program and the CPU runner

- Files: `src/Sharpwired.Gpu/Loops/LoopProgram.cs`, `LoopBackends.cs`, `CompiledStage.cs`, `CpuLoopBackend.cs`; tests `LoopProgramTests.cs`, `CompiledStageTests.cs`, `CpuLoopRunnerTests.cs`, `LoopInputs.cs`.
- `LoopInputs.cs` holds small step methods for the tests: a sum over the positions (one register), a two-pass program (a backward stage writes a field and a forward stage reads it), a program with a document output and a position output, and a program whose register passes its declared range at a chosen position.
- Tests:
  - `LoopProgram.Create` refuses each bad program of its XML comment with `ArgumentException`.
  - `CompiledStage` equals `TensorEvaluator` with `Js.ObjectIs` on each root over the phase 2 seeded programs (`RandomPrograms`), with and without gather lowering (`Js.SameNumericValue` with gathers, as phase 5). It runs the rows in the reference order, so a test with the FMA inputs of phase 5 gives the reference bits.
  - The CPU runner with both stage backends equals `LoopProgram.EvaluatePlain` on 1,000 seeded documents for each input program.
  - `StopAtLength` gives the same outputs.
  - A register past its declared range at document 7 gives a `LaneFault` for document 7, with the message of the cell check, and the outputs of the other documents.
  - Cancellation stops between blocks: `DocumentsDone` is a multiple of the block size, and the progress reports increase.
  - Disposal: a call after `Dispose` throws `ObjectDisposedException`.
- R items: R6.3 (part: the CPU path in parallel over document blocks). Carried: no status for each lane (loop runners).

### Task 3: the GPU loop runner, the two loop writers and the packed words

- Files: `src/Sharpwired.Gpu/Loops/LoopKernelText.cs`, `LoopFloatWriter.cs`, `LoopIntegerWriter.cs`, `LoopLanes.cs`, `PackedReads.cs`, `GpuLoopBackend.cs`; `ShaderRows.cs`, `WgslFloatWriter.cs`, `WgslIntegerWriter.cs`, `Batch.cs`; tests `LoopWriterTests.cs`, `PackedWordTests.cs`, `GpuLoopRunnerTests.cs`, `LoopRowInterpreter.cs`, `BatchEvaluatorTests.cs`.
- The kernel follows question 3: one invocation for each document, the `for` loops, registers as `var`, fields as function arrays (a field of bits as one word for each position), the shared buffer with the constant tables, the cell checks, the status for each lane. No WGSL name comes from a Handwired or a user name (the word `continuing` is reserved).
- The integer writer ports each bit-emit.ts rule: Sum, Gate, Comparison, Read, Packed reads (with the proof that two bits in one gate form are never both 1, and at most 4 bits that gate one read), Lanes (a field of bits, registers of bits that the same stages write, a list of shared cells packed one time for each document, at most 32 bits for each word). A statement is written when a later statement or an output first needs it.
- `GpuEvaluator` (phase 5) also gets the constant tables in the storage buffer and the rule "no statement for a source cell that no row reads directly". Its source checks run in parallel, before the device lock.
- Tests (no adapter):
  - `LoopRowInterpreter` runs the WGSL rows of both loop writers in C# (as `RowInterpreter` of phase 5), and equals the CPU runner on the loop inputs of task 2.
  - Each rule of bit-emit.ts fires on its program and does not fire on its counterexample, with the programs of bit-emit.test.ts and writer-probes.test.ts that apply.
  - `PackedWords.Pack` gives the bits of the cells, for 1, 31, 32 and 33 bits.
  - The statement counts of the loop inputs are pinned.
- Tests (each adapter, both writers):
  - Each loop input over 10,000 seeded documents equals the CPU runner (`Js.ObjectIs` for the f32 writer on a program with no gather lowering, `Js.SameNumericValue` for the integer writer).
  - A chained run: one runner writes a position group into a document buffer, and a second runner reads it as an input, with no readback between them. The result equals the CPU chain.
  - A chunk test with an internal limit of 1,000 documents for each chunk and 2,500 documents.
  - One invalid lane: the fault names the document and holds the CPU message. The other lanes equal the CPU runner.
  - The create time of each shader and the run time over 100,000 documents go to the test output.
  - `BatchEvaluatorTests`: the phase 5 tests pass. The output gives the check time before and after.
- R items: R6.3 (part: the GPU path). Carried: shader creation time, integer CPU checks, packed words, no status for each lane (loop runners).

### Task 4: the documents and the highlighter

- Files: `samples/JsonSearch/JsonSearch.csproj`, `Documents.cs`, `DocumentBuilders.cs`, `DocumentDefects.cs`, `DocumentCells.cs`, `Highlighter/Kinds.cs`, `Highlighter/Tables.cs`, `Highlighter/HighlighterStep.cs`, `Highlighter/HighlighterProgram.cs`; `tests/JsonSearch.Tests/JsonSearch.Tests.csproj`, `References/JsonReference.cs`, `Data/Corpus.cs`, `Data/ShortTexts.cs`, `Data/EdgeCases.cs`, `DocumentTests.cs`, `DocumentCellTests.cs`, `JsonReferenceTests.cs`, `HighlighterTests.cs`, `GpuHighlighterTests.cs`; `Sharpwired.slnx`.
- Write `JsonReference` and its tests first, then the generator and its tests, then the step methods.
- `HighlighterStep.Step` has one statement for each line of json-step.ts `emitPosition` and the step part, in the same order. It uses the task 1 forms: `Tables.Classes[Math.Clamp(codePoint + 1, 0, 128)]`, `Hw.ReadOrZero(Tables.ObjectWeight, depth * EventCount + ev)`, and `b ? 1 : 0` in sums.
- Tests: question 6, "References", "Generator tests", "Cells", and the highlighter part of "Program tests". Also:
  - The operation counts of `Step` and `End` and the layer counts are pinned, and written next to the Handwired counts (37 and 17).
  - On the reference evaluator, each declared grid and each index read holds at each position for the corpus (`ValidateGrids`, `ValidateIndexReads`), as json-step.test.ts does.
  - GPU (each adapter, both writers): the highlighter over the first 100,000 generated documents (invalid fraction 0) and the corpus equals the CPU runner, and a sample of 2,000 equals `JsonReference`.
- R items: R6.1, R6.2 (highlighter), R6.5 (highlighter, generator).

### Task 5: the pattern compiler and the search programs

- Files: `samples/JsonSearch/Regex/PatternParser.cs`, `Automaton.cs`, `PatternCompiler.cs`, `SearchSteps.cs`, `SearchProgram.cs`, `DotNetBaseline.cs` (`DotNetPattern` only); tests `References/RegexReference.cs`, `Data/TestPatterns.cs`, `PatternTests.cs`, `SearchTests.cs`, `GpuSearchTests.cs`.
- `SearchSteps.cs` holds one class for each capacity. The three classes differ only in the constant `S`. A test reads the three method bodies through the IL and asserts that they are equal apart from the constants, so a change to one class without the others fails.
- Tests: question 6, the search part of "References" and "Program tests", and "Syntax errors". Also:
  - The operation counts of `Reverse` and `Forward` at 8, 16 and 32 states and the pattern cell counts are pinned, next to the Handwired counts (148, 356, 1,156 and 92, 308, 1,124).
  - The integer writer statistics of the three search shaders are pinned: packed reads and lane operations fire (Handwired: 147, 171 and 219 statements).
  - GPU (each adapter, both writers): the search at each capacity over 10,000 generated documents in each scope, with the patterns that fill the capacity, a match on the last position and the empty pattern (one state, no match), equals the CPU runner. The window marks of 64 documents equal `AutomatonMatches`.
  - The edge cases of edge-cases.ts on the CPU and on the GPU.
- R items: R6.2 (search), R6.5 (search).

### Task 6: the searcher, 100,000 documents and the .NET Regex timings

- Files: `samples/JsonSearch/Searcher.cs`, `DotNetBaseline.cs`, `SearchBench.cs`, `samples/JsonSearch.App/JsonSearch.App.csproj`, `samples/JsonSearch.App/Program.cs` (the `--bench` mode only); tests `SearcherTests.cs`, `BaselineTests.cs`; `Sharpwired.slnx`.
- `Searcher.Create` writes the cells, makes the highlighter runner and the three search runners, and runs the highlighter one time. The kinds stay on the backend. `Search` compiles the pattern, picks the capacity, writes the shared cells and runs the search. It returns the counts, the window marks and the times. Only the newest request gets a result, as searcher.ts does.
- Tests:
  - On the GPU (each adapter) and on the CPU: 100,000 generated documents, the four example patterns in their scopes and in All, at the capacity that holds each. The GPU counts equal the CPU counts on each document.
  - `DotNetBaseline` counts equal the program counts for the four examples over 100,000 documents (Handwired README: equal for the page's patterns). `([a-z]+)+x` stays inside the limit or reports `OverLimit`. Both results are accepted, and the test writes which one occurred.
  - A syntax error and a pattern of 33 states return their results and run no search.
  - A request that a newer request replaces returns no result.
  - `--bench` writes one row for each pattern, scope and backend, and exits with 0. A test runs `SearchBench` with 2,000 documents and checks the table columns.
  - The times of the 100,000-document runs go to the test output: GPU search, CPU search, .NET `Regex`.
- R items: R6.3.

### Task 7: the recurrent runner items of phase 5

- Files: `src/Sharpwired.Gpu/GpuRecurrentRunner.cs`, `RecurrentKernelWriter.cs`, `RecurrentRunners.cs`; tests `CpuRecurrentRunnerTests.cs`, `GpuRecurrentTests.cs`, `GpuRecurrentInputs.cs`.
- Tests:
  - `CpuRecurrentRunner` equals `RecurrentRunner.Run` for each lane of the phase 5 recurrent inputs over 1,024 seeded argument tables, on 1 and on 8 threads.
  - The phase 5 GPU recurrent tests run through `IRecurrentBatchRunner` for the GPU and the CPU runner.
  - A batch where lane 5 is invalid (`DoubleUntil` with `n` = 30): lane 5 has status `Invalid` and the CPU message. Each other lane equals the CPU runner. `ThrowIfInvalid` throws the phase 5 message.
  - `StepsPerDispatch` of 1, 7 and 1,024 give equal batches. A value below 1 throws `ArgumentOutOfRangeException`.
  - A method with 600 state cells (a fixed array of 600 `int` values that a loop with no maximum shifts) runs on the GPU and equals the CPU runner.
  - The suffix runs one time: a test counts the suffix dispatches through an internal counter.
- R items: none. Carried: the phase 5 recurrent items.

### Task 8: the window

- Files: `src/Sharpwired.Raylib/WindowApp.cs`, `RaylibWindow.cs`; `samples/JsonSearch/Window/JsonSearchApp.cs`, `MatchMap.cs`, `DocumentView.cs`, `PatternField.cs`, `SearchService.cs`, `Sentences.cs`; `samples/JsonSearch.App/Program.cs`; tests `WindowInputTests.cs`, `DrawCommandTests.cs`, `MatchMapTests.cs`, `DocumentViewTests.cs`, `PatternFieldTests.cs`, `JsonSearchAppTests.cs`.
- Tests:
  - `WindowInput`: the phase 3 constructor gives `Text` "" and empty `Keys`. The toggle tests pass with no change.
  - `PatternField`: typed text at the caret, each edit key, a caret at each end, and surrogate pairs as one code point.
  - `MatchMap`: each function with values from demo-map.ts and demo.test.ts, and `paintCells` for 20 and 100,000 documents.
  - `DocumentView`: glyph rows and columns for a text with line feeds, tabs, a wide code point and a surrogate pair; the color of each display kind; the mark rectangles with alternate colors.
  - `JsonSearchApp` with scripted frames and a fake service:
    - The text `error`, typed one character in each frame, sends one request in each frame, and the newest result shows.
    - `Control+2` selects Keys and sends a request with `SearchScope.Keys`. `Control+B` changes the backend.
    - A click on an example sets the pattern and the scope.
    - The pointer over the map picks the nearest document with a match. A click keeps it. `Enter` and `Shift+Enter` step to the next and the previous match and go around the ends.
    - A syntax error shows its sentence, and a pattern of 33 states shows the capacity sentence.
    - A CPU search in progress shows the "of the first" sentence and the gray cells of the documents that it has not reached.
- The manual window check of "Verification" passes.
- R items: R6.4. Carried: phase 3 keyboard input.

### Task 9: the close of the phase

- Files: `docs/design.md` (Layer 2: the five rules; Layer 3: loop programs, the loop writers, packed words, the compiled stage, the recurrent runner changes; Layer 4: the JSON search as built, with the step methods, and the measured times), the spec (R6.1 to R6.5 checked), `.superpowers/sdd/phase6/progress.md` (the ledger).
- Run the verification list. Record the test run times, the bench table and the pinned sizes in the ledger.
- R items: RA.1, RA.2, RA.3.

## Verification

- `dotnet build -warnaserror` passes.
- `dotnet test` passes (Debug). `dotnet test -c Release` passes.
- `grep -rn "TODO\|Skip =" src tests samples` gives no result.
- `grep -rln "Silk.NET" src samples tests --include=*.cs` gives only files in `src/Sharpwired.Gpu/`.
- `grep -rln "Raylib_cs" src samples tests` gives only `src/Sharpwired.Raylib/RaylibWindow.cs`.
- `grep -rln "unsafe" src --include='*.cs'` gives only `src/Sharpwired.Gpu/GpuDevice.cs`, `src/Sharpwired.Gpu/ComputeKernel.cs` and `src/Sharpwired.IL/IlDecoder.cs`.
- Each new source file is below 800 lines.
- The test output names each adapter, its backend and `FlushesSubnormals`, and gives the shader create times and the 100,000-document times.
- `dotnet run --project samples/JsonSearch.App -c Release -- --bench` writes the bench table. The ledger records it.
- The manual window check below passes.
- Each R6 item maps to at least one test:

| Item | Tests |
| --- | --- |
| R6.1 the document generator | `DocumentTests`, `DocumentCellTests` |
| R6.2 the highlighter and the search at 8, 16 and 32 states | `HighlighterTests`, `GpuHighlighterTests`, `PatternTests`, `SearchTests`, `GpuSearchTests`, `LoopWriterTests` (pinned sizes) |
| R6.3 the GPU and CPU paths over 100,000 documents, with .NET `Regex` timings | `SearcherTests`, `BaselineTests`, `GpuLoopRunnerTests`, `CpuLoopRunnerTests`, the bench table |
| R6.4 the window | `JsonSearchAppTests`, `MatchMapTests`, `DocumentViewTests`, `PatternFieldTests`, `WindowInputTests`, `DrawCommandTests`, the manual window check |
| R6.5 tests without a GPU against a plain C# reference | `JsonReferenceTests`, `HighlighterTests`, `PatternTests`, `SearchTests` (all run with `SHARPWIRED_NO_GPU` set and no adapter) |
| RA.1 | `NOTICE` keeps the Handwired MIT notice (no change) |
| RA.2 | Each new comment and doc in ASD-STE100 |
| RA.3 | The green `dotnet test` run at the end of each task |

### How to run the sample and check the window

Run from the worktree root:

```sh
dotnet run --project samples/JsonSearch.App -c Release
```

The manual check, after task 8:

1. Run the command above. Make sure that a window of 1,280 × 860 px opens with the title "JSON search", and that the line says that 100,000 documents are ready. The highlighter runs before the first frame that shows the map.
2. Make sure that the pattern field has the focus and a caret, that "All" and "GPU" are selected, and that the map shows gray cells.
3. Type `error`. Make sure that the map shows copper marks while you type, that the line gives the count of documents with a match, the program time on the GPU and the .NET `Regex` time, and that the GPU time stays below 100 ms.
4. Press `Control+3` (String values). Make sure that the count changes and that "String values" is selected.
5. Move the pointer over the map. Make sure that the document at the right changes to the nearest document with a match, with the colors of the display kinds and a mark under each match. Click. Make sure that the document stays when the pointer moves away.
6. Press `Enter` three times and `Shift+Enter` one time. Make sure that the document steps to the next matches and back.
7. Click the example `\d\d\d\d-\d\d-\d\d`. Make sure that the pattern and "All" change, and that the dates in the document have marks.
8. Click the example `([a-z]+)+x`. Make sure that the program time stays near the time of the other patterns, and that the .NET `Regex` time is long or says "more than 10 s".
9. Type `(` at the end of the field. Make sure that the line shows the syntax error sentence and that the map keeps the last result. Press `Backspace`.
10. Press `Control+B` (CPU). Make sure that the map fills from the top, that the line says "of the first … documents match … so far", and that a new typed character starts a new search at once.
11. Type a pattern of 33 literal characters. Make sure that the line says that the pattern needs 34 states and that the program holds 32.
12. Pick a document with `美咲` or `東京`. Make sure that these code points show as glyphs (with the macOS font) and take two cells.
13. Change the size of the window. Make sure that the map and the document stay inside the window.
14. Close the window with the close button or the Esc key. Make sure that the process stops without an error.

## Open questions

1. The form of the programs. This plan writes each stage as a `[Handwired]` step method, and a `LoopProgram` composes the stages (question 1). The alternative is a translator form 4: one `[Handwired]` method over a whole document, whose constant loops over the positions the translator lifts into stages. Form 4 is nearer to the design text "methods over the characters of a document", and it needs a new lowering of loops, registers and fields. Recommendation: the step methods, and a change of the Layer 4 text to "step methods over one character, composed into a loop program". Do you accept?
2. The translator rules. Question 4 adds four recognized forms (the clamp read, the gated read, the candidate cut, the `Math.Clamp` range) and one intrinsic (`Hw.ReadOrZero`). Without them, the search steps have 2.4 to 3.8 times the Handwired operations, and the packed reads of bit-emit.ts do not fire, because they need the gated read form. Recommendation: add them in task 1. Do you accept?
3. The CPU stage backend. This plan adds `CompiledStage`, a .NET delegate for each stage, as the default CPU backend. The probe measured 1.77 µs for a synthetic delegate of 3,200 rows, against 13 µs for `TensorEvaluator` on the reverse step at 32 states without its source check. The synthetic rows are not the real rows, so task 2 measures the real ratio. `TensorEvaluator` stays as the `Tensor` option, and the tests compare both. Recommendation: accept the new backend. Do you accept?
4. The font. The window loads `/System/Library/Fonts/Supplemental/Arial Unicode.ttf` on macOS for the non-ASCII code points of the documents (`美咲`, `東京`, `Søren`, `µg/m³`). On a system without one of the listed fonts, the window draws ASCII with the Raylib default font and `?` for other code points. I did not verify that this font holds each code point of the generator. Task 8 checks the glyph count that Raylib loads and writes the code points that the font does not hold. Do you accept a system font, or must the repository hold a font file with its license?
5. The document texts. I did not make the C# texts equal to the Handwired texts byte for byte (question 1, item 7). The tests pin a hash of the C# texts. Recommendation: accept equal structure and pin the C# hash. Do you accept?
6. The display kinds on the GPU. This plan keeps one 32-bit value for each position, 205 MB for 100,000 documents, as Handwired does. Four kinds in one word would use 51 MB and need a pack and an unpack statement in each kernel. Recommendation: one value for each position. Do you agree?
7. The spec text of R6.2 says "as in Handwired `src/json-search`". The values and the stages are as in Handwired. The operation counts can stay above the Handwired counts after the task 1 rules. Recommendation: read R6.2 as equal values and equal stages, and pin the measured counts next to the Handwired counts. Do you accept?
