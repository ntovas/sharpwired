// Handwired source: tests/runtime/index-read.test.ts (the index read instruction cases that
// evaluate a circuit). Each test name holds the Handwired test name. Handwired
// checkEveryCompileMode is TensorChecks.EveryCompileMode with CompileModes.IndexRead, and Handwired
// programCounts is TensorChecks.ProgramCounts.

namespace Sharpwired.Core.Tests;

public partial class IndexReadTests
{
    private const double LargestFloat32 = float.MaxValue;
    private const double SmallestSubnormalFloat32 = float.Epsilon;
    private static readonly double Limit = Math.Pow(2, 24);

    // 'reads candidates[floor(index)] with clamp or +0 outside the table, in every compile mode'.
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

        var references = TensorChecks.EveryCompileMode(circuit, reads, sources);
        for (var sourceIndex = 0; sourceIndex < indices.Length; sourceIndex++)
        {
            for (var readIndex = 0; readIndex < sideRules.Length; readIndex++)
            {
                var expected = ExpectedIndexRead(Js.F(indices[sourceIndex]), candidateValues, sideRules[readIndex].Below, sideRules[readIndex].Above);
                Js.Is(expected, references[sourceIndex][reads[readIndex].Position]);
            }
        }
        // EvaluateTensor compiles every wire when no program is set.
        circuit.TensorProgram = null;
        Js.Is(0, circuit.EvaluateTensor(sources[0])[reads[1].Position]);
        var program = circuit.CompileTensorProgram(reads);
        Assert.Equal(
            new ProgramCountsResult(LiveOperations: 4, Layers: 1, StoredWeights: 4, GatherReads: 0, IndexReads: 4),
            TensorChecks.ProgramCounts(program));
    }

    // 'a broken range promise: NaN and infinite indices read the same in every compile mode'.
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

        var references = TensorChecks.EveryCompileMode(circuit, [clampRead, zeroRead], sources);
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

    // 'differential: equals the dense one-hot masked sum on random tables, at both ends and out of range'.
    [Fact]
    public void Differential_equals_the_dense_one_hot_masked_sum_on_random_tables()
    {
        var random = Js.CreateRandom(0x1dea);
        double[] extremes = [-0.0, 0, LargestFloat32, -LargestFloat32, SmallestSubnormalFloat32];
        var comparisons = 0;
        var loweredDenseReads = 0;
        var tablesWithSeveralEntries = 0;
        for (var trial = 0; trial < 240; trial++)
        {
            var style = trial % 2 == 0 ? DenseStyle.Clamp : DenseStyle.ZeroAbove;
            var count = 1 + RandomInteger(random, 40);
            var pair = BuildPair(count, style, LargestFloat32);
            loweredDenseReads += TensorChecks.ProgramCounts(pair.Dense.CompileTensorProgram([pair.DenseRead])).GatherReads;
            if (count > 1) tablesWithSeveralEntries++;
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

            var denseReferences = TensorChecks.EveryCompileMode(pair.Dense, [pair.DenseRead], sources);
            var instructionReferences = TensorChecks.EveryCompileMode(pair.Instruction, [pair.InstructionRead], sources);
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
                pair.Dense.ValidateOneHotGroups(denseReferences[sourceIndex]);
                comparisons++;
            }
        }
        Assert.Equal(240 * 9, comparisons);
        // Each dense table with more than one entry lowered to a gather, so the comparison also
        // covers the gather reads of the tensor evaluator.
        Assert.True(loweredDenseReads >= tablesWithSeveralEntries, $"{loweredDenseReads} lowered reads, {tablesWithSeveralEntries} tables");
    }

    // 'signed zero: a selected -0 comes through the read, while the masked sum returns +0'.
    [Fact]
    public void Signed_zero_a_selected_negative_zero_comes_through_the_read_while_the_masked_sum_returns_positive_zero()
    {
        var pair = BuildPair(3, DenseStyle.ZeroAbove, 100);
        float[][] sources = [Source([0, -0.0, 5, 7]), Source([3, -0.0, 5, 7])];
        var instructionReferences = TensorChecks.EveryCompileMode(pair.Instruction, [pair.InstructionRead], sources);
        var denseReferences = TensorChecks.EveryCompileMode(pair.Dense, [pair.DenseRead], sources);
        Js.Is(-0.0, instructionReferences[0][pair.InstructionRead.Position]);
        Js.Is(0, denseReferences[0][pair.DenseRead.Position]);
        // Past the end, the 'zero' side reads +0.
        Js.Is(0, instructionReferences[1][pair.InstructionRead.Position]);
    }

    // 'infinite candidates: the read returns the selected value; the masked sum turns into NaN and stays dense'.
    [Fact]
    public void Infinite_candidates_the_read_returns_the_selected_value_and_the_masked_sum_turns_into_NaN()
    {
        var pair = BuildPair(4, DenseStyle.Clamp, double.PositiveInfinity);
        Assert.Equal(0, TensorChecks.ProgramCounts(pair.Dense.CompileTensorProgram([pair.DenseRead])).GatherReads);
        float[][] sources =
        [
            Source([1, double.PositiveInfinity, 5, 6, 7]),
            Source([0, double.PositiveInfinity, 5, 6, 7]),
            Source([2, 1, 2, double.NegativeInfinity, 7]),
        ];
        var instructionReferences = TensorChecks.EveryCompileMode(pair.Instruction, [pair.InstructionRead], sources);
        var denseReferences = TensorChecks.EveryCompileMode(pair.Dense, [pair.DenseRead], sources);
        Assert.Equal([5f, float.PositiveInfinity, float.NegativeInfinity], instructionReferences.Select(values => values[pair.InstructionRead.Position]));
        Assert.True(float.IsNaN(denseReferences[0][pair.DenseRead.Position]));
        // A selected infinity survives the masked sum when every other candidate is finite.
        Js.Is(double.PositiveInfinity, denseReferences[1][pair.DenseRead.Position]);
    }

    // 'fractional indices: the read floors, while the ReLU one-hot interpolates'.
    [Fact]
    public void Fractional_indices_the_read_floors_while_the_ReLU_one_hot_interpolates()
    {
        double[] rowHeights = [28, 44, 28, 60, 36, 28, 52, 40];
        var pair = BuildPair(rowHeights.Length, DenseStyle.Clamp, 1000);
        double[] fractionalIndices = [2.5, 2.25, 0.5, 6.75];
        var sources = fractionalIndices.Select(index => Source([index, .. rowHeights])).ToArray();
        var instructionReferences = TensorChecks.EveryCompileMode(pair.Instruction, [pair.InstructionRead], sources);
        for (var sourceIndex = 0; sourceIndex < fractionalIndices.Length; sourceIndex++)
        {
            var index = fractionalIndices[sourceIndex];
            var lowerPosition = (int)Math.Floor(index);
            var fraction = index - lowerPosition;
            var interpolated = rowHeights[lowerPosition] * (1 - fraction) + rowHeights[lowerPosition + 1] * fraction;
            Js.Is(rowHeights[lowerPosition], instructionReferences[sourceIndex][pair.InstructionRead.Position]);
            Js.Is(interpolated, pair.Dense.EvaluateReference(sources[sourceIndex])[pair.DenseRead.Position]);
            Js.Throws(() => pair.Instruction.ValidateIndexReads(instructionReferences[sourceIndex]), "index read wire");
            Js.Throws(() => pair.Dense.ValidateOneHotGroups(pair.Dense.EvaluateReference(sources[sourceIndex])), "one-hot");
        }
        // The guide's example: 44 from the masked sum against 28 from the read.
        Js.Is(44, pair.Dense.EvaluateReference(sources[0])[pair.DenseRead.Position]);
    }

    // 'duplicate candidates, composed index forms, and chained reads match plain JavaScript'.
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
        Wire[] roots = [.. cursors, composedRead, shifted];
        var sources = Enumerable.Range(0, 200).Select(_ =>
        {
            var source = new float[circuit.SourceSize];
            source[start.Position] = RandomInteger(random, 10);
            foreach (var wire in nextStarts) source[wire.Position] = RandomInteger(random, 10);
            foreach (var wire in payload) source[wire.Position] = (float)((random() * 2 - 1) * 1000);
            return source;
        }).ToArray();

        var references = TensorChecks.EveryCompileMode(circuit, roots, sources);
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

    // 'evenly spaced tables store a base and a stride; other tables store positions shared by every read'.
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
        var contiguousProgram = circuit.CompileTensorProgram([contiguous]);
        Assert.Equal(
            new ProgramCountsResult(LiveOperations: 1, Layers: 1, StoredWeights: 1, GatherReads: 0, IndexReads: 1),
            TensorChecks.ProgramCounts(contiguousProgram));
        var contiguousRead = contiguousProgram.Layers[0].IndexReads[0];
        Assert.Equal((count, 1, 1), (contiguousRead.CandidateCount, contiguousRead.CandidateBase, contiguousRead.CandidateStride));
        Assert.Empty(contiguousRead.CandidatePositions);
        Wire[] roots = [contiguous, reversed, everyOther, repeatedWire, scattered, scatteredNext];
        var program = circuit.CompileTensorProgram(roots);
        var readsByWire = program.Layers[0].IndexReads.ToDictionary(read => Wire.FromPosition(program.WireOrder[circuit.SourceSize + read.Row]));
        Assert.Equal((count, -1), (readsByWire[reversed].CandidateBase, readsByWire[reversed].CandidateStride));
        var everyOtherRead = readsByWire[everyOther];
        Assert.Equal((2, 2, count / 2), (everyOtherRead.CandidateBase, everyOtherRead.CandidateStride, everyOtherRead.CandidateCount));
        var repeatedRead = readsByWire[repeatedWire];
        Assert.Equal((6, 0, 3), (repeatedRead.CandidateBase, repeatedRead.CandidateStride, repeatedRead.CandidateCount));
        foreach (var wire in new[] { reversed, everyOther, repeatedWire }) Assert.Empty(readsByWire[wire].CandidatePositions);
        Assert.Equal(count, readsByWire[scattered].CandidatePositions.Count);
        Assert.Same(readsByWire[scattered].CandidatePositions, readsByWire[scatteredNext].CandidatePositions);
        var sources = new[] { 0, 1, 511, 512, 1022, 1023 }.Select(at =>
        {
            var source = new float[circuit.SourceSize];
            source[0] = at;
            for (var position = 0; position < count; position++) source[values[position]] = position;
            return source;
        }).ToArray();

        var references = TensorChecks.EveryCompileMode(circuit, roots, sources);
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

    // 'a range declared on the index wire counts, also when the builder copies a small affine index into the read'.
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
        Assert.Equal(1, TensorChecks.ProgramCounts(circuit.CompileTensorProgram([read])).LiveOperations);
        // The declared range 0..3 can't reach the zero sides.
        Assert.Equal(new NumericRange(20, 55), circuit.Ranges[read.Position]);
        (double FirstRow, double Offset)[] cells = [(0, 0), (1, 2), (3, 0), (-1, 2), (2, 2), (-3, 1)];
        var sources = cells.Select(cell => Source([cell.FirstRow, cell.Offset, 20, 30, 40, 50])).ToArray();
        var references = TensorChecks.EveryCompileMode(circuit, [read], sources);
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
        // Inlining keeps the accepted range, although the target infers the copied form from undeclared cells again.
        var parent = new Circuit(6);
        var wireMap = parent.InlineCircuit(circuit, [.. Enumerable.Range(0, 6).Select(parent.Source)]);
        Assert.Equal(new NumericRange(20, 55), parent.Ranges[wireMap[read.Position].Position]);
        var parentReferences = TensorChecks.EveryCompileMode(parent, [wireMap[read.Position]], sources);
        for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            Js.Is(references[sourceIndex][read.Position], parentReferences[sourceIndex][wireMap[read.Position].Position]);
        }

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
        foreach (var values in TensorChecks.EveryCompileMode(wide, [narrowRead], wideSources))
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

    // 'validators reject malformed index read programs and circuits'. Handwired changes a structuredClone
    // of the program. This port makes each changed copy with a with expression.
    [Fact]
    public void Validators_reject_malformed_index_read_circuits()
    {
        var circuit = new Circuit(8);
        circuit.AssumeRange(circuit.Source(0), 0, 5);
        var gate = circuit.Reglu([(circuit.Source(1), 1)], 0, [(circuit.Source(2), 1)], 0);
        var contiguousRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(1), circuit.Source(2), circuit.Source(3), circuit.Source(4)]);
        var scatteredRead = circuit.IndexRead(circuit.Source(0), [circuit.Source(5), circuit.Source(1), circuit.Source(7), circuit.Source(6)]);
        var program = circuit.CompileTensorProgram([gate, contiguousRead, scatteredRead]);
        circuit.ValidateTensorProgram(program);
        Action Corrupt(Func<TensorLayer, TensorLayer> change) =>
            () => circuit.ValidateTensorProgram(program with { Layers = program.Layers.SetItem(0, change(program.Layers[0])) });
        TensorLayer ChangeRead(TensorLayer layer, int read, Func<TensorIndexRead, TensorIndexRead> change) =>
            layer with { IndexReads = layer.IndexReads.SetItem(read, change(layer.IndexReads[read])) };
        var contiguousIndex = program.Layers[0].IndexReads.ToList().FindIndex(read => read.CandidatePositions.Count == 0);
        var scatteredIndex = 1 - contiguousIndex;
        Js.Throws(Corrupt(layer => ChangeRead(layer, 0, read => read with { IndexRow = layer.OutputSize })), "index row must follow the gate rows");
        Js.Throws(Corrupt(layer => ChangeRead(layer, 1, read => read with { IndexRow = layer.IndexReads[0].IndexRow })), "index row must be unique");
        Js.Throws(Corrupt(layer => ChangeRead(layer, 1, read => read with { Row = layer.IndexReads[0].Row })), "output row must be unique");
        Js.Throws(Corrupt(layer => ChangeRead(layer, contiguousIndex, read => read with { CandidateBase = 7 })), "not available");
        Js.Throws(Corrupt(layer => ChangeRead(layer, contiguousIndex, read => read with { CandidateStride = -2 })), "not available");
        Js.Throws(
            Corrupt(layer => ChangeRead(layer, scatteredIndex, read => read with { CandidatePositions = UnsignedIndices.From([5, 1, 8, 6]) })),
            "not available");
        Js.Throws(Corrupt(layer => ChangeRead(layer, scatteredIndex, read => read with { CandidateCount = 3 })), "size mismatch");
        Js.Throws(Corrupt(layer => layer with { IndexReads = layer.IndexReads.RemoveAt(layer.IndexReads.Length - 1) }), "projection bias shape mismatch");
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

    private static float[] Source(double[] cells) => [.. cells.Select(cell => (float)cell)];

    private static Wire[] Wires(params IEnumerable<int> positions) => [.. positions.Select(Wire.FromPosition)];

    private static int RandomInteger(Func<double> random, int limit) => (int)Math.Floor(random() * limit);
}
