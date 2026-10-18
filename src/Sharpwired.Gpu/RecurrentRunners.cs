// No Handwired source. The batch runners of a RecurrentFunction (phase 6 plan, task 7, and question 4,
// the carried items of phase 5). The file holds the status of each lane, the batch result, and the
// interface that the GPU and the CPU runners share. It also holds the parallel CPU runner and the factory.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Sharpwired.Cells;
using Sharpwired.IL;

namespace Sharpwired.Gpu;

/// <summary>The status of one lane of a <see cref="RecurrentBatch"/>.</summary>
public enum RecurrentLaneStatus
{
    /// <summary>An evaluation of the step gave done = 1.</summary>
    Completed,

    /// <summary>The step limit stopped the lane. <see cref="RecurrentResult.Completed"/> is false.</summary>
    Limit,

    /// <summary>A check failed. The message of the lane is the message of <see cref="RecurrentRunner"/>.</summary>
    Invalid,
}

/// <summary>
/// The result of a batch, lane after lane. Lane l output o is at <c>l * OutputCount + o</c> of
/// <see cref="Outputs"/>. A lane with the status <see cref="RecurrentLaneStatus.Invalid"/> has NaN
/// outputs, 0 steps, and the message of <see cref="RecurrentRunner"/> in <see cref="Messages"/>. The
/// message of each other lane is null.
/// </summary>
public sealed record RecurrentBatch(
    ImmutableArray<float> Outputs, ImmutableArray<int> Steps, ImmutableArray<RecurrentLaneStatus> Status,
    ImmutableArray<string?> Messages, int OutputCount)
{
    /// <summary>The result of one lane, as <see cref="RecurrentRunner.Run"/> gives it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The lane is not in the batch.</exception>
    /// <exception cref="InvalidOperationException">The lane is invalid. The message is "lane n: " and the
    /// message of the lane.</exception>
    public RecurrentResult Lane(int lane)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)lane, (uint)Steps.Length, nameof(lane));
        if (Status[lane] == RecurrentLaneStatus.Invalid) throw Fault(lane);
        return new RecurrentResult([.. Outputs.AsSpan(lane * OutputCount, OutputCount)], Steps[lane], Status[lane] == RecurrentLaneStatus.Completed);
    }

    /// <summary>
    /// Throw for the first invalid lane, as the runners of phase 5 do: an
    /// <see cref="InvalidOperationException"/> with "lane n: " and the message of the lane. A batch with
    /// no invalid lane does nothing.
    /// </summary>
    /// <remarks>
    /// The batch is a record of values, and it holds the message of each lane, not the exception of the CPU
    /// runner. So the exception has no inner exception.
    /// </remarks>
    public void ThrowIfInvalid()
    {
        var lane = Status.IndexOf(RecurrentLaneStatus.Invalid);
        if (lane >= 0) throw Fault(lane);
    }

    private InvalidOperationException Fault(int lane) => new($"lane {lane}: {Messages[lane]}");
}

/// <summary>A runner of the lanes of a <see cref="RecurrentFunction"/> on one backend.</summary>
public interface IRecurrentBatchRunner : IDisposable
{
    /// <summary>The function that the runner runs.</summary>
    RecurrentFunction Function { get; }

    /// <summary>
    /// Run the lanes. Lane l reads the argument table at <c>l * ArgumentCount</c> of
    /// <paramref name="arguments"/>. Each lane runs at most <paramref name="stepLimit"/> evaluations of
    /// the step. Each lane gives the result of <see cref="RecurrentRunner.Run"/>. A lane that fails a check
    /// gets the status <see cref="RecurrentLaneStatus.Invalid"/>, and the other lanes keep their results.
    /// </summary>
    /// <exception cref="ArgumentException">The span has the wrong length, a count is negative, or an
    /// argument table breaks a declaration of the prefix (the exception of its validator).</exception>
    RecurrentBatch Run(ReadOnlySpan<float> arguments, int laneCount, int stepLimit);
}

/// <summary>
/// The parallel CPU runner of a <see cref="RecurrentFunction"/>. Each lane runs
/// <see cref="RecurrentRunner.Run"/>. Parallel.For runs blocks of lanes. Each worker takes a
/// <see cref="RecurrentRunner"/> from a pool, so two calls on two threads at the same time are safe.
/// </summary>
public sealed class CpuRecurrentRunner : IRecurrentBatchRunner
{
    // The lanes of one block of Parallel.For.
    private const int BlockSize = 64;

    private readonly int? maxDegreeOfParallelism;
    private readonly ConcurrentBag<RecurrentRunner> pool = [];
    private readonly int argumentCount;
    private readonly int outputCount;
    private volatile bool disposed;

    /// <param name="function">The function.</param>
    /// <param name="maxDegreeOfParallelism">The limit of the threads, or null for no limit.</param>
    /// <exception cref="ArgumentOutOfRangeException">The limit is below 1.</exception>
    public CpuRecurrentRunner(RecurrentFunction function, int? maxDegreeOfParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (maxDegreeOfParallelism is { } limit) ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(maxDegreeOfParallelism));
        Function = function;
        this.maxDegreeOfParallelism = maxDegreeOfParallelism;
        argumentCount = function.Prefix.Table.Circuit.SourceSize;
        outputCount = function.Suffix.Outputs.Length;
        pool.Add(new RecurrentRunner(function));
    }

    /// <inheritdoc/>
    public RecurrentFunction Function { get; }

    /// <inheritdoc/>
    public RecurrentBatch Run(ReadOnlySpan<float> arguments, int laneCount, int stepLimit)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RecurrentBatches.CheckArguments(Function, arguments, argumentCount, laneCount, stepLimit);
        var table = arguments.ToArray();
        var outputs = new float[checked(laneCount * outputCount)];
        var steps = new int[laneCount];
        var status = new RecurrentLaneStatus[laneCount];
        var messages = new string?[laneCount];
        var blocks = (laneCount + BlockSize - 1) / BlockSize;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism ?? -1 };
        Parallel.For(0, blocks, parallel, block =>
        {
            var runner = pool.TryTake(out var free) ? free : new RecurrentRunner(Function);
            try
            {
                var end = Math.Min(laneCount, (block + 1) * BlockSize);
                for (var lane = block * BlockSize; lane < end; lane++)
                {
                    var laneArguments = table.AsSpan(lane * argumentCount, argumentCount);
                    if (RecurrentBatches.RunLane(runner, laneArguments, argumentCount, stepLimit, out var message) is { } result)
                    {
                        result.Outputs.AsSpan().CopyTo(outputs.AsSpan(lane * outputCount, outputCount));
                        steps[lane] = result.Steps;
                        status[lane] = result.Completed ? RecurrentLaneStatus.Completed : RecurrentLaneStatus.Limit;
                        continue;
                    }

                    outputs.AsSpan(lane * outputCount, outputCount).Fill(float.NaN);
                    status[lane] = RecurrentLaneStatus.Invalid;
                    messages[lane] = message;
                }
            }
            finally
            {
                pool.Add(runner);
            }
        });
        return new RecurrentBatch([.. outputs], [.. steps], [.. status], [.. messages], outputCount);
    }

    /// <summary>Stop the runner. A second call does nothing.</summary>
    public void Dispose()
    {
        disposed = true;
        pool.Clear();
    }
}

/// <summary>The factory of a recurrent batch runner.</summary>
public static class RecurrentRunners
{
    /// <summary>
    /// The GPU runner on the device, or the CPU runner when <paramref name="device"/> is null. The CPU
    /// runner does not read <paramref name="options"/>.
    /// </summary>
    public static IRecurrentBatchRunner Create(RecurrentFunction function, GpuDevice? device, GpuRecurrentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        return device is null ? new CpuRecurrentRunner(function) : GpuRecurrentRunner.Create(device, function, options);
    }
}

/// <summary>The parts that the GPU and the CPU batch runners share.</summary>
internal static class RecurrentBatches
{
    /// <summary>Check the counts, the length of the span and each argument table.</summary>
    public static void CheckArguments(RecurrentFunction function, ReadOnlySpan<float> arguments, int argumentCount, int laneCount, int stepLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(laneCount);
        ArgumentOutOfRangeException.ThrowIfNegative(stepLimit);
        if ((long)laneCount * argumentCount != arguments.Length)
        {
            throw new ArgumentException($"arguments holds {arguments.Length} values, and {laneCount} lanes need {(long)laneCount * argumentCount}", nameof(arguments));
        }
        var validator = function.Prefix.Table.Validator;
        for (var lane = 0; lane < laneCount; lane++) validator.Validate(arguments.Slice(lane * argumentCount, argumentCount));
    }

    /// <summary>
    /// Run one lane on the CPU. A check of the runner that fails gives null and the message of the
    /// runner. The arguments passed the validator of the prefix before this call.
    /// </summary>
    /// <remarks>
    /// A failed check is an <see cref="InvalidOperationException"/>, a <see cref="CellValueException"/>, or the
    /// plain <see cref="ArgumentException"/> of a one-hot or at-most-one rule of the cell validator. That rule
    /// has no type of its own. The method checks the step limit and the argument count first, so these argument
    /// errors throw. Each other subtype of <see cref="ArgumentException"/> also throws.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The step limit is negative.</exception>
    /// <exception cref="ArgumentException">The lane has other than argumentCount arguments.</exception>
    public static RecurrentResult? RunLane(RecurrentRunner runner, ReadOnlySpan<float> arguments, int argumentCount, int stepLimit, out string? message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stepLimit);
        if (arguments.Length != argumentCount) throw new ArgumentException($"the lane holds {arguments.Length} arguments, and the function has {argumentCount}", nameof(arguments));
        try
        {
            message = null;
            return runner.Run(arguments, stepLimit);
        }
        catch (Exception error) when (error is InvalidOperationException or CellValueException || error.GetType() == typeof(ArgumentException))
        {
            message = error.Message;
            return null;
        }
    }
}
