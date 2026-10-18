// Handwired source: tests/json-search/edge-cases.ts (handWrittenDocuments). The documents at the limits of the
// programs: each length around the capacity and nesting around the maximum depth. Also texts whose matches touch
// the first and the last position, and characters past U+FFFF. Also the patterns at the limits of the search
// programs (edgePatterns) and the searches of each capacity (edgeSearches).

using System.Globalization;
using JsonSearch;

namespace JsonSearch.Tests;

internal sealed record EdgeDocument(string Name, string Text);

/// <summary>A search at the limits: a pattern, and the display kinds that the search reads, with a name.</summary>
internal sealed record EdgeSearch(string Pattern, string ScopeName, IReadOnlyCollection<DisplayKind> Kinds);

internal static class EdgeCases
{
    private const int Capacity = Kinds.Capacity;
    private const int MaximumDepth = Kinds.MaximumDepth;

    // 16 letters with a letter between each two. So a pattern that tells all 16 apart has 16 classes and 32
    // class boundaries, the capacities.
    public static readonly string[] IsolatedLetters = [.. "acegikmoqsuwyACE".Select(letter => letter.ToString())];

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, Math.Max(0, count)));

    private static string Nested(int depth, string inner = "") => Repeat("[", depth) + inner + Repeat("]", depth);

    private static string Objects(int depth, string inner = "1") => Repeat("{\"k\":", depth) + inner + Repeat("}", depth);

    // A text of exactly length code points that ends with ending, with padding in front.
    private static string EndingWith(int length, string ending, string padding = " ") =>
        Repeat(padding, length - CodePoints.Of(ending).Length) + ending;

    // JavaScript String.prototype.slice(0, length) on UTF-16 code units.
    private static string Slice(string text, int length) => text[..Math.Min(length, text.Length)];

    private static string Hex(int codePoint) => codePoint.ToString("x", CultureInfo.InvariantCulture);

    public static List<EdgeDocument> HandWritten()
    {
        var documents = new List<EdgeDocument>();
        void Add(string name, string text) => documents.Add(new EdgeDocument(name, text));
        Add("empty", "");
        // One code point: each ASCII character, and some others.
        for (var codePoint = 0; codePoint < 128; codePoint++) Add($"one code point, U+{codePoint:x4}", char.ConvertFromUtf32(codePoint));
        foreach (var character in new[] { "é", "字", "😀", "\ud800", "\udc00", "\U0010ffff", "\u2028", "\ufeff" })
        {
            Add($"one code point, U+{Hex(CodePoints.Of(character)[0])}", character);
        }

        // Exactly at the capacity, one below and one above.
        foreach (var length in new[] { Capacity - 1, Capacity, Capacity + 1, Capacity + 2 })
        {
            Add($"a string of {length} code points", "\"" + Repeat("a", length - 2) + "\"");
            Add($"an array of {length} code points", "[" + Repeat("1,", (length - 4) / 2) + (length % 2 == 0 ? "11" : "111") + "]");
            Add($"{length} spaces", Repeat(" ", length));
            Add($"a number at the last of {length} code points", EndingWith(length, "1"));
            Add($"an error at the last of {length} code points", EndingWith(length, "x"));
            Add($"brackets at the end of {length} code points", EndingWith(length, "[]"));
            Add($"an open bracket at the end of {length} code points", EndingWith(length, "[1"));
            Add($"a string of {length} emoji and quotes", "\"" + Repeat("😀", length - 2) + "\"");
            Add($"a lone surrogate at the last of {length} code points", "\"" + Repeat("a", length - 2) + "\ud800");
            Add($"an open string up to {length} code points", Slice("\"" + Repeat("the tide ", 60), length));
            Add($"an escape cut at {length} code points", "\"" + Repeat("a", length - 3) + "\\u00e9\"");
            Add($"a key and a value up to {length} code points", Slice("{\"the pier\": \"" + Repeat("an owl ", 80), length - 2) + "\"}");
        }
        Add("a text of 2,000 code points", "[" + Repeat("\"the tide\", ", 180) + "1]");
        Add("a text of 1,200 emoji", Repeat("😀", 1200));

        // Nesting around the maximum depth of 16.
        foreach (var depth in new[] { 1, 2, MaximumDepth - 1, MaximumDepth, MaximumDepth + 1, MaximumDepth + 2, 40 })
        {
            Add($"arrays nested {depth} deep", Nested(depth));
            Add($"arrays nested {depth} deep around a number", Nested(depth, "1"));
            Add($"objects nested {depth} deep", Objects(depth));
            var turns = string.Concat(Enumerable.Range(0, depth).Select(level => level % 2 == 0 ? "{\"k\":" : "["))
                + "\"the tide\""
                + string.Concat(Enumerable.Range(0, depth).Select(level => (depth - 1 - level) % 2 == 0 ? "}" : "]"));
            Add($"objects and arrays in turn, {depth} deep", turns);
            Add($"{depth} openers at the end of {Capacity} code points", EndingWith(Capacity, Repeat("[", depth)));
            Add($"{depth} openers at the end of {Capacity + 1} code points", EndingWith(Capacity + 1, Repeat("[", depth)));
            Add($"{depth} braces, closed by brackets", Repeat("{\"k\":", depth) + "1" + Repeat("]", depth));
        }
        Add($"{MaximumDepth} deep with {Capacity} code points", Repeat("[", MaximumDepth) + Repeat(" ", Capacity - (2 * MaximumDepth)) + Repeat("]", MaximumDepth));
        Add($"{MaximumDepth + 1} deep with {Capacity} code points", Repeat("[", MaximumDepth + 1) + Repeat(" ", Capacity - (2 * MaximumDepth) - 2) + Repeat("]", MaximumDepth + 1));
        // Closers below depth 0, and openers that never close: after the first error the totals continue.
        Add("closers only", Repeat("]", Capacity));
        Add("closing braces only", Repeat("}", Capacity));
        Add("openers only", Repeat("[", Capacity));
        Add("opening braces only", Repeat("{", Capacity));
        Add("closers, then openers", Repeat("]", 40) + Repeat("[", 40) + Repeat("}", 40) + Repeat("{", 40));
        // The largest totals of the container code: a brace at depth 15 adds 2^15, and a bracket closes it without a subtraction.
        Add("braces at depth 15, closed by brackets", Repeat("[", MaximumDepth - 1) + Repeat("{]", (Capacity - MaximumDepth) / 2));
        Add("brackets at depth 15, closed by braces", Repeat("[", MaximumDepth - 1) + Repeat("[}", (Capacity - MaximumDepth) / 2));
        Add("objects in an array up to the capacity", "[" + Repeat("{},", 169) + "{}] ");
        Add("16 braces and 16 brackets in turn", Repeat(Repeat("{", MaximumDepth) + Repeat("]", MaximumDepth), 16));

        // Matches at the first and the last position, and a match that the capacity cuts.
        foreach (var length in new[] { Capacity - 1, Capacity, Capacity + 1 })
        {
            Add($"{length} times a", Repeat("a", length));
            Add($"b, then {length - 1} times a", "b" + Repeat("a", length - 1));
            Add($"{length - 1} times a, then b", Repeat("a", length - 1) + "b");
            Add($"the tide at both ends of {length} code points", "the tide" + EndingWith(length - 8, "an owl"));
            Add($"the tide in quotes at both ends of {length} code points", "[\"the tide\"," + EndingWith(length - 12, "\"an owl\"]"));
            Add($"a date at the end of {length} code points", EndingWith(length, "2026-10-13", "1"));
            Add($"abcdefg at the end of {length} code points", EndingWith(length, "abcdefg", "x"));
            Add($"the isolated letters up to {length} code points", Slice(Repeat(string.Concat(IsolatedLetters), 40), length));
            Add($"{length} digits", Slice(Repeat("0123456789", 60), length));
        }
        Add("emoji around a match", "\"" + Repeat("😀", 200) + " the tide " + Repeat("😀", 200) + " an owl\"");
        Add("an escape inside a string value and a key", "{\"the\\ntide\": \"an\\towl the pier\", \"the pier\": [\"a\\u00e9b the tide\", \"an owl\"]}");
        Add("the same words as key, value, and outside strings", "{\"the tide\": \"the tide\", \"a\": the tide}");
        Add("numbers and literals", "[12.5, -3e10, true, false, null, 2026-10-13, 0.5, 1e, tru]");
        Add("a raw line feed in a string", "[\"the tide\nan owl\", \"the pier\"]");
        Add("every code point at a class boundary", string.Concat(IsolatedLetters.Select(letter => $"{(char)(letter[0] - 1)}{letter}{(char)(letter[0] + 1)}")));
        return documents;
    }

    // Atom k holds the isolated letters whose number, from 1, has bit k set.
    private static string LettersOfBit(int bit) => string.Concat(IsolatedLetters.Where((_, index) => (((index + 1) >> bit) & 1) == 1));

    // One more state that reads any of the 16 letters, which adds no class and no boundary.
    private static string AnyIsolatedLetter => "[" + string.Concat(IsolatedLetters) + "]";

    /// <summary>A pattern that tells the 16 isolated letters apart: 16 classes and 32 class boundaries, the capacities.</summary>
    public static string FullClassesPattern =>
        $"([{LettersOfBit(0)}]|[{LettersOfBit(1)}]|[{LettersOfBit(2)}]|[{LettersOfBit(3)}]|[{LettersOfBit(4)}]|a|c)+";

    /// <summary>
    /// Patterns at the limits of each capacity: as many states, classes and class boundaries as a program holds.
    /// Also patterns that match nothing, each code point or the whole text. Also patterns that can match the empty
    /// text, which the search never reports.
    /// </summary>
    public static IReadOnlyList<string> EdgePatterns(int states) => states switch
    {
        8 => ["a", "abcdefg", "(the|an?) [a-z]+", FullClassesPattern, "[]", "[^]", "[^]+", ".+", "", "a*", "(a|ab)(c|bcd)", "\"[a-z ]+\"", "\\d+", "😀+", "[^a]+", "tide", "\"", "\\d\\d\\d\\d-"],
        16 => [new string('a', 15), "(the|an?|some) [a-z]+(ing)?s?", "\\d\\d\\d\\d-\\d\\d-\\d\\d", "the tide", FullClassesPattern + Repeat(AnyIsolatedLetter, 8)],
        32 => [new string('a', 31), "(the|an?|some|this|that|these) [a-z]+(ing|ed|s)? ", "connection reset", FullClassesPattern + Repeat(AnyIsolatedLetter, 24)],
        _ => throw new ArgumentException($"no search program has {states} states", nameof(states)),
    };

    /// <summary>
    /// Each pattern with all kinds, and then the other scopes in turn, so that each scope meets some patterns. The
    /// last two scopes are lists of kinds: numbers and literals, and no kind, which finds nothing.
    /// </summary>
    public static IReadOnlyList<EdgeSearch> EdgeSearches(int states)
    {
        (string Name, DisplayKind[] Kinds)[] others =
        [
            ("strings", [DisplayKind.String]),
            ("keys", [DisplayKind.Key]),
            ("strings and keys", [DisplayKind.String, DisplayKind.Key]),
            ("numbers and literals", [DisplayKind.Number, DisplayKind.Literal]),
            ("no kind", []),
        ];
        return [.. EdgePatterns(states).SelectMany((pattern, index) => new[]
        {
            new EdgeSearch(pattern, "everything", Enum.GetValues<DisplayKind>()),
            new EdgeSearch(pattern, others[index % others.Length].Name, others[index % others.Length].Kinds),
        })];
    }
}
