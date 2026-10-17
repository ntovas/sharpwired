// Handwired source: src/runtime/cells.ts (CellValueError, createValidator).

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.Cells;

/// <summary>
/// Handwired CellValueError: a source table breaks the declaration of one cell. Position is the
/// position of the cell in the table, so that ProgramEvaluator can tell whether the value that
/// failed is NaN.
/// </summary>
public sealed class CellValueException : ArgumentException
{
    public CellValueException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    /// <summary>The position of the cell in the table.</summary>
    public int Position { get; }
}

// Handwired Rule: a one-hot group or an at-most-one rule over the members.
internal sealed record CellRule(string Name, bool AtMostOne, ImmutableArray<Wire> Members, bool AllowAllZero, bool DeclareToCircuit)
{
    // The Handwired rule name in messages: 'oneHot' or 'atMostOne'.
    public string RuleName => AtMostOne ? "atMostOne" : "oneHot";
}

/// <summary>
/// Handwired createValidator and the function that it returns: check a table against each
/// declaration. The checks run in this order: the length of the table, each cell for a finite value
/// within its range, each exact bit, each grid, then each one-hot and at-most-one rule. The message
/// names the cell or the rule.
/// </summary>
public sealed class CellValidator
{
    private readonly string[] names;
    private readonly double[] minimums;
    private readonly double[] maximums;
    private readonly int[] bitWires;
    private readonly int[] gridWires;
    private readonly double[] gridSteps;
    private readonly CellRule[] rules;
    private readonly string[] minimumTexts;
    private readonly string[] maximumTexts;

    internal CellValidator(IReadOnlyList<(string Name, CellSpec Spec)> cells, IReadOnlyList<CellRule> rules)
    {
        names = [.. cells.Select(cell => cell.Name)];
        minimums = [.. cells.Select(cell => cell.Spec.Range.Minimum)];
        maximums = [.. cells.Select(cell => cell.Spec.Range.Maximum)];
        minimumTexts = [.. minimums.Select(Fp32.Format)];
        maximumTexts = [.. maximums.Select(Fp32.Format)];
        bitWires = [.. Enumerable.Range(0, cells.Count).Where(wire => cells[wire].Spec.ExactBit)];
        gridWires = [.. Enumerable.Range(0, cells.Count).Where(wire => cells[wire].Spec.Grid is not null)];
        gridSteps = [.. gridWires.Select(wire => cells[wire].Spec.Grid!.Value)];
        this.rules = [.. rules];
    }

    /// <summary>The number of declared cells, which is the length of a valid table.</summary>
    public int CellCount => names.Length;

    /// <summary>
    /// Check the table. Throw <see cref="CellValueException"/> for a cell, and
    /// <see cref="ArgumentException"/> for the length or a rule.
    /// </summary>
    public void Validate(ReadOnlySpan<float> source)
    {
        if (source.Length != names.Length)
        {
            throw new ArgumentException($"the table holds {source.Length} numbers, but {names.Length} cells are declared");
        }
        for (var wire = 0; wire < source.Length; wire++)
        {
            double value = source[wire];
            if (!(value >= minimums[wire] && value <= maximums[wire] && double.IsFinite(value)))
            {
                throw new CellValueException(
                    $"{names[wire]} must be finite and within {minimumTexts[wire]}..{maximumTexts[wire]}, got {Fp32.Format(value)}", wire);
            }
        }
        for (var index = 0; index < bitWires.Length; index++)
        {
            var wire = bitWires[index];
            if (source[wire] != 0 && source[wire] != 1)
            {
                throw new CellValueException($"{names[wire]} must be exactly 0 or 1, got {Fp32.Format(source[wire])}", wire);
            }
        }
        for (var index = 0; index < gridWires.Length; index++)
        {
            var wire = gridWires[index];
            var grid = gridSteps[index];
            // Handwired Number.isInteger(source[wire] / grid) divides in float64.
            if (!IsInteger(source[wire] / grid))
            {
                throw new CellValueException($"{names[wire]} must be a multiple of {Fp32.Format(grid)}, got {Fp32.Format(source[wire])}", wire);
            }
        }
        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
        {
            var rule = rules[ruleIndex];
            // Handwired adds in float64.
            var mass = 0.0;
            var members = rule.Members.AsSpan();
            for (var index = 0; index < members.Length; index++) mass += source[members[index].Position];
            var valid = rule.AtMostOne ? mass <= 1 : mass == 1 || (rule.AllowAllZero && mass == 0);
            if (!valid) throw new ArgumentException($"{rule.Name} breaks its {rule.RuleName} rule with mass {Fp32.Format(mass)}");
        }
    }

    // Handwired Number.isInteger: false for NaN and for infinities.
    private static bool IsInteger(double value) => double.IsFinite(value) && Math.Floor(value) == value;
}
