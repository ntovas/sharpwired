// Handwired source: $REF/docs/expressions.md, table "Familiar expressions", rows 13 to 15 ("Math.round(x),
// Math.floor(x)", "Math.floor(x / step) and i % count with few possible results", "Math.floor(i / 64)
// and i % 64 for a runtime integer with many possible results") and line 48 (Math.floor(n / m) for a
// runtime m with few possible values, through the one-hot of m), and
// $REF/lab/ui-exploration/guide/patterns/threshold-counts.md. The threshold counts of plan task 6b.
//
// A floor, an int division or an int remainder of a value is a step function of one input wire x:
// MathF.Floor(e(x)), (int)MathF.Floor(e(x) / c), (int)(e(x) / c), e(x) / d and e(x) % d with
// constants c and d, and a / b and a % b with a constant a and a runtime int b. Here e is the C#
// expression of the operand: an open affine form whose only wire is x, or x itself. The translator
// evaluates the function in C# arithmetic (each IL step rounds to FP32, an int division rounds toward
// zero) at each grid point of the declared range of x, and finds the points where the value changes.
// With the change points t_1 < ... < t_(K-1) of K results:
//
//   F(x) = F(lowest) + Σ (F(t_j) - F(t_(j-1))) × [x >= t_j]
//
// Each [x >= t_j] is one Compare of x with a constant (an integer bit on a grid), and the sum is one
// affine operation of exact bits with integer weights. So K results cost K - 1 comparisons and one sum
// in 3 layers (threshold-counts.md, section "Cost"). The sum is a sum over the one-hot of the count
// in another form: [x >= t_j] - [x >= t_(j+1)] is the one-hot bit of result j. An int remainder with
// a constant divisor d is x - d × (x / d), one more sum (expressions.md, row 15).
//
// - More than 64 results of an int value (a grid of 1 or more): an index read over a table of the
//   results, one entry for each grid point, at most 65536 entries (expressions.md, row 15).
// - A value with no grid, with a range that is not finite, with more than 65536 grid points and a
//   function that the translator cannot prove monotone, or with more than 64 results and no int grid,
//   keeps the normal lowering, which refuses the division, the remainder or the floor.
// - A divisor whose range holds 0 is refused, because C# throws.
// - A float division by a constant that is not a power of two is refused, except when a floor or a
//   conversion to int is its only reader. Then the division has no wire of its own: the floor or the
//   conversion evaluates the C# quotient at each grid point.
// - With TranslationOptions.RecognizePatterns false, each of these constructs keeps its refusal.

using System.Reflection;
using System.Reflection.Metadata;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>
/// A float division by a constant that is not a power of two, whose only reader is MathF.Floor or
/// conv.i4. It has no wire: the reader gives a threshold count of the C# quotient.
/// </summary>
internal sealed class QuotientValue(ScalarValue dividend, double divisor, int ilOffset) : ScalarValue(ScalarKind.Float)
{
    public ScalarValue Dividend { get; } = dividend;

    public double Divisor { get; } = divisor;

    public int IlOffset { get; } = ilOffset;
}

internal sealed partial class OperationLowering
{
    /// <summary>The most results of a threshold count (plan, task 6b: "K at most a limit, for example 64").</summary>
    private const int ThresholdResults = 64;

    /// <summary>The most entries of an index read over a table of results, and the most grid points of a scan.</summary>
    private const int TableEntries = 65_536;

    /// <summary>The reason of the refusal of an int division or remainder with no pattern.</summary>
    private const string IntegerDivisionReason = "the program has no exact integer division or remainder";

    // The refusal of a float division by a constant that is not a power of two (plan, "Behavior with
    // no direct form").
    private TranslationException FloatDivisionRefusal(double divisor, int offset) =>
        Refuse(offset, $"float division by {Fp32.Format(divisor)}",
            $"C# rounds x / {Fp32.Format(divisor)} one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / {Fp32.Format(divisor)})");

    // A float division by a constant that is not a power of two, whose only reader is a floor or a
    // conversion to int, gives a QuotientValue. Otherwise null.
    private QuotientValue? DeferredQuotient(ScalarValue dividend, double divisor, SsaValue operation)
    {
        if (!Emitter.RecognizePatterns || divisor == 0 || !double.IsFinite(divisor) || uses.GetValueOrDefault(operation) != 1)
        {
            return null;
        }

        var readers = ssa.Blocks.SelectMany(block => block.Operations).Where(user => user.Operands.Contains(operation)).ToArray();
        var floorOrTruncation = readers is [var reader]
            && (reader.Op == ILOpCode.Conv_i4 || (reader is { Op: ILOpCode.Call, Member: MethodInfo { Name: nameof(MathF.Floor) } floor } && floor.DeclaringType == typeof(MathF)));
        return floorOrTruncation ? new QuotientValue(dividend, divisor, operation.IlOffset) : null;
    }

    /// <summary>
    /// MathF.Floor of a float value: a threshold count of the value, or null when the value has no
    /// pattern (then the call keeps its refusal). The floor of a QuotientValue that has no pattern is
    /// refused as its division.
    /// </summary>
    public ScalarValue? Floor(ScalarValue value, int offset)
    {
        if (value is QuotientValue quotient)
        {
            return QuotientStep(quotient, number => MathF.Floor(number), ScalarKind.Float, offset)
                ?? throw FloatDivisionRefusal(quotient.Divisor, quotient.IlOffset);
        }

        return value is ConstantValue constant
            ? new ConstantValue(MathF.Floor((float)constant.Number), ScalarKind.Float)
            : Step(value, ScalarKind.Float, number => MathF.Floor((float)number), ScalarKind.Float, monotone: true, offset);
    }

    // rem: an int remainder with a pattern, otherwise the refusal of the IL instruction.
    private ScalarValue Remainder(SsaValue operation)
    {
        var offset = operation.IlOffset;
        return KindOf(operation) == ScalarKind.Int
            && IntegerQuotient(Scalar(operation.Operands[0], offset), Scalar(operation.Operands[1], offset), remainder: true, offset) is { } value
            ? value
            : throw Refuse(offset, "the IL instruction rem", IntegerDivisionReason);
    }

    // conv.i4 of a QuotientValue: (int)(dividend / divisor), the C# truncation toward zero.
    private ScalarValue Truncate(QuotientValue quotient, int offset) =>
        QuotientStep(quotient, number => (int)number, ScalarKind.Int, offset) ?? throw FloatDivisionRefusal(quotient.Divisor, quotient.IlOffset);

    // conv.i4 of a float wire with no grid of 1: a threshold count of (int)x, or null.
    private ScalarValue? TruncateWire(Wire wire, int offset) => Emitter.RecognizePatterns
        ? Step(new WireValue(wire, ScalarKind.Float), ScalarKind.Float, number => (int)(float)number, ScalarKind.Int, monotone: true, offset)
        : null;

    // A step function of the C# quotient f(dividend / divisor): the reader rounds the quotient.
    private ScalarValue? QuotientStep(QuotientValue quotient, Func<float, double> round, ScalarKind kind, int offset)
    {
        var divisor = (float)quotient.Divisor;
        return quotient.Dividend is ConstantValue constant
            ? new ConstantValue(round((float)constant.Number / divisor), kind)
            : Step(quotient.Dividend, ScalarKind.Float, number => round((float)number / divisor), kind, monotone: true, offset);
    }

    /// <summary>
    /// An int division (remainder false) or remainder of two int values: a constant for two constants,
    /// a threshold count for one runtime operand, or null for two runtime operands or with the option
    /// off. A divisor that can be 0 is refused.
    /// </summary>
    private ScalarValue? IntegerQuotient(ScalarValue left, ScalarValue right, bool remainder, int offset)
    {
        if (!Emitter.RecognizePatterns)
        {
            return null;
        }

        var construct = remainder ? "an int remainder by a value that can be 0" : "an int division by a value that can be 0";
        const string zeroReason = "C# throws for a division by 0; declare a range of the divisor that does not hold 0";
        long Apply(long a, long b) => remainder ? a % b : a / b;
        switch (left, right)
        {
            case (ConstantValue a, ConstantValue b):
                return b.Number == 0 ? throw Refuse(offset, construct, zeroReason) : new ConstantValue(Apply((long)a.Number, (long)b.Number), ScalarKind.Int);
            case (_, ConstantValue { Number: 0 }):
                throw Refuse(offset, construct, zeroReason);
            case (_, ConstantValue b) when !remainder:
                return Step(left, ScalarKind.Int, number => Apply((long)number, (long)b.Number), ScalarKind.Int, monotone: true, offset);
            case (_, ConstantValue b):
                {
                    // x % d = x - d × (x / d), with the quotient as a threshold count of the same input.
                    if (Step(left, ScalarKind.Int, number => (long)number / (long)b.Number, ScalarKind.Int, monotone: true, offset) is not { } count)
                    {
                        return null;
                    }

                    var (terms, bias) = FormOf(left, offset);
                    var (countTerms, countBias) = FormOf(count, offset);
                    Term[] all = [.. terms, .. countTerms.Select(term => term with { Coefficient = -b.Number * term.Coefficient })];
                    var total = bias - b.Number * countBias;
                    return Emitter.IsExact(all, total)
                        ? new WireValue(Circuit.Affine(all, total), ScalarKind.Int)
                        : throw Refuse(offset, "an int value with a range past 2^24", "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24");
                }

            case (ConstantValue a, _):
                {
                    var divisor = Emit(right, offset);
                    var range = Circuit.Ranges[divisor.Position];
                    if (range.Minimum <= 0 && range.Maximum >= 0)
                    {
                        throw Refuse(offset, construct, zeroReason);
                    }

                    return Step(new WireValue(divisor, ScalarKind.Int), ScalarKind.Int, number => Apply((long)a.Number, (long)number), ScalarKind.Int, monotone: false, offset);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// The step function F(e(x)) of a value e with one input wire x, as a threshold count or an index
    /// read, or null when the value has no pattern. kind is the kind of the value e, and function
    /// takes the C# value of e. monotone: the function of e is monotone, so a range with more grid
    /// points than a scan reads can be searched by bisection, when x occurs one time in e.
    /// </summary>
    private ScalarValue? Step(ScalarValue value, ScalarKind kind, Func<double, double> function, ScalarKind resultKind, bool monotone, int offset)
    {
        if (value is QuotientValue)
        {
            return null;
        }

        var (input, occurrences) = SingleInput(value);
        Func<double, double> evaluate;
        if (input is { } wire)
        {
            evaluate = x => function(EvaluateChain(value, wire, x));
        }
        else
        {
            wire = Emit(value, offset);
            occurrences = 1;
            evaluate = function;
        }

        var range = Circuit.Ranges[wire.Position];
        if (Circuit.Grids[wire.Position] is not { } grid || !double.IsFinite(range.Minimum) || !double.IsFinite(range.Maximum))
        {
            return null;
        }

        var lowest = Math.Ceiling(range.Minimum / grid);
        var highest = Math.Floor(range.Maximum / grid);
        var points = highest - lowest + 1;
        if (!(points >= 1))
        {
            return null;
        }

        double Point(double index) => index * grid;
        var first = evaluate(Point(lowest));
        var changes = new List<(double Point, double Step)>();
        if (points <= TableEntries)
        {
            var previous = first;
            for (var index = lowest + 1; index <= highest; index++)
            {
                var current = evaluate(Point(index));
                if (current != previous)
                {
                    changes.Add((Point(index), current - previous));
                    previous = current;
                }
            }
        }
        else if (monotone && occurrences == 1)
        {
            // Once the value changes, it does not come back, so bisection finds each next change.
            var (index, previous) = (lowest, first);
            while (changes.Count < ThresholdResults)
            {
                if (evaluate(Point(highest)) == previous)
                {
                    break;
                }

                var (low, high) = (index + 1, highest);
                while (low < high)
                {
                    var middle = Math.Floor((low + high) / 2);
                    (low, high) = evaluate(Point(middle)) != previous ? (low, middle) : (middle + 1, high);
                }

                var current = evaluate(Point(low));
                changes.Add((Point(low), current - previous));
                (index, previous) = (low, current);
            }

            if (changes.Count >= ThresholdResults)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        var inputValue = new WireValue(wire, kind == ScalarKind.Int ? ScalarKind.Int : ScalarKind.Float);
        if (changes.Count < ThresholdResults)
        {
            return ThresholdCount(inputValue, first, changes, resultKind, offset);
        }

        if (grid < 1 || points > TableEntries)
        {
            return null;
        }

        // An index read over a table of the results, one entry for each grid point.
        var candidates = new Wire[(int)points];
        for (var index = 0; index < candidates.Length; index++)
        {
            candidates[index] = Circuit.Constant(evaluate(Point(lowest + index)));
        }

        var read = Macro(offset, "an index read over a table of results",
            () => Circuit.IndexReadAffine([new Term(wire, 1 / grid)], -lowest, candidates, OutOfRange.Clamp, OutOfRange.Clamp));
        Circuit.AssumeGrid(read, 1);
        return new WireValue(read, resultKind);
    }

    // F(lowest) + Σ step_j × [x >= t_j]. A comparison that the range refinement of a loop copy decides
    // is a constant.
    private ScalarValue ThresholdCount(WireValue input, double first, List<(double Point, double Step)> changes, ScalarKind kind, int offset)
    {
        // Emission rule 5: each step and each result must be an exact integer form within 2^24.
        const double exactLimit = 16_777_216;
        var results = changes.Select(change => change.Step).Aggregate(new List<double> { first }, (list, step) => { list.Add(list[^1] + step); return list; });
        if (changes.Any(change => Math.Abs(change.Step) > exactLimit) || results.Any(result => Math.Abs(result) > exactLimit))
        {
            throw Refuse(offset, "a threshold count with a value past 2^24",
                "each step and each result of a threshold count must lie within -2^24..2^24; declare a smaller range");
        }

        var terms = new List<Term>();
        var bias = first;
        foreach (var (point, step) in changes)
        {
            switch (CompareValues(input, new ConstantValue(point, input.Kind), Relation.GreaterOrEqual, offset))
            {
                case ConstantValue { Number: var bit }:
                    bias += bit * step;
                    break;
                case var bit:
                    terms.Add(new Term(Emit(bit, offset), step));
                    break;
            }
        }

        if (terms.Count == 0)
        {
            return new ConstantValue(bias, kind);
        }

        var count = Circuit.Affine(terms, bias);
        Emitter.NoteValue(count, terms.Select(term => term.Wire));
        return new WireValue(count, kind);
    }

    // The one wire of a value and the count of its occurrences: a wire, or an open affine form whose
    // leaves are one wire and constants. Otherwise (null, 0).
    private static (Wire? Wire, int Occurrences) SingleInput(ScalarValue value)
    {
        Wire? found = null;
        var occurrences = 0;
        bool Visit(ScalarValue leaf)
        {
            switch (leaf)
            {
                case ConstantValue:
                    return true;
                case { Wire: { } wire }:
                    if (found is { } known && known != wire)
                    {
                        return false;
                    }

                    found = wire;
                    occurrences++;
                    return true;
                case AffineValue form:
                    return Visit(form.Left) && (form.Right is null || Visit(form.Right));
                default:
                    return false;
            }
        }

        return Visit(value) && found is not null ? (found, occurrences) : (null, 0);
    }

    // The C# value of an open affine form at x: one FP32 (or exact int) step for each IL instruction.
    private static double EvaluateChain(ScalarValue value, Wire input, double x)
    {
        switch (value)
        {
            case ConstantValue constant:
                return constant.Number;
            case { Wire: { } wire } when wire == input:
                return x;
            case AffineValue form:
                {
                    var left = EvaluateChain(form.Left, input, x);
                    var right = form.Right is null ? 0 : EvaluateChain(form.Right, input, x);
                    if (form.Kind == ScalarKind.Int)
                    {
                        return form.Op switch
                        {
                            AffineOp.Add => (long)left + (long)right,
                            AffineOp.Subtract => (long)left - (long)right,
                            AffineOp.Negate => -(long)left,
                            AffineOp.Scale => (long)left * (long)form.Factor,
                            _ => left,
                        };
                    }

                    float a = (float)left, b = (float)right;
                    return form.Op switch
                    {
                        AffineOp.Add => a + b,
                        AffineOp.Subtract => a - b,
                        AffineOp.Negate => -a,
                        AffineOp.Scale => a * (float)form.Factor,
                        _ => a,
                    };
                }

            default:
                throw new InvalidOperationException($"the value {value.GetType().Name} is not a leaf of a one-input form");
        }
    }
}
