// Handwired source: src/json-search/highlighter/kinds.ts. The names and the numbers that the highlighter, the
// code that shows its outputs and the plain C# reference of the tests share. They are the capacity, the maximum
// depth, the display kinds and the error kinds. This file holds only names and numbers. The tables of the program
// are in Tables.cs, which the reference does not read.

namespace JsonSearch;

/// <summary>The capacity and the maximum depth of the highlighter.</summary>
public static class Kinds
{
    /// <summary>
    /// The highlighter reads up to this number of code points of a text. Past it, the highlighter reads only
    /// the first 512 and reports TooLong.
    /// </summary>
    public const int Capacity = 512;

    /// <summary>Containers nest up to this depth. An opening bracket that makes the depth 17 is the error TooDeep.</summary>
    public const int MaximumDepth = 16;
}

/// <summary>
/// What the highlighter shows a code point as. Key is a string character in a key position, for example the
/// "id" of {"id": 1}, that the grammar finds before the first error. PastEnd is a cell after the text.
/// </summary>
public enum DisplayKind
{
    Whitespace,
    Punctuation,
    String,
    Escape,
    Number,
    Literal,
    Error,
    PastEnd,
    Key,
}

/// <summary>
/// The kinds of the first error. The lexer errors come first, then the grammar errors between tokens, then
/// the named fallbacks.
/// </summary>
public enum ErrorKind
{
    None,

    /// <summary>A character that cannot start or continue a token, for example the x of [x] or the + of +1.</summary>
    UnexpectedCharacter,

    /// <summary>A code point below U+0020 in a string, for example a tab or a line feed between the quotes.</summary>
    ControlCharacterInString,

    /// <summary>A backslash before a character other than " \ / b f n r t u, for example "\q".</summary>
    BadEscape,

    /// <summary>A \u before fewer than 4 hex digits, for example "\u12x4".</summary>
    BadUnicodeEscape,

    /// <summary>A number that breaks the grammar of JSON, for example 01, -x, 1. or 1e.</summary>
    BadNumber,

    /// <summary>A literal that is not true, false or null, for example tru or nul!.</summary>
    BadLiteral,

    /// <summary>A value was expected, for example at the ] of [1,] or at the : of [:].</summary>
    ExpectedValue,

    /// <summary>A string key or a closing brace was expected, for example at the 1 of {1: 2}.</summary>
    ExpectedKey,

    /// <summary>A colon was expected after a key, for example at the 2 of {"a" 2}.</summary>
    ExpectedColon,

    /// <summary>A comma or a closing bracket was expected after a value, for example at the 2 of [1 2].</summary>
    ExpectedCommaOrClose,

    /// <summary>A closing bracket of the other kind, for example the } of [1}.</summary>
    MismatchedBracket,

    /// <summary>A character other than whitespace after the top-level value, for example the second 1 of 1 1.</summary>
    ExtraAfterValue,

    /// <summary>The named fallback for the depth: an opening bracket at depth 16.</summary>
    TooDeep,

    /// <summary>The text ends in a token or before the value is complete, for example [1, or "abc.</summary>
    UnexpectedEnd,

    /// <summary>The named fallback for the capacity: the text has more than 512 code points, and no error came before the 512th.</summary>
    TooLong,
}

/// <summary>
/// The summary of one document. FirstErrorPosition is the code point position of the first error, or -1 for
/// a valid text.
/// </summary>
public sealed record DocumentSummary(bool Valid, int FirstErrorPosition, ErrorKind FirstErrorKind, bool Truncated);
