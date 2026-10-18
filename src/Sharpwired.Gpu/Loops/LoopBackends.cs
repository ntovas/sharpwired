// Handwired source: src/json-search/gpu/host.ts (the document buffers that stay on the device, a run over
// a batch of documents, the window of documents whose values for each position come back, and the
// times). The contracts of a loop backend: the documents that a backend holds, the runner of one loop
// program, and the records of a run. CpuLoopBackend implements them on the CPU, and the GPU backend of
// task 3 on a WebGPU device. The phase 5 carried item "one invalid lane throws and the batch loses the
// other lanes" gives LaneFault: a run keeps the values of each valid document.

using System.Collections.Immutable;

namespace Sharpwired.Gpu;

/// <summary>Documents that a backend holds: on the GPU a storage buffer, on the CPU an array.</summary>
public interface IDocumentBuffer : IDisposable
{
    /// <summary>The number of documents.</summary>
    int DocumentCount { get; }

    /// <summary>The number of cells of each document.</summary>
    int CellsPerDocument { get; }
}

/// <summary>A document that failed a check, and the message of the check. The run keeps the other documents.</summary>
public sealed record LaneFault(int Document, string Message);

/// <summary>
/// The times of one run in milliseconds: the writes before the run, the submission, the wait for the
/// work, the reads of the results, and the total.
/// </summary>
public sealed record LoopTimes(double WriteMs, double SubmitMs, double WaitMs, double ReadMs, double TotalMs);

/// <summary>One run of a loop program over the first documents of its input buffers.</summary>
/// <param name="Inputs">One buffer for each <see cref="LoopBuffer"/> of the program, in buffer order.</param>
/// <param name="DocumentCount">The number of documents to run, from document 0.</param>
/// <param name="PositionTargets">
/// One entry for each position group of the program, in group order: a buffer that keeps all values of the
/// group, with one document for each document of the run and Positions cells for each document, or null.
/// An empty list gives null for each group.
/// </param>
/// <param name="WindowFirst">The first document whose values for each position come back.</param>
/// <param name="WindowCount">The number of documents whose values for each position come back.</param>
public sealed record LoopRequest(
    IReadOnlyList<IDocumentBuffer> Inputs,
    int DocumentCount,
    IReadOnlyList<IDocumentBuffer?> PositionTargets,
    int WindowFirst,
    int WindowCount);

/// <summary>The result of one run.</summary>
/// <param name="DocumentValues">
/// The values of each document group, group after group in group order, document after document, with
/// ValuesPerDocument values for each document. A document that has a fault, or that the run did not reach,
/// holds NaN.
/// </param>
/// <param name="WindowPositionValues">
/// The values of each position group for the window documents: group after group in group order, window
/// document after window document, with Positions values for each document. A position past the end of
/// the text holds the fill value of its group when the loops stop at the length.
/// </param>
/// <param name="Faults">One fault for each document that failed a check, in document order.</param>
/// <param name="DocumentsDone">
/// The number of documents from document 0 that the run completed. It is less than the document count
/// only after a cancellation, and then it is a multiple of the block size.
/// </param>
/// <param name="Times">The times of the run.</param>
public sealed record LoopRun(
    ImmutableArray<float> DocumentValues,
    ImmutableArray<float> WindowPositionValues,
    ImmutableArray<LaneFault> Faults,
    int DocumentsDone,
    LoopTimes Times);

/// <summary>The runner of one loop program on one backend.</summary>
public interface ILoopRunner : IDisposable
{
    /// <summary>The program that the runner runs.</summary>
    LoopProgram Program { get; }

    /// <summary>
    /// Write the shared cells. Each shared cell is checked against the declaration of each stage cell that
    /// reads it. A program with no shared cell needs no call.
    /// </summary>
    /// <exception cref="ArgumentException">The span has the wrong length, or a value breaks a declaration.</exception>
    void WriteShared(ReadOnlySpan<float> shared);

    /// <summary>
    /// Run the program over the documents of the request. The progress gets the number of documents done
    /// from document 0, in increasing order. A cancellation stops the run between blocks of documents and
    /// returns the documents done. A document that fails a check gives a <see cref="LaneFault"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The request does not match the program or the backend.</exception>
    /// <exception cref="InvalidOperationException">The program has shared cells, and WriteShared was not called.</exception>
    LoopRun Run(LoopRequest request, IProgress<int>? progress = null, CancellationToken cancel = default);
}

/// <summary>A device that holds documents and runs loop programs over them.</summary>
public interface ILoopBackend : IDisposable
{
    /// <summary>Copy documents to the backend: document after document, cellsPerDocument cells each.</summary>
    IDocumentBuffer Upload(ReadOnlySpan<float> cells, int documentCount, int cellsPerDocument);

    /// <summary>A buffer of documents whose cells all hold the fill value.</summary>
    IDocumentBuffer Allocate(int documentCount, int cellsPerDocument, float fill);

    /// <summary>Copy the cells of documents firstDocument..firstDocument + documentCount - 1 back to the host.</summary>
    float[] Read(IDocumentBuffer buffer, int firstDocument, int documentCount);

    /// <summary>A runner of the program on this backend.</summary>
    ILoopRunner CreateRunner(LoopProgram program);
}

/// <summary>How the CPU backend evaluates one stage.</summary>
public enum CpuStageBackend
{
    /// <summary>The compiled delegate of <see cref="CompiledStage"/>, the default.</summary>
    Compiled,

    /// <summary><see cref="Sharpwired.Core.TensorEvaluator"/>, for tests.</summary>
    Tensor,
}

/// <summary>The options of the CPU backend.</summary>
/// <param name="Stages">How each stage is evaluated.</param>
/// <param name="StopAtLength">
/// True: the looped stages stop at the end of the text, which the length cell of the program gives.
/// This gives the same outputs only for steps that keep each register past the end and write the fill
/// value of each position group there (Handwired stopAtLength).
/// </param>
/// <param name="MaxDegreeOfParallelism">The limit of the threads, or null for no limit.</param>
/// <param name="BlockSize">The number of documents of one block of the parallel loop.</param>
public sealed record CpuLoopOptions(
    CpuStageBackend Stages = CpuStageBackend.Compiled,
    bool StopAtLength = true,
    int? MaxDegreeOfParallelism = null,
    int BlockSize = 256);
