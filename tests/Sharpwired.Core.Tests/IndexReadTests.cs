// Handwired source: tests/runtime/index-read.test.ts, and one case of
// tests/runtime/numeric-semantics.test.ts. Each test name holds the Handwired test name.
// A test that compares with the tensor evaluator or reads the compiled program keeps its
// reference-evaluator part here. PHASE2-TENSOR-ASSERTIONS.md lists the tensor assertions.
// Handwired checkEveryCompileMode calls validateCircuit and evaluateReference on each source.
// EvaluateAll does the same, without the compile modes.

namespace Sharpwired.Core.Tests;

public class IndexReadTests
{
    private const double LargestFloat32 = float.MaxValue;
    private const double SmallestSubnormalFloat32 = float.Epsilon;
    private static readonly double Limit = Math.Pow(2, 24);

    // 'reads candidates[floor(index)] with clamp or +0 outside the table, in every compile mode' (reference part).
    [Fact]
    public void Reads_candidates_at_floor_index_with_clamp_or_zero_outside_the_table()
    {
        double[] candidateValues = [Js.F(-7.25), -0.0, double.PositiveInfinity, LargestFloat32, double.NegativeInfinity, SmallestSubnormalFloat32, Js.F(42.5)];
        var count = candidateValues.Length;
        var circuit = new Circuit(1 + count);
        circuit.AssumeRange(circuit.Source(0), -8, count + 8);
        var candidates = Wires(Enumerable.Range(1, count));
        (OutOfRange Below, OutOfRange Above)[] sideRules =
        [
            (OutOfRange.Clamp, OutOfRange.Clamp),
            (OutOfRange.Zero, OutOfRange.Zero),
            (OutOfRange.Zero, OutOfRange.Clamp),
            (OutOfRange.Clamp, OutOfRange.Zero),
        ];
        var reads = sideRules.Select(rule => circuit.IndexReadAffine([(circuit.Source(0), 1)], 0, candidates, rule.Below, rule.Above)).ToArray();
        double[] indices = [-8, -3, -1, -0.5, -0.0, 0, 0.25, 1, 2, 3.999, 4, 5, 6, 6.5, 7, 8, count + 8];
        var sources = indices.Select(index => Source([index, .. candidateValues])).ToArray();

        var references = EvaluateAll(circuit, sources);
        for (var sourceIndex = 0; sourceIndex < indices.Length; sourceIndex++)
        {
            for (var readIndex = 0; readIndex < sideRules.Length; readIndex++)
            {
                var expected = ExpectedIndexRead(Js.F(indices[sourceIndex]), candidateValues, sideRules[readIndex].Below, sideRules[readIndex].Above);
                Js.Is(expected, references[sourceIndex][reads[readIndex].Position]);
            }
        }
    }

    // 'a broken range promise: NaN and infinite indices read the same in every compile mode' (reference part).
    [Fact]
    public void A_broken_range_promise_NaN_and_infinite_indices_read_as_the_instruction_defines()
    {
        var circuit = new Circuit(5);
        // difference is NaN when both cells hold Infinity, although its range was declared.
        var difference = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), -1)]);
        circuit.AssumeRange(difference, 0, 2);
        var candidates = Wires(2, 3, 4);
        var clampRead = circuit.IndexRead(difference, candidates);
        var zeroRead = circuit.IndexRead(difference, candidates, OutOfRange.Zero);
        (double Left, double Right)[] cellPairs =
        [
            (double.PositiveInfinity, double.PositiveInfinity),
            (double.PositiveInfinity, 0),
            (double.NegativeInfinity, 0),
            (1e30, 0),
            (-1e30, 0),
            (5, 0),
            (-1, 0),
        ];
        var sources = cellPairs.Select(pair => Source([pair.Left, pair.Right, 10, 20, 30])).ToArray();

        var references = EvaluateAll(circuit, sources);
        for (var sourceIndex = 0; sourceIndex < cellPairs.Length; sourceIndex++)
        {
            var index = Js.F(Js.F(cellPairs[sourceIndex].Left) - Js.F(cellPairs[sourceIndex].Right));
            Js.Is(ExpectedIndexRead(index, [10, 20, 30], OutOfRange.Clamp, OutOfRange.Clamp), references[sourceIndex][clampRead.Position]);
            Js.Is(ExpectedIndexRead(index, [10, 20, 30], OutOfRange.Zero, OutOfRange.Zero), references[sourceIndex][zeroRead.Position]);
        }
        Assert.True(float.IsNaN(references[0][clampRead.Position]));
        Js.Throws(() => circuit.ValidateIndexReads(references[3]), "index read wire");
        Js.Throws(() => circuit.ValidateIndexReads(references[0]), "index read wire");
    }

    // 'differential: equals the dense one-hot masked sum on random tables, at both ends and out of range'
    // (reference part). Plan task 6 adds validateOneHotGroups on the dense values.
    [Fact]
    public void Differential_equals_the_dense_one_hot_masked_sum_on_random_tables()
    {
        var random = Js.CreateRandom(0x1dea);
        double[] extremes = [-0.0, 0, LargestFloat32, -LargestFloat32, SmallestSubnormalFloat32];
        var comparisons = 0;
        for (var trial = 0; trial < 240; trial++)
        {
            var style = trial % 2 == 0 ? DenseStyle.Clamp : DenseStyle.ZeroAbove;
            var count = 1 + RandomInteger(random, 40);
            var pair = BuildPair(count, style, LargestFloat32);
            var tableValues = Enumerable.Range(0, count)
                .Select(_ => random() < 0.15 ? extremes[RandomInteger(random, extremes.Length)] : Js.F((random() * 2 - 1) * 1000))
                .ToArray();
            double[] indices =
            [
                0,
                count - 1,
                RandomInteger(random, count),
                -1,
                -8,
                -RandomInteger(random, 8),
                count,
                count + 8,
                count + RandomInteger(random, 8),
            ];
            var sources = indices.Select(index => Source([index, .. tableValues])).ToArray();

            var denseReferences = EvaluateAll(pair.Dense, sources);
            var instructionReferences = EvaluateAll(pair.Instruction, sources);
            for (var sourceIndex = 0; sourceIndex < indices.Length; sourceIndex++)
            {
                var expected = ExpectedIndexRead(indices[sourceIndex], tableValues, OutOfRange.Clamp, pair.Above);
                var instructionValue = instructionReferences[sourceIndex][pair.InstructionRead.Position];
                var denseValue = denseReferences[sourceIndex][pair.DenseRead.Position];
                Js.Is(expected, instructionValue);
                // The masked sum starts from +0, so a selected -0 comes out as +0.
                Js.Is(expected == 0 ? 0 : expected, denseValue);
                Assert.True(instructionValue == denseValue);
                pair.Instruction.ValidateIndexReads(instructionReferences[sourceIndex]);
                comparisons++;
            }
        }
        Assert.Equal(240 * 9, comparisons);
    }

    // 'signed zero: a selected -0 comes through the read, while the masked sum returns +0' (reference part).
    [Fact]
    public void Signed_zero_a_selected_negative_zero_comes_through_the_read_while_the_masked_sum_returns_positive_zero()
    {
        var pair = BuildPair(3, DenseStyle.ZeroAbove, 100);
        float[][] sources = [Source([0, -0.0, 5, 7]), Source([3, -0.0, 5, 7])];
        var instructionReferences = EvaluateAll(pair.Instruction, sources);
        var denseReferences = EvaluateAll(pair.Dense, sources);
        Js.Is(-0.0, instructionReferences[0][pair.InstructionRead.Position]);
        Js.Is(0, denseReferences[0][pair.DenseRead.Position]);
        // Past the end, the 'zero' side reads +0.
        Js.Is(0, instructionReferences[1][pair.InstructionRead.Position]);
    }

    // 'infinite candidates: the read returns the selected value; the masked sum turns into NaN and stays dense' (reference part).
    [Fact]
    public void Infinite_candidates_the_read_returns_the_selected_value_and_the_masked_sum_turns_into_NaN()
    {
        var pair = BuildPair(4, DenseStyle.Clamp, double.PositiveInfinity);
        float[][] sources =
        [
            Source([1, double.PositiveInfinity, 5, 6, 7]),
            Source([0, double.PositiveInfinity, 5, 6, 7]),
            Source([2, 1, 2, double.NegativeInfinity, 7]),
        ];
        var instructionReferences = EvaluateAll(pair.Instruction, sources);
        var denseReferences = EvaluateAll(pair.Dense, sources);
        Assert.Equal([5f, float.PositiveInfinity, float.NegativeInfinity], instructionReferences.Select(values => values[pair.InstructionRead.Position]));
        Assert.True(float.IsNaN(denseReferences[0][pair.DenseRead.Position]));
        // A selected infinity survives the masked sum when every other candidate is finite.
        Js.Is(double.PositiveInfinity, denseReferences[1][pair.DenseRead.Position]);
    }

    // 'fractional indices: the read floors, while the ReLU one-hot interpolates' (reference part).
    // Plan task 6 adds the validateOneHotGroups refusal on the dense values.
    [Fact]
    public void Fractional_indices_the_read_floors_while_the_ReLU_one_hot_interpolates()
    {
        double[] rowHeights = [28, 44, 28, 60, 36, 28, 52, 40];
        var pair = BuildPair(rowHeights.Length, DenseStyle.Clamp, 1000);
        double[] fractionalIndices = [2.5, 2.25, 0.5, 6.75];
        var sources = fractionalIndices.Select(index => Source([index, .. rowHeights])).ToArray();
        var instructionReferences = EvaluateAll(pair.Instruction, sources);
        for (var sourceIndex = 0; sourceIndex < fractionalIndices.Length; sourceIndex++)
        {
            var index = fractionalIndices[sourceIndex];
            var lowerPosition = (int)Math.Floor(index);
            var fraction = index - lowerPosition;
            var interpolated = rowHeights[lowerPosition] * (1 - fraction) + rowHeights[lowerPosition + 1] * fraction;
            Js.Is(rowHeights[lowerPosition], instructionReferences[sourceIndex][pair.InstructionRead.Position]);
            Js.Is(interpolated, pair.Dense.EvaluateReference(sources[sourceIndex])[pair.DenseRead.Position]);
            Js.Throws(() => pair.Instruction.ValidateIndexReads(instructionReferences[sourceIndex]), "index read wire");
        }
        // The guide's example: 44 from the masked sum against 28 from the read.
        Js.Is(44, pair.Dense.EvaluateReference(sources[0])[pair.DenseRead.Position]);
    }

    // 'duplicate candidates, composed index forms, and chained reads match plain JavaScript' (reference part).
    [Fact]
    public void Duplicate_candidates_composed_index_forms_and_chained_reads_match_the_plain_definition()
    {
        var random = Js.CreateRandom(7);
        var start = Wire.FromPosition(0);
        var nextStarts = Wires(Enumerable.Range(1, 10));
        var payload = Wires(Enumerable.Range(11, 4));
        var circuit = new Circuit(15);
        circuit.AssumeRange(start, 0, 9);
        foreach (var wire in nextStarts) circuit.AssumeRange(wire, 0, 9);
        foreach (var wire in payload) circuit.AssumeRange(wire, -1000, 1000);
        var cursors = new List<Wire>();
        var cursor = start;
        for (var hop = 0; hop < 3; hop++)
        {
            cursor = circuit.IndexRead(cursor, nextStarts);
            cursors.Add(cursor);
        }
        // A small affine producer composes into the index form.
        var cursorPlusTwo = circuit.Affine([(cursor, 1)], 2);
        var composedRead = circuit.IndexRead(cursorPlusTwo, nextStarts, OutOfRange.Zero);
        var composedOperation = circuit.Operations[composedRead.Position - circuit.SourceSize];
        Assert.Equal<Term>([new Term(cursor, 1)], composedOperation.GateTerms);
        Assert.Equal(2, composedOperation.GateBias);
        Wire[] repeatedTable = [payload[0], payload[0], payload[2], payload[1], payload[0]];
        var shifted = circuit.IndexReadAffine([(cursor, 1)], -7, repeatedTable, OutOfRange.Clamp, OutOfRange.Zero);
        var sameWire = circuit.IndexReadAffine(
            [(circuit.IndexRead(cursors[1], nextStarts), 1)],
            -7,
            [.. repeatedTable],
            OutOfRange.Clamp,
            OutOfRange.Zero);
        Assert.Equal(shifted, sameWire);
        var sources = Enumerable.Range(0, 200).Select(_ =>
        {
            var source = new float[circuit.SourceSize];
            source[start.Position] = RandomInteger(random, 10);
            foreach (var wire in nextStarts) source[wire.Position] = RandomInteger(random, 10);
            foreach (var wire in payload) source[wire.Position] = (float)((random() * 2 - 1) * 1000);
            return source;
        }).ToArray();

        var references = EvaluateAll(circuit, sources);
        for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            var source = sources[sourceIndex];
            var values = references[sourceIndex];
            circuit.ValidateIndexReads(values);
            double expectedCursor = source[start.Position];
            for (var hop = 0; hop < 3; hop++)
            {
                expectedCursor = source[nextStarts[(int)expectedCursor].Position];
                Js.Is(expectedCursor, values[cursors[hop].Position]);
            }
            var nextStartValues = nextStarts.Select(wire => (double)source[wire.Position]).ToArray();
            Js.Is(ExpectedIndexRead(expectedCursor + 2, nextStartValues, OutOfRange.Zero, OutOfRange.Zero), values[composedRead.Position]);
            var repeatedValues = repeatedTable.Select(wire => (double)source[wire.Position]).ToArray();
            Js.Is(ExpectedIndexRead(expectedCursor - 7, repeatedValues, OutOfRange.Clamp, OutOfRange.Zero), values[shifted.Position]);
        }
    }

    // 'evenly spaced tables store a base and a stride; other tables store positions shared by every read' (reference part).
    // Handwired shuffles with [...values].sort(() => random() - 0.5). This port shuffles with a
    // Fisher-Yates pass over the same seeded generator, so the scattered order is different. The
    // assertions read the order from the shuffled table, so they hold for any order.
    [Fact]
    public void Evenly_spaced_and_scattered_tables_read_the_selected_candidate()
    {
        const int count = 1024;
        var circuit = new Circuit(1 + count);
        circuit.AssumeRange(circuit.Source(0), 0, count - 1);
        var values = Enumerable.Range(1, count).ToArray();
        foreach (var wire in values) circuit.AssumeRange(Wire.FromPosition(wire), -1e6, 1e6);
        var contiguous = circuit.IndexRead(circuit.Source(0), Wires(values));
        var reversed = circuit.IndexRead(circuit.Source(0), Wires(Enumerable.Reverse(values)));
        var everyOther = circuit.IndexRead(circuit.Source(0), Wires(values.Where((_, position) => position % 2 == 1)));
        var repeatedWire = circuit.IndexRead(circuit.Source(0), Wires(values[5], values[5], values[5]));
        var random = Js.CreateRandom(3);
        var shuffled = values.ToArray();
        for (var position = shuffled.Length - 1; position > 0; position--)
        {
            var other = RandomInteger(random, position + 1);
            (shuffled[position], shuffled[other]) = (shuffled[other], shuffled[position]);
        }
        var scattered = circuit.IndexRead(circuit.Source(0), Wires(shuffled));
        var scatteredNext = circuit.IndexReadAffine([(circuit.Source(0), 1)], 1, Wires(shuffled), OutOfRange.Clamp, OutOfRange.Zero);
        // Reads of the same content share one candidate table.
        Assert.Same(
            circuit.Operations[scattered.Position - circuit.SourceSize].IndexRead!.Table,
            circuit.Operations[scatteredNext.Position - circuit.SourceSize].IndexRead!.Table);
        var sources = new[] { 0, 1, 511, 512, 1022, 1023 }.Select(at =>
        {
            var source = new float[circuit.SourceSize];
            source[0] = at;
            for (var position = 0; position < count; position++) source[values[position]] = position;
            return source;
        }).ToArray();

        var references = EvaluateAll(circuit, sources);
        for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            var at = (int)sources[sourceIndex][0];
            var reference = references[sourceIndex];
            Js.Is(at, reference[contiguous.Position]);
            Js.Is(count - 1 - at, reference[reversed.Position]);
            Js.Is(Math.Min(2 * at + 1, count - 1), reference[everyOther.Position]);
            Js.Is(5, reference[repeatedWire.Position]);
            Js.Is(shuffled[at] - 1, reference[scattered.Position]);
            Js.Is(at + 1 < count ? shuffled[at + 1] - 1 : 0, reference[scatteredNext.Position]);
        }
    }

    // 'candidate tables are registered once, frozen, and shared by arrays with the same content'.
    // Handwired freezes the caller's array, so a change after a read throws. Sharpwired copies the
    // candidates into an immutable table, so a change to the caller's list cannot reach a read.
    [Fact]
    public void Candidate_tables_are_registered_once_copied_and_shared_by_lists_with_the_same_content()
    {
        var circuit = new Circuit(6);
        circuit.AssumeRange(circuit.Source(0), 0, 3);
        var candidates = Wires(1, 2, 3, 4).ToList();
        var first = circuit.IndexRead(circuit.Source(0), candidates);
        Assert.Equal(first, circuit.IndexRead(circuit.Source(0), candidates));
        Assert.Equal(first, circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)]));
        var zeroRead = circuit.IndexRead(circuit.Source(0), candidates, OutOfRange.Zero);
        Assert.NotEqual(first, zeroRead);
        var otherContent = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(5)]);
        Assert.NotEqual(first, otherContent);
        var firstTable = circuit.Operations[first.Position - circuit.SourceSize].IndexRead!.Table;
        Assert.Same(firstTable, circuit.Operations[zeroRead.Position - circuit.SourceSize].IndexRead!.Table);
        Assert.NotEqual(firstTable.Id, circuit.Operations[otherContent.Position - circuit.SourceSize].IndexRead!.Table.Id);

        candidates[0] = circuit.Source(5);
        candidates.Add(circuit.Source(5));
        Assert.Equal<Wire>([circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)], firstTable.Candidates);
        Js.Is(10, circuit.EvaluateReference([0, 10, 20, 30, 40, 50])[first.Position]);
        // The changed list has new content, so it registers a new table.
        Assert.NotEqual(first, circuit.IndexRead(circuit.Source(0), candidates));
    }

    // 'a constant index selects its candidate while building'.
    [Fact]
    public void A_constant_index_selects_its_candidate_while_building()
    {
        var circuit = new Circuit(5);
        var three = circuit.Affine([], 3);
        var nine = circuit.Affine([], 9);
        Wire[] table = [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)];
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(three, table));
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(nine, table));
        Assert.Equal(Wire.FromPosition(1), circuit.IndexReadAffine([(three, -1)], 1, table, OutOfRange.Clamp, OutOfRange.Zero));
        var beyond = circuit.IndexRead(nine, table, OutOfRange.Zero);
        Assert.Equal(0, circuit.Constants[beyond.Position]);
        var belowZero = circuit.IndexReadAffine([(three, 1)], -5, table, OutOfRange.Zero, OutOfRange.Clamp);
        Assert.Equal(0, circuit.Constants[belowZero.Position]);
    }

    // 'the builder refuses reads that break the contract'.
    [Fact]
    public void The_builder_refuses_reads_that_break_the_contract()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(1), 0, Limit - 1);
        circuit.AssumeRange(circuit.Source(2), -Math.Pow(2, 25), 0);
        // An index cell with no declared range, and one declared past -2^24.
        Js.Throws(() => circuit.IndexRead(circuit.Source(0), [circuit.Source(3)]), "index range");
        Js.Throws(() => circuit.IndexRead(circuit.Source(2), [circuit.Source(3)]), "index range");
        // Both endpoints must lie strictly inside ±2^24: 0..2^24 - 1 builds, and adding 1 reaches 2^24.
        Assert.Equal(Wire.FromPosition(4), circuit.IndexRead(circuit.Source(1), [circuit.Source(3)]));
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), []), "at least one candidate");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [Wire.FromPosition(1000)]), "wire is outside");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [circuit.Source(3)], (OutOfRange)7), "clamp or zero");
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), double.NaN)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "finite");
        var positiveInfinity = circuit.Affine([], double.PositiveInfinity);
        var negativeInfinity = circuit.Affine([], double.NegativeInfinity);
        Js.Throws(
            () => circuit.IndexReadAffine([(positiveInfinity, 1), (negativeInfinity, 1)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "must not be NaN");
    }

    // 'an index range is accepted only strictly inside ±2^24, so an endpoint that FP32 rounded onto 2^24 is refused'.
    [Fact]
    public void An_index_range_is_accepted_only_strictly_inside_the_limit_so_an_endpoint_rounded_onto_the_limit_is_refused()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, Limit);
        circuit.AssumeRange(circuit.Source(1), -Limit, 0);
        circuit.AssumeRange(circuit.Source(2), 0, Limit - 2);
        // f(2^24 + 1) = 2^24, so the form's range ends on 2^24, and at cell value 2^24
        // the read would land one position early.
        Assert.Equal(Limit, Js.F(Limit + 1));
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(0), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(0), [circuit.Source(3)]), "strictly inside ±2^24");
        // The negative mirror: f(-2^24 - 1) = -2^24.
        Js.Throws(
            () => circuit.IndexReadAffine([(circuit.Source(1), 1)], -1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp),
            "strictly inside ±2^24");
        Js.Throws(() => circuit.IndexRead(circuit.Source(1), [circuit.Source(3)]), "strictly inside ±2^24");
        // One position inside the limit on either side builds.
        var upper = circuit.IndexReadAffine([(circuit.Source(2), 1)], 1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(1, Limit - 1), circuit.Operations[upper.Position - circuit.SourceSize].IndexRead!.IndexRange);
        var lower = circuit.IndexReadAffine([(circuit.Source(2), -1)], -1, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(-(Limit - 1), -1), circuit.Operations[lower.Position - circuit.SourceSize].IndexRead!.IndexRange);
        // A range declared on the form can't put an endpoint on the limit either.
        Js.Throws(
            () => circuit.IndexReadAffine(
                [(circuit.Source(0), 1)], 0, [circuit.Source(3)], OutOfRange.Clamp, OutOfRange.Clamp, new NumericRange(0, Limit)),
            "strictly inside ±2^24");
        circuit.ValidateCircuit();
        var operationIndex = upper.Position - circuit.SourceSize;
        var operation = circuit.OperationList[operationIndex];
        circuit.OperationList[operationIndex] = operation with { IndexRead = operation.IndexRead! with { IndexRange = new NumericRange(0, Limit) } };
        Js.Throws(() => circuit.ValidateCircuit(), "strictly inside ±2^24");
    }

    // 'the read range is the union of the reachable candidates, plus 0 where a zero side is reachable'.
    [Fact]
    public void The_read_range_is_the_union_of_the_reachable_candidates_plus_zero_where_a_zero_side_is_reachable()
    {
        var circuit = new Circuit(9);
        (double Minimum, double Maximum)[] candidateRanges = [(0.5, 1), (2, 3), (-5, -4), (10, 20)];
        for (var position = 0; position < candidateRanges.Length; position++)
        {
            circuit.AssumeRange(Wire.FromPosition(1 + position), candidateRanges[position].Minimum, candidateRanges[position].Maximum);
        }
        var candidates = Wires(1, 2, 3, 4);
        Wire IndexCell(Wire wire, double minimum, double maximum)
        {
            circuit.AssumeRange(wire, minimum, maximum);
            return wire;
        }
        var inside = circuit.IndexRead(IndexCell(circuit.Source(5), 1, 2), candidates);
        Assert.Equal(new NumericRange(-5, 3), circuit.Ranges[inside.Position]);
        var zeroBelow = circuit.IndexReadAffine([(IndexCell(circuit.Source(6), -3, 1), 1)], 0, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0, 3), circuit.Ranges[zeroBelow.Position]);
        var clampBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], 0, candidates, OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0.5, 3), circuit.Ranges[clampBelow.Position]);
        var zeroAbove = circuit.IndexReadAffine([(IndexCell(circuit.Source(7), 2, 9), 1)], 0, candidates, OutOfRange.Clamp, OutOfRange.Zero);
        Assert.Equal(new NumericRange(-5, 20), circuit.Ranges[zeroAbove.Position]);
        // Index ranges entirely below the table reach only the 'zero' side, whatever their distance,
        // and they share one cached union. Handwired also checks that each read gets its own range
        // object. NumericRange is a value type, so each wire holds its own copy.
        var justBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -2, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        var fartherBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -50, candidates, OutOfRange.Zero, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0, 0), circuit.Ranges[justBelow.Position]);
        Assert.Equal(new NumericRange(0, 0), circuit.Ranges[fartherBelow.Position]);
        var clampedFartherBelow = circuit.IndexReadAffine([(circuit.Source(6), 1)], -50, candidates, OutOfRange.Clamp, OutOfRange.Clamp);
        Assert.Equal(new NumericRange(0.5, 1), circuit.Ranges[clampedFartherBelow.Position]);
        // Cell 8 has no declared range. It opens the read's range only where the index can reach it.
        var unreachableUnknown = circuit.IndexRead(circuit.Source(5), [circuit.Source(8), circuit.Source(1)]);
        Assert.Equal(new NumericRange(0.5, 1), circuit.Ranges[unreachableUnknown.Position]);
        var reachableUnknown = circuit.IndexRead(circuit.Source(5), [circuit.Source(1), circuit.Source(8)]);
        Assert.Equal(NumericRange.Unknown, circuit.Ranges[reachableUnknown.Position]);
    }

    // 'a range declared on the index wire counts, also when the builder copies a small affine index into the read'
    // (reference part). Plan task 6 adds the inlineCircuit part.
    [Fact]
    public void A_range_declared_on_the_index_wire_counts_also_when_the_builder_copies_a_small_affine_index_into_the_read()
    {
        // Cells 0 and 1 (firstRow and offset) have no declared ranges. Candidate cell k is within 10k..10k + 5.
        var circuit = new Circuit(6);
        for (var wire = 2; wire < 6; wire++) circuit.AssumeRange(Wire.FromPosition(wire), 10 * wire, 10 * wire + 5);
        var candidates = Wires(2, 3, 4, 5);
        var row = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        Js.Throws(() => circuit.IndexRead(row, candidates), "index range");
        circuit.AssumeRange(row, 0, 3);
        var read = circuit.IndexRead(row, candidates, OutOfRange.Zero);
        // The read copies firstRow + offset, so the affine row isn't live.
        Assert.Equal<Term>([new Term(circuit.Source(0), 1), new Term(circuit.Source(1), 1)], circuit.Operations[read.Position - circuit.SourceSize].GateTerms);
        // The declared range 0..3 can't reach the zero sides.
        Assert.Equal(new NumericRange(20, 55), circuit.Ranges[read.Position]);
        (double FirstRow, double Offset)[] cells = [(0, 0), (1, 2), (3, 0), (-1, 2), (2, 2), (-3, 1)];
        var sources = cells.Select(cell => Source([cell.FirstRow, cell.Offset, 20, 30, 40, 50])).ToArray();
        var references = EvaluateAll(circuit, sources);
        for (var sourceIndex = 0; sourceIndex < cells.Length; sourceIndex++)
        {
            var values = references[sourceIndex];
            var index = cells[sourceIndex].FirstRow + cells[sourceIndex].Offset;
            Js.Is(ExpectedIndexRead(index, [20, 30, 40, 50], OutOfRange.Zero, OutOfRange.Zero), values[read.Position]);
            // ValidateIndexReads checks the declared range: firstRow + offset = 4 or -2 fails, although both cells are undeclared.
            if (index >= 0 && index <= 3) circuit.ValidateIndexReads(values);
            else Js.Throws(() => circuit.ValidateIndexReads(values), "index read wire");
        }
        // IndexReadAffine takes the same promise as an option. Without it, the same form is refused, although a read of it exists.
        Term[] form = [(circuit.Source(0), 1), (circuit.Source(1), 1)];
        Js.Throws(() => circuit.IndexReadAffine(form, 0, candidates, OutOfRange.Zero, OutOfRange.Zero), "index range");
        Assert.Equal(read, circuit.IndexReadAffine(form, 0, candidates, OutOfRange.Zero, OutOfRange.Zero, new NumericRange(0, 3)));
        Js.Throws(
            () => circuit.IndexReadAffine(form, 0, candidates, OutOfRange.Zero, OutOfRange.Zero, new NumericRange(0, Math.Pow(2, 25))),
            "index range");

        // A declared range narrows which candidates the read can reach, and operands with wide ranges don't matter.
        var wide = new Circuit(6);
        wide.AssumeRange(wide.Source(0), 0, Math.Pow(2, 30));
        wide.AssumeRange(wide.Source(1), 0, Math.Pow(2, 30));
        for (var wire = 2; wire < 6; wire++) wide.AssumeRange(Wire.FromPosition(wire), 10 * wire, 10 * wire + 5);
        var difference = wide.Affine([(wide.Source(0), 1), (wide.Source(1), -1)]);
        Js.Throws(() => wide.IndexRead(difference, candidates), "index range");
        wide.AssumeRange(difference, 1, 2);
        var narrowRead = wide.IndexRead(difference, candidates);
        Assert.Equal(new NumericRange(30, 45), wide.Ranges[narrowRead.Position]);
        // FP32 holds each operand exactly, e.g. not 2^30 - 1, which rounds to 2^30.
        (double Left, double Right)[] wideCells = [(Limit - 1, Limit - 3), (Math.Pow(2, 23) + 2, Math.Pow(2, 23) + 1), (7, 6)];
        var wideSources = wideCells.Select(cell => Source([cell.Left, cell.Right, 20, 30, 40, 50])).ToArray();
        foreach (var values in EvaluateAll(wide, wideSources))
        {
            wide.ValidateIndexReads(values);
            Js.Is(values[0] - values[1] == 1 ? 30 : 40, values[narrowRead.Position]);
        }
        // A declared range that contradicts the inferred one is refused.
        var contradiction = new Circuit(3);
        contradiction.AssumeRange(contradiction.Source(0), 0, 3);
        Js.Throws(
            () => contradiction.IndexReadAffine(
                [(contradiction.Source(0), 1)],
                0,
                [contradiction.Source(1), contradiction.Source(2)],
                OutOfRange.Clamp,
                OutOfRange.Clamp,
                new NumericRange(5, 9)),
            "contradicts");
    }

    // 'validators reject malformed index read programs and circuits' (the validateCircuit part).
    [Fact]
    public void Validators_reject_malformed_index_read_circuits()
    {
        var circuit = new Circuit(8);
        circuit.AssumeRange(circuit.Source(0), 0, 5);
        circuit.Reglu([(circuit.Source(1), 1)], 0, [(circuit.Source(2), 1)], 0);
        var contiguousRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)]);
        circuit.IndexRead(circuit.Source(0), [circuit.Source(5), circuit.Source(1), circuit.Source(7), circuit.Source(6)]);
        var operationIndex = contiguousRead.Position - circuit.SourceSize;
        var operation = circuit.OperationList[operationIndex];
        var table = operation.IndexRead!.Table;
        circuit.OperationList[operationIndex] = operation with
        {
            IndexRead = operation.IndexRead with { Table = new CandidateTable(table.Id, [Wire.FromPosition(1), contiguousRead], table.MaximumDepth) },
        };
        Js.Throws(() => circuit.ValidateCircuit(), "earlier wires");
        circuit.OperationList[operationIndex] = operation;
        circuit.ValidateCircuit();
    }

    // numeric-semantics.test.ts 'an index read reads the wires of its index form and its candidates, so a later declaration on them throws'.
    [Fact]
    public void An_index_read_reads_the_wires_of_its_index_form_and_its_candidates_so_a_later_declaration_on_them_throws()
    {
        var circuit = new Circuit(5);
        var (row, offset, first, second, third) = (circuit.Source(0), circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4));
        circuit.AssumeRange(row, 0, 2);
        // JS: [first, second, third][row + offset * 0]. The zero coefficient is dropped, so the read doesn't read offset.
        circuit.IndexReadAffine([(row, 1), (offset, 0)], 0, [first, second, third], OutOfRange.Clamp, OutOfRange.Clamp);
        Js.Throws(() => circuit.AssumeRange(row, 0, 1), "already read wire 0");
        Js.Throws(() => circuit.AssumeRange(second, 0, 1), "already read wire 3");
        Js.Throws(() => circuit.AssumeGrid(third, 1), "already read wire 4");
        circuit.AssumeRange(offset, 0, 1);
    }

    // Sharpwired addition. Handwired reachableCandidates has no direct test: the 'proven bits' test of
    // index-read.test.ts checks it through emitFirstActive (plan task 6). These cases are the reach
    // cases of that test, with the candidates that each read can return.
    [Fact]
    public void Reachable_candidates_follow_the_accepted_index_range_and_the_side_rules()
    {
        var circuit = new Circuit(4);
        circuit.AssumeRange(circuit.Source(0), 0, 2);
        var (a, b) = (circuit.Source(1), circuit.Source(2));
        IReadOnlyList<Wire> Reach(double bias, OutOfRange below, OutOfRange above)
        {
            var read = circuit.IndexReadAffine([(circuit.Source(0), 1)], bias, [a, b], below, above);
            return Circuit.ReachableCandidates(circuit.Operations[read.Position - circuit.SourceSize].IndexRead!);
        }
        // Index -1..1 reaches both candidates directly, and the first one again through the clamp below.
        Assert.Equal<Wire>([a, b, a], Reach(-1, OutOfRange.Clamp, OutOfRange.Zero));
        // Index 2..4 reaches only past the end.
        Assert.Equal<Wire>([b], Reach(2, OutOfRange.Zero, OutOfRange.Clamp));
        Assert.Equal<Wire>([], Reach(2, OutOfRange.Clamp, OutOfRange.Zero));
        // Index -4..-2 reaches only before the start.
        Assert.Equal<Wire>([a], Reach(-4, OutOfRange.Clamp, OutOfRange.Zero));
        Assert.Equal<Wire>([], Reach(-4, OutOfRange.Zero, OutOfRange.Clamp));
        // Index 0..2 reaches both candidates and the last one again through the clamp above.
        Assert.Equal<Wire>([a, b, b], Reach(0, OutOfRange.Zero, OutOfRange.Clamp));
    }

    // Sharpwired addition, from indexReadAffine: the read is one deeper than its deepest candidate
    // and its deepest index operand, and it has no grid and no constant.
    [Fact]
    public void An_index_read_is_one_deeper_than_its_deepest_candidate_and_index_operand()
    {
        var circuit = new Circuit(3);
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        circuit.AssumeGrid(circuit.Source(1), 1);
        var deepCandidate = circuit.RoundAffine([(circuit.RoundAffine([(circuit.Source(1), 2)]), 2)]);
        var shallowRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2)]);
        Assert.Equal(1, circuit.Depths[shallowRead.Position]);
        Assert.Null(circuit.Grids[shallowRead.Position]);
        Assert.Null(circuit.Constants[shallowRead.Position]);
        var deepRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), deepCandidate]);
        Assert.Equal(3, circuit.Depths[deepRead.Position]);
        var index = circuit.RoundAffine([(circuit.Source(0), 1)]);
        circuit.AssumeRange(index, 0, 1);
        var readOfRead = circuit.IndexRead(index, [deepRead]);
        Assert.Equal(4, circuit.Depths[readOfRead.Position]);
    }

    // Sharpwired addition: the Js.CreateRandom port gives the values of the JavaScript createRandom.
    // The expected values come from node with the Handwired function text.
    [Fact]
    public void Create_random_gives_the_values_of_the_Handwired_generator()
    {
        var random = Js.CreateRandom(0x1dea);
        Assert.Equal(0.022467014146968722, random());
        Assert.Equal(0.26093018613755703, random());
        Assert.Equal(0.6365113609936088, random());
    }

    private enum DenseStyle
    {
        Clamp,
        ZeroAbove,
    }

    private sealed record Pair(Circuit Dense, Wire DenseRead, Circuit Instruction, Wire InstructionRead, OutOfRange Above);

    // The instruction's definition, written independently of the runtime.
    private static double ExpectedIndexRead(double index, IReadOnlyList<double> values, OutOfRange below, OutOfRange above)
    {
        if (double.IsNaN(index)) return double.NaN;
        var position = Math.Floor(index);
        if (position < 0) return below == OutOfRange.Zero ? 0 : values[0];
        if (position > values.Count - 1) return above == OutOfRange.Zero ? 0 : values[^1];
        return values[(int)position];
    }

    // [index >= threshold] for an integer index, as relu(index - threshold + 1) - relu(index - threshold),
    // kept as its own rounding point.
    private static Wire EmitIntegerAtLeast(Circuit circuit, Wire index, double threshold)
    {
        var upper = circuit.Reglu([], 1, [(index, 1)], 1 - threshold);
        var lower = circuit.Reglu([], 1, [(index, 1)], -threshold);
        var bit = circuit.RoundAffine([(upper, 1), (lower, -1)]);
        circuit.AssumeRange(bit, 0, 1);
        return bit;
    }

    // The one-hot masked sum that an index read replaces, in the two styles of the prototypes. Clamp
    // selects the first member for every index <= 0 and the last member for every index >= count - 1.
    // ZeroAbove selects the first member for every index <= 0, and no member once index >= count.
    private static Wire EmitOneHotMaskedRead(Circuit circuit, Wire index, IReadOnlyList<Wire> values, DenseStyle style)
    {
        var count = values.Count;
        var atLeast = new List<Wire> { circuit.Affine([], 1) };
        var lastThreshold = style == DenseStyle.Clamp ? count - 1 : count;
        for (var threshold = 1; threshold <= lastThreshold; threshold++) atLeast.Add(EmitIntegerAtLeast(circuit, index, threshold));
        if (style == DenseStyle.Clamp) atLeast.Add(circuit.Affine([], 0));
        var members = values.Select((_, position) =>
        {
            var member = circuit.RoundAffine([(atLeast[position], 1), (atLeast[position + 1], -1)]);
            circuit.AssumeRange(member, 0, 1);
            return member;
        }).ToArray();
        circuit.AssumeOneHot(members, style == DenseStyle.ZeroAbove);
        return circuit.RoundAffine(members.Select((member, position) => new Term(circuit.Multiply(member, values[position]), 1)).ToArray());
    }

    // One source layout shared by a dense construction and the instruction: an index cell, then
    // one cell for each table entry.
    private static Pair BuildPair(int count, DenseStyle style, double valueRange)
    {
        var sourceSize = 1 + count;
        var index = Wire.FromPosition(0);
        var values = Wires(Enumerable.Range(1, count));
        void DeclareRanges(Circuit circuit)
        {
            circuit.AssumeRange(index, -8, count + 8);
            foreach (var value in values) circuit.AssumeRange(value, -valueRange, valueRange);
        }
        var dense = new Circuit(sourceSize);
        DeclareRanges(dense);
        var denseRead = EmitOneHotMaskedRead(dense, index, values, style);
        var instruction = new Circuit(sourceSize);
        DeclareRanges(instruction);
        var above = style == DenseStyle.Clamp ? OutOfRange.Clamp : OutOfRange.Zero;
        var instructionRead = instruction.IndexReadAffine([(index, 1)], 0, [.. values], OutOfRange.Clamp, above);
        return new Pair(dense, denseRead, instruction, instructionRead, above);
    }

    private static float[][] EvaluateAll(Circuit circuit, IEnumerable<float[]> sources)
    {
        circuit.ValidateCircuit();
        return [.. sources.Select(source => circuit.EvaluateReference(source))];
    }

    private static float[] Source(double[] cells) => [.. cells.Select(cell => (float)cell)];

    private static Wire[] Wires(params IEnumerable<int> positions) => [.. positions.Select(Wire.FromPosition)];

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);
}
