// Handwired source: tests/json-search/json-step.test.ts and the highlighter part of tests/json-search/search.test.ts
// ("the step is 37 operations"). The highlighter loop program runs on the CPU runner, with both stage backends,
// against the plain C# reference (JsonReference). The reference does not read the tables or the step methods. The
// texts are the 2,509 corpus texts, the documents at the limits, and an object and an array at each depth. They
// also include the first 2,000 generated documents with defects. The compiled-delegate backend also runs the 43,420 short texts. LoopProgram.EvaluatePlain,
// the plain step methods in a loop, equals the CPU runner. The operation and layer counts are pinned next to the
// Handwired counts. On the reference evaluator, each declared grid and each index read holds at each position.

using Sharpwired.Core;
using Sharpwired.Gpu;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

public sealed class HighlighterTests(ITestOutputHelper output)
{
    private const int Capacity = Kinds.Capacity;
    private const int SummaryValues = 4;

    internal static readonly Lazy<LoopProgram> Program = new(HighlighterProgram.Build);

    public static TheoryData<CpuStageBackend> Backends => new() { CpuStageBackend.Compiled, CpuStageBackend.Tensor };

    // For each depth, the two texts whose container code is nearest the level weight of the depth, 2^(depth - 1).
    // An object in arrays, for example [[{"a":1}]] at depth 3, has a code of exactly the weight. So the step calls the
    // container an array when the weight of the table is larger. An array in objects, for example {"k":{"k":[1]}},
    // has a code of the weight minus 1. So the step calls the container an object when the weight of the table is smaller.
    internal static readonly string[] NestedTexts = [.. Enumerable.Range(1, Kinds.MaximumDepth).SelectMany(depth => new[]
    {
        new string('[', depth - 1) + "{\"a\":1}" + new string(']', depth - 1),
        string.Concat(Enumerable.Repeat("{\"k\":", depth - 1)) + "[1]" + new string('}', depth - 1),
    })];

    internal static IReadOnlyList<string> EdgeTexts => [.. EdgeCases.HandWritten().Select(document => document.Text)];

    /// <summary>The summaries (4 values for each text) and the display kinds (512 for each text) of a run on the CPU runner.</summary>
    internal static (float[] Summary, float[] Kinds) RunCpu(IReadOnlyList<string> texts, CpuStageBackend stages, bool stopAtLength = true)
    {
        var program = Program.Value;
        var summary = new float[texts.Count * SummaryValues];
        var kinds = new float[texts.Count * Capacity];
        using var backend = new CpuLoopBackend(new CpuLoopOptions(stages, stopAtLength));
        using var runner = backend.CreateRunner(program);
        const int Chunk = 8192;
        for (var first = 0; first < texts.Count; first += Chunk)
        {
            var count = Math.Min(Chunk, texts.Count - first);
            using var input = backend.Upload(DocumentCells.WriteAll([.. texts.Skip(first).Take(count)]), count, DocumentCells.CellsPerDocument);
            var run = runner.Run(new LoopRequest([input], count, [], 0, count));
            Assert.Empty(run.Faults);
            run.DocumentValues.CopyTo(summary.AsSpan(first * SummaryValues));
            run.WindowPositionValues.CopyTo(kinds.AsSpan(first * Capacity));
        }
        return (summary, kinds);
    }

    /// <summary>
    /// Assert that each text has the summary (Valid, FirstErrorPosition, Truncated, FirstErrorKind) and the 512
    /// display kinds of the reference, with Object.is. Return the references.
    /// </summary>
    internal static ReferenceResult[] AssertReference(IReadOnlyList<string> texts, float[] summary, float[] kinds, string where)
    {
        var references = new ReferenceResult[texts.Count];
        Parallel.For(0, texts.Count, row => references[row] = JsonReference.Highlight(texts[row]));
        for (var row = 0; row < texts.Count; row++)
        {
            var reference = references[row];
            float[] expected = [reference.Valid ? 1 : 0, reference.FirstErrorPosition, reference.Truncated ? 1 : 0, (int)reference.FirstErrorKind];
            var actual = summary.AsSpan(row * SummaryValues, SummaryValues).ToArray();
            var differing = Enumerable.Range(0, Capacity).FirstOrDefault(position => !Js.ObjectIs((int)reference.DisplayKinds[position], kinds[(row * Capacity) + position]), -1);
            if (differing >= 0 || !expected.Zip(actual).All(pair => Js.ObjectIs(pair.First, pair.Second)))
            {
                var kindText = differing < 0
                    ? "the display kinds are the same"
                    : $"the display kind at position {differing} is {kinds[(row * Capacity) + differing]}, and the reference gives {(int)reference.DisplayKinds[differing]}";
                Assert.Fail($"{System.Text.Json.JsonSerializer.Serialize(texts[row])} on {where}: valid, firstErrorPosition, truncated, firstErrorKind are {string.Join(", ", actual)}, and the reference gives {string.Join(", ", expected)}; {kindText}");
            }
        }
        return references;
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void The_highlighter_equals_the_reference_on_the_2509_corpus_texts(CpuStageBackend stages)
    {
        var (summary, kinds) = RunCpu(Corpus.All, stages);
        var references = AssertReference(Corpus.All, summary, kinds, stages.ToString());
        Assert.Equal(2509, references.Length);
        // The texts reach each kind of first error, and none, so each kind is compared.
        Assert.Equal(Enum.GetValues<ErrorKind>().Length, references.Select(reference => reference.FirstErrorKind).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void The_highlighter_equals_the_reference_on_the_documents_at_the_limits(CpuStageBackend stages)
    {
        var texts = EdgeTexts;
        var (summary, kinds) = RunCpu(texts, stages);
        var references = AssertReference(texts, summary, kinds, stages.ToString());
        Assert.True(references.Length > 250);
        Assert.Contains(references, reference => reference.Truncated);
        // The empty text, 511 to 514 code points, depth 16 and 17, and a surrogate pair at position 511.
        Assert.Contains("", texts);
        Assert.Contains(texts, text => CodePoints.Of(text) is { Length: Capacity + 1 } codePoints && codePoints[Capacity - 1] == 0x1f600);
        Assert.Contains(references, reference => reference.FirstErrorKind == ErrorKind.TooDeep);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void The_highlighter_equals_the_reference_on_an_object_and_an_array_at_each_depth(CpuStageBackend stages)
    {
        var (summary, kinds) = RunCpu(NestedTexts, stages);
        var references = AssertReference(NestedTexts, summary, kinds, stages.ToString());
        Assert.All(references, reference => Assert.True(reference.Valid));
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public void The_highlighter_equals_the_reference_on_the_first_2000_generated_documents_with_defects(CpuStageBackend stages)
    {
        var documents = Documents.Generate(2000);
        var texts = documents.Select(document => document.Text).ToArray();
        var (summary, kinds) = RunCpu(texts, stages);
        var references = AssertReference(texts, summary, kinds, stages.ToString());
        Assert.Equal(documents.Count(document => document.Defect == DocumentDefect.None), references.Count(reference => reference.Valid));
    }

    [Fact]
    public void The_compiled_backend_equals_the_reference_on_the_43420_short_texts()
    {
        var texts = ShortTexts.Create();
        Assert.Equal(43_420, texts.Count);
        var (summary, kinds) = RunCpu(texts, CpuStageBackend.Compiled);
        var references = AssertReference(texts, summary, kinds, "the compiled backend");
        // The texts are at most 125 code points long, so they reach each kind of first error except TooLong, and none.
        Assert.Equal(Enum.GetValues<ErrorKind>().Length - 1, references.Select(reference => reference.FirstErrorKind).Distinct().Count());
        Assert.True(references.Count(reference => reference.Valid) > 1_500);
    }

    [Fact]
    public void Stop_at_length_gives_the_same_outputs_because_the_program_has_no_length_cell()
    {
        Assert.Null(Program.Value.LengthCell);
        var texts = Corpus.All.Take(400).ToArray();
        var stopped = RunCpu(texts, CpuStageBackend.Compiled, stopAtLength: true);
        var full = RunCpu(texts, CpuStageBackend.Compiled, stopAtLength: false);
        Assert.Equal(full.Summary, stopped.Summary);
        Assert.Equal(full.Kinds, stopped.Kinds);
    }

    [Fact]
    public void The_plain_step_methods_in_a_loop_equal_the_cpu_runner()
    {
        var texts = Corpus.All.Where((_, index) => index % 7 == 0).Concat(NestedTexts).Concat(EdgeTexts.Where((_, index) => index % 5 == 0)).ToArray();
        var (summary, kinds) = RunCpu(texts, CpuStageBackend.Compiled);
        var cells = new float[DocumentCells.CellsPerDocument];
        for (var row = 0; row < texts.Length; row++)
        {
            DocumentCells.Write(cells, texts[row]);
            var plain = Program.Value.EvaluatePlain([cells.ToArray()], []);
            Js.AllIs(plain.DocumentOutputs.Select(value => (double)value), summary.Skip(row * SummaryValues).Take(SummaryValues).Select(value => (double)value));
            Js.AllIs(plain.PositionOutputs.Select(value => (double)value), kinds.Skip(row * Capacity).Take(Capacity).Select(value => (double)value));
        }
        output.WriteLine($"{texts.Length} texts compared");
    }

    [Fact]
    public void The_step_and_the_end_have_the_pinned_sizes()
    {
        // A Handwired table cell is a constant source cell. A Sharpwired table cell is a constant wire, an operation
        // with no terms. So the count to compare is the live operations that compute: the live operations of the
        // compiled program with a term or an index read. "Operations" counts each operation of the translated
        // circuit, also the constants and the operations that no output reads.
        var program = Program.Value;
        var lines = new List<string>();
        foreach (var stage in program.Stages)
        {
            var circuit = stage.Function.Table.Circuit;
            var roots = stage.Function.Outputs.Select(leaf => leaf.Wire).Distinct().ToList();
            var tensor = circuit.CompileTensorProgram(roots);
            var live = tensor.WireOrder.Skip(tensor.SourceSize).Select(wire => circuit.Operations[wire - circuit.SourceSize]).ToList();
            var computing = live.Count(operation => operation.Kind == OperationKind.Index || operation.LeftTerms.Length > 0 || operation.GateTerms.Length > 0);
            var reads = live.Count(operation => operation.Kind == OperationKind.Index);
            lines.Add($"{stage.Name}: {circuit.Operations.Count} operations, {computing} live operations that compute, {reads} live index reads, {tensor.Layers.Length} layers");
        }
        foreach (var line in lines) output.WriteLine(line);
        // Handwired search.test.ts: step 37 operations with 12 index reads, end 17 operations with 6 index reads.
        // Step has 3 more. The two error-state bits are 2 operations each (Handwired: 1 ReGLU each). The first
        // error bit is one operation more than the ReGLU of Handwired. End has the same 3 more, and 2 fewer. The
        // affine form of firstErrorPosition composes errorFreeCount (Handwired: its own roundAffine). The output
        // truncated is the bit itself (Handwired: a roundAffine of the bit). So End has 1 more.
        Assert.Equal(
            [
                "Step: 103 operations, 40 live operations that compute, 12 live index reads, 24 layers",
                "End: 48 operations, 18 live operations that compute, 6 live index reads, 10 layers",
            ],
            lines);
    }

    [Fact]
    public void Each_declared_grid_and_index_read_holds_at_each_position_on_the_reference_evaluator()
    {
        // The nested texts and each tenth document at the limits. Also, for each kind of first error and for none,
        // the first three corpus texts that have it.
        var ofKind = Enum.GetValues<ErrorKind>().ToDictionary(kind => kind, _ => new List<string>());
        foreach (var text in Corpus.All)
        {
            var list = ofKind[JsonReference.Highlight(text).FirstErrorKind];
            if (list.Count < 3) list.Add(text);
        }
        var texts = NestedTexts.Concat(EdgeTexts.Where((_, index) => index % 10 == 0)).Concat(ofKind.Values.SelectMany(list => list)).ToArray();
        var program = Program.Value;
        var cells = new float[DocumentCells.CellsPerDocument];
        var evaluations = 0;
        foreach (var text in texts)
        {
            DocumentCells.Write(cells, text);
            var registers = program.Registers.Select(register => register.Initial).ToArray();
            var summary = new float[SummaryValues];
            var kinds = Enumerable.Repeat((float)DisplayKind.PastEnd, Capacity).ToArray();
            foreach (var stage in program.Stages)
            {
                var circuit = stage.Function.Table.Circuit;
                var count = stage.Repeat == StageRepeat.Once ? 1 : program.Positions;
                for (var position = 0; position < count; position++)
                {
                    var source = new float[circuit.SourceSize];
                    foreach (var input in stage.Function.Inputs)
                    {
                        source[input.Wire.Position] = stage.Cells[input.Path] switch
                        {
                            CellSource.Item => cells[position],
                            CellSource.DocumentCell fixedCell => cells[fixedCell.Column],
                            CellSource.Register register => registers[register.Index],
                            var other => throw new InvalidOperationException($"the highlighter reads no {other}"),
                        };
                    }
                    var values = circuit.EvaluateReference(source);
                    circuit.ValidateIndexReads(values);
                    circuit.ValidateGrids(values);
                    circuit.ValidateNoNaN(values);
                    // Each value of the program is a whole number within ±2^24, so no value is subnormal.
                    Assert.All(values, value => Assert.True(float.IsInteger(value) && Math.Abs(value) <= 16_777_216, $"a wire holds {value}"));
                    evaluations++;
                    var next = registers.ToArray();
                    foreach (var leaf in stage.Function.Outputs)
                    {
                        var value = values[leaf.Wire.Position];
                        switch (stage.Writes[leaf.Path])
                        {
                            case StageWrite.Register register:
                                next[register.Index] = value;
                                break;
                            case StageWrite.Position:
                                kinds[position] = value;
                                break;
                            case StageWrite.Document document:
                                summary[document.Column] = value;
                                break;
                        }
                    }
                    registers = next;
                }
            }
            var reference = JsonReference.Highlight(text);
            Js.AllIs([reference.Valid ? 1 : 0, reference.FirstErrorPosition, reference.Truncated ? 1 : 0, (int)reference.FirstErrorKind], summary.Select(value => (double)value));
            Js.AllIs(reference.DisplayKinds.Select(kind => (double)(int)kind), kinds.Select(value => (double)value));
        }
        Assert.Equal(NestedTexts.Length + 28 + (3 * Enum.GetValues<ErrorKind>().Length), texts.Length);
        output.WriteLine($"{texts.Length} texts, {evaluations} evaluations");
    }

    [Fact]
    public void The_program_has_the_handwired_stages_registers_and_outputs()
    {
        var program = Program.Value;
        Assert.Equal(Capacity, program.Positions);
        Assert.Equal([new LoopBuffer("documents", DocumentCells.CellsPerDocument)], program.Buffers.ToArray());
        Assert.Equal(["lexerState", "depth", "code", "grammarState", "errorCount", "firstErrorKind"], program.Registers.Select(register => register.Name));
        Assert.All(program.Registers, register => Assert.Equal(0, register.Initial));
        Assert.Empty(program.Fields);
        Assert.Equal(
            [new LoopOutputGroup("summary", OutputGroupKind.Document, 4, 0), new LoopOutputGroup("displayKinds", OutputGroupKind.Position, Capacity, (int)DisplayKind.PastEnd)],
            program.OutputGroups.ToArray());
        Assert.Equal([("Step", StageRepeat.Forward), ("End", StageRepeat.Once)], program.Stages.Select(stage => (stage.Name, stage.Repeat)));
        Assert.Equal(0, program.SharedSize);
        Assert.Equal(new CellSource.DocumentCell(0, Capacity), program.Stages[1].Cells["textLength"]);
    }
}
