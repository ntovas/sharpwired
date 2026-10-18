// No Handwired source. The seeded differential test of the phase 6 plan, task 1. Each generated method
// of Inputs/DifferentialInputs*.cs translates, and the program equals the plain method on each output
// leaf over 200 seeded source tables. The methods mix the new translator forms with the forms of
// phase 4. The test run in Debug checks the Debug IL, and the test run in Release checks the Release IL.
// An affine or a gate output can give +0 where C# gives -0 (phase 4 plan, decision 8).

using System.Globalization;
using System.Reflection;
using Sharpwired.Core;
using Sharpwired.IL.Tests.Inputs;
using Xunit.Abstractions;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class DifferentialTests(ITestOutputHelper output)
{
    private static IEnumerable<MethodInfo> Methods() =>
        typeof(DifferentialInputs).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Differential", StringComparison.Ordinal))
            .OrderBy(method => method.Name, StringComparer.Ordinal);

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (var method in Methods())
        {
            names.Add(method.Name);
        }

        return names;
    }

    [Fact]
    public void The_input_files_have_at_least_300_methods() => Assert.True(Methods().Count() >= 300);

    [Theory]
    [MemberData(nameof(Names))]
    public void A_generated_method_equals_the_plain_method(string name)
    {
        var translated = HandwiredTranslator.TranslateFunction(Input(typeof(DifferentialInputs), name));
        AssertMatchesPlain(translated, 9000 + int.Parse(name["Differential".Length..], CultureInfo.InvariantCulture), ZeroSign.MayDiffer);
    }

    // The new rules fire in the generated methods: zero reads (rule 3), gated reads (rule 4) and reads
    // with fewer candidates than their array (rule 5). The counts go to the ledger of the task.
    [Fact]
    public void The_generated_methods_use_each_new_read_rule()
    {
        int zeroReads = 0, gatedReads = 0, cutReads = 0;
        foreach (var method in Methods())
        {
            var translated = HandwiredTranslator.TranslateFunction(method);
            var arrays = method.GetParameters().Where(parameter => parameter.ParameterType.IsArray).Select(parameter =>
                parameter.GetCustomAttribute<Sharpwired.Annotations.FixedLengthAttribute>()!.Length).ToHashSet();
            foreach (var operation in translated.Table.Circuit.Operations.Where(operation => operation.IndexRead is not null))
            {
                var read = operation.IndexRead!;
                if (read.Below == OutOfRange.Zero) zeroReads++;
                if (read.Below == OutOfRange.Clamp && read.Above == OutOfRange.Zero) gatedReads++;
                if (!arrays.Contains(read.Table.Candidates.Length)) cutReads++;
            }
        }

        output.WriteLine($"zero reads {zeroReads}, gated reads {gatedReads}, cut reads {cutReads}");
        Assert.True(zeroReads >= 50, $"{zeroReads} zero reads");
        Assert.True(gatedReads >= 50, $"{gatedReads} gated reads");
        Assert.True(cutReads >= 50, $"{cutReads} cut reads");
    }
}
