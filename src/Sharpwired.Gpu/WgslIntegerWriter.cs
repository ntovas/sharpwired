// Handwired source: src/json-search/bit-emit.ts (the rules Sum, Gate, Comparison and Read, the functions
// conjunction, zeroTest, gateText, atMostZero, intSum and writeRow), without the loop parts and without the
// lanes and the packed reads (decision 2 of the phase 5 plan). The integer writer: a proven bit is a u32
// that holds 0 or 1, a proven whole number is an i32, and each other row is the statement of the f32
// writer (question 3 of the phase 5 plan).

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The integer writer. It holds each wire that <see cref="IntegerProof"/> proves as an i32 or a u32,
/// and writes its row with integer operations, which need no keep. It writes each other row as
/// <see cref="WgslFloatWriter"/> does. An f32 statement reads a whole value as <c>k(f32(w))</c>, which
/// is exact.
/// </summary>
/// <remarks>
/// <para>A whole value can be +0 where the reference value is -0, because i32 has no -0. Compare the
/// outputs with sameNumericValue. The f32 rows keep the subnormal limit of
/// <see cref="WgslFloatWriter"/>. The integer rows hold whole numbers only, so the limit does not apply
/// to them.</para>
/// <para>The integer evaluator checks the grid and the bit declarations of each source table, because the
/// writer reads such a cell as an integer.</para>
/// </remarks>
public static class WgslIntegerWriter
{
    /// <summary>Write the shader of the program.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit.</exception>
    public static WgslModule Write(Circuit circuit, TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        ShaderRows.Validate(circuit, program);
        var rows = ShaderRows.FlattenProgram(program);
        var rootPositions = ShaderRows.RootPositions(program);
        var plan = IntegerPlan.Create(IntegerProof.Analyze(circuit, program, rows), rows, program.GatherSlotCount, rootPositions);
        return new IntegerBody(plan, program.SourceSize).Write(rows, rootPositions);
    }
}

// The statements of the main function of an integer shader. Names: s<cell> for an f32 source cell,
// w<position> for a whole value, b<position> for a bit, v<position> for an f32 value.
internal sealed partial class IntegerBody
{
    private readonly IntegerPlan plan;
    private readonly int sourceSize;
    private readonly FloatBody body;

    // True for a row whose f32 value has the name v<position>: an f32 row, also one that the writer
    // then converts to an integer.
    private readonly bool[] hasFloatName;

    public IntegerBody(IntegerPlan plan, int sourceSize)
    {
        this.plan = plan;
        this.sourceSize = sourceSize;
        hasFloatName = new bool[plan.Proofs.Length];
        body = new FloatBody(sourceSize, FloatNameOf);
    }

    private ImmutableArray<WholeProof> Proofs => plan.Proofs;

    public WgslModule Write(ImmutableArray<ShaderRow> rows, int[] rootPositions)
    {
        var (floatRows, wholeRows, bitRows) = (0, 0, 0);
        foreach (var row in rows)
        {
            if (row is GatherComputeRow compute)
            {
                if (plan.SlotNeeded[compute.Slot]) body.Write(compute);
                continue;
            }
            var position = IntegerPlan.PositionOf(row);
            if (!plan.Needed[position]) continue;
            WriteForm(plan.Forms[position]!);
            switch (Proofs[position].Kind)
            {
                case WholeKind.Whole: wholeRows++; break;
                case WholeKind.Bit: bitRows++; break;
                default: floatRows++; break;
            }
        }
        return body.Finish(rootPositions, floatRows, wholeRows, bitRows);
    }

    [GeneratedRegex(@"^\w+$")]
    private static partial Regex PlainName();

    private static string Grouped(string text) => text.Contains(' ', StringComparison.Ordinal) ? $"({text})" : text;

    private static string TypeOf(WholeKind kind) => kind switch
    {
        WholeKind.Whole => "i32",
        WholeKind.Bit => "u32",
        _ => "f32",
    };

    private static string ZeroOf(WholeKind kind) => kind switch
    {
        WholeKind.Whole => "0i",
        WholeKind.Bit => "0u",
        _ => "0.0f",
    };

    // The f32 text of a position that the writer holds as an integer, or null for the f32 name. The
    // conversion is inside keep: the shader compiler can know the value of a constant integer row,
    // and Metal fast math then regroups the f32 row that reads it.
    private string? FloatNameOf(int position)
    {
        if (!Proofs[position].IsProven || (position >= sourceSize && hasFloatName[position])) return null;
        return $"k(f32({Typed(position)}))";
    }

    // The integer name of a proven position. The first use of a source cell adds its read.
    private string Typed(int position)
    {
        var proof = Proofs[position];
        if (!proof.IsProven) throw new InvalidOperationException($"position {position} is not proven whole");
        var name = $"{(proof.IsBit ? 'b' : 'w')}{position}";
        if (position < sourceSize)
        {
            var read = $"sources[base + {WgslText.Uint(position)}]";
            body.AddSourceRead(position, proof.IsBit ? $"let {name}: u32 = select(0u, 1u, {read} > 0.5f);" : $"let {name}: i32 = i32({read});");
        }
        return name;
    }

    private string IntText(int position) => Proofs[position].IsBit ? $"i32({Typed(position)})" : Typed(position);

    private string BitText(int position)
    {
        if (!Proofs[position].IsBit) throw new InvalidOperationException($"position {position} is not a proven bit");
        return Typed(position);
    }

    private string TextOf(int position, WholeKind kind) => kind switch
    {
        WholeKind.Whole => IntText(position),
        WholeKind.Bit => BitText(position),
        _ => body.Name(position),
    };

    // bit-emit.ts intSum: bias + Σ weight × value as an i32 expression, with the bias first, for
    // example 2i + w3 - 3i * w7. A weight of 1 or -1 writes no product.
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

    // A whole sum that is a bit as a u32 expression: a copy of a bit, 1 - bit as an XOR, or u32(sum).
    private string BitSum(float bias, ImmutableArray<ShaderTerm> terms)
    {
        if (terms.Length == 1 && Proofs[terms[0].Position].IsBit)
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

    // let w<position>: i32 = <expression>, or let b<position>: u32 = u32(<expression>) for a bit.
    private void Whole(int position, string expression)
    {
        if (Proofs[position].IsBit) Bit(position, $"u32({expression})");
        else body.AddStatement($"let {Typed(position)}: i32 = {expression};");
    }

    private void Bit(int position, string expression) => body.AddStatement($"let {Typed(position)}: u32 = {expression};");

    private void WriteForm(IntegerForm form)
    {
        switch (form)
        {
            case FloatForm floatForm:
                {
                    var position = IntegerPlan.PositionOf(floatForm.Row);
                    hasFloatName[position] = true;
                    body.Write(floatForm.Row);
                    // A value that the circuit proves without the range arithmetic, for example the bit
                    // of a sign cascade, or an index read of whole candidates with an f32 index.
                    var proof = Proofs[position];
                    if (proof.IsProven) body.AddStatement($"let {Typed(position)}: {TypeOf(proof.Kind)} = {TypeOf(proof.Kind)}(v{position});");
                    break;
                }
            case SumForm sum:
                WriteSum(sum.Row);
                break;
            case ProductForm product:
                {
                    var row = product.Row;
                    var gate = IntSum(row.GateBias, row.GateTerms);
                    Whole(row.Position, $"{Grouped(IntSum(row.Bias, row.Terms))} * {(product.GateNonNegative ? Grouped(gate) : $"max({gate}, 0i)")}");
                    break;
                }
            case ConjunctionForm conjunction:
                Bit(conjunction.Row.Position, ConjunctionText(conjunction));
                break;
            case GateForm gateForm:
                {
                    var row = gateForm.Row;
                    var gate = IntSum(row.GateBias, row.GateTerms);
                    if (Proofs[row.Position].IsBit) Bit(row.Position, $"u32({gate} > 0i)");
                    else body.AddStatement($"let {Typed(row.Position)}: i32 = max({gate}, 0i);");
                    break;
                }
            case ScoreForm score:
                Bit(score.Row.Position, BitSum(score.Row.Bias, score.Row.Terms));
                break;
            case IndexForm read:
                WriteIndexRead(read);
                break;
            default:
                throw new InvalidOperationException($"unknown form {form.GetType().Name}");
        }
    }

    // The sum of the row, then the candidate of each gather read, or the mass times the candidate for
    // a masked read. The candidate is at the clamped index of the slot, as in the f32 writer.
    private void WriteSum(AffineRow row)
    {
        if (row.Gathers.Length == 0)
        {
            if (Proofs[row.Position].IsBit) Bit(row.Position, BitSum(row.Bias, row.Terms));
            else Whole(row.Position, IntSum(row.Bias, row.Terms));
            return;
        }
        var parts = new List<string>();
        if (row.Bias != 0 || row.Terms.Length > 0) parts.Add(IntSum(row.Bias, row.Terms));
        for (var read = 0; read < row.Gathers.Length; read++)
        {
            var gather = row.Gathers[read];
            var index = $"clamp(gi{gather.Slot}, 0i, {WgslText.Int(gather.Candidates.Count - 1)})";
            var candidate = Candidate(gather.Candidates, index, $"a{row.Position}_{read}", WholeKind.Whole, 0, gather.Candidates.Count - 1);
            parts.Add(gather.Masked ? $"i32(gm{gather.Slot}) * {candidate}" : candidate);
        }
        Whole(row.Position, string.Join(" + ", parts));
    }

    // bit-emit.ts gateText: the bits that must be 1, then the test of each value that must be 0: a
    // comparison is u32(sum <= 0), another whole number is u32(w == 0i), and the bits are one
    // "none of these bits is 1".
    private string ConjunctionText(ConjunctionForm form)
    {
        var factors = form.All.Select(BitText).ToList();
        foreach (var test in form.Tests)
        {
            factors.Add(test switch
            {
                ComparisonTest comparison => $"u32({AtMostZero(comparison.Inner.GateBias, comparison.Inner.GateTerms)})",
                NumberTest number => $"u32({IntText(number.Position)} == 0i)",
                _ => throw new InvalidOperationException($"unknown test {test.GetType().Name}"),
            });
        }
        if (form.ZeroBits.Length > 0)
        {
            var bits = form.ZeroBits.Select(BitText).ToList();
            factors.Add($"({(bits.Count == 1 ? bits[0] : $"({string.Join(" | ", bits)})")} ^ 1u)");
        }
        return factors.Count == 0 ? "1u" : string.Join(" & ", factors);
    }

    // bit-emit.ts, "Read": the index in i32 with no floor. The writer drops a clamp or a select that the
    // index range rules out.
    private void WriteIndexRead(IndexForm form)
    {
        var row = form.Row;
        var p = row.Position;
        var kind = Proofs[p].Kind;
        var last = WgslText.Int(row.Candidates.Count - 1);
        var index = IntSum(row.IndexBias, row.IndexTerms);
        if (!PlainName().IsMatch(index))
        {
            body.AddStatement($"let x{p}: i32 = {index};");
            index = $"x{p}";
        }
        var clamped = form.Clamp ? $"clamp({index}, 0i, {last})" : index;
        var read = Candidate(row.Candidates, clamped, $"a{p}", kind, form.First, form.Final);
        if (form.ZeroAbove) read = $"select({read}, {ZeroOf(kind)}, {index} > {last})";
        if (form.ZeroBelow) read = $"select({read}, {ZeroOf(kind)}, {index} < 0i)";
        body.AddStatement($"let {(kind == WholeKind.Float ? $"v{p}" : Typed(p))}: {TypeOf(kind)} = {read};");
        if (kind == WholeKind.Float) hasFloatName[p] = true;
    }

    // The candidate at the index, of the type of kind. Source cells are read from the buffer and
    // converted. A list of computed values is a select over first..final, the candidates that the
    // index can reach.
    private string Candidate(ShaderCandidates candidates, string index, string arrayName, WholeKind kind, int first, int final)
    {
        if (candidates.Count == 1) return TextOf(candidates.PositionAt(0), kind);
        string Convert(string read) => kind == WholeKind.Float ? read : $"{TypeOf(kind)}({read})";
        switch (candidates.Form)
        {
            case CandidateForm.Strided:
                return Convert($"sources[base + u32({WgslText.Int(candidates.Base)} + {index} * {WgslText.Int(candidates.Stride)})]");
            case CandidateForm.SourceList:
                return Convert($"sources[base + {body.TableName(candidates)}[{index}]]");
            default:
                body.AddStatement($"var {arrayName}: {TypeOf(kind)} = {TextOf(candidates.Positions[first], kind)};");
                for (var candidate = first + 1; candidate <= final; candidate++)
                {
                    body.AddStatement($"{arrayName} = select({arrayName}, {TextOf(candidates.Positions[candidate], kind)}, {index} == {WgslText.Int(candidate)});");
                }
                return arrayName;
        }
    }
}
