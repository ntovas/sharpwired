// Handwired source: $REF/lab/ui-exploration/guide/sections/loops.md, section 3 ("Prefix sums three
// ways") and section "The shapes of cost", $REF/docs/expressions.md, line 48, and
// src/runtime/prefix-networks.ts through Sharpwired.Core/PrefixNetworks.cs. The running totals of plan
// task 6b, after docs/design.md, Layer 2, step 4.
//
// A running total is an accumulator of a loop of form 1 or 2 that takes an associative combine in
// each copy: acc = combine(acc, x[i]) with acc in a local (a phi of the loop header) or in an array
// element of the step before (acc[i] = combine(acc[i - 1], x[i])). The combine is float or int +,
// Math.Min, Math.Max, MathF.Min, MathF.Max, the bit and, the bit or (C# &, |, and && or || with a
// local right operand), or a static [Handwired] [Associative] method of two parameters. The proof
// from the SSA form, before the unroll:
//
// - The accumulator operand of the combine is a header phi whose back-edge operands are the combine,
//   or an array read at a loop-copy constant index of an array that the loop stores the combine into,
//   in the block of the combine. A + in array elements with one store is not a running total for the
//   translator: its normal form already is one wide sum for each element, in 1 layer.
// - The element operand does not depend on the accumulator, on the combine, or on a read of an array
//   that the loop stores the total into.
// - Inside the loop, the accumulator and the combine go only to the combine, the back edge and array
//   stores, and each such array goes only to these stores, the accumulator read and ldlen.
//
// The unroll then lowers each copy as usual, except the combine and its stores. In copy k the combine
// keeps its input (the accumulator value), its element t_k, and the gate g_k: the bit that is 1 when
// the copy runs the combine (the active bit of the copy and the branch literals from the header). Its
// value is a placeholder P_k. A store of the total writes a pending store: the stored value under the
// store's active condition, with the old element. After the unroll the translator checks that the
// input of copy k is P_(k-1) (for the array form: the element that copy k - 1 stored), and lowers the
// items with PrefixNetworks.Sklansky (depth first). The items are the value before the loop (when it is
// not the identity of the combine) and the elements. The value before the loop has no gate. In a copy
// with a runtime gate, the element is gated: ITE(g_k, t_k, identity), so an inactive copy adds
// nothing. The identity is 0 for +, 1 for AND, 0 for OR. For Min and Max it is a bound of the ranges
// of the elements and of the value before the loop. Each prefix replaces its placeholder, and each
// pending store gets its prefix.
//
// Exactness (plan, decision 8): a prefix network groups the combines in another order. Min, Max, AND
// and OR return one operand, so each grouping gives the same value. A float or int + is exact only
// when each item is on a grid and each partial sum in any order stays within 2^24 grid steps.
//
// The normal form stays in these cases. The first copy decides most of them: an element that is not
// on a grid (for +), a constant element (the normal form folds it), or an [Associative] method under a
// runtime gate (the method has no identity value). Then the combine of each copy lowers at its
// instruction, and the operations are the operations of the normal form. A case that shows only after
// the unroll (a broken chain, a sum past 2^24 grid steps, a runtime gate in a later copy) gets the
// combines in loop order after the unroll, each one the lowering of the IL instruction of the combine:
// the values of the normal form, with its operations in another order.
//
// Cost (loops.md, "The shapes of cost"): a chain of k items has k - 1 combines in k - 1 levels, and
// the Sklansky network has (k / 2) × log2(k) combines in log2(k) levels. So in a loop of form 1 the
// network has more operations and fewer layers. In a loop of form 2 the network also removes the
// Select that keeps the state in each copy, so it has fewer operations too.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using Sharpwired.Annotations;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The value of the running total after one copy of its loop, before the prefix network replaces it.</summary>
internal sealed class PrefixPlaceholder(int step, ScalarKind kind) : ScalarValue(kind)
{
    public int Step { get; } = step;
}

/// <summary>An array element that a copy of a running total loop stores under a runtime condition.</summary>
internal sealed class PendingStore(Value stored, ScalarValue condition, Value old, int ilOffset) : Value
{
    public Value Stored { get; } = stored;

    public ScalarValue Condition { get; } = condition;

    public Value Old { get; } = old;

    public int IlOffset { get; } = ilOffset;
}

internal sealed partial class OperationLowering
{
    // The combines and the stores of the running totals of the loops that the lowering unrolls now.
    private readonly Dictionary<SsaValue, RunningTotal> runningTotals = [];

    // The values that replace SSA values in Get, for the normal form of a running total after the unroll.
    private readonly Dictionary<SsaValue, Value> operandValues = [];

    private enum CombineKind
    {
        Add,
        Minimum,
        Maximum,
        And,
        Or,
        Method,
    }

    // The running totals of a loop, by their combines. Empty when the loop has none or the option is off.
    private List<RunningTotal> MatchRunningTotals(NaturalLoop loop)
    {
        var found = new List<RunningTotal>();
        if (!Emitter.RecognizePatterns || recurrent is not null)
        {
            return found;
        }

        var body = loop.Body.ToHashSet();
        var users = new Dictionary<SsaValue, List<object>>();
        foreach (var block in loop.Body.Select(index => ssa.Blocks[index]))
        {
            foreach (var user in block.Phis.Concat(block.Operations))
            {
                foreach (var operand in user.Operands)
                {
                    Users(operand).Add(user);
                }
            }

            foreach (var operand in block.Terminator.Operands)
            {
                Users(operand).Add(block.Terminator);
            }
        }

        List<object> Users(SsaValue value) => users.TryGetValue(value, out var list) ? list : users[value] = [];

        // A combine in a loop inside this loop belongs to the inner loop.
        foreach (var combine in loop.Body.Where(index => ssa.Graph.OutermostLoopIn(index, loop) is null).SelectMany(index => ssa.Blocks[index].Operations))
        {
            if (Classify(combine) is not var (kind, method))
            {
                continue;
            }

            for (var position = 0; position < 2; position++)
            {
                if (MatchTotal(loop, body, Users, combine, kind, method, position) is { } total)
                {
                    found.Add(total);
                    runningTotals[combine] = total;
                    foreach (var store in total.Stores)
                    {
                        runningTotals[store] = total;
                    }

                    break;
                }
            }
        }

        return found;
    }

    // The kind of an associative combine of two operands, or null.
    private static (CombineKind Kind, MethodInfo? Method)? Classify(SsaValue operation)
    {
        switch (operation.Op)
        {
            case ILOpCode.Add when operation.Type.Kind is SsaTypeKind.Float or SsaTypeKind.Int:
                return (CombineKind.Add, null);
            case ILOpCode.And when operation.Type.Kind == SsaTypeKind.Bit:
                return (CombineKind.And, null);
            case ILOpCode.Or when operation.Type.Kind == SsaTypeKind.Bit:
                return (CombineKind.Or, null);
            case ILOpCode.Call when operation.Member is MethodInfo { IsStatic: true } method && method.GetParameters() is [var first, var second]
                && first.ParameterType == second.ParameterType && method.ReturnType == first.ParameterType
                && DataTypes.KindOf(method.ReturnType) is { } kind:
                {
                    if ((method.DeclaringType == typeof(Math) || method.DeclaringType == typeof(MathF)) && kind != ScalarKind.Bit)
                    {
                        return method.Name switch
                        {
                            nameof(Math.Min) => (CombineKind.Minimum, method),
                            nameof(Math.Max) => (CombineKind.Maximum, method),
                            _ => null,
                        };
                    }

                    return method.GetCustomAttribute<HandwiredAttribute>() is not null && method.GetCustomAttribute<AssociativeAttribute>() is not null
                        && !method.IsGenericMethod
                        ? (CombineKind.Method, method)
                        : null;
                }

            default:
                return null;
        }
    }

    // The running total of a combine whose operand at the position is the accumulator, or null.
    private RunningTotal? MatchTotal(
        NaturalLoop loop, HashSet<int> body, Func<SsaValue, List<object>> users, SsaValue combine, CombineKind kind, MethodInfo? method, int position)
    {
        var graph = ssa.Graph;
        var accumulator = combine.Operands[position];
        var element = combine.Operands[1 - position];
        var header = graph.Blocks[loop.Header];
        var isPhi = accumulator.Kind == SsaValueKind.Phi && accumulator.Block == loop.Header
            && header.Predecessors.Select((predecessor, index) => (predecessor, index)).All(pair =>
                graph.IsBackEdge(pair.predecessor, loop.Header) ? accumulator.Operands[pair.index] == combine : !Inside(accumulator.Operands[pair.index]));
        var isRead = !isPhi && accumulator.Kind == SsaValueKind.Operation && IsElementRead(accumulator.Op) && body.Contains(accumulator.Block)
            && graph.OutermostLoopIn(accumulator.Block, loop) is null
            && !Inside(accumulator.Operands[0]) && IsCopyConstant(accumulator.Operands[1], loop, []);
        // A + in array elements with one store: each element is an open form, and the normal form
        // emits one wide sum for each element (loops.md, section 3), in 1 layer with fewer operations.
        if ((!isPhi && !isRead) || (isRead && kind == CombineKind.Add && uses.GetValueOrDefault(combine) == 1))
        {
            return null;
        }

        var stores = loop.Body.SelectMany(index => ssa.Blocks[index].Operations)
            .Where(operation => IsElementStore(operation.Op) && (operation.Operands[2] == combine || (isPhi && operation.Operands[2] == accumulator)))
            .ToArray();
        if (stores.Any(store => graph.OutermostLoopIn(store.Block, loop) is not null))
        {
            return null;
        }

        var arrays = stores.Select(store => store.Operands[0]).ToHashSet();
        if (isRead && !stores.Any(store => store.Operands[0] == accumulator.Operands[0] && store.Block == combine.Block && IsCopyConstant(store.Operands[1], loop, [])))
        {
            return null;
        }

        // The uses inside the loop.
        bool OnlyUsedBy(SsaValue value, Func<object, bool> allowed) => users(value).All(allowed);
        var allowedAccumulator = OnlyUsedBy(accumulator, user => user == combine || (isPhi && stores.Contains(user)));
        var allowedCombine = OnlyUsedBy(combine, user => stores.Contains(user) || (isPhi && user == accumulator));
        var allowedArrays = arrays.All(array => !Inside(array) && OnlyUsedBy(array, user =>
            stores.Contains(user) || user == accumulator || user is SsaValue { Op: ILOpCode.Ldlen }));
        if (!allowedAccumulator || !allowedCombine || !allowedArrays)
        {
            return null;
        }

        // The element does not read the total.
        var seen = new HashSet<SsaValue>();
        var work = new Stack<SsaValue>([element]);
        while (work.Count > 0)
        {
            var value = work.Pop();
            if (!seen.Add(value) || !Inside(value) || (value.Kind == SsaValueKind.Phi && value.Block == loop.Header && value != accumulator))
            {
                continue;
            }

            if (value == accumulator || value == combine || (IsElementRead(value.Op) && value.Kind == SsaValueKind.Operation && arrays.Contains(value.Operands[0])))
            {
                return null;
            }

            foreach (var operand in value.Operands)
            {
                work.Push(operand);
            }
        }

        var valueKind = DataTypes.KindOf(Gates.TypeOf(combine.Type) ?? typeof(void));
        return valueKind is { } scalar
            ? new RunningTotal(combine, accumulator, element, isPhi, kind, method, position == 0, [.. stores], scalar)
            : null;

        bool Inside(SsaValue value) => value.Block >= 0 && body.Contains(value.Block) && value.Kind is SsaValueKind.Operation or SsaValueKind.Phi;
    }

    // True for a value that is a constant in each copy of the loop: a constant, an arithmetic of such
    // values, or a header phi whose operands are such values (a loop counter). The unroll keeps a
    // constant back-edge value a constant.
    private static bool IsCopyConstant(SsaValue value, NaturalLoop loop, HashSet<SsaValue> visiting)
    {
        if (value.Kind == SsaValueKind.Constant)
        {
            return true;
        }

        if (!visiting.Add(value))
        {
            return true;
        }

        return value switch
        {
            { Kind: SsaValueKind.Phi } when value.Block == loop.Header => value.Operands.All(operand => IsCopyConstant(operand, loop, visiting)),
            { Kind: SsaValueKind.Operation, Op: ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Neg }
                when loop.Body.Contains(value.Block) => value.Operands.All(operand => IsCopyConstant(operand, loop, visiting)),
            _ => false,
        };
    }

    private static bool IsElementRead(ILOpCode op) =>
        op is ILOpCode.Ldelem or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i1;

    private static bool IsElementStore(ILOpCode op) => op is ILOpCode.Stelem or ILOpCode.Stelem_r4 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i1;

    // In a loop copy: the combine of a running total keeps its input, its element and its gate, and
    // gives a placeholder; a store of the total writes a pending store. Returns false for another
    // operation.
    private bool LowerRunningTotal(SsaValue operation)
    {
        if (!runningTotals.TryGetValue(operation, out var total) || copy is not { } current)
        {
            return false;
        }

        var offset = operation.IlOffset;
        if (operation == total.Combine)
        {
            var gate = And(current.Active, gates.Reach(current.Loop.Header, currentBlock, ssa.Graph.Blocks[currentBlock].StartOffset));
            var (input, element) = (Get(total.Accumulator), Scalar(total.Element, offset));
            if (total.Steps.Count == 0 && KeepsNormalForm(total, input, element, gate))
            {
                Abandon(total);
                return false;
            }

            var output = new PrefixPlaceholder(total.Steps.Count, total.ValueKind);
            total.Steps.Add(new TotalStep(input, element, gate, output, total.Pass));
            values[operation] = output;
            return true;
        }

        var (array, index) = StoreTarget(operation);
        var condition = ActiveCondition();
        array.Elements[index] = condition is ConstantValue { Number: not 0 }
            ? Get(operation.Operands[2])
            : new PendingStore(Get(operation.Operands[2]), condition, array.Elements[index], offset);
        total.Arrays.Add(array);
        return true;
    }

    // The first copy shows that a total keeps its normal form: a + whose first item is not on a grid,
    // a constant element, or an [Associative] method under a runtime gate. Then the unroll lowers the
    // combine of each copy at its instruction, and the operations are those of the normal form.
    private bool KeepsNormalForm(RunningTotal total, Value input, ScalarValue element, ScalarValue gate) =>
        element is ConstantValue || input is not ScalarValue first || (total.Kind == CombineKind.Method && gate is not ConstantValue)
        || (total.Kind == CombineKind.Add && !IsExactTotal(Identity(total, first, [element]) is null ? [first, element] : [element]));

    // The total keeps its normal form: its combine and its stores lower at their instructions. A store
    // that the copy already made under a runtime condition is refused, as the normal form refuses it.
    private void Abandon(RunningTotal total)
    {
        total.Abandoned = true;
        runningTotals.Remove(total.Combine);
        foreach (var store in total.Stores)
        {
            runningTotals.Remove(store);
        }

        if (total.Arrays.SelectMany(array => array.Elements).OfType<PendingStore>().FirstOrDefault() is { } pending)
        {
            throw Refuse(pending.IlOffset, "an array store under a runtime condition in a loop",
                "the program changes an array in place, so a store in a loop must run in each copy of the loop");
        }
    }

    // The array and the constant index of an array store into a local array (plan, "IL opcode table", row stelem).
    private (ArrayValue Array, int Index) StoreTarget(SsaValue operation)
    {
        var offset = operation.IlOffset;
        var array = Array(operation.Operands[0], offset);
        if (!array.IsLocal)
        {
            throw Refuse(offset, "a store into a cell array or a static array", "the program changes only an array that the method creates");
        }

        if (Scalar(operation.Operands[1], offset) is not ConstantValue { Number: var index })
        {
            throw Refuse(offset, "an array store at a computed index", "the program stores into an array at a constant index only");
        }

        if (index < 0 || index >= array.Elements.Length)
        {
            throw Refuse(offset, $"the array index {Fp32.Format(index)}", $"the index must lie in 0..{array.Elements.Length - 1}");
        }

        return (array, (int)index);
    }

    // After the unroll: the prefix network or the normal form of each running total, the pending
    // stores, and the value of the accumulator after the loop. initial holds the value of each header
    // phi at the loop entry.
    private void FinishRunningTotals(List<RunningTotal> totals, Value[] initial, ImmutableArray<SsaValue> headerPhis)
    {
        foreach (var total in totals.Where(total => !total.Abandoned))
        {
            var init = total.IsPhi ? initial[headerPhis.IndexOf(total.Accumulator)] : total.Steps.FirstOrDefault()?.Input;
            var offset = total.Combine.IlOffset;
            var outputs = new Value[total.Steps.Count];
            Value Resolve(Value value) => value switch
            {
                PrefixPlaceholder placeholder when placeholder.Step < outputs.Length && ReferenceEquals(total.Steps[placeholder.Step].Output, placeholder)
                    => outputs[placeholder.Step],
                PendingStore pending => gates.Make(pending.Condition, Resolve(pending.Stored), Resolve(pending.Old), Gates.TypeOf(total.Combine.Type), pending.IlOffset)!,
                _ => value,
            };

            if (total.Steps.Count > 0 && !(UsesNetwork(total, init!) is { } items && Network(total, items, outputs)))
            {
                NormalForm(total, outputs, Resolve);
            }

            foreach (var array in total.Arrays)
            {
                for (var index = 0; index < array.Elements.Length; index++)
                {
                    array.Elements[index] = Resolve(array.Elements[index]);
                }
            }

            var after = outputs.Length > 0 ? outputs[^1] : init;
            if (total.IsPhi && after is not null)
            {
                gates.SetPhiValue(total.Accumulator, after);
            }

            if (outputs.Length > 0)
            {
                values[total.Combine] = outputs[^1];
            }

            runningTotals.Remove(total.Combine);
            foreach (var store in total.Stores)
            {
                runningTotals.Remove(store);
            }
        }
    }

    // The items of the prefix network, or null when the total keeps its normal form.
    private List<ScalarValue>? UsesNetwork(RunningTotal total, Value init)
    {
        var steps = total.Steps;
        var chained = steps.Select((step, index) => index == 0
            ? step.Input is not PrefixPlaceholder and not PendingStore
            : ReferenceEquals(step.Input, steps[index - 1].Output) || (step.Input is PendingStore { Stored: var stored } && ReferenceEquals(stored, steps[index - 1].Output))).All(link => link);
        var gated = steps.Any(step => step.Gate is not ConstantValue);
        if (!chained || init is not ScalarValue first || steps.All(step => step.Element is ConstantValue) || (total.Kind == CombineKind.Method && gated))
        {
            return null;
        }

        var identity = Identity(total, first, [.. steps.Select(step => step.Element)]);
        var items = new List<ScalarValue>();
        if (identity is null)
        {
            items.Add(first);
        }

        if (total.Kind == CombineKind.Add && !IsExactTotal([.. items, .. steps.Select(step => step.Element)]))
        {
            return null;
        }

        // The value before the loop is an item with no gate. A gated element gives the identity of the
        // combine when its copy is inactive, so the copy adds nothing (review of task 6b, round 1).
        if (!gated)
        {
            items.AddRange(steps.Select(step => step.Element));
            return items;
        }

        if (GateIdentity(total, [first, .. steps.Select(step => step.Element)]) is not { } off)
        {
            return null;
        }

        var type = Gates.TypeOf(total.Combine.Type);
        items.AddRange(steps.Select(step => (ScalarValue)gates.Make(step.Gate, step.Element, off, type, total.Combine.IlOffset)!));
        return items;
    }

    // The identity of the combine for the items: 0 for +, 1 for AND, 0 for OR, the largest maximum of
    // the item ranges for Min and the smallest minimum for Max. Null when a range is not finite. The
    // items are the value before the loop and the elements. The bound must include the value before
    // the loop. When it is dropped as the identity, an inactive copy must give it back (final fix
    // wave, item A). When it is kept, an inactive copy must not give a value past it.
    private ConstantValue? GateIdentity(RunningTotal total, ScalarValue[] elements)
    {
        if (total.Kind is CombineKind.Add or CombineKind.And or CombineKind.Or)
        {
            return new ConstantValue(total.Kind == CombineKind.And ? 1 : 0, total.ValueKind);
        }

        var ranges = elements.Select(element => element is ConstantValue number
            ? new NumericRange(number.Number, number.Number)
            : Circuit.Ranges[Emitter.Emit(element).Position]).ToArray();
        var bound = total.Kind == CombineKind.Minimum ? ranges.Max(range => range.Maximum) : ranges.Min(range => range.Minimum);
        return double.IsFinite(bound) && (double)(float)bound == bound ? new ConstantValue(bound, total.ValueKind) : null;
    }

    // The identity value of the combine when the value before the loop is one, otherwise null: 0 for
    // +, 1 for AND, 0 for OR, a constant at or above each element range for Min, at or below for Max.
    private ConstantValue? Identity(RunningTotal total, ScalarValue first, IEnumerable<ScalarValue> elements)
    {
        if (first is not ConstantValue constant)
        {
            return null;
        }

        var ranges = elements.Select(element => element switch
        {
            ConstantValue number => new NumericRange(number.Number, number.Number),
            { Wire: { } wire } => Circuit.Ranges[wire.Position],
            _ => NumericRange.Unknown,
        }).ToArray();
        var holds = total.Kind switch
        {
            CombineKind.Add or CombineKind.Or => constant.Number == 0,
            CombineKind.And => constant.Number == 1,
            CombineKind.Minimum => ranges.All(range => range.Maximum <= constant.Number),
            CombineKind.Maximum => ranges.All(range => range.Minimum >= constant.Number),
            _ => false,
        };
        return holds ? constant : null;
    }

    // True when each partial sum of the items, in any order, is an exact FP32 value: each item is on a
    // grid, and the sum of the largest magnitudes stays within 2^24 steps of the finest grid.
    private bool IsExactTotal(IEnumerable<ScalarValue> items)
    {
        var grid = double.PositiveInfinity;
        var magnitude = 0.0;
        bool Add(double itemGrid, double largest)
        {
            if (!double.IsFinite(largest))
            {
                return false;
            }

            grid = Math.Min(grid, itemGrid);
            magnitude += largest;
            return true;
        }

        bool Constant(double number) => number == 0 || ((double)(float)number == number && Add(Fp32.LowestPowerOfTwo(number), Math.Abs(number)));
        bool Wired(Wire wire, double coefficient) => Circuit.Grids[wire.Position] is { } wireGrid
            && Add(wireGrid * Fp32.LowestPowerOfTwo(coefficient), Math.Abs(coefficient) * Math.Max(Math.Abs(Circuit.Ranges[wire.Position].Minimum), Math.Abs(Circuit.Ranges[wire.Position].Maximum)));

        foreach (var item in items)
        {
            var exact = item switch
            {
                ConstantValue constant => Constant(constant.Number),
                AffineValue { Wire: null } form => Emitter.IsExactChain(form) && Constant(form.Bias) && form.Terms.All(term => term.Coefficient == 0 || Wired(term.Wire, term.Coefficient)),
                _ => Wired(Emitter.Emit(item), 1),
            };
            if (!exact)
            {
                return false;
            }
        }

        return double.IsFinite(grid) ? grid >= Fp32.SmallestGrid && magnitude <= Fp32.ExactGridSteps * grid : magnitude == 0;
    }

    // The Sklansky network of the items. outputs[k] gets the prefix after copy k. Returns true.
    private bool Network(RunningTotal total, List<ScalarValue> items, Value[] outputs)
    {
        var offset = total.Combine.IlOffset;
        var wires = items.Select(item => Emit(item, offset)).ToArray();
        var prefixes = PrefixNetworks.Sklansky(wires, (earlier, later) => Combine(total, earlier, later, offset));
        var first = prefixes.Length - outputs.Length;
        for (var index = 0; index < outputs.Length; index++)
        {
            outputs[index] = new WireValue(prefixes[first + index], total.ValueKind);
        }

        return true;
    }

    // One combine of the network: combine(earlier, later), with the operands in the order of the IL.
    private Wire Combine(RunningTotal total, Wire earlier, Wire later, int offset)
    {
        var (left, right) = total.AccumulatorFirst ? (earlier, later) : (later, earlier);
        switch (total.Kind)
        {
            case CombineKind.Add:
                {
                    var sum = Circuit.RoundAffine([(left, 1), (right, 1)]);
                    Emitter.NoteValue(sum, [left, right]);
                    return sum;
                }

            case CombineKind.Minimum:
                return Macro(offset, CallText(total.Method!), () => Circuit.MinimumExact(left, right));
            case CombineKind.Maximum:
                return Macro(offset, CallText(total.Method!), () => Circuit.MaximumExact(left, right));
            case CombineKind.And:
                return Emitter.Emit(new AndValue([new WireValue(left, ScalarKind.Bit), new WireValue(right, ScalarKind.Bit)]));
            case CombineKind.Or:
                return Emitter.Emit(new OrValue([new WireValue(left, ScalarKind.Bit), new WireValue(right, ScalarKind.Bit)]));
            default:
                {
                    var kind = total.ValueKind;
                    var result = Inliner.Inline(this, total.Method!, [new WireValue(left, kind), new WireValue(right, kind)], offset);
                    return Emit(result, offset);
                }
        }
    }

    // The normal form: the combines in loop order, each one the lowering of the IL instruction of the
    // combine on the value before the copy and the element. A copy with a runtime gate keeps the value
    // before it when the gate is 0. A value that a later copy reads emits, as the unroll emits the
    // loop state at the start of each copy. A pending store is refused, as the normal form refuses an
    // array store under a runtime condition.
    private void NormalForm(RunningTotal total, Value[] outputs, Func<Value, Value> resolve)
    {
        var steps = total.Steps;
        foreach (var array in total.Arrays)
        {
            if (array.Elements.OfType<PendingStore>().FirstOrDefault() is { } pending)
            {
                throw Refuse(pending.IlOffset, "an array store under a runtime condition in a loop",
                    "the program changes an array in place, so a store in a loop must run in each copy of the loop");
            }
        }

        var block = currentBlock;
        currentBlock = total.Combine.Block;
        try
        {
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var input = resolve(step.Input);
                operandValues[total.Accumulator] = input;
                operandValues[total.Element] = step.Element;
                var combined = LowerOperation(total.Combine)!;
                operandValues.Clear();
                outputs[index] = gates.Make(step.Gate, combined, input, Gates.TypeOf(total.Combine.Type), total.Combine.IlOffset)!;
                if (total.IsPhi && step.Pass < total.Pass)
                {
                    Inliner.EmitLeaves(Emitter, outputs[index]);
                }

                CheckRanges(total.Combine.IlOffset);
            }
        }
        finally
        {
            operandValues.Clear();
            currentBlock = block;
        }
    }

    /// <summary>One copy of a running total: the accumulator input, the element, the gate, the placeholder and the copy number.</summary>
    private sealed record TotalStep(Value Input, ScalarValue Element, ScalarValue Gate, PrefixPlaceholder Output, int Pass);

    /// <summary>
    /// A running total of a loop: the combine, its accumulator operand (a header phi, or an array
    /// read), its element operand, the stores of the total, and the copies after the unroll.
    /// </summary>
    private sealed class RunningTotal(
        SsaValue combine, SsaValue accumulator, SsaValue element, bool isPhi, CombineKind kind, MethodInfo? method, bool accumulatorFirst,
        ImmutableArray<SsaValue> stores, ScalarKind valueKind)
    {
        public SsaValue Combine { get; } = combine;

        public SsaValue Accumulator { get; } = accumulator;

        public SsaValue Element { get; } = element;

        public bool IsPhi { get; } = isPhi;

        public CombineKind Kind { get; } = kind;

        public MethodInfo? Method { get; } = method;

        public bool AccumulatorFirst { get; } = accumulatorFirst;

        public ImmutableArray<SsaValue> Stores { get; } = stores;

        public ScalarKind ValueKind { get; } = valueKind;

        public List<TotalStep> Steps { get; } = [];

        public HashSet<ArrayValue> Arrays { get; } = [];

        /// <summary>The number of the copy that the unroll lowers, and after the unroll the number of its last copy.</summary>
        public int Pass { get; set; }

        /// <summary>True when the first copy showed that the total keeps its normal form.</summary>
        public bool Abandoned { get; set; }

        /// <summary>True for the header phi or the combine of a total that did not keep its normal form.</summary>
        public bool Holds(SsaValue value) => !Abandoned && (value == Accumulator || value == Combine);
    }
}
