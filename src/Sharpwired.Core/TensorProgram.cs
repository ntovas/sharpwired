// Handwired source: src/runtime/numeric-ui-core.ts (UnsignedIndexArray, compactUnsignedIndices,
// TensorGatherCompute, TensorGatherRead, TensorIndexRead, TensorLayer, TensorProgram,
// PackedCandidateTable).

using System.Collections;
using System.Collections.Immutable;

namespace Sharpwired.Core;

/// <summary>
/// A read-only array of nonnegative indices, stored with the smallest element width that holds the
/// largest value: 8, 16 or 32 bits. Handwired uses a Uint8Array, a Uint16Array or a Uint32Array.
/// </summary>
/// <remarks>
/// Two instances are equal only when they are the same instance, as two typed arrays are. The
/// compiler stores one instance in each read of a candidate table, so the validator checks a
/// shared positions array one time only.
/// </remarks>
public sealed class UnsignedIndices : IReadOnlyList<int>
{
    private readonly byte[]? bytes;
    private readonly ushort[]? shorts;
    private readonly uint[]? words;

    private UnsignedIndices(byte[]? bytes, ushort[]? shorts, uint[]? words, int count)
    {
        this.bytes = bytes;
        this.shorts = shorts;
        this.words = words;
        Count = count;
    }

    /// <summary>An empty array with 8-bit elements, as Handwired new Uint8Array(0).</summary>
    public static UnsignedIndices Empty { get; } = new([], null, null, 0);

    /// <summary>The number of bits of each element: 8, 16 or 32.</summary>
    public int ElementBits => bytes is not null ? 8 : shorts is not null ? 16 : 32;

    /// <summary>The number of bytes that the elements use.</summary>
    public int ByteLength => Count * ElementBits / 8;

    public int Count { get; }

    // The elements as a span of their stored type, for the loops of the evaluator. ElementBits tells
    // which span holds the elements. The other two spans are empty.
    internal ReadOnlySpan<byte> Bytes => bytes;

    internal ReadOnlySpan<ushort> Shorts => shorts;

    internal ReadOnlySpan<uint> Words => words;

    public int this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index), index, "index is outside the array");
            if (bytes is not null) return bytes[index];
            if (shorts is not null) return shorts[index];
            return (int)words![index];
        }
    }

    /// <summary>
    /// Handwired compactUnsignedIndices: copy the values into the smallest element width that holds
    /// the largest value. A negative value throws.
    /// </summary>
    public static UnsignedIndices From(ReadOnlySpan<int> values)
    {
        var maximum = 0;
        foreach (var value in values)
        {
            if (value < 0) throw new ArgumentException($"index must be a nonnegative integer, got {value}");
            maximum = Math.Max(maximum, value);
        }
        if (maximum <= byte.MaxValue)
        {
            var copy = new byte[values.Length];
            for (var index = 0; index < values.Length; index++) copy[index] = (byte)values[index];
            return new UnsignedIndices(copy, null, null, values.Length);
        }
        if (maximum <= ushort.MaxValue)
        {
            var copy = new ushort[values.Length];
            for (var index = 0; index < values.Length; index++) copy[index] = (ushort)values[index];
            return new UnsignedIndices(null, copy, null, values.Length);
        }
        var wideCopy = new uint[values.Length];
        for (var index = 0; index < values.Length; index++) wideCopy[index] = (uint)values[index];
        return new UnsignedIndices(null, null, wideCopy, values.Length);
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (var index = 0; index < Count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// One group index that a layer resolves. The executor reads the packed one-hot bits and takes
/// their dot product with 0..N-1. It stores the index and the mass for the gather reads of the
/// layer. The executor computes it one time for each group and each evaluation.
/// </summary>
public sealed record TensorGatherCompute(int Slot, UnsignedIndices BitPositions);

/// <summary>
/// One lowered masked sum: the executor finds the packed position of the selected candidate and
/// adds that value to the bias of the row. Masked is true for a group that allows all zeros. Then
/// the executor first multiplies the value by the group mass, so an inactive group adds 0.
/// </summary>
public sealed record TensorGatherRead(int Row, int Slot, bool Masked, UnsignedIndices CandidatePositions);

/// <summary>
/// One index read inside a layer. Its index form is the projection row IndexRow, after the gate
/// rows. The executor floors the value of that row and copies the packed value of the selected
/// candidate into the output row Row. When CandidatePositions is empty, candidate k is at the
/// packed position CandidateBase + k × CandidateStride, for example a run of source cells.
/// </summary>
public sealed record TensorIndexRead(
    int Row,
    int IndexRow,
    bool BelowZero,
    bool AboveZero,
    int CandidateCount,
    int CandidateBase,
    int CandidateStride,
    UnsignedIndices CandidatePositions);

/// <summary>
/// One layer of a compiled program. The layer reads the packed values InputIndices into its input
/// columns. It computes its projection rows with one sparse matrix. First come the output rows,
/// one for each output. Then come the gate rows, one for each ReGLU (GateRows gives its output
/// row). Then come the index rows, one for each index read. Then the layer applies the gather
/// reads, the index reads, the gates and the boolean atoms, in that order.
/// </summary>
public sealed record TensorLayer
{
    public required UnsignedIndices InputIndices { get; init; }

    public required int InputSize { get; init; }

    public required int OutputSize { get; init; }

    /// <summary>The bias of each projection row, rounded to FP32 one time.</summary>
    public required ImmutableArray<float> Bias { get; init; }

    public required UnsignedIndices GateRows { get; init; }

    public required UnsignedIndices BooleanRows { get; init; }

    /// <summary>
    /// The sparse matrix in CSR (compressed sparse row) form. Row r stores the columns and the
    /// weights RowOffsets[r] to RowOffsets[r + 1] - 1, in the term order of its operation.
    /// </summary>
    public required UnsignedIndices RowOffsets { get; init; }

    public required UnsignedIndices Columns { get; init; }

    /// <summary>The stored weights, each rounded to FP32 one time.</summary>
    public required ImmutableArray<float> Weights { get; init; }

    public required ImmutableArray<TensorGatherCompute> GatherComputes { get; init; }

    public required ImmutableArray<TensorGatherRead> GatherReads { get; init; }

    public required ImmutableArray<TensorIndexRead> IndexReads { get; init; }
}

/// <summary>
/// A compiled program: the layers of the live operations. The packed values are the source cells,
/// then the outputs of each layer in layer order. WireOrder gives the wire of each packed value.
/// </summary>
/// <remarks>
/// A program is immutable. Threads can share one program. Make one evaluator for each thread from
/// the shared program with <see cref="Circuit.CreateTensorProgramEvaluator"/>.
/// </remarks>
public sealed record TensorProgram
{
    public required ImmutableArray<TensorLayer> Layers { get; init; }

    /// <summary>The wires that the program must compute, in the order of the caller.</summary>
    public required UnsignedIndices Roots { get; init; }

    public required int SourceSize { get; init; }

    /// <summary>The number of packed values: the source cells and the live operations.</summary>
    public required int ValueCount { get; init; }

    public required UnsignedIndices WireOrder { get; init; }

    /// <summary>The number of wires of the circuit, also the removed ones.</summary>
    public required int Width { get; init; }

    public required int GatherSlotCount { get; init; }
}

// Handwired PackedCandidateTable: the stored form of one candidate table, shared by each read of
// the table in a program.
internal sealed record PackedCandidateTable(int CandidateCount, int CandidateBase, int CandidateStride, UnsignedIndices CandidatePositions);
