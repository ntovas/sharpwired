// Handwired source: src/runtime/numeric-ui-core.ts (inlineCircuit).

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    /// <summary>
    /// Append the operations of the source circuit to this circuit, with the source cells of the
    /// source circuit read from sourceWires. Returns the wire in this circuit of each wire of the
    /// source circuit: first the source wires, then one wire for each operation. The builder rules
    /// apply, so an operation that this circuit already holds gives its existing wire.
    /// </summary>
    /// <remarks>
    /// The declarations move to the mapped wires: the ranges, grids and exact bits of the source
    /// cells, the range and grid of each operation, the one-hot groups, the rounding points and the
    /// comparison bits. A multiply or select record builds again through Multiply or Select, and an
    /// index read keeps the index range that the source circuit accepted. allowExistingReaders
    /// applies to the declared ranges and grids that land on wires that operations of this circuit
    /// already read, for example a timestep that springs read before the source circuit declares it
    /// nonnegative.
    /// </remarks>
    public Wire[] InlineCircuit(Circuit source, IReadOnlyList<Wire> sourceWires, bool allowExistingReaders = false)
    {
        if (sourceWires.Count != source.SourceSize) throw new ArgumentException("inlined source mapping has the wrong length");
        var wireMap = new List<Wire>(source.Width);
        wireMap.AddRange(sourceWires);
        for (var wire = 0; wire < source.SourceSize; wire++)
        {
            var range = source.RangeList[wire];
            AssumeRange(sourceWires[wire], range.Minimum, range.Maximum, allowExistingReaders);
            if (source.GridList[wire] is { } grid) AssumeGrid(sourceWires[wire], grid, allowExistingReaders);
            if (source.ExactBitList[wire]) AssumeBit(sourceWires[wire], allowExistingReaders);
        }
        // Map each distinct table one time, so that reads of one large table share one mapped list.
        var mappedTables = new Dictionary<CandidateTable, Wire[]>(ReferenceEqualityComparer.Instance);
        Term[] MapTerms(IEnumerable<Term> terms) => [.. terms.Select(term => new Term(wireMap[term.Wire.Position], term.Coefficient))];
        for (var operationIndex = 0; operationIndex < source.OperationList.Count; operationIndex++)
        {
            var operation = source.OperationList[operationIndex];
            Wire wire;
            if (operation.Macro is MultiplyMacro multiply)
            {
                wire = Multiply(wireMap[multiply.Left.Position], wireMap[multiply.Right.Position]);
            }
            else if (operation.Macro is SelectMacro select)
            {
                wire = Select(wireMap[select.Gate.Position], wireMap[select.Off.Position], wireMap[select.On.Position]);
            }
            else if (operation.Kind == OperationKind.Reglu)
            {
                wire = Append(
                    OperationKind.Reglu,
                    MapTerms(operation.LeftTerms),
                    operation.LeftBias,
                    MapTerms(operation.GateTerms),
                    operation.GateBias,
                    operation.PreserveRounding);
            }
            else if (operation.Kind == OperationKind.Index)
            {
                var read = operation.IndexRead!;
                if (!mappedTables.TryGetValue(read.Table, out var mappedCandidates))
                {
                    mappedCandidates = [.. read.Table.Candidates.Select(candidate => wireMap[candidate.Position])];
                    mappedTables[read.Table] = mappedCandidates;
                }
                // The accepted index range carries over, for example a range declared on a small affine
                // index that this circuit infers again from undeclared cells.
                wire = IndexReadAffine(MapTerms(operation.GateTerms), operation.GateBias, mappedCandidates, read.Below, read.Above, read.IndexRange);
            }
            else if (operation.Kind == OperationKind.Boolean && operation.PreserveRounding)
            {
                // The atom of Compare on grid operands reads its score terms directly.
                wire = Append(OperationKind.Boolean, MapTerms(operation.LeftTerms), operation.LeftBias, [], 0, preserveRounding: true);
            }
            else
            {
                var value = operation.PreserveRounding
                    ? RoundAffine(MapTerms(operation.LeftTerms), operation.LeftBias)
                    : Affine(MapTerms(operation.LeftTerms), operation.LeftBias);
                wire = operation.Kind == OperationKind.Boolean ? Boolean(value) : value;
            }
            var sourceWire = source.SourceSize + operationIndex;
            var operationRange = source.RangeList[sourceWire];
            AssumeRange(wire, operationRange.Minimum, operationRange.Maximum, allowExistingReaders);
            if (source.GridList[sourceWire] is { } operationGrid) AssumeGrid(wire, operationGrid, allowExistingReaders);
            // A marked affine can fold into a source cell here, for example when two of its terms map
            // to one wire, and a source cell has no operation to record.
            if (operation.RoundingPoint is { } kind && wire.Position >= SourceSize) RecordRoundingPoint(wire, kind);
            // The mapped operation computes the same values from the mapped operands, so it still holds
            // exactly 0 or 1.
            if (operation.ComparisonBit && wire.Position >= SourceSize)
            {
                var targetIndex = wire.Position - SourceSize;
                OperationList[targetIndex] = OperationList[targetIndex] with { ComparisonBit = true };
            }
            wireMap.Add(wire);
        }
        // Declared one-hot groups survive the inlining: the property is about runtime values, and
        // the mapped wires carry the same values.
        foreach (var group in source.OneHotGroupList)
        {
            AssumeOneHot([.. group.Members.Select(member => wireMap[member.Position])], group.AllowAllZero, allowExistingReaders);
        }
        return [.. wireMap];
    }
}
