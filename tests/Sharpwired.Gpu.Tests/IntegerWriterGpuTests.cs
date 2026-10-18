// No Handwired source. The integer writer on each adapter, against the reference evaluator, for the two
// findings of the review of task 3 of the phase 5 plan: an f32 row that reads an integer value with no
// keep, which Metal fast math regroups, and a row that converts an f32 value that can be NaN to an
// integer. The test also runs the seeded circuits of the review and a program at the 2^24 bound of
// the affine rule. The comparison is sameNumericValue, which also requires NaN for NaN.

using Sharpwired.Core;
using Xunit.Abstractions;
using static Sharpwired.Core.Tests.RandomPrograms;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class IntegerWriterGpuTests(GpuFixture fixture, ITestOutputHelper output)
{
    // The 5 compile modes of TensorDifferentialTests.
    private static readonly Func<Circuit, IReadOnlyList<Wire>, TensorProgram>[] Modes =
    [
        (circuit, roots) => circuit.CompileTensorProgram(roots),
        (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false),
        (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true),
        (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true),
        (circuit, _) => circuit.CompileTensorProgram(),
    ];

    // Run the program with the integer writer on each device, and compare each root of each table.
    private void Check(Circuit circuit, TensorProgram program, float[] sources, int tableCount, string label)
    {
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Integer);
            try
            {
                GpuChecks.Compare(device, circuit, program, sources, tableCount, evaluator.Evaluate(sources, tableCount), sameNumericValue: true);
            }
            catch (Exception failure)
            {
                throw new Xunit.Sdk.XunitException($"{label}: {failure.Message}");
            }
        });
    }

    // Review finding 2. relu(1 - relu(s0 - s1)) × u is within 0..1 by range inference, and is NaN for
    // s0 = s1 = +Infinity. A boolean atom, a Compare bit and an index read of that value must give NaN,
    // as the reference does. A row that converts the f32 value to an integer gave 1, 1 and 7.
    [Fact]
    public void A_value_that_range_inference_puts_in_0_to_1_and_that_is_NaN_stays_NaN()
    {
        var circuit = new Circuit(5);
        var (s0, s1, u, c0, c1) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4));
        circuit.AssumeRange(u, 0, 1);
        foreach (var cell in new[] { c0, c1 })
        {
            circuit.AssumeRange(cell, -10, 10);
            circuit.AssumeGrid(cell, 1);
        }
        var difference = circuit.Affine([(s0, 1), (s1, -1)]);
        var ramp = circuit.Reglu([], 1, [(difference, 1)], 0);
        var p = circuit.Reglu([(u, 1)], 0, [(circuit.Affine([(ramp, -1)], 1), 1)], 0);
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[p.Position]);
        Wire[] reads = [circuit.Boolean(p), circuit.Compare(p, Relation.Greater, circuit.Constant(0.5)), circuit.IndexRead(p, [c0, c1])];
        var roots = reads.Select(read => circuit.Affine([(read, 2)], 1)).ToList();
        float[] sources =
        [
            float.PositiveInfinity, float.PositiveInfinity, 1, 3, 4,
            float.NegativeInfinity, float.NegativeInfinity, 0.5f, -3, 4,
            1, 0.25f, 0.75f, 3, -4,
            0, 0, 1, 7, 2,
        ];
        Assert.True(float.IsNaN(circuit.EvaluateReference(sources.AsSpan(0, 5))[roots[0].Position]));
        var program = circuit.CompileTensorProgram(roots);
        Check(circuit, program, sources, 4, "the roots of the boolean atom, the Compare bit and the index read");
        Check(circuit, circuit.CompileTensorProgram(), sources, 4, "every wire as a root");
    }

    // Review finding 1. An f32 row reads a constant integer row, in the form of circuit 25 of seed 4242:
    // k(... + k(k(0.0f) * f32(b))) - f32(b), and a product of a constant 0 and Infinity. On the M5 Pro,
    // this program gave the reference values also without keep on f32(b). The seeded circuits of the
    // next test show the failure without keep.
    [Fact]
    public void An_f32_row_that_reads_a_constant_integer_row_gives_the_reference_bits()
    {
        var circuit = new Circuit(2);
        var (x, unbounded) = (circuit.Source(0), circuit.Source(1));
        circuit.AssumeRange(x, -100, 100);
        var one = circuit.RoundAffine([], 1);
        var zero = circuit.RoundAffine([], 0);
        var row = circuit.RoundAffine([(x, 0.3), (one, 0), (one, -1)]);
        var product = circuit.Multiply(zero, unbounded);
        var random = Js.CreateRandom(0xc0de);
        var sources = new List<float> { 1, float.PositiveInfinity, -1, float.NegativeInfinity, 0, 3 };
        for (var table = 0; table < 256; table++)
        {
            sources.Add((float)((random() - 0.5) * 200));
            sources.Add((float)((random() - 0.5) * 1e6));
        }
        var program = circuit.CompileTensorProgram([row, product]);
        Check(circuit, program, [.. sources], sources.Count / 2, "the f32 row and the product");
    }

    // The review found mismatches in these circuits of RandomPrograms with the integer writer: circuits
    // 25, 86, 199 and 209 of seed 4242 (an f32 row that reads an integer value), and circuit 141 of seed
    // 77713 (a sign cascade bit of a NaN difference). Each circuit runs in the 5 compile modes over 200
    // tables, with the generator sequence of 300 circuits.
    [Theory]
    [InlineData(4242, new[] { 25, 86, 199, 209 })]
    [InlineData(77713, new[] { 141 })]
    public void The_circuits_of_the_review_give_the_reference_value(int seed, int[] circuitIndices)
    {
        const int SourcesPerCircuit = 200;
        var random = Js.CreateRandom(seed);
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];
        var maximum = circuitIndices.Max();
        for (var circuitIndex = 0; circuitIndex <= maximum; circuitIndex++)
        {
            var (circuit, cells, groups, wires) = BuildCircuit(random, builderCalls);
            var operationWires = wires.Where(wire => wire.Position >= circuit.SourceSize).Distinct().ToList();
            var rootCount = 1 + RandomInteger(random, 5);
            var roots = Enumerable.Range(0, rootCount).Select(_ => operationWires[RandomInteger(random, operationWires.Count)]).ToList();
            var sources = Enumerable.Range(0, SourcesPerCircuit).SelectMany(_ => CreateSource(random, cells, groups)).ToArray();
            if (!circuitIndices.Contains(circuitIndex)) continue;
            for (var mode = 0; mode < Modes.Length; mode++)
            {
                Check(circuit, Modes[mode](circuit, roots), sources, SourcesPerCircuit, $"seed {seed}, circuit {circuitIndex}, mode {mode}");
            }
        }
    }

    // The affine rule at its bound: sums whose products and partial sums reach 2^24 are i32 rows, and
    // sums one step past the bound are f32 rows. The seeded tables include each end of each range.
    [Fact]
    public void Sums_at_the_2_to_the_24_bound_give_the_reference_value()
    {
        const double Half = 8_388_608;
        var circuit = new Circuit(3);
        var cells = Enumerable.Range(0, 3).Select(circuit.Source).ToArray();
        circuit.AssumeRange(cells[0], -Half, Half);
        circuit.AssumeRange(cells[1], -Half, Half);
        circuit.AssumeRange(cells[2], -5_592_405, 5_592_405);
        foreach (var cell in cells) circuit.AssumeGrid(cell, 1);
        Wire[] atBound =
        [
            circuit.RoundAffine([(cells[0], 1), (cells[1], 1)]),
            circuit.RoundAffine([(cells[0], 1), (cells[1], -1)]),
            circuit.RoundAffine([(cells[0], 2)]),
            circuit.RoundAffine([(cells[2], 3)], 1),
        ];
        Wire[] pastBound =
        [
            circuit.RoundAffine([(cells[0], 1), (cells[1], 1)], 1),
            circuit.RoundAffine([(cells[0], 2), (cells[1], 1)], -1),
            circuit.RoundAffine([(cells[2], 3)], 2),
        ];
        var program = circuit.CompileTensorProgram([.. atBound, .. pastBound]);
        var proofs = IntegerProof.Prove(circuit, program);
        var rootPositions = ShaderRows.RootPositions(program);
        foreach (var position in rootPositions.Take(atBound.Length)) Assert.Equal(WholeKind.Whole, proofs[position].Kind);
        foreach (var position in rootPositions.Skip(atBound.Length)) Assert.Equal(WholeKind.Float, proofs[position].Kind);

        var random = Js.CreateRandom(0x2424);
        double[][] ends = [[-Half, Half, -1, 0, 1, Half - 1, 1 - Half], [-Half, Half, -1, 0, 1, Half - 1, 1 - Half], [-5_592_405, 5_592_405, -1, 0, 1]];
        var sources = new List<float>();
        for (var table = 0; table < 1024; table++)
        {
            for (var cell = 0; cell < 3; cell++)
            {
                var limit = circuit.Ranges[cell].Maximum;
                sources.Add((float)(random() < 0.5 ? ends[cell][RandomInteger(random, ends[cell].Length)] : Math.Floor((random() * 2 - 1) * limit)));
            }
        }
        float[] tables = [.. sources];
        var rows = ShaderRows.FlattenProgram(program);
        var plan = IntegerPlan.Create(IntegerProof.Analyze(circuit, program, rows), rows, program.GatherSlotCount, rootPositions);
        for (var table = 0; table < 1024; table++)
        {
            var source = tables.AsSpan(table * 3, 3);
            var reference = circuit.EvaluateReference(source);
            var values = RowInterpreter.RunInteger(program, rows, plan, source);
            for (var root = 0; root < rootPositions.Length; root++)
            {
                Assert.True(Js.SameNumericValue(reference[program.Roots[root]], values[rootPositions[root]]), $"table {table}, root {root}");
            }
        }
        Check(circuit, program, tables, 1024, "the sums at the bound");
    }
}
