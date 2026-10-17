// Handwired source: these test helpers. programCounts and checkEveryCompileMode of
// tests/runtime/index-read.test.ts and tests/runtime/spring-matrix.test.ts. checkTensorAgreement and
// cost of tests/runtime/comparison.test.ts. gatherStatistics and loweredWires of
// tests/runtime/gather-lowering.test.ts. The toEqual comparison of two compiled programs.

namespace Sharpwired.Core.Tests;

// The counts that the Handwired tests read from a compiled program.
internal sealed record ProgramCountsResult(int LiveOperations, int Layers, int StoredWeights, int GatherReads, int IndexReads);

// The counts of Handwired gatherStatistics.
internal sealed record GatherStatisticsResult(int ReadCount, int ComputeCount, int SlotCount, int StoredWeightCount, int GatherBytes);

// The compile modes of Handwired checkEveryCompileMode. Each Handwired test file has its own list.
internal enum CompileModes
{
    // index-read.test.ts: 5 compile modes, and createTensorEvaluator with and without recomputeDepths.
    IndexRead,

    // spring-matrix.test.ts: 4 compile modes, and createTensorEvaluator without recomputeDepths.
    SpringMatrix,
}

internal static class TensorChecks
{
    private static readonly (string Label, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] IndexReadModes =
    [
        ("gather lowering, build-time depths", (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("gather lowering, recomputed depths", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("no gather lowering, recomputed depths", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true)),
        ("every wire as a root", (circuit, _) => circuit.CompileTensorProgram()),
    ];

    private static readonly (string Label, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] SpringMatrixModes =
    [
        ("gather lowering, build-time depths", (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("recomputed depths", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("every wire as a root", (circuit, _) => circuit.CompileTensorProgram()),
    ];

    // Handwired checkEveryCompileMode. Compile the circuit in each mode, and validate each program.
    // Check that each program gives the reference value on each wire of wireOrder for each source.
    // Then check the reusable evaluators on the roots. The comparison is Object.is (Js.ObjectIs), as
    // index-read.test.ts:84 and :96 and spring-matrix.test.ts:93 and :104 compare. The helper puts
    // back the previous TensorProgram of the circuit, and returns the reference values.
    public static float[][] EveryCompileMode(
        Circuit circuit, IReadOnlyList<Wire> roots, IReadOnlyList<float[]> sources, CompileModes modes = CompileModes.IndexRead)
    {
        circuit.ValidateCircuit();
        var references = sources.Select(source => circuit.EvaluateReference(source)).ToArray();
        foreach (var (label, compile) in modes == CompileModes.IndexRead ? IndexReadModes : SpringMatrixModes)
        {
            var program = compile(circuit, roots);
            circuit.ValidateTensorProgram(program);
            var previousProgram = circuit.TensorProgram;
            circuit.TensorProgram = program;
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var tensorValues = circuit.EvaluateTensor(sources[sourceIndex]);
                foreach (var wire in program.WireOrder)
                {
                    Assert.True(
                        Js.ObjectIs(references[sourceIndex][wire], tensorValues[wire]),
                        $"{label}: wire {wire} got {tensorValues[wire]:R}, reference {references[sourceIndex][wire]:R}");
                }
            }
            circuit.TensorProgram = previousProgram;
        }
        bool[] recomputeOptions = modes == CompileModes.IndexRead ? [false, true] : [false];
        foreach (var recomputeDepths in recomputeOptions)
        {
            var evaluator = circuit.CreateTensorEvaluator(roots, recomputeDepths);
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var outputs = evaluator.Evaluate(sources[sourceIndex]);
                for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                {
                    var expected = references[sourceIndex][roots[rootIndex].Position];
                    Assert.True(
                        Js.ObjectIs(expected, outputs[rootIndex]),
                        $"reusable evaluator (recomputeDepths {recomputeDepths}): root {roots[rootIndex]} got {outputs[rootIndex]:R}, reference {expected:R}");
                }
            }
        }
        return references;
    }

    // Handwired checkTensorAgreement of comparison.test.ts. Validate the circuit. Then, with and
    // without gather lowering, compile the roots and validate the program. Check that the tensor
    // evaluator gives the reference value on each wire of wireOrder. The comparison is Object.is
    // (Js.ObjectIs), as comparison.test.ts:249 compares. At the end, TensorProgram is null.
    public static void TensorAgreement(Circuit circuit, IReadOnlyList<Wire> roots, IEnumerable<float[]> sources)
    {
        circuit.ValidateCircuit();
        var sourceList = sources.ToList();
        foreach (var lowerGathers in new[] { true, false })
        {
            var program = circuit.CompileTensorProgram(roots, lowerGathers);
            circuit.ValidateTensorProgram(program);
            circuit.TensorProgram = program;
            foreach (var source in sourceList)
            {
                var reference = circuit.EvaluateReference(source);
                var tensor = circuit.EvaluateTensor(source);
                foreach (var wire in program.WireOrder)
                {
                    Assert.True(
                        Js.ObjectIs(reference[wire], tensor[wire]),
                        $"lowerGathers {lowerGathers}: wire {wire}: tensor {tensor[wire]:R}, reference {reference[wire]:R}");
                }
            }
        }
        circuit.TensorProgram = null;
    }

    // Handwired cost of comparison.test.ts: the live operations (valueCount - sourceSize) and the
    // layers of compileTensorProgram([root]).
    public static (int Operations, int Layers) Cost(Circuit circuit, Wire root)
    {
        var program = circuit.CompileTensorProgram([root]);
        return (program.ValueCount - circuit.SourceSize, program.Layers.Length);
    }

    // Handwired programCounts: the live operations, the layers, and the stored weights, gather reads
    // and index reads summed over the layers.
    public static ProgramCountsResult ProgramCounts(TensorProgram program)
    {
        var storedWeights = 0;
        var gatherReads = 0;
        var indexReads = 0;
        foreach (var layer in program.Layers)
        {
            storedWeights += layer.Weights.Length;
            gatherReads += layer.GatherReads.Length;
            indexReads += layer.IndexReads.Length;
        }
        return new ProgramCountsResult(program.ValueCount - program.SourceSize, program.Layers.Length, storedWeights, gatherReads, indexReads);
    }

    // Handwired gatherStatistics: the gather reads, the gather computes, the slots, the stored weights,
    // and the bytes of the gather tables. A compute stores its bit table and a 32-bit slot number. A
    // read stores its candidate table and three 32-bit values (row, slot and mask flag).
    public static GatherStatisticsResult GatherStatistics(TensorProgram program)
    {
        var readCount = 0;
        var computeCount = 0;
        var storedWeightCount = 0;
        var gatherBytes = 0;
        foreach (var layer in program.Layers)
        {
            readCount += layer.GatherReads.Length;
            computeCount += layer.GatherComputes.Length;
            storedWeightCount += layer.Weights.Length;
            foreach (var compute in layer.GatherComputes) gatherBytes += compute.BitPositions.ByteLength + 4;
            foreach (var read in layer.GatherReads) gatherBytes += read.CandidatePositions.ByteLength + 12;
        }
        return new GatherStatisticsResult(readCount, computeCount, program.GatherSlotCount, storedWeightCount, gatherBytes);
    }

    // Handwired loweredWires: the wires whose operation the program lowered to a gather read.
    public static HashSet<int> LoweredWires(TensorProgram program)
    {
        var wires = new HashSet<int>();
        var availableValues = program.SourceSize;
        foreach (var layer in program.Layers)
        {
            foreach (var read in layer.GatherReads) wires.Add(program.WireOrder[availableValues + read.Row]);
            availableValues += layer.OutputSize;
        }
        return wires;
    }

    // Handwired toEqual on two programs: each field, each index array with its element width, and
    // each float with Object.is. Record equality is not used, because it compares arrays by reference.
    public static void AssertSameProgram(TensorProgram expected, TensorProgram actual)
    {
        Assert.Equal(expected.SourceSize, actual.SourceSize);
        Assert.Equal(expected.ValueCount, actual.ValueCount);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.GatherSlotCount, actual.GatherSlotCount);
        AssertSameIndices(expected.Roots, actual.Roots);
        AssertSameIndices(expected.WireOrder, actual.WireOrder);
        Assert.Equal(expected.Layers.Length, actual.Layers.Length);
        for (var layerIndex = 0; layerIndex < expected.Layers.Length; layerIndex++)
        {
            var expectedLayer = expected.Layers[layerIndex];
            var actualLayer = actual.Layers[layerIndex];
            AssertSameIndices(expectedLayer.InputIndices, actualLayer.InputIndices);
            Assert.Equal(expectedLayer.InputSize, actualLayer.InputSize);
            Assert.Equal(expectedLayer.OutputSize, actualLayer.OutputSize);
            Js.AllIs(expectedLayer.Bias.Select(value => (double)value), actualLayer.Bias.Select(value => (double)value));
            AssertSameIndices(expectedLayer.GateRows, actualLayer.GateRows);
            AssertSameIndices(expectedLayer.BooleanRows, actualLayer.BooleanRows);
            AssertSameIndices(expectedLayer.RowOffsets, actualLayer.RowOffsets);
            AssertSameIndices(expectedLayer.Columns, actualLayer.Columns);
            Js.AllIs(expectedLayer.Weights.Select(value => (double)value), actualLayer.Weights.Select(value => (double)value));
            Assert.Equal(expectedLayer.GatherComputes.Length, actualLayer.GatherComputes.Length);
            for (var index = 0; index < expectedLayer.GatherComputes.Length; index++)
            {
                Assert.Equal(expectedLayer.GatherComputes[index].Slot, actualLayer.GatherComputes[index].Slot);
                AssertSameIndices(expectedLayer.GatherComputes[index].BitPositions, actualLayer.GatherComputes[index].BitPositions);
            }
            Assert.Equal(expectedLayer.GatherReads.Length, actualLayer.GatherReads.Length);
            for (var index = 0; index < expectedLayer.GatherReads.Length; index++)
            {
                var expectedRead = expectedLayer.GatherReads[index];
                var actualRead = actualLayer.GatherReads[index];
                Assert.Equal((expectedRead.Row, expectedRead.Slot, expectedRead.Masked), (actualRead.Row, actualRead.Slot, actualRead.Masked));
                AssertSameIndices(expectedRead.CandidatePositions, actualRead.CandidatePositions);
            }
            Assert.Equal(expectedLayer.IndexReads.Length, actualLayer.IndexReads.Length);
            for (var index = 0; index < expectedLayer.IndexReads.Length; index++)
            {
                var expectedRead = expectedLayer.IndexReads[index];
                var actualRead = actualLayer.IndexReads[index];
                Assert.Equal(
                    (expectedRead.Row, expectedRead.IndexRow, expectedRead.BelowZero, expectedRead.AboveZero,
                        expectedRead.CandidateCount, expectedRead.CandidateBase, expectedRead.CandidateStride),
                    (actualRead.Row, actualRead.IndexRow, actualRead.BelowZero, actualRead.AboveZero,
                        actualRead.CandidateCount, actualRead.CandidateBase, actualRead.CandidateStride));
                AssertSameIndices(expectedRead.CandidatePositions, actualRead.CandidatePositions);
            }
        }
    }

    // Handwired toEqual on two operation lists, for example cells.test.ts:101 and :126. Each field of
    // each operation, each term with Object.is on the coefficient, and the content of each candidate
    // table. Record equality is not used, because it compares the term arrays by reference.
    public static void AssertSameOperations(IReadOnlyList<Operation> expected, IReadOnlyList<Operation> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var expectedOperation = expected[index];
            var actualOperation = actual[index];
            var label = $"operation {index}";
            Assert.True(expectedOperation.Kind == actualOperation.Kind, $"{label}: kind {actualOperation.Kind}, expected {expectedOperation.Kind}");
            AssertSameTerms(expectedOperation.LeftTerms, actualOperation.LeftTerms, $"{label} left terms");
            Assert.True(Js.ObjectIs(expectedOperation.LeftBias, actualOperation.LeftBias), $"{label}: left bias {actualOperation.LeftBias:R}, expected {expectedOperation.LeftBias:R}");
            AssertSameTerms(expectedOperation.GateTerms, actualOperation.GateTerms, $"{label} gate terms");
            Assert.True(Js.ObjectIs(expectedOperation.GateBias, actualOperation.GateBias), $"{label}: gate bias {actualOperation.GateBias:R}, expected {expectedOperation.GateBias:R}");
            Assert.True(expectedOperation.Depth == actualOperation.Depth, $"{label}: depth {actualOperation.Depth}, expected {expectedOperation.Depth}");
            Assert.True(expectedOperation.PreserveRounding == actualOperation.PreserveRounding, $"{label}: preserveRounding differs");
            Assert.True(Equals(expectedOperation.Macro, actualOperation.Macro), $"{label}: macro {actualOperation.Macro}, expected {expectedOperation.Macro}");
            Assert.True(expectedOperation.RoundingPoint == actualOperation.RoundingPoint, $"{label}: rounding point {actualOperation.RoundingPoint}, expected {expectedOperation.RoundingPoint}");
            Assert.True(expectedOperation.ComparisonBit == actualOperation.ComparisonBit, $"{label}: comparison bit differs");
            var expectedRead = expectedOperation.IndexRead;
            var actualRead = actualOperation.IndexRead;
            Assert.True((expectedRead is null) == (actualRead is null), $"{label}: index read differs");
            if (expectedRead is null || actualRead is null) continue;
            Assert.Equal(expectedRead.Table.Id, actualRead.Table.Id);
            Assert.Equal<Wire>(expectedRead.Table.Candidates, actualRead.Table.Candidates);
            Assert.Equal(expectedRead.Table.MaximumDepth, actualRead.Table.MaximumDepth);
            Assert.Equal((expectedRead.Below, expectedRead.Above), (actualRead.Below, actualRead.Above));
            Js.Is(expectedRead.IndexRange.Minimum, actualRead.IndexRange.Minimum);
            Js.Is(expectedRead.IndexRange.Maximum, actualRead.IndexRange.Maximum);
        }
    }

    private static void AssertSameTerms(IReadOnlyList<Term> expected, IReadOnlyList<Term> actual, string label)
    {
        Assert.True(expected.Count == actual.Count, $"{label}: {actual.Count} terms, expected {expected.Count}");
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.True(
                expected[index].Wire == actual[index].Wire && Js.ObjectIs(expected[index].Coefficient, actual[index].Coefficient),
                $"{label} {index}: {actual[index]}, expected {expected[index]}");
        }
    }

    // toEqual on two typed arrays: the same typed array type and the same values.
    public static void AssertSameIndices(UnsignedIndices expected, UnsignedIndices actual)
    {
        Assert.Equal(expected.ElementBits, actual.ElementBits);
        Assert.Equal<int>(expected, actual);
    }
}
