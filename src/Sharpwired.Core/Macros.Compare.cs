// Handwired source: src/runtime/numeric-ui-core.ts (Relation, CompareOptions, SignTest,
// CompareOperand, emitCompare, compareOperand, constantOperand, emitGridCompare,
// largestPartialSum, emitIntegerBit, fullLiftExponent, roundingStageLiftExponent, CascadeStage,
// planSignCascade, powerOfTwoExponentAtLeast, emitCascadeCompare, emitMinimumExact,
// emitMaximumExact, emitClampExact, emitSelectOperand).

using System.Collections.Immutable;

namespace Sharpwired.Core;

/// <summary>The relation of a comparison: a > b, a >= b, a &lt; b, a &lt;= b or a === b.</summary>
public enum Relation
{
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Equal,
}

/// <summary>The options of Compare.</summary>
public readonly record struct CompareOptions
{
    /// <summary>
    /// End with a boolean atom instead of a ReLU stage. For example, [a > b] for operands within
    /// 0..16,384 then takes 13 layers instead of 15, and [a >= b] on a 1/64 grid is one atom. The
    /// atom needs an executor that keeps FP32 rounding at the atom.
    /// </summary>
    public bool EndWithBoolean { get; init; }
}

/// <summary>One stage min(limit, scale × input) of the sign cascade.</summary>
internal readonly record struct CascadeStage(double Scale, double Limit);

public static partial class Macros
{
    // Every nonzero FP32 difference, down to 2^-149, lifted to at least 1.
    private const int FullLiftExponent = 149;

    // Every nonzero FP32 difference lifted to at least 2^-45, which a final rounding stage with
    // scale 2^22 resolves.
    private const int RoundingStageLiftExponent = 104;

    private enum SignTest
    {
        Greater,
        AtLeast,
        Equal,
    }

    // One side of a comparison. A constant has no wire, a range of one value, and the grid of its
    // value, or Infinity for 0, which is on every grid.
    private readonly record struct CompareOperand(Wire? Wire, double Value, NumericRange Range, double? Grid);

    /// <summary>
    /// [a relation b] as an exact bit, for example circuit.Compare(pointerX, Relation.GreaterOrEqual,
    /// windowLeft). The bit equals the JavaScript comparison of the FP32 values of the two wires for
    /// each pair of values within the declared ranges. A threshold is a constant wire, for example
    /// circuit.Constant(3), which folds into the bias. Pick an FP32 threshold before the build.
    /// <list type="bullet">
    /// <item>Both operands need finite declared ranges. The builder refuses an open range.</item>
    /// <item>When both operands are on grids and the ranges stay within 2^24 grid steps, the bit
    /// comes from the integer D = (a - b) / grid. For example, [a >= b] is relu(1 - relu(-D)).</item>
    /// <item>Otherwise the difference d = f(a - b) stays its own operation, and a sign cascade lifts
    /// each nonzero d, down to 2^-149, to at least 1 in stages that multiply by powers of two and
    /// clamp, with no rounding in a stage.</item>
    /// </list>
    /// Each operation that Compare builds is a rounding point of kind IntegerBit or SignCascade.
    /// Compare reads the operands as wires, so a small affine operand becomes its own operation.
    /// </summary>
    public static Wire Compare(this Circuit circuit, Wire a, Relation relation, Wire b, CompareOptions options = default)
    {
        if (!Enum.IsDefined(relation))
        {
            throw new ArgumentException($"comparison relation must be >, >=, <, <= or ===, got {relation}");
        }
        var left = CreateCompareOperand(circuit, a);
        var right = CreateCompareOperand(circuit, b);
        if (relation is Relation.Less or Relation.LessOrEqual) (left, right) = (right, left);
        var test = relation switch
        {
            Relation.Greater or Relation.Less => SignTest.Greater,
            Relation.Equal => SignTest.Equal,
            _ => SignTest.AtLeast,
        };
        if (left.Wire is null && right.Wire is null)
        {
            var holds = test switch
            {
                SignTest.Greater => left.Value > right.Value,
                SignTest.AtLeast => left.Value >= right.Value,
                _ => left.Value == right.Value,
            };
            return circuit.Affine([], holds ? 1 : 0);
        }
        foreach (var operand in new[] { left, right })
        {
            var (minimum, maximum) = operand.Range;
            if (!(double.IsFinite(minimum) && double.IsFinite(maximum)))
            {
                throw new ArgumentException(
                    $"Compare needs finite declared ranges on both operands, got {Fp32.Format(minimum)}..{Fp32.Format(maximum)}");
            }
        }
        // A wire equals itself for each value that its finite declared range allows. This check comes
        // after the range check, because a wire with an open range can be NaN, for example
        // Infinity × 0, and NaN >= NaN is false.
        if (left.Wire == right.Wire) return circuit.Affine([], test == SignTest.Greater ? 0 : 1);
        var differenceBound = Math.Max(
            Math.Abs(left.Range.Maximum - right.Range.Minimum),
            Math.Abs(left.Range.Minimum - right.Range.Maximum));
        // Rounding is monotone, so each f(a - b) stays within f(differenceBound).
        if (!double.IsFinite(Fp32.Round(differenceBound)))
        {
            throw new ArgumentException("the difference of the declared operand ranges can overflow FP32");
        }
        var terms = new List<Term>();
        if (left.Wire is { } leftWire) terms.Add((leftWire, 1));
        if (right.Wire is { } rightWire) terms.Add((rightWire, -1));
        // At most one side is a constant, so the bias is exactly that constant or its negation.
        var bias = left.Value - right.Value;
        var endWithBoolean = options.EndWithBoolean;
        var bit = EmitGridCompare(circuit, left, right, terms, bias, test, endWithBoolean)
            ?? EmitCascadeCompare(circuit, terms, bias, Fp32.Round(differenceBound), test, endWithBoolean);
        circuit.AssumeRange(bit, 0, 1);
        circuit.AssumeGrid(bit, 1);
        // Both constructions end with an operation, a ReGLU product or a boolean atom.
        if (bit.Position >= circuit.SourceSize)
        {
            var operationIndex = bit.Position - circuit.SourceSize;
            circuit.OperationList[operationIndex] = circuit.OperationList[operationIndex] with { ComparisonBit = true };
        }
        return bit;
    }

    private static CompareOperand CreateCompareOperand(Circuit circuit, Wire operand)
    {
        Validation.AssertWire(operand, circuit.Width);
        if (circuit.Constants[operand.Position] is { } constant)
        {
            // A constant wire holds f(bias).
            var value = Fp32.Round(constant);
            if (!double.IsFinite(value)) throw new ArgumentException("a constant comparison operand must be finite");
            return ConstantOperand(value);
        }
        return new CompareOperand(operand, 0, circuit.Ranges[operand.Position], circuit.Grids[operand.Position]);
    }

    private static CompareOperand ConstantOperand(double value)
    {
        // -0 and +0 compare equal, so a zero constant becomes +0.
        var normalized = value == 0 ? 0 : value;
        return new CompareOperand(
            null,
            normalized,
            new NumericRange(normalized, normalized),
            normalized == 0 ? double.PositiveInfinity : Fp32.LowestPowerOfTwo(normalized));
    }

    // Integer bits for operands on grids. D = (a - b) / grid is an integer, and each product and
    // partial sum below is an integer number of grid steps within 2^24, so nothing rounds, and
    // float64, reordered or fused sums give the same bits. Returns null when an operand has no grid
    // or the ranges are too wide, and Compare then builds a sign cascade.
    private static Wire? EmitGridCompare(
        Circuit circuit,
        CompareOperand left,
        CompareOperand right,
        IReadOnlyList<Term> terms,
        double bias,
        SignTest test,
        bool endWithBoolean)
    {
        if (left.Grid is not { } leftGrid || right.Grid is not { } rightGrid) return null;
        var grid = Math.Min(leftGrid, rightGrid);
        // The largest partial sum of sign × D + offset in stored order, bias first, in grid steps.
        // Each partial sum is an integer number of grid steps, so a float64 bound that is at most a
        // power of two cannot hide a larger integer.
        double LargestSteps(double sign, double offset) => LargestPartialSum(
            [.. terms.Select(term => new Term(term.Wire, sign * term.Coefficient / grid))],
            sign * bias / grid + offset,
            circuit.Ranges);
        var emitted = new List<Wire>();
        Wire bit;
        if (endWithBoolean)
        {
            // The one-atom integer comparison: the score 0.5 + (D - offset) × 2^-24, added bias
            // first, is a multiple of 2^-24 within 0.25..0.75 while each partial sum stays within
            // 2^22 grid steps, which FP32 holds exactly. So D = 0 scores exactly 0.5 and rounds to
            // 1, and D = -1 scores 0.5 - 2^-24 and rounds to 0.
            var scoreScale = Math.ScaleB(1.0, -24) / grid;
            var fits = test == SignTest.Equal
                ? LargestSteps(1, 0) <= Math.ScaleB(1.0, 22) && LargestSteps(-1, 0) <= Math.ScaleB(1.0, 22)
                : LargestSteps(1, test == SignTest.Greater ? -1 : 0) <= Math.ScaleB(1.0, 22);
            if (!(fits && scoreScale >= Fp32.SmallestGrid && scoreScale <= Fp32.LargestGrid)) return null;
            Wire EmitAtom(double sign, double offset)
            {
                var atom = circuit.Append(
                    OperationKind.Boolean,
                    [.. terms.Select(term => new Term(term.Wire, sign * term.Coefficient * scoreScale))],
                    0.5 + (sign * bias / grid - offset) * Math.ScaleB(1.0, -24),
                    [],
                    0,
                    preserveRounding: true);
                emitted.Add(atom);
                return atom;
            }
            if (test == SignTest.Equal)
            {
                bit = circuit.Reglu([], 1, [(EmitAtom(1, 0), 1), (EmitAtom(-1, 0), 1)], -1);
                emitted.Add(bit);
            }
            else
            {
                bit = EmitAtom(1, test == SignTest.Greater ? 1 : 0);
            }
        }
        else
        {
            var scale = 1 / grid;
            // The sum of the bit, 1 - ramp, adds one more step.
            var fits = test == SignTest.Equal
                ? LargestSteps(1, 0) < Fp32.ExactGridSteps && LargestSteps(-1, 0) < Fp32.ExactGridSteps
                : LargestSteps(-1, test == SignTest.Greater ? 1 : 0) < Fp32.ExactGridSteps;
            if (!(fits && scale <= Fp32.LargestGrid)) return null;
            // relu(sign × D + offset), which reads the operand wires as written.
            bit = EmitIntegerBit(circuit, test, emitted, (sign, offset) => circuit.Append(
                OperationKind.Reglu,
                [],
                1,
                [.. terms.Select(term => new Term(term.Wire, sign * term.Coefficient * scale))],
                sign * bias / grid + offset,
                preserveRounding: true));
        }
        foreach (var wire in emitted) circuit.RecordRoundingPoint(wire, RoundingPointKind.IntegerBit);
        return bit;
    }

    // The largest magnitude of a product or partial sum of bias + Σ coefficient × operand, added in
    // stored order, from the declared operand ranges.
    private static double LargestPartialSum(ImmutableArray<Term> terms, double bias, IReadOnlyList<NumericRange> ranges)
    {
        var minimum = bias;
        var maximum = bias;
        var largest = Math.Abs(bias);
        foreach (var (wire, coefficient) in terms)
        {
            var range = ranges[wire.Position];
            var low = coefficient >= 0 ? coefficient * range.Minimum : coefficient * range.Maximum;
            var high = coefficient >= 0 ? coefficient * range.Maximum : coefficient * range.Minimum;
            minimum += low;
            maximum += high;
            largest = Math.Max(largest, Math.Max(Math.Max(Math.Abs(low), Math.Abs(high)), Math.Max(Math.Abs(minimum), Math.Abs(maximum))));
        }
        return largest;
    }

    // The exact bit of D > 0, D >= 0 or D === 0, when each value of D is 0 or at least 1 away
    // from 0, for example an integer. emitRamp(sign, offset) builds relu(sign × D + offset):
    // D >= 0 is relu(1 - relu(-D)), D > 0 is relu(1 - relu(1 - D)), and D === 0 is
    // relu(1 - relu(D) - relu(-D)). The bit is a ReGLU product, so readers cannot copy large ReLU
    // values into their own sums.
    private static Wire EmitIntegerBit(Circuit circuit, SignTest test, List<Wire> emitted, Func<double, double, Wire> emitRamp)
    {
        Wire[] ramps = test == SignTest.Equal
            ? [emitRamp(1, 0), emitRamp(-1, 0)]
            : [emitRamp(-1, test == SignTest.Greater ? 1 : 0)];
        var bit = circuit.Reglu([], 1, [.. ramps.Select(ramp => new Term(ramp, -1))], 1);
        emitted.AddRange(ramps);
        emitted.Add(bit);
        return bit;
    }

    // Stages of min(limit, scale × input). A multiplication by a power of two changes only the
    // exponent, and min(limit, x) = x - relu(x - limit) is exact while x is at most 2^24 × limit.
    // So the first stage multiplies as far as the difference bound allows, and each later stage by
    // up to 2^24, until the product of the scales reaches 2^liftExponent. A bound above 2^24 first
    // comes down by up to 2^24 for each stage with no multiplication, because a multiplication by
    // less than 1 rounds the smallest differences to 0. For example, a bound of 16,384 with
    // liftExponent 149 gives the scales 2^10, 2^24, 2^24, 2^24, 2^24, 2^24 and 2^19, each with
    // limit 1. A finite bound needs at most 12 stages.
    internal static IReadOnlyList<CascadeStage> PlanSignCascade(double differenceBound, int liftExponent)
    {
        var boundExponent = PowerOfTwoExponentAtLeast(Math.Max(differenceBound, Fp32.SmallestGrid));
        var stages = new List<CascadeStage>();
        var limitExponent = boundExponent;
        var gainExponent = 0;
        while (stages.Count == 0 || limitExponent > 0 || gainExponent < liftExponent)
        {
            var nextLimitExponent = Math.Max(0, limitExponent - 24);
            // A coefficient above 2^127 is not an FP32 value, for example for a bound below 2^-103.
            var room = Math.Min(127, 24 + nextLimitExponent - limitExponent);
            var scaleExponent = Math.Min(room, Math.Max(0, liftExponent - gainExponent));
            stages.Add(new CascadeStage(Math.ScaleB(1.0, scaleExponent), Math.ScaleB(1.0, nextLimitExponent)));
            limitExponent = nextLimitExponent;
            gainExponent += scaleExponent;
        }
        return stages;
    }

    // The smallest integer e with 2^e >= value, for a finite positive value.
    private static int PowerOfTwoExponentAtLeast(double value)
    {
        var exponent = (int)Math.Ceiling(Math.Log2(value));
        while (Math.ScaleB(1.0, exponent) < value) exponent++;
        while (Math.ScaleB(1.0, exponent - 1) >= value) exponent--;
        return exponent;
    }

    // The sign cascade on d = f(a - b). The final stage is one or two ReGLU products, for example
    // relu(1 - scale × stage) for a >= b, instead of a clamp and a negation.
    private static Wire EmitCascadeCompare(
        Circuit circuit,
        IReadOnlyList<Term> terms,
        double bias,
        double differenceBound,
        SignTest test,
        bool endWithBoolean)
    {
        var emitted = new List<Wire>();
        // For finite FP32 operands, f(a - b) is 0 exactly when a === b, and otherwise has the sign
        // of a - b, because subnormal values fill the gap next to 0. A comparison against 0 reads
        // its operand directly.
        Wire difference;
        double differenceSign;
        if (bias == 0 && terms.Count == 1)
        {
            (difference, differenceSign) = terms[0];
        }
        else
        {
            difference = circuit.RoundAffine(terms, bias);
            differenceSign = 1;
            emitted.Add(difference);
        }
        var stages = PlanSignCascade(differenceBound, endWithBoolean ? RoundingStageLiftExponent : FullLiftExponent);
        // After clamp stage k, the output is min(limit_k, scale_1 × ... × scale_k × relu(sign × d)).
        Wire EmitClampStages(double sign, IEnumerable<CascadeStage> clampStages)
        {
            var stageOutput = difference;
            var index = 0;
            foreach (var (scale, limit) in clampStages)
            {
                Term scaledTerm;
                Wire aboveLimit;
                if (index == 0)
                {
                    // The first stage reads the signed difference as written, and keeps only its positive side.
                    Term[] gateTerms = [(difference, sign * differenceSign * scale)];
                    var scaledPositive = circuit.Append(OperationKind.Reglu, [], 1, gateTerms, 0, preserveRounding: true);
                    aboveLimit = circuit.Append(OperationKind.Reglu, [], 1, gateTerms, -limit, preserveRounding: true);
                    scaledTerm = (scaledPositive, 1);
                    emitted.Add(scaledPositive);
                }
                else
                {
                    aboveLimit = circuit.Reglu([], 1, [(stageOutput, scale)], -limit);
                    scaledTerm = (stageOutput, scale);
                }
                // Its own operation: copied into the next stage, the regrouped partial sums would round.
                stageOutput = circuit.RoundAffine([scaledTerm, (aboveLimit, -1)]);
                circuit.AssumeRange(stageOutput, 0, limit);
                emitted.Add(aboveLimit);
                emitted.Add(stageOutput);
                index++;
            }
            return stageOutput;
        }
        Wire bit;
        if (endWithBoolean)
        {
            // The final rounding stage: the clamp stages lift each positive difference to at least
            // 2^-45, and 2^22 × 2^-45 moves a score next to 0.5 across 0.5.
            var scoreScale = Math.ScaleB(1.0, 22);
            if (test == SignTest.Greater)
            {
                var stageOutput = EmitClampStages(1, stages);
                // The FP32 value just below 0.5, so d = 0 rounds to 0.
                var justBelowHalf = 0.5 - Math.ScaleB(1.0, -25);
                var scoreAboveZero = circuit.Reglu([], 1, [(stageOutput, scoreScale)], justBelowHalf);
                var scoreAboveOne = circuit.Reglu([], 1, [(stageOutput, scoreScale)], justBelowHalf - 1);
                var score = circuit.Affine([(scoreAboveZero, 1), (scoreAboveOne, -1)]);
                circuit.AssumeRange(score, 0, 1);
                emitted.Add(scoreAboveZero);
                emitted.Add(scoreAboveOne);
                bit = circuit.Boolean(score);
            }
            else
            {
                // a >= b scores 0.5 - 2^22 × (the stages that lift b - a), and equality also
                // subtracts the stages that lift a - b. The score stays within 0..0.5, so it needs
                // no upper clamp.
                var gateTerms = new List<Term> { (EmitClampStages(-1, stages), -scoreScale) };
                if (test == SignTest.Equal) gateTerms.Add((EmitClampStages(1, stages), -scoreScale));
                var score = circuit.Reglu([], 1, gateTerms, 0.5);
                emitted.Add(score);
                bit = circuit.Boolean(score);
            }
            emitted.Add(bit);
        }
        else
        {
            var finalScale = stages[^1].Scale;
            var clampStages = stages.Take(stages.Count - 1).ToArray();
            if (clampStages.Length == 0)
            {
                // One stage lifts the signed difference itself, so D = scale × d is 0 or at least 1 away from 0.
                bit = EmitIntegerBit(circuit, test, emitted, (sign, offset) => circuit.Append(
                    OperationKind.Reglu,
                    [],
                    1,
                    [(difference, sign * differenceSign * finalScale)],
                    offset,
                    preserveRounding: true));
            }
            else if (test == SignTest.Greater)
            {
                // scale × stage is 0 when d <= 0, and at least 1 when d > 0.
                var notGreater = circuit.Reglu([], 1, [(EmitClampStages(1, clampStages), -finalScale)], 1);
                bit = circuit.Reglu([], 1, [(notGreater, -1)], 1);
                emitted.Add(notGreater);
                emitted.Add(bit);
            }
            else
            {
                var gateTerms = new List<Term> { (EmitClampStages(-1, clampStages), -finalScale) };
                if (test == SignTest.Equal) gateTerms.Add((EmitClampStages(1, clampStages), -finalScale));
                bit = circuit.Reglu([], 1, gateTerms, 1);
                emitted.Add(bit);
            }
        }
        foreach (var wire in emitted) circuit.RecordRoundingPoint(wire, RoundingPointKind.SignCascade);
        return bit;
    }

    /// <summary>
    /// Math.min(a, b) as exactly one of the operands, for example 0.3 for min(1000.37, 0.3), where
    /// Minimum rebuilds the result through a rounded difference and returns 0.29998779. Ties return
    /// a, so the result differs from Math.min at most in the sign of zero. Needs finite declared
    /// ranges, as Compare does.
    /// </summary>
    public static Wire MinimumExact(this Circuit circuit, Wire a, Wire b)
    {
        var aIsSmaller = circuit.Compare(a, Relation.LessOrEqual, b);
        var aRange = CreateCompareOperand(circuit, a).Range;
        var bRange = CreateCompareOperand(circuit, b).Range;
        return EmitSelectOperand(
            circuit,
            aIsSmaller,
            b,
            a,
            Math.Min(aRange.Minimum, bRange.Minimum),
            Math.Min(aRange.Maximum, bRange.Maximum));
    }

    /// <summary>Math.max(a, b) as exactly one of the operands. Ties return a.</summary>
    public static Wire MaximumExact(this Circuit circuit, Wire a, Wire b)
    {
        var aIsLarger = circuit.Compare(a, Relation.GreaterOrEqual, b);
        var aRange = CreateCompareOperand(circuit, a).Range;
        var bRange = CreateCompareOperand(circuit, b).Range;
        return EmitSelectOperand(
            circuit,
            aIsLarger,
            b,
            a,
            Math.Max(aRange.Minimum, bRange.Minimum),
            Math.Max(aRange.Maximum, bRange.Maximum));
    }

    /// <summary>Math.min(Math.max(value, minimum), maximum), for example maximum when minimum > maximum.</summary>
    public static Wire ClampExact(this Circuit circuit, Wire value, Wire minimum, Wire maximum) =>
        circuit.MinimumExact(circuit.MaximumExact(value, minimum), maximum);

    // on × relu(bit) + off × relu(1 - bit) for an exact bit. The product that is not selected is
    // f(off × 0) = ±0, so the sum returns the selected operand exactly, up to the sign of zero. The
    // sum is an ordinary affine, so an affine reader composes it with no change to a value.
    private static Wire EmitSelectOperand(Circuit circuit, Wire bit, Wire off, Wire on, double minimum, double maximum)
    {
        var selectedOn = circuit.Reglu([(on, 1)], 0, [(bit, 1)], 0);
        var selectedOff = circuit.Reglu([(off, 1)], 0, [(bit, -1)], 1);
        var result = circuit.Affine([(selectedOn, 1), (selectedOff, 1)]);
        circuit.AssumeRange(result, minimum, maximum);
        return result;
    }
}
