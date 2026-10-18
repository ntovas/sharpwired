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
// DotNetBaseline counts the matches of the translated pattern as plain-search.ts does: in the first 512 code points
// of each document for All, or in each run of searched code points (the keys, or the string values with their
// quotes) for the other scopes. The runs come from the display kinds of the highlighter and are found before the
// time starts. The baseline counts each match of Regex, also an empty match, as plain-search.ts counts each match
// of RegExp. Differences from Handwired: one total limit stops the count between documents, and the match timeout
// of Regex stops one long match (Handwired ends the worker of RegExp after 10 s).

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

/// <summary>
/// The counts of the .NET Regex baseline. MatchCounts holds one count for each document that the count reached, in
/// document order. OverLimit is true when the limit stopped the count before the last document.
/// </summary>
public sealed record BaselineResult(long Matches, int DocumentsWithMatch, double Milliseconds, bool OverLimit, ImmutableArray<int> MatchCounts);

/// <summary>The .NET Regex baseline next to the search program (plain-search.ts).</summary>
public static class DotNetBaseline
{
    private const int Positions = Kinds.Capacity;

    // A pass faster than this runs a second time, and the faster pass counts.
    private const double RepeatBelowMs = 100;

    /// <summary>
    /// Count the matches of the pattern in each document, or in each run of the kinds of the scope, on one thread.
    /// The kinds hold the 512 display kinds of each document. The limit stops the count between documents, and it is
    /// also the match timeout of Regex. A pass under 100 ms runs two times, and the faster pass counts. A cancellation
    /// stops the count between documents.
    /// </summary>
    /// <exception cref="PatternSyntaxException">The pattern is outside the language.</exception>
    /// <exception cref="ArgumentException">The kinds do not match the texts.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static BaselineResult Count(IReadOnlyList<string> texts, IReadOnlyList<ImmutableArray<byte>> kinds, string pattern, SearchScope scope, TimeSpan limit, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, TimeSpan.Zero);
        if (kinds.Count != texts.Count) throw new ArgumentException($"{kinds.Count} kind lists for {texts.Count} texts", nameof(kinds));
        var translated = DotNetPattern.Translate(pattern);
        var regex = new Regex(translated, RegexOptions.CultureInvariant, limit > TimeSpan.Zero ? limit : Regex.InfiniteMatchTimeout);
        var (runs, firstRun) = Runs(texts, kinds, scope);
        var best = Pass(regex, runs, firstRun, texts.Count, limit, cancel);
        if (!best.OverLimit && best.Milliseconds < RepeatBelowMs)
        {
            var second = Pass(regex, runs, firstRun, texts.Count, limit, cancel);
            if (second.Milliseconds < best.Milliseconds) best = second;
        }
        return best;
    }

    private static BaselineResult Pass(Regex regex, List<string> runs, int[] firstRun, int documentCount, TimeSpan limit, CancellationToken cancel)
    {
        var counts = new List<int>(documentCount);
        var matches = 0L;
        var withMatch = 0;
        var overLimit = false;
        var watch = Stopwatch.StartNew();
        for (var row = 0; row < documentCount; row++)
        {
            cancel.ThrowIfCancellationRequested();
            if (watch.Elapsed >= limit)
            {
                overLimit = true;
                break;
            }
            var found = 0;
            try
            {
                for (var run = firstRun[row]; run < firstRun[row + 1]; run++) found += regex.Count(runs[run]);
            }
            catch (RegexMatchTimeoutException)
            {
                overLimit = true;
                break;
            }
            counts.Add(found);
            matches += found;
            if (found > 0) withMatch++;
        }
        return new BaselineResult(matches, withMatch, watch.Elapsed.TotalMilliseconds, overLimit, [.. counts]);
    }

    // The runs of searched code points of each document: document row has runs[firstRun[row]] up to
    // runs[firstRun[row + 1] - 1]. For All, a document is one run: its first 512 code points.
    private static (List<string> Runs, int[] FirstRun) Runs(IReadOnlyList<string> texts, IReadOnlyList<ImmutableArray<byte>> kinds, SearchScope scope)
    {
        var searchable = new bool[SearchCells.KindCount];
        foreach (var kind in SearchProgram.KindsOf(scope)) searchable[(int)kind] = true;
        var runs = new List<string>();
        var firstRun = new int[texts.Count + 1];
        for (var row = 0; row < texts.Count; row++)
        {
            var text = texts[row] ?? throw new ArgumentException($"text {row} is null", nameof(texts));
            var documentKinds = kinds[row];
            if (documentKinds.Length != Positions) throw new ArgumentException($"the kinds of document {row} hold {documentKinds.Length} values, not {Positions}", nameof(kinds));
            var runStart = -1;
            var unit = 0;
            for (var position = 0; position < Positions && unit < text.Length; position++)
            {
                var kind = documentKinds[position];
                var reads = kind < searchable.Length && searchable[kind];
                if (reads && runStart == -1) runStart = unit;
                if (!reads && runStart != -1)
                {
                    runs.Add(text[runStart..unit]);
                    runStart = -1;
                }
                unit += char.IsHighSurrogate(text[unit]) && unit + 1 < text.Length && char.IsLowSurrogate(text[unit + 1]) ? 2 : 1;
            }
            if (runStart != -1) runs.Add(text[runStart..unit]);
            firstRun[row + 1] = runs.Count;
        }
        return (runs, firstRun);
    }
}
