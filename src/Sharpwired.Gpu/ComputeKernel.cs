// Handwired source: src/json-search/gpu/host.ts (createProgramRunner and its evaluate function). One
// compute shader with an explicit bind group layout, one storage buffer for each binding, the dispatch,
// and the readback through a map-read buffer. Kernels can share a storage buffer, and more than one
// dispatch can run in one command buffer (the step kernel of GpuRecurrentRunner). This file and
// GpuDevice.cs are the only files that call the native API.

using System.Runtime.InteropServices;
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

/// <summary>One dispatch of a kernel: the workgroups in x.</summary>
internal readonly record struct KernelDispatch(ComputeKernel Kernel, uint Workgroups);

/// <summary>A copy of the first bytes of a storage buffer to the readback buffer.</summary>
internal readonly record struct BufferRead(StorageBuffer Buffer, long Bytes);

/// <summary>
/// One storage buffer of a device. A kernel makes one for each binding, or binds a buffer that other
/// kernels also bind. <see cref="Size"/> is the count of bytes that the last write or reserve declared.
/// The native buffer grows when a write or a reserve needs more bytes, and keeps its data.
/// </summary>
internal sealed unsafe class StorageBuffer : IDisposable
{
    // A buffer holds at least 16 bytes, so that an unset binding of a 12-byte struct is valid.
    private const long MinimumBytes = 16;

    private WgpuBuffer* handle;
    private bool disposed;

    public StorageBuffer(GpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Device = device;
    }

    public GpuDevice Device { get; }

    /// <summary>The bytes that the last <see cref="Write{T}"/> or <see cref="Reserve"/> declared.</summary>
    public long Size { get; private set; }

    /// <summary>The bytes of the native buffer.</summary>
    public long Capacity { get; private set; }

    /// <summary>
    /// The count of native buffers that this object created. wgpu-native can give the address of a
    /// released buffer to a new buffer, so a kernel compares this number and not the address.
    /// </summary>
    public long Generation { get; private set; }

    private static WebGPU Api => GpuDevice.Api;

    /// <summary>
    /// Make the buffer hold at least <paramref name="bytes"/> bytes, and set <see cref="Size"/>. The data
    /// of the buffer stays: a larger native buffer gets a copy of the old one.
    /// </summary>
    public void Reserve(long bytes)
    {
        lock (Device.Sync)
        {
            ThrowIfUnusable();
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            Grow(bytes, keep: true);
            Size = bytes;
        }
    }

    /// <summary>
    /// Write the data to the start of the buffer, and set <see cref="Size"/> to its length. The length of
    /// the data in bytes must be a multiple of 4.
    /// </summary>
    public void Write<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        lock (Device.Sync)
        {
            ThrowIfUnusable();
            var bytes = (long)data.Length * sizeof(T);
            if (bytes % 4 != 0) throw new ArgumentException($"the data holds {bytes} bytes, which is not a multiple of 4", nameof(data));
            Grow(bytes, keep: false);
            Size = bytes;
            if (bytes == 0) return;
            fixed (T* source = data)
            {
                Api.QueueWriteBuffer(Device.Queue, handle, 0, source, (nuint)bytes);
            }
        }
    }

    /// <summary>Release the native buffer. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (Device.Sync)
        {
            if (disposed) return;
            disposed = true;
            GpuDevice.ReleaseBuffer(handle);
            handle = null;
            Capacity = 0;
        }
    }

    /// <summary>The native buffer. The first use creates a buffer of the minimum size.</summary>
    internal WgpuBuffer* Handle()
    {
        ThrowIfUnusable();
        Grow(0, keep: false);
        return handle;
    }

    internal void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Device.ThrowIfUnusable();
    }

    private static long Align(long bytes) => Math.Max(MinimumBytes, (bytes + 3) / 4 * 4);

    // A new native buffer replaces a small one. With keep, the GPU copies the old buffer into the new
    // one, in queue order after each earlier write.
    private void Grow(long bytes, bool keep)
    {
        var needed = Align(bytes);
        if (handle != null && Capacity >= needed) return;
        var buffer = Device.CreateBuffer(needed, BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc);
        if (keep && handle != null)
        {
            try
            {
                var old = handle;
                var copied = Capacity;
                Device.Checked("the copy of a growing buffer", () =>
                {
                    var encoderDescriptor = new CommandEncoderDescriptor();
                    var encoder = Api.DeviceCreateCommandEncoder(Device.Handle, &encoderDescriptor);
                    Api.CommandEncoderCopyBufferToBuffer(encoder, old, 0, buffer, 0, (ulong)copied);
                    var commandDescriptor = new CommandBufferDescriptor();
                    var commands = Api.CommandEncoderFinish(encoder, &commandDescriptor);
                    Api.CommandEncoderRelease(encoder);
                    Api.QueueSubmit(Device.Queue, 1, &commands);
                    Api.CommandBufferRelease(commands);
                });
            }
            catch
            {
                GpuDevice.ReleaseBuffer(buffer);
                throw;
            }
        }
        GpuDevice.ReleaseBuffer(handle);
        handle = buffer;
        Capacity = needed;
        Generation++;
    }
}

/// <summary>A map-read buffer that receives the copies of a submit.</summary>
internal sealed unsafe class ReadbackBuffer : IDisposable
{
    private readonly GpuDevice device;
    private WgpuBuffer* handle;
    private long capacity;
    private bool disposed;

    public ReadbackBuffer(GpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        this.device = device;
    }

    /// <summary>Release the native buffer. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            GpuDevice.ReleaseBuffer(handle);
            handle = null;
            capacity = 0;
        }
    }

    // The native buffer with at least the bytes.
    internal WgpuBuffer* Reserve(long bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var needed = Math.Max(16, (bytes + 3) / 4 * 4);
        if (handle == null || capacity < needed)
        {
            GpuDevice.ReleaseBuffer(handle);
            handle = null;
            capacity = 0;
            handle = device.CreateBuffer(needed, BufferUsage.MapRead | BufferUsage.CopyDst);
            capacity = needed;
        }
        return handle;
    }
}

/// <summary>
/// One compute shader with its buffers. The bind group layout comes from the list of buffer kinds, and
/// not from the shader. So a binding that the shader does not use causes no error (the automatic layout
/// of wgpu drops such a binding). Each call takes the lock of the device.
/// </summary>
internal sealed unsafe class ComputeKernel : IDisposable
{
    private readonly GpuDevice device;
    private readonly KernelBuffer[] layout;
    private readonly StorageBuffer[] owned;
    private readonly StorageBuffer[] bound;

    // The buffers of the bind group and their generations, so that a grown buffer gives a new bind
    // group. The native address is not enough, because wgpu-native gives a released address again.
    private readonly StorageBuffer?[] groupBuffers;
    private readonly long[] groupGenerations;
    private readonly ReadbackBuffer readback;
    private ShaderModule* module;
    private BindGroupLayout* bindGroupLayout;
    private PipelineLayout* pipelineLayout;
    private ComputePipeline* pipeline;
    private BindGroup* bindGroup;
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
        owned = new StorageBuffer[layout.Length];
        for (var binding = 0; binding < owned.Length; binding++) owned[binding] = new StorageBuffer(device);
        bound = [.. owned];
        groupBuffers = new StorageBuffer?[layout.Length];
        groupGenerations = new long[layout.Length];
        readback = new ReadbackBuffer(device);
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

    /// <summary>
    /// Bind a storage buffer of the same device to a binding, in place of the buffer of the kernel. The
    /// kernel does not dispose a bound buffer.
    /// </summary>
    public void Bind(int binding, StorageBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (device.Sync)
        {
            ThrowIfUnusable();
            CheckBinding(binding);
            if (!ReferenceEquals(buffer.Device, device)) throw new ArgumentException("the buffer belongs to another device", nameof(buffer));
            bound[binding] = buffer;
        }
    }

    /// <summary>Make the buffer of a binding hold at least <paramref name="bytes"/> bytes. Its data stays.</summary>
    public void Reserve(int binding, long bytes)
    {
        lock (device.Sync)
        {
            ThrowIfUnusable();
            CheckBinding(binding);
            bound[binding].Reserve(bytes);
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
            bound[binding].Write(data);
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
            var size = bound[readBinding].Size;
            if (bytes > size) throw new ArgumentException($"the destination holds {bytes} bytes, and binding {readBinding} holds {size}", nameof(destination));
            Submit(device, [new KernelDispatch(this, workgroups)], [new BufferRead(bound[readBinding], bytes)], readback, MemoryMarshal.AsBytes(destination));
        }
    }

    /// <summary>
    /// Encode one compute pass for each dispatch, in order. Then encode the copies of
    /// <paramref name="reads"/> one after the other into the readback buffer. Submit all of them as one
    /// command buffer. A dispatch sees the writes of each earlier dispatch. Wait until the GPU is done,
    /// and copy the readback bytes into <paramref name="destination"/>, which holds the sum of the bytes
    /// of the reads.
    /// </summary>
    public static void Submit(GpuDevice device, ReadOnlySpan<KernelDispatch> dispatches, ReadOnlySpan<BufferRead> reads, ReadbackBuffer readback, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(readback);
        lock (device.Sync)
        {
            device.ThrowIfUnusable();
            long total = 0;
            foreach (var read in reads)
            {
                if (!ReferenceEquals(read.Buffer.Device, device)) throw new ArgumentException("a read buffer belongs to another device", nameof(reads));
                if (read.Bytes < 0 || read.Bytes % 4 != 0 || read.Bytes > read.Buffer.Size)
                {
                    throw new ArgumentException($"a read of {read.Bytes} bytes from a buffer of {read.Buffer.Size} bytes", nameof(reads));
                }
                total += read.Bytes;
            }
            if (total != destination.Length) throw new ArgumentException($"the destination holds {destination.Length} bytes, and the reads hold {total}", nameof(destination));

            var passes = dispatches.ToArray();
            foreach (var pass in passes)
            {
                if (!ReferenceEquals(pass.Kernel.device, device)) throw new ArgumentException("a kernel belongs to another device", nameof(dispatches));
                pass.Kernel.ThrowIfUnusable();
                pass.Kernel.EnsureBindGroup();
            }
            var sources = new nint[reads.Length];
            var sizes = new long[reads.Length];
            for (var index = 0; index < reads.Length; index++)
            {
                sources[index] = (nint)reads[index].Buffer.Handle();
                sizes[index] = reads[index].Bytes;
            }
            var target = total > 0 ? readback.Reserve(total) : null;
            device.Checked("the dispatch", () => Encode(device, passes, sources, sizes, target));
            if (total == 0) return;
            device.MapRead(target, total);
            try
            {
                var mapped = Api.BufferGetConstMappedRange(target, 0, (nuint)total);
                if (mapped == null) throw new InvalidOperationException($"{device.Adapter.Name}: the readback map gave no memory");
                new ReadOnlySpan<byte>(mapped, (int)total).CopyTo(destination);
            }
            finally
            {
                Api.BufferUnmap(target);
            }
        }
    }

    /// <summary>Release the native objects of the kernel and of its own buffers. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            ReleaseNative();
        }
    }

    // Encode the passes and the copies, and submit them. Each object that this method creates is
    // released before it returns.
    private static void Encode(GpuDevice device, KernelDispatch[] passes, nint[] sources, long[] sizes, WgpuBuffer* target)
    {
        var encoderDescriptor = new CommandEncoderDescriptor();
        var encoder = Api.DeviceCreateCommandEncoder(device.Handle, &encoderDescriptor);
        foreach (var dispatch in passes)
        {
            var passDescriptor = new ComputePassDescriptor();
            var pass = Api.CommandEncoderBeginComputePass(encoder, &passDescriptor);
            Api.ComputePassEncoderSetPipeline(pass, dispatch.Kernel.pipeline);
            Api.ComputePassEncoderSetBindGroup(pass, 0, dispatch.Kernel.bindGroup, 0, null);
            Api.ComputePassEncoderDispatchWorkgroups(pass, dispatch.Workgroups, 1, 1);
            Api.ComputePassEncoderEnd(pass);
            Api.ComputePassEncoderRelease(pass);
        }
        long offset = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            if (sizes[index] > 0) Api.CommandEncoderCopyBufferToBuffer(encoder, (WgpuBuffer*)sources[index], 0, target, (ulong)offset, (ulong)sizes[index]);
            offset += sizes[index];
        }
        var commandDescriptor = new CommandBufferDescriptor();
        var commands = Api.CommandEncoderFinish(encoder, &commandDescriptor);
        Api.CommandEncoderRelease(encoder);
        Api.QueueSubmit(device.Queue, 1, &commands);
        Api.CommandBufferRelease(commands);
    }

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

    // Create the bind group when it is absent. Also create it when a binding holds another buffer, or
    // another generation of a buffer, than the bind group holds. This occurs when a bound buffer
    // changed or a buffer grew.
    private void EnsureBindGroup()
    {
        var handles = new nint[layout.Length];
        var capacities = new long[layout.Length];
        var generations = new long[layout.Length];
        var same = bindGroup != null;
        for (var i = 0; i < layout.Length; i++)
        {
            handles[i] = (nint)bound[i].Handle();
            capacities[i] = bound[i].Capacity;
            generations[i] = bound[i].Generation;
            same = same && ReferenceEquals(bound[i], groupBuffers[i]) && generations[i] == groupGenerations[i];
        }
        if (same) return;
        ReleaseBindGroup();
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
                        Buffer = (WgpuBuffer*)handles[i],
                        Offset = 0,
                        Size = (ulong)capacities[i],
                    };
                }

                var descriptor = new BindGroupDescriptor { Layout = bindGroupLayout, EntryCount = (nuint)layout.Length, Entries = entries };
                bindGroup = Api.DeviceCreateBindGroup(device.Handle, &descriptor);
                GpuDevice.Created(bindGroup);
            });
            bound.CopyTo(groupBuffers, 0);
            generations.CopyTo(groupGenerations, 0);
        }
        catch
        {
            ReleaseBindGroup();
            throw;
        }
    }

    private void ReleaseBindGroup()
    {
        Array.Clear(groupBuffers);
        Array.Clear(groupGenerations);
        if (bindGroup == null) return;
        Api.BindGroupRelease(bindGroup);
        bindGroup = null;
        GpuDevice.Released();
    }

    private void ReleaseNative()
    {
        ReleaseBindGroup();
        readback.Dispose();
        foreach (var buffer in owned) buffer.Dispose();

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
