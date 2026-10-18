// Handwired source: no single Handwired test. The seeded program generator of spec item R2.4, moved
// out of TensorDifferentialTests so that the GPU tests can link this file. The generator builds random
// circuits of 4 to 12 source cells and 20 to 60 builder calls, and random source tables in the
// declared ranges. The random number sequence is the same as before the move, so the counts of
// TensorDifferentialTests do not change.

namespace Sharpwired.Core.Tests;

internal static class RandomPrograms
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

    internal enum CellKind
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
    internal sealed record Cell(CellKind Kind, double Step);

    // One declared group of source cells. DeclaredAfter groups are declared after their readers.
    internal sealed record SourceGroup(int Start, int Size, bool AllowAllZero, bool DeclaredAfter);

    internal enum BuilderCall
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

    internal static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);

    // Build one random circuit. The result holds the circuit, the source cells, the source groups,
    // and the pool of wires that the builder calls returned. openRangeShare above 0 gives the
    // open-range variant of the GPU tests. Then the finite cell 1 and each random cell that is not a
    // group member become cells with no range, with that probability. The default 0 takes no random number
    // for it, so the sequence of the phase 2 test does not change.
    internal static (Circuit Circuit, Cell[] Cells, List<SourceGroup> Groups, List<Wire> Wires) BuildCircuit(
        Func<double> random, int[] builderCalls, double openRangeShare = 0)
    {
        // The first 4 cells are fixed: an integer, a finite value, a unit value and the cell that
        // holds 1. The other cells are groups and cells of random kinds.
        var sourceSize = 4 + RandomInteger(random, 9);
        var cells = new Cell[sourceSize];
        cells[0] = new Cell(CellKind.Integer, 1);
        cells[1] = new Cell(CellKind.Finite, 0);
        cells[2] = new Cell(CellKind.Unit, 0);
        cells[3] = new Cell(CellKind.One, 0);
        if (openRangeShare > 0 && random() < openRangeShare) cells[1] = new Cell(CellKind.Unbounded, 0);
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
            if (openRangeShare > 0 && random() < openRangeShare) kind = CellKind.Unbounded;
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
            // GroupMaskedSum counts itself below, only when it builds an operation.
            if (kind != BuilderCall.GroupMaskedSum) builderCalls[(int)kind]++;
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
                        var width = circuit.Width;
                        wires.Add(MaskedSum(circuit, random, members, candidates));
                        if (circuit.Width > width) builderCalls[(int)kind]++;
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
        // The open-range variant only. This part comes after the builder calls, so that the default
        // sequence does not change. It makes 1 to 3 values u × relu(1 - relu(a - b)), with a and b wires
        // of no finite range. Range inference puts each value in 0..1, and the value is NaN for
        // a = b = ±Infinity. A boolean atom, a Compare bit and an index read of the value then read a NaN
        // that their ranges do not show. The builder calls above make no value in 0..1 over an open cell.
        var open = wires.Where(wire => !double.IsFinite(circuit.Ranges[wire.Position].Minimum) || !double.IsFinite(circuit.Ranges[wire.Position].Maximum)).ToList();
        if (openRangeShare > 0 && open.Count > 0)
        {
            var clamps = 1 + RandomInteger(random, 3);
            for (var clamp = 0; clamp < clamps; clamp++)
            {
                var ramp = circuit.Reglu([], 1, [(circuit.Affine([(Pick(open), 1), (Pick(open), -1)]), 1)], 0);
                var value = circuit.Reglu([(circuit.Source(2), 1)], 0, [(circuit.Affine([(ramp, -1)], 1), 1)], 0);
                wires.Add(value);
                wires.Add(circuit.Boolean(value));
                wires.Add(circuit.Compare(value, Relation.Greater, circuit.Constant(0.5)));
                wires.Add(circuit.IndexRead(value, [Pick(integers), Pick(integers)]));
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

    // A sum of bit × value products with a random bias. Each product has a random orientation of
    // four. Two orientations are the two operand orders of Multiply. The other two are a ReGLU with
    // the bit in the gate, and a ReGLU with the bit in the left form. The last orientation is a
    // masked read only for a nonnegative value.
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

    // One source table. Each cell gets a value in its declared range. The value is a random value,
    // an end of the range, ±0, the smallest subnormal, or the largest FP32 value in the range. A
    // cell with no bound can also get ±Infinity. Each group gets a valid one-hot, or all zeros
    // where the group allows it. With subnormals false (the GPU tests), the subnormal choice gives ±0,
    // and each other subnormal value becomes +0. Thus a device that flushes subnormal values compares
    // more evaluations. The default true keeps the sequence and the values of the phase 2 test.
    internal static float[] CreateSource(Func<double> random, Cell[] cells, List<SourceGroup> groups, bool subnormals = true)
    {
        var source = new float[cells.Length];
        for (var position = 0; position < cells.Length; position++)
        {
            var cell = cells[position];
            source[position] = cell.Kind switch
            {
                CellKind.Finite => (float)RangeValue(random, -FiniteLimit, FiniteLimit, subnormals),
                CellKind.NonNegative => (float)RangeValue(random, 0, NonNegativeLimit, subnormals),
                CellKind.Unit => (float)RangeValue(random, 0, 1, subnormals),
                CellKind.Unbounded => (float)RangeValue(random, double.NegativeInfinity, double.PositiveInfinity, subnormals),
                CellKind.Grid => (float)GridValue(random, cell.Step, GridLimit),
                CellKind.Integer => (float)GridValue(random, 1, IntegerLimit),
                CellKind.ExactBit => (float)(random() < 0.5 ? 1 : random() < 0.5 ? 0 : -0.0),
                CellKind.One => 1,
                _ => 0,
            };
            if (!subnormals && float.IsSubnormal(source[position])) source[position] = 0;
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

    private static double RangeValue(Func<double> random, double minimum, double maximum, bool subnormals)
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
            4 => (random() < 0.5 ? 1 : -1) * (subnormals ? SmallestSubnormalFloat32 : 0.0),
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
