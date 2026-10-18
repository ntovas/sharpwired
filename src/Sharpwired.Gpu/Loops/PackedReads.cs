// Handwired source: src/json-search/bit-emit.ts, the rule "Packed reads" (gatedRead, maximumGatingBits,
// neverBothOne, openText, packedWord, opensOf, zeroTest, zeroTests, gateText) and packWords. A gated read is
// an index read of bits whose index form is base + gate, where the gate sums the bits whose weight is at least
// the length of the table. For each combination of those bits, either the gate is one fixed number, and the
// read is table[base], or the index lies past a side of the table that reads +0. So the read is
// table[base] && open, with open a function of the gating bits. In a gate, two or more gated reads of one
// base that must all be 0 are one read of a packed word: (word[base] & opens) == 0u, where bit k of the word
// is table k and bit k of opens is the open bit of read k. The runner fills the words from the shared cells
// with PackedWords.Pack. Two gating bits are an OR only when they are never both 1: the proof takes both as 1
// and follows what each gate then says about its own operands, until one value must be both 0 and 1.
// Differences from Handwired:
// - The forms of IntegerPlan give the negated bits and the conjunctions. A term of weight 0 gets no test.
// - The candidates of a gated read must be shared cells that are proven bits. A constant is not packed.

using System.Collections.Immutable;
using System.Globalization;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>bit-emit.ts packWords: the words of the buffer packed_words from the shared cells.</summary>
public static class PackedWords
{
    /// <summary>
    /// Set each word to 0, then set bit <see cref="PackedCell.Lane"/> of word <see cref="PackedCell.Word"/> for
    /// each cell whose shared value is not 0. The shared cells of a packed read are checked bits, 0, -0 or 1.
    /// </summary>
    /// <exception cref="ArgumentException">A cell names a word, a lane or a shared offset outside its span.</exception>
    public static void Pack(ImmutableArray<PackedCell> cells, ReadOnlySpan<float> shared, Span<uint> words)
    {
        words.Clear();
        foreach (var cell in cells)
        {
            if ((uint)cell.Word >= (uint)words.Length || (uint)cell.Lane >= 32 || (uint)cell.SharedOffset >= (uint)shared.Length)
            {
                throw new ArgumentException($"the packed cell {cell} is outside the {words.Length} words or the {shared.Length} shared cells", nameof(cells));
            }
            if (shared[cell.SharedOffset] != 0) words[cell.Word] |= 1u << cell.Lane;
        }
    }
}

/// <summary>
/// A gated read: the base terms and the base bias of the index when the read is open, the range of that index,
/// the gating bits, the open bit of each combination of the gating bits (bit c for combination c, gating bit 0
/// first), and the key of the reads that share one packed word.
/// </summary>
internal sealed record GatedRead(ImmutableArray<ShaderTerm> BaseTerms, int BaseBias, long Minimum, long Maximum, ImmutableArray<int> Gating, uint Open, string Key)
{
    // The open mask with each combination open.
    public uint Every => Gating.Length == 0 ? 1u : (uint)((1L << (1 << Gating.Length)) - 1);
}

/// <summary>The proofs of the packed reads of one stage: the negated bits, the conjunctions, the bits that are never both 1, and the gated reads.</summary>
internal sealed class StageRules
{
    /// <summary>
    /// A read with more gating bits is read on its own. The rule goes through each combination of the gating
    /// bits, 16 for 4 bits, and the open mask holds one bit for each combination.
    /// </summary>
    public const int MaximumGatingBits = 4;

    private readonly int sourceSize;
    private readonly ImmutableArray<WholeProof> proofs;
    private readonly ImmutableArray<bool> exact;
    private readonly ImmutableArray<IntegerForm?> forms;
    private readonly ShaderRow?[] rowAt;
    private readonly Func<int, bool> sharedCell;
    private readonly Dictionary<(int, int), bool> excluded = [];
    private readonly Dictionary<int, GatedRead?> gated = [];

    public StageRules(int sourceSize, ImmutableArray<ShaderRow> rows, ProofResult proof, IntegerPlan plan, Func<int, bool> sharedCell)
    {
        this.sourceSize = sourceSize;
        proofs = proof.Proofs;
        exact = proof.Exact;
        forms = plan.Forms;
        this.sharedCell = sharedCell;
        rowAt = new ShaderRow?[proofs.Length];
        foreach (var row in rows)
        {
            if (row is not GatherComputeRow) rowAt[IntegerPlan.PositionOf(row)] = row;
        }
    }

    /// <summary>The rules of a program of a circuit, for the tests. sharedCell tells which source cells are shared cells.</summary>
    public static StageRules For(Circuit circuit, TensorProgram program, Func<int, bool> sharedCell)
    {
        var rows = ShaderRows.FlattenProgram(program);
        var proof = IntegerProof.Analyze(circuit, program, rows);
        var plan = IntegerPlan.Create(proof, rows, program.GatherSlotCount, ShaderRows.RootPositions(program));
        return new StageRules(program.SourceSize, rows, proof, plan, sharedCell);
    }

    public ShaderRow? RowAt(int position) => position < sourceSize ? null : rowAt[position];

    public WholeProof Proof(int position) => proofs[position];

    /// <summary>JS !bit, as 1 - bit: the position of the bit, or null for another row.</summary>
    public int? NegatedBit(int position) =>
        forms[position] is SumForm { Row: { Bias: 1, Terms: [{ Weight: -1 } term], Gathers.Length: 0 } } && proofs[term.Position].IsBit ? term.Position : null;

    /// <summary>The Gate rule of a row, or null.</summary>
    public ConjunctionForm? Conjunction(int position) => position < sourceSize ? null : forms[position] as ConjunctionForm;

    /// <summary>The values that must all be 0 for the gate to be 1.</summary>
    public static IEnumerable<int> NoneOf(ConjunctionForm form) =>
        form.ZeroBits.Concat(form.Tests.Select(test => test is ComparisonTest comparison ? comparison.Inner.Position : ((NumberTest)test).Position));

    /// <summary>
    /// True when the two bits are never both 1. Both are taken as 1, and each gate that is then 1 says what its
    /// own operands are, until one value must be both 0 and 1.
    /// </summary>
    public bool NeverBothOne(int first, int second)
    {
        var key = (Math.Min(first, second), Math.Max(first, second));
        if (excluded.TryGetValue(key, out var proven)) return proven;
        proven = false;
        var forced = new Dictionary<int, bool>();
        var pending = new Stack<(int Position, bool One)>([(first, true), (second, true)]);
        while (pending.Count > 0 && !proven)
        {
            var (position, one) = pending.Pop();
            if (forced.TryGetValue(position, out var known))
            {
                proven = known != one;
                continue;
            }
            forced[position] = one;
            if (position < sourceSize) continue;
            if (NegatedBit(position) is { } negated)
            {
                pending.Push((negated, !one));
            }
            else if (one && Conjunction(position) is { } gate)
            {
                foreach (var operand in gate.All) pending.Push((operand, true));
                foreach (var operand in NoneOf(gate)) pending.Push((operand, false));
            }
        }
        excluded[key] = proven;
        return proven;
    }

    /// <summary>The Packed reads rule's reading of one index read, or null when the read is not a gated read of bits.</summary>
    public GatedRead? Gated(IndexRow row)
    {
        if (gated.TryGetValue(row.Position, out var known)) return known;
        var result = Read(row);
        gated[row.Position] = result;
        return result;
    }

    private GatedRead? Read(IndexRow row)
    {
        if (!exact[row.Position]) return null;
        var count = row.Candidates.Count;
        // The words are packed from each candidate, also from one that the index never reaches. So each one
        // must be a shared cell that is a proven bit, which the check of the shared cells holds.
        for (var candidate = 0; candidate < count; candidate++)
        {
            var cell = row.Candidates.PositionAt(candidate);
            if (cell >= sourceSize || !sharedCell(cell) || !proofs[cell].IsBit) return null;
        }
        bool IsGating(ShaderTerm term) => proofs[term.Position].IsBit && MathF.Abs(term.Weight) >= count;
        var gating = row.IndexTerms.Where(IsGating).ToImmutableArray();
        var baseTerms = row.IndexTerms.Where(term => !IsGating(term)).ToImmutableArray();
        if (gating.Length > MaximumGatingBits || IntegerProof.SumRange(proofs, 0, baseTerms) is not { } range) return null;
        var last = count - 1;
        var open = 0u;
        long? baseBias = null;
        for (var combination = 0; combination < 1 << gating.Length; combination++)
        {
            var gate = (long)row.IndexBias;
            var possible = true;
            for (var bit = 0; bit < gating.Length; bit++)
            {
                if (((combination >> bit) & 1) == 0) continue;
                gate += (long)gating[bit].Weight;
                for (var earlier = 0; earlier < bit; earlier++)
                {
                    if (((combination >> earlier) & 1) == 1 && NeverBothOne(gating[earlier].Position, gating[bit].Position)) possible = false;
                }
            }
            // A combination that never occurs can count as open, which keeps an OR of two bits an OR.
            var closed = (row.AboveZero && gate + range.Minimum > last) || (row.BelowZero && gate + range.Maximum < 0);
            if (!possible || closed)
            {
                if (!possible) open |= 1u << combination;
                continue;
            }
            if (baseBias is { } fixedBias && fixedBias != gate) return null;
            baseBias = gate;
            open |= 1u << combination;
        }
        if (baseBias is not { } bias) return null;
        var key = string.Join(",", new[] { count.ToString(CultureInfo.InvariantCulture), row.BelowZero ? "1" : "0", row.AboveZero ? "1" : "0", bias.ToString(CultureInfo.InvariantCulture) }
            .Concat(baseTerms.Select(term => $"{term.Position.ToString(CultureInfo.InvariantCulture)}*{term.Weight.ToString("R", CultureInfo.InvariantCulture)}")));
        return new GatedRead(baseTerms, (int)bias, range.Minimum + bias, range.Maximum + bias, [.. gating.Select(term => term.Position)], open, key);
    }

    /// <summary>
    /// How the gate tests that a value with a negative weight is 0: a gated read goes into a packed word, a bit is
    /// tested as a bit. The comparisons and the numbers of the form are tested on their own.
    /// </summary>
    public bool IsGatedReadOfBits(int position) => RowAt(position) is IndexRow read && proofs[position].IsBit && Gated(read) is not null;
}

internal sealed partial class LoopStageWriter
{
    private enum OpenKind { Always, Not, Any, Each }

    // bit-emit.ts gateText: the bits that must be 1, the comparisons and the numbers, the packed words, then
    // "none of these bits is 1" as one comparison of a word with 0 or an OR of the bits.
    private string GateText(ConjunctionForm form)
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
        var (packs, bitLists) = ZeroTests(form.ZeroBits);
        foreach (var (gatedRead, reads) in packs) factors.Add($"u32(({PackedWord(gatedRead, reads)}) == 0u)");
        foreach (var (bits, word) in bitLists)
        {
            if (word is not null) factors.Add($"u32({WordOf(word)} == 0u)");
            else factors.Add($"({(bits.Count == 1 ? BitText(bits[0]) : $"({string.Join(" | ", bits.Select(BitText))})")} ^ 1u)");
        }
        return factors.Count == 0 ? "1u" : string.Join(" & ", factors);
    }

    // bit-emit.ts zeroTests: the gated reads with the same key go into packed words, at most 32 reads in a word.
    // One gated read alone is an ordinary bit. The bits, in lists of at most 32, are one word each where they
    // are a word that is not built lane by lane.
    private (List<(GatedRead Gated, List<IndexRow> Reads)> Packs, List<(List<int> Bits, WordPlan? Word)> BitLists) ZeroTests(ImmutableArray<int> zeroBits)
    {
        var groups = new List<(GatedRead Gated, List<IndexRow> Reads)>();
        var bits = new List<int>();
        foreach (var position in zeroBits)
        {
            if (!rules!.IsGatedReadOfBits(position))
            {
                bits.Add(position);
                continue;
            }
            var read = (IndexRow)stage.RowAt[position]!;
            var gatedRead = rules.Gated(read)!;
            var group = groups.FindIndex(entry => entry.Gated.Key == gatedRead.Key);
            if (group < 0) groups.Add((gatedRead, [read]));
            else groups[group].Reads.Add(read);
        }
        var packs = new List<(GatedRead Gated, List<IndexRow> Reads)>();
        foreach (var (gatedRead, reads) in groups)
        {
            foreach (var list in WordLists(reads, read => read.Position))
            {
                if (list.Count == 1) bits.Add(list[0].Position);
                else packs.Add((gatedRead, list));
            }
        }
        var bitLists = WordLists(bits, position => position).Select(list => (list, WordPlanOf(list) is { } word && IsWhole(word) ? word : null)).ToList();
        return (packs, bitLists);
    }

    // The kind of the word of the open bits of some gated reads: no word when each read is always open, the
    // gating bits as a word negated (each read is open when its one gating bit is 0), the gating bits with the
    // further bits of each read in its lane (each read is open when one of its gating bits is 1), or each open
    // bit written into its lane.
    private OpenKind OpensOf(IReadOnlyList<IndexRow> reads, out List<GatedRead> gateds)
    {
        gateds = [.. reads.Select(read => rules!.Gated(read)!)];
        if (gateds.All(each => each.Open == each.Every)) return OpenKind.Always;
        if (gateds.All(each => each.Gating.Length == 1 && each.Open == 1)) return OpenKind.Not;
        if (gateds.All(each => each.Gating.Length >= 1 && each.Open == each.Every - 1)) return OpenKind.Any;
        return OpenKind.Each;
    }

    // bit-emit.ts openText: open as a u32 expression of 0 or 1 over the gating bits.
    private string OpenText(GatedRead read)
    {
        if (read.Open == read.Every) return "1u";
        var bits = read.Gating.Select(BitText).ToList();
        if (bits.Count == 1) return read.Open == 2 ? bits[0] : $"({bits[0]} ^ 1u)";
        if (read.Open == read.Every - 1) return $"({string.Join(" | ", bits)})";
        return $"(({read.Open.ToString(CultureInfo.InvariantCulture)}u >> ({InLanes(bits)})) & 1u)";
    }

    // bit-emit.ts packedWord: the word whose bit k is gated read k, for reads that share a key: one read of a
    // packed word, and one AND with the word of the open bits of the reads. A word of zeros before or after the
    // table stands for a side that reads +0.
    private string PackedWord(GatedRead gatedRead, List<IndexRow> reads)
    {
        var first = reads[0];
        var last = first.Candidates.Count - 1;
        var zeroBefore = first.BelowZero && gatedRead.Minimum < 0;
        var zeroAfter = first.AboveZero && gatedRead.Maximum > last;
        int SharedOffset(IndexRow read, int candidate) => ((CellSource.Shared)stage.Sources[read.Candidates.PositionAt(candidate)]).Offset;
        var tableKey = string.Join(";", [zeroBefore ? "1" : "0", zeroAfter ? "1" : "0", Text(first.Candidates.Count),
            .. reads.Select(read => string.Join(",", Enumerable.Range(0, read.Candidates.Count).Select(candidate => Text(SharedOffset(read, candidate)))))]);
        if (!state.PackedTables.TryGetValue(tableKey, out var start))
        {
            start = state.PackedCells.Count;
            state.PackedTables[tableKey] = start;
            if (zeroBefore) state.PackedCells.Add([]);
            for (var candidate = 0; candidate <= last; candidate++) state.PackedCells.Add([.. reads.Select(read => SharedOffset(read, candidate))]);
            if (zeroAfter) state.PackedCells.Add([]);
        }
        var lowest = zeroBefore ? -1 : 0;
        var highest = zeroAfter ? last + 1 : last;
        var index = SharedLet("i32", IntSum(gatedRead.BaseBias, gatedRead.BaseTerms));
        if (gatedRead.Minimum < lowest || gatedRead.Maximum > highest) index = $"clamp({index}, {WgslText.Int(lowest)}, {WgslText.Int(highest)})";
        state.PackedReads += reads.Count;
        var offset = start - lowest;
        var word = $"packed_words[u32({(offset == 0 ? "" : $"{WgslText.Int(offset)} + ")}{index})]";
        var kind = OpensOf(reads, out var gateds);
        // The first gating bit of each read: the kinds Not and Any have at least one for each read.
        List<int> FirstBits() => [.. gateds.Select(each => each.Gating[0])];
        return kind switch
        {
            OpenKind.Always => word,
            OpenKind.Not => $"{word} & {SharedLet("u32", $"{WordOfBits(FirstBits())} ^ {LanesMask(reads.Count)}")}",
            OpenKind.Any => $"{word} & {SharedLet("u32", string.Join(" | ", [WordOfBits(FirstBits()), .. gateds.SelectMany((each, lane) => each.Gating.Skip(1).Select(bit => lane == 0 ? BitText(bit) : $"({BitText(bit)} << {WgslText.Uint(lane)})"))]))}",
            _ => $"{word} & {SharedLet("u32", InLanes([.. gateds.Select(OpenText)]))}",
        };
    }

    private static string LanesMask(int count) => count == 32 ? "4294967295u" : $"{((1u << count) - 1).ToString(CultureInfo.InvariantCulture)}u";
}
