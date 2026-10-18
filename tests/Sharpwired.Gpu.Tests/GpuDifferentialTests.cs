// Handwired source: no single Handwired test. TensorDifferentialTests (spec item R2.4) on the GPU: the
// seeded programs of RandomPrograms run through GpuEvaluator on each adapter, with both writers, and
// each compared value equals the reference evaluator. The compile modes are the 5 modes of the phase 2
// differential and the mode "no gather lowering, every wire as a root" of question 6 of the phase 5
// plan. The comparison rule is the rule of question 6: Object.is for the f32 writer without gather
// lowering, sameNumericValue with gather lowering and for the integer writer. On a device that flushes
// subnormal values, an evaluation with a subnormal value is not compared and is counted (decision 4).

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

    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

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

    // R5.1, R5.2, R5.3 on seeded programs. 200 circuits of RandomPrograms, 64 source tables each, in the
    // 6 compile modes. Each root of each compared table equals the reference value. In the two modes
    // with each wire as a root, each live wire is a root.
    [Theory]
    [MemberData(nameof(Writers))]
    public void Seeded_random_programs_on_the_GPU_give_the_reference_value_on_each_root_in_each_compile_mode(GpuWriter writer)
    {
        var random = Js.CreateRandom(0x9d1ff);
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];
        var cases = new List<Case>();
        for (var circuitIndex = 0; circuitIndex < CircuitCount; circuitIndex++)
        {
            var (circuit, cells, groups, wires) = BuildCircuit(random, builderCalls);
            var operationWires = wires.Where(wire => wire.Position >= circuit.SourceSize).Distinct().ToList();
            var rootCount = 1 + RandomInteger(random, 5);
            var roots = Enumerable.Range(0, rootCount).Select(_ => operationWires[RandomInteger(random, operationWires.Count)]).ToList();
            var sources = Enumerable.Range(0, SourcesPerCircuit).SelectMany(_ => CreateSource(random, cells, groups)).ToArray();
            cases.Add(new Case(circuit, roots, sources));
        }

        fixture.ForEachDevice(output, device =>
        {
            var watch = Stopwatch.StartNew();
            var programs = 0;
            var comparedEvaluations = 0;
            var comparedValues = 0;
            var excludedEvaluations = 0;
            var gatherReads = 0;
            var indexReads = 0;
            var (floatRows, wholeRows, bitRows) = (0, 0, 0);
            var modeCounts = new int[Modes.Length];
            for (var circuitIndex = 0; circuitIndex < cases.Count; circuitIndex++)
            {
                var (circuit, roots, sources) = cases[circuitIndex];
                for (var mode = 0; mode < Modes.Length; mode++)
                {
                    var (label, lowersGathers, compile) = Modes[mode];
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
                        (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, SourcesPerCircuit, outputs, sameNumericValue);
                    }
                    catch (Exception failure)
                    {
                        throw new Xunit.Sdk.XunitException($"circuit {circuitIndex}, {label}, {writer}: {failure.Message}");
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
                $"[gpu differential] {writer}: circuits={CircuitCount} programs={programs} comparedEvaluations={comparedEvaluations}"
                + $" comparedValues={comparedValues} excludedBySubnormalRule={excludedEvaluations} gatherReads={gatherReads}"
                + $" indexReads={indexReads} rows: {floatRows} f32, {wholeRows} i32, {bitRows} u32; {watch.Elapsed.TotalSeconds:F1} s");
            output.WriteLine($"[gpu differential] {writer}: compared evaluations by mode: "
                + string.Join(", ", Modes.Select((mode, index) => $"{mode.Label}={modeCounts[index]}")));

            // The floors are half of the counts of the first run on the Apple M5 Pro (Metal), which
            // flushes subnormal values. Each writer gave comparedEvaluations 55,830, comparedValues
            // 4,950,124, gatherReads 896 and indexReads 1,244. The integer writer wrote 22,580 i32
            // rows and 35,406 u32 rows. Each writer excluded 20,970 evaluations by the subnormal rule,
            // because RandomPrograms writes subnormal source values. Each mode must compare at least a
            // quarter of its tables, so that the subnormal rule cannot exclude a full mode. One writer
            // took 48 s to 54 s in 2 runs and 4.6 s to 4.9 s in 4 runs. The cause of the slow runs was
            // not found. Each run is below the limit of 60 s of question 6.
            Assert.Equal(CircuitCount * Modes.Length, programs);
            Assert.True(comparedEvaluations >= 27_915, $"comparedEvaluations {comparedEvaluations}");
            Assert.True(comparedValues >= 2_475_062, $"comparedValues {comparedValues}");
            Assert.True(gatherReads >= 448, $"gatherReads {gatherReads}");
            Assert.True(indexReads >= 622, $"indexReads {indexReads}");
            foreach (var (count, index) in modeCounts.Select((count, index) => (count, index)))
            {
                Assert.True(count >= CircuitCount * SourcesPerCircuit / 4, $"{Modes[index].Label}: {count} evaluations compared");
            }
            if (writer == GpuWriter.Integer)
            {
                Assert.True(wholeRows >= 11_290, $"wholeRows {wholeRows}");
                Assert.True(bitRows >= 17_703, $"bitRows {bitRows}");
            }
        });
    }
}
