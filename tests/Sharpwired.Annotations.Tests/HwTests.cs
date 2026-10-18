// No Handwired source. Each test compares the plain body of one Hw method with its macro in
// Sharpwired.Core, which the reference evaluator (Circuit.EvaluateReference) runs. The comparison
// is Object.is, so +0 and -0 must also agree. The inputs are seeded, and they include 0, -0,
// subnormal values and the range ends.

using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.Timing;

namespace Sharpwired.Annotations.Tests;

public class HwTests
{
    private const int SeededCount = 2_000;

    // The toggle ranges (plan "Files", the toggle cells).
    private static readonly double MatrixBound = SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45));
    private static readonly float MatrixSample = AtOrBelow(MatrixBound);
    private const float DisplacementBound = 40;
    private const float VelocityBound = 1280;
    private const float DestinationMaximum = 20;

    private static readonly SpringMatrix ToggleStep = SpringMatrix.SpringStepMatrix(new SpringConstants(650, 45));

    // The default settle thresholds of the macro, as FP32 values.
    private static readonly float Threshold = (float)0.01;

    // The largest FP32 value at or below a float64 value.
    private static float AtOrBelow(double value)
    {
        var rounded = (float)value;
        return rounded > value ? MathF.BitDecrement(rounded) : rounded;
    }

    // A seeded FP32 value within minimum..maximum, two FP32 values. One value in 6 is an edge value
    // (a range end, 0, -0 or the smallest subnormal value). One value in 3 has a small magnitude, down
    // to the subnormal values. The other values are uniform in the range.
    private static float Sample(Func<double> random, float minimum, float maximum)
    {
        var choice = random();
        if (choice < 1.0 / 6)
        {
            float[] edges = [minimum, maximum, 0f, -0f, float.Epsilon, -float.Epsilon];
            var edge = edges[(int)(random() * edges.Length)];
            return edge >= minimum && edge <= maximum ? edge : minimum;
        }
        if (choice < 0.5)
        {
            var largest = Math.Max(Math.Abs((double)minimum), Math.Abs((double)maximum));
            var small = (float)(largest * random() * Math.ScaleB(1.0, -(int)(random() * 150)));
            var signed = random() < 0.5 ? -small : small;
            return signed >= minimum && signed <= maximum ? signed : small;
        }
        return Math.Clamp((float)(minimum + ((double)maximum - minimum) * random()), minimum, maximum);
    }

    private static bool Bit(Func<double> random) => random() < 0.5;

    private static float AsFloat(bool bit) => bit ? 1f : 0f;

    private static void AssertIs(float expected, float actual, string context) =>
        Assert.True(Js.ObjectIs(expected, actual), $"expected {expected:R}, got {actual:R}, for {context}");

    [Fact]
    public void Relu_equals_the_relu_macro_bit_for_bit()
    {
        var circuit = new Circuit(1);
        var output = circuit.Relu(circuit.Source(0));
        var random = Js.CreateRandom(41);
        var inputs = new List<float>
        {
            0f, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue,
            float.PositiveInfinity, float.NegativeInfinity, MathF.BitDecrement(float.Epsilon * 8388608f),
        };
        for (var item = 0; item < SeededCount; item++) inputs.Add(Sample(random, -float.MaxValue, float.MaxValue));
        foreach (var input in inputs)
        {
            var values = circuit.EvaluateReference([input]);
            AssertIs(values[output.Position], Hw.Relu(input), $"Relu({input:R})");
        }
    }

    [Fact]
    public void Boolean_equals_the_boolean_atom_bit_for_bit_and_refuses_a_score_outside_0_to_1()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var output = circuit.Boolean(circuit.Source(0));
        var random = Js.CreateRandom(42);
        var inputs = new List<float> { 0f, -0f, float.Epsilon, 1f, MathF.BitDecrement(1f), 0.5f, MathF.BitDecrement(0.5f), MathF.BitIncrement(0.5f) };
        for (var item = 0; item < SeededCount; item++) inputs.Add(Sample(random, 0, 1));
        var ones = 0;
        foreach (var input in inputs)
        {
            var values = circuit.EvaluateReference([input]);
            var bit = Hw.Boolean(input);
            if (bit) ones++;
            AssertIs(values[output.Position], AsFloat(bit), $"Boolean({input:R})");
        }
        Assert.InRange(ones, 100, inputs.Count - 100);
        foreach (var score in new[] { -float.Epsilon, MathF.BitIncrement(1f), float.NaN, float.PositiveInfinity })
        {
            Assert.IsType<ArgumentException>(Js.Throws(() => Hw.Boolean(score), "within 0..1"));
        }
    }

    // ApplySpringMatrix reads each product through a sum that starts at +0. Thus the macro value does
    // not depend on whether a range proves an operand nonnegative. The second row of ranges checks this.
    [Theory]
    [InlineData(-40.0, -1280.0)]
    [InlineData(0.0, 0.0)]
    public void ApplySpringMatrix_equals_the_spring_matrix_macro_bit_for_bit(double displacementMinimum, double velocityMinimum)
    {
        var circuit = new Circuit(6);
        for (var entry = 0; entry < 4; entry++) circuit.AssumeRange(circuit.Source(entry), -MatrixBound, MatrixBound);
        circuit.AssumeRange(circuit.Source(4), displacementMinimum, DisplacementBound);
        circuit.AssumeRange(circuit.Source(5), velocityMinimum, VelocityBound);
        var matrixWires = new SpringMatrixWires(circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3));
        var state = circuit.ApplySpringMatrix(matrixWires, circuit.Source(4), circuit.Source(5));
        var random = Js.CreateRandom(43);
        int[] powers = [0, 1, 4, 300];
        for (var item = 0; item < SeededCount; item++)
        {
            var matrix = item % 2 == 0
                ? SpringMatrix.SpringMatrixPower(ToggleStep, powers[item / 2 % powers.Length]).ToArray().Select(value => (float)value).ToArray()
                : [.. Enumerable.Range(0, 4).Select(_ => Sample(random, -MatrixSample, MatrixSample))];
            var displacement = Sample(random, (float)displacementMinimum, DisplacementBound);
            var velocity = Sample(random, (float)velocityMinimum, VelocityBound);
            var values = circuit.EvaluateReference([.. matrix, displacement, velocity]);
            var plain = Hw.ApplySpringMatrix(matrix, displacement, velocity);
            var context = $"matrix [{string.Join(", ", matrix.Select(value => value.ToString("R")))}], displacement {displacement:R}, velocity {velocity:R}";
            AssertIs(values[state.Displacement.Position], plain.Displacement, context);
            AssertIs(values[state.Velocity.Position], plain.Velocity, context);
        }
    }

    [Fact]
    public void ApplySpringMatrix_refuses_a_matrix_that_does_not_have_4_entries()
    {
        Assert.IsType<ArgumentException>(Js.Throws(() => Hw.ApplySpringMatrix([1, 0, 0], 0, 0), "4 entries"));
        Assert.Throws<ArgumentNullException>(() => Hw.ApplySpringMatrix(null!, 0, 0));
    }

    private sealed record AnimatedCircuit(Circuit Circuit, AnimatedObject Animated);

    // The toggle call: one spring with the toggle ranges, the default thresholds, a settle check and a snap.
    // With sameDestination, the macro gets the destination wire also as destinationDuringSteps, and it
    // skips the rebase.
    private static AnimatedCircuit BuildAnimatedObject(bool sameDestination = false)
    {
        var circuit = new Circuit(10);
        for (var entry = 0; entry < 4; entry++) circuit.AssumeRange(circuit.Source(entry), -MatrixBound, MatrixBound);
        circuit.AssumeRange(circuit.Source(4), -DisplacementBound, DisplacementBound);
        circuit.AssumeRange(circuit.Source(5), -VelocityBound, VelocityBound);
        circuit.AssumeRange(circuit.Source(6), 0, DestinationMaximum);
        circuit.AssumeRange(circuit.Source(7), 0, DestinationMaximum);
        circuit.AssumeRange(circuit.Source(8), 0, 1);
        circuit.AssumeRange(circuit.Source(9), 0, 1);
        var matrix = new SpringMatrixWires(circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3));
        var during = circuit.Source(sameDestination ? 6 : 7);
        var spring = new AnimatedSpring(matrix, circuit.Source(4), circuit.Source(5), circuit.Source(6)) { DestinationDuringSteps = during };
        var animated = circuit.AnimatedObject([spring], new AnimatedObjectOptions(circuit.Source(8)) { Snap = circuit.Source(9) });
        return new AnimatedCircuit(circuit, animated);
    }

    private static void AssertAnimatedObject(
        AnimatedCircuit built,
        float[] matrix,
        float displacement,
        float velocity,
        float destination,
        float during,
        bool settleCheck,
        bool snap)
    {
        var values = built.Circuit.EvaluateReference([.. matrix, displacement, velocity, destination, during, AsFloat(settleCheck), AsFloat(snap)]);
        var plain = Hw.AnimatedObject(matrix, displacement, velocity, destination, during, settleCheck, snap);
        var context = $"matrix [{string.Join(", ", matrix.Select(value => value.ToString("R")))}], displacement {displacement:R}, "
            + $"velocity {velocity:R}, destination {destination:R}, during {during:R}, settleCheck {settleCheck}, snap {snap}";
        var animated = built.Animated;
        AssertIs(values[animated.Positions[0].Position], plain.Position, context);
        AssertIs(values[animated.Displacements[0].Position], plain.Displacement, context);
        AssertIs(values[animated.Velocities[0].Position], plain.Velocity, context);
        AssertIs(values[animated.Active.Position], AsFloat(plain.Active), context);
    }

    [Fact]
    public void AnimatedObject_equals_the_animated_object_macro_bit_for_bit_over_the_toggle_ranges()
    {
        var built = BuildAnimatedObject();
        var random = Js.CreateRandom(44);
        int[] powers = [0, 1, 4, 300];
        var active = 0;
        for (var item = 0; item < SeededCount; item++)
        {
            var matrix = SpringMatrix.SpringMatrixPower(ToggleStep, powers[item % powers.Length]).ToArray().Select(value => (float)value).ToArray();
            var destination = Sample(random, 0, DestinationMaximum);
            // One case in 4 keeps the destination, so the rebase adds exactly 0.
            var during = random() < 0.25 ? destination : Sample(random, 0, DestinationMaximum);
            var settleCheck = Bit(random);
            var snap = random() < 0.2;
            var displacement = Sample(random, -DisplacementBound, DisplacementBound);
            var velocity = Sample(random, -VelocityBound, VelocityBound);
            AssertAnimatedObject(built, matrix, displacement, velocity, destination, during, settleCheck, snap);
            if (Hw.AnimatedObject(matrix, displacement, velocity, destination, during, settleCheck, snap).Active) active++;
        }
        // Both values of the active bit occur.
        Assert.InRange(active, 100, SeededCount - 100);
    }

    [Fact]
    public void AnimatedObject_equals_the_macro_at_the_settle_thresholds_and_at_rest()
    {
        var built = BuildAnimatedObject();
        // The identity matrix keeps the displacement and the velocity, so each value meets its threshold as written.
        var identity = SpringMatrix.SpringMatrixPower(ToggleStep, 0).ToArray().Select(value => (float)value).ToArray();
        float[] edges =
        [
            0f, -0f, float.Epsilon, -float.Epsilon,
            Threshold, MathF.BitIncrement(Threshold), MathF.BitDecrement(Threshold),
            -Threshold, -MathF.BitIncrement(Threshold), -MathF.BitDecrement(Threshold),
            DisplacementBound, -DisplacementBound,
        ];
        var settledCount = 0;
        foreach (var displacement in edges)
        {
            foreach (var velocity in edges)
            {
                foreach (var settleCheck in new[] { false, true })
                {
                    foreach (var snap in new[] { false, true })
                    {
                        foreach (var (destination, during) in new[] { (20f, 20f), (0f, 0f), (20f, 0f), (0f, 20f) })
                        {
                            AssertAnimatedObject(built, identity, displacement, velocity, destination, during, settleCheck, snap);
                            if (!Hw.AnimatedObject(identity, displacement, velocity, destination, during, settleCheck, snap).Active) settledCount++;
                        }
                    }
                }
            }
        }
        Assert.InRange(settledCount, 1, edges.Length * edges.Length * 16 - 1);
    }

    // The macro skips the rebase when destination and destinationDuringSteps are one wire, and the
    // body always rebases. For finite values the outputs are still equal bit for bit (final fix wave,
    // item F). destination - destination is +0, and the displacement after the spring step is never
    // -0. The identity matrix and the zero displacements and velocities test the zero cases.
    [Fact]
    public void AnimatedObject_with_one_wire_as_both_destinations_equals_the_macro_bit_for_bit()
    {
        var built = BuildAnimatedObject(sameDestination: true);
        var random = Js.CreateRandom(45);
        int[] powers = [0, 1, 4, 300];
        float[] displacements = [0f, -0f, float.Epsilon, -float.Epsilon, Threshold, -Threshold];
        for (var item = 0; item < SeededCount; item++)
        {
            var matrix = SpringMatrix.SpringMatrixPower(ToggleStep, powers[item % powers.Length]).ToArray().Select(value => (float)value).ToArray();
            var destination = item % 3 == 0 ? 0f : Sample(random, 0, DestinationMaximum);
            var displacement = item % 2 == 0 ? displacements[item / 2 % displacements.Length] : Sample(random, -DisplacementBound, DisplacementBound);
            var velocity = item % 4 == 0 ? (item % 8 == 0 ? -0f : 0f) : Sample(random, -VelocityBound, VelocityBound);
            AssertAnimatedObject(built, matrix, displacement, velocity, destination, destination, Bit(random), random() < 0.2);
        }
    }

    [Fact]
    public void AnimatedObject_refuses_a_matrix_that_does_not_have_4_entries()
    {
        Assert.IsType<ArgumentException>(Js.Throws(() => Hw.AnimatedObject([1, 0, 0, 1, 0], 0, 0, 0, 0, false, false), "4 entries"));
        Assert.Throws<ArgumentNullException>(() => Hw.AnimatedObject(null!, 0, 0, 0, 0, false, false));
    }

    private static void AssertFirstActive(Circuit circuit, Wire[] selected, bool active, bool[] candidates)
    {
        var values = circuit.EvaluateReference([AsFloat(active), .. candidates.Select(AsFloat)]);
        var plain = Hw.FirstActive(active, candidates);
        Assert.Equal(selected.Length, plain.Length);
        var context = $"active {active}, candidates [{string.Join(", ", candidates)}]";
        for (var index = 0; index < selected.Length; index++) AssertIs(values[selected[index].Position], AsFloat(plain[index]), context);
    }

    private static (Circuit Circuit, Wire[] Selected) BuildFirstActive(int count)
    {
        var circuit = new Circuit(count + 1);
        for (var cell = 0; cell <= count; cell++) circuit.AssumeRange(circuit.Source(cell), 0, 1);
        var selected = circuit.FirstActive(circuit.Source(0), [.. Enumerable.Range(1, count).Select(circuit.Source)]);
        return (circuit, selected);
    }

    [Fact]
    public void FirstActive_equals_the_first_active_macro_for_each_input_of_up_to_8_candidates()
    {
        for (var count = 0; count <= 8; count++)
        {
            var (circuit, selected) = BuildFirstActive(count);
            for (var pattern = 0; pattern < 1 << (count + 1); pattern++)
            {
                var candidates = Enumerable.Range(0, count).Select(index => (pattern >> (index + 1) & 1) == 1).ToArray();
                AssertFirstActive(circuit, selected, (pattern & 1) == 1, candidates);
            }
        }
    }

    [Fact]
    public void FirstActive_equals_the_first_active_macro_for_seeded_inputs_of_9_to_17_candidates()
    {
        var random = Js.CreateRandom(45);
        for (var count = 9; count <= 17; count++)
        {
            var (circuit, selected) = BuildFirstActive(count);
            for (var item = 0; item < SeededCount / 9; item++)
            {
                // A low density gives a first active candidate at each position.
                var density = random();
                var candidates = Enumerable.Range(0, count).Select(_ => random() < density * 0.5).ToArray();
                AssertFirstActive(circuit, selected, random() < 0.9, candidates);
            }
        }
    }

    [Fact]
    public void FirstActive_selects_the_first_true_candidate_and_refuses_a_null_array()
    {
        Assert.Equal([false, true, false, false], Hw.FirstActive(true, [false, true, false, true]));
        Assert.Equal([false, false, false, false], Hw.FirstActive(false, [false, true, false, true]));
        Assert.Empty(Hw.FirstActive(true, []));
        Assert.Throws<ArgumentNullException>(() => Hw.FirstActive(true, null!));
    }
}
