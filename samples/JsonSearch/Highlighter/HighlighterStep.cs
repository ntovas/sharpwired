// Handwired source: src/json-search/json-step.ts (buildJsonLoop: emitPosition, the step and the end). The JSON
// highlighter as the work of one position. The document cells hold the first 512 code points and the length of
// the text (DocumentCells.cs). For each code point, the step gives its display kind. The end gives whether the
// text is valid, the position and the kind of the first error, and whether the text is too long.
//
// What one position computes, from the tables of Tables.cs and from the registers of the position before:
// 1. The class of the character, one read of a table of 129 cells.
// 2. The event of the tokenizer, for example StringStart or BadNumber, and its next state, each one read at
//    (state, class).
// 3. The depth after the character, a running total of +1 and -1.
// 4. The container kinds as one integer, the container code: a running total of ±2^depth for braces. Whether the
//    innermost container is an object is one comparison of the code with 2^(depth - 1). Before the first error
//    each container is open at its depth. So the bits of the code below the depth are the kinds of the open
//    containers, and the comparison reads the top bit. After the first error the code can be wrong, and the
//    grammar stays in its error state for each result of the comparison.
// 5. The grammar between tokens, a state machine of 8 states over (token, depth class, container kind), one read.
// 6. The display kind, one read at (event, grammar state), and the error kind of this position. The error kind
//    of this position is the error kind of the event plus the error kind of the grammar step.
//
// The stages (HighlighterProgram.cs): Step runs at the positions 0 to 511, and End runs one time for position 512.
// Position 512 is the end of the text or the 513th code point of a longer text. The first error position is a
// count. The error state of the grammar never goes back, so the positions before it are 513 minus the positions
// in it. The first error kind is carried. The step adds the error kind of the position where the grammar goes
// into the error state, which occurs at most one time.
//
// A cell after the text holds -1, the class End. After the text the tokenizer stays in Between with the event
// End, and the grammar stays in End or in Error. So each position after the text gives PastEnd and keeps the
// registers. The GPU runs each position, and the CPU runner gives the same values (the program has no length cell).
//
// Differences from Handwired:
// - Step and End are two translated functions, so each has its own copy of the tables (plan, question 1, item 6).
// - inError of json-step.ts is reglu(state - 6) for the last state 7, one operation. Here it is the comparison
//   `grammarState >= ErrorState`, which gives the same bit in 2 operations.
// - The first error kind of json-step.ts is one ReGLU, errorKindHere × relu(errorAfter - errorBefore). Here it
//   is the bit `errorAfter && !errorBefore` and a select, 2 operations.
// - The Handwired step is 37 operations and the end 17. Here the step is 40 and the end 18 live operations that
//   compute (HighlighterTests pins them). The two items above give 3 more operations in each stage: 1 for each
//   error-state bit, and 1 for the first error kind. The end also has 2 fewer. Its affine form of
//   firstErrorPosition composes errorFreeCount, which is a roundAffine of its own in json-step.ts. Its output
//   truncated is the bit itself (rule 1), and json-step.ts writes a roundAffine of the bit.

using Sharpwired.Annotations;

namespace JsonSearch;

/// <summary>
/// The registers of the highlighter, each with the range that its value has after any number of positions.
/// The registers are the tokenizer state, the depth, the container code and the grammar state. They are also
/// the number of positions in the error state, and the kind of the first error.
/// </summary>
public readonly record struct HighlighterState(
    [Range(0, Tables.LexerStateCount - 1)] int LexerState,
    [Range(-Kinds.Capacity, Kinds.Capacity)] int Depth,
    [Range(-HighlighterStep.CodeLimit, HighlighterStep.CodeLimit)] int Code,
    [Range(0, Tables.GrammarStateCount - 1)] int GrammarState,
    [Range(0, HighlighterStep.Positions)] int ErrorCount,
    [Range(0, 15)] int FirstErrorKind);

/// <summary>The values of emitPosition of json-step.ts: the work of both stages after the class of the character is known.</summary>
public readonly record struct PositionValues(int Event, int LexerNext, int GrammarNext, bool ErrorAfter, int FirstErrorKind);

/// <summary>The step methods of the highlighter.</summary>
public static class HighlighterStep
{
    /// <summary>The positions of a document: the 512 code points and the end.</summary>
    public const int Positions = Kinds.Capacity + 1;

    /// <summary>The container code is a running total of ±2^depth over at most 511 positions before the last position that reads it.</summary>
    public const int CodeLimit = (Kinds.Capacity - 1) * (1 << (Kinds.MaximumDepth - 1));

    /// <summary>The work of one of the positions 0 to 511: the next registers and the display kind of the code point.</summary>
    [Handwired]
    public static (HighlighterState Next, int DisplayKind) Step(HighlighterState state, [Range(-1, 0x10FFFF)] int codePoint)
    {
        bool errorBefore = state.GrammarState >= Tables.ErrorState;
        int characterClass = Tables.Classes[Math.Clamp(codePoint + 1, 0, 128)];
        bool inObject = state.Code >= Tables.LevelWeight[Math.Clamp(state.Depth - 1, 0, 15)];
        PositionValues step = Position(state, characterClass, inObject, errorBefore);
        int depthChange = Tables.DepthChange[step.Event];
        int objectWeight = Hw.ReadOrZero(Tables.ObjectWeight, state.Depth * Tables.EventCount + step.Event);
        var next = new HighlighterState(
            step.LexerNext,
            state.Depth + depthChange,
            state.Code + objectWeight,
            step.GrammarNext,
            state.ErrorCount + (step.ErrorAfter ? 1 : 0),
            step.FirstErrorKind);
        int displayKind = Tables.Display[step.Event * Tables.GrammarStateCount + step.GrammarNext];
        return (next, displayKind);
    }

    /// <summary>
    /// The work of position 512, the end of the text or the 513th code point of a longer text. It gives the last
    /// event and grammar step, then Valid, FirstErrorPosition (-1 for a valid text), Truncated and FirstErrorKind.
    /// </summary>
    [Handwired]
    public static (int Valid, int FirstErrorPosition, int Truncated, int FirstErrorKind) End(
        HighlighterState state, [Range(0, DocumentCells.LengthLimit)] int textLength)
    {
        bool errorBefore = state.GrammarState >= Tables.ErrorState;
        // The class of position 512 is TruncatedEnd (1) after the 512th code point of a longer text, else End (0).
        bool truncated = textLength > Kinds.Capacity;
        PositionValues end = Position(state, truncated ? 1 : 0, false, errorBefore);
        int errorFreeCount = Positions - state.ErrorCount - (end.ErrorAfter ? 1 : 0);
        int valid = end.ErrorAfter ? 0 : 1;
        int firstErrorPosition = errorFreeCount - (Positions + 1) * valid;
        return (valid, firstErrorPosition, truncated ? 1 : 0, end.FirstErrorKind);
    }

    // emitPosition of json-step.ts. The end has no container kind, so it gives false for inObject.
    [Handwired]
    private static PositionValues Position(HighlighterState state, [Range(0, Tables.ClassCount - 1)] int characterClass, bool inObject, bool errorBefore)
    {
        int ev = Tables.LexerEvents[state.LexerState * Tables.ClassCount + characterClass];
        int lexerNext = Tables.LexerNext[state.LexerState * Tables.ClassCount + characterClass];
        int depthClass = Tables.DepthClass[Math.Clamp(state.Depth, 0, 16)];
        int grammarToken = Tables.GrammarTokens[ev];
        int symbol = grammarToken * Tables.ContextCount + depthClass * 2 + (inObject ? 1 : 0);
        int grammarNext = Tables.GrammarNext[state.GrammarState * Tables.SymbolCount + symbol];
        int errorKindHere = Tables.LexerErrorKinds[ev] + Tables.GrammarError[state.GrammarState * Tables.SymbolCount + symbol];
        bool errorAfter = grammarNext >= Tables.ErrorState;
        // The grammar goes into the error state at this position. A local bit gives one operation for the AND;
        // the same AND in the condition of the select gives 4 operations more.
        bool firstError = errorAfter && !errorBefore;
        int firstErrorKind = state.FirstErrorKind + (firstError ? errorKindHere : 0);
        return new PositionValues(ev, lexerNext, grammarNext, errorAfter, firstErrorKind);
    }
}
