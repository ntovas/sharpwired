// Handwired source: src/runtime/numeric-ui-core.ts (resolveIndexPosition, readIndexedValue,
// Circuit.indexRead, Circuit.indexReadAffine, outOfRangeRules, registerCandidateTable,
// indexReadRange, reachableCandidates).

using System.Collections.Immutable;

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    // floor(index) as a table position: 0..count-1 inside the table. Outside the table, the
    // nearest end, or -1 where that side reads +0. -2 for a NaN index. The floor and the
    // comparisons run in float64 on an FP32 value, so nothing rounds.
    internal static int ResolveIndexPosition(double index, int count, bool belowZero, bool aboveZero)
    {
        if (double.IsNaN(index)) return -2;
        var position = Math.Floor(index);
        if (position < 0) return belowZero ? -1 : 0;
        if (position > count - 1) return aboveZero ? -1 : count - 1;
        return (int)position;
    }

    // The index read instruction on one index value: copy the FP32 value of the selected
    // candidate, +0 where that side reads zero, and NaN for a NaN index.
    internal static float ReadIndexedValue(float[] values, IndexReadInstruction read, double index)
    {
        var position = ResolveIndexPosition(
            index,
            read.Table.Candidates.Length,
            read.Below == OutOfRange.Zero,
            read.Above == OutOfRange.Zero);
        if (position >= 0) return values[read.Table.Candidates[position].Position];
        return position == -1 ? 0f : float.NaN;
    }

    // Handwired keys candidate tables by array identity and by content. Sharpwired copies the
    // candidates, so it keys the tables by content only.
    internal readonly Dictionary<ImmutableArray<Wire>, CandidateTable> CandidateTablesByContent = new(CandidateContentComparer.Instance);

    /// <summary>
    /// Read candidates[floor(index)] as one instruction, for example the height of a row at a
    /// computed row. The index needs a declared range strictly inside ±2^24 and must hold an
    /// integer, which ValidateIndexReads checks in tests. The range declared on the index wire
    /// counts also when the builder copies a small affine index into the read. Outside the table,
    /// the read clamps to the nearest end, or reads +0 with OutOfRange.Zero. The read copies the
    /// selected value without rounding. Candidates can repeat, can be infinite, and need no
    /// declared range.
    /// </summary>
    public Wire IndexRead(Wire index, IReadOnlyList<Wire> candidates, OutOfRange outOfRange = OutOfRange.Clamp)
    {
        Validation.AssertWire(index, Width);
        return IndexReadAffine([new Term(index, 1)], 0, candidates, outOfRange, outOfRange, RangeList[index.Position]);
    }

    /// <summary>
    /// The index read instruction with the index written as an affine form and one rule for each
    /// side. Small affine producers compose into the form, as they compose into a ReGLU gate. The
    /// builder infers the index range from the declared ranges of the operands after it composes
    /// them. Then it narrows the range with indexRange, a caller promise about the value of the
    /// form, as AssumeRange is. ValidateIndexReads checks the promise in tests.
    /// </summary>
    /// <remarks>
    /// The builder copies the candidates into an immutable table. Handwired freezes the caller's
    /// array, so that a change after a read throws. Here, a change to the caller's list after a
    /// read cannot reach the table, so that case cannot occur.
    /// </remarks>
    public Wire IndexReadAffine(
        IReadOnlyList<Term> indexTerms,
        double indexBias,
        IReadOnlyList<Wire> candidates,
        OutOfRange below,
        OutOfRange above,
        NumericRange? indexRange = null)
    {
        var width = Width;
        if (!Enum.IsDefined(below) || !Enum.IsDefined(above)) throw new ArgumentException("index read sides must be clamp or zero");
        foreach (var (wire, coefficient) in indexTerms)
        {
            Validation.AssertWire(wire, width);
            if (!double.IsFinite(coefficient)) throw new ArgumentException("index coefficient must be finite");
        }
        if (!double.IsFinite(indexBias)) throw new ArgumentException("index bias must be finite");
        var table = RegisterCandidateTable(candidates);
        var (terms, bias) = FoldConstantTerms(NormalizeTerms(indexTerms), indexBias, ConstantList);
        // The wires that the index form reads, counted as Append counts them: after the builder
        // drops zero coefficients and folds constant wires into the bias, but before it copies
        // small affine producers in.
        var readTerms = terms;
        (terms, bias) = ComposeAffineTerms(terms, bias, SourceSize, OperationList);
        if (terms.Length == 0)
        {
            // A constant index selects its candidate while the builder builds.
            var constantIndex = Fp32.Round(bias);
            if (double.IsNaN(constantIndex)) throw new ArgumentException("a constant index must not be NaN");
            var position = ResolveIndexPosition(constantIndex, table.Candidates.Length, below == OutOfRange.Zero, above == OutOfRange.Zero);
            return position >= 0 ? table.Candidates[position] : Affine([], 0);
        }
        var acceptedRange = EvaluateAffineRange(terms, bias, RangeList);
        if (indexRange is { } declared)
        {
            acceptedRange = new NumericRange(
                Math.Max(acceptedRange.Minimum, declared.Minimum),
                Math.Min(acceptedRange.Maximum, declared.Maximum));
            // A NaN endpoint passes here and fails the magnitude check below.
            if (acceptedRange.Minimum > acceptedRange.Maximum)
            {
                throw new ArgumentException("the declared index range contradicts the range inferred from the index form");
            }
        }
        if (!(acceptedRange.Minimum > -Validation.IndexMagnitudeLimit && acceptedRange.Maximum < Validation.IndexMagnitudeLimit))
        {
            throw new ArgumentException(
                "index read needs an index range declared strictly inside ±2^24, "
                + $"got {Fp32.Format(acceptedRange.Minimum)}..{Fp32.Format(acceptedRange.Maximum)}");
        }
        var expressionKey = SerializeExpression(OperationKind.Index, [], 0, terms, bias)
            + $"|{RuleName(below)},{RuleName(above)}|table{table.Id}";
        if (ExpressionWires.TryGetValue(expressionKey, out var existingWire)) return existingWire;
        var depth = table.MaximumDepth;
        foreach (var term in terms) depth = Math.Max(depth, DepthList[term.Wire.Position]);
        OperationList.Add(new Operation
        {
            Kind = OperationKind.Index,
            LeftTerms = [],
            LeftBias = 0,
            GateTerms = terms,
            GateBias = bias,
            Depth = depth + 1,
            IndexRead = new IndexReadInstruction(table, below, above, acceptedRange),
        });
        DepthList.Add(depth + 1);
        ConstantList.Add(null);
        GridList.Add(null);
        ExactBitList.Add(false);
        HasReaderList.Add(false);
        foreach (var term in readTerms) HasReaderList[term.Wire.Position] = true;
        RangeList.Add(IndexReadRange(table, acceptedRange, below, above, RangeList));
        var readWire = new Wire(width);
        ExpressionWires[expressionKey] = readWire;
        return readWire;
    }

    // Register a candidates list once for each content: check every candidate, record the deepest
    // one, and copy the list into an immutable table. Lists with the same content share one table,
    // for example a table that a loop builds again in each iteration.
    internal CandidateTable RegisterCandidateTable(IReadOnlyList<Wire> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("an index read needs at least one candidate");
        var width = Width;
        var maximumDepth = 0;
        foreach (var candidate in candidates)
        {
            Validation.AssertWire(candidate, width);
            maximumDepth = Math.Max(maximumDepth, DepthList[candidate.Position]);
        }
        ImmutableArray<Wire> content = [.. candidates];
        if (!CandidateTablesByContent.TryGetValue(content, out var table))
        {
            table = new CandidateTable(CandidateTablesByContent.Count, content, maximumDepth);
            CandidateTablesByContent[content] = table;
            foreach (var candidate in content) HasReaderList[candidate.Position] = true;
        }
        return table;
    }

    // The union of the ranges of every candidate that an index within indexRange can reach, plus 0
    // where a side that reads zero is reachable. The table caches the union for each reachable
    // span, so the builder scans a large table one time for each span. A candidate range that
    // narrows after a read keeps the wider union for later reads with the same span. That union
    // is still a correct bound.
    internal static NumericRange IndexReadRange(
        CandidateTable table,
        NumericRange indexRange,
        OutOfRange below,
        OutOfRange above,
        IReadOnlyList<NumericRange> ranges)
    {
        var candidates = table.Candidates;
        var count = candidates.Length;
        var (lowestPosition, highestPosition) = ReachableSpan(indexRange, count);
        var key = $"{lowestPosition},{highestPosition},{RuleName(below)},{RuleName(above)}";
        if (table.RangeByReach.TryGetValue(key, out var cached)) return cached;
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        void Include(NumericRange candidateRange)
        {
            minimum = Math.Min(minimum, candidateRange.Minimum);
            maximum = Math.Max(maximum, candidateRange.Maximum);
        }
        var lastDirectPosition = Math.Min(highestPosition, count - 1);
        for (var position = Math.Max(lowestPosition, 0); position <= lastDirectPosition; position++)
        {
            Include(ranges[candidates[position].Position]);
        }
        var zeroRange = new NumericRange(0, 0);
        if (lowestPosition < 0) Include(below == OutOfRange.Zero ? zeroRange : ranges[candidates[0].Position]);
        if (highestPosition > count - 1) Include(above == OutOfRange.Zero ? zeroRange : ranges[candidates[count - 1].Position]);
        var range = new NumericRange(minimum, maximum);
        table.RangeByReach[key] = range;
        return range;
    }

    // The candidates that an index read can return for an index within the range that the builder
    // accepted, as in IndexReadRange. A side that reads zero adds +0, not a candidate.
    internal static IReadOnlyList<Wire> ReachableCandidates(IndexReadInstruction read)
    {
        var candidates = read.Table.Candidates;
        var count = candidates.Length;
        var (lowestPosition, highestPosition) = ReachableSpan(read.IndexRange, count);
        var reachable = new List<Wire>();
        var lastDirectPosition = Math.Min(highestPosition, count - 1);
        for (var position = Math.Max(lowestPosition, 0); position <= lastDirectPosition; position++) reachable.Add(candidates[position]);
        if (lowestPosition < 0 && read.Below == OutOfRange.Clamp) reachable.Add(candidates[0]);
        if (highestPosition > count - 1 && read.Above == OutOfRange.Clamp) reachable.Add(candidates[count - 1]);
        return reachable;
    }

    // floor of each bound of the index range, limited to -1..count. The builder accepts only
    // finite bounds strictly inside ±2^24, so each result fits in an int.
    private static (int Lowest, int Highest) ReachableSpan(NumericRange indexRange, int count) =>
        ((int)Math.Max(-1, Math.Min(count, Math.Floor(indexRange.Minimum))),
         (int)Math.Max(-1, Math.Min(count, Math.Floor(indexRange.Maximum))));

    private static string RuleName(OutOfRange rule) => rule == OutOfRange.Zero ? "zero" : "clamp";

    // Two tables have the same content when they hold the same wires in the same order.
    private sealed class CandidateContentComparer : IEqualityComparer<ImmutableArray<Wire>>
    {
        public static readonly CandidateContentComparer Instance = new();

        public bool Equals(ImmutableArray<Wire> left, ImmutableArray<Wire> right) => left.AsSpan().SequenceEqual(right.AsSpan());

        public int GetHashCode(ImmutableArray<Wire> candidates)
        {
            var hash = new HashCode();
            foreach (var candidate in candidates) hash.Add(candidate.Position);
            return hash.ToHashCode();
        }
    }
}
