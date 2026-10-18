// Handwired source: tests/json-search/bit-emit.test.ts (the programs "switches", "oneByOne", "mixed" and
// "declaredField") and src/json-search/search-step.ts (a search of two passes). [Handwired] step methods and
// loop programs for the tests of the two loop writers (task 3 of the phase 6 plan):
// - Switches: six switches that stay on from one row to the next. on[k] && !blocked[k][row] && !stuck[k]
//   has the same form for each k, over six registers of bits, six table reads at the same row and six
//   shared cells. So each of the three lists is one word (the Lanes rule), and the reads are one packed
//   word (the Packed reads rule).
// - OneByOne: switch k reads on[k + 1] && !on[k + 2], which are no neighbor lanes of a word.
// - Mixed: bits next to ordinary numbers: a sign cascade of two f32 cells, a whole streak, an f32 total from
//   a constant table, and a whole bucket from a second constant table.
// - DeclaredField: a field that the loop program declares as bits, from a cell within 0..1 that is not a
//   bit, so nothing proves that the field holds bits.
// - Search8: the Reverse8 and Forward8 steps of LoopInputs as a search of two passes, with a field of 8 bits.
// Each program has its own seeded documents, with each item inside the declared range of its cell.

using System.Collections.Immutable;
using Sharpwired.Annotations;
using Sharpwired.IL;
using static Sharpwired.Gpu.Tests.LoopInputPrograms;

namespace Sharpwired.Gpu.Tests;

public static class LoopWriterInputs
{
    public const int Rows = 5;
    public const int SwitchCount = 6;

    private static readonly float[] Weights = [0.5f, 1.25f, 3f, 7.75f];
    private static readonly int[] NextBuckets = [0, 1, 2, 3, 3];

    [Handwired]
    public static (bool[] On, int Count, bool Any) Switches(
        [Range(0, Rows - 1)] int row,
        [Range(0, 64)] int count,
        [FixedLength(SwitchCount)] bool[] on,
        [FixedLength(SwitchCount * Rows)] bool[] blocked,
        [FixedLength(SwitchCount)] bool[] stuck)
    {
        var next = new bool[SwitchCount];
        for (int k = 0; k < SwitchCount; k++)
        {
            next[k] = on[k] && !blocked[(k * Rows) + row] && !stuck[k];
        }

        bool any = next[0] || next[1] || next[2] || next[3] || next[4] || next[5];
        return (next, any ? count + 1 : count, any);
    }

    [Handwired]
    public static (bool[] On, int Count, bool Any) OneByOne([Range(0, 64)] int count, [FixedLength(SwitchCount)] bool[] on)
    {
        var next = new bool[SwitchCount];
        for (int k = 0; k < SwitchCount; k++)
        {
            next[k] = on[(k + 1) % SwitchCount] && !on[(k + 2) % SwitchCount];
        }

        bool any = next[0] || next[1] || next[2] || next[3] || next[4] || next[5];
        return (next, any ? count + 1 : count, any);
    }

    [Handwired]
    public static (int Streak, float Total, int Bucket, bool Over) Mixed(
        [Range(0, 100)] float value,
        [Range(0, 100)] float limit,
        [Range(0, 64)] int streak,
        [Range(0, 512)] float total,
        [Range(0, 3)] int bucket)
    {
        bool over = value > limit;
        int nextStreak = over ? streak + 1 : 0;
        float nextTotal = total + (over ? Weights[bucket] : 0f);
        int nextBucket = NextBuckets[bucket + (over ? 1 : 0)];
        return (nextStreak, nextTotal, nextBucket, over);
    }

    [Handwired]
    public static (float Kept, float Opposite) DeclaredKeep([Range(0, 1)] float value) => (value, 1f - value);

    [Handwired]
    public static (int Count, float Marked) DeclaredCount([Range(0, 64)] int count, [FixedLength(2)] bool[] kept) =>
        (count + (kept[0] ? 1 : 0), kept[1] ? 1f : 0f);
}

// The loop programs of LoopWriterInputs and their documents.
internal static class LoopWriterPrograms
{
    public const int Positions = 32;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TranslatedFunction> Functions = new();

    private static TranslatedFunction Function(string name) =>
        Functions.GetOrAdd(name, key => HandwiredTranslator.TranslateFunction(typeof(LoopWriterInputs).GetMethod(key)!));

    private static LoopStage Stage(string name, StageRepeat repeat, TranslatedFunction function, (string Path, CellSource Source)[] cells, (string Path, StageWrite Write)[] writes) =>
        new(name, repeat, function,
            ImmutableDictionary.CreateRange(cells.Select(cell => KeyValuePair.Create(cell.Path, cell.Source))),
            ImmutableDictionary.CreateRange(writes.Select(write => KeyValuePair.Create(write.Path, write.Write))));

    private static LoopRegister[] SwitchRegisters() =>
        [.. Enumerable.Range(0, LoopWriterInputs.SwitchCount).Select(index => new LoopRegister($"on{index}", 1)), new LoopRegister("count", 0)];

    // The shared cells: blocked at 0..29, stuck at 30..35.
    public static LoopProgram Switches() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        SwitchRegisters(),
        [],
        [new LoopOutputGroup("summary", OutputGroupKind.Document, 2, 0), new LoopOutputGroup("any", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Rows", StageRepeat.Forward, Function(nameof(LoopWriterInputs.Switches)),
                [("row", new CellSource.Item(0)), ("count", new CellSource.Register(LoopWriterInputs.SwitchCount)), ("on", new CellSource.Register(0)), ("blocked", new CellSource.Shared(0)), ("stuck", new CellSource.Shared(30))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Register(LoopWriterInputs.SwitchCount)), ("item3", new StageWrite.Position(1))]),
            Stage("End", StageRepeat.Once, LoopInputPrograms.Function(nameof(LoopInputs.LookupEnd)),
                [("above", new CellSource.Register(LoopWriterInputs.SwitchCount)), ("length", new CellSource.DocumentCell(0, Positions))],
                [("item1", new StageWrite.Document(0, 0)), ("item2", new StageWrite.Document(0, 1))]),
        ],
        36);

    public static LoopProgram OneByOne() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        SwitchRegisters(),
        [],
        [new LoopOutputGroup("any", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Rows", StageRepeat.Forward, Function(nameof(LoopWriterInputs.OneByOne)),
                [("count", new CellSource.Register(LoopWriterInputs.SwitchCount)), ("on", new CellSource.Register(0))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Register(LoopWriterInputs.SwitchCount)), ("item3", new StageWrite.Position(0))]),
        ],
        0);

    // The shared cell: limit at 0.
    public static LoopProgram Mixed() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [new LoopRegister("streak", 0), new LoopRegister("total", 0), new LoopRegister("bucket", 0)],
        [],
        [new LoopOutputGroup("over", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Values", StageRepeat.Forward, Function(nameof(LoopWriterInputs.Mixed)),
                [("value", new CellSource.Item(0)), ("limit", new CellSource.Shared(0)), ("streak", new CellSource.Register(0)), ("total", new CellSource.Register(1)), ("bucket", new CellSource.Register(2))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Register(1)), ("item3", new StageWrite.Register(2)), ("item4", new StageWrite.Position(0))]),
        ],
        1);

    public static LoopProgram DeclaredField() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [new LoopRegister("count", 0)],
        [new LoopField("kept", 2, true)],
        [new LoopOutputGroup("marked", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Keep", StageRepeat.Forward, Function(nameof(LoopWriterInputs.DeclaredKeep)),
                [("value", new CellSource.Item(0))],
                [("item1", new StageWrite.Field(0, 0)), ("item2", new StageWrite.Field(0, 1))]),
            Stage("Count", StageRepeat.Forward, Function(nameof(LoopWriterInputs.DeclaredCount)),
                [("count", new CellSource.Register(0)), ("kept", new CellSource.Field(0, 0))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Position(0))]),
        ],
        0);

    // The shared cells: boundaries at 0..31, region classes at 32..64, accepting at 65..72, transitions at 73..1160.
    public static LoopProgram Search8()
    {
        const int States = 8;
        const int Accepting = LoopInputs.Boundaries + LoopInputs.Boundaries + 1;
        const int Transitions = Accepting + States;
        return LoopProgram.Create(
            Positions,
            [Documents(Positions)],
            [.. Enumerable.Range(0, States).Select(state => new LoopRegister($"notFinish{state}", 1)),
             .. Enumerable.Range(0, States).Select(state => new LoopRegister($"reached{state}", 0)),
             new LoopRegister("matchCount", 0)],
            [new LoopField("class", 1, false), new LoopField("notReach", States, true)],
            [new LoopOutputGroup("matchCount", OutputGroupKind.Document, 1, 0), new LoopOutputGroup("marks", OutputGroupKind.Position, Positions, 0)],
            [
                LoopInputPrograms.Stage("Reverse", StageRepeat.Backward, nameof(LoopInputs.Reverse8),
                    [("codePoint", new CellSource.Item(0)), ("boundaries", new CellSource.Shared(0)), ("regionClasses", new CellSource.Shared(LoopInputs.Boundaries)),
                     ("notFinish", new CellSource.Register(0)), ("accepting", new CellSource.Shared(Accepting)), ("transitions", new CellSource.Shared(Transitions))],
                    [("item1", new StageWrite.Field(0, 0)), ("item2", new StageWrite.Field(1, 0)), ("item3", new StageWrite.Register(0))]),
                LoopInputPrograms.Stage("Forward", StageRepeat.Forward, nameof(LoopInputs.Forward8),
                    [("codePointClass", new CellSource.Field(0, 0)), ("notReach", new CellSource.Field(1, 0)), ("reached", new CellSource.Register(States)),
                     ("matchCount", new CellSource.Register(2 * States)), ("transitions", new CellSource.Shared(Transitions))],
                    [("item1", new StageWrite.Register(States)), ("item2", new StageWrite.Position(1)), ("item3", new StageWrite.Register(2 * States))]),
                LoopInputPrograms.Stage("Total", StageRepeat.Once, nameof(LoopInputs.SumTotal),
                    [("total", new CellSource.Register(2 * States))],
                    [("", new StageWrite.Document(0, 0))]),
            ],
            Transitions + (LoopInputs.ClassSlots * States * States));
    }

    // Seeded documents: each item in the declared range of its program, and the length at column Positions.
    public static float[] DocumentCells(string name, int count, int seed)
    {
        var random = Js.CreateRandom(seed);
        var cells = new float[count * (Positions + 1)];
        for (var document = 0; document < count; document++)
        {
            var row = cells.AsSpan(document * (Positions + 1), Positions + 1);
            var length = (int)Math.Floor(random() * (Positions + 1));
            for (var position = 0; position < Positions; position++)
            {
                row[position] = name switch
                {
                    "Switches" => (float)Math.Floor(random() * LoopWriterInputs.Rows),
                    "Mixed" => random() < 0.3 ? new[] { 0f, 12.5f, 12.25f, 50f, 50.5f, 99.75f, 100f }[(int)Math.Floor(random() * 7)] : MathF.Round((float)(random() * 100), 2),
                    "DeclaredField" => random() < 0.5 ? 0f : 1f,
                    "Search8" => position < length ? (random() < 0.1 ? (float)Math.Floor(random() * 0x110000) : 97 + (float)Math.Floor(random() * 5)) : -1f,
                    _ => position < length ? (float)Math.Floor(random() * 1000) : -1f,
                };
            }
            row[Positions] = length;
        }
        return cells;
    }

    // Seeded shared cells inside the declared ranges.
    public static float[] Shared(string name, int seed)
    {
        var random = Js.CreateRandom(seed);
        switch (name)
        {
            case "Switches":
                return [.. Enumerable.Range(0, 30).Select(_ => random() < 0.1 ? 1f : 0f), .. Enumerable.Range(0, 6).Select(_ => random() < 0.15 ? 1f : 0f)];
            case "Mixed":
                return [new[] { 0f, 12.5f, 50f, 99.75f }[(int)Math.Floor(random() * 4)]];
            case "Search8":
                {
                    var boundaries = Enumerable.Range(0, LoopInputs.Boundaries).Select(index => index < 6 ? 97f + index : 0x110000).ToArray();
                    var classes = Enumerable.Range(0, LoopInputs.Boundaries + 1).Select(_ => (float)Math.Floor(random() * 17)).ToArray();
                    var accepting = Enumerable.Range(0, 8).Select(_ => random() < 0.3 ? 1f : 0f);
                    var transitions = Enumerable.Range(0, LoopInputs.ClassSlots * 64).Select(_ => random() < 0.15 ? 1f : 0f);
                    return [.. boundaries, .. classes, .. accepting, .. transitions];
                }
            default:
                return [];
        }
    }
}
