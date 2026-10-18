// Handwired source: src/json-search/gpu/host.ts (createProgramRunner and its evaluate function). One
// compute shader with an explicit bind group layout, one storage buffer for each binding, the dispatch,
// and the readback through a map-read buffer. This file and GpuDevice.cs are the only files that call
// the native API.

using System.Text;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Sharpwired.Gpu;

/// <summary>The kind of one binding of a kernel. Binding i of group 0 is item i of the layout.</summary>
internal enum KernelBuffer
{
    // var<storage, read>
    ReadOnlyStorage,

    // var<storage, read_write>
    Storage,
}

/// <summary>
/// One compute shader with its buffers. The bind group layout comes from the list of buffer kinds, and
/// not from the shader. So a binding that the shader does not use causes no error (the automatic layout
/// of wgpu drops such a binding). Each call takes the lock of the device.
/// </summary>
internal sealed unsafe class ComputeKernel : IDisposable
{
    // A buffer holds at least 16 bytes, so that an unset binding of a 12-byte struct is valid.
    private const long MinimumBufferBytes = 16;

    private readonly GpuDevice device;
    private readonly KernelBuffer[] layout;
    private readonly nint[] buffers;
    private readonly long[] capacities;
    private readonly long[] sizes;
    private ShaderModule* module;
    private BindGroupLayout* bindGroupLayout;
    private PipelineLayout* pipelineLayout;
    private ComputePipeline* pipeline;
    private BindGroup* bindGroup;
    private WgpuBuffer* readback;
    private long readbackCapacity;
    private bool disposed;

    /// <summary>
    /// Create the shader module, the bind group layout, the pipeline layout and the pipeline, each inside
    /// the error scopes of the device. Throw <see cref="InvalidOperationException"/> with the wgpu
    /// message for a shader or pipeline error.
    /// </summary>
    public ComputeKernel(GpuDevice device, string code, ReadOnlySpan<KernelBuffer> layout, string entryPoint = "main")
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentException.ThrowIfNullOrEmpty(entryPoint);
        if (layout.IsEmpty) throw new ArgumentException("the layout holds no binding", nameof(layout));
        this.device = device;
        this.layout = layout.ToArray();
        buffers = new nint[layout.Length];
        capacities = new long[layout.Length];
        sizes = new long[layout.Length];
        lock (device.Sync)
        {
            device.ThrowIfUnusable();
            try
            {
                CreateModule(code);
                CreatePipeline(entryPoint);
            }
            catch
            {
                ReleaseNative();
                throw;
            }
        }
    }

    private static WebGPU Api => GpuDevice.Api;

    /// <summary>Make the buffer of a binding hold at least <paramref name="bytes"/> bytes.</summary>
    public void Reserve(int binding, long bytes)
    {
        lock (device.Sync)
        {
            ThrowIfUnusable();
            CheckBinding(binding);
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            EnsureCapacity(binding, bytes);
            sizes[binding] = bytes;
        }
    }

    /// <summary>
    /// Write the data to the start of the buffer of a binding. The buffer grows when it is too small.
    /// The length of the data in bytes must be a multiple of 4.
    /// </summary>
    public void Write<T>(int binding, ReadOnlySpan<T> data) where T : unmanaged
    {
        lock (device.Sync)
        {
            ThrowIfUnusable();
            CheckBinding(binding);
            var bytes = (long)data.Length * sizeof(T);
            if (bytes % 4 != 0) throw new ArgumentException($"the data holds {bytes} bytes, which is not a multiple of 4", nameof(data));
            EnsureCapacity(binding, bytes);
            sizes[binding] = bytes;
            if (bytes == 0) return;
            fixed (T* source = data)
            {
                Api.QueueWriteBuffer(device.Queue, (WgpuBuffer*)buffers[binding], 0, source, (nuint)bytes);
            }
        }
    }

    /// <summary>
    /// Dispatch <paramref name="workgroups"/> workgroups in x, then copy the start of the buffer of
    /// <paramref name="readBinding"/> into <paramref name="destination"/>. Wait until the GPU is done.
    /// </summary>
    public void Run<T>(uint workgroups, int readBinding, Span<T> destination) where T : unmanaged
    {
        lock (device.Sync)
        {
            ThrowIfUnusable();
            CheckBinding(readBinding);
            var bytes = (long)destination.Length * sizeof(T);
            if (bytes % 4 != 0) throw new ArgumentException($"the destination holds {bytes} bytes, which is not a multiple of 4", nameof(destination));
            if (bytes > sizes[readBinding])
                throw new ArgumentException($"the destination holds {bytes} bytes, and binding {readBinding} holds {sizes[readBinding]}", nameof(destination));
            for (var binding = 0; binding < layout.Length; binding++) EnsureCapacity(binding, 0);
            EnsureBindGroup();
            if (bytes > readbackCapacity)
            {
                ReleaseReadback();
                readback = device.CreateBuffer(Align(bytes), BufferUsage.MapRead | BufferUsage.CopyDst);
                readbackCapacity = Align(bytes);
            }

            device.Checked("the dispatch", () => Submit(workgroups, readBinding, bytes));
            if (bytes == 0) return;
            device.MapRead(readback, bytes);
            var mapped = Api.BufferGetConstMappedRange(readback, 0, (nuint)bytes);
            if (mapped == null)
            {
                Api.BufferUnmap(readback);
                throw new InvalidOperationException($"{device.Adapter.Name}: the readback map gave no memory");
            }

            new ReadOnlySpan<T>(mapped, destination.Length).CopyTo(destination);
            Api.BufferUnmap(readback);
        }
    }

    /// <summary>Release the native objects of the kernel. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            ReleaseNative();
        }
    }

    private static long Align(long bytes) => Math.Max(MinimumBufferBytes, (bytes + 3) / 4 * 4);

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        device.ThrowIfUnusable();
    }

    private void CheckBinding(int binding) =>
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)binding, (uint)layout.Length, nameof(binding));

    private void CreateModule(string code)
    {
        var text = Encoding.UTF8.GetBytes(code + "\0");
        device.Checked("the shader module", () =>
        {
            fixed (byte* source = text)
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = source,
                };
                var descriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
                module = Api.DeviceCreateShaderModule(device.Handle, &descriptor);
                GpuDevice.Created(module);
            }
        });
    }

    private void CreatePipeline(string entryPoint)
    {
        var entryName = Encoding.UTF8.GetBytes(entryPoint + "\0");
        device.Checked("the compute pipeline", () =>
        {
            var entries = stackalloc BindGroupLayoutEntry[layout.Length];
            for (var i = 0; i < layout.Length; i++)
            {
                entries[i] = new BindGroupLayoutEntry
                {
                    Binding = (uint)i,
                    Visibility = ShaderStage.Compute,
                    Buffer = new BufferBindingLayout
                    {
                        Type = layout[i] == KernelBuffer.ReadOnlyStorage ? BufferBindingType.ReadOnlyStorage : BufferBindingType.Storage,
                    },
                };
            }

            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = (nuint)layout.Length, Entries = entries };
            bindGroupLayout = Api.DeviceCreateBindGroupLayout(device.Handle, &layoutDescriptor);
            GpuDevice.Created(bindGroupLayout);

            var groupLayout = bindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &groupLayout };
            pipelineLayout = Api.DeviceCreatePipelineLayout(device.Handle, &pipelineLayoutDescriptor);
            GpuDevice.Created(pipelineLayout);

            fixed (byte* name = entryName)
            {
                var pipelineDescriptor = new ComputePipelineDescriptor
                {
                    Layout = pipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = module, EntryPoint = name },
                };
                pipeline = Api.DeviceCreateComputePipeline(device.Handle, &pipelineDescriptor);
                GpuDevice.Created(pipeline);
            }
        });
    }

    // Make the buffer of a binding hold at least the bytes. A new buffer replaces a small one, and the
    // bind group is created again at the next run.
    private void EnsureCapacity(int binding, long bytes)
    {
        var needed = Align(bytes);
        if (buffers[binding] != 0 && capacities[binding] >= needed) return;
        var buffer = device.CreateBuffer(needed, BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc);
        GpuDevice.ReleaseBuffer((WgpuBuffer*)buffers[binding]);
        buffers[binding] = (nint)buffer;
        capacities[binding] = needed;
        ReleaseBindGroup();
    }

    private void EnsureBindGroup()
    {
        if (bindGroup != null) return;
        try
        {
            device.Checked("the bind group", () =>
            {
                var entries = stackalloc BindGroupEntry[layout.Length];
                for (var i = 0; i < layout.Length; i++)
                {
                    entries[i] = new BindGroupEntry
                    {
                        Binding = (uint)i,
                        Buffer = (WgpuBuffer*)buffers[i],
                        Offset = 0,
                        Size = (ulong)capacities[i],
                    };
                }

                var descriptor = new BindGroupDescriptor { Layout = bindGroupLayout, EntryCount = (nuint)layout.Length, Entries = entries };
                bindGroup = Api.DeviceCreateBindGroup(device.Handle, &descriptor);
                GpuDevice.Created(bindGroup);
            });
        }
        catch
        {
            ReleaseBindGroup();
            throw;
        }
    }

    // Encode one compute pass and the copy to the readback buffer, and submit them. Each object that
    // this method creates is released before it returns.
    private void Submit(uint workgroups, int readBinding, long bytes)
    {
        var encoderDescriptor = new CommandEncoderDescriptor();
        var encoder = Api.DeviceCreateCommandEncoder(device.Handle, &encoderDescriptor);
        var passDescriptor = new ComputePassDescriptor();
        var pass = Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
        Api.ComputePassEncoderSetPipeline(pass, pipeline);
        Api.ComputePassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
        Api.ComputePassEncoderDispatchWorkgroups(pass, workgroups, 1, 1);
        Api.ComputePassEncoderEnd(pass);
        Api.ComputePassEncoderRelease(pass);
        if (bytes > 0) Api.CommandEncoderCopyBufferToBuffer(encoder, (WgpuBuffer*)buffers[readBinding], 0, readback, 0, (ulong)bytes);
        var commandDescriptor = new CommandBufferDescriptor();
        var commands = Api.CommandEncoderFinish(encoder, &commandDescriptor);
        Api.CommandEncoderRelease(encoder);
        Api.QueueSubmit(device.Queue, 1, &commands);
        Api.CommandBufferRelease(commands);
    }

    private void ReleaseBindGroup()
    {
        if (bindGroup == null) return;
        Api.BindGroupRelease(bindGroup);
        bindGroup = null;
        GpuDevice.Released();
    }

    private void ReleaseReadback()
    {
        GpuDevice.ReleaseBuffer(readback);
        readback = null;
        readbackCapacity = 0;
    }

    private void ReleaseNative()
    {
        ReleaseBindGroup();
        ReleaseReadback();
        for (var binding = 0; binding < buffers.Length; binding++)
        {
            GpuDevice.ReleaseBuffer((WgpuBuffer*)buffers[binding]);
            buffers[binding] = 0;
            capacities[binding] = 0;
        }

        if (pipeline != null)
        {
            Api.ComputePipelineRelease(pipeline);
            pipeline = null;
            GpuDevice.Released();
        }

        if (pipelineLayout != null)
        {
            Api.PipelineLayoutRelease(pipelineLayout);
            pipelineLayout = null;
            GpuDevice.Released();
        }

        if (bindGroupLayout != null)
        {
            Api.BindGroupLayoutRelease(bindGroupLayout);
            bindGroupLayout = null;
            GpuDevice.Released();
        }

        if (module != null)
        {
            Api.ShaderModuleRelease(module);
            module = null;
            GpuDevice.Released();
        }
    }
}
