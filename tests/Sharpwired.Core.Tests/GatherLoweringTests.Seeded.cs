// Handwired source: tests/runtime/gather-lowering.test.ts, the block 'seeded circuits'. The test keeps
// the Handwired seed, the 3,000 circuits, the order of the random calls and the coverage floors. It
// writes the counts with ITestOutputHelper, where Handwired calls console.log.

using Xunit.Abstractions;

namespace Sharpwired.Core.Tests;

public partial class GatherLoweringTests(ITestOutputHelper output)
{
    private enum GroupKind
    {
        DeclaredBefore,
        DeclaredAfter,
        Argmax,
        FirstActive,
    }

    // One built group. ControlStart and ControlCount give the source cells that the sampler writes
    // to steer the group.
    private sealed record BuiltGroup(GroupKind Kind, IReadOnlyList<Wire> Members, bool AllowAllZero, int ControlStart, int ControlCount);

    private static readonly double[] SeededExtremeValues =
    [
        3.4028234663852886e38,
        -3.4028234663852886e38,
        1.1754943508222875e-38,
        1.401298464324817e-45,
        -0.0,
        0,
        16_777_216,
        -16_777_217,
        2.5,
        -0.1,
    ];

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);

    // 'seeded circuits' / 'seeded circuits with gathers match the reference on every wire'.
    // Thousands of seeded circuits with one-hot groups of each origin (source bits declared before or
    // after the reads, Argmax, FirstActive), masked reads in each orientation that Multiply builds,
    // chained gathers, deduplicated reads, decoy sums that must not lower, and downstream consumers.
    // Each wire of each circuit is compared across the reference evaluator, the program with gather
    // lowering, and the program without gather lowering.
    [Fact]
    public void Seeded_circuits_with_gathers_match_the_reference_on_every_wire()
    {
        var random = Js.CreateRandom(0x6a7 << 12 | 0x3e5);
        const int CircuitCount = 3_000;
        const int SamplesPerCircuit = 3;
        var totalReads = 0;
        var totalDecoys = 0;
        var chainedReads = 0;
        var chainedCandidatePicks = 0;
        var pairFormCircuits = 0;
        var allowAllZeroGroups = 0;
        var comparedWires = 0;

        for (var circuitIndex = 0; circuitIndex < CircuitCount; circuitIndex++)
        {
            var groupCount = 1 + RandomInteger(random, 2);
            var groupPlans = new List<(GroupKind Kind, int Size)>();
            for (var group = 0; group < groupCount; group++)
            {
                var kind = (GroupKind)RandomInteger(random, 4);
                var size = RandomInteger(random, 20) == 0 ? 17 : 1 + RandomInteger(random, 9);
                groupPlans.Add((kind, size));
            }

            // The source cells: for each group, size control cells (bits or scores), then the value
            // pools: nonnegative, signed and extreme cells, then the unit cell.
            const int NonnegCount = 6;
            const int SignedCount = 6;
            const int ExtremeCount = 4;
            var sourceSize = 0;
            var controlStarts = new List<int>();
            foreach (var plan in groupPlans)
            {
                controlStarts.Add(sourceSize);
                sourceSize += plan.Size;
            }
            var nonnegStart = sourceSize;
            sourceSize += NonnegCount;
            var signedStart = sourceSize;
            sourceSize += SignedCount;
            var extremeStart = sourceSize;
            sourceSize += ExtremeCount;
            var unitCell = sourceSize;
            sourceSize += 1;

            var circuit = new Circuit(sourceSize);
            var unitWire = circuit.Source(unitCell);
            for (var cell = 0; cell < NonnegCount; cell++) circuit.AssumeRange(circuit.Source(nonnegStart + cell), 0, 1e6);
            for (var cell = 0; cell < SignedCount; cell++) circuit.AssumeRange(circuit.Source(signedStart + cell), -1e6, 1e6);
            for (var cell = 0; cell < ExtremeCount; cell++)
            {
                circuit.AssumeRange(circuit.Source(extremeStart + cell), -3.4028234663852886e38, 3.4028234663852886e38);
            }
            circuit.AssumeRange(unitWire, 1, 1);

            // Build the groups. A DeclaredAfter group declares itself after the reads, so Multiply
            // sees bits of unknown range and builds its mixed-sign form for signed values.
            var groups = new List<BuiltGroup>();
            var lateDeclarations = new List<(IReadOnlyList<Wire> Members, bool AllowAllZero)>();
            for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                var plan = groupPlans[groupIndex];
                var controlStart = controlStarts[groupIndex];
                var controls = Enumerable.Range(controlStart, plan.Size).Select(circuit.Source).ToArray();
                if (plan.Kind == GroupKind.Argmax)
                {
                    foreach (var control in controls) circuit.AssumeRange(control, 0, 1);
                    var winner = circuit.Argmax(controls, unitWire);
                    groups.Add(new BuiltGroup(plan.Kind, winner.Selected, false, controlStart, plan.Size));
                }
                else if (plan.Kind == GroupKind.FirstActive)
                {
                    foreach (var control in controls) circuit.AssumeRange(control, 0, 1);
                    // FirstActive declares its selection only for proven exact bits, so the candidates
                    // go through boolean atoms first. The sampler writes exact 0 or 1 into the cells.
                    var selected = circuit.FirstActive(unitWire, [.. controls.Select(circuit.Boolean)]);
                    Assert.True(
                        circuit.OneHotGroups.Any(group => group.Members.SequenceEqual(selected)),
                        "first-active selection over proven bits must declare its group");
                    allowAllZeroGroups++;
                    groups.Add(new BuiltGroup(plan.Kind, selected, true, controlStart, plan.Size));
                }
                else
                {
                    var allowAllZero = RandomInteger(random, 3) == 0;
                    if (plan.Kind == GroupKind.DeclaredBefore) circuit.AssumeOneHot(controls, allowAllZero);
                    else lateDeclarations.Add((controls, allowAllZero));
                    if (allowAllZero) allowAllZeroGroups++;
                    groups.Add(new BuiltGroup(plan.Kind, controls, allowAllZero, controlStart, plan.Size));
                }
            }

            // The value pool: source cells and derived wires. The builder copies small derived affines
            // into the product ReGLUs, so the recognition must find them through the expression
            // cache. Derived values stay well inside FP32, so each candidate is finite, as the
            // lowering contract requires. The extreme cells are used directly.
            var valuePool = new List<Wire>();
            for (var cell = 0; cell < NonnegCount; cell++) valuePool.Add(circuit.Source(nonnegStart + cell));
            for (var cell = 0; cell < SignedCount; cell++) valuePool.Add(circuit.Source(signedStart + cell));
            for (var cell = 0; cell < ExtremeCount; cell++) valuePool.Add(circuit.Source(extremeStart + cell));
            for (var derived = 0; derived < 3; derived++)
            {
                valuePool.Add(circuit.Affine(
                    [
                        (circuit.Source(nonnegStart + RandomInteger(random, NonnegCount)), 0.5),
                        (circuit.Source(signedStart + RandomInteger(random, SignedCount)), 0.25),
                    ],
                    RandomInteger(random, 5) - 2));
            }
            valuePool.Add(circuit.Multiply(
                circuit.Source(nonnegStart + RandomInteger(random, NonnegCount)),
                circuit.Source(signedStart + RandomInteger(random, SignedCount))));

            // readWires is a JavaScript Set: the list keeps the insertion order.
            var readWireSet = new HashSet<Wire>();
            var readWires = new List<Wire>();
            var decoyWires = new HashSet<Wire>();
            Wire PickValue() => valuePool[RandomInteger(random, valuePool.Count)];
            var usedPairForm = false;
            foreach (var group in groups)
            {
                var readCount = 1 + RandomInteger(random, 3);
                for (var read = 0; read < readCount; read++)
                {
                    var bias = RandomInteger(random, 3) == 0 ? RandomInteger(random, 7) - 3 : 0;
                    var candidates = new Wire[group.Members.Count];
                    for (var position = 0; position < candidates.Length; position++) candidates[position] = PickValue();
                    // Count a chained read by use: a candidate that is an earlier read makes a gather
                    // that feeds a gather.
                    var pickedReadCandidates = candidates.Count(readWireSet.Contains);
                    chainedCandidatePicks += pickedReadCandidates;
                    if (pickedReadCandidates > 0) chainedReads++;
                    var sum = circuit.Affine(MaskedTerms(circuit, group.Members, candidates), bias);
                    if (group.Kind == GroupKind.DeclaredAfter && candidates.Any(candidate => !circuit.IsNonNegative(candidate)))
                    {
                        usedPairForm = true;
                    }
                    // A sum of one term with no bias is an alias of the product. Only a sum operation
                    // can lower.
                    if (sum.Position >= circuit.SourceSize && circuit.Operations[sum.Position - circuit.SourceSize].Kind == OperationKind.Affine)
                    {
                        if (readWireSet.Add(sum)) readWires.Add(sum);
                    }
                    // Some reads join the value pool, where later picks can chain on them. Only sums
                    // with finite stored ranges can join: a read built before the declaration of its
                    // group has a range from bits with no bound, and the finiteness rule refuses such
                    // a wire as the candidate of a later gather.
                    var sumRange = circuit.Ranges[sum.Position];
                    if (RandomInteger(random, 3) == 0 && double.IsFinite(sumRange.Minimum) && double.IsFinite(sumRange.Maximum))
                    {
                        valuePool.Add(sum);
                    }
                }

                // One decoy for each group: a sum that is almost the masked read but must stay dense.
                // Its value is also compared.
                var decoyKind = RandomInteger(random, 3);
                if (group.Members.Count >= 2)
                {
                    var candidates = new Wire[group.Members.Count];
                    for (var position = 0; position < candidates.Length; position++) candidates[position] = PickValue();
                    var terms = MaskedTerms(circuit, group.Members, candidates).ToList();
                    if (decoyKind == 0)
                    {
                        var index = RandomInteger(random, terms.Count);
                        terms[index] = terms[index] with { Coefficient = 0.5 };
                    }
                    else if (decoyKind == 1)
                    {
                        terms.RemoveAt(terms.Count - 1);
                    }
                    else
                    {
                        terms.Add(new Term(PickValue(), 1));
                    }
                    decoyWires.Add(circuit.Affine(terms));
                    totalDecoys++;
                }
            }
            foreach (var (members, allowAllZero) in lateDeclarations)
            {
                circuit.AssumeOneHot(members, allowAllZero, allowExistingReaders: true);
            }
            if (usedPairForm) pairFormCircuits++;

            // Downstream consumers of the reads keep the gathers in real work.
            if (readWires.Count >= 2)
            {
                circuit.Maximum(readWires[0], readWires[1]);
                circuit.Minimum(readWires[0], readWires[^1]);
            }

            circuit.ValidateCircuit();
            var lowered = circuit.CompileTensorProgram(null, lowerGathers: true);
            var plain = circuit.CompileTensorProgram(null, lowerGathers: false);
            circuit.ValidateTensorProgram(lowered);
            circuit.ValidateTensorProgram(plain);
            var statistics = TensorChecks.GatherStatistics(lowered);
            // Each built read lowers, and no decoy lowers. Other operations can also lower (a
            // mixed-sign multiply against a group of size 1 is itself a complete masked read), and
            // the comparison of each wire below checks them.
            var loweredWireSet = TensorChecks.LoweredWires(lowered);
            foreach (var read in readWires)
            {
                Assert.True(loweredWireSet.Contains(read.Position), $"circuit {circuitIndex}: read wire {read} did not lower");
            }
            foreach (var decoy in decoyWires)
            {
                Assert.False(loweredWireSet.Contains(decoy.Position), $"circuit {circuitIndex}: decoy wire {decoy} lowered");
            }
            Assert.Equal(0, TensorChecks.GatherStatistics(plain).ReadCount);
            totalReads += statistics.ReadCount;

            for (var sample = 0; sample < SamplesPerCircuit; sample++)
            {
                var source = new float[sourceSize];
                source[unitCell] = 1;
                foreach (var group in groups)
                {
                    if (group.Kind == GroupKind.Argmax)
                    {
                        for (var cell = 0; cell < group.ControlCount; cell++) source[group.ControlStart + cell] = (float)random();
                    }
                    else if (group.Kind == GroupKind.FirstActive)
                    {
                        for (var cell = 0; cell < group.ControlCount; cell++) source[group.ControlStart + cell] = RandomInteger(random, 3) == 0 ? 1 : 0;
                    }
                    else
                    {
                        var inactive = group.AllowAllZero && RandomInteger(random, 4) == 0;
                        if (!inactive) source[group.ControlStart + RandomInteger(random, group.ControlCount)] = 1;
                    }
                }
                for (var cell = 0; cell < NonnegCount; cell++) source[nonnegStart + cell] = (float)(random() * 1e6);
                for (var cell = 0; cell < SignedCount; cell++) source[signedStart + cell] = (float)(random() * 2e6 - 1e6);
                for (var cell = 0; cell < ExtremeCount; cell++)
                {
                    source[extremeStart + cell] = (float)SeededExtremeValues[RandomInteger(random, SeededExtremeValues.Length)];
                }

                var reference = circuit.EvaluateReference(source);
                circuit.ValidateOneHotGroups(reference);
                circuit.TensorProgram = lowered;
                var tensorLowered = circuit.EvaluateTensor(source);
                circuit.TensorProgram = plain;
                var tensorPlain = circuit.EvaluateTensor(source);
                for (var wire = 0; wire < reference.Length; wire++)
                {
                    comparedWires++;
                    if (!Js.SameNumericValue(tensorLowered[wire], reference[wire]))
                    {
                        Assert.Fail($"circuit {circuitIndex} sample {sample} wire {wire}: lowered {tensorLowered[wire]:R} vs reference {reference[wire]:R}");
                    }
                    if (!Js.SameNumericValue(tensorPlain[wire], reference[wire]))
                    {
                        Assert.Fail($"circuit {circuitIndex} sample {sample} wire {wire}: plain {tensorPlain[wire]:R} vs reference {reference[wire]:R}");
                    }
                }
            }
        }

        // The test must cover what it states. A chained read is a read with at least one candidate
        // that is an earlier read, and the picks count each such candidate.
        Assert.True(totalReads > 4_000, $"totalReads {totalReads}");
        Assert.True(totalDecoys > 1_500, $"totalDecoys {totalDecoys}");
        Assert.True(chainedReads > 300, $"chainedReads {chainedReads}");
        Assert.True(chainedCandidatePicks > 400, $"chainedCandidatePicks {chainedCandidatePicks}");
        Assert.True(pairFormCircuits > 100, $"pairFormCircuits {pairFormCircuits}");
        Assert.True(allowAllZeroGroups > 300, $"allowAllZeroGroups {allowAllZeroGroups}");
        Assert.True(comparedWires > 500_000, $"comparedWires {comparedWires}");
        output.WriteLine(
            $"[differential] circuits={CircuitCount} reads={totalReads} decoys={totalDecoys}"
            + $" chainedReads={chainedReads} chainedCandidatePicks={chainedCandidatePicks}"
            + $" pairFormCircuits={pairFormCircuits}"
            + $" allowAllZeroGroups={allowAllZeroGroups} comparedWires={comparedWires}");
    }
}
