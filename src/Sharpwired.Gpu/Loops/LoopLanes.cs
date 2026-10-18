// Handwired source: src/json-search/bit-emit.ts, the rule "Lanes" (the words of registers, storedLane,
// originOf, wordLists, wordPlan, isWhole, wordOf, wordOfBits). Bits that are stored together are one u32
// word, each bit at its own lane. Such bits are a field of bits, or registers of bits that the same stages
// write. When the same gate or negation is computed for each bit of such a list, one word operation computes
// all of them. AND, OR and XOR work on each bit of a word on its own. For example, continuing[s] =
// reached[s] && !notReach[s] for 8 states is reached & (notReach ^ 255u). "None of these bits is 1" over
// such a word is word == 0u. A list of gated reads with the same base is the packed word itself. A list of
// shared cells is packed into a word one time for each document. Another list of bits is written into its
// lanes one by one. A word holds 32 bits, and a longer list is taken 32 bits at a time.
// The words of registers: the stages are written with a first choice of words. A word of which half the
// registers or more were read one by one is replaced, and the stages are written again. The new words hold
// first the registers that the same stages write. Then they hold, of those, the registers that each stage
// writes with one kind of operation. Then they hold each register on its own.

using System.Collections.Immutable;
using System.Globalization;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The plan of one word of bits, bit k at lane k. The bits are neighbor lanes of a stored word, shared cells,
/// or gated reads with one key. Or they are the negations of the bits of a word, the gates of the bits of
/// words, or bits built lane by lane.
/// </summary>
internal abstract record WordPlan(ImmutableArray<int> Positions);

internal sealed record StoredWord(ImmutableArray<int> Positions, Func<string> Word, int First, int Width) : WordPlan(Positions);

internal sealed record SharedWord(ImmutableArray<int> Positions) : WordPlan(Positions);

internal sealed record ReadsWord(ImmutableArray<int> Positions, GatedRead Gated, List<IndexRow> Reads) : WordPlan(Positions);

internal sealed record NotWord(ImmutableArray<int> Positions, WordPlan Operand) : WordPlan(Positions);

internal sealed record GateWord(ImmutableArray<int> Positions, ImmutableArray<WordPlan> All, ImmutableArray<WordPlan> None) : WordPlan(Positions);

internal sealed record LanesWord(ImmutableArray<int> Positions) : WordPlan(Positions);

internal sealed partial class LoopKernelWriter
{
    private List<(ImmutableArray<int> Registers, bool Split)> wordChoices = [];

    // The first choice: the registers of bits that the same stages write, 32 a word, with no register alone.
    private ImmutableArray<ImmutableArray<int>> FirstWords()
    {
        var bits = Enumerable.Range(0, RegisterStorage.Length).Where(register => RegisterStorage[register] == CellStorage.Bit).ToList();
        wordChoices = [.. WordsBy(bits, register => WritesOf(register, form: false)).Select(registers => (registers, false))];
        return [.. wordChoices.Select(choice => choice.Registers)];
    }

    // The next choice after an attempt: a word that pays stays. A word of the first choice splits by the kind
    // of operation of each write, and a word of the second choice ends.
    private ImmutableArray<ImmutableArray<int>> NextWords(ImmutableArray<ImmutableArray<int>> words, KernelState state)
    {
        bool Pays(int word) => state.LaneReads[word] * 2 < words[word].Length;
        if (Enumerable.Range(0, words.Length).All(Pays)) return words;
        var next = new List<(ImmutableArray<int> Registers, bool Split)>();
        for (var word = 0; word < words.Length; word++)
        {
            if (Pays(word))
            {
                next.Add(wordChoices[word]);
                continue;
            }
            if (wordChoices[word].Split) continue;
            next.AddRange(WordsBy([.. words[word]], register => WritesOf(register, form: true))
                .Where(registers => registers.Length < words[word].Length)
                .Select(registers => (registers, true)));
        }
        wordChoices = next;
        return [.. next.Select(choice => choice.Registers)];
    }

    // The registers with the same key, 32 a word, with no register alone.
    private static List<ImmutableArray<int>> WordsBy(List<int> registers, Func<int, string> keyOf)
    {
        var groups = new List<(string Key, List<int> Registers)>();
        foreach (var register in registers)
        {
            var key = keyOf(register);
            var group = groups.FindIndex(entry => entry.Key == key);
            if (group < 0) groups.Add((key, [register]));
            else groups[group].Registers.Add(register);
        }
        var words = new List<ImmutableArray<int>>();
        foreach (var (_, group) in groups)
        {
            for (var from = 0; from < group.Count; from += 32) words.Add([.. group.Skip(from).Take(32)]);
        }
        return [.. words.Where(word => word.Length > 1)];
    }

    // What the stages write into a register: for each stage, "written". With form, the text gives the kind
    // of the row and how many of its weights are positive.
    private string WritesOf(int register, bool form) => string.Join(",", Stages.Select(stage =>
    {
        var output = Enumerable.Range(0, stage.Writes.Length).FirstOrDefault(index => stage.Writes[index] is StageWrite.Register target && target.Index == register, -1);
        if (output < 0) return "";
        if (!form) return "written";
        var position = stage.OutputPositions[output];
        if (position < stage.SourceSize) return "cell";
        var row = stage.RowAt[position]!;
        var terms = row switch
        {
            IndexRow read => read.IndexTerms,
            RegluRow reglu => [.. reglu.GateTerms, .. reglu.Terms],
            AffineRow affine => affine.Terms,
            BooleanRow boolean => boolean.Terms,
            _ => [],
        };
        return $"{row.GetType().Name} {terms.Count(term => term.Weight > 0).ToString(CultureInfo.InvariantCulture)} {terms.Length.ToString(CultureInfo.InvariantCulture)}";
    }));
}

internal sealed partial class LoopStageWriter
{
    private readonly Dictionary<int, (string Key, int Lane)?> origins = [];
    private readonly Dictionary<string, string> wordTexts = new(StringComparer.Ordinal);

    // The stored word and the lane of a source cell that is one bit of a word of a field or of registers.
    private (string Key, int Lane, Func<string> Word, int Width)? StoredLane(int position)
    {
        if (position >= SourceSize) return null;
        switch (stage.Sources[position])
        {
            case CellSource.Register register when state.LaneOf.TryGetValue(register.Index, out var lane):
                return ($"r{Text(lane.Word)}", lane.Lane, () => $"rw{Text(lane.Word)}", state.RegisterWords[lane.Word].Length);
            case CellSource.Field field when kernel.FieldStorage[field.Index] == CellStorage.Word:
                return ($"f{Text(field.Index)}", field.Component, () => FieldWord(field.Index), kernel.Shape.Fields[field.Index].Width);
            default:
                return null;
        }
    }

    // The stored lane that a bit is computed from: its own, or that of the first operand that has one. The
    // search goes through negations, gates of the Gate rule and the gating bits of gated reads.
    private (string Key, int Lane)? OriginOf(int position)
    {
        if (origins.TryGetValue(position, out var known)) return known;
        origins[position] = null;
        IEnumerable<int> operands = [];
        if (position >= SourceSize)
        {
            if (rules!.NegatedBit(position) is { } negated) operands = [negated];
            else if (rules.Conjunction(position) is { } gate) operands = gate.All.Concat(StageRules.NoneOf(gate));
            else if (stage.RowAt[position] is IndexRow read && rules.Gated(read) is { } gated) operands = gated.Gating;
        }
        (string Key, int Lane)? origin = position < SourceSize && StoredLane(position) is { } stored ? (stored.Key, stored.Lane) : null;
        foreach (var operand in operands)
        {
            if (origin is not null) break;
            origin = OriginOf(operand);
        }
        origins[position] = origin;
        return origin;
    }

    // A list of bits whose order is free, as lists of at most 32 bits that can each be one word. The bits that
    // come from one stored word are in the order of its lanes, and the other bits are in the given order.
    private List<List<T>> WordLists<T>(IReadOnlyList<T> items, Func<T, int> positionOf)
    {
        var byWord = new List<(string Key, List<(T Item, int Lane)> Entries)>();
        foreach (var item in items)
        {
            var origin = OriginOf(positionOf(item));
            var key = origin?.Key ?? "";
            var group = byWord.FindIndex(entry => entry.Key == key);
            if (group < 0) byWord.Add((key, [(item, origin?.Lane ?? 0)]));
            else byWord[group].Entries.Add((item, origin?.Lane ?? 0));
        }
        var lists = new List<List<T>>();
        foreach (var (_, entries) in byWord)
        {
            // The order of two bits of one lane, and of the bits with no stored word, stays.
            var sorted = entries.Select((entry, index) => (entry, index)).OrderBy(pair => pair.entry.Lane).ThenBy(pair => pair.index).Select(pair => pair.entry.Item).ToList();
            for (var from = 0; from < sorted.Count; from += 32) lists.Add([.. sorted.Skip(from).Take(32)]);
        }
        return lists;
    }

    private WordPlan SlotOf(IReadOnlyList<int> positions) => WordPlanOf(positions) ?? new LanesWord([.. positions]);

    // bit-emit.ts wordPlan: the plan of a word of at most 32 proven bits, or null.
    private WordPlan? WordPlanOf(IReadOnlyList<int> positions)
    {
        if (positions.Count > 32) throw new InvalidOperationException("a word holds at most 32 bits");
        if (positions.Count < 2 || !positions.All(position => proofs[position].IsBit)) return null;
        ImmutableArray<int> list = [.. positions];
        var lanes = positions.Select(StoredLane).ToList();
        if (lanes[0] is { } first && lanes.Select((lane, index) => lane is { } each && each.Key == first.Key && each.Lane == first.Lane + index).All(same => same))
        {
            return new StoredWord(list, first.Word, first.Lane, first.Width);
        }
        if (positions.All(position => position < SourceSize && stage.Sources[position] is CellSource.Shared)) return new SharedWord(list);
        var rows = positions.Select(position => position < SourceSize ? null : stage.RowAt[position]).ToList();
        var reads = rows.OfType<IndexRow>().ToList();
        var gateds = reads.Select(read => rules!.Gated(read)).OfType<GatedRead>().ToList();
        if (gateds.Count == positions.Count && gateds.All(gated => gated.Key == gateds[0].Key)) return new ReadsWord(list, gateds[0], reads);
        var negated = positions.Select(position => position < SourceSize ? null : rules!.NegatedBit(position)).ToList();
        if (negated.All(operand => operand is not null)) return new NotWord(list, SlotOf([.. negated.Select(operand => operand!.Value)]));
        var gates = positions.Select(position => rules!.Conjunction(position)).ToList();
        if (gates.Any(gate => gate is null)) return null;
        var shape = gates[0]!;
        // A comparison or a number among the values that must be 0 is tested on its own, so such gates stay one
        // statement each.
        if (!gates.All(gate => gate!.All.Length == shape.All.Length && gate.Tests.Length == 0 && gate.ZeroBits.Length == shape.ZeroBits.Length)) return null;
        var none = Enumerable.Range(0, shape.ZeroBits.Length).Select(index => SlotOf([.. gates.Select(gate => gate!.ZeroBits[index])])).ToImmutableArray();
        // The values that must be 0 are taken lane by lane when each of those lists is a word not built lane by
        // lane. When one is not, a gate that tests its own values as a word keeps them.
        if (!none.All(IsWhole) && gates.Any(gate => TestsWords(gate!.ZeroBits))) return null;
        var all = Enumerable.Range(0, shape.All.Length).Select(index => SlotOf([.. gates.Select(gate => gate!.All[index])])).ToImmutableArray();
        return new GateWord(list, all, none);
    }

    // True when the zero tests of a gate read a packed word or a word that is not built lane by lane.
    private bool TestsWords(ImmutableArray<int> zeroBits)
    {
        var (packs, bitLists) = ZeroTests(zeroBits);
        return packs.Count > 0 || bitLists.Any(list => list.Word is not null);
    }

    private bool IsWhole(WordPlan plan)
    {
        switch (plan)
        {
            case LanesWord:
                return false;
            case StoredWord or SharedWord:
                return true;
            case ReadsWord reads:
                {
                    var kind = OpensOf(reads.Reads, out var gateds);
                    if (kind == OpenKind.Always) return true;
                    if (kind == OpenKind.Each || gateds.Any(each => each.Gating.Length > 1)) return false;
                    return WordPlanOf([.. gateds.Select(each => each.Gating[0])]) is { } gating && IsWhole(gating);
                }
            case NotWord not:
                return IsWhole(not.Operand);
            case GateWord gate:
                return gate.All.All(IsWhole) && gate.None.All(IsWhole);
            default:
                return false;
        }
    }

    // The text of a word, the same for the same list of bits.
    private string WordOf(WordPlan plan)
    {
        var key = string.Join(",", plan.Positions);
        if (wordTexts.TryGetValue(key, out var known)) return known;
        var mask = LanesMask(plan.Positions.Length);
        string text;
        switch (plan)
        {
            case StoredWord stored:
                {
                    var shifted = stored.First == 0 ? stored.Word() : $"({stored.Word()} >> {WgslText.Uint(stored.First)})";
                    // A stored word holds 0 above its last lane.
                    text = stored.First + plan.Positions.Length == stored.Width ? shifted : $"({shifted} & {mask})";
                    break;
                }
            case SharedWord:
                {
                    // One let before the first stage, which each stage that reads the same cells as a word shares.
                    var cells = string.Join(",", plan.Positions.Select(position => ((CellSource.Shared)stage.Sources[position]).Offset));
                    var bits = plan.Positions.Select(BitText).ToList();
                    if (!state.SharedWords.TryGetValue(cells, out var name))
                    {
                        name = $"shw{Text(state.SharedWords.Count)}";
                        state.SharedWords[cells] = name;
                        state.Hoist(name, $"let {name}: u32 = {InLanes(bits)};");
                    }
                    text = name;
                    break;
                }
            case ReadsWord reads:
                text = SharedLet("u32", PackedWord(reads.Gated, reads.Reads));
                break;
            case NotWord not:
                state.LaneOperations += plan.Positions.Length;
                text = SharedLet("u32", $"{WordOf(not.Operand)} ^ {mask}");
                break;
            case GateWord gate:
                {
                    state.LaneOperations += plan.Positions.Length;
                    var zeros = gate.None.Select(WordOf).ToList();
                    var factors = gate.All.Select(WordOf).ToList();
                    if (zeros.Count > 0) factors.Add($"({(zeros.Count == 1 ? zeros[0] : $"({string.Join(" | ", zeros)})")} ^ {mask})");
                    text = factors.Count == 0 ? mask : SharedLet("u32", string.Join(" & ", factors));
                    break;
                }
            default:
                text = SharedLet("u32", InLanes([.. plan.Positions.Select(BitText)]));
                break;
        }
        wordTexts[key] = text;
        return text;
    }

    private string WordOfBits(IReadOnlyList<int> positions) => WordOf(SlotOf(positions));
}
