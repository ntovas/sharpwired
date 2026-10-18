// No Handwired source. The tests of the batch evaluators: GpuEvaluator on each adapter, with both
// writers, and CpuBatchEvaluator with no adapter. Each test compares the outputs with the reference
// evaluator: Object.is for the f32 writer, sameNumericValue for the integer writer, which has no -0.
// GpuChecks holds the comparison and the subnormal rule of decision 4 of the phase 5 plan.

using System.Diagnostics;
using Sharpwired.Core;
using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

/// <summary>The comparison of batch outputs with the reference evaluator.</summary>
internal static class GpuChecks
{
    /// <summary>
    /// Compare each root of each table with the reference evaluator. The method does not compare a table
    /// whose f32 evaluation holds a nonzero subnormal value on a device that flushes subnormal values.
    /// This is decision 4 of the phase 5 plan. Return the count of compared tables and the count of
    /// tables that the method did not compare. A root that the program does not hold must give NaN, as
    /// TensorEvaluator gives.
    /// </summary>
    /// <remarks>
    /// This rule applies to a root whose reference value is NaN and whose TensorEvaluator value is not
    /// NaN. When <paramref name="gatherNaN"/> is not null (a program with gather lowering), the method
    /// compares such a root with the TensorEvaluator value. It counts the root in
    /// <paramref name="gatherNaN"/>. A gather reads only the candidate of the bit that is 1. So it skips
    /// the product 0 × NaN of the dense sum of the reference. This difference is a property of the gather
    /// lowering of Core, and the GPU must give the value of the compiled program.
    /// </remarks>
    public static (int Compared, int Excluded) Compare(
        GpuDevice? device, Circuit circuit, TensorProgram program, float[] sources, int tableCount, float[] outputs, bool sameNumericValue,
        System.Runtime.CompilerServices.StrongBox<int>? gatherNaN = null)
    {
        TensorEvaluator? tensor = null;
        var rows = ShaderRows.FlattenProgram(program);
        var sourceSize = program.SourceSize;
        var rootCount = program.Roots.Count;
        Assert.Equal(tableCount * rootCount, outputs.Length);
        var held = new bool[rootCount];
        for (var root = 0; root < rootCount; root++) held[root] = program.WireOrder.Contains(program.Roots[root]);
        var compared = 0;
        var excluded = 0;
        for (var table = 0; table < tableCount; table++)
        {
            var source = sources.AsSpan(table * sourceSize, sourceSize);
            if (device is { FlushesSubnormals: true })
            {
                RowInterpreter.Run(program, rows, source, out var sawSubnormal);
                if (sawSubnormal)
                {
                    excluded++;
                    continue;
                }
            }
            var reference = circuit.EvaluateReference(source);
            float[]? tensorValues = null;
            for (var root = 0; root < rootCount; root++)
            {
                var expected = held[root] ? reference[program.Roots[root]] : float.NaN;
                var actual = outputs[(table * rootCount) + root];
                if (gatherNaN is not null && held[root] && float.IsNaN(expected))
                {
                    tensorValues ??= [.. (tensor ??= circuit.CreateTensorProgramEvaluator(program)).Evaluate(source)];
                    if (!float.IsNaN(tensorValues[root]))
                    {
                        expected = tensorValues[root];
                        gatherNaN.Value++;
                    }
                }
                var agrees = sameNumericValue ? Js.SameNumericValue(expected, actual) : Js.ObjectIs(expected, actual);
                if (!agrees)
                {
                    Assert.Fail($"table {table} ({string.Join(", ", source.ToArray().Select(value => value.ToString("R")))}): root {root} "
                        + $"(wire {program.Roots[root]}) got {actual:R}, reference {expected:R}");
                }
            }
            compared++;
        }
        return (compared, excluded);
    }
}

/// <summary>The program of 20 operations and its seeded source tables.</summary>
internal static class BatchPrograms
{
    public const int OperationCount = 20;

    // The cells: x0 and x1 within ±100, a unit cell u, a whole number n within ±12, and a, b within ±1e6.
    public static (Circuit Circuit, TensorProgram Program) Create()
    {
        var circuit = new Circuit(6);
        var (x0, x1, u, n, a, b) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4), circuit.Source(5));
        circuit.AssumeRange(x0, -100, 100);
        circuit.AssumeRange(x1, -100, 100);
        circuit.AssumeRange(u, 0, 1);
        circuit.AssumeRange(n, -12, 12);
        circuit.AssumeGrid(n, 1);
        circuit.AssumeRange(a, -1e6, 1e6);
        circuit.AssumeRange(b, -1e6, 1e6);
        var sum = circuit.RoundAffine([(x0, 0.3), (x1, -1.7), (u, 1.0 / 3)], 0.1);
        var fma = circuit.RoundAffine([(x0, 1.0000001)], -1.0000002);
        var gated = circuit.Reglu([(x0, 1)], 0.5, [(x1, 0.7)], 0.25);
        var atom = circuit.Boolean(u);
        var product = circuit.Multiply(x0, x1);
        var selected = circuit.Select(atom, sum, gated);
        var read = circuit.IndexRead(n, [x0, x1, u, sum, product], OutOfRange.Zero);
        var shifted = circuit.RoundAffine([(read, 1)], 12_582_912);
        var rounded = circuit.RoundAffine([(shifted, 1)], -12_582_912);
        var difference = circuit.RoundAffine([(a, 1), (b, -1)], 0);
        var positive = circuit.Relu(difference);
        var atom2 = circuit.Boolean(circuit.RoundAffine([(u, 0.5)], 0.25));
        var scaled = circuit.Multiply(u, x1);
        var mix = circuit.RoundAffine([(fma, 2), (selected, -0.7), (rounded, 1), (positive, 1e-6), (atom2, 5), (scaled, 1)], -0.0);
        Assert.Equal(OperationCount, circuit.Operations.Count);
        return (circuit, circuit.CompileTensorProgram([mix, selected, read, product, sum, positive], lowerGathers: false));
    }

    // Seeded source tables in the declared ranges, with the ends of the ranges, both zeros and some
    // subnormal values.
    public static float[] Tables(int tableCount, int seed)
    {
        var random = Js.CreateRandom(seed);
        var sources = new float[tableCount * 6];
        float Value(double limit)
        {
            var choice = random();
            if (choice < 0.03) return (float)limit;
            if (choice < 0.06) return (float)-limit;
            if (choice < 0.09) return random() < 0.5 ? 0f : -0f;
            if (choice < 0.1) return random() < 0.5 ? float.Epsilon : -MathF.ScaleB(1f, -130);
            return (float)((random() - 0.5) * 2 * limit);
        }
        for (var table = 0; table < tableCount; table++)
        {
            var cells = sources.AsSpan(table * 6, 6);
            cells[0] = Value(100);
            cells[1] = Value(100);
            cells[2] = random() < 0.1 ? 0.5f : (float)random();
            cells[3] = (float)Math.Floor((random() * 25) - 12);
            cells[4] = Value(1e6);
            cells[5] = random() < 0.1 ? cells[4] : Value(1e6);
        }
        return sources;
    }
}

[Collection(GpuCollection.Name)]
public sealed class BatchEvaluatorTests(GpuFixture fixture, ITestOutputHelper output)
{
    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    // A gather can change the sign of an exactly zero result (TensorDifferentialTests header). Also, an
    // i32 has no -0 (question 3 of the phase 5 plan).
    private static bool SameNumericValue(GpuWriter writer, bool lowersGathers = false) => lowersGathers || writer == GpuWriter.Integer;

    [Theory]
    [MemberData(nameof(Writers))]
    public void Evaluate_over_100000_tables_equals_the_reference_on_each_table(GpuWriter writer)
    {
        const int TableCount = 100_000;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x100000);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            // The first call creates the buffers. The second call is the measured one.
            evaluator.Evaluate(sources, TableCount);
            var watch = Stopwatch.StartNew();
            var outputs = evaluator.Evaluate(sources, TableCount);
            watch.Stop();
            var (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, TableCount, outputs, SameNumericValue(writer));
            var statistics = evaluator.Module.Statistics;
            output.WriteLine($"[batch] {writer}: {TableCount} tables on the GPU in {watch.Elapsed.TotalMilliseconds:F1} ms; "
                + $"{compared} compared, {excluded} with a subnormal value not compared; "
                + $"rows: {statistics.FloatRows} f32, {statistics.WholeRows} i32, {statistics.BitRows} u32; {statistics.Keeps} keeps");
            Assert.True(compared > TableCount * 0.5, $"compared {compared}");
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void A_chunk_limit_of_1000_tables_gives_the_reference_for_2500_tables(GpuWriter writer)
    {
        const int TableCount = 2_500;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x2500);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer, maxTablesPerChunk: 1_000);
            Assert.Equal(1_000, evaluator.TablesPerChunk);
            var outputs = new float[TableCount * evaluator.RootCount];
            // NaN in each output shows a chunk that the evaluator did not write.
            Array.Fill(outputs, float.NaN);
            evaluator.Evaluate(sources, TableCount, outputs);
            var (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, TableCount, outputs, SameNumericValue(writer));
            output.WriteLine($"[chunks] {writer}: {compared} compared, {excluded} with a subnormal value not compared");
            // The subnormal rule must leave most tables compared, so that each chunk is checked.
            Assert.True(compared >= TableCount / 2, $"compared {compared} of {TableCount}");
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void A_root_that_the_program_does_not_hold_gives_NaN(GpuWriter writer)
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -10, 10);
        circuit.AssumeRange(circuit.Source(1), -10, 10);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var program = circuit.CompileTensorProgram([sum]) with { Roots = UnsignedIndices.From([sum.Position, unused.Position]) };
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            var outputs = evaluator.Evaluate([2f, 3f, -1f, 0.5f], 2);
            Js.AllIs([5, double.NaN, -0.5, double.NaN], outputs.Select(value => (double)value));
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void Bad_arguments_throw_ArgumentException_and_a_bad_source_table_throws_the_TensorEvaluator_exception(GpuWriter writer)
    {
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(3, 7);
        var bad = (float[])sources.Clone();
        // Table 1, cell 2 (u) is past its range 0..1.
        bad[6 + 2] = 1.5f;
        var expected = Assert.Throws<ArgumentException>(() => circuit.CreateTensorProgramEvaluator(program).Evaluate(bad.AsSpan(6, 6)));
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            var rootCount = evaluator.RootCount;
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources.AsSpan(0, 17), 3, new float[3 * rootCount]));
            Assert.ThrowsAny<ArgumentException>(() => evaluator.Evaluate(sources, -1, new float[3 * rootCount]));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources, 3, new float[(3 * rootCount) - 1]));
            var actual = Assert.Throws<ArgumentException>(() => evaluator.Evaluate(bad, 3));
            Assert.Equal(expected.Message, actual.Message);
        });
    }

    // The integer writer reads a cell on a grid of 1 as an i32. So the integer evaluator checks the
    // grid and the bit declarations of each table. The f32 evaluator checks the ranges only, as
    // TensorEvaluator does, and gives the reference value.
    [Fact]
    public void The_integer_evaluator_refuses_a_source_table_that_breaks_a_grid_or_a_bit_declaration()
    {
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(3, 11);
        var offGrid = (float[])sources.Clone();
        // Table 2, cell 3 (n, on a grid of 1) holds 0.5.
        offGrid[12 + 3] = 0.5f;
        var bitCircuit = new Circuit(1);
        bitCircuit.AssumeBit(bitCircuit.Source(0));
        var bitProgram = bitCircuit.CompileTensorProgram([bitCircuit.RoundAffine([(bitCircuit.Source(0), 3)], 1)]);
        fixture.ForEachDevice(output, device =>
        {
            using var integer = GpuEvaluator.Create(device, circuit, program, GpuWriter.Integer);
            var exception = Assert.Throws<ArgumentException>(() => integer.Evaluate(offGrid, 3));
            Assert.Equal("source wire 3 is declared on the grid 1 and must be a multiple of it, got 0.5", exception.Message);
            using var f32 = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            GpuChecks.Compare(device, circuit, program, offGrid, 3, f32.Evaluate(offGrid, 3), sameNumericValue: false);

            using var bits = GpuEvaluator.Create(device, bitCircuit, bitProgram, GpuWriter.Integer);
            Assert.Equal("source wire 0 is declared a bit and must be 0 or 1, got 0.5", Assert.Throws<ArgumentException>(() => bits.Evaluate([0.5f], 1)).Message);
            Assert.True(Js.SameNumericValue(1, bits.Evaluate([-0f], 1)[0]));
            Assert.True(Js.SameNumericValue(4, bits.Evaluate([1f], 1)[0]));
        });
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void Two_evaluators_on_one_device_run_on_two_threads(GpuWriter writer)
    {
        const int TableCount = 5_000;
        const int Rounds = 20;
        var (circuit, program) = BatchPrograms.Create();
        var other = new Circuit(1);
        other.AssumeRange(other.Source(0), -100, 100);
        other.RoundAffine([(other.Source(0), 1.0 / 3)], 0.7);
        var otherProgram = other.CompileTensorProgram();
        var sources = BatchPrograms.Tables(TableCount, 0x7a0);
        var random = Js.CreateRandom(0x7a1);
        var otherSources = Enumerable.Range(0, TableCount).Select(_ => (float)((random() - 0.5) * 200)).ToArray();
        fixture.ForEachDevice(output, device =>
        {
            using var first = GpuEvaluator.Create(device, circuit, program, writer);
            using var second = GpuEvaluator.Create(device, other, otherProgram, writer);
            var results = new float[2][];
            // LongRunning gives each task its own thread.
            Task.WaitAll(
                Task.Factory.StartNew(() => { for (var round = 0; round < Rounds; round++) results[0] = first.Evaluate(sources, TableCount); }, TaskCreationOptions.LongRunning),
                Task.Factory.StartNew(() => { for (var round = 0; round < Rounds; round++) results[1] = second.Evaluate(otherSources, TableCount); }, TaskCreationOptions.LongRunning));
            GpuChecks.Compare(device, circuit, program, sources, TableCount, results[0], SameNumericValue(writer));
            GpuChecks.Compare(device, other, otherProgram, otherSources, TableCount, results[1], SameNumericValue(writer));
        });
    }

    // Each row form of the f32 writer on the device. The forms are index reads of each candidate form and
    // each out-of-range rule, and masked and unmasked gathers of computed values. They also include a row
    // of 33 terms, a ReGLU, a boolean atom and a bias of -0. A circuit of 32 source cells puts an index
    // read at packed position 32. So its index value has the name x32 (the name i32 is a WGSL type). 24
    // seeded circuits of RandomPrograms add more forms.
    [Theory]
    [MemberData(nameof(Writers))]
    public void Each_row_form_runs_on_the_device_and_equals_the_reference(GpuWriter writer)
    {
        var circuit = new Circuit(8);
        var cells = Enumerable.Range(0, 8).Select(circuit.Source).ToArray();
        circuit.AssumeRange(cells[0], -6, 6);
        circuit.AssumeGrid(cells[0], 1);
        foreach (var cell in cells[1..5]) circuit.AssumeRange(cell, -100, 100);
        circuit.AssumeOneHot(cells[5..], allowAllZero: true);
        var listRead = circuit.IndexRead(cells[0], [cells[3], cells[1], cells[4], cells[1]]);
        var a = circuit.RoundAffine([(cells[1], 1.0 / 3), (cells[2], -0.7)], -0.0);
        var b = circuit.Reglu([(cells[3], 0.5)], 1, [(cells[4], 1)], -2);
        var computedRead = circuit.IndexReadAffine([(cells[0], 1)], 1, [a, b, cells[1]], OutOfRange.Zero, OutOfRange.Clamp);
        var stridedRead = circuit.IndexReadAffine([(cells[0], 1)], 0, [cells[1], cells[2], cells[3]], OutOfRange.Clamp, OutOfRange.Zero);
        var gathered = circuit.Affine([
            (circuit.Multiply(cells[5], a), 1),
            (circuit.Multiply(cells[6], b), 1),
            (circuit.Multiply(cells[7], computedRead), 1),
        ]);
        var unmaskedGroup = circuit.Argmax([circuit.ClampZeroOne(cells[1], circuit.Constant(1)), circuit.ClampZeroOne(cells[2], circuit.Constant(1))], circuit.Constant(1)).Selected;
        var unmasked = circuit.Affine([(circuit.Multiply(unmaskedGroup[0], a), 1), (circuit.Multiply(unmaskedGroup[1], stridedRead), 1)]);
        var longRow = circuit.RoundAffine([.. Enumerable.Range(0, 33).Select(index => new Term(cells[1 + (index % 4)], index % 3 == 0 ? -1 : 0.3))], 0.25);
        var atom = circuit.Boolean(circuit.ClampZeroOne(longRow, circuit.Constant(1)));
        circuit.RoundAffine([(gathered, 1), (unmasked, 1), (atom, 2), (listRead, 1)]);

        var random = Js.CreateRandom(0xf0f);
        const int TableCount = 256;
        var sources = new float[TableCount * 8];
        for (var table = 0; table < TableCount; table++)
        {
            var cellsOfTable = sources.AsSpan(table * 8, 8);
            cellsOfTable[0] = (float)Math.Floor((random() * 13) - 6);
            for (var cell = 1; cell < 5; cell++) cellsOfTable[cell] = random() < 0.1 ? -0f : (float)((random() - 0.5) * 200);
            var selected = (int)Math.Floor(random() * 4);
            for (var member = 0; member < 3; member++) cellsOfTable[5 + member] = member == selected ? 1 : 0;
        }

        var lowered = circuit.CompileTensorProgram();
        var plain = circuit.CompileTensorProgram(lowerGathers: false);
        var forms = ShaderRows.FlattenProgram(lowered).Concat(ShaderRows.FlattenProgram(plain)).ToList();
        var wide = new Circuit(32);
        wide.AssumeRange(wide.Source(0), -6, 6);
        wide.AssumeGrid(wide.Source(0), 1);
        for (var cell = 1; cell < 32; cell++) wide.AssumeRange(wide.Source(cell), -100, 100);
        wide.IndexRead(wide.Source(0), [wide.Source(1), wide.Source(2), wide.Source(3)], OutOfRange.Zero);
        var wideProgram = wide.CompileTensorProgram();
        Assert.Contains(ShaderRows.FlattenProgram(wideProgram), row => row is IndexRow { Position: 32 });
        var wideSources = new float[TableCount * 32];
        for (var index = 0; index < wideSources.Length; index++) wideSources[index] = index % 32 == 0 ? (float)Math.Floor((random() * 13) - 6) : (float)((random() - 0.5) * 200);
        foreach (var form in Enum.GetValues<CandidateForm>()) Assert.Contains(forms, row => row is IndexRow read && read.Candidates.Form == form);
        Assert.Contains(forms, row => row is AffineRow affine && affine.Gathers.Any(gather => gather.Masked && gather.Candidates.Form == CandidateForm.Computed));
        Assert.Contains(forms, row => row is AffineRow affine && affine.Gathers.Any(gather => !gather.Masked));

        var seeded = new List<(Circuit Circuit, float[] Sources)>();
        var seedRandom = Js.CreateRandom(0x2424);
        var calls = new int[Enum.GetValues<RandomPrograms.BuilderCall>().Length];
        for (var index = 0; index < 24; index++)
        {
            var (randomCircuit, randomCells, groups, _) = RandomPrograms.BuildCircuit(seedRandom, calls);
            seeded.Add((randomCircuit, [.. Enumerable.Range(0, 64).SelectMany(_ => RandomPrograms.CreateSource(seedRandom, randomCells, groups))]));
        }

        fixture.ForEachDevice(output, device =>
        {
            var compared = 0;
            var excluded = 0;
            var (floatRows, wholeRows, bitRows) = (0, 0, 0);
            void Run(Circuit target, TensorProgram program, float[] tables, int count, bool lowersGathers)
            {
                using var evaluator = GpuEvaluator.Create(device, target, program, writer);
                var (c, e) = GpuChecks.Compare(device, target, program, tables, count, evaluator.Evaluate(tables, count), SameNumericValue(writer, lowersGathers));
                compared += c;
                excluded += e;
                floatRows += evaluator.Module.Statistics.FloatRows;
                wholeRows += evaluator.Module.Statistics.WholeRows;
                bitRows += evaluator.Module.Statistics.BitRows;
            }
            Run(circuit, lowered, sources, TableCount, lowersGathers: true);
            Run(circuit, plain, sources, TableCount, lowersGathers: false);
            Run(wide, wideProgram, wideSources, TableCount, lowersGathers: false);
            foreach (var (randomCircuit, tables) in seeded)
            {
                Run(randomCircuit, randomCircuit.CompileTensorProgram(), tables, 64, lowersGathers: true);
                Run(randomCircuit, randomCircuit.CompileTensorProgram(lowerGathers: false), tables, 64, lowersGathers: false);
            }
            output.WriteLine($"[forms] {writer}: {compared} evaluations compared, {excluded} with a subnormal value not compared; "
                + $"rows: {floatRows} f32, {wholeRows} i32, {bitRows} u32");
            if (writer == GpuWriter.Integer) Assert.True(wholeRows > 0 && bitRows > 0, "the integer writer wrote no integer row");
            // The forms give 3 * 256 + 48 * 64 = 3,840 evaluations. The subnormal rule must leave at
            // least half of them compared, so that it cannot exclude each table and pass.
            Assert.True(compared >= 1_920, $"compared {compared} of 3840");
        });
    }

    // A one-hot group of -0 bits and a row with the bias -0. The mass of a gather starts at +0, as the
    // tensor evaluator starts it. So a group of -0 bits gives the mass +0, the masked product -0 for a
    // negative candidate, and the row value -0 + -0 = -0. A mass that starts at the first bit gives -0,
    // the product +0, and the row value +0. The f32 writer must give the bits of the tensor evaluator,
    // and each writer must give the numeric value of the reference.
    [Theory]
    [MemberData(nameof(Writers))]
    public void A_gather_over_negative_zero_bits_with_a_negative_zero_bias_gives_the_sign_of_the_tensor_evaluator(GpuWriter writer)
    {
        var circuit = new Circuit(6);
        var cells = Enumerable.Range(0, 6).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(cells[..3], allowAllZero: true);
        foreach (var cell in cells[3..]) circuit.AssumeRange(cell, -100, 100);
        var sum = circuit.Affine([
            (circuit.Multiply(cells[0], cells[3]), 1),
            (circuit.Multiply(cells[1], cells[4]), 1),
            (circuit.Multiply(cells[2], cells[5]), 1),
        ], -0.0);
        var program = circuit.CompileTensorProgram([sum]);
        Assert.Equal(1, TensorChecks.ProgramCounts(program).GatherReads);
        float[][] tables =
        [
            [-0f, -0f, -0f, -5f, -6f, -7f],
            [-0f, -0f, -0f, 5f, -6f, -0f],
            [-0f, -0f, -0f, -0f, -0f, -0f],
            [1f, -0f, -0f, -0f, 6f, 7f],
            [-0f, 1f, -0f, 5f, -0f, 7f],
            [-0f, -0f, 1f, 5f, 6f, -7f],
            [0f, 0f, 0f, -5f, -6f, -7f],
        ];
        var sources = tables.SelectMany(table => table).ToArray();
        var tensor = circuit.CreateTensorProgramEvaluator(program);
        var expected = tables.Select(table => tensor.Evaluate(table)[0]).ToArray();
        // The case of the test: the tensor evaluator gives -0 for the first table.
        Assert.True(expected[0] == 0 && float.IsNegative(expected[0]), $"tensor value {expected[0]:R}");
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            var outputs = evaluator.Evaluate(sources, tables.Length);
            var (compared, _) = GpuChecks.Compare(device, circuit, program, sources, tables.Length, outputs, sameNumericValue: true);
            Assert.Equal(tables.Length, compared);
            if (writer == GpuWriter.Float32)
            {
                Js.AllIs(expected.Select(value => (double)value), outputs.Select(value => (double)value));
            }
        });
    }

    // GpuEvaluator is also a root evaluator of one source table (IRootEvaluator). The values equal the
    // batch values and the reference. The evaluator returns the same array on each call.
    [Theory]
    [MemberData(nameof(Writers))]
    public void The_root_evaluator_of_one_table_gives_the_batch_values(GpuWriter writer)
    {
        const int TableCount = 64;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x1e0);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            IRootEvaluator root = evaluator;
            Assert.Same(program, root.Program);
            var batch = evaluator.Evaluate(sources, TableCount);
            var single = new float[batch.Length];
            float[]? first = null;
            for (var table = 0; table < TableCount; table++)
            {
                var values = root.Evaluate(sources.AsSpan(table * 6, 6));
                Assert.Equal(evaluator.RootCount, values.Length);
                first ??= values;
                Assert.Same(first, values);
                values.CopyTo(single, table * evaluator.RootCount);
            }
            Js.AllIs(batch.Select(value => (double)value), single.Select(value => (double)value));
            var (compared, _) = GpuChecks.Compare(device, circuit, program, sources, TableCount, single, SameNumericValue(writer));
            Assert.True(compared >= TableCount / 2, $"compared {compared}");
            Assert.Throws<ArgumentException>(() => root.Evaluate(sources.AsSpan(0, 5)));
        });
    }

    [Fact]
    public void Dispose_two_times_does_not_throw_and_a_call_after_Dispose_throws()
    {
        var (circuit, program) = BatchPrograms.Create();
        var smaller = new Circuit(2);
        fixture.ForEachDevice(output, device =>
        {
            var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            evaluator.Dispose();
            // The default writer is the integer writer (decision 5 of the phase 5 plan).
            using (var integer = GpuEvaluator.Create(device, circuit, program))
            {
                Assert.True(integer.Module.Statistics.BitRows > 0, "the default writer wrote no u32 row");
                Assert.Equal(WgslIntegerWriter.Write(circuit, program).Code, integer.Module.Code);
            }
            evaluator.Dispose();
            Assert.Throws<ObjectDisposedException>(() => evaluator.Evaluate(BatchPrograms.Tables(1, 1), 1));
            // A program of another circuit with other source cells throws.
            Assert.Throws<ArgumentException>(() => GpuEvaluator.Create(device, smaller, program, GpuWriter.Float32));
            // BatchEvaluators gives the GPU evaluator for a device, with the integer writer by default.
            using var batch = BatchEvaluators.Create(circuit, program, device, GpuWriter.Float32);
            Assert.IsType<GpuEvaluator>(batch);
            using var defaultBatch = BatchEvaluators.Create(circuit, program, device);
            Assert.Equal(WgslIntegerWriter.Write(circuit, program).Code, Assert.IsType<GpuEvaluator>(defaultBatch).Module.Code);
        });
    }
}

/// <summary>The CPU batch evaluator. These tests need no adapter.</summary>
public sealed class CpuBatchEvaluatorTests(ITestOutputHelper output)
{
    [Fact]
    public void CpuBatchEvaluator_equals_the_reference_over_100000_tables()
    {
        const int TableCount = 100_000;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x100000);
        using var evaluator = new CpuBatchEvaluator(circuit, program);
        var outputs = new float[TableCount * evaluator.RootCount];
        evaluator.Evaluate(sources, TableCount, outputs);
        var watch = Stopwatch.StartNew();
        evaluator.Evaluate(sources, TableCount, outputs);
        watch.Stop();
        var (compared, _) = GpuChecks.Compare(null, circuit, program, sources, TableCount, outputs, sameNumericValue: false);
        Assert.Equal(TableCount, compared);
        output.WriteLine($"[batch] {TableCount} tables on the CPU in {watch.Elapsed.TotalMilliseconds:F1} ms ({Environment.ProcessorCount} processors)");
    }

    [Fact]
    public void BatchEvaluators_Create_with_no_device_gives_the_cpu_evaluator()
    {
        var (circuit, program) = BatchPrograms.Create();
        using var evaluator = BatchEvaluators.Create(circuit, program, null);
        Assert.IsType<CpuBatchEvaluator>(evaluator);
        Assert.Same(program, evaluator.Program);
        Assert.Equal(program.Roots.Count, evaluator.RootCount);
    }

    [Fact]
    public void The_cpu_evaluator_checks_its_arguments()
    {
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(3, 7);
        var evaluator = new CpuBatchEvaluator(circuit, program, maxDegreeOfParallelism: 2);
        var rootCount = evaluator.RootCount;
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources.AsSpan(0, 17), 3, new float[3 * rootCount]));
        Assert.ThrowsAny<ArgumentException>(() => evaluator.Evaluate(sources, -1, new float[3 * rootCount]));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources, 3, new float[(3 * rootCount) - 1]));
        var bad = (float[])sources.Clone();
        bad[6 + 2] = 1.5f;
        var expected = Assert.Throws<ArgumentException>(() => circuit.CreateTensorProgramEvaluator(program).Evaluate(bad.AsSpan(6, 6)));
        Assert.Equal(expected.Message, Assert.Throws<ArgumentException>(() => evaluator.Evaluate(bad, 3, new float[3 * rootCount])).Message);
        Assert.ThrowsAny<ArgumentException>(() => new CpuBatchEvaluator(circuit, program, maxDegreeOfParallelism: 0));
        Assert.Throws<ArgumentException>(() => new CpuBatchEvaluator(new Circuit(2), program));
        evaluator.Dispose();
        Assert.Throws<ObjectDisposedException>(() => evaluator.Evaluate(sources, 3, new float[3 * rootCount]));
    }
}
