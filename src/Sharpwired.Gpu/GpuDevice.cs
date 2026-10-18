// Handwired source: src/json-search/gpu/host.ts (requestBatchDevice, and checked for the error scopes).
// The adapters, and the open of one device with the adapter maximum of the buffer limits. The uncaptured
// error callback, the error scopes, the device-lost flag and the readback map. The wgpu-native log lines
// and the subnormal probe.
// This file and ComputeKernel.cs are the only files that call the native API.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Sharpwired.Gpu;

/// <summary>The native graphics API under a WebGPU adapter.</summary>
public enum GpuBackend { Metal, Vulkan, D3D12, OpenGL, OpenGLES, Other }

/// <summary>The kind of GPU of a WebGPU adapter.</summary>
public enum GpuAdapterType { DiscreteGpu, IntegratedGpu, Cpu, Other }

/// <summary>One WebGPU adapter. <see cref="Index"/> is its position in <see cref="GpuDevice.ListAdapters"/>.</summary>
public sealed record GpuAdapterInfo(int Index, string Name, GpuBackend Backend, GpuAdapterType Type);

/// <summary>
/// One open WebGPU device, with its queue and its limits. All calls that use the device take one lock,
/// so the GPU calls of one device run one at a time. Dispose the kernels and the evaluators of a device
/// before the device.
/// </summary>
public sealed unsafe class GpuDevice : IDisposable
{
    // The WGSL of the subnormal probe: k(x * k(2^-10)) with x = 2^-120 from a storage buffer. The exact
    // result 2^-130 is subnormal.
    private const string SubnormalProbe = WgslText.Prelude + """
        @group(0) @binding(0) var<storage, read> src: array<f32>;
        @group(0) @binding(1) var<storage, read_write> dst: array<f32>;
        @group(0) @binding(2) var<storage, read> params: Params;

        @compute @workgroup_size(1)
        fn main() { dst[0] = k(src[0] * k(0x1p-10f)); }
        """;

    /// <summary>A callback that did not run after this count of polls is a fault of the native library.</summary>
    internal const int DefaultPollLimit = 10_000;

    private static readonly Lazy<WebGPU> LazyApi = new(WebGPU.GetApi);
    private static readonly Lazy<Wgpu> LazyNative = new(() => new Wgpu(LazyApi.Value.Context));

    // The callbacks get their target through userdata, a key of this table. A callback with a key that
    // is not in the table does nothing. So a late callback never reaches a released object.
    private static readonly ConcurrentDictionary<nint, object> Targets = new();
    private static long lastTarget;

    // The delegates and their function pointers stay in static fields, so the garbage collector never
    // collects a delegate that native code can call.
    private static readonly RequestAdapterCallback AdapterCallback = OnAdapter;
    private static readonly RequestDeviceCallback DeviceCallback = OnDevice;
    private static readonly ErrorCallback UncapturedCallback = OnUncaptured;
    private static readonly ErrorCallback ScopeCallback = OnScope;
    private static readonly DeviceLostCallback LostCallback = OnLost;
    private static readonly BufferMapCallback MapCallback = OnMap;
    private static readonly PfnRequestAdapterCallback AdapterPfn = new(AdapterCallback);
    private static readonly PfnRequestDeviceCallback DevicePfn = new(DeviceCallback);
    private static readonly PfnErrorCallback UncapturedPfn = new(UncapturedCallback);
    private static readonly PfnErrorCallback ScopePfn = new(ScopeCallback);
    private static readonly PfnDeviceLostCallback LostPfn = new(LostCallback);
    private static readonly PfnBufferMapCallback MapPfn = new(MapCallback);
    private static readonly LogCallback LogFunction = OnLog;
    private static readonly PfnLogCallback LogPfn = new(LogFunction);

    // The log callback of wgpu-native is global. It is installed one time, before the first instance.
    private static readonly Lazy<bool> LogInstalled = new(() =>
    {
        Native.SetLogCallback(LogPfn, null);
        Native.SetLogLevel(LogLevel.Warn);
        return true;
    });

    // The last log lines, each with its sequence number. An error scope that holds an error adds the
    // lines that came after its push to its message.
    private const int LogCapacity = 64;
    private static readonly ConcurrentQueue<(long Sequence, string Line)> LogLines = new();
    private static long logSequence;

    private static int liveObjects;

    private readonly nint self;
    private readonly ConcurrentQueue<string> uncaptured = new();
    private Instance* instance;
    private Adapter* adapter;
    private Device* device;
    private Queue* queue;
    private volatile string? lostMessage;

    // The cause of a native fault that leaves the device in an unknown state: a map request or an error
    // scope that did not complete. Each later call throws with it.
    private volatile string? faultMessage;
    private bool disposed;

    // Takes the ownership of the instance and the adapter. On a failure, releases both and throws.
    private GpuDevice(Instance* instance, Adapter* adapter, GpuAdapterInfo info)
    {
        this.instance = instance;
        this.adapter = adapter;
        Adapter = info;
        self = Register(this);
        try
        {
            SupportedLimits supported = default;
            Api.AdapterGetLimits(adapter, &supported);
            // host.ts asks for the adapter maximum of the two buffer limits. The other limits are the
            // adapter values too, which a device request always accepts.
            var required = new RequiredLimits { Limits = supported.Limits };
            required.Limits.MaxStorageBufferBindingSize =
                Math.Min(supported.Limits.MaxStorageBufferBindingSize, supported.Limits.MaxBufferSize);
            var descriptor = new DeviceDescriptor
            {
                RequiredLimits = &required,
                DeviceLostCallback = LostPfn,
                DeviceLostUserdata = (void*)self,
            };
            var reply = new Reply();
            var key = Register(reply);
            try
            {
                Api.AdapterRequestDevice(adapter, &descriptor, DevicePfn, (void*)key);
                for (var i = 0; !reply.Done && i < DefaultPollLimit; i++) Api.InstanceProcessEvents(instance);
            }
            finally
            {
                Unregister(key);
            }

            if (!reply.Done) throw new InvalidOperationException($"{info.Name}: the device request did not complete");
            if (reply.Status != (int)RequestDeviceStatus.Success || reply.Handle == 0)
                throw new InvalidOperationException($"{info.Name}: the device request failed: {reply.Message}");
            device = (Device*)reply.Handle;
            Created(device);

            // The default handler of wgpu-native aborts the process on a validation error. So this is
            // the first call on the device.
            Api.DeviceSetUncapturedErrorCallback(device, UncapturedPfn, (void*)self);
            queue = Api.DeviceGetQueue(device);
            Created(queue);

            SupportedLimits granted = default;
            Api.DeviceGetLimits(device, &granted);
            MaxStorageBufferBindingSize = (long)granted.Limits.MaxStorageBufferBindingSize;
            MaxBufferSize = (long)granted.Limits.MaxBufferSize;
            MaxWorkgroupsPerDimension = (int)Math.Min(granted.Limits.MaxComputeWorkgroupsPerDimension, int.MaxValue);
            FlushesSubnormals = MeasureSubnormalFlush();
        }
        catch
        {
            ReleaseNative();
            throw;
        }
    }

    /// <summary>The adapter of this device.</summary>
    public GpuAdapterInfo Adapter { get; }

    /// <summary>The largest storage buffer binding of this device, in bytes.</summary>
    public long MaxStorageBufferBindingSize { get; }

    /// <summary>The largest workgroup count of one dispatch dimension.</summary>
    public int MaxWorkgroupsPerDimension { get; }

    /// <summary>
    /// True when the GPU writes 0 in place of an FP32 result whose magnitude is below 2^-126. No WGSL
    /// form stops it. On such a device, the f32 writer is not exact for an evaluation with a subnormal
    /// value. <see cref="Open"/> measures it with one kernel.
    /// </summary>
    public bool FlushesSubnormals { get; }

    /// <summary>The largest buffer of this device, in bytes.</summary>
    internal long MaxBufferSize { get; }

    /// <summary>The lock of all GPU calls on this device.</summary>
    internal Lock Sync { get; } = new();

    internal Device* Handle => device;

    internal Queue* Queue => queue;

    internal static WebGPU Api => LazyApi.Value;

    internal static Wgpu Native => LazyNative.Value;

    /// <summary>
    /// The count of native objects that this backend created and did not release. A test compares the
    /// count before and after its work.
    /// </summary>
    internal static int LiveObjects => Volatile.Read(ref liveObjects);

    /// <summary>
    /// An action that runs after each successful map, before the error check of <see cref="MapRead"/>.
    /// A test sets it to put an error in the queue between the map and the check.
    /// </summary>
    internal Action? MapCompleted { get; set; }

    /// <summary>Put an error in the queue of the uncaptured errors, as the uncaptured error callback does.</summary>
    internal void ReportUncaptured(string message) => uncaptured.Enqueue(message);

    /// <summary>Set the device-lost flag, as the device-lost callback does.</summary>
    internal void ReportLost(string message) => lostMessage = message;

    /// <summary>The count of polls of this device that wait for one callback. A test lowers it.</summary>
    internal int PollLimit { get; set; } = DefaultPollLimit;

    /// <summary>An action that runs before each pop of an error scope. A test sets it.</summary>
    internal Action? BeforeScopePop { get; set; }

    /// <summary>Add a line to the wgpu-native log, as the log callback does.</summary>
    internal static void ReportLog(string level, string message)
    {
        LogLines.Enqueue((Interlocked.Increment(ref logSequence), $"{level}: {message}"));
        while (LogLines.Count > LogCapacity) LogLines.TryDequeue(out _);
    }

    /// <summary>List the WebGPU adapters of this machine.</summary>
    public static IReadOnlyList<GpuAdapterInfo> ListAdapters()
    {
        var instance = CreateInstance();
        try
        {
            var adapters = Enumerate(instance);
            var result = new GpuAdapterInfo[adapters.Length];
            for (var i = 0; i < adapters.Length; i++) result[i] = Describe((Adapter*)adapters[i], i);
            foreach (var handle in adapters) ReleaseAdapter((Adapter*)handle);
            return result;
        }
        finally
        {
            ReleaseInstance(instance);
        }
    }

    /// <summary>
    /// Open the adapter, or the high-performance adapter when <paramref name="adapter"/> is null.
    /// Throws <see cref="InvalidOperationException"/> when no adapter opens.
    /// </summary>
    public static GpuDevice Open(GpuAdapterInfo? adapter = null)
    {
        var instance = CreateInstance();
        Adapter* chosen = null;
        GpuAdapterInfo? info = null;
        try
        {
            var adapters = Enumerate(instance);
            try
            {
                var index = adapter is null ? HighPerformanceIndex(instance, adapters) : adapter.Index;
                if (index < 0 || index >= adapters.Length)
                    throw new InvalidOperationException(adapter is null
                        ? "no WebGPU adapter was found"
                        : $"the adapter {adapter.Index} ({adapter.Name}) is not in the list of {adapters.Length} adapters");
                info = Describe((Adapter*)adapters[index], index);
                if (adapter is not null && info != adapter)
                    throw new InvalidOperationException($"the adapter list changed: position {index} holds {info.Name} ({info.Backend}), not {adapter.Name} ({adapter.Backend})");
                chosen = (Adapter*)adapters[index];
            }
            finally
            {
                foreach (var handle in adapters)
                {
                    if ((Adapter*)handle != chosen) ReleaseAdapter((Adapter*)handle);
                }
            }
        }
        catch
        {
            if (chosen != null) ReleaseAdapter(chosen);
            ReleaseInstance(instance);
            throw;
        }

        return new GpuDevice(instance, chosen, info);
    }

    /// <summary>Release the queue, the device, the adapter and the instance. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (Sync)
        {
            if (disposed) return;
            disposed = true;
            ReleaseNative();
        }
    }

    /// <summary>
    /// Throw <see cref="ObjectDisposedException"/> after <see cref="Dispose"/>. Throw
    /// <see cref="InvalidOperationException"/> when the device is lost, after a native fault, or when an
    /// error occurred outside an error scope since the last call.
    /// </summary>
    internal void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lostMessage is { } lost) throw new InvalidOperationException($"{Adapter.Name}: the WebGPU device is lost: {lost}");
        if (faultMessage is { } fault) throw new InvalidOperationException($"{Adapter.Name}: the device is unusable: {fault}");
        if (uncaptured.TryDequeue(out var error))
        {
            while (uncaptured.TryDequeue(out var more)) error += "; " + more;
            throw new InvalidOperationException($"{Adapter.Name}: a WebGPU error occurred outside an error scope: {error}");
        }
    }

    /// <summary>
    /// Run <paramref name="body"/> inside a validation scope and an out-of-memory scope. Throw
    /// <see cref="InvalidOperationException"/> with the wgpu message when a scope holds an error. The
    /// message also holds the wgpu-native log lines of the scope. The caller releases the objects that
    /// the body created, also on an error. When the body throws, this method throws the same exception.
    /// </summary>
    internal void Checked(string action, Action body)
    {
        lock (Sync)
        {
            ThrowIfUnusable();
            var logMark = Interlocked.Read(ref logSequence);
            Api.DevicePushErrorScope(device, ErrorFilter.Validation);
            Api.DevicePushErrorScope(device, ErrorFilter.OutOfMemory);
            try
            {
                body();
            }
            catch
            {
                // A pop that fails marks the device unusable. The exception of the body stays the
                // exception of this call.
                try
                {
                    PopScope();
                    PopScope();
                }
                catch (InvalidOperationException)
                {
                }

                throw;
            }

            var outOfMemory = PopScope();
            var validation = PopScope();
            if ((validation ?? outOfMemory) is { } error)
            {
                throw new InvalidOperationException($"{Adapter.Name}: {action} failed: {error}{LogSince(logMark)}");
            }
        }
    }

    /// <summary>Create a buffer inside the error scopes.</summary>
    internal WgpuBuffer* CreateBuffer(long bytes, BufferUsage usage)
    {
        WgpuBuffer* buffer = null;
        try
        {
            Checked($"a buffer of {bytes} bytes", () =>
            {
                var descriptor = new BufferDescriptor { Usage = usage, Size = (ulong)bytes };
                buffer = Api.DeviceCreateBuffer(device, &descriptor);
                Created(buffer);
            });
        }
        catch
        {
            ReleaseBuffer(buffer);
            throw;
        }

        return buffer;
    }

    /// <summary>
    /// Map the first bytes of a map-read buffer, and wait until the map is done. When this method
    /// returns, the buffer is mapped, and the caller unmaps it. When it throws, the buffer is not mapped.
    /// </summary>
    internal void MapRead(WgpuBuffer* buffer, long bytes)
    {
        lock (Sync)
        {
            var reply = new Reply();
            var key = Register(reply);
            try
            {
                Api.BufferMapAsync(buffer, MapMode.Read, 0, (nuint)bytes, MapPfn, (void*)key);
                for (var i = 0; !reply.Done && i < PollLimit; i++) Native.DevicePoll(device, true, null);
            }
            finally
            {
                Unregister(key);
            }

            // The map request stays pending in the native library, and a submit that uses a pending
            // buffer aborts the process. So the device refuses each later call.
            if (!reply.Done) throw Fault("the readback map did not complete");
            if (reply.Status != (int)BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"{Adapter.Name}: the readback map failed: {(BufferMapAsyncStatus)reply.Status}");
            // The map succeeded, so a failure of the check must unmap the buffer. Else the next copy into
            // it is a validation error (review of task 1).
            try
            {
                MapCompleted?.Invoke();
                ThrowIfUnusable();
            }
            catch
            {
                Api.BufferUnmap(buffer);
                throw;
            }
        }
    }

    internal static void ReleaseBuffer(WgpuBuffer* buffer)
    {
        if (buffer == null) return;
        Api.BufferRelease(buffer);
        Released();
    }

    /// <summary>Count one native object. A null handle is not counted.</summary>
    internal static void Created(void* handle)
    {
        if (handle != null) Interlocked.Increment(ref liveObjects);
    }

    /// <summary>Count the release of one native object.</summary>
    internal static void Released() => Interlocked.Decrement(ref liveObjects);

    private static nint Register(object target)
    {
        var key = (nint)Interlocked.Increment(ref lastTarget);
        Targets[key] = target;
        return key;
    }

    private static void Unregister(nint key) => Targets.TryRemove(key, out _);

    private static T? Target<T>(void* userdata) where T : class =>
        Targets.TryGetValue((nint)userdata, out var target) ? target as T : null;

    private static string Text(byte* message) => message == null ? "" : Marshal.PtrToStringUTF8((nint)message) ?? "";

    private static void OnAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
    {
        // A callback after the wait stopped has no target. Its adapter has no owner, so release it here.
        if (Target<Reply>(userdata) is not { } reply)
        {
            if (adapter != null) Api.AdapterRelease(adapter);
            return;
        }

        reply.Status = (int)status;
        reply.Handle = (nint)adapter;
        reply.Message = Text(message);
        reply.Done = true;
    }

    private static void OnDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
    {
        // A callback after the wait stopped has no target. Its device has no owner, so release it here.
        if (Target<Reply>(userdata) is not { } reply)
        {
            if (device != null) Api.DeviceRelease(device);
            return;
        }

        reply.Status = (int)status;
        reply.Handle = (nint)device;
        reply.Message = Text(message);
        reply.Done = true;
    }

    private static void OnScope(ErrorType type, byte* message, void* userdata)
    {
        if (Target<Reply>(userdata) is not { } reply) return;
        reply.Status = (int)type;
        reply.Message = Text(message);
        reply.Done = true;
    }

    private static void OnMap(BufferMapAsyncStatus status, void* userdata)
    {
        if (Target<Reply>(userdata) is not { } reply) return;
        reply.Status = (int)status;
        reply.Done = true;
    }

    private static void OnUncaptured(ErrorType type, byte* message, void* userdata)
    {
        if (Target<GpuDevice>(userdata) is { } owner) owner.uncaptured.Enqueue($"{type}: {Text(message)}");
    }

    private static void OnLog(LogLevel level, byte* message, void* userdata) => ReportLog(level.ToString(), Text(message));

    private static void OnLost(DeviceLostReason reason, byte* message, void* userdata)
    {
        // Dispose releases the device, and the release can report the reason Destroyed.
        if (Target<GpuDevice>(userdata) is { disposed: false } owner) owner.lostMessage = $"{reason}: {Text(message)}";
    }

    private static Instance* CreateInstance()
    {
        _ = LogInstalled.Value;
        InstanceDescriptor descriptor = default;
        var instance = Api.CreateInstance(&descriptor);
        if (instance == null) throw new InvalidOperationException("the WebGPU instance did not open");
        Created(instance);
        return instance;
    }

    private static void ReleaseInstance(Instance* instance)
    {
        Api.InstanceRelease(instance);
        Released();
    }

    private static void ReleaseAdapter(Adapter* adapter)
    {
        Api.AdapterRelease(adapter);
        Released();
    }

    // The adapters of the instance, in the order of wgpu-native. The caller releases each one.
    private static nint[] Enumerate(Instance* instance)
    {
        var count = (int)Native.InstanceEnumerateAdapters(instance, null, null);
        var adapters = new nint[count];
        if (count == 0) return adapters;
        fixed (nint* list = adapters)
        {
            count = (int)Native.InstanceEnumerateAdapters(instance, null, (Adapter**)list);
        }

        if (count < adapters.Length) Array.Resize(ref adapters, count);
        foreach (var handle in adapters) Created((void*)handle);
        return adapters;
    }

    private static AdapterProperties Properties(Adapter* adapter)
    {
        AdapterProperties properties = default;
        Api.AdapterGetProperties(adapter, &properties);
        return properties;
    }

    private static GpuAdapterInfo Describe(Adapter* adapter, int index)
    {
        var properties = Properties(adapter);
        var backend = properties.BackendType switch
        {
            BackendType.Metal => GpuBackend.Metal,
            BackendType.Vulkan => GpuBackend.Vulkan,
            BackendType.D3D12 => GpuBackend.D3D12,
            BackendType.OpenGL => GpuBackend.OpenGL,
            BackendType.OpenGles => GpuBackend.OpenGLES,
            _ => GpuBackend.Other,
        };
        var type = properties.AdapterType switch
        {
            AdapterType.DiscreteGpu => GpuAdapterType.DiscreteGpu,
            AdapterType.IntegratedGpu => GpuAdapterType.IntegratedGpu,
            AdapterType.Cpu => GpuAdapterType.Cpu,
            _ => GpuAdapterType.Other,
        };
        var name = Text(properties.Name);
        if (string.IsNullOrWhiteSpace(name)) name = $"{backend} adapter {index}";
        return new GpuAdapterInfo(index, name, backend, type);
    }

    // The position in the list of the adapter that wgpu-native gives for the power preference
    // HighPerformance, or -1 when it gives no adapter.
    private static int HighPerformanceIndex(Instance* instance, nint[] adapters)
    {
        var options = new RequestAdapterOptions { PowerPreference = PowerPreference.HighPerformance };
        var reply = new Reply();
        var key = Register(reply);
        try
        {
            Api.InstanceRequestAdapter(instance, &options, AdapterPfn, (void*)key);
            for (var i = 0; !reply.Done && i < DefaultPollLimit; i++) Api.InstanceProcessEvents(instance);
        }
        finally
        {
            Unregister(key);
        }

        if (!reply.Done || reply.Status != (int)RequestAdapterStatus.Success || reply.Handle == 0) return -1;
        var requested = (Adapter*)reply.Handle;
        Created(requested);
        try
        {
            var wanted = Properties(requested);
            for (var i = 0; i < adapters.Length; i++)
            {
                var candidate = Properties((Adapter*)adapters[i]);
                if (candidate.VendorID == wanted.VendorID && candidate.DeviceID == wanted.DeviceID &&
                    candidate.BackendType == wanted.BackendType && candidate.AdapterType == wanted.AdapterType &&
                    Text(candidate.Name) == Text(wanted.Name))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"the high-performance adapter {Text(wanted.Name)} is not in the adapter list");
        }
        finally
        {
            ReleaseAdapter(requested);
        }
    }

    // The message of the error in the top scope, or null for no error. Pops the scope. A pop that does
    // not complete leaves the scope stack in an unknown state, so it marks the device unusable.
    private string? PopScope()
    {
        var reply = new Reply();
        var key = Register(reply);
        try
        {
            BeforeScopePop?.Invoke();
            Api.DevicePopErrorScope(device, ScopePfn, (void*)key);
            for (var i = 0; !reply.Done && i < PollLimit; i++) Native.DevicePoll(device, false, null);
        }
        catch (Exception error)
        {
            throw Fault($"an error scope did not pop: {error.Message}");
        }
        finally
        {
            Unregister(key);
        }

        if (!reply.Done) throw Fault("an error scope did not return");
        return reply.Status == (int)ErrorType.NoError ? null : $"WebGPU {(ErrorType)reply.Status} error: {reply.Message}";
    }

    // Mark the device unusable with the cause, and return the exception of this call.
    private InvalidOperationException Fault(string cause)
    {
        faultMessage ??= cause;
        return new InvalidOperationException($"{Adapter.Name}: {cause}");
    }

    // The log lines after the mark, as a suffix of an error message. Empty when no line came.
    private static string LogSince(long mark) =>
        string.Concat(LogLines.Where(entry => entry.Sequence > mark).Select(entry => $"; wgpu log: {entry.Line}"));

    private bool MeasureSubnormalFlush()
    {
        using var kernel = new ComputeKernel(this, SubnormalProbe,
            [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage]);
        kernel.Write<float>(0, [MathF.ScaleB(1f, -120)]);
        kernel.Write<uint>(2, [0u, 1u, 0u, 0u]);
        kernel.Reserve(1, sizeof(float));
        Span<float> result = stackalloc float[1];
        kernel.Run(1, 1, result);
        var bits = BitConverter.SingleToUInt32Bits(result[0]);
        if (bits == BitConverter.SingleToUInt32Bits(MathF.ScaleB(1f, -130))) return false;
        if (bits == 0) return true;
        throw new InvalidOperationException($"{Adapter.Name}: the subnormal probe returned {result[0]:R}, which is neither 2^-130 nor 0");
    }

    private void ReleaseNative()
    {
        if (queue != null)
        {
            Api.QueueRelease(queue);
            queue = null;
            Released();
        }

        if (device != null)
        {
            Api.DeviceRelease(device);
            device = null;
            Released();
        }

        if (adapter != null)
        {
            ReleaseAdapter(adapter);
            adapter = null;
        }

        if (instance != null)
        {
            ReleaseInstance(instance);
            instance = null;
        }

        Unregister(self);
    }

    // The result of one native callback.
    private sealed class Reply
    {
        public volatile bool Done;
        public int Status;
        public nint Handle;
        public string Message = "";
    }
}
