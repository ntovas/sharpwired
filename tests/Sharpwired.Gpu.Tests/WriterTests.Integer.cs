// Handwired source: tests/json-search/bit-emit.test.ts and tests/json-search/writer-probes.test.ts (the
// rules that fire on a program, and the near misses where a rule must not fire). These tests read the text
// of WgslIntegerWriter for each form of the integer column of the WGSL forms table of the phase 5 plan.
// They need no adapter.

using Sharpwired.Core;

namespace Sharpwired.Gpu.Tests;

public sealed partial class WriterTests
{
    private static WgslModule WriteInteger(Circuit circuit, out TensorProgram program, IReadOnlyList<Wire>? roots = null)
    {
        program = circuit.CompileTensorProgram(roots);
        return WgslIntegerWriter.Write(circuit, program);
    }

    private static Wire WholeCell(Circuit circuit, int cell, double minimum, double maximum)
    {
        var wire = circuit.Source(cell);
        circuit.AssumeRange(wire, minimum, maximum);
        circuit.AssumeGrid(wire, 1);
        return wire;
    }

    private static Wire BitCell(Circuit circuit, int cell)
    {
        var wire = circuit.Source(cell);
        circuit.AssumeBit(wire);
        return wire;
    }

    // w<position> for a whole value, b<position> for a bit, v<position> for an f32 value.
    private static string W(TensorProgram program, Wire wire) => $"w{PositionOf(program, wire)}";

    private static string B(TensorProgram program, Wire wire) => $"b{PositionOf(program, wire)}";

    private static string V(TensorProgram program, Wire wire) => $"v{PositionOf(program, wire)}";

    [Fact]
    public void The_integer_writer_reads_bits_and_whole_cells_as_integers_and_converts_them_for_an_f32_row()
    {
        var circuit = new Circuit(3);
        BitCell(circuit, 0);
        WholeCell(circuit, 1, -12, 12);
        circuit.AssumeRange(circuit.Source(2), -12, 12);
        var sum = circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1)]);
        var doubled = circuit.RoundAffine([(circuit.Source(1), 2)]);
        var module = WriteInteger(circuit, out var program);

        Line(module, "let b0: u32 = select(0u, 1u, sources[base + 0u] > 0.5f);");
        Line(module, "let w1: i32 = i32(sources[base + 1u]);");
        Line(module, "let s2: f32 = sources[base + 2u];");
        Line(module, $"let {V(program, sum)}: f32 = k(k(k(k(0.0f) + k(f32(b0))) + k(f32(w1))) + s2);");
        Line(module, $"outputs[o + 0u] = {V(program, sum)};");
        // A whole root converts to f32 at the store.
        Line(module, $"outputs[o + 1u] = k(f32({W(program, doubled)}));");
        Assert.Equal(1, module.Statistics.FloatRows);
        Assert.Equal(1, module.Statistics.WholeRows);
    }

    [Fact]
    public void An_affine_row_of_whole_values_is_an_i32_sum_and_a_negated_bit_is_an_XOR()
    {
        var circuit = new Circuit(3);
        var (a, b, bit) = (WholeCell(circuit, 0, -10, 10), WholeCell(circuit, 1, 0, 5), BitCell(circuit, 2));
        var sum = circuit.RoundAffine([(a, 1), (b, -3)], 2);
        var negated = circuit.RoundAffine([(bit, -1)], 1);
        var copy = circuit.RoundAffine([(bit, 1)]);
        var bits = circuit.RoundAffine([(bit, 1), (negated, 1)]);
        var negative = circuit.RoundAffine([(a, -2), (b, 1)], -1);
        var half = circuit.RoundAffine([(a, 0.5)]);
        var module = WriteInteger(circuit, out var program);

        Line(module, $"let {W(program, sum)}: i32 = 2i + w0 - 3i * w1;");
        Line(module, $"let {B(program, negated)}: u32 = b2 ^ 1u;");
        Line(module, $"let {B(program, copy)}: u32 = b2;");
        Line(module, $"let {W(program, bits)}: i32 = i32(b2) + i32({B(program, negated)});");
        Line(module, $"let {W(program, negative)}: i32 = (-1i) - 2i * w0 + w1;");
        // A weight of 0.5 is not a whole number: the row is an f32 row that reads k(f32(w0)).
        Line(module, $"let {V(program, half)}: f32 = k(k(0.0f) + k(k(0x1.000000p-1f) * k(f32(w0))));");
        var root = program.Roots.ToList().IndexOf(sum.Position);
        Line(module, $"outputs[o + {root}u] = k(f32({W(program, sum)}));");
        Assert.Equal(3, module.Statistics.WholeRows);
        Assert.Equal(2, module.Statistics.BitRows);
        Assert.Equal(1, module.Statistics.FloatRows);
    }

    // bit-emit.ts, "Gate". The gate relu(bias + Σ weight × value) has the largest value 1. Its values of
    // positive weight are bits, and its values of negative weight are nonnegative. This gate is an AND of
    // the bits and of "each value is 0".
    [Fact]
    public void A_gate_of_bits_is_an_AND_and_a_NOR()
    {
        var circuit = new Circuit(4);
        var (a, b, c) = (BitCell(circuit, 0), BitCell(circuit, 1), BitCell(circuit, 2));
        var n = WholeCell(circuit, 3, 0, 5);
        var andNot = circuit.Reglu([], 1, [(a, 1), (b, -1)], 0);
        var nor = circuit.Reglu([], 1, [(a, -1), (b, -1), (c, -1)], 1);
        var and = circuit.Reglu([], 1, [(a, 1), (b, 1)], -1);
        var zero = circuit.Reglu([], 1, [(a, 1), (n, -1)], 0);
        var module = WriteInteger(circuit, out var program);

        Line(module, $"let {B(program, andNot)}: u32 = b0 & (b1 ^ 1u);");
        Line(module, $"let {B(program, nor)}: u32 = ((b0 | b1 | b2) ^ 1u);");
        Line(module, $"let {B(program, and)}: u32 = b0 & b1;");
        Line(module, $"let {B(program, zero)}: u32 = b0 & u32(w3 == 0i);");
    }

    // A term of weight 0 adds nothing to a gate, so it gets no test. bit-emit.ts puts such a term with
    // the values that must be 0. This gives relu(a + 0 × c) = a & (c ^ 1u), and 0 for a = c = 1. The
    // public builder drops a weight of 0, so this test writes the row of a compiled program directly.
    [Fact]
    public void A_gate_term_of_weight_0_gets_no_test()
    {
        WholeProof bit = new(WholeKind.Bit, 0, 1);
        var row = new RegluRow(2, 1, [], 0, [new ShaderTerm(1, 0), new ShaderTerm(0, 1)]);
        var proof = new ProofResult([bit, bit, bit], [false, false, true]);
        var form = IntegerPlan.Conjunction(row, proof, new ShaderRow?[3]);
        Assert.NotNull(form);
        Assert.Equal([0], form.All.ToArray());
        Assert.Empty(form.ZeroBits);
        Assert.Empty(form.Tests);
    }

    // The near misses of the gate rule. In the first, a value of positive weight is not a bit. In the
    // second, a value of negative weight can be negative. In the third, the largest value is 2. Each is a
    // ReLU in i32.
    [Fact]
    public void A_gate_that_misses_one_precondition_is_max_or_a_test_above_zero()
    {
        var circuit = new Circuit(4);
        var (a, b) = (BitCell(circuit, 0), BitCell(circuit, 1));
        var (n, m) = (WholeCell(circuit, 2, 0, 2), WholeCell(circuit, 3, -1, 1));
        var notBit = circuit.Reglu([], 1, [(n, 1), (b, -1)], 0);
        var negative = circuit.Reglu([], 1, [(a, 1), (m, -1)], 0);
        var two = circuit.Reglu([], 1, [(a, 1), (b, 1)], 0);
        var unitGate = circuit.Relu(m);
        var module = WriteInteger(circuit, out var program);

        // The compiler stores the terms in the order of their positions.
        Line(module, $"let {W(program, notBit)}: i32 = max(-i32(b1) + w2, 0i);");
        Line(module, $"let {W(program, negative)}: i32 = max(i32(b0) - w3, 0i);");
        Line(module, $"let {W(program, two)}: i32 = max(i32(b0) + i32(b1), 0i);");
        // A gate within 0..1 that is not of the gate form.
        Line(module, $"let {B(program, unitGate)}: u32 = u32(w3 > 0i);");
    }

    [Fact]
    public void A_reglu_row_with_a_left_form_is_an_i32_product()
    {
        var circuit = new Circuit(3);
        var bit = BitCell(circuit, 0);
        var (n, m) = (WholeCell(circuit, 1, 0, 5), WholeCell(circuit, 2, -3, 3));
        var masked = circuit.Reglu([(n, 1)], 0, [(bit, 1)], 0);
        var signed = circuit.Reglu([(n, 2)], 1, [(m, 1)], 0);
        var module = WriteInteger(circuit, out var program);

        Line(module, $"let {W(program, masked)}: i32 = w1 * i32(b0);");
        Line(module, $"let {W(program, signed)}: i32 = (1i + 2i * w1) * max(w2, 0i);");
    }

    // bit-emit.ts, "Comparison": the pair relu(1 - relu(b - a)) that Compare builds for [a >= b] on
    // grids is one u32(b <= a). The inner ReLU gets no statement of its own.
    [Fact]
    public void The_integer_bit_of_Compare_is_one_comparison_statement()
    {
        var circuit = new Circuit(2);
        var (a, b) = (WholeCell(circuit, 0, -100, 100), WholeCell(circuit, 1, -100, 100));
        var bit = circuit.Compare(a, Relation.GreaterOrEqual, b);
        var points = circuit.RoundingPoints().Where(point => point.Kind == RoundingPointKind.IntegerBit).ToList();
        Assert.Equal(2, points.Count);
        var module = WriteInteger(circuit, out var program, [bit]);

        Line(module, $"let {B(program, bit)}: u32 = u32(w1 <= w0);");
        var inner = PositionOf(program, points[0].Wire);
        NoLineStarts(module, $"let w{inner}:");
        NoLineStarts(module, $"let b{inner}:");
        NoLineStarts(module, $"let v{inner}:");
        // The two source reads, the bit and the store of the root.
        Assert.Equal(4, module.Statistics.Statements);
        Assert.Equal(1, module.Statistics.BitRows);
        Assert.Equal(0, module.Statistics.FloatRows + module.Statistics.WholeRows);
        // The one keep is the conversion of the bit at the store of the root.
        Assert.Equal(1, module.Statistics.Keeps);
    }

    // The near miss of the comparison rule: with no grid, Compare builds a sign cascade of f32 rows. The
    // circuit declares the last row a bit, which the writer converts from its f32 value.
    [Fact]
    public void The_bit_of_a_sign_cascade_converts_its_f32_value()
    {
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), -1e6, 1e6);
        circuit.AssumeRange(circuit.Source(1), -1e6, 1e6);
        var bit = circuit.Compare(circuit.Source(0), Relation.Greater, circuit.Source(1));
        var module = WriteInteger(circuit, out var program, [bit]);

        var value = Line(module, $"let {B(program, bit)}: u32 = u32({V(program, bit)});");
        Assert.True(value > Line(module, "let s0: f32 = sources[base + 0u];"));
        Line(module, $"outputs[o + 0u] = {V(program, bit)};");
        Assert.True(module.Statistics.FloatRows > 4);
        Assert.Equal(1, module.Statistics.BitRows);
    }

    [Fact]
    public void A_boolean_atom_of_a_bit_is_the_bit_and_of_an_f32_score_is_the_converted_atom()
    {
        var circuit = new Circuit(2);
        var bit = BitCell(circuit, 0);
        circuit.AssumeRange(circuit.Source(1), 0, 1);
        var copy = circuit.Boolean(bit);
        var atom = circuit.Boolean(circuit.Source(1));
        var module = WriteInteger(circuit, out var program);

        Line(module, $"let {B(program, copy)}: u32 = b0;");
        Line(module, $"let {V(program, atom)}: f32 = k(k(k(k(0.0f) + s1) + k(8388609.0f)) - k(8388609.0f));");
        Line(module, $"let {B(program, atom)}: u32 = u32({V(program, atom)});");
    }

    // bit-emit.ts, "Read": a whole index form computes in i32 with no floor. The writer drops a clamp or
    // a select that the index range excludes.
    [Fact]
    public void An_index_read_with_a_whole_index_computes_the_index_in_i32()
    {
        var circuit = new Circuit(6);
        var wide = WholeCell(circuit, 0, -3, 5);
        foreach (var cell in new[] { 1, 2, 3 }) WholeCell(circuit, cell, -4, 4);
        var narrow = WholeCell(circuit, 4, 0, 2);
        circuit.AssumeRange(circuit.Source(5), -10, 10);
        var candidates = new[] { circuit.Source(1), circuit.Source(2), circuit.Source(3) };
        var zeroSides = circuit.IndexRead(wide, candidates, OutOfRange.Zero);
        var inRange = circuit.IndexRead(narrow, candidates);
        var floatCandidates = circuit.IndexReadAffine([(narrow, 1)], 1, [circuit.Source(1), circuit.Source(5), circuit.Source(5)], OutOfRange.Clamp, OutOfRange.Zero);
        var module = WriteInteger(circuit, out var program);

        Line(module, $"let {W(program, zeroSides)}: i32 = select(select(i32(sources[base + u32(1i + clamp(w0, 0i, 2i) * 1i)]), 0i, w0 > 2i), 0i, w0 < 0i);");
        Line(module, $"let {W(program, inRange)}: i32 = i32(sources[base + u32(1i + w4 * 1i)]);");
        var p = PositionOf(program, floatCandidates);
        Line(module, $"let x{p}: i32 = 1i + w4;");
        Line(module, $"let v{p}: f32 = select(sources[base + t0[clamp(x{p}, 0i, 2i)]], 0.0f, x{p} > 2i);");
    }

    // A list of computed candidates is a select over the candidates that the index can reach only.
    [Fact]
    public void An_index_read_of_computed_candidates_selects_the_candidates_that_the_index_can_reach()
    {
        var circuit = new Circuit(3);
        var index = WholeCell(circuit, 0, 0, 1);
        var n = WholeCell(circuit, 1, -4, 4);
        circuit.AssumeRange(circuit.Source(2), -10, 10);
        var first = circuit.RoundAffine([(n, 3)]);
        var second = circuit.RoundAffine([(n, -5)], 1);
        var third = circuit.RoundAffine([(circuit.Source(2), 0.25)]);
        var read = circuit.IndexRead(index, [first, second, third]);
        var module = WriteInteger(circuit, out var program);
        var p = PositionOf(program, read);

        // A whole cell within 0..1 is a bit, so the index is i32(b0).
        var start = Line(module, $"let x{p}: i32 = i32(b0);");
        Assert.Equal(start + 1, Line(module, $"var a{p}: i32 = {W(program, first)};"));
        Assert.Equal(start + 2, Line(module, $"a{p} = select(a{p}, {W(program, second)}, x{p} == 1i);"));
        Assert.Equal(start + 3, Line(module, $"let w{p}: i32 = a{p};"));
    }

    // A gather read of whole candidates adds the selected candidate in i32. For a group of proven bits
    // that allows all zeros, it adds the mass times the candidate.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_gather_read_of_whole_candidates_adds_the_candidate_in_i32(bool allowAllZero)
    {
        var circuit = new Circuit(6);
        var cells = Enumerable.Range(0, 6).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(cells[..3], allowAllZero);
        if (allowAllZero)
        {
            foreach (var cell in cells[..3]) circuit.AssumeBit(cell);
        }
        foreach (var cell in new[] { 3, 4, 5 }) WholeCell(circuit, cell, -5, 5);
        var sum = circuit.Affine([
            (circuit.Multiply(cells[0], cells[3]), 1),
            (circuit.Multiply(cells[1], cells[4]), 1),
            (circuit.Multiply(cells[2], cells[5]), 1),
        ], 2);
        var program = circuit.CompileTensorProgram([sum]);
        Assert.Equal(1, TensorChecks.ProgramCounts(program).GatherReads);
        var module = WgslIntegerWriter.Write(circuit, program);

        // The gather compute reads a proven bit as f32(b), which is exact.
        Line(module, allowAllZero ? "let gi0: i32 = i32(k(f32(b1)) + 2.0f * k(f32(b2)));" : "let gi0: i32 = i32(s1 + 2.0f * s2);");
        var candidate = "i32(sources[base + u32(3i + clamp(gi0, 0i, 2i) * 1i)])";
        Line(module, $"let {W(program, sum)}: i32 = 2i + {(allowAllZero ? $"i32(gm0) * {candidate}" : candidate)};");
    }
}
