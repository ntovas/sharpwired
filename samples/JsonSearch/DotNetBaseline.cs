// Handwired source: src/json-search/plain-search.ts (the baseline with the built-in regular expressions: the
// pattern goes to RegExp with the u flag). DotNetPattern translates a pattern of the language for .NET Regex, so
// that Regex reads code points as the programs do. The Handwired baseline gives the pattern to RegExp as it is.
// .NET Regex reads UTF-16 units, its \d and \w read Unicode classes, and its . excludes only \n. So the
// translation writes each set of code points of the pattern as .NET text:
// - The code points of the basic plane, except the surrogates, as one .NET class, for example [a-z].
// - A code point past U+FFFF as its surrogate pair, a range of them as ranges of pairs.
// - A lone surrogate as its own code point: a high surrogate that no low surrogate follows, or a low surrogate
//   that no high surrogate comes before.
// Groups become (?: ), and |, ?, * and + stay. The translation reads the pattern text itself. It calls the
// compiler first only to throw the same PatternSyntaxException for a pattern outside the language.
// Task 6 adds the counts of the baseline to this file.

using System.Globalization;
using System.Text;

namespace JsonSearch;

/// <summary>The translation of a pattern for .NET Regex.</summary>
public static class DotNetPattern
{
    private const int HighFirst = 0xd800;
    private const int HighLast = 0xdbff;
    private const int LowFirst = 0xdc00;
    private const int LowLast = 0xdfff;

    /// <summary>
    /// The .NET Regex pattern that matches the same code point sequences as the pattern. Throws
    /// PatternSyntaxException for a pattern outside the language.
    /// </summary>
    public static string Translate(string pattern)
    {
        PatternCompiler.Compile(pattern);
        var codePoints = CodePointsOf(pattern);
        var text = new StringBuilder();
        var position = 0;
        while (position < codePoints.Count)
        {
            var codePoint = codePoints[position];
            position++;
            switch (codePoint)
            {
                case '(':
                    text.Append("(?:");
                    break;
                case ')' or '|' or '?' or '*' or '+':
                    text.Append((char)codePoint);
                    break;
                case '.':
                    text.Append(SetText(CodePointSets.Dot));
                    break;
                case '\\':
                    text.Append(SetText(Escape(codePoints, ref position).Set));
                    break;
                case '[':
                    text.Append(SetText(Class(codePoints, ref position)));
                    break;
                default:
                    text.Append(SetText([new CodePointRange(codePoint, codePoint)]));
                    break;
            }
        }
        return text.ToString();
    }

    private static List<int> CodePointsOf(string text)
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
        return values;
    }

    // After a backslash: \d, \w, or an escaped code point. The compiler accepted the pattern, so the escape is valid.
    private static (IReadOnlyList<CodePointRange> Set, int? CodePoint) Escape(List<int> codePoints, ref int position)
    {
        var escaped = codePoints[position];
        position++;
        return escaped switch
        {
            'd' => (CodePointSets.Digit, null),
            'w' => (CodePointSets.Word, null),
            _ => ([new CodePointRange(escaped, escaped)], escaped),
        };
    }

    // After [: the items up to ], with ranges, and the negation.
    private static IReadOnlyList<CodePointRange> Class(List<int> codePoints, ref int position)
    {
        var negated = codePoints[position] == '^';
        if (negated) position++;
        var ranges = new List<CodePointRange>();
        while (codePoints[position] != ']')
        {
            var low = ClassItem(codePoints, ref position);
            if (codePoints[position] == '-' && position + 1 < codePoints.Count && codePoints[position + 1] != ']')
            {
                position++;
                var high = ClassItem(codePoints, ref position);
                ranges.Add(new CodePointRange(low.CodePoint!.Value, high.CodePoint!.Value));
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

    private static (IReadOnlyList<CodePointRange> Set, int? CodePoint) ClassItem(List<int> codePoints, ref int position)
    {
        var codePoint = codePoints[position];
        position++;
        return codePoint == '\\' ? Escape(codePoints, ref position) : ([new CodePointRange(codePoint, codePoint)], codePoint);
    }

    // The .NET text of one code point of the set.
    private static string SetText(IReadOnlyList<CodePointRange> set)
    {
        var plane = new StringBuilder();
        var alternatives = new List<string>();
        var loneHigh = new StringBuilder();
        var loneLow = new StringBuilder();
        foreach (var range in set)
        {
            AppendPart(plane, range, 0, HighFirst - 1);
            AppendPart(plane, range, LowLast + 1, 0xffff);
            AppendPart(loneHigh, range, HighFirst, HighLast);
            AppendPart(loneLow, range, LowFirst, LowLast);
            if (range.High >= 0x10000) alternatives.AddRange(Pairs(Math.Max(range.Low, 0x10000), range.High));
        }
        if (loneHigh.Length > 0) alternatives.Add($"[{loneHigh}](?![{Unit(LowFirst)}-{Unit(LowLast)}])");
        if (loneLow.Length > 0) alternatives.Add($"(?<![{Unit(HighFirst)}-{Unit(HighLast)}])[{loneLow}]");
        if (plane.Length > 0) alternatives.Insert(0, $"[{plane}]");
        return alternatives.Count switch
        {
            0 => "(?!)",
            1 when plane.Length > 0 => alternatives[0],
            _ => "(?:" + string.Join("|", alternatives) + ")",
        };
    }

    // The part of the range within first..last, as a .NET class item.
    private static void AppendPart(StringBuilder text, CodePointRange range, int first, int last)
    {
        var low = Math.Max(range.Low, first);
        var high = Math.Min(range.High, last);
        if (low > high) return;
        text.Append(Unit(low));
        if (high > low) text.Append('-').Append(Unit(high));
    }

    // The code points low..high past U+FFFF as alternatives of surrogate pairs.
    private static IEnumerable<string> Pairs(int low, int high)
    {
        var (lowHigh, lowLow) = Split(low);
        var (highHigh, highLow) = Split(high);
        if (lowHigh == highHigh)
        {
            yield return $"{Unit(lowHigh)}[{Unit(lowLow)}-{Unit(highLow)}]";
            yield break;
        }
        yield return $"{Unit(lowHigh)}[{Unit(lowLow)}-{Unit(LowLast)}]";
        if (highHigh - lowHigh > 1) yield return $"[{Unit(lowHigh + 1)}-{Unit(highHigh - 1)}][{Unit(LowFirst)}-{Unit(LowLast)}]";
        yield return $"{Unit(highHigh)}[{Unit(LowFirst)}-{Unit(highLow)}]";
    }

    private static (int High, int Low) Split(int codePoint)
    {
        var offset = codePoint - 0x10000;
        return (HighFirst + (offset >> 10), LowFirst + (offset & 0x3ff));
    }

    private static string Unit(int value) => "\\u" + value.ToString("x4", CultureInfo.InvariantCulture);
}
