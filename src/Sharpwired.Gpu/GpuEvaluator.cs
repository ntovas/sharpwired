// Handwired source: src/json-search/gpu/host.ts (the evaluate function of createProgramRunner). The GPU
// batch evaluator: one shader of the program, the source tables in chunks that fit the buffer limits
// of the device, one dispatch and one readback for each chunk.

using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The GPU batch evaluator of one program. One shader invocation evaluates one source table. The
/// evaluator checks each source table on the CPU, as <see cref="TensorEvaluator"/> does, before it
/// sends the tables to the GPU.
/// </summary>
/// <remarks>
/// The evaluator uses the lock of its device for the full call, so two threads can call evaluators of
/// one device. Dispose the evaluators before their device. With <see cref="GpuWriter.Float32"/> the
/// values are the reference values bit for bit, except on a device that flushes subnormal values
/// (<see cref="GpuDevice.FlushesSubnormals"/>), for an evaluation that holds a subnormal value.
/// </remarks>
public sealed class GpuEvaluator : IBatchEvaluator
{
    // The bindings of the shader: sources, outputs, params (WgslFloatWriter).
    private const int SourceBinding = 0;
    private const int OutputBinding = 1;
    private const int ParamsBinding = 2;

    private static readonly KernelBuffer[] Layout = [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage];

    private readonly GpuDevice device;
    private readonly Circuit circuit;
    private readonly ComputeKernel kernel;
    private bool disposed;

    private GpuEvaluator(GpuDevice device, Circuit circuit, TensorProgram program, WgslModule module, int tablesPerChunk)
    {
        this.device = device;
        this.circuit = circuit;
        Program = program;
        Module = module;
        TablesPerChunk = tablesPerChunk;
        kernel = new ComputeKernel(device, module.Code, Layout);
    }

    /// <summary>The program that the evaluator runs.</summary>
    public TensorProgram Program { get; }

    /// <summary>The shader of the program.</summary>
    public WgslModule Module { get; }

    public int RootCount => Module.RootCount;

    /// <summary>The largest number of tables of one dispatch.</summary>
    internal int TablesPerChunk { get; }

    /// <summary>Write the shader of the program and create its pipeline on the device.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit.</exception>
    /// <exception cref="InvalidOperationException">The device is lost, or the shader or the pipeline
    /// failed (the message holds the wgpu message).</exception>
    public static GpuEvaluator Create(GpuDevice device, Circuit circuit, TensorProgram program, GpuWriter writer = GpuWriter.Float32) =>
        Create(device, circuit, program, writer, null);

    /// <summary>
    /// <see cref="Create(GpuDevice, Circuit, TensorProgram, GpuWriter)"/> with a lower chunk limit, so
    /// that a test can split a small batch into chunks.
    /// </summary>
    internal static GpuEvaluator Create(GpuDevice device, Circuit circuit, TensorProgram program, GpuWriter writer, int? maxTablesPerChunk)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        if (maxTablesPerChunk is { } limit) ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(maxTablesPerChunk));
        var module = writer switch
        {
            GpuWriter.Float32 => WgslFloatWriter.Write(circuit, program),
            _ => throw new ArgumentOutOfRangeException(nameof(writer), writer, "unknown writer"),
        };
        var tablesPerChunk = Math.Min(ChunkLimit(device, module), maxTablesPerChunk ?? int.MaxValue);
        return new GpuEvaluator(device, circuit, program, module, tablesPerChunk);
    }

    /// <summary>Evaluate the tables and return a new array of <c>tableCount * RootCount</c> outputs.</summary>
    public float[] Evaluate(ReadOnlySpan<float> sources, int tableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tableCount);
        var outputs = new float[checked(tableCount * RootCount)];
        Evaluate(sources, tableCount, outputs);
        return outputs;
    }

    public void Evaluate(ReadOnlySpan<float> sources, int tableCount, Span<float> outputs)
    {
        lock (device.Sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            device.ThrowIfUnusable();
            BatchArguments.Check(circuit, Program, sources, tableCount, outputs.Length);
            if (RootCount == 0) return;
            var sourceSize = Module.SourceSize;
            for (var first = 0; first < tableCount; first += TablesPerChunk)
            {
                var count = Math.Min(TablesPerChunk, tableCount - first);
                kernel.Write(SourceBinding, sources.Slice(first * sourceSize, count * sourceSize));
                // Params: first (the first table of the chunk in the batch, which the shader does not
                // read), count, zero (always 0), and one word of padding.
                kernel.Write<uint>(ParamsBinding, [(uint)first, (uint)count, 0u, 0u]);
                kernel.Reserve(OutputBinding, (long)count * RootCount * sizeof(float));
                var workgroups = (uint)((count + Module.WorkgroupSize - 1) / Module.WorkgroupSize);
                kernel.Run(workgroups, OutputBinding, outputs.Slice(first * RootCount, count * RootCount));
            }
        }
    }

    /// <summary>Release the shader, the pipeline and the buffers. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            kernel.Dispose();
        }
    }

    // The largest chunk: the source buffer and the output buffer must fit one binding and one buffer,
    // and the workgroups must fit one dispatch dimension.
    private static int ChunkLimit(GpuDevice device, WgslModule module)
    {
        var bytes = Math.Min(device.MaxStorageBufferBindingSize, device.MaxBufferSize);
        var bySources = bytes / (4L * Math.Max(1, module.SourceSize));
        var byOutputs = bytes / (4L * Math.Max(1, module.RootCount));
        var byWorkgroups = (long)module.WorkgroupSize * device.MaxWorkgroupsPerDimension;
        return (int)Math.Max(1, Math.Min(Math.Min(bySources, byOutputs), Math.Min(byWorkgroups, int.MaxValue)));
    }
}
