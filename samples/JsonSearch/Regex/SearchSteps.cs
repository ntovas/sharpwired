// Handwired source: src/json-search/search-step.ts (the stages start, reverse and forward), with
// src/json-search/regex/data-program.ts (emitClasses, createStepAnd) and src/json-search/regex/bits.ts. The regex
// search as the work of one code point. The pattern is data in shared cells (SearchProgram.WritePattern), so a new
// pattern writes cells and changes no program.
//
// The search runs two passes over the code points of a document. Each step multiplies the transition matrix of
// the class of the code point with a 0/1 state vector:
// - The reverse pass, from the end of the text: reach[s] at position j is 1 when a path from state s at j reads
//   at least one code point and ends in an accepting state. The stage carries notFinish[t] = !(accepting[t] ||
//   reach[t]) of the position after j, and writes the class and notReach[s] = !reach[s] of j to two fields.
// - The forward pass, from the start: reached[s] is 1 when the match in progress has reached state s. Code point j
//   continues the match when reached[s] && reach[s] for a state s. When no match continues, a match starts at j
//   when reach[0]. So the forward pass knows at each position whether a longer match exists, and never goes back.
//
// The stages of SearchProgram: Start runs one time (notFinish[s] = !accepting[s], the bits before the last code
// point), Reverse runs from position 511 down, Forward from position 0 up, and SearchFinish.Finish runs one time
// and writes the match count.
//
// The masked search: Reverse also reads the display kind of the code point, which the highlighter wrote, and the
// 9 cells searchable[kind]. A code point of a kind that the search does not read becomes -1, the value of a cell
// after the text, which no step reads. So a match lies inside one run of searched characters.
//
// The three classes Search8, Search16 and Search32 differ only in the constant S. SearchTests reads the three
// method bodies through the IL and asserts that they are equal apart from the constants.
//
// The cells of the pattern (data-program.ts dataSearchCells):
// - boundaries[k]: where the class changes, ascending. An unused boundary holds 0x110000.
// - regionClasses[r]: the class of the code points that reach r - 1 boundaries. Cell 0 is for -1 (after the
//   text) and holds 16. A region that no step reads holds 16, one past the last class.
// - transitions[((from * S) + to) * 17 + class]: 1 when a step from state from reads a code point of the class
//   and goes to state to. Class 16 is the 17th class, and its cells hold 0, so a code point of class 16 has no
//   step (Handwired reads +0 past the table with the zero rule).
// - accepting[s].
//
// The C# forms. With TranslationOptions.RecognizePatterns, each form below gives the Handwired operations:
// - bit && transitions[...] is one index read with the zero rule (data-program.ts createStepAnd; plan, task 1,
//   rules 4 and 5).
// - An OR into an array element whose index is the counter of the outer loop, for example reach[from] =
//   reach[from] | (...), stays one open OR, and !reach[from] is one ReLU (bits.ts noneOf). With a local
//   accumulator or a constant index, the translator takes the OR as a running total and gives a prefix network
//   of ORs (RunningTotals.cs).
// - A + into an array element, for example region[0] = region[0] + (...), is one wide sum.
// - (b ? x + 1 : 0) - 1 is one product and one sum (search-step.ts emitMaskedCodePoint).
//
// Differences from Handwired:
// - Handwired holds 16 classes for each pair (from, to), and reads +0 for class 16 past the table. Here each pair
//   has 17 cells, and the host writes 0 in the 17th (plan, question 1, item 5).
// - Cell 0 of regionClasses is a constant cell in Handwired. Here it is a shared cell that the host writes 16
//   into (plan, question 1, item 4).
// - Start is in each capacity class, because it depends on S. The match count goes to a document output through
//   the stage SearchFinish.Finish, because a Sharpwired loop program writes document outputs only from a stage
//   that runs one time (LoopProgram.cs). Handwired writes the register as a result after the last stage.
// - Forward: "a match continues" is a sum of the continuing bits and a comparison of 2 operations, 3 operations
//   in place of the one noneOf of Handwired. The OR of the continuing bits has no outer loop, so the open OR
//   form above does not apply (SearchTests.Sizes.cs pins the counts).

using Sharpwired.Annotations;

namespace JsonSearch;

/// <summary>The sizes that the three search programs share.</summary>
public static class SearchCells
{
    /// <summary>The character classes of each program.</summary>
    public const int Classes = 16;

    /// <summary>The classes of each pair (from, to) of the transition table: the 16 classes and the class that reads 0.</summary>
    public const int ClassSlots = Classes + 1;

    /// <summary>The class boundaries of each program.</summary>
    public const int Boundaries = 32;

    /// <summary>The cells of regionClasses: the cell for -1, and one cell for each of the 33 regions.</summary>
    public const int RegionCells = Boundaries + 2;

    /// <summary>The number of display kinds.</summary>
    public const int KindCount = 9;

    /// <summary>The positions of a document.</summary>
    public const int Positions = Kinds.Capacity;
}

/// <summary>The stage that writes the match count of the document.</summary>
public static class SearchFinish
{
    /// <summary>The match count of the document, after the last position of the forward pass.</summary>
    [Handwired]
    public static int Finish([Range(0, SearchCells.Positions)] int matchCount) => matchCount;
}

/// <summary>The search steps at 8 states.</summary>
public static class Search8
{
    /// <summary>The number of states.</summary>
    public const int S = 8;

    /// <summary>notFinish[s] = !accepting[s]: the bits of the reverse pass before the last code point.</summary>
    [Handwired]
    public static bool[] Start([FixedLength(S)] bool[] accepting)
    {
        var notFinish = new bool[S];
        for (int s = 0; s < S; s++) notFinish[s] = !accepting[s];
        return notFinish;
    }

    /// <summary>One position of the reverse pass: the class of the code point, notReach[s], and the next notFinish[s].</summary>
    [Handwired]
    public static (int CodePointClass, bool[] NotReach, bool[] NotFinish) Reverse(
        [Range(-1, PatternCompiler.MaxCodePoint)] int codePoint,
        [Range(0, SearchCells.KindCount - 1)] int kind,
        [FixedLength(SearchCells.KindCount)] bool[] searchable,
        [FixedLength(SearchCells.Boundaries), Range(1, PatternCompiler.CodePointLimit)] int[] boundaries,
        [FixedLength(SearchCells.RegionCells), Range(0, SearchCells.Classes)] int[] regionClasses,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions,
        [FixedLength(S)] bool[] accepting,
        [FixedLength(S)] bool[] notFinish)
    {
        // JS: searched = searchable[kind] ? codePoint : -1
        int searched = (searchable[kind] ? codePoint + 1 : 0) - 1;
        // JS: regionClasses[[searched >= 0] + boundaries.filter(boundary => searched >= boundary).length]
        var region = new int[1];
        region[0] = searched >= 0 ? 1 : 0;
        for (int k = 0; k < SearchCells.Boundaries; k++) region[0] = region[0] + (searched >= boundaries[k] ? 1 : 0);
        int codePointClass = regionClasses[region[0]];
        // JS: reach[s] = states.some(t => transitions[class][s][t] && (accepting[t] || reachAfter[t]))
        var reach = new bool[S];
        var notReach = new bool[S];
        var nextNotFinish = new bool[S];
        for (int from = 0; from < S; from++)
        {
            for (int to = 0; to < S; to++) reach[from] = reach[from] | (!notFinish[to] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
            notReach[from] = !reach[from];
            nextNotFinish[from] = !reach[from] && !accepting[from];
        }
        return (codePointClass, notReach, nextNotFinish);
    }

    /// <summary>
    /// One position of the forward pass: the next reached[s], the mark (0 outside a match, 1 inside one, 2 at the
    /// first code point of a match), and the next match count.
    /// </summary>
    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward(
        [Range(0, SearchCells.Classes)] int codePointClass,
        [FixedLength(S)] bool[] notReach,
        [FixedLength(S)] bool[] reached,
        [Range(0, SearchCells.Positions)] int matchCount,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions)
    {
        // JS: continuing[s] = reached[s] && reach[s]
        var continuing = new bool[S];
        for (int s = 0; s < S; s++) continuing[s] = reached[s] && !notReach[s];
        var continuingCount = new int[1];
        for (int s = 0; s < S; s++) continuingCount[0] = continuingCount[0] + (continuing[s] ? 1 : 0);
        bool anyContinuing = continuingCount[0] > 0;
        // JS: start = !continuing.some(Boolean) && reach[0]
        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing | start;
        var live = new bool[S];
        for (int s = 0; s < S; s++) live[s] = s == 0 ? continuing[s] | start : continuing[s];
        // JS: reached[t] = states.some(s => live[s] && transitions[class][s][t])
        var nextReached = new bool[S];
        for (int to = 0; to < S; to++)
        {
            for (int from = 0; from < S; from++) nextReached[to] = nextReached[to] | (live[from] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
        }
        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        return (nextReached, mark, matchCount + (start ? 1 : 0));
    }
}

/// <summary>The search steps at 16 states.</summary>
public static class Search16
{
    /// <summary>The number of states.</summary>
    public const int S = 16;

    /// <summary>notFinish[s] = !accepting[s]: the bits of the reverse pass before the last code point.</summary>
    [Handwired]
    public static bool[] Start([FixedLength(S)] bool[] accepting)
    {
        var notFinish = new bool[S];
        for (int s = 0; s < S; s++) notFinish[s] = !accepting[s];
        return notFinish;
    }

    /// <summary>One position of the reverse pass: the class of the code point, notReach[s], and the next notFinish[s].</summary>
    [Handwired]
    public static (int CodePointClass, bool[] NotReach, bool[] NotFinish) Reverse(
        [Range(-1, PatternCompiler.MaxCodePoint)] int codePoint,
        [Range(0, SearchCells.KindCount - 1)] int kind,
        [FixedLength(SearchCells.KindCount)] bool[] searchable,
        [FixedLength(SearchCells.Boundaries), Range(1, PatternCompiler.CodePointLimit)] int[] boundaries,
        [FixedLength(SearchCells.RegionCells), Range(0, SearchCells.Classes)] int[] regionClasses,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions,
        [FixedLength(S)] bool[] accepting,
        [FixedLength(S)] bool[] notFinish)
    {
        // JS: searched = searchable[kind] ? codePoint : -1
        int searched = (searchable[kind] ? codePoint + 1 : 0) - 1;
        // JS: regionClasses[[searched >= 0] + boundaries.filter(boundary => searched >= boundary).length]
        var region = new int[1];
        region[0] = searched >= 0 ? 1 : 0;
        for (int k = 0; k < SearchCells.Boundaries; k++) region[0] = region[0] + (searched >= boundaries[k] ? 1 : 0);
        int codePointClass = regionClasses[region[0]];
        // JS: reach[s] = states.some(t => transitions[class][s][t] && (accepting[t] || reachAfter[t]))
        var reach = new bool[S];
        var notReach = new bool[S];
        var nextNotFinish = new bool[S];
        for (int from = 0; from < S; from++)
        {
            for (int to = 0; to < S; to++) reach[from] = reach[from] | (!notFinish[to] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
            notReach[from] = !reach[from];
            nextNotFinish[from] = !reach[from] && !accepting[from];
        }
        return (codePointClass, notReach, nextNotFinish);
    }

    /// <summary>
    /// One position of the forward pass: the next reached[s], the mark (0 outside a match, 1 inside one, 2 at the
    /// first code point of a match), and the next match count.
    /// </summary>
    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward(
        [Range(0, SearchCells.Classes)] int codePointClass,
        [FixedLength(S)] bool[] notReach,
        [FixedLength(S)] bool[] reached,
        [Range(0, SearchCells.Positions)] int matchCount,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions)
    {
        // JS: continuing[s] = reached[s] && reach[s]
        var continuing = new bool[S];
        for (int s = 0; s < S; s++) continuing[s] = reached[s] && !notReach[s];
        var continuingCount = new int[1];
        for (int s = 0; s < S; s++) continuingCount[0] = continuingCount[0] + (continuing[s] ? 1 : 0);
        bool anyContinuing = continuingCount[0] > 0;
        // JS: start = !continuing.some(Boolean) && reach[0]
        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing | start;
        var live = new bool[S];
        for (int s = 0; s < S; s++) live[s] = s == 0 ? continuing[s] | start : continuing[s];
        // JS: reached[t] = states.some(s => live[s] && transitions[class][s][t])
        var nextReached = new bool[S];
        for (int to = 0; to < S; to++)
        {
            for (int from = 0; from < S; from++) nextReached[to] = nextReached[to] | (live[from] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
        }
        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        return (nextReached, mark, matchCount + (start ? 1 : 0));
    }
}

/// <summary>The search steps at 32 states.</summary>
public static class Search32
{
    /// <summary>The number of states.</summary>
    public const int S = 32;

    /// <summary>notFinish[s] = !accepting[s]: the bits of the reverse pass before the last code point.</summary>
    [Handwired]
    public static bool[] Start([FixedLength(S)] bool[] accepting)
    {
        var notFinish = new bool[S];
        for (int s = 0; s < S; s++) notFinish[s] = !accepting[s];
        return notFinish;
    }

    /// <summary>One position of the reverse pass: the class of the code point, notReach[s], and the next notFinish[s].</summary>
    [Handwired]
    public static (int CodePointClass, bool[] NotReach, bool[] NotFinish) Reverse(
        [Range(-1, PatternCompiler.MaxCodePoint)] int codePoint,
        [Range(0, SearchCells.KindCount - 1)] int kind,
        [FixedLength(SearchCells.KindCount)] bool[] searchable,
        [FixedLength(SearchCells.Boundaries), Range(1, PatternCompiler.CodePointLimit)] int[] boundaries,
        [FixedLength(SearchCells.RegionCells), Range(0, SearchCells.Classes)] int[] regionClasses,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions,
        [FixedLength(S)] bool[] accepting,
        [FixedLength(S)] bool[] notFinish)
    {
        // JS: searched = searchable[kind] ? codePoint : -1
        int searched = (searchable[kind] ? codePoint + 1 : 0) - 1;
        // JS: regionClasses[[searched >= 0] + boundaries.filter(boundary => searched >= boundary).length]
        var region = new int[1];
        region[0] = searched >= 0 ? 1 : 0;
        for (int k = 0; k < SearchCells.Boundaries; k++) region[0] = region[0] + (searched >= boundaries[k] ? 1 : 0);
        int codePointClass = regionClasses[region[0]];
        // JS: reach[s] = states.some(t => transitions[class][s][t] && (accepting[t] || reachAfter[t]))
        var reach = new bool[S];
        var notReach = new bool[S];
        var nextNotFinish = new bool[S];
        for (int from = 0; from < S; from++)
        {
            for (int to = 0; to < S; to++) reach[from] = reach[from] | (!notFinish[to] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
            notReach[from] = !reach[from];
            nextNotFinish[from] = !reach[from] && !accepting[from];
        }
        return (codePointClass, notReach, nextNotFinish);
    }

    /// <summary>
    /// One position of the forward pass: the next reached[s], the mark (0 outside a match, 1 inside one, 2 at the
    /// first code point of a match), and the next match count.
    /// </summary>
    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward(
        [Range(0, SearchCells.Classes)] int codePointClass,
        [FixedLength(S)] bool[] notReach,
        [FixedLength(S)] bool[] reached,
        [Range(0, SearchCells.Positions)] int matchCount,
        [FixedLength(SearchCells.ClassSlots * S * S)] bool[] transitions)
    {
        // JS: continuing[s] = reached[s] && reach[s]
        var continuing = new bool[S];
        for (int s = 0; s < S; s++) continuing[s] = reached[s] && !notReach[s];
        var continuingCount = new int[1];
        for (int s = 0; s < S; s++) continuingCount[0] = continuingCount[0] + (continuing[s] ? 1 : 0);
        bool anyContinuing = continuingCount[0] > 0;
        // JS: start = !continuing.some(Boolean) && reach[0]
        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing | start;
        var live = new bool[S];
        for (int s = 0; s < S; s++) live[s] = s == 0 ? continuing[s] | start : continuing[s];
        // JS: reached[t] = states.some(s => live[s] && transitions[class][s][t])
        var nextReached = new bool[S];
        for (int to = 0; to < S; to++)
        {
            for (int from = 0; from < S; from++) nextReached[to] = nextReached[to] | (live[from] && transitions[(((from * S) + to) * SearchCells.ClassSlots) + codePointClass]);
        }
        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        return (nextReached, mark, matchCount + (start ? 1 : 0));
    }
}
