// Handwired source: tests/runtime/index-read.test.ts (the cases with inlineCircuit, emitArgmax,
// emitFirstActive and validateOneHotGroups). Each test name holds the Handwired test name.
// Handwired checkEveryCompileMode is TensorChecks.EveryCompileMode with CompileModes.IndexRead.

namespace Sharpwired.Core.Tests;

public partial class IndexReadTests
{
    // 'inlining carries index reads, and the inlined circuit agrees with the original'.
    [Fact]
    public void Inlining_carries_index_reads_and_the_inlined_circuit_agrees_with_the_original()
    {
        var child = new Circuit(5);
        child.AssumeRange(child.Source(0), 0, 3);
        for (var wire = 1; wire < 5; wire++) child.AssumeRange(Wire.FromPosition(wire), -10, 10);
        var doubled = Enumerable.Range(0, 4).Select(position => child.RoundAffine([(Wire.FromPosition(1 + position), 2)])).ToArray();
        var readSource = child.IndexRead(child.Source(0), [child.Source(1), child.Source(2), child.Source(3), child.Source(4)], OutOfRange.Zero);
        var readComputed = child.IndexReadAffine([(child.Source(0), 1)], 1, doubled, OutOfRange.Clamp, OutOfRange.Zero);
        var parent = new Circuit(5);
        int[] sourceWires = [4, 3, 2, 1, 0];
        var wireMap = parent.InlineCircuit(child, Wires(sourceWires));
        Assert.Equal(wireMap, parent.InlineCircuit(child, Wires(sourceWires)));
        var random = Js.CreateRandom(5);
        for (var trial = 0; trial < 50; trial++)
        {
            double[] childCells = [RandomInteger(random, 4), .. Enumerable.Range(0, 4).Select(_ => RandomInteger(random, 21) - 10.0)];
            var childSource = Source(childCells);
            var parentSource = new float[5];
            for (var childWire = 0; childWire < sourceWires.Length; childWire++) parentSource[sourceWires[childWire]] = childSource[childWire];
            var childValues = child.EvaluateReference(childSource);
            Wire[] parentRoots = [wireMap[readSource.Position], wireMap[readComputed.Position]];
            var parentValues = TensorChecks.EveryCompileMode(parent, parentRoots, [parentSource])[0];
            Js.Is(childValues[readSource.Position], parentValues[wireMap[readSource.Position].Position]);
            Js.Is(childValues[readComputed.Position], parentValues[wireMap[readComputed.Position].Position]);
        }
    }

    // 'recomputed depths: a lowered gather read stops waiting for the products it replaced'.
    [Fact]
    public void Recomputed_depths_a_lowered_gather_read_stops_waiting_for_the_products_it_replaced()
    {
        const int ScoreCount = 5;
        var circuit = new Circuit(2 * ScoreCount + 1);
        var unit = Wire.FromPosition(2 * ScoreCount);
        circuit.AssumeRange(unit, 1, 1);
        var scores = Wires(Enumerable.Range(0, ScoreCount));
        var values = Wires(Enumerable.Range(ScoreCount, ScoreCount));
        foreach (var score in scores) circuit.AssumeRange(score, 0, 1);
        foreach (var value in values) circuit.AssumeRange(value, -100, 100);
        var selected = circuit.Argmax(scores, unit).Selected;
        var selectedValue = circuit.RoundAffine([.. selected.Select((bit, item) => new Term(circuit.Multiply(bit, values[item]), 1))]);
        var downstream = circuit.RoundAffine([(selectedValue, 2)], 1);
        // The compiled program skips depths with no live operation, so the removed products only
        // cost a layer while another live operation shares their depth.
        var deepestBit = selected.Aggregate((deepest, bit) => circuit.Depths[bit.Position] > circuit.Depths[deepest.Position] ? bit : deepest);
        var bitCopy = circuit.RoundAffine([(deepestBit, 3)]);
        Wire[] roots = [downstream, bitCopy];
        var buildTimeProgram = circuit.CompileTensorProgram(roots);
        var buildTimeCounts = TensorChecks.ProgramCounts(buildTimeProgram);
        var recomputedCounts = TensorChecks.ProgramCounts(circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true));
        Assert.Equal(1, buildTimeCounts.GatherReads);
        Assert.Equal(buildTimeCounts.Layers - 1, recomputedCounts.Layers);
        Assert.Equal(buildTimeCounts.LiveOperations, recomputedCounts.LiveOperations);
        // Without bitCopy, the compiler already skips the empty depth, and both give the same layer count.
        Assert.Equal(
            TensorChecks.ProgramCounts(circuit.CompileTensorProgram([downstream])).Layers,
            TensorChecks.ProgramCounts(circuit.CompileTensorProgram([downstream], lowerGathers: true, recomputeDepths: true)).Layers);
        // The default is off.
        TensorChecks.AssertSameProgram(buildTimeProgram, circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: false));
        var random = Js.CreateRandom(11);
        var sources = Enumerable.Range(0, 40).Select(_ =>
        {
            var source = new float[circuit.SourceSize];
            foreach (var score in scores) source[score.Position] = RandomInteger(random, 5) / 4f;
            foreach (var value in values) source[value.Position] = (float)((random() * 2 - 1) * 100);
            source[unit.Position] = 1;
            return source;
        }).ToArray();
        TensorChecks.EveryCompileMode(circuit, [.. roots, selectedValue], sources);
    }

    // 'random circuits mixing index reads, products, selects and gathers agree in every compile mode'.
    [Fact]
    public void Random_circuits_mixing_index_reads_products_selects_and_gathers_read_the_plain_definition()
    {
        var random = Js.CreateRandom(0x51de);
        double[] coefficients = [1.0 / 3, -0.7, 2, -1, 12_345.6789, 0.1];
        var indexReadsChecked = 0;
        var loweredGatherReads = 0;
        for (var circuitIndex = 0; circuitIndex < 120; circuitIndex++)
        {
            var integerCellCount = 2 + RandomInteger(random, 3);
            var valueCellCount = 2 + RandomInteger(random, 4);
            var extremeCellCount = 1 + RandomInteger(random, 2);
            const int UnitCellCount = 3;
            var sourceSize = integerCellCount + valueCellCount + extremeCellCount + UnitCellCount + 1;
            var unit = Wire.FromPosition(sourceSize - 1);
            var circuit = new Circuit(sourceSize);
            const int IntegerLimit = 12;
            var integerWires = new List<Wire>();
            var finiteWires = new List<Wire>();
            var anyWires = new List<Wire>();
            var unitWires = new List<Wire>();
            var cell = 0;
            for (var item = 0; item < integerCellCount; item++, cell++)
            {
                circuit.AssumeRange(Wire.FromPosition(cell), -IntegerLimit, IntegerLimit);
                integerWires.Add(Wire.FromPosition(cell));
                finiteWires.Add(Wire.FromPosition(cell));
            }
            for (var item = 0; item < valueCellCount; item++, cell++)
            {
                circuit.AssumeRange(Wire.FromPosition(cell), -100, 100);
                finiteWires.Add(Wire.FromPosition(cell));
            }
            for (var item = 0; item < extremeCellCount; item++, cell++) anyWires.Add(Wire.FromPosition(cell));
            for (var item = 0; item < UnitCellCount; item++, cell++)
            {
                circuit.AssumeRange(Wire.FromPosition(cell), 0, 1);
                unitWires.Add(Wire.FromPosition(cell));
            }
            circuit.AssumeRange(unit, 1, 1);
            anyWires.AddRange(finiteWires);
            T Pick<T>(IReadOnlyList<T> items) => items[RandomInteger(random, items.Count)];
            // The plain definition of every index read, checked against the reference evaluator.
            var intents = new List<(Wire Wire, Wire IndexWire, double Bias, Wire[] Candidates, OutOfRange Below, OutOfRange Above)>();
            Wire ReadTable(Wire[] candidates)
            {
                var indexWire = Pick(integerWires);
                double bias = RandomInteger(random, 7) - 3;
                var below = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                var above = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                var wire = circuit.IndexReadAffine([(indexWire, 1)], bias, candidates, below, above);
                intents.Add((wire, indexWire, bias, candidates, below, above));
                return wire;
            }
            Wire[] PickMany(int count, List<Wire> pool) => [.. Enumerable.Range(0, count).Select(_ => Pick(pool))];
            var operationCount = 8 + RandomInteger(random, 12);
            for (var operation = 0; operation < operationCount; operation++)
            {
                var choice = RandomInteger(random, 7);
                if (choice == 0)
                {
                    var first = new Term(Pick(finiteWires), Pick(coefficients));
                    var second = new Term(Pick(finiteWires), Pick(coefficients));
                    anyWires.Add(circuit.Affine([first, second], Pick(coefficients)));
                }
                else if (choice == 1)
                {
                    var left = Pick(anyWires);
                    anyWires.Add(circuit.Multiply(left, Pick(anyWires)));
                }
                else if (choice == 2)
                {
                    var bit = circuit.Boolean(Pick(unitWires));
                    var off = Pick(finiteWires);
                    anyWires.Add(circuit.Select(bit, off, Pick(finiteWires)));
                }
                else if (choice == 3)
                {
                    // A table of integer wires reads an integer, which can index again.
                    var wire = ReadTable(PickMany(1 + RandomInteger(random, 8), integerWires));
                    integerWires.Add(wire);
                    finiteWires.Add(wire);
                    anyWires.Add(wire);
                }
                else if (choice == 4)
                {
                    anyWires.Add(ReadTable(PickMany(1 + RandomInteger(random, 10), anyWires)));
                }
                else if (choice == 5)
                {
                    var wire = ReadTable(PickMany(1 + RandomInteger(random, 10), finiteWires));
                    finiteWires.Add(wire);
                    anyWires.Add(wire);
                }
                else
                {
                    // An argmax selection whose masked sum reads finite wires, for example index reads.
                    var scores = PickMany(2 + RandomInteger(random, 3), unitWires);
                    var selected = circuit.Argmax(scores, unit).Selected;
                    var wire = circuit.RoundAffine([.. selected.Select(bit => new Term(circuit.Multiply(bit, Pick(finiteWires)), 1))]);
                    finiteWires.Add(wire);
                    anyWires.Add(wire);
                }
            }
            var roots = anyWires.Where(wire => wire.Position >= sourceSize && random() < 0.6).ToList();
            if (roots.Count == 0) roots.Add(anyWires[^1]);
            loweredGatherReads += TensorChecks.ProgramCounts(circuit.CompileTensorProgram(roots)).GatherReads;
            double[] extremes = [double.PositiveInfinity, double.NegativeInfinity, -0.0, LargestFloat32, SmallestSubnormalFloat32];
            var sources = Enumerable.Range(0, 6).Select(_ =>
            {
                var source = new float[sourceSize];
                var sourceCell = 0;
                for (var item = 0; item < integerCellCount; item++) source[sourceCell++] = RandomInteger(random, 2 * IntegerLimit + 1) - IntegerLimit;
                for (var item = 0; item < valueCellCount; item++) source[sourceCell++] = (float)((random() * 2 - 1) * 100);
                for (var item = 0; item < extremeCellCount; item++) source[sourceCell++] = (float)Pick(extremes);
                for (var item = 0; item < UnitCellCount; item++) source[sourceCell++] = RandomInteger(random, 5) / 4f;
                source[unit.Position] = 1;
                return source;
            }).ToArray();
            foreach (var values in TensorChecks.EveryCompileMode(circuit, roots, sources))
            {
                circuit.ValidateIndexReads(values);
                foreach (var intent in intents)
                {
                    var expected = ExpectedIndexRead(
                        values[intent.IndexWire.Position] + intent.Bias,
                        [.. intent.Candidates.Select(candidate => (double)values[candidate.Position])],
                        intent.Below,
                        intent.Above);
                    var actual = values[intent.Wire.Position];
                    Assert.True(
                        Js.ObjectIs(expected, actual),
                        $"circuit {circuitIndex}: read {intent.Wire} got {actual}, expected {expected}");
                    indexReadsChecked++;
                }
            }
        }
        Assert.True(indexReadsChecked > 2000, $"checked {indexReadsChecked} reads");
        Assert.True(loweredGatherReads > 20, $"{loweredGatherReads} lowered gather reads");
    }

    // 'proven bits' / 'emitFirstActive declares a one-hot group over comparison bits and index reads of
    // proven bits, and not over a sign cascade stage or a reachable fraction'.
    [Fact]
    public void Emit_first_active_declares_a_one_hot_group_over_comparison_bits_and_index_reads_of_proven_bits_and_not_over_a_sign_cascade_stage_or_a_reachable_fraction()
    {
        var circuit = new Circuit(8);
        // Cells 0 and 1: pointer coordinates on a 1/64 grid. Cells 2 and 3: widths with
        // no grid. Cell 4: a row index. Cell 5: a fraction. Cell 6: a bit the host writes.
        foreach (var wire in Wires(0, 1))
        {
            circuit.AssumeRange(wire, -1024, 1024);
            circuit.AssumeGrid(wire, 1.0 / 64);
        }
        circuit.AssumeRange(circuit.Source(2), 0, 16_384);
        circuit.AssumeRange(circuit.Source(3), 0, 16_384);
        circuit.AssumeRange(circuit.Source(4), 0, 2);
        circuit.AssumeRange(circuit.Source(5), 0, 1);
        circuit.AssumeRange(circuit.Source(6), 0, 1);
        int GroupCount() => circuit.OneHotGroups.Count;
        var gridBit = circuit.Compare(circuit.Source(0), Relation.GreaterOrEqual, circuit.Source(1));
        var cascadeBit = circuit.Compare(circuit.Source(2), Relation.Greater, circuit.Source(3));
        var atomBit = circuit.Compare(circuit.Source(2), Relation.Equal, circuit.Source(3), new CompareOptions { EndWithBoolean = true });
        var active = circuit.Boolean(circuit.Source(6));
        circuit.FirstActive(active, [gridBit, cascadeBit, atomBit]);
        Assert.Equal(1, GroupCount());
        // A clamp stage's output within 0..1 is recorded as a sign cascade rounding point, and isn't a bit.
        var stage = Enumerable.Range(0, circuit.Operations.Count)
            .Select(operationIndex => (Operation: circuit.Operations[operationIndex], Wire: Wire.FromPosition(circuit.SourceSize + operationIndex)))
            .First(entry => entry.Operation.RoundingPoint == RoundingPointKind.SignCascade
                && entry.Operation.Kind == OperationKind.Affine
                && circuit.Ranges[entry.Wire.Position] == new NumericRange(0, 1));
        circuit.FirstActive(active, [gridBit, stage.Wire]);
        Assert.Equal(1, GroupCount());
        // Reads of bits, a side that reads +0, and a read of reads.
        Wire[] table = [gridBit, cascadeBit, atomBit];
        var clampedRead = circuit.IndexRead(circuit.Source(4), table);
        var zeroRead = circuit.IndexReadAffine([(circuit.Source(4), 1)], 1, table, OutOfRange.Zero, OutOfRange.Zero);
        var readOfReads = circuit.IndexReadAffine([(circuit.Source(4), 1)], -1, [clampedRead, zeroRead], OutOfRange.Zero, OutOfRange.Clamp);
        circuit.FirstActive(active, [clampedRead, zeroRead, readOfReads]);
        Assert.Equal(2, GroupCount());
        // A fraction in the table only matters when the index can reach it.
        Wire[] withFraction = [gridBit, cascadeBit, circuit.Source(5)];
        Wire Read(double bias, IReadOnlyList<Wire> candidates, OutOfRange below, OutOfRange above) =>
            circuit.IndexReadAffine([(circuit.Source(4), 1)], bias, candidates, below, above);
        circuit.FirstActive(active, [gridBit, Read(1, withFraction, OutOfRange.Clamp, OutOfRange.Clamp)]);
        Assert.Equal(2, GroupCount());
        circuit.FirstActive(active, [gridBit, Read(-2, withFraction, OutOfRange.Zero, OutOfRange.Clamp)]);
        Assert.Equal(3, GroupCount());
        // Index -1..1 over 2 candidates reaches the fraction directly at position 0, and
        // through the clamp below. The +0 side above can't be reached.
        circuit.FirstActive(active, [gridBit, Read(-1, [circuit.Source(5), cascadeBit], OutOfRange.Clamp, OutOfRange.Zero)]);
        Assert.Equal(3, GroupCount());
        // Index 2..4 over 2 candidates only reaches past the end: 'clamp' above reads the
        // last candidate, a fraction, and 'zero' above reads +0.
        circuit.FirstActive(active, [gridBit, Read(2, [gridBit, circuit.Source(5)], OutOfRange.Zero, OutOfRange.Clamp)]);
        Assert.Equal(3, GroupCount());
        circuit.FirstActive(active, [gridBit, Read(2, [gridBit, circuit.Source(5)], OutOfRange.Clamp, OutOfRange.Zero)]);
        Assert.Equal(4, GroupCount());
        // The mirror: index -4..-2 only reaches before the start.
        circuit.FirstActive(active, [gridBit, Read(-4, [circuit.Source(5), gridBit], OutOfRange.Clamp, OutOfRange.Zero)]);
        Assert.Equal(4, GroupCount());
        circuit.FirstActive(active, [gridBit, Read(-4, [circuit.Source(5), gridBit], OutOfRange.Zero, OutOfRange.Clamp)]);
        Assert.Equal(5, GroupCount());
        // Inlining keeps the comparison bits proven.
        var parent = new Circuit(8);
        var wireMap = parent.InlineCircuit(circuit, [.. Enumerable.Range(0, 8).Select(parent.Source)]);
        var parentGroups = parent.OneHotGroups.Count;
        parent.FirstActive(wireMap[active.Position], [wireMap[gridBit.Position], wireMap[cascadeBit.Position], wireMap[zeroRead.Position]]);
        Assert.Equal(parentGroups + 1, parent.OneHotGroups.Count);
    }

    // 'proven bits' / 'gather lowering gives the same values as the dense compile over random circuits
    // whose selections read comparison bits and index reads of bits'.
    [Fact]
    public void Random_circuits_whose_selections_read_comparison_bits_and_index_reads_of_bits_hold_their_groups()
    {
        var random = Js.CreateRandom(0xb175);
        Relation[] relations = [Relation.Greater, Relation.GreaterOrEqual, Relation.Less, Relation.LessOrEqual, Relation.Equal];
        var groupsOverComparisonsOrReads = 0;
        var undeclaredSelections = 0;
        var readsOnlyThroughASide = 0;
        var loweredGatherReads = 0;
        var sourcesChecked = 0;
        for (var circuitIndex = 0; circuitIndex < 80; circuitIndex++)
        {
            // Integer cells within -6..6 on a grid of 1, pointer cells within ±512 on a 1/64
            // grid, widths within 0..4,096 with no grid, fractions within 0..1, and values within ±100.
            int[] integerCells = [0, 1, 2];
            int[] pointerCells = [3, 4];
            int[] widthCells = [5, 6];
            int[] fractionCells = [7, 8, 9];
            int[] valueCells = [10, 11, 12];
            var circuit = new Circuit(13);
            foreach (var wire in Wires(integerCells))
            {
                circuit.AssumeRange(wire, -6, 6);
                circuit.AssumeGrid(wire, 1);
            }
            foreach (var wire in Wires(pointerCells))
            {
                circuit.AssumeRange(wire, -512, 512);
                circuit.AssumeGrid(wire, 1.0 / 64);
            }
            foreach (var wire in Wires(widthCells)) circuit.AssumeRange(wire, 0, 4096);
            foreach (var wire in Wires(fractionCells)) circuit.AssumeRange(wire, 0, 1);
            foreach (var wire in Wires(valueCells)) circuit.AssumeRange(wire, -100, 100);
            T Pick<T>(IReadOnlyList<T> items) => items[RandomInteger(random, items.Count)];
            // What the test knows to be bits, and wires within 0..1 that can hold fractions.
            var bits = new List<Wire>();
            var unitWires = Wires(fractionCells).ToList();
            var comparisonsAndReads = new HashSet<Wire>();
            var values = Wires(valueCells).ToList();
            var roots = new List<Wire>();
            var operationCount = 12 + RandomInteger(random, 12);
            for (var operation = 0; operation < operationCount; operation++)
            {
                var choice = RandomInteger(random, 6);
                if (choice == 0 || bits.Count < 2)
                {
                    var operands = random() < 0.4 ? pointerCells : random() < 0.5 ? widthCells : integerCells;
                    var a = Wire.FromPosition(Pick(operands));
                    var relation = Pick(relations);
                    var b = Wire.FromPosition(Pick(operands));
                    var bit = circuit.Compare(a, relation, b, new CompareOptions { EndWithBoolean = random() < 0.25 });
                    bits.Add(bit);
                    comparisonsAndReads.Add(bit);
                }
                else if (choice == 1)
                {
                    bits.Add(circuit.Boolean(Wire.FromPosition(Pick(fractionCells))));
                }
                else if (choice == 2)
                {
                    // A table of bits, sometimes with a fraction, read at an integer cell with random sides.
                    var table = Enumerable.Range(0, 1 + RandomInteger(random, 5)).Select(_ => random() < 0.15 ? Pick(unitWires) : Pick(bits)).ToArray();
                    var indexCell = Pick(integerCells);
                    // Biases within -9..9 over integer cells within -6..6, so some reads reach
                    // their table only through a side, for example bias 9 over a table of 2 bits.
                    var bias = RandomInteger(random, 19) - 9;
                    var below = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                    var above = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                    var read = circuit.IndexReadAffine([(Wire.FromPosition(indexCell), 1)], bias, table, below, above);
                    if (-6 + bias > table.Length - 1 || 6 + bias < 0) readsOnlyThroughASide++;
                    // The two pools never share a wire, so a table of bits only holds wires from bits.
                    if (table.All(bits.Contains)) bits.Add(read);
                    else unitWires.Add(read);
                    comparisonsAndReads.Add(read);
                }
                else if (choice == 3)
                {
                    // A selection over bits, sometimes with a fraction, and a masked sum over it.
                    var candidates = Enumerable.Range(0, 2 + RandomInteger(random, 4)).Select(_ => random() < 0.85 ? Pick(bits) : Pick(unitWires)).ToArray();
                    var active = random() < 0.85 ? Pick(bits) : Pick(unitWires);
                    var groupsBefore = circuit.OneHotGroups.Count;
                    var selected = circuit.FirstActive(active, candidates);
                    if (circuit.OneHotGroups.Count > groupsBefore)
                    {
                        if (candidates.Prepend(active).Any(comparisonsAndReads.Contains)) groupsOverComparisonsOrReads++;
                    }
                    else
                    {
                        undeclaredSelections++;
                    }
                    var sum = circuit.RoundAffine([.. selected.Select(member => new Term(circuit.Multiply(member, Pick(values)), 1))]);
                    values.Add(sum);
                    roots.Add(sum);
                    unitWires.AddRange(selected);
                }
                else if (choice == 4)
                {
                    var index = Wire.FromPosition(Pick(integerCells));
                    var candidates = Enumerable.Range(0, 1 + RandomInteger(random, 4)).Select(_ => Pick(values)).ToArray();
                    values.Add(circuit.IndexRead(index, candidates));
                }
                else
                {
                    var gate = Pick(bits);
                    var off = Pick(values);
                    roots.Add(circuit.Select(gate, off, Pick(values)));
                }
            }
            if (roots.Count == 0) roots.Add(values[^1]);
            roots.AddRange(bits.Where(_ => random() < 0.3).ToList());
            loweredGatherReads += TensorChecks.ProgramCounts(circuit.CompileTensorProgram(roots)).GatherReads;
            var sources = Enumerable.Range(0, 8).Select(_ =>
            {
                var source = new float[13];
                foreach (var wire in integerCells) source[wire] = RandomInteger(random, 13) - 6;
                // A few pointer and width values, so equal operands happen often.
                foreach (var wire in pointerCells) source[wire] = (float)Pick(new[] { -3.5, 0, 12.25, 12.265625, 511.984375 });
                foreach (var wire in widthCells) source[wire] = (float)Pick(new[] { 0, Js.F(300.1), Js.F(300.10001), 4096 });
                foreach (var wire in fractionCells)
                {
                    // JavaScript builds the array, with its random() call, before pick draws.
                    double[] choices = [0, 1, 0.25, 0.5, Js.F(random())];
                    source[wire] = (float)Pick(choices);
                }
                foreach (var wire in valueCells) source[wire] = (float)((random() * 2 - 1) * 100);
                return source;
            }).ToArray();
            // Every compile mode, with and without gather lowering, gives the reference value, and
            // every declared group holds on the values.
            foreach (var evaluated in TensorChecks.EveryCompileMode(circuit, roots, sources))
            {
                circuit.ValidateOneHotGroups(evaluated);
                circuit.ValidateIndexReads(evaluated);
                sourcesChecked++;
            }
        }
        Assert.Equal(640, sourcesChecked);
        Assert.True(groupsOverComparisonsOrReads > 50, $"{groupsOverComparisonsOrReads} groups over comparisons or reads");
        Assert.True(undeclaredSelections > 10, $"{undeclaredSelections} undeclared selections");
        Assert.True(loweredGatherReads > 50, $"{loweredGatherReads} lowered gather reads");
        // Measured in Handwired: 46 reads whose index range reaches only past one end of the table.
        Assert.True(readsOnlyThroughASide > 20, $"{readsOnlyThroughASide} reads only through a side");
    }
}
