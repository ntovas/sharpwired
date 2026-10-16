// Handwired source: the test helpers programCounts of tests/runtime/index-read.test.ts and
// tests/runtime/spring-matrix.test.ts, gatherStatistics and loweredWires of
// tests/runtime/gather-lowering.test.ts, and the toEqual comparison of two compiled programs.

namespace Sharpwired.Core.Tests;

// The counts that the Handwired tests read from a compiled program.
internal sealed record ProgramCountsResult(int LiveOperations, int Layers, int StoredWeights, int GatherReads, int IndexReads);

// The counts of Handwired gatherStatistics.
internal sealed record GatherStatisticsResult(int ReadCount, int ComputeCount, int SlotCount, int StoredWeightCount, int GatherBytes);

internal static class TensorChecks
{
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

    // toEqual on two typed arrays: the same typed array type and the same values.
    public static void AssertSameIndices(UnsignedIndices expected, UnsignedIndices actual)
    {
        Assert.Equal(expected.ElementBits, actual.ElementBits);
        Assert.Equal<int>(expected, actual);
    }
}
