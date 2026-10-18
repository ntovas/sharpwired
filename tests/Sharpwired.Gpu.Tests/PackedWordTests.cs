// Handwired source: tests/json-search/bit-emit.test.ts (the "flags" programs and their variants, the near
// misses that hold values that the rule would get wrong, and packWords). PackedWords.Pack gives the bits of
// the cells for 1, 31, 32 and 33 bits. The proof of the packed reads (StageRules) runs on the circuits of
// bit-emit.test.ts, which the builder makes directly: each read that the rule takes as a gated read is
// table[base] && open for each source table (a check against the reference evaluator over each combination
// of the gating bits), and each near miss is not a gated read. Two bits that are never both 1 are proven,
// and two free bits are not. These tests need no adapter.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed class PackedWordTests
{
    private const int RowCount = 5;

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void Pack_gives_the_bits_of_the_cells(int bits)
    {
        var random = Js.CreateRandom(0x9ac0 + bits);
        var shared = Enumerable.Range(0, bits + 3).Select(_ => random() < 0.3 ? 0f : random() < 0.5 ? 1f : -0f).ToArray();
        var cells = Enumerable.Range(0, bits).Select(bit => new PackedCell(bit / 32, bit % 32, bit + 3)).ToImmutableArray();
        // One more word with no cell, as the word of zeros past a table.
        var words = Enumerable.Repeat(0xffffffffu, (bits + 31) / 32 + 1).ToArray();
        PackedWords.Pack(cells, shared, words);
        for (var bit = 0; bit < bits; bit++)
        {
            Assert.Equal(shared[bit + 3] == 1 ? 1u : 0u, (words[bit / 32] >> (bit % 32)) & 1u);
        }
        Assert.Equal(0u, words[^1]);
        if (bits % 32 != 0) Assert.Equal(0u, words[bits / 32] >> (bits % 32));
        Assert.Throws<ArgumentException>(() => PackedWords.Pack([new PackedCell(words.Length, 0, 0)], shared, words));
        Assert.Throws<ArgumentException>(() => PackedWords.Pack([new PackedCell(0, 0, shared.Length)], shared, words));
    }

    // The variants of bit-emit.test.ts buildFlags. JS: none = !tables.some((flags, k) => wanted[k] && flags[row]).
    public static TheoryData<string> Variants => new()
    {
        "gated", "ungated", "manyTables", "zeroBelow", "exclusiveBits", "shortGate", "fractionInGate", "differentIndexes", "fractionAsBit", "twoFreeBits",
    };

    private sealed record Flags(Circuit Circuit, TensorProgram Program, Wire[] Reads, Wire None, int TableCount, int Row, int FlagsStart, int WantedStart, int P, int Q, int Fraction);

    private static Flags Build(string variant)
    {
        var tableCount = variant == "manyTables" ? 40 : 3;
        var lowestRow = variant is "zeroBelow" or "shortGate" ? -1 : 0;
        var gateWeight = variant == "zeroBelow" ? RowCount + 1 : RowCount;
        var size = 1 + (tableCount * RowCount) + tableCount + 3;
        var circuit = new Circuit(size);
        var row = circuit.Source(0);
        circuit.AssumeRange(row, lowestRow, RowCount);
        circuit.AssumeGrid(row, 1);
        var flagsStart = 1;
        var wantedStart = flagsStart + (tableCount * RowCount);
        var (p, q, fraction) = (circuit.Source(wantedStart + tableCount), circuit.Source(wantedStart + tableCount + 1), circuit.Source(wantedStart + tableCount + 2));
        for (var cell = flagsStart; cell < wantedStart; cell++) circuit.AssumeBit(circuit.Source(cell));
        for (var table = 0; table < tableCount; table++)
        {
            var wanted = circuit.Source(wantedStart + table);
            if (variant == "fractionAsBit") circuit.AssumeRange(wanted, 0, 1);
            else circuit.AssumeBit(wanted);
        }
        circuit.AssumeBit(p);
        circuit.AssumeBit(q);
        circuit.AssumeRange(fraction, 0, 1);
        var below = lowestRow < 0 ? OutOfRange.Zero : OutOfRange.Clamp;
        var reads = new Wire[tableCount];
        for (var table = 0; table < tableCount; table++)
        {
            var flags = Enumerable.Range(flagsStart + (table * RowCount), RowCount).Select(circuit.Source).ToArray();
            if (variant == "ungated")
            {
                reads[table] = circuit.IndexReadAffine([(row, 1)], 0, flags, OutOfRange.Clamp, OutOfRange.Clamp);
            }
            else if (table == 0 && variant is "exclusiveBits" or "twoFreeBits")
            {
                // p && !q and p && q, which are never both 1, or p and q, which can both be 1.
                var (first, second) = variant == "exclusiveBits"
                    ? (circuit.Reglu([], 1, [(p, 1), (q, -1)], 0), circuit.Reglu([], 1, [(p, 1), (q, 1)], -1))
                    : (p, q);
                reads[table] = circuit.IndexReadAffine([(row, 1), (first, -RowCount), (second, -RowCount)], RowCount, flags, below, OutOfRange.Zero);
            }
            else
            {
                // JS: wanted && flags[row], as a read at row + 5 × (1 - wanted), which is +0 past the table.
                var wanted = circuit.Source(wantedStart + table);
                reads[table] = circuit.IndexReadAffine([(row, 1), (wanted, -gateWeight)], gateWeight + (variant == "differentIndexes" ? table : 0), flags, below, OutOfRange.Zero);
            }
        }
        List<Term> gate = [.. reads.Select(read => new Term(read, -1))];
        if (variant == "fractionInGate") gate.Add(new Term(fraction, -1));
        var none = circuit.Reglu([], 1, gate, 1);
        var program = circuit.CompileTensorProgram([none, .. reads]);
        return new Flags(circuit, program, reads, none, tableCount, 0, flagsStart, wantedStart, p.Position, q.Position, fraction.Position);
    }

    private static int PositionOf(TensorProgram program, Wire wire) => Enumerable.Range(0, program.WireOrder.Count).First(position => program.WireOrder[position] == wire.Position);

    // A source table in the declared ranges: random flags, wanted cells (0, 0.5 or 1 for fractionAsBit), p, q, a
    // fraction and a row.
    private static float[] Source(Flags flags, Func<double> random, int row, int p, int q, string variant)
    {
        var source = new float[flags.Program.SourceSize];
        source[flags.Row] = row;
        for (var cell = flags.FlagsStart; cell < flags.WantedStart; cell++) source[cell] = random() < 0.4 ? 1 : 0;
        for (var table = 0; table < flags.TableCount; table++)
        {
            source[flags.WantedStart + table] = variant == "fractionAsBit" ? new[] { 0f, 0.5f, 1f }[(int)Math.Floor(random() * 3)] : random() < 0.5 ? 1 : 0;
        }
        source[flags.P] = p;
        source[flags.Q] = q;
        source[flags.Fraction] = new[] { 0f, 0.25f, 1f }[(int)Math.Floor(random() * 3)];
        return source;
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_gated_read_is_the_table_at_its_base_and_its_open_bit_and_a_near_miss_is_no_gated_read(string variant)
    {
        var flags = Build(variant);
        var rules = StageRules.For(flags.Circuit, flags.Program, cell => cell != flags.Row);
        var gated = flags.Reads.Select(read => rules.Gated((IndexRow)rules.RowAt(PositionOf(flags.Program, read))!)).ToArray();

        bool[] expected = variant switch
        {
            "shortGate" or "fractionAsBit" => [.. gated.Select(_ => false)],
            "twoFreeBits" => [.. gated.Select((_, table) => table != 0)],
            _ => [.. gated.Select(_ => true)],
        };
        Assert.Equal(expected, gated.Select(read => read is not null).ToArray());
        var keys = gated.OfType<GatedRead>().Select(read => read.Key).Distinct().Count();
        Assert.Equal(variant switch { "differentIndexes" => 3, "shortGate" or "fractionAsBit" => 0, _ => 1 }, keys);
        if (variant == "exclusiveBits") Assert.Equal(2, gated[0]!.Gating.Length);
        if (variant == "ungated") Assert.All(gated, read => Assert.Equal(read!.Every, read.Open));
        // The gate is exact only with whole operands, so a fraction in it leaves no Gate rule to pack.
        Assert.Equal(variant != "fractionInGate", rules.Conjunction(PositionOf(flags.Program, flags.None)) is not null);

        // Each gated read is table[base] && open for each combination of p and q and each row.
        var random = Js.CreateRandom(0x6a7e + variant.Length);
        var checkedValues = 0;
        for (var round = 0; round < 40; round++)
        {
            for (var row = variant is "zeroBelow" or "shortGate" ? -1 : 0; row <= RowCount; row++)
            {
                for (var bits = 0; bits < 4; bits++)
                {
                    var source = Source(flags, random, row, bits & 1, bits >> 1, variant);
                    var reference = flags.Circuit.EvaluateReference(source);
                    for (var table = 0; table < flags.Reads.Length; table++)
                    {
                        if (gated[table] is not { } read) continue;
                        var indexRow = (IndexRow)rules.RowAt(PositionOf(flags.Program, flags.Reads[table]))!;
                        Assert.True(Predicted(read, indexRow, flags.Program, reference) == reference[flags.Reads[table].Position], $"{variant}: table {table}, row {row}, p {bits & 1}, q {bits >> 1}");
                        checkedValues++;
                    }
                }
            }
        }
        Assert.True(checkedValues > 0 || expected.All(fires => !fires));
    }

    // table[baseBias + base] && open: the value that the packed word gives for one gated read.
    private static float Predicted(GatedRead read, IndexRow row, TensorProgram program, float[] reference)
    {
        double Value(int position) => reference[program.WireOrder[position]];
        var combination = 0;
        for (var bit = 0; bit < read.Gating.Length; bit++) combination |= (Value(read.Gating[bit]) == 1 ? 1 : 0) << bit;
        if (((read.Open >> combination) & 1) == 0) return 0;
        var index = read.BaseBias + read.BaseTerms.Sum(term => term.Weight * Value(term.Position));
        var last = row.Candidates.Count - 1;
        if (index < 0 && row.BelowZero) return 0;
        if (index > last && row.AboveZero) return 0;
        return (float)Value(row.Candidates.PositionAt((int)Math.Clamp(index, 0, last)));
    }

    // p && !q and p && q are never both 1, which the proof finds by taking both as 1. p and q can both be 1, and
    // the proof does not claim otherwise. The reference evaluator agrees on each of the four inputs.
    [Fact]
    public void Two_bits_that_are_never_both_one_are_proven_and_two_free_bits_are_not()
    {
        foreach (var variant in new[] { "exclusiveBits", "twoFreeBits" })
        {
            var flags = Build(variant);
            var rules = StageRules.For(flags.Circuit, flags.Program, cell => cell != flags.Row);
            var read = (IndexRow)rules.RowAt(PositionOf(flags.Program, flags.Reads[0]))!;
            var (first, second) = (read.IndexTerms[1].Position, read.IndexTerms[2].Position);
            var proven = rules.NeverBothOne(first, second);
            Assert.Equal(variant == "exclusiveBits", proven);
            var random = Js.CreateRandom(5);
            var bothOne = Enumerable.Range(0, 4).Any(bits =>
            {
                var reference = flags.Circuit.EvaluateReference(Source(flags, random, 0, bits & 1, bits >> 1, variant));
                return reference[flags.Program.WireOrder[first]] == 1 && reference[flags.Program.WireOrder[second]] == 1;
            });
            Assert.Equal(!proven, bothOne);
        }
    }
}
