// Handwired source: src/json-search/highlighter/tables.ts. The constant tables of the highlighter step: a
// character class for each code point, the transition and event tables of the tokenizer, small tables for each
// event, and the transition and error tables of the grammar. Each table comes from a transition function written
// from the grammar of JSON (RFC 8259) and the rules in Kinds.cs, for example LexerStep(state, characterClass).
// The reference of the tests (tests/JsonSearch.Tests/References/JsonReference.cs) has the same rules as a scanner
// and a parser written by hand, without these tables, and the highlighter tests compare the program with it.

namespace JsonSearch;

/// <summary>The tables of the highlighter. The step methods read them as constant tables.</summary>
public static class Tables
{
    // The character classes. A letter that is part of a token has its own class, for example `e` is a hex digit,
    // an exponent and a letter of true and false. Each code point from U+0080 up, and U+007F, is Other, which only
    // a string accepts. End is a cell after the text, and TruncatedEnd the position after the 512th code point of
    // a longer text.
    private enum CharacterClass
    {
        End, TruncatedEnd, Space, TabOrCarriageReturn, LineFeed, Control, Quote, Backslash, Slash,
        OpenBrace, CloseBrace, OpenBracket, CloseBracket, Colon, Comma, Minus, Plus, Dot, Zero, Digit,
        A, B, E, CapitalE, F, L, N, R, S, T, U, OtherHexLetter, Other,
    }

    // The states of the tokenizer. Hex4 expects 4 more hex digits of a \u escape, and Hex1 the last one. T, Tr and
    // Tru are the letters of true read so far. ErrorLine follows a lexer error up to the next line feed, where the
    // tokenizer starts again, so the next lines keep their colors.
    private enum LexerState
    {
        Between, String, Escape, Hex4, Hex3, Hex2, Hex1, Minus, Zero, Integer, Dot, Fraction,
        Exponent, ExponentSign, ExponentDigits, T, Tr, Tru, F, Fa, Fal, Fals, N, Nu, Nul, ErrorLine,
    }

    // What one character does, from the state of the tokenizer before it and its class.
    private enum LexerEvent
    {
        Whitespace, StringStart, StringContent, StringEnd, Escape, NumberStart, NumberContent, LiteralStart,
        LiteralContent, OpenObject, OpenArray, CloseObject, CloseArray, Colon, Comma, UnexpectedCharacter,
        ControlCharacterInString, BadEscape, BadUnicodeEscape, BadNumber, BadLiteral, ErrorContinuation, End,
        EndInsideToken, TruncatedEnd,
    }

    // The grammar reads one token for each event: none, the start of a string, of a number or of a literal, a
    // punctuation mark, a lexer error, or the end of the text.
    private enum GrammarToken
    {
        None, String, Scalar, OpenObject, OpenArray, CloseObject, CloseArray, Colon, Comma, LexerError, End,
    }

    // The states of the grammar: what can come next. The initial state is Value. Error keeps each position after
    // the first error.
    private enum GrammarState
    {
        Value, ValueOrClose, KeyOrClose, Key, Colon, CommaOrClose, End, Error,
    }

    public const int ClassCount = 33;
    public const int LexerStateCount = 26;
    public const int EventCount = 25;
    public const int GrammarStateCount = 8;
    public const int GrammarTokenCount = 11;
    public const int DepthClassCount = 4;

    /// <summary>The context of a character for the grammar: depth class × 2 + [the container at the depth is an object].</summary>
    public const int ContextCount = DepthClassCount * 2;

    public const int SymbolCount = GrammarTokenCount * ContextCount;

    /// <summary>The grammar state Error, the last state.</summary>
    public const int ErrorState = (int)GrammarState.Error;

    /// <summary>Classes[codePoint + 1]: entry 0 is End for the empty cell value -1, and a code point past the table clamps to U+007F, which is Other.</summary>
    public static readonly int[] Classes = [.. Enumerable.Range(0, 129).Select(entry => (int)ClassOfCodePoint(entry - 1))];

    /// <summary>LexerNext[state × ClassCount + class].</summary>
    public static readonly int[] LexerNext = [.. AllLexerSteps().Select(step => (int)step.Next)];

    /// <summary>LexerEvent[state × ClassCount + class].</summary>
    public static readonly int[] LexerEvents = [.. AllLexerSteps().Select(step => (int)step.Event)];

    public static readonly int[] GrammarTokens = [.. Enum.GetValues<LexerEvent>().Select(@event => (int)GrammarTokenOf(@event))];

    public static readonly int[] LexerErrorKinds = [.. Enum.GetValues<LexerEvent>().Select(@event => (int)LexerErrorKindOf(@event))];

    public static readonly int[] DepthChange = [.. Enum.GetValues<LexerEvent>().Select(@event => @event is LexerEvent.OpenObject or LexerEvent.OpenArray ? 1 : @event is LexerEvent.CloseObject or LexerEvent.CloseArray ? -1 : 0)];

    /// <summary>
    /// The container kinds as one integer, before the first error: bit d - 1 is 1 when the container at depth d
    /// is an object. An opening brace at depth d - 1 adds 2^(d - 1), and a closing brace at depth d subtracts it,
    /// so ObjectWeight[depthBefore × EventCount + event] is 2^depthBefore for { and -2^(depthBefore - 1) for }.
    /// The depths outside 0..16 read 0.
    /// </summary>
    public static readonly int[] ObjectWeight = [.. Enumerable.Range(0, (Kinds.MaximumDepth + 1) * EventCount).Select(entry =>
    {
        var depthBefore = entry / EventCount;
        var @event = (LexerEvent)(entry % EventCount);
        if (@event == LexerEvent.OpenObject && depthBefore < Kinds.MaximumDepth) return 1 << depthBefore;
        if (@event == LexerEvent.CloseObject && depthBefore >= 1) return -(1 << (depthBefore - 1));
        return 0;
    })];

    /// <summary>LevelWeight[depth - 1] is 2^(depth - 1), the bit of the container at that depth, for example 1 for a top-level container.</summary>
    public static readonly int[] LevelWeight = [.. Enumerable.Range(0, Kinds.MaximumDepth).Select(level => 1 << level)];

    /// <summary>
    /// DepthClass[depth]: 0 at the top level, 1 in a top-level container, 2 deeper, and 3 at the maximum depth,
    /// where an opening bracket is TooDeep. The depths past the table clamp to its ends.
    /// </summary>
    public static readonly int[] DepthClass = [.. Enumerable.Range(0, Kinds.MaximumDepth + 1).Select(depth => depth == 0 ? 0 : depth == 1 ? 1 : depth == Kinds.MaximumDepth ? 3 : 2)];

    /// <summary>GrammarNext[state × SymbolCount + token × ContextCount + context].</summary>
    public static readonly int[] GrammarNext = [.. AllGrammarSteps().Select(step => (int)step.Next)];

    /// <summary>GrammarError[state × SymbolCount + token × ContextCount + context].</summary>
    public static readonly int[] GrammarError = [.. AllGrammarSteps().Select(step => (int)step.Error)];

    /// <summary>
    /// Display[event × GrammarStateCount + grammar state after the character]: a string character is a key when
    /// the grammar expects a colon after it, which occurs only in a key.
    /// </summary>
    public static readonly int[] Display = [.. Enum.GetValues<LexerEvent>().SelectMany(@event => Enum.GetValues<GrammarState>().Select(state => (int)DisplayKindOf(@event, state)))];

    private static CharacterClass ClassOfCodePoint(int codePoint)
    {
        if (codePoint == -1) return CharacterClass.End;
        if (codePoint == 0x20) return CharacterClass.Space;
        if (codePoint is 0x09 or 0x0d) return CharacterClass.TabOrCarriageReturn;
        if (codePoint == 0x0a) return CharacterClass.LineFeed;
        if (codePoint < 0x20) return CharacterClass.Control;
        if (codePoint is >= 0x30 and <= 0x39) return codePoint == 0x30 ? CharacterClass.Zero : CharacterClass.Digit;
        return (char)codePoint switch
        {
            '"' => CharacterClass.Quote,
            '\\' => CharacterClass.Backslash,
            '/' => CharacterClass.Slash,
            '{' => CharacterClass.OpenBrace,
            '}' => CharacterClass.CloseBrace,
            '[' => CharacterClass.OpenBracket,
            ']' => CharacterClass.CloseBracket,
            ':' => CharacterClass.Colon,
            ',' => CharacterClass.Comma,
            '-' => CharacterClass.Minus,
            '+' => CharacterClass.Plus,
            '.' => CharacterClass.Dot,
            'E' => CharacterClass.CapitalE,
            'a' => CharacterClass.A,
            'b' => CharacterClass.B,
            'e' => CharacterClass.E,
            'f' => CharacterClass.F,
            'l' => CharacterClass.L,
            'n' => CharacterClass.N,
            'r' => CharacterClass.R,
            's' => CharacterClass.S,
            't' => CharacterClass.T,
            'u' => CharacterClass.U,
            'c' or 'd' or 'A' or 'B' or 'C' or 'D' or 'F' => CharacterClass.OtherHexLetter,
            _ => CharacterClass.Other,
        };
    }

    private readonly record struct Step(LexerState Next, LexerEvent Event);

    private static IEnumerable<Step> AllLexerSteps() =>
        Enum.GetValues<LexerState>().SelectMany(state => Enum.GetValues<CharacterClass>().Select(characterClass => LexerStep(state, characterClass)));

    private static bool IsDigit(CharacterClass characterClass) => characterClass is CharacterClass.Zero or CharacterClass.Digit;

    private static bool IsHexDigit(CharacterClass characterClass) =>
        IsDigit(characterClass) || characterClass is CharacterClass.A or CharacterClass.B or CharacterClass.E or CharacterClass.CapitalE or CharacterClass.F or CharacterClass.OtherHexLetter;

    // A lexer error at a character. A line feed starts the tokenizer again at once, and each other character
    // starts an error line.
    private static Step LexerError(CharacterClass characterClass, LexerEvent @event) =>
        new(characterClass == CharacterClass.LineFeed ? LexerState.Between : LexerState.ErrorLine, @event);

    // A character that starts a token, or whitespace between tokens.
    private static Step StartToken(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Space or CharacterClass.TabOrCarriageReturn or CharacterClass.LineFeed => new(LexerState.Between, LexerEvent.Whitespace),
        CharacterClass.Quote => new(LexerState.String, LexerEvent.StringStart),
        CharacterClass.OpenBrace => new(LexerState.Between, LexerEvent.OpenObject),
        CharacterClass.CloseBrace => new(LexerState.Between, LexerEvent.CloseObject),
        CharacterClass.OpenBracket => new(LexerState.Between, LexerEvent.OpenArray),
        CharacterClass.CloseBracket => new(LexerState.Between, LexerEvent.CloseArray),
        CharacterClass.Colon => new(LexerState.Between, LexerEvent.Colon),
        CharacterClass.Comma => new(LexerState.Between, LexerEvent.Comma),
        CharacterClass.Minus => new(LexerState.Minus, LexerEvent.NumberStart),
        CharacterClass.Zero => new(LexerState.Zero, LexerEvent.NumberStart),
        CharacterClass.Digit => new(LexerState.Integer, LexerEvent.NumberStart),
        CharacterClass.T => new(LexerState.T, LexerEvent.LiteralStart),
        CharacterClass.F => new(LexerState.F, LexerEvent.LiteralStart),
        CharacterClass.N => new(LexerState.N, LexerEvent.LiteralStart),
        CharacterClass.End => new(LexerState.Between, LexerEvent.End),
        CharacterClass.TruncatedEnd => new(LexerState.Between, LexerEvent.TruncatedEnd),
        _ => LexerError(characterClass, LexerEvent.UnexpectedCharacter),
    };

    // In a token that is not complete, for example after - or tr: the end of the text is EndInsideToken, the 513th
    // code point is TruncatedEnd, and each other character that does not continue the token is the given error.
    private static Step ContinueOr(CharacterClass characterClass, Step? continued, LexerEvent error)
    {
        if (continued is { } step) return step;
        if (characterClass == CharacterClass.End) return new(LexerState.Between, LexerEvent.EndInsideToken);
        if (characterClass == CharacterClass.TruncatedEnd) return new(LexerState.Between, LexerEvent.TruncatedEnd);
        return LexerError(characterClass, error);
    }

    // A letter of a literal: the expected letter continues to next, where Between means that the literal is complete.
    private static Step LiteralLetter(CharacterClass characterClass, CharacterClass expected, LexerState next) =>
        ContinueOr(characterClass, characterClass == expected ? new Step(next, LexerEvent.LiteralContent) : null, LexerEvent.BadLiteral);

    private static Step LexerStep(LexerState state, CharacterClass characterClass)
    {
        var digit = IsDigit(characterClass);
        var exponentLetter = characterClass is CharacterClass.E or CharacterClass.CapitalE;
        Step NumberContent(LexerState next) => new(next, LexerEvent.NumberContent);
        switch (state)
        {
            case LexerState.Between:
                return StartToken(characterClass);
            case LexerState.String:
            {
                if (characterClass == CharacterClass.Quote) return new(LexerState.Between, LexerEvent.StringEnd);
                if (characterClass == CharacterClass.Backslash) return new(LexerState.Escape, LexerEvent.Escape);
                var isControl = characterClass is CharacterClass.Control or CharacterClass.TabOrCarriageReturn or CharacterClass.LineFeed;
                var stops = isControl || characterClass is CharacterClass.End or CharacterClass.TruncatedEnd;
                return ContinueOr(characterClass, stops ? null : new Step(LexerState.String, LexerEvent.StringContent), LexerEvent.ControlCharacterInString);
            }
            case LexerState.Escape:
            {
                var simple = characterClass is CharacterClass.Quote or CharacterClass.Backslash or CharacterClass.Slash or CharacterClass.B
                    or CharacterClass.F or CharacterClass.N or CharacterClass.R or CharacterClass.T;
                Step? continued = simple ? new Step(LexerState.String, LexerEvent.Escape) : characterClass == CharacterClass.U ? new Step(LexerState.Hex4, LexerEvent.Escape) : null;
                return ContinueOr(characterClass, continued, LexerEvent.BadEscape);
            }
            case LexerState.Hex4 or LexerState.Hex3 or LexerState.Hex2 or LexerState.Hex1:
            {
                var next = state switch
                {
                    LexerState.Hex4 => LexerState.Hex3,
                    LexerState.Hex3 => LexerState.Hex2,
                    LexerState.Hex2 => LexerState.Hex1,
                    _ => LexerState.String,
                };
                return ContinueOr(characterClass, IsHexDigit(characterClass) ? new Step(next, LexerEvent.Escape) : null, LexerEvent.BadUnicodeEscape);
            }
            case LexerState.Minus:
            {
                Step? continued = characterClass == CharacterClass.Zero ? NumberContent(LexerState.Zero) : characterClass == CharacterClass.Digit ? NumberContent(LexerState.Integer) : null;
                return ContinueOr(characterClass, continued, LexerEvent.BadNumber);
            }
            case LexerState.Zero:
                if (digit) return LexerError(characterClass, LexerEvent.BadNumber);
                if (characterClass == CharacterClass.Dot) return NumberContent(LexerState.Dot);
                if (exponentLetter) return NumberContent(LexerState.Exponent);
                return StartToken(characterClass);
            case LexerState.Integer:
                if (digit) return NumberContent(LexerState.Integer);
                if (characterClass == CharacterClass.Dot) return NumberContent(LexerState.Dot);
                if (exponentLetter) return NumberContent(LexerState.Exponent);
                return StartToken(characterClass);
            case LexerState.Dot:
                return ContinueOr(characterClass, digit ? NumberContent(LexerState.Fraction) : null, LexerEvent.BadNumber);
            case LexerState.Fraction:
                if (digit) return NumberContent(LexerState.Fraction);
                if (exponentLetter) return NumberContent(LexerState.Exponent);
                return StartToken(characterClass);
            case LexerState.Exponent:
            {
                var sign = characterClass is CharacterClass.Plus or CharacterClass.Minus;
                Step? continued = sign ? NumberContent(LexerState.ExponentSign) : digit ? NumberContent(LexerState.ExponentDigits) : null;
                return ContinueOr(characterClass, continued, LexerEvent.BadNumber);
            }
            case LexerState.ExponentSign:
                return ContinueOr(characterClass, digit ? NumberContent(LexerState.ExponentDigits) : null, LexerEvent.BadNumber);
            case LexerState.ExponentDigits:
                return digit ? NumberContent(LexerState.ExponentDigits) : StartToken(characterClass);
            case LexerState.T: return LiteralLetter(characterClass, CharacterClass.R, LexerState.Tr);
            case LexerState.Tr: return LiteralLetter(characterClass, CharacterClass.U, LexerState.Tru);
            case LexerState.Tru: return LiteralLetter(characterClass, CharacterClass.E, LexerState.Between);
            case LexerState.F: return LiteralLetter(characterClass, CharacterClass.A, LexerState.Fa);
            case LexerState.Fa: return LiteralLetter(characterClass, CharacterClass.L, LexerState.Fal);
            case LexerState.Fal: return LiteralLetter(characterClass, CharacterClass.S, LexerState.Fals);
            case LexerState.Fals: return LiteralLetter(characterClass, CharacterClass.E, LexerState.Between);
            case LexerState.N: return LiteralLetter(characterClass, CharacterClass.U, LexerState.Nu);
            case LexerState.Nu: return LiteralLetter(characterClass, CharacterClass.L, LexerState.Nul);
            case LexerState.Nul: return LiteralLetter(characterClass, CharacterClass.L, LexerState.Between);
            default:
                // ErrorLine.
                if (characterClass == CharacterClass.LineFeed) return new(LexerState.Between, LexerEvent.Whitespace);
                if (characterClass == CharacterClass.End) return new(LexerState.Between, LexerEvent.End);
                if (characterClass == CharacterClass.TruncatedEnd) return new(LexerState.Between, LexerEvent.TruncatedEnd);
                return new(LexerState.ErrorLine, LexerEvent.ErrorContinuation);
        }
    }

    private static GrammarToken GrammarTokenOf(LexerEvent @event) => @event switch
    {
        LexerEvent.Whitespace or LexerEvent.StringContent or LexerEvent.StringEnd or LexerEvent.Escape or LexerEvent.NumberContent or LexerEvent.LiteralContent => GrammarToken.None,
        LexerEvent.StringStart => GrammarToken.String,
        LexerEvent.NumberStart or LexerEvent.LiteralStart => GrammarToken.Scalar,
        LexerEvent.OpenObject => GrammarToken.OpenObject,
        LexerEvent.OpenArray => GrammarToken.OpenArray,
        LexerEvent.CloseObject => GrammarToken.CloseObject,
        LexerEvent.CloseArray => GrammarToken.CloseArray,
        LexerEvent.Colon => GrammarToken.Colon,
        LexerEvent.Comma => GrammarToken.Comma,
        LexerEvent.End => GrammarToken.End,
        _ => GrammarToken.LexerError,
    };

    // The error kind of a lexer event, which the step adds to the error kind of the grammar. ErrorContinuation only
    // follows an earlier lexer error, so it reports nothing.
    private static ErrorKind LexerErrorKindOf(LexerEvent @event) => @event switch
    {
        LexerEvent.UnexpectedCharacter => ErrorKind.UnexpectedCharacter,
        LexerEvent.ControlCharacterInString => ErrorKind.ControlCharacterInString,
        LexerEvent.BadEscape => ErrorKind.BadEscape,
        LexerEvent.BadUnicodeEscape => ErrorKind.BadUnicodeEscape,
        LexerEvent.BadNumber => ErrorKind.BadNumber,
        LexerEvent.BadLiteral => ErrorKind.BadLiteral,
        LexerEvent.EndInsideToken => ErrorKind.UnexpectedEnd,
        LexerEvent.TruncatedEnd => ErrorKind.TooLong,
        _ => ErrorKind.None,
    };

    private readonly record struct GrammarStep(GrammarState Next, ErrorKind Error);

    private static IEnumerable<GrammarStep> AllGrammarSteps() =>
        Enum.GetValues<GrammarState>().SelectMany(state => Enum.GetValues<GrammarToken>().SelectMany(token =>
            Enumerable.Range(0, ContextCount).Select(context => StepGrammar(state, token, context / 2, context % 2 == 1))));

    // The grammar between tokens, from the state before a character, the token of the character, the depth class
    // before it, and whether the container at that depth is an object. Only the starts of strings and scalars and
    // the punctuation change the state, and a string in a key position goes to Colon.
    private static GrammarStep StepGrammar(GrammarState state, GrammarToken token, int depthClass, bool inObject)
    {
        if (state == GrammarState.Error) return new(GrammarState.Error, ErrorKind.None);
        if (token == GrammarToken.None) return new(state, ErrorKind.None);
        if (token == GrammarToken.LexerError) return new(GrammarState.Error, ErrorKind.None);
        if (token == GrammarToken.End) return state == GrammarState.End ? new(GrammarState.End, ErrorKind.None) : new(GrammarState.Error, ErrorKind.UnexpectedEnd);
        // The error of a token that this state does not accept, for example a number where a key must be.
        GrammarStep Unexpected() => state switch
        {
            GrammarState.Value or GrammarState.ValueOrClose => new(GrammarState.Error, ErrorKind.ExpectedValue),
            GrammarState.KeyOrClose or GrammarState.Key => new(GrammarState.Error, ErrorKind.ExpectedKey),
            GrammarState.Colon => new(GrammarState.Error, ErrorKind.ExpectedColon),
            GrammarState.CommaOrClose => new(GrammarState.Error, ErrorKind.ExpectedCommaOrClose),
            _ => new(GrammarState.Error, ErrorKind.ExtraAfterValue),
        };
        var expectsValue = state is GrammarState.Value or GrammarState.ValueOrClose;
        // After a complete value at this depth: the end of the text at the top level, else a comma or a closing bracket.
        var afterValue = new GrammarStep(depthClass == 0 ? GrammarState.End : GrammarState.CommaOrClose, ErrorKind.None);
        switch (token)
        {
            case GrammarToken.String:
                if (state is GrammarState.KeyOrClose or GrammarState.Key) return new(GrammarState.Colon, ErrorKind.None);
                return expectsValue ? afterValue : Unexpected();
            case GrammarToken.Scalar:
                return expectsValue ? afterValue : Unexpected();
            case GrammarToken.OpenObject or GrammarToken.OpenArray:
                if (!expectsValue) return Unexpected();
                if (depthClass == 3) return new(GrammarState.Error, ErrorKind.TooDeep);
                return new(token == GrammarToken.OpenObject ? GrammarState.KeyOrClose : GrammarState.ValueOrClose, ErrorKind.None);
            case GrammarToken.CloseObject or GrammarToken.CloseArray:
            {
                var closesObject = token == GrammarToken.CloseObject;
                var mayClose = closesObject ? state is GrammarState.KeyOrClose or GrammarState.CommaOrClose : state is GrammarState.ValueOrClose or GrammarState.CommaOrClose;
                var otherKindMayClose = closesObject ? state is GrammarState.ValueOrClose or GrammarState.CommaOrClose : state is GrammarState.KeyOrClose or GrammarState.CommaOrClose;
                if (mayClose && inObject == closesObject) return new(depthClass == 1 ? GrammarState.End : GrammarState.CommaOrClose, ErrorKind.None);
                if (otherKindMayClose) return new(GrammarState.Error, ErrorKind.MismatchedBracket);
                return Unexpected();
            }
            case GrammarToken.Colon:
                return state == GrammarState.Colon ? new(GrammarState.Value, ErrorKind.None) : Unexpected();
            default:
                // Comma.
                return state == GrammarState.CommaOrClose ? new(inObject ? GrammarState.Key : GrammarState.Value, ErrorKind.None) : Unexpected();
        }
    }

    private static DisplayKind DisplayKindOf(LexerEvent @event, GrammarState after) => @event switch
    {
        LexerEvent.Whitespace => DisplayKind.Whitespace,
        LexerEvent.StringStart or LexerEvent.StringContent or LexerEvent.StringEnd => after == GrammarState.Colon ? DisplayKind.Key : DisplayKind.String,
        LexerEvent.Escape => DisplayKind.Escape,
        LexerEvent.NumberStart or LexerEvent.NumberContent => DisplayKind.Number,
        LexerEvent.LiteralStart or LexerEvent.LiteralContent => DisplayKind.Literal,
        LexerEvent.OpenObject or LexerEvent.OpenArray or LexerEvent.CloseObject or LexerEvent.CloseArray or LexerEvent.Colon or LexerEvent.Comma => DisplayKind.Punctuation,
        LexerEvent.End or LexerEvent.EndInsideToken or LexerEvent.TruncatedEnd => DisplayKind.PastEnd,
        _ => DisplayKind.Error,
    };
}
