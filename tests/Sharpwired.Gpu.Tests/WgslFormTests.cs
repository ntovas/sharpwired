// Handwired source: the header of lab/ui-exploration/codegen/wgsl.ts, which lists the FP32 failures
// that Handwired measured on Metal through wgpu and Dawn. These tests run the hand-written WGSL forms of
// question 2 of the phase 5 plan on each adapter. Each group has forms without keep and forms with keep.
// The forms with keep must give the CPU bits for each input. The forms without keep are written to the
// output with their counts of differences, and have no assertion, because a backend without fast math
// gives no difference.

using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class WgslFormTests(GpuFixture fixture, ITestOutputHelper output)
{
    // The lane count of each form: one workgroup of 64 invocations.
    private const int Lanes = 64;

    // consts[0] = 2^23 + 1 (the atom constant), consts[1] = 2^23 + 2^22 (the round constant),
    // consts[2] = B, consts[3] = C (the FMA weight and bias).
    private const float AtomConstant = 8388609f;
    private const float RoundConstant = 12582912f;
    private const float B = 1.0000001f;
    private const float C = -1.0000002f;
    private static readonly float[] Constants = [AtomConstant, RoundConstant, B, C];

    // The 3-term affine row: bias + W1 x + W2 y + W3 z.
    private const float Bias = 0.1f;
    private const float W1 = 0.3f;
    private const float W2 = -1.7f;
    private const float W3 = 2.9f;

    private static readonly float SmallFactor = 1e-38f;

    // The six bindings of a form kernel. A form uses some of them. The explicit layout lets a shader leave
    // a binding unused.
    private static readonly KernelBuffer[] Layout =
    [
        KernelBuffer.ReadOnlyStorage, // 0: src, 3 * Lanes values: x, then y, then z
        KernelBuffer.Storage, // 1: dst
        KernelBuffer.ReadOnlyStorage, // 2: params
        KernelBuffer.ReadOnlyStorage, // 3: consts
        KernelBuffer.Storage, // 4: scratch
        KernelBuffer.Storage, // 5: bits, atomic
    ];

    private sealed record Form(string Name, string Body, Func<float, float, float, float> Cpu);

    private sealed record Inputs(float[] X, float[] Y, float[] Z);

    private sealed record Group(string Name, Inputs Inputs, Form[] WithoutKeep, Form[] WithKeep);

    private static readonly Dictionary<string, Group> Groups = CreateGroups().ToDictionary(group => group.Name);

    public static TheoryData<string> GroupNames => [.. CreateGroups().Select(group => group.Name)];

    [Theory]
    [MemberData(nameof(GroupNames))]
    public void The_forms_with_keep_give_the_cpu_bits(string groupName)
    {
        var group = Groups[groupName];
        fixture.ForEachDevice(output, device =>
        {
            foreach (var form in group.WithoutKeep)
            {
                var differences = Differences(form, group.Inputs, Run(device, form, group.Inputs));
                output.WriteLine($"without keep: {form.Name}: {differences.Count} of {Lanes} differ{Example(differences)}");
            }

            foreach (var form in group.WithKeep)
            {
                var differences = Differences(form, group.Inputs, Run(device, form, group.Inputs));
                output.WriteLine($"with keep: {form.Name}: {differences.Count} of {Lanes} differ{Example(differences)}");
                Assert.True(differences.Count == 0, $"{form.Name} on {device.Adapter.Name}: {differences.Count} of {Lanes} differ{Example(differences)}");
            }
        });
    }

    [Fact]
    public void FlushesSubnormals_equals_the_result_of_the_probe()
    {
        // k(x * k(2^-10)) with x = 2^-120. The exact result is 2^-130, a subnormal value.
        var x = MathF.ScaleB(1f, -120);
        var probe = new Form("probe", $"r = k(x * k({L(MathF.ScaleB(1f, -10))}));", (a, _, _) => a * MathF.ScaleB(1f, -10));
        var inputs = new Inputs(Enumerable.Repeat(x, Lanes).ToArray(), new float[Lanes], new float[Lanes]);
        fixture.ForEachDevice(output, device =>
        {
            var result = Run(device, probe, inputs);
            output.WriteLine($"probe: {result[0]:R} (2^-130 is {MathF.ScaleB(1f, -130):R}), FlushesSubnormals {device.FlushesSubnormals}");
            foreach (var value in result) Js.Is(device.FlushesSubnormals ? 0f : MathF.ScaleB(1f, -130), value);
        });
    }

    [Fact]
    public void A_subnormal_product_follows_FlushesSubnormals()
    {
        var inputs = new Inputs(Fill([0.3f, -0.3f, 1f, -1f, 0.5f], 11, -1.1f, 1.1f), new float[Lanes], new float[Lanes]);
        var withoutKeep = new Form("x * 1e-38", $"r = x * {L(SmallFactor)};", (x, _, _) => x * SmallFactor);
        var withKeep = new Form("k(x * k(1e-38))", $"r = k(x * k({L(SmallFactor)}));", (x, _, _) => x * SmallFactor);
        fixture.ForEachDevice(output, device =>
        {
            var plain = Differences(withoutKeep, inputs, Run(device, withoutKeep, inputs));
            output.WriteLine($"without keep: {withoutKeep.Name}: {plain.Count} of {Lanes} differ{Example(plain)}");
            var result = Run(device, withKeep, inputs);
            var kept = Differences(withKeep, inputs, result);
            output.WriteLine($"with keep: {withKeep.Name}: {kept.Count} of {Lanes} differ{Example(kept)}");
            for (var i = 0; i < Lanes; i++)
            {
                var exact = withKeep.Cpu(inputs.X[i], 0, 0);
                Assert.True(float.IsSubnormal(exact), $"the input {inputs.X[i]:R} gives no subnormal product");
                if (device.FlushesSubnormals)
                {
                    // A flush writes a zero. The sign of the flushed zero is not part of the contract.
                    Assert.True(Js.SameNumericValue(0f, result[i]), $"x = {inputs.X[i]:R}: expected a flushed 0, got {result[i]:R}");
                }
                else
                {
                    Js.Is(exact, result[i]);
                }
            }
        });
    }

    private static IEnumerable<Group> CreateGroups()
    {
        var atomInputs = new Inputs(Fill([0f, -0f, 1f, 0.5f, 0.49999997f, 0.50000006f, 0.25f, 0.75f], 1, -2f, 3f), new float[Lanes], new float[Lanes]);
        float Atom(float x, float y, float z) => (x + AtomConstant) - AtomConstant;
        yield return new Group("boolean atom", atomInputs,
        [
            new Form("one expression", "r = (x + 8388609.0f) - 8388609.0f;", Atom),
            new Form("let", "let s = x + 8388609.0f; r = s - 8388609.0f;", Atom),
            new Form("var", "var s = x + 8388609.0f; r = s - 8388609.0f;", Atom),
            new Form("bitcast", "let s = bitcast<f32>(bitcast<u32>(x + 8388609.0f)); r = s - 8388609.0f;", Atom),
            new Form("storage buffer, literal K", "scratch[i] = x + 8388609.0f; r = scratch[i] - 8388609.0f;", Atom),
            new Form("K from a storage buffer", "r = (x + consts[0]) - consts[0];", Atom),
            new Form("K from a storage buffer, sum through a storage buffer", "scratch[i] = x + consts[0]; r = scratch[i] - consts[0];", Atom),
            new Form("workgroup variable and barrier", "shared_values[local] = x + consts[0]; workgroupBarrier(); r = shared_values[local] - consts[0];", Atom),
            new Form("atomic store and load", "atomicStore(&bits[i], bitcast<u32>(x + consts[0])); r = bitcast<f32>(atomicLoad(&bits[i])) - consts[0];", Atom),
        ],
        [
            new Form("k(k(x + k(8388609.0)) - k(8388609.0))", "r = k(k(x + k(8388609.0f)) - k(8388609.0f));", Atom),
        ]);

        var roundInputs = new Inputs(RoundValues(), new float[Lanes], new float[Lanes]);
        float Round(float x, float y, float z) => (x + RoundConstant) - RoundConstant;
        yield return new Group("round chain", roundInputs,
        [
            new Form("(x + 12582912.0) - 12582912.0", "r = (x + 12582912.0f) - 12582912.0f;", Round),
            new Form("WGSL round(x)", "r = round(x);", Round),
        ],
        [
            new Form("k(k(x + k(12582912.0)) - k(12582912.0))", "r = k(k(x + k(12582912.0f)) - k(12582912.0f));", Round),
        ]);

        var fmaInputs = new Inputs(NearOne(), new float[Lanes], new float[Lanes]);
        float Affine(float x, float y, float z) => (x * B) + C;
        yield return new Group("product and sum (FMA)", fmaInputs,
        [
            new Form("x * b + c, literals", $"r = x * {L(B)} + {L(C)};", Affine),
            new Form("x * b + c, storage buffer", "r = x * consts[2] + consts[3];", Affine),
        ],
        [
            new Form("k(x * b) + c", $"r = k(x * {L(B)}) + {L(C)};", Affine),
            new Form("k(k(x * k(b)) + k(c))", $"r = k(k(x * k({L(B)})) + k({L(C)}));", Affine),
        ]);

        var sumInputs = new Inputs(Fill([], 2, -1000f, 1000f), Fill([], 3, -1000f, 1000f), Fill([], 4, -1000f, 1000f));
        float Sum(float x, float y, float z) => ((Bias + (W1 * x)) + (W2 * y)) + (W3 * z);
        yield return new Group("3-term affine sum", sumInputs,
        [
            new Form("one expression", $"r = {L(Bias)} + {L(W1)} * x + {L(W2)} * y + {L(W3)} * z;", Sum),
        ],
        [
            new Form("left fold with keep", $"r = k(k(k(k({L(Bias)}) + k(k({L(W1)}) * x)) + k(k({L(W2)}) * y)) + k(k({L(W3)}) * z));", Sum),
        ]);

        var specialInputs = new Inputs(Fill([float.NaN, -float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -0f, 1f, -1f, float.MaxValue, float.MinValue, 1.1754944e-38f, -1.1754944e-38f], 5, -100f, 100f), new float[Lanes], new float[Lanes]);
        yield return new Group("-0 product", specialInputs,
        [
            new Form("-abs(x) * 0.0", "r = -abs(x) * 0.0f;", (x, _, _) => -MathF.Abs(x) * 0f),
        ],
        [
            new Form("k(-abs(x) * k(0.0))", "r = k(-abs(x) * k(0.0f));", (x, _, _) => -MathF.Abs(x) * 0f),
        ]);

        yield return new Group("ReLU", specialInputs,
        [
            new Form("max(0.0, x)", "r = max(0.0f, x);", (x, _, _) => MathF.Max(0f, x)),
        ],
        [
            new Form("relu(x)", "r = relu(x);", (x, _, _) => MathF.Max(0f, x)),
        ]);

        yield return new Group("NaN and finite tests", specialInputs,
        [
            new Form("x != x", "r = select(0.0f, 1.0f, x != x);", (x, _, _) => float.IsNaN(x) ? 1f : 0f),
        ],
        [
            new Form("is_nan(x)", "r = select(0.0f, 1.0f, is_nan(x));", (x, _, _) => float.IsNaN(x) ? 1f : 0f),
            new Form("is_finite(x)", "r = select(0.0f, 1.0f, is_finite(x));", (x, _, _) => float.IsFinite(x) ? 1f : 0f),
        ]);

        yield return new Group("NaN and infinity arithmetic", specialInputs,
        [
            new Form("x + 1.0", "r = x + 1.0f;", (x, _, _) => x + 1f),
            new Form("x * 0.0", "r = x * 0.0f;", (x, _, _) => x * 0f),
        ],
        [
            new Form("k(x + k(1.0))", "r = k(x + k(1.0f));", (x, _, _) => x + 1f),
            new Form("k(x * k(0.0))", "r = k(x * k(0.0f));", (x, _, _) => x * 0f),
        ]);
    }

    private static string Shader(Form form) => WgslText.Prelude + $$"""
        @group(0) @binding(0) var<storage, read> src: array<f32>;
        @group(0) @binding(1) var<storage, read_write> dst: array<f32>;
        @group(0) @binding(2) var<storage, read> params: Params;
        @group(0) @binding(3) var<storage, read> consts: array<f32>;
        @group(0) @binding(4) var<storage, read_write> scratch: array<f32>;
        @group(0) @binding(5) var<storage, read_write> bits: array<atomic<u32>>;
        var<workgroup> shared_values: array<f32, {{Lanes}}>;

        @compute @workgroup_size({{Lanes}})
        fn main(@builtin(global_invocation_id) id: vec3<u32>, @builtin(local_invocation_index) local: u32) {
            // The dispatch has exactly one invocation for each lane, so each read is in range, and each
            // invocation reaches a barrier.
            let i = id.x;
            let x = src[i];
            let y = src[params.count + i];
            let z = src[2u * params.count + i];
            var r: f32 = 0.0f;
            {{form.Body}}
            dst[i] = r;
        }
        """;

    private static float[] Run(GpuDevice device, Form form, Inputs inputs)
    {
        using var kernel = new ComputeKernel(device, Shader(form), Layout);
        kernel.Write<float>(0, [.. inputs.X, .. inputs.Y, .. inputs.Z]);
        kernel.Write<uint>(2, [0u, Lanes, 0u, 0u]);
        kernel.Write<float>(3, Constants);
        kernel.Reserve(1, Lanes * sizeof(float));
        kernel.Reserve(4, Lanes * sizeof(float));
        kernel.Reserve(5, Lanes * sizeof(uint));
        var result = new float[Lanes];
        kernel.Run(1, 1, result.AsSpan());
        return result;
    }

    // The lanes whose GPU value differs from the CPU value in its bits (Object.is).
    private static List<(float X, float Expected, float Actual)> Differences(Form form, Inputs inputs, float[] result)
    {
        var differences = new List<(float, float, float)>();
        for (var i = 0; i < Lanes; i++)
        {
            var expected = form.Cpu(inputs.X[i], inputs.Y[i], inputs.Z[i]);
            if (!Js.ObjectIs(expected, result[i])) differences.Add((inputs.X[i], expected, result[i]));
        }

        return differences;
    }

    private static string Example(List<(float X, float Expected, float Actual)> differences) =>
        differences.Count == 0 ? "" : $" (x = {differences[0].X:R}: CPU {differences[0].Expected:R}, GPU {differences[0].Actual:R})";

    // The given values, then seeded values in [low, high) up to the lane count.
    private static float[] Fill(float[] given, int seed, float low, float high)
    {
        var random = new Random(seed);
        var values = new float[Lanes];
        given.CopyTo(values, 0);
        for (var i = given.Length; i < Lanes; i++) values[i] = (float)(low + (random.NextDouble() * (high - low)));
        return values;
    }

    // The ties 0.5, 1.5, 2.5, -0.5, -1.5, the values 1 ulp on each side of each tie, and seeded values
    // within ±2^22.
    private static float[] RoundValues()
    {
        var given = new List<float>();
        foreach (var tie in new[] { 0.5f, 1.5f, 2.5f, -0.5f, -1.5f })
        {
            given.Add(tie);
            given.Add(MathF.BitDecrement(tie));
            given.Add(MathF.BitIncrement(tie));
        }

        var values = Fill([.. given], 6, -4194304f, 4194304f);
        // Half of the seeded values get a fraction, so the chain rounds them.
        var random = new Random(7);
        for (var i = given.Count; i < Lanes; i += 2) values[i] = MathF.Round(values[i]) + (float)random.NextDouble();
        return values;
    }

    // 64 consecutive FP32 values around 1, where a fused multiply-add gives a different result.
    private static float[] NearOne()
    {
        var values = new float[Lanes];
        var value = 1f;
        for (var i = 0; i < Lanes / 2; i++) value = MathF.BitDecrement(value);
        for (var i = 0; i < Lanes; i++)
        {
            values[i] = value;
            value = MathF.BitIncrement(value);
        }

        return values;
    }

    // The exact WGSL literal of an FP32 value: a hexadecimal float with the bits of the value
    // (wgslFloat of emit.ts).
    private static string L(float value)
    {
        if (value == 0) return float.IsNegative(value) ? "(-0.0f)" : "0.0f";
        var bits = BitConverter.SingleToUInt32Bits(value);
        var exponent = (int)((bits >> 23) & 0xff);
        var fraction = ((bits & 0x7fffff) << 1).ToString("x6");
        var magnitude = exponent == 0 ? $"0x0.{fraction}p-126f" : $"0x1.{fraction}p{exponent - 127}f";
        return bits >> 31 == 1 ? $"(-{magnitude})" : magnitude;
    }
}
