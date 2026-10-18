// Handwired source: tests/json-search/highlighter/short-texts.ts. Short texts that put the highlighter in each
// of its situations, written from the grammar of JSON and not from the tables of the program: each tokenizer
// state, then each kind of character, then one more character that shows the next state of the tokenizer; each
// ASCII character in the places where its kind matters; and short texts in containers at each nesting depth.
// The random texts of the corpus reach only some of these pairs, for example `nu1`, a digit in null, or `-]`, a
// bracket after a minus sign.

namespace JsonSearch.Tests;

internal static class ShortTexts
{
    // One character of each kind that the grammar of JSON tells apart, and three past ASCII.
    private static readonly string[] Characters =
    [
        " ", "\t", "\r", "\n", "\u0001", "\"", "\\", "/", "{", "}", "[", "]", ":", ",", "-", "+", ".", "0", "7",
        "a", "b", "e", "E", "f", "l", "n", "r", "s", "t", "u", "c", "D", "x", "\u007f", "é", "😀",
    ];

    // A start of a token that leaves the tokenizer in each of its states, for example "\u00 in a \u escape and 1e+
    // after the sign of an exponent, and some whole tokens.
    private static readonly string[] TokenStarts =
    [
        "", "\"", "\"\\", "\"\\u", "\"\\u0", "\"\\u00", "\"\\u000", "\"\\u0000", "\"a", "\"a\"", "-", "0", "1", "12", "1.", "1.5", "1e",
        "1e+", "1e5", "-0", "t", "tr", "tru", "true", "f", "fa", "fal", "fals", "false", "n", "nu", "nul", "null", "x", "x\n", " ",
    ];

    // What comes before the token: nothing, a value of an array, and an open object.
    private static readonly string[] TokenContexts = ["", "[1", "{"];

    // What comes after: the end of the text, a line feed, and one more token.
    private static readonly string[] TokenTails = ["", "\n", " 1"];

    // One more character immediately after, which continues some tokens and breaks others.
    private static readonly string[] NextCharacters = ["0", ".", "e", "+", "l", "u", "r", "s", "a", "\"", "\\", "{"];

    // Short texts that go in containers, some valid there and some not, for example a key without an object.
    private static readonly string[] InsideContainers = ["1", "\"a\"", "[]", "{}", "{\"a\":1}", "[1,2]", "true", "1,", "\"a\":1", "{\"a\":[", "[{", "{},{}", "[],[]", "{\"a\":{}}"];

    // Past the maximum depth of 16, so that the 17th and the 18th opener are there too.
    private const int DeepestNesting = 18;

    // depth openers and their closers: arrays, objects, and both alternations.
    private static (string Open, string Close)[] Nestings(int depth)
    {
        (string, string) Alternating(bool firstIsArray)
        {
            var isArray = Enumerable.Range(0, depth).Select(level => (level % 2 == 0) == firstIsArray).ToArray();
            return (string.Concat(isArray.Select(array => array ? "[" : "{\"k\":")), string.Concat(Enumerable.Reverse(isArray).Select(array => array ? "]" : "}")));
        }
        return
        [
            (new string('[', depth), new string(']', depth)),
            (string.Concat(Enumerable.Repeat("{\"k\":", depth)), new string('}', depth)),
            Alternating(true),
            Alternating(false),
        ];
    }

    public static List<string> Create()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var texts = new List<string>();
        void Add(string text)
        {
            if (seen.Add(text)) texts.Add(text);
        }
        string[] withNone = ["", .. Characters];
        // Each tokenizer state, then each kind of character.
        foreach (var context in TokenContexts)
        {
            foreach (var start in TokenStarts)
            {
                foreach (var character in withNone)
                {
                    foreach (var tail in TokenTails) Add(context + start + character + tail);
                }
            }
        }
        // The same pairs, then one more character.
        foreach (var start in TokenStarts)
        {
            foreach (var character in Characters)
            {
                foreach (var next in NextCharacters) Add(start + character + next);
            }
        }
        // Each ASCII character and U+0080 where a token starts, in a string, after a backslash, in a \u escape, in
        // a number and in a literal.
        for (var codePoint = 0; codePoint <= 0x80; codePoint++)
        {
            foreach (var start in new[] { "", "\"", "\"\\", "\"\\u0", "0", "1e", "t" })
            {
                foreach (var tail in new[] { "", " 1" }) Add(start + char.ConvertFromUtf32(codePoint) + tail);
            }
        }
        for (var depth = 0; depth <= DeepestNesting; depth++)
        {
            foreach (var (open, close) in Nestings(depth))
            {
                // Each kind of character at this depth, in an array and in an object, alone and after one more opener.
                foreach (var character in withNone)
                {
                    Add(open + character);
                    Add(open + character + close);
                    foreach (var opener in new[] { "[", "{" }) Add(open + opener + character);
                }
                // A short text at this depth, then the closers, one closer too few, one too many, a closer of
                // each kind, and a second value.
                foreach (var inside in InsideContainers)
                {
                    foreach (var tail in new[] { "", close, close.Length > 0 ? close[1..] : "", close + " ", close + "]", close + "}", "," + inside + close }) Add(open + inside + tail);
                }
            }
        }
        return texts;
    }
}
