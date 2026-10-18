// Handwired source: docs/execution-model.md, "Rounding points". Spec item R5.3: the boolean atom, a round
// chain and a sign cascade run on each WebGPU adapter, and each value equals the reference evaluator.
// The inputs are at the rounding boundaries (question 6 of the phase 5 plan). The FMA program checks
// that the writer blocks the contraction of a product and a sum. Each program compiles with every wire
// as a root, so the test compares each live wire, and not only the last one. Each test runs with both
// writers. The test compares the integer writer with sameNumericValue, because an i32 has no -0.

using Sharpwired.Core;
using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class RoundingPointTests(GpuFixture fixture, ITestOutputHelper output)
{
    private const int SeededCount = 256;

    public static TheoryData<GpuWriter> Writers => new() { GpuWriter.Float32, GpuWriter.Integer };

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_boolean_atom_equals_the_reference_at_the_rounding_boundary(GpuWriter writer)
    {
        var circuit = new Circuit(2);
        var (x, u) = (circuit.Source(0), circuit.Source(1));
        circuit.AssumeRange(x, 0, 2);
        circuit.AssumeRange(u, 0, 1);
        circuit.Boolean(circuit.RoundAffine([(x, 0.3)], 0.1));
        circuit.Boolean(u);

        // The scores of u, and values of x near 4/3, where 0.3 x + 0.1 is 0.5.
        float[] scores = [0f, -0f, 1f, 0.5f, 0.49999997f, 0.50000006f, 0.25f, 0.75f];
        var random = Js.CreateRandom(0xa70);
        var tables = new List<float[]>();
        var nearHalf = 4f / 3;
        for (var step = 0; step < 16; step++) nearHalf = MathF.BitDecrement(nearHalf);
        for (var index = 0; index < scores.Length + SeededCount; index++)
        {
            var score = index < scores.Length ? scores[index] : (float)random();
            var xValue = index < 32 ? nearHalf : (float)(random() * 2);
            if (index < 32) nearHalf = MathF.BitIncrement(nearHalf);
            tables.Add([xValue, score]);
        }
        Check(circuit, tables, writer);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_round_chain_equals_the_reference_at_the_ties(GpuWriter writer)
    {
        var circuit = new Circuit(1);
        var x = circuit.Source(0);
        circuit.AssumeRange(x, -4_194_304, 4_194_304);
        var shifted = circuit.RoundAffine([(x, 1)], 12_582_912);
        var rounded = circuit.RoundAffine([(shifted, 1)], -12_582_912);
        circuit.MarkRoundingPoint(shifted);
        circuit.MarkRoundingPoint(rounded);

        var values = new List<float>();
        foreach (var tie in new[] { 0.5f, 1.5f, 2.5f, -0.5f, -1.5f })
        {
            values.Add(tie);
            values.Add(MathF.BitDecrement(tie));
            values.Add(MathF.BitIncrement(tie));
        }
        var random = Js.CreateRandom(0x40c);
        for (var index = 0; index < SeededCount; index++)
        {
            var whole = Math.Floor((random() - 0.5) * 8_388_608);
            // Half of the values get a fraction, so the chain rounds them.
            values.Add((float)(index % 2 == 0 ? whole + random() : whole));
        }
        Check(circuit, [.. values.Select(value => new[] { value })], writer);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void The_sign_cascade_equals_the_reference_for_near_and_equal_operands(GpuWriter writer)
    {
        var circuit = new Circuit(2);
        var (a, b) = (circuit.Source(0), circuit.Source(1));
        circuit.AssumeRange(a, -1e6, 1e6);
        circuit.AssumeRange(b, -1e6, 1e6);
        var greater = circuit.Compare(a, Relation.Greater, b);
        var equal = circuit.Compare(a, Relation.Equal, b);
        Assert.Contains(circuit.RoundingPoints(), point => point.Kind == RoundingPointKind.SignCascade);

        var pairs = new List<float[]> { new[] { 1f, 1f }, new[] { 0f, -0f }, new[] { -0f, 0f }, new[] { -7.25f, -7.25f } };
        foreach (var value in new[] { 1f, 1000f, 1e6f, -1f, -1000f })
        {
            var below = MathF.BitDecrement(value);
            pairs.Add([value, below]);
            pairs.Add([below, value]);
        }
        foreach (var difference in new[] { MathF.ScaleB(1f, -126), MathF.ScaleB(1f, -100) })
        {
            pairs.Add([difference, 0f]);
            pairs.Add([0f, difference]);
            pairs.Add([-difference, 0f]);
        }
        var random = Js.CreateRandom(0x5c4);
        for (var index = 0; index < SeededCount; index++)
        {
            var left = (float)((random() - 0.5) * 2e6);
            var right = random() < 0.25 ? left : random() < 0.5 ? MathF.BitIncrement(left) : (float)((random() - 0.5) * 2e6);
            pairs.Add([left, right]);
        }
        Check(circuit, pairs, writer);

        // A difference of 2^-140 is a subnormal value. A device that flushes it gives the bits of equal
        // operands (execution-model.md, "Rounding points"). Another device gives the reference.
        var tiny = MathF.ScaleB(1f, -140);
        var program = circuit.CompileTensorProgram([greater, equal]);
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            float[][] tinyPairs = [[tiny, 0f], [0f, tiny]];
            foreach (var pair in tinyPairs)
            {
                var actual = evaluator.Evaluate(pair, 1);
                var expected = device.FlushesSubnormals ? circuit.EvaluateReference([0f, 0f]) : circuit.EvaluateReference(pair);
                output.WriteLine($"a = {pair[0]:R}, b = {pair[1]:R}: greater {actual[0]:R}, equal {actual[1]:R}");
                Agrees(writer, expected[greater.Position], actual[0]);
                Agrees(writer, expected[equal.Position], actual[1]);
            }
        });
    }

    // x × 1.0000001 - 1.0000002 over the 64 FP32 values around 1. A fused multiply-add gives other bits.
    [Theory]
    [MemberData(nameof(Writers))]
    public void The_affine_row_is_not_contracted_to_a_fused_multiply_add(GpuWriter writer)
    {
        var circuit = new Circuit(1);
        var x = circuit.Source(0);
        circuit.AssumeRange(x, 0.5, 2);
        circuit.RoundAffine([(x, 1.0000001)], -1.0000002);
        Check(circuit, [.. WgslFormTests.NearOne().Select(value => new[] { value })], writer);
    }

    // Object.is for the f32 writer, sameNumericValue for the integer writer (question 3 of the plan).
    private static void Agrees(GpuWriter writer, float expected, float actual)
    {
        if (writer == GpuWriter.Float32) Js.Is(expected, actual);
        else Assert.True(Js.SameNumericValue(expected, actual), $"got {actual:R}, reference {expected:R}");
    }

    // Run the program of every wire on each device, and compare each live wire with the reference.
    private void Check(Circuit circuit, IReadOnlyList<float[]> tables, GpuWriter writer)
    {
        var program = circuit.CompileTensorProgram();
        var sources = tables.SelectMany(table => table).ToArray();
        fixture.ForEachDevice(output, device =>
        {
            using var evaluator = GpuEvaluator.Create(device, circuit, program, writer);
            var outputs = evaluator.Evaluate(sources, tables.Count);
            var (compared, excluded) = GpuChecks.Compare(device, circuit, program, sources, tables.Count, outputs, sameNumericValue: writer == GpuWriter.Integer);
            var statistics = evaluator.Module.Statistics;
            output.WriteLine($"[rounding] {writer}: {compared} evaluations compared, {excluded} with a subnormal value not compared; "
                + $"rows: {statistics.FloatRows} f32, {statistics.WholeRows} i32, {statistics.BitRows} u32");
            Assert.True(compared > 0);
        });
    }
}
