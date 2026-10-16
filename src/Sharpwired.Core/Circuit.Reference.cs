// Handwired source: src/runtime/numeric-ui-core.ts (Circuit.evaluateReference,
// Circuit.validateSourceRanges, evaluateOperationStream, evaluateTerms,
// Circuit.backpropagateReference, accumulateTermGradients).

using System.Collections.Immutable;

namespace Sharpwired.Core;

/// <summary>What the reference backward pass does at a boolean atom (Handwired booleanGradient).</summary>
public enum BooleanGradient
{
    /// <summary>Throw: the atom is a step function, so it has no useful derivative.</summary>
    Reject,

    /// <summary>Send the gradient of the atom to its score unchanged.</summary>
    StraightThrough,
}

/// <summary>What the reference backward pass gives the index form of an index read (Handwired indexGradient).</summary>
public enum IndexGradient
{
    /// <summary>Throw: the read is a step function of the index.</summary>
    Reject,

    /// <summary>Give the index form no gradient. The selected candidate gets the output gradient.</summary>
    Zero,

    /// <summary>
    /// Give the index form read(floor(index)) - read(floor(index) - 1), the gradient that a ReLU
    /// one-hot masked sum gets at an integer index. The selected candidate gets the output gradient.
    /// </summary>
    BackwardDifference,
}

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

    internal float[] EvaluateOperationStream(ReadOnlySpan<float> source)
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

    /// <summary>
    /// The reference backward pass. It returns a new table that holds, for each wire, its output
    /// gradient plus the gradients that the operations after it send back. The pass goes through
    /// the operations in reverse wire order and differentiates FP32 arithmetic as real-valued
    /// arithmetic. A ReLU has the slope 0 at its corner. A multiply or select record gets the
    /// gradient of the whole product. An operation with an output gradient of 0 sends nothing.
    /// </summary>
    /// <param name="values">The forward values, from EvaluateReference.</param>
    /// <param name="outputGradients">The gradient of the loss with respect to each wire.</param>
    /// <param name="booleanGradient">Reject refuses a boolean atom. StraightThrough sends the gradient of the atom to its score unchanged.</param>
    /// <param name="indexGradient">
    /// Reject refuses an index read. Zero gives the index form no gradient. BackwardDifference gives
    /// the index form read(floor(index)) - read(floor(index) - 1). With each option except Reject,
    /// the selected candidate gets the output gradient.
    /// </param>
    public float[] BackpropagateReference(
        float[] values,
        float[] outputGradients,
        BooleanGradient booleanGradient = BooleanGradient.Reject,
        IndexGradient indexGradient = IndexGradient.Reject)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(outputGradients);
        if (values.Length != Width) throw new ArgumentException("forward values have the wrong length");
        if (outputGradients.Length != Width) throw new ArgumentException("output gradients have the wrong length");
        var gradients = (float[])outputGradients.Clone();
        // Differentiate FP32 arithmetic as real-valued; ReLU uses zero slope at its corner.
        for (var operationIndex = OperationList.Count - 1; operationIndex >= 0; operationIndex--)
        {
            var operation = OperationList[operationIndex];
            var outputGradient = gradients[SourceSize + operationIndex];
            if (outputGradient == 0) continue;
            switch (operation.Macro)
            {
                case MultiplyMacro multiply:
                    AccumulateTermGradients(gradients, [new Term(multiply.Left, values[multiply.Right.Position])], outputGradient);
                    AccumulateTermGradients(gradients, [new Term(multiply.Right, values[multiply.Left.Position])], outputGradient);
                    continue;
                case SelectMacro select:
                {
                    // The coefficients are float64 values, as in Handwired. AccumulateTermGradients rounds them.
                    double gate = values[select.Gate.Position];
                    AccumulateTermGradients(gradients, [new Term(select.Off, 1 - gate), new Term(select.On, gate)], outputGradient);
                    AccumulateTermGradients(
                        gradients,
                        [new Term(select.Gate, (double)values[select.On.Position] - values[select.Off.Position])],
                        outputGradient);
                    continue;
                }
            }
            switch (operation.Kind)
            {
                case OperationKind.Index:
                    BackpropagateIndexRead(values, gradients, operation, outputGradient, indexGradient);
                    break;
                case OperationKind.Boolean:
                    if (booleanGradient != BooleanGradient.StraightThrough)
                    {
                        throw new InvalidOperationException("cannot backpropagate through a boolean atom");
                    }
                    AccumulateTermGradients(gradients, operation.LeftTerms.AsSpan(), outputGradient);
                    break;
                case OperationKind.Affine:
                    AccumulateTermGradients(gradients, operation.LeftTerms.AsSpan(), outputGradient);
                    break;
                default:
                {
                    var left = EvaluateTerms(values, operation.LeftTerms, operation.LeftBias);
                    var gate = EvaluateTerms(values, operation.GateTerms, operation.GateBias);
                    var activeGate = MathF.Max(0f, gate);
                    AccumulateTermGradients(gradients, operation.LeftTerms.AsSpan(), (float)(outputGradient * activeGate));
                    if (gate > 0)
                    {
                        AccumulateTermGradients(gradients, operation.GateTerms.AsSpan(), (float)(outputGradient * left));
                    }
                    break;
                }
            }
        }
        return gradients;
    }

    // The selected candidate receives the output gradient, like the backward pass of a gather.
    // The read is a step function of the index: its derivative is 0 between integers and has no
    // value at them, so the caller chooses what the index form receives.
    private static void BackpropagateIndexRead(float[] values, float[] gradients, Operation operation, float outputGradient, IndexGradient indexGradient)
    {
        var read = operation.IndexRead!;
        if (indexGradient == IndexGradient.Reject)
        {
            throw new InvalidOperationException("cannot backpropagate into the index of an index read; pass indexGradient");
        }
        double indexValue = EvaluateTerms(values, operation.GateTerms, operation.GateBias);
        var position = ResolveIndexPosition(
            indexValue,
            read.Table.Candidates.Length,
            read.Below == OutOfRange.Zero,
            read.Above == OutOfRange.Zero);
        if (position >= 0)
        {
            AccumulateTermGradients(gradients, [new Term(read.Table.Candidates[position], 1)], outputGradient);
        }
        if (indexGradient == IndexGradient.BackwardDifference && double.IsFinite(indexValue))
        {
            // The backward pass gives this gradient to a ReLU one-hot masked sum at an integer index
            // i, with the bits relu(i - j + 1) - relu(i - j). The ReLU at its corner gets the slope 0.
            // So the index receives read(i) - read(i - 1), with the same rule outside the table. The
            // difference is a float64 value, as in Handwired.
            var floorIndex = Math.Floor(indexValue);
            var difference = (double)ReadIndexedValue(values, read, floorIndex) - ReadIndexedValue(values, read, floorIndex - 1);
            AccumulateTermGradients(gradients, operation.GateTerms.AsSpan(), (float)(outputGradient * difference));
        }
    }

    // gradients[wire] = f(gradients[wire] + f(outputGradient × f(coefficient))) for each term in stored order.
    private static void AccumulateTermGradients(float[] gradients, ReadOnlySpan<Term> terms, float outputGradient)
    {
        foreach (var (wire, coefficient) in terms)
        {
            gradients[wire.Position] = (float)(gradients[wire.Position] + (float)(outputGradient * (double)(float)coefficient));
        }
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
