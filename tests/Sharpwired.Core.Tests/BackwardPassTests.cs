// Sharpwired tests of the reference backward pass (Handwired source:
// src/runtime/numeric-ui-core.ts, Circuit.backpropagateReference). Handwired has no test with
// these names. The ported Handwired backward-pass cases are in CircuitTests, NumericSemanticsTests,
// ComparisonTests, IndexReadTests.Backward and SpringMatrixTests.

namespace Sharpwired.Core.Tests;

public class BackwardPassTests
{
    // Compare the gradient of each source cell with the central difference
    // (output(cell + step) - output(cell - step)) / (2 × step). The points are away from the ReLU
    // corners, where the backward pass uses the slope 0.
    private static void CheckCentralDifferences(Circuit circuit, Wire output, float[] source, double step, double tolerance)
    {
        var values = circuit.EvaluateReference(source);
        var seeds = new float[values.Length];
        seeds[output.Position] = 1;
        var gradients = circuit.BackpropagateReference(values, seeds);
        for (var cell = 0; cell < source.Length; cell++)
        {
            var plus = (float[])source.Clone();
            var minus = (float[])source.Clone();
            plus[cell] = (float)(source[cell] + step);
            minus[cell] = (float)(source[cell] - step);
            var slope = ((double)circuit.EvaluateReference(plus)[output.Position] - circuit.EvaluateReference(minus)[output.Position])
                / ((double)plus[cell] - minus[cell]);
            Assert.True(
                Math.Abs(gradients[cell] - slope) <= tolerance * Math.Max(1, Math.Abs(slope)),
                $"cell {cell} of [{string.Join(", ", source)}]: gradient {gradients[cell]:R}, central difference {slope:R}");
        }
    }

    // A value within 0.25..4 in magnitude, with a random sign: away from 0, where a ReLU has its corner.
    private static float AwayFromZero(Func<double> random) => (float)((random() < 0.5 ? -1 : 1) * (0.25 + 3.75 * random()));

    [Fact]
    public void Multiply_gradients_match_central_differences_for_each_construction()
    {
        var random = Js.CreateRandom(71);
        foreach (var nonnegative in new bool?[] { null, false, true })
        {
            var circuit = new Circuit(2);
            if (nonnegative is { } right)
            {
                circuit.AssumeRange(circuit.Source(right ? 1 : 0), 0, 8);
            }
            var product = circuit.Multiply(circuit.Source(0), circuit.Source(1));
            // Without a declaration the macro builds three operations. With one, it builds one ReGLU.
            Assert.Equal(nonnegative is null ? 3 : 1, circuit.Operations.Count);
            for (var sample = 0; sample < 50; sample++)
            {
                float[] source = [AwayFromZero(random), AwayFromZero(random)];
                if (nonnegative is { } onRight) source[onRight ? 1 : 0] = MathF.Abs(source[onRight ? 1 : 0]);
                CheckCentralDifferences(circuit, product, source, 1.0 / 64, 1e-3);
            }
        }
    }

    [Fact]
    public void Select_gradients_match_central_differences()
    {
        var random = Js.CreateRandom(73);
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var output = circuit.Select(circuit.Source(0), circuit.Source(1), circuit.Source(2));
        for (var sample = 0; sample < 50; sample++)
        {
            float[] source = [(float)(0.1 + 0.8 * random()), AwayFromZero(random), AwayFromZero(random)];
            CheckCentralDifferences(circuit, output, source, 1.0 / 128, 1e-3);
        }
    }

    [Fact]
    public void ApplySpringMatrix_gradients_match_central_differences()
    {
        var random = Js.CreateRandom(79);
        var circuit = new Circuit(6);
        for (var cell = 0; cell < 4; cell++) circuit.AssumeRange(circuit.Source(cell), -8, 8);
        var next = circuit.ApplySpringMatrix(
            new SpringMatrixWires(circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3)),
            circuit.Source(4),
            circuit.Source(5));
        for (var sample = 0; sample < 50; sample++)
        {
            var source = Enumerable.Range(0, 6).Select(_ => AwayFromZero(random)).ToArray();
            CheckCentralDifferences(circuit, next.Displacement, source, 1.0 / 64, 1e-3);
            CheckCentralDifferences(circuit, next.Velocity, source, 1.0 / 64, 1e-3);
        }
    }

    [Fact]
    public void The_backward_pass_refuses_tables_of_the_wrong_length()
    {
        var circuit = new Circuit(1);
        var output = circuit.Affine([(circuit.Source(0), 2)]);
        var values = circuit.EvaluateReference([3]);
        var seeds = new float[values.Length];
        seeds[output.Position] = 1;
        Js.Throws(() => circuit.BackpropagateReference([3], seeds), "forward values have the wrong length");
        Js.Throws(() => circuit.BackpropagateReference(values, [1]), "output gradients have the wrong length");
        // The result is a new table: the output gradients stay as they were.
        Assert.Equal([2f, 1f], circuit.BackpropagateReference(values, seeds));
        Assert.Equal([0f, 1f], seeds);
    }
}
