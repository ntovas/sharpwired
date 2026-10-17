// Handwired source: tests/runtime/spring-matrix.test.ts, the helpers of the describe block
// 'springs along frame-time traces, with presses between frames': Press, appPairs, appSpringPair,
// appObjectSprings, appInitialPositions, appCell, appSpringCell, appOutput, appPowers, createApp,
// AppRun, runApp, Float64Spring, stepFloat64Springs, settleFloat64Objects, runFloat64Reference,
// runUiMdOrder, appPresses, framePositions, frameDisplacements, maxFrameDifference, differingBits,
// traceCases and traceSchedules.
//
// A small app along frame-time traces. Object 0 has x and y springs (stiffness 290, damping 30)
// and a scale spring (stiffness 333, damping 20). Object 1 has one spring (stiffness 290, damping
// 30). A press replaces the destinations of one object, and a jump press also snaps that object
// onto them. The host writes one matrix for each stiffness and damping pair before each
// evaluation, and 1 into the snap cell of each object for a planned snap.

using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.Timing.Tests;

/// <summary>Handwired Press: an event that replaces the destinations of one object.</summary>
internal sealed record Press(double TimeStamp, int Object, double[] Destinations, bool Jump) : ITimedEvent;

/// <summary>Handwired settles entry: the time of a settle check and the settled bit of each object.</summary>
internal sealed record SettleEntry(double TimeMs, double[] Settled);

/// <summary>Handwired commits entry: an evaluation that wrote state back, and a copy of the stored table.</summary>
internal sealed record Commit(double TimeMs, EvaluationKind Kind, float[] Table);

/// <summary>Handwired frames entry: the drawn positions and a copy of all outputs of a frame evaluation.</summary>
internal sealed record FrameOutput(double[] Positions, float[] Outputs);

/// <summary>Handwired AppRun.</summary>
internal sealed class AppRun
{
    public Dictionary<double, FrameOutput> Frames { get; } = [];
    public List<SettleEntry> Settles { get; } = [];
    public List<Commit> Commits { get; } = [];
    public List<int> StepsPerFrame { get; } = [];
    public SpringClock Clock { get; set; } = SpringClock.Stopped;

    // Evaluations that wrote state back without a settle check, and found nothing animating.
    public int StopsWithoutSettleCheck { get; set; }
}

/// <summary>Handwired Float64Spring.</summary>
internal sealed class Float64Spring(double position, double velocity, double destination)
{
    public double Position { get; set; } = position;
    public double Velocity { get; set; } = velocity;
    public double Destination { get; set; } = destination;
}

/// <summary>The result of Handwired runFloat64Reference.</summary>
internal sealed record Float64Run(
    Dictionary<double, double[]> Positions,
    Dictionary<double, double[]> Displacements,
    List<SettleEntry> Settles);

internal static class SpringTraceApp
{
    public static readonly SpringConstants[] Pairs = [new(290, 30), new(333, 20)];
    public static readonly int[] SpringPair = [0, 0, 1, 0];
    public static readonly int[][] ObjectSprings = [[0, 1, 2], [3]];
    public static readonly double[] InitialPositions = [400, 300, 1, 200];

    // Handwired appCell.
    public const int MatrixStart = 0;
    public const int SettleCheckCell = 8;
    public const int EventActiveStart = 9;
    public const int SnapStart = 11;
    public const int EventDestinationStart = 13;
    public const int SpringStart = 17;
    public const int Size = 29;

    // Handwired appOutput.
    public const int PositionsOutput = Size;
    public const int ActiveOutput = Size + 4;
    public const int SettledOutput = Size + 6;

    // Handwired appSpringCell. For each spring: its displacement from the destination in effect,
    // its velocity, and that destination.
    public static int SpringCell(int spring, int field) => SpringStart + 3 * spring + field;

    // Handwired appPowers: for each pair, the matrix powers 0..MaxStepsPerFrame.
    public static readonly SpringMatrix[][] Powers = [.. Pairs.Select(constants =>
    {
        var step = SpringMatrix.SpringStepMatrix(constants);
        return Enumerable.Range(0, SpringClockConfig.Default.MaxStepsPerFrame + 1)
            .Select(steps => SpringMatrix.SpringMatrixPower(step, steps))
            .ToArray();
    })];

    // Handwired createApp.
    public static TensorEvaluator CreateApp()
    {
        var circuit = new Circuit(Size);
        for (var pair = 0; pair < Pairs.Length; pair++)
        {
            var bound = SpringMatrix.SpringMatrixBound(Pairs[pair]);
            for (var entry = 0; entry < 4; entry++) circuit.AssumeRange(Wire.FromPosition(MatrixStart + 4 * pair + entry), -bound, bound);
        }
        circuit.AssumeRange(circuit.Source(SettleCheckCell), 0, 1);
        for (var item = 0; item < 2; item++)
        {
            circuit.AssumeRange(Wire.FromPosition(EventActiveStart + item), 0, 1);
            circuit.AssumeRange(Wire.FromPosition(SnapStart + item), 0, 1);
        }
        int ObjectOfSpring(int spring) => Array.FindIndex(ObjectSprings, members => members.Contains(spring));
        var destinations = SpringPair.Select((_, spring) => circuit.Select(
            Wire.FromPosition(EventActiveStart + ObjectOfSpring(spring)),
            Wire.FromPosition(SpringCell(spring, 2)),
            Wire.FromPosition(EventDestinationStart + spring))).ToArray();
        var objects = ObjectSprings.Select((members, objectIndex) => circuit.AnimatedObject(
            [.. members.Select(spring =>
            {
                var start = MatrixStart + 4 * SpringPair[spring];
                return new AnimatedSpring(
                    new SpringMatrixWires(circuit.Source(start), circuit.Source(start + 1), circuit.Source(start + 2), circuit.Source(start + 3)),
                    circuit.Source(SpringCell(spring, 0)),
                    circuit.Source(SpringCell(spring, 1)),
                    destinations[spring])
                {
                    DestinationDuringSteps = circuit.Source(SpringCell(spring, 2)),
                };
            })],
            new AnimatedObjectOptions(circuit.Source(SettleCheckCell)) { Snap = circuit.Source(SnapStart + objectIndex) })).ToArray();
        var positions = new Wire[4];
        var displacements = new Wire[4];
        var velocities = new Wire[4];
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var members = ObjectSprings[objectIndex];
            for (var index = 0; index < members.Length; index++)
            {
                positions[members[index]] = objects[objectIndex].Positions[index];
                displacements[members[index]] = objects[objectIndex].Displacements[index];
                velocities[members[index]] = objects[objectIndex].Velocities[index];
            }
        }
        var zero = circuit.Affine([], 0);
        var nextSource = Enumerable.Range(0, Size).Select(circuit.Source).ToArray();
        for (var item = 0; item < 2; item++)
        {
            nextSource[EventActiveStart + item] = zero;
            nextSource[SnapStart + item] = zero;
        }
        for (var spring = 0; spring < 4; spring++)
        {
            nextSource[EventDestinationStart + spring] = zero;
            nextSource[SpringCell(spring, 0)] = displacements[spring];
            nextSource[SpringCell(spring, 1)] = velocities[spring];
            nextSource[SpringCell(spring, 2)] = destinations[spring];
        }
        Wire[] roots = [.. nextSource, .. positions, .. objects.Select(item => item.Active), .. objects.Select(item => item.Settled)];
        return circuit.CreateTensorEvaluator(roots);
    }

    // Handwired runApp. offset is added to each destination, for example 65,536 runs the app near
    // 65,536 px. Each table write is an FP32 store, as a Float32Array store is in Handwired. The
    // evaluator returns the same array on each call, so the run copies each value that it keeps.
    public static AppRun RunApp(TensorEvaluator evaluator, IReadOnlyList<Press> presses, IReadOnlyList<double> frameTimes, WriteBack writeBack, double offset = 0)
    {
        var state = new float[Size];
        for (var spring = 0; spring < InitialPositions.Length; spring++) state[SpringCell(spring, 2)] = (float)(InitialPositions[spring] + offset);
        var scratch = new float[Size];
        var run = new AppRun();
        var nextPress = 0;
        foreach (var frameTime in frameTimes)
        {
            var arrived = new List<Press>();
            while (nextPress < presses.Count && presses[nextPress].TimeStamp <= frameTime) arrived.Add(presses[nextPress++]);
            var steps = 0;
            run.Clock = HostTiming.AdvanceFrame(run.Clock, frameTime, arrived, evaluation =>
            {
                steps += evaluation.Steps;
                var table = evaluation.WritesBack ? state : scratch;
                if (!evaluation.WritesBack) state.CopyTo(scratch, 0);
                for (var pair = 0; pair < Powers.Length; pair++)
                {
                    var matrix = Powers[pair][evaluation.Steps];
                    var start = MatrixStart + 4 * pair;
                    table[start] = (float)matrix.DisplacementFromDisplacement;
                    table[start + 1] = (float)matrix.DisplacementFromVelocity;
                    table[start + 2] = (float)matrix.VelocityFromDisplacement;
                    table[start + 3] = (float)matrix.VelocityFromVelocity;
                }
                table[SettleCheckCell] = evaluation.SettleCheck ? 1f : 0f;
                for (var item = 0; item < 2; item++) table[SnapStart + item] = evaluation.Snap ? 1f : 0f;
                foreach (var press in evaluation.Events)
                {
                    table[EventActiveStart + press.Object] = 1f;
                    if (press.Jump) table[SnapStart + press.Object] = 1f;
                    var members = ObjectSprings[press.Object];
                    for (var index = 0; index < members.Length; index++)
                    {
                        table[EventDestinationStart + members[index]] = (float)(press.Destinations[index] + offset);
                    }
                }
                var outputs = evaluator.Evaluate(table);
                if (evaluation.WritesBack)
                {
                    outputs.AsSpan(0, Size).CopyTo(state);
                    run.Commits.Add(new Commit(evaluation.TimeMs, evaluation.Kind, state.ToArray()));
                    if (evaluation.SettleCheck && evaluation.Kind != EvaluationKind.Frame)
                    {
                        run.Settles.Add(new SettleEntry(evaluation.TimeMs, [outputs[SettledOutput], outputs[SettledOutput + 1]]));
                    }
                }
                if (evaluation.Kind == EvaluationKind.Frame)
                {
                    run.Frames[frameTime] = new FrameOutput(
                        [.. outputs.AsSpan(PositionsOutput, 4).ToArray().Select(value => (double)value)],
                        outputs.ToArray());
                }
                var animating = outputs[ActiveOutput] > 0 || outputs[ActiveOutput + 1] > 0;
                if (evaluation.WritesBack && !evaluation.SettleCheck && !animating) run.StopsWithoutSettleCheck++;
                return animating;
            }, writeBack);
            run.StepsPerFrame.Add(steps);
        }
        return run;
    }

    // Handwired stepFloat64Springs: one 4 ms semi-implicit Euler step of each spring.
    public static void StepFloat64Springs(IReadOnlyList<Float64Spring> springs)
    {
        for (var index = 0; index < springs.Count; index++)
        {
            var spring = springs[index];
            var (stiffness, damping) = Pairs[SpringPair[index]];
            var displacement = spring.Position - spring.Destination;
            spring.Velocity += (-stiffness * displacement - damping * spring.Velocity) * 0.004;
            spring.Position = spring.Destination + displacement + spring.Velocity * 0.004;
        }
    }

    // Handwired settleFloat64Objects: snaps each object whose springs are all within both
    // thresholds, and returns the bits.
    public static double[] SettleFloat64Objects(IReadOnlyList<Float64Spring> springs)
    {
        var threshold = Js.F(0.01);
        return [.. ObjectSprings.Select(members =>
        {
            var settled = members.All(spring =>
                Math.Abs(springs[spring].Position - springs[spring].Destination) <= threshold && Math.Abs(springs[spring].Velocity) <= threshold);
            if (settled)
            {
                foreach (var spring in members)
                {
                    springs[spring].Position = springs[spring].Destination;
                    springs[spring].Velocity = 0;
                }
            }
            return settled ? 1.0 : 0.0;
        })];
    }

    // Handwired runFloat64Reference. Frame-free float64 springs: 4 ms Euler steps from the moment
    // the clock started, settle checks every 16 steps, presses applied at their timestamps after
    // the steps up to them, and a stopped clock that starts at a press. A jump snaps its object
    // after the settle check of the press, and the clock stops when each spring is exactly at rest.
    public static Float64Run RunFloat64Reference(IReadOnlyList<Press> presses, IReadOnlyList<double> frameTimes, double offset = 0)
    {
        var springs = InitialPositions.Select(position => new Float64Spring(position + offset, 0, position + offset)).ToArray();
        double? animatedUntilMs = null;
        var originMs = 0.0;
        var settles = new List<SettleEntry>();
        bool Check(double timeMs)
        {
            var settled = SettleFloat64Objects(springs);
            settles.Add(new SettleEntry(timeMs, settled));
            return settled.All(bit => bit == 1);
        }
        void AdvanceTo(double timeMs)
        {
            while (animatedUntilMs is { } until && until + 4 <= timeMs)
            {
                StepFloat64Springs(springs);
                var stepped = until + 4;
                animatedUntilMs = (stepped - originMs) % 64 == 0 && Check(stepped) ? null : stepped;
            }
        }
        var positions = new Dictionary<double, double[]>();
        var displacements = new Dictionary<double, double[]>();
        var nextPress = 0;
        foreach (var frameTime in frameTimes)
        {
            while (nextPress < presses.Count && presses[nextPress].TimeStamp <= frameTime)
            {
                var timeMs = presses[nextPress].TimeStamp;
                AdvanceTo(timeMs);
                var jumped = new HashSet<int>();
                while (nextPress < presses.Count && presses[nextPress].TimeStamp == timeMs)
                {
                    var press = presses[nextPress++];
                    var members = ObjectSprings[press.Object];
                    for (var index = 0; index < members.Length; index++) springs[members[index]].Destination = press.Destinations[index] + offset;
                    if (press.Jump) jumped.Add(press.Object);
                }
                void SnapJumped()
                {
                    foreach (var item in jumped)
                    {
                        foreach (var spring in ObjectSprings[item])
                        {
                            springs[spring].Position = springs[spring].Destination;
                            springs[spring].Velocity = 0;
                        }
                    }
                }
                if (animatedUntilMs is null)
                {
                    var settled = SettleFloat64Objects(springs);
                    settles.Add(new SettleEntry(timeMs, settled));
                    SnapJumped();
                    if (settled.Where((bit, item) => bit == 0 && !jumped.Contains(item)).Any())
                    {
                        animatedUntilMs = timeMs;
                        originMs = timeMs;
                    }
                }
                else
                {
                    SnapJumped();
                    if (springs.All(spring => spring.Position == spring.Destination && spring.Velocity == 0)) animatedUntilMs = null;
                }
            }
            AdvanceTo(frameTime);
            positions[frameTime] = [.. springs.Select(spring => spring.Position)];
            displacements[frameTime] = [.. springs.Select(spring => spring.Position - spring.Destination)];
        }
        return new Float64Run(positions, displacements, settles);
    }

    // Handwired runUiMdOrder. The order of chenguini ui.md in float64: handle inputs, make the new
    // layout, then an animation tick that catches up toward the new destinations, with one settle
    // check in each frame.
    public static Dictionary<double, double[]> RunUiMdOrder(IReadOnlyList<Press> presses, IReadOnlyList<double> frameTimes)
    {
        var springs = InitialPositions.Select(position => new Float64Spring(position, 0, position)).ToArray();
        double? animatedUntilMs = null;
        var positions = new Dictionary<double, double[]>();
        var nextPress = 0;
        foreach (var now in frameTimes)
        {
            while (nextPress < presses.Count && presses[nextPress].TimeStamp <= now)
            {
                var press = presses[nextPress++];
                var members = ObjectSprings[press.Object];
                for (var index = 0; index < members.Length; index++) springs[members[index]].Destination = press.Destinations[index];
            }
            var newAnimatedUntilMs = animatedUntilMs ?? now;
            var steps = Math.Min(300, Math.Floor((now - newAnimatedUntilMs) / 4));
            newAnimatedUntilMs += steps * 4;
            for (var step = 0; step < steps; step++) StepFloat64Springs(springs);
            animatedUntilMs = SettleFloat64Objects(springs).All(bit => bit == 1) ? null : newAnimatedUntilMs;
            positions[now] = [.. springs.Select(spring => spring.Position)];
        }
        return positions;
    }

    // Handwired appPresses: 12 presses between frames on the 1/64 ms grid, 90 to 700 ms apart, so
    // most of them change the destination of a moving spring. With jumpChance above 0, some presses
    // are jumps.
    public static List<Press> AppPresses(int seed, double jumpChance = 0)
    {
        var random = Js.CreateRandom(seed);
        var presses = new List<Press>();
        var time = 100.0;
        for (var index = 0; index < 12; index++)
        {
            time += 90 + random() * 610;
            var item = random() < 0.6 ? 0 : 1;
            double[] destinations = item == 0
                ? [Js.Round(random() * 800), Js.Round(random() * 600), Js.Round((0.5 + random()) * 64) / 64]
                : [Js.Round(random() * 800)];
            // The run draws a number only when jumpChance is above 0, so the traces without jumps
            // stay the same.
            var jump = jumpChance > 0 && random() < jumpChance;
            presses.Add(new Press(Js.Round(time * 64) / 64, item, destinations, jump));
        }
        return presses;
    }

    // Handwired framePositions.
    public static Dictionary<double, double[]> FramePositions(AppRun run) =>
        run.Frames.ToDictionary(frame => frame.Key, frame => frame.Value.Positions);

    // Handwired frameDisplacements: the stored displacement of each spring after the evaluation of
    // each frame.
    public static Dictionary<double, double[]> FrameDisplacements(AppRun run) =>
        run.Frames.ToDictionary(
            frame => frame.Key,
            frame => Enumerable.Range(0, 4).Select(spring => (double)frame.Value.Outputs[SpringCell(spring, 0)]).ToArray());

    // Handwired maxFrameDifference: the largest difference at the frame times that both runs share.
    // More than 20 frame times must be shared.
    public static double MaxFrameDifference(IReadOnlyDictionary<double, double[]> left, IReadOnlyDictionary<double, double[]> right)
    {
        var worst = 0.0;
        var shared = 0;
        foreach (var (time, positions) in left)
        {
            if (!right.TryGetValue(time, out var other)) continue;
            shared++;
            for (var index = 0; index < positions.Length; index++) worst = Math.Max(worst, Math.Abs(positions[index] - other[index]));
        }
        Assert.True(shared > 20, $"expected more than 20 shared frame times, got {shared}");
        return worst;
    }

    // Handwired differingBits: the count of entries that differ by Object.is.
    public static int DifferingBits(float[] left, float[] right)
    {
        var count = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (!Js.ObjectIs(left[index], right[index])) count++;
        }
        return count;
    }

    // Handwired traceCases: seeds 1 to 8 without jumps, then 4 seeds in which about a third of the
    // presses jump.
    public static readonly (int Seed, double JumpChance)[] TraceCases =
    [
        .. new[] { 1, 2, 3, 4, 5, 6, 7, 8 }.Select(seed => (seed, 0.0)),
        .. new[] { 21, 22, 23, 24 }.Select(seed => (seed, 0.35)),
    ];

    // Handwired traceSchedules: the 120 fps baseline, and 10, 30 and 60 fps, and 60 and 120 fps
    // with a 500 ms stall that starts 100 ms before the third press.
    public static (List<double> Baseline, List<double>[] Others) TraceSchedules(IReadOnlyList<Press> presses)
    {
        var durationMs = presses[^1].TimeStamp + 2000;
        var stall = (presses[2].TimeStamp - 100, 500.0);
        return (
            AdvanceFrameTests.FrameTimesAt(120, durationMs),
            [
                AdvanceFrameTests.FrameTimesAt(10, durationMs),
                AdvanceFrameTests.FrameTimesAt(30, durationMs),
                AdvanceFrameTests.FrameTimesAt(60, durationMs),
                AdvanceFrameTests.FrameTimesAt(60, durationMs, stall),
                AdvanceFrameTests.FrameTimesAt(120, durationMs, stall),
            ]);
    }

    // run.settles toEqual reference.settles: the same length, equal times, and equal bits by
    // Object.is.
    public static void AssertSameSettles(IReadOnlyList<SettleEntry> expected, IReadOnlyList<SettleEntry> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.True(expected[index].TimeMs == actual[index].TimeMs, $"settle check {index}: expected {expected[index].TimeMs:R} ms, got {actual[index].TimeMs:R} ms");
            Js.AllIs(expected[index].Settled, actual[index].Settled);
        }
    }

    // JSON.stringify(run.settles) === JSON.stringify(reference.settles): JSON writes -0 as 0, so
    // this compares times and bits with ==.
    public static bool SameSettlesAsJson(IReadOnlyList<SettleEntry> left, IReadOnlyList<SettleEntry> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => pair.First.TimeMs == pair.Second.TimeMs
            && pair.First.Settled.Length == pair.Second.Settled.Length
            && pair.First.Settled.Zip(pair.Second.Settled).All(bits => bits.First == bits.Second));
}
