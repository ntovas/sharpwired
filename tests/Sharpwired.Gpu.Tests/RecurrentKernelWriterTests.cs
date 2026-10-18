// No Handwired source. The tests of RecurrentKernelWriter that need no adapter (phase 5 plan, task 5):
// one var for each state cell and one let for each invariant, the cell checks of the WGSL forms table,
// and the status codes of a lane.

using System.Text.RegularExpressions;
using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL;
using Sharpwired.IL.Tests.Inputs;

namespace Sharpwired.Gpu.Tests;

public sealed class RecurrentKernelWriterTests
{
    public static TheoryData<GpuWriter> Writers => [GpuWriter.Float32, GpuWriter.Integer];

    internal static RecurrentFunction Translate(Type type, string name) =>
        HandwiredTranslator.TranslateRecurrent(type.GetMethod(name) ?? throw new ArgumentException($"no method {name}"));

    // The order key of the bits of an FP32 value, as the WGSL function ordered gives it.
    private static int Key(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        return bits ^ ((bits >> 31) & 0x7fffffff);
    }

    private static string Int(int value) => WgslText.Int(value);

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_step_kernel_holds_each_state_cell_in_a_var_and_each_invariant_in_a_let(GpuWriter writer)
    {
        var function = Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Invariant));
        var kernel = RecurrentKernelWriter.Write(function, writer);
        var code = kernel.Code;

        // The cells x and count are state cells, and limit is an invariant (RecurrentTests).
        Assert.Equal(3, kernel.CellCount);
        Assert.Equal(["cell0", "cell1"], Regex.Matches(code, @"var (cell\d+): f32 = ").Select(match => match.Groups[1].Value));
        Assert.Equal(["cell2"], Regex.Matches(code, @"let (cell\d+): f32 = ").Select(match => match.Groups[1].Value));

        // The first dispatch reads the prefix outputs, and a later dispatch reads the state buffer.
        Assert.Contains("var cell0: f32 = select(state[base + 0u], initial[origin + 0u], fresh);", code);
        Assert.Contains("let cell2: f32 = select(state[base + 2u], initial[origin + 2u], fresh);", code);

        // The kernel copies each next value of a state cell into its var, and stores each cell.
        Assert.Contains("cell0 = next0;", code);
        Assert.Contains("cell1 = next1;", code);
        Assert.DoesNotContain("cell2 = ", code);
        Assert.Contains("state[base + 2u] = cell2;", code);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_step_kernel_writes_the_status_codes_of_a_lane(GpuWriter writer)
    {
        var code = RecurrentKernelWriter.Write(Translate(typeof(RecurrentInputs), nameof(RecurrentInputs.Halve)), writer).Code;
        Assert.Equal(0u, (uint)LaneStatus.Fresh);
        Assert.Equal(1u, (uint)LaneStatus.Running);
        Assert.Contains("if (status > 1u) {", code);
        Assert.Contains("let fresh: bool = status == 0u;", code);
        Assert.Contains("if (steps >= run.limit) { status = 3u; break; }", code);
        Assert.Contains("if (evaluations == run.budget) { break; }", code);
        Assert.Contains("if (!is_finite(done)) { status = 4u; detail = 4294967294u; break; }", code);
        Assert.Contains("if (is_nonzero(done)) { status = 2u; break; }", code);
        Assert.Contains("lanes[lane] = status;", code);
        Assert.Contains("lanes[lane + 1u] = steps;", code);
        Assert.Contains("lanes[lane + 2u] = detail;", code);
        Assert.Equal(2u, (uint)LaneStatus.Completed);
        Assert.Equal(3u, (uint)LaneStatus.Limit);
        Assert.Equal(4u, (uint)LaneStatus.Invalid);
        Assert.Equal(4294967294u, RecurrentKernelWriter.DoneFault);
    }

    [Fact]
    public void A_prefix_overflow_bit_stops_the_lane_at_the_first_evaluation()
    {
        var kernel = RecurrentKernelWriter.Write(CheckStep(out _), new RecurrentInitial(Offset: 2, Stride: 16, OverflowCount: 2), GpuWriter.Float32);
        Assert.Contains("if (is_nonzero(initial[origin + 12u])) { status = 4u; detail = 4294967293u; break; }", kernel.Code);
        Assert.Contains("if (is_nonzero(initial[origin + 13u])) { status = 4u; detail = 4294967293u; break; }", kernel.Code);
        Assert.Contains("let origin: u32 = t * 16u + 2u;", kernel.Code);
    }

    [Fact]
    public void The_f32_cell_checks_follow_the_forms_table()
    {
        var kernel = RecurrentKernelWriter.Write(CheckStep(out var cells), new RecurrentInitial(0, cells, 0), GpuWriter.Float32);
        var code = kernel.Code;

        // a: -2.5..100, no grid. Its next value is a + 1.
        Assert.Contains($"if (!(is_finite(next0) && ordered(next0) >= {Int(Key(-2.5f))} && ordered(next0) <= {Int(Key(100f))})) {{ status = 4u; detail = 0u; break; }}", code);

        // b: the wide int range with the grid 1.
        Assert.Contains($"is_finite(next1) && ordered(next1) >= {Int(Key(-8388608f))} && ordered(next1) <= {Int(Key(8388608f))} && on_grid(next1, 0i)", code);

        // c: an exact bit. The range 0..1 starts at -0, so that -0 passes as the validator lets it pass.
        Assert.Contains($"is_finite(next2) && ordered(next2) >= {Int(Key(-0f))} && ordered(next2) <= {Int(Key(1f))} && is_bit(next2)", code);

        // d: the grid 4 = 2^2, e: the grid 0.25 = 2^-2, f: no bound, h: a bound below the
        // smallest subnormal value. The first evaluation checks each cell.
        Assert.Contains("on_grid(cell3, 2i)", code);
        Assert.Contains("on_grid(cell4, (-2i))", code);
        Assert.Contains("if (!(is_finite(cell5))) { status = 4u; detail = 5u; break; }", code);
        Assert.Contains($"ordered(cell6) >= {Int(Key(float.Epsilon))}", code);

        // The one-hot group g0..g2 and the at-most-one rule over p and q.
        Assert.Contains($"if (!(ones(next7) + ones(next8) + ones(next9) == 1u)) {{ status = 4u; detail = {cells}u; break; }}", code);
        Assert.Contains($"if (!(ones(cell10) + ones(cell11) <= 1u)) {{ status = 4u; detail = {cells + 1}u; break; }}", code);
        foreach (var helper in new[] { "fn ordered(", "fn is_bit(", "fn ones(", "fn on_grid(", "fn is_nonzero(" }) Assert.Contains(helper, code);
    }

    [Fact]
    public void The_integer_writer_checks_whole_and_bit_next_values_in_integer_arithmetic()
    {
        var kernel = RecurrentKernelWriter.Write(CheckStep(out var cells), new RecurrentInitial(0, cells, 0), GpuWriter.Integer);
        var code = kernel.Code;

        // b + 1 is a whole row: an i32 check with no f32 test.
        var whole = Regex.Match(code, @"if \(!\((w\d+) >= \(-8388608i\) && \1 <= 8388608i\)\) \{ status = 4u; detail = 1u; break; \}");
        Assert.True(whole.Success, code);
        Assert.DoesNotContain("is_finite(next1)", code);

        // 1 - c is a u32 bit within the range 0..1 of its cell, so it needs no check.
        Assert.DoesNotContain("detail = 2u;", code[code.IndexOf("loop {\n        if (status != 1u)", StringComparison.Ordinal)..]);

        // a + 1 stays an f32 row with the f32 check.
        Assert.Contains("is_finite(next0) && ordered(next0)", code);
    }

    [Fact]
    public void A_program_or_a_prefix_layout_of_another_shape_is_refused()
    {
        var step = CheckStep(out var cells);
        var doneOnly = step.Table.Circuit.CompileTensorProgram([step.Done]);
        var error = Assert.Throws<ArgumentException>(() => RecurrentKernelWriter.Write(step, doneOnly, new RecurrentInitial(0, cells, 0), GpuWriter.Float32));
        Assert.Contains($"needs {cells + 1}", error.Message);
        Assert.Throws<ArgumentException>(() => RecurrentKernelWriter.Write(step, new RecurrentInitial(1, cells, 0), GpuWriter.Float32));
        Assert.Throws<ArgumentException>(() => RecurrentKernelWriter.Write(step, new RecurrentInitial(0, cells + 1, 2), GpuWriter.Integer));
    }

    // A step with one cell of each kind of check: a: -2.5..100; b: wide int; c: a bit; d: grid 4;
    // e: grid 0.25; f: any number; h: 1e-50..1; g0..g2: a one-hot group; p, q: an at-most-one rule.
    // The next values: a + 1, b + 1, 1 - c, g unchanged. Done is the constant 1.
    internal static RecurrentStep CheckStep(out int cellCount)
    {
        var table = new SourceTable();
        var a = table.Declare("a", Cell.State(new NumericRange(-2.5, 100)));
        var b = table.Declare("b", Cell.State(new NumericRange(-8_388_608, 8_388_608), 0, 1));
        var c = table.Declare("c", Cell.StateBit());
        var d = table.Declare("d", Cell.Input(new NumericRange(0, 1e6), 0, 4));
        var e = table.Declare("e", Cell.Input(new NumericRange(-1, 1), 0, 0.25));
        var f = table.Declare("f", Cell.Input(Cell.AnyNumber));
        var h = table.Declare("h", Cell.Input(new NumericRange(1e-50, 1), 1));
        var g = table.Declare("g", Cell.OneHot([Cell.StateBit(1), Cell.StateBit(), Cell.StateBit()]));
        var p = table.Declare("p", Cell.InputBit());
        var q = table.Declare("q", Cell.InputBit());
        table.AtMostOne("pq", [p, q]);
        var circuit = table.CreateCircuit();
        table.Next(a, circuit.Affine([(a, 1)], 1));
        table.Next(b, circuit.Affine([(b, 1)], 1));
        table.Next(c, circuit.Affine([(c, -1)], 1));
        table.Next(g, g);
        var done = circuit.Constant(1);
        var finished = table.Finish();
        cellCount = circuit.SourceSize;
        Wire[] state = [a, b, c, .. g];
        Wire[] invariants = [d, e, f, h, p, q];
        return new RecurrentStep(finished, [.. state.Select(wire => new NamedWire($"cell{wire.Position}", wire))],
            [.. invariants.Select(wire => new NamedWire($"cell{wire.Position}", wire))], done);
    }
}
