// No Handwired source. Handwired has no translator. The entry points of the plan, section "Public
// API", Sharpwired.IL: TranslateFunction and TranslateStep, with TranslatedFunction and
// TranslatedStep. A translation checks the method, walks the cells (CellLayout.cs), decodes the IL,
// builds the CFG and the SSA form, lowers the operations (OperationLowering.cs), emits the output
// leaves at the return in walk order, pairs each state cell with its next value, and finishes the
// source table. The translator caches nothing between calls.

using System.Collections.Immutable;
using System.Reflection;
using Sharpwired.Annotations;
using Sharpwired.Cells;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>A wire with the path of its cell or output leaf, for example "down.x" or "trackLeft".</summary>
public readonly record struct NamedWire(string Path, Wire Wire);

/// <summary>The translator of [Handwired] methods into programs.</summary>
public static class HandwiredTranslator
{
    /// <summary>
    /// Translates a static [Handwired] method. Each parameter leaf is an input or event cell, and each
    /// return leaf is an output. Throws <see cref="TranslationException"/> for a refused construct and
    /// <see cref="ArgumentException"/> for a method that is not a static, non-generic [Handwired] method.
    /// </summary>
    public static TranslatedFunction TranslateFunction(MethodInfo method)
    {
        CheckMethod(method);
        var layout = CellLayout.Walk(method, [.. method.GetParameters().Select(_ => ParameterRole.Function)]);
        var (table, circuit) = layout.Declare();
        var (result, location, emitter) = Lower(method, layout, circuit);
        var leaves = new List<(string Path, ScalarValue Value)>();
        var shape = CellLayout.Output(method.ReturnType, result, "", top: true, location, leaves);
        var outputs = leaves.Select(leaf => new NamedWire(leaf.Path, emitter.Emit(leaf.Value))).ToImmutableArray();
        var finished = table.Finish();
        var inputs = layout.Leaves.Select(leaf => new NamedWire(leaf.Path, circuit.Source(leaf.Cell))).ToImmutableArray();
        return new TranslatedFunction(method, layout, finished, inputs, outputs, shape);
    }

    /// <summary>
    /// Translates <c>static (TState Next, TOutput Output) Step(TState state, TInput input)</c>. The
    /// leaves of state are state cells; the leaves of input are input and event cells. Next gives the
    /// next value of each state cell, and the leaves of Output are the outputs.
    /// </summary>
    public static TranslatedStep TranslateStep(MethodInfo method)
    {
        CheckMethod(method);
        var parameters = method.GetParameters();
        var returnType = method.ReturnType;
        if (parameters.Length != 2 || !CellLayout.IsValueTuple(returnType) || returnType.GetGenericArguments() is not [var nextType, var outputType]
            || nextType != parameters[0].ParameterType)
        {
            throw new ArgumentException("TranslateStep needs two parameters and a (Next, Output) tuple");
        }

        var layout = CellLayout.Walk(method, [ParameterRole.State, ParameterRole.StepInput]);
        var (table, circuit) = layout.Declare();
        var (result, location, emitter) = Lower(method, layout, circuit);
        var tuple = (AggregateValue)result;
        var nextLeaves = new List<(string Path, ScalarValue Value)>();
        var nextShape = CellLayout.Output(nextType, tuple.Fields["Item1"], "", top: false, location, nextLeaves);
        var outputLeaves = new List<(string Path, ScalarValue Value)>();
        var outputShape = CellLayout.Output(outputType, tuple.Fields["Item2"], "", top: false, location, outputLeaves);
        var nextWires = nextLeaves.Select(leaf => emitter.Emit(leaf.Value)).ToArray();
        var outputs = outputLeaves.Select(leaf => new NamedWire(leaf.Path, emitter.Emit(leaf.Value))).ToImmutableArray();
        var stateLeaves = layout.Leaves.Where(leaf => leaf.Spec.Policy == CellPolicy.State).ToArray();
        if (stateLeaves.Length != nextWires.Length)
        {
            throw new InvalidOperationException($"the state parameter has {stateLeaves.Length} cells, and Next has {nextWires.Length} leaves");
        }

        for (var index = 0; index < stateLeaves.Length; index++)
        {
            table.Next(circuit.Source(stateLeaves[index].Cell), nextWires[index]);
        }

        var finished = table.Finish();
        var cells = layout.Leaves.Select(leaf => new NamedWire(leaf.Path, circuit.Source(leaf.Cell))).ToImmutableArray();
        return new TranslatedStep(method, layout, finished, cells, outputs, nextShape, outputShape);
    }

    // The errors of the method itself are plain ArgumentException values (plan, "Error message rules", rule 8).
    private static void CheckMethod(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.GetCustomAttribute<HandwiredAttribute>() is null)
        {
            throw new ArgumentException($"the method {IlDecoder.Describe(method)} has no [Handwired] attribute");
        }

        if (!method.IsStatic)
        {
            throw new ArgumentException("a [Handwired] method must be static");
        }

        if (method.IsGenericMethod || method.ContainsGenericParameters)
        {
            throw new ArgumentException("a [Handwired] method must not be generic");
        }

        if (method.GetCustomAttribute<MaxIterationsAttribute>() is { Count: < 1 } iterations)
        {
            throw new TranslationException(CellLayout.MemberLocation(method), 0,
                $"[MaxIterations({iterations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)})]",
                "the maximum number of iterations must be at least 1");
        }
    }

    private static (Value Result, SourceLocation Location, Emitter Emitter) Lower(MethodInfo method, CellLayout layout, Circuit circuit)
    {
        var decoded = IlDecoder.Decode(method);
        var ssa = SsaBuilder.Build(ControlFlowGraph.Build(decoded));
        var emitter = new Emitter(circuit);
        var arguments = layout.Parameters.Select(parameter => CellLayout.ValueOf(parameter, circuit)).ToArray();
        var (result, offset) = OperationLowering.Lower(decoded, ssa, emitter, arguments);
        return (result, decoded.Locations.Locate(offset), emitter);
    }
}

/// <summary>The program of a [Handwired] method: input and event cells, and one output wire for each return leaf.</summary>
public sealed class TranslatedFunction
{
    private readonly MethodInfo method;
    private readonly CellLayout layout;
    private readonly OutputShape shape;

    internal TranslatedFunction(MethodInfo method, CellLayout layout, FinishedTable table, ImmutableArray<NamedWire> inputs, ImmutableArray<NamedWire> outputs, OutputShape shape)
    {
        this.method = method;
        this.layout = layout;
        this.shape = shape;
        Table = table;
        Inputs = inputs;
        Outputs = outputs;
    }

    public FinishedTable Table { get; }

    /// <summary>The cells in walk order.</summary>
    public ImmutableArray<NamedWire> Inputs { get; }

    /// <summary>The output leaves of the return value in walk order. A return value of one leaf has the path "".</summary>
    public ImmutableArray<NamedWire> Outputs { get; }

    /// <summary>The overflow bits of the loops with a maximum. Straight-line code has none.</summary>
    public ImmutableArray<Wire> LoopOverflowBits { get; } = [];

    /// <summary>A program evaluator whose outputs are the output wires in the order of Outputs.</summary>
    public ProgramEvaluator<IReadOnlyList<Wire>> CreateEvaluator() =>
        ProgramEvaluator.Create<IReadOnlyList<Wire>>(Table, Outputs.Select(output => output.Wire).ToArray());

    /// <summary>Runs the plain method by reflection on the leaves of the source table, and returns the output leaves.</summary>
    public float[] EvaluatePlain(ReadOnlySpan<float> source)
    {
        Table.Validator.Validate(source);
        var leaves = new List<float>(Outputs.Length);
        PlainInvoker.Read(shape, PlainInvoker.Invoke(method, layout, source), leaves);
        return [.. leaves];
    }
}

/// <summary>The program of a step method: state, input and event cells, and the outputs.</summary>
public sealed class TranslatedStep
{
    private readonly MethodInfo method;
    private readonly CellLayout layout;
    private readonly OutputShape nextShape;
    private readonly OutputShape outputShape;

    internal TranslatedStep(
        MethodInfo method, CellLayout layout, FinishedTable table, ImmutableArray<NamedWire> cells, ImmutableArray<NamedWire> outputs,
        OutputShape nextShape, OutputShape outputShape)
    {
        this.method = method;
        this.layout = layout;
        this.nextShape = nextShape;
        this.outputShape = outputShape;
        Table = table;
        Cells = cells;
        Outputs = outputs;
    }

    public FinishedTable Table { get; }

    /// <summary>The cells in walk order: the state cells, then the input and event cells.</summary>
    public ImmutableArray<NamedWire> Cells { get; }

    /// <summary>The output leaves of Output in walk order.</summary>
    public ImmutableArray<NamedWire> Outputs { get; }

    /// <summary>The wire of the cell with the path, for example "down.x".</summary>
    public Wire Cell(string path) => Find(Cells, path, "cell");

    /// <summary>The wire of the output with the path, for example "trackLeft".</summary>
    public Wire Output(string path) => Find(Outputs, path, "output");

    /// <summary>A program evaluator whose outputs are the output wires in the order of Outputs.</summary>
    public ProgramEvaluator<IReadOnlyList<Wire>> CreateEvaluator() =>
        ProgramEvaluator.Create<IReadOnlyList<Wire>>(Table, Outputs.Select(output => output.Wire).ToArray());

    /// <summary>
    /// An evaluator that runs the plain method: the next values of the state cells, 0 for the events,
    /// and the inputs unchanged.
    /// </summary>
    public ISourceTableEvaluator CreatePlainEvaluator() => new PlainStepEvaluator(this);

    private static Wire Find(ImmutableArray<NamedWire> wires, string path, string kind)
    {
        foreach (var wire in wires)
        {
            if (wire.Path == path)
            {
                return wire.Wire;
            }
        }

        throw new ArgumentException($"the step has no {kind} with the path {path}");
    }

    private sealed class PlainStepEvaluator(TranslatedStep step) : ISourceTableEvaluator
    {
        public ISourceTableEvaluation Evaluate(ReadOnlySpan<float> source)
        {
            step.Table.Validator.Validate(source);
            var result = PlainInvoker.Invoke(step.method, step.layout, source)!;
            var tuple = result.GetType();
            var next = new List<float>();
            PlainInvoker.Read(step.nextShape, tuple.GetField("Item1")!.GetValue(result), next);
            var outputs = new List<float>();
            PlainInvoker.Read(step.outputShape, tuple.GetField("Item2")!.GetValue(result), outputs);
            var nextValues = new float[step.Cells.Length];
            var nextIndex = 0;
            foreach (var leaf in step.layout.Leaves)
            {
                nextValues[leaf.Cell] = leaf.Spec.Policy switch
                {
                    CellPolicy.State => next[nextIndex++],
                    CellPolicy.Event => 0,
                    _ => source[leaf.Cell],
                };
            }

            return new PlainStepEvaluation(nextValues, step.Outputs, [.. outputs]);
        }
    }

    private sealed class PlainStepEvaluation(float[] nextValues, ImmutableArray<NamedWire> outputs, float[] values) : ISourceTableEvaluation
    {
        public float[] NextValues { get; } = nextValues;

        public float Read(Wire output)
        {
            for (var index = 0; index < outputs.Length; index++)
            {
                if (outputs[index].Wire == output)
                {
                    return values[index];
                }
            }

            throw new ArgumentException($"wire {output} is not an output");
        }
    }
}
