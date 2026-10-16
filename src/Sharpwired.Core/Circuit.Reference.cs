// Handwired source: src/runtime/numeric-ui-core.ts (Circuit.evaluateReference,
// Circuit.validateSourceRanges, evaluateOperationStream, evaluateTerms).

using System.Collections.Immutable;

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    /// <summary>
    /// Evaluate one operation at a time, in wire order, in FP32. The result holds the value of
    /// each wire: the source cells, then one value for each operation. This defines what the
    /// program computes.
    /// </summary>
    public float[] EvaluateReference(ReadOnlySpan<float> source)
    {
        ValidateSourceRanges(source);
        return EvaluateOperationStream(source);
    }

    /// <summary>Check the length of the source table and each source cell against its declared range.</summary>
    public void ValidateSourceRanges(ReadOnlySpan<float> source)
    {
        if (source.Length != SourceSize) throw new ArgumentException("source table has the wrong length");
        for (var wire = 0; wire < source.Length; wire++)
        {
            var range = RangeList[wire];
            if (!(source[wire] >= range.Minimum && source[wire] <= range.Maximum))
            {
                throw new ArgumentException($"source wire {wire} is outside its assumed range");
            }
        }
    }

    private float[] EvaluateOperationStream(ReadOnlySpan<float> source)
    {
        if (source.Length != SourceSize) throw new ArgumentException("source table has the wrong length");
        var values = new float[Width];
        source.CopyTo(values);
        for (var operationIndex = 0; operationIndex < OperationList.Count; operationIndex++)
        {
            var operation = OperationList[operationIndex];
            var value = EvaluateTerms(values, operation.LeftTerms, operation.LeftBias);
            switch (operation.Kind)
            {
                case OperationKind.Reglu:
                    // MathF.Max gives NaN for a NaN gate and +0 for a -0 gate, as Math.max does.
                    value = (float)(value * MathF.Max(0f, EvaluateTerms(values, operation.GateTerms, operation.GateBias)));
                    break;
                case OperationKind.Boolean:
                    value = Fp32.EvaluateBoolean(value);
                    break;
                case OperationKind.Index:
                    value = ReadIndexedValue(values, operation.IndexRead!, EvaluateTerms(values, operation.GateTerms, operation.GateBias));
                    break;
            }
            values[SourceSize + operationIndex] = value;
        }
        return values;
    }

    // value = f(bias), then value = f(value + f(f(coefficient) × values[wire])) for each term in stored order.
    internal static float EvaluateTerms(float[] values, ImmutableArray<Term> terms, double bias)
    {
        var value = (float)bias;
        foreach (var (wire, coefficient) in terms)
        {
            value = (float)(value + (float)((float)coefficient * values[wire.Position]));
        }
        return value;
    }
}
