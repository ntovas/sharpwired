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
/// <remarks>
/// Only Sharpwired.Core can derive from this class, so the code that switches over the records
/// knows every record. A C# record type cannot have this limit, because a record that is not
/// sealed must have a public or protected copy constructor. So the records are classes with
/// value equality.
/// </remarks>
public abstract class MacroRecord
{
    private protected MacroRecord()
    {
    }
}

/// <summary>The record of Circuit.Multiply. Two records with the same wires are equal.</summary>
public sealed class MultiplyMacro(Wire left, Wire right) : MacroRecord, IEquatable<MultiplyMacro>
{
    public Wire Left { get; } = left;

    public Wire Right { get; } = right;

    public static bool operator ==(MultiplyMacro? left, MultiplyMacro? right) => Equals(left, right);

    public static bool operator !=(MultiplyMacro? left, MultiplyMacro? right) => !Equals(left, right);

    public bool Equals(MultiplyMacro? other) => other is not null && Left == other.Left && Right == other.Right;

    public override bool Equals(object? obj) => Equals(obj as MultiplyMacro);

    public override int GetHashCode() => HashCode.Combine(Left, Right);

    public override string ToString() => $"MultiplyMacro {{ Left = {Left}, Right = {Right} }}";
}

/// <summary>The record of Circuit.Select. Two records with the same wires are equal.</summary>
public sealed class SelectMacro(Wire gate, Wire off, Wire on) : MacroRecord, IEquatable<SelectMacro>
{
    public Wire Gate { get; } = gate;

    public Wire Off { get; } = off;

    public Wire On { get; } = on;

    public static bool operator ==(SelectMacro? left, SelectMacro? right) => Equals(left, right);

    public static bool operator !=(SelectMacro? left, SelectMacro? right) => !Equals(left, right);

    public bool Equals(SelectMacro? other) => other is not null && Gate == other.Gate && Off == other.Off && On == other.On;

    public override bool Equals(object? obj) => Equals(obj as SelectMacro);

    public override int GetHashCode() => HashCode.Combine(Gate, Off, On);

    public override string ToString() => $"SelectMacro {{ Gate = {Gate}, Off = {Off}, On = {On} }}";
}

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
