// Handwired source: src/runtime/numeric-ui-core.ts (validateCircuit, validateTerms, assertWire,
// indexMagnitudeLimit).

namespace Sharpwired.Core;

/// <summary>Validators for circuits and the values that they compute.</summary>
public static class Validation
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

    private static void ValidateTerms(IEnumerable<Term> terms, int wireLimit)
    {
        foreach (var (wire, coefficient) in terms)
        {
            AssertWire(wire, wireLimit);
            if (!double.IsFinite(coefficient)) throw new InvalidOperationException("weight must be finite");
        }
    }
}
