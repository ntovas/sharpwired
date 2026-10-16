// Handwired source: src/runtime/numeric-ui-core.ts (emitRelu, emitMultiply, emitMaximum,
// emitMinimum, emitSelect, hasFiniteRange).
// Handwired emitMultiply and emitSelect only call circuit.multiply and circuit.select. In C#,
// an instance method hides an extension method of the same signature, so Circuit.Multiply and
// Circuit.Select are the Multiply and Select macros.

namespace Sharpwired.Core;

/// <summary>The macros: operations that expand into the four instructions.</summary>
public static partial class Macros
{
    /// <summary>relu(value), as one ReGLU: f(1 × max(0, value)).</summary>
    public static Wire Relu(this Circuit circuit, Wire value) => circuit.Reglu([], 1, [(value, 1)], 0);

    /// <summary>
    /// max(a, b) as b + relu(a - b). The subtraction and the sum keep their rounding order and
    /// operand wires. The result is exact for integer operands with magnitudes below 2^23. A
    /// fractional result can change in its last bits. MaximumExact returns exactly one operand.
    /// </summary>
    public static Wire Maximum(this Circuit circuit, Wire a, Wire b)
    {
        var difference = circuit.RoundAffine([(a, 1), (b, -1)]);
        var result = circuit.RoundAffine([(b, 1), (circuit.Relu(difference), 1)]);
        var aRange = circuit.Ranges[a.Position];
        var bRange = circuit.Ranges[b.Position];
        // In 0..1, the error of the subtraction is below the midpoint above 1, so the sum cannot
        // round above 1. This does not make the maximum exact.
        if (aRange.Minimum >= 0 && aRange.Maximum <= 1 && bRange.Minimum >= 0 && bRange.Maximum <= 1)
        {
            circuit.AssumeRange(result, 0, 1);
        }
        return result;
    }

    /// <summary>
    /// min(a, b) as a - relu(a - b), with the same limits as Maximum. MinimumExact returns exactly
    /// one operand.
    /// </summary>
    public static Wire Minimum(this Circuit circuit, Wire a, Wire b)
    {
        var difference = circuit.RoundAffine([(a, 1), (b, -1)]);
        var result = circuit.RoundAffine([(a, 1), (circuit.Relu(difference), -1)]);
        // For finite nonnegative operands, the rounded max(0, a - b) is within 0..a. The result can
        // be below min(a, b), so the range keeps only this weaker bound.
        if (circuit.IsNonNegative(a) && circuit.IsNonNegative(b) && HasFiniteRange(circuit, a) && HasFiniteRange(circuit, b))
        {
            circuit.AssumeRange(result, 0, Fp32.Round(circuit.Ranges[a.Position].Maximum));
        }
        return result;
    }

    private static bool HasFiniteRange(Circuit circuit, Wire wire)
    {
        var range = circuit.Ranges[wire.Position];
        return double.IsFinite(Fp32.Round(range.Minimum)) && double.IsFinite(Fp32.Round(range.Maximum));
    }
}
