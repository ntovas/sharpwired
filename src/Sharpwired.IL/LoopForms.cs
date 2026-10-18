// No Handwired source. Handwired has no translator. The loop lowering of the plan, section
// "Decisions", question 5, forms 1 and 2, after docs/design.md, Layer 2, step 4. The translator
// unrolls each natural loop by interpretation of its blocks, one copy at a time. A copy is one pass
// from the header to the back edge or to the exit edge. In copy k the header phis read the loop
// state after copy k - 1.
//
// - Form 1: the continue bit of each copy is a constant, because the loop variables are constants.
//   The translator follows the branch and emits no operation for the condition. A block that a
//   constant branch skips in a copy is not lowered in that copy.
// - Form 2: the continue bit c_k is a runtime bit. The active bit of copy k + 1 is
//   r_(k+1) = AND(r_k, c_k), with r_0 = 1. Copy k + 1 reads the kept state
//   Select(r_(k+1), state before, value of copy k), so an inactive copy computes on the kept state.
//   A constant back-edge value stays a constant, so a loop counter stays a constant, and each copy
//   computes the bit k < n. A value after the loop is the value of the last active copy:
//   Select(r_k, value before copy k, value of copy k). A Select is not made when r_k is the
//   constant 1.
// - The range refinement keeps an interval for each int wire that a comparison with a constant
//   reads, on the active path of the current copy. The literals of the loop entry condition and of
//   each continue bit shrink the intervals. A comparison of an int form +-w + b with a constant whose
//   interval decides it gives a constant. When c_k is the constant 0, the unroll stops.
// - [MaxIterations(N)] limits the copies of a loop with a runtime active bit to N copies of its
//   body. The bit "the loop needs one more copy" after copy N, AND the entry condition of the loop,
//   is the overflow bit of the loop.
// - A loop with a runtime continue bit whose copy shrinks no interval of a wire from before the
//   loop, and no [MaxIterations], has no maximum (form 3, RecurrentLowering.cs). A fact on a wire of
//   the loop is not progress, because the next copy compares a new wire. A loop with two exit
//   blocks, with no exit, or with more than 65536 copies of its body is refused.
// - An emitted operation whose range passes FP32, while the wires that it reads have finite
//   ranges, is refused (in loops and in straight-line code).
// - The lowering changes a local array in place, so a store in a loop copy is accepted only when
//   the copy always runs: its active bit is the constant 1 and constant branches reach the store.
// - A running total of the loop (RunningTotals.cs) keeps a placeholder as its state in each copy,
//   and its stores keep their conditions. After the unroll a prefix network gives its values.

using System.Collections.Immutable;
using System.Reflection;
using Sharpwired.Annotations;
using Sharpwired.Core;

namespace Sharpwired.IL;

internal sealed partial class OperationLowering
{
    private const int MaximumCopies = 65_536;

    private const string NoMaximumReason =
        "the loop count has no declared maximum; declare [Range] on the count or [MaxIterations] on the method, or translate the loop as a step program (form 3)";

    /// <summary>The construct of a value whose range passes FP32.</summary>
    public const string OverflowConstruct = "a value past the FP32 range";

    /// <summary>The reason of a value whose range passes FP32.</summary>
    public const string OverflowReason = "the value can overflow FP32; declare smaller ranges";

    /// <summary>The construct of a gate with a side whose range is not finite.</summary>
    public const string OpenSideConstruct = "a choice with a side that has no finite range";

    /// <summary>The reason of a gate with a side whose range is not finite.</summary>
    public const string OpenSideReason = "a side of this choice can overflow FP32 when C# does not compute it; declare finite ranges";

    private static readonly ConstantValue Zero = new(0, ScalarKind.Bit);

    private static readonly ConstantValue One = new(1, ScalarKind.Bit);

    private readonly Func<ScalarValue>? entryCondition;

    // The comparisons with a constant that the lowering emitted, as a bound on one int wire, by the
    // wire of their bit, for the range refinement.
    private readonly Dictionary<Wire, IntBound> comparisons = [];

    private int currentBlock;

    // The innermost loop copy that the lowering is in, or null outside the loops.
    private LoopCopy? copy;

    /// <summary>
    /// The bit that is 1 when the current block runs in the plain method: the entry condition of the
    /// method, the active bit and entry condition of each loop copy around the block, and the
    /// literals of the branches before the block.
    /// </summary>
    public ScalarValue ActiveCondition()
    {
        var offset = ssa.Graph.Blocks[currentBlock].StartOffset;
        return copy is { } current
            ? And(And(current.Entry, current.Active), gates.Reach(current.Loop.Header, currentBlock, offset))
            : And(entryCondition?.Invoke() ?? One, gates.Reach(TopStart, currentBlock, offset));
    }

    // AND of two bits, with the constants folded.
    private static ScalarValue And(ScalarValue left, ScalarValue right) => (left, right) switch
    {
        (ConstantValue { Number: 0 }, _) or (_, ConstantValue { Number: 0 }) => Zero,
        (ConstantValue, _) => right,
        (_, ConstantValue) => left,
        _ => new AndValue([.. Gates.Literals<AndValue>(left), .. Gates.Literals<AndValue>(right)]),
    };

    // Lowers the blocks in the order. A block of a loop inside the enclosing loop (or of a loop at the
    // top level) unrolls that loop at the position of its first block. In a copy of the enclosing
    // loop, a block is lowered only when an edge from a lowered block reaches it with a literal other
    // than the constant 0, and only when "only" holds it. Returns the lowered blocks.
    private HashSet<int> LowerRegion(ImmutableArray<int> order, NaturalLoop? enclosing, HashSet<int>? only = null)
    {
        var graph = ssa.Graph;
        CheckOrder(order, enclosing);
        var handled = new HashSet<int>();
        var lowered = new HashSet<int>();

        bool Reached(int block) => graph.Blocks[block].Predecessors.Any(predecessor =>
            lowered.Contains(predecessor) && !graph.IsBackEdge(predecessor, block)
            && (graph.OutermostLoopIn(predecessor, enclosing) is not null || !gates.EdgeIsDead(predecessor, block)));

        foreach (var index in order)
        {
            if (recurrent is { Stopped: true })
            {
                break;
            }

            if (handled.Contains(index) || (only is not null && !only.Contains(index)) || (enclosing is null && SkipsBlock(index)))
            {
                continue;
            }

            if (graph.OutermostLoopIn(index, enclosing) is { } loop)
            {
                handled.UnionWith(loop.Body);
                if (enclosing is null || Reached(loop.Header))
                {
                    Unroll(loop);
                    lowered.UnionWith(loop.Body);
                }

                continue;
            }

            if (enclosing is null || index == enclosing.Header || Reached(index))
            {
                LowerBlock(ssa.Blocks[index]);
                lowered.Add(index);
            }
        }

        return lowered;
    }

    // Each edge between two units of the order must go forward. A unit is a block, or a loop inside
    // the enclosing loop at the position of its first block. A back edge is not checked.
    private void CheckOrder(ImmutableArray<int> order, NaturalLoop? enclosing)
    {
        var graph = ssa.Graph;
        var unitOf = new Dictionary<int, int>();
        var positionOf = new Dictionary<int, int>();
        foreach (var block in order)
        {
            var unit = graph.OutermostLoopIn(block, enclosing)?.Header ?? block;
            unitOf[block] = unit;
            positionOf.TryAdd(unit, positionOf.Count);
        }

        foreach (var block in order)
        {
            foreach (var successor in graph.Blocks[block].Successors)
            {
                if (unitOf.TryGetValue(successor, out var target) && target != unitOf[block] && !graph.IsBackEdge(block, successor)
                    && positionOf[target] <= positionOf[unitOf[block]])
                {
                    throw Refuse(LastOffset(block), "a backward branch outside a natural loop",
                        "the program accepts a backward branch only as the back edge of a loop with one entry");
                }
            }
        }
    }

    private int LastOffset(int block)
    {
        var basic = ssa.Graph.Blocks[block];
        return method.Instructions[basic.FirstInstruction + Math.Max(basic.InstructionCount - 1, 0)].Offset;
    }

    // Unrolls one loop (forms 1 and 2), and sets the values that the code after the loop reads.
    private void Unroll(NaturalLoop loop)
    {
        if (recurrent is { Part: not RecurrentPart.Probe } marked && loop.Header == marked.Header)
        {
            LowerRecurrent(loop);
            return;
        }

        var graph = ssa.Graph;
        CheckExits(loop);
        var exitSources = loop.ExitEdges.Select(edge => edge.From).Distinct().ToArray();
        var testOffset = LastOffset(exitSources.Contains(loop.Header) ? loop.Header : exitSources.Min());

        // A copy count is a count of copies of the body. When the blocks of the exit test come before
        // the body (no back edge leaves them), the last pass runs only the test, so the test runs one
        // more time than the body. The test can span the header and other blocks, for example
        // "i < 1 || i < n", and an && value in Debug (review of task 5, round 2, finding B).
        var testBlocks = BlocksBefore(loop, exitSources);
        var extra = testBlocks.Overlaps(loop.BackEdgeSources) ? 0 : 1;
        var maximum = method.Method.GetCustomAttribute<MaxIterationsAttribute>()?.Count;
        int? limitPasses = maximum is { } count && count <= MaximumCopies ? count + extra : null;
        var hardPasses = MaximumCopies + extra;

        var entry = copy is { } outer
            ? And(And(outer.Entry, outer.Active), gates.Reach(outer.Loop.Header, loop.Header, testOffset))
            : And(entryCondition?.Invoke() ?? One, gates.Reach(TopStart, loop.Header, testOffset));
        var facts = copy?.Facts.Clone() ?? new Intervals(this);
        EmitInvariantGates(loop, testBlocks);

        // A fact on a wire of this loop does not count as progress: the next copy reads a new wire.
        facts.Before = Circuit.Width;
        facts.Add(entry);
        var certain = copy is { } around
            ? around.Certain && around.Active is ConstantValue && gates.Reach(around.Loop.Header, loop.Header, testOffset) is ConstantValue { Number: not 0 }
            : returns.All(block => graph.Dominates(loop.Header, block.Block.Index));

        var bodyPhis = loop.Body.SelectMany(block => ssa.Blocks[block].Phis).ToArray();
        var headerPhis = ssa.Blocks[loop.Header].Phis;
        var state = headerPhis.Select(gates.EntryValue).ToArray();
        var initial = state.ToArray();

        // The running totals of the loop (RunningTotals.cs) keep their own state: a placeholder in each copy.
        var totals = MatchRunningTotals(loop);
        bool IsTotal(SsaValue value) => totals.Any(total => total.Holds(value));
        var liveOuts = LiveOuts(loop);
        var held = new Dictionary<SsaValue, Value>();
        var order = CopyOrder(loop);

        // kept[i]: a Select of the active bit keeps the state of header phi i; previous[i]: the state
        // of the pass before.
        var kept = new bool[headerPhis.Length];
        var previous = new Value?[headerPhis.Length];
        var outerCopy = copy;
        ScalarValue active = One;
        try
        {
            for (var pass = 0; ; pass++)
            {
                var atLimit = limitPasses is { } limit && pass >= limit - 1;
                copy = new LoopCopy(loop, entry, active, facts, certain);
                gates.ForgetPhis(bodyPhis);
                totals.ForEach(total => total.Pass = pass);
                for (var index = 0; index < headerPhis.Length; index++)
                {
                    gates.SetPhiValue(headerPhis[index], state[index]);
                    if (!IsTotal(headerPhis[index]) || pass == 0)
                    {
                        Inliner.EmitLeaves(Emitter, state[index]);
                    }
                }

                CheckRanges(testOffset);

                // The last pass of a loop with a runtime active bit at its limit lowers only the blocks
                // that the exit test reads.
                var lowered = LowerRegion(order, loop, atLimit && active is not ConstantValue ? testBlocks : null);
                var proceed = Gates.Not(gates.ExitReach(loop, testOffset));
                foreach (var value in liveOuts.Where(value => !IsTotal(value)))
                {
                    if (CurrentValue(value, loop, lowered) is { } current)
                    {
                        // The value after the loop is the value of the last active copy. A Select is
                        // not needed when the copy is always active, or when the state of a header phi
                        // already is the Select of the active bit over the value that held keeps.
                        var phiIndex = value.Kind == SsaValueKind.Phi && value.Block == loop.Header ? headerPhis.IndexOf(value) : -1;
                        held[value] = active is ConstantValue || !held.TryGetValue(value, out var before)
                            || (phiIndex >= 0 && kept[phiIndex] && ReferenceEquals(before, previous[phiIndex]))
                            ? current
                            : gates.Make(active, current, before, Gates.TypeOf(value.Type), testOffset)!;
                        EmitResults(held[value], testOffset);
                    }
                }

                CheckRanges(testOffset);

                var next = And(active, proceed);
                var narrowed = next is not ConstantValue && facts.Add(proceed);
                if (facts.IsEmpty)
                {
                    next = Zero;
                }

                if (next is ConstantValue { Number: 0 })
                {
                    break;
                }

                if (atLimit && next is not ConstantValue)
                {
                    AddOverflow(And(entry, next), testOffset, maximum!.Value);
                    break;
                }

                if (pass == hardPasses - 1)
                {
                    throw Refuse(testOffset, $"a loop with more than {MaximumCopies} copies",
                        $"the program unrolls a loop into at most {MaximumCopies} copies of its body");
                }

                if (next is not ConstantValue && !narrowed && maximum is null)
                {
                    throw NoMaximum(loop, testOffset, entry, outerCopy);
                }

                if (next is not ConstantValue)
                {
                    Emit(next, testOffset);
                }

                for (var index = 0; index < headerPhis.Length; index++)
                {
                    var back = gates.BackEdgeValue(headerPhis[index])
                        ?? throw new InvalidOperationException($"the phi {headerPhis[index]} has no value on the back edges");
                    previous[index] = state[index];
                    (state[index], kept[index]) = IsTotal(headerPhis[index]) ? (back, false) : Keep(next, back, state[index], Gates.TypeOf(headerPhis[index].Type), testOffset);
                }

                active = next;
            }
        }
        finally
        {
            copy = outerCopy;
        }

        foreach (var (value, after) in held)
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

        FinishRunningTotals(totals, initial, headerPhis);
    }

    // Emits the int gates that the loop test reads from outside the loop, before the first copy. A
    // gate with one reader emits at that reader, and here the reader is in a copy. A fact on a wire
    // of a copy does not count as progress of the range refinement. So the count of a loop such as
    // "if (c) n = n + 1; for (i < n)" had no maximum in Release (final fix wave, item J).
    private void EmitInvariantGates(NaturalLoop loop, HashSet<int> testBlocks)
    {
        var operands = testBlocks.Select(index => ssa.Blocks[index])
            .SelectMany(block => block.Operations.SelectMany(operation => operation.Operands).Concat(block.Terminator.Operands));
        foreach (var operand in operands.Where(operand => operand.Kind == SsaValueKind.Phi && !loop.Body.Contains(operand.Block)))
        {
            if (Get(operand) is GateValue { Wire: null, Kind: ScalarKind.Int } gate)
            {
                Emit(gate, operand.IlOffset);
            }
        }
    }

    // A loop must have one exit (plan, question 5). Two or more exit edges are one exit when they go
    // to one block, and each phi of that block reads one value on each of them. This holds for a loop
    // test that spans two or more blocks, for example "i < n && x > 1f" in Release, and for a break
    // to the end of the loop. A return in the loop gives a second exit block.
    private void CheckExits(NaturalLoop loop)
    {
        if (loop.ExitEdges.IsEmpty)
        {
            throw Refuse(LastOffset(loop.BackEdgeSources[^1]), "a loop with no exit", "the program has a fixed size, so each loop must end");
        }

        // The blocks from which control reaches the exit block on an exit edge: the exit sources and
        // the forwarding blocks after them.
        var graph = ssa.Graph;
        var target = graph.ExitBlock(loop);
        var sources = new HashSet<int>();
        foreach (var (from, to) in loop.ExitEdges)
        {
            sources.Add(from);
            for (var block = to; block != target && graph.Forward(block) != block; block = graph.Blocks[block].Jump ?? graph.Blocks[block].Next!.Value)
            {
                sources.Add(block);
            }
        }

        var predecessors = graph.Blocks[target].Predecessors;
        var oneBlock = loop.ExitEdges.All(edge => graph.Forward(edge.To) == target);
        var oneValue = ssa.Blocks[target].Phis.All(phi => predecessors.Where(sources.Contains).Select(source => phi.Operands[predecessors.IndexOf(source)]).Distinct().Count() == 1);
        if (!oneBlock || !oneValue)
        {
            var second = loop.ExitEdges.Where(edge => edge.From != loop.Header).Select(edge => edge.From).DefaultIfEmpty(loop.ExitEdges[1].From).Min();

            // The exit edges reach one block with different values: a break after the body changed a
            // value (final fix wave, item J). Otherwise a second exit block: a return in the loop.
            throw oneBlock
                ? Refuse(LastOffset(second), "a break after a change of a loop value",
                    "a loop must have one exit with one value for each variable; put the break before the changes in the loop body")
                : Refuse(LastOffset(second), "a loop with two exits", "a loop must have one exit block; remove the return in the loop");
        }
    }

    // The loop state for the next copy (docs/design.md: a select keeps the old loop state when the bit
    // is 0): Select(next, state before, back-edge value). An inactive copy then computes on the kept
    // state, so its values stay in the ranges of the active copies. A constant back-edge value, for
    // example a loop counter, stays a constant: an active copy reads the same constant, and the value
    // of an inactive copy is not read. Returns true when a Select keeps the state.
    private (Value State, bool Kept) Keep(ScalarValue next, Value back, Value old, Type? type, int offset) =>
        next is ConstantValue || back is ConstantValue || ReferenceEquals(back, old)
            ? (back, false)
            : (gates.Make(next, back, old, type, offset)!, true);

    // A topological order of the loop body with no back edges. A loop inside the body is one unit.
    // The header comes first. Among the units whose units before them are placed, the unit with the
    // lowest block index comes next, so the order is the IL order when the IL order is topological.
    private ImmutableArray<int> CopyOrder(NaturalLoop loop)
    {
        var graph = ssa.Graph;
        var unitOf = loop.Body.ToDictionary(block => block, block => graph.OutermostLoopIn(block, loop)?.Header ?? block);
        var members = loop.Body.GroupBy(block => unitOf[block]).ToDictionary(group => group.Key, group => group.Order().ToArray());
        var incoming = members.Keys.ToDictionary(unit => unit, _ => new HashSet<int>());
        foreach (var block in loop.Body)
        {
            foreach (var successor in graph.Blocks[block].Successors)
            {
                if (unitOf.TryGetValue(successor, out var target) && target != unitOf[block] && !graph.IsBackEdge(block, successor))
                {
                    incoming[target].Add(unitOf[block]);
                }
            }
        }

        var placed = new HashSet<int>();
        var ready = new SortedSet<(int First, int Unit)>(members.Where(pair => pair.Value.Contains(loop.Header)).Select(pair => (pair.Value[0], pair.Key)));
        var order = ImmutableArray.CreateBuilder<int>();
        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            placed.Add(next.Unit);
            order.AddRange(next.Unit == loop.Header ? [loop.Header, .. members[next.Unit].Where(block => block != loop.Header)] : members[next.Unit]);
            foreach (var (unit, sources) in incoming)
            {
                if (!placed.Contains(unit) && sources.Count > 0 && sources.All(placed.Contains))
                {
                    ready.Add((members[unit][0], unit));
                }
            }
        }

        if (placed.Count != members.Count)
        {
            throw Refuse(LastOffset(loop.Header), "a backward branch outside a natural loop",
                "the program accepts a backward branch only as the back edge of a loop with one entry");
        }

        return order.ToImmutable();
    }

    // Refuses a value past the FP32 range (an emitted operation whose range is not finite while each
    // wire that it reads has a finite range, or a constant that is not finite), and a gate with a side
    // that can be Infinity (Emitter.cs).
    private void CheckRanges(int offset)
    {
        if (Emitter.CheckRanges() is { } fault)
        {
            throw fault is { Owner: { } owner, IlOffset: >= 0 }
                ? owner.Refuse(fault.IlOffset, fault.Construct, fault.Reason)
                : Refuse(offset, fault.Construct, fault.Reason);
        }
    }

    private void AddOverflow(ScalarValue bit, int testOffset, int copies)
    {
        if (bit is ConstantValue { Number: 0 })
        {
            return;
        }

        Emitter.Overflows.Add(new LoopOverflow(Emit(bit, testOffset), Locate(testOffset), copies));
    }

    // The blocks of the loop that reach a target block without a back edge, with the targets.
    private HashSet<int> BlocksBefore(NaturalLoop loop, int[] targets)
    {
        var graph = ssa.Graph;
        var found = new HashSet<int>(targets);
        var work = new Stack<int>(targets);
        while (work.Count > 0)
        {
            var block = work.Pop();
            if (block == loop.Header)
            {
                continue;
            }

            foreach (var predecessor in graph.Blocks[block].Predecessors)
            {
                if (loop.Body.Contains(predecessor) && !graph.IsBackEdge(predecessor, block) && found.Add(predecessor))
                {
                    work.Push(predecessor);
                }
            }
        }

        return found;
    }

    // The values of the loop that a block outside the loop reads, in definition order.
    private SsaValue[] LiveOuts(NaturalLoop loop)
    {
        var inside = loop.Body.ToHashSet();
        var found = new HashSet<SsaValue>();
        foreach (var block in ssa.Blocks.Where(block => !inside.Contains(block.Block.Index)))
        {
            foreach (var operand in block.Phis.Concat(block.Operations).SelectMany(value => value.Operands).Concat(block.Terminator.Operands))
            {
                if (operand.Kind is SsaValueKind.Operation or SsaValueKind.Phi && inside.Contains(operand.Block))
                {
                    found.Add(operand);
                }
            }
        }

        return [.. found.OrderBy(value => value.Id)];
    }

    // The value of a loop value in the current copy, or null when its block did not run in the copy.
    private Value? CurrentValue(SsaValue value, NaturalLoop loop, HashSet<int> lowered)
    {
        if (value.Kind == SsaValueKind.Phi)
        {
            return value.Block == loop.Header || lowered.Contains(value.Block) ? gates.PhiValue(value) : null;
        }

        return lowered.Contains(value.Block) && values.TryGetValue(value, out var found) ? found : null;
    }

    // The comparison "left relation right" as a bound on one int wire, before the emit of its operands
    // (the emit gives an open form a wire, and then the form no longer shows its base wire).
    private static IntBound? BoundOf(ScalarValue left, ScalarValue right, Relation relation) => Intervals.Constraint(left, right, relation);

    // The range refinement: a comparison in a loop copy that the intervals decide gives a constant.
    private ConstantValue? DecideInLoop(IntBound? bound) =>
        bound is { } known && copy?.Facts.Decide(known) is { } holds ? new ConstantValue(holds ? 1 : 0, ScalarKind.Bit) : null;

    private void RecordComparison(Wire bit, IntBound? bound)
    {
        if (bound is { } known)
        {
            comparisons[bit] = known;
        }
    }

    /// <summary>A bound "Wire Bound Limit" on an int wire, for example w &lt; 8.</summary>
    private readonly record struct IntBound(Wire Wire, Bound Bound, double Limit);

    // True when the block runs on each call: outside the loops, a block that dominates each ret
    // block; in a loop copy, a block that the copy always reaches, in a copy whose active bit is the
    // constant 1, of a loop whose header runs on each call.
    private bool RunsOnEachCall(int block) => copy is { } current
        ? current.Certain && current.Active is ConstantValue
            && gates.Reach(current.Loop.Header, block, ssa.Graph.Blocks[block].StartOffset) is ConstantValue { Number: not 0 }
        : returns.All(ret => ssa.Graph.Dominates(block, ret.Block.Index));

    /// <summary>
    /// One copy of a loop: the loop, its entry condition, the active bit of the copy, the intervals on
    /// its active path, and Certain: the header of the loop runs on each call when the copies before
    /// it are active.
    /// </summary>
    private sealed record LoopCopy(NaturalLoop Loop, ScalarValue Entry, ScalarValue Active, Intervals Facts, bool Certain);

    /// <summary>A bound on an int wire: w &lt; m, w &lt;= m, w &gt; m, w &gt;= m, w == m or w != m.</summary>
    private enum Bound
    {
        Below,
        AtMost,
        Above,
        AtLeast,
        Equal,
        NotEqual,
    }

    /// <summary>
    /// The intervals of the range refinement on the active path of a loop copy (plan, question 5):
    /// one integer interval for each int wire that a comparison with a constant reads. A wire with
    /// no fact has the interval of its declared range. The interval of a NotEqual fact loses only an
    /// end that equals the bound.
    /// </summary>
    private sealed class Intervals(OperationLowering lowering)
    {
        private readonly Dictionary<Wire, (double Low, double High)> bounds = [];

        /// <summary>True when an interval is empty: the active path cannot occur.</summary>
        public bool IsEmpty { get; private set; }

        /// <summary>Add returns true only for a shrunk interval of a wire before this position.</summary>
        public int Before { get; set; } = int.MaxValue;

        /// <summary>The interval of an int wire, or null when the wire has none or the path cannot occur.</summary>
        public (double Low, double High)? Interval(Wire wire) => IsEmpty ? null : Of(wire);

        public Intervals Clone()
        {
            var clone = new Intervals(lowering) { IsEmpty = IsEmpty };
            foreach (var (wire, interval) in bounds)
            {
                clone.bounds[wire] = interval;
            }

            return clone;
        }

        /// <summary>Adds the literals of a condition that holds on the active path. Returns true when an interval shrinks.</summary>
        public bool Add(ScalarValue condition)
        {
            var narrowed = false;
            foreach (var literal in Gates.Literals<AndValue>(condition))
            {
                // NOT(OR(l1, ..., ln)) = AND(NOT l1, ..., NOT ln).
                narrowed |= literal is NotValue { Operand: OrValue { Wire: null } disjunction }
                    ? disjunction.Literals.Aggregate(false, (any, inner) => Add(Gates.Not(inner)) | any)
                    : AddLiteral(literal);
            }

            return narrowed;
        }

        /// <summary>The value of the bound when the interval of its wire decides it, otherwise null.</summary>
        public bool? Decide(IntBound comparison)
        {
            var (wire, bound, limit) = comparison;
            if (IsEmpty || Of(wire) is not var (low, high))
            {
                return null;
            }

            return bound switch
            {
                Bound.Below when high < limit => true,
                Bound.Below when low >= limit => false,
                Bound.AtMost when high <= limit => true,
                Bound.AtMost when low > limit => false,
                Bound.Above when low > limit => true,
                Bound.Above when high <= limit => false,
                Bound.AtLeast when low >= limit => true,
                Bound.AtLeast when high < limit => false,
                Bound.Equal when low == limit && high == limit => true,
                Bound.Equal when limit < low || limit > high || limit != Math.Floor(limit) => false,
                _ => null,
            };
        }

        private bool AddLiteral(ScalarValue literal)
        {
            var negated = false;
            while (literal is NotValue negation)
            {
                negated = !negated;
                literal = negation.Operand;
            }

            if (literal.Wire is not { } bit || !lowering.comparisons.TryGetValue(bit, out var comparison))
            {
                return false;
            }

            var (wire, bound, limit) = comparison;
            if (negated)
            {
                bound = bound switch
                {
                    Bound.Below => Bound.AtLeast,
                    Bound.AtMost => Bound.Above,
                    Bound.Above => Bound.AtMost,
                    Bound.AtLeast => Bound.Below,
                    Bound.Equal => Bound.NotEqual,
                    _ => Bound.Equal,
                };
            }

            if (Of(wire) is not var (low, high))
            {
                return false;
            }

            var (newLow, newHigh) = bound switch
            {
                Bound.Below => (low, Math.Min(high, Math.Ceiling(limit) - 1)),
                Bound.AtMost => (low, Math.Min(high, Math.Floor(limit))),
                Bound.Above => (Math.Max(low, Math.Floor(limit) + 1), high),
                Bound.AtLeast => (Math.Max(low, Math.Ceiling(limit)), high),
                Bound.Equal => limit == Math.Floor(limit) ? (Math.Max(low, limit), Math.Min(high, limit)) : (1, 0),
                _ => (low == limit ? low + 1 : low, high == limit ? high - 1 : high),
            };
            if (newLow == low && newHigh == high)
            {
                return false;
            }

            bounds[wire] = (newLow, newHigh);
            IsEmpty |= newLow > newHigh;
            return wire.Position < Before;
        }

        // The interval of an int wire: its fact, or its declared range rounded to integers. A wire
        // with no grid of 1 or with an open range has no interval.
        private (double Low, double High)? Of(Wire wire)
        {
            if (bounds.TryGetValue(wire, out var known))
            {
                return known;
            }

            var circuit = lowering.Circuit;
            var range = circuit.Ranges[wire.Position];
            return circuit.Grids[wire.Position] == 1 && double.IsFinite(range.Minimum) && double.IsFinite(range.Maximum)
                ? (Math.Ceiling(range.Minimum), Math.Floor(range.Maximum))
                : null;
        }

        /// <summary>
        /// The comparison "left relation right" as a bound on one int wire w, when one side is a
        /// constant and the other side is the int form c * w + b with c = 1 or -1. Otherwise null.
        /// </summary>
        public static IntBound? Constraint(ScalarValue left, ScalarValue right, Relation relation)
        {
            var bound = relation switch
            {
                Relation.Less => Bound.Below,
                Relation.LessOrEqual => Bound.AtMost,
                Relation.Greater => Bound.Above,
                Relation.GreaterOrEqual => Bound.AtLeast,
                _ => Bound.Equal,
            };
            double constant;
            (Wire Wire, double Coefficient, double Bias) form;
            if (right is ConstantValue { Number: var k } && Linear(left) is { } leftForm)
            {
                (constant, form) = (k, leftForm);
            }
            else if (left is ConstantValue { Number: var j } && Linear(right) is { } rightForm)
            {
                (constant, form, bound) = (j, rightForm, Mirror(bound));
            }
            else
            {
                return null;
            }

            // c * w + b R k gives w R (k - b) for c = 1 and w R' (b - k) for c = -1.
            return form.Coefficient > 0
                ? new IntBound(form.Wire, bound, constant - form.Bias)
                : new IntBound(form.Wire, Mirror(bound), form.Bias - constant);
        }

        private static Bound Mirror(Bound bound) => bound switch
        {
            Bound.Below => Bound.Above,
            Bound.AtMost => Bound.AtLeast,
            Bound.Above => Bound.Below,
            Bound.AtLeast => Bound.AtMost,
            _ => bound,
        };

        // An int value as c * w + b with c = 1 or -1: an open affine form of one term, or a wire.
        private static (Wire Wire, double Coefficient, double Bias)? Linear(ScalarValue value) => value switch
        {
            { Kind: not ScalarKind.Int } => null,
            AffineValue { Wire: null, Terms: [var term] } form when Math.Abs(term.Coefficient) == 1 => (term.Wire, term.Coefficient, form.Bias),
            AffineValue { Wire: null } => null,
            { Wire: { } wire } => (wire, 1, 0),
            _ => null,
        };
    }
}
