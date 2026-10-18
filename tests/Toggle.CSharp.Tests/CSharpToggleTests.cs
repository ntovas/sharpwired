// Handwired source: template/toggle.ts (buildToggle and toggleCells) and template/toggle.test.ts
// (scriptedTrace and its tests). Handwired has no translator, so these tests have no Handwired twin.
// They check the plain C# toggle of Toggle.CSharp (ToggleStep.Step) after the IL translator. They run
// the five checks of decision 1 in the phase 4 plan (TranslatedToggleChecks). They also check the C#
// parts of the sample.

using Sharpwired.Core.Tests;
using Sharpwired.IL;
using Sharpwired.Timing;
using Toggle.Builder;
using Toggle.CSharp;
using Xunit.Abstractions;

namespace Toggle.CSharp.Tests;

public class CSharpToggleTests(ITestOutputHelper output) : TranslatedToggleChecks(output)
{
    protected override TranslatedStep Translate() => CSharpToggle.Translate();

    // An attribute argument must be a constant, so the plain types write the matrix bound as a literal.
    [Fact]
    public void The_matrix_bound_literal_equals_the_spring_matrix_bound_of_the_toggle_spring()
    {
        Js.Is(SpringMatrix.SpringMatrixBound(ToggleLayout.Spring), ToggleStep.MatrixBound);
    }

    [Fact]
    public void The_builder_option_selects_the_builder_toggle_and_leaves_the_window_options()
    {
        var (builder, rest) = CSharpToggle.SplitBuilderOption(["--frames", "120", "--builder", "--screenshot", "a.png"]);
        Assert.True(builder);
        Assert.Equal(["--frames", "120", "--screenshot", "a.png"], rest);
        var (none, all) = CSharpToggle.SplitBuilderOption(["--frames", "120"]);
        Assert.False(none);
        Assert.Equal(["--frames", "120"], all);
        Js.Throws(() => CSharpToggle.SplitBuilderOption(["--builder", "--builder"]), "--builder is given two times");
    }
}
