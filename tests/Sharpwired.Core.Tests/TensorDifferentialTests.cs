// Handwired source: no single Handwired test. Spec item R2.4: seeded random programs give equal
// values in the reference evaluator and in the tensor evaluator. The compile modes are the 5 modes of
// checkEveryCompileMode in tests/runtime/index-read.test.ts. The comparison follows Decision 1 of the
// phase 2 plan. The modes without gather lowering use Object.is. The modes with gather lowering use
// sameNumericValue of tests/runtime/gather-lowering.test.ts. The reason is that a gather can change
// the sign of an exactly zero result (docs/execution-model.md, "One-hot groups and gathers"). The
// generator is in RandomPrograms.cs.

using Xunit.Abstractions;
using static Sharpwired.Core.Tests.RandomPrograms;

namespace Sharpwired.Core.Tests;

public class TensorDifferentialTests(ITestOutputHelper output)
{
    // The 5 compile modes of index-read.test.ts. LowersGathers selects the comparison.
    private static readonly (string Label, bool LowersGathers, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] Modes =
    [
        ("gather lowering, build-time depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("gather lowering, recomputed depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("no gather lowering, recomputed depths", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true)),
        ("every wire as a root", true, (circuit, _) => circuit.CompileTensorProgram()),
    ];

    // R2.4. 600 seeded circuits of 4 to 12 source cells and 20 to 60 builder calls. Each circuit has
    // 4 source tables and 1 to 5 roots. Each compile mode must give the reference value on each
    // wire of wireOrder, and both reusable evaluators must give the reference value on each root.
    // ValidateTensorEvaluation also runs on each evaluation with no NaN reference value.
    [Fact]
    public void Seeded_random_programs_give_the_reference_value_on_every_live_wire_in_every_compile_mode()
    {
        var random = Js.CreateRandom(0x2d1ff);
        const int CircuitCount = 600;
        const int SourcesPerCircuit = 4;
        var comparedWires = 0;
        var gatherReads = 0;
        var indexReads = 0;
        var nanWires = 0;
        var negativeZeroWires = 0;
        var gatherSignDifferences = 0;
        var tensorEvaluationChecks = 0;
        var operationKinds = new int[Enum.GetValues<OperationKind>().Length];
        // The rounding points of Compare show which path each comparison took: IntegerBit for the
        // grid path, SignCascade for the sign cascade.
        var roundingPointKinds = new int[Enum.GetValues<RoundingPointKind>().Length];
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];

        for (var circuitIndex = 0; circuitIndex < CircuitCount; circuitIndex++)
        {
            var (circuit, cells, groups, wires) = BuildCircuit(random, builderCalls);
            foreach (var operation in circuit.Operations) operationKinds[(int)operation.Kind]++;
            foreach (var point in circuit.RoundingPoints()) roundingPointKinds[(int)point.Kind]++;
            var operationWires = wires.Where(wire => wire.Position >= circuit.SourceSize).Distinct().ToList();
            var rootCount = 1 + RandomInteger(random, 5);
            var roots = Enumerable.Range(0, rootCount).Select(_ => operationWires[RandomInteger(random, operationWires.Count)]).ToList();
            var sources = Enumerable.Range(0, SourcesPerCircuit).Select(_ => CreateSource(random, cells, groups)).ToArray();

            circuit.ValidateCircuit();
            var references = sources.Select(source => circuit.EvaluateReference(source)).ToArray();
            foreach (var reference in references) circuit.ValidateOneHotGroups(reference);
            foreach (var (label, lowersGathers, compile) in Modes)
            {
                var program = compile(circuit, roots);
                circuit.ValidateTensorProgram(program);
                var counts = TensorChecks.ProgramCounts(program);
                if (!lowersGathers) Assert.Equal(0, counts.GatherReads);
                gatherReads += counts.GatherReads;
                indexReads += counts.IndexReads;
                circuit.TensorProgram = program;
                for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    var reference = references[sourceIndex];
                    var tensor = circuit.EvaluateTensor(sources[sourceIndex], program);
                    var hasNaN = false;
                    foreach (var wire in program.WireOrder)
                    {
                        var expected = reference[wire];
                        var actual = tensor[wire];
                        var agrees = lowersGathers ? Js.SameNumericValue(expected, actual) : Js.ObjectIs(expected, actual);
                        if (!agrees)
                        {
                            Assert.Fail(
                                $"circuit {circuitIndex}, source {sourceIndex}, {label}: wire {wire} got {actual:R}, reference {expected:R}");
                        }
                        if (lowersGathers && !Js.ObjectIs(expected, actual)) gatherSignDifferences++;
                        if (float.IsNaN(expected))
                        {
                            hasNaN = true;
                            nanWires++;
                        }
                        if (expected == 0 && float.IsNegative(expected)) negativeZeroWires++;
                        comparedWires++;
                    }
                    if (!hasNaN)
                    {
                        circuit.ValidateTensorEvaluation(sources[sourceIndex], tensor);
                        tensorEvaluationChecks++;
                    }
                }
            }
            circuit.TensorProgram = null;

            // Both reusable evaluators compile with gather lowering.
            foreach (var recomputeDepths in new[] { false, true })
            {
                var evaluator = circuit.CreateTensorEvaluator(roots, recomputeDepths);
                for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    var outputs = evaluator.Evaluate(sources[sourceIndex]);
                    for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
                    {
                        var expected = references[sourceIndex][roots[rootIndex].Position];
                        if (!Js.SameNumericValue(expected, outputs[rootIndex]))
                        {
                            Assert.Fail(
                                $"circuit {circuitIndex}, source {sourceIndex}, reusable evaluator (recomputeDepths {recomputeDepths}): "
                                + $"root {roots[rootIndex]} got {outputs[rootIndex]:R}, reference {expected:R}");
                        }
                        comparedWires++;
                    }
                }
            }
        }

        var kindCounts = string.Join(" ", Enum.GetValues<OperationKind>().Select(kind => $"{kind}={operationKinds[(int)kind]}"));
        var pointCounts = string.Join(" ", Enum.GetValues<RoundingPointKind>().Select(kind => $"{kind}={roundingPointKinds[(int)kind]}"));
        var callCounts = string.Join(" ", Enum.GetValues<BuilderCall>().Select(call => $"{call}={builderCalls[(int)call]}"));
        output.WriteLine(
            $"[differential] circuits={CircuitCount} comparedWires={comparedWires} gatherReads={gatherReads} indexReads={indexReads}"
            + $" nanWires={nanWires} negativeZeroWires={negativeZeroWires} gatherSignDifferences={gatherSignDifferences}"
            + $" tensorEvaluationChecks={tensorEvaluationChecks}");
        output.WriteLine($"[differential] operations: {kindCounts}");
        output.WriteLine($"[differential] rounding points: {pointCounts}");
        output.WriteLine($"[differential] builder calls: {callCounts}");

        // The coverage floors are half of the counts of the first run. The counts are comparedWires
        // 1,213,156, gatherReads 2,724, indexReads 2,481, nanWires 574, negativeZeroWires 24,363 and
        // tensorEvaluationChecks 11,883. The operation counts are Affine 66,034, Reglu 72,801,
        // Boolean 8,333 and Index 1,617. The rounding point counts are IntegerBit 1,445 and
        // SignCascade 52,042. Each other builder call ran at least 1,188 times. GroupMaskedSum counts
        // only the calls that build an operation: 795.
        // gatherSignDifferences counts the wires where a gather gives the other sign of zero. The
        // measured count is 0. A gather can change the sign only of a zero result, through the zero
        // terms that the dense sum adds and the gather skips. These random circuits made no such
        // result, so no floor applies.
        Assert.True(comparedWires >= 606_578, $"comparedWires {comparedWires}");
        Assert.True(gatherReads >= 1_362, $"gatherReads {gatherReads}");
        Assert.True(indexReads >= 1_240, $"indexReads {indexReads}");
        Assert.True(nanWires >= 287, $"nanWires {nanWires}");
        Assert.True(negativeZeroWires >= 12_181, $"negativeZeroWires {negativeZeroWires}");
        Assert.True(tensorEvaluationChecks >= 5_941, $"tensorEvaluationChecks {tensorEvaluationChecks}");
        Assert.True(operationKinds[(int)OperationKind.Affine] >= 33_017, $"Affine {operationKinds[(int)OperationKind.Affine]}");
        Assert.True(operationKinds[(int)OperationKind.Reglu] >= 36_400, $"Reglu {operationKinds[(int)OperationKind.Reglu]}");
        Assert.True(operationKinds[(int)OperationKind.Boolean] >= 4_166, $"Boolean {operationKinds[(int)OperationKind.Boolean]}");
        Assert.True(operationKinds[(int)OperationKind.Index] >= 808, $"Index {operationKinds[(int)OperationKind.Index]}");
        Assert.True(roundingPointKinds[(int)RoundingPointKind.IntegerBit] >= 722, $"IntegerBit {roundingPointKinds[(int)RoundingPointKind.IntegerBit]}");
        Assert.True(roundingPointKinds[(int)RoundingPointKind.SignCascade] >= 26_021, $"SignCascade {roundingPointKinds[(int)RoundingPointKind.SignCascade]}");
        foreach (var call in Enum.GetValues<BuilderCall>().Where(call => call != BuilderCall.GroupMaskedSum))
        {
            Assert.True(builderCalls[(int)call] >= 594, $"builder call {call}: {builderCalls[(int)call]}");
        }
        Assert.True(builderCalls[(int)BuilderCall.GroupMaskedSum] >= 397, $"builder call GroupMaskedSum: {builderCalls[(int)BuilderCall.GroupMaskedSum]}");
    }
}
