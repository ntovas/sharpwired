// Handwired source: tests/runtime/spring-matrix.test.ts.
// Each test name holds the Handwired test name. Handwired evaluates with createTensorEvaluator
// and checkEveryCompileMode. This port evaluates with the reference evaluator and keeps the
// validateCircuit part of checkEveryCompileMode. PHASE2-TENSOR-ASSERTIONS.md lists the tensor
// parts, and PHASE3-TIMING-ASSERTIONS.md lists the host timing and advanceFrame tests. Cost
// counts the live operations and their layers, as ComparisonTests.Cost does.

namespace Sharpwired.Core.Tests;

public class SpringMatrixTests
{
    private static double F(double value) => Js.F(value);

    // The next FP32 value above a positive finite FP32 value.
    private static double NextFloat32Up(double value) =>
        BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits((float)value) + 1);

    private static readonly double SmallestSubnormal = F(1e-45);

    private static readonly double Largest = F(3.4028234663852886e38);

    private static readonly SpringConstants[] Constants =
    [
        new(290, 30),
        new(650, 45),
        new(120, 14),
    ];

    private static readonly double MatrixBound = Constants.Max(constants => SpringMatrix.SpringMatrixBound(constants));

    // Plain float64 semi-implicit Euler steps, as index.ts's stepSpringTo loop.
    private static (double Displacement, double Velocity) EulerSteps(SpringConstants constants, double displacement, double velocity, int steps)
    {
        var e = displacement;
        var v = velocity;
        for (var step = 0; step < steps; step++)
        {
            v += (-constants.Stiffness * e - constants.Damping * v) * 0.004;
            e += v * 0.004;
        }
        return (e, v);
    }

    // ApplySpringMatrix written independently as FP32 steps: each product is f(entry × operand),
    // and each sum adds from +0 in stored order.
    private static (double Displacement, double Velocity) ApplyMatrixFp32(IReadOnlyList<double> matrix, double displacement, double velocity) =>
        (F(F(0 + F(matrix[0] * displacement)) + F(matrix[1] * velocity)),
         F(F(0 + F(matrix[2] * displacement)) + F(matrix[3] * velocity)));

    // An FP32 sum as the runtime adds it: from f(bias), then each coefficient times its operand
    // in stored order.
    private static double SumFp32(double bias, params (double Coefficient, double Operand)[] terms)
    {
        var value = F(bias);
        foreach (var (coefficient, operand) in terms) value = F(value + F(F(coefficient) * operand));
        return value;
    }

    private static double RegluFp32(double left, double gate) => F(left * Math.Max(0, gate));

    private static double BooleanFp32(double score) => F(F(F(score) + 8_388_609) - 8_388_609);

    private sealed record SpringSample(double[] Matrix, double Displacement, double Velocity, double Destination, double DestinationDuringSteps);

    private sealed record ObjectFp32(double[] Displacements, double[] Positions, double[] Velocities, double Settled, double Active, double AtRest);

    // AnimatedObject written independently as FP32 steps, for springs whose displacement is
    // measured from a separate destinationDuringSteps wire, or from destination itself.
    private static ObjectFp32 AnimatedObjectFp32(
        IReadOnlyList<SpringSample> springs,
        bool separateDestinationDuringSteps,
        double settleCheck,
        double? snap,
        double? settleDistance = null,
        double? settleSpeed = null)
    {
        var distance = settleDistance ?? F(0.01);
        var speed = settleSpeed ?? F(0.01);
        var exceedance = 0.0;
        var liftedRest = 0.0;
        var stepped = springs.Select(spring =>
        {
            var next = ApplyMatrixFp32(spring.Matrix, spring.Displacement, spring.Velocity);
            var displacement = separateDestinationDuringSteps
                ? SumFp32(0, (1, spring.DestinationDuringSteps), (-1, spring.Destination), (1, next.Displacement))
                : next.Displacement;
            foreach (var (value, threshold) in new[] { (displacement, distance), (next.Velocity, speed) })
            {
                var positivePart = RegluFp32(1, SumFp32(0, (1, value)));
                var negativePart = RegluFp32(1, SumFp32(0, (-1, value)));
                exceedance = F(exceedance + RegluFp32(1, SumFp32(-threshold, (1, positivePart), (1, negativePart))));
                liftedRest = F(F(liftedRest + F(Math.Pow(2, 127) * positivePart)) + F(Math.Pow(2, 127) * negativePart));
            }
            return (Displacement: displacement, next.Velocity);
        }).ToList();
        var atRest = RegluFp32(1, SumFp32(1, (-Math.Pow(2, 22), RegluFp32(1, liftedRest))));
        var settled = BooleanFp32(RegluFp32(1, SumFp32(0, (0.5, settleCheck), (0.5, atRest), (-Math.Pow(2, 20), exceedance))));
        var notSettled = SumFp32(1, (-1, settled));
        var active = snap is not { } snapValue
            ? notSettled
            : RegluFp32(SumFp32(0, (1, notSettled)), SumFp32(0, (1, SumFp32(1, (-1, snapValue)))));
        var displacements = stepped.Select(step => RegluFp32(SumFp32(0, (1, step.Displacement)), SumFp32(0, (1, active)))).ToArray();
        return new ObjectFp32(
            displacements,
            [.. springs.Select((spring, index) => SumFp32(0, (1, spring.Destination), (1, displacements[index])))],
            [.. stepped.Select(step => RegluFp32(SumFp32(0, (1, step.Velocity)), SumFp32(0, (1, active))))],
            settled,
            active,
            atRest);
    }

    private static float[] Floats(IEnumerable<double> values) => [.. values.Select(value => (float)value)];

    private static (Circuit Circuit, SpringState Next) BuildMatrixCircuit()
    {
        var circuit = new Circuit(6);
        for (var cell = 0; cell < 4; cell++) circuit.AssumeRange(Wire.FromPosition(cell), -MatrixBound, MatrixBound);
        var matrix = new SpringMatrixWires(circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3));
        return (circuit, circuit.ApplySpringMatrix(matrix, circuit.Source(4), circuit.Source(5)));
    }

    // 'applying a spring matrix' / 'equals float64 Euler steps, every compile mode matches the
    // reference evaluator, and the cost is pinned'.
    [Fact]
    public void Applying_a_spring_matrix_equals_float64_Euler_steps_and_the_cost_is_pinned()
    {
        var (circuit, next) = BuildMatrixCircuit();
        Assert.Equal((14, 3), ComparisonTests.Cost(circuit, [next.Displacement, next.Velocity]));
        var random = Js.CreateRandom(3);
        var worstDisplacement = 0.0;
        var worstVelocity = 0.0;
        for (var sample = 0; sample < 1500; sample++)
        {
            var constants = Constants[(int)Math.Floor(random() * Constants.Length)];
            var steps = random() < 0.2 ? (int)Math.Floor(random() * 4) : (int)Math.Floor(random() * 301);
            var displacement = F((random() * 2 - 1) * 2000);
            var velocity = F((random() * 2 - 1) * 5000);
            var matrix = SpringMatrix.SpringMatrixPower(SpringMatrix.SpringStepMatrix(constants), steps);
            var source = Floats([.. matrix.ToArray(), displacement, velocity]);
            var values = circuit.EvaluateReference(source);
            var expectedFp32 = ApplyMatrixFp32([.. source.Take(4).Select(value => (double)value)], displacement, velocity);
            Js.Is(expectedFp32.Displacement, values[next.Displacement.Position]);
            Js.Is(expectedFp32.Velocity, values[next.Velocity.Position]);
            var expected = EulerSteps(constants, displacement, velocity, steps);
            worstDisplacement = Math.Max(worstDisplacement, Math.Abs(values[next.Displacement.Position] - expected.Displacement));
            worstVelocity = Math.Max(worstVelocity, Math.Abs(values[next.Velocity.Position] - expected.Velocity));
        }
        Assert.True(worstDisplacement < 1e-3, $"worst displacement error {worstDisplacement}");
        Assert.True(worstVelocity < 1e-2, $"worst velocity error {worstVelocity}");
        circuit.ValidateCircuit();
    }

    // 'applying a spring matrix' / 'zero steps return the state unchanged, and extreme states stay
    // FP32 products'.
    [Fact]
    public void Zero_steps_return_the_state_unchanged_and_extreme_states_stay_FP32_products()
    {
        var (circuit, next) = BuildMatrixCircuit();
        (double, double)[] states = [(123.456, -7.5), (-0.0, 0), (F(1e-45), -F(1e-45)), (Largest, -Largest), (F(Math.Pow(2, 24) + 2), 3)];
        foreach (var (displacement, velocity) in states)
        {
            // Sums start from +0, so -0 comes back as +0. Adding 0 ignores the sign of zero.
            var identity = circuit.EvaluateReference(Floats([1, 0, 0, 1, displacement, velocity]));
            Js.Is(F(displacement) + 0, identity[next.Displacement.Position] + 0.0);
            Js.Is(F(velocity) + 0, identity[next.Velocity.Position] + 0.0);
            var matrix = SpringMatrix.SpringMatrixPower(SpringMatrix.SpringStepMatrix(Constants[1]), 300);
            var stepped = circuit.EvaluateReference(Floats([.. matrix.ToArray(), displacement, velocity]));
            var expected = ApplyMatrixFp32([.. matrix.ToArray().Select(F)], F(displacement), F(velocity));
            Js.Is(expected.Displacement, stepped[next.Displacement.Position]);
            Js.Is(expected.Velocity, stepped[next.Velocity.Position]);
        }
    }

    // 'applying a spring matrix' / 'refuses matrix wires without finite declared ranges'.
    [Fact]
    public void Refuses_matrix_wires_without_finite_declared_ranges()
    {
        var circuit = new Circuit(6);
        for (var cell = 0; cell < 3; cell++) circuit.AssumeRange(Wire.FromPosition(cell), -MatrixBound, MatrixBound);
        var matrix = new SpringMatrixWires(circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3));
        Js.Throws(() => circuit.ApplySpringMatrix(matrix, circuit.Source(4), circuit.Source(5)), "finite declared ranges");
        circuit.AssumeRange(circuit.Source(3), -MatrixBound, double.PositiveInfinity);
        Js.Throws(() => circuit.ApplySpringMatrix(matrix, circuit.Source(4), circuit.Source(5)), "finite declared ranges");
        // Sharpwired: a wire that does not exist yet is refused, as Handwired assertWire does.
        Js.Throws(() => circuit.ApplySpringMatrix(matrix with { VelocityFromVelocity = Wire.FromPosition(6) }, circuit.Source(4), circuit.Source(5)), "outside the compiled circuit");
    }

    // Cells per spring: 4 matrix cells, displacement, velocity, destination,
    // destinationDuringSteps. Then the settle check and snap bits.
    private const int FieldsPerSpring = 8;

    private sealed record ObjectCircuit(Circuit Circuit, AnimatedObject Object, Wire[] Roots, int SettleCheckCell, int SnapCell);

    private static ObjectCircuit BuildObject(int springCount, bool separateDestinationDuringSteps, bool withSnap, double? settleDistance = null, double? settleSpeed = null)
    {
        var settleCheckCell = springCount * FieldsPerSpring;
        var snapCell = settleCheckCell + 1;
        var circuit = new Circuit(snapCell + 1);
        circuit.AssumeRange(Wire.FromPosition(settleCheckCell), 0, 1);
        circuit.AssumeRange(Wire.FromPosition(snapCell), 0, 1);
        var springs = Enumerable.Range(0, springCount).Select(index =>
        {
            var start = index * FieldsPerSpring;
            for (var cell = start; cell < start + 4; cell++) circuit.AssumeRange(Wire.FromPosition(cell), -MatrixBound, MatrixBound);
            return new AnimatedSpring(
                new SpringMatrixWires(circuit.Source(start), circuit.Source(start + 1), circuit.Source(start + 2), circuit.Source(start + 3)),
                circuit.Source(start + 4),
                circuit.Source(start + 5),
                circuit.Source(start + 6))
            {
                DestinationDuringSteps = separateDestinationDuringSteps ? circuit.Source(start + 7) : null,
            };
        }).ToList();
        var options = new AnimatedObjectOptions(circuit.Source(settleCheckCell)) { Snap = withSnap ? circuit.Source(snapCell) : null };
        if (settleDistance is { } distance) options = options with { SettleDistance = distance };
        if (settleSpeed is { } speed) options = options with { SettleSpeed = speed };
        var animated = circuit.AnimatedObject(springs, options);
        Wire[] roots = [.. animated.Displacements, .. animated.Positions, .. animated.Velocities, animated.Settled, animated.Active];
        return new ObjectCircuit(circuit, animated, roots, settleCheckCell, snapCell);
    }

    private static void WriteSprings(float[] source, IReadOnlyList<SpringSample> springs)
    {
        for (var index = 0; index < springs.Count; index++)
        {
            var spring = springs[index];
            var start = index * FieldsPerSpring;
            for (var entry = 0; entry < 4; entry++) source[start + entry] = (float)spring.Matrix[entry];
            source[start + 4] = (float)spring.Displacement;
            source[start + 5] = (float)spring.Velocity;
            source[start + 6] = (float)spring.Destination;
            source[start + 7] = (float)spring.DestinationDuringSteps;
        }
    }

    private static double[] EvaluateRoots(ObjectCircuit built, float[] source)
    {
        var values = built.Circuit.EvaluateReference(source);
        return [.. built.Roots.Select(root => (double)values[root.Position])];
    }

    // The FP32 matrix that a host writes for the steps.
    private static double[] Fp32Matrix(SpringConstants constants, int steps) =>
        [.. SpringMatrix.SpringMatrixPower(SpringMatrix.SpringStepMatrix(constants), steps).ToArray().Select(F)];

    // 'animated objects' / 'costs: removing the position subtraction saves 1 operation per spring,
    // and the rest bit adds 2 per spring and 2 per object'.
    [Fact]
    public void Animated_object_costs()
    {
        (int, int) Counts(int springCount, bool separate, bool withSnap)
        {
            var built = BuildObject(springCount, separate, withSnap);
            return ComparisonTests.Cost(built.Circuit, built.Roots);
        }
        Assert.Equal((29, 11), Counts(1, false, false));
        Assert.Equal((30, 12), Counts(1, true, false));
        Assert.Equal((52, 11), Counts(2, false, false));
        Assert.Equal((128, 13), Counts(5, true, true));
    }

    // 'animated objects' / 'match an independent FP32 twin bit for bit, including samples at the
    // settle thresholds and at exact rest'.
    [Fact]
    public void Animated_objects_match_an_independent_FP32_twin_bit_for_bit()
    {
        var threshold = F(0.01);
        var aboveThreshold = NextFloat32Up(threshold);
        var random = Js.CreateRandom(17);
        // A value near a threshold. Inside: exactly at it, or within it. Otherwise also one FP32
        // step above it, or up to twice the threshold.
        double NearThreshold(bool inside)
        {
            var pick = random();
            var sign = random() < 0.5 ? -1 : 1;
            if (inside) return pick < 0.4 ? sign * threshold : F(sign * random() * 0.01);
            return pick < 0.3 ? sign * threshold : pick < 0.5 ? sign * aboveThreshold : F((random() * 2 - 1) * 0.02);
        }
        double SignedZero() => random() < 0.5 ? -0.0 : 0.0;
        var settledSamples = 0;
        var notSettledNearThreshold = 0;
        var settledAtRestWithoutCheck = 0;
        var subnormalNotAtRest = 0;
        foreach (var separateDestinationDuringSteps in new[] { false, true })
        {
            foreach (var withSnap in new[] { false, true })
            {
                var built = BuildObject(3, separateDestinationDuringSteps, withSnap);
                for (var sample = 0; sample < 1500; sample++)
                {
                    var source = new float[built.Circuit.SourceSize];
                    var pick = random();
                    var nearRest = pick < 0.45;
                    var atRest = pick >= 0.45 && pick < 0.6;
                    // One sample in four of those at rest moves one spring by the smallest subnormal value.
                    var subnormalSpring = atRest && random() < 0.25 ? (int)Math.Floor(random() * 3) : -1;
                    var inside = random() < 0.5;
                    var springs = new List<SpringSample>();
                    for (var index = 0; index < 3; index++)
                    {
                        var constants = Constants[(int)Math.Floor(random() * Constants.Length)];
                        // Zero steps keep the displacements exactly where the sample puts them.
                        var steps = nearRest || atRest ? 0 : (int)Math.Floor(random() * 301);
                        var destination = random() < 0.4 ? 0 : F(Js.Round(random() * 800) + (random() < 0.5 ? 0 : random()));
                        // At rest the destination does not change, so the rebase adds exactly 0.
                        var destinationDuringSteps = atRest || random() < 0.5 ? destination : F(destination + (random() * 2 - 1) * 50);
                        var matrix = Fp32Matrix(constants, steps);
                        if (atRest)
                        {
                            var subnormal = index == subnormalSpring;
                            var onVelocity = random() < 0.5;
                            var restDisplacement = subnormal && !onVelocity ? -SmallestSubnormal : SignedZero();
                            var restVelocity = subnormal && onVelocity ? SmallestSubnormal : SignedZero();
                            springs.Add(new SpringSample(matrix, restDisplacement, restVelocity, destination, destinationDuringSteps));
                            continue;
                        }
                        var displacement = nearRest ? NearThreshold(inside) : F((random() * 2 - 1) * 900);
                        var velocity = nearRest ? NearThreshold(inside) : F((random() * 2 - 1) * 3000);
                        springs.Add(new SpringSample(matrix, displacement, velocity, destination, destinationDuringSteps));
                    }
                    WriteSprings(source, springs);
                    source[built.SettleCheckCell] = random() < 0.7 ? 1 : 0;
                    source[built.SnapCell] = withSnap && random() < 0.15 ? 1 : 0;
                    var outputs = EvaluateRoots(built, source);
                    var expected = AnimatedObjectFp32(springs, separateDestinationDuringSteps, source[built.SettleCheckCell], withSnap ? source[built.SnapCell] : null);
                    double[] expectedOutputs = [.. expected.Displacements, .. expected.Positions, .. expected.Velocities, expected.Settled, expected.Active];
                    Assert.Equal(expectedOutputs.Length, outputs.Length);
                    for (var output = 0; output < outputs.Length; output++) Js.Is(expectedOutputs[output], outputs[output]);
                    if (expected.Settled == 1)
                    {
                        settledSamples++;
                        // A settled object lands exactly on its destinations with zero velocity. A
                        // displacement or velocity can be -0, for example f(-3 × 0).
                        for (var index = 0; index < springs.Count; index++)
                        {
                            Assert.True(outputs[index] == 0);
                            Js.Is(springs[index].Destination, outputs[3 + index]);
                            Assert.True(outputs[6 + index] == 0);
                        }
                        if (atRest && source[built.SettleCheckCell] == 0) settledAtRestWithoutCheck++;
                    }
                    else if (nearRest && source[built.SettleCheckCell] == 1)
                    {
                        notSettledNearThreshold++;
                    }
                    if (subnormalSpring >= 0)
                    {
                        Js.Is(0, expected.AtRest);
                        if (source[built.SettleCheckCell] == 0)
                        {
                            Js.Is(0, outputs[9]);
                            subnormalNotAtRest++;
                        }
                    }
                }
                built.Circuit.ValidateCircuit();
            }
        }
        Assert.True(settledSamples > 500, $"settled samples {settledSamples}");
        Assert.True(notSettledNearThreshold > 300, $"not settled near threshold {notSettledNearThreshold}");
        Assert.True(settledAtRestWithoutCheck > 200, $"settled at rest without check {settledAtRestWithoutCheck}");
        Assert.True(subnormalNotAtRest > 30, $"subnormal not at rest {subnormalNotAtRest}");
    }

    // 'animated objects' / 'settle at a settle check with index.ts thresholds, or at exact rest
    // without one, and a huge exceedance never flips the bit'.
    [Fact]
    public void Settle_at_a_settle_check_or_at_exact_rest_and_a_huge_exceedance_never_flips_the_bit()
    {
        var built = BuildObject(1, false, false);
        var threshold = F(0.01);
        // [displacement, position, velocity, settled, active]
        double[] Run(double displacement, double velocity, double settleCheck, double destination = 0)
        {
            var source = new float[built.Circuit.SourceSize];
            WriteSprings(source, [new SpringSample([1, 0, 0, 1], displacement, velocity, destination, destination)]);
            source[built.SettleCheckCell] = (float)settleCheck;
            // Adding 0 ignores the sign of zero, for example a settled negative velocity is f(-0.01 × 0) = -0.
            return [.. EvaluateRoots(built, source).Select(value => value + 0)];
        }
        void Expect(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (var index = 0; index < expected.Length; index++) Js.Is(expected[index], actual[index]);
        }
        var above = NextFloat32Up(threshold);
        // At the threshold settles. One FP32 step above does not, on either side.
        Expect([0, 0, 0, 1, 0], Run(threshold, -threshold, 1));
        Expect([0, 0, 0, 1, 0], Run(-threshold, threshold, 1));
        Expect([above, above, 0, 0, 1], Run(above, 0, 1));
        Expect([0, 0, -above, 0, 1], Run(0, -above, 1));
        // Within both thresholds, but not at a settle check: keeps animating.
        Expect([threshold, threshold, 0, 0, 1], Run(threshold, 0, 0));
        // Exactly at rest settles without a settle check, and the smallest subnormal displacement
        // or velocity is not rest.
        Expect([0, 0, 0, 1, 0], Run(0, 0, 0));
        Expect([0, 0, 0, 1, 0], Run(-0.0, -0.0, 0));
        Expect([SmallestSubnormal, SmallestSubnormal, 0, 0, 1], Run(SmallestSubnormal, 0, 0));
        Expect([0, 0, -SmallestSubnormal, 0, 1], Run(0, -SmallestSubnormal, 0));
        // Huge states: the exceedance and the rest lift overflow to Infinity, and the score clamps at 0.
        Expect([0, 1], Run(Largest, Largest, 1)[3..]);
        Expect([0, 1], Run(Largest, Largest, 0)[3..]);
        // At 100,000 px positions are 1/128 px apart, and the stored displacement still settles at
        // exactly 0.01 and not one FP32 step above it.
        Expect([0, 100_000, 0, 1, 0], Run(threshold, 0, 1, 100_000));
        Expect([above, F(100_000 + above), 0, 0, 1], Run(above, 0, 1, 100_000));
    }

    // 'animated objects' / 'the settle score adds the settle check and rest bits first, so the
    // smallest threshold floor still resolves'.
    [Fact]
    public void The_settle_score_adds_the_settle_check_and_rest_bits_first_so_the_smallest_threshold_floor_still_resolves()
    {
        var floor = Math.Pow(2, -22);
        var built = BuildObject(1, false, false, settleDistance: floor, settleSpeed: floor);
        var source = new float[built.Circuit.SourceSize];
        // One FP32 step above 2^-22 exceeds the threshold by 2^-45, which moves the score by 2^-25.
        WriteSprings(source, [new SpringSample([1, 0, 0, 1], NextFloat32Up(floor), 0, 0, 0)]);
        source[built.SettleCheckCell] = 1;
        Js.Is(0, EvaluateRoots(built, source)[3]);
        source[0 + 4] = (float)floor;
        Js.Is(1, EvaluateRoots(built, source)[3]);
        var kinds = built.Circuit.RoundingPoints().Select(point => point.Kind).ToList();
        Assert.Single(kinds, kind => kind == RoundingPointKind.Marked);
        Assert.Single(kinds, kind => kind == RoundingPointKind.Boolean);
        Js.Throws(() => BuildObject(1, false, false, settleDistance: Math.Pow(2, -23)), "at least 2^-22");
        Js.Throws(() => BuildObject(1, false, false, settleSpeed: double.NaN), "at least 2^-22");
    }

    // 'animated objects' / 'a changed destination rebases the displacement once, and an unchanged
    // one adds exactly 0'.
    [Fact]
    public void A_changed_destination_rebases_the_displacement_once_and_an_unchanged_one_adds_exactly_0()
    {
        var built = BuildObject(1, true, false);
        var random = Js.CreateRandom(23);
        var rebased = 0;
        for (var sample = 0; sample < 2000; sample++)
        {
            var source = new float[built.Circuit.SourceSize];
            var steps = (int)Math.Floor(random() * 301);
            var matrix = Fp32Matrix(Constants[0], steps);
            var displacement = F((random() * 2 - 1) * 900);
            var velocity = F((random() * 2 - 1) * 3000);
            var destinationDuringSteps = F(Math.Pow(2, 20) + Js.Round(random() * 800));
            var destination = random() < 0.5 ? destinationDuringSteps : F(Math.Pow(2, 20) + Js.Round(random() * 800));
            WriteSprings(source, [new SpringSample(matrix, displacement, velocity, destination, destinationDuringSteps)]);
            source[built.SettleCheckCell] = 0;
            var outputs = EvaluateRoots(built, source);
            var stepped = ApplyMatrixFp32(matrix, displacement, velocity);
            if (destination == destinationDuringSteps)
            {
                // The stepped displacement comes back as it is, at any distance from the origin.
                Js.Is(stepped.Displacement + 0, outputs[0] + 0);
            }
            else
            {
                // Whole-pixel destinations near 2^20 subtract exactly, so only the sum rounds.
                Js.Is(F(destinationDuringSteps - destination + stepped.Displacement), outputs[0]);
                rebased++;
            }
        }
        Assert.True(rebased > 800, $"rebased {rebased}");
    }

    // 'animated objects' / 'inlining keeps the values and the rounding points'.
    [Fact]
    public void Inlining_keeps_the_values_and_the_rounding_points()
    {
        var built = BuildObject(2, true, true);
        var circuit = built.Circuit;
        var target = new Circuit(circuit.SourceSize);
        var wireMap = target.InlineCircuit(circuit, [.. Enumerable.Range(0, circuit.SourceSize).Select(target.Source)]);
        var random = Js.CreateRandom(5);
        for (var sample = 0; sample < 200; sample++)
        {
            var source = new float[circuit.SourceSize];
            var springs = Enumerable.Range(0, 2).Select(_ =>
            {
                var matrix = Fp32Matrix(Constants[0], (int)Math.Floor(random() * 40));
                var displacement = random() < 0.2 ? 0 : F(random() * 0.03);
                var velocity = random() < 0.2 ? 0 : F((random() * 2 - 1) * 0.02);
                var destinationDuringSteps = random() < 0.5 ? 0 : F(random() * 0.01);
                return new SpringSample(matrix, displacement, velocity, 0, destinationDuringSteps);
            }).ToList();
            WriteSprings(source, springs);
            source[built.SettleCheckCell] = random() < 0.5 ? 1 : 0;
            var expected = circuit.EvaluateReference(source);
            var actual = target.EvaluateReference(source);
            foreach (var root in built.Roots) Js.Is(expected[root.Position], actual[wireMap[root.Position].Position]);
        }
        Assert.Equal(
            circuit.RoundingPoints().Select(point => point.Kind).Order(),
            target.RoundingPoints().Select(point => point.Kind).Order());
    }

    // 'animated objects' / 'the backward pass gives matrix cells and state the gradients of whole
    // products, and rejects the settle atom by default'.
    [Fact]
    public void The_backward_pass_gives_matrix_cells_and_state_the_gradients_of_whole_products_and_rejects_the_settle_atom_by_default()
    {
        var built = BuildObject(1, false, false);
        var circuit = built.Circuit;
        var random = Js.CreateRandom(29);
        for (var sample = 0; sample < 100; sample++)
        {
            var steps = sample < 10 ? 0 : (int)Math.Floor(random() * 301);
            var matrix = Fp32Matrix(Constants[sample % 3], steps);
            var source = new float[circuit.SourceSize];
            var destination = F(random() * 800);
            var displacement = F((random() * 2 - 1) * 500);
            var velocity = F((random() * 2 - 1) * 2000);
            WriteSprings(source, [new SpringSample(matrix, displacement, velocity, destination, destination)]);
            source[built.SettleCheckCell] = 0;
            var values = circuit.EvaluateReference(source);
            var seeds = new float[values.Length];
            seeds[built.Object.Positions[0].Position] = 1;
            Js.Throws(() => circuit.BackpropagateReference(values, seeds), "boolean atom");
            var gradients = circuit.BackpropagateReference(values, seeds, BooleanGradient.StraightThrough);
            static void Close(double actual, double expected) =>
                Assert.True(Math.Abs(actual - expected) <= 1e-4 * Math.Max(1, Math.Abs(expected)), $"expected {expected:R}, got {actual:R}");
            // position = destination + m0 × displacement + m1 × velocity
            Close(gradients[0], displacement);
            Close(gradients[1], velocity);
            Js.Is(0, gradients[2]);
            Js.Is(0, gradients[3]);
            Close(gradients[4], matrix[0]);
            Close(gradients[5], matrix[1]);
            Js.Is(1, gradients[6]);
        }
    }

    // 'animated objects' / 'refuses settle check and snap wires outside 0..1, and objects without springs'.
    [Fact]
    public void Refuses_settle_check_and_snap_wires_outside_0_to_1_and_objects_without_springs()
    {
        var circuit = new Circuit(8);
        for (var cell = 0; cell < 4; cell++) circuit.AssumeRange(Wire.FromPosition(cell), -MatrixBound, MatrixBound);
        var cells = Enumerable.Range(0, 8).Select(circuit.Source).ToArray();
        var spring = new AnimatedSpring(new SpringMatrixWires(cells[0], cells[1], cells[2], cells[3]), cells[4], cells[5], cells[6]);
        var settleCheck = cells[7];
        Js.Throws(() => circuit.AnimatedObject([spring], new AnimatedObjectOptions(settleCheck)), "within 0..1");
        circuit.AssumeRange(settleCheck, 0, 1);
        Js.Throws(() => circuit.AnimatedObject([], new AnimatedObjectOptions(settleCheck)), "at least one spring");
        Js.Throws(() => circuit.AnimatedObject([spring], new AnimatedObjectOptions(settleCheck) { Snap = cells[6] }), "within 0..1");
    }

    // 'host timing' / 'spring matrices: powers by squaring equal repeated Euler steps, and the
    // declared bound covers every FP32 entry the host writes' (the matrix part).
    // PHASE3-TIMING-ASSERTIONS.md lists the validateSpringClockConfig part.
    [Fact]
    public void Spring_matrices_powers_by_squaring_equal_repeated_Euler_steps_and_the_declared_bound_covers_every_FP32_entry()
    {
        foreach (var constants in Constants)
        {
            var step = SpringMatrix.SpringStepMatrix(constants);
            var euler = EulerSteps(constants, 3, -7, 1);
            Assert.True(Math.Abs(step[0] * 3 + step[1] * -7 - euler.Displacement) < 1e-12);
            Assert.True(Math.Abs(step[2] * 3 + step[3] * -7 - euler.Velocity) < 1e-12);
            var bound = SpringMatrix.SpringMatrixBound(constants);
            var repeated = SpringMatrix.Identity;
            var largest = 1.0;
            for (var steps = 0; steps <= 300; steps++)
            {
                if (steps > 0) repeated = SpringMatrix.MultiplySpringMatrices(repeated, step);
                var power = SpringMatrix.SpringMatrixPower(step, steps);
                for (var index = 0; index < 4; index++)
                {
                    var entry = power[index];
                    Assert.True(Math.Abs(entry - repeated[index]) < 1e-12);
                    Assert.True(Math.Abs(F(entry)) <= bound);
                    largest = Math.Max(largest, Math.Abs(entry));
                }
            }
            Assert.True(bound < largest * (1 + 1e-5));
        }
        // toBeCloseTo(10.36, 2): |expected - received| < 10^-2 / 2.
        Assert.True(Math.Abs(SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45)) - 10.36) < 0.005);
        Js.Throws(() => SpringMatrix.SpringMatrixBound(new SpringConstants(1e7, 0)), "overflows");
        Js.Throws(() => SpringMatrix.SpringStepMatrix(new SpringConstants(-1, 0)), "nonnegative");
        // Handwired passes the count 1.5. A C# count is an int, so this port passes -1.
        Js.Throws(() => SpringMatrix.SpringMatrixPower(SpringMatrix.Identity, -1), "nonnegative integer");
    }
}
