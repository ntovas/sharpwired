// Handwired source: src/json-search/gpu/host.ts (the evaluate function of createProgramRunner). The GPU
// batch evaluator. It has one shader of the program. It puts the source tables in chunks that fit the
// buffer limits of the device. It does one dispatch and one readback for each chunk. It is also the root
// evaluator of one table (IRootEvaluator) that ProgramEvaluator uses for a program on the GPU. Binding 3
// holds the constant tables of the shader. The source checks run on parallel threads before the device
// lock (phase 5 carried item, task 3 of the phase 6 plan).

using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The GPU batch evaluator of one program. One shader invocation evaluates one source table. The
/// evaluator checks each source table on the CPU, as <see cref="TensorEvaluator"/> does, before it
/// sends the tables to the GPU. The checks of a large batch run on parallel threads.
/// </summary>
/// <remarks>
/// <para>The evaluator checks the arguments and the source tables before it takes the lock of its
/// device. It holds the lock for the GPU work of the call, so two threads can call evaluators of one
/// device. Dispose the evaluators before their device.</para>
/// <para>With <see cref="GpuWriter.Float32"/> the values are the reference values bit for bit, with one
/// exception. On a device that flushes subnormal values (<see cref="GpuDevice.FlushesSubnormals"/>), an
/// evaluation that holds a subnormal value in an f32 row can differ. With <see cref="GpuWriter.Integer"/>,
/// the default, the same holds, and a zero can also have the other sign. The integer evaluator also
/// checks the grid and the bit declarations of the source cells. The integer writer reads such a cell
/// as an integer.</para>
/// <para>A gather read of a lowered program reads the candidate at the index that the bits of its
/// one-hot group give. A broken one-hot declaration is outside the contract. For such a group, the
/// index and the mass of the GPU can differ from the values of <see cref="TensorEvaluator"/>. The WGSL
/// conversion <c>i32()</c> of a NaN index sum is not defined, and <see cref="TensorEvaluator"/> gives 0.
/// A NaN member makes the sum of a masked read NaN on the GPU too.</para>
/// </remarks>
public sealed class GpuEvaluator : IBatchEvaluator, IRootEvaluator
{
    // The bindings of the shader: sources, outputs, params and the constant tables (WgslFloatWriter).
    private const int SourceBinding = 0;
    private const int OutputBinding = 1;
    private const int ParamsBinding = 2;
    private const int TableBinding = 3;

    private static readonly KernelBuffer[] Layout = [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage, KernelBuffer.ReadOnlyStorage];

    private readonly GpuDevice device;
    private readonly Circuit circuit;
    private readonly ComputeKernel kernel;
    private readonly bool checksDeclarations;
    // The values of the last call of the root evaluator of one table.
    private readonly float[] rootValues;
    private bool disposed;

    private GpuEvaluator(GpuDevice device, Circuit circuit, TensorProgram program, WgslModule module, int tablesPerChunk, bool checksDeclarations)
    {
        this.device = device;
        this.circuit = circuit;
        this.checksDeclarations = checksDeclarations;
        Program = program;
        Module = module;
        TablesPerChunk = tablesPerChunk;
        rootValues = new float[module.RootCount];
        kernel = new ComputeKernel(device, module.Code, Layout);
        try
        {
            // A binding of no bytes is not valid, so a shader with no constant table gets one unused cell.
            kernel.Write<float>(TableBinding, module.ConstantTable.IsEmpty ? [0f] : module.ConstantTable.AsSpan());
        }
        catch
        {
            kernel.Dispose();
            throw;
        }
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
    public static GpuEvaluator Create(GpuDevice device, Circuit circuit, TensorProgram program, GpuWriter writer = GpuWriter.Integer) =>
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
            GpuWriter.Integer => WgslIntegerWriter.Write(circuit, program),
            _ => throw new ArgumentOutOfRangeException(nameof(writer), writer, "unknown writer"),
        };
        var tablesPerChunk = Math.Min(ChunkLimit(device, module), maxTablesPerChunk ?? int.MaxValue);
        return new GpuEvaluator(device, circuit, program, module, tablesPerChunk, writer == GpuWriter.Integer);
    }

    /// <summary>Evaluate the tables and return a new array of <c>tableCount * RootCount</c> outputs.</summary>
    public float[] Evaluate(ReadOnlySpan<float> sources, int tableCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tableCount);
        var outputs = new float[checked(tableCount * RootCount)];
        Evaluate(sources, tableCount, outputs);
        return outputs;
    }

    /// <summary>
    /// Evaluate one source table on the GPU, and return the values of Program.Roots, in root order. The
    /// call does one dispatch and one readback. For many tables, use the batch Evaluate.
    /// </summary>
    /// <remarks>
    /// Each call returns the same array. So do not call this method of one evaluator on two threads at
    /// the same time. The second call overwrites the values of the first call.
    /// </remarks>
    /// <returns>The same array on each call. The next call overwrites it.</returns>
    public float[] Evaluate(ReadOnlySpan<float> source)
    {
        Evaluate(source, 1, rootValues);
        return rootValues;
    }

    public void Evaluate(ReadOnlySpan<float> sources, int tableCount, Span<float> outputs)
    {
        // The checks read only the circuit, the program and the arguments. So they run before the lock, on
        // parallel threads (the phase 5 carried item "the integer evaluator CPU declaration checks").
        ObjectDisposedException.ThrowIf(disposed, this);
        BatchArguments.CheckLengths(Program, sources, tableCount, outputs.Length);
        BatchArguments.CheckTables(circuit, Program, sources, tableCount, checksDeclarations, parallel: true);
        var sourceSize = Module.SourceSize;
        lock (device.Sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            device.ThrowIfUnusable();
            if (RootCount == 0) return;
            for (var first = 0; first < tableCount; first += TablesPerChunk)
            {
                var count = Math.Min(TablesPerChunk, tableCount - first);
                kernel.Write(SourceBinding, sources.Slice(first * sourceSize, count * sourceSize));
                // Params: first (the first table of the chunk in the batch, which the shader does not
                // read), count, zero (always 0), and one unused word. The unused word makes the size 16 bytes.
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
