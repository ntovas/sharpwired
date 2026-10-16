// Handwired source: src/runtime/numeric-ui-core.ts (AtomKind, Macro, CandidateTable,
// IndexRead, Operation, OneHotGroup).

using System.Collections.Immutable;

namespace Sharpwired.Core;

/// <summary>The four instructions of a program.</summary>
public enum OperationKind
{
    /// <summary>affine(left) = left.</summary>
    Affine,

    /// <summary>reglu(left, gate) = f(left × max(0, gate)).</summary>
    Reglu,

    /// <summary>boolean(score) = f(f(f(score) + 8_388_609) - 8_388_609).</summary>
    Boolean,

    /// <summary>index(form, candidates, below, above) = values[candidates[floor(form)]], with a rule outside the table.</summary>
    Index,
}

/// <summary>
/// The record of the operation that a circuit method expanded. The backward pass uses it to
/// differentiate the whole operation, and the builder uses it in the expression key.
/// </summary>
public abstract record MacroRecord;

/// <summary>The record of Circuit.Multiply.</summary>
public sealed record MultiplyMacro(Wire Left, Wire Right) : MacroRecord;

/// <summary>The record of Circuit.Select.</summary>
public sealed record SelectMacro(Wire Gate, Wire Off, Wire On) : MacroRecord;

/// <summary>
/// One distinct candidates array, registered once for each circuit. Later reads of the same
/// content find it, so the builder checks a large table one time only.
/// </summary>
public sealed class CandidateTable
{
    internal CandidateTable(int id, ImmutableArray<Wire> candidates, int maximumDepth)
    {
        Id = id;
        Candidates = candidates;
        MaximumDepth = maximumDepth;
    }

    public int Id { get; }

    public ImmutableArray<Wire> Candidates { get; }

    /// <summary>The largest depth of a candidate.</summary>
    public int MaximumDepth { get; }

    /// <summary>The union of the candidate ranges, for each reachable span of positions and out-of-range rules.</summary>
    internal Dictionary<string, NumericRange> RangeByReach { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The index read part of an index operation. IndexRange is the index range that the
/// builder accepted: the range inferred from the index form, narrowed by a declared range.
/// </summary>
public sealed record IndexReadInstruction(CandidateTable Table, OutOfRange Below, OutOfRange Above, NumericRange IndexRange);

/// <summary>
/// One operation. For an index read, GateTerms and GateBias hold the index form, and the
/// left side is empty.
/// </summary>
public sealed record Operation
{
    public required OperationKind Kind { get; init; }

    public required ImmutableArray<Term> LeftTerms { get; init; }

    public required double LeftBias { get; init; }

    public required ImmutableArray<Term> GateTerms { get; init; }

    public required double GateBias { get; init; }

    public required int Depth { get; init; }

    /// <summary>True when the builder must keep the evaluation, the term order and the operand wires.</summary>
    public bool PreserveRounding { get; init; }

    public MacroRecord? Macro { get; init; }

    public IndexReadInstruction? IndexRead { get; init; }

    public RoundingPointKind? RoundingPoint { get; init; }

    /// <summary>
    /// True for the bit that Compare returns, so that the builder knows that the wire holds
    /// exactly 0 or 1. The operations inside the comparison do not get it.
    /// </summary>
    public bool ComparisonBit { get; init; }
}

/// <summary>
/// A declared invariant over a set of wires: each member is exactly 0 or 1, and the sum of the
/// members is exactly 1. With AllowAllZero, the state with all members at 0 is also legal.
/// </summary>
public sealed record OneHotGroup(ImmutableArray<Wire> Members, bool AllowAllZero);
