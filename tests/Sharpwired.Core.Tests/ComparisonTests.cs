// Handwired source: tests/runtime/comparison.test.ts.
// Each test name holds the Handwired test name. Handwired checkTensorAgreement compiles the
// circuit and compares the tensor evaluator with the reference evaluator. This port keeps the
// validateCircuit part, and PHASE2-TENSOR-ASSERTIONS.md lists the tensor part. Handwired cost
// reads compileTensorProgram([root]). Cost here counts the operations that the root reads, by
// the findLiveOperations rule without gather plans, and the layers of their depths.

using System.Numerics;

namespace Sharpwired.Core.Tests;

public class ComparisonTests
{
    private static readonly double SmallestSubnormal = Math.ScaleB(1.0, -149);
    private static readonly double SmallestNormal = Math.ScaleB(1.0, -126);
    private static readonly double LargestFinite = float.MaxValue;

    private static readonly Relation[] Relations =
        [Relation.Greater, Relation.GreaterOrEqual, Relation.Less, Relation.LessOrEqual, Relation.Equal];

    private static double Float32FromBits(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    // The neighboring FP32 value above value, for example NextUp(1) is 1 + 2^-23.
    private static double NextUp(double value)
    {
        var rounded = (float)value;
        if (rounded == 0) return SmallestSubnormal;
        var bits = BitConverter.SingleToUInt32Bits(rounded);
        return Float32FromBits(unchecked(rounded > 0 ? bits + 1 : bits - 1));
    }

    private static double NextDown(double value) => -NextUp(-value);

    // A random finite FP32 bit pattern: every exponent has the same probability, so tiny and
    // huge values occur as frequently as ordinary values.
    private static double RandomFiniteFloat32(Func<double> random)
    {
        while (true)
        {
            var value = Float32FromBits((uint)Math.Floor(random() * 4_294_967_296));
            if (double.IsFinite(value)) return value;
        }
    }

    private static int NativeCompare(double a, Relation relation, double b) => relation switch
    {
        Relation.Greater => a > b ? 1 : 0,
        Relation.GreaterOrEqual => a >= b ? 1 : 0,
        Relation.Less => a < b ? 1 : 0,
        Relation.LessOrEqual => a <= b ? 1 : 0,
        Relation.Equal => a == b ? 1 : 0,
        _ => throw new ArgumentException("unknown relation"),
    };

    // Differences that a comparison must get right, in groups where FP32 has a different behavior.
    private static List<double> DifferenceClasses(double bound, Func<double> random, int randomCount)
    {
        static IEnumerable<double> BothSigns(params double[] values) => values.SelectMany(value => new[] { value, -value });
        var values = new List<double> { 0, -0.0 };
        values.AddRange(BothSigns(SmallestSubnormal, 2 * SmallestSubnormal, 3 * SmallestSubnormal, Js.F(Math.Pow(2, -140)), Js.F(Math.Pow(2, -126) - Math.Pow(2, -149))));
        values.AddRange(BothSigns(SmallestNormal, NextUp(SmallestNormal), Js.F(Math.Pow(2, -100))));
        values.AddRange(BothSigns(Js.F(Math.Pow(2, -40)), NextDown(1), 1, NextUp(1), Js.F(0.3)));
        values.AddRange(BothSigns(NextDown(Math.Pow(2, 14)), Math.Pow(2, 14), NextDown(Math.Pow(2, 24)), Math.Pow(2, 24), NextUp(Math.Pow(2, 24)), Math.Pow(2, 25)));
        values.AddRange(BothSigns(Math.Pow(2, 60), Math.Pow(2, 100), NextDown(LargestFinite), LargestFinite));
        for (var index = 0; index < randomCount; index++)
        {
            values.Add(RandomFiniteFloat32(random));
            values.Add(Js.F((2 * random() - 1) * bound));
        }
        return [.. values.Where(value => Math.Abs(value) <= bound)];
    }

    // Operand pairs within minimum..maximum, which contains 0: every class of difference against
    // 0, equal and adjacent operands, and random bit patterns.
    private static List<(double A, double B)> OperandPairs(double minimum, double maximum, int seed, int randomCount)
    {
        var random = Js.CreateRandom(seed);
        bool InRange(double value) => value >= minimum && value <= maximum;
        var pairs = new List<(double A, double B)>();
        foreach (var difference in DifferenceClasses(maximum - minimum, random, randomCount))
        {
            if (InRange(difference)) pairs.Add((difference, 0));
            if (InRange(-difference)) pairs.Add((0, -difference));
        }
        double Pick(bool uniform)
        {
            while (true)
            {
                var value = uniform ? Js.F(minimum + random() * (maximum - minimum)) : RandomFiniteFloat32(random);
                if (InRange(value)) return value;
            }
        }
        for (var index = 0; index < randomCount; index++)
        {
            var a = Pick(index % 2 == 0);
            pairs.Add((a, a));
            pairs.Add((a, Pick(index % 3 == 0)));
            foreach (var neighbor in new[] { NextUp(a), NextDown(a) })
            {
                if (InRange(neighbor))
                {
                    pairs.Add((a, neighbor));
                    pairs.Add((neighbor, a));
                }
            }
        }
        return pairs;
    }

    // The operations that the root reads, directly or through other operations, as Handwired
    // findLiveOperations marks them without gather plans.
    internal static SortedSet<int> LiveOperations(Circuit circuit, IEnumerable<Wire> roots)
    {
        var live = new SortedSet<int>();
        var pending = new Stack<Wire>(roots);
        while (pending.Count > 0)
        {
            var wire = pending.Pop();
            if (wire.Position < circuit.SourceSize) continue;
            var operationIndex = wire.Position - circuit.SourceSize;
            if (!live.Add(operationIndex)) continue;
            var operation = circuit.Operations[operationIndex];
            foreach (var term in operation.LeftTerms) pending.Push(term.Wire);
            foreach (var term in operation.GateTerms) pending.Push(term.Wire);
            if (operation.IndexRead is { } read)
            {
                foreach (var candidate in read.Table.Candidates) pending.Push(candidate);
            }
        }
        return live;
    }

    // The operation count and the layer count of the program for one root: one layer for each
    // depth of a live operation, as compileTensorProgram without recomputeDepths builds it.
    internal static (int Operations, int Layers) Cost(Circuit circuit, Wire root) => Cost(circuit, [root]);

    // The same count for the program of several roots, as compileTensorProgram(roots) builds it.
    internal static (int Operations, int Layers) Cost(Circuit circuit, IEnumerable<Wire> roots)
    {
        var live = LiveOperations(circuit, roots);
        return (live.Count, live.Select(operationIndex => circuit.Operations[operationIndex].Depth).Distinct().Count());
    }

    // Every finite float is an integer times a power of two, so BigInteger gives exact sums and products.
    private readonly record struct Dyadic(BigInteger Mantissa, int Exponent);

    private static Dyadic ToDyadic(double value)
    {
        if (value == 0) return new Dyadic(0, 0);
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        var biasedExponent = (int)((bits >> 52) & 0x7ff);
        var mantissa = new BigInteger(bits & 0xf_ffff_ffff_ffff) | (biasedExponent == 0 ? BigInteger.Zero : BigInteger.One << 52);
        return new Dyadic(bits >> 63 == 1 ? -mantissa : mantissa, biasedExponent == 0 ? -1074 : biasedExponent - 1075);
    }

    private static Dyadic AddDyadic(Dyadic left, Dyadic right)
    {
        var exponent = Math.Min(left.Exponent, right.Exponent);
        return new Dyadic((left.Mantissa << (left.Exponent - exponent)) + (right.Mantissa << (right.Exponent - exponent)), exponent);
    }

    private static Dyadic MultiplyDyadic(Dyadic left, Dyadic right) => new(left.Mantissa * right.Mantissa, left.Exponent + right.Exponent);

    private static Dyadic ExactSum(float[] values, IEnumerable<Term> terms, double bias)
    {
        var sum = ToDyadic(Js.F(bias));
        foreach (var (wire, coefficient) in terms)
        {
            sum = AddDyadic(sum, MultiplyDyadic(ToDyadic(Js.F(coefficient)), ToDyadic(values[wire.Position])));
        }
        return sum;
    }

    // The operations among operationIndices whose FP32 result differs from the exact result of their
    // arithmetic on the same FP32 inputs. For a ReGLU product only the result counts, for example a
    // gate sum of -1 + 2^-100 rounds to -1, but the ReLU returns 0 for both.
    private static int CountRoundedOperations(Circuit circuit, float[] values, IReadOnlyList<int> operationIndices)
    {
        var rounded = 0;
        foreach (var operationIndex in operationIndices)
        {
            var operation = circuit.Operations[operationIndex];
            var exact = ExactSum(values, operation.LeftTerms, operation.LeftBias);
            if (operation.Kind == OperationKind.Reglu)
            {
                var gate = ExactSum(values, operation.GateTerms, operation.GateBias);
                exact = gate.Mantissa > 0 ? MultiplyDyadic(exact, gate) : new Dyadic(0, 0);
            }
            var output = values[circuit.SourceSize + operationIndex];
            if (!AddDyadic(exact, MultiplyDyadic(ToDyadic(output), new Dyadic(-1, 0))).Mantissa.IsZero) rounded++;
        }
        return rounded;
    }

    // Evaluators that add terms in a different way, as a backend can.
    private enum ExecutorMode
    {
        Float64,
        ReversedTerms,
        SumsRoundedOnce,
        FlushSubnormals,
    }

    private static readonly ExecutorMode[] ExecutorModes =
        [ExecutorMode.Float64, ExecutorMode.ReversedTerms, ExecutorMode.SumsRoundedOnce, ExecutorMode.FlushSubnormals];

    private static double[] EvaluateWithMode(Circuit circuit, float[] source, ExecutorMode mode)
    {
        static double Flush(double value) => Math.Abs(Js.F(value)) < SmallestNormal ? 0 : Js.F(value);
        Func<double, double> round = mode switch
        {
            ExecutorMode.Float64 => value => value,
            ExecutorMode.FlushSubnormals => Flush,
            _ => Js.F,
        };
        var values = new double[circuit.Width];
        for (var wire = 0; wire < source.Length; wire++)
        {
            values[wire] = mode == ExecutorMode.FlushSubnormals ? (float)Flush(source[wire]) : source[wire];
        }
        double Sum(IReadOnlyList<Term> terms, double bias)
        {
            if (mode == ExecutorMode.SumsRoundedOnce)
            {
                var unrounded = Js.F(bias);
                foreach (var (wire, coefficient) in terms) unrounded += Js.F(coefficient) * values[wire.Position];
                return Js.F(unrounded);
            }
            if (mode == ExecutorMode.ReversedTerms)
            {
                var reversed = 0.0;
                for (var index = terms.Count - 1; index >= 0; index--)
                {
                    var (wire, coefficient) = terms[index];
                    reversed = Js.F(reversed + Js.F(Js.F(coefficient) * values[wire.Position]));
                }
                return Js.F(reversed + Js.F(bias));
            }
            var value = round(bias);
            foreach (var (wire, coefficient) in terms) value = round(value + round(round(coefficient) * values[wire.Position]));
            return value;
        }
        for (var operationIndex = 0; operationIndex < circuit.Operations.Count; operationIndex++)
        {
            var operation = circuit.Operations[operationIndex];
            var value = Sum(operation.LeftTerms, operation.LeftBias);
            if (operation.Kind == OperationKind.Reglu) value = round(value * Math.Max(0, Sum(operation.GateTerms, operation.GateBias)));
            else if (operation.Kind == OperationKind.Boolean) value = round(round(round(value) + 8_388_609) - 8_388_609);
            values[circuit.SourceSize + operationIndex] = value;
        }
        return values;
    }

    // Two source cells within minimum..maximum, with each relation built on them.
    private static (Circuit Circuit, Wire[] Bits) BuildPairCircuit(double minimum, double maximum, double? grid = null, bool endWithBoolean = false)
    {
        var circuit = new Circuit(2);
        foreach (var position in new[] { 0, 1 })
        {
            circuit.AssumeRange(Wire.FromPosition(position), minimum, maximum);
            if (grid is { } step) circuit.AssumeGrid(Wire.FromPosition(position), step);
        }
        var options = new CompareOptions { EndWithBoolean = endWithBoolean };
        var bits = Relations.Select(relation => circuit.Compare(circuit.Source(0), relation, circuit.Source(1), options)).ToArray();
        return (circuit, bits);
    }

    private static float[] Pair(double a, double b) => [(float)a, (float)b];

    // sign cascade: 'equals JavaScript over every class of FP32 difference, and no operation between
    // the kept difference and the bit or score rounds'.
    [Fact]
    public void Sign_cascade_equals_JavaScript_over_every_class_of_FP32_difference_and_no_stage_operation_rounds()
    {
        (double Minimum, double Maximum, int Seed)[] configurations =
        [
            (0, 16_384, 1),
            (-Math.Pow(2, 24), Math.Pow(2, 24), 2),
            (-Js.F(LargestFinite / 2), Js.F(LargestFinite / 2), 3),
            (-Math.Pow(2, -120), Math.Pow(2, -120), 4),
            (-1, 1, 5),
        ];
        foreach (var (minimum, maximum, seed) in configurations)
        {
            var pairs = OperandPairs(minimum, maximum, seed, 150);
            foreach (var endWithBoolean in new[] { false, true })
            {
                var (circuit, bits) = BuildPairCircuit(minimum, maximum, endWithBoolean: endWithBoolean);
                // Every operation that Compare built, except two kinds that can round by design: the
                // kept difference, which reads the two operand cells, for example f(16384 - 2^-20) is
                // 16384 but keeps the sign, and the score that a final boolean atom reads, which only
                // has to be on the correct side of 0.5. So the check covers the affine
                // x - relu(x - limit) of each clamp stage, which the cascade needs to be exact.
                var atomOperands = circuit.Operations
                    .Where(operation => operation.Kind == OperationKind.Boolean)
                    .SelectMany(operation => operation.LeftTerms.Select(term => term.Wire))
                    .ToHashSet();
                var stageOperations = Enumerable.Range(0, circuit.Operations.Count)
                    .Where(operationIndex =>
                    {
                        var operation = circuit.Operations[operationIndex];
                        return operation.RoundingPoint == RoundingPointKind.SignCascade
                            && !(operation.Kind == OperationKind.Affine && operation.LeftTerms.All(term => term.Wire.Position < circuit.SourceSize))
                            && !atomOperands.Contains(Wire.FromPosition(circuit.SourceSize + operationIndex));
                    })
                    .ToArray();
                Assert.Contains(stageOperations, operationIndex => circuit.Operations[operationIndex].Kind == OperationKind.Affine);
                var wrong = 0;
                var rounded = 0;
                foreach (var (a, b) in pairs)
                {
                    var values = circuit.EvaluateReference(Pair(a, b));
                    for (var index = 0; index < Relations.Length; index++)
                    {
                        if (values[bits[index].Position] != NativeCompare(a, Relations[index], b)) wrong++;
                    }
                    rounded += CountRoundedOperations(circuit, values, stageOperations);
                }
                Assert.Equal((minimum, endWithBoolean, 0, 0), (minimum, endWithBoolean, wrong, rounded));
                circuit.ValidateCircuit();
            }
        }
    }

    // sign cascade: 'costs for operands within 0..16,384, and the stage count for the largest bound'.
    [Fact]
    public void Sign_cascade_costs_for_operands_within_0_to_16384_and_the_stage_count_for_the_largest_bound()
    {
        var (circuit, bits) = BuildPairCircuit(0, 16_384);
        Assert.Equal([(16, 15), (15, 14), (16, 15), (15, 14), (28, 14)], bits.Select(bit => Cost(circuit, bit)));
        var rounding = BuildPairCircuit(0, 16_384, endWithBoolean: true);
        Assert.Equal([(15, 13), (14, 13), (15, 13), (14, 13), (25, 13)], rounding.Bits.Select(bit => Cost(rounding.Circuit, bit)));
        // Operands within ±largestFinite / 2: 12 stages, 11 of them clamps.
        var largest = BuildPairCircuit(-Js.F(LargestFinite / 2), Js.F(LargestFinite / 2));
        Assert.Equal((1 + 3 + 10 * 2 + 1, 1 + 11 * 2 + 1), Cost(largest.Circuit, largest.Bits[1]));
    }

    // Sharpwired addition: the stage plan of the planSignCascade comment, and the 12-stage limit.
    [Fact]
    public void The_sign_cascade_plan_for_a_bound_of_16384_and_for_the_largest_bound()
    {
        var plan = Macros.PlanSignCascade(16_384, 149);
        Assert.Equal([10, 24, 24, 24, 24, 24, 19], plan.Select(stage => Math.ILogB(stage.Scale)));
        Assert.All(plan, stage => Assert.Equal(1, stage.Limit));
        Assert.Equal(12, Macros.PlanSignCascade(Js.F(LargestFinite), 149).Count);
    }

    // sign cascade: 'a comparison against 0 reads its operand directly, constants fold, and a wire equals itself'.
    [Fact]
    public void A_comparison_against_0_reads_its_operand_directly_constants_fold_and_a_wire_equals_itself()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), -16_384, 16_384);
        var zero = circuit.Affine([], 0);
        var positive = circuit.Compare(circuit.Source(0), Relation.Greater, zero);
        var notNegative = circuit.Compare(zero, Relation.LessOrEqual, circuit.Source(0));
        Assert.Equal((15, 14), Cost(circuit, positive));
        foreach (var value in new[] { 0, -0.0, SmallestSubnormal, -SmallestSubnormal, 1, -16_384, NextDown(16_384) })
        {
            var values = circuit.EvaluateReference([(float)value]);
            Js.Is(value > 0 ? 1 : 0, values[positive.Position]);
            Js.Is(value >= 0 ? 1 : 0, values[notNegative.Position]);
        }
        // A threshold that is not 0 keeps its difference as an operation.
        var threshold = Js.F(0.3);
        var aboveThreshold = circuit.Compare(circuit.Source(0), Relation.GreaterOrEqual, circuit.Affine([], threshold));
        foreach (var value in new[] { threshold, NextDown(threshold), NextUp(threshold), 0.3 })
        {
            Js.Is(Js.F(value) >= threshold ? 1 : 0, circuit.EvaluateReference([(float)value])[aboveThreshold.Position]);
        }
        var seven = circuit.Affine([], 7);
        var constantBit = circuit.Compare(seven, Relation.Greater, circuit.Affine([], 3));
        Assert.Equal(1, circuit.Constants[constantBit.Position]);
        Assert.Equal(1, circuit.Constants[circuit.Compare(circuit.Source(0), Relation.Equal, circuit.Source(0)).Position]);
        Assert.Equal(0, circuit.Constants[circuit.Compare(circuit.Source(0), Relation.Less, circuit.Source(0)).Position]);
    }

    // sign cascade: 'refuses open ranges, overflowing differences and unknown relations'.
    [Fact]
    public void Refuses_open_ranges_overflowing_differences_and_unknown_relations()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 16_384);
        circuit.AssumeRange(circuit.Source(1), 0, double.PositiveInfinity);
        circuit.AssumeRange(circuit.Source(2), -LargestFinite, LargestFinite);
        Js.Throws(() => circuit.Compare(circuit.Source(0), Relation.Greater, circuit.Source(1)), "finite declared ranges");
        var unrangedCircuit = new Circuit(2);
        unrangedCircuit.AssumeRange(unrangedCircuit.Source(0), 0, 1);
        Js.Throws(() => unrangedCircuit.Compare(unrangedCircuit.Source(0), Relation.GreaterOrEqual, unrangedCircuit.Source(1)), "finite declared ranges");
        // A wire compared with itself folds to a constant only with a finite declared range: a product
        // of undeclared cells can be NaN, for example Infinity × 0, and NaN >= NaN is false.
        var productCircuit = new Circuit(2);
        var product = productCircuit.Multiply(productCircuit.Source(0), productCircuit.Source(1));
        foreach (var relation in Relations)
        {
            Js.Throws(() => productCircuit.Compare(product, relation, product), "finite declared ranges");
        }
        // f(largestFinite - -largestFinite) is Infinity.
        Js.Throws(() => circuit.Compare(circuit.Source(2), Relation.Greater, circuit.Affine([(circuit.Source(2), -1)])), "can overflow FP32");
        Js.Throws(() => circuit.Compare(circuit.Source(0), (Relation)99, circuit.Source(2)), "comparison relation");
        // Handwired writes the relation value that the caller passed, for example "got !==". Here
        // the caller passed an enum value that has no name, so the message writes the cast.
        Js.Throws(
            () => circuit.Compare(circuit.Source(0), (Relation)99, circuit.Source(2)),
            "comparison relation must be >, >=, <, <= or ===, got (Relation)99");
    }

    // sign cascade: 'the 1732 px column boundary reads the FP32 value, not float64'.
    [Fact]
    public void The_1732_px_column_boundary_reads_the_FP32_value_not_float64()
    {
        // f(f(1732 - 24) × f(1/244)) is one FP32 step below 7, and float64 gives exactly 7.
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 16_384);
        var widthMinusGutter = circuit.RoundAffine([(circuit.Source(0), 1)], -24);
        var columnsReal = circuit.RoundAffine([(widthMinusGutter, Js.F(1.0 / 244))]);
        var atLeastSeven = circuit.Compare(columnsReal, Relation.GreaterOrEqual, circuit.Affine([], 7));
        var values = circuit.EvaluateReference([1732]);
        Js.Is(NextDown(7), values[columnsReal.Position]);
        Js.Is(0, values[atLeastSeven.Position]);
    }

    // grids: 'operands on a grid get integer bits that equal JavaScript' (reference part).
    [Fact]
    public void Operands_on_a_grid_get_integer_bits_that_equal_JavaScript()
    {
        var random = Js.CreateRandom(11);
        foreach (var endWithBoolean in new[] { false, true })
        {
            var (circuit, bits) = BuildPairCircuit(-8192, 8192, 1.0 / 64, endWithBoolean);
            var pairs = new List<(double A, double B)> { (0, 0), (-0.0, 0), (8192, -8192), (-8192, 8192), (100, 100 + 1.0 / 64), (100 + 1.0 / 64, 100), (-1.0 / 64, 0) };
            for (var index = 0; index < 3000; index++)
            {
                var a = Js.Round((2 * random() - 1) * 8192 * 64) / 64;
                var b = index % 3 == 0
                    ? a
                    : index % 3 == 1
                        ? Math.Max(-8192, Math.Min(8192, a + (random() < 0.5 ? 1 : -1) / 64.0))
                        : Js.Round((2 * random() - 1) * 8192 * 64) / 64;
                pairs.Add((a, b));
            }
            var wrong = 0;
            foreach (var (a, b) in pairs)
            {
                var values = circuit.EvaluateReference(Pair(a, b));
                circuit.ValidateGrids(values);
                for (var index = 0; index < Relations.Length; index++)
                {
                    if (values[bits[index].Position] != NativeCompare(a, Relations[index], b)) wrong++;
                }
            }
            Assert.Equal(0, wrong);
            circuit.ValidateCircuit();
        }
    }

    // grids: 'costs, mixed grids, thresholds, and the fallback to the cascade'.
    [Fact]
    public void Costs_mixed_grids_thresholds_and_the_fallback_to_the_cascade()
    {
        var (circuit, bits) = BuildPairCircuit(-8192, 8192, 1.0 / 64);
        Assert.Equal([(2, 2), (2, 2), (2, 2), (2, 2), (3, 2)], bits.Select(bit => Cost(circuit, bit)));
        var rounding = BuildPairCircuit(-8192, 8192, 1.0 / 64, endWithBoolean: true);
        Assert.Equal([(1, 1), (1, 1), (1, 1), (1, 1), (3, 2)], rounding.Bits.Select(bit => Cost(rounding.Circuit, bit)));
        // A 1/64 grid against a 1/4 grid resolves on the finer grid, and a constant threshold on the grid folds into the bias.
        var mixed = new Circuit(2);
        mixed.AssumeRange(mixed.Source(0), 0, 4096);
        mixed.AssumeGrid(mixed.Source(0), 1.0 / 64);
        mixed.AssumeRange(mixed.Source(1), 0, 4096);
        mixed.AssumeGrid(mixed.Source(1), 1.0 / 4);
        var pointerPastEdge = mixed.Compare(mixed.Source(0), Relation.Greater, mixed.Source(1));
        var pointerPastHalf = mixed.Compare(mixed.Source(0), Relation.GreaterOrEqual, mixed.Affine([], 7.5));
        Assert.Equal([(2, 2), (2, 2)], new[] { Cost(mixed, pointerPastEdge), Cost(mixed, pointerPastHalf) });
        foreach (var (pointer, edge) in new[] { (10.0, 10.0), (10 + 1.0 / 64, 10.0), (7.5 - 1.0 / 64, 0.0), (7.5, 4096.0), (4096.0, 4096.0) })
        {
            var values = mixed.EvaluateReference(Pair(pointer, edge));
            Js.Is(pointer > edge ? 1 : 0, values[pointerPastEdge.Position]);
            Js.Is(pointer >= 7.5 ? 1 : 0, values[pointerPastHalf.Position]);
        }
        // f(0.3) is on a grid of 2^-25, so operands up to 16,384 are too many grid steps: the cascade takes over.
        var threshold = mixed.Affine([], Js.F(0.3));
        Assert.Equal((15, 14), Cost(mixed, mixed.Compare(mixed.Source(0), Relation.GreaterOrEqual, threshold)));
        // Operands within 0..2^17 on a 1/64 grid fit 2^24 grid steps; 0..2^18 does not.
        Assert.Equal((2, 2), Cost(BuildPairCircuit(0, Math.Pow(2, 17), 1.0 / 64).Circuit, Wire.FromPosition(3)));
        var wide = BuildPairCircuit(0, Math.Pow(2, 18), 1.0 / 64);
        Assert.Equal((15, 14), Cost(wide.Circuit, wide.Bits[1]));
        var wideRounding = BuildPairCircuit(0, Math.Pow(2, 18), 1.0 / 64, endWithBoolean: true);
        Assert.True(Cost(wideRounding.Circuit, wideRounding.Bits[1]).Operations > 1);
    }

    // grids: 'grid bits need no FP32 rounding; atoms do; subnormal values matter only to the cascade'.
    [Fact]
    public void Grid_bits_need_no_FP32_rounding_atoms_do_subnormal_values_matter_only_to_the_cascade()
    {
        var random = Js.CreateRandom(12);
        var gridPairs = Enumerable.Range(0, 1500).Select(index =>
        {
            var a = Js.Round(random() * 4096 * 64) / 64;
            return (a, index % 2 == 0 ? a : Js.Round(random() * 4096 * 64) / 64);
        }).ToArray();
        static int CountWrong(Circuit circuit, Wire[] bits, IEnumerable<(double A, double B)> pairs, ExecutorMode mode)
        {
            var wrong = 0;
            foreach (var (a, b) in pairs)
            {
                var values = EvaluateWithMode(circuit, Pair(a, b), mode);
                for (var index = 0; index < Relations.Length; index++)
                {
                    if (values[bits[index].Position] != NativeCompare(a, Relations[index], b)) wrong++;
                }
            }
            return wrong;
        }
        var grid = BuildPairCircuit(0, 4096, 1.0 / 64);
        Assert.Equal([0, 0, 0, 0], ExecutorModes.Select(mode => CountWrong(grid.Circuit, grid.Bits, gridPairs, mode)));
        var gridRounding = BuildPairCircuit(0, 4096, 1.0 / 64, endWithBoolean: true);
        Assert.Equal(0, CountWrong(gridRounding.Circuit, gridRounding.Bits, gridPairs, ExecutorMode.ReversedTerms));
        Assert.True(CountWrong(gridRounding.Circuit, gridRounding.Bits, gridPairs, ExecutorMode.Float64) > 0);
        var cascadePairs = OperandPairs(0, 4096, 13, 300);
        var cascade = BuildPairCircuit(0, 4096);
        var wrongByMode = ExecutorModes.Select(mode => CountWrong(cascade.Circuit, cascade.Bits, cascadePairs, mode)).ToArray();
        Assert.Equal([0, 0, 0], wrongByMode[..3]);
        // Subnormal values set to 0 break differences below 2^-126, and native comparisons on the same hardware.
        var subnormalPairs = cascadePairs.Count(pair => pair.A != pair.B && Math.Abs(Js.F(pair.A - pair.B)) < SmallestNormal);
        Assert.True(subnormalPairs > 0);
        Assert.True(wrongByMode[3] > 0);
        var normalPairs = cascadePairs.Where(pair => pair.A == pair.B || Math.Abs(Js.F(pair.A - pair.B)) >= SmallestNormal);
        Assert.Equal(0, CountWrong(cascade.Circuit, cascade.Bits, normalPairs, ExecutorMode.FlushSubnormals));
    }

    // grids: 'random circuits over grid sources hold the grids they infer'.
    [Fact]
    public void Random_circuits_over_grid_sources_hold_the_grids_they_infer()
    {
        var random = Js.CreateRandom(21);
        double[] coefficients = [1, -1, 0.5, 2, 3, -0.25, 1.0 / 3, 64];
        // Products grow ranges quickly, so a circuit can end with no provable grid. Count over all circuits.
        var inferred = 0;
        for (var circuitIndex = 0; circuitIndex < 40; circuitIndex++)
        {
            double[] sourceGrids = [1.0 / 64, 1.0 / 4, 1, Math.Pow(2, -10)];
            var circuit = new Circuit(sourceGrids.Length);
            for (var wire = 0; wire < sourceGrids.Length; wire++)
            {
                circuit.AssumeRange(Wire.FromPosition(wire), -512, 512);
                circuit.AssumeGrid(Wire.FromPosition(wire), sourceGrids[wire]);
            }
            Wire PickWire() => Wire.FromPosition((int)Math.Floor(random() * circuit.Width));
            Term[] PickTerms()
            {
                var length = 1 + (int)Math.Floor(random() * 3);
                var terms = new Term[length];
                for (var index = 0; index < length; index++)
                {
                    // JavaScript evaluates the wire before the coefficient.
                    var wire = PickWire();
                    terms[index] = new Term(wire, coefficients[(int)Math.Floor(random() * coefficients.Length)]);
                }
                return terms;
            }
            for (var operationIndex = 0; operationIndex < 30; operationIndex++)
            {
                var choice = (int)Math.Floor(random() * 4);
                if (choice == 0)
                {
                    var terms = PickTerms();
                    circuit.Affine(terms, Js.Round(random() * 8) / 8);
                }
                else if (choice == 1)
                {
                    circuit.RoundAffine(PickTerms());
                }
                else if (choice == 2)
                {
                    var leftTerms = PickTerms();
                    var gateTerms = PickTerms();
                    circuit.Reglu(leftTerms, 0, gateTerms, Js.Round(random() * 4) / 4);
                }
                else
                {
                    var left = PickWire();
                    circuit.Multiply(left, PickWire());
                }
            }
            inferred += circuit.Grids.Skip(circuit.SourceSize).Count(grid => grid is not null);
            for (var sample = 0; sample < 30; sample++)
            {
                var source = sourceGrids.Select(grid => (float)(Js.Round((2 * random() - 1) * 512 / grid) * grid)).ToArray();
                circuit.ValidateGrids(circuit.EvaluateReference(source));
            }
        }
        Assert.True(inferred > 100, $"inferred {inferred} grids");
    }

    private static float[] GradientsAt(Circuit circuit, Wire bit, float[] source, BooleanGradient booleanGradient = BooleanGradient.Reject)
    {
        var values = circuit.EvaluateReference(source);
        var seeds = new float[values.Length];
        seeds[bit.Position] = 1;
        return circuit.BackpropagateReference(values, seeds, booleanGradient);
    }

    // backward pass: 'the cascade runs without a special case and gives the operands 0'.
    [Fact]
    public void The_cascade_runs_without_a_special_case_and_gives_the_operands_0()
    {
        var (circuit, bits) = BuildPairCircuit(0, 16_384);
        foreach (var difference in new[] { -100, -1, -SmallestSubnormal, 0, SmallestSubnormal, Math.Pow(2, -30), 0.3, 100 })
        {
            float[] source = difference >= 0 ? [(float)difference, 0] : [0, (float)-difference];
            foreach (var bit in bits)
            {
                var gradients = GradientsAt(circuit, bit, source);
                Js.Is(0, gradients[0]);
                Js.Is(0, gradients[1]);
            }
        }
    }

    // backward pass: 'grid bits have slope 1 on fractional inputs, and atoms throw without
    // straight-through'.
    [Fact]
    public void Grid_bits_have_slope_1_on_fractional_inputs_and_atoms_throw_without_straight_through()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), -1000, 1000);
        circuit.AssumeGrid(circuit.Source(0), 1);
        var atLeastFive = circuit.Compare(circuit.Source(0), Relation.GreaterOrEqual, circuit.Affine([], 5));
        Assert.Equal([0f, 0f, 1f, 0f, 0f], new[] { 3f, 4f, 4.5f, 5f, 6f }.Select(value => GradientsAt(circuit, atLeastFive, [value])[0]));
        Js.Is(0.5, circuit.EvaluateReference([4.5f])[atLeastFive.Position]);
        // Sharpwired addition: the forward values at the inputs where Handwired reads the gradients.
        Assert.Equal([0.0, 0.0, 0.5, 1.0, 1.0], new[] { 3f, 4f, 4.5f, 5f, 6f }.Select(value => (double)circuit.EvaluateReference([value])[atLeastFive.Position]));
        foreach (var grid in new double?[] { 1, null })
        {
            var rounding = new Circuit(2);
            foreach (var position in new[] { 0, 1 })
            {
                rounding.AssumeRange(Wire.FromPosition(position), 0, 16_384);
                if (grid is { } step) rounding.AssumeGrid(Wire.FromPosition(position), step);
            }
            var bit = rounding.Compare(rounding.Source(0), Relation.Greater, rounding.Source(1), new CompareOptions { EndWithBoolean = true });
            Js.Throws(() => GradientsAt(rounding, bit, [3, 2]), "cannot backpropagate through a boolean atom");
            Assert.True(float.IsFinite(GradientsAt(rounding, bit, [3, 2], BooleanGradient.StraightThrough)[0]));
        }
    }

    // rounding points: 'every operation emitCompare builds is recorded, as integer bits on grids and
    // sign cascade stages otherwise, and atoms are recorded as booleans'.
    [Fact]
    public void Every_operation_Compare_builds_is_recorded_as_integer_bits_on_grids_and_sign_cascade_stages_otherwise()
    {
        foreach (var grid in new double?[] { null, 1.0 / 64 })
        {
            foreach (var endWithBoolean in new[] { false, true })
            {
                foreach (var relation in Relations)
                {
                    var circuit = new Circuit(2);
                    foreach (var position in new[] { 0, 1 })
                    {
                        circuit.AssumeRange(Wire.FromPosition(position), 0, 4096);
                        if (grid is { } step) circuit.AssumeGrid(Wire.FromPosition(position), step);
                    }
                    var bit = circuit.Compare(circuit.Source(0), relation, circuit.Source(1), new CompareOptions { EndWithBoolean = endWithBoolean });
                    var recorded = circuit.RoundingPoints().ToDictionary(point => point.Wire, point => point.Kind);
                    foreach (var operationIndex in LiveOperations(circuit, [bit]))
                    {
                        var operation = circuit.Operations[operationIndex];
                        // Operands within 0..4096 on a 1/64 grid are 2^18 grid steps, so each grid comparison builds integer bits.
                        var expected = operation.Kind == OperationKind.Boolean
                            ? RoundingPointKind.Boolean
                            : grid is null ? RoundingPointKind.SignCascade : RoundingPointKind.IntegerBit;
                        Assert.Equal(expected, recorded[Wire.FromPosition(circuit.SourceSize + operationIndex)]);
                    }
                    // The only operation without a record is the score that the atom of a strict rounding stage copies in.
                    var unrecorded = grid is null && endWithBoolean && relation is Relation.Greater or Relation.Less ? 1 : 0;
                    Assert.Equal(unrecorded, circuit.Operations.Count - recorded.Count);
                }
            }
        }
    }

    // exact minimum, maximum and clamp: 'return exactly Math.min, Math.max and the clamp, including
    // mixed magnitudes and subnormal values'.
    [Fact]
    public void Exact_minimum_maximum_and_clamp_return_exactly_Math_min_Math_max_and_the_clamp()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -16_384, 16_384);
        circuit.AssumeRange(circuit.Source(1), -16_384, 16_384);
        var minimum = circuit.MinimumExact(circuit.Source(0), circuit.Source(1));
        var maximum = circuit.MaximumExact(circuit.Source(0), circuit.Source(1));
        var drifting = circuit.Minimum(circuit.Source(0), circuit.Source(1));
        Assert.Equal(new NumericRange(-16_384, 16_384), circuit.Ranges[minimum.Position]);
        Assert.Equal(new NumericRange(-16_384, 16_384), circuit.Ranges[maximum.Position]);
        var pairs = OperandPairs(-16_384, 16_384, 31, 300);
        pairs.Add((Js.F(1000.37), Js.F(0.3)));
        var wrong = 0;
        foreach (var (a, b) in pairs)
        {
            var values = circuit.EvaluateReference(Pair(a, b));
            if (values[minimum.Position] != Math.Min(a, b) || values[maximum.Position] != Math.Max(a, b)) wrong++;
        }
        Assert.Equal(0, wrong);
        var mixedMagnitudes = circuit.EvaluateReference(Pair(Js.F(1000.37), Js.F(0.3)));
        Js.Is(Js.F(0.3), mixedMagnitudes[minimum.Position]);
        Assert.NotEqual(Js.F(0.3), mixedMagnitudes[drifting.Position]);
        circuit.ValidateCircuit();

        var clampCircuit = new Circuit(3);
        clampCircuit.AssumeRange(clampCircuit.Source(0), -100, 100);
        clampCircuit.AssumeRange(clampCircuit.Source(1), -50, 50);
        clampCircuit.AssumeRange(clampCircuit.Source(2), -50, 50);
        var clamped = clampCircuit.ClampExact(clampCircuit.Source(0), clampCircuit.Source(1), clampCircuit.Source(2));
        var toSixteen = clampCircuit.ClampExact(clampCircuit.Source(0), clampCircuit.Affine([], 0), clampCircuit.Affine([], 16));
        Assert.Equal(new NumericRange(0, 16), clampCircuit.Ranges[toSixteen.Position]);
        var random = Js.CreateRandom(32);
        for (var sample = 0; sample < 2000; sample++)
        {
            var value = Js.F((2 * random() - 1) * 100);
            var low = Js.F((2 * random() - 1) * 50);
            var high = Js.F((2 * random() - 1) * 50);
            var values = clampCircuit.EvaluateReference([(float)value, (float)low, (float)high]);
            Js.Is(Math.Min(Math.Max(value, low), high), values[clamped.Position]);
            Js.Is(Math.Min(Math.Max(value, 0), 16), values[toSixteen.Position]);
        }
    }
}
