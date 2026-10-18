// No Handwired source. Handwired has no translator. Loop form 3 of the plan, section "Decisions",
// question 5, and docs/design.md, Layer 2, step 4: "the loop body becomes a separate step program, a
// recurrent net. The loop state is the step's state cells, and the loop condition is a done output
// bit. The runner evaluates the step until done is 1 or until a configured limit."
//
// TranslateRecurrent splits the method into three programs (RecurrentLowering.cs gives each part):
// - The prefix reads the parameters. Its outputs are the initial loop state, then the invariants.
// - The step has one state cell for each leaf of the loop state and one input cell for each leaf of an
//   invariant. The cells are in the order of the prefix outputs. Its next values are Select(c, state,
//   back), and its output is done = NOT c. The step is a plain Circuit and a FinishedTable, so a GPU
//   runner (phase 5, R5.4) can read the same table.
// - The suffix has the same cells as the step, as input cells. Its outputs are the return leaves.
//
// The range of a state cell is a least fixed point. It is the range of the initial value, joined with
// the range of the back-edge value, with the state ranges refined by c. The translator tries at most 8
// rounds. A cell whose range still changes in round 8 gets a wide range: -2^23..2^23 with grid 1 for
// an int, -float.MaxValue..float.MaxValue for a float. The int range is 2^23, so that a body step such
// as count + 1 stays exact within 2^24. After the widening, more rounds run until a round changes no
// range. In these rounds, a cell that is not wide and whose range changes also gets the wide range.
// The round that changes no range is the fixed-point check: the next-state range of each cell that is
// not wide lies inside its cell range. The cell validator checks a next value of a wide cell.
// RecurrentRunner is the CPU runner.

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Sharpwired.Cells;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>
/// The step program of a loop with no maximum. State holds one cell for each leaf of the loop
/// variables. Invariants holds one input cell for each leaf of an invariant. An invariant is a value
/// of the code before the loop that the loop or the code after it reads. Done is 1 when the loop
/// ended. A lane that is done keeps its state.
/// </summary>
public sealed record RecurrentStep(FinishedTable Table, ImmutableArray<NamedWire> State, ImmutableArray<NamedWire> Invariants, Wire Done);

/// <summary>
/// The three programs of a method with one loop with no maximum. The outputs of Prefix are the source
/// table of Step. The cells of Suffix are the cells of Step, and its outputs are the return leaves.
/// </summary>
public sealed class RecurrentFunction
{
    private readonly MethodInfo method;
    private readonly CellLayout layout;
    private readonly OutputShape shape;

    internal RecurrentFunction(MethodInfo method, CellLayout layout, OutputShape shape, TranslatedFunction prefix, RecurrentStep step, TranslatedFunction suffix)
    {
        this.method = method;
        this.layout = layout;
        this.shape = shape;
        Prefix = prefix;
        Step = step;
        Suffix = suffix;
    }

    /// <summary>The program from the parameters to the initial loop state and the invariants.</summary>
    public TranslatedFunction Prefix { get; }

    /// <summary>The step program of the loop.</summary>
    public RecurrentStep Step { get; }

    /// <summary>The program from the final loop state and the invariants to the return leaves.</summary>
    public TranslatedFunction Suffix { get; }

    /// <summary>Runs the plain method by reflection on the argument table of Prefix, and returns the return leaves.</summary>
    public float[] EvaluatePlain(ReadOnlySpan<float> arguments)
    {
        Prefix.Table.Validator.Validate(arguments);
        var leaves = new List<float>(Suffix.Outputs.Length);
        PlainInvoker.Read(shape, PlainInvoker.Invoke(method, layout, arguments), leaves);
        return [.. leaves];
    }
}

/// <summary>
/// The result of <see cref="RecurrentRunner.Run"/>: the return leaves, the count of step evaluations
/// with done 0, and Completed. Completed is false when the step limit stopped the loop.
/// </summary>
public sealed record RecurrentResult(ImmutableArray<float> Outputs, int Steps, bool Completed);

/// <summary>
/// The CPU runner of a step program. Phase 5 adds a GPU runner of the same step table (R5.4). A runner
/// reuses its evaluators and their result arrays, so one runner must not run on two threads at the
/// same time.
/// </summary>
public sealed class RecurrentRunner
{
    private readonly TranslatedEvaluator prefix;
    private readonly ProgramEvaluator<IReadOnlyList<Wire>> step;
    private readonly TranslatedEvaluator suffix;

    public RecurrentRunner(RecurrentFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        prefix = function.Prefix.CreateEvaluator();
        step = ProgramEvaluator.Create<IReadOnlyList<Wire>>(function.Step.Table, [function.Step.Done]);
        suffix = function.Suffix.CreateEvaluator();
    }

    /// <summary>
    /// Evaluates the prefix, then the step until done is 1 or until stepLimit evaluations, then the
    /// suffix on the last state. A loop of n iterations needs n + 1 evaluations: the last one gives
    /// done = 1. When the limit stops the loop, Completed is false.
    /// </summary>
    public RecurrentResult Run(ReadOnlySpan<float> arguments, int stepLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stepLimit);
        float[] source = [.. prefix.Evaluate(arguments).Outputs];
        var steps = 0;
        var completed = false;
        for (var evaluation = 0; evaluation < stepLimit; evaluation++)
        {
            var result = step.Evaluate(source);
            if (result.Outputs[0] != 0)
            {
                completed = true;
                break;
            }

            result.NextValues.CopyTo(source, 0);
            steps++;
        }

        return new RecurrentResult([.. suffix.Evaluate(source).Outputs], steps, completed);
    }
}

/// <summary>The split of a method with a loop of form 3 into the prefix, the step and the suffix.</summary>
internal sealed class RecurrentTranslator
{
    private const int Rounds = 8;

    // 2^23: a body step of an int with a small constant stays exact within 2^24. With 2^24, the body
    // form count + 1 passes 2^24, and the exactness rule of int forms refuses it.
    private const double WideInt = 8_388_608;

    private readonly MethodInfo method;
    private readonly CellLayout layout;
    private readonly DecodedMethod decoded;
    private readonly SsaMethod ssa;
    private readonly int header;

    // The option of the translation, for each part (TranslationOptions.RecognizePatterns).
    private readonly bool recognizePatterns;
    private readonly ImmutableArray<SsaValue> headerPhis;
    private RecurrentContext shape = null!;
    private CellPlan[] statePlans = [];
    private CellPlan[] invariantPlans = [];

    private RecurrentTranslator(MethodInfo method, bool recognizePatterns)
    {
        this.method = method;
        this.recognizePatterns = recognizePatterns;
        layout = CellLayout.Walk(method, [.. method.GetParameters().Select(_ => ParameterRole.Function)]);
        decoded = IlDecoder.Decode(method);
        ssa = SsaBuilder.Build(ControlFlowGraph.Build(decoded));
        header = Probe();
        headerPhis = ssa.Blocks[header].Phis;
    }

    private SourceLocation Location => decoded.Locations.Locate(shape.TestOffset);

    public static RecurrentFunction Translate(MethodInfo method, TranslationOptions options)
    {
        var translator = new RecurrentTranslator(method, options.RecognizePatterns);
        var prefix = translator.Prefix();
        translator.FixStateRanges();
        var step = translator.Step();
        var (suffix, returnShape) = translator.Suffix();
        return new RecurrentFunction(method, translator.layout, returnShape, prefix, step, suffix);
    }

    // The normal lowering until the first loop with no maximum.
    private int Probe()
    {
        var (_, circuit) = layout.Declare();
        try
        {
            Lower(new Emitter(circuit, recognizePatterns), Parameters(circuit), new RecurrentContext(RecurrentPart.Probe, -1));
        }
        catch (RecurrentLoopFoundException found)
        {
            return found.Header;
        }

        throw new ArgumentException($"the method {IlDecoder.Describe(method)} has no loop with no maximum; use TranslateFunction");
    }

    private TranslatedFunction Prefix()
    {
        var (table, circuit) = layout.Declare();
        var emitter = new Emitter(circuit, recognizePatterns);
        shape = new RecurrentContext(RecurrentPart.Prefix, header);
        Lower(emitter, Parameters(circuit), shape);
        var stateLeaves = shape.InitialState.SelectMany((value, index) => Leaves(value, StateName(headerPhis[index]))).ToArray();
        var invariantLeaves = shape.Invariants.SelectMany(pair => Leaves(pair.Lowered, InvariantName(pair.Value))).Where(leaf => leaf.Value is not ConstantValue).ToArray();
        var outputs = stateLeaves.Concat(invariantLeaves).Select(leaf => new NamedWire(leaf.Path, emitter.Emit(leaf.Value))).ToImmutableArray();
        CheckRanges(emitter);
        statePlans = [.. stateLeaves.Select((leaf, index) => Plan(leaf, circuit, outputs[index].Wire, state: true))];
        invariantPlans = [.. invariantLeaves.Select((leaf, index) => Plan(leaf, circuit, outputs[stateLeaves.Length + index].Wire, state: false))];
        var finished = table.Finish();
        var inputs = layout.Leaves.Select(leaf => new NamedWire(leaf.Path, circuit.Source(leaf.Cell))).ToImmutableArray();
        return new TranslatedFunction(null, null, finished, inputs, outputs, null, [.. emitter.Overflows]);
    }

    // The least fixed point of the state ranges in at most 8 rounds. Else each cell whose range changed
    // in round 8 gets the wide range. Then more rounds run (review of task 6, round 1). In these
    // rounds, a cell that is not wide and whose range changes also gets the wide range. Each such round
    // widens at least one cell, so the rounds end after one round for each cell. The round that changes
    // no range is the fixed-point check.
    private void FixStateRanges()
    {
        var changed = new bool[statePlans.Length];
        for (var round = 0; round < Rounds; round++)
        {
            var next = Round(statePlans);
            changed = [.. next.Select((plan, index) => plan.Range != statePlans[index].Range)];
            statePlans = next;
            if (!changed.Any(item => item))
            {
                return;
            }
        }

        var wide = changed;
        statePlans = [.. statePlans.Select((plan, index) => wide[index] ? Widen(plan) : plan)];
        for (var round = 0; round <= statePlans.Length; round++)
        {
            var next = Round(statePlans);
            var grown = next.Select((plan, index) => !wide[index] && plan.Range != statePlans[index].Range).ToArray();
            if (!grown.Any(item => item))
            {
                return;
            }

            wide = [.. wide.Zip(grown, (before, now) => before || now)];
            statePlans = [.. statePlans.Select((plan, index) => grown[index] ? Widen(plan) : plan)];
        }

        // Each round before this point widened at least one cell, and the last round found no cell to
        // widen. So this point cannot run (final fix wave, item K).
        throw new InvalidOperationException("the state ranges of the step program do not reach a fixed point after each cell is wide");
    }

    private static CellPlan Widen(CellPlan plan) => plan with
    {
        Range = plan.Kind == ScalarKind.Int ? new NumericRange(-WideInt, WideInt) : new NumericRange(-float.MaxValue, float.MaxValue),
    };

    // One round: the state ranges joined with the ranges of the back-edge values, on the state ranges
    // refined by the continue bit.
    private CellPlan[] Round(CellPlan[] plans)
    {
        var probe = Lowered(plans, RecurrentPart.Step);
        if (probe.Context.NeverContinues)
        {
            return plans;
        }

        var refined = plans.Select((plan, index) => probe.Context.Intervals.GetValueOrDefault(probe.State[index]) is { } interval
            ? plan with { Range = new NumericRange(Math.Max(interval.Low, plan.Range.Minimum), Math.Min(interval.High, plan.Range.Maximum)) }
            : plan).ToArray();
        var body = Lowered(refined, RecurrentPart.Step);
        if (body.Context.NeverContinues)
        {
            return plans;
        }

        var backLeaves = BackLeaves(body.Context.Back);
        return [.. plans.Select((plan, index) =>
        {
            if (plan.Kind == ScalarKind.Bit)
            {
                return plan;
            }

            var range = body.Circuit.Ranges[body.Emitter.Emit(backLeaves[index]).Position];
            return plan with { Range = new NumericRange(Math.Min(plan.Range.Minimum, range.Minimum), Math.Max(plan.Range.Maximum, range.Maximum)) };
        })];
    }

    private RecurrentStep Step()
    {
        var lowered = Lowered(statePlans, RecurrentPart.Step);
        var emitter = lowered.Emitter;
        var nextLeaves = BackLeaves(lowered.Context.Next);
        var nextWires = nextLeaves.Select(emitter.Emit).ToArray();
        var done = emitter.Emit(lowered.Context.Done!);
        CheckRanges(emitter);
        if (emitter.Overflows.FirstOrDefault() is { } overflow)
        {
            // A step program has no evaluation that checks the bit.
            throw new TranslationException(overflow.Location, 0, "a loop with an overflow bit in a step program",
                "a step program does not check a loop overflow bit; declare [Range] on the loop count");
        }

        for (var index = 0; index < nextWires.Length; index++)
        {
            lowered.Table.Next(lowered.State[index], nextWires[index]);
        }

        var finished = lowered.Table.Finish();
        return new RecurrentStep(finished, Named(statePlans, lowered.State), Named(invariantPlans, lowered.Invariants), done);
    }

    private (TranslatedFunction Suffix, OutputShape Shape) Suffix()
    {
        var lowered = Lowered(statePlans, RecurrentPart.Suffix);
        var emitter = lowered.Emitter;
        var leaves = new List<(string Path, ScalarValue Value)>();
        var returnShape = CellLayout.Output(method.ReturnType, lowered.Result!, "", top: true, Location, leaves);
        var outputs = leaves.Select(leaf => new NamedWire(leaf.Path, emitter.Emit(leaf.Value))).ToImmutableArray();
        CheckRanges(emitter);
        var finished = lowered.Table.Finish();
        var inputs = Named(statePlans, lowered.State).AddRange(Named(invariantPlans, lowered.Invariants));
        return (new TranslatedFunction(null, null, finished, inputs, outputs, null, [.. emitter.Overflows]), returnShape);
    }

    // The step or the suffix on the state ranges: the cells, the circuit, and the lowering of the part.
    private LoweredPart Lowered(CellPlan[] plans, RecurrentPart part)
    {
        var table = new SourceTable();
        var state = plans.Select(plan => table.Declare(plan.Path, Spec(plan, part == RecurrentPart.Step))).ToArray();
        var invariants = invariantPlans.Select(plan => table.Declare(plan.Path, Spec(plan, state: false))).ToArray();
        var circuit = table.CreateCircuit();
        var stateIndex = 0;
        var invariantIndex = 0;
        var context = new RecurrentContext(part, header)
        {
            State = [.. shape.InitialState.Select(value => Rebuild(value, leaf => new WireValue(state[stateIndex++], leaf.Kind)))],
            RefinedWires = state,
        };

        // An argument that is no invariant is not read by the loop or the code after it.
        var arguments = method.GetParameters().Select(_ => (Value)new AggregateValue(typeof(void), ImmutableDictionary<string, Value>.Empty)).ToArray();
        foreach (var (value, lowered) in shape.Invariants)
        {
            var input = Rebuild(lowered, leaf => leaf is ConstantValue ? leaf : new WireValue(invariants[invariantIndex++], leaf.Kind));
            if (value.Kind == SsaValueKind.Argument)
            {
                arguments[value.Index] = input;
            }
            else
            {
                context.Inputs[value] = input;
            }
        }

        var emitter = new Emitter(circuit, recognizePatterns);
        var (result, _) = Lower(emitter, arguments, context);
        return new LoweredPart(table, circuit, emitter, context, state, invariants, part == RecurrentPart.Suffix ? result : null);
    }

    private (Value Result, int Offset) Lower(Emitter emitter, IReadOnlyList<Value> arguments, RecurrentContext context) =>
        OperationLowering.Lower(decoded, ssa, emitter, arguments, [method], null, context);

    private Value[] Parameters(Circuit circuit) => [.. layout.Parameters.Select(parameter => CellLayout.ValueOf(parameter, circuit))];

    private void CheckRanges(Emitter emitter)
    {
        if (emitter.CheckRanges() is { } fault)
        {
            throw fault is { Owner: { } owner, IlOffset: >= 0 }
                ? owner.Refuse(fault.IlOffset, fault.Construct, fault.Reason)
                : new TranslationException(Location, shape.TestOffset, fault.Construct, fault.Reason);
        }
    }

    // The leaves of the back-edge or next values, in the order of the state cells.
    private ScalarValue[] BackLeaves(Value[] values)
    {
        var leaves = values.SelectMany((value, index) => Leaves(value, StateName(headerPhis[index]))).Select(leaf => leaf.Value).ToArray();
        return leaves.Length == statePlans.Length
            ? leaves
            : throw new InvalidOperationException($"the loop state has {statePlans.Length} leaves, and the back-edge values have {leaves.Length}");
    }

    private string StateName(SsaValue phi) => phi.Variable.Kind switch
    {
        SsaVariableKind.Argument => method.GetParameters()[phi.Variable.Index].Name ?? $"argument{Text(phi.Variable.Index)}",
        SsaVariableKind.Local => $"local{Text(phi.Variable.Index)}",
        _ => $"stack{Text(phi.Variable.Index)}",
    };

    private string InvariantName(SsaValue value) => value.Kind == SsaValueKind.Argument
        ? method.GetParameters()[value.Index].Name ?? $"argument{Text(value.Index)}"
        : $"value{Text(value.Id)}";

    private static string Text(int number) => number.ToString(CultureInfo.InvariantCulture);

    // A state cell: an int has the grid 1. A float has no grid, because the back-edge values can
    // leave the grid of the initial value. An invariant keeps the range and the grid of its wire.
    private static CellPlan Plan((string Path, ScalarValue Value) leaf, Circuit circuit, Wire wire, bool state) =>
        new(leaf.Path, leaf.Value.Kind, circuit.Ranges[wire.Position],
            leaf.Value.Kind == ScalarKind.Int ? 1 : state || leaf.Value.Kind == ScalarKind.Bit ? null : circuit.Grids[wire.Position]);

    private static CellSpec Spec(CellPlan plan, bool state)
    {
        if (plan.Kind == ScalarKind.Bit)
        {
            return state ? Cell.StateBit() : Cell.InputBit();
        }

        var initial = CellLayout.NearestToZero(plan.Range, plan.Grid);
        return state ? Cell.State(plan.Range, initial, plan.Grid) : Cell.Input(plan.Range, initial, plan.Grid);
    }

    private static ImmutableArray<NamedWire> Named(CellPlan[] plans, Wire[] wires) => [.. plans.Select((plan, index) => new NamedWire(plan.Path, wires[index]))];

    // The number and bit leaves of a value with their paths, in field order.
    private static List<(string Path, ScalarValue Value)> Leaves(Value value, string path)
    {
        var leaves = new List<(string Path, ScalarValue Value)>();
        Collect(value, path, leaves);
        return leaves;
    }

    private static void Collect(Value value, string path, List<(string Path, ScalarValue Value)> leaves)
    {
        switch (value)
        {
            case ScalarValue scalar:
                leaves.Add((path, scalar));
                return;
            case AggregateValue aggregate:
                foreach (var field in Fields(aggregate))
                {
                    Collect(aggregate.Fields[field.Name], $"{path}.{FieldPath(field)}", leaves);
                }

                return;
            case ArrayValue array:
                for (var index = 0; index < array.Elements.Length; index++)
                {
                    Collect(array.Elements[index], $"{path}[{Text(index)}]", leaves);
                }

                return;
            default:
                throw new InvalidOperationException($"the value {value.GetType().Name} has no leaves");
        }
    }

    // The value with each leaf replaced, in the order of Collect.
    private static Value Rebuild(Value value, Func<ScalarValue, ScalarValue> replace)
    {
        switch (value)
        {
            case ScalarValue scalar:
                return replace(scalar);
            case AggregateValue aggregate:
                var fields = ImmutableDictionary.CreateBuilder<string, Value>(StringComparer.Ordinal);
                foreach (var field in Fields(aggregate))
                {
                    fields[field.Name] = Rebuild(aggregate.Fields[field.Name], replace);
                }

                return new AggregateValue(aggregate.Type, fields.ToImmutable());
            case ArrayValue array:
                return new ArrayValue(array.ElementType, [.. array.Elements.Select(element => Rebuild(element, replace))], isLocal: false);
            default:
                throw new InvalidOperationException($"the value {value.GetType().Name} has no leaves");
        }
    }

    private static IEnumerable<FieldInfo> Fields(AggregateValue aggregate) =>
        DataTypes.InstanceFields(aggregate.Type).Where(field => aggregate.Fields.ContainsKey(field.Name));

    // The path part of a field: the property name of a C# backing field (<Level>k__BackingField) or
    // of an F# field (Level@). The first letter of the path part is in lower case.
    private static string FieldPath(FieldInfo field)
    {
        var name = field.Name;
        if (name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and var end)
        {
            name = name[1..end];
        }

        return CellLayout.FieldName(name.TrimEnd('@'));
    }

    /// <summary>One cell of the step and the suffix: its path, its kind, its range and its grid.</summary>
    private sealed record CellPlan(string Path, ScalarKind Kind, NumericRange Range, double? Grid);

    /// <summary>A lowered step or suffix: its table, circuit, emitter, context, cells and return value.</summary>
    private sealed record LoweredPart(SourceTable Table, Circuit Circuit, Emitter Emitter, RecurrentContext Context, Wire[] State, Wire[] Invariants, Value? Result);
}
