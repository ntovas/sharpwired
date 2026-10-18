// Handwired source: src/json-search/loop-program.ts (the stages, registers, fields and outputs of a loop
// program). Also src/json-search/search-step.ts (the reverse and forward steps, for the sizes of the
// speed test). Small [Handwired] step methods for the tests of task 2 of the phase 6 plan.
// - Sum: a sum over the positions in one register, and a stage that runs once and writes the sum.
// - Leaders: a backward stage writes a field of the largest item from each position to the end, and
//   a field of two bits. A forward stage reads both fields and counts the items that equal that
//   largest item.
// - Lookup: a position output from a shared table. A stage that runs once writes two document outputs
//   from a register and the length cell.
// - Overflow: the register passes its declared range at the position of the item 77.
// - Reverse8, Reverse16, Reverse32, Forward8, Forward16, Forward32: steps of the shape of search-step.ts.
//   The speed tests use them to compare CompiledStage with TensorEvaluator at the step sizes of the plan.
// An item past the end of the text is -1. Each step keeps its registers and writes the fill value of
// its position outputs at -1, so StopAtLength gives the same outputs.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Sharpwired.Annotations;
using Sharpwired.IL;

namespace Sharpwired.Gpu.Tests;

public static class LoopInputs
{
    public const int ItemLimit = 1000;
    public const int TableLength = 8;
    public const int ClassSlots = 17;
    public const int Boundaries = 32;

    [Handwired]
    public static int SumStep([Range(0, 1000000)] int total, [Range(-1, ItemLimit)] int item) => item < 0 ? total : total + item;

    [Handwired]
    public static int SumTotal([Range(0, 1000000)] int total) => total + 0;

    // The largest item from this position to the end, as the register and as the field, and two bits
    // of the item.
    [Handwired]
    public static (int Largest, int Field, bool[] Flags) LeadersBackward([Range(-1, ItemLimit)] int largest, [Range(-1, ItemLimit)] int item)
    {
        int next = item > largest ? item : largest;
        var flags = new bool[2];
        flags[0] = item >= 0 && item < 500;
        flags[1] = item > 900;
        return (next, next, flags);
    }

    // A leader is an item that equals the largest item from its position to the end.
    [Handwired]
    public static (int Count, int Leader, int Weighted) LeadersForward(
        [Range(0, 4096)] int count,
        [Range(-1, ItemLimit)] int item,
        [Range(-1, ItemLimit)] int largest,
        [FixedLength(2)] bool[] flags)
    {
        bool leader = item >= 0 && item == largest;
        int next = count;
        if (leader) next = next + 1;
        int weighted = (flags[0] ? 1 : 0) + (flags[1] ? 2 : 0);
        return (next, leader ? 1 : 0, item < 0 ? 0 : weighted);
    }

    [Handwired]
    public static int LeadersTotal([Range(0, 4096)] int count) => count * 2;

    // The kind of each position from a shared table, and the count of items above a shared threshold.
    [Handwired]
    public static (int Above, float Kind) LookupStep(
        [Range(0, 4096)] int above,
        [Range(-1, ItemLimit)] int item,
        [Range(0, ItemLimit)] int threshold,
        [FixedLength(TableLength), Range(-100, 100)] float[] table)
    {
        int next = item > threshold ? above + 1 : above;
        float kind = item < 0 ? 9f : table[Math.Clamp(item, 0, TableLength - 1)];
        return (next, kind);
    }

    [Handwired]
    public static (int Above, int Length) LookupEnd([Range(0, 4096)] int above, [Range(0, 1048576)] int length) => (above, length * 2);

    // The item 77 sets the register to 100, past its declared range 0..50.
    [Handwired]
    public static int OverflowStep([Range(0, 50)] int count, [Range(-1, ItemLimit)] int item) =>
        item == 77 ? 100 : item == 5 ? (count < 50 ? count + 1 : count) : count;

    [Handwired]
    public static int OverflowEnd([Range(0, 50)] int count) => count + 0;

    // A loop with a maximum of 2 iterations and an overflow bit. LoopProgram.Create refuses it.
    [Handwired, MaxIterations(2)]
    public static float Halving([Range(0, 100)] float x)
    {
        while (x > 1f) x = x * 0.5f;
        return x;
    }

    [Handwired]
    public static (int Class, bool[] NotReach, bool[] NotFinish) Reverse8(
        [Range(-1, 0x10FFFF)] int codePoint,
        [FixedLength(Boundaries), Range(0, 0x110000)] int[] boundaries,
        [FixedLength(Boundaries + 1), Range(0, 16)] int[] regionClasses,
        [FixedLength(8)] bool[] notFinish,
        [FixedLength(8)] bool[] accepting,
        [FixedLength(ClassSlots * 8 * 8)] bool[] transitions)
    {
        const int States = 8;
        int region = 0;
        for (int b = 0; b < Boundaries; b++)
        {
            if (codePoint >= boundaries[b]) region = region + 1;
        }

        int codePointClass = regionClasses[region];
        var notReach = new bool[States];
        var nextNotFinish = new bool[States];
        for (int from = 0; from < States; from++)
        {
            bool any = false;
            for (int to = 0; to < States; to++)
            {
                any = any || (!notFinish[to] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            notReach[from] = !any;
            nextNotFinish[from] = !any && !accepting[from];
        }

        return (codePointClass, notReach, nextNotFinish);
    }

    [Handwired]
    public static (int Class, bool[] NotReach, bool[] NotFinish) Reverse16(
        [Range(-1, 0x10FFFF)] int codePoint,
        [FixedLength(Boundaries), Range(0, 0x110000)] int[] boundaries,
        [FixedLength(Boundaries + 1), Range(0, 16)] int[] regionClasses,
        [FixedLength(16)] bool[] notFinish,
        [FixedLength(16)] bool[] accepting,
        [FixedLength(ClassSlots * 16 * 16)] bool[] transitions)
    {
        const int States = 16;
        int region = 0;
        for (int b = 0; b < Boundaries; b++)
        {
            if (codePoint >= boundaries[b]) region = region + 1;
        }

        int codePointClass = regionClasses[region];
        var notReach = new bool[States];
        var nextNotFinish = new bool[States];
        for (int from = 0; from < States; from++)
        {
            bool any = false;
            for (int to = 0; to < States; to++)
            {
                any = any || (!notFinish[to] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            notReach[from] = !any;
            nextNotFinish[from] = !any && !accepting[from];
        }

        return (codePointClass, notReach, nextNotFinish);
    }

    [Handwired]
    public static (int Class, bool[] NotReach, bool[] NotFinish) Reverse32(
        [Range(-1, 0x10FFFF)] int codePoint,
        [FixedLength(Boundaries), Range(0, 0x110000)] int[] boundaries,
        [FixedLength(Boundaries + 1), Range(0, 16)] int[] regionClasses,
        [FixedLength(32)] bool[] notFinish,
        [FixedLength(32)] bool[] accepting,
        [FixedLength(ClassSlots * 32 * 32)] bool[] transitions)
    {
        const int States = 32;
        int region = 0;
        for (int b = 0; b < Boundaries; b++)
        {
            if (codePoint >= boundaries[b]) region = region + 1;
        }

        int codePointClass = regionClasses[region];
        var notReach = new bool[States];
        var nextNotFinish = new bool[States];
        for (int from = 0; from < States; from++)
        {
            bool any = false;
            for (int to = 0; to < States; to++)
            {
                any = any || (!notFinish[to] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            notReach[from] = !any;
            nextNotFinish[from] = !any && !accepting[from];
        }

        return (codePointClass, notReach, nextNotFinish);
    }

    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward8(
        [Range(0, 16)] int codePointClass,
        [FixedLength(8)] bool[] notReach,
        [FixedLength(8)] bool[] reached,
        [Range(0, 512)] int matchCount,
        [FixedLength(ClassSlots * 8 * 8)] bool[] transitions)
    {
        const int States = 8;
        bool anyContinuing = false;
        var continuing = new bool[States];
        for (int s = 0; s < States; s++)
        {
            continuing[s] = reached[s] && !notReach[s];
            anyContinuing = anyContinuing || continuing[s];
        }

        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing ^ start;
        var live = new bool[States];
        for (int s = 0; s < States; s++)
        {
            live[s] = s == 0 ? continuing[s] ^ start : continuing[s];
        }

        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        var next = new bool[States];
        for (int to = 0; to < States; to++)
        {
            bool any = false;
            for (int from = 0; from < States; from++)
            {
                any = any || (live[from] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            next[to] = any;
        }

        int nextCount = matchCount;
        if (start) nextCount = nextCount + 1;
        return (next, mark, nextCount);
    }

    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward16(
        [Range(0, 16)] int codePointClass,
        [FixedLength(16)] bool[] notReach,
        [FixedLength(16)] bool[] reached,
        [Range(0, 512)] int matchCount,
        [FixedLength(ClassSlots * 16 * 16)] bool[] transitions)
    {
        const int States = 16;
        bool anyContinuing = false;
        var continuing = new bool[States];
        for (int s = 0; s < States; s++)
        {
            continuing[s] = reached[s] && !notReach[s];
            anyContinuing = anyContinuing || continuing[s];
        }

        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing ^ start;
        var live = new bool[States];
        for (int s = 0; s < States; s++)
        {
            live[s] = s == 0 ? continuing[s] ^ start : continuing[s];
        }

        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        var next = new bool[States];
        for (int to = 0; to < States; to++)
        {
            bool any = false;
            for (int from = 0; from < States; from++)
            {
                any = any || (live[from] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            next[to] = any;
        }

        int nextCount = matchCount;
        if (start) nextCount = nextCount + 1;
        return (next, mark, nextCount);
    }

    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward32(
        [Range(0, 16)] int codePointClass,
        [FixedLength(32)] bool[] notReach,
        [FixedLength(32)] bool[] reached,
        [Range(0, 512)] int matchCount,
        [FixedLength(ClassSlots * 32 * 32)] bool[] transitions)
    {
        const int States = 32;
        bool anyContinuing = false;
        var continuing = new bool[States];
        for (int s = 0; s < States; s++)
        {
            continuing[s] = reached[s] && !notReach[s];
            anyContinuing = anyContinuing || continuing[s];
        }

        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing ^ start;
        var live = new bool[States];
        for (int s = 0; s < States; s++)
        {
            live[s] = s == 0 ? continuing[s] ^ start : continuing[s];
        }

        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        var next = new bool[States];
        for (int to = 0; to < States; to++)
        {
            bool any = false;
            for (int from = 0; from < States; from++)
            {
                any = any || (live[from] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            next[to] = any;
        }

        int nextCount = matchCount;
        if (start) nextCount = nextCount + 1;
        return (next, mark, nextCount);
    }
}

// The loop programs of the step methods of LoopInputs, and seeded documents for them. Buffer 0 holds
// the items at columns 0..positions - 1 and the length of the text at column positions.
internal static class LoopInputPrograms
{
    private static readonly ConcurrentDictionary<string, TranslatedFunction> Functions = new();

    public static TranslatedFunction Function(string name) =>
        Functions.GetOrAdd(name, key => HandwiredTranslator.TranslateFunction(typeof(LoopInputs).GetMethod(key)!));

    public static LoopStage Stage(string name, StageRepeat repeat, string method, (string Path, CellSource Source)[] cells, (string Path, StageWrite Write)[] writes) =>
        new(name, repeat, Function(method),
            ImmutableDictionary.CreateRange(cells.Select(cell => KeyValuePair.Create(cell.Path, cell.Source))),
            ImmutableDictionary.CreateRange(writes.Select(write => KeyValuePair.Create(write.Path, write.Write))));

    public static LoopBuffer Documents(int positions) => new("documents", positions + 1);

    public static LoopStage SumStage() => Stage("Sum", StageRepeat.Forward, nameof(LoopInputs.SumStep),
        [("total", new CellSource.Register(0)), ("item", new CellSource.Item(0))],
        [("", new StageWrite.Register(0))]);

    public static LoopStage SumTotalStage() => Stage("Total", StageRepeat.Once, nameof(LoopInputs.SumTotal),
        [("total", new CellSource.Register(0))],
        [("", new StageWrite.Document(0, 0))]);

    public static LoopProgram Sum(int positions) => LoopProgram.Create(
        positions,
        [Documents(positions)],
        [new LoopRegister("total", 0)],
        [],
        [new LoopOutputGroup("total", OutputGroupKind.Document, 1, 0)],
        [SumStage(), SumTotalStage()],
        0,
        new LengthCell(0, positions));

    public static LoopStage LeadersBackwardStage() => Stage("Backward", StageRepeat.Backward, nameof(LoopInputs.LeadersBackward),
        [("largest", new CellSource.Register(0)), ("item", new CellSource.Item(0))],
        [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Field(0, 0)), ("item3", new StageWrite.Field(1, 0))]);

    public static LoopStage LeadersForwardStage() => Stage("Forward", StageRepeat.Forward, nameof(LoopInputs.LeadersForward),
        [("count", new CellSource.Register(1)), ("item", new CellSource.Item(0)), ("largest", new CellSource.Field(0, 0)), ("flags", new CellSource.Field(1, 0))],
        [("item1", new StageWrite.Register(1)), ("item2", new StageWrite.Position(1)), ("item3", new StageWrite.Position(2))]);

    public static LoopStage LeadersTotalStage() => Stage("Total", StageRepeat.Once, nameof(LoopInputs.LeadersTotal),
        [("count", new CellSource.Register(1))],
        [("", new StageWrite.Document(0, 0))]);

    public static LoopProgram Leaders(int positions) => LoopProgram.Create(
        positions,
        [Documents(positions)],
        LeadersRegisters(),
        LeadersFields(),
        LeadersGroups(positions),
        [LeadersBackwardStage(), LeadersForwardStage(), LeadersTotalStage()],
        0,
        new LengthCell(0, positions));

    public static LoopRegister[] LeadersRegisters() => [new LoopRegister("largest", -1), new LoopRegister("count", 0)];

    public static LoopField[] LeadersFields() => [new LoopField("largest", 1, false), new LoopField("flags", 2, true)];

    public static LoopOutputGroup[] LeadersGroups(int positions) =>
    [
        new LoopOutputGroup("count", OutputGroupKind.Document, 1, 0),
        new LoopOutputGroup("leaders", OutputGroupKind.Position, positions, 0),
        new LoopOutputGroup("weighted", OutputGroupKind.Position, positions, 0),
    ];

    // The shared cells: the threshold at offset 0, and the table at offsets 1..8.
    public static LoopProgram Lookup(int positions) => LoopProgram.Create(
        positions,
        [Documents(positions)],
        [new LoopRegister("above", 0)],
        [],
        [new LoopOutputGroup("kinds", OutputGroupKind.Position, positions, 9), new LoopOutputGroup("summary", OutputGroupKind.Document, 2, 0)],
        [LookupStage(), LookupEndStage(positions)],
        1 + LoopInputs.TableLength,
        new LengthCell(0, positions));

    public static LoopStage LookupStage() => Stage("Step", StageRepeat.Forward, nameof(LoopInputs.LookupStep),
        [("above", new CellSource.Register(0)), ("item", new CellSource.Item(0)), ("threshold", new CellSource.Shared(0)), ("table", new CellSource.Shared(1))],
        [("item1", new StageWrite.Register(0)), ("item2", new StageWrite.Position(0))]);

    public static LoopStage LookupEndStage(int positions) => Stage("End", StageRepeat.Once, nameof(LoopInputs.LookupEnd),
        [("above", new CellSource.Register(0)), ("length", new CellSource.DocumentCell(0, positions))],
        [("item1", new StageWrite.Document(1, 0)), ("item2", new StageWrite.Document(1, 1))]);

    public static float[] LookupShared() => [400, -3.5f, 0, -0f, 7.25f, 100, -100, 1, 2];

    public static LoopProgram Overflow(int positions) => LoopProgram.Create(
        positions,
        [Documents(positions)],
        [new LoopRegister("count", 0)],
        [],
        [new LoopOutputGroup("count", OutputGroupKind.Document, 1, 0)],
        [
            Stage("Step", StageRepeat.Forward, nameof(LoopInputs.OverflowStep),
                [("count", new CellSource.Register(0)), ("item", new CellSource.Item(0))],
                [("", new StageWrite.Register(0))]),
            Stage("End", StageRepeat.Once, nameof(LoopInputs.OverflowEnd),
                [("count", new CellSource.Register(0))],
                [("", new StageWrite.Document(0, 0))]),
        ],
        0,
        new LengthCell(0, positions));

    // Seeded documents: a length of 0..positions + 3, items 0..1000 before the end and -1 after it. The
    // item 5 occurs often. The item 77 occurs only at position 3 of the document overflowAt, when that
    // argument is 0 or more.
    public static float[] CreateDocuments(int count, int positions, int seed, int overflowAt = -1)
    {
        var random = Js.CreateRandom(seed);
        var cells = new float[count * (positions + 1)];
        for (var document = 0; document < count; document++)
        {
            var row = cells.AsSpan(document * (positions + 1), positions + 1);
            var length = document == overflowAt ? positions : (int)Math.Floor(random() * (positions + 4));
            for (var position = 0; position < positions; position++)
            {
                var item = random() < 0.2 ? 5 : (int)Math.Floor(random() * (LoopInputs.ItemLimit + 1));
                if (item == 77) item = 78;
                row[position] = position < length ? item : -1;
            }
            if (document == overflowAt) row[3] = 77;
            row[positions] = length;
        }
        return cells;
    }
}
