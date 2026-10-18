// No Handwired source. The tests of rule 1 of the phase 6 plan, task 1 (question 4): a bool used as an
// int is the bit wire with the grid 1 and the range 0..1, with no operation. They also test the phase 4
// carried items a (a bit with no grid 1 in an int form) and b (the Release form cgt.un of a bool
// against 0), and the forward step of search-step.ts, alone and inlined at 6 positions. The test run
// in Debug checks the Debug IL, and the test run in Release checks the Release IL.

using System.Reflection.Metadata;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class BitArithmeticTests
{
    private static TranslatedFunction Translate(string name) =>
        HandwiredTranslator.TranslateFunction(Input(typeof(BitArithmeticInputs), name));

    [Theory]
    [InlineData(nameof(BitArithmeticInputs.BitAsInt))]
    [InlineData(nameof(BitArithmeticInputs.ConstantPlusBit))]
    [InlineData(nameof(BitArithmeticInputs.TwoBits))]
    [InlineData(nameof(BitArithmeticInputs.IncrementAfterBit))]
    [InlineData(nameof(BitArithmeticInputs.BitTimesInt))]
    [InlineData(nameof(BitArithmeticInputs.BitAsFloat))]
    [InlineData(nameof(BitArithmeticInputs.BitTimesTwo))]
    [InlineData(nameof(BitArithmeticInputs.CountOfBits))]
    public void A_bool_used_as_a_number_gives_the_plain_values(string name)
    {
        var translated = Translate(name);
        AssertMatchesPlain(translated, 8101, ZeroSign.MayDiffer);
    }

    [Fact]
    public void A_bool_used_as_an_int_is_the_bit_wire_with_no_operation()
    {
        var translated = Translate(nameof(BitArithmeticInputs.BitAsInt));
        var circuit = translated.Table.Circuit;
        Assert.Empty(circuit.Operations);
        Assert.Equal(translated.Inputs[0].Wire, translated.Outputs[0].Wire);
    }

    // k + (b ? 1 : 0) is one exact affine operation k + b. The bit b gets the grid 1.
    [Fact]
    public void A_constant_plus_a_bit_is_one_affine_operation()
    {
        var translated = Translate(nameof(BitArithmeticInputs.ConstantPlusBit));
        var circuit = translated.Table.Circuit;
        Assert.Single(circuit.Operations);
        Assert.Equal(1d, circuit.Grids[translated.Outputs[0].Wire.Position]);
        Assert.Equal(new Sharpwired.Core.NumericRange(-100, 101), circuit.Ranges[translated.Outputs[0].Wire.Position]);
    }

    // Rule 1 is not under TranslationOptions.RecognizePatterns: with the patterns off, a bit as an int is still the
    // bit wire with the grid 1, and k + (b ? 1 : 0) is still one affine operation.
    [Fact]
    public void With_patterns_off_a_bool_used_as_an_int_is_still_the_bit_wire()
    {
        var options = new TranslationOptions { RecognizePatterns = false };
        var bitAsInt = HandwiredTranslator.TranslateFunction(Input(typeof(BitArithmeticInputs), nameof(BitArithmeticInputs.BitAsInt)), options);
        Assert.Empty(bitAsInt.Table.Circuit.Operations);
        var sum = HandwiredTranslator.TranslateFunction(Input(typeof(BitArithmeticInputs), nameof(BitArithmeticInputs.ConstantPlusBit)), options);
        Assert.Single(sum.Table.Circuit.Operations);
        Assert.Equal(1d, sum.Table.Circuit.Grids[sum.Outputs[0].Wire.Position]);
        AssertMatchesPlain(sum, 8102, ZeroSign.MayDiffer);
    }

    // x = c ? 1 : 0; if (b) x = x + 1: the gate of the int x has the grid 1.
    [Fact]
    public void An_increment_of_a_bit_under_a_branch_gives_a_gate_on_the_grid_1()
    {
        var translated = Translate(nameof(BitArithmeticInputs.IncrementAfterBit));
        var output = translated.Outputs[0].Wire.Position;
        Assert.Equal(1d, translated.Table.Circuit.Grids[output]);
        Assert.Equal(new Sharpwired.Core.NumericRange(0, 2), translated.Table.Circuit.Ranges[output]);
    }

    // Carried item b: Release writes b ? 1 : 0 as "ldarg b; ldc.i4.0; cgt.un". Debug writes a branch.
    // So the value tests above cover the Release form in the Release run.
    [Theory]
    [InlineData(nameof(BitArithmeticInputs.ConstantPlusBit))]
    [InlineData(nameof(BitArithmeticInputs.TwoBits))]
    [InlineData(nameof(BitArithmeticInputs.IncrementAfterBit))]
    [InlineData(nameof(BitArithmeticInputs.BitTimesInt))]
    public void The_test_input_has_the_il_form_of_its_configuration(string name)
    {
        var decoded = IlDecoder.Decode(Input(typeof(BitArithmeticInputs), name));
        var unsignedTests = decoded.Instructions.Count(instruction => instruction.OpCode == ILOpCode.Cgt_un);
#if DEBUG
        Assert.Equal(0, unsignedTests);
#else
        Assert.True(unsignedTests >= 1, $"{name} has no cgt.un in Release");
#endif
    }

    [Fact]
    public void The_forward_step_of_the_search_gives_the_plain_values()
    {
        var translated = Translate(nameof(BitArithmeticInputs.Forward));
        AssertMatchesPlain(translated, 8102, ZeroSign.MayDiffer);
    }

    [Fact]
    public void The_forward_step_inlined_at_6_positions_gives_the_plain_values()
    {
        var translated = Translate(nameof(BitArithmeticInputs.ForwardUnrolled));
        AssertMatchesPlain(translated, 8103, ZeroSign.MayDiffer);
    }
}
