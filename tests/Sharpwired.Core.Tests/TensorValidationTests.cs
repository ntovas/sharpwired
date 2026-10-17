// Handwired source: src/runtime/numeric-ui-core.ts validateTensorProgram and validateTensorEvaluation.
// Handwired has no test for most of their messages. Each ValidateTensorProgram test here changes one
// field of a compiled program with a with expression. Then it checks that ValidateTensorProgram
// refuses the copy with the Handwired message. IndexReadTests holds index-read.test.ts 'validators
// reject malformed index read programs and circuits'.

namespace Sharpwired.Core.Tests;

public class TensorValidationTests
{
    // Two layers. Layer 0 reads the inputs 0..3. Its rows are an affine (row 0) and a ReGLU (row 1,
    // gate row 4). Then an index read (row 2, index row 5) and a boolean atom (row 3). Layer 1: a ReGLU that reads the
    // packed values 4, 5 and 6.
    private static (Circuit Circuit, TensorProgram Program) Build()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 2);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        var affine = circuit.RoundAffine([(circuit.Source(1), 2)], 1);
        var product = circuit.Reglu([(circuit.Source(1), 3)], 2, [(circuit.Source(2), -1)], 5);
        var read = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3)]);
        var decision = circuit.Boolean(circuit.Source(3));
        var deep = circuit.Reglu([(affine, 1), (read, 1)], 0, [(product, 1)], 0);
        var program = circuit.CompileTensorProgram([deep, decision]);
        circuit.ValidateTensorProgram(program);
        return (circuit, program);
    }

    private static Action Corrupt(Func<TensorProgram, TensorProgram> change)
    {
        var (circuit, program) = Build();
        var copy = change(program);
        return () => circuit.ValidateTensorProgram(copy);
    }

    private static Action CorruptLayer(int layerIndex, Func<TensorLayer, TensorLayer> change) =>
        Corrupt(program => program with { Layers = program.Layers.SetItem(layerIndex, change(program.Layers[layerIndex])) });

    private static TensorLayer ChangeRead(TensorLayer layer, Func<TensorIndexRead, TensorIndexRead> change) =>
        layer with { IndexReads = layer.IndexReads.SetItem(0, change(layer.IndexReads[0])) };

    [Fact]
    public void The_test_program_has_the_expected_shape()
    {
        var (_, program) = Build();
        Assert.Equal(2, program.Layers.Length);
        Assert.Equal(4, program.Layers[0].OutputSize);
        Assert.Equal<int>([1], program.Layers[0].GateRows);
        Assert.Equal<int>([3], program.Layers[0].BooleanRows);
        Assert.Equal(2, program.Layers[0].IndexReads[0].Row);
        Assert.Equal(5, program.Layers[0].IndexReads[0].IndexRow);
        Assert.Equal(1, program.Layers[1].OutputSize);
    }

    [Fact]
    public void A_program_that_does_not_match_the_circuit_is_refused()
    {
        Js.Throws(Corrupt(program => program with { SourceSize = 5 }), "tensor source size mismatch");
        Js.Throws(Corrupt(program => program with { Width = program.Width + 1 }), "tensor width mismatch");
        Js.Throws(Corrupt(program => program with { ValueCount = program.Width + 1 }), "tensor value count exceeds circuit width");
        Js.Throws(Corrupt(program => program with { ValueCount = program.ValueCount - 1 }), "tensor wire order size mismatch");
        Js.Throws(
            Corrupt(program => program with { WireOrder = UnsignedIndices.From([1, 0, 2, 3, 4, 5, 6, 7, 8]) }),
            "tensor source order mismatch");
        // A value count below the source size leaves a source cell without a wire order entry.
        Js.Throws(
            Corrupt(program => program with { ValueCount = 2, WireOrder = UnsignedIndices.From([0, 1]) }),
            "tensor source order mismatch");
    }

    [Fact]
    public void Layer_inputs_and_rows_out_of_their_bounds_are_refused()
    {
        Js.Throws(CorruptLayer(1, layer => layer with { OutputSize = 0 }), "tensor layer must produce at least one value");
        Js.Throws(CorruptLayer(0, layer => layer with { InputSize = 3 }), "tensor input index size mismatch");
        Js.Throws(
            CorruptLayer(1, layer => layer with { InputIndices = UnsignedIndices.From([4, 5, 8]) }),
            "tensor layer input is not available");
        Js.Throws(
            CorruptLayer(0, layer => layer with { GateRows = UnsignedIndices.From([4]) }),
            "gate output row is invalid");
        Js.Throws(
            CorruptLayer(0, layer => layer with
            {
                GateRows = UnsignedIndices.From([1, 1]),
                Bias = layer.Bias.Insert(4, 0f),
                RowOffsets = UnsignedIndices.From([.. layer.RowOffsets, layer.RowOffsets[^1]]),
            }),
            "gate output row must be unique");
        Js.Throws(CorruptLayer(0, layer => layer with { BooleanRows = UnsignedIndices.From([4]) }), "boolean output row is invalid");
        Js.Throws(CorruptLayer(0, layer => layer with { BooleanRows = UnsignedIndices.From([1]) }), "boolean output row cannot also be gated");
        Js.Throws(CorruptLayer(0, layer => layer with { BooleanRows = UnsignedIndices.From([3, 3]) }), "boolean output row must be unique");
    }

    [Fact]
    public void A_malformed_sparse_matrix_is_refused()
    {
        Js.Throws(
            CorruptLayer(0, layer => layer with { RowOffsets = UnsignedIndices.From([.. layer.RowOffsets.Take(layer.RowOffsets.Count - 1)]) }),
            "sparse row offset shape mismatch");
        Js.Throws(
            CorruptLayer(0, layer => layer with { RowOffsets = UnsignedIndices.From([1, .. layer.RowOffsets.Skip(1)]) }),
            "sparse row offsets must start at zero");
        Js.Throws(
            CorruptLayer(0, layer => layer with { Columns = UnsignedIndices.From([.. layer.Columns.Skip(1)]) }),
            "sparse column shape mismatch");
        Js.Throws(
            CorruptLayer(0, layer => layer with
            {
                Columns = UnsignedIndices.From([.. layer.Columns, 0]),
                Weights = layer.Weights.Add(1f),
            }),
            "sparse row offsets must end at the stored weight count");
        // Rows 0 and 1 store one weight each. Offsets 0, 2, 1, ... keep the first and the last offset.
        Js.Throws(
            CorruptLayer(0, layer => layer with { RowOffsets = UnsignedIndices.From([0, 2, 1, .. layer.RowOffsets.Skip(3)]) }),
            "sparse row offsets must be ordered");
        Js.Throws(
            CorruptLayer(0, layer => layer with { Columns = UnsignedIndices.From([layer.InputSize, .. layer.Columns.Skip(1)]) }),
            "sparse matrix column is invalid");
    }

    [Fact]
    public void A_malformed_index_read_is_refused()
    {
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { Row = layer.OutputSize })), "index read output row is invalid");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { Row = -1 })), "index read output row is invalid");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { Row = 1 })), "index read output row cannot be gated");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { Row = 3 })), "index read output row cannot be boolean");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { Row = 0 })), "index read output row must store no weights and no bias");
        Js.Throws(
            CorruptLayer(0, layer => layer with { Bias = layer.Bias.SetItem(layer.IndexReads[0].Row, 0.5f) }),
            "index read output row must store no weights and no bias");
        // The gate row stores the weight of the gate, so it is no index row.
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { IndexRow = 4 })), "index row must follow the gate rows");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { IndexRow = 6 })), "index row must follow the gate rows");
        // An index row with no stored weight reads no runtime index.
        Js.Throws(
            CorruptLayer(0, layer => layer with
            {
                RowOffsets = UnsignedIndices.From([.. layer.RowOffsets.Take(layer.RowOffsets.Count - 1), layer.RowOffsets[^2]]),
                Columns = UnsignedIndices.From([.. layer.Columns.Take(layer.Columns.Count - 1)]),
                Weights = layer.Weights.RemoveAt(layer.Weights.Length - 1),
            }),
            "index row must read a runtime index");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidateCount = 0 })), "index read needs at least one candidate");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidateCount = 4 })), "evenly spaced index read candidates are not available");
        Js.Throws(CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidateBase = -1, CandidateStride = 2 })), "evenly spaced index read candidates are not available");
        Js.Throws(
            CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidateCount = int.MaxValue, CandidateStride = int.MaxValue })),
            "evenly spaced index read candidates are not available");
        Js.Throws(
            CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidatePositions = UnsignedIndices.From([1, 2]) })),
            "index read candidate table size mismatch");
        Js.Throws(
            CorruptLayer(0, layer => ChangeRead(layer, read => read with { CandidatePositions = UnsignedIndices.From([1, 2, 4]) })),
            "index read candidate is not available");
    }

    [Fact]
    public void Non_finite_constants_are_refused()
    {
        Js.Throws(CorruptLayer(0, layer => layer with { Bias = layer.Bias.SetItem(0, float.NaN) }), "projection bias contains a non-finite number");
        Js.Throws(
            CorruptLayer(1, layer => layer with { Weights = layer.Weights.SetItem(0, float.PositiveInfinity) }),
            "projection matrix contains a non-finite number");
    }

    [Fact]
    public void Wires_that_are_not_written_exactly_once_are_refused()
    {
        Js.Throws(Corrupt(program => program with { WireOrder = UnsignedIndices.From([0, 1, 2, 3, 2, 5, 6, 7, 8]) }), "tensor output wire is invalid");
        Js.Throws(Corrupt(program => program with { WireOrder = UnsignedIndices.From([0, 1, 2, 3, 4, 5, 6, 7, 9]) }), "tensor output wire is invalid");
        Js.Throws(Corrupt(program => program with { WireOrder = UnsignedIndices.From([0, 1, 2, 3, 4, 5, 6, 7, 4]) }), "tensor wire must be written exactly once");
        Js.Throws(
            Corrupt(program => program with { Layers = program.Layers.RemoveAt(1), ValueCount = 8, WireOrder = UnsignedIndices.From([0, 1, 2, 3, 4, 5, 6, 7]) }),
            "every tensor root must be present");
        Js.Throws(Corrupt(program => program with { Roots = UnsignedIndices.From([20]) }), "every tensor root must be present");
        // A layer list that writes fewer values than the value count.
        Js.Throws(
            Corrupt(program => program with { Layers = program.Layers.RemoveAt(1), Roots = UnsignedIndices.From([7]) }),
            "tensor program output size mismatch");
    }

    [Fact]
    public void The_validator_reads_no_more_than_the_program_holds()
    {
        // A wire order shorter than the layers write gives the output wire message, not an index error.
        var (circuit, program) = Build();
        var shorter = program with
        {
            ValueCount = 5,
            WireOrder = UnsignedIndices.From([0, 1, 2, 3, 4]),
            Layers = [program.Layers[0]],
        };
        Js.Throws(() => circuit.ValidateTensorProgram(shorter), "tensor output wire is invalid");
    }

    // ValidateTensorEvaluation reads TensorProgram ??= CompileTensorProgram(), as Handwired does.
    [Fact]
    public void Tensor_evaluation_of_the_cached_program_passes_and_a_missing_program_is_compiled()
    {
        var (circuit, _) = Build();
        float[] source = [1.5f, 3, -2, 1];
        circuit.ValidateTensorEvaluation(source, circuit.EvaluateTensor(source));
        var cached = circuit.TensorProgram;
        Assert.NotNull(cached);

        circuit.TensorProgram = null;
        circuit.ValidateTensorEvaluation(source, circuit.EvaluateReference(source));
        Assert.NotNull(circuit.TensorProgram);
        TensorChecks.AssertSameProgram(cached, circuit.TensorProgram);
    }

    [Fact]
    public void A_changed_tensor_value_is_refused()
    {
        var (circuit, _) = Build();
        float[] source = [1.5f, 3, -2, 1];
        var values = circuit.EvaluateTensor(source);
        var deep = circuit.Width - 1;
        values[deep] = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(values[deep]) + 1);

        var exception = Assert.Throws<InvalidOperationException>(() => circuit.ValidateTensorEvaluation(source, values));
        Assert.Equal($"tensor wire {deep} does not match the reference FP32 atom program", exception.Message);
    }

    // Only the wires of WireOrder are compared. A wire that the cached program removed can hold any value.
    [Fact]
    public void Only_the_wires_of_the_cached_program_are_compared()
    {
        var (circuit, _) = Build();
        var removed = circuit.CompileTensorProgram([Wire.FromPosition(4)]);
        circuit.TensorProgram = removed;
        float[] source = [1.5f, 3, -2, 1];
        var values = circuit.EvaluateTensor(source);
        Assert.DoesNotContain(5, removed.WireOrder);
        values[5] = 1234;

        circuit.ValidateTensorEvaluation(source, values);
    }

    // Handwired compares with ===: +0 equals -0, and NaN equals no value, also not NaN.
    [Fact]
    public void Tensor_evaluation_compares_with_JavaScript_strict_equality()
    {
        var circuit = new Circuit(2);
        var sum = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        float[] zero = [-0f, 0f];
        var values = circuit.EvaluateTensor(zero);
        values[sum.Position] = -0f;
        Js.Is(0, circuit.EvaluateReference(zero)[sum.Position]);
        circuit.ValidateTensorEvaluation(zero, values);

        float[] infinities = [float.PositiveInfinity, float.NegativeInfinity];
        var nanValues = circuit.EvaluateTensor(infinities);
        Js.Is(double.NaN, nanValues[sum.Position]);
        Js.Throws(
            () => circuit.ValidateTensorEvaluation(infinities, nanValues),
            $"tensor wire {sum.Position} does not match the reference FP32 atom program");
    }

    [Fact]
    public void A_tensor_result_of_the_wrong_width_is_refused()
    {
        var (circuit, _) = Build();
        float[] source = [1.5f, 3, -2, 1];
        var values = circuit.EvaluateTensor(source);

        var exception = Assert.Throws<ArgumentException>(() => circuit.ValidateTensorEvaluation(source, values[..^1]));
        Assert.Equal("tensor result width mismatch", exception.Message);
        Js.Throws(() => circuit.ValidateTensorEvaluation(source, [.. values, 0]), "tensor result width mismatch");
    }

    // Handwired evaluateOperationStream checks the source length and not the source ranges.
    [Fact]
    public void Tensor_evaluation_checks_the_source_length_and_not_the_source_ranges()
    {
        var (circuit, _) = Build();
        Js.Throws(() => circuit.ValidateTensorEvaluation([1, 2, 3], new float[circuit.Width]), "source table has the wrong length");

        // Source 0 is outside its range 0..2. A circuit with the same operations and the range 0..10
        // on source 0 gives the values.
        float[] source = [7, 3, -2, 1];
        circuit.ValidateTensorEvaluation(source, BuildWithAWiderIndexRange().EvaluateReference(source));
    }

    private static Circuit BuildWithAWiderIndexRange()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 10);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        var affine = circuit.RoundAffine([(circuit.Source(1), 2)], 1);
        var product = circuit.Reglu([(circuit.Source(1), 3)], 2, [(circuit.Source(2), -1)], 5);
        var read = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3)]);
        circuit.Boolean(circuit.Source(3));
        circuit.Reglu([(affine, 1), (read, 1)], 0, [(product, 1)], 0);
        return circuit;
    }

    // A program with gathers. Layer 0: the sum p and the input of the boolean atom. Layer 1, in
    // operation order: the gather reads read and second (one group, so one slot), and the ReGLU
    // gated. Then the boolean atom decision, and the affine plain, which stores a weight.
    private static (Circuit Circuit, TensorProgram Program, int[] Rows) BuildGathers()
    {
        var circuit = new Circuit(4);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        circuit.AssumeOneHot(bits);
        circuit.AssumeRange(circuit.Source(2), 0, 100);
        circuit.AssumeRange(circuit.Source(3), 0, 100);
        Term[] Masked() => [(circuit.Multiply(bits[0], circuit.Source(2)), 1), (circuit.Multiply(bits[1], circuit.Source(3)), 1)];
        var read = circuit.Affine(Masked());
        var second = circuit.Affine(Masked(), 1);
        var p = circuit.RoundAffine([(circuit.Source(2), 1), (circuit.Source(3), 1)]);
        var gated = circuit.Reglu([(p, 1)], 0, [(circuit.Source(3), 1)], 0);
        var decision = circuit.Boolean(circuit.RoundAffine([(circuit.Source(2), 0.01)]));
        var plain = circuit.RoundAffine([(p, 1)], 3);
        var program = circuit.CompileTensorProgram([read, second, gated, decision, plain]);
        circuit.ValidateTensorProgram(program);
        // The row of each wire in layer 1.
        var layerStart = program.SourceSize + program.Layers[0].OutputSize;
        int RowOf(Wire wire) => program.WireOrder.ToList().IndexOf(wire.Position) - layerStart;
        return (circuit, program, [RowOf(read), RowOf(second), RowOf(gated), RowOf(decision), RowOf(plain)]);
    }

    private static Action CorruptGathers(Func<TensorLayer, TensorLayer> change)
    {
        var (circuit, program, _) = BuildGathers();
        var copy = program with { Layers = program.Layers.SetItem(1, change(program.Layers[1])) };
        return () => circuit.ValidateTensorProgram(copy);
    }

    private static TensorLayer ChangeGatherRead(TensorLayer layer, Func<TensorGatherRead, TensorGatherRead> change) =>
        layer with { GatherReads = layer.GatherReads.SetItem(0, change(layer.GatherReads[0])) };

    private static TensorLayer ChangeGatherCompute(TensorLayer layer, Func<TensorGatherCompute, TensorGatherCompute> change) =>
        layer with { GatherComputes = layer.GatherComputes.SetItem(0, change(layer.GatherComputes[0])) };

    [Fact]
    public void The_gather_test_program_has_the_expected_shape()
    {
        var (_, program, rows) = BuildGathers();
        Assert.Equal(2, program.Layers.Length);
        Assert.Equal(1, program.GatherSlotCount);
        var layer = program.Layers[1];
        Assert.Equal(5, layer.OutputSize);
        Assert.Equal([0, 1, 2, 3, 4], rows);
        var compute = Assert.Single(layer.GatherComputes);
        Assert.Equal(0, compute.Slot);
        Assert.Equal<int>([0, 1], compute.BitPositions);
        Assert.Equal([(0, 0, false), (1, 0, false)], layer.GatherReads.Select(read => (read.Row, read.Slot, read.Masked)));
        Assert.Equal<int>([2], layer.GateRows);
        Assert.Equal<int>([3], layer.BooleanRows);
    }

    [Fact]
    public void A_malformed_gather_compute_is_refused()
    {
        Js.Throws(CorruptGathers(layer => ChangeGatherCompute(layer, compute => compute with { Slot = 1 })), "gather slot is invalid");
        Js.Throws(CorruptGathers(layer => ChangeGatherCompute(layer, compute => compute with { Slot = -1 })), "gather slot is invalid");
        Js.Throws(
            CorruptGathers(layer => layer with { GatherComputes = layer.GatherComputes.Add(layer.GatherComputes[0]) }),
            "gather slot must be resolved exactly once");
        Js.Throws(
            CorruptGathers(layer => ChangeGatherCompute(layer, compute => compute with { BitPositions = UnsignedIndices.From([0, 6]) })),
            "gather bit is not available");
    }

    [Fact]
    public void A_malformed_gather_read_is_refused()
    {
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Row = layer.OutputSize })), "gather output row is invalid");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Row = -1 })), "gather output row is invalid");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Row = 2 })), "gather output row cannot be gated");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Row = 3 })), "gather output row cannot be boolean");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Row = 4 })), "gather output row must store no weights");
        // A read in a layer that resolves no slot, and reads of slots outside the program.
        Js.Throws(CorruptGathers(layer => layer with { GatherComputes = [] }), "gather read must follow its slot resolution");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Slot = 1 })), "gather read must follow its slot resolution");
        Js.Throws(CorruptGathers(layer => ChangeGatherRead(layer, read => read with { Slot = -1 })), "gather read must follow its slot resolution");
        Js.Throws(
            CorruptGathers(layer => ChangeGatherRead(layer, read => read with { CandidatePositions = UnsignedIndices.Empty })),
            "gather read needs at least one candidate");
        Js.Throws(
            CorruptGathers(layer => ChangeGatherRead(layer, read => read with { CandidatePositions = UnsignedIndices.From([2, 6]) })),
            "gather candidate is not available");
    }

    // Handwired validateTensorProgram ends with "every gather slot must be resolved" (:3183-3185).
    [Fact]
    public void A_gather_slot_that_no_layer_resolves_is_refused()
    {
        var (circuit, program, _) = BuildGathers();
        Js.Throws(() => circuit.ValidateTensorProgram(program with { GatherSlotCount = 2 }), "every gather slot must be resolved");
        // A program with no gathers that states one slot.
        var (denseCircuit, denseProgram) = Build();
        Assert.Equal(0, denseProgram.GatherSlotCount);
        Js.Throws(() => denseCircuit.ValidateTensorProgram(denseProgram with { GatherSlotCount = 1 }), "every gather slot must be resolved");
        // Handwired new Uint8Array(-1) throws a RangeError. This port refuses the count with a message.
        Js.Throws(() => denseCircuit.ValidateTensorProgram(denseProgram with { GatherSlotCount = -1 }), "gather slot count must be a nonnegative integer");
    }

    // An index read output row that a gather read also writes.
    [Fact]
    public void An_index_read_on_a_gather_output_row_is_refused()
    {
        var circuit = new Circuit(5);
        Wire[] bits = [circuit.Source(0), circuit.Source(1)];
        circuit.AssumeOneHot(bits);
        circuit.AssumeRange(circuit.Source(2), 0, 100);
        circuit.AssumeRange(circuit.Source(3), 0, 100);
        circuit.AssumeRange(circuit.Source(4), 0, 1);
        var read = circuit.Affine([(circuit.Multiply(bits[0], circuit.Source(2)), 1), (circuit.Multiply(bits[1], circuit.Source(3)), 1)]);
        var p = circuit.RoundAffine([(circuit.Source(4), 1)]);
        var indexed = circuit.IndexRead(p, [circuit.Source(2), circuit.Source(3)]);
        var program = circuit.CompileTensorProgram([read, indexed]);
        circuit.ValidateTensorProgram(program);
        var layer = program.Layers[1];
        Assert.Single(layer.GatherReads);
        Assert.Single(layer.IndexReads);
        var changed = layer with { IndexReads = layer.IndexReads.SetItem(0, layer.IndexReads[0] with { Row = layer.GatherReads[0].Row }) };
        Js.Throws(
            () => circuit.ValidateTensorProgram(program with { Layers = program.Layers.SetItem(1, changed) }),
            "index read output row must be unique");
    }
}
