// No Handwired source. Handwired has no translator. The seeded differential test of the affine
// emission rule (plan, section "Emission rules"; review of task 4, finding 1): each generated chain
// of Inputs/ChainInputs.cs translates, and the program equals the plain method on each output leaf
// over 200 seeded source tables. An affine output can give +0 where C# gives -0 (plan, decision 8).

using System.Globalization;
using System.Reflection;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class ChainTests
{
    public static TheoryData<string> Chains()
    {
        var names = new TheoryData<string>();
        foreach (var method in typeof(ChainInputs).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(method => method.Name.StartsWith("Chain", StringComparison.Ordinal)))
        {
            names.Add(method.Name);
        }

        return names;
    }

    [Fact]
    public void The_input_file_has_at_least_30_chains() => Assert.True(Chains().Count >= 30);

    [Theory]
    [MemberData(nameof(Chains))]
    public void A_generated_chain_equals_the_plain_method(string name)
    {
        var translated = HandwiredTranslator.TranslateFunction(Input(typeof(ChainInputs), name));
        AssertMatchesPlain(translated, 1000 + int.Parse(name[5..], CultureInfo.InvariantCulture), ZeroSign.MayDiffer);
    }
}
