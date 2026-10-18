// No Handwired source. Handwired has no translator. The per-construct tests of task 4 for inlining
// (plan, section "Decisions", question 3, "Inlining decision"). The translator inlines each call to
// a [Handwired] method. The value of the call emits at the call, so the helper result is one wire.
// Each test builds the expected circuit by hand and checks AssertSameOperations. It also compares
// the translated program with the plain method over 200 seeded source tables in Debug and in Release.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class InlineTests
{
    private static CellSpec Hundred => Cell.Input(new NumericRange(-100, 100));

    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), name));

    // The helper has two ret instructions in Release. Its AND emits at the call, and the outer AND
    // reads the helper wire as one literal.
    [Fact]
    public void A_helper_with_two_returns_gives_its_and_at_the_call_and_the_outer_and_reads_one_literal()
    {
        var expected = new ExpectedProgram(Cell.InputBit(), Hundred, Hundred);
        var (armed, x, y) = (expected.Cells[0], expected.Cells[1], expected.Cells[2]);
        var circuit = expected.Circuit;
        var c1 = circuit.Compare(x, Relation.GreaterOrEqual, circuit.Constant(0));
        var c2 = circuit.Compare(x, Relation.Less, circuit.Constant(10));
        var c3 = circuit.Compare(y, Relation.GreaterOrEqual, circuit.Constant(0));
        var c4 = circuit.Compare(y, Relation.Less, circuit.Constant(10));
        var inside = circuit.Reglu([], 1, [(c1, 1), (c2, 1), (c3, 1), (c4, 1)], -3);
        circuit.Reglu([], 1, [(armed, 1), (inside, 1)], -1);
        var translated = Translate(nameof(CallInputs.ArmedInside));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 801, ZeroSign.Exact);
    }

    [Fact]
    public void A_helper_affine_result_emits_at_the_call()
    {
        var expected = new ExpectedProgram(Cell.Input(Cell.AnyNumber));
        var half = expected.Circuit.Affine([(expected.Cells[0], 0.5)]);
        expected.Circuit.RoundAffine([(half, 1)], 1);
        var translated = Translate(nameof(CallInputs.HalfPlusOne));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 802, ZeroSign.MayDiffer);
    }

    // In an instance method of a struct, ldarg.0 loads the address of this. Each getter call reads a
    // field through it (final fix wave, item I).
    [Fact]
    public void In_an_instance_method_of_a_struct_the_first_argument_is_the_address_of_this()
    {
        var width = typeof(Span).GetMethod(nameof(Span.Width))!;
        var ssa = SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(width)));
        var addresses = ssa.Blocks.SelectMany(block => block.Operations).Where(operation => operation.Address is { Root: { Kind: SsaVariableKind.Argument, Index: 0 } }).ToArray();
        Assert.Equal(2, addresses.Length);
        Assert.All(addresses, address => Assert.Equal(SsaTypeKind.Address, address.Type.Kind));
    }

    // A call on the address of a struct argument passes the struct value as this.
    [Fact]
    public void An_instance_method_of_a_struct_that_reads_this_is_inlined()
    {
        var expected = new ExpectedProgram(Hundred, Hundred);
        var (low, high) = (expected.Cells[0], expected.Cells[1]);
        expected.Circuit.Affine([(high, 1), (low, -1)]);
        var translated = Translate(nameof(CallInputs.SpanWidth));
        Assert.Equal(["low", "high"], translated.Inputs.Select(input => input.Path));
        expected.AssertSameOperations(translated);
        AssertMatchesPlain(translated, 803, ZeroSign.MayDiffer);
    }
}
