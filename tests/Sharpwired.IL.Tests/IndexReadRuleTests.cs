// No Handwired source. The tests of the rules 2 to 5 of the phase 6 plan, task 1 (question 4): the
// clamp read, Hw.ReadOrZero, the gated read and the candidate cut. Each rule gives its operations on
// its smallest method, each counterexample keeps the form of phase 4, each method equals its plain
// method, and with RecognizePatterns = false the rules 2, 4 and 5 give the operations of phase 4.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class IndexReadRuleTests
{
    private static readonly TranslationOptions PatternsOff = new() { RecognizePatterns = false };

    private static readonly CellSpec Ten = Cell.Input(new NumericRange(-10, 10));

    private static TranslatedFunction Translate(string name, TranslationOptions? options = null) =>
        HandwiredTranslator.TranslateFunction(Input(typeof(IndexReadInputs), name), options ?? TranslationOptions.Default);

    // The operations that are not constants: constant wires of a static table are operations too.
    private static List<Operation> Working(TranslatedFunction translated) =>
        [.. translated.Table.Circuit.Operations.Where((_, index) =>
            translated.Table.Circuit.Constants[translated.Table.Circuit.SourceSize + index] is null)];

    private static Operation SingleRead(TranslatedFunction translated)
    {
        var operation = Assert.Single(Working(translated));
        Assert.Equal(OperationKind.Index, operation.Kind);
        return operation;
    }

    private static Wire CellOf(TranslatedFunction translated, string path) => translated.Inputs.Single(input => input.Path == path).Wire;

    // Each int value of the index cell, on one seeded table.
    private static void AssertEachIndex(TranslatedFunction translated, string indexPath, int seed, ZeroSign zeroSign)
    {
        var index = CellOf(translated, indexPath).Position;
        var range = translated.Table.Circuit.Ranges[index];
        foreach (var source in SeededSources.Create(translated.Table, 4, seed))
        {
            for (var value = (int)range.Minimum; value <= (int)range.Maximum; value++)
            {
                source[index] = value;
                AssertMatchesPlainAt(translated, source, zeroSign);
            }
        }
    }

    [Theory]
    [InlineData(nameof(IndexReadInputs.ClampRead), "i")]
    [InlineData(nameof(IndexReadInputs.ClampReadStatic), "depth")]
    [InlineData(nameof(IndexReadInputs.ClampRange), "depth")]
    [InlineData(nameof(IndexReadInputs.ClampReadInner), "i")]
    [InlineData(nameof(IndexReadInputs.ClampReadTwice), "i")]
    [InlineData(nameof(IndexReadInputs.ZeroRead), "i")]
    [InlineData(nameof(IndexReadInputs.ZeroReadStatic), "depth")]
    [InlineData(nameof(IndexReadInputs.ZeroReadOutside), "i")]
    [InlineData(nameof(IndexReadInputs.ZeroReadPlusOne), "i")]
    [InlineData(nameof(IndexReadInputs.GatedRead), "c")]
    [InlineData(nameof(IndexReadInputs.GatedReadTwoBits), "c")]
    [InlineData(nameof(IndexReadInputs.GatedClampRead), "c")]
    [InlineData(nameof(IndexReadInputs.GatedIntRead), "c")]
    [InlineData(nameof(IndexReadInputs.CutRead), "c")]
    [InlineData(nameof(IndexReadInputs.StepAnd), "c")]
    [InlineData(nameof(IndexReadInputs.StepRow), "c")]
    public void A_method_with_a_read_rule_gives_the_plain_values(string name, string indexPath)
    {
        var translated = Translate(name);
        AssertMatchesPlain(translated, 8301, ZeroSign.MayDiffer);
        AssertEachIndex(translated, indexPath, 8302, ZeroSign.MayDiffer);
    }

    // Rule 2: one index read with the clamp rule, at the index i itself.
    [Fact]
    public void A_read_at_a_clamp_to_the_table_bounds_is_one_index_read_with_the_clamp_rule()
    {
        var translated = Translate(nameof(IndexReadInputs.ClampRead));
        var read = SingleRead(translated);
        Assert.Equal(OutOfRange.Clamp, read.IndexRead!.Below);
        Assert.Equal(OutOfRange.Clamp, read.IndexRead.Above);
        Assert.Equal([new Term(CellOf(translated, "i"), 1)], read.GateTerms.ToArray());
        Assert.Equal(0, read.GateBias);
        Assert.Equal(8, read.IndexRead.Table.Candidates.Length);

        var table = Translate(nameof(IndexReadInputs.ClampReadStatic));
        Assert.Equal(OutOfRange.Clamp, SingleRead(table).IndexRead!.Above);
    }

    // Rule 2: the plan probe refused this line with the range 0..13224 of the index form.
    [Fact]
    public void A_read_at_a_form_of_a_clamp_reads_the_range_of_the_clamp()
    {
        var translated = Translate(nameof(IndexReadInputs.ClampRange));
        var read = Assert.Single(Working(translated), operation => operation.Kind == OperationKind.Index);
        Assert.Equal(new NumericRange(0, 424), read.IndexRead!.IndexRange);
        AssertRefused(() => Translate(nameof(IndexReadInputs.ClampRange), PatternsOff), "IndexReadInputs.cs", "clamp range",
            "an array index with a range past the array", "the index range 0..13224 must lie in 0..424");
    }

    // Counterexamples of rule 2: the clamp stays ClampExact, and the read reads the clamp result.
    [Theory]
    [InlineData(nameof(IndexReadInputs.ClampReadInner))]
    [InlineData(nameof(IndexReadInputs.ClampReadTwice))]
    public void A_clamp_to_other_bounds_or_with_a_second_reader_keeps_its_comparisons(string name)
    {
        var translated = Translate(name);
        var phase4 = Translate(name, PatternsOff);
        Assert.Equal(phase4.Table.Circuit.Operations.Count, translated.Table.Circuit.Operations.Count);
        Assert.Contains(translated.Table.Circuit.Operations, operation => operation.ComparisonBit);
        var read = Assert.Single(translated.Table.Circuit.Operations, operation => operation.Kind == OperationKind.Index);
        Assert.DoesNotContain(read.GateTerms, term => term.Wire == CellOf(translated, "i"));
    }

    // Rule 3: one index read with the zero rule on both sides.
    [Fact]
    public void Hw_ReadOrZero_is_one_index_read_with_the_zero_rule_on_both_sides()
    {
        var translated = Translate(nameof(IndexReadInputs.ZeroRead));
        var read = SingleRead(translated);
        Assert.Equal(OutOfRange.Zero, read.IndexRead!.Below);
        Assert.Equal(OutOfRange.Zero, read.IndexRead.Above);
        Assert.Equal([new Term(CellOf(translated, "i"), 1)], read.GateTerms.ToArray());

        var table = SingleRead(Translate(nameof(IndexReadInputs.ZeroReadStatic)));
        Assert.Equal(OutOfRange.Zero, table.IndexRead!.Below);
        Assert.Equal(17 * IndexReadInputs.EventCount, table.IndexRead.Table.Candidates.Length);

        var plusOne = Translate(nameof(IndexReadInputs.ZeroReadPlusOne));
        Assert.Equal(2, plusOne.Table.Circuit.Operations.Count);
        Assert.Equal(1d, plusOne.Table.Circuit.Grids[plusOne.Outputs[0].Wire.Position]);
    }

    [Fact]
    public void Hw_ReadOrZero_with_an_index_outside_the_table_is_the_constant_zero()
    {
        var translated = Translate(nameof(IndexReadInputs.ZeroReadOutside));
        Assert.DoesNotContain(translated.Table.Circuit.Operations, operation => operation.Kind == OperationKind.Index);
        Assert.Equal(0d, translated.Table.Circuit.Constants[translated.Outputs[0].Wire.Position]);
    }

    // Rule 4: createStepAnd of data-program.ts.
    [Fact]
    public void A_bit_and_a_bool_read_is_one_index_read_with_the_zero_rule_above()
    {
        var translated = Translate(nameof(IndexReadInputs.GatedRead));
        var read = SingleRead(translated);
        Assert.Equal(OutOfRange.Clamp, read.IndexRead!.Below);
        Assert.Equal(OutOfRange.Zero, read.IndexRead.Above);
        Assert.Equal(IndexReadInputs.ClassSlots, read.IndexRead.Table.Candidates.Length);
        Assert.Equal(IndexReadInputs.ClassSlots, read.GateBias);
        Assert.Equal(
            new[] { new Term(CellOf(translated, "bit"), -IndexReadInputs.ClassSlots), new Term(CellOf(translated, "c"), 1) }.OrderBy(term => term.Wire.Position),
            read.GateTerms.OrderBy(term => term.Wire.Position));
    }

    // Rule 4 with an AND of two literals: the NOT (1 - b, which the AND composes), the AND, and one
    // read that the AND gates.
    [Fact]
    public void An_and_of_two_bits_and_a_bool_read_is_one_and_and_one_index_read()
    {
        var translated = Translate(nameof(IndexReadInputs.GatedReadTwoBits));
        var circuit = translated.Table.Circuit;
        Assert.Equal(3, circuit.Operations.Count);
        var and = circuit.SourceSize + circuit.Operations.ToList().FindIndex(operation => operation.Kind == OperationKind.Reglu);
        var read = Assert.Single(circuit.Operations, operation => operation.Kind == OperationKind.Index);
        Assert.Equal(OutOfRange.Zero, read.IndexRead!.Above);
        Assert.Contains(read.GateTerms, term => term.Wire.Position == and && term.Coefficient == -IndexReadInputs.ClassSlots);
    }

    // Counterexamples of rule 4: an index that is not proven in the table, and an int table.
    [Fact]
    public void A_clamped_index_or_an_int_table_keeps_the_read_and_its_gate()
    {
        var clamped = Translate(nameof(IndexReadInputs.GatedClampRead));
        var bit = CellOf(clamped, "bit");
        var read = Assert.Single(clamped.Table.Circuit.Operations, operation => operation.Kind == OperationKind.Index);
        Assert.Equal(OutOfRange.Clamp, read.IndexRead!.Above);
        Assert.DoesNotContain(read.GateTerms, term => term.Wire == bit);
        Assert.Equal(2, Working(clamped).Count);

        var integer = Translate(nameof(IndexReadInputs.GatedIntRead));
        var intRead = Assert.Single(integer.Table.Circuit.Operations, operation => operation.Kind == OperationKind.Index);
        Assert.Equal(OutOfRange.Clamp, intRead.IndexRead!.Above);
        Assert.DoesNotContain(intRead.GateTerms, term => term.Wire == CellOf(integer, "bit"));
    }

    // Rule 5: the read at k × 17 + c registers the 17 cells of its pair, and its index form is c.
    [Fact]
    public void A_read_at_k_times_17_plus_c_has_a_candidate_table_of_17_wires()
    {
        var translated = Translate(nameof(IndexReadInputs.CutRead));
        var read = SingleRead(translated);
        const int First = (3 * IndexReadInputs.States + 5) * IndexReadInputs.ClassSlots;
        Assert.Equal(Enumerable.Range(First, IndexReadInputs.ClassSlots).Select(cell => CellOf(translated, $"t[{cell}]")), read.IndexRead!.Table.Candidates);
        Assert.Equal([new Term(CellOf(translated, "c"), 1)], read.GateTerms.ToArray());
        Assert.Equal(0, read.GateBias);

        var step = SingleRead(Translate(nameof(IndexReadInputs.StepAnd)));
        Assert.Equal(IndexReadInputs.ClassSlots, step.IndexRead!.Table.Candidates.Length);
        Assert.Equal(OutOfRange.Zero, step.IndexRead.Above);
    }

    // The search row: 64 gated reads, each with a table of 17 cells, and the ORs of the rows.
    [Fact]
    public void The_search_row_has_one_read_for_each_pair()
    {
        var translated = Translate(nameof(IndexReadInputs.StepRow));
        var reads = translated.Table.Circuit.Operations.Where(operation => operation.Kind == OperationKind.Index).ToList();
        Assert.Equal(IndexReadInputs.States * IndexReadInputs.States, reads.Count);
        Assert.All(reads, read => Assert.Equal(IndexReadInputs.ClassSlots, read.IndexRead!.Table.Candidates.Length));
        Assert.True(translated.Table.Circuit.Operations.Count < Translate(nameof(IndexReadInputs.StepRow), PatternsOff).Table.Circuit.Operations.Count);
    }

    // RecognizePatterns = false: the clamp gives ClampExact and the read of phase 4.
    [Fact]
    public void With_patterns_off_a_clamp_read_gives_the_operations_of_phase_4()
    {
        var expected = new ExpectedProgram([.. Enumerable.Repeat(Ten, 8), Cell.Input(new NumericRange(-20, 20), grid: 1)]);
        var circuit = expected.Circuit;
        var clamp = circuit.ClampExact(expected.Cells[8], circuit.Constant(0), circuit.Constant(7));
        circuit.IndexRead(clamp, expected.Cells[..8], OutOfRange.Clamp);
        expected.AssertSameOperations(Translate(nameof(IndexReadInputs.ClampRead), PatternsOff));
    }

    // RecognizePatterns = false: the read registers all 1,088 cells, and its index is an affine operation.
    [Fact]
    public void With_patterns_off_a_read_at_k_times_17_plus_c_gives_the_operations_of_phase_4()
    {
        const int Cells = IndexReadInputs.ClassSlots * IndexReadInputs.States * IndexReadInputs.States;
        var expected = new ExpectedProgram([.. Enumerable.Repeat(Cell.InputBit(), Cells), Cell.Input(new NumericRange(0, 16), grid: 1)]);
        var circuit = expected.Circuit;
        var index = circuit.Affine([(expected.Cells[Cells], 1)], (3 * IndexReadInputs.States + 5) * IndexReadInputs.ClassSlots);
        circuit.IndexRead(index, expected.Cells[..Cells], OutOfRange.Clamp);
        expected.AssertSameOperations(Translate(nameof(IndexReadInputs.CutRead), PatternsOff));
    }

    // RecognizePatterns = false: the read, then the AND of the bit and the read.
    [Fact]
    public void With_patterns_off_a_bit_and_a_bool_read_gives_the_operations_of_phase_4()
    {
        var expected = new ExpectedProgram([Cell.InputBit(), .. Enumerable.Repeat(Cell.InputBit(), IndexReadInputs.ClassSlots), Cell.Input(new NumericRange(0, 16), grid: 1)]);
        var circuit = expected.Circuit;
        var read = circuit.IndexRead(expected.Cells[^1], expected.Cells[1..^1], OutOfRange.Clamp);
        circuit.Reglu([], 1, [(expected.Cells[0], 1), (read, 1)], -1);
        expected.AssertSameOperations(Translate(nameof(IndexReadInputs.GatedRead), PatternsOff));
    }
}
