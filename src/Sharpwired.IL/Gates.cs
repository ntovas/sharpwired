// No Handwired source for the walk. Handwired has no translator. This file holds the branch to select
// of the plan, section "Decisions", question 4. It computes the literal of each branch edge and the
// gate of each phi. It also holds the gate rules 1 to 4 (the emit applies the rules 5 to 7,
// Emitter.cs). The gate forms follow $REF/docs/expressions.md, rows "condition ? on : off" and "NOT,
// AND and OR".
//
// The gate of a phi at the merge block M is value(D), with D the immediate dominator of M. Let B be
// a block in the region from D to M. When B jumps to M, value(B) is the phi operand. When B branches
// on the literal c to T and F, value(B) is ITE(c, value(T), value(F)). An edge to a block that
// does not reach M gives no value, because the phi never reads it: ITE(c, x, none) = x. The return
// value of a method with two or more ret instructions is the gate of a merge after the ret blocks.
//
// Loops (plan, question 5, and LoopForms.cs): a region does not follow a back edge, so each region
// is acyclic. A region that enters a loop at its header continues at the exit edge of the loop. The
// reason: the unrolled loop always leaves through that edge. A constant literal follows only its
// edge, so a region does not read the values of a block that a constant branch skips.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The branch literals and the gates of one lowering.</summary>
internal sealed class Gates(OperationLowering lowering, SsaMethod ssa)
{
    private readonly Dictionary<int, ScalarValue> jumpLiterals = [];
    private readonly Dictionary<int, ImmutableArray<ScalarValue>> caseLiterals = [];
    private readonly Dictionary<SsaValue, Value> phiValues = [];
    private readonly HashSet<ScalarValue> results = [];

    private ControlFlowGraph Graph => ssa.Graph;

    /// <summary>NOT of a bit. NOT of a NOT with no wire gives its operand.</summary>
    public static ScalarValue Not(ScalarValue value) => value switch
    {
        ConstantValue constant => new ConstantValue(1 - constant.Number, ScalarKind.Bit),
        NotValue { Wire: null } negation => negation.Operand,
        _ => new NotValue(value),
    };

    /// <summary>The literals of a value in an AND (T is AndValue) or an OR (T is OrValue). An AND with no wire gives its literals in order (gate rule 4).</summary>
    public static ImmutableArray<ScalarValue> Literals<T>(ScalarValue value)
        where T : ScalarValue => value switch
        {
            AndValue { Wire: null } conjunction when typeof(T) == typeof(AndValue) => conjunction.Literals,
            OrValue { Wire: null } disjunction when typeof(T) == typeof(OrValue) => disjunction.Literals,
            _ => [value],
        };

    /// <summary>True when a gate made the value. The emit at a merge block emits only these values.</summary>
    public bool IsResult(ScalarValue value) => results.Contains(value);

    /// <summary>
    /// Computes the literal of each edge of a conditional branch or a switch. A branch on a
    /// comparison emits its Compare here, at its position in IL order.
    /// </summary>
    public void LowerTerminator(SsaBlock block)
    {
        var instruction = block.Terminator.Instruction;
        if (instruction is null || instruction.OpCode == ILOpCode.Ret || instruction.IsJump)
        {
            return;
        }

        var offset = instruction.Offset;
        var operands = block.Terminator.Operands;
        var index = block.Block.Index;
        switch (instruction.OpCode)
        {
            case ILOpCode.Brtrue or ILOpCode.Brtrue_s:
                jumpLiterals[index] = Bit(operands[0], offset);
                return;
            case ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                jumpLiterals[index] = Not(Bit(operands[0], offset));
                return;
            case ILOpCode.Switch:
                caseLiterals[index] = SwitchLiterals(operands[0], instruction.SwitchTargets.Length, offset);
                return;
            default:
                jumpLiterals[index] = BranchLiteral(instruction.OpCode, operands[0], operands[1], offset);
                return;
        }
    }

    /// <summary>The gate of a phi. The translator computes it one time.</summary>
    public Value PhiValue(SsaValue phi)
    {
        if (phiValues.TryGetValue(phi, out var known))
        {
            return known;
        }

        var merge = phi.Block;
        var predecessors = Graph.Blocks[merge].Predecessors;
        var value = Region(Graph.ImmediateDominator(merge), merge,
            block => lowering.Get(phi.Operands[predecessors.IndexOf(block)]), TypeOf(phi.Type), phi.IlOffset)
            ?? throw new InvalidOperationException($"the phi {phi} has no value on a path from its dominator");
        phiValues[phi] = value;
        return value;
    }

    /// <summary>Sets the value of a phi: the loop state of a header phi in one copy, or the value after a loop.</summary>
    public void SetPhiValue(SsaValue phi, Value value) => phiValues[phi] = value;

    /// <summary>Removes the values of the phis, so that the next copy of a loop computes them again.</summary>
    public void ForgetPhis(IEnumerable<SsaValue> phis)
    {
        foreach (var phi in phis)
        {
            phiValues.Remove(phi);
        }
    }

    /// <summary>The value of a header phi at the loop entry: the gate of the operands of the edges from outside the loop.</summary>
    public Value EntryValue(SsaValue phi)
    {
        var header = phi.Block;
        var predecessors = Graph.Blocks[header].Predecessors;
        return Region(Graph.ImmediateDominator(header), header,
            block => lowering.Get(phi.Operands[predecessors.IndexOf(block)]), TypeOf(phi.Type), phi.IlOffset)
            ?? throw new InvalidOperationException($"the phi {phi} has no value at the loop entry");
    }

    /// <summary>The value of a header phi after one copy of the loop: the gate of the operands of the back edges.</summary>
    public Value? BackEdgeValue(SsaValue phi)
    {
        var header = phi.Block;
        var predecessors = Graph.Blocks[header].Predecessors;
        return Region(header, header, block => lowering.Get(phi.Operands[predecessors.IndexOf(block)]), TypeOf(phi.Type), phi.IlOffset);
    }

    /// <summary>
    /// The bit that is 1 when control goes from the start block to the target block, with the
    /// literals of the blocks between them. The start block dominates the target block.
    /// </summary>
    public ScalarValue Reach(int start, int target, int ilOffset) => start == target
        ? new ConstantValue(1, ScalarKind.Bit)
        : (ScalarValue)Region(start, target, _ => new ConstantValue(1, ScalarKind.Bit), typeof(bool), ilOffset, new ConstantValue(0, ScalarKind.Bit))!;

    /// <summary>The bit that is 1 when one copy of the loop leaves through its exit edge.</summary>
    public ScalarValue ExitReach(NaturalLoop loop, int ilOffset) =>
        (ScalarValue)Region(loop.Header, Graph.ExitBlock(loop), _ => new ConstantValue(1, ScalarKind.Bit), typeof(bool), ilOffset,
            new ConstantValue(0, ScalarKind.Bit))!;

    /// <summary>True when the literal of the edge is the constant 0 in the current lowering of the source block.</summary>
    public bool EdgeIsDead(int from, int to)
    {
        var block = Graph.Blocks[from];
        if (!block.SwitchTargets.IsEmpty)
        {
            var cases = caseLiterals[from];
            if (cases.Any(literal => literal is ConstantValue { Number: not 0 }))
            {
                return !Enumerable.Range(0, cases.Length).Any(k => block.SwitchTargets[k] == to && cases[k] is ConstantValue { Number: not 0 });
            }

            return block.Next != to && Enumerable.Range(0, cases.Length).All(k => block.SwitchTargets[k] != to || cases[k] is ConstantValue { Number: 0 });
        }

        if (block.Jump is { } jump && block.Next is { } next && jump != next && jumpLiterals[from] is ConstantValue { Number: var bit })
        {
            return to == jump ? bit == 0 : bit != 0;
        }

        return false;
    }

    /// <summary>The return value of a method: the value of its one ret, or the gate of a merge after two or more ret blocks.</summary>
    public Value ReturnValue(IReadOnlyList<SsaBlock> returns, Type returnType)
    {
        if (returns is [var single])
        {
            return lowering.Get(single.Terminator.Operands[0]);
        }

        var dominator = returns[0].Block.Index;
        while (!returns.All(block => Graph.Dominates(dominator, block.Block.Index)))
        {
            dominator = Graph.ImmediateDominator(dominator);
        }

        return Region(dominator, -1, block => lowering.Get(ssa.Blocks[block].Terminator.Operands[0]), returnType, returns[0].Terminator.Instruction!.Offset)
            ?? throw new InvalidOperationException("the return value has no value on a path from its dominator");
    }

    /// <summary>
    /// The gate rules 1 to 4: ITE(condition, on, off). A null operand comes from an edge that does not
    /// reach the merge. The gate of two aggregates or two arrays is the gate of each field or element.
    /// A gate of two different arrays when one of them is a local array is refused at the IL offset.
    /// </summary>
    public Value? Make(ScalarValue condition, Value? on, Value? off, Type? type, int ilOffset)
    {
        if (on is null || off is null || ReferenceEquals(on, off))
        {
            return on ?? off;
        }

        if (condition is NotValue { Wire: null } negation)
        {
            return Make(negation.Operand, off, on, type, ilOffset);
        }

        if (condition is ConstantValue constant)
        {
            return constant.Number != 0 ? on : off;
        }

        switch (on, off)
        {
            case (ScalarValue x, ScalarValue y):
                return MakeScalar(condition, x, y, KindOf(type, x, y), ilOffset);
            case (AggregateValue x, AggregateValue y) when x.Type == y.Type:
                {
                    var fields = ImmutableDictionary.CreateBuilder<string, Value>(StringComparer.Ordinal);
                    foreach (var field in DataTypes.InstanceFields(x.Type).Where(field => x.Fields.ContainsKey(field.Name)))
                    {
                        fields[field.Name] = Make(condition, x.Fields[field.Name], y.Fields[field.Name], field.FieldType, ilOffset)!;
                    }

                    return fields.All(pair => ReferenceEquals(pair.Value, x.Fields[pair.Key])) ? x : new AggregateValue(x.Type, fields.ToImmutable());
                }

            case (ArrayValue x, ArrayValue y) when x.IsLocal || y.IsLocal:
                // The lowering changes a local array in place. A gate of two arrays is a new array
                // value. So a later store through the chosen variable or through the first array
                // does not reach the other one (review of task 4, finding 2).
                throw lowering.Refuse(ilOffset, "a choice between two arrays", "the program changes an array in place, so a branch must not choose between two arrays");
            case (ArrayValue x, ArrayValue y) when x.ElementType == y.ElementType && x.Elements.Length == y.Elements.Length:
                {
                    Value[] elements = [.. x.Elements.Zip(y.Elements, (a, b) => Make(condition, a, b, x.ElementType, ilOffset)!)];
                    return elements.Select((element, index) => ReferenceEquals(element, x.Elements[index])).All(same => same)
                        ? x
                        : new ArrayValue(x.ElementType, elements, x.IsLocal && y.IsLocal);
                }
            default:
                throw new InvalidOperationException($"a gate of a {on.GetType().Name} and a {off.GetType().Name}");
        }
    }

    // value(B) for the blocks of the region from the dominator to the merge (-1: a merge after the
    // ret blocks). Here operandOf gives the phi operand of a predecessor of the merge, or the return
    // value
    // of a ret block. An edge that does not reach the merge gives missing. Each block gets its value
    // one time. A back edge reaches only a merge that is its target. A loop header other than the
    // dominator, in a loop that does not hold the merge, continues at the exit edge of its loop.
    private Value? Region(int dominator, int merge, Func<int, Value> operandOf, Type? type, int ilOffset, Value? missing = null)
    {
        var values = new Dictionary<int, Value?>();
        var reaches = new Dictionary<int, bool>();

        // The loop that a region enters at its header, or null.
        NaturalLoop? Entered(int block) =>
            block != dominator && Graph.LoopAt(block) is { } loop && (merge < 0 || !loop.Body.Contains(merge)) ? loop : null;

        bool Reaches(int block)
        {
            if (merge < 0)
            {
                return true;
            }

            if (!reaches.TryGetValue(block, out var found))
            {
                found = Entered(block) is { } loop
                    ? loop.ExitEdges[0].To == merge || Reaches(loop.ExitEdges[0].To)
                    : Graph.Blocks[block].Successors.Any(successor =>
                        successor == merge || (!Graph.IsBackEdge(block, successor) && Reaches(successor)));
                reaches[block] = found;
            }

            return found;
        }

        Value? Edge(int from, int to) =>
            to == merge ? operandOf(from) : !Graph.IsBackEdge(from, to) && Reaches(to) ? ValueAt(to) : missing;

        Value? ValueAt(int index)
        {
            if (values.TryGetValue(index, out var known))
            {
                return known;
            }

            var block = Graph.Blocks[index];
            Value? value;
            if (Entered(index) is { } loop)
            {
                value = Edge(loop.ExitEdges[0].From, loop.ExitEdges[0].To);
            }
            else if (block.Terminator?.OpCode == ILOpCode.Ret)
            {
                value = merge < 0 ? operandOf(index) : missing;
            }
            else if (!block.SwitchTargets.IsEmpty)
            {
                var cases = caseLiterals[index];
                var taken = Enumerable.Range(0, cases.Length).FirstOrDefault(k => cases[k] is ConstantValue { Number: not 0 }, -1);
                if (taken >= 0)
                {
                    value = Edge(index, block.SwitchTargets[taken]);
                }
                else
                {
                    value = Edge(index, block.Next!.Value);
                    for (var k = cases.Length - 1; k >= 0; k--)
                    {
                        if (cases[k] is not ConstantValue)
                        {
                            value = Make(cases[k], Edge(index, block.SwitchTargets[k]), value, type, BranchOffset(index, ilOffset));
                        }
                    }
                }
            }
            else if (block.Jump is { } jump && block.Next is { } next)
            {
                var literal = jumpLiterals[index];
                value = literal is ConstantValue { Number: var bit }
                    ? Edge(index, bit != 0 ? jump : next)
                    : Make(literal, Edge(index, jump), Edge(index, next), type, BranchOffset(index, ilOffset));
            }
            else
            {
                value = Edge(index, block.Jump ?? block.Next!.Value);
            }

            values[index] = value;
            return value;
        }

        return ValueAt(dominator);
    }

    // The IL offset of the branch at the end of a block. A gate of this branch has this offset, so a
    // refusal of the gate names the line of the branch (final fix wave, item E).
    private int BranchOffset(int block, int fallback) => ssa.Blocks[block].Terminator.Instruction?.Offset ?? fallback;

    private ScalarValue MakeScalar(ScalarValue condition, ScalarValue on, ScalarValue off, ScalarKind kind, int ilOffset)
    {
        // Rule 1: ITE(c, x, x) = x.
        if (Same(on, off))
        {
            return on;
        }

        // Rule 2, for bits.
        if (kind == ScalarKind.Bit && on is ConstantValue { Number: var k1 })
        {
            return k1 != 0
                ? off is ConstantValue { Number: 0 } ? condition : Or(condition, off)
                : off is ConstantValue { Number: 1 } ? Record(Not(condition)) : And(Record(Not(condition)), off);
        }

        if (kind == ScalarKind.Bit && off is ConstantValue { Number: var k0 })
        {
            return k0 == 0 ? And(condition, on) : Or(Record(Not(condition)), on);
        }

        // Rule 3: ITE(c1, ITE(c2, x, y), y) = ITE(AND(c1, c2), x, y); ITE(c1, x, ITE(c2, x, y)) = ITE(OR(c1, c2), x, y).
        if (on is GateValue { Wire: null } inner && Same(inner.Off, off))
        {
            return MakeScalar(And(condition, inner.Condition), inner.On, off, kind, ilOffset);
        }

        if (off is GateValue { Wire: null } other && Same(other.On, on))
        {
            return MakeScalar(Or(condition, other.Condition), on, other.Off, kind, ilOffset);
        }

        return Record(new GateValue(condition, on, off, kind, ilOffset, lowering));
    }

    private AndValue And(ScalarValue left, ScalarValue right) =>
        Record(new AndValue([.. Literals<AndValue>(left), .. Literals<AndValue>(right)]));

    private OrValue Or(ScalarValue left, ScalarValue right) =>
        Record(new OrValue([.. Literals<OrValue>(left), .. Literals<OrValue>(right)]));

    private T Record<T>(T value)
        where T : ScalarValue
    {
        results.Add(value);
        return value;
    }

    // Two values are the same when they are one object, one constant, or one wire.
    private static bool Same(ScalarValue left, ScalarValue right) =>
        ReferenceEquals(left, right)
        || (left is ConstantValue a && right is ConstantValue b && BitConverter.DoubleToInt64Bits(a.Number) == BitConverter.DoubleToInt64Bits(b.Number))
        || (left.Wire is { } wire && wire == right.Wire);

    // The kind of a gate: the type of the phi, or the kinds of the two values. The IL has int
    // constants 0 and 1 in a bool phi, so the type of the phi decides.
    private static ScalarKind KindOf(Type? type, ScalarValue on, ScalarValue off)
    {
        if (type is not null && DataTypes.KindOf(type) is { } kind)
        {
            return kind;
        }

        return on.Kind == ScalarKind.Float || off.Kind == ScalarKind.Float ? ScalarKind.Float
            : on.Kind == ScalarKind.Bit && off.Kind == ScalarKind.Bit ? ScalarKind.Bit
            : ScalarKind.Int;
    }

    /// <summary>The .NET type of an SSA type: float, int, bool, or the type of an aggregate or an array.</summary>
    public static Type? TypeOf(SsaType type) => type.Kind switch
    {
        SsaTypeKind.Float => typeof(float),
        SsaTypeKind.Int => typeof(int),
        SsaTypeKind.Bit => typeof(bool),
        _ => type.ClrType,
    };

    // brtrue and brfalse read a bit: a bool value, a comparison or the constant 0 or 1. On an int
    // they read x != 0, as Roslyn writes the branch form of x != 0 (final fix wave, item D). An int
    // constant k gives the constant bit k != 0. Release writes "if (Mode != 0)" with a constant Mode
    // as "ldc.i4 Mode; brtrue". Debug writes "s == 0 ? a : b" in a constant loop as "brfalse" on the
    // constant s (phase 4 carried item c).
    private ScalarValue Bit(SsaValue operand, int offset)
    {
        var value = lowering.Scalar(operand, offset);
        if (value is ConstantValue { Kind: ScalarKind.Int, Number: var number })
        {
            return new ConstantValue(number != 0 ? 1 : 0, ScalarKind.Bit);
        }

        if (value.Kind == ScalarKind.Int)
        {
            return Not(lowering.CompareValues(value, new ConstantValue(0, ScalarKind.Int), Relation.Equal, offset));
        }

        return value.Kind == ScalarKind.Bit || value is ConstantValue { Number: 0 or 1 }
            ? value
            : throw lowering.Refuse(offset, "a branch on a number", "the program branches on a bool value or on a comparison; compare the number with a value");
    }

    // The relation map of question 4. The .un forms on float give the other relation, with NOT on
    // the jump edge. The reason: the declared ranges are finite and no operand is NaN.
    private ScalarValue BranchLiteral(ILOpCode op, SsaValue left, SsaValue right, int offset)
    {
        var (relation, jumpReadsBit, unsigned) = op switch
        {
            ILOpCode.Beq or ILOpCode.Beq_s => (Relation.Equal, true, false),
            ILOpCode.Bne_un or ILOpCode.Bne_un_s => (Relation.Equal, false, false),
            ILOpCode.Bge or ILOpCode.Bge_s => (Relation.GreaterOrEqual, true, false),
            ILOpCode.Bgt or ILOpCode.Bgt_s => (Relation.Greater, true, false),
            ILOpCode.Ble or ILOpCode.Ble_s => (Relation.LessOrEqual, true, false),
            ILOpCode.Blt or ILOpCode.Blt_s => (Relation.Less, true, false),
            ILOpCode.Bge_un or ILOpCode.Bge_un_s => (Relation.Less, false, true),
            ILOpCode.Bgt_un or ILOpCode.Bgt_un_s => (Relation.LessOrEqual, false, true),
            ILOpCode.Ble_un or ILOpCode.Ble_un_s => (Relation.Greater, false, true),
            ILOpCode.Blt_un or ILOpCode.Blt_un_s => (Relation.GreaterOrEqual, false, true),
            _ => throw new InvalidOperationException($"the branch {IlDecoder.NameOf(op)} has no relation"),
        };
        var bit = lowering.CompareOperands(left, right, relation, unsigned, offset);
        return jumpReadsBit ? bit : Not(bit);
    }

    // The switch opcode on an int: the edge of case k reads Compare(v, Equal, k). The default edge
    // is the last off value of the nested gates, which is the AND of the NOTs of the cases.
    private ImmutableArray<ScalarValue> SwitchLiterals(SsaValue operand, int count, int offset)
    {
        var value = lowering.Scalar(operand, offset);
        if (value.Kind != ScalarKind.Int)
        {
            throw lowering.Refuse(offset, "a switch on a value that is not an int", "the switch instruction reads an int with a declared range");
        }

        return [.. Enumerable.Range(0, count).Select(k => lowering.CompareValues(value, new ConstantValue(k, ScalarKind.Int), Relation.Equal, offset))];
    }
}
