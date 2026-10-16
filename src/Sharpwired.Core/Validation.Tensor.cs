// Handwired source: src/runtime/numeric-ui-core.ts (validateTensorProgram, assertFiniteValues,
// validateTensorEvaluation).

namespace Sharpwired.Core;

public static partial class Validation
{
    /// <summary>
    /// Check that a compiled program fits the circuit: the sizes match, each layer reads only the
    /// values that earlier layers wrote, each row and column is inside its layer, each gather slot is
    /// resolved exactly once before its reads, each gather read and index read can reach its
    /// candidates, each constant is finite, each live wire is written exactly once, and each root is
    /// present.
    /// </summary>
    /// <remarks>
    /// A program record can hold values that a JavaScript program object cannot have the same way,
    /// for example a negative row or a wire order shorter than the layers write. Where Handwired
    /// reads undefined and the next check fails, this port checks the bounds first and throws the
    /// message of that next check.
    /// </remarks>
    public static void ValidateTensorProgram(this Circuit circuit, TensorProgram program)
    {
        var layers = program.Layers;
        var roots = program.Roots;
        var valueCount = program.ValueCount;
        var wireOrder = program.WireOrder;
        var width = program.Width;
        Invariant(program.SourceSize == circuit.SourceSize, "tensor source size mismatch");
        Invariant(width == circuit.SourceSize + circuit.Operations.Count, "tensor width mismatch");
        Invariant(valueCount <= width, "tensor value count exceeds circuit width");
        Invariant(wireOrder.Count == valueCount, "tensor wire order size mismatch");
        var seenWires = new bool[width];
        for (var wire = 0; wire < circuit.SourceSize; wire++)
        {
            Invariant(wire < wireOrder.Count && wireOrder[wire] == wire, "tensor source order mismatch");
            seenWires[wire] = true;
        }
        // Handwired new Uint8Array(gatherSlotCount) throws a RangeError for a negative count.
        Invariant(program.GatherSlotCount >= 0, "gather slot count must be a nonnegative integer");
        var resolvedGatherSlots = new bool[program.GatherSlotCount];
        // The explicit candidate position arrays already checked, so that reads that share one
        // table are checked one time.
        var checkedCandidatePositions = new HashSet<UnsignedIndices>(ReferenceEqualityComparer.Instance);
        var availableValues = circuit.SourceSize;
        foreach (var layer in layers)
        {
            var outputSize = layer.OutputSize;
            Invariant(outputSize > 0, "tensor layer must produce at least one value");
            Invariant(layer.InputIndices.Count == layer.InputSize, "tensor input index size mismatch");
            foreach (var inputIndex in layer.InputIndices)
            {
                Invariant(inputIndex < availableValues, "tensor layer input is not available");
            }
            var gateSize = layer.GateRows.Count;
            var projectionSize = outputSize + gateSize + layer.IndexReads.Length;
            Invariant(layer.Bias.Length == projectionSize, "projection bias shape mismatch");
            var seenGateRows = new bool[outputSize];
            foreach (var gateRow in layer.GateRows)
            {
                Invariant(gateRow < outputSize, "gate output row is invalid");
                Invariant(!seenGateRows[gateRow], "gate output row must be unique");
                seenGateRows[gateRow] = true;
            }
            var seenBooleanRows = new bool[outputSize];
            foreach (var booleanRow in layer.BooleanRows)
            {
                Invariant(booleanRow < outputSize, "boolean output row is invalid");
                Invariant(!seenGateRows[booleanRow], "boolean output row cannot also be gated");
                Invariant(!seenBooleanRows[booleanRow], "boolean output row must be unique");
                seenBooleanRows[booleanRow] = true;
            }
            var rowOffsets = layer.RowOffsets;
            Invariant(rowOffsets.Count == projectionSize + 1, "sparse row offset shape mismatch");
            Invariant(rowOffsets[0] == 0, "sparse row offsets must start at zero");
            Invariant(layer.Columns.Count == layer.Weights.Length, "sparse column shape mismatch");
            Invariant(rowOffsets[projectionSize] == layer.Weights.Length, "sparse row offsets must end at the stored weight count");
            for (var row = 0; row < projectionSize; row++)
            {
                Invariant(rowOffsets[row] <= rowOffsets[row + 1], "sparse row offsets must be ordered");
            }
            foreach (var column in layer.Columns)
            {
                Invariant(column < layer.InputSize, "sparse matrix column is invalid");
            }
            foreach (var compute in layer.GatherComputes)
            {
                Invariant(compute.Slot >= 0 && compute.Slot < program.GatherSlotCount, "gather slot is invalid");
                Invariant(!resolvedGatherSlots[compute.Slot], "gather slot must be resolved exactly once");
                resolvedGatherSlots[compute.Slot] = true;
                foreach (var bitPosition in compute.BitPositions)
                {
                    Invariant(bitPosition < availableValues, "gather bit is not available");
                }
            }
            foreach (var read in layer.GatherReads)
            {
                Invariant(read.Row >= 0 && read.Row < outputSize, "gather output row is invalid");
                Invariant(!seenGateRows[read.Row], "gather output row cannot be gated");
                Invariant(!seenBooleanRows[read.Row], "gather output row cannot be boolean");
                Invariant(rowOffsets[read.Row] == rowOffsets[read.Row + 1], "gather output row must store no weights");
                Invariant(
                    read.Slot >= 0 && read.Slot < program.GatherSlotCount && resolvedGatherSlots[read.Slot],
                    "gather read must follow its slot resolution");
                Invariant(read.CandidatePositions.Count > 0, "gather read needs at least one candidate");
                foreach (var candidatePosition in read.CandidatePositions)
                {
                    Invariant(candidatePosition < availableValues, "gather candidate is not available");
                }
            }
            var seenReadRows = new bool[outputSize];
            foreach (var read in layer.GatherReads) seenReadRows[read.Row] = true;
            var seenIndexRows = new bool[projectionSize];
            foreach (var read in layer.IndexReads)
            {
                Invariant(read.Row >= 0 && read.Row < outputSize, "index read output row is invalid");
                Invariant(!seenGateRows[read.Row], "index read output row cannot be gated");
                Invariant(!seenBooleanRows[read.Row], "index read output row cannot be boolean");
                Invariant(!seenReadRows[read.Row], "index read output row must be unique");
                seenReadRows[read.Row] = true;
                Invariant(
                    rowOffsets[read.Row] == rowOffsets[read.Row + 1] && layer.Bias[read.Row] == 0,
                    "index read output row must store no weights and no bias");
                Invariant(read.IndexRow >= outputSize + gateSize && read.IndexRow < projectionSize, "index row must follow the gate rows");
                Invariant(!seenIndexRows[read.IndexRow], "index row must be unique");
                seenIndexRows[read.IndexRow] = true;
                Invariant(rowOffsets[read.IndexRow] < rowOffsets[read.IndexRow + 1], "index row must read a runtime index");
                Invariant(read.CandidateCount > 0, "index read needs at least one candidate");
                if (read.CandidatePositions.Count == 0)
                {
                    // Evenly spaced positions are between the first and the last, so when both ends
                    // are available, each candidate is available. long, because Handwired computes
                    // the last position in float64.
                    var lastPosition = read.CandidateBase + (long)(read.CandidateCount - 1) * read.CandidateStride;
                    Invariant(
                        read.CandidateBase >= 0
                            && read.CandidateBase < availableValues
                            && lastPosition >= 0
                            && lastPosition < availableValues,
                        "evenly spaced index read candidates are not available");
                }
                else
                {
                    Invariant(read.CandidatePositions.Count == read.CandidateCount, "index read candidate table size mismatch");
                    if (checkedCandidatePositions.Add(read.CandidatePositions))
                    {
                        foreach (var candidatePosition in read.CandidatePositions)
                        {
                            Invariant(candidatePosition < availableValues, "index read candidate is not available");
                        }
                    }
                }
            }
            AssertFiniteProgramValues(layer.Bias.AsSpan(), "projection bias");
            AssertFiniteProgramValues(layer.Weights.AsSpan(), "projection matrix");
            for (var row = 0; row < outputSize; row++)
            {
                var packed = availableValues + row;
                var wire = packed < wireOrder.Count ? wireOrder[packed] : -1;
                Invariant(wire >= circuit.SourceSize && wire < width, "tensor output wire is invalid");
                Invariant(!seenWires[wire], "tensor wire must be written exactly once");
                seenWires[wire] = true;
            }
            availableValues += outputSize;
        }
        Invariant(availableValues == valueCount, "tensor program output size mismatch");
        foreach (var resolved in resolvedGatherSlots)
        {
            Invariant(resolved, "every gather slot must be resolved");
        }
        foreach (var root in roots)
        {
            Invariant(root < width && seenWires[root], "every tensor root must be present");
        }
    }

    /// <summary>
    /// Check that the tensor values equal the reference values on each wire of the program that
    /// EvaluateTensor runs without a program argument: <see cref="Circuit.TensorProgram"/>, or a
    /// program of every operation that this call compiles into it when it is null.
    /// </summary>
    /// <remarks>
    /// The comparison is == on float, as Handwired uses ===: +0 equals -0, and a NaN value never
    /// passes. The reference values come from the operation stream, which checks the length of the
    /// source table and not the source ranges.
    /// </remarks>
    /// <param name="circuit">The circuit.</param>
    /// <param name="source">The source table of the evaluation.</param>
    /// <param name="tensorValues">The value of each wire, for example from EvaluateTensor.</param>
    public static void ValidateTensorEvaluation(this Circuit circuit, ReadOnlySpan<float> source, float[] tensorValues)
    {
        ArgumentNullException.ThrowIfNull(tensorValues);
        var expectedValues = circuit.EvaluateOperationStream(source);
        if (expectedValues.Length != tensorValues.Length) throw new ArgumentException("tensor result width mismatch");
        var program = circuit.TensorProgram ??= circuit.CompileTensorProgram();
        foreach (var wire in program.WireOrder)
        {
            // The message is built only on failure, so a passing check allocates no string.
            if (!(tensorValues[wire] == expectedValues[wire]))
            {
                throw new InvalidOperationException($"tensor wire {wire} does not match the reference FP32 atom program");
            }
        }
    }

    // Handwired assertFiniteValues on the constants of a program. It throws InvalidOperationException,
    // as each other check of a program does.
    private static void AssertFiniteProgramValues(ReadOnlySpan<float> values, string label)
    {
        foreach (var value in values)
        {
            if (!float.IsFinite(value)) throw new InvalidOperationException($"{label} contains a non-finite number");
        }
    }
}
