// No Handwired source. The tests of GpuDevice and ComputeKernel: the adapters, the open, a trivial
// kernel, the error scopes, the explicit bind group layout and the disposal. GpuPolicyTests checks the
// zero-adapter rule with fake adapter lists and needs no adapter.

using Xunit.Abstractions;

namespace Sharpwired.Gpu.Tests;

[Collection(GpuCollection.Name)]
public sealed class DeviceTests(GpuFixture fixture, ITestOutputHelper output)
{
    // A kernel over three buffers: the sources, the outputs and the parameters (WgslText.Prelude).
    private const string Bindings = """
        @group(0) @binding(0) var<storage, read> src: array<f32>;
        @group(0) @binding(1) var<storage, read_write> dst: array<f32>;
        @group(0) @binding(2) var<storage, read> params: Params;

        """;

    private static readonly KernelBuffer[] ThreeBuffers = [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage];

    private static string DoubleKernel(int workgroupSize) => WgslText.Prelude + Bindings + $$"""
        @compute @workgroup_size({{workgroupSize}})
        fn main(@builtin(global_invocation_id) id: vec3<u32>) {
            let i = id.x;
            if (i >= params.count) { return; }
            dst[i] = src[i] * 2.0f;
        }
        """;

    private static float[] RunDouble(ComputeKernel kernel, float[] values)
    {
        kernel.Write<float>(0, values);
        kernel.Write<uint>(2, [0u, (uint)values.Length, 0u, 0u]);
        kernel.Reserve(1, values.Length * sizeof(float));
        var result = new float[values.Length];
        kernel.Run((uint)((values.Length + 63) / 64), 1, result.AsSpan());
        return result;
    }

    private static float[] SeededValues(int count, int seed)
    {
        var random = new Random(seed);
        var values = new float[count];
        for (var i = 0; i < count; i++) values[i] = (float)((random.NextDouble() - 0.5) * 2e6);
        return values;
    }

    [Fact]
    public void ListAdapters_gives_each_adapter_with_a_name_and_a_backend()
    {
        if (!fixture.RequireAdapters(output)) return;
        var listed = GpuDevice.ListAdapters();
        Assert.NotEmpty(listed);
        for (var i = 0; i < listed.Count; i++)
        {
            var adapter = listed[i];
            output.WriteLine($"[gpu] adapter {adapter.Index}: {adapter.Name}, {adapter.Backend}, {adapter.Type}");
            Assert.Equal(i, adapter.Index);
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            Assert.True(Enum.IsDefined(adapter.Backend));
            Assert.True(Enum.IsDefined(adapter.Type));
        }

        Assert.Equal(listed, fixture.Adapters);
    }

    [Fact]
    public void Each_device_reports_its_limits()
    {
        fixture.ForEachDevice(output, device =>
        {
            output.WriteLine($"MaxStorageBufferBindingSize {device.MaxStorageBufferBindingSize}, MaxWorkgroupsPerDimension {device.MaxWorkgroupsPerDimension}");
            // The WebGPU defaults: 128 MiB for one storage binding, 65,535 workgroups in one dimension.
            Assert.True(device.MaxStorageBufferBindingSize >= 134_217_728);
            Assert.True(device.MaxWorkgroupsPerDimension >= 65_535);
        });
    }

    [Fact]
    public void Each_adapter_doubles_1000_values()
    {
        fixture.ForEachDevice(output, device =>
        {
            var values = SeededValues(1000, 7);
            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            var result = RunDouble(kernel, values);
            for (var i = 0; i < values.Length; i++) Js.Is((float)(values[i] * 2f), result[i]);
        });
    }

    [Fact]
    public void Open_without_an_adapter_opens_the_high_performance_adapter()
    {
        fixture.ForEachDevice(output, _ =>
        {
            using var device = GpuDevice.Open();
            output.WriteLine($"Open() gave {device.Adapter.Name}, {device.Adapter.Backend}");
            Assert.Contains(device.Adapter, fixture.Adapters);
            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            Assert.Equal([2f, -6f], RunDouble(kernel, [1f, -3f]));
        });
    }

    [Fact]
    public void A_bad_shader_throws_the_wgpu_message_and_the_device_continues()
    {
        fixture.ForEachDevice(output, device =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ComputeKernel(device, "@compute @workgroup_size(1) fn main( {", ThreeBuffers));
            output.WriteLine(exception.Message);
            Assert.Contains("Validation Error", exception.Message);
            Assert.Contains("shader module", exception.Message);

            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            Assert.Equal([2f, 4f, 6f], RunDouble(kernel, [1f, 2f, 3f]));
        });
    }

    [Fact]
    public void A_pipeline_that_does_not_match_the_layout_throws_and_the_device_continues()
    {
        fixture.ForEachDevice(output, device =>
        {
            // The shader writes binding 0, and the layout declares binding 0 read-only.
            var code = WgslText.Prelude + """
                @group(0) @binding(0) var<storage, read_write> dst: array<f32>;
                @group(0) @binding(1) var<storage, read> params: Params;
                @compute @workgroup_size(1) fn main() { dst[0] = k(1.0f); }
                """;
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ComputeKernel(device, code, [KernelBuffer.ReadOnlyStorage, KernelBuffer.ReadOnlyStorage]));
            output.WriteLine(exception.Message);
            Assert.Contains("Validation Error", exception.Message);
            Assert.Contains("pipeline", exception.Message);

            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            Assert.Equal([10f], RunDouble(kernel, [5f]));
        });
    }

    [Fact]
    public void A_layout_binding_that_the_shader_does_not_use_causes_no_error()
    {
        fixture.ForEachDevice(output, device =>
        {
            // Binding 3 is in the layout and not in the shader. The automatic layout drops it, so a bind
            // group with 4 entries makes a validation error there.
            using var kernel = new ComputeKernel(device, DoubleKernel(64),
                [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage]);
            kernel.Reserve(3, 64);
            Assert.Equal([2f, 4f], RunDouble(kernel, [1f, 2f]));
        });
    }

    [Fact]
    public void A_dispatch_past_the_workgroup_limit_throws_and_the_device_continues()
    {
        fixture.ForEachDevice(output, device =>
        {
            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            kernel.Write<float>(0, [1f]);
            kernel.Write<uint>(2, [0u, 1u, 0u, 0u]);
            kernel.Reserve(1, 4);
            var exception = Assert.Throws<InvalidOperationException>(() =>
                kernel.Run((uint)device.MaxWorkgroupsPerDimension + 1, 1, new float[1].AsSpan()));
            output.WriteLine(exception.Message);
            Assert.Contains("Validation Error", exception.Message);

            Assert.Equal([2f, 4f], RunDouble(kernel, [1f, 2f]));
        });
    }

    [Fact]
    public void Dispose_two_times_does_not_throw_and_a_call_after_Dispose_throws()
    {
        fixture.ForEachDevice(output, shared =>
        {
            var device = GpuDevice.Open(shared.Adapter);
            var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            Assert.Equal([2f], RunDouble(kernel, [1f]));

            kernel.Dispose();
            kernel.Dispose();
            Assert.Throws<ObjectDisposedException>(() => kernel.Write<float>(0, [1f]));
            Assert.Throws<ObjectDisposedException>(() => kernel.Run(1, 1, new float[1].AsSpan()));

            device.Dispose();
            device.Dispose();
            Assert.Throws<ObjectDisposedException>(() => new ComputeKernel(device, DoubleKernel(64), ThreeBuffers));
        });
    }

    [Fact]
    public void A_kernel_checks_its_arguments()
    {
        fixture.ForEachDevice(output, device =>
        {
            using var kernel = new ComputeKernel(device, DoubleKernel(64), ThreeBuffers);
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Write<float>(3, [1f]));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Reserve(-1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => kernel.Reserve(0, -4));
            Assert.Throws<ArgumentException>(() => kernel.Write<byte>(0, [1, 2, 3]));
            kernel.Reserve(1, 8);
            // The read binding holds 8 bytes, and the destination needs 12.
            Assert.Throws<ArgumentException>(() => kernel.Run(1, 1, new float[3].AsSpan()));
        });
    }
}

public sealed class GpuPolicyTests
{
    private static readonly GpuAdapterInfo FakeAdapter = new(0, "Fake GPU", GpuBackend.Vulkan, GpuAdapterType.DiscreteGpu);

    [Fact]
    public void No_adapter_and_no_reason_fails_with_the_message()
    {
        var result = GpuPolicy.Decide([], null);
        Assert.Equal(GpuPolicyAction.Fail, result.Action);
        Assert.Equal(GpuPolicy.NoAdapterMessage, result.Message);
        Assert.Equal(GpuPolicyAction.Fail, GpuPolicy.Decide([], "  ").Action);
    }

    [Fact]
    public void No_adapter_and_a_reason_passes_with_the_reason()
    {
        var result = GpuPolicy.Decide([], "CI machine with no GPU");
        Assert.Equal(GpuPolicyAction.PassWithReason, result.Action);
        Assert.Equal("[gpu] no adapter: CI machine with no GPU", result.Message);
    }

    [Fact]
    public void A_list_error_is_in_the_failure_message()
    {
        var result = GpuPolicy.Decide([], null, "DllNotFoundException: wgpu_native");
        Assert.Equal(GpuPolicyAction.Fail, result.Action);
        Assert.StartsWith(GpuPolicy.NoAdapterMessage, result.Message);
        Assert.Contains("DllNotFoundException: wgpu_native", result.Message);
    }

    [Fact]
    public void An_adapter_runs_the_tests_with_or_without_a_reason()
    {
        Assert.Equal(GpuPolicyAction.Run, GpuPolicy.Decide([FakeAdapter], null).Action);
        Assert.Equal(GpuPolicyAction.Run, GpuPolicy.Decide([FakeAdapter], "CI machine with no GPU").Action);
    }
}
