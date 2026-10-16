// Handwired source: tests/runtime/numeric-ui-core.test.ts and tests/runtime/index-read.test.ts,
// the compiler tests that have no Sharpwired test from phase 1. Each test name holds the
// Handwired test name. The tests marked "Sharpwired addition" have no Handwired test.

using System.Collections.Immutable;

namespace Sharpwired.Core.Tests;

public class TensorCompilerTests
{
    // numeric-ui-core.test.ts 'compiled tensor constants use native FP32 storage'.
    // Handwired checks instanceof Float32Array. Bias and Weights are ImmutableArray<float>, so each
    // stored value is an FP32 value that the compiler rounds once.
    [Fact]
    public void Compiled_tensor_constants_use_native_FP32_storage()
    {
        var circuit = new Circuit(1);
        circuit.Affine([(circuit.Source(0), 1.0 / 3)], 1.0 / 10);
        var layer = circuit.CompileTensorProgram().Layers[0];

        Assert.Equal(typeof(ImmutableArray<float>), typeof(TensorLayer).GetProperty(nameof(TensorLayer.Weights))!.PropertyType);
        Assert.Equal(typeof(ImmutableArray<float>), typeof(TensorLayer).GetProperty(nameof(TensorLayer.Bias))!.PropertyType);
        Js.Is(Js.F(1.0 / 3), layer.Weights[0]);
        Js.Is(Js.F(1.0 / 10), layer.Bias[0]);
    }

    // numeric-ui-core.test.ts 'compiled indices use the smallest safe unsigned width'.
    // Handwired checks instanceof Uint8Array, Uint16Array and Uint32Array. This port checks ElementBits.
    [Fact]
    public void Compiled_indices_use_the_smallest_safe_unsigned_width()
    {
        var small = new Circuit(255);
        var smallOutput = small.Affine([(small.Source(254), 2)]);
        var smallProgram = small.CompileTensorProgram([smallOutput]);
        Assert.Equal(8, smallProgram.Roots.ElementBits);
        Assert.Equal(8, smallProgram.WireOrder.ElementBits);
        Assert.Equal(8, smallProgram.Layers[0].InputIndices.ElementBits);

        var medium = new Circuit(257);
        var mediumOutput = medium.Affine([.. Enumerable.Range(0, 257).Select(wire => new Term(medium.Source(wire), 1))]);
        var mediumProgram = medium.CompileTensorProgram([mediumOutput]);
        Assert.Equal(16, mediumProgram.Roots.ElementBits);
        Assert.Equal(16, mediumProgram.WireOrder.ElementBits);
        Assert.Equal(16, mediumProgram.Layers[0].InputIndices.ElementBits);
        Assert.Equal(16, mediumProgram.Layers[0].Columns.ElementBits);
        Assert.Equal(16, mediumProgram.Layers[0].RowOffsets.ElementBits);

        var large = new Circuit(65_537);
        var largeOutput = large.Affine([(large.Source(65_536), 2)]);
        var largeProgram = large.CompileTensorProgram([largeOutput]);
        Assert.Equal(32, largeProgram.Roots.ElementBits);
        Assert.Equal(32, largeProgram.WireOrder.ElementBits);
        Assert.Equal(32, largeProgram.Layers[0].InputIndices.ElementBits);
    }

    // numeric-ui-core.test.ts 'tensor layers gather only the values they read'.
    [Fact]
    public void Tensor_layers_gather_only_the_values_they_read()
    {
        var circuit = new Circuit(2);
        var first = circuit.Reglu([(circuit.Source(0), 1)], 0, [(circuit.Source(1), 1)], 0);
        var second = circuit.Reglu([(first, 2)], 1, [(first, -1)], 5);
        var program = circuit.CompileTensorProgram([second]);

        Assert.Equal(2, program.Layers.Length);
        Assert.Equal<int>([0, 1], program.Layers[0].InputIndices);
        Assert.Equal(2, program.Layers[0].InputSize);
        Assert.Equal(1, program.Layers[0].OutputSize);
        Assert.Equal<int>([2], program.Layers[1].InputIndices);
        Assert.Equal(1, program.Layers[1].InputSize);
        Assert.Equal(1, program.Layers[1].OutputSize);
    }

    // index-read.test.ts 'recomputing depths without lowered gathers reproduces the build-time program'.
    // Handwired compiles with lowerGathers false. The compiler of this task has no gather lowering yet,
    // so each compile is a compile without lowered gathers.
    [Fact]
    public void Recomputing_depths_without_lowered_gathers_reproduces_the_build_time_program()
    {
        var random = Js.CreateRandom(0xdee9);
        for (var circuitIndex = 0; circuitIndex < 40; circuitIndex++)
        {
            var circuit = new Circuit(6);
            for (var wire = 0; wire < 3; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 7);
            var integerWires = new List<Wire> { Wire.FromPosition(0), Wire.FromPosition(1), Wire.FromPosition(2) };
            var wires = Enumerable.Range(0, 6).Select(Wire.FromPosition).ToList();
            for (var operation = 0; operation < 25; operation++)
            {
                var choice = RandomInteger(random, 3);
                if (choice == 0)
                {
                    var table = Enumerable.Range(0, 1 + RandomInteger(random, 6))
                        .Select(_ => integerWires[RandomInteger(random, integerWires.Count)])
                        .ToArray();
                    var wire = circuit.IndexRead(integerWires[RandomInteger(random, integerWires.Count)], table);
                    integerWires.Add(wire);
                    wires.Add(wire);
                }
                else if (choice == 1)
                {
                    var left = wires[RandomInteger(random, wires.Count)];
                    var gate = wires[RandomInteger(random, wires.Count)];
                    wires.Add(circuit.Reglu([(left, 1)], 0.5, [(gate, -1)], 2));
                }
                else
                {
                    var first = wires[RandomInteger(random, wires.Count)];
                    var second = wires[RandomInteger(random, wires.Count)];
                    wires.Add(circuit.RoundAffine([(first, 3), (second, 1)]));
                }
            }
            var roots = wires.Where(wire => wire.Position >= circuit.SourceSize && random() < 0.5).ToArray();
            TensorChecks.AssertSameProgram(
                circuit.CompileTensorProgram(roots),
                circuit.CompileTensorProgram(roots, recomputeDepths: true));
        }
    }

    // Sharpwired addition, from compileTensorProgram: the projection rows of a layer are the outputs in
    // operation order, then one gate row for each ReGLU, then one index row for each index read. An
    // index read output row stores no weights and the bias 0. The columns follow the packed positions.
    [Fact]
    public void A_layer_stores_the_outputs_then_the_gate_rows_then_the_index_rows()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 3);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        var (s0, s1, s2, s3) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3));
        var affine = circuit.Affine([(s1, 2)], 1);
        var product = circuit.Reglu([(s1, 3)], 2, [(s2, -1)], 5);
        var clampRead = circuit.IndexRead(s0, [s1, s2, s3]);
        var decision = circuit.Boolean(s3);
        var secondProduct = circuit.Reglu([(s2, 1)], 0, [(s3, 1)], 0);
        var zeroRead = circuit.IndexReadAffine([(s0, 1)], 1, [s1, s2, s3], OutOfRange.Zero, OutOfRange.Zero);
        Wire[] roots = [affine, product, clampRead, decision, secondProduct, zeroRead];
        var program = circuit.CompileTensorProgram(roots);
        circuit.ValidateTensorProgram(program);

        Assert.Equal(4, program.SourceSize);
        Assert.Equal(10, program.ValueCount);
        Assert.Equal(10, program.Width);
        Assert.Equal(0, program.GatherSlotCount);
        Assert.Equal<int>([.. roots.Select(root => root.Position)], program.Roots);
        Assert.Equal<int>([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], program.WireOrder);
        var layer = Assert.Single(program.Layers);
        Assert.Equal<int>([0, 1, 2, 3], layer.InputIndices);
        Assert.Equal(4, layer.InputSize);
        Assert.Equal(6, layer.OutputSize);
        Assert.Equal<float>([1, 2, 0, 0, 0, 0, 5, 0, 0, 1], layer.Bias);
        Assert.Equal<int>([1, 4], layer.GateRows);
        Assert.Equal<int>([3], layer.BooleanRows);
        Assert.Equal<int>([0, 1, 2, 2, 3, 4, 4, 5, 6, 7, 8], layer.RowOffsets);
        Assert.Equal<int>([1, 1, 3, 2, 2, 3, 0, 0], layer.Columns);
        Assert.Equal<float>([2, 3, 1, 1, -1, 1, 1, 1], layer.Weights);
        Assert.Empty(layer.GatherComputes);
        Assert.Empty(layer.GatherReads);
        Assert.Equal(2, layer.IndexReads.Length);
        var (first, second) = (layer.IndexReads[0], layer.IndexReads[1]);
        Assert.Equal((2, 8, false, false, 3, 1, 1), (first.Row, first.IndexRow, first.BelowZero, first.AboveZero, first.CandidateCount, first.CandidateBase, first.CandidateStride));
        Assert.Equal((5, 9, true, true, 3, 1, 1), (second.Row, second.IndexRow, second.BelowZero, second.AboveZero, second.CandidateCount, second.CandidateBase, second.CandidateStride));
        Assert.Empty(first.CandidatePositions);
        Assert.Empty(second.CandidatePositions);
    }

    // Sharpwired addition, from packCandidateTable: evenly spaced candidates store a base and a stride
    // (also a negative stride, and the stride 0 of one repeated wire or of one candidate), and other
    // tables store one position for each candidate. Reads of one table share one positions array.
    [Fact]
    public void Candidate_tables_store_a_base_and_a_stride_or_positions_shared_by_every_read()
    {
        var circuit = new Circuit(6);
        circuit.AssumeRange(circuit.Source(0), 0, 3);
        var index = circuit.Source(0);
        Wire[] Cells(params int[] positions) => [.. positions.Select(circuit.Source)];
        var forward = circuit.IndexRead(index, Cells(2, 3, 4, 5));
        var reversed = circuit.IndexRead(index, Cells(5, 4, 3, 2));
        var repeated = circuit.IndexRead(index, Cells(1, 1, 1));
        var scattered = circuit.IndexRead(index, Cells(3, 1, 5, 2));
        var single = circuit.IndexRead(index, Cells(4));
        var scatteredZero = circuit.IndexReadAffine([(index, 1)], 0, Cells(3, 1, 5, 2), OutOfRange.Zero, OutOfRange.Zero);
        var program = circuit.CompileTensorProgram([forward, reversed, repeated, scattered, single, scatteredZero]);
        circuit.ValidateTensorProgram(program);

        var reads = Assert.Single(program.Layers).IndexReads;
        Assert.Equal(6, reads.Length);
        var packed = reads.Select(read => (read.CandidateCount, read.CandidateBase, read.CandidateStride, read.CandidatePositions.Count)).ToArray();
        Assert.Equal((4, 2, 1, 0), packed[0]);
        Assert.Equal((4, 5, -1, 0), packed[1]);
        Assert.Equal((3, 1, 0, 0), packed[2]);
        Assert.Equal((4, 3, -2, 4), packed[3]);
        Assert.Equal((1, 4, 0, 0), packed[4]);
        Assert.Equal((4, 3, -2, 4), packed[5]);
        Assert.Equal<int>([3, 1, 5, 2], reads[3].CandidatePositions);
        Assert.Same(reads[3].CandidatePositions, reads[5].CandidatePositions);
        Assert.Same(UnsignedIndices.Empty, reads[0].CandidatePositions);
    }

    // Sharpwired addition, from findLiveOperations: a root outside the circuit is refused with the
    // assertWire message. With no roots, every operation is a root.
    [Fact]
    public void Roots_must_be_wires_of_the_circuit_and_no_roots_means_every_operation()
    {
        var circuit = new Circuit(2);
        var first = circuit.RoundAffine([(circuit.Source(0), 2)]);
        var second = circuit.RoundAffine([(circuit.Source(1), 3)]);
        Js.Throws(() => circuit.CompileTensorProgram([Wire.FromPosition(4)]), "wire is outside the compiled circuit");
        var program = circuit.CompileTensorProgram();
        Assert.Equal<int>([first.Position, second.Position], program.Roots);
        Assert.Equal(4, program.ValueCount);
        var empty = circuit.CompileTensorProgram([]);
        Assert.Empty(empty.Layers);
        Assert.Empty(empty.Roots);
        Assert.Equal<int>([0, 1], empty.WireOrder);
        circuit.ValidateTensorProgram(empty);
    }

    // Sharpwired addition, from compactUnsignedIndices: the smallest element width that holds the
    // largest value, and a refusal of a negative value.
    [Fact]
    public void Unsigned_indices_use_the_smallest_width_that_holds_the_largest_value()
    {
        Assert.Equal((8, 2, 2), Shape(UnsignedIndices.From([0, 255])));
        Assert.Equal((16, 2, 4), Shape(UnsignedIndices.From([0, 256])));
        Assert.Equal((16, 1, 2), Shape(UnsignedIndices.From([65_535])));
        Assert.Equal((32, 1, 4), Shape(UnsignedIndices.From([65_536])));
        Assert.Equal((32, 1, 4), Shape(UnsignedIndices.From([int.MaxValue])));
        Assert.Equal((8, 0, 0), Shape(UnsignedIndices.Empty));
        Assert.Equal((8, 0, 0), Shape(UnsignedIndices.From([])));
        Assert.Equal<int>([7, 65_536, 0], UnsignedIndices.From([7, 65_536, 0]));
        Assert.Equal(65_536, UnsignedIndices.From([7, 65_536, 0])[1]);
        Assert.Throws<ArgumentException>(() => UnsignedIndices.From([3, -1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnsignedIndices.From([3])[1]);

        static (int Bits, int Count, int Bytes) Shape(UnsignedIndices indices) => (indices.ElementBits, indices.Count, indices.ByteLength);
    }

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);
}
