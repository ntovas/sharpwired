// Handwired source: tests/json-search/regex/regex-search.test.ts and tests/json-search/search.test.ts (the search
// loop program against the automaton reference). The search programs at 8, 16 and 32 states on the CPU runner,
// with both stage backends, against RegexReference.AutomatonMatches. The inputs are the 26 hand-written cases, and the
// seeded random patterns with 6 texts each. The inputs also include patterns at the capacity over full texts of
// 512 code points. The inputs also include each scope over 300 generated documents with the display kinds of the highlighter. Also StopAtLength, LoopProgram.EvaluatePlain,
// the declarations at each position on the reference evaluator, and the cells that WritePattern writes. The sizes
// are in SearchTests.Sizes.cs, and the documents and searches at the limits in SearchTests.Edges.cs.

using Sharpwired.Core;
using Sharpwired.Gpu;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

public sealed partial class SearchTests(ITestOutputHelper output)
{
    internal const int Positions = SearchCells.Positions;

    internal static readonly IReadOnlyDictionary<int, Lazy<SearchProgram>> Programs =
        SearchCapacity.All.ToDictionary(capacity => capacity.States, capacity => new Lazy<SearchProgram>(() => SearchProgram.Build(capacity)));

    public static TheoryData<int, CpuStageBackend> CapacitiesAndBackends
    {
        get
        {
            var data = new TheoryData<int, CpuStageBackend>();
            foreach (var capacity in SearchCapacity.All)
            {
                data.Add(capacity.States, CpuStageBackend.Compiled);
                data.Add(capacity.States, CpuStageBackend.Tensor);
            }
            return data;
        }
    }

    public static TheoryData<int> Capacities => new() { 8, 16, 32 };

    /// <summary>The document cells of the code points: the first 512, -1 after the text, then the length.</summary>
    internal static float[] CellsOf(IReadOnlyList<int[]> texts)
    {
        var cells = new float[texts.Count * DocumentCells.CellsPerDocument];
        for (var row = 0; row < texts.Count; row++)
        {
            var span = cells.AsSpan(row * DocumentCells.CellsPerDocument, DocumentCells.CellsPerDocument);
            for (var position = 0; position < Positions; position++) span[position] = position < texts[row].Length ? texts[row][position] : -1;
            span[Positions] = texts[row].Length;
        }
        return cells;
    }

    /// <summary>The shared cells of the pattern and the scope.</summary>
    internal static float[] SharedOf(SearchProgram program, CompiledPattern compiled, IReadOnlyCollection<DisplayKind> kinds)
    {
        var shared = program.InitialShared();
        program.WritePattern(shared, compiled);
        program.WriteKinds(shared, kinds);
        return shared;
    }

    /// <summary>
    /// The reference of a masked search over the first 512 code points. It is AutomatonMatches with -1 in place of
    /// each code point whose display kind the search does not read. With no kinds, the search reads each code point.
    /// </summary>
    internal static List<Match> ReferenceMatches(CompiledPattern compiled, int[] codePoints, ReadOnlySpan<float> kinds = default, IReadOnlyCollection<DisplayKind>? searched = null)
    {
        var masked = new int[Math.Min(codePoints.Length, Positions)];
        for (var position = 0; position < masked.Length; position++)
        {
            var reads = searched is null || searched.Contains((DisplayKind)(int)kinds[position]);
            masked[position] = reads ? codePoints[position] : -1;
        }
        return RegexReference.AutomatonMatches(compiled, masked);
    }

    /// <summary>A CPU runner of one search program, and the runs of a pattern over documents.</summary>
    internal sealed class CpuSearch : IDisposable
    {
        private readonly CpuLoopBackend backend;
        private readonly ILoopRunner runner;

        public CpuSearch(SearchProgram program, CpuStageBackend stages = CpuStageBackend.Compiled, bool stopAtLength = true)
        {
            Program = program;
            backend = new CpuLoopBackend(new CpuLoopOptions(stages, stopAtLength));
            runner = backend.CreateRunner(program.Loop);
        }

        public SearchProgram Program { get; }

        /// <summary>The match count of each document, and the 512 marks of each document.</summary>
        public (float[] Counts, float[] Marks) Run(float[] shared, float[] cells, float[] kinds, int count)
        {
            runner.WriteShared(shared);
            using var documents = backend.Upload(cells, count, DocumentCells.CellsPerDocument);
            using var kindBuffer = backend.Upload(kinds, count, Positions);
            var run = runner.Run(new LoopRequest([documents, kindBuffer], count, [null], 0, count));
            Assert.Empty(run.Faults);
            Assert.Equal(count, run.DocumentsDone);
            return ([.. run.DocumentValues], [.. run.WindowPositionValues]);
        }

        /// <summary>The run of the pattern over the texts with each kind searched.</summary>
        public (float[] Counts, float[] Marks) Run(CompiledPattern compiled, IReadOnlyList<int[]> texts) =>
            Run(SharedOf(Program, compiled, SearchProgram.KindsOf(SearchScope.All)), CellsOf(texts), new float[texts.Count * Positions], texts.Count);

        public void Dispose()
        {
            runner.Dispose();
            backend.Dispose();
        }
    }

    /// <summary>Assert that each document has the count and the marks of the matches.</summary>
    internal static void AssertMatches(string what, IReadOnlyList<List<Match>> expected, float[] counts, float[] marks)
    {
        for (var row = 0; row < expected.Count; row++)
        {
            var found = RegexReference.MatchesFromMarks(marks.AsSpan(row * Positions, Positions));
            if (counts[row] != expected[row].Count || TestPatterns.Pairs(found) != TestPatterns.Pairs(expected[row]))
            {
                Assert.Fail($"{what}, document {row}: {counts[row]} matches at {TestPatterns.Pairs(found)}, and the reference gives {expected[row].Count} at {TestPatterns.Pairs(expected[row])}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CapacitiesAndBackends))]
    public void The_search_finds_the_hand_written_matches(int states, CpuStageBackend stages)
    {
        using var search = new CpuSearch(Programs[states].Value, stages);
        foreach (var (pattern, text, expected) in TestPatterns.HandWrittenCases)
        {
            var compiled = PatternCompiler.Compile(pattern);
            var codePoints = CodePoints.Of(text);
            var (counts, marks) = search.Run(compiled, [codePoints]);
            Assert.Equal((pattern, text, TestPatterns.Show(expected)), (pattern, text, TestPatterns.Pairs(RegexReference.MatchesFromMarks(marks))));
            Assert.Equal(expected.Length, counts[0]);
            AssertMatches(pattern, [RegexReference.AutomatonMatches(compiled, codePoints)], counts, marks);
        }
    }

    [Theory]
    [MemberData(nameof(CapacitiesAndBackends))]
    public void The_search_equals_the_automaton_on_the_random_patterns_that_fit(int states, CpuStageBackend stages)
    {
        var program = Programs[states].Value;
        using var search = new CpuSearch(program, stages);
        var random = Js.CreateRandom(20260917);
        var fitting = 0;
        var matches = 0;
        for (var index = 0; index < 400; index++)
        {
            var pattern = TestPatterns.RandomPattern(random, 3);
            var compiled = PatternCompiler.Compile(pattern);
            var texts = Enumerable.Range(0, 4).Select(_ => TestPatterns.CodePointsOf(TestPatterns.RandomText(random, 16)))
                .Concat(Enumerable.Range(0, 2).Select(_ => TestPatterns.BoundaryText(random, compiled, 16)))
                .ToList();
            if (!PatternCompiler.CapacityErrors(compiled, program.Capacity).IsEmpty) continue;
            fitting++;
            var expected = texts.Select(text => RegexReference.AutomatonMatches(compiled, text)).ToList();
            var (counts, marks) = search.Run(compiled, texts);
            AssertMatches(pattern, expected, counts, marks);
            matches += expected.Sum(list => list.Count);
        }
        output.WriteLine($"{states} states, {stages}: {fitting} of 400 patterns fit, {matches} matches");
        Assert.True(fitting > 250, $"{fitting} patterns fit");
        Assert.True(matches > 6 * fitting, $"{matches} matches");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    public void The_search_equals_the_automaton_on_patterns_at_the_capacity_over_full_texts(int states)
    {
        var program = Programs[states].Value;
        using var search = new CpuSearch(program);
        var random = Js.CreateRandom(4242);
        var patterns = 0;
        var matches = 0;
        while (patterns < 12)
        {
            var pattern = TestPatterns.RandomPattern(random, states == 16 ? 4 : 5);
            var compiled = PatternCompiler.Compile(pattern);
            if (compiled.StateCount <= states / 2 || !PatternCompiler.CapacityErrors(compiled, program.Capacity).IsEmpty) continue;
            int[][] texts = [TestPatterns.FullText(random, Positions), TestPatterns.FullText(random, Positions), TestPatterns.BoundaryText(random, compiled, Positions)];
            var expected = texts.Select(text => RegexReference.AutomatonMatches(compiled, text)).ToList();
            var (counts, marks) = search.Run(compiled, texts);
            AssertMatches(pattern, expected, counts, marks);
            matches += expected.Sum(list => list.Count);
            patterns++;
        }
        output.WriteLine($"{states} states: {patterns} patterns, {matches} matches");
        Assert.True(matches > 12 * 30, $"{matches} matches");
    }

    [Fact]
    public void The_largest_capacity_finds_a_match_on_the_last_cell()
    {
        // 31 atoms and 13 classes.
        const string Pattern = "(the|an?|some|this|that|these) [a-z]+(ing|ed|s)? ";
        var compiled = PatternCompiler.Compile(Pattern);
        Assert.Equal((32, 13), (compiled.StateCount, compiled.ClassCount));
        var words = string.Concat(Enumerable.Repeat("the cat sat on some mats, this dog barked at these cats and an owl hooted. that ", 8));
        // The words up to the last space before position 502, then spaces, then "this word " at 502.
        var prefix = words[..(Positions - "this word ".Length)];
        prefix = prefix[..(prefix.LastIndexOf(' ') + 1)].PadRight(Positions - "this word ".Length);
        var codePoints = CodePoints.Of(prefix + "this word ");
        Assert.Equal(Positions, codePoints.Length);
        var expected = RegexReference.AutomatonMatches(compiled, codePoints);
        Assert.Equal(new Match(502, 512), expected[^1]);
        using var search = new CpuSearch(Programs[32].Value);
        var (counts, marks) = search.Run(compiled, [codePoints]);
        AssertMatches(Pattern, [expected], counts, marks);
        Assert.Equal(2, marks[502]);
        Assert.Equal(1, marks[511]);
    }

    [Theory]
    [MemberData(nameof(Capacities))]
    public void Each_scope_over_300_generated_documents_equals_the_automaton_over_the_searched_kinds(int states)
    {
        var program = Programs[states].Value;
        var texts = Documents.Generate(300).Select(document => document.Text).ToArray();
        var cells = DocumentCells.WriteAll(texts);
        var (_, kinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        var references = texts.Select(JsonReference.Highlight).ToArray();
        using var search = new CpuSearch(program);
        var byScope = new Dictionary<SearchScope, int>();
        foreach (var pattern in new[] { "[a-z0-9.]+@[a-z.]+", "[a-z]+Id", "\\d\\d\\d\\d-\\d\\d-\\d\\d", "([a-z]+)+x", "(the|an?) [a-z]+", "\"", "\\d+", "e" })
        {
            var compiled = PatternCompiler.Compile(pattern);
            if (!PatternCompiler.CapacityErrors(compiled, program.Capacity).IsEmpty) continue;
            foreach (var scope in Enum.GetValues<SearchScope>())
            {
                var searched = SearchProgram.KindsOf(scope);
                var (counts, marks) = search.Run(SharedOf(program, compiled, searched), cells, kinds, texts.Length);
                var expected = texts.Select((text, row) => ReferenceMatches(compiled, CodePoints.Of(text), references[row].DisplayKinds.Select(kind => (float)(int)kind).ToArray(), searched)).ToList();
                AssertMatches($"{pattern} in {scope}", expected, counts, marks);
                byScope[scope] = byScope.GetValueOrDefault(scope) + expected.Sum(list => list.Count);
            }
        }
        output.WriteLine($"{states} states: matches by scope {string.Join(", ", byScope.Select(pair => $"{pair.Key} {pair.Value}"))}");
        Assert.All(byScope.Values, count => Assert.True(count > 20, $"{count} matches in a scope"));
        Assert.True(byScope[SearchScope.StringValues] < byScope[SearchScope.All]);
        Assert.True(byScope[SearchScope.Keys] < byScope[SearchScope.All]);
    }

    [Theory]
    [MemberData(nameof(Capacities))]
    public void Stop_at_length_true_and_false_give_equal_outputs(int states)
    {
        var program = Programs[states].Value;
        Assert.Equal(new LengthCell(0, DocumentCells.Capacity), program.Loop.LengthCell);
        var texts = Documents.Generate(200).Select(document => document.Text).ToArray();
        var cells = DocumentCells.WriteAll(texts);
        var (_, kinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        using var stopped = new CpuSearch(program, stopAtLength: true);
        using var full = new CpuSearch(program, stopAtLength: false);
        var compared = 0;
        foreach (var pattern in new[] { "[a-z]+", "\"[^\"]*\"", "", "[^]", "\\d+" })
        {
            var shared = SharedOf(program, PatternCompiler.Compile(pattern), SearchProgram.KindsOf(SearchScope.All));
            var a = stopped.Run(shared, cells, kinds, texts.Length);
            var b = full.Run(shared, cells, kinds, texts.Length);
            Assert.Equal(b.Counts, a.Counts);
            Assert.Equal(b.Marks, a.Marks);
            compared += a.Counts.Length;
        }
        output.WriteLine($"{states} states: {compared} document runs compared");
    }

    [Theory]
    [MemberData(nameof(Capacities))]
    public void The_plain_step_methods_in_a_loop_equal_the_cpu_runner(int states)
    {
        var program = Programs[states].Value;
        // The plain step methods run each of the 512 positions. A call of the plain Reverse at 32 states takes
        // near 1 ms, so the larger programs run fewer documents.
        var generated = states == 8 ? 40 : states == 16 ? 12 : 4;
        var texts = Documents.Generate(generated).Select(document => document.Text).Concat(TestPatterns.HandWrittenCases.Take(generated / 2).Select(entry => entry.Text)).ToArray();
        var cells = DocumentCells.WriteAll(texts);
        var (_, kinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        using var search = new CpuSearch(program);
        foreach (var (pattern, scope) in new[] { ("[a-z]+", SearchScope.All), ("(a|ab)(c|bcd)", SearchScope.All), ("[a-z]+Id", SearchScope.Keys), ("\\d+", SearchScope.StringValues) })
        {
            var shared = SharedOf(program, PatternCompiler.Compile(pattern), SearchProgram.KindsOf(scope));
            var (counts, marks) = search.Run(shared, cells, kinds, texts.Length);
            for (var row = 0; row < texts.Length; row++)
            {
                var plain = program.Loop.EvaluatePlain(
                    [cells.AsMemory(row * DocumentCells.CellsPerDocument, DocumentCells.CellsPerDocument), kinds.AsMemory(row * Positions, Positions)],
                    shared);
                Js.AllIs([counts[row]], plain.DocumentOutputs.Select(value => (double)value));
                Js.AllIs(marks.Skip(row * Positions).Take(Positions).Select(value => (double)value), plain.PositionOutputs.Select(value => (double)value));
            }
        }
    }

    [Fact]
    public void Each_declaration_holds_at_each_position_on_the_reference_evaluator()
    {
        var program = Programs[8].Value;
        var random = Js.CreateRandom(7);
        var checkedPatterns = 0;
        var matches = 0;
        var evaluations = 0;
        while (checkedPatterns < 60)
        {
            var pattern = TestPatterns.RandomPattern(random, 3);
            var compiled = PatternCompiler.Compile(pattern);
            if (!PatternCompiler.CapacityErrors(compiled, program.Capacity).IsEmpty) continue;
            var codePoints = TestPatterns.CodePointsOf(TestPatterns.RandomText(random, 16));
            var shared = SharedOf(program, compiled, SearchProgram.KindsOf(SearchScope.All));
            var cells = CellsOf([codePoints]);
            var kinds = new float[Positions];
            var registers = program.Loop.Registers.Select(register => register.Initial).ToArray();
            var fields = program.Loop.Fields.Select(field => new float[Positions * field.Width]).ToArray();
            var marks = new float[Positions];
            var count = float.NaN;
            foreach (var stage in program.Loop.Stages)
            {
                var circuit = stage.Function.Table.Circuit;
                var steps = stage.Repeat == StageRepeat.Once ? 1 : Positions;
                for (var step = 0; step < steps; step++)
                {
                    var position = stage.Repeat == StageRepeat.Backward ? Positions - 1 - step : step;
                    var source = new float[circuit.SourceSize];
                    foreach (var input in stage.Function.Inputs)
                    {
                        source[input.Wire.Position] = Source(stage, input.Path) switch
                        {
                            CellSource.Item { Buffer: 0 } => cells[position],
                            CellSource.Item => kinds[position],
                            CellSource.Register register => registers[register.Index],
                            CellSource.Field field => fields[field.Index][(position * program.Loop.Fields[field.Index].Width) + field.Component],
                            CellSource.Shared sharedCell => shared[sharedCell.Offset],
                            var other => throw new InvalidOperationException($"the search reads no {other}"),
                        };
                    }
                    var values = circuit.EvaluateReference(source);
                    circuit.ValidateIndexReads(values);
                    circuit.ValidateGrids(values);
                    circuit.ValidateNoNaN(values);
                    // Each value is a whole number within ±2^24, so no value is subnormal.
                    Assert.All(values, value => Assert.True(float.IsInteger(value) && Math.Abs(value) <= 16_777_216, $"a wire holds {value}"));
                    evaluations++;
                    var next = registers.ToArray();
                    foreach (var leaf in stage.Function.Outputs)
                    {
                        var value = values[leaf.Wire.Position];
                        switch (Write(stage, leaf.Path))
                        {
                            case StageWrite.Register register:
                                next[register.Index] = value;
                                break;
                            case StageWrite.Field field:
                                fields[field.Index][(position * program.Loop.Fields[field.Index].Width) + field.Component] = value;
                                break;
                            case StageWrite.Position:
                                marks[position] = value;
                                break;
                            case StageWrite.Document:
                                count = value;
                                break;
                        }
                    }
                    registers = next;
                }
            }
            var expected = RegexReference.AutomatonMatches(compiled, codePoints);
            Assert.Equal((pattern, TestPatterns.Pairs(expected)), (pattern, TestPatterns.Pairs(RegexReference.MatchesFromMarks(marks))));
            Assert.Equal(expected.Count, count);
            matches += expected.Count;
            checkedPatterns++;
        }
        output.WriteLine($"{checkedPatterns} patterns, {matches} matches, {evaluations} evaluations");
        Assert.True(matches > 60, $"{matches} matches");
    }

    // The source of a cell path: the exact key, or the array key moved by the element index.
    private static CellSource Source(LoopStage stage, string path)
    {
        if (stage.Cells.TryGetValue(path, out var exact)) return exact;
        var open = path.LastIndexOf('[');
        var index = int.Parse(path[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        return stage.Cells[path[..open]] switch
        {
            CellSource.Register register => register with { Index = register.Index + index },
            CellSource.Field field => field with { Component = field.Component + index },
            CellSource.Shared shared => shared with { Offset = shared.Offset + index },
            var other => other,
        };
    }

    private static StageWrite Write(LoopStage stage, string path)
    {
        if (stage.Writes.TryGetValue(path, out var exact)) return exact;
        var open = path.LastIndexOf('[');
        var index = int.Parse(path[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        return stage.Writes[path[..open]] switch
        {
            StageWrite.Register register => register with { Index = register.Index + index },
            StageWrite.Field field => field with { Component = field.Component + index },
            var other => other,
        };
    }

    [Fact]
    public void Write_pattern_writes_the_handwired_cells()
    {
        var program = Programs[8].Value;
        var shared = program.InitialShared();
        program.WritePattern(shared, PatternCompiler.Compile("[a-c]+"));
        program.WriteScope(shared, SearchScope.Keys);
        // searchable[9]: only the kind Key.
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 1], shared.Take(9));
        // boundaries[32]: 97 and 100, then 0x110000 in each unused boundary.
        Assert.Equal(new float[] { 97, 100 }.Concat(Enumerable.Repeat((float)0x110000, 30)), shared.Skip(9).Take(32));
        // regionClasses[34]: cell 0 for -1 holds 16, then the regions -1 (16), 0 and -1 (16), then 16.
        Assert.Equal(new float[] { 16, 16, 0 }.Concat(Enumerable.Repeat(16f, 31)), shared.Skip(41).Take(34));
        // transitions[(from × 8 + to) × 17 + class]: from 0 and from 1 to 1 with class 0. The 17th class is 0.
        var transitions = shared.Skip(75).Take(17 * 64).ToArray();
        Assert.Equal(2, transitions.Count(value => value == 1));
        Assert.Equal(1, transitions[((0 * 8) + 1) * 17]);
        Assert.Equal(1, transitions[((1 * 8) + 1) * 17]);
        // accepting[8]: state 1.
        Assert.Equal([0, 1, 0, 0, 0, 0, 0, 0], shared.Skip(75 + (17 * 64)).Take(8));
        Assert.Equal(75 + (17 * 64) + 8, program.Loop.SharedSize);

        var error = Assert.Throws<ArgumentException>(() => program.WritePattern(shared, PatternCompiler.Compile("abcdefgh")));
        Assert.StartsWith("the pattern needs 9 states, and the program holds 8", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => program.WritePattern(new float[3], PatternCompiler.Compile("a")));
        Assert.Throws<ArgumentException>(() => SearchProgram.StepClass(new SearchCapacity(12)));
    }

    [Fact]
    public void The_program_has_the_handwired_stages_registers_fields_and_outputs()
    {
        var loop = Programs[16].Value.Loop;
        Assert.Equal(Positions, loop.Positions);
        Assert.Equal([new LoopBuffer("documents", DocumentCells.CellsPerDocument), new LoopBuffer("kinds", Positions)], loop.Buffers.ToArray());
        Assert.Equal(33, loop.Registers.Length);
        Assert.All(loop.Registers.Take(16), register => Assert.Equal(1, register.Initial));
        Assert.All(loop.Registers.Skip(16), register => Assert.Equal(0, register.Initial));
        Assert.Equal([new LoopField("codePointClass", 1, false), new LoopField("notReach", 16, true)], loop.Fields.ToArray());
        Assert.Equal(
            [new LoopOutputGroup("matchCount", OutputGroupKind.Document, 1, 0), new LoopOutputGroup("marks", OutputGroupKind.Position, Positions, 0)],
            loop.OutputGroups.ToArray());
        Assert.Equal(
            [("Start", StageRepeat.Once), ("Reverse", StageRepeat.Backward), ("Forward", StageRepeat.Forward), ("Finish", StageRepeat.Once)],
            loop.Stages.Select(stage => (stage.Name, stage.Repeat)));
    }
}
