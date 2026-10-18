// Handwired source: src/json-search/bit-emit.ts (emitBitLoopWgsl and writeStages: hold, loadSource, intSum,
// atMostZero, writeFloat, writeRow, the writes of a stage). The integer loop writer, and the statements of
// one stage, which the f32 loop writer shares. A statement is written when a later statement or an output
// first needs it, so a row that only feeds a packed read, a comparison or a word operation gets no statement.
// The rules of the rows are the forms of IntegerPlan (Sum, Gate, Comparison, Read, and the product, the gate
// and the score of phase 5), with the packed reads (PackedReads.cs) and the lanes (LoopLanes.cs). Each other
// row is the statement of the f32 writer (FloatBody). In the f32 writer, each row is that statement.
// Differences from Handwired:
// - Each cell that is not a shared cell is checked before each evaluation, with the messages of the CPU
//   runner on a replay. A check that a proof makes true is not written.
// - The proofs come from the declarations of the cells, which the checks hold. Handwired proves a register
//   only from its writes, because it has no checks.
// - An i32 value that an f32 statement reads is k(f32(w)), as in the integer writer of phase 5.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The integer loop writer (bit-emit.ts): a proven whole value is an i32 and a proven bit a u32, with the rules
/// Sum, Gate, Comparison, Read, Packed reads and Lanes. Each other row is an f32 statement with keep.
/// </summary>
public static class LoopIntegerWriter
{
    /// <summary>Write the kernel of the loop program. A salt that is not 0 adds a comment, so that the shader text is new.</summary>
    public static LoopWgslModule Write(LoopProgram program, uint salt = 0) => LoopKernelWriter.Write(program, GpuWriter.Integer, salt);
}

/// <summary>One value of a stage in the kernel: the text that reads it, and its type (f32, i32, or u32 of 0 or 1).</summary>
internal readonly record struct Held(string Text, WholeKind Type);

/// <summary>The statements of one stage at one position.</summary>
internal sealed partial class LoopStageWriter
{
    private const string NaN = "bitcast<f32>(0x7fc00000u)";

    private readonly LoopKernelWriter kernel;
    private readonly KernelState state;
    private readonly StageInfo stage;
    private readonly int stageIndex;
    private readonly bool integer;
    private readonly ImmutableArray<WholeProof> proofs;
    private readonly StageRules? rules;
    private readonly FloatBody body;
    private readonly List<string> prologue = [];
    private readonly Dictionary<int, Held> held = [];
    private readonly Dictionary<(int, WholeKind), string> conversions = [];
    private readonly HashSet<int> fieldWords = [];
    private readonly HashSet<int> fieldSigns = [];
    private readonly HashSet<int> slotsWritten = [];
    private readonly Dictionary<string, string> sharedLets = new(StringComparer.Ordinal);
    private readonly Dictionary<int, GatherComputeRow> computeOfSlot = [];
    private bool inPrologue;
    private int outputLets;

    public LoopStageWriter(LoopKernelWriter kernel, KernelState state, StageInfo stage, int stageIndex)
    {
        this.kernel = kernel;
        this.state = state;
        this.stage = stage;
        this.stageIndex = stageIndex;
        integer = kernel.Integer;
        proofs = integer ? stage.Proof.Proofs : [.. Enumerable.Repeat(WholeProof.Float, stage.Proof.Proofs.Length)];
        if (integer) rules = new StageRules(stage.SourceSize, stage.Rows, stage.Proof, stage.Plan, cell => stage.Sources[cell] is CellSource.Shared);
        body = new FloatBody(stage.SourceSize, FloatText, candidateRead: CandidateRead, sourceBuffer: false);
        foreach (var row in stage.Rows)
        {
            if (row is GatherComputeRow compute) computeOfSlot[compute.Slot] = compute;
        }
    }

    private int SourceSize => stage.SourceSize;

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex PlainName();

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Grouped(string text) => text.Contains(' ', StringComparison.Ordinal) ? $"({text})" : text;

    private static string TypeOf(WholeKind kind) => kind switch
    {
        WholeKind.Whole => "i32",
        WholeKind.Bit => "u32",
        _ => "f32",
    };

    /// <summary>The lines of the stage: the loads and the checks, the rows, then the writes of the outputs.</summary>
    public List<string> Write()
    {
        inPrologue = true;
        var checks = new List<string>();
        for (var cell = 0; cell < SourceSize; cell++)
        {
            if (stage.Sources[cell] is CellSource.Shared) continue;
            var check = CheckOf(cell);
            if (check != "true") checks.Add(check);
        }
        foreach (var rule in stage.Rules)
        {
            if (rule.Members.All(member => stage.Sources[member] is CellSource.Shared)) continue;
            var mass = string.Join(" + ", rule.Members.Select(member => $"ones({FloatText(member)})"));
            checks.Add(rule.AtMostOne || rule.AllowAllZero ? $"{mass} <= 1u" : $"{mass} == 1u");
        }
        foreach (var check in checks)
        {
            prologue.Add($"if (!({check})) {{");
            prologue.Add("status = 1u;");
            prologue.Add("break;");
            prologue.Add("}");
        }
        inPrologue = false;

        var writes = WriteOutputs();
        return [.. prologue, .. body.BodyStatements(), .. writes];
    }

    // Add a line to the loads before the checks, or to the statements of the rows.
    private void Emit(string line)
    {
        if (inPrologue) prologue.Add(line);
        else body.AddStatement(line);
    }

    // The check of a cell that is not a shared cell, or "true" when the stored range of its register or
    // field proves the declaration.
    private string CheckOf(int cell)
    {
        var declaration = stage.Cells[cell];
        var grid = stage.Grids[cell];
        (long Minimum, long Maximum)? stored = stage.Sources[cell] switch
        {
            CellSource.Register register => kernel.RegisterRange[register.Index],
            CellSource.Field field => kernel.FieldRange[field.Index],
            _ => null,
        };
        if (stored is { } range && range.Minimum >= declaration.Minimum && range.Maximum <= declaration.Maximum
            && (!declaration.ExactBit || (range.Minimum >= 0 && range.Maximum <= 1)) && grid is null or <= 0)
        {
            return "true";
        }
        var value = Hold(cell);
        var tuple = (declaration.Name, declaration.Minimum, declaration.Maximum, declaration.ExactBit, declaration.Grid);
        return value.Type == WholeKind.Float
            ? RecurrentKernelWriter.FloatCheck(value.Text, tuple, grid)
            : RecurrentKernelWriter.IntegerCheck((value.Type, value.Text), tuple, grid);
    }

    // The value at a position, loaded or written the first time something needs it.
    private Held Hold(int position)
    {
        if (held.TryGetValue(position, out var value)) return value;
        value = position < SourceSize ? LoadSource(position) : WriteRow(stage.RowAt[position]!);
        held[position] = value;
        return value;
    }

    private Held LoadSource(int cell)
    {
        switch (stage.Sources[cell])
        {
            case CellSource.Item item:
                Emit($"let c{Text(cell)}: f32 = buffer{Text(item.Buffer)}[base{Text(item.Buffer)} + j];");
                return new Held($"c{Text(cell)}", WholeKind.Float);
            case CellSource.DocumentCell fixedCell:
                {
                    var name = $"d{Text(fixedCell.Buffer)}_{Text(fixedCell.Column)}";
                    state.Hoist(name, $"let {name}: f32 = buffer{Text(fixedCell.Buffer)}[base{Text(fixedCell.Buffer)} + {WgslText.Uint(fixedCell.Column)}];");
                    return new Held(name, WholeKind.Float);
                }
            case CellSource.Shared shared:
                {
                    var name = $"sh{Text(shared.Offset)}";
                    state.Hoist(name, $"let {name}: f32 = shared_values[{WgslText.Uint(shared.Offset)}];");
                    return new Held(name, WholeKind.Float);
                }
            case CellSource.Register register:
                if (state.LaneOf.TryGetValue(register.Index, out var lane))
                {
                    state.LaneReads[lane.Word]++;
                    return new Held($"((rw{Text(lane.Word)} >> {WgslText.Uint(lane.Lane)}) & 1u)", WholeKind.Bit);
                }
                return new Held($"r{Text(register.Index)}", kernel.RegisterStorage[register.Index] switch
                {
                    CellStorage.Bit => WholeKind.Bit,
                    CellStorage.Whole => WholeKind.Whole,
                    _ => WholeKind.Float,
                });
            case CellSource.Field field:
                return LoadField(cell, field);
            default:
                throw new InvalidOperationException($"cell {cell} has no source");
        }
    }

    private Held LoadField(int cell, CellSource.Field field)
    {
        var name = $"fd{Text(cell)}";
        var width = kernel.Shape.Fields[field.Index].Width;
        var element = $"field{Text(field.Index)}[j * {WgslText.Uint(width)} + {WgslText.Uint(field.Component)}]";
        switch (kernel.FieldStorage[field.Index])
        {
            case CellStorage.Whole:
                Emit($"let {name}: i32 = {element};");
                return new Held(name, WholeKind.Whole);
            case CellStorage.Word:
                Emit($"let {name}: u32 = ({FieldWord(field.Index)} >> {WgslText.Uint(field.Component)}) & 1u;");
                return new Held(name, WholeKind.Bit);
            case CellStorage.FloatWord:
                {
                    var word = FieldWord(field.Index);
                    var signs = $"fs{Text(field.Index)}";
                    if (fieldSigns.Add(field.Index)) Emit($"let {signs}: u32 = signs{Text(field.Index)}[j];");
                    var component = WgslText.Uint(field.Component);
                    Emit($"let {name}: f32 = bitcast<f32>(((({word} >> {component}) & 1u) * 1065353216u) | ((({signs} >> {component}) & 1u) << 31u));");
                    return new Held(name, WholeKind.Float);
                }
            default:
                Emit($"let {name}: f32 = {element};");
                return new Held(name, WholeKind.Float);
        }
    }

    // The word of the bits of a field at this position, read one time.
    private string FieldWord(int field)
    {
        var word = $"fw{Text(field)}";
        if (fieldWords.Add(field)) Emit($"let {word}: u32 = field{Text(field)}[j];");
        return word;
    }

    /// <summary>The f32 text of a position. An integer is k(f32(w)), which keeps the f32 row that reads it.</summary>
    private string FloatText(int position)
    {
        var value = Hold(position);
        return value.Type == WholeKind.Float ? value.Text : $"k(f32({value.Text}))";
    }

    private string IntText(int position)
    {
        var value = Hold(position);
        if (!proofs[position].IsProven) throw new InvalidOperationException($"position {position} is not proven whole");
        return value.Type switch
        {
            WholeKind.Whole => value.Text,
            WholeKind.Bit => $"i32({value.Text})",
            _ => Conversion(position, value, WholeKind.Whole),
        };
    }

    private string BitText(int position)
    {
        var value = Hold(position);
        if (!proofs[position].IsBit) throw new InvalidOperationException($"position {position} is not a proven bit");
        return value.Type switch
        {
            WholeKind.Bit => value.Text,
            WholeKind.Whole => $"u32({value.Text})",
            _ => Conversion(position, value, WholeKind.Bit),
        };
    }

    // The let of an f32 value as an i32 or a u32, one time. The value is a proven whole number or bit, so the
    // conversion is exact. A cell of a document or a shared cell gets its let before the first stage.
    private string Conversion(int position, Held value, WholeKind kind)
    {
        if (conversions.TryGetValue((position, kind), out var name)) return name;
        var suffix = kind == WholeKind.Bit ? "b" : "i";
        name = position < SourceSize ? value.Text + suffix : $"{(kind == WholeKind.Bit ? 'b' : 'w')}{Text(position)}";
        var line = $"let {name}: {TypeOf(kind)} = {TypeOf(kind)}({value.Text});";
        if (position < SourceSize && stage.Sources[position] is CellSource.DocumentCell or CellSource.Shared) state.Hoist(name, line);
        else Emit(line);
        conversions[(position, kind)] = name;
        return name;
    }

    private string TextOf(int position, WholeKind kind) => kind switch
    {
        WholeKind.Whole => IntText(position),
        WholeKind.Bit => BitText(position),
        _ => FloatText(position),
    };

    // A read of the candidates at an i32 index from shared_values: a constant table of the stage, or shared
    // cells at evenly spaced offsets. Null for other candidates, which a select reads.
    private string? CandidateRead(ShaderCandidates candidates, string index)
    {
        var start = stage.Tables.StartOf(candidates);
        if (start >= 0) return $"shared_values[{WgslText.Uint(kernel.TableOffsets[stageIndex] + start)} + u32({index})]";
        var offsets = new int[candidates.Count];
        for (var candidate = 0; candidate < offsets.Length; candidate++)
        {
            var position = candidates.PositionAt(candidate);
            if (position >= SourceSize || stage.Sources[position] is not CellSource.Shared shared) return null;
            offsets[candidate] = shared.Offset;
        }
        var stride = offsets.Length > 1 ? offsets[1] - offsets[0] : 0;
        for (var candidate = 1; candidate < offsets.Length; candidate++)
        {
            if (offsets[candidate] - offsets[candidate - 1] != stride) return null;
        }
        return stride == 1
            ? $"shared_values[{WgslText.Uint(offsets[0])} + u32({index})]"
            : $"shared_values[u32({WgslText.Int(offsets[0])} + {index} * {WgslText.Int(stride)})]";
    }

    private bool ReadsShared(ShaderCandidates candidates) => candidates.Count == 1 ? false : CandidateRead(candidates, "0i") is not null;

    private Held WriteRow(ShaderRow row)
    {
        var form = integer ? stage.Plan.Forms[IntegerPlan.PositionOf(row)] : null;
        switch (form)
        {
            case SumForm sum when sum.Row.Gathers.Length == 0:
                return WriteSum(sum.Row);
            case ProductForm product:
                {
                    var gate = IntSum(product.Row.GateBias, product.Row.GateTerms);
                    return Whole(product.Row.Position, $"{Grouped(IntSum(product.Row.Bias, product.Row.Terms))} * {(product.GateNonNegative ? Grouped(gate) : $"max({gate}, 0i)")}");
                }
            case ConjunctionForm conjunction:
                return Bit(conjunction.Row.Position, GateText(conjunction));
            case GateForm gate:
                {
                    var sum = IntSum(gate.Row.GateBias, gate.Row.GateTerms);
                    return proofs[gate.Row.Position].IsBit ? Bit(gate.Row.Position, $"u32({sum} > 0i)") : Whole(gate.Row.Position, $"max({sum}, 0i)");
                }
            case ScoreForm score:
                return Bit(score.Row.Position, BitSum(score.Row.Bias, score.Row.Terms));
            case IndexForm read:
                return WriteIndexRead(read);
            default:
                return WriteFloat(row);
        }
    }

    // The statement of the f32 writer. Each operand is held first, so that the row writer finds each name.
    private Held WriteFloat(ShaderRow row)
    {
        void Hold(ImmutableArray<ShaderTerm> terms)
        {
            foreach (var term in terms) FloatText(term.Position);
        }
        void HoldCandidates(ShaderCandidates candidates)
        {
            if (candidates.Count > 1 && CandidateRead(candidates, "0i") is not null) return;
            for (var candidate = 0; candidate < candidates.Count; candidate++) FloatText(candidates.PositionAt(candidate));
        }
        var position = IntegerPlan.PositionOf(row);
        switch (row)
        {
            case AffineRow affine:
                Hold(affine.Terms);
                foreach (var gather in affine.Gathers)
                {
                    if (slotsWritten.Add(gather.Slot))
                    {
                        var compute = computeOfSlot[gather.Slot];
                        foreach (var bit in compute.BitPositions) FloatText(bit);
                        body.Write(compute);
                    }
                    HoldCandidates(gather.Candidates);
                }
                break;
            case RegluRow reglu:
                Hold(reglu.Terms);
                Hold(reglu.GateTerms);
                break;
            case BooleanRow boolean:
                Hold(boolean.Terms);
                break;
            case IndexRow read:
                Hold(read.IndexTerms);
                HoldCandidates(read.Candidates);
                break;
        }
        body.Write(row);
        state.FloatRows++;
        return new Held($"v{Text(position)}", WholeKind.Float);
    }

    // bit-emit.ts, "Sum": a negated bit is an XOR, a copy of a bit stays a bit, and another sum is an i32 sum.
    private Held WriteSum(AffineRow row)
    {
        if (rules!.NegatedBit(row.Position) is { } negated) return Bit(row.Position, $"{BitText(negated)} ^ 1u");
        if (row.Bias == 0 && row.Terms.Length == 1 && row.Terms[0].Weight == 1 && proofs[row.Terms[0].Position].IsBit)
        {
            return Bit(row.Position, BitText(row.Terms[0].Position));
        }
        return proofs[row.Position].IsBit ? Bit(row.Position, BitSum(row.Bias, row.Terms)) : Whole(row.Position, IntSum(row.Bias, row.Terms));
    }

    // bit-emit.ts, "Read": the index in i32 with no floor. A clamp or a select that the index range rules out
    // is not written.
    private Held WriteIndexRead(IndexForm form)
    {
        var row = form.Row;
        var p = row.Position;
        var kind = proofs[p].Kind;
        var last = WgslText.Int(row.Candidates.Count - 1);
        var index = SharedLet("i32", IntSum(row.IndexBias, row.IndexTerms));
        var read = Candidate(row.Candidates, form.Clamp ? $"clamp({index}, 0i, {last})" : index, $"a{Text(p)}", kind, form.First, form.Final);
        var zero = kind switch
        {
            WholeKind.Whole => "0i",
            WholeKind.Bit => "0u",
            _ => "0.0f",
        };
        if (form.ZeroAbove) read = $"select({read}, {zero}, {index} > {last})";
        if (form.ZeroBelow) read = $"select({read}, {zero}, {index} < 0i)";
        var name = kind switch
        {
            WholeKind.Whole => $"w{Text(p)}",
            WholeKind.Bit => $"b{Text(p)}",
            _ => $"v{Text(p)}",
        };
        Emit($"let {name}: {TypeOf(kind)} = {read};");
        Count(kind);
        return new Held(name, kind);
    }

    // The candidate at the index, of the type of kind: a read of shared_values, or a select over the
    // candidates first..final that the index can reach.
    private string Candidate(ShaderCandidates candidates, string index, string arrayName, WholeKind kind, int first, int final)
    {
        if (candidates.Count == 1) return TextOf(candidates.PositionAt(0), kind);
        if (CandidateRead(candidates, index) is { } read) return kind == WholeKind.Float ? read : $"{TypeOf(kind)}({read})";
        var start = TextOf(candidates.PositionAt(first), kind);
        var values = Enumerable.Range(first + 1, final - first).Select(candidate => TextOf(candidates.PositionAt(candidate), kind)).ToList();
        Emit($"var {arrayName}: {TypeOf(kind)} = {start};");
        for (var candidate = first + 1; candidate <= final; candidate++)
        {
            Emit($"{arrayName} = select({arrayName}, {values[candidate - first - 1]}, {index} == {WgslText.Int(candidate)});");
        }
        return arrayName;
    }

    private void Count(WholeKind kind)
    {
        switch (kind)
        {
            case WholeKind.Whole: state.WholeRows++; break;
            case WholeKind.Bit: state.BitRows++; break;
            default: state.FloatRows++; break;
        }
    }

    // An i32 expression, held as a bit when the row is a proven bit.
    private Held Whole(int position, string expression)
    {
        if (proofs[position].IsBit) return Bit(position, $"u32({expression})");
        Emit($"let w{Text(position)}: i32 = {expression};");
        state.WholeRows++;
        return new Held($"w{Text(position)}", WholeKind.Whole);
    }

    private Held Bit(int position, string expression)
    {
        Emit($"let b{Text(position)}: u32 = {expression};");
        state.BitRows++;
        return new Held($"b{Text(position)}", WholeKind.Bit);
    }

    // bit-emit.ts intSum: bias + Σ weight × value as an i32 expression, the bias first. A weight of 1 or -1
    // writes no product.
    private string IntSum(float bias, IEnumerable<ShaderTerm> terms)
    {
        var text = bias == 0 ? "" : WgslText.Int((int)bias);
        foreach (var term in terms)
        {
            var magnitude = (int)MathF.Abs(term.Weight);
            var product = magnitude == 1 ? IntText(term.Position) : $"{WgslText.Int(magnitude)} * {IntText(term.Position)}";
            var negative = term.Weight < 0;
            text = text.Length == 0 ? (negative ? $"-{product}" : product) : $"{text} {(negative ? '-' : '+')} {product}";
        }
        return text.Length == 0 ? "0i" : text;
    }

    // A whole sum that is a bit, as a u32 expression.
    private string BitSum(float bias, ImmutableArray<ShaderTerm> terms)
    {
        if (terms.Length == 1 && proofs[terms[0].Position].IsBit)
        {
            if (bias == 0 && terms[0].Weight == 1) return BitText(terms[0].Position);
            if (bias == 1 && terms[0].Weight == -1) return $"{BitText(terms[0].Position)} ^ 1u";
        }
        return $"u32({IntSum(bias, terms)})";
    }

    // bit-emit.ts atMostZero: sum <= 0 for a whole sum, with the subtracted terms on the right.
    private string AtMostZero(float bias, ImmutableArray<ShaderTerm> terms)
    {
        var added = terms.Where(term => term.Weight > 0);
        var subtracted = terms.Where(term => term.Weight < 0).Select(term => term with { Weight = -term.Weight });
        return $"{IntSum(MathF.Max(bias, 0), added)} <= {IntSum(MathF.Max(-bias, 0), subtracted)}";
    }

    // A sum or a word as one operand of a larger expression: a name as it is, another text as a let that
    // later statements with the same text read too.
    private string SharedLet(string type, string text)
    {
        if (PlainName().IsMatch(text)) return text;
        if (sharedLets.TryGetValue(text, out var name)) return name;
        name = $"e{Text(sharedLets.Count)}";
        sharedLets[text] = name;
        Emit($"let {name}: {type} = {text};");
        return name;
    }

    // The writes of the outputs, after the statements of the rows. The registers come last, so that each
    // statement above reads the registers of the previous position.
    private List<string> WriteOutputs()
    {
        var (positionIndex, documentStart, _) = kernel.Shape.GroupLayout();
        var fieldLines = new List<string>();
        var windowLines = new List<string>();
        var documentLines = new List<string>();
        var registerLines = new List<string>();
        var wordLanes = new Dictionary<int, int[]>();
        var fieldLanes = new Dictionary<int, int[]>();
        var fieldBits = new Dictionary<int, (string[] Bits, string[] Signs)>();
        string Value(int position) => position < 0 ? NaN : FloatText(position);
        string OutputLet(string text)
        {
            if (PlainName().IsMatch(text)) return text;
            var name = $"o{Text(outputLets++)}";
            Emit($"let {name}: f32 = {text};");
            return name;
        }
        for (var output = 0; output < stage.Writes.Length; output++)
        {
            var position = stage.OutputPositions[output];
            switch (stage.Writes[output])
            {
                case StageWrite.Register register:
                    if (state.LaneOf.TryGetValue(register.Index, out var lane))
                    {
                        if (!wordLanes.TryGetValue(lane.Word, out var lanes)) wordLanes[lane.Word] = lanes = Enumerable.Repeat(-1, state.RegisterWords[lane.Word].Length).ToArray();
                        lanes[lane.Lane] = position;
                        break;
                    }
                    registerLines.Add($"r{Text(register.Index)} = {kernel.RegisterStorage[register.Index] switch
                    {
                        CellStorage.Bit => BitText(position),
                        CellStorage.Whole => IntText(position),
                        _ => Value(position),
                    }};");
                    break;
                case StageWrite.Field field:
                    WriteField(field, position, fieldLines, fieldLanes, fieldBits, OutputLet);
                    break;
                case StageWrite.Position target:
                    windowLines.AddRange(kernel.PositionWrite(positionIndex[target.Group], OutputLet(Value(position))));
                    break;
                case StageWrite.Document target:
                    documentLines.Add($"{LoopKernelWriter.DocumentTarget(documentStart[target.Group], kernel.Shape.Groups[target.Group].ValuesPerDocument, target.Column)} = {Value(position)};");
                    break;
            }
        }
        foreach (var (field, (bits, signs)) in fieldBits)
        {
            if (bits.Contains(null)) throw new InvalidOperationException("a stage that writes a field of bits writes each bit of it");
            fieldLines.Add($"field{Text(field)}[j] = {InLanes(bits)};");
            if (kernel.FieldStorage[field] == CellStorage.FloatWord) fieldLines.Add($"signs{Text(field)}[j] = {InLanes(signs)};");
        }
        foreach (var (field, lanes) in fieldLanes)
        {
            if (lanes.Contains(-1)) throw new InvalidOperationException("a stage that writes a field of bits writes each bit of it");
            fieldLines.Add($"field{Text(field)}[j] = {WordOfBits(lanes)};");
        }
        foreach (var (word, lanes) in wordLanes)
        {
            if (lanes.Contains(-1)) throw new InvalidOperationException("a stage that writes one register of a word writes each register of it");
            registerLines.Add($"rw{Text(word)} = {WordOfBits(lanes)};");
        }
        return [.. fieldLines, .. windowLines, .. documentLines, .. registerLines];
    }

    // The write of one component of a field. A value of a field of bits that no proof makes a bit is
    // checked to be exactly 0 or 1 (or -0), as the CPU runner checks it.
    private void WriteField(
        StageWrite.Field field, int position, List<string> lines, Dictionary<int, int[]> fieldLanes,
        Dictionary<int, (string[] Bits, string[] Signs)> fieldBits, Func<string, string> outputLet)
    {
        var shape = kernel.Shape.Fields[field.Index];
        var storage = kernel.FieldStorage[field.Index];
        var element = $"field{Text(field.Index)}[j * {WgslText.Uint(shape.Width)} + {WgslText.Uint(field.Component)}]";
        switch (storage)
        {
            case CellStorage.Whole:
                lines.Add($"{element} = {IntText(position)};");
                return;
            case CellStorage.Float:
                lines.Add($"{element} = {(position < 0 ? NaN : FloatText(position))};");
                return;
        }
        if (storage == CellStorage.Word && position >= 0 && proofs[position].IsBit)
        {
            if (!fieldLanes.TryGetValue(field.Index, out var lanes)) fieldLanes[field.Index] = lanes = Enumerable.Repeat(-1, shape.Width).ToArray();
            lanes[field.Component] = position;
            return;
        }
        var value = outputLet(position < 0 ? NaN : FloatText(position));
        var proven = position >= 0 && stage.Proof.Proofs[position].IsBit;
        if (!proven)
        {
            lines.Add($"if (!(is_bit({value}))) {{");
            lines.Add("status = 1u;");
            lines.Add("break;");
            lines.Add("}");
        }
        if (!fieldBits.TryGetValue(field.Index, out var parts)) fieldBits[field.Index] = parts = (new string[shape.Width], new string[shape.Width]);
        parts.Bits[field.Component] = $"u32(bitcast<u32>({value}) == 1065353216u)";
        parts.Signs[field.Component] = $"(bitcast<u32>({value}) >> 31u)";
    }

    // Bits written into the lanes of a word one by one, bit k into lane k.
    private static string InLanes(IReadOnlyList<string> bits) =>
        string.Join(" | ", bits.Select((bit, lane) => lane == 0 ? bit : $"({bit} << {WgslText.Uint(lane)})"));
}
