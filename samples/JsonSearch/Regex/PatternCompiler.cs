// Handwired source: src/json-search/regex/pattern.ts (CompiledPattern, compilePattern, SearchCapacity,
// CapacityError, capacityErrors). The compile of a pattern into an automaton with numbered states and a class
// partition, which SearchProgram.WritePattern writes into the shared cells of a search program. The parser is in
// PatternParser.cs, and the automaton and the partition are in Automaton.cs.

using System.Collections.Immutable;

namespace JsonSearch;

/// <summary>What one search program holds: States states, Classes character classes and Boundaries class boundaries.</summary>
public sealed record SearchCapacity(int States, int Classes = 16, int Boundaries = 32)
{
    /// <summary>The three search programs: 8, 16 and 32 states, each with 16 classes and 32 boundaries.</summary>
    public static ImmutableArray<SearchCapacity> All { get; } = [new(8), new(16), new(32)];
}

/// <summary>One part of a pattern that does not fit a capacity. Reason is "states", "classes" or "boundaries".</summary>
public sealed record CapacityError(string Reason, int Needed, int Capacity, string Message);

/// <summary>
/// A compiled pattern. The automaton: StateCount states, the targets of each state (ascending), the set of code
/// points that a step into each state reads (the set of state 0 is empty), and the accepting bit of each state.
/// The partition: ClassCount classes, the boundaries where the class changes, the class of each region (-1 for a
/// region that no step reads), and ClassReads[c][s], whether a step into state s reads class c.
/// </summary>
public sealed class CompiledPattern
{
    internal CompiledPattern(string pattern, Automaton automaton, ClassPartition partition)
    {
        Pattern = pattern;
        StateCount = automaton.StateCount;
        Targets = automaton.Targets;
        StateSets = automaton.StateSets;
        Accepting = automaton.Accepting;
        ClassCount = partition.ClassCount;
        Boundaries = partition.Boundaries;
        RegionClasses = partition.RegionClasses;
        ClassReads = partition.ClassReads;
    }

    public string Pattern { get; }

    public int StateCount { get; }

    public ImmutableArray<ImmutableArray<int>> Targets { get; }

    public ImmutableArray<ImmutableArray<CodePointRange>> StateSets { get; }

    public ImmutableArray<bool> Accepting { get; }

    public int ClassCount { get; }

    public ImmutableArray<int> Boundaries { get; }

    public ImmutableArray<int> RegionClasses { get; }

    public ImmutableArray<ImmutableArray<bool>> ClassReads { get; }
}

/// <summary>The pattern compiler (compilePattern and capacityErrors of pattern.ts).</summary>
public static class PatternCompiler
{
    /// <summary>The largest code point.</summary>
    public const int MaxCodePoint = 0x10FFFF;

    /// <summary>One past the largest code point.</summary>
    public const int CodePointLimit = 0x110000;

    /// <summary>Compile the pattern. Throws PatternSyntaxException for a pattern outside the language.</summary>
    public static CompiledPattern Compile(string pattern)
    {
        var automaton = Automaton.Build(PatternParser.Parse(pattern));
        return new CompiledPattern(pattern, automaton, ClassPartition.Build(automaton));
    }

    /// <summary>
    /// One error for each of the states, the classes and the boundaries that the pattern needs past the capacity,
    /// for example "the pattern needs 33 states, and the program holds 32". Empty when the pattern fits.
    /// </summary>
    public static ImmutableArray<CapacityError> CapacityErrors(CompiledPattern compiled, SearchCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(capacity);
        (string Reason, int Needed, int Held, string Noun)[] needs =
        [
            ("states", compiled.StateCount, capacity.States, "states"),
            ("classes", compiled.ClassCount, capacity.Classes, "character classes"),
            ("boundaries", compiled.Boundaries.Length, capacity.Boundaries, "class boundaries"),
        ];
        return [.. needs.Where(need => need.Needed > need.Held)
            .Select(need => new CapacityError(need.Reason, need.Needed, need.Held, $"the pattern needs {need.Needed} {need.Noun}, and the program holds {need.Held}"))];
    }
}
