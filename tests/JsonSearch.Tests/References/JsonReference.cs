// Handwired source: tests/json-search/highlighter/reference.ts. The highlighter in plain C#: a scanner, written
// by hand, that reads one token at a time, and a parser with a real stack, written from the grammar of JSON
// (RFC 8259) and the rules in Kinds.cs. It does not read Tables.cs, the step methods or the loop program.
//
// The rules past the grammar of JSON:
// - The first error position is the first code point at which the text stops being the start of a valid JSON
//   text, or the length of the text when the text is such a start but is not complete, for example 3 for
//   [1 2] and 2 for [1.
// - After a lexer error, for example the x of [x], the rest of the line shows as an error, and the scan starts
//   again after the next line feed. A line feed that is itself the error, for example in a string, starts the
//   scan again at once.
// - A string shows as a key when the parser accepts it in a key position before the first error.
// - The text past 512 code points is not read, and the error is TooLong when no error comes first. An opening
//   bracket at depth 16 is the error TooDeep.

using JsonSearch;

namespace JsonSearch.Tests;

/// <summary>The display kind of each of the 512 positions, and the summary of the text.</summary>
internal sealed record ReferenceResult(DisplayKind[] DisplayKinds, int FirstErrorPosition, ErrorKind FirstErrorKind, bool Valid, bool Truncated)
{
    public DocumentSummary Summary => new(Valid, FirstErrorPosition, FirstErrorKind, Truncated);
}

internal static class JsonReference
{
    private const int Capacity = Kinds.Capacity;
    private const int LineFeed = 0x0a;

    private enum TokenType
    {
        String,
        Scalar,
        OpenObject,
        OpenArray,
        CloseObject,
        CloseArray,
        Colon,
        Comma,
    }

    private enum Expected
    {
        Value,
        ValueOrClose,
        KeyOrClose,
        Key,
        Colon,
        CommaOrClose,
        End,
    }

    private sealed class Token(TokenType type, int start, int end)
    {
        public TokenType Type { get; } = type;

        public int Start { get; } = start;

        public int End { get; set; } = end;
    }

    private readonly record struct Found(int Position, ErrorKind Kind);

    public static ReferenceResult Highlight(string text)
    {
        var all = CodePoints.Of(text);
        var truncated = all.Length > Capacity;
        var codePoints = all.Take(Capacity).ToArray();
        var length = codePoints.Length;
        var scan = new Scanner(codePoints);
        scan.Run();
        var (grammarError, keys, expected) = Parse(scan.Tokens, scan.FirstLexerError?.Position ?? length);

        var firstError = grammarError ?? scan.FirstLexerError;
        if (firstError is null && truncated) firstError = new Found(Capacity, ErrorKind.TooLong);
        if (firstError is null && (scan.EndsInsideToken || expected != Expected.End)) firstError = new Found(length, ErrorKind.UnexpectedEnd);

        var errorPosition = firstError?.Position ?? int.MaxValue;
        foreach (var key in keys)
        {
            for (var position = key.Start; position < Math.Min(key.End, errorPosition); position++)
            {
                if (scan.Kinds[position] == DisplayKind.String) scan.Kinds[position] = DisplayKind.Key;
            }
        }

        return new ReferenceResult(scan.Kinds, firstError?.Position ?? -1, firstError?.Kind ?? ErrorKind.None, firstError is null, truncated);
    }

    private static bool IsDigit(int codePoint) => codePoint is >= 0x30 and <= 0x39;

    private static bool IsHexDigit(int codePoint) => IsDigit(codePoint) || codePoint is >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static bool Is(int codePoint, string characters) => codePoint >= 0 && codePoint <= 0xffff && characters.Contains((char)codePoint, StringComparison.Ordinal);

    // The scanner over the first 512 code points.
    private sealed class Scanner(int[] codePoints)
    {
        private readonly int length = codePoints.Length;
        private int index;

        public DisplayKind[] Kinds { get; } = Enumerable.Repeat(DisplayKind.PastEnd, Capacity).ToArray();

        public List<Token> Tokens { get; } = [];

        public Found? FirstLexerError { get; private set; }

        public bool EndsInsideToken { get; private set; }

        // The code point at the index, or -1 past the end.
        private int At(int at) => at < length ? codePoints[at] : -1;

        // Mark a lexer error and the rest of its line, and return where the scan continues.
        private int Fail(int position, ErrorKind kind)
        {
            FirstLexerError ??= new Found(position, kind);
            Kinds[position] = DisplayKind.Error;
            if (codePoints[position] == LineFeed) return position + 1;
            var next = position + 1;
            while (next < length && codePoints[next] != LineFeed) Kinds[next++] = DisplayKind.Error;
            return next;
        }

        private void Mark(int from, int to, DisplayKind kind)
        {
            for (var position = from; position < to; position++) Kinds[position] = kind;
        }

        public void Run()
        {
            while (index < length)
            {
                var start = index;
                var codePoint = codePoints[index];
                if (Is(codePoint, " \t\n\r"))
                {
                    Kinds[index++] = DisplayKind.Whitespace;
                }
                else if (Is(codePoint, "{}[]:,"))
                {
                    Kinds[index++] = DisplayKind.Punctuation;
                    var type = codePoint switch
                    {
                        '{' => TokenType.OpenObject,
                        '[' => TokenType.OpenArray,
                        '}' => TokenType.CloseObject,
                        ']' => TokenType.CloseArray,
                        ':' => TokenType.Colon,
                        _ => TokenType.Comma,
                    };
                    Tokens.Add(new Token(type, start, index));
                }
                else if (codePoint == '"')
                {
                    ScanString(start);
                }
                else if (codePoint == '-' || IsDigit(codePoint))
                {
                    var (end, missingDigitAt) = ScanNumber(start);
                    Tokens.Add(new Token(TokenType.Scalar, start, end));
                    Mark(start, end, DisplayKind.Number);
                    index = end;
                    if (missingDigitAt == length) EndsInsideToken = true;
                    else if (missingDigitAt is { } at) index = Fail(at, ErrorKind.BadNumber);
                }
                else if (Is(codePoint, "tfn"))
                {
                    ScanLiteral(start, codePoint == 't' ? "true" : codePoint == 'f' ? "false" : "null");
                }
                else
                {
                    index = Fail(index, ErrorKind.UnexpectedCharacter);
                }
            }
        }

        private void ScanString(int start)
        {
            var token = new Token(TokenType.String, start, length);
            Tokens.Add(token);
            Kinds[index++] = DisplayKind.String;
            var closed = false;
            while (!closed)
            {
                if (index == length)
                {
                    EndsInsideToken = true;
                    break;
                }
                var character = codePoints[index];
                if (character == '"')
                {
                    Kinds[index++] = DisplayKind.String;
                    closed = true;
                }
                else if (character == '\\')
                {
                    Kinds[index++] = DisplayKind.Escape;
                    if (index == length)
                    {
                        EndsInsideToken = true;
                        break;
                    }
                    if (Is(codePoints[index], "\"\\/bfnrt"))
                    {
                        Kinds[index++] = DisplayKind.Escape;
                    }
                    else if (codePoints[index] == 'u')
                    {
                        Kinds[index++] = DisplayKind.Escape;
                        var digits = 0;
                        while (digits < 4 && index < length && IsHexDigit(codePoints[index]))
                        {
                            Kinds[index++] = DisplayKind.Escape;
                            digits++;
                        }
                        if (digits < 4)
                        {
                            if (index == length)
                            {
                                EndsInsideToken = true;
                                break;
                            }
                            index = Fail(index, ErrorKind.BadUnicodeEscape);
                            break;
                        }
                    }
                    else
                    {
                        index = Fail(index, ErrorKind.BadEscape);
                        break;
                    }
                }
                else if (character < 0x20)
                {
                    index = Fail(index, ErrorKind.ControlCharacterInString);
                    break;
                }
                else
                {
                    Kinds[index++] = DisplayKind.String;
                }
            }
            token.End = index;
        }

        // -?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)? from start. Gives the end of the number, and the
        // position of a missing digit (after -, . or e) or of a digit after a leading zero, or null.
        private (int End, int? MissingDigitAt) ScanNumber(int start)
        {
            var at = start;
            if (At(at) == '-') at++;
            if (!IsDigit(At(at))) return (at, at);
            if (At(at) == '0')
            {
                at++;
                if (IsDigit(At(at))) return (at, at);
            }
            else
            {
                while (IsDigit(At(at))) at++;
            }
            if (At(at) == '.')
            {
                at++;
                if (!IsDigit(At(at))) return (at, at);
                while (IsDigit(At(at))) at++;
            }
            if (Is(At(at), "eE"))
            {
                at++;
                if (Is(At(at), "+-")) at++;
                if (!IsDigit(At(at))) return (at, at);
                while (IsDigit(At(at))) at++;
            }
            return (at, null);
        }

        private void ScanLiteral(int start, string word)
        {
            var token = new Token(TokenType.Scalar, start, length);
            Tokens.Add(token);
            Kinds[index++] = DisplayKind.Literal;
            for (var letter = 1; letter < word.Length; letter++)
            {
                if (index == length)
                {
                    EndsInsideToken = true;
                    break;
                }
                if (codePoints[index] != word[letter])
                {
                    index = Fail(index, ErrorKind.BadLiteral);
                    break;
                }
                Kinds[index++] = DisplayKind.Literal;
            }
            token.End = Math.Min(index, start + word.Length);
        }
    }

    // The grammar between tokens, up to the first lexer error. Gives the first grammar error and the key strings.
    private static (Found? Error, List<Token> Keys, Expected Expected) Parse(List<Token> tokens, int limit)
    {
        var stack = new Stack<bool>(); // true: an object, false: an array
        var expected = Expected.Value;
        var keys = new List<Token>();
        ErrorKind Unexpected() => expected switch
        {
            Expected.Value or Expected.ValueOrClose => ErrorKind.ExpectedValue,
            Expected.KeyOrClose or Expected.Key => ErrorKind.ExpectedKey,
            Expected.Colon => ErrorKind.ExpectedColon,
            Expected.CommaOrClose => ErrorKind.ExpectedCommaOrClose,
            _ => ErrorKind.ExtraAfterValue,
        };
        Expected AfterValue() => stack.Count == 0 ? Expected.End : Expected.CommaOrClose;
        foreach (var token in tokens)
        {
            if (token.Start >= limit) break;
            var expectsValue = expected is Expected.Value or Expected.ValueOrClose;
            Found Error(ErrorKind kind) => new(token.Start, kind);
            switch (token.Type)
            {
                case TokenType.String:
                    if (expected is Expected.KeyOrClose or Expected.Key)
                    {
                        keys.Add(token);
                        expected = Expected.Colon;
                    }
                    else if (expectsValue)
                    {
                        expected = AfterValue();
                    }
                    else
                    {
                        return (Error(Unexpected()), keys, expected);
                    }
                    break;
                case TokenType.Scalar:
                    if (!expectsValue) return (Error(Unexpected()), keys, expected);
                    expected = AfterValue();
                    break;
                case TokenType.OpenObject or TokenType.OpenArray:
                    if (!expectsValue) return (Error(Unexpected()), keys, expected);
                    if (stack.Count == Kinds.MaximumDepth) return (Error(ErrorKind.TooDeep), keys, expected);
                    stack.Push(token.Type == TokenType.OpenObject);
                    expected = token.Type == TokenType.OpenObject ? Expected.KeyOrClose : Expected.ValueOrClose;
                    break;
                case TokenType.CloseObject or TokenType.CloseArray:
                    // A container can close immediately after its opener, or after a value.
                    var mayClose = expected is Expected.CommaOrClose or Expected.KeyOrClose or Expected.ValueOrClose;
                    if (!mayClose) return (Error(Unexpected()), keys, expected);
                    if (stack.Count == 0 || stack.Peek() != (token.Type == TokenType.CloseObject)) return (Error(ErrorKind.MismatchedBracket), keys, expected);
                    stack.Pop();
                    expected = AfterValue();
                    break;
                case TokenType.Colon:
                    if (expected != Expected.Colon) return (Error(Unexpected()), keys, expected);
                    expected = Expected.Value;
                    break;
                case TokenType.Comma:
                    if (expected != Expected.CommaOrClose) return (Error(Unexpected()), keys, expected);
                    expected = stack.Count > 0 && stack.Peek() ? Expected.Key : Expected.Value;
                    break;
            }
        }
        return (null, keys, expected);
    }
}
