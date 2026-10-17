// No Handwired source. Helpers for the translator tests of task 3: the input methods, the path of an
// input file, the value comparison of a translated program with its plain method over seeded source
// tables, and the check of a refusal message with the file and line of a marker comment.

using System.Reflection;
using System.Runtime.CompilerServices;
using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.IL.Tests;

/// <summary>How a value test compares the outputs of the program and of the plain method.</summary>
internal enum ZeroSign
{
    /// <summary>Object.is: +0 and -0 differ.</summary>
    Exact,

    /// <summary>
    /// The operator ==: +0 equals -0. An affine operation adds its terms to f(bias), so a sum with the
    /// bias 0 starts at +0 and gives +0 where C# gives -0 (Circuit.EvaluateTerms). Select,
    /// MinimumExact, MaximumExact and Multiply also allow a different sign of zero (execution-model.md,
    /// "Macro contracts"). Plan decision 7.
    /// </summary>
    MayDiffer,
}

internal static class TranslationSupport
{
    public const int SeededTableCount = 200;

    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static MethodInfo Input(Type type, string name) =>
        type.GetMethod(name, Static) ?? throw new InvalidOperationException($"no input {type.Name}.{name}");

    public static string InputPath(string fileName, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Inputs", fileName);

    /// <summary>
    /// Compares the program evaluator with the plain method over the seeded source tables, for each
    /// output leaf.
    /// </summary>
    public static void AssertMatchesPlain(TranslatedFunction translated, int seed, ZeroSign zeroSign)
    {
        var evaluator = translated.CreateEvaluator();
        var sources = SeededSources.Create(translated.Table, SeededTableCount, seed);
        foreach (var source in sources)
        {
            var program = evaluator.Evaluate(source).Outputs;
            var plain = translated.EvaluatePlain(source);
            Assert.Equal(plain.Length, program.Length);
            for (var leaf = 0; leaf < plain.Length; leaf++)
            {
                AssertSameValue(plain[leaf], program[leaf], zeroSign, $"output {translated.Outputs[leaf].Path} for [{string.Join(", ", source)}]");
            }
        }
    }

    public static void AssertSameValue(float plain, float program, ZeroSign zeroSign, string context)
    {
        var same = zeroSign == ZeroSign.Exact ? Js.ObjectIs(plain, program) : plain == program;
        Assert.True(same, $"plain {plain:R}, program {program:R}, {context}");
    }

    /// <summary>The message of a refusal at the marker line of an input file.</summary>
    public static string RefusalMessage(string fileName, string marker, string construct, string reason)
    {
        var path = InputPath(fileName);
        return $"{path}:{FrontEndSupport.MarkerLine(path, marker)}: {construct} is not supported in a [Handwired] method: {reason}";
    }

    /// <summary>Checks that the action throws a TranslationException with the full message.</summary>
    public static TranslationException AssertRefused(Action translate, string fileName, string marker, string construct, string reason)
    {
        var error = Assert.Throws<TranslationException>(translate);
        Assert.Equal(RefusalMessage(fileName, marker, construct, reason), error.Message);
        Assert.Equal(construct, error.Construct);
        Assert.Equal(reason, error.Reason);
        return error;
    }
}

/// <summary>
/// An expected program built by hand: the cells in the order of the walk, then the builder calls of
/// the test. The cell names do not change the operations, so the cells are named c0, c1 and so on.
/// </summary>
internal sealed class ExpectedProgram
{
    private readonly SourceTable table = new();

    public ExpectedProgram(params CellSpec[] cells)
    {
        Cells = [.. cells.Select((cell, index) => table.Declare($"c{index}", cell))];
        Circuit = table.CreateCircuit();
    }

    public Wire[] Cells { get; }

    public Circuit Circuit { get; }

    /// <summary>Finishes the expected table and compares its operations with the translated operations.</summary>
    public void AssertSameOperations(TranslatedFunction translated) =>
        TensorChecks.AssertSameOperations(table.Finish().Circuit.Operations, translated.Table.Circuit.Operations);
}
