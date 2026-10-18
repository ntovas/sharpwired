// Handwired source: tests/json-search/bit-emit.test.ts and writer-probes.test.ts (the near misses). The first
// file holds the rules that fire on small loop programs, and the values of both shaders through the strict
// WGSL interpreter. The kernels of both loop writers run in LoopRowInterpreter and equal the CPU runner on the
// loop inputs. They equal the values of each valid document (Object.is for the f32 writer, sameNumericValue
// for the integer writer, which has no -0). They also equal the status of each document (1 exactly where the
// CPU runner gives a fault). The rules of the
// integer writer fire on their programs and not on their near misses, and the statement counts are pinned.
// These tests need no adapter.

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Sharpwired.Core;
using Xunit.Abstractions;
using static Sharpwired.Gpu.Tests.LoopInputPrograms;

namespace Sharpwired.Gpu.Tests;

/// <summary>The programs of the writer tests, their shared cells and their documents.</summary>
internal static class LoopCases
{
    public const int Positions = 32;

    public static readonly string[] Names =
        ["Sum", "Leaders", "LeadersBits", "Lookup", "LookupFaults", "Overflow", "Switches", "OneByOne", "Mixed", "DeclaredField", "Fma", "Search8", "Delay", "DelayInt", "DelayBits", "Mul", "SignField", "OneHot"];

    public static (LoopProgram Program, float[] Shared, float[] Documents) Case(string name, int count, int seed) => name switch
    {
        "Sum" => (Sum(Positions), [], CreateDocuments(count, Positions, seed)),
        "Leaders" => (Leaders(Positions), [], CreateDocuments(count, Positions, seed)),
        "LeadersBits" => (LeadersBits(), [], LeadersBitsDocuments(count, seed)),
        "Lookup" => (Lookup(Positions), LookupShared(), CreateDocuments(count, Positions, seed)),
        "LookupFaults" => (Lookup(Positions), LookupShared(), LookupFaultDocuments(count, seed)),
        "Overflow" => (Overflow(Positions), [], CreateDocuments(count, Positions, seed, overflowAt: 7)),
        _ => (Program(name), LoopWriterPrograms.Shared(name, seed), LoopWriterPrograms.DocumentCells(name, count, seed)),
    };

    private static LoopProgram Program(string name) => name switch
    {
        "Switches" => LoopWriterPrograms.Switches(),
        "OneByOne" => LoopWriterPrograms.OneByOne(),
        "Mixed" => LoopWriterPrograms.Mixed(),
        "Fma" => LoopWriterPrograms.Fma(),
        "DeclaredField" => LoopWriterPrograms.DeclaredField(),
        "Search8" => LoopWriterPrograms.Search8(),
        "Delay" => LoopWriterPrograms.Delay(whole: false),
        "DelayInt" => LoopWriterPrograms.Delay(whole: true),
        "DelayBits" => LoopWriterPrograms.DelayBits(),
        "Mul" => LoopWriterPrograms.Mul(),
        "SignField" => LoopWriterPrograms.SignField(),
        "OneHot" => LoopWriterPrograms.OneHot(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "no such program"),
    };

    // The Lookup documents, with an item past the declared range 0..1000 in each 50th document. Thus a program
    // with a document group of 2 values has faults.
    private static float[] LookupFaultDocuments(int count, int seed)
    {
        var cells = CreateDocuments(count, Positions, seed);
        for (var document = 7; document < count; document += 50) cells[(document * (Positions + 1)) + 2] = 2000;
        return cells;
    }

    /// <summary>
    /// The index of each value of a document in LoopRun.DocumentValues, with its name. The document groups come
    /// group after group, and in a group document after document, with ValuesPerDocument values each.
    /// </summary>
    public static IEnumerable<(int Index, string Name)> DocumentValueIndices(LoopProgram program, int count, int document)
    {
        var start = 0;
        foreach (var group in program.OutputGroups.Where(group => group.Kind == OutputGroupKind.Document))
        {
            for (var column = 0; column < group.ValuesPerDocument; column++) yield return (start + (document * group.ValuesPerDocument) + column, $"{group.Name}[{column}]");
            start += group.ValuesPerDocument * count;
        }
    }

    // The Leaders program with a field of bits that gets the largest item. Each document whose largest item is
    // not 0 or 1 gets a fault.
    public static LoopProgram LeadersBits() => LoopProgram.Create(Positions, [Documents(Positions)], LeadersRegisters(),
        [new LoopField("largest", 1, true), new LoopField("flags", 2, true)], LeadersGroups(Positions),
        [LeadersBackwardStage(), LeadersForwardStage(), LeadersTotalStage()], 0, new LengthCell(0, Positions));

    // Documents whose items are mostly 0 and 1, so that some documents keep the field of bits valid.
    private static float[] LeadersBitsDocuments(int count, int seed)
    {
        var cells = CreateDocuments(count, Positions, seed);
        var random = Js.CreateRandom(seed + 1);
        for (var document = 0; document < count; document++)
        {
            if (random() < 0.5) continue;
            for (var position = 0; position < Positions; position++)
            {
                var at = (document * (Positions + 1)) + position;
                if (cells[at] >= 0) cells[at] = random() < 0.5 ? 0 : 1;
            }
        }
        return cells;
    }

    /// <summary>The CPU runner over each position (no stop at the length), with each document in the window and a target for each position group.</summary>
    public static (LoopRun Run, float[][] Targets) RunCpu(LoopProgram program, float[] shared, float[] documents, int count)
    {
        using var backend = new CpuLoopBackend(new CpuLoopOptions(CpuStageBackend.Tensor, StopAtLength: false));
        using var runner = backend.CreateRunner(program);
        runner.WriteShared(shared);
        var inputs = program.Buffers.Select(_ => backend.Upload(documents, count, program.Buffers[0].CellsPerDocument)).ToArray();
        var groups = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        var targets = Enumerable.Range(0, groups).Select(_ => backend.Allocate(count, program.Positions, float.NaN)).ToArray();
        var run = runner.Run(new LoopRequest(inputs, count, targets, 0, count));
        var values = targets.Select(target => backend.Read(target, 0, count)).ToArray();
        foreach (var buffer in inputs.Concat(targets)) buffer.Dispose();
        return (run, values);
    }

    /// <summary>The kernel in the interpreter, over each document, with each document in the window and a target for each position group.</summary>
    public static (KernelRun Run, float[][] Targets) RunInterpreted(LoopProgram program, LoopWgslModule module, float[] shared, float[] documents, int count)
    {
        var words = new uint[Math.Max(1, module.PackedWordCount)];
        PackedWords.Pack(module.PackedCells, shared, words);
        var groups = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        var targets = Enumerable.Range(0, groups).Select(_ => Enumerable.Repeat(float.NaN, count * program.Positions).ToArray()).ToArray();
        var documentValues = program.OutputGroups.Where(group => group.Kind == OutputGroupKind.Document).Sum(group => group.ValuesPerDocument);
        var inputs = new KernelInputs([.. shared, .. module.ConstantTable], words, [.. program.Buffers.Select(_ => documents)], count, 0, count, targets, documentValues, program.Positions, groups);
        return (LoopRowInterpreter.Compile(module.Code).Run(inputs), targets);
    }
}

public sealed class LoopWriterTests(ITestOutputHelper output)
{
    private const int DocumentCount = 300;

    public static TheoryData<string, GpuWriter> ProgramsAndWriters()
    {
        var data = new TheoryData<string, GpuWriter>();
        foreach (var name in LoopCases.Names)
        {
            data.Add(name, GpuWriter.Float32);
            data.Add(name, GpuWriter.Integer);
        }
        return data;
    }

    internal static LoopWgslModule Write(GpuWriter writer, LoopProgram program) =>
        writer == GpuWriter.Float32 ? LoopFloatWriter.Write(program) : LoopIntegerWriter.Write(program);

    [Theory]
    [MemberData(nameof(ProgramsAndWriters))]
    public void The_interpreted_kernel_equals_the_cpu_runner(string name, GpuWriter writer)
    {
        var (program, shared, documents) = LoopCases.Case(name, DocumentCount, 0x10a0 + name.Length);
        var module = Write(writer, program);
        var (expected, expectedTargets) = LoopCases.RunCpu(program, shared, documents, DocumentCount);
        var (actual, actualTargets) = LoopCases.RunInterpreted(program, module, shared, documents, DocumentCount);

        var faults = expected.Faults.Select(fault => fault.Document).ToHashSet();
        var compared = 0;
        for (var document = 0; document < DocumentCount; document++)
        {
            Assert.True(faults.Contains(document) == (actual.Lanes[document] != 0), $"document {document}: CPU fault {faults.Contains(document)}, kernel status {actual.Lanes[document]}");
            if (faults.Contains(document)) continue;
            compared++;
            foreach (var (index, value) in LoopCases.DocumentValueIndices(program, DocumentCount, document))
            {
                Same(writer, expected.DocumentValues[index], actual.DocumentValues[index], $"document {document} value {value}");
            }
            for (var group = 0; group < expectedTargets.Length; group++)
            {
                for (var position = 0; position < program.Positions; position++)
                {
                    var window = (((group * DocumentCount) + document) * program.Positions) + position;
                    Same(writer, expected.WindowPositionValues[window], actual.WindowValues[window], $"document {document} group {group} position {position}");
                    var at = (document * program.Positions) + position;
                    Same(writer, expectedTargets[group][at], actualTargets[group][at], $"document {document} target {group} position {position}");
                }
            }
        }
        output.WriteLine($"[loop writer] {name} {writer}: {compared} documents compared, {faults.Count} faults; statistics {module.Statistics}");
        Assert.True(compared > DocumentCount / 3 || name == "LeadersBits", $"compared {compared}");
        if (name is "Overflow") Assert.Contains(7, faults);
        if (name is "LeadersBits") Assert.NotEmpty(faults);
        if (name is "LookupFaults") Assert.Equal(Enumerable.Range(0, 6).Select(index => 7 + (50 * index)), faults.Order());
        if (name is "OneHot") Assert.Equal([7], faults);
    }

    private static void Same(GpuWriter writer, float expected, float actual, string what)
    {
        // An i32 has no -0, so the integer writer can give +0 where the CPU runner gives -0 (question 3 of the
        // phase 5 plan). The f32 writer gives the bits of the CPU runner.
        var agrees = writer == GpuWriter.Integer ? Js.SameNumericValue(expected, actual) : Js.ObjectIs(expected, actual);
        if (!agrees) Assert.Fail($"{what}: expected {expected:R}, got {actual:R}");
    }

    // The statement counts of the loop inputs, pinned. A change of a writer that changes a count must change this
    // table, so the change is seen.
    [Fact]
    public void The_statement_counts_of_the_loop_inputs_are_pinned()
    {
        var counts = new List<string>();
        foreach (var name in LoopCases.Names)
        {
            var (program, _, _) = LoopCases.Case(name, 1, 1);
            foreach (var writer in new[] { GpuWriter.Float32, GpuWriter.Integer })
            {
                var statistics = Write(writer, program).Statistics;
                counts.Add($"{name} {writer}: {statistics.Statements} statements, rows {statistics.FloatRows}/{statistics.WholeRows}/{statistics.BitRows}, packed {statistics.PackedReads}, lanes {statistics.LaneOperations}, tables {statistics.TableCells}");
            }
        }
        foreach (var line in counts) output.WriteLine(line);
        Assert.Equal(PinnedCounts, counts);
    }

    private static readonly string[] PinnedCounts =
    [
        "Sum Float32: 27 statements, rows 7/0/0, packed 0, lanes 0, tables 0",
        "Sum Integer: 27 statements, rows 0/4/2, packed 0, lanes 0, tables 0",
        "Leaders Float32: 77 statements, rows 31/0/0, packed 0, lanes 0, tables 0",
        "Leaders Integer: 72 statements, rows 0/11/13, packed 0, lanes 0, tables 0",
        "LeadersBits Float32: 81 statements, rows 31/0/0, packed 0, lanes 0, tables 0",
        "LeadersBits Integer: 74 statements, rows 0/11/13, packed 0, lanes 0, tables 0",
        "Lookup Float32: 46 statements, rows 16/0/0, packed 0, lanes 0, tables 0",
        "Lookup Integer: 44 statements, rows 3/7/4, packed 0, lanes 0, tables 0",
        "LookupFaults Float32: 46 statements, rows 16/0/0, packed 0, lanes 0, tables 0",
        "LookupFaults Integer: 44 statements, rows 3/7/4, packed 0, lanes 0, tables 0",
        "Overflow Float32: 39 statements, rows 19/0/0, packed 0, lanes 0, tables 0",
        "Overflow Integer: 35 statements, rows 0/8/6, packed 0, lanes 0, tables 0",
        "Switches Float32: 80 statements, rows 20/0/0, packed 0, lanes 0, tables 0",
        "Switches Integer: 52 statements, rows 0/5/3, packed 6, lanes 6, tables 0",
        "OneByOne Float32: 41 statements, rows 13/0/0, packed 0, lanes 0, tables 0",
        "OneByOne Integer: 42 statements, rows 0/4/9, packed 0, lanes 0, tables 0",
        "Mixed Float32: 54 statements, rows 22/0/0, packed 0, lanes 0, tables 9",
        "Mixed Integer: 50 statements, rows 19/3/0, packed 0, lanes 0, tables 9",
        "DeclaredField Float32: 34 statements, rows 2/0/0, packed 0, lanes 0, tables 0",
        "DeclaredField Integer: 32 statements, rows 1/1/0, packed 0, lanes 0, tables 0",
        "Fma Float32: 17 statements, rows 2/0/0, packed 0, lanes 0, tables 0",
        "Fma Integer: 17 statements, rows 2/0/0, packed 0, lanes 0, tables 0",
        "Search8 Float32: 1001 statements, rows 503/0/0, packed 0, lanes 0, tables 0",
        "Search8 Integer: 635 statements, rows 30/99/309, packed 80, lanes 50, tables 0",
        "Delay Float32: 24 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "Delay Integer: 24 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "DelayInt Float32: 20 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "DelayInt Integer: 22 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "DelayBits Float32: 31 statements, rows 6/0/0, packed 0, lanes 0, tables 0",
        "DelayBits Integer: 27 statements, rows 0/0/4, packed 0, lanes 0, tables 0",
        "Mul Float32: 20 statements, rows 2/0/0, packed 0, lanes 0, tables 0",
        "Mul Integer: 20 statements, rows 2/0/0, packed 0, lanes 0, tables 0",
        "SignField Float32: 26 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "SignField Integer: 24 statements, rows 0/0/0, packed 0, lanes 0, tables 0",
        "OneHot Float32: 30 statements, rows 5/0/0, packed 0, lanes 0, tables 0",
        "OneHot Integer: 30 statements, rows 0/0/3, packed 0, lanes 0, tables 0",
    ];

    // Switches: the six gates of one form are one word operation. The operation reads the word of the six
    // registers, the packed word of the six reads and the word of the six shared cells. "No switch is on" is one comparison of a word
    // with 0.
    [Fact]
    public void The_lanes_and_the_packed_reads_fire_on_the_switches()
    {
        var module = LoopIntegerWriter.Write(LoopWriterPrograms.Switches());
        var lines = Lines(module);
        Assert.Equal(6, module.Statistics.PackedReads);
        Assert.Equal(6, module.Statistics.LaneOperations);
        Assert.Contains("var rw0: u32 = 63u;", lines);
        Assert.Contains(lines, line => line.StartsWith("let shw0: u32 = ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("packed_words[u32(", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("rw0 = ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("== 0u)", StringComparison.Ordinal));
        // Five rows of five tables of six switches: one word for each row, of six bits.
        Assert.Equal(5, module.PackedWordCount);
        Assert.Equal(30, module.PackedCells.Length);
        Assert.All(module.PackedCells, cell => Assert.Equal((cell.Lane * LoopWriterInputs.Rows) + cell.Word, cell.SharedOffset));
    }

    // OneByOne: switch k reads on[k + 1] && !on[k + 2]. These are no neighbor lanes, so no word of registers pays,
    // and each register stays a variable of its own.
    [Fact]
    public void Registers_that_are_read_one_by_one_stay_variables_of_their_own()
    {
        var module = LoopIntegerWriter.Write(LoopWriterPrograms.OneByOne());
        var lines = Lines(module);
        Assert.Equal(0, module.Statistics.PackedReads);
        Assert.Equal(0, module.Statistics.LaneOperations);
        Assert.DoesNotContain(lines, line => line.StartsWith("var rw", StringComparison.Ordinal));
        Assert.Equal(6, lines.Count(line => line.StartsWith("var r", StringComparison.Ordinal) && line.EndsWith(": u32 = 1u;", StringComparison.Ordinal)));
    }

    // Mixed: the sign cascade and the f32 total stay f32 statements, and the streak and the bucket are i32
    // registers. Each constant table is one read of shared_values after the shared cell.
    [Fact]
    public void The_constant_tables_of_a_stage_are_reads_of_the_shared_buffer()
    {
        var program = LoopWriterPrograms.Mixed();
        var module = LoopIntegerWriter.Write(program);
        var lines = Lines(module);
        Assert.Equal(9, module.Statistics.TableCells);
        Assert.True(module.ConstantTable.SequenceEqual([0.5f, 1.25f, 3f, 7.75f, 0, 1, 2, 3, 3]), string.Join(", ", module.ConstantTable));
        Assert.Contains("var r0: i32 = 0i;", lines);
        Assert.Contains("var r1: f32 = 0.0f;", lines);
        Assert.Contains("var r2: i32 = 0i;", lines);
        Assert.Equal(2, lines.Count(line => line.Contains("shared_values[1u + u32(", StringComparison.Ordinal) || line.Contains("shared_values[5u + u32(", StringComparison.Ordinal)));
        Assert.True(module.Statistics.FloatRows > 0 && module.Statistics.WholeRows > 0);
        var f32 = LoopFloatWriter.Write(program);
        Assert.True(module.ConstantTable.SequenceEqual(f32.ConstantTable));
        Assert.Equal(0, f32.Statistics.WholeRows + f32.Statistics.BitRows);
    }

    // DeclaredField: nothing proves that the field of bits holds bits, so each write is checked with is_bit.
    // The f32 writer keeps a word of signs next to the word of bits.
    [Fact]
    public void A_field_of_bits_with_no_proof_checks_each_write()
    {
        var program = LoopWriterPrograms.DeclaredField();
        foreach (var writer in new[] { GpuWriter.Float32, GpuWriter.Integer })
        {
            var lines = Lines(Write(writer, program));
            Assert.Equal(2, lines.Count(line => line.StartsWith("if (!(is_bit(", StringComparison.Ordinal)));
            Assert.Contains("var field0: array<u32, 32>;", lines);
            Assert.Equal(writer == GpuWriter.Float32, lines.Contains("var signs0: array<u32, 32>;"));
        }
    }

    // No WGSL name comes from a name of the program: a Handwired name such as continuing is a reserved word of WGSL.
    [Fact]
    public void No_name_of_the_kernel_comes_from_a_name_of_the_program()
    {
        foreach (var name in LoopCases.Names)
        {
            var (program, _, _) = LoopCases.Case(name, 1, 1);
            foreach (var writer in new[] { GpuWriter.Float32, GpuWriter.Integer })
            {
                var code = Write(writer, program).Code;
                var main = code[code.IndexOf("fn main", StringComparison.Ordinal)..];
                var declared = System.Text.RegularExpressions.Regex.Matches(main, @"\b(?:let|var)\s+(\w+)\s*:").Select(match => match.Groups[1].Value).ToList();
                Assert.NotEmpty(declared);
                foreach (var declaredName in declared)
                {
                    Assert.Matches(@"^(?:(?:c|d|sh|shw|fd|fw|fs|r|rw|v|w|b|x|p|a|g|e|o|gi|gm|field|signs|base)\d+(?:_\d+)?[ib]?|t|doc|j|back|status|in_window|window_doc)$", declaredName);
                }
                Assert.DoesNotContain("continuing", code, StringComparison.Ordinal);
            }
        }
    }

    // A check that a proof makes true is not written: the six registers of the switches are proven bits. The
    // count is proven within 0..65 only, past its declared 0..64 in the stage Rows, so it keeps its check there.
    // The item row, which comes from the documents, also keeps its check there. The stage End reads the count as a cell of
    // 0..4096, which the proof holds, so it checks only its length cell.
    [Fact]
    public void A_register_whose_writes_are_proven_inside_its_declaration_gets_no_check()
    {
        var module = LoopIntegerWriter.Write(LoopWriterPrograms.Switches());
        var checks = Lines(module).Where(line => line.StartsWith("if (!(", StringComparison.Ordinal)).ToList();
        Assert.Equal(
        [
            "if (!(is_finite(c0) && ordered(c0) >= (-1i) && ordered(c0) <= 1082130432i && on_grid(c0, 0i))) {",
            "if (!(r6 >= 0i && r6 <= 64i)) {",
            "if (!(is_finite(d0_32) && ordered(d0_32) >= (-1i) && ordered(d0_32) <= 1233125376i && on_grid(d0_32, 0i))) {",
        ], checks);
    }

    // DelayBits: the word of b0 and b1 gets the old word of a0 and a1. So the writer copies the old word into a let
    // before the first register write. The write of the b word reads the let (the rw branch of WriteOutputs).
    [Fact]
    public void A_word_of_registers_that_gets_the_old_value_of_another_word_reads_a_let()
    {
        var module = LoopIntegerWriter.Write(LoopWriterPrograms.DelayBits());
        var lines = Lines(module);
        var copy = Assert.Single(lines, line => Regex.IsMatch(line, @"^let o\d+: u32 = .*\brw\d+\b"));
        var name = copy["let ".Length..copy.IndexOf(':', StringComparison.Ordinal)];
        var source = Regex.Match(copy, @"\brw\d+\b").Value;
        var target = Assert.Single(lines, line => Regex.IsMatch(line, $@"^rw\d+ = {name};$"));
        Assert.NotEqual(source, target[..target.IndexOf(' ', StringComparison.Ordinal)]);
        var sourceWrite = Array.FindIndex(lines, line => line.StartsWith($"{source} = ", StringComparison.Ordinal));
        Assert.True(Array.IndexOf(lines, copy) < sourceWrite, "the let comes before the write of the source word");
    }

    private static string[] Lines(LoopWgslModule module) =>
        [.. module.Code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
}
