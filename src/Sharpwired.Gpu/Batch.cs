// No Handwired source. The batch contract of the evaluators: IBatchEvaluator, the CPU evaluator that
// runs TensorEvaluator on parallel threads, and BatchEvaluators, which selects the GPU or the CPU path.

using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>An evaluator of many source tables of one program.</summary>
public interface IBatchEvaluator : IDisposable
{
    /// <summary>The program that the evaluator runs.</summary>
    TensorProgram Program { get; }

    /// <summary>The number of outputs of each table: one for each root of the program.</summary>
    int RootCount { get; }

    /// <summary>
    /// Evaluate the tables. Table t cell c is at <c>t * SourceSize + c</c> of <paramref name="sources"/>.
    /// Table t root r is at <c>t * RootCount + r</c> of <paramref name="outputs"/>. A root that the
    /// program does not hold gives NaN.
    /// </summary>
    /// <exception cref="ArgumentException">A span has the wrong length, the count is negative, or a source
    /// table is outside the declared ranges of the circuit.</exception>
    void Evaluate(ReadOnlySpan<float> sources, int tableCount, Span<float> outputs);
}

/// <summary>
/// The CPU batch evaluator. It splits the tables into blocks, and runs the blocks on parallel threads.
/// Each thread has its own <see cref="TensorEvaluator"/> of the shared program.
/// </summary>
public sealed class CpuBatchEvaluator : IBatchEvaluator
{
    // The tables of one block of the parallel loop.
    private const int BlockSize = 1024;

    private readonly Circuit circuit;
    private readonly ParallelOptions options;
    private volatile bool disposed;

    /// <summary>Make the evaluator. <paramref name="maxDegreeOfParallelism"/> limits the threads.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit, or the limit
    /// is below 1.</exception>
    public CpuBatchEvaluator(Circuit circuit, TensorProgram program, int? maxDegreeOfParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        if (maxDegreeOfParallelism is { } limit) ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(maxDegreeOfParallelism));
        ShaderRows.Validate(circuit, program);
        this.circuit = circuit;
        Program = program;
        options = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism ?? -1 };
    }

    public TensorProgram Program { get; }

    public int RootCount => Program.Roots.Count;

    public void Evaluate(ReadOnlySpan<float> sources, int tableCount, Span<float> outputs)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        BatchArguments.Check(circuit, Program, sources, tableCount, outputs.Length);
        // A span cannot go into the lambda of the parallel loop, so the loop reads and writes arrays.
        var sourceArray = sources.ToArray();
        var outputArray = new float[outputs.Length];
        var sourceSize = Program.SourceSize;
        var rootCount = RootCount;
        var blockCount = (tableCount + BlockSize - 1) / BlockSize;
        Parallel.For(
            0,
            blockCount,
            options,
            () => circuit.CreateTensorProgramEvaluator(Program),
            (block, _, evaluator) =>
            {
                var end = Math.Min(tableCount, (block + 1) * BlockSize);
                for (var table = block * BlockSize; table < end; table++)
                {
                    var values = evaluator.Evaluate(sourceArray.AsSpan(table * sourceSize, sourceSize));
                    values.CopyTo(outputArray, table * rootCount);
                }
                return evaluator;
            },
            _ => { });
        outputArray.CopyTo(outputs);
    }

    /// <summary>Mark the evaluator as disposed. It holds no native object.</summary>
    public void Dispose() => disposed = true;
}

/// <summary>Selects the GPU or the CPU batch evaluator.</summary>
public static class BatchEvaluators
{
    /// <summary>
    /// Return a <see cref="GpuEvaluator"/> on the device, or a <see cref="CpuBatchEvaluator"/> when
    /// <paramref name="device"/> is null. The CPU evaluator does not use the writer.
    /// </summary>
    public static IBatchEvaluator Create(Circuit circuit, TensorProgram program, GpuDevice? device, GpuWriter writer = GpuWriter.Integer) =>
        device is null ? new CpuBatchEvaluator(circuit, program) : GpuEvaluator.Create(device, circuit, program, writer);
}

// The argument checks of IBatchEvaluator.Evaluate.
internal static class BatchArguments
{
    // Check the count, the span lengths and each source table, before any evaluation. A source table
    // outside the declared ranges throws the exception of TensorEvaluator.
    public static void Check(Circuit circuit, TensorProgram program, ReadOnlySpan<float> sources, int tableCount, int outputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tableCount);
        var sourceSize = program.SourceSize;
        if ((long)tableCount * sourceSize != sources.Length)
        {
            throw new ArgumentException($"sources holds {sources.Length} values, and {tableCount} tables need {(long)tableCount * sourceSize}", nameof(sources));
        }
        if ((long)tableCount * program.Roots.Count != outputLength)
        {
            throw new ArgumentException($"outputs holds {outputLength} values, and {tableCount} tables need {(long)tableCount * program.Roots.Count}", "outputs");
        }
        for (var table = 0; table < tableCount; table++) circuit.ValidateSourceRanges(sources.Slice(table * sourceSize, sourceSize));
    }
}
