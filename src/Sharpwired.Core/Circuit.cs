// Handwired source: src/runtime/numeric-ui-core.ts (class Circuit: the fields, the constructor,
// assumeRange, assumeOneHot, assumeGrid, assumeBit, markRoundingPoint, roundingPoints,
// isNonNegative, source, constant, affine, roundAffine, multiply, select, reglu, boolean, append;
// and unknownRange, inferFormGrid, inferOperationGrid, recordRoundingPoint, evaluateAffineRange,
// multiplyByReluRange, serializeExpression, serializeMacro, identityWire, affineShareThreshold,
// composeAffineTerms, foldConstantTerms, normalizeTerms).

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;

namespace Sharpwired.Core;

/// <summary>
/// A program: a fixed number of source cells and an ordered list of operations. Each
/// operation appends one value after the source cells.
/// </summary>
public sealed partial class Circuit
{
    // Keep wide sums as explicit wires, so that each consumer reads the same FP32 rounding
    // point. Affine forms with fewer terms compose into their consumers.
    internal const int AffineShareThreshold = 4;

    internal readonly List<Operation> OperationList = [];
    internal readonly List<int> DepthList;
    internal readonly List<double?> ConstantList;
    internal readonly Dictionary<string, Wire> ExpressionWires = new(StringComparer.Ordinal);
    internal readonly List<NumericRange> RangeList;
    internal readonly List<double?> GridList;
    internal readonly List<bool> ExactBitList;
    internal readonly List<bool> HasReaderList;
    internal readonly List<OneHotGroup> OneHotGroupList = [];

    // Read-only views of the lists, made at the first read, so that a caller cannot cast a list
    // property back to the list that the circuit changes.
    private ReadOnlyCollection<Operation>? operationsView;
    private ReadOnlyCollection<int>? depthsView;
    private ReadOnlyCollection<double?>? constantsView;
    private ReadOnlyCollection<NumericRange>? rangesView;
    private ReadOnlyCollection<double?>? gridsView;
    private ReadOnlyCollection<bool>? exactBitsView;
    private ReadOnlyCollection<bool>? hasReadersView;
    private ReadOnlyCollection<OneHotGroup>? oneHotGroupsView;

    public Circuit(int sourceSize)
    {
        if (sourceSize < 0) throw new ArgumentException($"the source size must be a nonnegative integer, got {sourceSize}");
        SourceSize = sourceSize;
        DepthList = [.. Enumerable.Repeat(0, sourceSize)];
        ConstantList = [.. Enumerable.Repeat<double?>(null, sourceSize)];
        RangeList = [.. Enumerable.Repeat(NumericRange.Unknown, sourceSize)];
        GridList = [.. Enumerable.Repeat<double?>(null, sourceSize)];
        ExactBitList = [.. Enumerable.Repeat(false, sourceSize)];
        HasReaderList = [.. Enumerable.Repeat(false, sourceSize)];
    }

    public int SourceSize { get; }

    /// <summary>The number of wires: the source cells and the operations.</summary>
    public int Width => SourceSize + OperationList.Count;

    public IReadOnlyList<Operation> Operations => operationsView ??= OperationList.AsReadOnly();

    /// <summary>The depth of each wire: 0 for a source cell, one more than the deepest input for an operation.</summary>
    public IReadOnlyList<int> Depths => depthsView ??= DepthList.AsReadOnly();

    /// <summary>The value of each constant wire, that is, an affine operation with no terms. Null for the other wires.</summary>
    public IReadOnlyList<double?> Constants => constantsView ??= ConstantList.AsReadOnly();

    public IReadOnlyList<NumericRange> Ranges => rangesView ??= RangeList.AsReadOnly();

    /// <summary>
    /// The power of two that every value of a wire is a multiple of, or null when it is not known.
    /// Declared with AssumeGrid, or inferred for sums and products that cannot round.
    /// </summary>
    public IReadOnlyList<double?> Grids => gridsView ??= GridList.AsReadOnly();

    /// <summary>True for a wire that AssumeBit declares to hold exactly 0 or 1.</summary>
    public IReadOnlyList<bool> ExactBits => exactBitsView ??= ExactBitList.AsReadOnly();

    /// <summary>True for a wire that an operation reads, so that a late declaration can be refused.</summary>
    public IReadOnlyList<bool> HasReaders => hasReadersView ??= HasReaderList.AsReadOnly();

    public IReadOnlyList<OneHotGroup> OneHotGroups => oneHotGroupsView ??= OneHotGroupList.AsReadOnly();

    /// <summary>
    /// Declare the range of the values of a wire. A declaration does not change the operations
    /// that already read the wire, so a declaration that narrows the range of such a wire throws,
    /// unless allowExistingReaders is true.
    /// </summary>
    public void AssumeRange(Wire wire, double minimum, double maximum, bool allowExistingReaders = false)
    {
        Validation.AssertWire(wire, Width);
        if (!(minimum <= maximum)) throw new ArgumentException("numeric range is inverted");
        var current = RangeList[wire.Position];
        var narrowed = new NumericRange(Math.Max(current.Minimum, minimum), Math.Min(current.Maximum, maximum));
        if (!(narrowed.Minimum <= narrowed.Maximum)) throw new ArgumentException("numeric range contradicts known bounds");
        var narrows = narrowed.Minimum > current.Minimum || narrowed.Maximum < current.Maximum;
        if (narrows && HasReaderList[wire.Position] && !allowExistingReaders)
        {
            throw new InvalidOperationException(
                $"operations that already read wire {wire} were built with its range {Fp32.Format(current.Minimum)}..{Fp32.Format(current.Maximum)}, "
                + $"and declaring {Fp32.Format(minimum)}..{Fp32.Format(maximum)} now doesn't change them. Declare the range before the first operation "
                + "that reads the wire, or pass {allowExistingReaders: true}");
        }
        RangeList[wire.Position] = narrowed;
    }

    /// <summary>
    /// Declare that the members form an exact one-hot: each member is exactly 0 or 1, and exactly
    /// one member is 1. With allowAllZero, all zeros is also legal. The declaration narrows the range
    /// of each member to 0..1, with the same refusal as AssumeRange.
    /// </summary>
    public void AssumeOneHot(IReadOnlyList<Wire> members, bool allowAllZero = false, bool allowExistingReaders = false)
    {
        if (members.Count == 0) throw new ArgumentException("a one-hot group needs at least one member");
        var seen = new HashSet<Wire>();
        foreach (var member in members)
        {
            Validation.AssertWire(member, Width);
            if (!seen.Add(member)) throw new ArgumentException("one-hot group members must be distinct wires");
            AssumeRange(member, 0, 1, allowExistingReaders);
        }
        OneHotGroupList.Add(new OneHotGroup([.. members], allowAllZero));
        TensorProgram = null;
    }

    /// <summary>
    /// Declare that every value of the wire is a multiple of step, a power of two. A coarser grid
    /// that the wire already has stays. A declaration that changes the grid of a wire that
    /// operations already read throws, unless allowExistingReaders is true.
    /// </summary>
    public void AssumeGrid(Wire wire, double step, bool allowExistingReaders = false)
    {
        Validation.AssertWire(wire, Width);
        if (!Fp32.IsPowerOfTwoGrid(step))
        {
            throw new ArgumentException($"a grid step must be a power of two from 2^-149 to 2^127, got {Fp32.Format(step)}");
        }
        var current = GridList[wire.Position];
        var grid = Math.Max(current ?? step, step);
        if (grid != current && HasReaderList[wire.Position] && !allowExistingReaders)
        {
            var built = current is { } currentGrid ? $"with its grid {Fp32.Format(currentGrid)}" : "without a grid";
            throw new InvalidOperationException(
                $"operations that already read wire {wire} were built {built}, "
                + $"and declaring the grid {Fp32.Format(step)} now doesn't change them. Declare the grid before the first operation "
                + "that reads the wire, or pass {allowExistingReaders: true}");
        }
        GridList[wire.Position] = grid;
    }

    /// <summary>
    /// Declare that the wire holds exactly 0 or 1. The declaration narrows the range to 0..1 and
    /// changes no operation that the builder appends later.
    /// </summary>
    public void AssumeBit(Wire wire, bool allowExistingReaders = false)
    {
        AssumeRange(wire, 0, 1, allowExistingReaders);
        ExactBitList[wire.Position] = true;
    }

    /// <summary>Record an operation as a rounding point, for example a step of a round chain.</summary>
    public void MarkRoundingPoint(Wire wire) => RecordRoundingPoint(wire, RoundingPointKind.Marked);

    /// <summary>Every rounding point in wire order.</summary>
    public IReadOnlyList<RoundingPoint> RoundingPoints()
    {
        var points = new List<RoundingPoint>();
        for (var operationIndex = 0; operationIndex < OperationList.Count; operationIndex++)
        {
            if (OperationList[operationIndex].RoundingPoint is { } kind)
            {
                points.Add(new RoundingPoint(new Wire(SourceSize + operationIndex), kind));
            }
        }
        return points;
    }

    // Earlier records win, for example a boolean atom that Compare returns stays Boolean.
    internal void RecordRoundingPoint(Wire wire, RoundingPointKind kind)
    {
        Validation.AssertWire(wire, Width);
        if (wire.Position < SourceSize) throw new ArgumentException("a rounding point must be an operation, not a source cell");
        var operationIndex = wire.Position - SourceSize;
        var operation = OperationList[operationIndex];
        if (operation.RoundingPoint is null) OperationList[operationIndex] = operation with { RoundingPoint = kind };
    }

    public bool IsNonNegative(Wire wire)
    {
        Validation.AssertWire(wire, Width);
        return RangeList[wire.Position].Minimum >= 0;
    }

    /// <summary>The wire of source cell index, for example Source(0) for the first cell.</summary>
    public Wire Source(int index)
    {
        if (index < 0 || index >= SourceSize) throw new ArgumentException($"source cell {index} is outside the source table");
        return new Wire(index);
    }

    /// <summary>A wire that always holds f(value). An ordinary affine that reads it adds the value to its bias.</summary>
    public Wire Constant(double value) => Affine([], value);

    public Wire Affine(IReadOnlyList<Term> terms, double bias = 0) => Append(OperationKind.Affine, terms, bias, [], 0);

    /// <summary>
    /// An affine operation that keeps its evaluation, its term order and its operand wires,
    /// including repeated terms and constant wires.
    /// </summary>
    public Wire RoundAffine(IReadOnlyList<Term> terms, double bias = 0) =>
        Append(OperationKind.Affine, terms, bias, [], 0, preserveRounding: true);

    /// <summary>f(left × right): one ReGLU when an operand is proven nonnegative, otherwise three operations.</summary>
    public Wire Multiply(Wire left, Wire right)
    {
        var macro = new MultiplyMacro(left, right);
        if (IsNonNegative(right))
        {
            return Append(OperationKind.Reglu, [(left, 1)], 0, [(right, 1)], 0, preserveRounding: true, macro);
        }
        if (IsNonNegative(left))
        {
            return Append(OperationKind.Reglu, [(right, 1)], 0, [(left, 1)], 0, preserveRounding: true, macro);
        }
        var positive = Append(OperationKind.Reglu, [(left, 1)], 0, [(right, 1)], 0, preserveRounding: true);
        var negative = Append(OperationKind.Reglu, [(left, 1)], 0, [(right, -1)], 0, preserveRounding: true);
        return Append(OperationKind.Affine, [(positive, 1), (negative, -1)], 0, [], 0, preserveRounding: true, macro);
    }

    /// <summary>gate × on + (1 - gate) × off, as two products and a sum.</summary>
    public Wire Select(Wire gate, Wire off, Wire on)
    {
        var complement = RoundAffine([(gate, -1)], 1);
        var selectedOn = Multiply(gate, on);
        var selectedOff = Multiply(complement, off);
        return Append(
            OperationKind.Affine,
            [(selectedOn, 1), (selectedOff, 1)],
            0,
            [],
            0,
            preserveRounding: true,
            new SelectMacro(gate, off, on));
    }

    public Wire Reglu(IReadOnlyList<Term> leftTerms, double leftBias, IReadOnlyList<Term> gateTerms, double gateBias) =>
        Append(OperationKind.Reglu, leftTerms, leftBias, gateTerms, gateBias);

    /// <summary>The boolean atom: 0 for a score below 0.5, otherwise 1. The input range must be within 0..1.</summary>
    public Wire Boolean(Wire value)
    {
        Validation.AssertWire(value, Width);
        var range = RangeList[value.Position];
        if (!(range.Minimum >= 0 && range.Maximum <= 1)) throw new ArgumentException("boolean input must be within zero and one");
        return Append(OperationKind.Boolean, [(value, 1)], 0, [], 0);
    }

    /// <summary>
    /// Append an affine, ReGLU or boolean operation. Without preserveRounding, the builder changes
    /// the forms: it drops zero coefficients, sorts and merges the terms, and folds constant wires
    /// into the bias. It also composes small affine producers and folds a constant gate. It returns
    /// an existing wire for an identity or for an expression that it already built.
    /// </summary>
    public Wire Append(
        OperationKind kind,
        IReadOnlyList<Term> leftTerms,
        double leftBias,
        IReadOnlyList<Term> gateTerms,
        double gateBias,
        bool preserveRounding = false,
        MacroRecord? macro = null)
    {
        if (kind == OperationKind.Index) throw new ArgumentException("append builds affine, reglu and boolean operations, not index reads");
        var width = Width;
        foreach (var term in leftTerms) Validation.AssertWire(term.Wire, width);
        foreach (var term in gateTerms) Validation.AssertWire(term.Wire, width);
        ImmutableArray<Term> left = [.. leftTerms];
        ImmutableArray<Term> gate = [.. gateTerms];
        // The wires that the operation reads, so that AssumeRange and AssumeGrid can refuse a later
        // declaration. Without preserveRounding, these are the terms after the builder drops zero
        // coefficients and folds constants, but before it composes small affine producers, so a
        // producer counts as read.
        var readLeft = left;
        var readGate = gate;
        if (!preserveRounding)
        {
            (left, leftBias) = FoldConstantTerms(NormalizeTerms(left), leftBias, ConstantList);
            (gate, gateBias) = FoldConstantTerms(NormalizeTerms(gate), gateBias, ConstantList);
            readLeft = left;
            readGate = gate;
            if (kind == OperationKind.Affine && IdentityWire(left, leftBias) is { } initialAlias) return initialAlias;
            (left, leftBias) = ComposeAffineTerms(left, leftBias, SourceSize, OperationList);
            (gate, gateBias) = ComposeAffineTerms(gate, gateBias, SourceSize, OperationList);
            if (kind == OperationKind.Reglu && gate.Length == 0)
            {
                var constantGate = Math.Max(0, gateBias);
                kind = OperationKind.Affine;
                left = constantGate == 0 ? [] : [.. left.Select(term => term with { Coefficient = term.Coefficient * constantGate })];
                leftBias *= constantGate;
                gateBias = 0;
            }
            else if (kind == OperationKind.Reglu && left.Length == 0 && leftBias == 0)
            {
                kind = OperationKind.Affine;
                gate = [];
                gateBias = 0;
            }
            if (kind == OperationKind.Affine && IdentityWire(left, leftBias) is { } composedAlias) return composedAlias;
        }
        var expressionKey = SerializeExpression(kind, left, leftBias, gate, gateBias)
            + (preserveRounding ? "|round" : "")
            + (macro is null ? "" : "|" + SerializeMacro(macro));
        if (ExpressionWires.TryGetValue(expressionKey, out var existingWire)) return existingWire;
        var wire = new Wire(width);
        var depth = 0;
        foreach (var term in left) depth = Math.Max(depth, DepthList[term.Wire.Position]);
        if (kind == OperationKind.Reglu)
        {
            foreach (var term in gate) depth = Math.Max(depth, DepthList[term.Wire.Position]);
        }
        OperationList.Add(new Operation
        {
            Kind = kind,
            LeftTerms = left,
            LeftBias = leftBias,
            GateTerms = gate,
            GateBias = gateBias,
            Depth = depth + 1,
            PreserveRounding = preserveRounding,
            Macro = macro,
            RoundingPoint = kind == OperationKind.Boolean ? RoundingPointKind.Boolean : null,
        });
        DepthList.Add(depth + 1);
        ConstantList.Add(!preserveRounding && kind == OperationKind.Affine && left.Length == 0 ? leftBias : null);
        GridList.Add(InferOperationGrid(kind, left, leftBias, gate, gateBias, GridList, RangeList));
        ExactBitList.Add(false);
        HasReaderList.Add(false);
        if (left.Length > 0)
        {
            foreach (var term in readLeft) HasReaderList[term.Wire.Position] = true;
        }
        if (gate.Length > 0)
        {
            foreach (var term in readGate) HasReaderList[term.Wire.Position] = true;
        }
        var leftRange = EvaluateAffineRange(left, leftBias, RangeList);
        RangeList.Add(kind switch
        {
            OperationKind.Affine => leftRange,
            OperationKind.Reglu => MultiplyByReluRange(leftRange, EvaluateAffineRange(gate, gateBias, RangeList)),
            _ => new NumericRange(0, 1),
        });
        ExpressionWires[expressionKey] = wire;
        TensorProgram = null;
        return wire;
    }

    // The range of an affine form evaluated in FP32. Each bound follows the rounded coefficient,
    // the rounded product and the rounded sum at each step. A NaN bound gives the unknown range.
    internal static NumericRange EvaluateAffineRange(ImmutableArray<Term> terms, double bias, IReadOnlyList<NumericRange> ranges)
    {
        var minimum = Fp32.Round(bias);
        var maximum = Fp32.Round(bias);
        foreach (var (wire, coefficient) in terms)
        {
            var range = ranges[wire.Position];
            var roundedCoefficient = Fp32.Round(coefficient);
            if (roundedCoefficient > 0)
            {
                minimum = Fp32.Round(minimum + Fp32.Round(roundedCoefficient * range.Minimum));
                maximum = Fp32.Round(maximum + Fp32.Round(roundedCoefficient * range.Maximum));
            }
            else
            {
                minimum = Fp32.Round(minimum + Fp32.Round(roundedCoefficient * range.Maximum));
                maximum = Fp32.Round(maximum + Fp32.Round(roundedCoefficient * range.Minimum));
            }
            if (double.IsNaN(minimum) || double.IsNaN(maximum)) return NumericRange.Unknown;
        }
        if (double.IsNaN(minimum) || double.IsNaN(maximum)) return NumericRange.Unknown;
        return new NumericRange(minimum, maximum);
    }

    internal static NumericRange MultiplyByReluRange(NumericRange left, NumericRange gate)
    {
        var gateMinimum = Math.Max(0, gate.Minimum);
        var gateMaximum = Math.Max(0, gate.Maximum);
        double[] products =
        [
            Fp32.Round(left.Minimum * gateMinimum),
            Fp32.Round(left.Minimum * gateMaximum),
            Fp32.Round(left.Maximum * gateMinimum),
            Fp32.Round(left.Maximum * gateMaximum),
        ];
        // Zero times an infinite bound can give NaN. Do not turn it into a finite bound.
        if (products.Any(double.IsNaN)) return NumericRange.Unknown;
        // JavaScript Math.min and Math.max order -0 below +0, as Math.Min and Math.Max do.
        var minimum = products[0];
        var maximum = products[0];
        for (var index = 1; index < products.Length; index++)
        {
            minimum = Math.Min(minimum, products[index]);
            maximum = Math.Max(maximum, products[index]);
        }
        return new NumericRange(minimum, maximum);
    }

    // The grid of an affine form evaluated in FP32, when every term reads a wire with a grid and the
    // declared ranges keep every product and partial sum within 2^24 grid steps. Magnitude bounds
    // |bias| plus every |coefficient × operand|, which bounds every partial sum in any term order.
    internal static (double Grid, double Magnitude)? InferFormGrid(
        ImmutableArray<Term> terms,
        double bias,
        IReadOnlyList<double?> grids,
        IReadOnlyList<NumericRange> ranges)
    {
        var roundedBias = Fp32.Round(bias);
        var grid = roundedBias == 0 ? double.PositiveInfinity : Fp32.LowestPowerOfTwo(roundedBias);
        var magnitude = Math.Abs(roundedBias);
        foreach (var (wire, coefficient) in terms)
        {
            if (grids[wire.Position] is not { } wireGrid) return null;
            var range = ranges[wire.Position];
            var largestOperand = Math.Max(Math.Abs(range.Minimum), Math.Abs(range.Maximum));
            // An open range can hold Infinity, and f(0 × Infinity) is NaN.
            if (!double.IsFinite(largestOperand)) return null;
            var roundedCoefficient = Fp32.Round(coefficient);
            if (roundedCoefficient == 0) continue;
            grid = Math.Min(grid, wireGrid * Fp32.LowestPowerOfTwo(roundedCoefficient));
            magnitude += Math.Abs(roundedCoefficient) * largestOperand;
        }
        if (!(double.IsFinite(grid) && grid >= Fp32.SmallestGrid && magnitude <= Fp32.ExactGridSteps * grid)) return null;
        // A finite FP32 multiple of a power of two above 2^127 can only be 0, which is also a multiple of 2^127.
        return (Math.Min(grid, Fp32.LargestGrid), magnitude);
    }

    // The grid of the result of an operation. An affine sum takes the grid of its form. A ReGLU
    // takes the product of the grids of both forms, while the product stays within 2^24 grid
    // steps. A boolean atom returns 0 or 1, so its grid is 1.
    internal static double? InferOperationGrid(
        OperationKind kind,
        ImmutableArray<Term> leftTerms,
        double leftBias,
        ImmutableArray<Term> gateTerms,
        double gateBias,
        IReadOnlyList<double?> grids,
        IReadOnlyList<NumericRange> ranges)
    {
        if (kind == OperationKind.Boolean) return 1;
        if (InferFormGrid(leftTerms, leftBias, grids, ranges) is not { } left) return null;
        if (kind == OperationKind.Affine) return left.Grid;
        if (InferFormGrid(gateTerms, gateBias, grids, ranges) is not { } gate) return null;
        var grid = left.Grid * gate.Grid;
        if (!(grid >= Fp32.SmallestGrid && left.Magnitude * gate.Magnitude <= Fp32.ExactGridSteps * grid)) return null;
        return Math.Min(grid, Fp32.LargestGrid);
    }

    // The key of the expression cache. Fp32.Format writes both zeros as "0", as JavaScript
    // String(number) does, so -0 and +0 give the same key, as in Handwired.
    internal static string SerializeExpression(
        OperationKind kind,
        ImmutableArray<Term> leftTerms,
        double leftBias,
        ImmutableArray<Term> gateTerms,
        double gateBias)
    {
        var key = new StringBuilder();
        key.Append(KindName(kind)).Append(',').Append(Fp32.Format(leftBias));
        foreach (var (wire, coefficient) in leftTerms) key.Append(',').Append(wire.Position).Append(',').Append(Fp32.Format(coefficient));
        key.Append(",|,").Append(Fp32.Format(gateBias));
        foreach (var (wire, coefficient) in gateTerms) key.Append(',').Append(wire.Position).Append(',').Append(Fp32.Format(coefficient));
        return key.ToString();
    }

    internal static string KindName(OperationKind kind) => kind switch
    {
        OperationKind.Affine => "affine",
        OperationKind.Reglu => "reglu",
        OperationKind.Boolean => "boolean",
        OperationKind.Index => "index",
        _ => throw new ArgumentException($"unknown operation kind {kind}"),
    };

    internal static string SerializeMacro(MacroRecord macro) => macro switch
    {
        MultiplyMacro multiply => $"multiply,{multiply.Left},{multiply.Right}",
        SelectMacro select => $"select,{select.Gate},{select.Off},{select.On}",
        _ => throw new ArgumentException($"unknown macro record {macro.GetType().Name}"),
    };

    internal static Wire? IdentityWire(ImmutableArray<Term> terms, double bias)
    {
        if (bias != 0 || terms.Length != 1 || terms[0].Coefficient != 1) return null;
        return terms[0].Wire;
    }

    // Copy the terms of each small affine producer into the form. A producer qualifies when it is
    // an affine operation without preserveRounding and with fewer than AffineShareThreshold terms.
    internal static (ImmutableArray<Term> Terms, double Bias) ComposeAffineTerms(
        ImmutableArray<Term> terms,
        double bias,
        int sourceSize,
        IReadOnlyList<Operation> operations)
    {
        var composedTerms = new List<Term>();
        foreach (var (wire, coefficient) in terms)
        {
            var operation = wire.Position < sourceSize ? null : operations[wire.Position - sourceSize];
            if (operation is null
                || operation.Kind != OperationKind.Affine
                || operation.PreserveRounding
                || operation.LeftTerms.Length >= AffineShareThreshold)
            {
                composedTerms.Add(new Term(wire, coefficient));
                continue;
            }
            bias += coefficient * operation.LeftBias;
            foreach (var (dependency, dependencyCoefficient) in operation.LeftTerms)
            {
                composedTerms.Add(new Term(dependency, coefficient * dependencyCoefficient));
            }
        }
        return (NormalizeTerms(composedTerms), bias);
    }

    internal static (ImmutableArray<Term> Terms, double Bias) FoldConstantTerms(
        ImmutableArray<Term> terms,
        double bias,
        IReadOnlyList<double?> constants)
    {
        var dynamicTerms = ImmutableArray.CreateBuilder<Term>(terms.Length);
        foreach (var (wire, coefficient) in terms)
        {
            if (constants[wire.Position] is { } constant) bias += coefficient * constant;
            else dynamicTerms.Add(new Term(wire, coefficient));
        }
        return (dynamicTerms.ToImmutable(), bias);
    }

    // Sort the terms by wire, merge the terms of the same wire in their given order, and drop
    // zero coefficients. The sort is stable, as the JavaScript sort is.
    internal static ImmutableArray<Term> NormalizeTerms(IEnumerable<Term> terms)
    {
        var normalizedTerms = new List<Term>();
        foreach (var term in terms.OrderBy(term => term.Wire.Position))
        {
            if (normalizedTerms.Count > 0 && normalizedTerms[^1].Wire == term.Wire)
            {
                normalizedTerms[^1] = normalizedTerms[^1] with { Coefficient = normalizedTerms[^1].Coefficient + term.Coefficient };
            }
            else
            {
                normalizedTerms.Add(term);
            }
        }
        return [.. normalizedTerms.Where(term => term.Coefficient != 0)];
    }
}
