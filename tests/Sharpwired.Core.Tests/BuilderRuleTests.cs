// Handwired source: src/runtime/numeric-ui-core.ts (append, normalizeTerms, composeAffineTerms,
// affineShareThreshold, serializeExpression, serializeMacro, multiply, select).
// These tests isolate builder rules that the Handwired tests use together.

namespace Sharpwired.Core.Tests;

public class BuilderRuleTests
{
    [Fact]
    public void Terms_of_one_wire_merge_in_their_given_order_after_a_stable_sort()
    {
        var circuit = new Circuit(2);
        var s0 = circuit.Source(0);
        var s1 = circuit.Source(1);
        // In float64, (1e16 + -1e16) + 1 is 1, and (1e16 + 1) + -1e16 is 0.
        var sum = circuit.Affine([(s0, 1e16), (s1, 1), (s0, -1e16), (s0, 1)]);
        Assert.Equal<Term>([new Term(s0, 1), new Term(s1, 1)], circuit.Operations[sum.Position - circuit.SourceSize].LeftTerms);
        var constant = circuit.Affine([(s0, 1e16), (s1, 0), (s0, 1), (s0, -1e16)], 2);
        Assert.Empty(circuit.Operations[constant.Position - circuit.SourceSize].LeftTerms);
        Assert.Equal(2, circuit.Constants[constant.Position]);
    }

    [Fact]
    public void Affine_producers_with_fewer_than_4_terms_compose_and_wider_producers_stay_wires()
    {
        var circuit = new Circuit(4);
        var three = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1)], 1);
        var four = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1), (circuit.Source(3), 1)]);
        var readsThree = circuit.Affine([(three, 2), (circuit.Source(3), 1)]);
        var readsFour = circuit.Affine([(four, 2)], 1);
        Assert.Equal<Term>(
            [new Term(circuit.Source(0), 2), new Term(circuit.Source(1), 2), new Term(circuit.Source(2), 2), new Term(circuit.Source(3), 1)],
            circuit.Operations[readsThree.Position - circuit.SourceSize].LeftTerms);
        Assert.Equal(2, circuit.Operations[readsThree.Position - circuit.SourceSize].LeftBias);
        Assert.Equal<Term>([new Term(four, 2)], circuit.Operations[readsFour.Position - circuit.SourceSize].LeftTerms);
        // A RoundAffine producer never composes.
        var rounded = circuit.RoundAffine([(circuit.Source(0), 1)], 1);
        var readsRounded = circuit.Affine([(rounded, 3)]);
        Assert.Equal<Term>([new Term(rounded, 3)], circuit.Operations[readsRounded.Position - circuit.SourceSize].LeftTerms);
    }

    [Fact]
    public void A_RoundAffine_constant_is_not_a_constant_wire_and_does_not_fold()
    {
        var circuit = new Circuit(1);
        var kept = circuit.RoundAffine([], 3);
        Assert.Null(circuit.Constants[kept.Position]);
        var reader = circuit.Affine([(circuit.Source(0), 1), (kept, 2)]);
        Assert.Equal<Term>([new Term(circuit.Source(0), 1), new Term(kept, 2)], circuit.Operations[reader.Position - circuit.SourceSize].LeftTerms);
        Assert.True(circuit.HasReaders[kept.Position]);
    }

    [Fact]
    public void Positive_and_negative_zero_constants_share_one_wire_as_in_Handwired()
    {
        var circuit = new Circuit(1);
        var zero = circuit.Constant(0);
        Assert.Equal(zero, circuit.Constant(-0.0));
        // A reglu with an empty left side and a zero bias folds to the constant 0.
        Assert.Equal(zero, circuit.Reglu([], 0, [(circuit.Source(0), 1)], 1));
        Assert.False(circuit.HasReaders[0]);
    }

    [Fact]
    public void Round_affine_and_macro_records_are_part_of_the_expression_key()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var sum = circuit.Affine([(circuit.Source(1), 1), (circuit.Source(2), 1)]);
        var roundedSum = circuit.RoundAffine([(circuit.Source(1), 1), (circuit.Source(2), 1)]);
        Assert.NotEqual(sum, roundedSum);
        Assert.Equal(roundedSum, circuit.RoundAffine([(circuit.Source(1), 1), (circuit.Source(2), 1)]));

        var product = circuit.Multiply(circuit.Source(1), circuit.Source(0));
        Assert.Equal(product, circuit.Multiply(circuit.Source(1), circuit.Source(0)));
        Assert.Equal(new MultiplyMacro(circuit.Source(1), circuit.Source(0)), circuit.Operations[product.Position - circuit.SourceSize].Macro);
        // The same ReGLU without the record is another operation.
        var plain = circuit.Append(OperationKind.Reglu, [(circuit.Source(1), 1)], 0, [(circuit.Source(0), 1)], 0, preserveRounding: true);
        Assert.NotEqual(product, plain);

        var selected = circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2));
        Assert.Equal(selected, circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2)));
        var selection = circuit.Operations[selected.Position - circuit.SourceSize];
        Assert.Equal(new SelectMacro(circuit.Source(0), circuit.Source(1), circuit.Source(2)), selection.Macro);
        Assert.True(selection.PreserveRounding);
        Assert.Equal(OperationKind.Affine, selection.Kind);
    }

    [Fact]
    public void A_multiply_of_two_signed_operands_is_two_ReGLUs_and_a_recorded_difference()
    {
        var circuit = new Circuit(2);
        var left = circuit.Source(0);
        var right = circuit.Source(1);
        var product = circuit.Multiply(left, right);
        Assert.Equal(3, circuit.Operations.Count);
        var positive = circuit.Operations[0];
        var negative = circuit.Operations[1];
        var difference = circuit.Operations[2];
        Assert.Equal((OperationKind.Reglu, true, (MacroRecord?)null), (positive.Kind, positive.PreserveRounding, positive.Macro));
        Assert.Equal<Term>([new Term(right, 1)], positive.GateTerms);
        Assert.Equal<Term>([new Term(right, -1)], negative.GateTerms);
        Assert.Equal<Term>([new Term(Wire.FromPosition(2), 1), new Term(Wire.FromPosition(3), -1)], difference.LeftTerms);
        Assert.Equal(new MultiplyMacro(left, right), difference.Macro);
        Js.Is(6, circuit.EvaluateReference([-3, -2])[product.Position]);
        Js.Is(-6, circuit.EvaluateReference([-3, 2])[product.Position]);
    }

    [Fact]
    public void Depth_is_one_more_than_the_deepest_wire_that_the_operation_reads()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var first = circuit.RoundAffine([(circuit.Source(0), 1)]);
        var second = circuit.RoundAffine([(first, 1)]);
        var product = circuit.Reglu([(circuit.Source(1), 1)], 0, [(second, 1)], 0);
        var bit = circuit.Boolean(second);
        Assert.Equal([0, 0, 1, 2, 3, 3], circuit.Depths);
        Assert.Equal(3, circuit.Operations[product.Position - circuit.SourceSize].Depth);
        Assert.Equal(3, circuit.Operations[bit.Position - circuit.SourceSize].Depth);
    }
}
