// Handwired source: tests/runtime/numeric-ui-core.test.ts.
// Each test name holds the Handwired test name. A test that compares with the
// tensor evaluator keeps its reference-evaluator part here. PHASE2-TENSOR-ASSERTIONS.md
// lists the tensor assertions.

namespace Sharpwired.Core.Tests;

public class CircuitTests
{
    // 'tensor layers append only their new values' (reference part).
    [Fact]
    public void Tensor_layers_append_only_their_new_values_reference_part()
    {
        var circuit = new Circuit(2);
        var first = circuit.Affine([(circuit.Source(0), 2)]);
        circuit.Affine([(first, 4)]);
        circuit.Affine([(circuit.Source(1), 3)]);

        var values = circuit.EvaluateReference([5, 7]);
        Assert.Equal([5f, 7f, 10f, 40f, 21f], values);
    }

    // 'the reference and tensor evaluators round every arithmetic step' (reference part).
    [Fact]
    public void The_reference_evaluator_rounds_every_arithmetic_step()
    {
        var circuit = new Circuit(2);
        var sum = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);

        var reference = circuit.EvaluateReference([16_777_216, 1]);
        Js.Is(16_777_216, reference[sum.Position]);
    }

    // 'tensor and reference evaluators both reject a wrong-sized source table' (reference part).
    [Fact]
    public void The_reference_evaluator_rejects_a_wrong_sized_source_table()
    {
        var circuit = new Circuit(2);
        circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);

        foreach (var source in new[] { new float[] { 7 }, new float[] { 7, 8, 9 } })
        {
            Js.Throws(() => circuit.EvaluateReference(source), "source table has the wrong length");
        }
    }

    // 'weighted sums normalize duplicate, zero, and unordered terms'.
    [Fact]
    public void Weighted_sums_normalize_duplicate_zero_and_unordered_terms()
    {
        var circuit = new Circuit(3);
        circuit.Affine([
            (circuit.Source(2), 0),
            (circuit.Source(1), 4),
            (circuit.Source(0), 3),
            (circuit.Source(1), -1),
            (circuit.Source(0), -3),
            (circuit.Source(2), 5),
        ], 7);

        Assert.Equal<Term>([new Term(Wire.FromPosition(1), 3), new Term(Wire.FromPosition(2), 5)], circuit.Operations[0].LeftTerms);
        Js.Is(131, circuit.EvaluateReference([11, 13, 17])[3]);
    }

    // 'a sum shared by many consumers stays one materialized wire' (builder part).
    // Handwired counts the stored weights of the compiled program. This port counts the
    // terms that the operations store, which the compiler copies into its weights.
    [Fact]
    public void A_sum_shared_by_many_consumers_stays_one_materialized_wire()
    {
        static int SharedSumTerms(int n)
        {
            var circuit = new Circuit(n + n);
            for (var wire = 0; wire < n; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
            var shared = circuit.Affine(Enumerable.Range(0, n).Select(wire => new Term(Wire.FromPosition(wire), 1)).ToArray());
            for (var item = 0; item < n; item++)
            {
                var output = circuit.Affine([(shared, 0.5), (Wire.FromPosition(n + item), 1)]);
                Assert.Contains(circuit.Operations[output.Position - circuit.SourceSize].LeftTerms, term => term.Wire == shared);
            }
            return circuit.Operations.Sum(operation => operation.LeftTerms.Length + operation.GateTerms.Length);
        }

        Assert.True(SharedSumTerms(64) < 3 * SharedSumTerms(32));
        Assert.True(SharedSumTerms(128) < 3 * SharedSumTerms(64));
    }

    // 'wide affine sharing is independent of consumer order'.
    [Fact]
    public void Wide_affine_sharing_is_independent_of_consumer_order()
    {
        float[] source = [
            (float)-0.00002327098263776861,
            (float)-6.589837084902683e-7,
            (float)0.0015497038839384913,
            (float)-0.02216961793601513,
        ];

        static (Circuit Circuit, Wire Live) Build(bool withUnusedConsumer)
        {
            var circuit = new Circuit(4);
            var shared = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1), (circuit.Source(3), 1)]);
            if (withUnusedConsumer) circuit.Affine([(shared, 2)], 1);
            var live = circuit.Affine([(shared, 2)], 1);
            return (circuit, live);
        }

        var clean = Build(false);
        var withUnusedConsumer = Build(true);
        Js.Is(
            clean.Circuit.EvaluateReference(source)[clean.Live.Position],
            withUnusedConsumer.Circuit.EvaluateReference(source)[withUnusedConsumer.Live.Position]);
    }

    // 'identical consumers of a wide affine share one wire'.
    [Fact]
    public void Identical_consumers_of_a_wide_affine_share_one_wire()
    {
        var circuit = new Circuit(4);
        var shared = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1), (circuit.Source(3), 1)]);
        var first = circuit.Affine([(shared, 2)], 1);
        var second = circuit.Affine([(shared, 2)], 1);

        Assert.Equal(first, second);
    }

    // 'duplicate-wire terms merge into one exact FP32 coefficient' (reference part).
    [Fact]
    public void Duplicate_wire_terms_merge_into_one_exact_FP32_coefficient()
    {
        const double merged = 1.0 / 3 + 0.1;
        var circuit = new Circuit(1);
        var sum = circuit.Affine([(circuit.Source(0), 1.0 / 3), (circuit.Source(0), 0.1)]);
        Assert.Equal<Term>([new Term(Wire.FromPosition(0), merged)], circuit.Operations[0].LeftTerms);

        // Handwired computes these two values in float64 with x as a float64 value.
        const double x = -17.309;
        var expected = Js.F(Js.F(merged) * x);
        var unmerged = Js.F(Js.F(Js.F(1.0 / 3) * x) + Js.F(Js.F(0.1) * x));
        Assert.NotEqual(expected, unmerged);
        Js.Is(expected, circuit.EvaluateReference([(float)x])[sum.Position]);
    }

    // 'constant terms and constant gates fold before compilation' (reference part).
    [Fact]
    public void Constant_terms_and_constant_gates_fold_before_compilation()
    {
        var circuit = new Circuit(1);
        var three = circuit.Affine([], 3);
        circuit.Affine([(circuit.Source(0), 4), (three, 2)], 1);
        circuit.Reglu([(circuit.Source(0), 2)], 5, [(three, 1)], -1);
        circuit.Reglu([(circuit.Source(0), 7)], 11, [(three, -1)], 2);

        var source = Wire.FromPosition(0);
        Assert.Equal(
            ["Affine [] 3", "Affine [0:4] 7", "Affine [0:4] 10", "Affine [] 0"],
            circuit.Operations.Select(operation =>
                $"{operation.Kind} [{string.Join(",", operation.LeftTerms.Select(term => $"{term.Wire.Position}:{term.Coefficient}"))}] {operation.LeftBias}"));
        Assert.All(circuit.Operations, operation => Assert.Empty(operation.GateTerms));
        Assert.Equal(source, circuit.Operations[1].LeftTerms[0].Wire);
        Assert.Equal([3f, 59f, 62f, 0f], circuit.EvaluateReference([13]).Skip(1));
    }

    // 'identity affine operations return an alias without allocating a wire'.
    [Fact]
    public void Identity_affine_operations_return_an_alias_without_allocating_a_wire()
    {
        var circuit = new Circuit(2);
        var alias = circuit.Affine([(circuit.Source(1), 1)]);

        Assert.Equal(1, alias.Position);
        Assert.Empty(circuit.Operations);
        Assert.Equal(2, circuit.Width);
    }

    // 'affine forms compose through affine producers'.
    [Fact]
    public void Affine_forms_compose_through_affine_producers()
    {
        var circuit = new Circuit(2);
        var first = circuit.Affine([(circuit.Source(0), 2), (circuit.Source(1), -1)], 3);
        var second = circuit.Affine([(first, 4), (circuit.Source(1), 5)], -2);
        circuit.Reglu([(second, 2)], 1, [(first, -3)], 7);

        var s0 = Wire.FromPosition(0);
        var s1 = Wire.FromPosition(1);
        var composed = circuit.Operations[1];
        Assert.Equal(OperationKind.Affine, composed.Kind);
        Assert.Equal<Term>([new Term(s0, 8), new Term(s1, 1)], composed.LeftTerms);
        Assert.Equal(10, composed.LeftBias);
        Assert.Empty(composed.GateTerms);
        Assert.Equal(0, composed.GateBias);
        Assert.Equal(1, composed.Depth);
        Assert.False(composed.PreserveRounding);
        Assert.Null(composed.Macro);
        Assert.Null(composed.IndexRead);
        Assert.Null(composed.RoundingPoint);
        Assert.False(composed.ComparisonBit);
        Assert.Equal<Term>([new Term(s0, 16), new Term(s1, 2)], circuit.Operations[2].LeftTerms);
        Assert.Equal<Term>([new Term(s0, -6), new Term(s1, 3)], circuit.Operations[2].GateTerms);
    }

    // 'identical normalized expressions share one wire'.
    [Fact]
    public void Identical_normalized_expressions_share_one_wire()
    {
        var circuit = new Circuit(2);
        var firstAffine = circuit.Affine([(circuit.Source(1), 3), (circuit.Source(0), 2)], 4);
        var secondAffine = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 3), (circuit.Source(0), 1)], 4);
        var firstReglu = circuit.Reglu([(firstAffine, 2)], 1, [(circuit.Source(1), -1)], 3);
        var secondReglu = circuit.Reglu([(secondAffine, 2)], 1, [(circuit.Source(1), -1)], 3);

        Assert.Equal(firstAffine, secondAffine);
        Assert.Equal(firstReglu, secondReglu);
        Assert.Equal(2, circuit.Operations.Count);
    }

    // 'tensor roots remove operations that cannot affect an output' (reference part).
    [Fact]
    public void Tensor_roots_remove_operations_that_cannot_affect_an_output_reference_part()
    {
        var circuit = new Circuit(2);
        var liveDependency = circuit.Reglu([(circuit.Source(0), 2)], 0, [(circuit.Source(1), 1)], 0);
        var liveOutput = circuit.Affine([(liveDependency, 1)], 3);
        circuit.Reglu([(circuit.Source(0), 7)], 11, [(circuit.Source(1), -2)], 5);

        Assert.Equal(5, circuit.Width);
        Js.Is(445, circuit.EvaluateReference([13, 17])[liveOutput.Position]);
    }

    // 'affine outputs do not allocate fake gate rows' (reference part).
    [Fact]
    public void Affine_outputs_do_not_allocate_fake_gate_rows_reference_part()
    {
        var circuit = new Circuit(2);
        var affine = circuit.Affine([(circuit.Source(0), 2)], 1);
        var reglu = circuit.Reglu([(circuit.Source(0), 3)], 2, [(circuit.Source(1), -1)], 5);

        Assert.Equal(OperationKind.Affine, circuit.Operations[0].Kind);
        Assert.Equal(OperationKind.Reglu, circuit.Operations[1].Kind);
        var values = circuit.EvaluateReference([7, 2]);
        Js.Is(15, values[affine.Position]);
        Js.Is(69, values[reglu.Position]);
    }

    // 'boolean outputs make complete decisions with an explicit tie rule' (reference part).
    [Fact]
    public void Boolean_outputs_make_complete_decisions_with_an_explicit_tie_rule()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var decision = circuit.Boolean(circuit.Source(0));

        Assert.Equal(OperationKind.Boolean, circuit.Operations[0].Kind);
        foreach (var (score, expected) in new (float, float)[] { (0, 0), (0.49f, 0), (0.5f, 1), (0.51f, 1), (1, 1) })
        {
            Js.Is(expected, circuit.EvaluateReference([score])[decision.Position]);
        }
    }

    // 'reference backpropagation follows affine and ReGLU atoms'.
    [Fact]
    public void Reference_backpropagation_follows_affine_and_ReGLU_atoms()
    {
        var circuit = new Circuit(3);
        var output = circuit.Reglu(
            [(circuit.Source(0), 2), (circuit.Source(2), 1)],
            0,
            [(circuit.Source(0), 3), (circuit.Source(1), -1)],
            0);
        var values = circuit.EvaluateReference([2, 1, -1]);
        var outputGradients = new float[values.Length];
        outputGradients[output.Position] = 1;
        var gradients = circuit.BackpropagateReference(values, outputGradients);

        Js.Is(15, values[output.Position]);
        Assert.Equal([19f, -3f, 5f], gradients[..3]);
    }

    // 'reference backpropagation rejects hard boolean decisions'.
    [Fact]
    public void Reference_backpropagation_rejects_hard_boolean_decisions()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var output = circuit.Boolean(circuit.Source(0));
        var values = circuit.EvaluateReference([0.4f]);
        var outputGradients = new float[values.Length];
        outputGradients[output.Position] = 1;

        Js.Throws(() => circuit.BackpropagateReference(values, outputGradients), "cannot backpropagate through a boolean atom");
    }
}
