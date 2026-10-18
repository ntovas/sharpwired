// Handwired source: none for the tests; the probe of the phase 6 plan, question 1 ("the same kernel with
// the constant tables in a read-only storage buffer"), and the phase 5 carried item "shader creation time".
// These tests read the text of both writers: an index read of constant candidates reads one table in the
// storage buffer of the constant tables, and the constants get no statement. A root that is a source cell
// is a copy from the source buffer, with no statement of its own. They need no adapter.

using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed partial class WriterTests
{
    public static TheoryData<GpuWriter> BothWriters => new() { GpuWriter.Float32, GpuWriter.Integer };

    private static readonly double[] TableValues = [1.5, -0.0, 7, 3, 2.25];

    // Two index reads of one list of five constants, and a sum that reads a third constant.
    internal static (Circuit Circuit, TensorProgram Program, Wire[] Constants, Wire First, Wire Second) ConstantTableProgram()
    {
        var circuit = new Circuit(1);
        var index = circuit.Source(0);
        circuit.AssumeRange(index, -12, 12);
        circuit.AssumeGrid(index, 1);
        var constants = TableValues.Select(circuit.Constant).ToArray();
        var first = circuit.IndexRead(index, constants, OutOfRange.Zero);
        var shifted = circuit.RoundAffine([(index, 1)], 1);
        var second = circuit.IndexRead(shifted, constants);
        var sum = circuit.RoundAffine([(first, 1), (second, 0.5)], 0.25);
        return (circuit, circuit.CompileTensorProgram([sum, first]), constants, first, second);
    }

    private static WgslModule WriteWith(GpuWriter writer, Circuit circuit, TensorProgram program) =>
        writer == GpuWriter.Float32 ? WgslFloatWriter.Write(circuit, program) : WgslIntegerWriter.Write(circuit, program);

    [Theory]
    [MemberData(nameof(BothWriters))]
    public void An_index_read_of_constant_candidates_reads_one_table_of_the_storage_buffer_and_the_constants_get_no_statement(GpuWriter writer)
    {
        var (circuit, program, constants, _, _) = ConstantTableProgram();
        var module = WriteWith(writer, circuit, program);

        // The two reads share one table of five cells, with the exact bits of the constants.
        Assert.Equal(TableValues.Select(value => BitConverter.SingleToInt32Bits((float)value)), module.ConstantTable.Select(BitConverter.SingleToInt32Bits));
        Assert.Contains("@group(0) @binding(3) var<storage, read> tables: array<f32>;", module.Code);
        Assert.Equal(2, Lines(module).Count(line => line.Contains("tables[0u + u32(", StringComparison.Ordinal)));
        foreach (var constant in constants)
        {
            var position = PositionOf(program, constant);
            NoLineStarts(module, $"let v{position}:");
            NoLineStarts(module, $"let w{position}:");
            NoLineStarts(module, $"let b{position}:");
        }
    }

    [Fact]
    public void A_program_with_no_constant_table_declares_no_table_binding()
    {
        var (circuit, cells) = Cells(2);
        circuit.RoundAffine([(cells[0], 1), (cells[1], 1)]);
        var module = Write(circuit, out _);
        Assert.Empty(module.ConstantTable);
        Assert.DoesNotContain("tables", module.Code, StringComparison.Ordinal);
    }

    // A list of a constant and a computed value is no constant table: the read stays a select.
    [Fact]
    public void A_list_of_a_constant_and_a_computed_value_stays_a_select()
    {
        var (circuit, cells) = Cells(2);
        circuit.AssumeRange(cells[0], -12, 12, allowExistingReaders: true);
        circuit.AssumeGrid(cells[0], 1);
        var computed = circuit.RoundAffine([(cells[1], 3)], 0);
        var read = circuit.IndexRead(cells[0], [circuit.Constant(4), computed]);
        var module = Write(circuit, out var program);
        var p = PositionOf(program, read);

        Assert.Empty(module.ConstantTable);
        Line(module, $"a{p} = select(a{p}, {Name(program, computed)}, c{p} == 1i);");
    }

    [Theory]
    [MemberData(nameof(BothWriters))]
    public void A_root_that_is_a_source_cell_is_a_copy_from_the_buffer_with_no_statement_of_its_own(GpuWriter writer)
    {
        var circuit = new Circuit(3);
        var cells = Enumerable.Range(0, 3).Select(circuit.Source).ToArray();
        circuit.AssumeBit(cells[0]);
        circuit.AssumeRange(cells[1], -100, 100);
        circuit.AssumeRange(cells[2], -100, 100);
        var sum = circuit.RoundAffine([(cells[2], 2)], 1);
        var compiled = circuit.CompileTensorProgram([sum]);
        var program = compiled with { Roots = UnsignedIndices.From([cells[0].Position, sum.Position, cells[1].Position]) };
        var module = WriteWith(writer, circuit, program);

        Line(module, "outputs[o + 0u] = sources[base + 0u];");
        Line(module, "outputs[o + 2u] = sources[base + 1u];");
        NoLineStarts(module, "let s0:");
        NoLineStarts(module, "let s1:");
        NoLineStarts(module, "let b0:");
        // The cell that the row reads keeps its read: s2 for the f32 writer.
        if (writer == GpuWriter.Float32) Line(module, "let s2: f32 = sources[base + 2u];");
    }
}
