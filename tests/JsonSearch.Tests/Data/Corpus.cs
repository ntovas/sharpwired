// Handwired source: tests/json-search/highlighter/corpus.ts. The test texts: edge cases written by hand,
// random JSON, and random edits of random JSON. The random choices use mulberry32 (Js.CreateRandom), with
// the same calls in the same order as corpus.ts, so the seed 20260917 gives the 2,509 Handwired texts.

using System.Globalization;
using System.Text;
using JsonSearch;

namespace JsonSearch.Tests;

internal static class Corpus
{
    public const int DefaultSeed = 20260917;

    private const int Capacity = Kinds.Capacity;
    private const int MaximumDepth = Kinds.MaximumDepth;

    // The corpus of the highlighter tests: 129 edge cases, 700 random texts, 1,600 edited texts and 80 long texts.
    private static readonly Lazy<string[]> DefaultCorpus = new(() => Create(DefaultSeed, 700, 1600, 80));

    public static IReadOnlyList<string> All => DefaultCorpus.Value;

    private static string Nested(int depth, string inner = "") => Repeat("[", depth) + inner + Repeat("]", depth);

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    public static readonly IReadOnlyList<string> EdgeCases =
    [
        // Valid texts.
        "0", "-0", "12", "-12.5", "1e5", "1E+5", "2.5e-3", "0.0", "-0.0e0", "true", "false", "null", "\"\"", "\"abc\"", "[]", "{}",
        " [ ] ", "\t{\r\n}\n", "[1,2,3]", "{\"a\":1,\"b\":[true,false,null]}", "{\"a\":{\"b\":{\"c\":[{}]}}}", "[[],[[]],{}]",
        "\"\\\" \\\\ \\/ \\b \\f \\n \\r \\t\"", "\"\\u00e9\\uD83D\\uDE00\\uABCD\\uabcd\"", "\"é字😀\u2028\u007f\"", "\"\ud800\"",
        "{\"\":\"\"}", "[-1,0.5,1e10,\"x\"]", Nested(16), Nested(16, "1"), $"{{\"a\":{Nested(15)}}}",
        // Numbers.
        "01", "-01", "00", "1.", ".5", "+1", "1e", "1e+", "1e-x", "-", "--1", "-a", "1.2.3", "1e5e", "0x10", "1_000", "1.e5",
        "NaN", "Infinity", "-Infinity",
        // Literals.
        "tru", "trux", "true1", "truefalse", "nul", "nulll", "fals", "f", "True", "undefined",
        // Strings.
        "\"abc", "\"\\q\"", "\"\\", "\"\\u12\"", "\"\\u12x4\"", "\"\\u\"", "\"a\tb\"", "\"a\nb\"", "[\"a\nb\"]", "\"\u0000\"", "'a'", "\"\\U0041\"",
        // The grammar between tokens.
        "[1 2]", "{\"a\" 1}", "{\"a\":}", "{\"a\"}", "{1:2}", "{,}", "[,1]", "[1,]", "{\"a\":1,}", "[1}", "{]", "{\"a\":1]", "[{]}",
        "1 1", "1]", "]", "}", ":", ",", "1,2", "[1,2]]", "{\"a\":1}}", "[:]", "{\"a\"::1}", "{\"a\":1:2}", "[\"a\":1]", "{\"a\",1}",
        "[[1]", "[", "{", "{\"a\":", "{\"a\":1", "[1,", "", " ", "\n",
        // Characters that start no token, and the line feed that starts the scan again.
        "[x]", "/* */", "\ufeff1", "[1]\u00a0", "[x\n1]", "[x\n1, 2]", "{\"a\":\u0001}", "[1,\nx y z\n2]", "[\"ok\"\n\"a\\qb\"\n]",
        // Nesting past the maximum depth.
        Nested(17), Nested(MaximumDepth + 1, "1"), $"{{\"a\":{Nested(16)}}}", Nested(40),
        // The capacity.
        $"[{Repeat("1,", 254)}1]", $"[{Repeat(" ", Capacity - 2)}]", $"[{Repeat(" ", Capacity - 1)}]", $"\"{Repeat("a", Capacity)}\"",
        $"{Repeat(" ", Capacity)}1", $"[{Repeat("\"é字\",", 80)}1]", $"x{Repeat(" ", Capacity + 20)}",
    ];

    private static readonly string[] StringParts = ["a", "id", "name", "x y", " ", "true", "{", "]", ":", ",", "0"];
    private static readonly string[] Escapes = ["\\\"", "\\\\", "\\/", "\\b", "\\f", "\\n", "\\r", "\\t"];
    private static readonly string[] HexDigits = [.. "0123456789abcdefABCDEF".Select(character => character.ToString())];
    private static readonly string[] Others = ["é", "字", "😀", "\u2028", "\u007f", "\ud800"];
    private static readonly string[] Spaces = [" ", "  ", "\n", "\t", "\r\n", "\n  "];
    private static readonly string[] EditAlphabet =
        [.. "{}[]:,\"\\/ -+.0123456789eEtrufalsnxbu\n\t\r".Select(character => character.ToString()), "é", "😀", "\u0000", "\u001f"];

    private static T Pick<T>(Func<double> random, IReadOnlyList<T> items) => items[(int)Math.Floor(random() * items.Count)];

    private static int Count(Func<double> random, int minimum, int maximum) => minimum + (int)Math.Floor(random() * (maximum - minimum + 1));

    private static string RandomDigits(Func<double> random, int minimum, int maximum)
    {
        var length = Count(random, minimum, maximum);
        var text = new StringBuilder();
        for (var digit = 0; digit < length; digit++) text.Append(((int)Math.Floor(random() * 10)).ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    private static string RandomNumber(Func<double> random)
    {
        var text = random() < 0.3 ? "-" : "";
        text += random() < 0.2 ? "0" : Count(random, 1, 9).ToString(CultureInfo.InvariantCulture) + RandomDigits(random, 0, 4);
        if (random() < 0.3) text += "." + RandomDigits(random, 1, 3);
        if (random() < 0.2) text += Pick(random, ["e", "E"]) + Pick(random, ["", "+", "-"]) + RandomDigits(random, 1, 3);
        return text;
    }

    private static string RandomString(Func<double> random)
    {
        var length = Count(random, 0, 6);
        var parts = new StringBuilder("\"");
        for (var part = 0; part < length; part++)
        {
            var roll = random();
            if (roll < 0.4) parts.Append(Pick(random, StringParts));
            else if (roll < 0.6) parts.Append(Pick(random, Escapes));
            else if (roll < 0.75) parts.Append("\\u").Append(Pick(random, HexDigits)).Append(Pick(random, HexDigits)).Append(Pick(random, HexDigits)).Append(Pick(random, HexDigits));
            else parts.Append(Pick(random, Others));
        }
        return parts.Append('"').ToString();
    }

    private static string RandomWhitespace(Func<double> random) => random() < 0.6 ? "" : Pick(random, Spaces);

    private static string RandomValue(Func<double> random, int depth, int maximumNesting)
    {
        var roll = random();
        if (depth < maximumNesting && roll < 0.35)
        {
            var items = Count(random, 0, 4);
            if (random() < 0.5)
            {
                // JS: `[${space()}${items.map(() => value + space()).join(`,${space()}`)}]`, in this order.
                var open = RandomWhitespace(random);
                var values = new List<string>();
                for (var item = 0; item < items; item++) values.Add(RandomValue(random, depth + 1, maximumNesting) + RandomWhitespace(random));
                var separator = "," + RandomWhitespace(random);
                return "[" + open + string.Join(separator, values) + "]";
            }
            var start = RandomWhitespace(random);
            var members = new List<string>();
            for (var item = 0; item < items; item++)
            {
                var key = RandomString(random);
                var beforeColon = RandomWhitespace(random);
                var afterColon = RandomWhitespace(random);
                var value = RandomValue(random, depth + 1, maximumNesting);
                members.Add(key + beforeColon + ":" + afterColon + value + RandomWhitespace(random));
            }
            var comma = "," + RandomWhitespace(random);
            return "{" + start + string.Join(comma, members) + "}";
        }
        if (roll < 0.6) return RandomNumber(random);
        if (roll < 0.85) return RandomString(random);
        return Pick(random, ["true", "false", "null"]);
    }

    private static string RandomJson(Func<double> random)
    {
        // Mostly shallow texts, and some chains of containers near the maximum depth.
        if (random() < 0.08)
        {
            var count = Count(random, MaximumDepth - 2, MaximumDepth + 3);
            var openers = new bool[count];
            for (var level = 0; level < count; level++) openers[level] = random() < 0.5;
            // Each object opens with a key and closes with a brace, and 1 chain in 5 closes each container with one kind.
            var matching = random() < 0.8;
            var closers = openers.Select(isObject => (matching ? isObject : random() < 0.5) ? "}" : "]").ToList();
            closers.Reverse();
            return string.Concat(openers.Select(isObject => isObject ? "{\"k\":" : "[")) + RandomValue(random, 0, 1) + string.Concat(closers);
        }
        var before = RandomWhitespace(random);
        var nesting = Count(random, 1, 5);
        var value = RandomValue(random, 0, nesting);
        return before + value + RandomWhitespace(random);
    }

    // 1 to 3 random edits: insert, delete or replace a code point, copy a slice, or cut the text.
    private static string Mutate(Func<double> random, string text)
    {
        var codePoints = CodePoints.Split(text);
        var edits = Count(random, 1, 3);
        for (var edit = 0; edit < edits; edit++)
        {
            var position = (int)Math.Floor(random() * (codePoints.Count + 1));
            var roll = random();
            if (roll < 0.3)
            {
                codePoints.Insert(position, Pick(random, EditAlphabet));
            }
            else if (roll < 0.55)
            {
                if (position < codePoints.Count) codePoints.RemoveAt(position);
            }
            else if (roll < 0.8)
            {
                var replacement = Pick(random, EditAlphabet);
                if (position < codePoints.Count) codePoints[position] = replacement;
                else codePoints.Add(replacement);
            }
            else if (roll < 0.9)
            {
                var end = Math.Min(codePoints.Count, position + Count(random, 1, 8));
                codePoints.InsertRange(position, codePoints.GetRange(position, end - position));
            }
            else
            {
                codePoints = codePoints.GetRange(0, position);
            }
        }
        return string.Concat(codePoints);
    }

    // Longer texts, from below the capacity to past it.
    private static string RandomLongJson(Func<double> random)
    {
        var items = new List<string>();
        var length = 2;
        var target = Count(random, Capacity - 40, Capacity + 60);
        while (length < target)
        {
            var item = RandomValue(random, 1, 3);
            items.Add(item);
            length += CodePoints.Of(item).Length + 1;
        }
        return "[" + string.Join(",", items) + "]";
    }

    public static string[] Create(int seed, int randomCount, int mutatedCount, int longCount)
    {
        var random = Js.CreateRandom(seed);
        var texts = new List<string>(EdgeCases);
        for (var index = 0; index < randomCount; index++) texts.Add(RandomJson(random));
        for (var index = 0; index < mutatedCount; index++) texts.Add(Mutate(random, RandomJson(random)));
        for (var index = 0; index < longCount; index++) texts.Add(random() < 0.5 ? RandomLongJson(random) : Mutate(random, RandomLongJson(random)));
        return [.. texts];
    }
}

/// <summary>The code points of a text as JavaScript Array.from reads them: a surrogate pair is one, a lone surrogate is its own.</summary>
internal static class CodePoints
{
    public static List<string> Split(string text)
    {
        var parts = new List<string>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                parts.Add(text.Substring(index, 2));
                index++;
            }
            else
            {
                parts.Add(text[index].ToString());
            }
        }
        return parts;
    }

    public static int[] Of(string text)
    {
        var values = new List<int>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                values.Add(char.ConvertToUtf32(text[index], text[index + 1]));
                index++;
            }
            else
            {
                values.Add(text[index]);
            }
        }
        return [.. values];
    }
}
