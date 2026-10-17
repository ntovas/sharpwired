// No Handwired source. Helpers for the tests of the IL front end: the input methods, the paths of
// the input source files, the line of a marker comment, and a text form of a decoded method.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sharpwired.IL.FSharpInputs;
using Sharpwired.IL.Tests.Inputs;

namespace Sharpwired.IL.Tests;

internal static class FrontEndSupport
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static MethodInfo CSharpInput(string name) =>
        typeof(FrontEndInputs).GetMethod(name, Static) ?? throw new InvalidOperationException($"no C# input {name}");

    public static MethodInfo FSharpInput(string name) =>
        typeof(FrontEnd).GetMethod(name, Static) ?? throw new InvalidOperationException($"no F# input {name}");

    public static string CSharpInputPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Inputs", "FrontEndInputs.cs");

    public static string FSharpInputPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "Sharpwired.IL.FSharpInputs", "Inputs.fs"));

    // The 1-based line of the comment "marker: <name>" in the file.
    public static int MarkerLine(string path, string name)
    {
        var lines = File.ReadAllLines(path);
        var marker = "marker: " + name;
        var found = Enumerable.Range(0, lines.Length).Where(i => lines[i].TrimEnd().EndsWith(marker, StringComparison.Ordinal)).ToArray();
        Assert.True(found.Length == 1, $"the marker '{marker}' occurs {found.Length} times in {path}");
        return found[0] + 1;
    }

    // The instructions without nop, each as its IL name and its operand (no branch target).
    public static string[] Listing(DecodedMethod method) =>
        method.Instructions.Where(i => i.Name != "nop").Select(Format).ToArray();

    public static string Format(IlInstruction instruction) => instruction.Operand switch
    {
        MethodBase method => $"{instruction.Name} {method.Name}",
        FieldInfo field => $"{instruction.Name} {field.Name}",
        Type type => $"{instruction.Name} {type.Name}",
        float value => $"{instruction.Name} {value.ToString("R", CultureInfo.InvariantCulture)}",
        int when instruction.IsBranch => instruction.Name,
        int value when instruction.Name.EndsWith(".s", StringComparison.Ordinal) || instruction.Name is "ldc.i4" or "ldarg" or "ldloc" or "stloc" or "starg" or "ldarga" or "ldloca" =>
            $"{instruction.Name} {value.ToString(CultureInfo.InvariantCulture)}",
        _ => instruction.Name,
    };

    public static string Text(IEnumerable<string> listing) => string.Join("; ", listing);
}
