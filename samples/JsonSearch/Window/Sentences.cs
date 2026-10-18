// Handwired source: src/json-search/demo-text.ts (count, duration, resultLine, limitSentence, analyzePattern,
// failureSentence) and the sentences of src/json-search/demo.ts ("Type a pattern to search the … documents.",
// "Searching … documents…"). The window names .NET Regex where the page names the native RegExp of the browser.
// The line is one text: the window draws no bold part.

using System.Globalization;

namespace JsonSearch;

/// <summary>The backend of the search program.</summary>
public enum SearchBackend
{
    Gpu,
    Cpu,
}

/// <summary>The .NET Regex time of a search: measured, or Stopped when the limit stopped the count after Ms.</summary>
public readonly record struct DotNetTime(double Ms, bool Stopped);

/// <summary>What the window does with the pattern in the field (analyzePattern of demo-text.ts).</summary>
public abstract record PatternAnalysis
{
    private PatternAnalysis()
    {
    }

    /// <summary>No pattern: no search.</summary>
    public sealed record Empty : PatternAnalysis;

    /// <summary>A pattern with a syntax error, or one that no search program holds: a sentence and no search.</summary>
    public sealed record Refused(string Sentence) : PatternAnalysis;

    /// <summary>A search on the smallest program that holds the pattern.</summary>
    public sealed record Fits(int States) : PatternAnalysis;
}

/// <summary>The numbers and the sentences of the window.</summary>
public static class Sentences
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>A number with separators, for example 1,203,712.</summary>
    public static string Count(long value) => value.ToString("N0", English);

    /// <summary>
    /// A measured time: one decimal under 10 ms, whole milliseconds under 1 s, then seconds with one decimal under
    /// 10 s, and whole seconds above.
    /// </summary>
    public static string Duration(double ms)
    {
        if (ms >= 995) return $"{(ms / 1000).ToString(ms >= 9950 ? "F0" : "F1", English)} s";
        if (ms >= 9.95) return $"{Math.Round(ms, MidpointRounding.AwayFromZero).ToString("F0", English)} ms";
        return ms < 0.05 ? "under 0.1 ms" : $"{ms.ToString("F1", English)} ms";
    }

    /// <summary>
    /// The line of a search: the documents with a match, the program time and its backend, and the .NET Regex time
    /// when it is known. A search that has not reached each document says how far it is.
    /// </summary>
    public static string ResultLine(int withMatch, int searched, double ms, int documentCount, SearchBackend backend, DotNetTime? dotNet)
    {
        var place = backend == SearchBackend.Gpu ? "on the GPU" : "on the CPU";
        var dotNetPart = dotNet is { } time ? $" .NET Regex: {(time.Stopped ? "more than " : "")}{Duration(time.Ms)}." : "";
        var verb = withMatch == 1 ? "matches" : "match";
        var line = searched < documentCount
            ? $"{Count(withMatch)} of the first {Count(searched)} documents {verb}. This program: {Duration(ms)} {place} so far."
            : withMatch == 0
                ? $"None of the {Count(documentCount)} documents matches. This program: {Duration(ms)} {place}."
                : $"{Count(withMatch)} of {Count(documentCount)} documents {verb}. This program: {Duration(ms)} {place}.";
        return line + dotNetPart;
    }

    /// <summary>The sentence of a syntax error at a code point position (0 is the first code point).</summary>
    public static string Syntax(string message, int position) => $"Character {Count(position + 1)} of the pattern: {message}.";

    /// <summary>The sentence of one capacity that a pattern needs past the largest program.</summary>
    /// <exception cref="ArgumentException">An unknown reason.</exception>
    public static string Limit(CapacityError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Reason switch
        {
            "states" => $"This pattern needs {Count(error.Needed)} states, and the largest search program holds {error.Capacity}.",
            "classes" => $"This pattern tells {Count(error.Needed)} classes of characters apart, and the search programs hold {error.Capacity}.",
            "boundaries" => $"This pattern’s classes of characters change at {Count(error.Needed)} places, and the search programs hold {error.Capacity}.",
            _ => throw new ArgumentException($"unknown capacity reason {error.Reason}", nameof(error)),
        };
    }

    /// <summary>The sentence for no pattern.</summary>
    public static string Empty(int documentCount) => $"Type a pattern to search the {Count(documentCount)} documents.";

    /// <summary>The sentence of a first search, or of a search that runs long.</summary>
    public static string Searching(int documentCount) => $"Searching {Count(documentCount)} documents…";

    /// <summary>The sentence of a search that stopped with an error.</summary>
    public static string Failure(string message) => $"The search stopped with an error: {message}.";

    /// <summary>
    /// What the window does with the pattern. For an empty pattern, it does nothing. For a pattern with a syntax
    /// error, or one that no search program holds, it gives a sentence. Otherwise, it runs a search on the smallest
    /// program that holds the pattern. An exception of the compiler other than a syntax error also gives a sentence,
    /// so that no pattern stops the window.
    /// </summary>
    public static PatternAnalysis Analyze(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0) return new PatternAnalysis.Empty();
        CompiledPattern compiled;
        try
        {
            compiled = PatternCompiler.Compile(pattern);
        }
        catch (PatternSyntaxException error)
        {
            return new PatternAnalysis.Refused(Syntax(error.Message, error.Position));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            return new PatternAnalysis.Refused($"The pattern could not be read: {error.Message}.");
        }
        foreach (var capacity in SearchCapacity.All)
        {
            if (PatternCompiler.CapacityErrors(compiled, capacity).IsEmpty) return new PatternAnalysis.Fits(capacity.States);
        }
        return new PatternAnalysis.Refused(string.Join(" ", PatternCompiler.CapacityErrors(compiled, SearchCapacity.All[^1]).Select(Limit)));
    }
}
