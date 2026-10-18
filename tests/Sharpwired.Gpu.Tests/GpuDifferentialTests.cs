// Handwired source: no single Handwired test. TensorDifferentialTests (spec item R2.4) on the GPU. The
// seeded programs of RandomPrograms run through GpuEvaluator on each adapter, with both writers. Each
// compared value equals the reference evaluator. The compile modes are the 5 modes of the phase 2
// differential and one mode of question 6 of the phase 5 plan. That mode is "no gather lowering, every
// wire as a root". The comparison rule is the rule of question 6. The f32 writer without gather lowering
// uses Object.is. Gather lowering and the integer writer use sameNumericValue. On a device that
// flushes subnormal values, the test does not compare an evaluation with a subnormal value, and counts it
// (decision 4). The open-range variant (fix round 2 of the review of task 3) gives about 30% of the
// source cells no range. It writes no subnormal source value. So NaN and the infinities reach more rows, and
// the subnormal rule excludes fewer evaluations.

using System.Diagnostics;
using Sharpwired.Core;
using Xunit.Abstractions;
using static Sharpwired.Core.Tests.RandomPrograms;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class GpuDifferentialTests(GpuFixture fixture, ITestOutputHelper output)
{
    private const int CircuitCount = 200;
    private const int SourcesPerCircuit = 64;

    // The circuits of each seed of the open-range variant.
    private const int OpenRangeCircuitCount = 80;

    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    public static TheoryData<int, GpuWriter> OpenRangeSeeds => new()
    {
        { 0x0be7a, GpuWriter.Float32 },
        { 0x0be7a, GpuWriter.Integer },
        { 0x51ce5, GpuWriter.Float32 },
        { 0x51ce5, GpuWriter.Integer },
    };

    // The 5 compile modes of TensorDifferentialTests, then the mode of question 6 with no gather
    // lowering and each wire as a root. LowersGathers selects the comparison of the f32 writer.
    private static readonly (string Label, bool LowersGathers, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] Modes =
    [
        ("gather lowering, build-time depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("gather lowering, recomputed depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("no gather lowering, recomputed depths", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true)),
        ("every wire as a root", true, (circuit, _) => circuit.CompileTensorProgram()),
        ("no gather lowering, every wire as a root", false, (circuit, _) => circuit.CompileTensorProgram(lowerGathers: false)),
    ];

    private sealed record Case(Circuit Circuit, List<Wire> Roots, float[] Sources);

    // The counts of one run on one device.
    private sealed record RunCounts(
        int Programs, int ComparedEvaluations, int ComparedValues, int ExcludedEvaluations, int GatherReads, int IndexReads,
        int FloatRows, int WholeRows, int BitRows, int[] ModeCounts);

    // R5.1, R5.2, R5.3 on seeded programs. 200 circuits of RandomPrograms, 64 source tables each, in the
    // 6 compile modes. Each root of each compared table equals the reference value. In the two modes
    // with each wire as a root, each live wire is a root.
    [Theory]
    [MemberData(nameof(Writers))]
    public void Seeded_random_programs_on_the_GPU_give_the_reference_value_on_each_root_in_each_compile_mode(GpuWriter writer)
    {
        var cases = Cases(0x9d1ff, CircuitCount, openRangeShare: 0, subnormals: true);
        fixture.ForEachDevice(output, device =>
        {
            var counts = Run(device, cases, writer, "gpu differential");

            // The floors are half of the counts of the first run on the Apple M5 Pro (Metal), which
            // flushes subnormal values. Each writer gave comparedEvaluations 55,830, comparedValues
            // 4,950,124, gatherReads 896 and indexReads 1,244. After the phase 5 fix wave (proven bits for a masked integer gather),
            // the integer writer wrote 22,513 i32 rows and 35,390 u32 rows. Before the fix, the counts
            // were 22,578 and 35,400. Each writer excluded 20,970 evaluations by the subnormal rule,
            // because this test keeps the subnormal source values of RandomPrograms. Each mode must
            // compare at least a quarter of its tables, so that the subnormal rule cannot exclude a full
            // mode. One writer took 4.6 s to 54 s; the cause of the slow runs is unknown. Each run is
            // below the limit of 60 s of question 6.
            Assert.Equal(CircuitCount * Modes.Length, counts.Programs);
            Assert.True(counts.ComparedEvaluations >= 27_915, $"comparedEvaluations {counts.ComparedEvaluations}");
            Assert.True(counts.ComparedValues >= 2_475_062, $"comparedValues {counts.ComparedValues}");
            Assert.True(counts.GatherReads >= 448, $"gatherReads {counts.GatherReads}");
            Assert.True(counts.IndexReads >= 622, $"indexReads {counts.IndexReads}");
            AssertModeFloor(counts, CircuitCount);
            if (writer == GpuWriter.Integer)
            {
                Assert.True(counts.WholeRows >= 11_289, $"wholeRows {counts.WholeRows}");
                Assert.True(counts.BitRows >= 17_700, $"bitRows {counts.BitRows}");
            }
        });
    }

    // The open-range variant: 80 circuits for each of 2 seeds, about 30% of the source cells with
    // no range, and no subnormal source value. With the two fixes of the review of task 3 reverted (keep
    // around f32(w), and the finite-cone rule), this test fails (fix round 2).
    [Theory]
    [MemberData(nameof(OpenRangeSeeds))]
    public void Seeded_programs_with_open_ranges_on_the_GPU_give_the_reference_value(int seed, GpuWriter writer)
    {
        var cases = Cases(seed, OpenRangeCircuitCount, openRangeShare: 0.7, subnormals: false);
        var cells = cases.Sum(item => item.Circuit.SourceSize);
        var open = cases.Sum(item => Enumerable.Range(0, item.Circuit.SourceSize).Count(cell => !double.IsFinite(item.Circuit.Ranges[cell].Minimum) || !double.IsFinite(item.Circuit.Ranges[cell].Maximum)));
        output.WriteLine($"seed {seed:x}: {open} of {cells} source cells have no range");
        fixture.ForEachDevice(output, device =>
        {
            var counts = Run(device, cases, writer, $"gpu differential, open ranges, seed {seed:x}");

            // The floors are half of the counts of the first run on the Apple M5 Pro (Metal). Each writer
            // compared 30,720 evaluations of each seed, and the subnormal rule excluded none. Seed be7a:
            // 221 of 650 source cells with no range, comparedValues 2,899,584, gatherReads 295,
            // indexReads 984, and 10 gather roots. A gather root is a root that the gather rule of
            // GpuChecks.Compare compared with TensorEvaluator. Seed 51ce5: 161 of 596 cells, 2,989,952,
            // 351, 740, and 0 roots. One writer took 2 s to 24 s for one seed, and at most 41 s for both
            // seeds.
            Assert.Equal(OpenRangeCircuitCount * Modes.Length, counts.Programs);
            var floors = seed == 0x0be7a ? (Values: 1_449_792, Gathers: 147, Indices: 492) : (Values: 1_494_976, Gathers: 175, Indices: 370);
            Assert.True(counts.ComparedEvaluations >= 15_360, $"comparedEvaluations {counts.ComparedEvaluations}");
            Assert.True(counts.ComparedValues >= floors.Values, $"comparedValues {counts.ComparedValues}");
            Assert.True(counts.GatherReads >= floors.Gathers, $"gatherReads {counts.GatherReads}");
            Assert.True(counts.IndexReads >= floors.Indices, $"indexReads {counts.IndexReads}");
            Assert.True(open * 4 >= cells, $"{open} of {cells} source cells have no range");
            AssertModeFloor(counts, OpenRangeCircuitCount);
        });
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
            var sources = Enumerable.Range(0, SourcesPerCircuit).SelectMany(_ => CreateSource(random, cells, groups, subnormals)).ToArray();
            cases.Add(new Case(circuit, roots, sources));
        }
        return cases;
    }

    // Each mode must compare at least a quarter of its tables, so that the subnormal rule cannot exclude
    // a full mode.
    private static void AssertModeFloor(RunCounts counts, int circuitCount)
    {
        foreach (var (count, index) in counts.ModeCounts.Select((count, index) => (count, index)))
        {
            Assert.True(count >= circuitCount * SourcesPerCircuit / 4, $"{Modes[index].Label}: {count} evaluations compared");
        }
    }

    private RunCounts Run(GpuDevice device, List<Case> cases, GpuWriter writer, string label)
    {
        var watch = Stopwatch.StartNew();
        var programs = 0;
        var comparedEvaluations = 0;
        var comparedValues = 0;
        var excludedEvaluations = 0;
        var gatherNaN = new System.Runtime.CompilerServices.StrongBox<int>(0);
        var gatherReads = 0;
        var indexReads = 0;
        var (floatRows, wholeRows, bitRows) = (0, 0, 0);
        var modeCounts = new int[Modes.Length];
        for (var circuitIndex = 0; circuitIndex < cases.Count; circuitIndex++)
        {
            var (circuit, roots, sources) = cases[circuitIndex];
            for (var mode = 0; mode < Modes.Length; mode++)
            {
                var (modeLabel, lowersGathers, compile) = Modes[mode];
                var program = compile(circuit, roots);
                if (!lowersGathers) Assert.Equal(0, TensorChecks.ProgramCounts(program).GatherReads);
                var counts = TensorChecks.ProgramCounts(program);
                gatherReads += counts.GatherReads;
                indexReads += counts.IndexReads;
                using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
                var outputs = evaluator.Evaluate(sources, SourcesPerCircuit);
                var sameNumericValue = lowersGathers || writer == GpuWriter.Integer;
                int compared;
                int excluded;
                try
                {
                    (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, SourcesPerCircuit, outputs, sameNumericValue, lowersGathers ? gatherNaN : null);
                }
                catch (Exception failure)
                {
                    throw new Xunit.Sdk.XunitException($"{label}: circuit {circuitIndex}, {modeLabel}, {writer}: {failure.Message}");
                }
                programs++;
                modeCounts[mode] += compared;
                comparedEvaluations += compared;
                comparedValues += compared * program.Roots.Count;
                excludedEvaluations += excluded;
                floatRows += evaluator.Module.Statistics.FloatRows;
                wholeRows += evaluator.Module.Statistics.WholeRows;
                bitRows += evaluator.Module.Statistics.BitRows;
            }
        }
        watch.Stop();
        output.WriteLine(
            $"[{label}] {writer}: circuits={cases.Count} programs={programs} comparedEvaluations={comparedEvaluations}"
            + $" comparedValues={comparedValues} excludedBySubnormalRule={excludedEvaluations} gatherNaNByTensorEvaluator={gatherNaN.Value} gatherReads={gatherReads}"
            + $" indexReads={indexReads} rows: {floatRows} f32, {wholeRows} i32, {bitRows} u32; {watch.Elapsed.TotalSeconds:F1} s");
        output.WriteLine($"[{label}] {writer}: compared evaluations by mode: "
            + string.Join(", ", Modes.Select((mode, index) => $"{mode.Label}={modeCounts[index]}")));
        return new RunCounts(programs, comparedEvaluations, comparedValues, excludedEvaluations, gatherReads, indexReads, floatRows, wholeRows, bitRows, modeCounts);
    }
}
