// No Handwired source. The cell checks of the step kernel on each adapter: for each edge value of each
// cell kind, the status of the first evaluation equals the result of the CellValidator of the step
// table. The checks depend on is_finite and the bit tests on a computed value, which the WGSL
// specification does not guarantee (section 15.7.2). So this test measures them on each adapter. Also
// the CPU replay of the subnormal rule.

using System.Collections.Immutable;
using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL;

namespace Sharpwired.Gpu.Tests;

public sealed partial class GpuRecurrentTests
{
    private static readonly KernelBuffer[] StepLayout =
        [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage];

    private static readonly float[] CheckValues =
    [
        float.NaN, -float.NaN, BitConverter.UInt32BitsToSingle(0x7f800001), float.PositiveInfinity, float.NegativeInfinity,
        0f, -0f, 1f, -1f, 0.5f, 0.25f, 0.125f, 0.375f, 2f, 3f, 4f, 8f, 12f, 6f,
        -2.5f, MathF.BitDecrement(-2.5f), MathF.BitIncrement(-2.5f), 100f, MathF.BitIncrement(100f), MathF.BitDecrement(100f),
        8_388_608f, 8_388_609f, 16_777_216f, -8_388_608f, -8_388_609f, -8_388_608.5f, 1e6f, 999_996f, 1_000_004f, 1e6f - 2f,
        float.Epsilon, -float.Epsilon, MathF.ScaleB(1f, -127), MathF.ScaleB(1f, -126), MathF.ScaleB(3f, -140),
        float.MaxValue, -float.MaxValue, 1.0000001f, 0.99999994f, 1e-30f, 2.0000002f,
    ];

    [Theory]
    [MemberData(nameof(WriterData))]
    public void The_cell_checks_of_the_kernel_equal_the_CPU_validator_on_edge_values(GpuWriter writer)
    {
        var step = RecurrentKernelWriterTests.CheckStep(out var cellCount);
        var validator = step.Table.Validator;
        List<float[]> tables = [[.. step.Table.InitialSource]];
        for (var cell = 0; cell < cellCount; cell++)
        {
            foreach (var value in CheckValues)
            {
                float[] table = [.. step.Table.InitialSource];
                table[cell] = value;
                tables.Add(table);
            }
        }

        // The rules: two members of the one-hot group at 1, no member at 1, and p = q = 1.
        float[] Rule(params (int Cell, float Value)[] changes)
        {
            float[] table = [.. step.Table.InitialSource];
            foreach (var (cell, value) in changes) table[cell] = value;
            return table;
        }

        tables.Add(Rule((8, 1f)));
        tables.Add(Rule((7, 0f)));
        tables.Add(Rule((7, 0f), (9, 1f)));
        tables.Add(Rule((10, 1f), (11, 1f)));
        tables.Add(Rule((10, 1f)));
        tables.Add(Rule((7, -0f), (8, 1f)));

        var expected = tables.Select(table =>
        {
            try
            {
                validator.Validate(table);
                return LaneStatus.Limit;
            }
            catch (ArgumentException)
            {
                return LaneStatus.Invalid;
            }
        }).ToArray();
        Assert.Contains(LaneStatus.Limit, expected);
        Assert.Contains(LaneStatus.Invalid, expected);

        var kernelText = RecurrentKernelWriter.Write(step, new RecurrentInitial(0, cellCount, 0), writer);
        fixture.ForEachDevice(output, device =>
        {
            using var kernel = new ComputeKernel(device, kernelText.Code, StepLayout);
            kernel.Write<float>(0, [.. tables.SelectMany(table => table)]);
            kernel.Reserve(1, (long)tables.Count * cellCount * sizeof(float));
            kernel.Write<uint>(2, [0u, (uint)tables.Count, 0u, 0u]);
            kernel.Write<uint>(3, new uint[tables.Count * RecurrentKernelWriter.LaneWords]);

            // The step limit 0: the first evaluation checks the cells, and then the lane stops at the limit.
            kernel.Write<uint>(4, [0u, (uint)GpuRecurrentRunner.StepsPerDispatch, 0u, 0u]);
            var lanes = new uint[tables.Count * RecurrentKernelWriter.LaneWords];
            kernel.Run((uint)((tables.Count + 63) / 64), 3, lanes.AsSpan());
            for (var table = 0; table < tables.Count; table++)
            {
                var status = (LaneStatus)lanes[table * RecurrentKernelWriter.LaneWords];
                Assert.True(expected[table] == status,
                    $"table {table} [{string.Join(", ", tables[table].Select(value => value.ToString("R")))}]: GPU {status}, validator {expected[table]}");
            }
            output.WriteLine($"{tables.Count} tables, {expected.Count(status => status == LaneStatus.Invalid)} invalid");
        });
    }
}

// The CPU replay of a lane with the f32 rows (RowInterpreter), for the subnormal rule.
internal static class SubnormalReplay
{
    /// <summary>
    /// True when the CPU run of the lane holds a nonzero value of magnitude below 2^-126 in a wire of
    /// the prefix, of a step evaluation, or of the suffix.
    /// </summary>
    public static bool Holds(RecurrentFunction function, float[] arguments, int stepLimit)
    {
        var prefix = function.Prefix.CreateEvaluator().Program;
        var step = RecurrentKernelWriter.StepProgram(function.Step);
        var suffix = function.Suffix.CreateEvaluator().Program;
        var initial = RecurrentKernelWriter.Initial(function, prefix);
        var saw = false;

        float[] Roots(TensorProgram program, float[] source)
        {
            var values = RowInterpreter.Run(program, ShaderRows.FlattenProgram(program), source, out var subnormal);
            saw |= subnormal;
            return [.. ShaderRows.RootPositions(program).Select(position => position < 0 ? float.NaN : values[position])];
        }

        var cells = Roots(prefix, arguments).AsSpan(initial.Offset, function.Step.Table.Circuit.SourceSize).ToArray();
        for (var evaluation = 0; evaluation < stepLimit; evaluation++)
        {
            var roots = Roots(step, cells);
            if (roots[^1] != 0) break;
            cells = roots[..^1];
        }

        Roots(suffix, cells);
        return saw;
    }
}
