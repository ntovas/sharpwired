// Handwired source: tests/json-search/edge-cases.test.ts and tests/json-search/edge-cases.ts (createReferenceSearch,
// compareWithReferences). The documents and the searches at the limits of the programs. First, the reference values
// of chosen documents. Then the patterns that fill the states, the classes and the class boundaries of each capacity.
// Last, the search of each capacity on the CPU runner over the hand-written documents. The test compares it with the
// automaton over the display kinds of JsonReference.

using Sharpwired.Gpu;

namespace JsonSearch.Tests;

public sealed partial class SearchTests
{
    internal static readonly Lazy<List<EdgeDocument>> EdgeDocuments = new(EdgeCases.HandWritten);

    // The documents of more than one code point, which each search runs over.
    internal static string[] LongerEdgeTexts => [.. EdgeDocuments.Value.Where(document => !document.Name.StartsWith("one code point", StringComparison.Ordinal)).Select(document => document.Text)];

    // The smallest capacity that holds the pattern, or null.
    internal static int? StateCapacityOf(CompiledPattern compiled) =>
        SearchCapacity.All.FirstOrDefault(capacity => PatternCompiler.CapacityErrors(compiled, capacity).IsEmpty)?.States;

    /// <summary>The reference of each search over the texts: the counts and the marks, with the kinds of JsonReference.</summary>
    internal static (float[] Counts, float[] Marks) ReferenceSearch(IReadOnlyList<string> texts, ReferenceResult[] references, CompiledPattern compiled, IReadOnlyCollection<DisplayKind> kinds)
    {
        var counts = new float[texts.Count];
        var marks = new float[texts.Count * Positions];
        Parallel.For(0, texts.Count, row =>
        {
            var found = ReferenceMatches(compiled, CodePoints.Of(texts[row]), references[row].DisplayKinds.Select(kind => (float)(int)kind).ToArray(), kinds);
            counts[row] = found.Count;
            RegexReference.MarksOf(found, Positions).CopyTo(marks, row * Positions);
        });
        return (counts, marks);
    }

    [Fact]
    public void The_edge_documents_and_patterns_reach_the_limits_by_the_reference_values()
    {
        var documents = EdgeDocuments.Value;
        Assert.Equal(documents.Count, documents.Select(document => document.Name).Distinct().Count());
        int Row(string name) => documents.FindIndex(document => document.Name == name);
        var lengths = documents.Select(document => CodePoints.Of(document.Text).Length).ToArray();
        foreach (var length in new[] { 0, 1, Positions - 1, Positions, Positions + 1 }) Assert.Contains(length, lengths);

        // The patterns fill the states of each program, and one of each capacity fills the classes and the boundaries.
        foreach (var states in new[] { 8, 16, 32 })
        {
            var compiled = EdgeCases.EdgePatterns(states).Select(PatternCompiler.Compile).ToList();
            Assert.All(compiled, pattern => Assert.Equal(states, StateCapacityOf(pattern)));
            Assert.Equal(states, compiled.Max(pattern => pattern.StateCount));
            Assert.Contains(compiled, pattern => pattern.ClassCount == 16 && pattern.Boundaries.Length == 32 && (states == 8 || pattern.StateCount == states));
        }

        int CountOf(string pattern, string name, out float[] marks)
        {
            var text = documents[Row(name)].Text;
            var found = ReferenceMatches(PatternCompiler.Compile(pattern), CodePoints.Of(text), JsonReference.Highlight(text).DisplayKinds.Select(kind => (float)(int)kind).ToArray(), Enum.GetValues<DisplayKind>());
            marks = RegexReference.MarksOf(found, Positions);
            return found.Count;
        }
        // Each code point is a match, the whole text is one match, nothing matches, and the empty match is never reported.
        Assert.Equal([Positions, Positions, 0], new[] { CountOf("[^]", $"{Positions} times a", out _), CountOf("[^]", $"{Positions + 1} times a", out _), CountOf("[^]", "empty", out _) });
        Assert.Equal([1, 0, 0, 1], new[] { CountOf("[^]+", $"{Positions} times a", out _), CountOf("[]", $"{Positions} times a", out _), CountOf("", $"{Positions} times a", out _), CountOf("a*", $"{Positions - 1} times a, then b", out _) });
        Assert.Equal(Positions - 1, CountOf("a", $"b, then {Positions - 1} times a", out _));
        // A match on the last 7 positions, and none when the capacity cuts its last code point.
        Assert.Equal(1, CountOf("abcdefg", $"abcdefg at the end of {Positions} code points", out var lastMarks));
        Assert.Equal([0, 2, 1, 1, 1, 1, 1, 1], lastMarks.Skip(Positions - 8));
        Assert.Equal(0, CountOf("abcdefg", $"abcdefg at the end of {Positions + 1} code points", out _));
        Assert.Equal(2, CountOf("(the|an?) [a-z]+", $"the tide at both ends of {Positions} code points", out var bothMarks));
        Assert.Equal((2f, 1f), (bothMarks[0], bothMarks[Positions - 1]));
    }

    [Theory]
    [MemberData(nameof(Capacities))]
    public void The_search_of_each_capacity_over_the_edge_documents_equals_the_reference(int states)
    {
        var program = Programs[states].Value;
        var texts = LongerEdgeTexts;
        var cells = DocumentCells.WriteAll(texts);
        var (_, kinds) = HighlighterTests.RunCpu(texts, CpuStageBackend.Compiled);
        var references = texts.Select(JsonReference.Highlight).ToArray();
        using var search = new CpuSearch(program);
        var searches = EdgeCases.EdgeSearches(states);
        var withMatches = 0;
        var matches = 0L;
        foreach (var edge in searches)
        {
            var compiled = PatternCompiler.Compile(edge.Pattern);
            var (counts, marks) = search.Run(SharedOf(program, compiled, edge.Kinds), cells, kinds, texts.Length);
            var expected = ReferenceSearch(texts, references, compiled, edge.Kinds);
            for (var row = 0; row < texts.Length; row++)
            {
                if (counts[row] != expected.Counts[row] || !marks.AsSpan(row * Positions, Positions).SequenceEqual(expected.Marks.AsSpan(row * Positions, Positions)))
                {
                    Assert.Fail($"{edge.Pattern} in {edge.ScopeName}, document {row}: {counts[row]} matches at {TestPatterns.Pairs(RegexReference.MatchesFromMarks(marks.AsSpan(row * Positions, Positions)))}, and the reference gives {expected.Counts[row]} at {TestPatterns.Pairs(RegexReference.MatchesFromMarks(expected.Marks.AsSpan(row * Positions, Positions)))}");
                }
            }
            var total = expected.Counts.Sum(count => (long)count);
            matches += total;
            if (total > 0) withMatches++;
        }
        output.WriteLine($"{states} states: {searches.Count} searches over {texts.Length} documents, {withMatches} with matches, {matches} matches");
        Assert.True(matches > 1000, $"{matches} matches");
        // The searches that find no match: the empty class, the empty pattern and a search of no kind. Also a scope
        // with no match of the pattern. At 32 states, also "connection reset", which no document holds.
        Assert.Equal(states switch { 8 => (36, 25), 16 => (10, 7), _ => (8, 4) }, (searches.Count, withMatches));
    }
}
