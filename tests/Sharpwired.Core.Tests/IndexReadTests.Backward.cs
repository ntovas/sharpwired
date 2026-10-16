// Handwired source: tests/runtime/index-read.test.ts (the backward pass cases).
// Each test name holds the Handwired test name.

namespace Sharpwired.Core.Tests;

public partial class IndexReadTests
{
    // 'backward pass: the selected candidate gets the gradient; the index gradient is rejected by default'.
    [Fact]
    public void Backward_pass_the_selected_candidate_gets_the_gradient_and_the_index_gradient_is_rejected_by_default()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), -2, 5);
        var candidates = Wires(1, 2, 3);
        var read = circuit.IndexRead(circuit.Source(0), candidates, OutOfRange.Zero);
        var readAgain = circuit.IndexReadAffine([(circuit.Source(0), 1)], 0, candidates, OutOfRange.Clamp, OutOfRange.Clamp);
        var sum = circuit.RoundAffine([(read, 3), (readAgain, 1)]);
        float[] OutputGradients(float[] values)
        {
            var gradients = new float[values.Length];
            gradients[sum.Position] = 1;
            return gradients;
        }
        var values = circuit.EvaluateReference([1, 10, 20, 40]);
        Js.Throws(() => circuit.BackpropagateReference(values, OutputGradients(values)), "indexGradient");
        var zeroGradients = circuit.BackpropagateReference(values, OutputGradients(values), indexGradient: IndexGradient.Zero);
        // Both reads select candidate 1, so its gradient adds, like a scatter-add.
        Assert.Equal([0f, 0f, 4f, 0f], zeroGradients[..4]);
        var differenceGradients = circuit.BackpropagateReference(values, OutputGradients(values), indexGradient: IndexGradient.BackwardDifference);
        Assert.Equal([3 * (20 - 10) + (20 - 10), 0f, 4f, 0f], differenceGradients[..4]);
        // Past the end, the 'zero' read selects nothing and the clamped read selects the last candidate.
        var pastEnd = circuit.EvaluateReference([4, 10, 20, 40]);
        var pastEndGradients = circuit.BackpropagateReference(pastEnd, OutputGradients(pastEnd), indexGradient: IndexGradient.BackwardDifference);
        Assert.Equal([0f, 0f, 0f, 1f], pastEndGradients[..4]);
        var atEnd = circuit.EvaluateReference([3, 10, 20, 40]);
        var atEndGradients = circuit.BackpropagateReference(atEnd, OutputGradients(atEnd), indexGradient: IndexGradient.BackwardDifference);
        Assert.Equal([3 * (0 - 40) + (40 - 40), 0f, 0f, 1f], atEndGradients[..4]);
        // A zero output gradient never reaches the read, so it does not throw.
        circuit.BackpropagateReference(values, new float[values.Length]);
    }

    // 'backward-difference equals the reference backward pass of both dense constructions'.
    [Fact]
    public void Backward_difference_equals_the_reference_backward_pass_of_both_dense_constructions()
    {
        var random = Js.CreateRandom(0xbac4);
        foreach (var style in new[] { DenseStyle.Clamp, DenseStyle.ZeroAbove })
        {
            const int count = 6;
            var pair = BuildPair(count, style, 1000);
            var sourceSize = 1 + count;
            foreach (var integerTable in new[] { true, false })
            {
                var tableValues = Enumerable.Range(0, count)
                    .Select(position => integerTable ? 10 * (position + 1) + position * position : (float)((random() * 2 - 1) * 100))
                    .ToArray();
                for (var index = -3; index <= count + 2; index++)
                {
                    float[] source = [index, .. tableValues];
                    var denseValues = pair.Dense.EvaluateReference(source);
                    var denseOutputGradients = new float[denseValues.Length];
                    denseOutputGradients[pair.DenseRead.Position] = 1;
                    var denseGradients = pair.Dense.BackpropagateReference(denseValues, denseOutputGradients);
                    var instructionValues = pair.Instruction.EvaluateReference(source);
                    var instructionOutputGradients = new float[instructionValues.Length];
                    instructionOutputGradients[pair.InstructionRead.Position] = 1;
                    var instructionGradients = pair.Instruction.BackpropagateReference(
                        instructionValues,
                        instructionOutputGradients,
                        indexGradient: IndexGradient.BackwardDifference);
                    for (var wire = 0; wire < sourceSize; wire++)
                    {
                        if (integerTable || wire != 0)
                        {
                            Js.Is(denseGradients[wire], instructionGradients[wire]);
                        }
                        else
                        {
                            // The dense pass adds and cancels one pair of terms per step, which
                            // rounds in FP32 when the table holds fractions.
                            Assert.True(Math.Abs(instructionGradients[wire] - denseGradients[wire]) < 1e-3);
                        }
                    }
                }
            }
        }
    }
}
