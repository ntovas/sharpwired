// No Handwired source. The tests of the phase 4 carried item c (phase 6 plan, task 1): brtrue and
// brfalse on an int constant other than 0 or 1. The branch follows the edge of k != 0 and emits no
// operation. The test run in Debug checks the Debug IL, and the test run in Release checks the Release IL.

using System.Reflection.Metadata;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class BranchOnConstantTests
{
    private static TranslatedFunction Translate(string name) =>
        HandwiredTranslator.TranslateFunction(Input(typeof(BitArithmeticInputs), name));

    [Theory]
    [InlineData(nameof(BitArithmeticInputs.ModeBranch), 1)]
    [InlineData(nameof(BitArithmeticInputs.ModeLoop), 2)]
    [InlineData(nameof(BitArithmeticInputs.FirstOfThree), 2)]
    public void A_branch_on_an_int_constant_follows_one_edge(string name, int operations)
    {
        var translated = Translate(name);
        AssertMatchesPlain(translated, 8201, ZeroSign.MayDiffer);
        Assert.Equal(operations, translated.Table.Circuit.Operations.Count);
    }

    // The inputs hold the IL form of the carried item: brtrue or brfalse that reads an int constant
    // other than 0 or 1. Release folds "Mode != 0" to "ldc.i4.2; brtrue" or removes the branch. Debug
    // writes "s == 0 ? x : y" as "brfalse" on the local s.
    [Fact]
    public void An_input_branches_on_an_int_value_in_each_configuration()
    {
        var names = new[] { nameof(BitArithmeticInputs.ModeBranch), nameof(BitArithmeticInputs.ModeLoop), nameof(BitArithmeticInputs.FirstOfThree) };
        var branches = names.Sum(name => IlDecoder.Decode(Input(typeof(BitArithmeticInputs), name)).Instructions
            .Count(instruction => instruction.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s));
        Assert.True(branches >= 1);
    }
}
