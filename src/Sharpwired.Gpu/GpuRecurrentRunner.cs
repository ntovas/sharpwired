// No Handwired source. The GPU runner of a step program (phase 5 plan, question 5, R5.4). It runs the
// prefix and the step kernel of a RecurrentFunction in one command buffer. It submits more dispatches
// of the step kernel while a lane runs, and it reads the lane status. Then it runs the suffix one time
// (phase 6 plan, task 7). It replays a failed lane on the CPU to get the message of the CPU runner. A
// failed lane gets the status Invalid, and the other lanes keep their results.

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Sharpwired.Core;
using Sharpwired.IL;

namespace Sharpwired.Gpu;

/// <summary>The settings of a <see cref="GpuRecurrentRunner"/>.</summary>
/// <param name="StepsPerDispatch">The largest count of step evaluations of one lane in one dispatch. It
/// must be 1 or more.</param>
/// <param name="Writer">The WGSL writer of the three programs.</param>
public sealed record GpuRecurrentOptions(int StepsPerDispatch = 1024, GpuWriter Writer = GpuWriter.Integer);

/// <summary>
/// The GPU runner of a <see cref="RecurrentFunction"/>. One lane is one argument table. Each lane gives
/// the result of <see cref="RecurrentRunner.Run"/>: the same steps, the same Completed, and the same
/// outputs, with the writer limits of <see cref="GpuEvaluator"/>. A lane that fails a check gets the
/// status <see cref="RecurrentLaneStatus.Invalid"/> and the message of the CPU runner.
/// </summary>
/// <remarks>
/// <para>The runner checks each argument table on the CPU with the validator of the prefix. The prefix
/// and the first dispatch of the step kernel run in one command buffer. The step kernel runs at most
/// <see cref="StepsPerDispatch"/> evaluations of each lane in one dispatch, because a GPU command that
/// runs too long stops on some systems. The runner submits more dispatches of the step kernel while a
/// lane runs. A lane that is done stays idle until the other lanes are done. After the last round, the
/// suffix runs one time on each lane of the chunk.</para>
/// <para>The step kernel checks each cell with the declarations of the step table. At the first
/// evaluation it checks each cell. Then it checks each next value of a state cell, before the done test.
/// So a value past a cell range, also the range -2^23..2^23 of a wide int cell, stops the lane. It is
/// never a silent wrong value. The kernel checks use is_finite and bit tests on computed values, which
/// the WGSL specification does not guarantee. For the message, the runner replays each failed lane on
/// the CPU with <see cref="RecurrentRunner"/>. The lane gets the status
/// <see cref="RecurrentLaneStatus.Invalid"/> and the CPU message. When the CPU replay passes, the lane
/// gets the result of the CPU replay: the CPU runner is the authority, as in the GPU loop runner. The
/// same replay applies to a nonzero loop overflow bit and to a suffix output that is not finite.</para>
/// <para>The runner uses the lock of its device for the full call. Dispose the runner before its
/// device.</para>
/// </remarks>
public sealed class GpuRecurrentRunner : IRecurrentBatchRunner
{
    private static readonly KernelBuffer[] PassLayout = [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage];

    private static readonly KernelBuffer[] StepLayout =
        [KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage, KernelBuffer.Storage, KernelBuffer.ReadOnlyStorage];

    private readonly GpuDevice device;
    private readonly RecurrentFunction function;
    private readonly int argumentCount;
    private readonly int cellCount;
    private readonly int prefixStride;
    private readonly int suffixStride;
    private readonly int outputOffset;
    private readonly int outputCount;
    private readonly int suffixOverflowCount;
    private readonly int workgroupSize;
    private readonly List<IDisposable> owned = [];
    private readonly ComputeKernel prefix;
    private readonly ComputeKernel step;
    private readonly ComputeKernel suffix;
    private readonly StorageBuffer arguments;
    private readonly StorageBuffer initial;
    private readonly StorageBuffer state;
    private readonly StorageBuffer parameters;
    private readonly StorageBuffer lanes;
    private readonly StorageBuffer run;
    private readonly StorageBuffer suffixOutputs;
    private readonly ReadbackBuffer readback;
    private RecurrentRunner? replayRunner;
    private bool disposed;

    private GpuRecurrentRunner(GpuDevice device, RecurrentFunction function, GpuRecurrentOptions options, int? maxLanesPerChunk)
    {
        this.device = device;
        this.function = function;
        StepsPerDispatch = options.StepsPerDispatch;
        var writer = options.Writer;
        var prefixProgram = function.Prefix.CreateEvaluator().Program;
        var suffixProgram = function.Suffix.CreateEvaluator().Program;
        var stepProgram = RecurrentKernelWriter.StepProgram(function.Step);
        var layout = RecurrentKernelWriter.Initial(function, prefixProgram);
        var kernelText = RecurrentKernelWriter.Write(function.Step, stepProgram, layout, writer);
        var prefixModule = Module(writer, function.Prefix.Table.Circuit, prefixProgram);
        var suffixModule = Module(writer, function.Suffix.Table.Circuit, suffixProgram);
        argumentCount = prefixModule.SourceSize;
        cellCount = kernelText.CellCount;
        if (suffixModule.SourceSize != cellCount)
        {
            throw new ArgumentException($"the suffix has {suffixModule.SourceSize} cells, and the step has {cellCount}", nameof(function));
        }
        prefixStride = layout.Stride;
        suffixStride = suffixModule.RootCount;
        outputOffset = function.Suffix.Table.NextSource.Length;
        outputCount = function.Suffix.Outputs.Length;
        suffixOverflowCount = function.Suffix.LoopOverflowBits.Length;
        workgroupSize = kernelText.WorkgroupSize;
        LanesPerChunk = Math.Min(ChunkLimit(device), maxLanesPerChunk ?? int.MaxValue);
        try
        {
            prefix = Own(new ComputeKernel(device, prefixModule.Code, PassLayout));
            step = Own(new ComputeKernel(device, kernelText.Code, StepLayout));
            suffix = Own(new ComputeKernel(device, suffixModule.Code, PassLayout));
            arguments = Own(new StorageBuffer(device));
            initial = Own(new StorageBuffer(device));
            state = Own(new StorageBuffer(device));
            parameters = Own(new StorageBuffer(device));
            lanes = Own(new StorageBuffer(device));
            run = Own(new StorageBuffer(device));
            suffixOutputs = Own(new StorageBuffer(device));
            readback = Own(new ReadbackBuffer(device));
            prefix.Bind(0, arguments);
            prefix.Bind(1, initial);
            prefix.Bind(2, parameters);
            step.Bind(0, initial);
            step.Bind(1, state);
            step.Bind(2, parameters);
            step.Bind(3, lanes);
            step.Bind(4, run);
            suffix.Bind(0, state);
            suffix.Bind(1, suffixOutputs);
            suffix.Bind(2, parameters);
        }
        catch
        {
            ReleaseOwned();
            throw;
        }
    }

    /// <inheritdoc/>
    public RecurrentFunction Function => function;

    /// <summary>The largest count of step evaluations of one lane in one dispatch.</summary>
    public int StepsPerDispatch { get; }

    /// <summary>The largest count of lanes of one chunk: the buffers of a chunk fit the device limits.</summary>
    internal int LanesPerChunk { get; }

    /// <summary>The count of step kernel dispatches since the runner was created.</summary>
    internal int StepDispatches { get; private set; }

    /// <summary>The count of suffix dispatches since the runner was created.</summary>
    internal int SuffixDispatches { get; private set; }

    /// <summary>
    /// Write the step kernel, the prefix shader and the suffix shader of the function, and create their
    /// pipelines on the device.
    /// </summary>
    /// <exception cref="ArgumentException">The function has parts of other shapes.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The writer is unknown, or
    /// <see cref="GpuRecurrentOptions.StepsPerDispatch"/> is below 1.</exception>
    /// <exception cref="InvalidOperationException">The device is lost, or a shader or a pipeline failed
    /// (the message holds the wgpu message).</exception>
    public static GpuRecurrentRunner Create(GpuDevice device, RecurrentFunction function, GpuRecurrentOptions? options = null) =>
        Create(device, function, options, null);

    /// <summary>
    /// <see cref="Create(GpuDevice, RecurrentFunction, GpuRecurrentOptions)"/> with the writer and the
    /// default value of each other setting.
    /// </summary>
    public static GpuRecurrentRunner Create(GpuDevice device, RecurrentFunction function, GpuWriter writer) =>
        Create(device, function, new GpuRecurrentOptions(Writer: writer), null);

    /// <summary>
    /// <see cref="Create(GpuDevice, RecurrentFunction, GpuRecurrentOptions)"/> with a lower chunk limit, so
    /// that a test can split a small batch into chunks.
    /// </summary>
    internal static GpuRecurrentRunner Create(GpuDevice device, RecurrentFunction function, GpuRecurrentOptions? options, int? maxLanesPerChunk)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(function);
        options ??= new GpuRecurrentOptions();
        if (options.Writer is not (GpuWriter.Float32 or GpuWriter.Integer)) throw new ArgumentOutOfRangeException(nameof(options), options.Writer, "unknown writer");
        ArgumentOutOfRangeException.ThrowIfLessThan(options.StepsPerDispatch, 1, nameof(options));
        if (maxLanesPerChunk is { } limit) ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(maxLanesPerChunk));
        return new GpuRecurrentRunner(device, function, options, maxLanesPerChunk);
    }

    /// <summary>Run one lane. The result equals the result of <see cref="RecurrentRunner.Run"/>.</summary>
    public RecurrentResult Run(ReadOnlySpan<float> arguments, int stepLimit) => Run(arguments, 1, stepLimit).Lane(0);

    /// <summary>
    /// Run the lanes. Lane l reads the argument table at <c>l * ArgumentCount</c> of
    /// <paramref name="arguments"/>. Each lane runs at most <paramref name="stepLimit"/> evaluations of
    /// the step. A lane that fails a check gets the status <see cref="RecurrentLaneStatus.Invalid"/> and
    /// the message of the CPU runner.
    /// </summary>
    /// <exception cref="ArgumentException">The span has the wrong length, a count is negative, or an
    /// argument table breaks a declaration of the prefix (the exception of its validator).</exception>
    /// <exception cref="InvalidOperationException">The device is lost, or the step kernel gave a lane
    /// status that is not known.</exception>
    public RecurrentBatch Run(ReadOnlySpan<float> arguments, int laneCount, int stepLimit)
    {
        lock (device.Sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            device.ThrowIfUnusable();
            RecurrentBatches.CheckArguments(function, arguments, argumentCount, laneCount, stepLimit);
            var result = new BatchArrays(laneCount, outputCount);
            for (var first = 0; first < laneCount; first += LanesPerChunk)
            {
                var count = Math.Min(LanesPerChunk, laneCount - first);
                RunChunk(arguments.Slice(first * argumentCount, count * argumentCount), first, count, stepLimit, result);
            }
            return new RecurrentBatch([.. result.Outputs], [.. result.Steps], [.. result.Status], [.. result.Messages], outputCount);
        }
    }

    /// <summary>Release the kernels and the buffers. A second call does nothing.</summary>
    public void Dispose()
    {
        lock (device.Sync)
        {
            if (disposed) return;
            disposed = true;
            ReleaseOwned();
        }
    }

    private static WgslModule Module(GpuWriter writer, Circuit circuit, TensorProgram program) =>
        writer == GpuWriter.Float32 ? WgslFloatWriter.Write(circuit, program) : WgslIntegerWriter.Write(circuit, program);

    private T Own<T>(T item) where T : IDisposable
    {
        owned.Add(item);
        return item;
    }

    private void ReleaseOwned()
    {
        for (var index = owned.Count - 1; index >= 0; index--) owned[index].Dispose();
        owned.Clear();
    }

    // The largest chunk of this runner on the device.
    private int ChunkLimit(GpuDevice gpu)
    {
        long widest = Math.Max(Math.Max(argumentCount, prefixStride), Math.Max(cellCount, suffixStride));
        return ChunkLimit(gpu.MaxStorageBufferBindingSize, gpu.MaxBufferSize, gpu.MaxWorkgroupsPerDimension, workgroupSize, widest, suffixStride);
    }

    /// <summary>
    /// The largest chunk. Each buffer of a chunk fits one binding and one buffer. The readback of a
    /// chunk fits one buffer and one byte array. The workgroups fit one dispatch dimension.
    /// <paramref name="widest"/> is the largest count of values of one lane in one buffer.
    /// </summary>
    internal static int ChunkLimit(long maxBinding, long maxBuffer, int maxWorkgroups, int workgroupSize, long widest, int suffixStride)
    {
        var binding = Math.Min(maxBinding, maxBuffer);
        widest = Math.Max(widest, RecurrentKernelWriter.LaneWords);
        var byBindings = binding / (4L * widest);
        // The readback of a chunk goes into one byte array, whose length is below int.MaxValue.
        var byReadback = Math.Min(maxBuffer, Array.MaxLength) / (4L * (RecurrentKernelWriter.LaneWords + suffixStride));
        var byWorkgroups = (long)workgroupSize * maxWorkgroups;
        return (int)Math.Max(1, Math.Min(Math.Min(byBindings, byReadback), Math.Min(byWorkgroups, int.MaxValue)));
    }

    // One chunk: the first round holds the prefix and one dispatch of the step kernel. Each later round
    // holds one dispatch of the step kernel. Each round reads back the lane records. The rounds end when
    // no lane is Fresh or Running. Then one submission holds the suffix, and it reads back the lane
    // records and the suffix outputs.
    private void RunChunk(ReadOnlySpan<float> chunkArguments, int first, int count, int stepLimit, BatchArrays result)
    {
        arguments.Write(chunkArguments);
        parameters.Write<uint>([0u, (uint)count, 0u, 0u]);
        run.Write<uint>([(uint)stepLimit, (uint)StepsPerDispatch, 0u, 0u]);
        lanes.Write<uint>(new uint[count * RecurrentKernelWriter.LaneWords]);
        initial.Reserve((long)count * prefixStride * sizeof(float));
        state.Reserve((long)count * cellCount * sizeof(float));
        suffixOutputs.Reserve((long)count * suffixStride * sizeof(float));
        var workgroups = (uint)((count + workgroupSize - 1) / workgroupSize);
        var laneBytes = (long)count * RecurrentKernelWriter.LaneWords * sizeof(uint);
        var outputBytes = (long)count * suffixStride * sizeof(float);
        var bytes = new byte[laneBytes + outputBytes];
        BufferRead[] laneReads = [new(lanes, laneBytes)];
        var laneRecords = bytes.AsMemory(0, (int)laneBytes);
        ComputeKernel.Submit(device, [new(prefix, workgroups), new(step, workgroups)], laneReads, readback, laneRecords.Span);
        StepDispatches++;
        while (AnyRunning(MemoryMarshal.Cast<byte, uint>(laneRecords.Span)))
        {
            ComputeKernel.Submit(device, [new(step, workgroups)], laneReads, readback, laneRecords.Span);
            StepDispatches++;
        }
        ComputeKernel.Submit(device, [new(suffix, workgroups)], [new(lanes, laneBytes), new(suffixOutputs, outputBytes)], readback, bytes);
        SuffixDispatches++;

        var records = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan(0, (int)laneBytes));
        var values = MemoryMarshal.Cast<byte, float>(bytes.AsSpan((int)laneBytes));
        for (var index = 0; index < count; index++)
        {
            var lane = first + index;
            var laneArguments = chunkArguments.Slice(index * argumentCount, argumentCount);
            var status = (LaneStatus)records[index * RecurrentKernelWriter.LaneWords];
            var laneSteps = records[(index * RecurrentKernelWriter.LaneWords) + 1];
            if (status is not (LaneStatus.Completed or LaneStatus.Limit or LaneStatus.Invalid))
            {
                throw new InvalidOperationException($"lane {lane}: the step kernel ended with the status {status}");
            }
            var row = values.Slice(index * suffixStride, suffixStride);
            var failed = status == LaneStatus.Invalid;
            for (var output = 0; output < outputCount; output++) failed |= !float.IsFinite(row[outputOffset + output]);
            for (var bit = 0; bit < suffixOverflowCount; bit++) failed |= row[outputOffset + outputCount + bit] != 0 || float.IsNaN(row[outputOffset + outputCount + bit]);
            if (failed)
            {
                Replay(lane, laneArguments, stepLimit, result);
                continue;
            }
            row.Slice(outputOffset, outputCount).CopyTo(result.Outputs.AsSpan(lane * outputCount, outputCount));
            result.Steps[lane] = (int)laneSteps;
            result.Status[lane] = status == LaneStatus.Completed ? RecurrentLaneStatus.Completed : RecurrentLaneStatus.Limit;
        }
    }

    private static bool AnyRunning(ReadOnlySpan<uint> records)
    {
        for (var index = 0; index < records.Length; index += RecurrentKernelWriter.LaneWords)
        {
            if ((LaneStatus)records[index] is LaneStatus.Fresh or LaneStatus.Running) return true;
        }
        return false;
    }

    // Run the lane on the CPU. A CPU check that fails gives the status Invalid and the CPU message. A CPU
    // run that passes gives the CPU result: the CPU runner is the authority, as in the GPU loop runner.
    private void Replay(int lane, ReadOnlySpan<float> laneArguments, int stepLimit, BatchArrays result)
    {
        replayRunner ??= new RecurrentRunner(function);
        if (RecurrentBatches.RunLane(replayRunner, laneArguments, stepLimit, out var message) is { } cpu)
        {
            cpu.Outputs.AsSpan().CopyTo(result.Outputs.AsSpan(lane * outputCount, outputCount));
            result.Steps[lane] = cpu.Steps;
            result.Status[lane] = cpu.Completed ? RecurrentLaneStatus.Completed : RecurrentLaneStatus.Limit;
            return;
        }
        result.Outputs.AsSpan(lane * outputCount, outputCount).Fill(float.NaN);
        result.Status[lane] = RecurrentLaneStatus.Invalid;
        result.Messages[lane] = message;
    }

    // The arrays of the result of one batch.
    private sealed class BatchArrays(int laneCount, int outputCount)
    {
        public float[] Outputs { get; } = new float[checked(laneCount * outputCount)];

        public int[] Steps { get; } = new int[laneCount];

        public RecurrentLaneStatus[] Status { get; } = new RecurrentLaneStatus[laneCount];

        public string?[] Messages { get; } = new string?[laneCount];
    }
}
