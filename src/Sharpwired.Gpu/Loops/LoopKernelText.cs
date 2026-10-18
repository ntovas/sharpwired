// Handwired source: src/json-search/loop-emit.ts (the buffers, the parameters, the for loops, the registers
// as vars and the fields as function arrays of a loop shader). Also the shared part of bit-emit.ts (the
// shader around the stages). The shared parts of the two loop writers. One invocation runs one document.
// Each register is a var, each field a function array of the positions, and a looped stage a WGSL for loop
// over the positions. A stage checks each cell that is not a shared cell before each evaluation, as the CPU
// runner does. A failed check stops the lane with the status 1 in the buffer lanes (the phase 5 carried
// item "a status for each lane"). No WGSL name comes from a name of the program.
// Differences from Handwired:
// - The kernel runs each position. Handwired stops at the length of the text in the shader.
// - The bindings are params, run, shared_values (the shared cells, then the constant tables of each stage),
//   packed_words, lanes, document_values and window_values. Then one buffer for each LoopBuffer, and one
//   target for each position group. document_values and window_values hold the groups in the order of a LoopRun.
// - Each value of a register or a field is checked against the declaration of each cell that reads it, or
//   proven to hold it. Handwired has no checks.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Sharpwired.Core;
using Sharpwired.IL;

namespace Sharpwired.Gpu;

/// <summary>
/// The counts of a loop kernel. <see cref="Statements"/> counts the statements of main, with the statements of
/// a looped stage one time. The row counts give the rows by the type that holds the value. The count
/// <see cref="PackedReads"/> gives the index reads that are one bit of a packed word. The count
/// <see cref="LaneOperations"/> gives the gates and the negations that one word operation computes for each lane
/// of a word. The count <see cref="TableCells"/> gives the cells of the constant tables after the shared cells.
/// </summary>
public sealed record LoopWgslStatistics(int Statements, int FloatRows, int WholeRows, int BitRows, int PackedReads, int LaneOperations, int TableCells);

/// <summary>Bit <see cref="Lane"/> of packed word <see cref="Word"/> is 1 when the shared cell at <see cref="SharedOffset"/> is not 0.</summary>
public readonly record struct PackedCell(int Word, int Lane, int SharedOffset);

/// <summary>
/// The shader of a loop program, its counts, and the packed cells (bit-emit.ts packedCells).
/// The property <see cref="PackedWordCount"/> is the number of words of the buffer packed_words. A word with no
/// cell is 0. The property <see cref="ConstantTable"/> holds the cells of the constant tables. The buffer
/// shared_values holds these cells after the shared cells.
/// </summary>
public sealed record LoopWgslModule(string Code, LoopWgslStatistics Statistics, ImmutableArray<PackedCell> PackedCells)
{
    public int PackedWordCount { get; init; }

    public ImmutableArray<float> ConstantTable { get; init; } = [];

    /// <summary>The invocations of one workgroup.</summary>
    public int WorkgroupSize => WgslFloatWriter.WorkgroupSize;
}

/// <summary>The bindings of a loop kernel.</summary>
internal static class LoopBindings
{
    public const int Params = 0;
    public const int Run = 1;
    public const int Shared = 2;
    public const int Packed = 3;
    public const int Lanes = 4;
    public const int DocumentValues = 5;
    public const int WindowValues = 6;
    public const int FirstBuffer = 7;

    public static int Target(int bufferCount, int positionGroup) => FirstBuffer + bufferCount + positionGroup;
}

/// <summary>The declaration of one cell of a stage, which the kernel checks.</summary>
internal sealed record StageCell(string Name, double Minimum, double Maximum, bool ExactBit, double? Grid);

/// <summary>A one-hot rule (exactly one member is 1, or at most one with AllowAllZero) or an at-most-one rule.</summary>
internal sealed record StageRule(string Name, bool AtMostOne, bool AllowAllZero, ImmutableArray<int> Members);

/// <summary>
/// One stage as the loop writers read it. It holds the circuit and the program of its outputs, and the source
/// and the declaration of each cell. It also holds the write and the packed position of each output. Then the
/// rows, the proofs, the forms and the constant tables of the program.
/// </summary>
internal sealed class StageInfo
{
    public StageInfo(
        string name, StageRepeat repeat, Circuit circuit, TensorProgram program, ImmutableArray<CellSource> sources,
        ImmutableArray<StageWrite> writes, ImmutableArray<int> outputRoots, ImmutableArray<StageCell> cells, ImmutableArray<StageRule> rules)
    {
        Name = name;
        Repeat = repeat;
        Circuit = circuit;
        Program = program;
        Sources = sources;
        Writes = writes;
        Cells = cells;
        Rules = rules;
        Rows = ShaderRows.FlattenProgram(program);
        var rootPositions = ShaderRows.RootPositions(program);
        OutputPositions = [.. outputRoots.Select(root => rootPositions[root])];
        Tables = ConstantTables.Find(Rows, program.SourceSize);
        Proof = IntegerProof.Analyze(circuit, program, Rows);
        Plan = IntegerPlan.Create(Proof, Rows, program.GatherSlotCount, [.. OutputPositions], Tables);
        RowAt = new ShaderRow?[program.ValueCount];
        foreach (var row in Rows)
        {
            if (row is not GatherComputeRow) RowAt[IntegerPlan.PositionOf(row)] = row;
        }
        Grids = [.. cells.Select(cell => cell.Grid is { } grid ? RecurrentKernelWriter.GridExponent(grid, cell.Name) : (int?)null)];
    }

    public string Name { get; }

    public StageRepeat Repeat { get; }

    public Circuit Circuit { get; }

    public TensorProgram Program { get; }

    public ImmutableArray<CellSource> Sources { get; }

    public ImmutableArray<StageWrite> Writes { get; }

    public ImmutableArray<StageCell> Cells { get; }

    public ImmutableArray<StageRule> Rules { get; }

    public ImmutableArray<ShaderRow> Rows { get; }

    // The packed position of each output leaf, or -1 for a root that the program does not hold.
    public ImmutableArray<int> OutputPositions { get; }

    public ConstantTables Tables { get; }

    public ProofResult Proof { get; }

    public IntegerPlan Plan { get; }

    public ShaderRow?[] RowAt { get; }

    // The exponent e of the grid 2^e of each cell, or null.
    public ImmutableArray<int?> Grids { get; }

    public int SourceSize => Program.SourceSize;

    public static StageInfo From(StagePlan plan)
    {
        var validator = plan.Stage.Function.Table.Validator;
        var cells = validator.Cells().Select(cell => new StageCell(cell.Name, cell.Minimum, cell.Maximum, cell.ExactBit, cell.Grid));
        var rules = validator.Rules.Select(rule => new StageRule(rule.Name, rule.AtMostOne, rule.AllowAllZero, [.. rule.Members.Select(member => member.Position)]));
        return new StageInfo(plan.Stage.Name, plan.Stage.Repeat, plan.Stage.Function.Table.Circuit, plan.Program, plan.Sources, plan.Writes, plan.OutputRoots, [.. cells], [.. rules]);
    }
}

/// <summary>The parts of a loop program without its stages.</summary>
internal sealed record LoopShape(
    int Positions, ImmutableArray<int> BufferCells, ImmutableArray<float> RegisterInitials, ImmutableArray<LoopField> Fields,
    ImmutableArray<LoopOutputGroup> Groups, int SharedSize)
{
    public static LoopShape From(LoopProgram program) => new(
        program.Positions, [.. program.Buffers.Select(buffer => buffer.CellsPerDocument)], [.. program.Registers.Select(register => register.Initial)],
        program.Fields, program.OutputGroups, program.SharedSize);

    // The index of each position group among the position groups, by group index. Also the start of each
    // document group in the values of one document, by group index.
    public (int[] PositionIndex, int[] DocumentStart, int DocumentValues) GroupLayout()
    {
        var positionIndex = new int[Groups.Length];
        var documentStart = new int[Groups.Length];
        var (positions, documents) = (0, 0);
        for (var group = 0; group < Groups.Length; group++)
        {
            if (Groups[group].Kind == OutputGroupKind.Position)
            {
                positionIndex[group] = positions++;
            }
            else
            {
                documentStart[group] = documents;
                documents += Groups[group].ValuesPerDocument;
            }
        }
        return (positionIndex, documentStart, documents);
    }
}

/// <summary>How the kernel holds a register or a field.</summary>
internal enum CellStorage
{
    // An f32 var, or an array of f32 values.
    Float,

    // An i32 var, or an array of i32 values.
    Whole,

    // A u32 var of 0 or 1 (a register only).
    Bit,

    // A field of bits as one u32 for each position (the integer writer).
    Word,

    // A field of bits as one u32 word of the bits and one u32 word of the signs for each position (the f32
    // writer). A bit of a field can be -0, and the f32 writer keeps the sign.
    FloatWord,
}

/// <summary>
/// The writer of a loop kernel. It chooses the storage of each register and field, and it tries the words of
/// registers (the Lanes rule). It also writes the text around the stages.
/// </summary>
internal sealed partial class LoopKernelWriter
{
    public LoopKernelWriter(LoopShape shape, IReadOnlyList<StageInfo> stages, bool integer)
    {
        Shape = shape;
        Stages = [.. stages];
        Integer = integer;
        TableOffsets = new int[stages.Count];
        var offset = shape.SharedSize;
        for (var stage = 0; stage < stages.Count; stage++)
        {
            TableOffsets[stage] = offset;
            offset += stages[stage].Tables.Values.Length;
        }
        (RegisterStorage, RegisterRange) = ChooseRegisters();
        (FieldStorage, FieldRange) = ChooseFields();
    }

    public LoopShape Shape { get; }

    public ImmutableArray<StageInfo> Stages { get; }

    public bool Integer { get; }

    // The offset in shared_values of the constant tables of each stage.
    public int[] TableOffsets { get; }

    public CellStorage[] RegisterStorage { get; }

    public CellStorage[] FieldStorage { get; }

    // The whole range that each value of a register or a field is proven to hold, or null.
    public (long Minimum, long Maximum)?[] RegisterRange { get; }

    public (long Minimum, long Maximum)?[] FieldRange { get; }

    /// <summary>Write the kernel of the loop program.</summary>
    public static LoopWgslModule Write(LoopProgram program, GpuWriter writer, uint salt = 0)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (writer is not (GpuWriter.Float32 or GpuWriter.Integer)) throw new ArgumentOutOfRangeException(nameof(writer), writer, "unknown writer");
        return new LoopKernelWriter(LoopShape.From(program), [.. program.Plans.Select(StageInfo.From)], writer == GpuWriter.Integer).Write(salt);
    }

    public LoopWgslModule Write(uint salt)
    {
        foreach (var field in Shape.Fields)
        {
            if (field.Bits && field.Width > 32) throw new ArgumentException($"the field of bits {field.Name} has {field.Width} bits, and a word holds 32");
        }
        if (Shape.Groups.Count(group => group.Kind == OutputGroupKind.Position) > 32) throw new ArgumentException("a loop kernel holds at most 32 position groups");
        var words = Integer ? FirstWords() : [];
        while (true)
        {
            var state = new KernelState(this, words);
            var stageLines = Stages.Select((stage, index) => new LoopStageWriter(this, state, stage, index).Write()).ToList();
            var next = Integer ? NextWords(words, state) : words;
            if (next.Length == words.Length && next.Zip(words).All(pair => pair.First.SequenceEqual(pair.Second))) return Assemble(state, stageLines, salt);
            words = next;
        }
    }

    // The register storage: the f32 writer holds each register as f32. The integer writer holds a register as
    // a bit when each write is a proven bit and the initial value is 0 or 1. It holds a register as an i32
    // when each write is a proven whole number and the initial value is a whole number. Otherwise it holds a
    // register as f32. The range of a register is the union of the proofs of the writes and the initial value.
    private (CellStorage[], (long, long)?[]) ChooseRegisters()
    {
        var count = Shape.RegisterInitials.Length;
        var storage = new CellStorage[count];
        var ranges = new (long, long)?[count];
        for (var register = 0; register < count; register++)
        {
            var initial = Shape.RegisterInitials[register];
            var range = IntegerProof.IsWholeNumber(initial) ? ((long)initial, (long)initial) : ((long, long)?)null;
            var written = false;
            foreach (var proof in WriteProofs(write => write is StageWrite.Register target && target.Index == register))
            {
                written = true;
                range = range is { } so && proof.IsProven ? (Math.Min(so.Item1, proof.Minimum), Math.Max(so.Item2, proof.Maximum)) : null;
            }
            ranges[register] = range;
            storage[register] = !Integer || !written || range is not { } whole ? CellStorage.Float
                : whole.Item1 >= 0 && whole.Item2 <= 1 ? CellStorage.Bit
                : CellStorage.Whole;
        }
        return (storage, ranges);
    }

    // The field storage: the f32 writer holds a field of bits as a FloatWord and another field as f32. The
    // integer writer holds a field of bits as a Word. It holds another field as a Word when three conditions
    // are true. Each write is a proven bit, the width is at most 32, and each stage that writes it writes all of
    // its components. It holds another field as an i32 array when each write is proven whole, and as f32
    // otherwise. A field of bits holds 0 or 1 after the check of each write, so its range is 0..1 also with
    // no proof.
    private (CellStorage[], (long, long)?[]) ChooseFields()
    {
        var count = Shape.Fields.Length;
        var storage = new CellStorage[count];
        var ranges = new (long, long)?[count];
        for (var index = 0; index < count; index++)
        {
            var field = Shape.Fields[index];
            (long, long)? range = null;
            var (written, allProven) = (false, true);
            foreach (var proof in WriteProofs(write => write is StageWrite.Field target && target.Index == index))
            {
                written = true;
                if (!proof.IsProven)
                {
                    allProven = false;
                    continue;
                }
                range = range is { } so ? (Math.Min(so.Item1, proof.Minimum), Math.Max(so.Item2, proof.Maximum)) : (proof.Minimum, proof.Maximum);
            }
            var wholeWrites = written && allProven;
            if (!wholeWrites) range = null;
            var allComponents = Stages.All(stage =>
            {
                var components = stage.Writes.OfType<StageWrite.Field>().Where(write => write.Index == index).Select(write => write.Component).Distinct().Count();
                return components == 0 || components == field.Width;
            });
            if (field.Bits)
            {
                storage[index] = Integer ? CellStorage.Word : CellStorage.FloatWord;
                ranges[index] = (Math.Max(0, range?.Item1 ?? 0), Math.Min(1, range?.Item2 ?? 1));
                continue;
            }
            ranges[index] = wholeWrites ? range : null;
            storage[index] = !Integer || !wholeWrites ? CellStorage.Float
                : range is { Item1: >= 0, Item2: <= 1 } && field.Width <= 32 && allComponents ? CellStorage.Word
                : CellStorage.Whole;
        }
        return (storage, ranges);
    }

    // The proof of each output of each stage that the predicate selects.
    private IEnumerable<WholeProof> WriteProofs(Func<StageWrite, bool> selects)
    {
        foreach (var stage in Stages)
        {
            for (var output = 0; output < stage.Writes.Length; output++)
            {
                if (!selects(stage.Writes[output])) continue;
                var position = stage.OutputPositions[output];
                yield return position < 0 ? WholeProof.Float : stage.Proof.Proofs[position];
            }
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    // The kernel around the stages.
    private LoopWgslModule Assemble(KernelState state, List<List<string>> stageLines, uint salt)
    {
        var shape = Shape;
        var (positionIndex, documentStart, _) = shape.GroupLayout();
        var main = new List<string>
        {
            "let t: u32 = id.x;",
            "if (t >= params.count) {",
            "return;",
            "}",
            "let doc: u32 = params.first + t;",
        };
        for (var buffer = 0; buffer < shape.BufferCells.Length; buffer++) main.Add($"let base{buffer}: u32 = doc * {WgslText.Uint(shape.BufferCells[buffer])};");
        main.Add("let in_window: bool = doc >= run.window_first && doc - run.window_first < run.window_count;");
        main.Add("let window_doc: u32 = doc - run.window_first;");
        main.AddRange(FillLines(documentStart, positionIndex));
        main.AddRange(state.HoistedLines);
        main.Add("var status: u32 = 0u;");
        for (var register = 0; register < shape.RegisterInitials.Length; register++)
        {
            if (state.LaneOf.ContainsKey(register)) continue;
            var initial = shape.RegisterInitials[register];
            main.Add(RegisterStorage[register] switch
            {
                CellStorage.Bit => $"var r{register}: u32 = {WgslText.Uint((int)initial)};",
                CellStorage.Whole => $"var r{register}: i32 = {WgslText.Int((int)initial)};",
                _ => $"var r{register}: f32 = {WgslText.Float(initial)};",
            });
        }
        for (var word = 0; word < state.RegisterWords.Length; word++)
        {
            var bits = 0u;
            for (var lane = 0; lane < state.RegisterWords[word].Length; lane++)
            {
                if (shape.RegisterInitials[state.RegisterWords[word][lane]] == 1) bits |= 1u << lane;
            }
            main.Add($"var rw{word}: u32 = {bits.ToString(CultureInfo.InvariantCulture)}u;");
        }
        for (var field = 0; field < shape.Fields.Length; field++)
        {
            var size = shape.Positions * shape.Fields[field].Width;
            main.Add(FieldStorage[field] switch
            {
                CellStorage.Word => $"var field{field}: array<u32, {Text(shape.Positions)}>;",
                CellStorage.FloatWord => $"var field{field}: array<u32, {Text(shape.Positions)}>;",
                CellStorage.Whole => $"var field{field}: array<i32, {Text(size)}>;",
                _ => $"var field{field}: array<f32, {Text(size)}>;",
            });
            if (FieldStorage[field] == CellStorage.FloatWord) main.Add($"var signs{field}: array<u32, {Text(shape.Positions)}>;");
        }
        main.Add("loop {");
        for (var stage = 0; stage < Stages.Length; stage++)
        {
            var last = WgslText.Uint(shape.Positions - 1);
            switch (Stages[stage].Repeat)
            {
                case StageRepeat.Once:
                    main.Add("{");
                    main.AddRange(stageLines[stage]);
                    main.Add("}");
                    break;
                case StageRepeat.Forward:
                    main.Add($"for (var j: u32 = 0u; j < {WgslText.Uint(shape.Positions)}; j = j + 1u) {{");
                    main.AddRange(stageLines[stage]);
                    main.Add("}");
                    main.Add("if (status != 0u) {");
                    main.Add("break;");
                    main.Add("}");
                    break;
                default:
                    main.Add($"for (var back: u32 = 0u; back < {WgslText.Uint(shape.Positions)}; back = back + 1u) {{");
                    main.Add($"let j: u32 = {last} - back;");
                    main.AddRange(stageLines[stage]);
                    main.Add("}");
                    main.Add("if (status != 0u) {");
                    main.Add("break;");
                    main.Add("}");
                    break;
            }
        }
        main.Add("break;");
        main.Add("}");
        main.Add("lanes[doc] = status;");

        var code = new StringBuilder();
        if (salt != 0) code.Append("// salt ").Append(salt.ToString(CultureInfo.InvariantCulture)).Append('\n');
        code.Append($"@group(0) @binding({LoopBindings.Params}) var<storage, read> params: Params;\n");
        code.Append($"@group(0) @binding({LoopBindings.Run}) var<storage, read> run: Run;\n");
        code.Append($"@group(0) @binding({LoopBindings.Shared}) var<storage, read> shared_values: array<f32>;\n");
        code.Append($"@group(0) @binding({LoopBindings.Packed}) var<storage, read> packed_words: array<u32>;\n");
        code.Append($"@group(0) @binding({LoopBindings.Lanes}) var<storage, read_write> lanes: array<u32>;\n");
        code.Append($"@group(0) @binding({LoopBindings.DocumentValues}) var<storage, read_write> document_values: array<f32>;\n");
        code.Append($"@group(0) @binding({LoopBindings.WindowValues}) var<storage, read_write> window_values: array<f32>;\n");
        for (var buffer = 0; buffer < shape.BufferCells.Length; buffer++) code.Append($"@group(0) @binding({LoopBindings.FirstBuffer + buffer}) var<storage, read> buffer{buffer}: array<f32>;\n");
        var positionGroups = shape.Groups.Count(group => group.Kind == OutputGroupKind.Position);
        for (var group = 0; group < positionGroups; group++) code.Append($"@group(0) @binding({LoopBindings.Target(shape.BufferCells.Length, group)}) var<storage, read_write> target{group}: array<f32>;\n");
        code.Append('\n').Append(WgslText.Prelude);
        code.Append("struct Run {\n    document_count: u32,\n    window_first: u32,\n    window_count: u32,\n    targets: u32,\n}\n\n");
        code.Append(RecurrentKernelWriter.CheckHelpers);
        code.Append($"@compute @workgroup_size({WgslFloatWriter.WorkgroupSize})\n");
        code.Append("fn main(@builtin(global_invocation_id) id: vec3<u32>) {\n");
        var depth = 1;
        foreach (var line in main)
        {
            if (line.StartsWith('}')) depth--;
            code.Append(' ', 4 * depth).Append(line).Append('\n');
            if (line.EndsWith('{')) depth++;
        }
        code.Append("}\n");

        var packed = ImmutableArray.CreateBuilder<PackedCell>();
        for (var word = 0; word < state.PackedCells.Count; word++)
        {
            for (var lane = 0; lane < state.PackedCells[word].Count; lane++) packed.Add(new PackedCell(word, lane, state.PackedCells[word][lane]));
        }
        var statistics = new LoopWgslStatistics(
            main.Count(line => line.EndsWith(';')), state.FloatRows, state.WholeRows, state.BitRows, state.PackedReads, state.LaneOperations,
            Stages.Sum(stage => stage.Tables.Values.Length));
        return new LoopWgslModule(code.ToString(), statistics, packed.ToImmutable())
        {
            PackedWordCount = state.PackedCells.Count,
            ConstantTable = [.. Stages.SelectMany(stage => stage.Tables.Values)],
        };
    }

    // The fill value of each document column that no stage writes, and of each position group that no stage
    // writes. A position group that a looped stage writes gets a value at each position.
    private List<string> FillLines(int[] documentStart, int[] positionIndex)
    {
        var lines = new List<string>();
        var shape = Shape;
        for (var group = 0; group < shape.Groups.Length; group++)
        {
            var outputGroup = shape.Groups[group];
            var fill = WgslText.Float(outputGroup.Fill);
            if (outputGroup.Kind == OutputGroupKind.Document)
            {
                for (var column = 0; column < outputGroup.ValuesPerDocument; column++)
                {
                    var written = Stages.Any(stage => stage.Writes.Any(write => write is StageWrite.Document target && target.Group == group && target.Column == column));
                    if (!written) lines.Add($"{DocumentTarget(documentStart[group], outputGroup.ValuesPerDocument, column)} = {fill};");
                }
                continue;
            }
            if (Stages.Any(stage => stage.Writes.Any(write => write is StageWrite.Position target && target.Group == group))) continue;
            lines.Add($"for (var j: u32 = 0u; j < {WgslText.Uint(shape.Positions)}; j = j + 1u) {{");
            lines.AddRange(PositionWrite(positionIndex[group], fill));
            lines.Add("}");
        }
        return lines;
    }

    /// <summary>The place of a document value: document_values in the order of a LoopRun.</summary>
    public static string DocumentTarget(int start, int valuesPerDocument, int column) =>
        $"document_values[{WgslText.Uint(start)} * run.document_count + doc * {WgslText.Uint(valuesPerDocument)} + {WgslText.Uint(column)}]";

    /// <summary>The writes of the value of a position group at the position j: the window and the target.</summary>
    public List<string> PositionWrite(int positionGroup, string value) =>
    [
        "if (in_window) {",
        $"window_values[({WgslText.Uint(positionGroup)} * run.window_count + window_doc) * {WgslText.Uint(Shape.Positions)} + j] = {value};",
        "}",
        $"if ((run.targets & {WgslText.Uint(1 << positionGroup)}) != 0u) {{",
        $"target{positionGroup.ToString(CultureInfo.InvariantCulture)}[doc * {WgslText.Uint(Shape.Positions)} + j] = {value};",
        "}",
    ];
}

/// <summary>The state of one attempt of the words of registers: the lines before the first stage, the packed cells and the counts.</summary>
internal sealed class KernelState
{
    private readonly HashSet<string> hoisted = new(StringComparer.Ordinal);

    public KernelState(LoopKernelWriter kernel, ImmutableArray<ImmutableArray<int>> registerWords)
    {
        RegisterWords = registerWords;
        for (var word = 0; word < registerWords.Length; word++)
        {
            for (var lane = 0; lane < registerWords[word].Length; lane++) LaneOf[registerWords[word][lane]] = (word, lane);
        }
        LaneReads = new int[registerWords.Length];
        Kernel = kernel;
    }

    public LoopKernelWriter Kernel { get; }

    public ImmutableArray<ImmutableArray<int>> RegisterWords { get; }

    public Dictionary<int, (int Word, int Lane)> LaneOf { get; } = [];

    // How many times a stage read one register of each word on its own.
    public int[] LaneReads { get; }

    public List<string> HoistedLines { get; } = [];

    public Dictionary<string, string> SharedWords { get; } = new(StringComparer.Ordinal);

    public List<List<int>> PackedCells { get; } = [];

    public Dictionary<string, int> PackedTables { get; } = new(StringComparer.Ordinal);

    public int FloatRows { get; set; }

    public int WholeRows { get; set; }

    public int BitRows { get; set; }

    public int PackedReads { get; set; }

    public int LaneOperations { get; set; }

    /// <summary>Add a line before the first stage, one time for each name.</summary>
    public void Hoist(string name, string line)
    {
        if (hoisted.Add(name)) HoistedLines.Add(line);
    }
}
