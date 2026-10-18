// No Handwired source. The gate range tests of the final fix wave of phase 4 (items B and C). A gate
// returns exactly one side, up to the sign of zero (execution-model.md, "Macro contracts"). So the
// translator declares the union of the side ranges and the finer side grid on the gate result. Each
// test compares the translated program with the plain method over seeded source tables.

using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class GateRangeTests
{
    private static TranslatedFunction Translate(string name) => HandwiredTranslator.TranslateFunction(Input(typeof(GateRangeInputs), name));

    // The seeded code points are in 0..1114111, so they almost never hit 123. Each third element of
    // these tables is 123.
    private static IEnumerable<float[]> WithBraces(TranslatedFunction translated, int seed)
    {
        foreach (var source in SeededSources.Create(translated.Table, 20, seed))
        {
            for (var index = seed % 3; index < source.Length; index += 3)
            {
                source[index] = 123;
            }

            yield return source;
        }
    }

    [Theory]
    [InlineData(nameof(GateRangeInputs.BraceDepth))]
    [InlineData(nameof(GateRangeInputs.BraceDepthFloat))]
    public void A_gate_in_each_of_512_copies_of_a_constant_loop_gives_the_plain_values(string name)
    {
        var translated = Translate(name);
        AssertMatchesPlain(translated, 7101, ZeroSign.MayDiffer);
        foreach (var source in WithBraces(translated, 7102))
        {
            AssertMatchesPlainAt(translated, source, ZeroSign.MayDiffer);
        }

        Assert.Equal(new Sharpwired.Core.NumericRange(0, 512), translated.Table.Circuit.Ranges[translated.Outputs[0].Wire.Position]);
    }

    [Fact]
    public void A_bool_state_machine_over_512_code_points_gives_the_plain_values()
    {
        var translated = Translate(nameof(GateRangeInputs.StringState));
        AssertMatchesPlain(translated, 7103, ZeroSign.Exact);
        var source = SeededSources.Create(translated.Table, 1, 7104)[0];
        foreach (var count in new[] { 0, 1, 2, 255, 511, 512 })
        {
            source[0] = count;
            AssertMatchesPlainAt(translated, source, ZeroSign.Exact);
        }
    }

    [Theory]
    [InlineData(nameof(GateRangeInputs.ChosenMinusThree))]
    [InlineData(nameof(GateRangeInputs.SelfDifference))]
    [InlineData(nameof(GateRangeInputs.KeptChoice))]
    public void An_int_gate_or_an_int_form_with_no_terms_gives_an_exact_int_form(string name)
    {
        var translated = Translate(name);
        AssertMatchesPlain(translated, 7105, ZeroSign.MayDiffer);
    }

    // i0 - i0 folds to the constant 0: the program has no operation.
    [Fact]
    public void An_int_form_with_no_terms_folds_to_the_constant_zero()
    {
        var translated = Translate(nameof(GateRangeInputs.SelfDifference));
        Assert.Equal(0d, translated.Table.Circuit.Constants[translated.Outputs[0].Wire.Position]);
    }
}
