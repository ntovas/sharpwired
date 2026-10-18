// No Handwired source. Handwired has no translator. The lowering of loop form 3 of the plan, section
// "Decisions", question 5, after docs/design.md, Layer 2, step 4: "the loop body becomes a separate
// step program, a recurrent net". The translator (Recurrent.cs) lowers the method one time for each
// part, with a recurrent context:
//
// - Probe: the normal lowering. The first loop with no maximum stops it and gives its header. A
//   loop with no maximum inside a loop or inside a branch is refused.
// - Prefix: the blocks before the loop. At the loop, the context gets the initial loop state (the
//   entry values of the header phis) and the invariants. The invariants are the values of the code
//   before the loop that the loop or the code after it reads. The lowering stops there.
// - Step: one pass of the loop from its header, on the state cells and the invariant cells. The
//   context gets the continue bit c, the back-edge values, next = Select(c, state, back) and
//   done = NOT c. A lane that is done keeps its state.
// - Suffix: one pass of the loop on the final state gives the values that the code after the loop
//   reads. Then the blocks after the loop and the return value.
//
// The step has no copy of the body in the prefix for a bottom-tested loop. One pass from the header
// holds the body and then the test, and the suffix computes the last pass again.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The part of a method with a loop of form 3 that one lowering builds.</summary>
internal enum RecurrentPart
{
    Probe,
    Prefix,
    Step,
    Suffix,
}

/// <summary>
/// The inputs and the results of one lowering of a method with a loop of form 3. Header is the header
/// block of the loop, or -1 for the probe. The lists of header phis follow the order of the phis of
/// the header block.
/// </summary>
internal sealed class RecurrentContext(RecurrentPart part, int header)
{
    public RecurrentPart Part { get; } = part;

    public int Header { get; } = header;

    /// <summary>True when the lowering reached the end of its part.</summary>
    public bool Stopped { get; set; }

    /// <summary>The IL offset of the loop test, for the location of a refusal.</summary>
    public int TestOffset { get; set; }

    /// <summary>Prefix: the entry value of each header phi.</summary>
    public Value[] InitialState { get; set; } = [];

    /// <summary>Prefix: the invariants and their values, in SSA value order.</summary>
    public List<(SsaValue Value, Value Lowered)> Invariants { get; } = [];

    /// <summary>Step and suffix: the loop state, one value for each header phi.</summary>
    public Value[] State { get; set; } = [];

    /// <summary>Step and suffix: the value of each invariant that is not an argument.</summary>
    public Dictionary<SsaValue, Value> Inputs { get; } = [];

    /// <summary>Step: the state wires whose interval on the path of the continue bit the context keeps.</summary>
    public IReadOnlyList<Wire> RefinedWires { get; set; } = [];

    /// <summary>Step: the interval of each refined wire when the continue bit is 1, or null when it has none.</summary>
    public Dictionary<Wire, (double Low, double High)?> Intervals { get; } = [];

    /// <summary>Step: true when the continue bit cannot be 1.</summary>
    public bool NeverContinues { get; set; }

    /// <summary>Step: the back-edge value of each header phi.</summary>
    public Value[] Back { get; set; } = [];

    /// <summary>Step: the next value of each header phi, Select(c, state, back).</summary>
    public Value[] Next { get; set; } = [];

    /// <summary>Step: NOT c.</summary>
    public ScalarValue? Done { get; set; }
}

/// <summary>The probe found the loop of form 3 with this header block.</summary>
internal sealed class RecurrentLoopFoundException(int header) : Exception("the method has a loop with no maximum")
{
    public int Header { get; } = header;
}

internal sealed partial class OperationLowering
{
    private RecurrentContext? recurrent;

    // The start block of a path condition outside the loops. The suffix does not lower the blocks
    // before the loop of form 3. That loop runs on each call, so its exit block dominates each block
    // after it. The path from the exit block gives the same condition.
    private int TopStart => recurrent is { Part: RecurrentPart.Suffix } marked ? ssa.Graph.ExitBlock(MarkedLoop(marked)) : 0;

    private NaturalLoop MarkedLoop(RecurrentContext context) =>
        ssa.Graph.LoopAt(context.Header) ?? throw new InvalidOperationException($"block {context.Header} is not a loop header");

    // True for a top-level block that the part does not lower. The step lowers only the loop, and
    // the suffix lowers the loop and the blocks after it.
    private bool SkipsBlock(int block)
    {
        if (recurrent is not { Part: RecurrentPart.Step or RecurrentPart.Suffix } context)
        {
            return false;
        }

        var loop = MarkedLoop(context);
        return !loop.Body.Contains(block) && (context.Part == RecurrentPart.Step || block < loop.Body.Min());
    }

    // The step and the suffix read the invariants that are not arguments from their cells.
    private void SetInputs()
    {
        foreach (var (value, input) in recurrent?.Inputs ?? [])
        {
            if (value.Kind == SsaValueKind.Phi)
            {
                gates.SetPhiValue(value, input);
            }
            else
            {
                values[value] = input;
            }
        }
    }

    // The refusal of a loop with no maximum, or the end of the probe at the loop of form 3.
    private Exception NoMaximum(NaturalLoop loop, int offset, ScalarValue entry, LoopCopy? outer)
    {
        var part = recurrent?.Part;
        if (part == RecurrentPart.Step || (part == RecurrentPart.Probe && outer is not null))
        {
            return Refuse(offset, "a loop with no maximum inside a loop",
                "the loop of a step program must be at the top level of the method; declare a maximum for the inner loop");
        }

        if (part == RecurrentPart.Probe && entry is not ConstantValue { Number: 1 })
        {
            return Refuse(offset, "a loop with no maximum inside a branch", "the loop of a step program must run on each call; move the loop out of the branch");
        }

        return part switch
        {
            RecurrentPart.Probe => new RecurrentLoopFoundException(loop.Header),
            RecurrentPart.Suffix => Refuse(offset, "a second loop with no maximum", "a step program holds one loop with no maximum; declare a maximum for each other loop"),
            _ => Refuse(offset, "a loop with no maximum", NoMaximumReason),
        };
    }

    // The loop of form 3 in the prefix, the step or the suffix.
    private void LowerRecurrent(NaturalLoop loop)
    {
        var context = recurrent!;
        var headerPhis = ssa.Blocks[loop.Header].Phis;
        switch (context.Part)
        {
            case RecurrentPart.Prefix:
                context.TestOffset = TestOffset(loop);
                context.InitialState = [.. headerPhis.Select(gates.EntryValue)];
                foreach (var value in Invariants(loop))
                {
                    context.Invariants.Add((value, Get(value)));
                }

                context.Stopped = true;
                return;
            case RecurrentPart.Step:
                {
                    var (proceed, back, _, offset) = Pass(loop, context.State);
                    context.TestOffset = offset;
                    context.Back = back;
                    context.Next = [.. headerPhis.Select((phi, index) => gates.Make(proceed, back[index], context.State[index], Gates.TypeOf(phi.Type), offset)!)];
                    context.Done = Gates.Not(proceed);
                    var facts = new Intervals(this);
                    facts.Add(proceed);
                    context.NeverContinues = facts.IsEmpty || proceed is ConstantValue { Number: 0 };
                    foreach (var wire in context.RefinedWires)
                    {
                        context.Intervals[wire] = facts.Interval(wire);
                    }

                    context.Stopped = true;
                    return;
                }

            default:
                {
                    // The final state is the state of the last pass, so this pass gives the values of the plain method.
                    var (_, _, liveOuts, _) = Pass(loop, context.State);
                    foreach (var (value, after) in liveOuts)
                    {
                        if (value.Kind == SsaValueKind.Phi)
                        {
                            gates.SetPhiValue(value, after);
                        }
                        else
                        {
                            values[value] = after;
                        }
                    }

                    return;
                }
        }
    }

    private int TestOffset(NaturalLoop loop)
    {
        var exitSources = loop.ExitEdges.Select(edge => edge.From).Distinct().ToArray();
        return LastOffset(exitSources.Contains(loop.Header) ? loop.Header : exitSources.Min());
    }

    // One pass of the loop from its header on the state. The pass gives the continue bit and the
    // back-edge value of each header phi (the state when no back edge runs). It also gives the values
    // that the code after the loop reads.
    // A store into a local array in the pass is refused, because the pass also runs in a lane that is done.
    private (ScalarValue Proceed, Value[] Back, List<(SsaValue Value, Value After)> LiveOuts, int TestOffset) Pass(NaturalLoop loop, Value[] state)
    {
        CheckExits(loop);
        var testOffset = TestOffset(loop);
        var headerPhis = ssa.Blocks[loop.Header].Phis;
        var outer = copy;
        copy = new LoopCopy(loop, One, One, new Intervals(this), Certain: false);
        try
        {
            gates.ForgetPhis(loop.Body.SelectMany(block => ssa.Blocks[block].Phis));
            for (var index = 0; index < headerPhis.Length; index++)
            {
                gates.SetPhiValue(headerPhis[index], state[index]);
                Inliner.EmitLeaves(Emitter, state[index]);
            }

            var lowered = LowerRegion(CopyOrder(loop), loop);
            var proceed = Gates.Not(gates.ExitReach(loop, testOffset));
            Value[] back = [.. headerPhis.Select((phi, index) => gates.BackEdgeValue(phi) ?? state[index])];
            var liveOuts = LiveOuts(loop).Select(value => (value, CurrentValue(value, loop, lowered)
                ?? throw new InvalidOperationException($"the value {value} after the loop has no value in the last pass"))).ToList();
            CheckRanges(testOffset);
            return (proceed, back, liveOuts, testOffset);
        }
        finally
        {
            copy = outer;
        }
    }

    // The values of the code before the loop that the loop or the code after it reads, in SSA value
    // order: arguments, operations and phis. The entry operands of the header phis are the initial
    // state. A constant, a zero local and an address need no cell.
    private SsaValue[] Invariants(NaturalLoop loop)
    {
        var first = loop.Body.Min();
        var inside = loop.Body.ToHashSet();
        var predecessors = ssa.Graph.Blocks[loop.Header].Predecessors;
        var found = new HashSet<SsaValue>();
        foreach (var block in ssa.Blocks.Where(block => inside.Contains(block.Block.Index) || block.Block.Index > first))
        {
            var phiOperands = block.Phis.SelectMany(phi => phi.Operands.Where((_, index) =>
                block.Block.Index != loop.Header || inside.Contains(predecessors[index])));
            var operands = block.Operations.Where(operation => operation.Address is null).SelectMany(operation => operation.Operands)
                .Concat(phiOperands).Concat(block.Terminator.Operands);
            foreach (var operand in operands)
            {
                var before = operand.Kind == SsaValueKind.Argument
                    || (operand.Kind is SsaValueKind.Operation or SsaValueKind.Phi && !inside.Contains(operand.Block) && operand.Block < first);
                if (before && operand.Address is null && operand.Type.Kind is not (SsaTypeKind.Address or SsaTypeKind.None))
                {
                    found.Add(operand);
                }
            }
        }

        return [.. found.OrderBy(value => value.Id)];
    }
}
