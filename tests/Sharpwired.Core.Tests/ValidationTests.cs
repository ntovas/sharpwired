// Handwired source: src/runtime/numeric-ui-core.ts (validateCircuit, validateTerms, assertWire,
// validateOneHotGroups, validateGrids, validateNoNaN, describeFirstNaN, assertNonNegativeFinite,
// assertUnitInterval, assertFiniteValues, validateWireView, invariant).
// Handwired tests call validateCircuit on built circuits and on circuits whose operations
// they change by hand (tests/runtime/index-read.test.ts). These tests do the same. The tests of
// the value validators and the assert helpers are Sharpwired additions: they isolate the
// conditions and the messages that the Handwired tests use together.

using System.Collections.Immutable;

namespace Sharpwired.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void Validate_circuit_returns_the_wire_count_of_a_built_circuit()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var sum = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 2)], 1);
        var product = circuit.Multiply(sum, circuit.Source(2));
        circuit.Select(circuit.Source(0), product, circuit.Source(1));
        circuit.Boolean(circuit.Source(0));
        Assert.Equal(circuit.Width, circuit.ValidateCircuit());
    }

    [Fact]
    public void Validate_circuit_refuses_non_finite_biases_and_weights()
    {
        var leftBias = new Circuit(1);
        leftBias.Affine([(leftBias.Source(0), 1)], double.NaN);
        Js.Throws(() => leftBias.ValidateCircuit(), "left bias must be finite");

        var gateBias = new Circuit(2);
        gateBias.Reglu([(gateBias.Source(0), 1)], 0, [(gateBias.Source(1), 1)], double.PositiveInfinity);
        Js.Throws(() => gateBias.ValidateCircuit(), "gate bias must be finite");

        var weight = new Circuit(1);
        weight.Affine([(weight.Source(0), double.NaN)]);
        Js.Throws(() => weight.ValidateCircuit(), "weight must be finite");
    }

    [Fact]
    public void Validate_circuit_refuses_a_term_that_reads_the_operation_itself_or_a_later_wire()
    {
        var circuit = new Circuit(1);
        circuit.Affine([(circuit.Source(0), 2)]);
        circuit.OperationList[0] = circuit.OperationList[0] with { LeftTerms = [new Term(Wire.FromPosition(1), 1)] };
        Js.Throws(() => circuit.ValidateCircuit(), "wire is outside the compiled circuit");
    }

    [Fact]
    public void The_builder_refuses_a_term_wire_outside_the_circuit()
    {
        var circuit = new Circuit(1);
        Js.Throws(() => circuit.Affine([(Wire.FromPosition(1), 2)]), "wire is outside the compiled circuit");
        Js.Throws(() => circuit.RoundAffine([(Wire.FromPosition(1), 2)]), "wire is outside the compiled circuit");
        Js.Throws(() => circuit.Multiply(circuit.Source(0), Wire.FromPosition(5)), "wire is outside the compiled circuit");
        Js.Throws(() => circuit.Boolean(Wire.FromPosition(1)), "wire is outside the compiled circuit");
        Js.Throws(() => circuit.Append(OperationKind.Index, [], 0, [], 0), "index");
    }

    [Fact]
    public void Validate_circuit_checks_the_index_read_contract()
    {
        static Circuit Build(Func<Circuit, Operation> operation)
        {
            var circuit = new Circuit(3);
            circuit.OperationList.Add(operation(circuit));
            return circuit;
        }

        static IndexReadInstruction Read(Circuit circuit, ImmutableArray<Wire> candidates, double maximum = 1) =>
            new(new CandidateTable(0, candidates, 0), OutOfRange.Clamp, OutOfRange.Zero, new NumericRange(0, maximum));

        static Operation Index(Circuit circuit) => new()
        {
            Kind = OperationKind.Index,
            LeftTerms = [],
            LeftBias = 0,
            GateTerms = [new Term(circuit.Source(0), 1)],
            GateBias = 0,
            Depth = 1,
            IndexRead = Read(circuit, [circuit.Source(1), circuit.Source(2)]),
        };

        Assert.Equal(4, Build(Index).ValidateCircuit());
        Js.Throws(() => Build(circuit => Index(circuit) with { IndexRead = null }).ValidateCircuit(), "only index reads carry a candidate table");
        Js.Throws(() => Build(circuit => Index(circuit) with { Kind = OperationKind.Affine }).ValidateCircuit(), "only index reads carry a candidate table");
        Js.Throws(
            () => Build(circuit => Index(circuit) with { LeftTerms = [new Term(circuit.Source(1), 1)] }).ValidateCircuit(),
            "an index read keeps its index form on the gate side");
        Js.Throws(() => Build(circuit => Index(circuit) with { LeftBias = 1 }).ValidateCircuit(), "an index read keeps its index form on the gate side");
        Js.Throws(() => Build(circuit => Index(circuit) with { GateTerms = [] }).ValidateCircuit(), "an index read needs a runtime index");
        Js.Throws(
            () => Build(circuit => Index(circuit) with { IndexRead = Read(circuit, [circuit.Source(1)], Math.Pow(2, 24)) }).ValidateCircuit(),
            "an index read needs an accepted index range strictly inside ±2^24");
        Js.Throws(
            () => Build(circuit => Index(circuit) with { IndexRead = Read(circuit, []) }).ValidateCircuit(),
            "an index read needs at least one candidate");
        Js.Throws(
            () => Build(circuit => Index(circuit) with { IndexRead = Read(circuit, [circuit.Source(1), Wire.FromPosition(3)]) }).ValidateCircuit(),
            "index read candidates must be earlier wires");
        Js.Throws(
            () => Build(circuit => Index(circuit) with { IndexRead = Read(circuit, [Wire.FromPosition(4)]) }).ValidateCircuit(),
            "wire is outside the compiled circuit");
    }
    [Fact]
    public void Validate_one_hot_groups_checks_exact_members_and_the_mass_of_each_group()
    {
        var circuit = new Circuit(4);
        circuit.AssumeOneHot([circuit.Source(0), circuit.Source(1)]);
        circuit.AssumeOneHot([circuit.Source(2), circuit.Source(3)], allowAllZero: true);
        circuit.ValidateOneHotGroups([1, 0, 0, 0]);
        circuit.ValidateOneHotGroups([0, 1, 0, 1]);
        // -0 is exactly 0, as in JavaScript value === 0.
        circuit.ValidateOneHotGroups([-0f, 1, -0f, -0f]);
        Js.Throws(() => circuit.ValidateOneHotGroups([0, 0, 0, 0]), "one-hot group must have exactly one active member, got mass 0");
        Js.Throws(() => circuit.ValidateOneHotGroups([1, 1, 0, 0]), "one-hot group must have exactly one active member, got mass 2");
        Js.Throws(() => circuit.ValidateOneHotGroups([1, 0, 1, 1]), "got mass 2");
        Js.Throws(() => circuit.ValidateOneHotGroups([0.5f, 0.5f, 0, 0]), "one-hot member wire 0 must be exactly 0 or 1, got 0.5");
        Js.Throws(() => circuit.ValidateOneHotGroups([1, 0, float.NaN, 0]), "one-hot member wire 2 must be exactly 0 or 1, got NaN");
        Js.Throws(() => circuit.ValidateOneHotGroups([1, 0, 0]), "one-hot validation needs the full wire values");
    }

    [Fact]
    public void Validate_grids_checks_each_wire_with_a_grid_for_a_finite_multiple()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), -100, 100);
        circuit.AssumeGrid(circuit.Source(0), 1.0 / 64);
        circuit.AssumeGrid(circuit.Source(1), 4);
        var sum = circuit.Affine([(circuit.Source(0), 2)], 1);
        Assert.Equal(1.0 / 32, circuit.Grids[sum.Position]);
        circuit.ValidateGrids(circuit.EvaluateReference([1.0f / 64, -8, 0.3f]));
        // Source cell 2 has no grid, so NaN passes. Wire 3 is the sum 2 × -0 + 1.
        circuit.ValidateGrids([-0f, 0, float.NaN, 1]);
        Js.Throws(() => circuit.ValidateGrids(circuit.EvaluateReference([0.01f, 0, 0])), $"wire 0 must be a multiple of 0.015625, got {Fp32.Format(0.01f)}");
        Js.Throws(() => circuit.ValidateGrids(circuit.EvaluateReference([0, 6, 0])), "wire 1 must be a multiple of 4, got 6");
        Js.Throws(() => circuit.ValidateGrids(circuit.EvaluateReference([0, float.PositiveInfinity, 0])), "wire 1 must be a multiple of 4, got Infinity");
        Js.Throws(() => circuit.ValidateGrids([0, 0, 0]), "grid validation needs the full wire values");
    }

    [Fact]
    public void Describe_first_NaN_names_source_cells_index_reads_and_sums_and_returns_null_without_a_NaN()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        var sum = circuit.RoundAffine([(circuit.Source(1), 1), (circuit.Source(2), 1)]);
        var read = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]);
        Assert.Null(circuit.DescribeFirstNaN(circuit.EvaluateReference([0, 1, 2])));
        circuit.ValidateNoNaN(circuit.EvaluateReference([0, 1, 2]));
        var values = circuit.EvaluateReference([1, float.PositiveInfinity, float.NegativeInfinity]);
        Assert.Equal(
            $"wire {sum} is the first NaN: a sum of wire 1 = Infinity, wire 2 = -Infinity",
            circuit.DescribeFirstNaN(values));
        // The read copies -Infinity, so only the sum is NaN.
        Js.Is(double.NegativeInfinity, values[read.Position]);
        Assert.Null(circuit.DescribeFirstNaN(values, [read]));
        float[] nanSource = [0, float.NaN, 1, float.NaN, float.NaN];
        Assert.Equal("source cell 1 is NaN", circuit.DescribeFirstNaN(nanSource));
        Js.Throws(() => circuit.ValidateNoNaN(nanSource), "source cell 1 is NaN");
        // A NaN index: the values come from the caller, so a NaN source cell 0 is possible.
        float[] nanIndex = [float.NaN, 1, 2, 3, float.NaN];
        Assert.Equal("source cell 0 is NaN", circuit.DescribeFirstNaN(nanIndex));
        Assert.Equal("source cell 0 is NaN", circuit.DescribeFirstNaN(nanIndex, [read]));
        Assert.Null(circuit.DescribeFirstNaN(nanIndex, [sum]));
        Js.Throws(() => circuit.DescribeFirstNaN([0, 1, 2]), "NaN validation needs the full wire values");
        Js.Throws(() => circuit.DescribeFirstNaN(nanIndex, [Wire.FromPosition(5)]), "wire is outside the compiled circuit");

        // A form that reads no NaN wire can still be NaN, for example Infinity + -Infinity.
        var operations = new Circuit(2);
        var nanRead = operations.IndexReadAffine(
            [(operations.Source(0), 1), (operations.Source(1), 1)],
            0,
            [operations.Source(0)],
            OutOfRange.Clamp,
            OutOfRange.Clamp,
            new NumericRange(0, 1));
        var product = operations.Reglu([(operations.Source(0), 1)], 0, [(operations.Source(1), 1)], 0);
        float[] infinities = [float.PositiveInfinity, float.NegativeInfinity, float.NaN, 0];
        Assert.Equal(
            $"wire {nanRead} is the first NaN: an index read whose index form is NaN, from wire 0 = Infinity, wire 1 = -Infinity",
            operations.DescribeFirstNaN(infinities));
        float[] productValues = [float.PositiveInfinity, 0, float.PositiveInfinity, float.NaN];
        Assert.Equal(
            $"wire {product} is the first NaN: f(left × max(0, gate)) with left = Infinity and gate = 0",
            operations.DescribeFirstNaN(productValues));
    }

    [Fact]
    public void The_assert_helpers_refuse_values_outside_their_contract()
    {
        Assert.Equal(0, Validation.AssertNonNegativeFinite(0, "delay"));
        Assert.Equal(-1e-3, Validation.AssertNonNegativeFinite(-1e-3, "delay", tolerance: 1e-3));
        Js.Throws(() => Validation.AssertNonNegativeFinite(-1e-3, "delay"), "delay must be non-negative and finite, got -0.001");
        Js.Throws(() => Validation.AssertNonNegativeFinite(double.PositiveInfinity, "delay"), "delay must be non-negative and finite, got Infinity");
        Js.Throws(() => Validation.AssertNonNegativeFinite(double.NaN, "delay"), "got NaN");
        Js.Throws(() => Validation.AssertNonNegativeFinite(null, "delay"), "delay must be non-negative and finite, got undefined");

        Assert.Equal(1 + 1e-9, Validation.AssertUnitInterval(1 + 1e-9, "opacity"));
        Assert.Equal(-1e-9, Validation.AssertUnitInterval(-1e-9, "opacity"));
        Js.Throws(() => Validation.AssertUnitInterval(1 + 2e-9, "opacity"), "opacity must be within zero and one, got 1.000000002");
        Js.Throws(() => Validation.AssertUnitInterval(0.5, "opacity", tolerance: double.NaN), "got 0.5");
        Js.Throws(() => Validation.AssertUnitInterval(null, "opacity"), "opacity must be within zero and one, got undefined");

        Validation.AssertFiniteValues([1f, -0f, float.MaxValue], "state");
        Js.Throws(() => Validation.AssertFiniteValues([1f, float.NaN], "state"), "state contains a non-finite number");
        Validation.AssertFiniteValues([1.0, double.MaxValue], "matrix");
        Js.Throws(() => Validation.AssertFiniteValues([double.NegativeInfinity], "matrix"), "matrix contains a non-finite number");

        Validation.ValidateWireView([Wire.FromPosition(0), Wire.FromPosition(2)], 2, 3);
        Js.Throws(() => Validation.ValidateWireView([Wire.FromPosition(0)], 2, 3), "output wire view has the wrong length");
        Js.Throws(() => Validation.ValidateWireView([Wire.FromPosition(0), Wire.FromPosition(3)], 2, 3), "wire is outside the compiled circuit");

        Validation.Invariant(true, "unused");
        var exception = Js.Throws(() => Validation.Invariant(false, "the invariant message"), "the invariant message");
        Assert.IsType<InvalidOperationException>(exception);
    }
}
