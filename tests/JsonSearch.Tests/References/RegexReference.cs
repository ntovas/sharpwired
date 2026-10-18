// Handwired source: tests/json-search/regex/reference.ts. Two plain C# references for the search, written from
// the definition, not from the programs. Both find each non-overlapping, non-empty match, leftmost and then
// longest: from the scan position, the longest match that starts there moves the scan position to the end of the
// match. When no match starts there, the scan moves one code point on.
// - AutomatonMatches runs the compiled automaton from each scan position, one code point at a time, until no
//   state is left, and keeps the last position where an accepting state was reached. It reads the states, the
//   targets, the sets and the accepting bits of the automaton, and not the class partition.
// - DotNetMatches does not use the automaton. It tests each substring with .NET Regex and the pattern of
//   DotNetPattern.Translate. Use it for texts of up to 40 code points.
// Also the conversion of the marks of a document (0 outside a match, 1 inside, 2 at the first code point) to the
// matches, and back.

using System.Text;
using JsonSearch;

namespace JsonSearch.Tests;

/// <summary>A match covers the code points from Start up to and not including End.</summary>
internal readonly record struct Match(int Start, int End);

internal static class RegexReference
{
    /// <summary>The matches of the automaton of the compiled pattern in the code points.</summary>
    public static List<Match> AutomatonMatches(CompiledPattern compiled, IReadOnlyList<int> codePoints)
    {
        var matches = new List<Match>();
        var position = 0;
        while (position < codePoints.Count)
        {
            var states = new HashSet<int> { 0 };
            var longestEnd = -1;
            for (var index = position; index < codePoints.Count && states.Count > 0; index++)
            {
                var next = new HashSet<int>();
                foreach (var state in states)
                {
                    foreach (var target in compiled.Targets[state])
                    {
                        if (Contains(compiled.StateSets[target], codePoints[index])) next.Add(target);
                    }
                }
                states = next;
                if (states.Any(state => compiled.Accepting[state])) longestEnd = index + 1;
            }
            if (longestEnd > position)
            {
                matches.Add(new Match(position, longestEnd));
                position = longestEnd;
            }
            else
            {
                position++;
            }
        }
        return matches;
    }

    /// <summary>The matches that .NET Regex finds when it tests each substring against the whole pattern.</summary>
    public static List<Match> DotNetMatches(string pattern, IReadOnlyList<int> codePoints)
    {
        // \A and \z: .NET $ also matches before a line feed at the end, and the test is of the whole substring.
        var whole = new System.Text.RegularExpressions.Regex(
            @"\A(?:" + DotNetPattern.Translate(pattern) + @")\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(10));
        var matches = new List<Match>();
        var position = 0;
        while (position < codePoints.Count)
        {
            var longestEnd = -1;
            for (var end = codePoints.Count; end > position; end--)
            {
                if (whole.IsMatch(TextOf(codePoints, position, end)))
                {
                    longestEnd = end;
                    break;
                }
            }
            if (longestEnd > position)
            {
                matches.Add(new Match(position, longestEnd));
                position = longestEnd;
            }
            else
            {
                position++;
            }
        }
        return matches;
    }

    /// <summary>The matches that the marks of one document give. Throws for a mark 1 that no match reaches, or a mark that is not 0, 1 or 2.</summary>
    public static List<Match> MatchesFromMarks(ReadOnlySpan<float> marks)
    {
        var matches = new List<Match>();
        for (var position = 0; position < marks.Length; position++)
        {
            var mark = marks[position];
            if (mark == 2)
            {
                matches.Add(new Match(position, position + 1));
            }
            else if (mark == 1)
            {
                if (matches.Count == 0 || matches[^1].End != position) throw new InvalidOperationException($"position {position} is marked as inside a match, and no match reaches it");
                matches[^1] = matches[^1] with { End = position + 1 };
            }
            else if (mark != 0)
            {
                throw new InvalidOperationException($"the mark at position {position} is {mark}, and a mark is 0, 1 or 2");
            }
        }
        return matches;
    }

    /// <summary>The marks of the matches over the given number of positions.</summary>
    public static float[] MarksOf(IEnumerable<Match> matches, int positions)
    {
        var marks = new float[positions];
        foreach (var match in matches)
        {
            for (var position = match.Start; position < match.End; position++) marks[position] = 1;
            marks[match.Start] = 2;
        }
        return marks;
    }

    /// <summary>The text of the code points from start up to end. A surrogate code point is one char.</summary>
    public static string TextOf(IReadOnlyList<int> codePoints, int start, int end)
    {
        var text = new StringBuilder();
        for (var index = start; index < end; index++)
        {
            var codePoint = codePoints[index];
            if (codePoint is >= 0xd800 and <= 0xdfff) text.Append((char)codePoint);
            else text.Append(char.ConvertFromUtf32(codePoint));
        }
        return text.ToString();
    }

    private static bool Contains(IEnumerable<CodePointRange> set, int codePoint) =>
        set.Any(range => codePoint >= range.Low && codePoint <= range.High);
}
