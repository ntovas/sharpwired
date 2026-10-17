// Handwired source: no single Handwired test. Spec item R2.4: seeded random programs give equal
// values in the reference evaluator and in the tensor evaluator. The compile modes are the 5 modes of
// checkEveryCompileMode in tests/runtime/index-read.test.ts. The comparison follows Decision 1 of the
// phase 2 plan: Object.is in the modes without gather lowering, and sameNumericValue of
// tests/runtime/gather-lowering.test.ts in the modes with gather lowering, because a gather can change
// the sign of an exactly zero result (docs/execution-model.md, "One-hot groups and gathers").

using Xunit.Abstractions;

namespace Sharpwired.Core.Tests;

public class TensorDifferentialTests(ITestOutputHelper output)
{
    private const double LargestFloat32 = 3.4028234663852886e38;
    private const double SmallestSubnormalFloat32 = 1.401298464324817e-45;
    private const int IntegerLimit = 12;
    private const double FiniteLimit = 100;
    private const double NonNegativeLimit = 1e6;
    private const double GridLimit = 64;

    // The limit of the operand ranges that Compare accepts in this test. The difference of two such
    // ranges cannot overflow FP32.
    private const double CompareLimit = 1e6;

    // 9.5367431640625e-7 is 2^-20.
    private static readonly double[] Coefficients = [1, -1, 2, 1.0 / 3, -0.7, 9.5367431640625e-7, 12_345.6789, 0];

    // The 5 compile modes of index-read.test.ts. LowersGathers selects the comparison.
    private static readonly (string Label, bool LowersGathers, Func<Circuit, IReadOnlyList<Wire>, TensorProgram> Compile)[] Modes =
    [
        ("gather lowering, build-time depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots)),
        ("no gather lowering", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false)),
        ("gather lowering, recomputed depths", true, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: true)),
        ("no gather lowering, recomputed depths", false, (circuit, roots) => circuit.CompileTensorProgram(roots, lowerGathers: false, recomputeDepths: true)),
        ("every wire as a root", true, (circuit, _) => circuit.CompileTensorProgram()),
    ];

    private enum CellKind
    {
        Finite,
        NonNegative,
        Unit,
        Unbounded,
        Grid,
        ExactBit,
        Integer,
        GroupMember,
        One,
    }

    // One source cell: its kind, and the grid step of a Grid cell.
    private sealed record Cell(CellKind Kind, double Step);

    // One declared group of source cells. DeclaredAfter groups are declared after their readers.
    private sealed record SourceGroup(int Start, int Size, bool AllowAllZero, bool DeclaredAfter);

    private enum BuilderCall
    {
        Affine,
        RoundAffine,
        Constant,
        Reglu,
        Boolean,
        Multiply,
        Select,
        Minimum,
        Maximum,
        MinimumExact,
        MaximumExact,
        CompareGrid,
        CompareCascade,
        IndexRead,
        IndexReadAffine,
        Argmax,
        FirstActive,
        GroupMaskedSum,
        InlineCircuit,
    }

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);

    // R2.4. 600 seeded circuits of 4 to 12 source cells and 20 to 60 builder calls, 4 source tables
    // for each circuit, and 1 to 5 roots. Each compile mode must give the reference value on each
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

        // The coverage floors are half of the counts of the first run: comparedWires 1,213,156,
        // gatherReads 2,724, indexReads 2,481, nanWires 574, negativeZeroWires 24,363,
        // tensorEvaluationChecks 11,883, operations Affine 66,034, Reglu 72,801, Boolean 8,333 and
        // Index 1,617, rounding points IntegerBit 1,445 and SignCascade 52,042. Each builder call
        // ran at least 1,188 times.
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
        foreach (var call in Enum.GetValues<BuilderCall>())
        {
            Assert.True(builderCalls[(int)call] >= 594, $"builder call {call}: {builderCalls[(int)call]}");
        }
    }

    // Build one random circuit. The result holds the circuit, the source cells, the source groups,
    // and the pool of wires that the builder calls returned.
    private static (Circuit Circuit, Cell[] Cells, List<SourceGroup> Groups, List<Wire> Wires) BuildCircuit(
        Func<double> random, int[] builderCalls)
    {
        // The first 4 cells are fixed: an integer, a finite value, a unit value and the cell that
        // holds 1. The other cells are groups and cells of random kinds.
        var sourceSize = 4 + RandomInteger(random, 9);
        var cells = new Cell[sourceSize];
        cells[0] = new Cell(CellKind.Integer, 1);
        cells[1] = new Cell(CellKind.Finite, 0);
        cells[2] = new Cell(CellKind.Unit, 0);
        cells[3] = new Cell(CellKind.One, 0);
        var groups = new List<SourceGroup>();
        var cell = 4;
        while (cell < sourceSize)
        {
            if (random() < 0.3)
            {
                var size = Math.Min(1 + RandomInteger(random, 4), sourceSize - cell);
                groups.Add(new SourceGroup(cell, size, random() < 0.4, random() < 0.4));
                for (var member = 0; member < size; member++) cells[cell++] = new Cell(CellKind.GroupMember, 0);
                continue;
            }
            var kind = (CellKind)RandomInteger(random, (int)CellKind.GroupMember);
            cells[cell++] = new Cell(kind, kind == CellKind.Grid ? Math.Pow(2, -RandomInteger(random, 8)) : 1);
        }

        var circuit = new Circuit(sourceSize);
        for (var position = 0; position < sourceSize; position++)
        {
            var wire = circuit.Source(position);
            switch (cells[position].Kind)
            {
                case CellKind.Finite: circuit.AssumeRange(wire, -FiniteLimit, FiniteLimit); break;
                case CellKind.NonNegative: circuit.AssumeRange(wire, 0, NonNegativeLimit); break;
                case CellKind.Unit: circuit.AssumeRange(wire, 0, 1); break;
                case CellKind.Grid:
                    circuit.AssumeRange(wire, -GridLimit, GridLimit);
                    circuit.AssumeGrid(wire, cells[position].Step);
                    break;
                case CellKind.ExactBit: circuit.AssumeBit(wire); break;
                case CellKind.Integer:
                    circuit.AssumeRange(wire, -IntegerLimit, IntegerLimit);
                    circuit.AssumeGrid(wire, 1);
                    break;
                case CellKind.One: circuit.AssumeRange(wire, 1, 1); break;
            }
        }
        foreach (var group in groups.Where(group => !group.DeclaredAfter))
        {
            circuit.AssumeOneHot(GroupMembers(circuit, group), group.AllowAllZero);
        }

        var unit = circuit.Source(3);
        var wires = Enumerable.Range(0, sourceSize).Select(circuit.Source).ToList();
        var integers = new List<Wire>();
        for (var position = 0; position < sourceSize; position++)
        {
            if (cells[position].Kind == CellKind.Integer) integers.Add(circuit.Source(position));
        }
        T Pick<T>(IReadOnlyList<T> items) => items[RandomInteger(random, items.Count)];
        // The pools read the declared ranges at the time of the call.
        List<Wire> Where(Func<NumericRange, bool> test) => [.. wires.Where(wire => test(circuit.Ranges[wire.Position]))];
        bool IsUnit(NumericRange range) => range.Minimum >= 0 && range.Maximum <= 1;
        bool IsComparable(NumericRange range) => range.Minimum >= -CompareLimit && range.Maximum <= CompareLimit;
        bool IsNonNegative(NumericRange range) => range.Minimum >= 0;
        Term[] RandomTerms(int maximumCount)
        {
            var count = 1 + RandomInteger(random, maximumCount);
            var terms = new Term[count];
            for (var index = 0; index < count; index++)
            {
                // A repeated wire gives duplicate terms.
                var wire = index > 0 && random() < 0.2 ? terms[index - 1].Wire : Pick(wires);
                terms[index] = new Term(wire, Pick(Coefficients));
            }
            return terms;
        }
        Wire[] RandomCandidates(int maximumCount, IReadOnlyList<Wire> pool) =>
            PickMany(1 + RandomInteger(random, maximumCount), pool);
        Wire[] PickMany(int count, IReadOnlyList<Wire> pool) => [.. Enumerable.Range(0, count).Select(_ => Pick(pool))];

        var callCount = 20 + RandomInteger(random, 41);
        for (var call = 0; call < callCount; call++)
        {
            var kind = (BuilderCall)RandomInteger(random, builderCalls.Length);
            builderCalls[(int)kind]++;
            switch (kind)
            {
                case BuilderCall.Affine:
                    wires.Add(circuit.Affine(RandomTerms(6), random() < 0.5 ? 0 : Pick(Coefficients)));
                    break;
                case BuilderCall.RoundAffine:
                    wires.Add(circuit.RoundAffine(RandomTerms(6), random() < 0.5 ? 0 : Pick(Coefficients)));
                    break;
                case BuilderCall.Constant:
                    wires.Add(circuit.Constant(Pick(Coefficients)));
                    break;
                case BuilderCall.Reglu:
                    wires.Add(circuit.Reglu(RandomTerms(3), Pick(Coefficients), RandomTerms(3), random() < 0.5 ? 0 : Pick(Coefficients)));
                    break;
                case BuilderCall.Boolean:
                    wires.Add(circuit.Boolean(Pick(Where(IsUnit))));
                    break;
                case BuilderCall.Multiply:
                    wires.Add(circuit.Multiply(Pick(wires), Pick(wires)));
                    break;
                case BuilderCall.Select:
                    wires.Add(circuit.Select(Pick(Where(IsUnit)), Pick(wires), Pick(wires)));
                    break;
                case BuilderCall.Minimum:
                    wires.Add(circuit.Minimum(Pick(wires), Pick(wires)));
                    break;
                case BuilderCall.Maximum:
                    wires.Add(circuit.Maximum(Pick(wires), Pick(wires)));
                    break;
                case BuilderCall.MinimumExact:
                    {
                        var pool = Where(IsComparable);
                        wires.Add(circuit.MinimumExact(Pick(pool), Pick(pool)));
                        break;
                    }
                case BuilderCall.MaximumExact:
                    {
                        var pool = Where(IsComparable);
                        wires.Add(circuit.MaximumExact(Pick(pool), Pick(pool)));
                        break;
                    }
                case BuilderCall.CompareGrid:
                case BuilderCall.CompareCascade:
                    {
                        // The grid path needs a grid on both operands. The other pool mixes wires with
                        // and without a grid, so most of its pairs use the sign cascade.
                        List<Wire> pool = kind == BuilderCall.CompareGrid
                            ? [.. Where(IsComparable).Where(wire => circuit.Grids[wire.Position] is not null)]
                            : Where(IsComparable);
                        var relation = (Relation)RandomInteger(random, 5);
                        var options = new CompareOptions { EndWithBoolean = random() < 0.5 };
                        wires.Add(circuit.Compare(Pick(pool), relation, Pick(pool), options));
                        break;
                    }
                case BuilderCall.IndexRead:
                    {
                        var integerTable = random() < 0.4;
                        var candidates = RandomCandidates(8, integerTable ? integers : wires);
                        var read = circuit.IndexRead(Pick(integers), candidates, random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero);
                        wires.Add(read);
                        // A read of integer candidates is an integer, which can index again.
                        if (integerTable) integers.Add(read);
                        break;
                    }
                case BuilderCall.IndexReadAffine:
                    {
                        var below = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                        var above = random() < 0.5 ? OutOfRange.Clamp : OutOfRange.Zero;
                        wires.Add(circuit.IndexReadAffine(
                            [(Pick(integers), 1), (Pick(integers), -1)],
                            RandomInteger(random, 7) - 3,
                            RandomCandidates(10, wires),
                            below,
                            above));
                        break;
                    }
                case BuilderCall.Argmax:
                    {
                        var scores = RandomCandidates(5, Where(IsUnit));
                        var selected = circuit.Argmax(scores, unit).Selected;
                        wires.AddRange(selected);
                        wires.Add(MaskedSum(circuit, random, selected, PickMany(selected.Count, wires)));
                        break;
                    }
                case BuilderCall.FirstActive:
                    {
                        // Boolean atoms are proven bits, so FirstActive declares the selection as a
                        // group. Fractional candidates split the route and declare no group.
                        var candidates = RandomCandidates(5, Where(IsUnit));
                        if (random() < 0.7) candidates = [.. candidates.Select(circuit.Boolean)];
                        var selected = circuit.FirstActive(random() < 0.7 ? unit : Pick(Where(IsUnit)), candidates);
                        wires.AddRange(selected);
                        wires.Add(MaskedSum(circuit, random, selected, PickMany(selected.Length, wires)));
                        break;
                    }
                case BuilderCall.GroupMaskedSum:
                    {
                        if (groups.Count == 0) break;
                        var group = Pick(groups);
                        var members = GroupMembers(circuit, group);
                        var candidates = members.Select(_ => random() < 0.5 ? Pick(wires) : Pick(Where(IsNonNegative))).ToArray();
                        wires.Add(MaskedSum(circuit, random, members, candidates));
                        break;
                    }
                case BuilderCall.InlineCircuit:
                    {
                        var child = BuildChild(random);
                        Wire[] sourceWires =
                        [
                            Pick(Where(range => range.Minimum >= -FiniteLimit && range.Maximum <= FiniteLimit)),
                            Pick(Where(range => range.Minimum >= -FiniteLimit && range.Maximum <= FiniteLimit)),
                            Pick(Where(IsUnit)),
                            unit,
                        ];
                        var mapped = circuit.InlineCircuit(child, sourceWires);
                        wires.AddRange(mapped.Skip(child.SourceSize));
                        break;
                    }
            }
        }
        foreach (var group in groups.Where(group => group.DeclaredAfter))
        {
            circuit.AssumeOneHot(GroupMembers(circuit, group), group.AllowAllZero, allowExistingReaders: true);
        }
        return (circuit, cells, groups, wires);
    }

    private static Wire[] GroupMembers(Circuit circuit, SourceGroup group) =>
        [.. Enumerable.Range(group.Start, group.Size).Select(circuit.Source)];

    // A sum of bit × value products with a random bias. Each product has a random orientation: the
    // two operand orders of Multiply, a ReGLU with the bit in the gate, and a ReGLU with the bit in
    // the left form. The last orientation is a masked read only for a nonnegative value.
    private static Wire MaskedSum(Circuit circuit, Func<double> random, IReadOnlyList<Wire> bits, IReadOnlyList<Wire> values)
    {
        var terms = new Term[bits.Count];
        for (var slot = 0; slot < bits.Count; slot++)
        {
            var (bit, value) = (bits[slot], values[slot]);
            var product = RandomInteger(random, 4) switch
            {
                0 => circuit.Multiply(bit, value),
                1 => circuit.Multiply(value, bit),
                2 => circuit.Reglu([(value, 1)], 0, [(bit, 1)], 0),
                _ => circuit.Reglu([(bit, 1)], 0, [(value, 1)], 0),
            };
            terms[slot] = new Term(product, 1);
        }
        return circuit.Affine(terms, random() < 0.3 ? RandomInteger(random, 7) - 3 : 0);
    }

    // A small random child circuit for InlineCircuit: two cells within ±100, a unit cell, and the cell
    // that holds 1. It holds affine forms, products, a select, and an argmax selection with its
    // masked sum, so the parent receives a declared group.
    private static Circuit BuildChild(Func<double> random)
    {
        var child = new Circuit(4);
        child.AssumeRange(child.Source(0), -FiniteLimit, FiniteLimit);
        child.AssumeRange(child.Source(1), -FiniteLimit, FiniteLimit);
        child.AssumeRange(child.Source(2), 0, 1);
        child.AssumeRange(child.Source(3), 1, 1);
        var (a, b, fraction, one) = (child.Source(0), child.Source(1), child.Source(2), child.Source(3));
        var sum = child.Affine([(a, Coefficients[RandomInteger(random, Coefficients.Length)]), (b, -0.7)], 1.0 / 3);
        var product = child.Multiply(a, random() < 0.5 ? fraction : b);
        var selected = child.Select(child.Boolean(fraction), sum, product);
        if (random() < 0.6)
        {
            var winner = child.Argmax([fraction, child.Affine([(fraction, -1)], 1)], one).Selected;
            child.Affine([(child.Multiply(winner[0], selected), 1), (child.Multiply(winner[1], a), 1)]);
        }
        return child;
    }

    // One source table. Each cell gets a value in its declared range: a random value, an end of the
    // range, ±0, the smallest subnormal or the largest FP32 value in the range. A cell with no bound
    // can also get ±Infinity. Each group gets a valid one-hot, or all zeros where the group allows it.
    private static float[] CreateSource(Func<double> random, Cell[] cells, List<SourceGroup> groups)
    {
        var source = new float[cells.Length];
        for (var position = 0; position < cells.Length; position++)
        {
            var cell = cells[position];
            source[position] = cell.Kind switch
            {
                CellKind.Finite => (float)RangeValue(random, -FiniteLimit, FiniteLimit),
                CellKind.NonNegative => (float)RangeValue(random, 0, NonNegativeLimit),
                CellKind.Unit => (float)RangeValue(random, 0, 1),
                CellKind.Unbounded => (float)RangeValue(random, double.NegativeInfinity, double.PositiveInfinity),
                CellKind.Grid => (float)GridValue(random, cell.Step, GridLimit),
                CellKind.Integer => (float)GridValue(random, 1, IntegerLimit),
                CellKind.ExactBit => (float)(random() < 0.5 ? 1 : random() < 0.5 ? 0 : -0.0),
                CellKind.One => 1,
                _ => 0,
            };
        }
        foreach (var group in groups)
        {
            var inactive = group.AllowAllZero && random() < 0.25;
            var selected = inactive ? -1 : RandomInteger(random, group.Size);
            for (var member = 0; member < group.Size; member++)
            {
                source[group.Start + member] = member == selected ? 1 : random() < 0.2 ? -0f : 0f;
            }
        }
        return source;
    }

    private static double RangeValue(Func<double> random, double minimum, double maximum)
    {
        var lower = Math.Max(minimum, -LargestFloat32);
        var upper = Math.Min(maximum, LargestFloat32);
        var choice = RandomInteger(random, 8);
        double value = choice switch
        {
            0 => minimum,
            1 => maximum,
            2 => 0,
            3 => -0.0,
            4 => random() < 0.5 ? SmallestSubnormalFloat32 : -SmallestSubnormalFloat32,
            5 => random() < 0.5 ? LargestFloat32 : -LargestFloat32,
            // A cell with no bound gets a random value of a random magnitude.
            _ => double.IsFinite(maximum - minimum)
                ? lower + random() * (upper - lower)
                : (random() * 2 - 1) * Math.Pow(10, RandomInteger(random, 40)),
        };
        // A value outside the range falls back to a random value in the range.
        var rounded = (float)value;
        return rounded >= minimum && rounded <= maximum ? value : Math.Min(upper, Math.Max(lower, lower + random() * (upper - lower)));
    }

    // A multiple of step within ±limit: a random multiple, an end, or ±0.
    private static double GridValue(Func<double> random, double step, double limit) => RandomInteger(random, 5) switch
    {
        0 => limit,
        1 => -limit,
        2 => -0.0,
        _ => (RandomInteger(random, (int)(2 * limit / step) + 1) - limit / step) * step,
    };
}
