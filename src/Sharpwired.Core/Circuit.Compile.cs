// Handwired source: src/runtime/numeric-ui-core.ts (Circuit.compileTensorProgram,
// findLiveOperations, computeCompiledDepths, packCandidateTable).

using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    /// <summary>
    /// Compile the operations that the roots read, directly or through other operations, into
    /// layers of sparse matrices. With no roots, every operation is a root. With lowerGathers, a
    /// masked sum over a declared one-hot group becomes one gather read of the selected candidate.
    /// Its products leave the program when nothing else reads them. The layer of an operation is
    /// its depth minus 1. Without recomputeDepths, the depth is the build-time depth. With
    /// recomputeDepths, the depth is one
    /// more than the deepest value that the operation reads in the compiled program. In a layer, the
    /// rows follow the operation order and the input columns follow the packed positions.
    /// </summary>
    public TensorProgram CompileTensorProgram(IReadOnlyList<Wire>? roots = null, bool lowerGathers = true, bool recomputeDepths = false)
    {
        var width = Width;
        Wire[] normalizedRoots = roots is null
            ? [.. Enumerable.Range(SourceSize, OperationList.Count).Select(position => new Wire(position))]
            : [.. roots];
        var gatherPlans = lowerGathers ? PlanGatherLowering(this) : [];
        var liveOperations = FindLiveOperations(normalizedRoots, SourceSize, OperationList, width, gatherPlans, OneHotGroupList);
        var compiledDepths = recomputeDepths ? ComputeCompiledDepths(liveOperations, gatherPlans) : null;
        var operationsByLayer = new List<List<int>?>();
        for (var operationIndex = 0; operationIndex < OperationList.Count; operationIndex++)
        {
            if (!liveOperations[operationIndex]) continue;
            var depth = compiledDepths is null ? OperationList[operationIndex].Depth : compiledDepths[operationIndex];
            var layerIndex = depth - 1;
            while (operationsByLayer.Count <= layerIndex) operationsByLayer.Add(null);
            (operationsByLayer[layerIndex] ??= []).Add(operationIndex);
        }

        var layers = ImmutableArray.CreateBuilder<TensorLayer>();
        var liveOperationCount = liveOperations.Count(live => live);
        var valueCount = SourceSize + liveOperationCount;
        var wireOrder = new int[valueCount];
        var packedIndexByWire = new int[width];
        Array.Fill(packedIndexByWire, -1);
        for (var wire = 0; wire < SourceSize; wire++)
        {
            wireOrder[wire] = wire;
            packedIndexByWire[wire] = wire;
        }
        var availableValues = SourceSize;
        // The column of each wire in the layer that the loop compiles, or -1 when the layer does not
        // read the wire. Every layer uses this one array and resets its own entries at the end. So
        // the compiler does not allocate the width of the circuit for each layer.
        var inputColumnByWire = new int[width];
        Array.Fill(inputColumnByWire, -1);
        // The packed form of each candidate table, keyed by the table instance, so that each read of
        // a table stores the same positions instance.
        var packedTables = new Dictionary<CandidateTable, PackedCandidateTable>(ReferenceEqualityComparer.Instance);
        // The slot of each group, given at the first read of the group. The slot numbers count up
        // from 0, so the count of the dictionary is the next slot.
        var slotByGroup = new Dictionary<int, int>();
        for (var layerIndex = 0; layerIndex < operationsByLayer.Count; layerIndex++)
        {
            var operationIndices = operationsByLayer[layerIndex];
            if (operationIndices is null) continue;
            var outputSize = operationIndices.Count;
            // The input wires in the order of their first read, as a JavaScript Set iterates.
            var inputWireSet = new HashSet<int>();
            var inputWires = new List<int>();
            foreach (var operationIndex in operationIndices)
            {
                // A gather row reads the packed values directly by position, so it adds no input
                // column to the layer.
                if (gatherPlans.ContainsKey(operationIndex)) continue;
                var operation = OperationList[operationIndex];
                foreach (var term in operation.LeftTerms)
                {
                    if (inputWireSet.Add(term.Wire.Position)) inputWires.Add(term.Wire.Position);
                }
                foreach (var term in operation.GateTerms)
                {
                    if (inputWireSet.Add(term.Wire.Position)) inputWires.Add(term.Wire.Position);
                }
            }
            // OrderBy is a stable sort, as the JavaScript sort is.
            int[] orderedInputWires = [.. inputWires.OrderBy(wire => packedIndexByWire[wire])];
            var inputSize = orderedInputWires.Length;
            var gateCount = 0;
            var indexReadCount = 0;
            foreach (var operationIndex in operationIndices)
            {
                var kind = OperationList[operationIndex].Kind;
                if (kind == OperationKind.Reglu) gateCount++;
                else if (kind == OperationKind.Index) indexReadCount++;
            }
            var inputIndices = UnsignedIndices.From([.. orderedInputWires.Select(wire => packedIndexByWire[wire])]);
            for (var column = 0; column < inputSize; column++) inputColumnByWire[orderedInputWires[column]] = column;
            // The projection rows: one for each output, then one gate row for each ReGLU, then one
            // index row for each index read.
            var projectionSize = outputSize + gateCount + indexReadCount;
            var bias = new float[projectionSize];
            var gateRows = new List<int>();
            var booleanRows = new List<int>();
            var projectionTerms = new ImmutableArray<Term>[projectionSize];
            var pendingGatherReads = new List<(int Row, GatherPlan Plan)>();
            var pendingIndexReads = new List<(int Row, int IndexRow, IndexReadInstruction Read)>();
            var gateRow = 0;
            var indexReadRow = 0;
            for (var row = 0; row < operationIndices.Count; row++)
            {
                var operationIndex = operationIndices[row];
                var operation = OperationList[operationIndex];
                var outputWire = SourceSize + operationIndex;
                var packedOutput = availableValues + row;
                wireOrder[packedOutput] = outputWire;
                packedIndexByWire[outputWire] = packedOutput;
                // A Float32Array store rounds the float64 bias to FP32 one time.
                bias[row] = (float)operation.LeftBias;
                if (gatherPlans.TryGetValue(operationIndex, out var plan))
                {
                    // The lowered sum keeps its bias in the sparse row and stores no weights. The
                    // executor adds the selected value to the bias.
                    projectionTerms[row] = [];
                    pendingGatherReads.Add((row, plan));
                    continue;
                }
                projectionTerms[row] = operation.LeftTerms;
                if (operation.Kind == OperationKind.Reglu)
                {
                    var projectionRow = outputSize + gateRow;
                    bias[projectionRow] = (float)operation.GateBias;
                    gateRows.Add(row);
                    projectionTerms[projectionRow] = operation.GateTerms;
                    gateRow++;
                }
                else if (operation.Kind == OperationKind.Boolean)
                {
                    booleanRows.Add(row);
                }
                else if (operation.Kind == OperationKind.Index)
                {
                    // The output row keeps the bias 0 and no weights. The executor copies the
                    // selected candidate into it.
                    var indexRow = outputSize + gateCount + indexReadRow;
                    bias[indexRow] = (float)operation.GateBias;
                    projectionTerms[indexRow] = operation.GateTerms;
                    pendingIndexReads.Add((row, indexRow, operation.IndexRead!));
                    indexReadRow++;
                }
            }
            var gatherComputes = ImmutableArray.CreateBuilder<TensorGatherCompute>();
            var gatherReads = ImmutableArray.CreateBuilder<TensorGatherRead>(pendingGatherReads.Count);
            foreach (var (row, plan) in pendingGatherReads)
            {
                var group = OneHotGroupList[plan.GroupIndex];
                if (!slotByGroup.TryGetValue(plan.GroupIndex, out var slot))
                {
                    // The first read of the group: resolve its index here, one time, before the reads
                    // of the layer run. The bits and the candidates are at earlier depths than the
                    // sum that they fed, so they are packed.
                    slot = slotByGroup.Count;
                    slotByGroup[plan.GroupIndex] = slot;
                    var bitPositions = new int[group.Members.Length];
                    for (var index = 0; index < bitPositions.Length; index++)
                    {
                        var position = packedIndexByWire[group.Members[index].Position];
                        Validation.Invariant(position >= 0 && position < availableValues, "gather bit is not available");
                        bitPositions[index] = position;
                    }
                    gatherComputes.Add(new TensorGatherCompute(slot, UnsignedIndices.From(bitPositions)));
                }
                var candidatePositions = new int[plan.CandidateWires.Length];
                for (var index = 0; index < candidatePositions.Length; index++)
                {
                    var position = packedIndexByWire[plan.CandidateWires[index].Position];
                    Validation.Invariant(position >= 0 && position < availableValues, "gather candidate is not available");
                    candidatePositions[index] = position;
                }
                gatherReads.Add(new TensorGatherRead(row, slot, group.AllowAllZero, UnsignedIndices.From(candidatePositions)));
            }
            var indexReads = ImmutableArray.CreateBuilder<TensorIndexRead>(pendingIndexReads.Count);
            foreach (var (row, indexRow, read) in pendingIndexReads)
            {
                if (!packedTables.TryGetValue(read.Table, out var packed))
                {
                    // The candidates are at earlier depths than each read of their table, so the
                    // first read finds them packed.
                    packed = PackCandidateTable(read.Table, packedIndexByWire, availableValues);
                    packedTables[read.Table] = packed;
                }
                indexReads.Add(new TensorIndexRead(
                    row,
                    indexRow,
                    read.Below == OutOfRange.Zero,
                    read.Above == OutOfRange.Zero,
                    packed.CandidateCount,
                    packed.CandidateBase,
                    packed.CandidateStride,
                    packed.CandidatePositions));
            }
            var rowOffsets = new int[projectionSize + 1];
            for (var row = 0; row < projectionSize; row++) rowOffsets[row + 1] = rowOffsets[row] + projectionTerms[row].Length;
            var storedWeightCount = rowOffsets[projectionSize];
            var columns = new int[storedWeightCount];
            var weights = new float[storedWeightCount];
            var storedWeight = 0;
            foreach (var terms in projectionTerms)
            {
                foreach (var (wire, coefficient) in terms)
                {
                    var column = inputColumnByWire[wire.Position];
                    Validation.Invariant(column >= 0, "tensor dependency is not available");
                    columns[storedWeight] = column;
                    // A Float32Array store rounds the float64 coefficient to FP32 one time.
                    weights[storedWeight] = (float)coefficient;
                    storedWeight++;
                }
            }
            for (var column = 0; column < inputSize; column++) inputColumnByWire[orderedInputWires[column]] = -1;
            layers.Add(new TensorLayer
            {
                InputIndices = inputIndices,
                InputSize = inputSize,
                OutputSize = outputSize,
                Bias = ImmutableCollectionsMarshal.AsImmutableArray(bias),
                GateRows = UnsignedIndices.From(CollectionsMarshal.AsSpan(gateRows)),
                BooleanRows = UnsignedIndices.From(CollectionsMarshal.AsSpan(booleanRows)),
                RowOffsets = UnsignedIndices.From(rowOffsets),
                Columns = UnsignedIndices.From(columns),
                Weights = ImmutableCollectionsMarshal.AsImmutableArray(weights),
                GatherComputes = gatherComputes.ToImmutable(),
                GatherReads = gatherReads.MoveToImmutable(),
                IndexReads = indexReads.MoveToImmutable(),
            });
            availableValues += outputSize;
        }
        return new TensorProgram
        {
            Layers = layers.ToImmutable(),
            Roots = UnsignedIndices.From([.. normalizedRoots.Select(root => root.Position)]),
            SourceSize = SourceSize,
            ValueCount = valueCount,
            WireOrder = UnsignedIndices.From(wireOrder),
            Width = width,
            GatherSlotCount = slotByGroup.Count,
        };
    }

    // Mark each operation that a root reads, directly or through other operations. The search pops
    // a stack, and adds the candidates of a table one time, also when many reads share the table.
    internal static bool[] FindLiveOperations(
        IReadOnlyList<Wire> roots,
        int sourceSize,
        IReadOnlyList<Operation> operations,
        int width,
        IReadOnlyDictionary<int, GatherPlan> gatherPlans,
        IReadOnlyList<OneHotGroup> oneHotGroups)
    {
        var liveOperations = new bool[operations.Count];
        var pending = new List<Wire>(roots);
        var visitedTables = new HashSet<CandidateTable>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            var wire = pending[^1];
            pending.RemoveAt(pending.Count - 1);
            Validation.AssertWire(wire, width);
            if (wire.Position < sourceSize) continue;
            var operationIndex = wire.Position - sourceSize;
            if (liveOperations[operationIndex]) continue;
            liveOperations[operationIndex] = true;
            if (gatherPlans.TryGetValue(operationIndex, out var plan))
            {
                // A lowered sum does not read its product terms. It reads the bits of the group (for
                // the index) and each candidate directly. A product that nothing else reads leaves
                // the program.
                foreach (var member in oneHotGroups[plan.GroupIndex].Members) pending.Add(member);
                foreach (var candidate in plan.CandidateWires) pending.Add(candidate);
                continue;
            }
            var operation = operations[operationIndex];
            foreach (var term in operation.LeftTerms) pending.Add(term.Wire);
            foreach (var term in operation.GateTerms) pending.Add(term.Wire);
            if (operation.IndexRead is { } read && visitedTables.Add(read.Table))
            {
                foreach (var candidate in read.Table.Candidates) pending.Add(candidate);
            }
        }
        return liveOperations;
    }

    // The depth of each live operation from the values that it reads in the compiled program. A
    // lowered masked sum reads the bits of its group and its candidates, not its products. An index
    // read reads its index form and its candidates. An operation comes after each value that it
    // reads, so one pass in operation order is enough. Without gather plans, this gives the
    // build-time depths.
    internal int[] ComputeCompiledDepths(bool[] liveOperations, IReadOnlyDictionary<int, GatherPlan> gatherPlans)
    {
        var depths = new int[OperationList.Count];
        int DepthOf(Wire wire) => wire.Position < SourceSize ? 0 : depths[wire.Position - SourceSize];
        var tableDepths = new Dictionary<CandidateTable, int>(ReferenceEqualityComparer.Instance);
        for (var operationIndex = 0; operationIndex < OperationList.Count; operationIndex++)
        {
            if (!liveOperations[operationIndex]) continue;
            var depth = 0;
            if (gatherPlans.TryGetValue(operationIndex, out var plan))
            {
                foreach (var member in OneHotGroupList[plan.GroupIndex].Members) depth = Math.Max(depth, DepthOf(member));
                foreach (var candidate in plan.CandidateWires) depth = Math.Max(depth, DepthOf(candidate));
                depths[operationIndex] = depth + 1;
                continue;
            }
            var operation = OperationList[operationIndex];
            foreach (var term in operation.LeftTerms) depth = Math.Max(depth, DepthOf(term.Wire));
            foreach (var term in operation.GateTerms) depth = Math.Max(depth, DepthOf(term.Wire));
            if (operation.IndexRead is { } read)
            {
                // Each candidate comes before the first read of the table, so its depth is final
                // then. Later reads can use the depth of the table again.
                if (!tableDepths.TryGetValue(read.Table, out var tableDepth))
                {
                    tableDepth = 0;
                    foreach (var candidate in read.Table.Candidates) tableDepth = Math.Max(tableDepth, DepthOf(candidate));
                    tableDepths[read.Table] = tableDepth;
                }
                depth = Math.Max(depth, tableDepth);
            }
            depths[operationIndex] = depth + 1;
        }
        return depths;
    }

    // Store a candidate table as a base position and a stride when the candidates are at evenly
    // spaced packed positions. Examples are a run of source cells, a reversed run, or one repeated
    // wire. Otherwise store one position for each candidate.
    internal static PackedCandidateTable PackCandidateTable(CandidateTable table, int[] packedIndexByWire, int availableValues)
    {
        var positions = new int[table.Candidates.Length];
        for (var index = 0; index < positions.Length; index++)
        {
            var position = packedIndexByWire[table.Candidates[index].Position];
            Validation.Invariant(position >= 0 && position < availableValues, "index read candidate is not available");
            positions[index] = position;
        }
        var first = positions[0];
        var stride = positions.Length > 1 ? positions[1] - first : 0;
        var evenlySpaced = true;
        for (var index = 2; index < positions.Length; index++)
        {
            // long, because index × stride can pass int.MaxValue, and Handwired computes in float64.
            if (positions[index] != first + (long)index * stride)
            {
                evenlySpaced = false;
                break;
            }
        }
        return new PackedCandidateTable(
            positions.Length,
            first,
            stride,
            evenlySpaced ? UnsignedIndices.Empty : UnsignedIndices.From(positions));
    }
}
