// Handwired source: src/json-search/emit.ts (Row, flattenProgram, positionsOfWires). The rows of a
// compiled program, one for each operation, in an order that gives the values of TensorExecution. emit.ts
// refuses gathers and index reads of computed candidates. These rows hold both. They have a gather
// compute and the gather reads of a row. They have three candidate forms: evenly spaced source cells, a
// list of source cells, and a list of computed values. ConstantTables finds the index reads whose
// candidates are all constants (the probe of the phase 6 plan, question 1): a writer reads such a list from
// a storage buffer of constant tables, and the constants get no statement. Needed gives the rows that the
// roots need, with no row for a constant that only a constant table reads.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>One stored term: the FP32 weight and the packed position of the value.</summary>
internal readonly record struct ShaderTerm(float Weight, int Position);

/// <summary>How a shader reads the candidates of an index read or of a gather read.</summary>
internal enum CandidateForm
{
    // Source cells at Base + k × Stride.
    Strided,

    // Source cells in a list, which the shader holds as one module constant.
    SourceList,

    // A list with at least one computed value, which the shader holds as one function array.
    Computed,
}

/// <summary>
/// The candidates of one read. <see cref="Positions"/> holds the packed positions of a list, and is
/// empty for the strided form. <see cref="Table"/> is the stored array of the program, which reads
/// of one table share. A writer uses it to write one constant for each shared list.
/// </summary>
internal sealed record ShaderCandidates(CandidateForm Form, int Count, int Base, int Stride, ImmutableArray<int> Positions, UnsignedIndices? Table)
{
    public int PositionAt(int index) => Form == CandidateForm.Strided ? Base + (index * Stride) : Positions[index];
}

/// <summary>One step of a shader: a gather compute, or the operation of one packed position.</summary>
internal abstract record ShaderRow;

/// <summary>
/// The index and the mass of one gather slot: the dot product of the bits with 0..N-1, and the sum
/// of the bits.
/// </summary>
internal sealed record GatherComputeRow(int Slot, ImmutableArray<int> BitPositions) : ShaderRow;

/// <summary>One gather read of a row: add the candidate at the index of the slot.</summary>
internal sealed record GatherTerm(int Slot, bool Masked, ShaderCandidates Candidates);

/// <summary>An affine row: the bias, the terms, then the gather reads in stored order.</summary>
internal sealed record AffineRow(int Position, float Bias, ImmutableArray<ShaderTerm> Terms, ImmutableArray<GatherTerm> Gathers) : ShaderRow;

/// <summary>A boolean atom over the sum of the bias and the terms.</summary>
internal sealed record BooleanRow(int Position, float Bias, ImmutableArray<ShaderTerm> Terms) : ShaderRow;

/// <summary>A ReGLU row: f(left × max(0, gate)).</summary>
internal sealed record RegluRow(int Position, float Bias, ImmutableArray<ShaderTerm> Terms, float GateBias, ImmutableArray<ShaderTerm> GateTerms) : ShaderRow;

/// <summary>An index read: the index form, the out-of-range rules and the candidates.</summary>
internal sealed record IndexRow(
    int Position,
    float IndexBias,
    ImmutableArray<ShaderTerm> IndexTerms,
    bool BelowZero,
    bool AboveZero,
    ShaderCandidates Candidates) : ShaderRow;

internal static class ShaderRows
{
    /// <summary>
    /// Check that the program is a valid program of the circuit, with the checks of
    /// <see cref="Validation.ValidateTensorProgram"/>. A root that the program does not hold is
    /// accepted: the evaluators give NaN for it, as <see cref="TensorEvaluator"/> does.
    /// </summary>
    /// <exception cref="ArgumentException">The program fails a check.</exception>
    public static void Validate(Circuit circuit, TensorProgram program)
    {
        if (program.SourceSize != circuit.SourceSize)
        {
            throw new ArgumentException($"the program has {program.SourceSize} source cells, and the circuit has {circuit.SourceSize}", nameof(program));
        }
        try
        {
            circuit.ValidateTensorProgram(program with { Roots = UnsignedIndices.Empty });
        }
        catch (InvalidOperationException exception)
        {
            throw new ArgumentException($"the program is not a valid program of the circuit: {exception.Message}", nameof(program), exception);
        }
    }

    /// <summary>
    /// The rows of the program, layer after layer. A layer starts with its gather computes, then holds
    /// one row for each output in output order.
    /// </summary>
    /// <remarks>
    /// TensorExecution runs the sums, the gather reads, the index reads, the gates and the boolean
    /// atoms of a layer, in that order. The validator of a program makes each step write a different
    /// projection row. A gather row has no weights and is not a gate or boolean row. An index row is
    /// after the gate rows, and an index read row is not a gather row. A step reads only its own
    /// projection rows and the packed values of earlier layers. So one row for each output, with its
    /// steps in the TensorExecution order, gives the same values.
    /// </remarks>
    public static ImmutableArray<ShaderRow> FlattenProgram(TensorProgram program)
    {
        var rows = ImmutableArray.CreateBuilder<ShaderRow>();
        var available = program.SourceSize;
        foreach (var layer in program.Layers)
        {
            var outputSize = layer.OutputSize;
            var gateProjection = new int[outputSize];
            Array.Fill(gateProjection, -1);
            for (var gate = 0; gate < layer.GateRows.Count; gate++) gateProjection[layer.GateRows[gate]] = outputSize + gate;
            var isBoolean = new bool[outputSize];
            foreach (var row in layer.BooleanRows) isBoolean[row] = true;
            var indexReads = new TensorIndexRead?[outputSize];
            foreach (var read in layer.IndexReads) indexReads[read.Row] = read;
            var gathers = new List<GatherTerm>?[outputSize];
            foreach (var read in layer.GatherReads)
            {
                (gathers[read.Row] ??= []).Add(new GatherTerm(read.Slot, read.Masked, ListCandidates(read.CandidatePositions, program.SourceSize)));
            }

            ImmutableArray<ShaderTerm> TermsOf(int projectionRow)
            {
                var start = layer.RowOffsets[projectionRow];
                var end = layer.RowOffsets[projectionRow + 1];
                var terms = ImmutableArray.CreateBuilder<ShaderTerm>(end - start);
                for (var index = start; index < end; index++)
                {
                    terms.Add(new ShaderTerm(layer.Weights[index], layer.InputIndices[layer.Columns[index]]));
                }
                return terms.MoveToImmutable();
            }

            foreach (var compute in layer.GatherComputes)
            {
                rows.Add(new GatherComputeRow(compute.Slot, [.. compute.BitPositions]));
            }
            for (var row = 0; row < outputSize; row++)
            {
                var position = available + row;
                if (indexReads[row] is { } read)
                {
                    rows.Add(new IndexRow(
                        position,
                        layer.Bias[read.IndexRow],
                        TermsOf(read.IndexRow),
                        read.BelowZero,
                        read.AboveZero,
                        IndexCandidates(read, program.SourceSize)));
                }
                else if (gateProjection[row] >= 0)
                {
                    rows.Add(new RegluRow(position, layer.Bias[row], TermsOf(row), layer.Bias[gateProjection[row]], TermsOf(gateProjection[row])));
                }
                else if (isBoolean[row])
                {
                    rows.Add(new BooleanRow(position, layer.Bias[row], TermsOf(row)));
                }
                else
                {
                    rows.Add(new AffineRow(position, layer.Bias[row], TermsOf(row), gathers[row] is { } list ? [.. list] : []));
                }
            }
            available += outputSize;
        }
        return rows.ToImmutable();
    }

    /// <summary>
    /// The packed position of each root of the program, or -1 for a root that the program does not
    /// hold. TensorEvaluator gives NaN for such a root.
    /// </summary>
    public static int[] RootPositions(TensorProgram program)
    {
        var positionOfWire = new Dictionary<int, int>();
        for (var position = program.WireOrder.Count - 1; position >= 0; position--) positionOfWire[program.WireOrder[position]] = position;
        var positions = new int[program.Roots.Count];
        for (var root = 0; root < positions.Length; root++)
        {
            positions[root] = positionOfWire.TryGetValue(program.Roots[root], out var position) ? position : -1;
        }
        return positions;
    }

    // The candidates of an index read. A run with a computed value becomes a list of computed values.
    private static ShaderCandidates IndexCandidates(TensorIndexRead read, int sourceSize)
    {
        if (read.CandidatePositions.Count > 0) return ListCandidates(read.CandidatePositions, sourceSize);
        var last = read.CandidateBase + ((read.CandidateCount - 1) * read.CandidateStride);
        if (read.CandidateBase < sourceSize && last < sourceSize)
        {
            return new ShaderCandidates(CandidateForm.Strided, read.CandidateCount, read.CandidateBase, read.CandidateStride, [], null);
        }
        var positions = Enumerable.Range(0, read.CandidateCount).Select(index => read.CandidateBase + (index * read.CandidateStride));
        return new ShaderCandidates(CandidateForm.Computed, read.CandidateCount, 0, 0, [.. positions], null);
    }

    // The candidates of a stored list: evenly spaced source cells, other source cells, or a list with
    // a computed value.
    private static ShaderCandidates ListCandidates(UnsignedIndices table, int sourceSize)
    {
        ImmutableArray<int> positions = [.. table];
        if (positions.Any(position => position >= sourceSize))
        {
            return new ShaderCandidates(CandidateForm.Computed, positions.Length, 0, 0, positions, table);
        }
        var stride = positions.Length > 1 ? positions[1] - positions[0] : 0;
        var evenlySpaced = true;
        for (var index = 1; index < positions.Length; index++) evenlySpaced &= positions[index] - positions[index - 1] == stride;
        return evenlySpaced
            ? new ShaderCandidates(CandidateForm.Strided, positions.Length, positions[0], stride, [], null)
            : new ShaderCandidates(CandidateForm.SourceList, positions.Length, 0, 0, positions, table);
    }

    /// <summary>
    /// The rows that the roots need, by packed position, and the gather slots that they need. A row reads
    /// its terms, its gate terms, its index terms, its candidates and the bits of its gather slots. An index
    /// read of a constant table reads no candidate: the writer reads the table from the storage buffer.
    /// </summary>
    public static (bool[] Rows, bool[] Slots) Needed(ImmutableArray<ShaderRow> rows, int[] rootPositions, int valueCount, int slotCount, ConstantTables tables)
    {
        var needed = new bool[valueCount];
        var slots = new bool[slotCount];
        foreach (var position in rootPositions)
        {
            if (position >= 0) needed[position] = true;
        }
        void Mark(ImmutableArray<ShaderTerm> terms)
        {
            foreach (var term in terms) needed[term.Position] = true;
        }
        void MarkCandidates(ShaderCandidates candidates)
        {
            if (tables.Contains(candidates)) return;
            for (var candidate = 0; candidate < candidates.Count; candidate++) needed[candidates.PositionAt(candidate)] = true;
        }
        for (var index = rows.Length - 1; index >= 0; index--)
        {
            switch (rows[index])
            {
                case GatherComputeRow compute:
                    if (!slots[compute.Slot]) continue;
                    foreach (var bit in compute.BitPositions) needed[bit] = true;
                    break;
                case AffineRow affine when needed[affine.Position]:
                    Mark(affine.Terms);
                    foreach (var gather in affine.Gathers)
                    {
                        slots[gather.Slot] = true;
                        MarkCandidates(gather.Candidates);
                    }
                    break;
                case RegluRow reglu when needed[reglu.Position]:
                    Mark(reglu.Terms);
                    Mark(reglu.GateTerms);
                    break;
                case BooleanRow boolean when needed[boolean.Position]:
                    Mark(boolean.Terms);
                    break;
                case IndexRow read when needed[read.Position]:
                    Mark(read.IndexTerms);
                    MarkCandidates(read.Candidates);
                    break;
            }
        }
        return (needed, slots);
    }
}

/// <summary>
/// The constant tables of a program: each list of computed candidates of an index read whose candidates
/// are all constant rows (an affine row with no term and no gather). Two reads of the same values share
/// one table. <see cref="Values"/> holds the tables one after the other, with the exact bits of each
/// constant.
/// </summary>
internal sealed class ConstantTables
{
    private readonly Dictionary<ShaderCandidates, int> starts;

    private ConstantTables(Dictionary<ShaderCandidates, int> starts, ImmutableArray<float> values)
    {
        this.starts = starts;
        Values = values;
    }

    /// <summary>A program with no constant table.</summary>
    public static ConstantTables None { get; } = new(new Dictionary<ShaderCandidates, int>(ReferenceEqualityComparer.Instance), []);

    /// <summary>The cells of the tables, one table after the other.</summary>
    public ImmutableArray<float> Values { get; }

    /// <summary>Find the constant tables of the index reads of the rows.</summary>
    public static ConstantTables Find(ImmutableArray<ShaderRow> rows, int sourceSize)
    {
        var constants = new Dictionary<int, float>();
        foreach (var row in rows)
        {
            if (row is AffineRow { Terms.Length: 0, Gathers.Length: 0 } constant) constants[constant.Position] = constant.Bias;
        }
        var starts = new Dictionary<ShaderCandidates, int>(ReferenceEqualityComparer.Instance);
        var byValues = new Dictionary<string, int>(StringComparer.Ordinal);
        var values = ImmutableArray.CreateBuilder<float>();
        foreach (var row in rows)
        {
            if (row is not IndexRow { Candidates: { Form: CandidateForm.Computed, Count: > 1 } candidates }) continue;
            var table = new float[candidates.Count];
            var allConstant = true;
            for (var candidate = 0; candidate < table.Length && allConstant; candidate++)
            {
                var position = candidates.PositionAt(candidate);
                allConstant = position >= sourceSize && constants.TryGetValue(position, out table[candidate]);
            }
            if (!allConstant) continue;
            var key = string.Join(",", table.Select(value => BitConverter.SingleToUInt32Bits(value).ToString("x8", System.Globalization.CultureInfo.InvariantCulture)));
            if (!byValues.TryGetValue(key, out var start))
            {
                start = values.Count;
                byValues[key] = start;
                values.AddRange(table);
            }
            starts[candidates] = start;
        }
        return new ConstantTables(starts, values.ToImmutable());
    }

    /// <summary>True when the candidates are a constant table.</summary>
    public bool Contains(ShaderCandidates candidates) => starts.ContainsKey(candidates);

    /// <summary>The first cell of the table of the candidates, or -1 when they are no constant table.</summary>
    public int StartOf(ShaderCandidates candidates) => starts.TryGetValue(candidates, out var start) ? start : -1;
}
