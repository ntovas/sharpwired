// Handwired source: src/json-search/bit-emit.ts, without the lanes and the packed reads (decision 2 of
// the phase 5 plan). The parts of the source are the rules Sum, Gate, Comparison and Read, and the
// functions conjunction and zeroTest. They also include the gate read of neverBothOne and the writeRow
// switch. The gate rules of the integer writer: the form of
// each row, and the rows that the roots need. WgslIntegerWriter writes the forms as WGSL text, and the
// RowInterpreter of the tests runs them in C# integer arithmetic.

using System.Collections.Immutable;

namespace Sharpwired.Gpu;

/// <summary>How the integer writer writes one row.</summary>
internal abstract record IntegerForm;

/// <summary>The statement of the f32 writer. A proven value then converts to its integer type.</summary>
internal sealed record FloatForm(ShaderRow Row) : IntegerForm;

/// <summary>bit-emit.ts, "Sum": an i32 sum of whole values, with the gather reads of the row.</summary>
internal sealed record SumForm(AffineRow Row) : IntegerForm;

/// <summary>An i32 product left × max(gate, 0). With a gate that is never negative, no max.</summary>
internal sealed record ProductForm(RegluRow Row, bool GateNonNegative) : IntegerForm;

/// <summary>
/// bit-emit.ts, "Gate": relu(bias + Σ weight × value) is 1 exactly when each bit of
/// <see cref="All"/> is 1, and each value of <see cref="Tests"/> and <see cref="ZeroBits"/> is 0.
/// </summary>
internal sealed record ConjunctionForm(RegluRow Row, ImmutableArray<int> All, ImmutableArray<ZeroTest> Tests, ImmutableArray<int> ZeroBits) : IntegerForm;

/// <summary>Another ReLU of a whole gate: u32(gate > 0i) for a bit, max(gate, 0i) otherwise.</summary>
internal sealed record GateForm(RegluRow Row) : IntegerForm;

/// <summary>A boolean atom of a whole score within 0..1: the score is the bit.</summary>
internal sealed record ScoreForm(BooleanRow Row) : IntegerForm;

/// <summary>
/// bit-emit.ts, "Read": an index read with a whole index. The index reaches the candidates
/// <see cref="First"/>..<see cref="Final"/>. <see cref="Clamp"/>, <see cref="ZeroBelow"/> and
/// <see cref="ZeroAbove"/> are false where the index range rules them out.
/// </summary>
internal sealed record IndexForm(IndexRow Row, int First, int Final, bool Clamp, bool ZeroBelow, bool ZeroAbove) : IntegerForm;

/// <summary>The test of one value of a gate that must be 0.</summary>
internal abstract record ZeroTest;

/// <summary>
/// bit-emit.ts, "Comparison": the value is relu(G) of a whole G, so it is 0 exactly when G &lt;= 0.
/// The value gets no statement for this test.
/// </summary>
internal sealed record ComparisonTest(RegluRow Inner) : ZeroTest;

/// <summary>A whole value that is never negative: w == 0.</summary>
internal sealed record NumberTest(int Position) : ZeroTest;

/// <summary>
/// The form of each row and the rows that the roots need, by packed position. A row that only a
/// comparison test reads gets no statement.
/// </summary>
internal sealed record IntegerPlan(ImmutableArray<WholeProof> Proofs, ImmutableArray<IntegerForm?> Forms, ImmutableArray<bool> Needed, ImmutableArray<bool> SlotNeeded)
{
    /// <summary>
    /// The forms and the needed rows. An index read of a constant table (<paramref name="tables"/>) needs no
    /// candidate row: the writer reads the table from its storage buffer.
    /// </summary>
    public static IntegerPlan Create(ProofResult proof, ImmutableArray<ShaderRow> rows, int slotCount, int[] rootPositions, ConstantTables? tables = null)
    {
        tables ??= ConstantTables.None;
        var proofs = proof.Proofs;
        var rowAt = new ShaderRow?[proofs.Length];
        foreach (var row in rows)
        {
            if (row is not GatherComputeRow) rowAt[PositionOf(row)] = row;
        }
        var forms = new IntegerForm?[proofs.Length];
        foreach (var row in rows)
        {
            if (row is not GatherComputeRow) forms[PositionOf(row)] = FormOf(row, proof, rowAt);
        }

        // The rows that the roots need, from the last row to the first. The rows of a program read
        // only earlier positions.
        var needed = new bool[proofs.Length];
        var slotNeeded = new bool[slotCount];
        foreach (var position in rootPositions)
        {
            if (position >= 0) needed[position] = true;
        }
        for (var index = rows.Length - 1; index >= 0; index--)
        {
            var row = rows[index];
            if (row is GatherComputeRow compute)
            {
                if (!slotNeeded[compute.Slot]) continue;
                foreach (var bit in compute.BitPositions) needed[bit] = true;
                continue;
            }
            var position = PositionOf(row);
            if (!needed[position]) continue;
            foreach (var read in Reads(forms[position]!, tables)) needed[read] = true;
            if (row is AffineRow affine)
            {
                foreach (var gather in affine.Gathers) slotNeeded[gather.Slot] = true;
            }
        }
        return new IntegerPlan(proofs, [.. forms], [.. needed], [.. slotNeeded]);
    }

    /// <summary>The packed position of a row that is not a gather compute.</summary>
    public static int PositionOf(ShaderRow row) => row switch
    {
        AffineRow affine => affine.Position,
        BooleanRow boolean => boolean.Position,
        RegluRow reglu => reglu.Position,
        IndexRow read => read.Position,
        _ => throw new InvalidOperationException($"the row {row.GetType().Name} has no position"),
    };

    private static IntegerForm FormOf(ShaderRow row, ProofResult proof, ShaderRow?[] rowAt)
    {
        var position = PositionOf(row);
        if (!proof.Exact[position]) return new FloatForm(row);
        switch (row)
        {
            case AffineRow affine:
                return new SumForm(affine);
            case RegluRow reglu:
                if (reglu.Terms.Length > 0 || reglu.Bias != 1)
                {
                    var gate = IntegerProof.SumRange(proof.Proofs, reglu.GateBias, reglu.GateTerms)!.Value;
                    return new ProductForm(reglu, gate.Minimum >= 0);
                }
                return Conjunction(reglu, proof, rowAt) ?? (IntegerForm)new GateForm(reglu);
            case BooleanRow boolean:
                return new ScoreForm(boolean);
            case IndexRow read:
                {
                    var index = IntegerProof.SumRange(proof.Proofs, read.IndexBias, read.IndexTerms)!.Value;
                    var (first, final, _) = IntegerProof.Reach(read, index);
                    var last = read.Candidates.Count - 1;
                    return new IndexForm(
                        read,
                        first,
                        final,
                        index.Minimum < 0 || index.Maximum > last,
                        read.BelowZero && index.Minimum < 0,
                        read.AboveZero && index.Maximum > last);
                }
            default:
                throw new InvalidOperationException($"unknown row {row.GetType().Name}");
        }
    }

    /// <summary>
    /// bit-emit.ts conjunction. The gate is max(0, bias + Σ weight × value). Its left form is the
    /// constant 1, and its weights are whole. Each value of positive weight is a bit, and each value of
    /// negative weight is never negative. Its largest value is bias + Σ positive weights. When that is 1,
    /// the gate is 1 exactly when each bit of positive weight is 1 and each value of negative weight is 0.
    /// Otherwise the sum does not get one positive weight of at least 1, because its bit is 0. Or one
    /// negative weight of at most -1 meets a value of at least 1. In both cases the sum is 0 or less. A
    /// term of weight 0 adds nothing, so it gets no test. Null when the row is not of this form.
    /// </summary>
    internal static ConjunctionForm? Conjunction(RegluRow row, ProofResult proof, ShaderRow?[] rowAt)
    {
        if (!proof.Exact[row.Position] || row.Terms.Length > 0 || row.Bias != 1) return null;
        var proofs = proof.Proofs;
        double largest = row.GateBias;
        var all = ImmutableArray.CreateBuilder<int>();
        var tests = ImmutableArray.CreateBuilder<ZeroTest>();
        var zeroBits = ImmutableArray.CreateBuilder<int>();
        foreach (var term in row.GateTerms)
        {
            var operand = proofs[term.Position];
            if (term.Weight > 0)
            {
                if (!operand.IsBit) return null;
                all.Add(term.Position);
                largest += term.Weight;
            }
            else if (term.Weight < 0)
            {
                if (operand.Minimum < 0) return null;
                if (rowAt[term.Position] is RegluRow inner && IsComparison(inner, proof, rowAt)) tests.Add(new ComparisonTest(inner));
                else if (operand.IsBit) zeroBits.Add(term.Position);
                else tests.Add(new NumberTest(term.Position));
            }
        }
        return largest == 1 ? new ConjunctionForm(row, all.ToImmutable(), tests.ToImmutable(), zeroBits.ToImmutable()) : null;
    }

    // bit-emit.ts zeroTest, 'comparison': an exact ReLU of a whole gate, with the left form the
    // constant 1, that is not of the gate form.
    private static bool IsComparison(RegluRow inner, ProofResult proof, ShaderRow?[] rowAt) =>
        proof.Exact[inner.Position] && inner.Terms.Length == 0 && inner.Bias == 1 && Conjunction(inner, proof, rowAt) is null;

    // The positions that the form reads.
    private static IEnumerable<int> Reads(IntegerForm form, ConstantTables tables)
    {
        static IEnumerable<int> Positions(ImmutableArray<ShaderTerm> terms) => terms.Select(term => term.Position);
        IEnumerable<int> AllCandidates(ShaderCandidates candidates) =>
            tables.Contains(candidates) ? [] : Enumerable.Range(0, candidates.Count).Select(candidates.PositionAt);
        IEnumerable<int> GatherCandidates(AffineRow row) => row.Gathers.SelectMany(gather => AllCandidates(gather.Candidates));
        static IEnumerable<int> TestReads(ZeroTest test) => test is ComparisonTest comparison ? Positions(comparison.Inner.GateTerms) : [((NumberTest)test).Position];
        return form switch
        {
            FloatForm { Row: AffineRow row } => Positions(row.Terms).Concat(GatherCandidates(row)),
            FloatForm { Row: RegluRow row } => Positions(row.Terms).Concat(Positions(row.GateTerms)),
            FloatForm { Row: BooleanRow row } => Positions(row.Terms),
            FloatForm { Row: IndexRow row } => Positions(row.IndexTerms).Concat(AllCandidates(row.Candidates)),
            SumForm sum => Positions(sum.Row.Terms).Concat(GatherCandidates(sum.Row)),
            ProductForm product => Positions(product.Row.Terms).Concat(Positions(product.Row.GateTerms)),
            ConjunctionForm conjunction => conjunction.All.Concat(conjunction.ZeroBits).Concat(conjunction.Tests.SelectMany(TestReads)),
            GateForm gate => Positions(gate.Row.GateTerms),
            ScoreForm score => Positions(score.Row.Terms),
            IndexForm read => Positions(read.Row.IndexTerms).Concat(tables.Contains(read.Row.Candidates)
                ? []
                : Enumerable.Range(read.First, read.Final - read.First + 1).Select(read.Row.Candidates.PositionAt)),
            _ => throw new InvalidOperationException($"unknown form {form.GetType().Name}"),
        };
    }
}
