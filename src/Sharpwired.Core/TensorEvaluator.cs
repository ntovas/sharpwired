// Handwired source: src/runtime/numeric-ui-core.ts (the function that
// Circuit.createTensorProgramEvaluator returns, with its packedValues property; TensorWorkspace,
// createTensorWorkspace, executeTensorProgram, sparseMatmulAddBias, applyGathers, applyIndexReads,
// evaluateTensorProgram, and the ECMAScript ToInt32 of an Int32Array store).

namespace Sharpwired.Core;

/// <summary>
/// A reusable evaluator of one compiled program. Each call checks the source table against the
/// circuit, runs the layers in one workspace, and returns the values of the roots.
/// </summary>
/// <remarks>
/// The evaluator does not allocate after it is made. The arrays that <see cref="PackedValues"/> and
/// <see cref="Evaluate"/> return are the arrays of the evaluator, and the next call overwrites them.
/// Copy the values to keep them. One evaluator must not run on two threads at the same time.
/// </remarks>
public sealed class TensorEvaluator
{
    private readonly Circuit circuit;
    private readonly TensorWorkspace workspace;
    // The packed position of each root, or -1 for a root that the program does not hold.
    private readonly int[] positions;
    private readonly float[] outputs;

    internal TensorEvaluator(Circuit circuit, TensorProgram program)
    {
        this.circuit = circuit;
        Program = program;
        workspace = new TensorWorkspace(program);
        positions = new int[program.Roots.Count];
        for (var index = 0; index < positions.Length; index++)
        {
            positions[index] = IndexOf(program.WireOrder, program.Roots[index]);
        }
        outputs = new float[positions.Length];
    }

    /// <summary>The program that the evaluator runs.</summary>
    public TensorProgram Program { get; }

    /// <summary>
    /// The array that the evaluator computes in. After a call, PackedValues[k] is the value of the
    /// wire Program.WireOrder[k], for the source cells and for each live operation. Each call
    /// overwrites this same array.
    /// </summary>
    public float[] PackedValues => workspace.PackedValues;

    /// <summary>
    /// Run the program on the source table and return the values of Program.Roots, in root order.
    /// A root that the program does not hold gives NaN.
    /// </summary>
    /// <returns>The same array on each call. The next call overwrites it.</returns>
    public float[] Evaluate(ReadOnlySpan<float> source)
    {
        circuit.ValidateSourceRanges(source);
        TensorExecution.ExecuteTensorProgram(source, Program, workspace);
        var packedValues = workspace.PackedValues;
        for (var index = 0; index < positions.Length; index++)
        {
            // Handwired reads packedValues[-1], which is undefined and stores as NaN.
            var position = positions[index];
            outputs[index] = position >= 0 ? packedValues[position] : float.NaN;
        }
        return outputs;
    }

    // Handwired wireOrder.indexOf(root): the first packed position of the wire, or -1.
    private static int IndexOf(UnsignedIndices wireOrder, int wire)
    {
        for (var index = 0; index < wireOrder.Count; index++)
        {
            if (wireOrder[index] == wire) return index;
        }
        return -1;
    }
}

// Handwired TensorWorkspace and createTensorWorkspace: the arrays that one evaluation computes in.
// The input and projection arrays have the size of the largest layer, and each layer uses their
// first entries. The gather arrays hold the index and the mass of each slot.
internal sealed class TensorWorkspace
{
    public TensorWorkspace(TensorProgram program)
    {
        var maxProjectionSize = 0;
        var maxInputSize = 0;
        foreach (var layer in program.Layers)
        {
            maxProjectionSize = Math.Max(maxProjectionSize, layer.Bias.Length);
            maxInputSize = Math.Max(maxInputSize, layer.InputSize);
        }
        PackedValues = new float[program.ValueCount];
        Input = new float[maxInputSize];
        Projection = new float[maxProjectionSize];
        GatherIndices = new int[program.GatherSlotCount];
        GatherMasses = new float[program.GatherSlotCount];
    }

    public float[] PackedValues { get; }

    public float[] Input { get; }

    public float[] Projection { get; }

    public int[] GatherIndices { get; }

    public float[] GatherMasses { get; }
}

internal static class TensorExecution
{
    // Handwired evaluateTensorProgram: run the program in a new workspace and return the value of
    // each wire of the circuit, with 0 for each wire that the program removed.
    public static float[] EvaluateTensorProgram(ReadOnlySpan<float> source, TensorProgram program)
    {
        var workspace = new TensorWorkspace(program);
        ExecuteTensorProgram(source, program, workspace);
        var values = new float[program.Width];
        for (var packedIndex = 0; packedIndex < program.WireOrder.Count; packedIndex++)
        {
            values[program.WireOrder[packedIndex]] = workspace.PackedValues[packedIndex];
        }
        return values;
    }

    // Handwired executeTensorProgram. The packed values are the source cells, then the outputs of
    // each layer. Each layer copies its inputs, computes its projection rows with the sparse matrix,
    // applies the gather reads, the index reads, the gates and the boolean atoms, and appends its
    // outputs.
    public static void ExecuteTensorProgram(ReadOnlySpan<float> source, TensorProgram program, TensorWorkspace workspace)
    {
        if (source.Length != program.SourceSize) throw new ArgumentException("source table has the wrong length");
        var packedValues = workspace.PackedValues;
        var input = workspace.Input;
        var projection = workspace.Projection;
        source.CopyTo(packedValues);
        var availableValues = source.Length;
        foreach (var layer in program.Layers)
        {
            var outputSize = layer.OutputSize;
            for (var column = 0; column < layer.InputSize; column++)
            {
                input[column] = packedValues[layer.InputIndices[column]];
            }
            SparseMatmulAddBias(projection, layer.Weights.AsSpan(), layer.Columns, layer.RowOffsets, input, layer.Bias.AsSpan());
            if (layer.GatherReads.Length > 0) ApplyGathers(layer, packedValues, projection, workspace.GatherIndices, workspace.GatherMasses);
            if (layer.IndexReads.Length > 0) ApplyIndexReads(layer, packedValues, projection);
            for (var gateRow = 0; gateRow < layer.GateRows.Count; gateRow++)
            {
                var outputRow = layer.GateRows[gateRow];
                // MathF.Max gives NaN for a NaN gate and +0 for a -0 gate, as Math.max does.
                projection[outputRow] = (float)(projection[outputRow] * MathF.Max(0f, projection[outputSize + gateRow]));
            }
            for (var booleanIndex = 0; booleanIndex < layer.BooleanRows.Count; booleanIndex++)
            {
                var booleanRow = layer.BooleanRows[booleanIndex];
                projection[booleanRow] = Fp32.EvaluateBoolean(projection[booleanRow]);
            }
            Array.Copy(projection, 0, packedValues, availableValues, outputSize);
            availableValues += outputSize;
        }
    }

    // Handwired applyGathers. Each compute resolves one slot: the dot product of the bits with
    // 0..N-1 and the sum of the bits, in double, as Handwired computes in float64. For exact bits of 0
    // and 1 these are small integers, with no rounding. The index store applies ToInt32, as an
    // Int32Array store does, and the mass store rounds to FP32, as a Float32Array store does. Each
    // read then adds the selected candidate (times the mass for a group that allows all zeros) to
    // the bias of its row, with one FP32 round.
    private static void ApplyGathers(TensorLayer layer, float[] packedValues, float[] projection, int[] gatherIndices, float[] gatherMasses)
    {
        foreach (var compute in layer.GatherComputes)
        {
            var index = 0.0;
            var mass = 0.0;
            for (var position = 0; position < compute.BitPositions.Count; position++)
            {
                double bit = packedValues[compute.BitPositions[position]];
                index += position * bit;
                mass += bit;
            }
            gatherIndices[compute.Slot] = JsToInt32(index);
            gatherMasses[compute.Slot] = (float)mass;
        }
        foreach (var read in layer.GatherReads)
        {
            // The clamp makes a broken one-hot contract read a wrong value, not a position outside
            // the table. For an exact one-hot the clamp changes nothing.
            var limit = read.CandidatePositions.Count - 1;
            var index = Math.Min(Math.Max(gatherIndices[read.Slot], 0), limit);
            var contribution = packedValues[read.CandidatePositions[index]];
            if (read.Masked) contribution = (float)(gatherMasses[read.Slot] * contribution);
            projection[read.Row] = (float)(projection[read.Row] + contribution);
        }
    }

    // ECMAScript ToInt32, the conversion of an Int32Array store: NaN and the infinities give 0, and
    // other values truncate toward zero and wrap modulo 2^32. The .NET (int) cast saturates.
    private static int JsToInt32(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var truncated = Math.Truncate(value);
        var wrapped = truncated % 4_294_967_296.0;
        if (wrapped < 0) wrapped += 4_294_967_296.0;
        return unchecked((int)(uint)wrapped);
    }

    // Handwired applyIndexReads: copy the packed value of the selected candidate into the output
    // row, or write +0 or NaN. The read does no arithmetic.
    private static void ApplyIndexReads(TensorLayer layer, float[] packedValues, float[] projection)
    {
        foreach (var read in layer.IndexReads)
        {
            var position = Circuit.ResolveIndexPosition(projection[read.IndexRow], read.CandidateCount, read.BelowZero, read.AboveZero);
            if (position >= 0)
            {
                projection[read.Row] = packedValues[
                    read.CandidatePositions.Count == 0
                        ? read.CandidateBase + position * read.CandidateStride
                        : read.CandidatePositions[position]];
            }
            else
            {
                projection[read.Row] = position == -1 ? 0f : float.NaN;
            }
        }
    }

    // Handwired sparseMatmulAddBias: row r is f(bias[r]), then f(value + f(weight × input)) for each
    // stored weight of the row, in stored term order. These are the three FP32 steps of
    // Circuit.EvaluateTerms, so each row gives the reference value bit for bit.
    private static void SparseMatmulAddBias(
        float[] output,
        ReadOnlySpan<float> weights,
        UnsignedIndices columns,
        UnsignedIndices rowOffsets,
        float[] input,
        ReadOnlySpan<float> bias)
    {
        for (var row = 0; row < bias.Length; row++)
        {
            var value = bias[row];
            for (var index = rowOffsets[row]; index < rowOffsets[row + 1]; index++)
            {
                var product = (float)(weights[index] * input[columns[index]]);
                value = (float)(value + product);
            }
            output[row] = value;
        }
    }
}
