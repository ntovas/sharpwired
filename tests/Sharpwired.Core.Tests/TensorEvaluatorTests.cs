// Handwired source: tests/runtime/numeric-ui-core.test.ts ("a program's evaluator exposes the packed
// values of its last evaluation: the source cells, then every live operation in wireOrder"), and
// src/runtime/numeric-ui-core.ts (Circuit.evaluateTensor, Circuit.createTensorEvaluator,
// Circuit.createTensorProgramEvaluator, and the resets of Circuit.tensorProgram in assumeOneHot,
// indexRead and append). Handwired has no test for the other cases here.

namespace Sharpwired.Core.Tests;

public class TensorEvaluatorTests
{
    // "a program's evaluator exposes the packed values of its last evaluation: the source cells,
    // then every live operation in wireOrder".
    [Fact]
    public void A_programs_evaluator_exposes_the_packed_values_of_its_last_evaluation_the_source_cells_then_every_live_operation_in_wireOrder()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var product = circuit.Reglu([(sum, 1)], 0, [(circuit.Source(0), 1)], 0);
        var program = circuit.CompileTensorProgram([product]);
        var evaluator = circuit.CreateTensorProgramEvaluator(program);
        Assert.Equal([10f], evaluator.Evaluate([2, 3]));
        // The compiler removed the operation that no root reads, so it has no packed value.
        Assert.Equal<int>([0, 1, sum.Position, product.Position], program.WireOrder);
        Assert.DoesNotContain(unused.Position, program.WireOrder);
        var packedValues = evaluator.PackedValues;
        Assert.Equal([2f, 3f, 5f, 10f], packedValues);
        // The next evaluation overwrites the same array.
        evaluator.Evaluate([4, 1]);
        Assert.Same(packedValues, evaluator.PackedValues);
        Assert.Equal([4f, 1f, 5f, 20f], packedValues);
    }

    // The FP32 rule of the sparse multiply: each row is f(bias), then f(value + f(weight × input))
    // for each stored weight, in stored term order. The source values make a skipped round of a
    // product or of a sum give a different value.
    [Fact]
    public void Each_row_rounds_the_bias_each_product_and_each_sum_in_stored_term_order()
    {
        var circuit = new Circuit(3);
        var sum = circuit.RoundAffine([(circuit.Source(2), 0.7), (circuit.Source(0), 0.1), (circuit.Source(1), -1.0 / 3)], 1.0 / 3);
        float[] source = [1.1f, 3.3f, 7.77f];

        var expected = Js.F(1.0 / 3);
        foreach (var (wire, coefficient) in new[] { (2, 0.7), (0, 0.1), (1, -1.0 / 3) })
        {
            expected = Js.F(expected + Js.F(Js.F(coefficient) * source[wire]));
        }
        Js.Is(expected, circuit.EvaluateReference(source)[sum.Position]);
        Js.Is(expected, circuit.EvaluateTensor(source)[sum.Position]);
        Js.Is(expected, circuit.CreateTensorEvaluator([sum]).Evaluate(source)[0]);
    }

    // Handwired createTensorProgramEvaluator returns one outputs array, and each call overwrites it.
    [Fact]
    public void Evaluate_returns_the_roots_in_root_order_in_one_array_that_each_call_overwrites()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var product = circuit.Reglu([(sum, 1)], 0, [(circuit.Source(0), 1)], 0);
        var program = circuit.CompileTensorProgram([product, circuit.Source(1), sum]);
        var evaluator = circuit.CreateTensorProgramEvaluator(program);

        Assert.Same(program, evaluator.Program);
        var first = evaluator.Evaluate([2, 3]);
        Assert.Equal([10f, 3f, 5f], first);
        var second = evaluator.Evaluate([4, 1]);
        Assert.Same(first, second);
        Assert.Equal([20f, 1f, 5f], second);
    }

    // Handwired createTensorEvaluator compiles the roots, with recomputeDepths when the caller asks.
    [Fact]
    public void Create_tensor_evaluator_compiles_the_roots_with_or_without_recomputed_depths()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var product = circuit.Reglu([(sum, 1)], 0, [(circuit.Source(0), 1)], 0);

        foreach (var recomputeDepths in new[] { false, true })
        {
            var evaluator = circuit.CreateTensorEvaluator([product, sum], recomputeDepths);
            TensorChecks.AssertSameProgram(circuit.CompileTensorProgram([product, sum], recomputeDepths), evaluator.Program);
            Assert.DoesNotContain(unused.Position, evaluator.Program.WireOrder);
            Assert.Equal([10f, 5f], evaluator.Evaluate([2, 3]));
        }
        Assert.Null(circuit.TensorProgram);
    }

    // Handwired wireOrder.indexOf(root) gives -1 for a root that the program does not hold, and the
    // read of packedValues[-1] gives NaN. Only a hand-made program can have such a root.
    [Fact]
    public void A_root_that_the_program_does_not_hold_reads_NaN()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var program = circuit.CompileTensorProgram([sum]);
        var changed = program with { Roots = UnsignedIndices.From([sum.Position, unused.Position]) };

        var output = circuit.CreateTensorProgramEvaluator(changed).Evaluate([2, 3]);
        Js.Is(5, output[0]);
        Js.Is(double.NaN, output[1]);
    }

    // Handwired createTensorProgramEvaluator calls validateSourceRanges on each call.
    [Fact]
    public void The_reusable_evaluator_checks_the_source_length_and_the_source_ranges()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var evaluator = circuit.CreateTensorEvaluator([sum]);

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate([1]));
        Js.Throws(() => evaluator.Evaluate([1, 2, 3]), "source table has the wrong length");
        Js.Throws(() => evaluator.Evaluate([2, 0]), "source wire 0 is outside its assumed range");
    }

    // Handwired evaluateTensorProgram: a new array of width values, 0 for each removed wire.
    [Fact]
    public void Evaluate_tensor_gives_0_for_each_removed_wire_and_a_new_array_on_each_call()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var program = circuit.CompileTensorProgram([sum]);

        var first = circuit.EvaluateTensor([2, 3], program);
        Assert.Equal([2f, 3f, 5f, 0f], first);
        Js.Is(0, first[unused.Position]);
        var second = circuit.EvaluateTensor([2, 3], program);
        Assert.NotSame(first, second);
        // A program passed as an argument does not become the cached program.
        Assert.Null(circuit.TensorProgram);
        Js.Throws(() => circuit.EvaluateTensor([2, 3, 4], program), "source table has the wrong length");
    }

    // Handwired evaluateTensor: tensorProgram ??= compileTensorProgram(), so the first call caches a
    // program of every operation and later calls use it.
    [Fact]
    public void Evaluate_tensor_with_no_program_caches_a_program_of_every_operation()
    {
        var circuit = new Circuit(2);
        circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        circuit.RoundAffine([(circuit.Source(0), 5)]);
        Assert.Null(circuit.TensorProgram);

        Assert.Equal([2f, 3f, 5f, 10f], circuit.EvaluateTensor([2, 3]));
        var cached = circuit.TensorProgram;
        Assert.NotNull(cached);
        TensorChecks.AssertSameProgram(circuit.CompileTensorProgram(), cached);
        circuit.EvaluateTensor([4, 1]);
        Assert.Same(cached, circuit.TensorProgram);
    }

    // A program that the caller sets is the program that EvaluateTensor runs.
    [Fact]
    public void Evaluate_tensor_runs_the_program_that_the_caller_sets()
    {
        var circuit = new Circuit(2);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var other = circuit.RoundAffine([(circuit.Source(0), 5)]);
        circuit.TensorProgram = circuit.CompileTensorProgram([sum]);

        var values = circuit.EvaluateTensor([2, 3]);
        Js.Is(5, values[sum.Position]);
        Js.Is(0, values[other.Position]);
    }

    private static (Circuit Circuit, TensorProgram Program) CachedCircuit(int sourceSize)
    {
        var circuit = new Circuit(sourceSize);
        for (var wire = 0; wire < sourceSize; wire++) circuit.AssumeRange(circuit.Source(wire), 0, 1);
        circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        circuit.EvaluateTensor(new float[sourceSize]);
        return (circuit, circuit.TensorProgram!);
    }

    // Handwired append sets tensorProgram to null after it adds the operation. An expression that the
    // builder already built, or an identity, returns before the reset.
    [Fact]
    public void Append_resets_the_cached_program_only_when_it_adds_an_operation()
    {
        var (circuit, cached) = CachedCircuit(3);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        Assert.Same(cached, circuit.TensorProgram);
        circuit.Affine([(circuit.Source(2), 1)]);
        Assert.Same(cached, circuit.TensorProgram);

        var product = circuit.Reglu([(sum, 1)], 0, [(circuit.Source(2), 1)], 0);
        Assert.Null(circuit.TensorProgram);
        Js.Is(1, circuit.EvaluateTensor([0.5f, 0.5f, 1])[product.Position]);
    }

    // Handwired assumeOneHot sets tensorProgram to null after it adds the group.
    [Fact]
    public void Assume_one_hot_resets_the_cached_program()
    {
        var (circuit, _) = CachedCircuit(3);
        circuit.AssumeOneHot([circuit.Source(1), circuit.Source(2)], allowExistingReaders: true);
        Assert.Null(circuit.TensorProgram);
    }

    // Handwired indexRead sets tensorProgram to null after it adds the operation. A read that the
    // builder already built, or a constant index, returns before the reset.
    [Fact]
    public void Index_read_resets_the_cached_program_only_when_it_adds_an_operation()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 2);
        var read = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]);
        circuit.EvaluateTensor([1, 3, 4]);
        var cached = circuit.TensorProgram;
        Assert.NotNull(cached);

        Assert.Equal(read, circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]));
        Assert.Same(cached, circuit.TensorProgram);
        var constantRead = circuit.IndexReadAffine([], 1, [circuit.Source(1), circuit.Source(2)], OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(circuit.Source(2), constantRead);
        Assert.Same(cached, circuit.TensorProgram);

        var zeroRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)], OutOfRange.Zero);
        Assert.Null(circuit.TensorProgram);
        Js.Is(4, circuit.EvaluateTensor([1, 3, 4])[zeroRead.Position]);
    }

    // Handwired assumeRange, assumeGrid, assumeBit and markRoundingPoint do not reset tensorProgram.
    // The compiler reads no grid, no exact bit and no rounding point record, and a narrower range
    // cannot make the cached dense program wrong.
    [Fact]
    public void Range_grid_bit_and_rounding_point_declarations_keep_the_cached_program()
    {
        var (circuit, cached) = CachedCircuit(3);
        var sum = Wire.FromPosition(3);
        circuit.AssumeRange(circuit.Source(2), 0, 0.5);
        circuit.AssumeRange(sum, 0, 1, allowExistingReaders: true);
        circuit.AssumeGrid(circuit.Source(2), 0.25);
        circuit.AssumeBit(circuit.Source(2));
        circuit.MarkRoundingPoint(sum);
        Assert.Same(cached, circuit.TensorProgram);
    }

    // Handwired inlineCircuit resets tensorProgram through append, indexRead and assumeOneHot.
    [Fact]
    public void Inline_circuit_resets_the_cached_program_when_it_adds_an_operation()
    {
        var child = new Circuit(2);
        var childSum = child.RoundAffine([(child.Source(0), 1), (child.Source(1), 1)]);
        var (parent, cached) = CachedCircuit(2);

        var sameMapping = parent.InlineCircuit(child, [parent.Source(0), parent.Source(1)]);
        Assert.Same(cached, parent.TensorProgram);

        var swappedMapping = parent.InlineCircuit(child, [parent.Source(1), parent.Source(0)]);
        Assert.Null(parent.TensorProgram);
        Assert.NotEqual(sameMapping[childSum.Position], swappedMapping[childSum.Position]);
    }
}
