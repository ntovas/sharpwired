// Handwired source: src/runtime/numeric-ui-core.ts (Wire, asWire, Terms, NumericRange,
// OutOfRange, RoundingPointKind).

using System.Globalization;

namespace Sharpwired.Core;

/// <summary>
/// A position in the array that an evaluation fills: the source cells first, then one
/// position for each operation. A plain int does not convert to a wire, so a call that
/// gives a number where a wire is necessary does not compile.
/// </summary>
public readonly record struct Wire : IComparable<Wire>
{
    internal Wire(int position) => Position = position;

    /// <summary>The position of the wire in the value array.</summary>
    public int Position { get; }

    /// <summary>
    /// Handwired asWire: a position counted by hand as a wire, for example the position
    /// of a source cell before the circuit exists. The position must not be negative.
    /// </summary>
    public static Wire FromPosition(int position)
    {
        if (position < 0) throw new ArgumentException($"a wire must be a nonnegative integer, got {position}");
        return new Wire(position);
    }

    public int CompareTo(Wire other) => Position.CompareTo(other.Position);

    public override string ToString() => Position.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// One term of an affine form: a wire times a coefficient. The coefficient is a float64
/// value, as in Handwired. The evaluator rounds it to FP32 when it reads it.
/// </summary>
public readonly record struct Term(Wire Wire, double Coefficient)
{
    public static implicit operator Term((Wire Wire, double Coefficient) term) => new(term.Wire, term.Coefficient);
}

/// <summary>The range of the values of a wire. The bounds are float64 values, as in Handwired.</summary>
public readonly record struct NumericRange(double Minimum, double Maximum)
{
    /// <summary>The range of a wire with no declaration: -Infinity to Infinity.</summary>
    public static NumericRange Unknown { get; } = new(double.NegativeInfinity, double.PositiveInfinity);
}

/// <summary>
/// What an index read returns when floor(index) is outside 0..count-1: Clamp reads the
/// nearest end of the table, Zero reads +0.
/// </summary>
public enum OutOfRange
{
    Clamp,
    Zero,
}

/// <summary>
/// Why an operation is a rounding point, that is, an operation whose evaluation, term
/// order or operand wires carry meaning. A backend that reorders, fuses or copies sums,
/// or evaluates sums in float64, must keep these points.
/// </summary>
public enum RoundingPointKind
{
    /// <summary>
    /// A boolean atom, recorded automatically. Its (score + K) - K rounds to a bit only
    /// when the backend evaluates it in FP32 as written.
    /// </summary>
    Boolean,

    /// <summary>
    /// An operation of the integer bits of Compare on grids. Every product and partial sum
    /// is a whole number of grid steps within 2^24. A backend can add the terms in any
    /// order, but each operation must stay a separate operation.
    /// </summary>
    IntegerBit,

    /// <summary>
    /// An operation of the sign cascade of Compare. A backend can evaluate the sum of the
    /// operation in any order or in float64, but each operation must stay separate and read
    /// the same wires. The smallest differences also need subnormal values.
    /// </summary>
    SignCascade,

    /// <summary>
    /// Recorded by the caller with MarkRoundingPoint, for example a step of a round chain.
    /// A backend keeps the stored FP32 evaluation, the term order and the operand wires.
    /// </summary>
    Marked,
}

/// <summary>A rounding point: the wire of the operation and the kind of the record.</summary>
public readonly record struct RoundingPoint(Wire Wire, RoundingPointKind Kind);
