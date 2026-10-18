// Handwired source: src/json-search/loop-program.ts (createLoopRunner: setShared and run). The CPU runner
// of a loop program. Parallel.For runs blocks of documents. Each worker thread has its own registers,
// fields and stage evaluators: a copy of the CompiledStage of each stage (the delegates are shared), or a
// TensorEvaluator of each stage for tests. For one document, the runner sets the registers to their
// initial values and runs the stages in order. A looped stage runs the positions 0..limit - 1 forward or
// backward, where limit is the number of positions, or the length of the text with StopAtLength. Before
// each evaluation, the runner checks each cell that is not a shared cell against the declaration of the
// stage, with the messages of CellValidator. The shared cells are checked one time in WriteShared. A
// document that fails a check gets a LaneFault and NaN outputs, and the other documents continue (phase 5
// carried item). A block reports its progress, and a cancellation stops the run between blocks.

using System.Collections.Concurrent;
using System.Diagnostics;
using Sharpwired.Cells;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>The CPU loop backend: documents in arrays, and runners that run blocks of documents in parallel.</summary>
public sealed class CpuLoopBackend : ILoopBackend
{
    private volatile bool disposed;

    /// <summary>Make the backend.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The block size or the thread limit is below 1.</exception>
    public CpuLoopBackend(CpuLoopOptions? options = null)
    {
        Options = options ?? new CpuLoopOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(Options.BlockSize, 1, nameof(options));
        if (Options.MaxDegreeOfParallelism is { } limit) ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1, nameof(options));
    }

    /// <summary>The options of the backend.</summary>
    public CpuLoopOptions Options { get; }

    public IDocumentBuffer Upload(ReadOnlySpan<float> cells, int documentCount, int cellsPerDocument)
    {
        ThrowIfDisposed();
        CheckSize(documentCount, cellsPerDocument);
        if (cells.Length != (long)documentCount * cellsPerDocument)
        {
            throw new ArgumentException(
                $"cells holds {cells.Length} values, and {documentCount} documents of {cellsPerDocument} cells need {(long)documentCount * cellsPerDocument}", nameof(cells));
        }
        return new CpuDocumentBuffer(this, cells.ToArray(), documentCount, cellsPerDocument);
    }

    public IDocumentBuffer Allocate(int documentCount, int cellsPerDocument, float fill)
    {
        ThrowIfDisposed();
        CheckSize(documentCount, cellsPerDocument);
        var cells = new float[(long)documentCount * cellsPerDocument];
        Array.Fill(cells, fill);
        return new CpuDocumentBuffer(this, cells, documentCount, cellsPerDocument);
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
        return owned.Cells.AsSpan(firstDocument * owned.CellsPerDocument, documentCount * owned.CellsPerDocument).ToArray();
    }

    public ILoopRunner CreateRunner(LoopProgram program)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(program);
        return new CpuLoopRunner(this, program);
    }

    /// <summary>Mark the backend as disposed. A later call of the backend or of its runners throws.</summary>
    public void Dispose() => disposed = true;

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    // The buffer as a buffer of this backend.
    internal CpuDocumentBuffer Own(IDocumentBuffer? buffer, string what)
    {
        ArgumentNullException.ThrowIfNull(buffer, what);
        if (buffer is not CpuDocumentBuffer owned || !ReferenceEquals(owned.Owner, this))
        {
            throw new ArgumentException($"{what} is a buffer of another backend", what);
        }
        owned.ThrowIfDisposed();
        return owned;
    }

    private static void CheckSize(int documentCount, int cellsPerDocument)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellsPerDocument, 1);
        if ((long)documentCount * cellsPerDocument > Array.MaxLength)
        {
            throw new ArgumentException($"{documentCount} documents of {cellsPerDocument} cells do not fit in one array");
        }
    }
}

// The documents of the CPU backend: one array, document after document.
internal sealed class CpuDocumentBuffer(CpuLoopBackend owner, float[] cells, int documentCount, int cellsPerDocument) : IDocumentBuffer
{
    private volatile bool disposed;

    public CpuLoopBackend Owner { get; } = owner;

    public float[] Cells { get; } = cells;

    public int DocumentCount { get; } = documentCount;

    public int CellsPerDocument { get; } = cellsPerDocument;

    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose() => disposed = true;
}

// The declarations of the cells of one stage, and the checks of the runner. The order of the checks and
// the messages are those of CellValidator.Validate: the range of each cell, then each exact bit, then each
// grid, then each rule. A source table whose shared cells passed WriteShared gives the first message of
// CellValidator.Validate on the full table.
internal sealed class StageCells
{
    private readonly string[] names;
    private readonly double[] minimums;
    private readonly double[] maximums;
    private readonly int[] boundCells;
    private readonly int[] boundBits;
    private readonly (int Cell, double Step)[] boundGrids;
    private readonly CellRule[] boundRules;
    private readonly int[] sharedCells;
    private readonly int[] sharedBits;
    private readonly (int Cell, double Step)[] sharedGrids;
    private readonly CellRule[] sharedRules;

    public StageCells(StagePlan plan)
    {
        var validator = plan.Stage.Function.Table.Validator;
        var cells = validator.Cells().ToArray();
        names = [.. cells.Select(cell => cell.Name)];
        minimums = [.. cells.Select(cell => cell.Minimum)];
        maximums = [.. cells.Select(cell => cell.Maximum)];
        bool IsShared(int cell) => plan.Sources[cell] is CellSource.Shared;
        var all = Enumerable.Range(0, cells.Length).ToArray();
        boundCells = [.. all.Where(cell => !IsShared(cell))];
        sharedCells = [.. all.Where(IsShared)];
        boundBits = [.. boundCells.Where(cell => cells[cell].ExactBit)];
        sharedBits = [.. sharedCells.Where(cell => cells[cell].ExactBit)];
        boundGrids = [.. boundCells.Where(cell => cells[cell].Grid is not null).Select(cell => (cell, cells[cell].Grid!.Value))];
        sharedGrids = [.. sharedCells.Where(cell => cells[cell].Grid is not null).Select(cell => (cell, cells[cell].Grid!.Value))];
        boundRules = [.. validator.Rules.Where(rule => rule.Members.Any(member => !IsShared(member.Position)))];
        sharedRules = [.. validator.Rules.Where(rule => rule.Members.All(member => IsShared(member.Position)))];
    }

    // The message of the first cell that is not a shared cell and breaks its declaration, or null.
    public string? CheckBound(float[] source) => Check(source, boundCells, boundBits, boundGrids, boundRules);

    // The message of the first shared cell that breaks its declaration, or null.
    public string? CheckShared(float[] source) => Check(source, sharedCells, sharedBits, sharedGrids, sharedRules);

    private string? Check(float[] source, int[] cells, int[] bits, (int Cell, double Step)[] grids, CellRule[] rules)
    {
        foreach (var cell in cells)
        {
            double value = source[cell];
            if (!(value >= minimums[cell] && value <= maximums[cell] && double.IsFinite(value)))
            {
                return $"{names[cell]} must be finite and within {Fp32.Format(minimums[cell])}..{Fp32.Format(maximums[cell])}, got {Fp32.Format(value)}";
            }
        }
        foreach (var cell in bits)
        {
            if (source[cell] != 0 && source[cell] != 1) return $"{names[cell]} must be exactly 0 or 1, got {Fp32.Format(source[cell])}";
        }
        foreach (var (cell, step) in grids)
        {
            var quotient = source[cell] / step;
            if (!double.IsFinite(quotient) || Math.Floor(quotient) != quotient) return $"{names[cell]} must be a multiple of {Fp32.Format(step)}, got {Fp32.Format(source[cell])}";
        }
        foreach (var rule in rules)
        {
            var mass = 0.0;
            foreach (var member in rule.Members) mass += source[member.Position];
            var valid = rule.AtMostOne ? mass <= 1 : mass == 1 || (rule.AllowAllZero && mass == 0);
            if (!valid) return $"{rule.Name} breaks its {rule.RuleName} rule with mass {Fp32.Format(mass)}";
        }
        return null;
    }
}

// The runner of one loop program on the CPU backend.
internal sealed class CpuLoopRunner : ILoopRunner
{
    private readonly CpuLoopBackend backend;
    private readonly CpuLoopOptions options;
    private readonly StageCells[] checks;
    // For each stage, the cells that are not shared cells: the runner writes only these before each step. A
    // stage of the search at 32 states has 17,494 cells, and 66 of them are not shared cells.
    private readonly int[][] boundCells;
    private readonly CompiledStage?[] compiled;
    private readonly int positionGroupCount;
    private float[]? shared;
    private volatile bool disposed;

    public CpuLoopRunner(CpuLoopBackend backend, LoopProgram program)
    {
        this.backend = backend;
        options = backend.Options;
        Program = program;
        checks = [.. program.Plans.Select(plan => new StageCells(plan))];
        boundCells = [.. program.Plans.Select(plan => Enumerable.Range(0, plan.Sources.Length).Where(cell => plan.Sources[cell] is not CellSource.Shared).ToArray())];
        compiled = [.. program.Plans.Select(plan => options.Stages == CpuStageBackend.Compiled
            ? CompiledStage.Compile(plan.Stage.Function.Table.Circuit, plan.Program)
            : null)];
        positionGroupCount = program.OutputGroups.Count(group => group.Kind == OutputGroupKind.Position);
        if (program.SharedSize == 0) shared = [];
    }

    public LoopProgram Program { get; }

    public void WriteShared(ReadOnlySpan<float> values)
    {
        ThrowIfDisposed();
        if (values.Length != Program.SharedSize)
        {
            throw new ArgumentException($"the shared table holds {values.Length} cells, and the program needs {Program.SharedSize}", nameof(values));
        }
        var copy = values.ToArray();
        for (var stage = 0; stage < checks.Length; stage++)
        {
            var plan = Program.Plans[stage];
            var source = new float[plan.Sources.Length];
            for (var cell = 0; cell < source.Length; cell++)
            {
                if (plan.Sources[cell] is CellSource.Shared sharedCell) source[cell] = copy[sharedCell.Offset];
            }
            if (checks[stage].CheckShared(source) is { } message) throw new ArgumentException($"stage {plan.Stage.Name}: {message}", nameof(values));
        }
        shared = copy;
    }

    public LoopRun Run(LoopRequest request, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var total = Stopwatch.StartNew();
        var sharedValues = shared ?? throw new InvalidOperationException($"WriteShared was not called; the program has {Program.SharedSize} shared cells");
        var work = new RunWork(this, request, sharedValues);
        var writeMs = total.Elapsed.TotalMilliseconds;

        var blockSize = options.BlockSize;
        var blockCount = (request.DocumentCount + blockSize - 1) / blockSize;
        var done = new bool[blockCount];
        var prefix = 0;
        var reported = 0;
        var gate = new object();
        var faults = new ConcurrentBag<LaneFault>();
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism ?? -1 };
        Parallel.For(
            0,
            blockCount,
            parallel,
            work.CreateWorker,
            (block, _, worker) =>
            {
                if (cancel.IsCancellationRequested) return worker;
                var end = Math.Min(request.DocumentCount, (block + 1) * blockSize);
                for (var document = block * blockSize; document < end; document++)
                {
                    if (work.RunDocument(worker, document) is { } message) faults.Add(new LaneFault(document, message));
                }
                lock (gate)
                {
                    done[block] = true;
                    while (prefix < blockCount && done[prefix]) prefix++;
                    var documentsDone = Math.Min(prefix * blockSize, request.DocumentCount);
                    if (documentsDone > reported)
                    {
                        reported = documentsDone;
                        progress?.Report(documentsDone);
                    }
                }
                return worker;
            },
            _ => { });
        var waitMs = total.Elapsed.TotalMilliseconds - writeMs;

        var documentsDoneTotal = Math.Min(prefix * blockSize, request.DocumentCount);
        for (var document = documentsDoneTotal; document < request.DocumentCount; document++) work.MarkInvalid(document);
        var run = new LoopRun(
            [.. work.DocumentValues],
            [.. work.WindowValues],
            [.. faults.Where(fault => fault.Document < documentsDoneTotal).OrderBy(fault => fault.Document)],
            documentsDoneTotal,
            new LoopTimes(writeMs, 0, waitMs, 0, 0));
        var readMs = total.Elapsed.TotalMilliseconds - writeMs - waitMs;
        return run with { Times = new LoopTimes(writeMs, 0, waitMs, readMs, total.Elapsed.TotalMilliseconds) };
    }

    public void Dispose() => disposed = true;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        backend.ThrowIfDisposed();
    }

    // The registers, the fields and the stage evaluators of one worker thread.
    private sealed class Worker(float[] registers, float[][] fields, StageWorker[] stages)
    {
        public float[] Registers { get; } = registers;

        public float[][] Fields { get; } = fields;

        public StageWorker[] Stages { get; } = stages;
    }

    // The evaluator of one stage on one worker. Source holds the source table, with the shared cells
    // written one time. Evaluate writes the value of each output leaf into Outputs.
    private sealed class StageWorker
    {
        private readonly CompiledStage? compiled;
        private readonly TensorEvaluator? tensor;
        private readonly int[] outputRoots;

        public StageWorker(StagePlan plan, CompiledStage? compiledStage, float[] sharedValues)
        {
            outputRoots = [.. plan.OutputRoots];
            Outputs = new float[outputRoots.Length];
            if (compiledStage is not null)
            {
                compiled = compiledStage.CreateCopy();
                Source = compiled.Values;
            }
            else
            {
                tensor = plan.Stage.Function.Table.Circuit.CreateTensorProgramEvaluator(plan.Program);
                Source = new float[plan.Sources.Length];
            }
            for (var cell = 0; cell < plan.Sources.Length; cell++)
            {
                if (plan.Sources[cell] is CellSource.Shared sharedCell) Source[cell] = sharedValues[sharedCell.Offset];
            }
        }

        public float[] Source { get; }

        public float[] Outputs { get; }

        // Evaluate the stage. Returns null, or the message of the range check of TensorEvaluator, which checks
        // the source table against the ranges of the circuit. The runner gives that message as a LaneFault.
        public string? Evaluate()
        {
            if (compiled is not null)
            {
                compiled.Run();
                var positions = compiled.RootPositions;
                for (var output = 0; output < Outputs.Length; output++)
                {
                    var position = positions[outputRoots[output]];
                    Outputs[output] = position >= 0 ? Source[position] : float.NaN;
                }
                return null;
            }
            float[] roots;
            try
            {
                roots = tensor!.Evaluate(Source.AsSpan(0, tensor.Program.SourceSize));
            }
            catch (ArgumentException error)
            {
                return error.Message;
            }
            for (var output = 0; output < Outputs.Length; output++) Outputs[output] = roots[outputRoots[output]];
            return null;
        }
    }

    // The checked request of one run, its output arrays, and the loop over the stages of one document.
    private sealed class RunWork
    {
        private readonly CpuLoopRunner runner;
        private readonly LoopProgram program;
        private readonly float[] sharedValues;
        private readonly float[][] inputs;
        private readonly int[] inputCells;
        private readonly float[]?[] targets;
        private readonly int windowFirst;
        private readonly int windowCount;
        private readonly int documentCount;
        // The start of each group in DocumentValues or in WindowValues, by group index.
        private readonly int[] groupStart;
        // The index of each position group among the position groups, by group index.
        private readonly int[] positionIndex;

        public RunWork(CpuLoopRunner runner, LoopRequest request, float[] sharedValues)
        {
            this.runner = runner;
            program = runner.Program;
            this.sharedValues = sharedValues;
            var backend = runner.backend;
            ArgumentNullException.ThrowIfNull(request.Inputs, nameof(request));
            if (request.Inputs.Count != program.Buffers.Length)
            {
                throw new ArgumentException($"the request has {request.Inputs.Count} inputs, and the program has {program.Buffers.Length} buffers", nameof(request));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(request.DocumentCount, nameof(request));
            documentCount = request.DocumentCount;
            inputs = new float[program.Buffers.Length][];
            inputCells = new int[program.Buffers.Length];
            for (var index = 0; index < inputs.Length; index++)
            {
                var input = backend.Own(request.Inputs[index], $"input {index}");
                var buffer = program.Buffers[index];
                if (input.CellsPerDocument != buffer.CellsPerDocument)
                {
                    throw new ArgumentException(
                        $"input {index} holds {input.CellsPerDocument} cells for each document, and buffer {buffer.Name} of the program needs {buffer.CellsPerDocument}", nameof(request));
                }
                if (input.DocumentCount < documentCount)
                {
                    throw new ArgumentException($"input {index} holds {input.DocumentCount} documents, and the request runs {documentCount}", nameof(request));
                }
                inputs[index] = input.Cells;
                inputCells[index] = input.CellsPerDocument;
            }
            if (request.WindowFirst < 0 || request.WindowCount < 0 || (long)request.WindowFirst + request.WindowCount > documentCount)
            {
                throw new ArgumentException(
                    $"the window {request.WindowFirst}..{(long)request.WindowFirst + request.WindowCount - 1} must lie inside the {documentCount} documents", nameof(request));
            }
            windowFirst = request.WindowFirst;
            windowCount = request.WindowCount;
            var positionTargets = request.PositionTargets ?? [];
            if (positionTargets.Count != 0 && positionTargets.Count != runner.positionGroupCount)
            {
                throw new ArgumentException(
                    $"the request has {positionTargets.Count} position targets, and the program has {runner.positionGroupCount} position groups", nameof(request));
            }
            targets = new float[]?[runner.positionGroupCount];
            for (var index = 0; index < positionTargets.Count; index++)
            {
                if (positionTargets[index] is not { } target) continue;
                var owned = backend.Own(target, $"position target {index}");
                if (owned.CellsPerDocument != program.Positions || owned.DocumentCount < documentCount || request.Inputs.Contains(target))
                {
                    throw new ArgumentException(
                        $"position target {index} must hold {documentCount} documents of {program.Positions} cells and must not be an input", nameof(request));
                }
                var earlier = Array.IndexOf(targets, owned.Cells);
                if (earlier >= 0) throw new ArgumentException($"position target {index} is also position target {earlier}", nameof(request));
                targets[index] = owned.Cells;
            }

            groupStart = new int[program.OutputGroups.Length];
            positionIndex = new int[program.OutputGroups.Length];
            var (documentSize, windowSize, positionGroups) = (0, 0, 0);
            for (var group = 0; group < groupStart.Length; group++)
            {
                var outputGroup = program.OutputGroups[group];
                if (outputGroup.Kind == OutputGroupKind.Document)
                {
                    groupStart[group] = documentSize;
                    documentSize += outputGroup.ValuesPerDocument * documentCount;
                }
                else
                {
                    positionIndex[group] = positionGroups++;
                    groupStart[group] = windowSize;
                    windowSize += program.Positions * windowCount;
                }
            }
            DocumentValues = new float[documentSize];
            WindowValues = new float[windowSize];
        }

        public float[] DocumentValues { get; }

        public float[] WindowValues { get; }

        public Worker CreateWorker() => new(
            new float[program.Registers.Length],
            [.. program.Fields.Select(field => new float[program.Positions * field.Width])],
            [.. program.Plans.Select((plan, stage) => new StageWorker(plan, runner.compiled[stage], sharedValues))]);

        // Run the stages over one document. Returns the message of a failed check, or null.
        public string? RunDocument(Worker worker, int document)
        {
            Fill(document, useFill: true);
            var message = RunStages(worker, document);
            if (message is not null) MarkInvalid(document);
            return message;
        }

        // Write NaN into each output of the document.
        public void MarkInvalid(int document) => Fill(document, useFill: false);

        private string? RunStages(Worker worker, int document)
        {
            var positions = program.Positions;
            var limit = positions;
            if (program.LengthCell is { } lengthCell && runner.options.StopAtLength)
            {
                var length = inputs[lengthCell.Buffer][((long)document * inputCells[lengthCell.Buffer]) + lengthCell.Column];
                if (!(length >= 0 && MathF.Floor(length) == length))
                {
                    return $"the length cell holds {Fp32.Format(length)}, which is not a whole number of 0 or more";
                }
                limit = length >= positions ? positions : (int)length;
            }
            var registers = worker.Registers;
            for (var register = 0; register < registers.Length; register++) registers[register] = program.Registers[register].Initial;
            var inWindow = document >= windowFirst && document < windowFirst + windowCount;
            for (var stage = 0; stage < program.Plans.Length; stage++)
            {
                var plan = program.Plans[stage];
                var stageWorker = worker.Stages[stage];
                var source = stageWorker.Source;
                var count = plan.Stage.Repeat == StageRepeat.Once ? 1 : limit;
                for (var step = 0; step < count; step++)
                {
                    var position = plan.Stage.Repeat == StageRepeat.Backward ? limit - 1 - step : step;
                    foreach (var cell in runner.boundCells[stage])
                    {
                        switch (plan.Sources[cell])
                        {
                            case CellSource.Item item:
                                source[cell] = inputs[item.Buffer][((long)document * inputCells[item.Buffer]) + position];
                                break;
                            case CellSource.DocumentCell fixedCell:
                                source[cell] = inputs[fixedCell.Buffer][((long)document * inputCells[fixedCell.Buffer]) + fixedCell.Column];
                                break;
                            case CellSource.Register register:
                                source[cell] = registers[register.Index];
                                break;
                            case CellSource.Field field:
                                source[cell] = worker.Fields[field.Index][(position * program.Fields[field.Index].Width) + field.Component];
                                break;
                        }
                    }
                    if (runner.checks[stage].CheckBound(source) is { } fault) return LoopProgram.FaultPrefix(plan.Stage, position) + fault;
                    if (stageWorker.Evaluate() is { } evaluationFault) return LoopProgram.FaultPrefix(plan.Stage, position) + evaluationFault;
                    var outputs = stageWorker.Outputs;
                    for (var output = 0; output < outputs.Length; output++)
                    {
                        var value = outputs[output];
                        switch (plan.Writes[output])
                        {
                            case StageWrite.Register register:
                                registers[register.Index] = value;
                                break;
                            case StageWrite.Field field:
                                if (program.BitsFault(field, value) is { } bits) return LoopProgram.FaultPrefix(plan.Stage, position) + bits;
                                worker.Fields[field.Index][(position * program.Fields[field.Index].Width) + field.Component] = value;
                                break;
                            case StageWrite.Document target:
                                DocumentValues[groupStart[target.Group] + (document * program.OutputGroups[target.Group].ValuesPerDocument) + target.Column] = value;
                                break;
                            case StageWrite.Position target:
                                if (inWindow) WindowValues[groupStart[target.Group] + ((document - windowFirst) * positions) + position] = value;
                                if (targets[positionIndex[target.Group]] is { } targetCells) targetCells[((long)document * positions) + position] = value;
                                break;
                        }
                    }
                }
            }
            return null;
        }

        // Write the fill value of each group, or NaN, into each output of the document.
        private void Fill(int document, bool useFill)
        {
            var positions = program.Positions;
            var inWindow = document >= windowFirst && document < windowFirst + windowCount;
            var positionGroup = 0;
            for (var group = 0; group < groupStart.Length; group++)
            {
                var outputGroup = program.OutputGroups[group];
                var value = useFill ? outputGroup.Fill : float.NaN;
                if (outputGroup.Kind == OutputGroupKind.Document)
                {
                    DocumentValues.AsSpan(groupStart[group] + (document * outputGroup.ValuesPerDocument), outputGroup.ValuesPerDocument).Fill(value);
                    continue;
                }
                if (inWindow) WindowValues.AsSpan(groupStart[group] + ((document - windowFirst) * positions), positions).Fill(value);
                if (targets[positionGroup] is { } targetCells) targetCells.AsSpan(document * positions, positions).Fill(value);
                positionGroup++;
            }
        }
    }
}
