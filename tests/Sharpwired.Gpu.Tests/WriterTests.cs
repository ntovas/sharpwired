// Handwired source: tests/json-search/json-search.test.ts (the WGSL text of the rows and the literals of
// wgslFloat). These tests read the text of WgslFloatWriter for one row of each kind, and need no adapter.
// The expected text follows the f32 column of the WGSL forms table of the phase 5 plan.

using System.Globalization;
using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed class WriterTests
{
    private const string NaNBits = "bitcast<f32>(0x7fc00000u)";

    // The name of a wire in the shader: s<cell> for a source cell, v<position> for a computed value.
    private static string Name(TensorProgram program, Wire wire)
    {
        var position = PositionOf(program, wire);
        return position < program.SourceSize ? $"s{position}" : $"v{position}";
    }

    private static int PositionOf(TensorProgram program, Wire wire)
    {
        for (var position = 0; position < program.WireOrder.Count; position++)
        {
            if (program.WireOrder[position] == wire.Position) return position;
        }
        throw new InvalidOperationException($"wire {wire.Position} is not in the program");
    }

    private static string[] Lines(WgslModule module) =>
        [.. module.Code.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];

    // Assert that the code holds the line, and return its line number.
    private static int Line(WgslModule module, string expected)
    {
        var lines = Lines(module);
        var index = Array.IndexOf(lines, expected);
        Assert.True(index >= 0, $"the code does not hold the line\n{expected}\ncode:\n{module.Code}");
        return index;
    }

    private static void NoLineStarts(WgslModule module, string start) =>
        Assert.DoesNotContain(Lines(module), line => line.StartsWith(start, StringComparison.Ordinal));

    private static (Circuit Circuit, Wire[] Cells) Cells(int count, double minimum = -100, double maximum = 100)
    {
        var circuit = new Circuit(count);
        var cells = Enumerable.Range(0, count).Select(circuit.Source).ToArray();
        foreach (var cell in cells) circuit.AssumeRange(cell, minimum, maximum);
        return (circuit, cells);
    }

    private static WgslModule Write(Circuit circuit, out TensorProgram program, bool lowerGathers = true)
    {
        program = circuit.CompileTensorProgram(lowerGathers: lowerGathers);
        return WgslFloatWriter.Write(circuit, program);
    }

    [Fact]
    public void Literals_follow_wgslFloat()
    {
        Assert.Equal("0.0f", WgslText.Float(0f));
        Assert.Equal("(-0.0f)", WgslText.Float(-0f));
        Assert.Equal("3.0f", WgslText.Float(3f));
        Assert.Equal("(-3.0f)", WgslText.Float(-3f));
        Assert.Equal("8388609.0f", WgslText.Float(8388609f));
        Assert.Equal("0x1.000000p24f", WgslText.Float(16777216f));
        Assert.Equal("0x1.000000p-1f", WgslText.Float(0.5f));
        Assert.Equal("0x1.555556p-2f", WgslText.Float(1f / 3));
        Assert.Equal("(-0x1.666666p-1f)", WgslText.Float(-0.7f));
        Assert.Equal("0x0.000400p-126f", WgslText.Float(MathF.ScaleB(1f, -140)));
        Assert.Equal("0x0.000002p-126f", WgslText.Float(float.Epsilon));
        Assert.Equal("0x1.fffffep127f", WgslText.Float(float.MaxValue));
        Assert.Equal("(-1i)", WgslText.Int(-1));
        Assert.Equal("7i", WgslText.Int(7));
        Assert.Throws<ArgumentException>(() => WgslText.Float(float.NaN));
        Assert.Throws<ArgumentException>(() => WgslText.Float(float.PositiveInfinity));
    }

    // wgslFloat gives the exact bits: a parser of the literal reads each of 10,000 seeded values back.
    [Fact]
    public void Each_literal_reads_back_to_the_same_bits()
    {
        var random = Js.CreateRandom(0x5eed);
        var checkedValues = 0;
        var subnormals = 0;
        var integers = 0;
        while (checkedValues < 10_000)
        {
            // Random bits give each exponent, and a scaled integer gives the decimal form.
            var bits = (uint)(random() * 4_294_967_296.0);
            var value = random() < 0.1 ? (float)Math.Floor((random() - 0.5) * 4e7) : BitConverter.UInt32BitsToSingle(bits);
            if (!float.IsFinite(value)) continue;
            if (float.IsSubnormal(value)) subnormals++;
            if (value == MathF.Truncate(value) && MathF.Abs(value) < 16777216f) integers++;
            var text = WgslText.Float(value);
            var parsed = ParseLiteral(text);
            Assert.True(
                BitConverter.SingleToUInt32Bits(value) == BitConverter.SingleToUInt32Bits(parsed),
                $"{value:R} gives {text}, which reads back as {parsed:R}");
            checkedValues++;
        }
        Assert.True(subnormals > 0 && integers > 100, $"subnormals {subnormals}, integers {integers}");
    }

    // A parser of the three forms of wgslFloat: "N.0f", "0x1.HHHHHHpEf" and "0x0.HHHHHHp-126f", each
    // with an optional "(-...)" around it.
    private static float ParseLiteral(string text)
    {
        var negative = false;
        if (text.StartsWith("(-", StringComparison.Ordinal))
        {
            Assert.EndsWith(")", text);
            negative = true;
            text = text[2..^1];
        }
        Assert.EndsWith("f", text);
        text = text[..^1];
        double magnitude;
        if (text.StartsWith("0x", StringComparison.Ordinal))
        {
            var point = text.IndexOf('.', StringComparison.Ordinal);
            var exponentAt = text.IndexOf('p', StringComparison.Ordinal);
            var lead = int.Parse(text[2..point], CultureInfo.InvariantCulture);
            Assert.True(lead is 0 or 1, text);
            var fraction = text[(point + 1)..exponentAt];
            Assert.Equal(6, fraction.Length);
            var exponent = int.Parse(text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var mantissa = lead + (int.Parse(fraction, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 16_777_216.0);
            magnitude = Math.ScaleB(mantissa, exponent);
        }
        else
        {
            Assert.EndsWith(".0", text);
            magnitude = double.Parse(text[..^2], CultureInfo.InvariantCulture);
        }
        var value = (float)magnitude;
        Assert.Equal(magnitude, value);
        return negative ? -value : value;
    }

    [Fact]
    public void An_affine_row_is_the_left_fold_with_one_keep_for_each_step()
    {
        var (circuit, cells) = Cells(2);
        var doubled = circuit.RoundAffine([(cells[0], 2)], 0);
        var row = circuit.RoundAffine([(cells[1], 1), (doubled, -1.0 / 3)], 2.5);
        var negated = circuit.RoundAffine([(cells[0], -1), (cells[1], 1)], 0);
        var module = Write(circuit, out var program);

        // A bias of 0 stays k(0.0f).
        Line(module, $"let {Name(program, doubled)}: f32 = k(k(0.0f) + k(k(2.0f) * s0));");
        // A weight of 1 adds the value, and a negative weight subtracts the product of its magnitude.
        Line(module, $"let {Name(program, row)}: f32 = k(k(k(0x1.400000p1f) + s1) - k(k(0x1.555556p-2f) * {Name(program, doubled)}));");
        // A weight of -1 subtracts the value.
        Line(module, $"let {Name(program, negated)}: f32 = k(k(k(0.0f) - s0) + s1);");
        Assert.Equal(3, module.Statistics.FloatRows);
        Assert.Equal(0, module.Statistics.WholeRows);
        Assert.Equal(0, module.Statistics.BitRows);
    }

    [Fact]
    public void Negative_zero_and_subnormal_literals_keep_their_bits()
    {
        var (circuit, cells) = Cells(2);
        var tiny = circuit.RoundAffine([(cells[0], Math.ScaleB(1.0, -140))], -0.0);
        // A weight of -0 has the sign bit, so the row subtracts the product of +0.
        var negativeZeroWeight = circuit.RoundAffine([(cells[1], -0.0)], 1);
        var module = Write(circuit, out var program);

        Line(module, $"let {Name(program, tiny)}: f32 = k(k((-0.0f)) + k(k(0x0.000400p-126f) * s0));");
        Line(module, $"let {Name(program, negativeZeroWeight)}: f32 = k(k(1.0f) - k(k(0.0f) * s1));");
    }

    [Fact]
    public void A_row_of_33_terms_continues_in_a_second_statement()
    {
        var (circuit, cells) = Cells(2);
        var terms = Enumerable.Range(0, 33).Select(index => new Term(cells[index % 2], 1)).ToArray();
        var row = circuit.RoundAffine(terms, 0);
        var module = Write(circuit, out var program);
        var name = Name(program, row);

        var chain = "k(0.0f)";
        for (var index = 0; index < 32; index++) chain = $"k({chain} + s{index % 2})";
        var first = Line(module, $"let {name}_1: f32 = {chain};");
        var second = Line(module, $"let {name}: f32 = k({name}_1 + s0);");
        Assert.True(first < second);
    }

    [Fact]
    public void A_reglu_row_multiplies_the_left_form_by_relu_of_the_gate()
    {
        var (circuit, cells) = Cells(2);
        var product = circuit.Reglu([(cells[0], 0.5)], 1, [(cells[1], 1)], -2);
        var relu = circuit.Relu(cells[1]);
        var module = Write(circuit, out var program);

        Line(module, $"let {Name(program, product)}: f32 = k(k(k(1.0f) + k(k(0x1.000000p-1f) * s0)) * relu(k(k((-2.0f)) + s1)));");
        // A left form of the constant 1: f(1 × relu(g)) is relu(g).
        Line(module, $"let {Name(program, relu)}: f32 = relu(k(k(0.0f) + s1));");
    }

    [Fact]
    public void A_boolean_row_is_the_stored_atom()
    {
        var (circuit, cells) = Cells(1, 0, 1);
        var atom = circuit.Boolean(cells[0]);
        var module = Write(circuit, out var program);

        Line(module, $"let {Name(program, atom)}: f32 = k(k(k(k(0.0f) + s0) + k(8388609.0f)) - k(8388609.0f));");
    }

    public static TheoryData<OutOfRange, OutOfRange> Sides => new()
    {
        { OutOfRange.Clamp, OutOfRange.Clamp },
        { OutOfRange.Zero, OutOfRange.Clamp },
        { OutOfRange.Clamp, OutOfRange.Zero },
        { OutOfRange.Zero, OutOfRange.Zero },
    };

    // Strided source cells: sources[base + u32(first + c * stride)]. Each zero side adds one if.
    [Theory]
    [MemberData(nameof(Sides))]
    public void An_index_read_writes_each_out_of_range_rule(OutOfRange below, OutOfRange above)
    {
        var (circuit, cells) = Cells(5);
        circuit.AssumeRange(cells[0], -12, 12, allowExistingReaders: true);
        circuit.AssumeGrid(cells[0], 1);
        var read = circuit.IndexReadAffine([(cells[0], 1)], 0, [cells[1], cells[2], cells[3]], below, above);
        var module = Write(circuit, out var program);
        var p = PositionOf(program, read);

        Line(module, $"let x{p}: f32 = k(k(0.0f) + s0);");
        Line(module, $"let p{p}: f32 = floor(x{p});");
        Line(module, $"let c{p}: i32 = select(i32(clamp(p{p}, 0.0f, 2.0f)), 0i, is_nan(x{p}));");
        Line(module, $"var v{p}: f32 = sources[base + u32(1i + c{p} * 1i)];");
        if (below == OutOfRange.Zero) Line(module, $"if (p{p} < 0.0f) {{ v{p} = 0.0f; }}");
        else NoLineStarts(module, $"if (p{p} < 0.0f)");
        if (above == OutOfRange.Zero) Line(module, $"if (p{p} > 2.0f) {{ v{p} = 0.0f; }}");
        else NoLineStarts(module, $"if (p{p} > 2.0f)");
        Line(module, $"if (is_nan(x{p})) {{ v{p} = {NaNBits}; }}");
    }

    [Fact]
    public void An_index_read_of_source_cells_in_a_list_reads_a_module_table()
    {
        var (circuit, cells) = Cells(5);
        circuit.AssumeRange(cells[0], -12, 12, allowExistingReaders: true);
        circuit.AssumeGrid(cells[0], 1);
        var read = circuit.IndexRead(cells[0], [cells[3], cells[1], cells[4], cells[1]]);
        var module = Write(circuit, out var program);
        var p = PositionOf(program, read);

        Line(module, "var<private> t0: array<u32, 4> = array<u32, 4>(3u, 1u, 4u, 1u);");
        Line(module, $"var v{p}: f32 = sources[base + t0[c{p}]];");
    }

    [Fact]
    public void An_index_read_of_computed_values_selects_the_candidate()
    {
        var (circuit, cells) = Cells(3);
        circuit.AssumeRange(cells[0], -12, 12, allowExistingReaders: true);
        circuit.AssumeGrid(cells[0], 1);
        var a = circuit.RoundAffine([(cells[1], 3)], 0);
        var b = circuit.RoundAffine([(cells[2], 5)], 0);
        var read = circuit.IndexRead(cells[0], [b, a, cells[1]]);
        var module = Write(circuit, out var program);
        var p = PositionOf(program, read);

        var first = Line(module, $"var a{p}: f32 = {Name(program, b)};");
        Assert.Equal(first + 1, Line(module, $"a{p} = select(a{p}, {Name(program, a)}, c{p} == 1i);"));
        Assert.Equal(first + 2, Line(module, $"a{p} = select(a{p}, s1, c{p} == 2i);"));
        Assert.Equal(first + 3, Line(module, $"var v{p}: f32 = a{p};"));
    }

    // A one-hot group of three source bits selects one of three values. The gather compute gives the
    // index and the mass of the slot. A group that allows all zeros multiplies the value by the mass.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_gather_read_adds_the_selected_candidate(bool allowAllZero)
    {
        var circuit = new Circuit(6);
        var cells = Enumerable.Range(0, 6).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(cells[..3], allowAllZero);
        foreach (var cell in cells[3..]) circuit.AssumeRange(cell, -100, 100);
        var sum = circuit.Affine([
            (circuit.Multiply(cells[0], cells[3]), 1),
            (circuit.Multiply(cells[1], cells[4]), 1),
            (circuit.Multiply(cells[2], cells[5]), 1),
        ]);
        var program = circuit.CompileTensorProgram([sum]);
        Assert.Equal(1, TensorChecks.ProgramCounts(program).GatherReads);
        var module = WgslFloatWriter.Write(circuit, program);
        var p = PositionOf(program, sum);

        Line(module, "let gi0: i32 = i32(s1 + 2.0f * s2);");
        Line(module, "let gm0: f32 = k(0.0f) + s0 + s1 + s2;");
        var candidate = "sources[base + u32(3i + clamp(gi0, 0i, 2i) * 1i)]";
        Line(module, allowAllZero
            ? $"let v{p}: f32 = k(k(0.0f) + k(gm0 * {candidate}));"
            : $"let v{p}: f32 = k(k(0.0f) + {candidate});");
    }

    [Fact]
    public void Each_root_is_stored_and_a_root_that_the_program_does_not_hold_stores_NaN()
    {
        var (circuit, cells) = Cells(2);
        var sum = circuit.RoundAffine([(cells[0], 1), (cells[1], 1)]);
        var unused = circuit.RoundAffine([(cells[0], 5)]);
        var compiled = circuit.CompileTensorProgram([sum]);
        var program = compiled with { Roots = UnsignedIndices.From([sum.Position, unused.Position, cells[1].Position]) };
        var module = WgslFloatWriter.Write(circuit, program);

        Assert.Equal(3, module.RootCount);
        Assert.Equal(2, module.SourceSize);
        Line(module, "let o: u32 = t * 3u;");
        Line(module, $"outputs[o + 0u] = {Name(program, sum)};");
        Line(module, $"outputs[o + 1u] = {NaNBits};");
        Line(module, "outputs[o + 2u] = s1;");
    }

    [Fact]
    public void The_module_holds_the_bindings_the_prelude_and_the_counts()
    {
        var (circuit, cells) = Cells(1);
        circuit.RoundAffine([(cells[0], 2)], 1);
        var module = Write(circuit, out _);

        Assert.Equal(64, module.WorkgroupSize);
        Assert.Contains("@group(0) @binding(0) var<storage, read> sources: array<f32>;", module.Code);
        Assert.Contains("@group(0) @binding(1) var<storage, read_write> outputs: array<f32>;", module.Code);
        Assert.Contains("@group(0) @binding(2) var<storage, read> params: Params;", module.Code);
        Assert.Contains(WgslText.Prelude, module.Code);
        Assert.Contains("@compute @workgroup_size(64)", module.Code);
        Line(module, "let base: u32 = t * 1u;");
        // The statements: the source cell, the row and the store of the root.
        Assert.Equal(3, module.Statistics.Statements);
        // k(1.0f), k(2.0f), k(k(2.0f) * s0) and the sum.
        Assert.Equal(4, module.Statistics.Keeps);
        Assert.Equal(1, module.Statistics.FloatRows);
    }

    [Fact]
    public void A_program_of_another_circuit_throws_ArgumentException()
    {
        var (circuit, cells) = Cells(2);
        circuit.RoundAffine([(cells[0], 1), (cells[1], 1)]);
        var (other, _) = Cells(3);
        Assert.Throws<ArgumentException>(() => WgslFloatWriter.Write(other, circuit.CompileTensorProgram()));
    }
}
