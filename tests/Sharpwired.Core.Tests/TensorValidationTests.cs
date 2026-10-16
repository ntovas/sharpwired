// Handwired source: src/runtime/numeric-ui-core.ts validateTensorProgram. Handwired has no test for
// most of its messages. Each test here changes one field of a compiled program with a with
// expression, and checks that ValidateTensorProgram refuses the copy with the Handwired message.
// index-read.test.ts 'validators reject malformed index read programs and circuits' is in
// IndexReadTests. The gather messages come with gather lowering.

namespace Sharpwired.Core.Tests;

public class TensorValidationTests
{
    // Two layers. Layer 0: an affine (row 0), a ReGLU (row 1, gate row 4), an index read (row 2,
    // index row 5) and a boolean atom (row 3), over the inputs 0..3. Layer 1: a ReGLU that reads the
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
}
