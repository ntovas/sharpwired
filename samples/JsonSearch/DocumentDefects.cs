// Handwired source: src/json-search/documents.ts (scanTokens, cut, previousNonSpace, nextNonSpace, applyDefect).
// The defects of a valid text: the places of its tokens, and one defect at a random place. The indices are
// UTF-16 code unit indices, as in JavaScript, and "whitespace" is the set of the JavaScript class \s.
// TooDeep and TooLong need other texts, so Documents.DocumentAt makes them.

namespace JsonSearch;

internal static class DocumentDefects
{
    private sealed class StringToken(int open, int close)
    {
        public int Open { get; } = open;

        public int Close { get; } = close;

        public bool IsKey { get; set; }
    }

    private sealed record Tokens(
        List<StringToken> Strings, List<int> Colons, List<int> Commas, List<int> Closers, List<(int Start, string Word)> Literals, List<int> Numbers);

    // The places of the tokens of a valid JSON text.
    private static Tokens ScanTokens(string text)
    {
        var tokens = new Tokens([], [], [], [], [], []);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                var open = index;
                for (index++; text[index] != '"'; index++)
                {
                    if (text[index] == '\\') index++;
                }
                tokens.Strings.Add(new StringToken(open, index));
            }
            else if (character == ':')
            {
                tokens.Colons.Add(index);
                tokens.Strings[^1].IsKey = true;
            }
            else if (character == ',')
            {
                tokens.Commas.Add(index);
            }
            else if (character is '}' or ']')
            {
                tokens.Closers.Add(index);
            }
            else if (character is 't' or 'f' or 'n')
            {
                var word = character == 't' ? "true" : character == 'f' ? "false" : "null";
                tokens.Literals.Add((index, word));
                index += word.Length - 1;
            }
            else if (character == '-' || char.IsAsciiDigit(character))
            {
                tokens.Numbers.Add(index);
                while (index + 1 < text.Length && "-+.eE0123456789".Contains(text[index + 1], StringComparison.Ordinal)) index++;
            }
        }
        return tokens;
    }

    private static string Cut(string text, int from, int to, string insert = "") => text[..from] + insert + text[to..];

    // The JavaScript class \s: the whitespace of ECMAScript and the line terminators.
    private static bool IsSpace(char character) =>
        (int)character is 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x20 or 0xa0 or 0x1680 or (>= 0x2000 and <= 0x200a)
            or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 or 0xfeff;

    private static int PreviousNonSpace(string text, int index)
    {
        var previous = index - 1;
        while (previous >= 0 && IsSpace(text[previous])) previous--;
        return previous;
    }

    private static int NextNonSpace(string text, int index)
    {
        var next = index + 1;
        while (next < text.Length && IsSpace(text[next])) next++;
        return next;
    }

    /// <summary>One defect in a valid text, or null when the text has no place for it, for example no literal to misspell.</summary>
    public static string? Apply(DocumentRandom random, DocumentDefect defect, string text)
    {
        var tokens = ScanTokens(text);
        switch (defect)
        {
            case DocumentDefect.MissingQuote:
            {
                if (tokens.Strings.Count == 0) return null;
                var token = random.Pick(tokens.Strings);
                var quote = random.Chance(0.5) ? token.Open : token.Close;
                return Cut(text, quote, quote + 1);
            }
            case DocumentDefect.TrailingComma:
            {
                // Only before the closer of a container that has an item, for example not in [].
                var places = tokens.Closers.Select(closer => PreviousNonSpace(text, closer)).Where(previous => previous >= 0 && text[previous] != '{' && text[previous] != '[').ToList();
                if (places.Count == 0) return null;
                var place = random.Pick(places);
                return Cut(text, place + 1, place + 1, ",");
            }
            case DocumentDefect.BadEscape:
            {
                if (tokens.Strings.Count == 0) return null;
                var open = random.Pick(tokens.Strings).Open;
                return Cut(text, open + 1, open + 1, random.Pick(["\\q", "\\x41", "\\'", "\\a", "\\u12G4", "\\U0041"]));
            }
            case DocumentDefect.MissingColon:
            {
                if (tokens.Colons.Count == 0) return null;
                var colon = random.Pick(tokens.Colons);
                return Cut(text, colon, colon + 1, random.Chance(0.5) ? " " : "");
            }
            case DocumentDefect.UnquotedKey:
            {
                var keys = tokens.Strings.Where(token => token.IsKey).ToList();
                if (keys.Count == 0) return null;
                var key = random.Pick(keys);
                return Cut(Cut(text, key.Close, key.Close + 1), key.Open, key.Open + 1);
            }
            case DocumentDefect.TruncatedText:
                return text[..random.Integer(1, text.Length - 1)];
            case DocumentDefect.SingleQuotes:
            {
                if (tokens.Strings.Count == 0) return null;
                var token = random.Pick(tokens.Strings);
                return Cut(Cut(text, token.Close, token.Close + 1, "'"), token.Open, token.Open + 1, "'");
            }
            case DocumentDefect.BadLiteral:
            {
                if (tokens.Literals.Count == 0) return null;
                var (start, word) = random.Pick(tokens.Literals);
                string[] replacements = word switch
                {
                    "true" => ["True", "TRUE", "tru", "yes"],
                    "false" => ["False", "FALSE", "fals", "no"],
                    _ => ["None", "nil", "NULL", "undefined", "NaN"],
                };
                return Cut(text, start, start + word.Length, random.Pick(replacements));
            }
            case DocumentDefect.BadNumber:
            {
                if (tokens.Numbers.Count == 0) return null;
                var start = random.Pick(tokens.Numbers);
                if (random.Chance(0.5)) return Cut(text, start, start, "+");
                var digit = text[start] == '-' ? start + 1 : start;
                return Cut(text, digit, digit, "0");
            }
            case DocumentDefect.MissingComma:
            {
                // Only before a string or a container: without the comma, [1,2] is the valid [12].
                var places = tokens.Commas.Where(comma => NextNonSpace(text, comma) is var next && next < text.Length && text[next] is '"' or '{' or '[').ToList();
                if (places.Count == 0) return null;
                var comma = random.Pick(places);
                return Cut(text, comma, comma + 1);
            }
            case DocumentDefect.MismatchedBracket:
            {
                var closer = random.Pick(tokens.Closers);
                return Cut(text, closer, closer + 1, text[closer] == '}' ? "]" : "}");
            }
            case DocumentDefect.ControlCharacter:
            {
                if (tokens.Strings.Count == 0) return null;
                var open = random.Pick(tokens.Strings).Open;
                return Cut(text, open + 1, open + 1, random.Pick(["\n", "\t", "\u0000", "\u001b"]));
            }
            case DocumentDefect.Comment:
                return random.Chance(0.5) ? "// generated\n" + text : Cut(text, 1, 1, "/* v2 */");
            case DocumentDefect.ExtraText:
                return text + random.Pick(["}", "]", ",", " {}", "\n[]", " null", ";"]);
            default:
                return null;
        }
    }
}
