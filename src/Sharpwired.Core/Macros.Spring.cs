// Handwired source: src/runtime/numeric-ui-core.ts (emitSpring, SpringMatrixWires,
// emitApplySpringMatrix, AnimatedSpring, AnimatedObjectOptions, AnimatedObject,
// emitAnimatedObject, emitAbsolute, emitClampZeroOne).
// Handwired emitMultiply and emitSelect only call circuit.multiply and circuit.select, so this
// port calls Circuit.Multiply and Circuit.Select.

using System.Collections.Immutable;

namespace Sharpwired.Core;

/// <summary>The wires of Macros.Spring: the next position, the next velocity and the active bit.</summary>
public readonly record struct SpringWires(Wire Position, Wire Velocity, Wire Active);

/// <summary>
/// The 4 cells of a spring matrix that the host writes before each evaluation, one time for
/// each stiffness and damping pair, for example
/// SpringMatrix.SpringMatrixPower(SpringMatrix.SpringStepMatrix(new(290, 30)), steps):
/// displacement' = m[0] × displacement + m[1] × velocity and
/// velocity' = m[2] × displacement + m[3] × velocity.
/// </summary>
public readonly record struct SpringMatrixWires(
    Wire DisplacementFromDisplacement,
    Wire DisplacementFromVelocity,
    Wire VelocityFromDisplacement,
    Wire VelocityFromVelocity)
{
    /// <summary>The 4 wires, in the order of the constructor.</summary>
    public Wire[] ToArray() => [DisplacementFromDisplacement, DisplacementFromVelocity, VelocityFromDisplacement, VelocityFromVelocity];
}

/// <summary>The displacement and the velocity of one spring after the steps of a matrix.</summary>
public readonly record struct SpringState(Wire Displacement, Wire Velocity);

/// <summary>One spring of Macros.AnimatedObject.</summary>
/// <param name="Matrix">The matrix cells of the spring.</param>
/// <param name="Displacement">
/// The displacement of the spring from DestinationDuringSteps, usually a state cell that the
/// previous evaluation wrote back, for example 0 for a spring at rest.
/// </param>
/// <param name="Velocity">The velocity of the spring.</param>
/// <param name="Destination">The destination from the time of this evaluation on, after its events.</param>
public sealed record AnimatedSpring(SpringMatrixWires Matrix, Wire Displacement, Wire Velocity, Wire Destination)
{
    /// <summary>
    /// The destination that the stored displacement is measured from, that is, the destination
    /// during the steps of this evaluation. Usually the state cell that holds the destination that
    /// the previous evaluation wrote back. The steps simulate time before the events of this
    /// evaluation, so they pull toward it. Null when this evaluation cannot change the destination.
    /// The steps then pull toward Destination.
    /// </summary>
    public Wire? DestinationDuringSteps { get; init; }
}

/// <summary>The options of Macros.AnimatedObject.</summary>
/// <param name="SettleCheck">
/// An exact bit that the host writes: 1 when this evaluation ends at a fixed simulated settle
/// time, otherwise 0. The object can settle only when it is 1, so the settle result does not
/// depend on the step counts of the frames.
/// </param>
public sealed record AnimatedObjectOptions(Wire SettleCheck)
{
    /// <summary>
    /// An exact bit that puts each spring on its destination with zero velocity, for example while
    /// the user drags the object, or when a frame needs more steps than its limit.
    /// </summary>
    public Wire? Snap { get; init; }

    /// <summary>The distance threshold, 0.01 px by default. It must be at least 2^-22.</summary>
    public double SettleDistance { get; init; } = 0.01;

    /// <summary>The speed threshold, 0.01 px/s by default. It must be at least 2^-22.</summary>
    public double SettleSpeed { get; init; } = 0.01;
}

/// <summary>The wires of Macros.AnimatedObject.</summary>
/// <param name="Displacements">
/// The next displacement of each spring from its Destination. Write it back, with Velocities, and
/// write Destination back as the DestinationDuringSteps of the next evaluation.
/// </param>
/// <param name="Velocities">The next velocity of each spring.</param>
/// <param name="Positions">
/// What to draw: destination + displacement, rounded one time at the magnitude of the destination.
/// This is not state, so the rounding does not accumulate.
/// </param>
/// <param name="Settled">
/// 1 when the settle check of this evaluation is 1 and each spring is within both thresholds, or
/// when each displacement and velocity is ±0, for example after a snap.
/// </param>
/// <param name="Active">
/// 1 while the object moves: not settled and not snapped. A host stops the clock when the active
/// bit of each object is 0.
/// </param>
public sealed record AnimatedObject(
    ImmutableArray<Wire> Displacements,
    ImmutableArray<Wire> Velocities,
    ImmutableArray<Wire> Positions,
    Wire Settled,
    Wire Active);

public static partial class Macros
{
    /// <summary>
    /// Four semi-implicit Euler steps of a spring with stiffness, damping and the timestep as
    /// wires, then a settle test. When the active bit is exactly 0 and the operands are finite, the
    /// position is exactly the destination and the velocity is exactly 0. This does not make each
    /// step before the settle equal to a float64 spring. Cost: 46 operations in 33 layers.
    /// </summary>
    public static SpringWires Spring(
        this Circuit circuit,
        Wire positionSource,
        Wire destination,
        Wire velocitySource,
        Wire stiffness,
        Wire damping,
        Wire timestep,
        Wire settleDistance,
        Wire settleSpeed,
        Wire settleDistanceScale,
        Wire settleSpeedScale,
        Wire unit)
    {
        var position = positionSource;
        var velocity = velocitySource;
        for (var step = 0; step < 4; step++)
        {
            var displacement = circuit.Affine([(position, 1), (destination, -1)]);
            var springForce = circuit.Multiply(stiffness, displacement);
            var dampingForce = circuit.Multiply(damping, velocity);
            var acceleration = circuit.Affine([(springForce, -1), (dampingForce, -1)]);
            velocity = circuit.Affine([(velocity, 1), (circuit.Multiply(acceleration, timestep), 1)]);
            position = circuit.Affine([(position, 1), (circuit.Multiply(velocity, timestep), 1)]);
        }

        var distanceAboveThreshold = circuit.Relu(circuit.Affine([
            (circuit.Absolute(circuit.Affine([(position, 1), (destination, -1)])), 1),
            (settleDistance, -1),
        ]));
        var speedAboveThreshold = circuit.Relu(circuit.Affine([
            (circuit.Absolute(velocity), 1),
            (settleSpeed, -1),
        ]));
        var active = circuit.ClampZeroOne(
            circuit.Affine([
                (circuit.Multiply(distanceAboveThreshold, settleDistanceScale), 1),
                (circuit.Multiply(speedAboveThreshold, settleSpeedScale), 1),
            ]),
            unit);
        return new SpringWires(
            circuit.Select(active, destination, position),
            circuit.Multiply(active, velocity),
            active);
    }

    /// <summary>
    /// The displacement of one spring from its destination and its velocity after each step that
    /// the matrix describes, for example all 4 ms steps of one evaluation. The matrix cells need
    /// finite declared ranges, for example ±SpringMatrix.SpringMatrixBound(constants), so that a
    /// host that writes an overflowing matrix fails the source range check. Each product is a
    /// Circuit.Multiply, so the backward pass differentiates whole products, also at a matrix
    /// entry of exactly 0. The result is f(f(m0 × e) + f(m1 × v)) and f(f(m2 × e) + f(m3 × v)) for
    /// finite operands. Cost: 14 operations in 3 layers.
    /// </summary>
    public static SpringState ApplySpringMatrix(this Circuit circuit, SpringMatrixWires matrix, Wire displacement, Wire velocity)
    {
        foreach (var entry in matrix.ToArray())
        {
            Validation.AssertWire(entry, circuit.Width);
            if (!HasFiniteRange(circuit, entry))
            {
                throw new ArgumentException("spring matrix wires need finite declared ranges, e.g. ±springMatrixBound(constants)");
            }
        }
        return new SpringState(
            circuit.RoundAffine([
                (circuit.Multiply(matrix.DisplacementFromDisplacement, displacement), 1),
                (circuit.Multiply(matrix.DisplacementFromVelocity, velocity), 1),
            ]),
            circuit.RoundAffine([
                (circuit.Multiply(matrix.VelocityFromDisplacement, displacement), 1),
                (circuit.Multiply(matrix.VelocityFromVelocity, velocity), 1),
            ]));
    }

    /// <summary>
    /// The springs of one animated object, for example the x, y, width, height and scale of a
    /// window, stepped by their matrices and settled together with one exact bit. Each spring
    /// stores its displacement from its destination and its velocity, so FP32 rounds the state at
    /// the size of the displacement. When the object settles or snaps, each displacement and
    /// velocity becomes 0, up to the sign of zero, so each spring stops exactly on its destination.
    /// <code>
    /// (displacement', velocity') = matrix × (displacement, velocity)
    /// displacement'' = f(f(destinationDuringSteps - destination) + displacement')
    /// exceedance = Σ relu(|displacement''| - distance) + relu(|velocity'| - speed)
    /// atRest = 1 when each displacement'' and velocity' is ±0, otherwise 0
    /// settled = boolean(relu(0.5 × settleCheck + 0.5 × atRest - 2^20 × exceedance))
    /// active = (1 - settled) × (1 - snap)
    /// next displacement = f(displacement'' × active), next velocity = f(velocity' × active)
    /// position = f(destination + next displacement)
    /// </code>
    /// The rebase adds the destination difference first, so an unchanged destination adds exactly
    /// 0. A nonzero relu(|value| - threshold) is at least 2^-45 for thresholds of at least 2^-22, so
    /// 2^20 × exceedance moves the score below 0.5. Cost: 29 operations in 11 layers for one spring.
    /// </summary>
    public static AnimatedObject AnimatedObject(this Circuit circuit, IReadOnlyList<AnimatedSpring> springs, AnimatedObjectOptions options)
    {
        if (springs.Count == 0) throw new ArgumentException("an animated object needs at least one spring");
        var width = circuit.Width;
        var settleDistance = Fp32.Round(options.SettleDistance);
        var settleSpeed = Fp32.Round(options.SettleSpeed);
        foreach (var threshold in new[] { settleDistance, settleSpeed })
        {
            if (!(double.IsFinite(threshold) && threshold >= Math.Pow(2, -22)))
            {
                throw new ArgumentException($"settle thresholds must be finite and at least 2^-22, got {Fp32.Format(threshold)}");
            }
        }
        Wire[] bits = options.Snap is { } snapBit ? [options.SettleCheck, snapBit] : [options.SettleCheck];
        foreach (var bit in bits)
        {
            Validation.AssertWire(bit, width);
            var range = circuit.Ranges[bit.Position];
            if (!(range.Minimum >= 0 && range.Maximum <= 1)) throw new ArgumentException("settleCheck and snap must be declared within 0..1");
        }
        var exceedanceTerms = new List<Term>();
        var restTerms = new List<Term>();
        var stepped = new List<SpringState>();
        foreach (var spring in springs)
        {
            var next = circuit.ApplySpringMatrix(spring.Matrix, spring.Displacement, spring.Velocity);
            // Its own operation, so each threshold ReLU reads one FP32 value and a nonzero ReLU is at
            // least the spacing above the threshold.
            var displacement = spring.DestinationDuringSteps is not { } during || during == spring.Destination
                ? next.Displacement
                : circuit.RoundAffine([(during, 1), (spring.Destination, -1), (next.Displacement, 1)]);
            foreach (var (value, threshold) in new[] { (displacement, settleDistance), (next.Velocity, settleSpeed) })
            {
                var positivePart = circuit.Reglu([], 1, [(value, 1)], 0);
                var negativePart = circuit.Reglu([], 1, [(value, -1)], 0);
                // relu(|value| - threshold), with |value| = relu(value) + relu(-value). One of the two
                // parts is 0, so the gate adds the same FP32 values as relu(value - threshold) or
                // relu(-value - threshold).
                exceedanceTerms.Add((circuit.Reglu([], 1, [(positivePart, 1), (negativePart, 1)], -threshold), 1));
                restTerms.Add((positivePart, Math.Pow(2, 127)));
                restTerms.Add((negativePart, Math.Pow(2, 127)));
            }
            stepped.Add(new SpringState(displacement, next.Velocity));
        }
        var exceedance = circuit.RoundAffine(exceedanceTerms);
        // Each lifted term is 0, at least 2^127 × 2^-149 = 2^-22, or Infinity, so the lifted sum is
        // 0 exactly when each displacement'' and velocity' is ±0, and relu(1 - 2^22 × liftedRest) is
        // the atRest bit. The terms stay as written, because a merged repeated part would get the
        // coefficient 2^128, which is not an FP32 value. The lift is a ReLU, so at exact rest the
        // backward pass stops at its corner and does not scale gradients by 2^149.
        var liftedRest = circuit.Append(OperationKind.Reglu, [], 1, restTerms, 0, preserveRounding: true);
        var atRest = circuit.Reglu([], 1, [(liftedRest, -Math.Pow(2, 22))], 1);
        // settleCheck and atRest first, so the partial sum is exactly 0, 0.5 or 1 before the
        // subtraction of the exceedance, and 0.5 - 2^20 × 2^-45 is the FP32 value just below 0.5.
        // The score resolves next to 0.5 only as FP32 values, so it is a marked rounding point.
        var settledScore = circuit.Append(
            OperationKind.Reglu,
            [],
            1,
            [(options.SettleCheck, 0.5), (atRest, 0.5), (exceedance, -Math.Pow(2, 20))],
            0,
            preserveRounding: true);
        circuit.MarkRoundingPoint(settledScore);
        var settled = circuit.Boolean(settledScore);
        var notSettled = circuit.RoundAffine([(settled, -1)], 1);
        var active = options.Snap is not { } snap
            ? notSettled
            : circuit.Multiply(notSettled, circuit.RoundAffine([(snap, -1)], 1));
        ImmutableArray<Wire> displacements = [.. stepped.Select(state => circuit.Multiply(state.Displacement, active))];
        ImmutableArray<Wire> velocities = [.. stepped.Select(state => circuit.Multiply(state.Velocity, active))];
        ImmutableArray<Wire> positions = [.. springs.Select((spring, index) => circuit.RoundAffine([(spring.Destination, 1), (displacements[index], 1)]))];
        return new AnimatedObject(displacements, velocities, positions, settled, active);
    }

    /// <summary>|value| as relu(value) + relu(-value), an ordinary affine sum of two ReLUs.</summary>
    public static Wire Absolute(this Circuit circuit, Wire value) => circuit.Affine([
        (circuit.Relu(value), 1),
        (circuit.Relu(circuit.Affine([(value, -1)])), 1),
    ]);

    /// <summary>
    /// The clamp to 0..1 as relu(value) - relu(value - unit), with unit a wire declared exactly 1.
    /// The result range is declared 0..1. The upper plateau needs value &lt;= 2^24 when value is its
    /// own operation. When value is an ordinary affine sum that the builder copies into both ReLUs,
    /// both copies must add without rounding, for example value &lt;= 2^23 for multiples of 1/2.
    /// Negative finite values give 0.
    /// </summary>
    public static Wire ClampZeroOne(this Circuit circuit, Wire value, Wire unit)
    {
        var positive = circuit.Relu(value);
        var aboveOne = circuit.Relu(circuit.Affine([(value, 1), (unit, -1)]));
        var result = circuit.Affine([(positive, 1), (aboveOne, -1)]);
        circuit.AssumeRange(result, 0, 1);
        return result;
    }
}
