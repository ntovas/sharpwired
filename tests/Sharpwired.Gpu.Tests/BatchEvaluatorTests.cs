// No Handwired source. The tests of the batch evaluators: GpuEvaluator on each adapter, and
// CpuBatchEvaluator with no adapter. Each test compares the outputs with the reference evaluator.
// GpuChecks holds the comparison and the subnormal rule of decision 4 of the phase 5 plan.

using System.Diagnostics;
using Sharpwired.Core;
using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

/// <summary>The comparison of batch outputs with the reference evaluator.</summary>
internal static class GpuChecks
{
    /// <summary>
    /// Compare each root of each table with the reference evaluator. On a device that flushes subnormal
    /// values, a table whose f32 evaluation holds a nonzero subnormal value is not compared (decision 4
    /// of the phase 5 plan). Return the count of compared tables and the count of tables that were not
    /// compared. A root that the program does not hold must give NaN, as TensorEvaluator gives.
    /// </summary>
    public static (int Compared, int Excluded) Compare(
        GpuDevice? device, Circuit circuit, TensorProgram program, float[] sources, int tableCount, float[] outputs, bool sameNumericValue)
    {
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
            for (var root = 0; root < rootCount; root++)
            {
                var expected = held[root] ? reference[program.Roots[root]] : float.NaN;
                var actual = outputs[(table * rootCount) + root];
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
    [Fact]
    public void Evaluate_over_100000_tables_equals_the_reference_on_each_table()
    {
        const int TableCount = 100_000;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x100000);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            // The first call creates the buffers. The second call is the measured one.
            evaluator.Evaluate(sources, TableCount);
            var watch = Stopwatch.StartNew();
            var outputs = evaluator.Evaluate(sources, TableCount);
            watch.Stop();
            var (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, TableCount, outputs, sameNumericValue: false);
            output.WriteLine($"[batch] {TableCount} tables on the GPU in {watch.Elapsed.TotalMilliseconds:F1} ms; "
                + $"{compared} compared, {excluded} with a subnormal value not compared");
            Assert.True(compared > TableCount * 0.5, $"compared {compared}");
        });
    }

    [Fact]
    public void A_chunk_limit_of_1000_tables_gives_the_reference_for_2500_tables()
    {
        const int TableCount = 2_500;
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(TableCount, 0x2500);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32, maxTablesPerChunk: 1_000);
            Assert.Equal(1_000, evaluator.TablesPerChunk);
            var outputs = new float[TableCount * evaluator.RootCount];
            // NaN in each output shows a chunk that the evaluator did not write.
            Array.Fill(outputs, float.NaN);
            evaluator.Evaluate(sources, TableCount, outputs);
            var (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, TableCount, outputs, sameNumericValue: false);
            output.WriteLine($"[chunks] {compared} compared, {excluded} with a subnormal value not compared");
        });
    }

    [Fact]
    public void A_root_that_the_program_does_not_hold_gives_NaN()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -10, 10);
        circuit.AssumeRange(circuit.Source(1), -10, 10);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        var unused = circuit.RoundAffine([(circuit.Source(0), 5)]);
        var program = circuit.CompileTensorProgram([sum]) with { Roots = UnsignedIndices.From([sum.Position, unused.Position]) };
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            var outputs = evaluator.Evaluate([2f, 3f, -1f, 0.5f], 2);
            Js.AllIs([5, double.NaN, -0.5, double.NaN], outputs.Select(value => (double)value));
        });
    }

    [Fact]
    public void Bad_arguments_throw_ArgumentException_and_a_bad_source_table_throws_the_TensorEvaluator_exception()
    {
        var (circuit, program) = BatchPrograms.Create();
        var sources = BatchPrograms.Tables(3, 7);
        var bad = (float[])sources.Clone();
        // Table 1, cell 2 (u) is past its range 0..1.
        bad[6 + 2] = 1.5f;
        var expected = Assert.Throws<ArgumentException>(() => circuit.CreateTensorProgramEvaluator(program).Evaluate(bad.AsSpan(6, 6)));
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            var rootCount = evaluator.RootCount;
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources.AsSpan(0, 17), 3, new float[3 * rootCount]));
            Assert.ThrowsAny<ArgumentException>(() => evaluator.Evaluate(sources, -1, new float[3 * rootCount]));
            Assert.Throws<ArgumentException>(() => evaluator.Evaluate(sources, 3, new float[(3 * rootCount) - 1]));
            var actual = Assert.Throws<ArgumentException>(() => evaluator.Evaluate(bad, 3));
            Assert.Equal(expected.Message, actual.Message);
        });
    }

    [Fact]
    public void Two_evaluators_on_one_device_run_on_two_threads()
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
            using var first = GpuEvaluator.Create(device, circuit, program, GpuWriter.Float32);
            using var second = GpuEvaluator.Create(device, other, otherProgram, GpuWriter.Float32);
            var results = new float[2][];
            // LongRunning gives each task its own thread.
            Task.WaitAll(
                Task.Factory.StartNew(() => { for (var round = 0; round < Rounds; round++) results[0] = first.Evaluate(sources, TableCount); }, TaskCreationOptions.LongRunning),
                Task.Factory.StartNew(() => { for (var round = 0; round < Rounds; round++) results[1] = second.Evaluate(otherSources, TableCount); }, TaskCreationOptions.LongRunning));
            GpuChecks.Compare(device, circuit, program, sources, TableCount, results[0], sameNumericValue: false);
            GpuChecks.Compare(device, other, otherProgram, otherSources, TableCount, results[1], sameNumericValue: false);
        });
    }

    // Each row form of the f32 writer on the device: index reads of each candidate form and each
    // out-of-range rule, masked and unmasked gathers of computed values, a row of 33 terms, a ReGLU, a
    // boolean atom and a bias of -0. A circuit of 32 source cells puts an index read at packed position
    // 32, so its index value has the name x32 (the name i32 is a WGSL type). 24 seeded circuits of
    // RandomPrograms add more forms.
    [Fact]
    public void Each_row_form_runs_on_the_device_and_equals_the_reference()
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
            void Run(Circuit target, TensorProgram program, float[] tables, int count, bool sameNumericValue)
            {
                using var evaluator = GpuEvaluator.Create(device, target, program, GpuWriter.Float32);
                var (c, e) = GpuChecks.Compare(device, target, program, tables, count, evaluator.Evaluate(tables, count), sameNumericValue);
                compared += c;
                excluded += e;
            }
            // A gather can change the sign of an exactly zero result (TensorDifferentialTests header).
            Run(circuit, lowered, sources, TableCount, sameNumericValue: true);
            Run(circuit, plain, sources, TableCount, sameNumericValue: false);
            Run(wide, wideProgram, wideSources, TableCount, sameNumericValue: false);
            foreach (var (randomCircuit, tables) in seeded)
            {
                Run(randomCircuit, randomCircuit.CompileTensorProgram(), tables, 64, sameNumericValue: true);
                Run(randomCircuit, randomCircuit.CompileTensorProgram(lowerGathers: false), tables, 64, sameNumericValue: false);
            }
            output.WriteLine($"[forms] {compared} evaluations compared, {excluded} with a subnormal value not compared");
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
            evaluator.Dispose();
            Assert.Throws<ObjectDisposedException>(() => evaluator.Evaluate(BatchPrograms.Tables(1, 1), 1));
            // A program of another circuit with other source cells throws.
            Assert.Throws<ArgumentException>(() => GpuEvaluator.Create(device, smaller, program, GpuWriter.Float32));
            // BatchEvaluators gives the GPU evaluator for a device.
            using var batch = BatchEvaluators.Create(circuit, program, device, GpuWriter.Float32);
            Assert.IsType<GpuEvaluator>(batch);
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
