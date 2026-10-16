// Handwired source: tests/runtime/gather-lowering.test.ts, the blocks 'recognition / adversarial value
// magnitudes', 'library circuits' and 'HandwiredOS'. The HandwiredOS test builds
// src/handwiredos/window-manager.ts, which has no Sharpwired port. The test here builds a circuit with
// the same properties and asserts the same facts (plan decision 2).

namespace Sharpwired.Core.Tests;

public partial class GatherLoweringTests
{
    // FP32 extremes inside the declared ranges: the largest finite float, the smallest normal, a
    // subnormal, -0, and integers at 2^24, where FP32 integers stop being exact. The gather must give
    // each stored value bit for bit, because f(1 × value) = value for each finite value.
    private static readonly double[] Extremes =
    [
        3.4028234663852886e38,
        -3.4028234663852886e38,
        1.1754943508222875e-38,
        -1.401298464324817e-45,
        -0.0,
        0,
        16_777_216,
        16_777_217,
        -16_777_215,
        0.1,
    ];

    // 'recognition' / 'adversarial value magnitudes' / 'extreme candidates gather bit-for-bit'.
    [Fact]
    public void Extreme_candidates_gather_bit_for_bit()
    {
        var slotCount = Extremes.Length;
        var circuit = new Circuit(slotCount * 2);
        var bits = Enumerable.Range(0, slotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(slotCount, slotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, -3.4028234663852886e38, 3.4028234663852886e38);
        var read = circuit.Affine(MaskedTerms(circuit, bits, values));
        var (lowered, _) = CompileBoth(circuit, [read]);
        Assert.Equal(1, TensorChecks.GatherStatistics(lowered).ReadCount);

        for (var selectedSlot = 0; selectedSlot < slotCount; selectedSlot++)
        {
            var source = new float[slotCount * 2];
            source[selectedSlot] = 1;
            for (var slot = 0; slot < slotCount; slot++) source[slotCount + slot] = (float)Extremes[slot];
            var reference = ExpectExact(circuit, lowered, source);
            Assert.True(Js.SameNumericValue(reference[read.Position], Js.F(Extremes[selectedSlot])));
        }
    }

    // Handwired buildLibraryCircuit: slotCount items, readCount value fields for each item, a one-hot
    // selection over the items, and one masked sum for each field, all over source cells.
    private static (Circuit Circuit, Wire[] Reads, Func<int, int, int> ValueCell) BuildLibraryCircuit(int slotCount, int readCount)
    {
        var circuit = new Circuit(slotCount + slotCount * readCount);
        var bits = Enumerable.Range(0, slotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        int ValueCell(int field, int slot) => slotCount + field * slotCount + slot;
        for (var field = 0; field < readCount; field++)
        {
            for (var slot = 0; slot < slotCount; slot++) circuit.AssumeRange(circuit.Source(ValueCell(field, slot)), 0, 1e6);
        }
        var reads = new Wire[readCount];
        for (var field = 0; field < readCount; field++)
        {
            reads[field] = circuit.Affine([.. bits.Select((bit, slot) => new Term(circuit.Multiply(bit, circuit.Source(ValueCell(field, slot))), 1))]);
        }
        return (circuit, reads, ValueCell);
    }

    // Handwired fillLibrarySource. Each field value is distinct, so a wrong gather index cannot give
    // the correct value by chance.
    private static void FillLibrarySource(float[] source, int slotCount, int readCount, int selectedSlot)
    {
        Array.Fill(source, 0f);
        source[selectedSlot] = 1;
        for (var field = 0; field < readCount; field++)
        {
            for (var slot = 0; slot < slotCount; slot++) source[slotCount + field * slotCount + slot] = (float)(1 + field * 7919 + slot * 13.25);
        }
    }

    // 'library circuits' / 'stored weights per read drop from 3N to zero under lowering'.
    [Fact]
    public void Stored_weights_per_read_drop_from_3N_to_zero_under_lowering()
    {
        foreach (var slotCount in new[] { 64, 256 })
        {
            foreach (var readCount in new[] { 1, 4 })
            {
                var (circuit, reads, _) = BuildLibraryCircuit(slotCount, readCount);
                var lowered = circuit.CompileTensorProgram(reads, lowerGathers: true);
                var plain = circuit.CompileTensorProgram(reads, lowerGathers: false);
                circuit.ValidateTensorProgram(lowered);
                circuit.ValidateTensorProgram(plain);
                var loweredStats = TensorChecks.GatherStatistics(lowered);
                var plainStats = TensorChecks.GatherStatistics(plain);

                // The dense form: for each read, N products of 2 stored weights and a sum of N terms.
                // The lowered form stores no weights: the products are dead, and each read is one
                // gather row.
                Assert.Equal(3 * slotCount * readCount, plainStats.StoredWeightCount);
                Assert.Equal(0, loweredStats.StoredWeightCount);
                Assert.Equal(readCount, loweredStats.ReadCount);
                Assert.Equal(1, loweredStats.ComputeCount);
                // Only the read outputs stay after the source cells.
                Assert.Equal(circuit.SourceSize + readCount, lowered.ValueCount);
                Assert.Equal(circuit.SourceSize + readCount + slotCount * readCount, plain.ValueCount);

                // The parts that stay proportional to N: the shared bit table (N entries) and one
                // candidate table for each read (N entries each), as index tables, not FP32 weights.
                Assert.True(loweredStats.GatherBytes > 0);
                Assert.True(loweredStats.GatherBytes <= 4 * slotCount * (readCount + 1) + 16 * readCount + 4);
            }
        }
    }

    // 'library circuits' / 'lowered and dense programs agree with the reference across selections'.
    [Fact]
    public void Lowered_and_dense_programs_agree_with_the_reference_across_selections()
    {
        const int SlotCount = 128;
        const int ReadCount = 4;
        var (circuit, reads, valueCell) = BuildLibraryCircuit(SlotCount, ReadCount);
        var lowered = circuit.CompileTensorProgram(reads, lowerGathers: true);
        var plain = circuit.CompileTensorProgram(reads, lowerGathers: false);
        var source = new float[circuit.SourceSize];
        foreach (var selectedSlot in new[] { 0, 1, 63, 64, 126, 127 })
        {
            FillLibrarySource(source, SlotCount, ReadCount, selectedSlot);
            var reference = circuit.EvaluateReference(source);
            circuit.TensorProgram = lowered;
            var tensorLowered = circuit.EvaluateTensor(source);
            circuit.TensorProgram = plain;
            var tensorPlain = circuit.EvaluateTensor(source);
            for (var field = 0; field < ReadCount; field++)
            {
                var read = reads[field].Position;
                Js.Is(source[valueCell(field, selectedSlot)], reference[read]);
                Assert.True(Js.SameNumericValue(tensorLowered[read], reference[read]));
                Assert.True(Js.SameNumericValue(tensorPlain[read], reference[read]));
            }
        }
    }

    // Replaces 'HandwiredOS' / 'window-manager declares its stacking order as 6 one-hot groups, and
    // none of its sums lowers'. The circuit copies the form of window-manager.ts with 3 windows:
    // pixel cells with finite ranges, the stacking order as a 3 × 3 permutation matrix that the
    // source table declares as 6 one-hot groups (3 ranks, then 3 windows, as cells.ts permutation
    // does), the frontmost-window selection of emitFrontmost, and the stack update. Each masked sum
    // over a group of 3 has 3 terms, and the builder copies such a sum into each operation that reads
    // it. So the sum operations are not live, and no live sum lowers.
    [Fact]
    public void A_window_stack_declares_6_one_hot_groups_and_none_of_its_sums_lowers()
    {
        const int WindowCount = 3;
        const double Pixels = 4096;
        // Source cells: unit, pointer x and y, then for each window open, left, top, width and
        // height, then the 9 stack bits.
        const int GeometryStart = 3;
        const int StackStart = GeometryStart + WindowCount * 5;
        var circuit = new Circuit(StackStart + WindowCount * WindowCount);
        var unit = circuit.Source(0);
        var x = circuit.Source(1);
        var y = circuit.Source(2);
        circuit.AssumeRange(unit, 1, 1);
        circuit.AssumeRange(x, 0, Pixels);
        circuit.AssumeRange(y, 0, Pixels);
        var stack = new Wire[WindowCount][];
        for (var rank = 0; rank < WindowCount; rank++)
        {
            stack[rank] = [.. Enumerable.Range(StackStart + rank * WindowCount, WindowCount).Select(circuit.Source)];
        }
        // cells.ts permutation: one group for each rank, then one group for each window.
        for (var rank = 0; rank < WindowCount; rank++) circuit.AssumeOneHot(stack[rank]);
        for (var window = 0; window < WindowCount; window++) circuit.AssumeOneHot([.. stack.Select(row => row[window])]);

        // window-manager.ts emitRectangleHit, multiplied by the open bit of the window.
        var half = circuit.Constant(0.5);
        var windowHit = new Wire[WindowCount];
        for (var window = 0; window < WindowCount; window++)
        {
            var cell = GeometryStart + window * 5;
            var open = circuit.Source(cell);
            var (left, top, width, height) = (circuit.Source(cell + 1), circuit.Source(cell + 2), circuit.Source(cell + 3), circuit.Source(cell + 4));
            circuit.AssumeRange(open, 0, 1);
            foreach (var pixel in new[] { left, top, width, height }) circuit.AssumeRange(pixel, 0, Pixels);
            Wire[] edgeBits =
            [
                .. new[]
                {
                    circuit.Affine([(x, 1), (left, -1)]),
                    circuit.Affine([(left, 1), (width, 1), (x, -1)]),
                    circuit.Affine([(y, 1), (top, -1)]),
                    circuit.Affine([(top, 1), (height, 1), (y, -1)]),
                }.Select(distance => circuit.Compare(distance, Relation.GreaterOrEqual, half)),
            ];
            var hit = circuit.Reglu([], 1, [.. edgeBits.Select(bit => new Term(bit, 1))], -3);
            circuit.AssumeRange(hit, 0, 1);
            windowHit[window] = circuit.Multiply(open, hit);
        }

        // window-manager.ts emitFrontmost.
        var frontRank = new Wire[WindowCount];
        var remaining = unit;
        for (var rank = WindowCount - 1; rank >= 0; rank--)
        {
            var rankHit = circuit.ClampZeroOne(circuit.Affine([.. Enumerable.Range(0, WindowCount).Select(window => new Term(circuit.Multiply(stack[rank][window], windowHit[window]), 1))]), unit);
            frontRank[rank] = circuit.Multiply(remaining, rankHit);
            remaining = circuit.Relu(circuit.Affine([(remaining, 1), (rankHit, -1)]));
        }
        var selectedWindow = Enumerable.Range(0, WindowCount)
            .Select(window => circuit.ClampZeroOne(circuit.Affine([.. Enumerable.Range(0, WindowCount).Select(rank => new Term(circuit.Multiply(stack[rank][window], frontRank[rank]), 1))]), unit))
            .ToArray();

        // The stack update of window-manager.ts: the selected window moves to the last rank.
        var selectionMass = circuit.ClampZeroOne(circuit.Affine([.. selectedWindow.Select(wire => new Term(wire, 1))]), unit);
        var selectionInactive = circuit.Affine([(unit, 1), (selectionMass, -1)]);
        var selectedRank = Enumerable.Range(0, WindowCount)
            .Select(rank => circuit.ClampZeroOne(circuit.Affine([.. Enumerable.Range(0, WindowCount).Select(window => new Term(circuit.Multiply(stack[rank][window], selectedWindow[window]), 1))]), unit))
            .ToArray();
        var roots = new List<Wire>();
        for (var rank = 0; rank < WindowCount; rank++)
        {
            for (var window = 0; window < WindowCount; window++)
            {
                var movedTerms = new List<Term>();
                if (rank == WindowCount - 1)
                {
                    movedTerms.Add(new Term(selectedWindow[window], 1));
                }
                else
                {
                    for (var selectedRankIndex = 0; selectedRankIndex < WindowCount; selectedRankIndex++)
                    {
                        var sourceRank = rank < selectedRankIndex ? rank : rank + 1;
                        movedTerms.Add(new Term(circuit.Multiply(selectedRank[selectedRankIndex], stack[sourceRank][window]), 1));
                    }
                }
                roots.Add(circuit.Affine([
                    (circuit.Multiply(selectionInactive, stack[rank][window]), 1),
                    (circuit.Affine(movedTerms), 1),
                ]));
            }
        }
        roots.AddRange(selectedWindow);
        circuit.ValidateCircuit();

        var program = circuit.CompileTensorProgram(roots);
        circuit.ValidateTensorProgram(program);
        var statistics = TensorChecks.GatherStatistics(program);
        var dense = circuit.CompileTensorProgram(roots, lowerGathers: false);
        Assert.Equal(6, circuit.OneHotGroups.Count);
        Assert.Equal(0, statistics.ReadCount);
        Assert.Equal(0, statistics.SlotCount);
        // So the compiled program is the dense compile of the same roots.
        Assert.Equal(TensorChecks.GatherStatistics(dense).StoredWeightCount, statistics.StoredWeightCount);
        Assert.Equal(dense.ValueCount, program.ValueCount);
        Assert.Equal(dense.Layers.Length, program.Layers.Length);
        // Sharpwired addition: the circuit has sum operations that the gather plan recognizes, so
        // the test fails if the builder stops copying the 3-term sums into their readers.
        Assert.NotEmpty(Circuit.PlanGatherLowering(circuit));
    }
}
