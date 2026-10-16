// Handwired source: tests/runtime/numeric-ui-core.test.ts (first-active selection and argmax),
// tests/runtime/random-hardened.test.ts (the reference parts of each test), and the emitFirstActive and
// emitArgmax rows of docs/execution-model.md#macro-contracts. Each test name holds the Handwired
// test name. Handwired evaluates these circuits with the tensor evaluator. This port uses the
// reference evaluator, and PHASE2-TENSOR-ASSERTIONS.md lists the tensor assertions. The layer
// bounds read the layers of the compiled program, as Handwired reads tensorProgram.layers.length.

namespace Sharpwired.Core.Tests;

public class SelectionTests
{
    private static Wire[] Wires(params IEnumerable<int> positions) => [.. positions.Select(Wire.FromPosition)];

    private static double[] ValuesAt(float[] values, IEnumerable<Wire> wires) => [.. wires.Select(wire => (double)values[wire.Position])];

    private static double[] OneHot(int count, int winner) => [.. Enumerable.Range(0, count).Select(item => item == winner ? 1.0 : 0.0)];

    // random-hardened.test.ts buildArgmax: scores in cells 0..count-1 within 0..1, and the unit in cell count.
    private static (Circuit Circuit, ArgmaxSelection Winner) BuildArgmax(int scoreCount)
    {
        var circuit = new Circuit(scoreCount + 1);
        for (var wire = 0; wire < scoreCount; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
        circuit.AssumeRange(circuit.Source(scoreCount), 1, 1);
        var winner = circuit.Argmax(Wires(Enumerable.Range(0, scoreCount)), circuit.Source(scoreCount));
        circuit.ValidateCircuit();
        return (circuit, winner);
    }

    // numeric-ui-core.test.ts 'first-active selection resolves ordered candidates through a balanced tree'.
    [Fact]
    public void First_active_selection_resolves_ordered_candidates_through_a_balanced_tree()
    {
        const int CandidateCount = 16;
        var circuit = new Circuit(CandidateCount + 1);
        for (var wire = 0; wire < circuit.SourceSize; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
        var selected = circuit.FirstActive(circuit.Source(0), Wires(Enumerable.Range(1, CandidateCount)));
        circuit.ValidateCircuit();
        (int Active, int[] Hits, int Winner)[] cases =
        [
            (1, [0], 0),
            (1, [2, 5], 2),
            (1, [1, 3, 14], 1),
            (1, [15], 15),
            (0, [.. Enumerable.Range(0, CandidateCount)], -1),
        ];
        foreach (var (active, hits, winner) in cases)
        {
            var source = new float[circuit.SourceSize];
            source[0] = active;
            foreach (var hit in hits) source[hit + 1] = 1;
            var values = circuit.EvaluateReference(source);
            Assert.Equal(OneHot(CandidateCount, winner), ValuesAt(values, selected));
        }
        Assert.True(TensorChecks.ProgramCounts(circuit.CompileTensorProgram(selected)).Layers <= 5 * (int)Math.Ceiling(Math.Log2(CandidateCount)) + 1);
    }

    // numeric-ui-core.test.ts 'argmax resolves continuous scores through a balanced tree'.
    [Fact]
    public void Argmax_resolves_continuous_scores_through_a_balanced_tree()
    {
        const int CandidateCount = 7;
        var (circuit, winner) = BuildArgmax(CandidateCount);
        (double[] Scores, int Winner)[] cases =
        [
            ([0.1, 0.7, 0.2, 0.4, 0.3, 0.6, 0.5], 1),
            ([0.8, 0.2, 0.8, 0.4, 0.8, 0.6, 0.5], 0),
            ([0, 0, 0, 0, 0, 0, 0], 0),
        ];
        foreach (var (scores, winnerIndex) in cases)
        {
            var values = circuit.EvaluateReference([.. scores.Select(score => (float)score), 1]);
            Assert.Equal(OneHot(CandidateCount, winnerIndex), ValuesAt(values, winner.Selected));
            Assert.True(Math.Abs(values[winner.Score.Position] - Js.F(scores[winnerIndex])) < 1e-6);
        }
        var layers = TensorChecks.ProgramCounts(circuit.CompileTensorProgram([.. winner.Selected, winner.Score])).Layers;
        Assert.True(layers <= 6 * (int)Math.Ceiling(Math.Log2(CandidateCount)));
    }

    // numeric-ui-core.test.ts 'argmax breaks exact ties toward the lower-indexed candidate'.
    [Fact]
    public void Argmax_breaks_exact_ties_toward_the_lower_indexed_candidate()
    {
        // patterns.md, "findIndex when passing items can be anywhere": equal scores resolve to the
        // left (lower) candidate, like a loop that replaces the best only on a strictly larger score.
        // The 0.5 ± 2^-24 comparison constants give this. A plain 0.5 comparison sends some ties, for
        // example 0.3 against 0.3, to the right.
        var (circuit, winner) = BuildArgmax(4);
        foreach (var tie in new[] { 0, 0.25f, 0.3f, 0.5f, 0.7f, 1 })
        {
            var values = circuit.EvaluateReference([tie, tie, tie, tie, 1]);
            Assert.Equal([1.0, 0, 0, 0], ValuesAt(values, winner.Selected));
        }
    }

    // Sharpwired addition: the reason for the constants. With 0.5 and 0.5, f(0.5 + 0.5 × 0.3 - 0.5 × 0.3)
    // rounds below 0.5 for some ties. The constants of Argmax keep each tie at or above 0.5.
    [Fact]
    public void The_argmax_comparison_constants_keep_every_FP32_tie_at_or_above_0_5()
    {
        const double ComparisonBias = 0.5000000596046448;
        const double ComparisonScale = 0.4999999403953552;
        Js.Is(Js.F(ComparisonBias), ComparisonBias);
        Js.Is(Js.F(ComparisonScale), ComparisonScale);
        Js.Is(1, ComparisonBias + ComparisonScale);
        // The comparison of the right score 1 against the left score 0 stays within 0..1.
        Assert.True(Js.F(Js.F(ComparisonBias + Js.F(ComparisonScale * 0)) + Js.F(-ComparisonScale * 1)) >= 0);
        Assert.True(Js.F(Js.F(ComparisonBias + Js.F(ComparisonScale * 1)) + Js.F(-ComparisonScale * 0)) <= 1);
        var random = Js.CreateRandom(3);
        for (var sample = 0; sample < 10_000; sample++)
        {
            var tie = Js.F(random());
            var comparison = Js.F(Js.F(ComparisonBias + Js.F(ComparisonScale * tie)) + Js.F(-ComparisonScale * tie));
            Assert.True(comparison >= 0.5, $"tie {tie} gives {comparison}");
        }
    }

    // random-hardened.test.ts 'boolean boundary battery' / 'boolean rounds adjacent-ulp scores identically
    // in both evaluators' (reference part).
    [Fact]
    public void Boolean_rounds_adjacent_ulp_scores_to_the_documented_bit()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var decision = circuit.Boolean(circuit.Source(0));
        // One ulp below 0.5, exactly 0.5, and one ulp above.
        foreach (var (score, expected) in new[] { (0.499_999_97f, 0.0), (0.5f, 1), (0.500_000_06f, 1) })
        {
            Js.Is(expected, circuit.EvaluateReference([score])[decision.Position]);
        }
    }

    // random-hardened.test.ts 'argmax sub-margin and exact ties' / 'a right score larger by less than
    // 2**-24 still keeps the left candidate'.
    [Fact]
    public void A_right_score_larger_by_less_than_2_pow_minus_24_still_keeps_the_left_candidate()
    {
        var (circuit, winner) = BuildArgmax(2);
        var subMargin = Math.Pow(2, -25);
        var checkedPairs = 0;
        foreach (var @base in new[] { 0.25, 0.5, 0.75, 1.0 / 3, 0.7 })
        {
            var left = Js.F(@base);
            var right = Js.F(@base + subMargin);
            // The right value must be greater after fround, else the case checks nothing.
            if (!(right > left)) continue;
            var values = circuit.EvaluateReference([(float)left, (float)right, 1]);
            Assert.Equal([1.0, 0], ValuesAt(values, winner.Selected));
            checkedPairs++;
        }
        // Sharpwired addition: 0.5 + 2^-25 and 0.75 + 2^-25 round to the left score, the other 3 bases do not.
        Assert.Equal(3, checkedPairs);
    }

    // random-hardened.test.ts 'argmax sub-margin and exact ties' / 'exact ties choose the left
    // candidate at every tree position for sizes 2..9'.
    [Fact]
    public void Exact_ties_choose_the_left_candidate_at_every_tree_position_for_sizes_2_to_9()
    {
        for (var scoreCount = 2; scoreCount <= 9; scoreCount++)
        {
            var (circuit, winner) = BuildArgmax(scoreCount);
            // For each adjacent pair (positions i and i+1 with the joint maximum), an exact tie
            // resolves to the lower (left) index.
            foreach (var tieValue in new[] { 0.25, 0.5, 0.7 })
            {
                for (var position = 0; position + 1 < scoreCount; position++)
                {
                    var scores = new float[scoreCount + 1];
                    scores[position] = (float)tieValue;
                    scores[position + 1] = (float)tieValue;
                    scores[scoreCount] = 1;
                    var values = circuit.EvaluateReference(scores);
                    // The one-hot puts its single 1 at the left tied index, in each subtree.
                    Assert.Equal(OneHot(scoreCount, position), ValuesAt(values, winner.Selected));
                    Js.Is(Js.F(tieValue), values[winner.Score.Position]);
                }
            }
        }
    }

    // random-hardened.test.ts 'argmax sub-margin and exact ties' / 'a global exact tie across all
    // positions selects index zero'.
    [Fact]
    public void A_global_exact_tie_across_all_positions_selects_index_zero()
    {
        for (var scoreCount = 2; scoreCount <= 9; scoreCount++)
        {
            var (circuit, winner) = BuildArgmax(scoreCount);
            var values = circuit.EvaluateReference([.. Enumerable.Repeat(0.5f, scoreCount), 1]);
            Assert.Equal(OneHot(scoreCount, 0), ValuesAt(values, winner.Selected));
        }
    }

    // execution-model.md#macro-contracts, emitArgmax: the band is not transitive, so the tree picks
    // index 1 of [0, 0.5, 0.5 + 2^-24, 0.5 + 2^-23], where a loop picks index 3. Argmax always
    // declares its selection as a one-hot group.
    [Fact]
    public void Argmax_keeps_the_left_candidate_within_its_band_and_always_declares_its_group()
    {
        var (circuit, winner) = BuildArgmax(4);
        var group = Assert.Single(circuit.OneHotGroups);
        Assert.Equal(winner.Selected, group.Members);
        Assert.False(group.AllowAllZero);
        float[] source = [0, 0.5f, (float)(0.5 + Math.Pow(2, -24)), (float)(0.5 + Math.Pow(2, -23)), 1];
        var values = circuit.EvaluateReference(source);
        Assert.Equal(OneHot(4, 1), ValuesAt(values, winner.Selected));
        Js.Is(0.5, values[winner.Score.Position]);
        circuit.ValidateOneHotGroups(values);
        // A single score selects the unit wire itself.
        var single = new Circuit(2);
        single.AssumeRange(single.Source(0), 0, 1);
        single.AssumeRange(single.Source(1), 1, 1);
        var only = single.Argmax([single.Source(0)], single.Source(1));
        Assert.Equal([single.Source(1)], only.Selected);
        Assert.Equal(single.Source(0), only.Score);
        Assert.Empty(single.Operations);
    }

    // Sharpwired addition: the Handwired refusals of emitArgmax.
    [Fact]
    public void Argmax_refuses_no_scores_a_unit_that_is_not_one_and_scores_outside_0_to_1()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeRange(circuit.Source(2), 1, 1);
        Js.Throws(() => circuit.Argmax([], circuit.Source(2)), "argmax needs at least one score");
        Js.Throws(() => circuit.Argmax([circuit.Source(0)], circuit.Source(0)), "argmax unit must equal one");
        Js.Throws(() => circuit.Argmax([circuit.Source(0), circuit.Source(1)], circuit.Source(2)), "argmax scores must be within zero and one");
    }

    // execution-model.md#macro-contracts, emitFirstActive: fractional candidates split the route, for
    // example candidates [0.5, 0] send 0.25 to each output. A cell that is only declared within 0..1
    // isn't a proven bit, so the selection has no group.
    [Fact]
    public void First_active_splits_the_route_over_fractional_candidates_and_declares_no_group_over_unproven_bits()
    {
        var circuit = new Circuit(3);
        for (var wire = 0; wire < 3; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
        var selected = circuit.FirstActive(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]);
        Assert.Empty(circuit.OneHotGroups);
        Assert.Equal([0.25, 0.25], ValuesAt(circuit.EvaluateReference([1, 0.5f, 0]), selected));
        Assert.Equal([0.0, 1], ValuesAt(circuit.EvaluateReference([1, 0, 1]), selected));
    }

    // Sharpwired addition: the declaration rule of emitFirstActive. Proven bits give a group that allows
    // all zeros.
    [Fact]
    public void First_active_declares_a_group_that_allows_all_zeros_only_over_proven_bits_with_distinct_routes()
    {
        var circuit = new Circuit(4);
        for (var wire = 0; wire < 4; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
        circuit.AssumeOneHot([circuit.Source(1), circuit.Source(2)]);
        var active = circuit.Boolean(circuit.Source(0));
        var selected = circuit.FirstActive(active, [circuit.Source(1), circuit.Source(2)]);
        var group = circuit.OneHotGroups[^1];
        Assert.Equal(selected, group.Members);
        Assert.True(group.AllowAllZero);
        foreach (var (gate, first, second) in new[] { (0f, 1f, 0f), (1f, 1f, 0f), (1f, 0f, 1f) })
        {
            circuit.ValidateOneHotGroups(circuit.EvaluateReference([gate, first, second, 0]));
        }
        // A range pinned to 0 or to 1 is a proven bit.
        var pinned = new Circuit(2);
        pinned.AssumeRange(pinned.Source(0), 1, 1);
        pinned.AssumeRange(pinned.Source(1), 0, 0);
        pinned.FirstActive(pinned.Source(0), [pinned.Source(1), pinned.Source(0)]);
        Assert.Single(pinned.OneHotGroups);
        // No candidates gives no selection and needs no gate range.
        Assert.Empty(new Circuit(1).FirstActive(Wire.FromPosition(0), []));
    }

    // Sharpwired addition: the Handwired refusals of emitFirstActive.
    [Fact]
    public void First_active_refuses_a_gate_or_a_candidate_outside_0_to_1()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        Js.Throws(() => circuit.FirstActive(circuit.Source(1), [circuit.Source(0)]), "winner selection must be gated within zero and one");
        Js.Throws(() => circuit.FirstActive(circuit.Source(0), [circuit.Source(1)]), "winner candidates must be within zero and one");
    }

    // random-hardened.test.ts 'hardened random verification' / 'non-dyadic macro circuits preserve every
    // live FP32 wire' (reference part: validateCircuit, and the reference evaluator on each source).
    [Fact]
    public void Non_dyadic_macro_circuits_build_valid_circuits_that_the_reference_evaluator_runs()
    {
        double[] nonDyadic =
        [
            1.0 / 3, 0.1, 0.7, 1e-3, 12_345.6789, -1.0 / 3, -0.1, -0.7, -1e-3, -12_345.6789,
            0.123_456_789, 2.0 / 7, -2.0 / 7, 3.141_592_653_589_793, -0.987_654_321,
        ];
        var random = Js.CreateRandom(unchecked((int)0x9e37_79b9));
        int RandomInteger(int limit) => (int)Math.Floor(random() * limit);
        double RandomCoefficient() => nonDyadic[RandomInteger(nonDyadic.Length)];
        var macroApplications = 0;
        var evaluations = 0;
        for (var circuitIndex = 0; circuitIndex < 320; circuitIndex++)
        {
            // Unit sources feed the boolean, argmax and first-active preconditions. One more source is
            // pinned to exactly 1 as the unit wire of argmax.
            var unitSourceCount = 3 + RandomInteger(4);
            var freeSourceCount = 2 + RandomInteger(3);
            var unitWire = unitSourceCount + freeSourceCount;
            var sourceSize = unitWire + 1;
            var circuit = new Circuit(sourceSize);
            for (var wire = 0; wire < unitSourceCount; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 0, 1);
            for (var wire = unitSourceCount; wire < unitWire; wire++) circuit.AssumeRange(Wire.FromPosition(wire), -2, 2);
            circuit.AssumeRange(Wire.FromPosition(unitWire), 1, 1);
            var unitWires = Wires(Enumerable.Range(0, unitSourceCount)).ToList();
            var allWires = Wires(Enumerable.Range(0, sourceSize)).ToList();
            Wire PickUnit() => unitWires[RandomInteger(unitWires.Count)];
            Wire PickAny() => allWires[RandomInteger(allWires.Count)];
            Term[] RandomTerms()
            {
                var terms = new Term[1 + RandomInteger(4)];
                for (var index = 0; index < terms.Length; index++)
                {
                    var wire = PickAny();
                    terms[index] = new Term(wire, RandomCoefficient());
                }
                return terms;
            }
            var operationCount = 6 + RandomInteger(14);
            for (var operation = 0; operation < operationCount; operation++)
            {
                var choice = RandomInteger(11);
                Wire produced;
                if (choice == 0)
                {
                    var terms = RandomTerms();
                    produced = circuit.Affine(terms, RandomCoefficient());
                }
                else if (choice == 1)
                {
                    var leftTerms = RandomTerms();
                    var leftBias = RandomCoefficient();
                    var gateTerms = RandomTerms();
                    produced = circuit.Reglu(leftTerms, leftBias, gateTerms, RandomCoefficient());
                }
                else if (choice is >= 2 and <= 5)
                {
                    var (first, second) = choice == 5 ? (PickUnit(), PickAny()) : (PickAny(), PickAny());
                    produced = choice switch
                    {
                        2 => circuit.Multiply(first, second),
                        3 => circuit.Maximum(first, second),
                        4 => circuit.Minimum(first, second),
                        _ => circuit.Select(first, second, PickAny()),
                    };
                    macroApplications++;
                }
                else if (choice == 6)
                {
                    produced = circuit.Boolean(PickUnit());
                    circuit.AssumeRange(produced, 0, 1);
                    unitWires.Add(produced);
                }
                else if (choice == 7)
                {
                    var scores = Enumerable.Range(0, 2 + RandomInteger(6)).Select(_ => PickUnit()).ToArray();
                    var winner = circuit.Argmax(scores, Wire.FromPosition(unitWire));
                    macroApplications++;
                    foreach (var selector in winner.Selected)
                    {
                        circuit.AssumeRange(selector, 0, 1);
                        unitWires.Add(selector);
                    }
                    circuit.AssumeRange(winner.Score, 0, 1);
                    unitWires.Add(winner.Score);
                    produced = winner.Score;
                }
                else if (choice == 8)
                {
                    var candidates = Enumerable.Range(0, 2 + RandomInteger(6)).Select(_ => PickUnit()).ToArray();
                    var selected = circuit.FirstActive(PickUnit(), candidates);
                    macroApplications++;
                    foreach (var selector in selected)
                    {
                        circuit.AssumeRange(selector, 0, 1);
                        unitWires.Add(selector);
                    }
                    produced = selected[^1];
                }
                else if (choice == 9)
                {
                    // A spring over signed free sources with non-dyadic constants.
                    Wire Constant(double value, double maximum)
                    {
                        var wire = circuit.Affine([], value);
                        circuit.AssumeRange(wire, 0, maximum);
                        return wire;
                    }
                    var stiffness = Constant(0.07, 1);
                    var damping = Constant(0.3, 1);
                    var timestep = Constant(0.1, 1);
                    var settleDistance = Constant(1e-3, 1);
                    var settleSpeed = Constant(1e-3, 1);
                    var scale = Constant(12_345.6789, 13_000);
                    var positionSource = PickAny();
                    var destination = PickAny();
                    var velocitySource = PickAny();
                    var spring = circuit.Spring(
                        positionSource,
                        destination,
                        velocitySource,
                        stiffness,
                        damping,
                        timestep,
                        settleDistance,
                        settleSpeed,
                        scale,
                        scale,
                        Wire.FromPosition(unitWire));
                    macroApplications++;
                    circuit.AssumeRange(spring.Active, 0, 1);
                    unitWires.Add(spring.Active);
                    produced = spring.Position;
                }
                else
                {
                    // A plain ReGLU as a gated product.
                    var leftTerms = RandomTerms();
                    var leftBias = RandomCoefficient();
                    var gateWire = PickAny();
                    var gateCoefficient = RandomCoefficient();
                    produced = circuit.Reglu(leftTerms, leftBias, [(gateWire, gateCoefficient)], RandomCoefficient());
                }
                allWires.Add(produced);
            }
            circuit.ValidateCircuit();
            // Handwired picks the roots of the tensor program here. The reference part keeps the draws.
            var operandWires = allWires.Where(wire => wire.Position >= sourceSize).ToList();
            var rootCount = 1 + RandomInteger(5);
            for (var root = 0; root < rootCount; root++) RandomInteger(operandWires.Count);
            for (var inputIndex = 0; inputIndex < 5; inputIndex++)
            {
                var source = new float[sourceSize];
                for (var wire = 0; wire < sourceSize; wire++)
                {
                    source[wire] = wire == unitWire
                        ? 1
                        : wire < unitSourceCount
                            ? (float)(random() * 0.999_998 + 0.000_001)
                            : (float)(random() * 4 - 2);
                }
                Assert.Equal(circuit.Width, circuit.EvaluateReference(source).Length);
                evaluations++;
            }
        }
        // The suite exercised the macros, not only plain operations.
        Assert.True(macroApplications > 0);
        Assert.Equal(320 * 5, evaluations);
    }
}
