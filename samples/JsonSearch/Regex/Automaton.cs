// Handwired source: src/json-search/regex/pattern.ts (buildAutomaton, partitionClasses). The automaton of a
// parsed pattern and the class partition of its code points.
//
// The automaton is the position automaton of Glushkov: state 0 is the initial state, and state p + 1 is the
// p-th atom of the pattern. For example, (ab)*c has the states initial, a, b and c. Each step into a state reads
// a code point of the set of that state. So the automaton has no empty steps, and one step for each code point
// runs it.
//
// The partition: the code points that each step reads the same way share a class. For example, (the|an?) [a-z]+
// has 7 classes: t, h, e, a, n, the space, and the other letters of a..z. The partition lists the code points
// where the class changes (the boundaries), and the class of each region. The class is -1 for a region that no
// step reads.

using System.Collections.Immutable;

namespace JsonSearch;

/// <summary>The automaton of a pattern: the targets, the set and the accepting bit of each state.</summary>
internal sealed record Automaton(
    int StateCount,
    ImmutableArray<ImmutableArray<int>> Targets,
    ImmutableArray<ImmutableArray<CodePointRange>> StateSets,
    ImmutableArray<bool> Accepting)
{
    private sealed record Summary(bool Nullable, ImmutableHashSet<int> First, ImmutableHashSet<int> Last);

    /// <summary>The position automaton of the parsed pattern (buildAutomaton of pattern.ts).</summary>
    public static Automaton Build(PatternNode node)
    {
        var sets = new List<ImmutableArray<CodePointRange>>();
        var follow = new List<HashSet<int>>();

        Summary Summarize(PatternNode current)
        {
            switch (current)
            {
                case PatternNode.Atom atom:
                    {
                        var index = sets.Count;
                        sets.Add(atom.Set);
                        follow.Add([]);
                        return new Summary(false, [index], [index]);
                    }
                case PatternNode.Sequence sequence:
                    {
                        var summary = new Summary(true, [], []);
                        foreach (var part in sequence.Parts)
                        {
                            var next = Summarize(part);
                            foreach (var atom in summary.Last) follow[atom].UnionWith(next.First);
                            summary = new Summary(
                                summary.Nullable && next.Nullable,
                                summary.Nullable ? summary.First.Union(next.First) : summary.First,
                                next.Nullable ? summary.Last.Union(next.Last) : next.Last);
                        }
                        return summary;
                    }
                case PatternNode.Alternation alternation:
                    {
                        var options = alternation.Options.Select(Summarize).ToList();
                        return new Summary(
                            options.Any(option => option.Nullable),
                            options.Aggregate(ImmutableHashSet<int>.Empty, (all, option) => all.Union(option.First)),
                            options.Aggregate(ImmutableHashSet<int>.Empty, (all, option) => all.Union(option.Last)));
                    }
                case PatternNode.Repeat repeat:
                    {
                        var body = Summarize(repeat.Body);
                        if (repeat.Repeats)
                        {
                            foreach (var atom in body.Last) follow[atom].UnionWith(body.First);
                        }
                        return new Summary(body.Nullable || repeat.Optional, body.First, body.Last);
                    }
                default:
                    throw new InvalidOperationException("unknown pattern node");
            }
        }

        var whole = Summarize(node);
        static ImmutableArray<int> Sorted(IEnumerable<int> atoms) => [.. atoms.Select(atom => atom + 1).Order()];
        return new Automaton(
            sets.Count + 1,
            [Sorted(whole.First), .. follow.Select(Sorted)],
            [[], .. sets],
            [whole.Nullable, .. Enumerable.Range(0, sets.Count).Select(whole.Last.Contains)]);
    }
}

/// <summary>
/// The class partition: Boundaries ascending within 1..0x10FFFF. Region 0 is the code points below Boundaries[0].
/// Region k is the code points from Boundaries[k - 1] up to Boundaries[k] - 1, or up to 0x10FFFF for the last
/// region. RegionClasses holds the class of each region, or -1. ClassReads[c][s] is whether a step into state s
/// reads the code points of class c.
/// </summary>
internal sealed record ClassPartition(
    int ClassCount,
    ImmutableArray<int> Boundaries,
    ImmutableArray<int> RegionClasses,
    ImmutableArray<ImmutableArray<bool>> ClassReads)
{
    /// <summary>The partition of the code points of the automaton (partitionClasses of pattern.ts).</summary>
    public static ClassPartition Build(Automaton automaton)
    {
        var cuts = new HashSet<int>();
        foreach (var set in automaton.StateSets)
        {
            foreach (var range in set)
            {
                cuts.Add(range.Low);
                cuts.Add(range.High + 1);
            }
        }
        var regionStarts = new[] { 0 }.Concat(cuts.Where(cut => cut > 0 && cut < PatternCompiler.CodePointLimit).Order());
        var classBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
        var classReads = new List<ImmutableArray<bool>>();
        var boundaries = new List<int>();
        var regionClasses = new List<int>();
        foreach (var start in regionStarts)
        {
            // Each code point of a region is inside or outside each set together, so the first code point decides.
            var reads = automaton.StateSets.Select(set => CodePointSets.Contains(set, start)).ToImmutableArray();
            var classIndex = -1;
            if (reads.Any(read => read))
            {
                var signature = string.Concat(reads.Select(read => read ? '1' : '0'));
                if (!classBySignature.TryGetValue(signature, out classIndex))
                {
                    classIndex = classReads.Count;
                    classBySignature.Add(signature, classIndex);
                    classReads.Add(reads);
                }
            }
            // A region starts where a set starts or stops. Each set is normalized, with a gap between neighbor
            // ranges. So the set holds the code point on one side of the start and not the other. So neighbor
            // regions always have different classes, and each region start above 0 is a boundary.
            if (start > 0 && regionClasses[^1] == classIndex)
            {
                throw new InvalidOperationException($"the regions on both sides of {start} have the same class");
            }
            if (start > 0) boundaries.Add(start);
            regionClasses.Add(classIndex);
        }
        return new ClassPartition(classReads.Count, [.. boundaries], [.. regionClasses], [.. classReads]);
    }
}
