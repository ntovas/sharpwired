// Handwired source: tests/json-search/regex/test-patterns.ts. The hand-written cases, and the seeded random
// patterns and texts of the search tests. The random source is Js.CreateRandom, the same generator as
// createRandom of Handwired, and each function calls it in the Handwired order.

using JsonSearch;

namespace JsonSearch.Tests;

internal static class TestPatterns
{
    /// <summary>A pattern, a text, and the matches that each version must find, as (start, end) pairs.</summary>
    public static readonly (string Pattern, string Text, (int Start, int End)[] Expected)[] HandWrittenCases =
    [
        ("a", "baaab", [(1, 2), (2, 3), (3, 4)]), // adjacent matches stay separate
        ("a*", "baab", [(1, 3)]), // the empty match is not reported
        ("", "abc", []),
        ("()*", "abc", []),
        ("a|ab", "xabx", [(1, 3)]), // longest, where the search of Regex returns a
        ("(a|ab)(c|bcd)", "abcd abc", [(0, 4), (5, 8)]),
        ("a+b|a", "aaac aab", [(0, 1), (1, 2), (2, 3), (5, 8)]), // a longest match needs code points past its end
        ("aba", "ababa", [(0, 3)]), // the overlapping aba at 2 is not reported
        ("a(bc)*", "abcbcbx", [(0, 5)]),
        (".+", "ab\ncd\re\u2028f", [(0, 2), (3, 5), (6, 7), (8, 9)]), // . does not match line terminators
        ("[^a]+", "b\ncad", [(0, 3), (4, 5)]), // a negated class does
        (".", "a\u2029b", [(0, 1), (2, 3)]),
        ("[^a]", "\U0010FFFF\U0010FFFE", [(0, 1), (1, 2)]), // up to the last code point
        ("[^\U0010FFFE]", "\U0010FFFE\U0010FFFF", [(1, 2)]), // a negated set whose last interval is only the last code point
        (".+", "\U0010FFFF", [(0, 1)]),
        ("\\w+", "-_aZ09-", [(1, 6)]),
        ("\\d+", "/0189:", [(1, 5)]),
        ("😀+", "x😀😀y😀", [(1, 3), (4, 5)]), // one code point in each cell
        ("[]", "abc", []),
        ("[^]", "ab", [(0, 1), (1, 2)]),
        ("[^]*", "ab\nc", [(0, 4)]),
        ("\\d+\\.\\d+", "pi 3.14, e 2.7.", [(3, 7), (11, 14)]),
        ("[a-c-]+", "x-ab-cd", [(1, 6)]),
        ("(x|y)*z", "xxyzyz zz", [(0, 4), (4, 6), (7, 8), (8, 9)]),
        ("abc", "ab", []), // a match that would continue past the end of the text
        ("b+", "aaaaaaaaaaaaaabb", [(14, 16)]), // a match that ends on the last cell
    ];

    private static readonly string[] AtomCharacters = ["a", "b", "c", "0", "\\.", "-", "\n", "é", "😀"];
    private static readonly string[] ClassCharacters = ["a", "b", "c", "0", "9", "é", "😀", "\U0010FFFE", "\n", ".", "\\-", "\\]"];

    // '9', 'Z', '_', U+2028, U+2029 and U+10FFFF are where \d, \w, . and negated classes start or stop.
    private static readonly string[] TextAlphabet = ["a", "b", "c", "0", "9", "Z", "_", "-", ".", "\n", "\r", "\u2028", "\u2029", "é", "😀", "\U0010FFFF", "x", " ", "]"];

    public static T Pick<T>(Func<double> random, IReadOnlyList<T> items) => items[(int)Math.Floor(random() * items.Count)];

    public static int[] CodePointsOf(string text) => CodePoints.Of(text);

    // Precedence: 0 alternation, 1 sequence, 2 repeat, 3 atom or group.
    private readonly record struct Rendered(string Source, int Precedence);

    private static string Wrap(Rendered node, int minimum) => node.Precedence >= minimum ? node.Source : $"({node.Source})";

    private static int FirstCodePoint(string text) => CodePoints.Of(text)[0];

    private static Rendered RandomAtom(Func<double> random)
    {
        var roll = random();
        if (roll < 0.4) return new Rendered(Pick(random, AtomCharacters), 3);
        if (roll < 0.52) return new Rendered(".", 3);
        if (roll < 0.58) return new Rendered(Pick(random, new[] { "\\d", "\\w" }), 3);
        if (roll < 0.62) return new Rendered("()", 3);
        var count = (int)Math.Floor(random() * 4);
        var items = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var low = Pick(random, ClassCharacters);
            if (random() < 0.3 && !low.StartsWith('\\'))
            {
                var high = Pick(random, ClassCharacters.Where(character => !character.StartsWith('\\') && FirstCodePoint(character) >= FirstCodePoint(low)).ToArray());
                items.Add($"{low}-{high}");
            }
            else
            {
                items.Add(random() < 0.1 ? "\\d" : low);
            }
        }
        return new Rendered($"[{(random() < 0.3 ? "^" : "")}{string.Concat(items)}]", 3);
    }

    private static Rendered RandomRendered(Func<double> random, int depth)
    {
        var roll = random();
        if (depth <= 0 || roll < 0.4) return RandomAtom(random);
        if (roll < 0.62)
        {
            var count = 2 + (int)Math.Floor(random() * 2);
            var parts = new List<string>();
            for (var index = 0; index < count; index++) parts.Add(Wrap(RandomRendered(random, depth - 1), 1));
            return new Rendered(string.Concat(parts), 1);
        }
        if (roll < 0.8)
        {
            var options = new List<string>();
            for (var index = 0; index < 2; index++) options.Add(random() < 0.1 ? "" : Wrap(RandomRendered(random, depth - 1), 1));
            return new Rendered(string.Join("|", options), 0);
        }
        var body = Wrap(RandomRendered(random, depth - 1), 3);
        return new Rendered(body + Pick(random, new[] { "?", "*", "+" }), 2);
    }

    /// <summary>A random pattern over a small alphabet with syntax characters, line terminators and an emoji.</summary>
    public static string RandomPattern(Func<double> random, int depth) => RandomRendered(random, depth).Source;

    /// <summary>A random text of 0 to maximumLength code points.</summary>
    public static string RandomText(Func<double> random, int maximumLength)
    {
        var length = (int)Math.Floor(random() * (maximumLength + 1));
        return string.Concat(Enumerable.Range(0, length).Select(_ => Pick(random, TextAlphabet)));
    }

    /// <summary>
    /// A text of the code points immediately below, at and above each class boundary of the pattern, and 0 and U+10FFFF.
    /// For example, for [a-c]+, a text over `, a, b, c, d, NUL and U+10FFFF.
    /// </summary>
    public static int[] BoundaryText(Func<double> random, CompiledPattern compiled, int length)
    {
        int[] near = [.. new[] { 0, PatternCompiler.MaxCodePoint }
            .Concat(compiled.Boundaries.SelectMany(boundary => new[] { boundary - 1, boundary, boundary + 1 }))
            .Where(codePoint => codePoint <= PatternCompiler.MaxCodePoint)];
        return [.. Enumerable.Range(0, length).Select(_ => Pick(random, near))];
    }

    /// <summary>A text of exactly length code points over the random text alphabet.</summary>
    public static int[] FullText(Func<double> random, int length) =>
        [.. Enumerable.Range(0, length).Select(_ => FirstCodePoint(Pick(random, TextAlphabet)))];

    /// <summary>The matches as a text of (start,end) pairs, for an assert with a readable message.</summary>
    public static string Pairs(IEnumerable<Match> matches) => Show(matches.Select(match => (match.Start, match.End)));

    /// <summary>The pairs as a text, for example "(1,2) (4,6)".</summary>
    public static string Show(IEnumerable<(int Start, int End)> pairs) => string.Join(" ", pairs.Select(pair => $"({pair.Start},{pair.End})"));

    /// <summary>The code points as a text of numbers, for an assert message.</summary>
    public static string Show(IEnumerable<int> codePoints) => string.Join(",", codePoints);
}
