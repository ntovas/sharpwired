// Handwired source: src/runtime/numeric-ui-core.ts (validateCircuit, validateTerms, assertWire).
// Handwired tests call validateCircuit on built circuits and on circuits whose operations
// they change by hand (tests/runtime/index-read.test.ts). These tests do the same.

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
}
