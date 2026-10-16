// Handwired source: src/runtime/numeric-ui-core.ts (WinnerNode, ArgmaxNode, emitArgmax,
// routeArgmax, emitFirstActive, isProvenBit, isBitByConstruction, routeWinner).
// Handwired emitMultiply, emitSelect and emitMaximum are Circuit.Multiply, Circuit.Select and
// Macros.Maximum here.

namespace Sharpwired.Core;

/// <summary>The wires of Macros.Argmax: the one-hot selection, and the score of the selected candidate.</summary>
public sealed record ArgmaxSelection(IReadOnlyList<Wire> Selected, Wire Score);

public static partial class Macros
{
    // One FP32 step above 0.5, so that an exact tie chooses the left candidate and the comparison
    // stays at or below 1.
    private const double ArgmaxComparisonBias = 0.5000000596046448;
    private const double ArgmaxComparisonScale = 0.4999999403953552;

    /// <summary>
    /// The one-hot selection of the highest score, through a balanced tree. Up the tree, each pair
    /// computes leftWins = boolean(c + s × left - s × right), where c is the FP32 value just above 0.5
    /// and s = 1 - c, and keeps Select(leftWins, right, left). Down the tree, route × leftWins goes
    /// left and the rest goes right, from the unit wire.
    /// </summary>
    /// <remarks>
    /// The scores need declared ranges within 0..1, and the unit wire needs the declared range 1..1.
    /// The executor must keep the FP32 rounding of the boolean atoms. The selection keeps the left
    /// candidate on an exact tie, and also when the right score is larger by less than 8.9e-8, or for
    /// some scores by up to 2.4e-7. This band is not transitive, so the tree can pick another
    /// candidate than a loop. Argmax always declares the selection as a one-hot group.
    /// </remarks>
    public static ArgmaxSelection Argmax(this Circuit circuit, IReadOnlyList<Wire> scores, Wire unit)
    {
        if (scores.Count == 0) throw new ArgumentException("argmax needs at least one score");
        Validation.AssertWire(unit, circuit.Width);
        var unitRange = circuit.Ranges[unit.Position];
        if (!(unitRange.Minimum == 1 && unitRange.Maximum == 1)) throw new ArgumentException("argmax unit must equal one");
        var level = new List<ArgmaxNode>(scores.Count);
        for (var item = 0; item < scores.Count; item++)
        {
            var score = scores[item];
            Validation.AssertWire(score, circuit.Width);
            var range = circuit.Ranges[score.Position];
            if (!(range.Minimum >= 0 && range.Maximum <= 1)) throw new ArgumentException("argmax scores must be within zero and one");
            level.Add(new ArgmaxLeaf(score, item));
        }
        while (level.Count > 1)
        {
            var next = new List<ArgmaxNode>((level.Count + 1) / 2);
            for (var item = 0; item < level.Count; item += 2)
            {
                var left = level[item];
                if (item + 1 == level.Count)
                {
                    next.Add(left);
                    continue;
                }
                var right = level[item + 1];
                var comparison = circuit.Affine(
                    [(left.Score, ArgmaxComparisonScale), (right.Score, -ArgmaxComparisonScale)],
                    ArgmaxComparisonBias);
                circuit.AssumeRange(comparison, 0, 1);
                var leftWins = circuit.Boolean(comparison);
                next.Add(new ArgmaxPair(circuit.Select(leftWins, right.Score, left.Score), leftWins, left, right));
            }
            level = next;
        }
        var root = level[0];
        var selected = new Wire[scores.Count];
        RouteArgmax(circuit, root, unit, selected);
        // The tree routes the unit down exactly one path, so the selection is an exact one-hot by
        // construction. The declaration lets masked reads of it lower to gathers.
        circuit.AssumeOneHot(selected);
        return new ArgmaxSelection(selected, root.Score);
    }

    /// <summary>
    /// The first active candidate, through a balanced tree. Up the tree, each pair keeps the Maximum
    /// of its masses. Down the tree, a node's route r gives r × leftMaximum to its left child and
    /// r - r × leftMaximum to its right child, from gate × rootMaximum.
    /// </summary>
    /// <remarks>
    /// The gate and the candidates need declared ranges within 0..1. The selection is an exact one-hot,
    /// or all zeros, when the gate and every candidate are exact bits. Fractional candidates split the
    /// route, for example candidates [0.5, 0] send 0.25 to each output. FirstActive declares the
    /// selection as a one-hot group that allows all zeros only when the gate and every candidate are
    /// proven bits: a wire whose declared range holds only 0 or only 1, a boolean atom, the bit that
    /// Compare returns, a member of a declared one-hot group, or an index read whose reachable
    /// candidates are all proven bits.
    /// </remarks>
    public static Wire[] FirstActive(this Circuit circuit, Wire active, IReadOnlyList<Wire> candidates)
    {
        if (candidates.Count == 0) return [];
        Validation.AssertWire(active, circuit.Width);
        var activeRange = circuit.Ranges[active.Position];
        if (!(activeRange.Minimum >= 0 && activeRange.Maximum <= 1))
        {
            throw new ArgumentException("winner selection must be gated within zero and one");
        }
        var level = new List<WinnerNode>(candidates.Count);
        for (var item = 0; item < candidates.Count; item++)
        {
            var mass = candidates[item];
            Validation.AssertWire(mass, circuit.Width);
            var range = circuit.Ranges[mass.Position];
            if (!(range.Minimum >= 0 && range.Maximum <= 1)) throw new ArgumentException("winner candidates must be within zero and one");
            level.Add(new WinnerLeaf(mass, item));
        }
        while (level.Count > 1)
        {
            var next = new List<WinnerNode>((level.Count + 1) / 2);
            for (var item = 0; item < level.Count; item += 2)
            {
                var left = level[item];
                next.Add(item + 1 == level.Count
                    ? left
                    : new WinnerPair(circuit.Maximum(left.Mass, level[item + 1].Mass), left, level[item + 1]));
            }
            level = next;
        }
        var root = level[0];
        var selected = new Wire[candidates.Count];
        RouteWinner(circuit, root, circuit.Multiply(active, root.Mass), selected);
        // The selection is one-hot or all zeros only when the gate and every candidate are exact bits.
        // Fractional scores in 0..1 are legal inputs and give fractional masses, so a declaration
        // without this condition is a false contract that gather lowering then compiles to wrong
        // values. Constant candidates can also fold routing wires together, and a group needs
        // distinct members, so these selections stay undeclared too.
        if (IsProvenBit(circuit, active) && candidates.All(candidate => IsProvenBit(circuit, candidate)))
        {
            if (selected.Distinct().Count() == selected.Length) circuit.AssumeOneHot(selected, allowAllZero: true);
        }
        return selected;
    }

    // A wire that holds an exact 0 or 1: its declared range holds only one of the two values, a
    // boolean atom produced it, Compare returned it, it is a member of a declared one-hot group (the
    // members are exactly 0 or 1 by contract, also in groups that allow all zeros), or it is an index
    // read whose reachable candidates are all proven bits. A side that reads zero adds +0, which is
    // also a bit. A plain 0..1 range is not sufficient, because it admits fractions. An operation
    // inside Compare, for example a sign cascade stage within 0..1, is also not sufficient.
    internal static bool IsProvenBit(Circuit circuit, Wire wire)
    {
        // A stack follows reads of reads, and the check visits each wire one time.
        var pending = new Stack<Wire>();
        pending.Push(wire);
        var visited = new HashSet<Wire>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current)) continue;
            if (IsBitByConstruction(circuit, current)) continue;
            var read = current.Position >= circuit.SourceSize ? circuit.Operations[current.Position - circuit.SourceSize].IndexRead : null;
            if (read is null) return false;
            foreach (var candidate in Circuit.ReachableCandidates(read)) pending.Push(candidate);
        }
        return true;
    }

    internal static bool IsBitByConstruction(Circuit circuit, Wire wire)
    {
        var range = circuit.Ranges[wire.Position];
        if (range.Minimum == range.Maximum && (range.Minimum == 0 || range.Minimum == 1)) return true;
        if (wire.Position >= circuit.SourceSize)
        {
            var operation = circuit.Operations[wire.Position - circuit.SourceSize];
            if (operation.Kind == OperationKind.Boolean || operation.ComparisonBit) return true;
        }
        foreach (var group in circuit.OneHotGroups)
        {
            if (group.Members.Contains(wire)) return true;
        }
        return false;
    }

    private static void RouteArgmax(Circuit circuit, ArgmaxNode node, Wire route, Wire[] selected)
    {
        if (node is ArgmaxLeaf leaf)
        {
            selected[leaf.Item] = route;
            return;
        }
        var pair = (ArgmaxPair)node;
        var leftRoute = circuit.Multiply(route, pair.LeftWins);
        var rightRoute = circuit.Affine([(route, 1), (leftRoute, -1)]);
        circuit.AssumeRange(rightRoute, 0, 1);
        RouteArgmax(circuit, pair.Left, leftRoute, selected);
        RouteArgmax(circuit, pair.Right, rightRoute, selected);
    }

    private static void RouteWinner(Circuit circuit, WinnerNode node, Wire route, Wire[] selected)
    {
        if (node is WinnerLeaf leaf)
        {
            selected[leaf.Item] = route;
            return;
        }
        var pair = (WinnerPair)node;
        var leftRoute = circuit.Multiply(route, pair.Left.Mass);
        var rightRoute = circuit.Affine([(route, 1), (leftRoute, -1)]);
        circuit.AssumeRange(rightRoute, 0, 1);
        RouteWinner(circuit, pair.Left, leftRoute, selected);
        RouteWinner(circuit, pair.Right, rightRoute, selected);
    }

    private abstract record WinnerNode(Wire Mass);

    private sealed record WinnerLeaf(Wire Mass, int Item) : WinnerNode(Mass);

    private sealed record WinnerPair(Wire Mass, WinnerNode Left, WinnerNode Right) : WinnerNode(Mass);

    private abstract record ArgmaxNode(Wire Score);

    private sealed record ArgmaxLeaf(Wire Score, int Item) : ArgmaxNode(Score);

    private sealed record ArgmaxPair(Wire Score, Wire LeftWins, ArgmaxNode Left, ArgmaxNode Right) : ArgmaxNode(Score);
}
