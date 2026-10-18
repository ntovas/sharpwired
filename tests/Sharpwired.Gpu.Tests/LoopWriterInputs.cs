// Handwired source: tests/json-search/bit-emit.test.ts (the programs "switches", "oneByOne", "mixed" and
// "declaredField") and src/json-search/search-step.ts (a search of two passes). This file holds the
// [Handwired] step methods and loop programs for the tests of the two loop writers (task 3 of the phase 6
// plan).
// - Switches: six switches that stay on from one row to the next. For each k, the form of on[k] &&
//   !blocked[k][row] && !stuck[k] is the same. It uses six registers of bits, six table reads at the same
//   row and six shared cells. So each of the three lists is one word (the Lanes rule), and the reads are one packed
//   word (the Packed reads rule).
// - OneByOne: switch k reads on[k + 1] && !on[k + 2], which are no neighbor lanes of a word.
// - Mixed: bits next to ordinary numbers. It has a sign cascade of two f32 cells, a whole streak and an f32
//   total from a constant table. It also has a whole bucket from a second constant table.
// - DeclaredField: a field that the loop program declares as bits, from a cell within 0..1 that is not a
//   bit. So nothing proves that the field holds bits.
// - Fma: x * 1.0000001 - 1.0000002 at each position, for the keeps of the f32 writer (the inputs near 1 of
//   WgslFormTests, as RoundingPointTests reads them).
// - Search8: the Reverse8 and Forward8 steps of LoopInputs as a search of two passes, with a field of 8 bits.
// - Delay and DelayInt: (x, a, b) with a and b registers. One register gets the old value of another
//   register (review of task 3: the kernel read the new value).
// - DelayBits: the same delay with four registers of bits in one word. The new word reads the old word
//   (the copy of a word of registers into a let).
// - Mul: x * y + 1.37, a ReGLU product that an affine row reads (the keep of the f32 writer).
// - SignField: a field of bits that holds -0, 0 and 1, read back as an output (the word of the signs).
// - OneHot: three registers in a one-hot group, which the item 77 breaks (the one-hot check of the kernel).
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

    // A product and a sum that a GPU compiler can contract to a fused multiply-add when no keep stops it.
    [Handwired]
    public static float Fma([Range(0.5, 2)] float x) => (x * 1.0000001f) - 1.0000002f;

    [Handwired]
    public static (float Kept, float Opposite) DeclaredKeep([Range(0, 1)] float value) => (value, 1f - value);

    [Handwired]
    public static (float X, float A, float B) Delay([Range(-1, 1000)] float x, [Range(-1, 1000)] float a, [Range(-1, 1000)] float b) => (x, a, b);

    [Handwired]
    public static (int X, int A, int B) DelayInt([Range(-1, 1000)] int x, [Range(-1, 1000)] int a, [Range(-1, 1000)] int b) => (x, a, b);

    [Handwired]
    public static (bool A0, bool A1, bool B0, bool B1, bool Out) DelayBits([Range(-1, 1000)] int item, bool a0, bool a1, bool b0, bool b1) =>
        (item > 500, item < 100, a0, a1, b0 || b1);

    [Handwired]
    public static float Mul([Range(-10, 10)] float x, [Range(0, 10)] float y) => (x * y) + 1.37f;

    [Handwired]
    public static float SignKeep([Range(0, 1)] float value) => value;

    [Handwired]
    public static float SignPass([Range(0, 1)] float kept) => kept;

    // The one-hot state moves one place at each position. The item 77 also sets bit 0, so the next state has two
    // bits. The group allows all zeros, because the initial source table of the translator holds zeros.
    [Handwired]
    public static (bool[] Next, bool First) OneHotRotate([OneHot(AllowAllZero = true), FixedLength(3)] bool[] state, [Range(-1, 1000)] int item)
    {
        var next = new bool[3];
        next[0] = state[2] || item == 77;
        next[1] = state[0];
        next[2] = state[1];
        return (next, next[0]);
    }

    [Handwired]
    public static (int Count, float Marked) DeclaredCount([Range(0, 64)] int count, [FixedLength(2)] bool[] kept) =>
        (count + (kept[0] ? 1 : 0), kept[1] ? 1f : 0f);
}

// The loop programs of LoopWriterInputs and their documents.
internal static class LoopWriterPrograms
{
    public const int Positions = 32;

    private static readonly float[] NearOne = WgslFormTests.NearOne();

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

    public static LoopProgram Fma() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [],
        [],
        [new LoopOutputGroup("product", OutputGroupKind.Position, Positions, 0)],
        [Stage("Fma", StageRepeat.Forward, Function(nameof(LoopWriterInputs.Fma)), [("x", new CellSource.Item(0))], [("", new StageWrite.Position(0))])],
        0);

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

    // a and b are registers 0 and 1. The stage writes x to a and the old a to b, and the old b goes out.
    public static LoopProgram Delay(bool whole) => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [new LoopRegister("a", 0), new LoopRegister("b", 0)],
        [],
        [new LoopOutputGroup("delayed", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Delay", StageRepeat.Forward, Function(whole ? nameof(LoopWriterInputs.DelayInt) : nameof(LoopWriterInputs.Delay)),
                [("x", new CellSource.Item(0)), ("a", new CellSource.Register(0)), ("b", new CellSource.Register(1))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Register(1)), ("item3", new StageWrite.Position(0))]),
        ],
        0);

    // a0, a1, b0 and b1 are registers of bits that one stage writes, so they are the lanes of one word. The stage
    // writes two comparisons to a0 and a1 and the old a0 and a1 to b0 and b1. The old b0 || b1 goes out. The new
    // word reads the old word.
    public static LoopProgram DelayBits() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [new LoopRegister("a0", 0), new LoopRegister("a1", 0), new LoopRegister("b0", 0), new LoopRegister("b1", 0)],
        [],
        [new LoopOutputGroup("delayed", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Delay", StageRepeat.Forward, Function(nameof(LoopWriterInputs.DelayBits)),
                [("item", new CellSource.Item(0)), ("a0", new CellSource.Register(0)), ("a1", new CellSource.Register(1)), ("b0", new CellSource.Register(2)), ("b1", new CellSource.Register(3))],
                [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Register(1)), ("item3", new StageWrite.Register(2)), ("item4", new StageWrite.Register(3)), ("item5", new StageWrite.Position(0))]),
        ],
        0);

    // x is the item at the position, and y the first item of the document.
    public static LoopProgram Mul() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [],
        [],
        [new LoopOutputGroup("product", OutputGroupKind.Position, Positions, 0)],
        [Stage("Mul", StageRepeat.Forward, Function(nameof(LoopWriterInputs.Mul)), [("x", new CellSource.Item(0)), ("y", new CellSource.DocumentCell(0, 0))], [("", new StageWrite.Position(0))])],
        0);

    public static LoopProgram SignField() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [],
        [new LoopField("kept", 1, true)],
        [new LoopOutputGroup("kept", OutputGroupKind.Position, Positions, 7)],
        [
            Stage("Keep", StageRepeat.Backward, Function(nameof(LoopWriterInputs.SignKeep)), [("value", new CellSource.Item(0))], [("", new StageWrite.Field(0, 0))]),
            Stage("Pass", StageRepeat.Forward, Function(nameof(LoopWriterInputs.SignPass)), [("kept", new CellSource.Field(0, 0))], [("", new StageWrite.Position(0))]),
        ],
        0);

    public static LoopProgram OneHot() => LoopProgram.Create(
        Positions,
        [Documents(Positions)],
        [new LoopRegister("state0", 1), new LoopRegister("state1", 0), new LoopRegister("state2", 0)],
        [],
        [new LoopOutputGroup("first", OutputGroupKind.Position, Positions, 0)],
        [
            Stage("Rotate", StageRepeat.Forward, Function(nameof(LoopWriterInputs.OneHotRotate)),
                [("state", new CellSource.Register(0)), ("item", new CellSource.Item(0))],
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
                    "Fma" => NearOne[((document * Positions) + position) % NearOne.Length],
                    "Mul" => (float)(random() * 10),
                    "SignField" => new[] { 0f, -0f, 1f }[(int)Math.Floor(random() * 3)],
                    "OneHot" => position < length ? (float)Math.Floor(random() * 77) : -1f,
                    "Search8" => position < length ? (random() < 0.1 ? (float)Math.Floor(random() * 0x110000) : 97 + (float)Math.Floor(random() * 5)) : -1f,
                    _ => position < length ? (float)Math.Floor(random() * 1000) : -1f,
                };
            }
            row[Positions] = length;
            if (name == "OneHot" && document == 7)
            {
                row[3] = 77;
                row[Positions] = Math.Max(length, 4);
            }
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
