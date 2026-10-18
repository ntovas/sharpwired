// Handwired source: src/json-search/bit-emit.ts (declaredWhole, narrowed, sumRange, proveStage), with the
// loop parts removed: a plain program has no registers and no fields. The proof of the whole wires and the
// bits of a compiled program, one proof for each packed position. The integer writer holds a proven wire
// as an i32 or a u32 (question 3 of the phase 5 plan).

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>How the integer writer holds a wire.</summary>
public enum WholeKind
{
    /// <summary>No proof: an f32 value.</summary>
    Float,

    /// <summary>A whole number within the range of the proof: an i32 value.</summary>
    Whole,

    /// <summary>A whole number within 0..1, that is 0 or 1: a u32 value.</summary>
    Bit,
}

/// <summary>
/// The proof of one packed position. For <see cref="WholeKind.Whole"/> and <see cref="WholeKind.Bit"/>,
/// each value of the wire is a whole number within <see cref="Minimum"/>..<see cref="Maximum"/>, and
/// the range is inside ±2^24. A <see cref="WholeKind.Float"/> proof has the range 0..0.
/// </summary>
public readonly record struct WholeProof(WholeKind Kind, long Minimum, long Maximum)
{
    internal static readonly WholeProof Float = new(WholeKind.Float, 0, 0);

    internal bool IsProven => Kind != WholeKind.Float;

    internal bool IsBit => Kind == WholeKind.Bit;

    // A whole proof of the range, a bit when the range is within 0..1, or no proof for an empty range.
    internal static WholeProof Of(long minimum, long maximum) =>
        minimum > maximum ? Float : new(minimum >= 0 && maximum <= 1 ? WholeKind.Bit : WholeKind.Whole, minimum, maximum);
}

/// <summary>
/// The proof of the whole wires and the bits of a program. A proven wire holds a whole number within
/// ±2^24 for each source table that passes the checks of the evaluators, and each FP32 product and
/// partial sum that computes it is a whole number within ±2^24 too. FP32 computes such a number with
/// no rounding, in any order, so an i32 statement gives the value of the f32 statement.
/// </summary>
/// <remarks>
/// <para>The rules, in the order of the rows:</para>
/// <list type="bullet">
/// <item>A source cell is a bit when <see cref="Circuit.ExactBits"/> declares it. It is whole when
/// its grid is 1 or more and its range is inside ±2^24, or when its range holds one whole number.
/// The integer GPU evaluator checks the grid and the bit declarations of each source table
/// (<see cref="ValidateSourceDeclarations"/>).</item>
/// <item>An affine row is whole when its bias, its weights and its operands are whole, and the sum of
/// the magnitudes of the bias and of each product is at most 2^24. That sum bounds each product and
/// each partial sum. A gather read adds a whole candidate, or the mass times the candidate.</item>
/// <item>A ReGLU row is whole when its left form and its gate form are whole by the same rule, and each
/// product of the two ranges is at most 2^24.</item>
/// <item>A boolean atom is a bit. The builder accepts a score within 0..1 only.</item>
/// <item>An index read is whole when each candidate that its index range can reach is whole. With an
/// index form that is not whole, the read can reach each candidate. A side that reads zero adds 0.</item>
/// <item>The bit that Compare returns (<see cref="Operation.ComparisonBit"/>) is a bit. Compare proves
/// it for each pair of operand values within their ranges and grids.</item>
/// </list>
/// <para>A grid or a bit that the caller declares on an operation is not a proof, because no evaluator
/// checks it. A whole value can be +0 where the reference value is -0, because i32 has no -0. The sign
/// of a zero never changes a nonzero value of the four instructions.</para>
/// <para>A gather read selects the candidate of the one bit of its group that is 1. The proof uses the
/// declared one-hot groups, as TensorExecution and the f32 writer do.</para>
/// </remarks>
public static class IntegerProof
{
    /// <summary>The largest magnitude of a whole value, of a product and of a partial sum: 2^24.</summary>
    public const long Limit = 16_777_216;

    /// <summary>One proof for each packed position of the program: the source cells, then the rows.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit.</exception>
    public static ImmutableArray<WholeProof> Prove(Circuit circuit, TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        ShaderRows.Validate(circuit, program);
        return Analyze(circuit, program, ShaderRows.FlattenProgram(program)).Proofs;
    }

    /// <summary>
    /// The proofs, and for each position whether the sums of its row are whole, so that integer
    /// statements give its value. For an index read, the sum is the index form. Each position
    /// without a row is not exact.
    /// </summary>
    internal static ProofResult Analyze(Circuit circuit, TensorProgram program, ImmutableArray<ShaderRow> rows)
    {
        var proofs = new WholeProof[program.ValueCount];
        var exact = new bool[program.ValueCount];
        for (var cell = 0; cell < program.SourceSize; cell++) proofs[cell] = SourceProof(circuit, cell);
        foreach (var row in rows)
        {
            switch (row)
            {
                case AffineRow affine:
                    {
                        var sum = SumRange(proofs, affine.Bias, affine.Terms);
                        foreach (var gather in affine.Gathers)
                        {
                            if (sum is not { } partial) break;
                            sum = GatherRange(proofs, partial, gather);
                        }
                        Set(affine.Position, sum is { } range ? WholeProof.Of(range.Minimum, range.Maximum) : WholeProof.Float, sum is not null);
                        break;
                    }
                case RegluRow reglu:
                    {
                        var product = ProductRange(SumRange(proofs, reglu.Bias, reglu.Terms), SumRange(proofs, reglu.GateBias, reglu.GateTerms));
                        Set(reglu.Position, product is { } range ? WholeProof.Of(range.Minimum, range.Maximum) : WholeProof.Float, product is not null);
                        break;
                    }
                case BooleanRow boolean:
                    {
                        var score = SumRange(proofs, boolean.Bias, boolean.Terms);
                        Set(boolean.Position, WholeProof.Of(0, 1), score is { Minimum: >= 0, Maximum: <= 1 });
                        break;
                    }
                case IndexRow read:
                    {
                        var index = SumRange(proofs, read.IndexBias, read.IndexTerms);
                        Set(read.Position, IndexProof(proofs, read, index), index is not null);
                        break;
                    }
            }
        }
        return new ProofResult([.. proofs], [.. exact]);

        void Set(int position, WholeProof inferred, bool rowExact)
        {
            var wire = program.WireOrder[position];
            var operation = circuit.Operations[wire - circuit.SourceSize];
            // Compare proves its bit also where the range arithmetic does not reach, for example
            // after a sign cascade.
            var proof = !inferred.IsProven && operation.ComparisonBit ? WholeProof.Of(0, 1) : inferred;
            proofs[position] = proof;
            // A row whose own sums are whole but whose value has no proof is an f32 row. An index read
            // keeps its integer index when the value that it reads is not whole.
            exact[position] = rowExact && (inferred.IsProven || operation.Kind == OperationKind.Index);
        }
    }

    /// <summary>
    /// Check the grid and the bit declarations of the source cells: a cell on a grid holds a finite
    /// multiple of the grid, and an exact bit holds 0, -0 or 1. The integer writer reads the cells
    /// with these declarations as whole numbers, and Compare uses the grids for its bits.
    /// </summary>
    /// <exception cref="ArgumentException">A cell breaks its declaration.</exception>
    internal static void ValidateSourceDeclarations(Circuit circuit, ReadOnlySpan<float> source)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        if (source.Length != circuit.SourceSize) throw new ArgumentException("source table has the wrong length");
        for (var cell = 0; cell < source.Length; cell++)
        {
            double value = source[cell];
            if (circuit.ExactBits[cell] && !(value == 0 || value == 1))
            {
                throw new ArgumentException($"source wire {cell} is declared a bit and must be 0 or 1, got {Fp32.Format(value)}");
            }
            // A division by a power of two is exact in float64 for these magnitudes.
            if (circuit.Grids[cell] is { } grid && !(double.IsFinite(value) && double.IsInteger(value / grid)))
            {
                throw new ArgumentException($"source wire {cell} is declared on the grid {Fp32.Format(grid)} and must be a multiple of it, got {Fp32.Format(value)}");
            }
        }
    }

    /// <summary>
    /// bit-emit.ts sumRange: the range of bias + Σ weight × value, when the bias, each weight and each
    /// value are whole, and the sum of the magnitudes of the bias and of each product is at most 2^24.
    /// Null otherwise. That sum bounds each product and each partial sum, in any order.
    /// </summary>
    internal static SumBound? SumRange(IReadOnlyList<WholeProof> proofs, float bias, ImmutableArray<ShaderTerm> terms)
    {
        if (!IsWholeNumber(bias)) return null;
        long minimum = (long)bias;
        long maximum = minimum;
        long magnitude = Math.Abs(minimum);
        foreach (var term in terms)
        {
            var operand = proofs[term.Position];
            // The writer writes a weight as an i32 literal, so it is a whole number within 2^24 also
            // where its product is small.
            if (!operand.IsProven || !IsWholeNumber(term.Weight)) return null;
            var weight = (long)term.Weight;
            minimum += weight > 0 ? weight * operand.Minimum : weight * operand.Maximum;
            maximum += weight > 0 ? weight * operand.Maximum : weight * operand.Minimum;
            magnitude += Math.Abs(weight) * Math.Max(Math.Abs(operand.Minimum), Math.Abs(operand.Maximum));
            if (magnitude > Limit) return null;
        }
        return new SumBound(minimum, maximum, magnitude);
    }

    /// <summary>A finite FP32 whole number of magnitude at most 2^24. -0 is the whole number 0.</summary>
    internal static bool IsWholeNumber(float value) => float.IsFinite(value) && value == MathF.Truncate(value) && MathF.Abs(value) <= Limit;

    // A source cell: a declared exact bit, a grid of whole numbers with a range inside ±2^24, or a
    // range of one whole number. The range check of the evaluators makes the last rule hold.
    private static WholeProof SourceProof(Circuit circuit, int cell)
    {
        var range = circuit.Ranges[cell];
        if (circuit.ExactBits[cell]) return WholeProof.Of(0, 1);
        var single = range.Minimum == range.Maximum && double.IsInteger(range.Minimum);
        var onGrid = circuit.Grids[cell] is >= 1;
        if (!(single || onGrid) || !(range.Minimum >= -Limit && range.Maximum <= Limit)) return WholeProof.Float;
        return WholeProof.Of((long)Math.Ceiling(range.Minimum), (long)Math.Floor(range.Maximum));
    }

    // f(left × max(0, gate)) with both forms whole: the range of the products of the two ranges.
    private static SumBound? ProductRange(SumBound? left, SumBound? gate)
    {
        if (left is not { } l || gate is not { } g) return null;
        var low = Math.Max(0, g.Minimum);
        var high = Math.Max(0, g.Maximum);
        long[] products = [l.Minimum * low, l.Minimum * high, l.Maximum * low, l.Maximum * high];
        var minimum = products.Min();
        var maximum = products.Max();
        var magnitude = Math.Max(Math.Abs(minimum), Math.Abs(maximum));
        return magnitude > Limit ? null : new SumBound(minimum, maximum, magnitude);
    }

    // The sum so far plus one gather read. The one-hot index can select each candidate. A masked read
    // multiplies the candidate by the mass of the group, 0 or 1, so it can also add 0.
    private static SumBound? GatherRange(IReadOnlyList<WholeProof> proofs, SumBound sum, GatherTerm gather)
    {
        long low = gather.Masked ? 0 : long.MaxValue;
        long high = gather.Masked ? 0 : long.MinValue;
        for (var candidate = 0; candidate < gather.Candidates.Count; candidate++)
        {
            var proof = proofs[gather.Candidates.PositionAt(candidate)];
            if (!proof.IsProven) return null;
            low = Math.Min(low, proof.Minimum);
            high = Math.Max(high, proof.Maximum);
        }
        var magnitude = sum.Magnitude + Math.Max(Math.Abs(low), Math.Abs(high));
        return magnitude > Limit ? null : new SumBound(sum.Minimum + low, sum.Maximum + high, magnitude);
    }

    // The union of the candidates that the index range can reach, and 0 for a side that reads zero.
    private static WholeProof IndexProof(IReadOnlyList<WholeProof> proofs, IndexRow read, SumBound? index)
    {
        var (first, final, zero) = Reach(read, index);
        long minimum = zero ? 0 : long.MaxValue;
        long maximum = zero ? 0 : long.MinValue;
        for (var candidate = first; candidate <= final; candidate++)
        {
            var proof = proofs[read.Candidates.PositionAt(candidate)];
            if (!proof.IsProven) return WholeProof.Float;
            minimum = Math.Min(minimum, proof.Minimum);
            maximum = Math.Max(maximum, proof.Maximum);
        }
        return WholeProof.Of(minimum, maximum);
    }

    /// <summary>
    /// The candidates that an index read can reach, first to final, and whether a side that reads zero
    /// can apply. With no whole index, each candidate and each zero side.
    /// </summary>
    internal static (int First, int Final, bool Zero) Reach(IndexRow read, SumBound? index)
    {
        var last = read.Candidates.Count - 1;
        if (index is not { } range) return (0, last, read.BelowZero || read.AboveZero);
        var first = (int)Math.Clamp(range.Minimum, 0, last);
        var final = (int)Math.Clamp(range.Maximum, 0, last);
        var zero = (read.BelowZero && range.Minimum < 0) || (read.AboveZero && range.Maximum > last);
        return (first, final, zero);
    }
}

/// <summary>The range of a whole sum, and the bound of the magnitudes of its products and partial sums.</summary>
internal readonly record struct SumBound(long Minimum, long Maximum, long Magnitude);

/// <summary>The proofs and the exact rows of a program, by packed position.</summary>
internal sealed record ProofResult(ImmutableArray<WholeProof> Proofs, ImmutableArray<bool> Exact);
