// Handwired source: template/toggle.ts, lines 39-84 (buildToggle, which returns the cells, the
// outputs and the finished table). Handwired has no translator. This file translates the plain C#
// toggle (ToggleStep.Step) with the IL translator and gives it the shape of the builder toggle, so
// that ToggleMachine and ToggleWindowApp run it without a change.

using System.Reflection;
using Sharpwired.Core;
using Sharpwired.IL;
using Toggle.Builder;

namespace Toggle.CSharp;

/// <summary>The C# toggle: the translated program of ToggleStep.Step as a <see cref="BuiltToggle"/>.</summary>
public static class CSharpToggle
{
    /// <summary>The option that opens the builder toggle in place of the translated C# toggle.</summary>
    public const string BuilderOption = "--builder";

    /// <summary>Translate ToggleStep.Step. Each call translates the method again.</summary>
    public static TranslatedStep Translate()
    {
        var method = typeof(ToggleStep).GetMethod(nameof(ToggleStep.Step), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("ToggleStep has no public static method Step");
        return HandwiredTranslator.TranslateStep(method);
    }

    /// <summary>Translate ToggleStep.Step and return its cells, its output wires and its table.</summary>
    public static BuiltToggle Build() => Build(Translate());

    /// <summary>Map the cell paths and the output paths of a translated toggle step to a <see cref="BuiltToggle"/>.</summary>
    public static BuiltToggle Build(TranslatedStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        PointerCells Pointer(string name) => new(step.Cell($"{name}.active"), step.Cell($"{name}.x"), step.Cell($"{name}.y"));
        Wire[] springMatrix = [.. Enumerable.Range(0, 4).Select(index => step.Cell($"springMatrix[{index}]"))];
        var cells = new ToggleCells(
            step.Cell("viewportWidth"),
            step.Cell("viewportHeight"),
            Pointer("down"),
            Pointer("up"),
            step.Cell("cancel"),
            step.Cell("on"),
            step.Cell("armed"),
            springMatrix,
            step.Cell("settleCheck"),
            step.Cell("snap"),
            new ThumbCells(step.Cell("thumb.displacement"), step.Cell("thumb.velocity"), step.Cell("thumb.destination")));
        var output = new ToggleOutputWires(
            step.Output("trackLeft"),
            step.Output("trackTop"),
            step.Output("thumbOffset"),
            step.Output("on"),
            step.Output("armed"),
            step.Output("active"));
        return new BuiltToggle(cells, output, step.Table);
    }

    /// <summary>
    /// Remove the option --builder from the command line, and tell whether it was there. The rest goes
    /// to WindowRunOptions.Parse. Throws <see cref="ArgumentException"/> when the option is given two times.
    /// </summary>
    public static (bool Builder, string[] WindowArgs) SplitBuilderOption(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var count = args.Count(arg => arg == BuilderOption);
        if (count > 1)
        {
            throw new ArgumentException($"{BuilderOption} is given two times");
        }

        return (count == 1, [.. args.Where(arg => arg != BuilderOption)]);
    }
}
