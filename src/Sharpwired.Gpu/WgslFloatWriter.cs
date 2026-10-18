// Handwired source: src/json-search/emit.ts (createRowWriter) and src/json-search/loop-emit.ts (the
// buffers and the parameters of a shader). The f32 writer: one WGSL statement for each row, with the
// keep function k around each literal, each product and each partial sum. emit.ts writes the rows
// without keep, drops a bias of 0, and writes the boolean atom as a select. This writer keeps the stored
// form of each operation, so each rounding point gives the reference bits (question 2 of the phase 5
// plan). A row that only a constant table reads gets no statement (ShaderRows.Needed).

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>The WGSL writer of a GPU evaluator.</summary>
public enum GpuWriter
{
    /// <summary>Each operation is an f32 statement with keep (<see cref="WgslFloatWriter"/>).</summary>
    Float32,

    /// <summary>
    /// Each proven whole wire is an i32, each proven bit a u32, and each other operation an f32
    /// statement with keep (<see cref="WgslIntegerWriter"/>).
    /// </summary>
    Integer,
}

/// <summary>
/// The counts of a written shader. <see cref="Statements"/> counts the statements of the source
/// cells, the rows and the roots. <see cref="Keeps"/> counts the calls of the keep function k. The
/// row counts give the written rows by the type that holds the value: f32, i32 or u32. The f32 writer
/// writes f32 rows only. A row that the integer writer converts from an f32 statement counts as i32
/// or u32.
/// </summary>
public sealed record WgslStatistics(int Statements, int FloatRows, int WholeRows, int BitRows, int Keeps);

/// <summary>
/// One compute shader of a program. Binding 0 holds the source tables, binding 1 the outputs, and
/// binding 2 the parameters. One invocation evaluates one source table.
/// </summary>
public sealed record WgslModule(string Code, int SourceSize, int RootCount, int WorkgroupSize, WgslStatistics Statistics)
{
    /// <summary>
    /// The cells of the constant tables, which binding 3 holds. An index read of constant candidates reads
    /// its table there. Empty when the shader reads no constant table.
    /// </summary>
    public ImmutableArray<float> ConstantTable { get; init; } = [];
}

/// <summary>
/// The f32 writer. Each FP32 step of a row is one keep, <c>k(a + b)</c> or <c>k(a * b)</c>, and each
/// literal is <c>k(L)</c> with the exact literal L. The shader compiler cannot move arithmetic across
/// k, so it cannot contract a product and a sum, regroup a sum, or fold the boolean atom.
/// </summary>
/// <remarks>
/// Limit: a device that flushes subnormal values to 0 (<see cref="GpuDevice.FlushesSubnormals"/>)
/// gives other values for an evaluation that holds a nonzero value of magnitude below 2^-126. No WGSL
/// form stops the flush. On other evaluations, each value equals the reference value bit for bit.
/// </remarks>
public static class WgslFloatWriter
{
    /// <summary>The invocations of one workgroup. Handwired uses 64.</summary>
    public const int WorkgroupSize = 64;

    /// <summary>Write the shader of the program.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit.</exception>
    public static WgslModule Write(Circuit circuit, TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        ShaderRows.Validate(circuit, program);
        var rows = ShaderRows.FlattenProgram(program);
        var rootPositions = ShaderRows.RootPositions(program);
        var tables = ConstantTables.Find(rows, program.SourceSize);
        var body = new FloatBody(program.SourceSize, candidateRead: FloatBody.TableRead(tables, "tables", 0));
        var (needed, slots) = ShaderRows.Needed(rows, rootPositions, program.ValueCount, program.GatherSlotCount, tables);
        var written = 0;
        foreach (var row in rows)
        {
            if (row is GatherComputeRow compute ? !slots[compute.Slot] : !needed[IntegerPlan.PositionOf(row)]) continue;
            body.Write(row);
            if (row is not GatherComputeRow) written++;
        }
        return body.Finish(rootPositions, written, constantTable: tables.Values);
    }
}

// The statements of the main function of an f32 shader. The integer writer passes floatName. floatName gives
// the f32 text of a position that the integer writer holds as an integer, for example f32(w3). It gives null
// for an f32 position. The step kernel of RecurrentKernelWriter passes cellName, which gives the variable of a
// source cell, for example cell3. Then a read of a source cell reads the variable. A read of a source cell at
// a computed index is a select over the variables, as for a list of computed values. candidateRead gives the
// text of a read of the candidates at an i32 index, for example a read of a constant table, or null for the
// forms below. sourceBuffer false (the loop kernels) reads no candidate from the buffer sources: such a read
// is a select over the names of the cells.
internal sealed partial class FloatBody(
    int sourceSize,
    Func<int, string?>? floatName = null,
    Func<int, string>? cellName = null,
    Func<ShaderCandidates, string, string?>? candidateRead = null,
    bool sourceBuffer = true)
{
    // A sum of more terms continues in a new statement (emit.ts termsPerStatement).
    private const int TermsPerStatement = 32;

    private const string NaN = "bitcast<f32>(0x7fc00000u)";

    private readonly List<string> statements = [];
    private readonly SortedSet<int> usedSources = [];
    private readonly SortedDictionary<int, string> integerSourceReads = [];
    private readonly Dictionary<UnsignedIndices, string> tableNames = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> tables = [];

    public void Write(ShaderRow row)
    {
        switch (row)
        {
            case GatherComputeRow compute: WriteGatherCompute(compute); break;
            case AffineRow affine: WriteAffine(affine); break;
            case BooleanRow boolean: WriteBoolean(boolean); break;
            case RegluRow reglu: WriteReglu(reglu); break;
            case IndexRow read: WriteIndexRead(read); break;
            default: throw new InvalidOperationException($"unknown row {row.GetType().Name}");
        }
    }

    /// <summary>True when a read of source candidates reads the buffer sources.</summary>
    public bool ReadsSourceBuffer => sourceBuffer && cellName is null;

    /// <summary>Add one statement after the statements so far.</summary>
    public void AddStatement(string statement) => statements.Add(statement);

    /// <summary>Add the read of a source cell that the integer writer holds as an integer.</summary>
    public void AddSourceRead(int cell, string statement) => integerSourceReads.TryAdd(cell, statement);

    /// <summary>The f32 text of the value of a source cell: a read of the buffer, or the variable of the cell.</summary>
    public string SourceText(int cell) => cellName?.Invoke(cell) ?? $"sources[base + {WgslText.Uint(cell)}]";

    /// <summary>
    /// The statements so far: first the reads of the source cells in cell order, then the statements of
    /// the rows. A call of <see cref="Name"/> after this call can add a read that this list does not hold.
    /// </summary>
    public List<string> BodyStatements() =>
        [.. usedSources
            .Select(cell => (Cell: cell, Statement: $"let s{cell}: f32 = {SourceText(cell)};"))
            .Concat(integerSourceReads.Select(pair => (Cell: pair.Key, Statement: pair.Value)))
            .OrderBy(read => read.Cell)
            .Select(read => read.Statement)
            .Concat(statements)];

    /// <summary>The module declarations of the candidate tables, one for each stored list of source cells.</summary>
    public IReadOnlyList<string> Tables => tables;

    /// <summary>The number of calls of the keep function k in the text.</summary>
    public static int CountKeeps(string text) => KeepCall().Count(text);

    /// <summary>
    /// The candidate read of the constant tables: buffer[offset + start + index] for a constant table, null
    /// for other candidates.
    /// </summary>
    public static Func<ShaderCandidates, string, string?> TableRead(ConstantTables tables, string buffer, int offset) => (candidates, index) =>
    {
        var start = tables.StartOf(candidates);
        return start < 0 ? null : $"{buffer}[{WgslText.Uint(offset + start)} + u32({index})]";
    };

    // A root that is a source cell is a copy from the buffer: a source cell gets a statement only where a
    // row reads it (the phase 5 carried item "shader creation time").
    public WgslModule Finish(int[] rootPositions, int floatRows, int wholeRows = 0, int bitRows = 0, ImmutableArray<float> constantTable = default)
    {
        var stores = new List<string>(rootPositions.Length);
        for (var root = 0; root < rootPositions.Length; root++)
        {
            var position = rootPositions[root];
            var value = position < 0 ? NaN : position < sourceSize ? SourceText(position) : Name(position);
            stores.Add($"outputs[o + {WgslText.Uint(root)}] = {value};");
        }
        var body = BodyStatements();
        var table = constantTable.IsDefault ? [] : constantTable;

        var code = new StringBuilder();
        code.Append("@group(0) @binding(0) var<storage, read> sources: array<f32>;\n");
        code.Append("@group(0) @binding(1) var<storage, read_write> outputs: array<f32>;\n");
        code.Append("@group(0) @binding(2) var<storage, read> params: Params;\n");
        if (table.Length > 0) code.Append("@group(0) @binding(3) var<storage, read> tables: array<f32>;\n");
        code.Append('\n');
        code.Append(WgslText.Prelude);
        foreach (var declaration in tables) code.Append(declaration).Append('\n');
        code.Append('\n');
        code.Append($"@compute @workgroup_size({WgslFloatWriter.WorkgroupSize})\n");
        code.Append("fn main(@builtin(global_invocation_id) id: vec3<u32>) {\n");
        code.Append("    let t: u32 = id.x;\n");
        code.Append("    if (t >= params.count) {\n        return;\n    }\n");
        code.Append($"    let base: u32 = t * {WgslText.Uint(sourceSize)};\n");
        var keeps = 0;
        foreach (var statement in body.Append($"let o: u32 = t * {WgslText.Uint(rootPositions.Length)};").Concat(stores))
        {
            code.Append("    ").Append(statement).Append('\n');
            keeps += KeepCall().Count(statement);
        }
        code.Append("}\n");
        var statistics = new WgslStatistics(body.Count + stores.Count, floatRows, wholeRows, bitRows, keeps);
        return new WgslModule(code.ToString(), sourceSize, rootPositions.Length, WgslFloatWriter.WorkgroupSize, statistics) { ConstantTable = table };
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])k\(")]
    private static partial Regex KeepCall();

    private static string K(string expression) => $"k({expression})";

    private static string Literal(float value) => K(WgslText.Float(value));

    // s<cell> for a source cell, v<position> for a computed value, or the text of floatName.
    public string Name(int position)
    {
        if (floatName?.Invoke(position) is { } name) return name;
        if (position >= sourceSize) return $"v{position}";
        usedSources.Add(position);
        return $"s{position}";
    }

    // The left fold of the reference: value = f(bias), then value = f(value + f(weight × input)) for
    // each term in stored order. A weight of 1 adds the input and a weight of -1 subtracts it, which is
    // exact. Another weight adds or subtracts f(|weight| × input), after its sign bit. This is the same
    // value, because FP32 rounding is symmetric and a - b is a + (-b). The text continues in a new
    // statement after each 32 terms.
    private string Sum(string partialName, float bias, ImmutableArray<ShaderTerm> terms)
    {
        var text = Literal(bias);
        for (var index = 0; index < terms.Length; index++)
        {
            var term = terms[index];
            var input = Name(term.Position);
            if (term.Weight == 1f) text = K($"{text} + {input}");
            else if (term.Weight == -1f) text = K($"{text} - {input}");
            else
            {
                var sign = float.IsNegative(term.Weight) ? "-" : "+";
                text = K($"{text} {sign} {K($"{Literal(MathF.Abs(term.Weight))} * {input}")}");
            }
            if ((index + 1) % TermsPerStatement == 0 && index + 1 < terms.Length)
            {
                var name = $"{partialName}_{(index + 1) / TermsPerStatement}";
                statements.Add($"let {name}: f32 = {text};");
                text = name;
            }
        }
        return text;
    }

    // The candidate at the index expression. A list of computed values becomes the variable arrayName
    // and one select for each candidate after the first. In a step kernel, source cells become the same
    // select over the cell variables. Else a list of source cells becomes one module
    // array for each stored table. Two forms of the plan failed on the M5 Pro with this wgpu-native. The
    // first is a const array with an index that is not a constant (shader module error). The second is a
    // function array of computed values in a shader with keep (pipeline error, with no detail in the
    // message).
    private string Candidate(ShaderCandidates candidates, string index, string arrayName)
    {
        if (candidates.Count == 1) return Name(candidates.PositionAt(0));
        if (candidateRead?.Invoke(candidates, index) is { } read) return read;
        if (ReadsSourceBuffer && candidates.Form == CandidateForm.Strided)
        {
            return $"sources[base + u32({WgslText.Int(candidates.Base)} + {index} * {WgslText.Int(candidates.Stride)})]";
        }
        if (ReadsSourceBuffer && candidates.Form == CandidateForm.SourceList) return $"sources[base + {TableName(candidates)}[{index}]]";
        statements.Add($"var {arrayName}: f32 = {Name(candidates.PositionAt(0))};");
        for (var candidate = 1; candidate < candidates.Count; candidate++)
        {
            statements.Add($"{arrayName} = select({arrayName}, {Name(candidates.PositionAt(candidate))}, {index} == {WgslText.Int(candidate)});");
        }
        return arrayName;
    }

    public string TableName(ShaderCandidates candidates)
    {
        var table = candidates.Table ?? UnsignedIndices.From([.. candidates.Positions]);
        if (tableNames.TryGetValue(table, out var name)) return name;
        name = $"t{tableNames.Count}";
        tableNames.Add(table, name);
        tables.Add($"var<private> {name}: array<u32, {candidates.Count}> = array<u32, {candidates.Count}>({string.Join(", ", candidates.Positions.Select(WgslText.Uint))});");
        return name;
    }

    // The index and the mass of one slot. For an exact one-hot, both are small whole numbers, which
    // FP32 computes without a rounding in any order, so the terms need no keep. The mass starts at
    // k(0.0f), as the reference starts at +0: a group of -0 bits then gives the mass +0. A broken
    // one-hot declaration is outside the contract. For such a group, a compiler can regroup the terms,
    // so the index and the mass can differ from the values of TensorEvaluator. For a NaN index sum,
    // the WGSL i32() conversion is not defined, and TensorEvaluator gives the index 0.
    private void WriteGatherCompute(GatherComputeRow compute)
    {
        var bits = compute.BitPositions;
        var index = bits.Length < 2
            ? "0i"
            : $"i32({string.Join(" + ", Enumerable.Range(1, bits.Length - 1).Select(bit => bit == 1 ? Name(bits[bit]) : $"{WgslText.Float(bit)} * {Name(bits[bit])}"))})";
        statements.Add($"let gi{compute.Slot}: i32 = {index};");
        statements.Add($"let gm{compute.Slot}: f32 = {Literal(0f)} + {string.Join(" + ", bits.Select(Name))};");
    }

    // The sum of the row, then f(value + candidate) for each gather read, or f(value + f(mass ×
    // candidate)) for a masked read.
    private void WriteAffine(AffineRow row)
    {
        var target = $"v{row.Position}";
        var text = Sum(target, row.Bias, row.Terms);
        for (var read = 0; read < row.Gathers.Length; read++)
        {
            var gather = row.Gathers[read];
            var index = $"clamp(gi{gather.Slot}, 0i, {WgslText.Int(gather.Candidates.Count - 1)})";
            var candidate = Candidate(gather.Candidates, index, $"a{row.Position}_{read}");
            text = gather.Masked ? K($"{text} + {K($"gm{gather.Slot} * {candidate}")}") : K($"{text} + {candidate}");
        }
        statements.Add($"let {target}: f32 = {text};");
    }

    // The stored atom f(f(S + 8388609) - 8388609).
    private void WriteBoolean(BooleanRow row)
    {
        var target = $"v{row.Position}";
        var shifted = K($"{Sum(target, row.Bias, row.Terms)} + {Literal(8_388_609f)}");
        statements.Add($"let {target}: f32 = {K($"{shifted} - {Literal(8_388_609f)}")};");
    }

    // f(left × relu(gate)). With the left form of the constant 1, f(1 × relu(g)) is relu(g).
    private void WriteReglu(RegluRow row)
    {
        var target = $"v{row.Position}";
        var left = row.Terms.Length == 0 && row.Bias == 1f ? null : Sum(target, row.Bias, row.Terms);
        var gate = Sum($"g{row.Position}", row.GateBias, row.GateTerms);
        statements.Add($"let {target}: f32 = {(left is null ? $"relu({gate})" : K($"{left} * relu({gate})"))};");
    }

    // Circuit.ResolveIndexPosition in WGSL. The floor and the bounds stay in f32, and a NaN index never
    // converts to i32. The select gives candidate 0, and the last if writes NaN. The index value is
    // x<position>: the name i<position> is a WGSL type name for the positions 8, 16, 32 and 64.
    private void WriteIndexRead(IndexRow row)
    {
        var p = row.Position;
        var last = WgslText.Float(row.Candidates.Count - 1);
        statements.Add($"let x{p}: f32 = {Sum($"x{p}", row.IndexBias, row.IndexTerms)};");
        statements.Add($"let p{p}: f32 = floor(x{p});");
        statements.Add($"let c{p}: i32 = select(i32(clamp(p{p}, 0.0f, {last})), 0i, is_nan(x{p}));");
        statements.Add($"var v{p}: f32 = {Candidate(row.Candidates, $"c{p}", $"a{p}")};");
        if (row.BelowZero) statements.Add($"if (p{p} < 0.0f) {{ v{p} = 0.0f; }}");
        if (row.AboveZero) statements.Add($"if (p{p} > {last}) {{ v{p} = 0.0f; }}");
        statements.Add($"if (is_nan(x{p})) {{ v{p} = {NaN}; }}");
    }
}
