// Handwired source: src/runtime/numeric-ui-core.ts (validateCircuit, validateTerms, assertWire,
// indexMagnitudeLimit, validateIndexReads, validateOneHotGroups, validateGrids, validateNoNaN,
// describeFirstNaN, markWiresRead, markWiresReading, forEachInput, assertNonNegativeFinite,
// assertUnitInterval, assertFiniteValues, validateWireView, invariant).

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Sharpwired.Core;

/// <summary>Validators for circuits and the values that they compute.</summary>
public static partial class Validation
{
    /// <summary>
    /// Every integer within ±2^24 is an FP32 value, so an index form with a range strictly inside
    /// these bounds holds each position exactly.
    /// </summary>
    public const double IndexMagnitudeLimit = 16_777_216;

    /// <summary>Refuse a wire that is not below limit.</summary>
    public static void AssertWire(Wire wire, int limit)
    {
        if (!(wire.Position >= 0 && wire.Position < limit)) throw new ArgumentException("wire is outside the compiled circuit");
    }

    /// <summary>
    /// Check that each operation has finite biases and weights, reads only earlier wires, and that
    /// each index read keeps the index read contract. Returns the number of wires.
    /// </summary>
    public static int ValidateCircuit(this Circuit circuit)
    {
        var wireCount = circuit.Width;
        // The largest candidate wire of each table, so that the validator scans a table one time only.
        var largestCandidateByTable = new Dictionary<CandidateTable, int>(ReferenceEqualityComparer.Instance);
        for (var operationIndex = 0; operationIndex < circuit.Operations.Count; operationIndex++)
        {
            var operation = circuit.Operations[operationIndex];
            if (!double.IsFinite(operation.LeftBias)) throw new InvalidOperationException("left bias must be finite");
            if (!double.IsFinite(operation.GateBias)) throw new InvalidOperationException("gate bias must be finite");
            ValidateTerms(operation.LeftTerms, circuit.SourceSize + operationIndex);
            ValidateTerms(operation.GateTerms, circuit.SourceSize + operationIndex);
            var read = operation.IndexRead;
            if ((operation.Kind == OperationKind.Index) != (read is not null))
            {
                throw new InvalidOperationException("only index reads carry a candidate table");
            }
            if (read is null) continue;
            if (!(operation.LeftTerms.Length == 0 && operation.LeftBias == 0))
            {
                throw new InvalidOperationException("an index read keeps its index form on the gate side");
            }
            if (operation.GateTerms.Length == 0) throw new InvalidOperationException("an index read needs a runtime index");
            if (!(read.IndexRange.Minimum > -IndexMagnitudeLimit
                && read.IndexRange.Minimum <= read.IndexRange.Maximum
                && read.IndexRange.Maximum < IndexMagnitudeLimit))
            {
                throw new InvalidOperationException("an index read needs an accepted index range strictly inside ±2^24");
            }
            if (read.Table.Candidates.Length == 0) throw new InvalidOperationException("an index read needs at least one candidate");
            if (!largestCandidateByTable.TryGetValue(read.Table, out var largestCandidate))
            {
                largestCandidate = 0;
                foreach (var candidate in read.Table.Candidates)
                {
                    AssertWire(candidate, wireCount);
                    largestCandidate = Math.Max(largestCandidate, candidate.Position);
                }
                largestCandidateByTable[read.Table] = largestCandidate;
            }
            if (!(largestCandidate < circuit.SourceSize + operationIndex))
            {
                throw new InvalidOperationException("index read candidates must be earlier wires");
            }
        }
        return wireCount;
    }

    /// <summary>
    /// Check that every index form holds an integer within the index range that the builder
    /// accepted, also a range declared on the index wire. Then floor changes nothing, and the read
    /// equals a one-hot masked sum over the same table. For example, an index built as
    /// scrollTop / 28 fails at scrollTop 14. For tests: the evaluators do not do this check.
    /// </summary>
    public static void ValidateIndexReads(this Circuit circuit, float[] values)
    {
        if (values.Length != circuit.Width) throw new ArgumentException("index read validation needs the full wire values");
        for (var operationIndex = 0; operationIndex < circuit.Operations.Count; operationIndex++)
        {
            var operation = circuit.Operations[operationIndex];
            if (operation.Kind != OperationKind.Index) continue;
            double index = Circuit.EvaluateTerms(values, operation.GateTerms, operation.GateBias);
            var range = operation.IndexRead!.IndexRange;
            if (!(double.IsInteger(index) && index >= range.Minimum && index <= range.Maximum))
            {
                throw new InvalidOperationException(
                    $"index read wire {circuit.SourceSize + operationIndex} needs an integer index within "
                    + $"{Fp32.Format(range.Minimum)}..{Fp32.Format(range.Maximum)}, got {Fp32.Format(index)}");
            }
        }
    }

    /// <summary>
    /// Check each declared one-hot group against evaluated wire values: each member is exactly 0 or
    /// 1, and the members add up to exactly 1, or to 0 where the group allows all zeros. For tests
    /// and for checks of values that a trainer writes back: the evaluators do not do this check.
    /// </summary>
    public static void ValidateOneHotGroups(this Circuit circuit, float[] values)
    {
        if (values.Length != circuit.Width) throw new ArgumentException("one-hot validation needs the full wire values");
        foreach (var group in circuit.OneHotGroups)
        {
            // The mass is a float64 sum, as in Handwired.
            double mass = 0;
            foreach (var member in group.Members)
            {
                double value = values[member.Position];
                if (!(value == 0 || value == 1))
                {
                    throw new InvalidOperationException($"one-hot member wire {member} must be exactly 0 or 1, got {Fp32.Format(value)}");
                }
                mass += value;
            }
            if (!(mass == 1 || (group.AllowAllZero && mass == 0)))
            {
                throw new InvalidOperationException($"one-hot group must have exactly one active member, got mass {Fp32.Format(mass)}");
            }
        }
    }

    /// <summary>
    /// Check each declared or inferred grid against evaluated wire values: each wire with a grid
    /// holds a finite multiple of it. For example, a pointer cell declared on a 1/64 grid fails at
    /// 100.01. Inferred grids come from the declared grids and ranges of their operands, so one
    /// false declaration can fail many wires. For tests: the evaluators do not do this check.
    /// </summary>
    public static void ValidateGrids(this Circuit circuit, float[] values)
    {
        if (values.Length != circuit.Width) throw new ArgumentException("grid validation needs the full wire values");
        for (var wire = 0; wire < values.Length; wire++)
        {
            if (circuit.Grids[wire] is not { } grid) continue;
            double value = values[wire];
            // A division by a power of two is exact in float64 for these magnitudes.
            if (!(double.IsFinite(value) && double.IsInteger(value / grid)))
            {
                throw new InvalidOperationException($"wire {wire} must be a multiple of {Fp32.Format(grid)}, got {Fp32.Format(value)}");
            }
        }
    }

    /// <summary>
    /// Check that no evaluated wire is NaN, and name the operation that produced the first NaN.
    /// Range inference does not follow NaN, so the builder cannot refuse what produces one. For
    /// example, both sides of a select always compute, and an overflowing side that the gate does
    /// not pick still gives f(Infinity × 0) = NaN. The values hold every wire. With roots, only the
    /// roots and the wires that they read, directly or through other operations, are checked. For
    /// tests and error messages: the evaluators do not do this check.
    /// </summary>
    public static void ValidateNoNaN(this Circuit circuit, float[] values, IReadOnlyList<Wire>? roots = null)
    {
        if (circuit.DescribeFirstNaN(values, roots) is { } firstNaN) throw new InvalidOperationException(firstNaN);
    }

    /// <summary>
    /// The message of ValidateNoNaN, or null when no checked wire is NaN, for example for a caller
    /// that adds the message to its own error.
    /// </summary>
    public static string? DescribeFirstNaN(this Circuit circuit, float[] values, IReadOnlyList<Wire>? roots = null)
    {
        if (values.Length != circuit.Width) throw new ArgumentException("NaN validation needs the full wire values");
        var sourceSize = circuit.SourceSize;
        var operations = circuit.Operations;
        var checkedWires = roots is null ? null : MarkWiresRead(circuit, roots);
        for (var wire = 0; wire < sourceSize; wire++)
        {
            if (float.IsNaN(values[wire]) && (checkedWires is null || checkedWires[wire])) return $"source cell {wire} is NaN";
        }
        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            var wire = sourceSize + operationIndex;
            if (!float.IsNaN(values[wire]) || (checkedWires is not null && !checkedWires[wire])) continue;
            // Operations read only earlier wires, so the first NaN comes from inputs that are not NaN.
            var operation = operations[operationIndex];
            string DescribeTerms(ImmutableArray<Term> terms) =>
                string.Join(", ", terms.Select(term => $"wire {term.Wire} = {Fp32.Format(values[term.Wire.Position])}"));
            var description = operation.Kind switch
            {
                OperationKind.Reglu =>
                    $"f(left × max(0, gate)) with left = {Fp32.Format(Circuit.EvaluateTerms(values, operation.LeftTerms, operation.LeftBias))} "
                    + $"and gate = {Fp32.Format(Circuit.EvaluateTerms(values, operation.GateTerms, operation.GateBias))}",
                OperationKind.Index => $"an index read whose index form is NaN, from {DescribeTerms(operation.GateTerms)}",
                _ => $"a sum of {DescribeTerms(operation.LeftTerms)}",
            };
            var context = new StringBuilder();
            // The multiply and select that contain the operation, for example the product of the side
            // of a select that the gate does not pick. A multiply or select contains the operation when
            // its result reads the operation and none of its operands does. So a NaN operand, for
            // example a sum of Infinity and -Infinity on the side that the gate picks, gets no context.
            var reading = MarkWiresReading(circuit, new Wire(wire));
            for (var reader = operationIndex; reader < operations.Count; reader++)
            {
                var macro = operations[reader].Macro;
                if (macro is null || !reading[sourceSize + reader]) continue;
                Wire[] operands = macro switch
                {
                    MultiplyMacro multiply => [multiply.Left, multiply.Right],
                    SelectMacro select => [select.Gate, select.Off, select.On],
                    _ => throw new InvalidOperationException($"unknown macro record {macro.GetType().Name}"),
                };
                if (operands.Any(operand => reading[operand.Position])) continue;
                if (macro is MultiplyMacro product)
                {
                    context.Append($", in multiply(wire {product.Left} = {Fp32.Format(values[product.Left.Position])}, ")
                        .Append($"wire {product.Right} = {Fp32.Format(values[product.Right.Position])})");
                    reading = MarkWiresReading(circuit, new Wire(sourceSize + reader));
                }
                else
                {
                    var selection = (SelectMacro)macro;
                    context.Append($", in select(gate wire {selection.Gate} = {Fp32.Format(values[selection.Gate.Position])}, ")
                        .Append($"off wire {selection.Off} = {Fp32.Format(values[selection.Off.Position])}, ")
                        .Append($"on wire {selection.On} = {Fp32.Format(values[selection.On.Position])}). ")
                        .Append("Both sides of a select always compute, so a side the gate doesn't pick must stay finite");
                    break;
                }
            }
            return $"wire {wire} is the first NaN: {description}{context}";
        }
        return null;
    }

    // Mark the roots, and every wire that they read directly or through other operations.
    private static bool[] MarkWiresRead(Circuit circuit, IReadOnlyList<Wire> roots)
    {
        var read = new bool[circuit.Width];
        foreach (var root in roots)
        {
            AssertWire(root, read.Length);
            read[root.Position] = true;
        }
        for (var operationIndex = circuit.Operations.Count - 1; operationIndex >= 0; operationIndex--)
        {
            if (!read[circuit.SourceSize + operationIndex]) continue;
            ForEachInput(circuit.Operations[operationIndex], input => read[input.Position] = true);
        }
        return read;
    }

    // Mark the wire, and every operation that reads it directly or through other operations.
    private static bool[] MarkWiresReading(Circuit circuit, Wire wire)
    {
        var reading = new bool[circuit.Width];
        reading[wire.Position] = true;
        for (var operationIndex = Math.Max(0, wire.Position - circuit.SourceSize + 1); operationIndex < circuit.Operations.Count; operationIndex++)
        {
            var readerWire = circuit.SourceSize + operationIndex;
            ForEachInput(circuit.Operations[operationIndex], input =>
            {
                if (reading[input.Position]) reading[readerWire] = true;
            });
        }
        return reading;
    }

    // Visit every wire that an operation reads: its terms, and the candidates of an index read.
    private static void ForEachInput(Operation operation, Action<Wire> visit)
    {
        foreach (var term in operation.LeftTerms) visit(term.Wire);
        foreach (var term in operation.GateTerms) visit(term.Wire);
        if (operation.IndexRead is { } read)
        {
            foreach (var candidate in read.Table.Candidates) visit(candidate);
        }
    }

    /// <summary>
    /// Refuse a value that is missing, not finite, or below -tolerance, and return the value. The
    /// label names the value in the message, for example "delay".
    /// </summary>
    public static double AssertNonNegativeFinite([NotNull] double? value, string label, double tolerance = 0)
    {
        if (!(value is { } number && double.IsFinite(number) && number >= -tolerance))
        {
            throw new ArgumentException($"{label} must be non-negative and finite, got {FormatOptional(value)}");
        }
        return number;
    }

    /// <summary>Refuse a value that is missing, not finite, or outside -tolerance..1 + tolerance, and return the value.</summary>
    public static double AssertUnitInterval([NotNull] double? value, string label, double tolerance = 1e-9)
    {
        if (!(value is { } number && double.IsFinite(number) && number >= -tolerance && number <= 1 + tolerance))
        {
            throw new ArgumentException($"{label} must be within zero and one, got {FormatOptional(value)}");
        }
        return number;
    }

    /// <summary>Refuse values that hold a NaN or an infinity. The label names the values in the message.</summary>
    public static void AssertFiniteValues(ReadOnlySpan<float> values, string label)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value)) throw new ArgumentException($"{label} contains a non-finite number");
        }
    }

    /// <summary>Refuse values that hold a NaN or an infinity. The label names the values in the message.</summary>
    public static void AssertFiniteValues(ReadOnlySpan<double> values, string label)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value)) throw new ArgumentException($"{label} contains a non-finite number");
        }
    }

    /// <summary>Refuse a view of output wires that does not have the given size, or that holds a wire outside the circuit.</summary>
    public static void ValidateWireView(IReadOnlyList<Wire> wires, int size, int wireCount)
    {
        if (wires.Count != size) throw new ArgumentException("output wire view has the wrong length");
        foreach (var wire in wires) AssertWire(wire, wireCount);
    }

    /// <summary>Throw an InvalidOperationException with the message when the condition is false.</summary>
    public static void Invariant([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // JavaScript writes a missing value as "undefined".
    private static string FormatOptional(double? value) => value is { } number ? Fp32.Format(number) : "undefined";

    private static void ValidateTerms(IEnumerable<Term> terms, int wireLimit)
    {
        foreach (var (wire, coefficient) in terms)
        {
            AssertWire(wire, wireLimit);
            if (!double.IsFinite(coefficient)) throw new InvalidOperationException("weight must be finite");
        }
    }
}
