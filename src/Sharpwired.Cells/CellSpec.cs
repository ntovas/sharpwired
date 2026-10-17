// Handwired source: src/runtime/cells.ts (Range, anyNumber, unitInterval, CellPolicy, CellSpec,
// CellOptions, BitOptions, cell, constant, input, inputBit, event, state, stateBit, list, OneHotSpec,
// oneHot, PermutationSpec, permutation, Spec).
// Handwired Range is the Core type NumericRange here.

using System.Collections.Immutable;
using System.Globalization;
using Sharpwired.Core;

namespace Sharpwired.Cells;

/// <summary>
/// Handwired CellPolicy: what happens to a cell around an evaluation.
/// </summary>
public enum CellPolicy
{
    /// <summary>Set one time in the initial table, for example a margin of 12 px. The program keeps it.</summary>
    Constant,

    /// <summary>The host writes it before an evaluation, for example the viewport width. The program keeps it.</summary>
    Input,

    /// <summary>
    /// An exact bit that the host sets to 1 when an event arrives, for example a pointer press, next
    /// to input cells for the coordinates of the event. The program resets it to 0.
    /// </summary>
    Event,

    /// <summary>The program computes its next value. Builder code pairs the cell with it through SourceTable.Next.</summary>
    State,
}

/// <summary>Handwired Spec: a cell, a one-hot group or a permutation.</summary>
public abstract record Spec;

/// <summary>
/// Handwired CellSpec: one cell.
/// </summary>
/// <param name="Policy">What happens to the cell around an evaluation.</param>
/// <param name="Range">The range of the values. The validator checks it, and CreateCircuit declares it.</param>
/// <param name="Initial">The value of the cell in the initial table.</param>
/// <param name="ExactBit">True when the validator requires exactly 0 or 1.</param>
/// <param name="Grid">
/// Null, or a power of two that divides every value, for example 1/64 for pointer coordinates on a
/// 1/64 px grid. CreateCircuit declares it with AssumeGrid, so that Compare can build integer bits,
/// and the validator checks it.
/// </param>
public sealed record CellSpec(CellPolicy Policy, NumericRange Range, double Initial, bool ExactBit, double? Grid) : Spec;

/// <summary>
/// Handwired OneHotSpec: exactly one member is 1, or no member when AllowAllZero is true.
/// </summary>
/// <param name="Members">The cells of the group. Each member is an exact bit.</param>
/// <param name="AllowAllZero">True when all members can be 0.</param>
/// <param name="DeclareToCircuit">
/// True when CreateCircuit declares the group with AssumeOneHot. The compiler can then turn masked
/// sums over the group into gathers, so the declaration can change the compiled program. When it is
/// false, only the validator checks the group.
/// </param>
public sealed record OneHotSpec(ImmutableArray<CellSpec> Members, bool AllowAllZero, bool DeclareToCircuit) : Spec;

/// <summary>
/// Handwired PermutationSpec: a stacking order as a permutation matrix. Cells[rank][item] is 1 when
/// the item is at that rank. Each row and each column is a declared one-hot group.
/// </summary>
public sealed record PermutationSpec(ImmutableArray<ImmutableArray<CellSpec>> Cells) : Spec;

/// <summary>The Handwired cell functions.</summary>
public static class Cell
{
    /// <summary>Handwired anyNumber: -Infinity..Infinity.</summary>
    public static NumericRange AnyNumber { get; } = new(double.NegativeInfinity, double.PositiveInfinity);

    /// <summary>Handwired unitInterval: 0..1.</summary>
    public static NumericRange UnitInterval { get; } = new(0, 1);

    /// <summary>Handwired constant: a cell that the initial table sets to value.</summary>
    public static CellSpec Constant(double value, NumericRange range, double? grid = null) =>
        Create(CellPolicy.Constant, range, false, value, grid);

    /// <summary>Handwired input: a cell that the host writes before an evaluation.</summary>
    public static CellSpec Input(NumericRange range, double initial = 0, double? grid = null) =>
        Create(CellPolicy.Input, range, false, initial, grid);

    /// <summary>Handwired inputBit: an exact bit that the host writes, for example the settle check bit.</summary>
    public static CellSpec InputBit(double initial = 0) => Create(CellPolicy.Input, UnitInterval, true, initial, null);

    /// <summary>
    /// Handwired event: an exact bit that the host sets when an event arrives. The grid is null or 1.
    /// A grid of 1 lets sums and products that read the bit keep a grid.
    /// </summary>
    public static CellSpec Event(double? grid = null) => Create(CellPolicy.Event, UnitInterval, true, 0, BitGrid(grid));

    /// <summary>Handwired state: a cell that the program computes.</summary>
    public static CellSpec State(NumericRange range, double initial = 0, double? grid = null) =>
        Create(CellPolicy.State, range, false, initial, grid);

    /// <summary>Handwired stateBit: an exact bit that the program computes, for example whether a window is open. The grid is null or 1.</summary>
    public static CellSpec StateBit(double initial = 0, double? grid = null) =>
        Create(CellPolicy.State, UnitInterval, true, initial, BitGrid(grid));

    /// <summary>Handwired list: make(0), make(1), ..., make(count - 1).</summary>
    public static T[] List<T>(int count, Func<int, T> make)
    {
        ArgumentNullException.ThrowIfNull(make);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var items = new T[count];
        for (var index = 0; index < count; index++) items[index] = make(index);
        return items;
    }

    /// <summary>Handwired oneHot: exactly one member is 1, or none when allowAllZero is true, for example the selected tab.</summary>
    public static OneHotSpec OneHot(IReadOnlyList<CellSpec> members, bool allowAllZero = false, bool declareToCircuit = true)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (!members.All(member => member.ExactBit)) throw new ArgumentException("one-hot members must be exact bits, e.g. stateBit()");
        return new OneHotSpec([.. members], allowAllZero, declareToCircuit);
    }

    /// <summary>
    /// Handwired permutation: order[rank] is the item at that rank. For example, Permutation([2, 0, 1])
    /// puts item 2 at rank 0. The grid of each cell is null or 1.
    /// </summary>
    public static PermutationSpec Permutation(IReadOnlyList<int> order, double? grid = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        var sorted = order.Order().ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            if (sorted[index] != index)
            {
                var text = string.Join(", ", order.Select(item => item.ToString(CultureInfo.InvariantCulture)));
                throw new ArgumentException($"{text} is not a permutation of 0..{(order.Count - 1).ToString(CultureInfo.InvariantCulture)}");
            }
        }
        BitGrid(grid);
        return new PermutationSpec([.. order.Select(item => Enumerable.Range(0, order.Count).Select(column => StateBit(column == item ? 1 : 0, grid)).ToImmutableArray())]);
    }

    // Handwired cell: the one place that checks the range.
    private static CellSpec Create(CellPolicy policy, NumericRange range, bool exactBit, double initial, double? grid)
    {
        if (!(range.Minimum <= range.Maximum))
        {
            throw new ArgumentException($"a cell's range is inverted: {Fp32.Format(range.Minimum)}..{Fp32.Format(range.Maximum)}");
        }
        return new CellSpec(policy, range, initial, exactBit, grid);
    }

    // Handwired BitOptions = {grid?: 1}: the type checker refuses another grid. Here the check runs at runtime.
    private static double? BitGrid(double? grid)
    {
        if (grid is { } step && step != 1) throw new ArgumentException("a bit's grid can only be 1");
        return grid;
    }
}
