// Handwired source: src/json-search/gpu/host.ts, createLoopRunner on the device. It holds the document buffers
// that stay on the GPU, the shared buffer and the packed words of each upload. It also holds the dispatches, and
// the readback of the document values and of the window. The GPU loop backend. A document buffer is one storage
// buffer, which a run binds whole: an invocation reads document params.first + t. A run splits the documents into
// chunks of at most 65,535 workgroups of 64 invocations, with one dispatch for each chunk. At the end, the run
// reads back the status of each document, the document values and the window values in one copy. A document whose
// status is 1 failed a check in the kernel. The runner replays it on the CPU runner, which gives the message of
// the fault. When the CPU finds no fault, the CPU runner gives the values (phase 5 carried item "a status for each
// lane").
// Differences from Handwired:
// - The kernel runs each position, also past the length of the text.
// - A document buffer must fit one storage binding of the device. Handwired splits a batch by the binding limit.
//   Here the documents stay on the GPU, so a buffer past the limit is refused when it is made.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sharpwired.Gpu;

/// <summary>
/// The options of the GPU loop backend: the writer of the kernels, and a salt. The salt makes each shader text
/// new (a cold compile for a bench).
/// </summary>
public sealed record GpuLoopOptions(GpuWriter Writer = GpuWriter.Integer, uint Salt = 0);

/// <summary>The GPU loop backend: document buffers on a WebGPU device, and runners of loop kernels.</summary>
public sealed class GpuLoopBackend : ILoopBackend
{
    // The bytes of one write of a fill value.
    private const int FillSlab = 1 << 20;

    private readonly ReadbackBuffer readback;
    private volatile bool disposed;

    /// <summary>Make the backend on the device.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The writer is unknown.</exception>
    public GpuLoopBackend(GpuDevice device, GpuLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        Device = device;
        Options = options ?? new GpuLoopOptions();
        if (Options.Writer is not (GpuWriter.Float32 or GpuWriter.Integer)) throw new ArgumentOutOfRangeException(nameof(options), Options.Writer, "unknown writer");
        readback = new ReadbackBuffer(device);
    }

    public GpuDevice Device { get; }

    public GpuLoopOptions Options { get; }

    private int replayPasses;

    /// <summary>
    /// The number of documents that a kernel of this backend marked failed and whose CPU replay found no fault, for
    /// tests. Such a document gets the CPU values, so a kernel check that fails falsely is visible only here. A test
    /// expects 0, apart from the documents that the subnormal rule excludes.
    /// </summary>
    internal int ReplayPasses => Volatile.Read(ref replayPasses);

    internal void CountReplayPass() => Interlocked.Increment(ref replayPasses);

    /// <summary>The largest number of documents of one dispatch, lower than the device limit, for a test of the chunks.</summary>
    internal int? MaxDocumentsPerChunk { get; set; }

    public IDocumentBuffer Upload(ReadOnlySpan<float> cells, int documentCount, int cellsPerDocument)
    {
        ThrowIfDisposed();
        var bytes = CheckSize(documentCount, cellsPerDocument);
        if (cells.Length != (long)documentCount * cellsPerDocument)
        {
            throw new ArgumentException(
                $"cells holds {cells.Length} values, and {documentCount} documents of {cellsPerDocument} cells need {(long)documentCount * cellsPerDocument}", nameof(cells));
        }
        var buffer = new StorageBuffer(Device);
        try
        {
            buffer.Write(cells);
            buffer.Reserve(bytes);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        return new GpuDocumentBuffer(this, buffer, documentCount, cellsPerDocument);
    }

    public IDocumentBuffer Allocate(int documentCount, int cellsPerDocument, float fill)
    {
        ThrowIfDisposed();
        var bytes = CheckSize(documentCount, cellsPerDocument);
        var buffer = new StorageBuffer(Device);
        try
        {
            buffer.Reserve(bytes);
            Fill(buffer, 0, bytes, fill);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        return new GpuDocumentBuffer(this, buffer, documentCount, cellsPerDocument);
    }

    public float[] Read(IDocumentBuffer buffer, int firstDocument, int documentCount)
    {
        ThrowIfDisposed();
        var owned = Own(buffer, nameof(buffer));
        if (firstDocument < 0 || documentCount < 0 || (long)firstDocument + documentCount > owned.DocumentCount)
        {
            throw new ArgumentException(
                $"documents {firstDocument}..{(long)firstDocument + documentCount - 1} must lie inside the {owned.DocumentCount} documents of the buffer", nameof(firstDocument));
        }
        var values = new float[(long)documentCount * owned.CellsPerDocument];
        var bytes = values.LongLength * sizeof(float);
        if (bytes == 0) return values;
        lock (Device.Sync)
        {
            ThrowIfDisposed();
            ComputeKernel.Submit(Device, [], [new BufferRead(owned.Storage, bytes, (long)firstDocument * owned.CellsPerDocument * sizeof(float))], readback, MemoryMarshal.AsBytes(values.AsSpan()));
        }
        return values;
    }

    public ILoopRunner CreateRunner(LoopProgram program)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(program);
        return new GpuLoopRunner(this, program);
    }

    /// <summary>The kernel of a runner of this backend: the shader text and its counts.</summary>
    public LoopWgslModule Module(ILoopRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return runner is GpuLoopRunner owned && ReferenceEquals(owned.Backend, this)
            ? owned.Module
            : throw new ArgumentException("the runner is a runner of another backend", nameof(runner));
    }

    /// <summary>Release the readback buffer and mark the backend as disposed. Dispose the runners and the buffers too.</summary>
    public void Dispose()
    {
        disposed = true;
        readback.Dispose();
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    internal GpuDocumentBuffer Own(IDocumentBuffer? buffer, string what)
    {
        ArgumentNullException.ThrowIfNull(buffer, what);
        if (buffer is not GpuDocumentBuffer owned || !ReferenceEquals(owned.Owner, this)) throw new ArgumentException($"{what} is a buffer of another backend", what);
        owned.ThrowIfDisposed();
        return owned;
    }

    // Write the fill value into the bytes from the offset, in slabs.
    internal static void Fill(StorageBuffer buffer, long offset, long bytes, float fill)
    {
        if (BitConverter.SingleToInt32Bits(fill) == 0 && offset == 0 && bytes == buffer.Size && buffer.Generation == 1) return;
        var slab = new float[Math.Min(FillSlab, bytes) / sizeof(float)];
        Array.Fill(slab, fill);
        for (long at = 0; at < bytes; at += slab.Length * sizeof(float))
        {
            var count = (int)Math.Min(slab.Length, (bytes - at) / sizeof(float));
            buffer.WriteAt<float>(offset + at, slab.AsSpan(0, count));
        }
    }

    // The bytes of the documents. A buffer must fit one storage binding of the device.
    private long CheckSize(int documentCount, int cellsPerDocument)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellsPerDocument, 1);
        var bytes = (long)documentCount * cellsPerDocument * sizeof(float);
        if (bytes > Device.MaxStorageBufferBindingSize)
        {
            throw new ArgumentException($"{documentCount} documents of {cellsPerDocument} cells need {bytes} bytes, and one storage binding of {Device.Adapter.Name} holds {Device.MaxStorageBufferBindingSize}");
        }
        return bytes;
    }
}

// The documents of the GPU backend: one storage buffer, document after document.
internal sealed class GpuDocumentBuffer(GpuLoopBackend owner, StorageBuffer storage, int documentCount, int cellsPerDocument) : IDocumentBuffer
{
    private volatile bool disposed;

    public GpuLoopBackend Owner { get; } = owner;

    public StorageBuffer Storage { get; } = storage;

    public int DocumentCount { get; } = documentCount;

    public int CellsPerDocument { get; } = cellsPerDocument;

    public void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Owner.ThrowIfDisposed();
    }

    public void Dispose()
    {
        disposed = true;
        Storage.Dispose();
    }
}

// The runner of one loop program on the GPU backend.
internal sealed class GpuLoopRunner : ILoopRunner
{
    private readonly GpuDevice device;
    private readonly ComputeKernel kernel;
    private readonly CpuLoopBackend replayBackend;
    private readonly ILoopRunner replay;
    private readonly ReadbackBuffer readback;
    private readonly int positionGroups;
    private readonly int documentValues;
    private bool sharedWritten;
    private volatile bool disposed;

    public GpuLoopRunner(GpuLoopBackend backend, LoopProgram program)
    {
        Backend = backend;
        device = backend.Device;
        Program = program;
        positionGroups = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        documentValues = program.OutputGroups.Where(group => group.Kind == OutputGroupKind.Document).Sum(group => group.ValuesPerDocument);
        var watch = Stopwatch.StartNew();
        Module = LoopKernelWriter.Write(program, backend.Options.Writer, backend.Options.Salt);
        WriteMs = watch.Elapsed.TotalMilliseconds;
        KernelBuffer[] layout =
        [
            KernelBuffer.ReadOnlyStorage, KernelBuffer.ReadOnlyStorage, KernelBuffer.ReadOnlyStorage, KernelBuffer.ReadOnlyStorage,
            KernelBuffer.Storage, KernelBuffer.Storage, KernelBuffer.Storage,
            .. program.Buffers.Select(_ => KernelBuffer.ReadOnlyStorage),
            .. Enumerable.Repeat(KernelBuffer.Storage, positionGroups),
        ];
        watch.Restart();
        kernel = new ComputeKernel(device, Module.Code, layout);
        CreateMs = watch.Elapsed.TotalMilliseconds;
        readback = new ReadbackBuffer(device);
        // The replay of a failed document runs each position, as the kernel does.
        replayBackend = new CpuLoopBackend(new CpuLoopOptions(CpuStageBackend.Compiled, StopAtLength: false));
        replay = replayBackend.CreateRunner(program);
        if (program.SharedSize == 0) WriteShared([]);
    }

    public GpuLoopBackend Backend { get; }

    public LoopProgram Program { get; }

    public LoopWgslModule Module { get; }

    /// <summary>The time to write the shader text, in milliseconds.</summary>
    public double WriteMs { get; }

    /// <summary>The time to create the shader module and the pipeline, in milliseconds.</summary>
    public double CreateMs { get; }

    public void WriteShared(ReadOnlySpan<float> shared)
    {
        ThrowIfDisposed();
        // The CPU runner checks the shared cells with the messages of the CPU backend, and keeps them for a replay.
        replay.WriteShared(shared);
        var values = new float[Math.Max(1, shared.Length + Module.ConstantTable.Length)];
        shared.CopyTo(values);
        Module.ConstantTable.CopyTo(values, shared.Length);
        var words = new uint[Math.Max(1, Module.PackedWordCount)];
        PackedWords.Pack(Module.PackedCells, shared, words);
        lock (device.Sync)
        {
            ThrowIfDisposed();
            kernel.Write<float>(LoopBindings.Shared, values);
            kernel.Write<uint>(LoopBindings.Packed, words);
            sharedWritten = true;
        }
    }

    public LoopRun Run(LoopRequest request, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var total = Stopwatch.StartNew();
        if (!sharedWritten) throw new InvalidOperationException($"WriteShared was not called; the program has {Program.SharedSize} shared cells");
        var (inputs, targets) = CheckRequest(request);
        var count = request.DocumentCount;
        var documentBytes = (long)count * documentValues * sizeof(float);
        var windowBytes = (long)positionGroups * request.WindowCount * Program.Positions * sizeof(float);
        var lanes = new uint[count];
        var documentValuesOut = new float[count * documentValues];
        var windowValues = new float[positionGroups * request.WindowCount * Program.Positions];
        double writeMs, submitMs = 0, waitMs = 0;
        var done = 0;
        lock (device.Sync)
        {
            ThrowIfDisposed();
            for (var buffer = 0; buffer < inputs.Length; buffer++) kernel.Bind(LoopBindings.FirstBuffer + buffer, inputs[buffer].Storage);
            var mask = 0u;
            for (var group = 0; group < positionGroups; group++)
            {
                var binding = LoopBindings.Target(inputs.Length, group);
                if (targets[group] is { } target)
                {
                    kernel.Bind(binding, target.Storage);
                    mask |= 1u << group;
                }
                else
                {
                    kernel.Unbind(binding);
                }
            }
            kernel.Write<uint>(LoopBindings.Run, [(uint)count, (uint)request.WindowFirst, (uint)request.WindowCount, mask]);
            kernel.Reserve(LoopBindings.Lanes, (long)count * sizeof(uint));
            kernel.Reserve(LoopBindings.DocumentValues, documentBytes);
            kernel.Reserve(LoopBindings.WindowValues, windowBytes);
            writeMs = total.Elapsed.TotalMilliseconds;
            var chunk = (int)Math.Min((long)device.MaxWorkgroupsPerDimension * WgslFloatWriter.WorkgroupSize, Backend.MaxDocumentsPerChunk ?? int.MaxValue);
            for (var first = 0; first < count && !cancel.IsCancellationRequested; first += chunk)
            {
                var size = Math.Min(chunk, count - first);
                kernel.Write<uint>(LoopBindings.Params, [(uint)first, (uint)size, 0u, 0u]);
                ComputeKernel.Submit(device, [new KernelDispatch(kernel, (uint)((size + WgslFloatWriter.WorkgroupSize - 1) / WgslFloatWriter.WorkgroupSize))], [], readback, []);
                done = first + size;
            }
            submitMs = total.Elapsed.TotalMilliseconds - writeMs;
            var bytes = new byte[((long)count * sizeof(uint)) + documentBytes + windowBytes];
            if (bytes.Length > 0)
            {
                ComputeKernel.Submit(
                    device, [],
                    [
                        new BufferRead(kernel.Buffer(LoopBindings.Lanes), (long)count * sizeof(uint)),
                        new BufferRead(kernel.Buffer(LoopBindings.DocumentValues), documentBytes),
                        new BufferRead(kernel.Buffer(LoopBindings.WindowValues), windowBytes),
                    ],
                    readback, bytes);
            }
            MemoryMarshal.Cast<byte, uint>(bytes.AsSpan(0, count * sizeof(uint))).CopyTo(lanes);
            MemoryMarshal.Cast<byte, float>(bytes.AsSpan(count * sizeof(uint), (int)documentBytes)).CopyTo(documentValuesOut);
            MemoryMarshal.Cast<byte, float>(bytes.AsSpan((int)(count * sizeof(uint) + documentBytes))).CopyTo(windowValues);
            waitMs = total.Elapsed.TotalMilliseconds - writeMs - submitMs;
        }

        var results = new RunResults(this, request, targets, documentValuesOut, windowValues);
        results.WriteNaN(done, count - done);
        var faults = Replay(inputs, results, Enumerable.Range(0, done).Where(document => lanes[document] != 0).ToList());
        if (done > 0) progress?.Report(done);
        var readMs = total.Elapsed.TotalMilliseconds - writeMs - submitMs - waitMs;
        return new LoopRun([.. documentValuesOut], [.. windowValues], faults, done, new LoopTimes(writeMs, submitMs, waitMs, readMs, total.Elapsed.TotalMilliseconds));
    }

    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            kernel.Dispose();
            readback.Dispose();
        }
        replay.Dispose();
        replayBackend.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Backend.ThrowIfDisposed();
    }

    // The checks of the request of the CPU backend, with buffers of this backend.
    private (GpuDocumentBuffer[] Inputs, GpuDocumentBuffer?[] Targets) CheckRequest(LoopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Inputs, nameof(request));
        if (request.Inputs.Count != Program.Buffers.Length)
        {
            throw new ArgumentException($"the request has {request.Inputs.Count} inputs, and the program has {Program.Buffers.Length} buffers", nameof(request));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(request.DocumentCount, nameof(request));
        var count = request.DocumentCount;
        var inputs = new GpuDocumentBuffer[Program.Buffers.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = Backend.Own(request.Inputs[index], $"input {index}");
            var buffer = Program.Buffers[index];
            if (input.CellsPerDocument != buffer.CellsPerDocument)
            {
                throw new ArgumentException($"input {index} holds {input.CellsPerDocument} cells for each document, and buffer {buffer.Name} of the program needs {buffer.CellsPerDocument}", nameof(request));
            }
            if (input.DocumentCount < count) throw new ArgumentException($"input {index} holds {input.DocumentCount} documents, and the request runs {count}", nameof(request));
            inputs[index] = input;
        }
        if (request.WindowFirst < 0 || request.WindowCount < 0 || (long)request.WindowFirst + request.WindowCount > count)
        {
            throw new ArgumentException($"the window {request.WindowFirst}..{(long)request.WindowFirst + request.WindowCount - 1} must lie inside the {count} documents", nameof(request));
        }
        var positionTargets = request.PositionTargets ?? [];
        if (positionTargets.Count != 0 && positionTargets.Count != positionGroups)
        {
            throw new ArgumentException($"the request has {positionTargets.Count} position targets, and the program has {positionGroups} position groups", nameof(request));
        }
        var targets = new GpuDocumentBuffer?[positionGroups];
        for (var index = 0; index < positionTargets.Count; index++)
        {
            if (positionTargets[index] is not { } target) continue;
            var owned = Backend.Own(target, $"position target {index}");
            if (owned.CellsPerDocument != Program.Positions || owned.DocumentCount < count || request.Inputs.Contains(target))
            {
                throw new ArgumentException($"position target {index} must hold {count} documents of {Program.Positions} cells and must not be an input", nameof(request));
            }
            var earlier = Array.IndexOf(targets, owned);
            if (earlier >= 0) throw new ArgumentException($"position target {index} is also position target {earlier}", nameof(request));
            targets[index] = owned;
        }
        return (inputs, targets);
    }

    // Replay each document whose status is 1 on the CPU runner. A fault of the CPU runner gives a LaneFault and
    // NaN outputs. With no fault, the document gets the CPU values: the CPU runner is the authority.
    private ImmutableArray<LaneFault> Replay(GpuDocumentBuffer[] inputs, RunResults results, List<int> documents)
    {
        if (documents.Count == 0) return [];
        var cpuInputs = new IDocumentBuffer[inputs.Length];
        try
        {
            for (var buffer = 0; buffer < inputs.Length; buffer++)
            {
                var cells = inputs[buffer].CellsPerDocument;
                var values = new float[documents.Count * cells];
                // Many faults read the documents in one copy, few faults one document at a time.
                var whole = documents.Count * 8 > inputs[buffer].DocumentCount ? Backend.Read(inputs[buffer], 0, documents[^1] + 1) : null;
                for (var index = 0; index < documents.Count; index++)
                {
                    var document = whole is null ? Backend.Read(inputs[buffer], documents[index], 1) : whole.AsSpan(documents[index] * cells, cells).ToArray();
                    document.CopyTo(values, index * cells);
                }
                cpuInputs[buffer] = replayBackend.Upload(values, documents.Count, cells);
            }
            var run = replay.Run(new LoopRequest(cpuInputs, documents.Count, [], 0, documents.Count));
            var messages = run.Faults.ToDictionary(fault => fault.Document, fault => fault.Message);
            var faults = ImmutableArray.CreateBuilder<LaneFault>();
            for (var index = 0; index < documents.Count; index++)
            {
                if (messages.TryGetValue(index, out var message))
                {
                    faults.Add(new LaneFault(documents[index], message));
                    results.Write(documents[index], null, null);
                    continue;
                }
                results.Write(documents[index], run, index);
                Backend.CountReplayPass();
            }
            return faults.ToImmutable();
        }
        finally
        {
            foreach (var buffer in cpuInputs) buffer?.Dispose();
        }
    }

    // The outputs of one run on the host, and the writes of the values of one document. The class writes NaN,
    // or the values of one document of a CPU run.
    private sealed class RunResults(GpuLoopRunner runner, LoopRequest request, GpuDocumentBuffer?[] targets, float[] documentValues, float[] windowValues)
    {
        // Write NaN for the documents first..first + count - 1, which the run did not reach. Each output and each
        // target gets one fill for the whole range, with no array for each document.
        public void WriteNaN(int first, int count)
        {
            if (count <= 0) return;
            var program = runner.Program;
            var positions = program.Positions;
            var windowFirst = Math.Max(first, request.WindowFirst);
            var windowEnd = Math.Min(first + count, request.WindowFirst + request.WindowCount);
            int documentStart = 0, positionGroup = 0;
            foreach (var group in program.OutputGroups)
            {
                if (group.Kind == OutputGroupKind.Document)
                {
                    documentValues.AsSpan((documentStart * request.DocumentCount) + (first * group.ValuesPerDocument), count * group.ValuesPerDocument).Fill(float.NaN);
                    documentStart += group.ValuesPerDocument;
                    continue;
                }
                if (windowEnd > windowFirst)
                {
                    windowValues.AsSpan(((positionGroup * request.WindowCount) + windowFirst - request.WindowFirst) * positions, (windowEnd - windowFirst) * positions).Fill(float.NaN);
                }
                if (targets[positionGroup] is { } target)
                {
                    GpuLoopBackend.Fill(target.Storage, (long)first * positions * sizeof(float), (long)count * positions * sizeof(float), float.NaN);
                }
                positionGroup++;
            }
        }

        public void Write(int document, LoopRun? cpu, int? cpuDocument)
        {
            var program = runner.Program;
            var positions = program.Positions;
            var cpuCount = cpu is null ? 0 : cpu.DocumentValues.Length / Math.Max(1, runner.documentValues);
            int documentStart = 0, positionGroup = 0;
            foreach (var group in program.OutputGroups)
            {
                if (group.Kind == OutputGroupKind.Document)
                {
                    for (var column = 0; column < group.ValuesPerDocument; column++)
                    {
                        var value = cpu is null ? float.NaN : cpu.DocumentValues[(documentStart * cpuCount) + (cpuDocument!.Value * group.ValuesPerDocument) + column];
                        documentValues[(documentStart * request.DocumentCount) + (document * group.ValuesPerDocument) + column] = value;
                    }
                    documentStart += group.ValuesPerDocument;
                    continue;
                }
                var values = new float[positions];
                if (cpu is null) Array.Fill(values, float.NaN);
                else cpu.WindowPositionValues.AsSpan(((positionGroup * cpuCount) + cpuDocument!.Value) * positions, positions).CopyTo(values);
                if (document >= request.WindowFirst && document < request.WindowFirst + request.WindowCount)
                {
                    values.CopyTo(windowValues, ((positionGroup * request.WindowCount) + document - request.WindowFirst) * positions);
                }
                if (targets[positionGroup] is { } target) target.Storage.WriteAt<float>((long)document * positions * sizeof(float), values);
                positionGroup++;
            }
        }
    }
}
