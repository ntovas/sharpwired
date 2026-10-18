// Handwired source: tests/json-search/json-step.test.ts and search.test.ts (the 'generated' backend of
// loop-program.ts gives the bits of the executor). Task 2 of the phase 6 plan: CompiledStage, the compiled
// delegate of one stage, gives the bits of TensorEvaluator (Object.is) on each root. The programs are the
// seeded programs of RandomPrograms in the 6 compile modes of GpuDifferentialTests, and the open-range
// variant. The comparison is Object.is in each mode, also with gather lowering, because both evaluators
// run the same gather steps. The FMA test checks that the JIT does not contract a product and a sum.
// The speed test writes the time of one evaluation of each backend at the step sizes of the plan.

using System.Diagnostics;
using Sharpwired.Core;
using Sharpwired.IL;
using Sharpwired.IL.Tests;
using Xunit.Abstractions;
using static Sharpwired.Core.Tests.RandomPrograms;

namespace Sharpwired.Gpu.Tests;

public sealed class CompiledStageTests(ITestOutputHelper output)
{
    private const int CircuitCount = 200;
    private const int SourcesPerCircuit = 64;
    private const int OpenRangeCircuitCount = 80;

    private static readonly (string Label, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] Modes =
    [
        ("gather lowering, build-time depths", (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("gather lowering, recomputed depths", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("no gather lowering, recomputed depths", (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true)),
        ("every wire as a root", (circuit, _) => circuit.CompileTensorProgram()),
        ("no gather lowering, every wire as a root", (circuit, _) => circuit.CompileTensorProgram(lowerGathers: false)),
    ];

    public static TheoryData<int> OpenRangeSeeds => new() { 0x0be7a, 0x51ce5 };

    private sealed record Case(Circuit Circuit, List<Wire> Roots, List<float[]> Sources);

    private sealed record Counts(int Programs, int Evaluations, int Values, int GatherReads, int IndexReads, int NaNValues, int NegativeZeros);

    [Fact]
    public void Seeded_programs_give_the_bits_of_TensorEvaluator_on_each_root_in_each_compile_mode()
    {
        var counts = Run(Cases(0x9d1ff, CircuitCount, openRangeShare: 0, subnormals: true), "seeded");

        // The floors are half of the counts of the first run (Apple M5 Pro). The first run gave 1,200
        // programs, 76,800 evaluations, 6,806,400 values, 896 gather reads and 1,244 index reads. It also
        // gave 8,890 NaN values and 83,892 values of -0.
        Assert.Equal(CircuitCount * Modes.Length, counts.Programs);
        Assert.Equal(CircuitCount * Modes.Length * SourcesPerCircuit, counts.Evaluations);
        Assert.True(counts.Values >= 3_403_200, $"values {counts.Values}");
        Assert.True(counts.GatherReads >= 448, $"gatherReads {counts.GatherReads}");
        Assert.True(counts.IndexReads >= 622, $"indexReads {counts.IndexReads}");
        Assert.True(counts.NaNValues > 0 && counts.NegativeZeros > 0, $"NaN {counts.NaNValues}, -0 {counts.NegativeZeros}");
    }

    [Theory]
    [MemberData(nameof(OpenRangeSeeds))]
    public void Seeded_programs_with_open_ranges_give_the_bits_of_TensorEvaluator(int seed)
    {
        var counts = Run(Cases(seed, OpenRangeCircuitCount, openRangeShare: 0.7, subnormals: false), $"open ranges, seed {seed:x}");

        // The floors are half of the smaller counts of the two seeds in the first run. Seed be7a gave
        // 2,899,584 values, 295 gather reads and 984 index reads. Seed 51ce5 gave 2,989,952, 351 and 740.
        Assert.Equal(OpenRangeCircuitCount * Modes.Length, counts.Programs);
        Assert.True(counts.Values >= 1_449_792, $"values {counts.Values}");
        Assert.True(counts.GatherReads >= 147, $"gatherReads {counts.GatherReads}");
        Assert.True(counts.IndexReads >= 370, $"indexReads {counts.IndexReads}");
        Assert.True(counts.NaNValues > 0, $"NaN {counts.NaNValues}");
    }

    // x × 1.0000001 - 1.0000002 over the 64 FP32 values around 1. A fused multiply-add gives other bits on
    // at least one value, so a contraction in the delegate fails this test. Run it in Debug and in Release.
    [Fact]
    public void The_affine_row_is_not_contracted_to_a_fused_multiply_add()
    {
        var circuit = new Circuit(1);
        var x = circuit.Source(0);
        circuit.AssumeRange(x, 0.5, 2);
        var row = circuit.RoundAffine([(x, 1.0000001)], -1.0000002);
        var program = circuit.CompileTensorProgram([row]);
        var stage = CompiledStage.Compile(circuit, program);
        var reference = circuit.CreateTensorProgramEvaluator(program);
        var roots = new float[1];
        var fusedDiffers = 0;
        foreach (var value in WgslFormTests.NearOne())
        {
            stage.Evaluate([value], roots);
            var expected = reference.Evaluate([value])[0];
            Js.Is(expected, roots[0]);
            Js.Is(circuit.EvaluateReference([value])[row.Position], roots[0]);
            if (!Js.ObjectIs(expected, MathF.FusedMultiplyAdd(value, 1.0000001f, -1.0000002f))) fusedDiffers++;
        }
        output.WriteLine($"[fma] the fused multiply-add differs on {fusedDiffers} of {WgslFormTests.NearOne().Length} values");
        Assert.True(fusedDiffers > 0);
    }

    // Large rows, a gate, a boolean atom and a table of 1,000 candidates. The rows split into several
    // delegates, and a read crosses no chunk.
    [Fact]
    public void A_large_program_gives_the_bits_of_TensorEvaluator()
    {
        var circuit = new Circuit(1_002);
        var index = circuit.Source(0);
        circuit.AssumeRange(index, -5, 1_005);
        circuit.AssumeGrid(index, 1);
        var scale = circuit.Source(1);
        circuit.AssumeRange(scale, -2, 2);
        var cells = Enumerable.Range(2, 1_000).Select(circuit.Source).ToArray();
        foreach (var cell in cells) circuit.AssumeRange(cell, -1_000, 1_000);
        var roots = new List<Wire>();
        for (var row = 0; row < 3_000; row++)
        {
            var terms = Enumerable.Range(0, 1 + (row % 40)).Select(term => (cells[(row * 7 + term * 13) % cells.Length], (term % 5) - 2.25 + row * 1e-4)).ToArray();
            var affine = circuit.RoundAffine([.. terms, (scale, 1.0 / 3)], row % 3 == 0 ? -0.0 : row * 0.1);
            roots.Add(row % 5 == 0 ? circuit.Reglu([(affine, 1)], 0, [(scale, 1), (affine, 0.01)], 0.5) : affine);
        }
        roots.Add(circuit.Boolean(circuit.RoundAffine([(scale, 0.25)], 0.5)));
        roots.Add(circuit.IndexRead(index, cells, OutOfRange.Zero));
        roots.Add(circuit.IndexRead(index, [.. cells.Reverse()], OutOfRange.Clamp));
        roots.Add(circuit.IndexRead(index, [.. roots.Take(600)], OutOfRange.Clamp));
        var program = circuit.CompileTensorProgram(roots);
        var stage = CompiledStage.Compile(circuit, program);
        Assert.True(stage.ChunkCount > 1, $"chunks {stage.ChunkCount}");
        var reference = circuit.CreateTensorProgramEvaluator(program);
        var random = Js.CreateRandom(0x1a7e);
        var values = new float[roots.Count];
        for (var table = 0; table < 40; table++)
        {
            var source = new float[circuit.SourceSize];
            source[0] = (float)Math.Floor(random() * 1_011) - 5;
            source[1] = table == 0 ? -0f : (float)((random() - 0.5) * 4);
            for (var cell = 2; cell < source.Length; cell++) source[cell] = (float)((random() - 0.5) * 2_000);
            stage.Evaluate(source, values);
            Js.AllIs(reference.Evaluate(source).Select(value => (double)value), values.Select(value => (double)value));
        }
    }

    [Fact]
    public void Evaluate_checks_the_lengths_and_Compile_checks_the_program()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeRange(circuit.Source(1), 0, 1);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)], 0);
        var program = circuit.CompileTensorProgram([sum]);
        var stage = CompiledStage.Compile(circuit, program);
        Assert.Same(program, stage.Program);
        Assert.Equal(1, stage.RootCount);
        Js.Throws(() => stage.Evaluate([1f], new float[1]), "source");
        Js.Throws(() => stage.Evaluate([1f, 0f], new float[2]), "roots");
        var other = new Circuit(3);
        Assert.Throws<ArgumentException>(() => CompiledStage.Compile(other, program));
        Assert.Throws<ArgumentNullException>(() => CompiledStage.Compile(circuit, null!));
    }

    // Decision 3 of the plan: the time of one evaluation of CompiledStage and of TensorEvaluator at the
    // step sizes of the plan. The test asserts equal values, and writes the times. It asserts no time.
    [Fact]
    public void Writes_the_speed_against_TensorEvaluator_at_the_step_sizes_of_the_plan()
    {
        string[] names = ["Reverse8", "Forward8", "Reverse16", "Forward16", "Reverse32", "Forward32", "LookupStep"];
        foreach (var name in names)
        {
            var function = HandwiredTranslator.TranslateFunction(typeof(LoopInputs).GetMethod(name)!);
            var circuit = function.Table.Circuit;
            var program = circuit.CompileTensorProgram([.. function.Outputs.Select(leaf => leaf.Wire).Distinct()]);
            var compileWatch = Stopwatch.StartNew();
            var stage = CompiledStage.Compile(circuit, program);
            compileWatch.Stop();
            var reference = circuit.CreateTensorProgramEvaluator(program);
            var sources = SeededSources.Create(function.Table, 64, 0x5eed);
            var roots = new float[program.Roots.Count];
            foreach (var source in sources)
            {
                stage.Evaluate(source, roots);
                Js.AllIs(reference.Evaluate(source).Select(value => (double)value), roots.Select(value => (double)value));
            }

            var rounds = Math.Max(200, 400_000 / program.ValueCount);
            var compiledTime = Time(rounds, sources, source => stage.Evaluate(source, roots));
            var tensorTime = Time(rounds, sources, source => reference.Evaluate(source));
            // CompiledStage does not check the source ranges, and TensorEvaluator does. So the ratio uses the
            // time of TensorEvaluator less the time of its range check.
            var checkTime = Time(rounds, sources, source => circuit.ValidateSourceRanges(source));
            var tensorRowsTime = Math.Max(tensorTime - checkTime, 0.001);
            output.WriteLine(
                $"[speed] {name}: {program.ValueCount - program.SourceSize} rows, {program.Layers.Length} layers, {stage.ChunkCount} chunks, "
                + $"compile {compileWatch.Elapsed.TotalMilliseconds:F1} ms; CompiledStage {compiledTime:F3} us, TensorEvaluator {tensorTime:F3} us "
                + $"(range check {checkTime:F3} us, rows {tensorRowsTime:F3} us), ratio without the range check {tensorRowsTime / compiledTime:F1}");
        }
    }

    // The microseconds of one evaluation: the best of 5 passes of rounds evaluations.
    private static double Time(int rounds, List<float[]> sources, Action<float[]> evaluate)
    {
        var best = double.MaxValue;
        for (var pass = 0; pass < 5; pass++)
        {
            var watch = Stopwatch.StartNew();
            for (var round = 0; round < rounds; round++) evaluate(sources[round % sources.Count]);
            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalMicroseconds / rounds);
        }
        return best;
    }

    private static List<Case> Cases(int seed, int circuitCount, double openRangeShare, bool subnormals)
    {
        var random = Js.CreateRandom(seed);
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];
        var cases = new List<Case>();
        for (var circuitIndex = 0; circuitIndex < circuitCount; circuitIndex++)
        {
            var (circuit, cells, groups, wires) = BuildCircuit(random, builderCalls, openRangeShare);
            var operationWires = wires.Where(wire => wire.Position >= circuit.SourceSize).Distinct().ToList();
            var rootCount = 1 + RandomInteger(random, 5);
            var roots = Enumerable.Range(0, rootCount).Select(_ => operationWires[RandomInteger(random, operationWires.Count)]).ToList();
            var sources = Enumerable.Range(0, SourcesPerCircuit).Select(_ => CreateSource(random, cells, groups, subnormals)).ToList();
            cases.Add(new Case(circuit, roots, sources));
        }
        return cases;
    }

    private Counts Run(List<Case> cases, string label)
    {
        var watch = Stopwatch.StartNew();
        var (programs, evaluations, values, gatherReads, indexReads, nanValues, negativeZeros) = (0, 0, 0, 0, 0, 0, 0);
        for (var circuitIndex = 0; circuitIndex < cases.Count; circuitIndex++)
        {
            var (circuit, roots, sources) = cases[circuitIndex];
            foreach (var (modeLabel, compile) in Modes)
            {
                var program = compile(circuit, roots);
                var programCounts = TensorChecks.ProgramCounts(program);
                gatherReads += programCounts.GatherReads;
                indexReads += programCounts.IndexReads;
                var stage = CompiledStage.Compile(circuit, program);
                var reference = circuit.CreateTensorProgramEvaluator(program);
                var actual = new float[program.Roots.Count];
                foreach (var source in sources)
                {
                    var expected = reference.Evaluate(source);
                    stage.Evaluate(source, actual);
                    for (var root = 0; root < actual.Length; root++)
                    {
                        if (!Js.ObjectIs(expected[root], actual[root]))
                        {
                            throw new Xunit.Sdk.XunitException(
                                $"{label}: circuit {circuitIndex}, {modeLabel}, root {root}: expected {expected[root]:R}, got {actual[root]:R}");
                        }
                        if (float.IsNaN(actual[root])) nanValues++;
                        if (actual[root] == 0 && float.IsNegative(actual[root])) negativeZeros++;
                    }
                    evaluations++;
                    values += actual.Length;
                }
                programs++;
            }
        }
        watch.Stop();
        output.WriteLine(
            $"[compiled stage] {label}: programs={programs} evaluations={evaluations} values={values} gatherReads={gatherReads} "
            + $"indexReads={indexReads} NaN={nanValues} negativeZeros={negativeZeros} time={watch.Elapsed.TotalSeconds:F1} s");
        return new Counts(programs, evaluations, values, gatherReads, indexReads, nanValues, negativeZeros);
    }
}
