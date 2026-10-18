// Handwired source: tests/json-search/search.test.ts ("the step has the sizes the README gives") and
// tests/json-search/edge-cases.test.ts (the statement counts of the shaders: 107, 147, 171 and 219). The sizes of
// the three search programs, pinned next to the Handwired sizes. The sizes are the operations of each stage, the
// layers, the shared cells and the pattern cells, and the statistics of the integer writer. Also the check that Search8,
// Search16 and Search32 have equal IL apart from the constants of S.

using System.Reflection;
using System.Reflection.Emit;
using Sharpwired.Core;
using Sharpwired.Gpu;

namespace JsonSearch.Tests;

public sealed partial class SearchTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    // One IL instruction in a form without the encoding choices of the compiler. The short and the long form
    // of an opcode have the same name, and a branch target is an instruction index. A member of the step class
    // has the name "step." and its member name.
    private sealed record Instruction(string Name, string Operand, int? Constant);

    private static List<Instruction> ReadIl(MethodInfo method)
    {
        var body = method.GetMethodBody()!;
        var bytes = body.GetILAsByteArray()!;
        var raw = new List<(int Offset, OpCode Code, object? Operand)>();
        var position = 0;
        while (position < bytes.Length)
        {
            var offset = position;
            short value = bytes[position++];
            if (value == 0xfe) value = (short)(0xfe00 | bytes[position++]);
            var code = OpCodesByValue[value];
            object? operand = null;
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                    operand = position + 1 + (sbyte)bytes[position];
                    position += 1;
                    break;
                case OperandType.InlineBrTarget:
                    operand = position + 4 + BitConverter.ToInt32(bytes, position);
                    position += 4;
                    break;
                case OperandType.ShortInlineI:
                    operand = (int)(sbyte)bytes[position];
                    position += 1;
                    break;
                case OperandType.ShortInlineVar:
                    operand = (int)bytes[position];
                    position += 1;
                    break;
                case OperandType.InlineVar:
                    operand = (int)BitConverter.ToUInt16(bytes, position);
                    position += 2;
                    break;
                case OperandType.InlineI:
                    operand = BitConverter.ToInt32(bytes, position);
                    position += 4;
                    break;
                case OperandType.ShortInlineR:
                    operand = BitConverter.ToSingle(bytes, position);
                    position += 4;
                    break;
                case OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok:
                    operand = method.Module.ResolveMember(BitConverter.ToInt32(bytes, position));
                    position += 4;
                    break;
                default:
                    throw new InvalidOperationException($"the IL reader of the test does not read the operand of {code.Name}");
            }
            raw.Add((offset, code, operand));
        }

        var indexOf = raw.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(pair => pair.Offset, pair => pair.index);
        var instructions = new List<Instruction>();
        foreach (var (_, code, operand) in raw)
        {
            var name = code.Name!;
            int? constant = null;
            var text = "";
            if (name.StartsWith("ldc.i4", StringComparison.Ordinal))
            {
                constant = operand as int? ?? (name == "ldc.i4.m1" || name == "ldc.i4.M1" ? -1 : int.Parse(name[7..], System.Globalization.CultureInfo.InvariantCulture));
                name = "ldc.i4";
            }
            else if (code.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget)
            {
                name = name.Replace(".s", "", StringComparison.Ordinal);
                text = indexOf[(int)operand!].ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (name is "ldloc.0" or "ldloc.1" or "ldloc.2" or "ldloc.3" or "stloc.0" or "stloc.1" or "stloc.2" or "stloc.3"
                or "ldarg.0" or "ldarg.1" or "ldarg.2" or "ldarg.3")
            {
                text = name[^1..];
                name = name[..^2];
            }
            else if (operand is MemberInfo member)
            {
                name = name.Replace(".s", "", StringComparison.Ordinal);
                text = member.DeclaringType == method.DeclaringType ? "step." + member.Name : $"{member.DeclaringType?.FullName}.{member}";
            }
            else
            {
                if (name.EndsWith(".s", StringComparison.Ordinal)) name = name[..^2];
                text = Convert.ToString(operand, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
            instructions.Add(new Instruction(name, text, constant));
        }
        return instructions;
    }

    [Fact]
    public void The_step_methods_of_the_three_capacities_are_equal_apart_from_the_constant_s()
    {
        // The functions of S that a constant can be: S, the index of the last state, S × S, 17 × S, 17 × S × S.
        Func<int, int>[] functions = [s => s, s => s - 1, s => s * s, s => 17 * s, s => SearchCells.ClassSlots * s * s];
        var compared = 0;
        foreach (var name in new[] { "Start", "Reverse", "Forward" })
        {
            var bodies = SearchCapacity.All.Select(capacity => ReadIl(SearchProgram.StepClass(capacity).GetMethod(name)!)).ToArray();
            var locals = SearchCapacity.All.Select(capacity => string.Join(",", SearchProgram.StepClass(capacity).GetMethod(name)!.GetMethodBody()!.LocalVariables.Select(local => local.LocalType))).ToArray();
            Assert.All(locals, each => Assert.Equal(locals[0], each));
            Assert.All(bodies, body => Assert.Equal(bodies[0].Count, body.Count));
            var differing = 0;
            for (var index = 0; index < bodies[0].Count; index++)
            {
                var (eight, sixteen, thirtyTwo) = (bodies[0][index], bodies[1][index], bodies[2][index]);
                Assert.Equal((eight.Name, eight.Operand), (sixteen.Name, sixteen.Operand));
                Assert.Equal((eight.Name, eight.Operand), (thirtyTwo.Name, thirtyTwo.Operand));
                if (eight.Constant == sixteen.Constant && eight.Constant == thirtyTwo.Constant) continue;
                differing++;
                Assert.True(
                    functions.Any(function => function(8) == eight.Constant && function(16) == sixteen.Constant && function(32) == thirtyTwo.Constant),
                    $"{name}, instruction {index}: the constants {eight.Constant}, {sixteen.Constant} and {thirtyTwo.Constant} are no function of S");
            }
            Assert.True(differing > 0, $"{name} has no constant of S");
            compared += bodies[0].Count;
        }
        output.WriteLine($"{compared} IL instructions compared in each class");
    }

    // The live operations that compute: the operations of the compiled program of the outputs with a term or an
    // index read (HighlighterTests explains the count).
    private static string StageSizes(LoopStage stage)
    {
        var circuit = stage.Function.Table.Circuit;
        var roots = stage.Function.Outputs.Select(leaf => leaf.Wire).Distinct().ToList();
        var tensor = circuit.CompileTensorProgram(roots);
        var live = tensor.WireOrder.Skip(tensor.SourceSize).Select(wire => circuit.Operations[wire - circuit.SourceSize]).ToList();
        var computing = live.Count(operation => operation.Kind == OperationKind.Index || operation.LeftTerms.Length > 0 || operation.GateTerms.Length > 0);
        var reads = live.Count(operation => operation.Kind == OperationKind.Index);
        return $"{stage.Name} {computing}/{reads}/{tensor.Layers.Length}";
    }

    [Fact]
    public void The_programs_have_the_pinned_sizes()
    {
        var lines = SearchCapacity.All.Select(capacity =>
        {
            var program = Programs[capacity.States].Value;
            return $"{capacity.States} states: {string.Join(", ", program.Loop.Stages.Select(StageSizes))}; shared {program.Loop.SharedSize}, pattern cells {program.PatternCells}";
        }).ToList();
        foreach (var line in lines) output.WriteLine(line);
        // Each stage as "name live operations that compute / live index reads / layers". Handwired search.test.ts,
        // unmasked: start 8, reverse 148, forward 92 at 8 states; 16, 356, 308 at 16; 32, 1,156, 1,124 at 32. The
        // mask adds 3 operations to the reverse (Handwired: 151 masked at 8 states), so Reverse equals the
        // Handwired masked count. Forward has 4 more: "a match continues" is a sum and a comparison of 2
        // operations in place of one noneOf (SearchSteps.cs). Finish copies a register and computes nothing.
        // Handwired pattern cells: 1,097, 4,177, 16,481. Here each has S × S + 1 more. These are the 17th class of each
        // pair (from, to) and cell 0 of regionClasses (plan, question 1, items 4 and 5).
        Assert.Equal(
            [
                "8 states: Start 8/0/1, Reverse 151/66/9, Forward 96/64/9, Finish 0/0/0; shared 1171, pattern cells 1162",
                "16 states: Start 16/0/1, Reverse 359/258/9, Forward 312/256/9, Finish 0/0/0; shared 4443, pattern cells 4434",
                "32 states: Start 32/0/1, Reverse 1159/1026/9, Forward 1128/1024/9, Finish 0/0/0; shared 17515, pattern cells 17506",
            ],
            lines);
    }

    [Fact]
    public void The_integer_writer_statistics_of_the_three_search_shaders_are_pinned()
    {
        var lines = SearchCapacity.All.Select(capacity =>
        {
            var statistics = LoopIntegerWriter.Write(Programs[capacity.States].Value.Loop).Statistics;
            return $"{capacity.States} states: {statistics}";
        }).ToList();
        foreach (var line in lines) output.WriteLine(line);
        // Handwired edge-cases.test.ts: 147, 171 and 219 statements for 8, 16 and 32 states. The packed reads
        // fire for each gated read of the transitions (2 × S × S), and the lane operations fire 2 × S times. Each
        // search shader has no float row and no constant table.
        Assert.Equal(
            [
                "8 states: LoopWgslStatistics { Statements = 229, FloatRows = 0, WholeRows = 7, BitRows = 71, PackedReads = 128, LaneOperations = 16, TableCells = 0 }",
                "16 states: LoopWgslStatistics { Statements = 301, FloatRows = 0, WholeRows = 7, BitRows = 103, PackedReads = 512, LaneOperations = 32, TableCells = 0 }",
                "32 states: LoopWgslStatistics { Statements = 445, FloatRows = 0, WholeRows = 7, BitRows = 167, PackedReads = 2048, LaneOperations = 64, TableCells = 0 }",
            ],
            lines);
    }
}
