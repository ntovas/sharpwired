// Handwired source: lab/json-search (the tables of the search times only) and src/json-search/assets/json-search.html
// (the four example patterns under the pattern field, each with its scope). The bench of JsonSearch.App --bench.
// For each example pattern and each scope, it runs the program on each backend at the capacity that holds the
// pattern. It also runs .NET Regex on one thread with its limit. One row for each pattern, scope and backend. The
// CPU backend runs on all cores. The program time is the TotalMs of the search. It is the pattern compile, then
// the time from the evaluation lock to the copy of the last count. As for .NET Regex, a program search under 100 ms runs two times, and the faster
// search counts. So the table does not hold the first dispatch after the shader create for such a search. A first
// search of 100 ms or more runs one time, and its time can hold that first dispatch.

using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Sharpwired.Gpu;

namespace JsonSearch;

/// <summary>A backend of the bench and its name in the table.</summary>
public sealed record BenchBackend(string Name, ILoopBackend Backend);

/// <summary>One row of the bench table: one pattern, one scope, one backend.</summary>
public sealed record BenchRow(
    string Pattern,
    SearchScope Scope,
    string Backend,
    int States,
    double ProgramMs,
    int ProgramDocuments,
    long ProgramMatches,
    double DotNetMs,
    int DotNetDocuments,
    long DotNetMatches,
    bool DotNetOverLimit)
{
    /// <summary>
    /// The count column holds "equal", "differ" or "over limit". It is "equal" when the totals are equal (the matches
    /// and the documents with a match). It is "over limit" when the limit stopped .NET Regex. The column compares no single document. BaselineTests compares
    /// the count of each document.
    /// </summary>
    public string Counts => DotNetOverLimit ? "over limit" : ProgramMatches == DotNetMatches && ProgramDocuments == DotNetDocuments ? "equal" : "differ";
}

/// <summary>The bench table of the search programs and .NET Regex.</summary>
public static class SearchBench
{
    private const double RepeatBelowMs = 100;

    /// <summary>The examples of the page under the pattern field, each with the scope that a click selects.</summary>
    public static ImmutableArray<(string Pattern, SearchScope Scope)> Examples { get; } =
    [
        ("[a-z0-9.]+@[a-z.]+", SearchScope.StringValues),
        ("[a-z]+Id", SearchScope.Keys),
        (@"\d\d\d\d-\d\d-\d\d", SearchScope.All),
        ("([a-z]+)+x", SearchScope.All),
    ];

    /// <summary>
    /// Run each example pattern in each scope on each backend, and .NET Regex one time for each pattern and scope.
    /// The log gets one line for each step.
    /// </summary>
    public static ImmutableArray<BenchRow> Run(IReadOnlyList<string> texts, IReadOnlyList<BenchBackend> backends, TimeSpan limit, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(backends);
        var searches = Examples.SelectMany(example => Enum.GetValues<SearchScope>().Select(scope => (example.Pattern, Scope: scope))).ToArray();
        var programs = new Dictionary<(string, SearchScope, string), SearchResult.Searched>();
        ImmutableArray<ImmutableArray<byte>> kinds = [];
        foreach (var backend in backends)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var searcher = Searcher.Create(texts, backend.Backend);
            log?.Invoke($"{backend.Name}: the searcher of {texts.Count} documents in {started.Elapsed.TotalMilliseconds:F0} ms");
            if (kinds.IsDefaultOrEmpty)
            {
                var flat = searcher.Kinds(0, texts.Count);
                kinds = [.. Enumerable.Range(0, texts.Count).Select(row => ImmutableArray.Create(flat.AsSpan(row * Kinds.Capacity, Kinds.Capacity)))];
            }
            foreach (var (pattern, scope) in searches)
            {
                var result = Searched(searcher, new SearchRequest(pattern, scope, 0, 0));
                if (result.Times.TotalMs < RepeatBelowMs)
                {
                    var second = Searched(searcher, new SearchRequest(pattern, scope, 0, 0));
                    if (second.Times.TotalMs < result.Times.TotalMs) result = second;
                }
                programs[(pattern, scope, backend.Name)] = result;
                log?.Invoke($"{backend.Name}: {pattern} in {scope}: {result.Matches} matches in {result.Times.TotalMs:F1} ms");
            }
        }
        var rows = ImmutableArray.CreateBuilder<BenchRow>();
        foreach (var (pattern, scope) in searches)
        {
            if (backends.Count == 0) break;
            var baseline = DotNetBaseline.Count(texts, kinds, pattern, scope, limit);
            log?.Invoke($".NET Regex: {pattern} in {scope}: {baseline.Matches} matches in {baseline.Milliseconds:F1} ms{(baseline.OverLimit ? ", over the limit" : "")}");
            foreach (var backend in backends)
            {
                var program = programs[(pattern, scope, backend.Name)];
                rows.Add(new BenchRow(
                    pattern, scope, backend.Name, program.States, program.Times.TotalMs, program.DocumentsWithMatch, program.Matches,
                    baseline.Milliseconds, baseline.DocumentsWithMatch, baseline.Matches, baseline.OverLimit));
            }
        }
        return rows.ToImmutable();
    }

    /// <summary>The table as Markdown: one line of text, the header, and one line for each row.</summary>
    public static string Format(IReadOnlyList<BenchRow> rows, int documentCount)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var culture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append(culture, $"The search of {documentCount:N0} documents: the program at the capacity that holds the pattern, on all cores for the CPU backend, ");
        text.Append("and .NET Regex on one thread. Counts compares the totals; the tests compare the count of each document.\n");
        text.Append("| Pattern | Scope | Backend | States | Program ms | Program documents | Program matches | .NET Regex ms | .NET documents | .NET matches | Counts |\n");
        text.Append("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
        foreach (var row in rows)
        {
            var dotNetMs = row.DotNetOverLimit ? string.Create(culture, $"more than {row.DotNetMs / 1000:F0} s") : row.DotNetMs.ToString("F1", culture);
            text.Append(culture, $"| `{row.Pattern.Replace("|", "\\|", StringComparison.Ordinal)}` | {ScopeName(row.Scope)} | {row.Backend} | {row.States} | {row.ProgramMs:F1} | ");
            text.Append(culture, $"{row.ProgramDocuments:N0} | {row.ProgramMatches:N0} | {dotNetMs} | {row.DotNetDocuments:N0} | {row.DotNetMatches:N0} | {row.Counts} |\n");
        }
        return text.ToString();
    }

    /// <summary>The name of the scope on the page.</summary>
    public static string ScopeName(SearchScope scope) => scope switch
    {
        SearchScope.All => "All",
        SearchScope.Keys => "Keys",
        SearchScope.StringValues => "String values",
        _ => throw new ArgumentException($"unknown scope {scope}", nameof(scope)),
    };

    private static SearchResult.Searched Searched(Searcher searcher, SearchRequest request) =>
        searcher.Search(request) as SearchResult.Searched
        ?? throw new InvalidOperationException($"the search of {request.Pattern} gave no count");
}
