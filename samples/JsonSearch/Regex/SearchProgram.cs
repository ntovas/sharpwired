// Handwired source: src/json-search/search-step.ts (buildSearchLoop: the loop program, writePattern, writeScope,
// patternCells) and src/json-search/regex/data-program.ts (writePattern). The masked search as a loop program over
// the 512 positions of a document, for one capacity.
//
// Buffer 0 holds the document cells (512 code points and the length), and buffer 1 the 512 display kinds of the
// highlighter. The registers are notFinish[S] (initial 1), reached[S] (initial 0) and matchCount (initial 0). The
// fields are codePointClass (width 1) and notReach (width S, bits). The stages:
// - Start runs one time and writes notFinish[s] = !accepting[s].
// - Reverse runs from position 511 down and writes the two fields and the next notFinish.
// - Forward runs from position 0 up and writes the mark of each position to the position group marks, and the
//   next reached and matchCount.
// - Finish runs one time and writes matchCount to the document group matchCount.
// The length cell is column 512 of buffer 0: with StopAtLength, the CPU loops stop at the end of the text. A
// cell after the text holds -1, whose class has no step, so the reverse bits stay what Start wrote, the forward
// pass finds no match there, and the outputs are the same.
//
// The shared cells, in this order: searchable[9], boundaries[32], regionClasses[34], transitions[17 × S × S]
// and accepting[S].

using System.Collections.Immutable;
using Sharpwired.Gpu;
using Sharpwired.IL;

namespace JsonSearch;

/// <summary>The display kinds that a search reads: all, the keys, or the string values with their quotes.</summary>
public enum SearchScope
{
    All,
    Keys,
    StringValues,
}

/// <summary>The search loop program of one capacity, and the writes of a pattern and a scope into its shared cells.</summary>
public sealed class SearchProgram
{
    private const int SearchableOffset = 0;
    private const int BoundariesOffset = SearchableOffset + SearchCells.KindCount;
    private const int RegionClassesOffset = BoundariesOffset + SearchCells.Boundaries;
    private const int TransitionsOffset = RegionClassesOffset + SearchCells.RegionCells;

    private SearchProgram(SearchCapacity capacity, LoopProgram loop)
    {
        Capacity = capacity;
        Loop = loop;
    }

    public SearchCapacity Capacity { get; }

    public LoopProgram Loop { get; }

    /// <summary>The number of states.</summary>
    public int States => Capacity.States;

    /// <summary>The shared offset of the accepting bits.</summary>
    private int AcceptingOffset => TransitionsOffset + (SearchCells.ClassSlots * States * States);

    /// <summary>
    /// The number of shared cells that a new pattern writes: the boundaries, the region classes, the transitions
    /// and the accepting bits.
    /// </summary>
    public int PatternCells => SearchCells.Boundaries + SearchCells.RegionCells + (SearchCells.ClassSlots * States * States) + States;

    /// <summary>The step methods of each capacity: Start, Reverse and Forward.</summary>
    public static Type StepClass(SearchCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return capacity switch
        {
            { States: 8, Classes: SearchCells.Classes, Boundaries: SearchCells.Boundaries } => typeof(Search8),
            { States: 16, Classes: SearchCells.Classes, Boundaries: SearchCells.Boundaries } => typeof(Search16),
            { States: 32, Classes: SearchCells.Classes, Boundaries: SearchCells.Boundaries } => typeof(Search32),
            _ => throw new ArgumentException($"no search program has {capacity.States} states, {capacity.Classes} classes and {capacity.Boundaries} boundaries; use SearchCapacity.All", nameof(capacity)),
        };
    }

    /// <summary>Translate the step methods of the capacity and compose them into the loop program.</summary>
    public static SearchProgram Build(SearchCapacity capacity)
    {
        var steps = StepClass(capacity);
        var states = capacity.States;
        var start = HandwiredTranslator.TranslateFunction(steps.GetMethod("Start")!);
        var reverse = HandwiredTranslator.TranslateFunction(steps.GetMethod("Reverse")!);
        var forward = HandwiredTranslator.TranslateFunction(steps.GetMethod("Forward")!);
        var finish = HandwiredTranslator.TranslateFunction(typeof(SearchFinish).GetMethod(nameof(SearchFinish.Finish))!);
        var acceptingOffset = TransitionsOffset + (SearchCells.ClassSlots * states * states);
        var notFinishRegister = 0;
        var reachedRegister = states;
        var matchCountRegister = 2 * states;
        const int ClassField = 0;
        const int NotReachField = 1;
        const int MatchCountGroup = 0;
        const int MarksGroup = 1;

        static ImmutableDictionary<string, T> Map<T>(params (string Path, T Value)[] entries) =>
            ImmutableDictionary.CreateRange(StringComparer.Ordinal, entries.Select(entry => KeyValuePair.Create(entry.Path, entry.Value)));

        var stages = new[]
        {
            new LoopStage(
                "Start", StageRepeat.Once, start,
                Map<CellSource>(("accepting", new CellSource.Shared(acceptingOffset))),
                Map<StageWrite>(("", new StageWrite.Register(notFinishRegister)))),
            new LoopStage(
                "Reverse", StageRepeat.Backward, reverse,
                Map<CellSource>(
                    ("codePoint", new CellSource.Item(0)),
                    ("kind", new CellSource.Item(1)),
                    ("searchable", new CellSource.Shared(SearchableOffset)),
                    ("boundaries", new CellSource.Shared(BoundariesOffset)),
                    ("regionClasses", new CellSource.Shared(RegionClassesOffset)),
                    ("transitions", new CellSource.Shared(TransitionsOffset)),
                    ("accepting", new CellSource.Shared(acceptingOffset)),
                    ("notFinish", new CellSource.Register(notFinishRegister))),
                Map<StageWrite>(
                    ("item1", new StageWrite.Field(ClassField, 0)),
                    ("item2", new StageWrite.Field(NotReachField, 0)),
                    ("item3", new StageWrite.Register(notFinishRegister)))),
            new LoopStage(
                "Forward", StageRepeat.Forward, forward,
                Map<CellSource>(
                    ("codePointClass", new CellSource.Field(ClassField, 0)),
                    ("notReach", new CellSource.Field(NotReachField, 0)),
                    ("reached", new CellSource.Register(reachedRegister)),
                    ("matchCount", new CellSource.Register(matchCountRegister)),
                    ("transitions", new CellSource.Shared(TransitionsOffset))),
                Map<StageWrite>(
                    ("item1", new StageWrite.Register(reachedRegister)),
                    ("item2", new StageWrite.Position(MarksGroup)),
                    ("item3", new StageWrite.Register(matchCountRegister)))),
            new LoopStage(
                "Finish", StageRepeat.Once, finish,
                Map<CellSource>(("matchCount", new CellSource.Register(matchCountRegister))),
                Map<StageWrite>(("", new StageWrite.Document(MatchCountGroup, 0)))),
        };

        var registers = Enumerable.Range(0, states).Select(state => new LoopRegister($"notFinish[{state}]", 1))
            .Concat(Enumerable.Range(0, states).Select(state => new LoopRegister($"reached[{state}]", 0)))
            .Append(new LoopRegister("matchCount", 0))
            .ToArray();
        var loop = LoopProgram.Create(
            SearchCells.Positions,
            [new LoopBuffer("documents", DocumentCells.CellsPerDocument), new LoopBuffer("kinds", SearchCells.Positions)],
            registers,
            [new LoopField("codePointClass", 1, false), new LoopField("notReach", states, true)],
            [
                new LoopOutputGroup("matchCount", OutputGroupKind.Document, 1, 0),
                new LoopOutputGroup("marks", OutputGroupKind.Position, SearchCells.Positions, 0),
            ],
            stages,
            acceptingOffset + states,
            new LengthCell(0, DocumentCells.Capacity));
        return new SearchProgram(capacity, loop);
    }

    /// <summary>
    /// The shared cells before a pattern and a scope: each kind searchable, each boundary 0x110000, each region
    /// class 16, no transition and no accepting state. This is the empty class [], which matches nothing.
    /// </summary>
    public float[] InitialShared()
    {
        var shared = new float[Loop.SharedSize];
        shared.AsSpan(SearchableOffset, SearchCells.KindCount).Fill(1);
        shared.AsSpan(BoundariesOffset, SearchCells.Boundaries).Fill(PatternCompiler.CodePointLimit);
        shared.AsSpan(RegionClassesOffset, SearchCells.RegionCells).Fill(SearchCells.Classes);
        return shared;
    }

    /// <summary>
    /// Write the cells of the compiled pattern (data-program.ts writePattern). Throws ArgumentException with the
    /// capacity messages when the pattern does not fit, and for a span of the wrong length.
    /// </summary>
    public void WritePattern(Span<float> shared, CompiledPattern compiled)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        CheckLength(shared);
        var errors = PatternCompiler.CapacityErrors(compiled, Capacity);
        if (!errors.IsEmpty) throw new ArgumentException(string.Join("; ", errors.Select(error => error.Message)), nameof(compiled));
        for (var index = 0; index < SearchCells.Boundaries; index++)
        {
            shared[BoundariesOffset + index] = index < compiled.Boundaries.Length ? compiled.Boundaries[index] : PatternCompiler.CodePointLimit;
        }
        // Cell 0 is for -1, after the text. Cell r is the class of region r - 1.
        shared[RegionClassesOffset] = SearchCells.Classes;
        for (var index = 1; index < SearchCells.RegionCells; index++)
        {
            var region = index - 1;
            var classIndex = region < compiled.RegionClasses.Length ? compiled.RegionClasses[region] : -1;
            shared[RegionClassesOffset + index] = classIndex == -1 ? SearchCells.Classes : classIndex;
        }
        var states = States;
        for (var from = 0; from < states; from++)
        {
            var targets = from < compiled.StateCount ? compiled.Targets[from] : [];
            for (var to = 0; to < states; to++)
            {
                for (var classIndex = 0; classIndex < SearchCells.ClassSlots; classIndex++)
                {
                    var reads = classIndex < compiled.ClassCount && to < compiled.StateCount && compiled.ClassReads[classIndex][to];
                    shared[TransitionsOffset + (((from * states) + to) * SearchCells.ClassSlots) + classIndex] = reads && targets.Contains(to) ? 1 : 0;
                }
            }
        }
        for (var state = 0; state < states; state++)
        {
            shared[AcceptingOffset + state] = state < compiled.StateCount && compiled.Accepting[state] ? 1 : 0;
        }
    }

    /// <summary>Write the 9 cells searchable[kind] of the scope (search-step.ts writeScope).</summary>
    public void WriteScope(Span<float> shared, SearchScope scope) => WriteKinds(shared, KindsOf(scope));

    /// <summary>Write the 9 cells searchable[kind]: 1 for each kind of the list, else 0.</summary>
    public void WriteKinds(Span<float> shared, IReadOnlyCollection<DisplayKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        CheckLength(shared);
        for (var kind = 0; kind < SearchCells.KindCount; kind++) shared[SearchableOffset + kind] = kinds.Contains((DisplayKind)kind) ? 1 : 0;
    }

    /// <summary>The display kinds of the scope (search-step.ts searchableKinds).</summary>
    public static IReadOnlyCollection<DisplayKind> KindsOf(SearchScope scope) => scope switch
    {
        SearchScope.All => Enum.GetValues<DisplayKind>(),
        SearchScope.Keys => [DisplayKind.Key],
        SearchScope.StringValues => [DisplayKind.String],
        _ => throw new ArgumentException($"unknown scope {scope}", nameof(scope)),
    };

    private void CheckLength(Span<float> shared)
    {
        if (shared.Length != Loop.SharedSize) throw new ArgumentException($"the shared table holds {shared.Length} cells, and the program needs {Loop.SharedSize}", nameof(shared));
    }
}
