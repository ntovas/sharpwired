// Handwired source: template/toggle.ts (buildToggle and toggleCells) and template/toggle.test.ts
// (scriptedTrace and its tests). Handwired has no translator, so these tests have no Handwired twin.
// They check the plain F# toggle of Toggle.FSharp (ToggleStep.step) after the IL translator. They run
// the five checks of decision 1 in the phase 4 plan (TranslatedToggleChecks). They also check the
// equality of the F# and the C# translated operation lists.

using System.Reflection;
using Sharpwired.Core;
using Sharpwired.Core.Tests;
using Sharpwired.IL;
using Sharpwired.Timing;
using Toggle.Builder;
using Toggle.CSharp;
using Toggle.CSharp.Tests;
using Xunit.Abstractions;
using FSharpLiterals = Toggle.FSharp.ToggleLiterals;
using FSharpStep = Toggle.FSharp.ToggleStep;

namespace Toggle.FSharp.Tests;

public class FSharpToggleTests(ITestOutputHelper output) : TranslatedToggleChecks(output)
{
    private static TranslatedStep TranslateFSharp()
    {
        var method = typeof(FSharpStep).GetMethod("step", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Toggle.FSharp.ToggleStep has no public static method step");
        return HandwiredTranslator.TranslateStep(method);
    }

    protected override TranslatedStep Translate() => TranslateFSharp();

    // The F# compiler and Roslyn write other IL for the same statements (plan, decisions 1 and 3). The
    // translator gives the same operations from both.
    [Fact]
    public void The_FSharp_translated_operation_list_equals_the_CSharp_translated_operation_list()
    {
        var fsharp = TranslateFSharp();
        var csharp = CSharpToggle.Translate();
        TensorChecks.AssertSameOperations(csharp.Table.Circuit.Operations, fsharp.Table.Circuit.Operations);
        Assert.Equal(csharp.Cells.Select(cell => cell.Path), fsharp.Cells.Select(cell => cell.Path));
        Assert.Equal<NamedWire>(csharp.Outputs, fsharp.Outputs);
        Assert.Equal<Wire>(csharp.Table.NextSource, fsharp.Table.NextSource);
    }

    // An F# attribute argument must be a literal, so the F# records write the matrix bound as a literal.
    [Fact]
    public void The_FSharp_matrix_bound_literal_equals_the_spring_matrix_bound_of_the_toggle_spring()
    {
        Js.Is(SpringMatrix.SpringMatrixBound(ToggleLayout.Spring), FSharpLiterals.MatrixBound);
    }
}
