// Handwired source: tests/runtime/builder-differential.test.ts.
// A differential check of the rewrites of the builder. A sign error in the term merge, the
// constant fold, the composition of affine producers or the constant gate of a ReGLU gives a
// wrong program, and both Handwired evaluators run that same program. This test records the
// intended expression of each operation before the builder changes it, evaluates that record in
// float64 with no rewrite, and compares the result with the reference evaluator. The rewrites
// change the FP32 rounding, so a continuous wire is compared with a relative tolerance and an
// absolute floor. A boolean atom gives an exact bit and is compared exactly.

namespace Sharpwired.Core.Tests;

public class BuilderDifferentialTests
{
    private enum ShadowKind
    {
        Affine,
        Reglu,
        Boolean,
    }

    // Wire is the wire that the circuit returned. An alias or a shared expression can make it an
    // earlier wire. The recorded intent then equals the value of that wire.
    private sealed record ShadowOperation(ShadowKind Kind, Term[] LeftTerms, double LeftBias, Term[] GateTerms, double GateBias, Wire Wire);

    // A circuit with a record of the intent of each operation, taken when the circuit builds it.
    private sealed class ShadowCircuit(int sourceSize)
    {
        public Circuit Circuit { get; } = new(sourceSize);

        public List<ShadowOperation> Operations { get; } = [];

        public Wire Affine(Term[] terms, double bias)
        {
            var wire = Circuit.Affine(terms, bias);
            Operations.Add(new ShadowOperation(ShadowKind.Affine, terms, bias, [], 0, wire));
            return wire;
        }

        public Wire Reglu(Term[] leftTerms, double leftBias, Term[] gateTerms, double gateBias)
        {
            var wire = Circuit.Reglu(leftTerms, leftBias, gateTerms, gateBias);
            Operations.Add(new ShadowOperation(ShadowKind.Reglu, leftTerms, leftBias, gateTerms, gateBias, wire));
            return wire;
        }

        public Wire Boolean(Wire value)
        {
            var wire = Circuit.Boolean(value);
            Operations.Add(new ShadowOperation(ShadowKind.Boolean, [new Term(value, 1)], 0, [], 0, wire));
            return wire;
        }
    }

    // bias + Σ coefficient × value in float64, in the given term order, with no FP32 rounding.
    private static double EvaluateNaiveAffine(Term[] terms, double bias, Dictionary<Wire, double> values)
    {
        var value = bias;
        foreach (var (wire, coefficient) in terms)
        {
            Validation.Invariant(values.TryGetValue(wire, out var operand), "shadow read a wire with no recorded value");
            value += coefficient * operand;
        }
        return value;
    }

    // Evaluate the recorded intent in float64. A boolean uses the rule of the reference evaluator:
    // 0.5 rounds up.
    private static Dictionary<Wire, double> EvaluateShadow(ShadowCircuit shadow, float[] source)
    {
        var values = new Dictionary<Wire, double>();
        for (var wire = 0; wire < source.Length; wire++) values[Wire.FromPosition(wire)] = source[wire];
        foreach (var operation in shadow.Operations)
        {
            var left = EvaluateNaiveAffine(operation.LeftTerms, operation.LeftBias, values);
            values[operation.Wire] = operation.Kind switch
            {
                ShadowKind.Affine => left,
                ShadowKind.Reglu => left * Math.Max(0, EvaluateNaiveAffine(operation.GateTerms, operation.GateBias, values)),
                // The score is within zero and one.
                _ => left < 0.5 ? 0 : 1,
            };
        }
        return values;
    }

    // Continuous wires agree within the FP32 rounding. Boolean wires agree exactly.
    private static bool CloseEnough(double naive, double reference, bool isBoolean)
    {
        if (isBoolean) return naive == reference;
        var difference = Math.Abs(naive - reference);
        if (difference <= 1e-4) return true;
        return difference <= 1e-4 * Math.Max(Math.Abs(naive), Math.Abs(reference));
    }

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);

    // Small coefficients keep the magnitudes small across deep composition chains, so that the
    // absolute floor of the tolerance has a meaning.
    private static double RandomCoefficient(Func<double> random) => new double[] { -1, -0.5, -0.25, 0, 0.25, 0.5, 1, 2 }[RandomInteger(random, 8)];

    private static double RandomBias(Func<double> random) => new double[] { -2, -1, -0.5, 0, 0.5, 1, 2 }[RandomInteger(random, 7)];

    private static Term RandomTerm(Func<double> random, IReadOnlyList<Wire> wires)
    {
        var wire = wires[RandomInteger(random, wires.Count)];
        return new Term(wire, RandomCoefficient(random));
    }

    private static Term[] RandomTerms(Func<double> random, IReadOnlyList<Wire> wires, int maximumTerms) =>
        [.. Enumerable.Range(0, RandomInteger(random, maximumTerms + 1)).Select(_ => RandomTerm(random, wires))];

    // Build the shapes that start each rewrite of the builder.
    private static Wire EmitRandomOperation(
        Func<double> random,
        ShadowCircuit shadow,
        List<Wire> wires,
        List<Wire> unitWires,
        List<Wire> constantWires,
        List<Wire> affineWires)
    {
        var shape = RandomInteger(random, 10);
        Wire wire;
        if (shape == 0 && constantWires.Count > 0)
        {
            // Constant wires in the terms start the constant fold.
            var constant = constantWires[RandomInteger(random, constantWires.Count)];
            Term[] terms = [RandomTerm(random, wires), new Term(constant, RandomCoefficient(random)), new Term(constant, RandomCoefficient(random))];
            wire = shadow.Affine(terms, RandomBias(random));
        }
        else if (shape == 1)
        {
            // A new constant wire, for later folds and constant gates. Larger magnitudes put positive
            // constant gates away from one, where the scale of the bias and the coefficients shows.
            var constantValue = new double[] { -3, -2, -1, 0.5, 1, 2, 3 }[RandomInteger(random, 7)];
            wire = shadow.Affine([], constantValue);
            constantWires.Add(wire);
        }
        else if (shape == 2)
        {
            // Terms of the same wire start the term merge.
            var target = wires[RandomInteger(random, wires.Count)];
            var first = new Term(target, RandomCoefficient(random));
            var second = new Term(target, RandomCoefficient(random));
            var third = RandomTerm(random, wires);
            var fourth = new Term(target, RandomCoefficient(random));
            wire = shadow.Affine([first, second, third, fourth], RandomBias(random));
        }
        else if (shape == 3 && affineWires.Count > 0)
        {
            // Chains of affine operations start the composition.
            Term[] terms = [RandomTerm(random, affineWires), RandomTerm(random, affineWires), RandomTerm(random, wires)];
            wire = shadow.Affine(terms, RandomBias(random));
        }
        else if (shape == 4)
        {
            // One term with coefficient 1 and no bias starts the alias.
            wire = shadow.Affine([new Term(wires[RandomInteger(random, wires.Count)], 1)], 0);
        }
        else if (shape == 5 && constantWires.Count > 0)
        {
            // A ReGLU with a constant gate becomes an affine. A gate below 0 gives exactly zero. A
            // gate other than one scales the left coefficients and the left bias, so the left bias is
            // not zero and the gate is away from one.
            var constant = constantWires[RandomInteger(random, constantWires.Count)];
            var gateSign = RandomInteger(random, 2) == 0 ? 1 : -1;
            var nonzeroBias = new double[] { -2, -1, -0.5, 0.5, 1, 2 }[RandomInteger(random, 6)];
            Term[] leftTerms = [RandomTerm(random, wires), RandomTerm(random, wires)];
            wire = shadow.Reglu(leftTerms, nonzeroBias, [new Term(constant, gateSign * 2)], RandomBias(random));
        }
        else if (shape == 6)
        {
            // A ReGLU whose left form can fold to exactly zero.
            var gateTerms = RandomTerms(random, wires, 3);
            wire = shadow.Reglu([], 0, gateTerms, RandomBias(random));
        }
        else if (shape <= 8)
        {
            // A ReGLU over live wires.
            var leftTerms = RandomTerms(random, wires, 4);
            var leftBias = RandomBias(random);
            var gateTerms = RandomTerms(random, wires, 4);
            wire = shadow.Reglu(leftTerms, leftBias, gateTerms, RandomBias(random));
        }
        else
        {
            // A boolean atom over a wire within zero and one.
            var input = unitWires[RandomInteger(random, unitWires.Count)];
            wire = shadow.Boolean(input);
            unitWires.Add(wire);
        }
        // Keep the wires by category, so that later operations can build the shapes above.
        if (!wires.Contains(wire)) wires.Add(wire);
        var operationIndex = wire.Position - shadow.Circuit.SourceSize;
        if (operationIndex >= 0 && shadow.Circuit.Operations[operationIndex].Kind == OperationKind.Affine && !affineWires.Contains(wire))
        {
            affineWires.Add(wire);
        }
        return wire;
    }

    // 'builder rewrite differential oracle' / 'naive intent matches the reference across seeded rewrite-heavy circuits'.
    [Fact]
    public void Naive_intent_matches_the_reference_across_seeded_rewrite_heavy_circuits()
    {
        var random = Js.CreateRandom(0x5ad0c);
        var comparedWires = 0;
        var booleanWires = 0;
        var aliasReuses = 0;
        for (var circuitIndex = 0; circuitIndex < 400; circuitIndex++)
        {
            var sourceSize = 3 + RandomInteger(random, 4);
            var shadow = new ShadowCircuit(sourceSize);
            // The first two source wires stay within zero and one, for the booleans.
            shadow.Circuit.AssumeRange(shadow.Circuit.Source(0), 0, 1);
            shadow.Circuit.AssumeRange(shadow.Circuit.Source(1), 0, 1);
            var wires = Enumerable.Range(0, sourceSize).Select(Wire.FromPosition).ToList();
            var unitWires = new List<Wire> { Wire.FromPosition(0), Wire.FromPosition(1) };
            var constantWires = new List<Wire>();
            var affineWires = new List<Wire>();
            var operationCount = 10 + RandomInteger(random, 50);
            var emittedBefore = shadow.Operations.Count;
            for (var operation = 0; operation < operationCount; operation++)
            {
                var widthBefore = shadow.Circuit.Width;
                var wire = EmitRandomOperation(random, shadow, wires, unitWires, constantWires, affineWires);
                // No new operation: the builder returned an alias or an existing expression.
                if (shadow.Circuit.Width == widthBefore)
                {
                    Validation.Invariant(wire.Position < widthBefore, "reused wire must already exist");
                    aliasReuses++;
                }
            }
            Validation.Invariant(shadow.Operations.Count > emittedBefore, "recorded no operations");
            shadow.Circuit.ValidateCircuit();
            for (var sampleIndex = 0; sampleIndex < 6; sampleIndex++)
            {
                // The boolean inputs stay within zero and one. The other cells have small magnitudes of both signs.
                var source = new float[sourceSize];
                for (var wire = 0; wire < sourceSize; wire++) source[wire] = wire < 2 ? RandomInteger(random, 2) : (float)(random() * 4 - 2);
                var reference = shadow.Circuit.EvaluateReference(source);
                var naive = EvaluateShadow(shadow, source);
                foreach (var operation in shadow.Operations)
                {
                    var referenceValue = reference[operation.Wire.Position];
                    var naiveValue = naive[operation.Wire];
                    var isBoolean = operation.Kind == ShadowKind.Boolean;
                    if (isBoolean) booleanWires++;
                    comparedWires++;
                    Assert.True(
                        CloseEnough(naiveValue, referenceValue, isBoolean),
                        $"circuit {circuitIndex} sample {sampleIndex} wire {operation.Wire} ({operation.Kind}): naive {naiveValue} vs reference {referenceValue}");
                }
            }
        }
        // The generator built the shapes that the check needs.
        Assert.True(comparedWires > 50_000, $"compared {comparedWires} wires");
        Assert.True(booleanWires > 0);
        Assert.True(aliasReuses > 0);
    }

    // 'builder rewrite differential oracle' / 'hand-built rewrite shapes evaluate exactly to their intent'.
    [Fact]
    public void Hand_built_rewrite_shapes_evaluate_exactly_to_their_intent()
    {
        // Composition three deep, with a merge, a fold and constant gates.
        var shadow = new ShadowCircuit(2);
        shadow.Circuit.AssumeRange(shadow.Circuit.Source(0), 0, 1);
        var s0 = Wire.FromPosition(0);
        var constant = shadow.Affine([], 2); // wire 2, constant 2
        var first = shadow.Affine([(s0, 3), (constant, 1)], 1); // 3 × v0 + 2 + 1 = 3 × v0 + 3
        var second = shadow.Affine([(first, 2), (s0, 0.5), (s0, 0.5)], -1); // 2 × first + v0 - 1
        var third = shadow.Affine([(second, 1), (first, -1)], 0); // second - first
        // A constant gate below 0 gives exactly zero.
        var demotedNegative = shadow.Reglu([(s0, 5)], 7, [(constant, -1)], 0);
        // A constant gate above 0 scales the left form.
        var demotedPositive = shadow.Reglu([(s0, 5)], 7, [(constant, 1)], 0);
        // An identity returns the input wire and adds no operation.
        var alias = shadow.Affine([(third, 1)], 0);

        Assert.Equal(third, alias);
        Assert.Equal(OperationKind.Affine, shadow.Circuit.Operations[demotedNegative.Position - 2].Kind);
        Assert.Empty(shadow.Circuit.Operations[demotedNegative.Position - 2].LeftTerms);

        float[] source = [0.5f, 0];
        var reference = shadow.Circuit.EvaluateReference(source);
        var naive = EvaluateShadow(shadow, source);
        foreach (var operation in shadow.Operations)
        {
            Assert.True(CloseEnough(naive[operation.Wire], reference[operation.Wire.Position], operation.Kind == ShadowKind.Boolean));
        }
        // The constant gates against the closed form.
        Js.Is(0, reference[demotedNegative.Position]);
        Assert.True(Math.Abs(reference[demotedPositive.Position] - (7 + 5 * 0.5) * 2) < 0.5e-5);
    }
}
