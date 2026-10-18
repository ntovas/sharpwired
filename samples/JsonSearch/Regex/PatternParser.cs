// Handwired source: src/json-search/regex/pattern.ts (the sets of code points, PatternNode, parsePattern). The
// parser of the pattern language. The language is a subset of the syntax of JavaScript RegExp with the u flag:
// - A literal code point, for example a or é. The syntax characters ^ $ \ . * + ? ( ) [ ] { } | need a
//   backslash, for example \. A backslash before / is also accepted.
// - . matches each code point except the line terminators \n, \r, U+2028 and U+2029.
// - \d and \w are the ASCII digits and word characters.
// - Character classes with ranges and negation, for example [a-z_] or [^0-9]. [] matches nothing, and [^]
//   matches each code point. In a class, \- is a literal -, and a - first or last is literal.
// - ?, * and + after a literal, a class or a group.
// - | between alternatives, and ( ) groups.
// A lazy quantifier such as *?, a counted repeat such as {2}, an anchor, another escape and a group such as (?:
// throw a PatternSyntaxException with the position of the code point where the parser found the error.

using System.Collections.Immutable;

namespace JsonSearch;

/// <summary>An inclusive range of code points.</summary>
public readonly record struct CodePointRange(int Low, int High);

/// <summary>A pattern outside the language. Position is the index of the code point where the parser found the error.</summary>
public sealed class PatternSyntaxException(string message, int position) : ArgumentException(message)
{
    /// <summary>The index of the code point where the parser found the error.</summary>
    public int Position { get; } = position;
}

/// <summary>A node of a parsed pattern.</summary>
internal abstract record PatternNode
{
    /// <summary>One code point of a set.</summary>
    public sealed record Atom(ImmutableArray<CodePointRange> Set) : PatternNode;

    /// <summary>The parts in order. An empty sequence matches the empty text, for example () or the right side of a|.</summary>
    public sealed record Sequence(ImmutableArray<PatternNode> Parts) : PatternNode;

    /// <summary>One of the options.</summary>
    public sealed record Alternation(ImmutableArray<PatternNode> Options) : PatternNode;

    /// <summary>? is optional, + repeats, and * is both.</summary>
    public sealed record Repeat(PatternNode Body, bool Optional, bool Repeats) : PatternNode;
}

/// <summary>Sets of code points: sorted ranges with a gap between neighbors.</summary>
internal static class CodePointSets
{
    public static readonly ImmutableArray<CodePointRange> Dot = [new(0, 9), new(11, 12), new(14, 0x2027), new(0x202a, PatternCompiler.MaxCodePoint)];
    public static readonly ImmutableArray<CodePointRange> Digit = [new('0', '9')];
    public static readonly ImmutableArray<CodePointRange> Word = [new('0', '9'), new('A', 'Z'), new('_', '_'), new('a', 'z')];

    /// <summary>The ranges sorted, with each two ranges that touch or overlap merged.</summary>
    public static ImmutableArray<CodePointRange> Normalize(IEnumerable<CodePointRange> ranges)
    {
        var merged = new List<CodePointRange>();
        foreach (var range in ranges.OrderBy(range => range.Low))
        {
            if (merged.Count > 0 && range.Low <= merged[^1].High + 1) merged[^1] = merged[^1] with { High = Math.Max(merged[^1].High, range.High) };
            else merged.Add(range);
        }
        return [.. merged];
    }

    /// <summary>The code points of 0..U+10FFFF that the set does not hold.</summary>
    public static ImmutableArray<CodePointRange> Complement(ImmutableArray<CodePointRange> set)
    {
        var gaps = new List<CodePointRange>();
        var next = 0;
        foreach (var range in set)
        {
            if (range.Low > next) gaps.Add(new CodePointRange(next, range.Low - 1));
            next = range.High + 1;
        }
        if (next <= PatternCompiler.MaxCodePoint) gaps.Add(new CodePointRange(next, PatternCompiler.MaxCodePoint));
        return [.. gaps];
    }

    public static bool Contains(ImmutableArray<CodePointRange> set, int codePoint)
    {
        foreach (var range in set)
        {
            if (codePoint >= range.Low && codePoint <= range.High) return true;
        }
        return false;
    }
}

/// <summary>The parser of the pattern language (parsePattern of pattern.ts).</summary>
internal sealed class PatternParser
{
    private const string SyntaxCharacters = "^$\\.*+?()[]{}|";

    private readonly int[] codePoints;
    private int position;

    private PatternParser(string pattern)
    {
        var values = new List<int>(pattern.Length);
        for (var index = 0; index < pattern.Length; index++)
        {
            if (char.IsHighSurrogate(pattern[index]) && index + 1 < pattern.Length && char.IsLowSurrogate(pattern[index + 1]))
            {
                values.Add(char.ConvertToUtf32(pattern[index], pattern[index + 1]));
                index++;
            }
            else
            {
                values.Add(pattern[index]);
            }
        }
        codePoints = [.. values];
    }

    /// <summary>Parse the pattern. Throws PatternSyntaxException for a pattern outside the language.</summary>
    public static PatternNode Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var parser = new PatternParser(pattern);
        var node = parser.ParseAlternation();
        if (parser.position < parser.codePoints.Length) throw parser.Fail("this ) closes no group");
        return node;
    }

    private bool Is(char character) => position < codePoints.Length && codePoints[position] == character;

    private PatternSyntaxException Fail(string message, int? at = null) => new(message, at ?? position);

    private PatternNode ParseAlternation()
    {
        var options = new List<PatternNode> { ParseSequence() };
        while (Is('|'))
        {
            position++;
            options.Add(ParseSequence());
        }
        return options.Count == 1 ? options[0] : new PatternNode.Alternation([.. options]);
    }

    private PatternNode ParseSequence()
    {
        var parts = new List<PatternNode>();
        while (position < codePoints.Length && !Is('|') && !Is(')')) parts.Add(ParseRepeat());
        return parts.Count == 1 ? parts[0] : new PatternNode.Sequence([.. parts]);
    }

    private PatternNode ParseRepeat()
    {
        var body = ParseAtom();
        if (!(Is('?') || Is('*') || Is('+'))) return body;
        var quantifier = (char)codePoints[position];
        position++;
        if (Is('?')) throw Fail($"{quantifier}? is a lazy quantifier, which this language doesn’t have");
        if (Is('*') || Is('+')) throw Fail("nothing to repeat");
        return new PatternNode.Repeat(body, quantifier != '+', quantifier != '?');
    }

    private PatternNode ParseAtom()
    {
        var at = position;
        var codePoint = codePoints[position];
        position++;
        switch (codePoint)
        {
            case '(':
                {
                    if (Is('?')) throw Fail("groups such as (?: aren’t supported", at);
                    var inner = ParseAlternation();
                    if (!Is(')')) throw Fail("this group is never closed", at);
                    position++;
                    return inner;
                }
            case '[':
                return new PatternNode.Atom(ParseClass(at));
            case '.':
                return new PatternNode.Atom(CodePointSets.Dot);
            case '\\':
                return new PatternNode.Atom(ParseEscape(at, false).Set);
            case '?' or '*' or '+':
                throw Fail("nothing to repeat", at);
            case '^' or '$':
                throw Fail("the anchors ^ and $ aren’t supported", at);
            case '{' or '}' or ']':
                throw Fail($"{(char)codePoint} needs a backslash before it", at);
            default:
                return new PatternNode.Atom([new CodePointRange(codePoint, codePoint)]);
        }
    }

    // After a backslash. CodePoint is the escaped code point, or null for \d and \w, which cannot end a range.
    private (ImmutableArray<CodePointRange> Set, int? CodePoint) ParseEscape(int at, bool inClass)
    {
        if (position >= codePoints.Length) throw Fail("a pattern can’t end with a backslash", at);
        var escaped = codePoints[position];
        position++;
        if (escaped == 'd') return (CodePointSets.Digit, null);
        if (escaped == 'w') return (CodePointSets.Word, null);
        if ((escaped < 0x80 && SyntaxCharacters.Contains((char)escaped, StringComparison.Ordinal)) || escaped == '/' || (inClass && escaped == '-'))
        {
            return ([new CodePointRange(escaped, escaped)], escaped);
        }
        // A lone surrogate is its own code point, and char.ConvertFromUtf32 does not take it, so it is written as one char.
        var text = escaped is >= 0xd800 and <= 0xdfff ? ((char)escaped).ToString() : char.ConvertFromUtf32(escaped);
        throw Fail($"the escape \\{text} isn’t supported", at);
    }

    private (ImmutableArray<CodePointRange> Set, int? CodePoint) ParseClassAtom()
    {
        var at = position;
        var codePoint = codePoints[position];
        position++;
        return codePoint == '\\' ? ParseEscape(at, true) : ([new CodePointRange(codePoint, codePoint)], codePoint);
    }

    // After [. A - between two class atoms makes a range, and a - first or last is literal, for example [-a] or [a-].
    private ImmutableArray<CodePointRange> ParseClass(int at)
    {
        var negated = Is('^');
        if (negated) position++;
        var ranges = new List<CodePointRange>();
        while (!Is(']'))
        {
            if (position >= codePoints.Length) throw Fail("this set of characters is never closed", at);
            var low = ParseClassAtom();
            var hasNext = position + 1 < codePoints.Length;
            if (Is('-') && hasNext && codePoints[position + 1] != ']')
            {
                var dash = position;
                position++;
                var high = ParseClassAtom();
                if (low.CodePoint is not { } lowCodePoint || high.CodePoint is not { } highCodePoint) throw Fail("a range needs one character at each end", dash);
                if (lowCodePoint > highCodePoint) throw Fail("this range is out of order", dash);
                ranges.Add(new CodePointRange(lowCodePoint, highCodePoint));
            }
            else
            {
                ranges.AddRange(low.Set);
            }
        }
        position++;
        var set = CodePointSets.Normalize(ranges);
        return negated ? CodePointSets.Complement(set) : set;
    }
}
