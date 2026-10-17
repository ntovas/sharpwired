// No Handwired source. Handwired has no translator. The SSA (static single assignment) form of the
// plan, section "Decisions", question 3: an abstract IL stack for each block; locals, arguments and
// stack slots as variables; the phis of Braun, Buchwald, Hack, Leissa, Mallon and Zwinkau, "Simple
// and Efficient Construction of Static Single Assignment Form" (2013), on the control-flow graph;
// and a type for each value (Float, Int, Bit, Aggregate, Array, Address). The model types are in
// SsaModel.cs.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Sharpwired.IL;

/// <summary>Builds the SSA form of a control-flow graph.</summary>
internal sealed class SsaBuilder
{
    private readonly ControlFlowGraph graph;
    private readonly DecodedMethod method;
    private readonly Dictionary<SsaVariable, SsaValue>[] currentDefinitions;
    private readonly Dictionary<SsaVariable, SsaValue>[] incompletePhis;
    private readonly List<SsaValue>[] phis;
    private readonly List<SsaValue>[] operations;
    private readonly SsaTerminator[] terminators;
    private readonly bool[] sealedBlocks;
    private readonly bool[] filledBlocks;
    private readonly int?[] entryDepths;
    private readonly Dictionary<SsaValue, List<SsaValue>> phiUsers = [];
    private int nextId;
    private int currentBlock;
    private int currentOffset;

    private SsaBuilder(ControlFlowGraph graph)
    {
        this.graph = graph;
        method = graph.Method;
        var count = graph.Blocks.Length;
        currentDefinitions = NewArray<Dictionary<SsaVariable, SsaValue>>(count);
        incompletePhis = NewArray<Dictionary<SsaVariable, SsaValue>>(count);
        phis = NewArray<List<SsaValue>>(count);
        operations = NewArray<List<SsaValue>>(count);
        terminators = new SsaTerminator[count];
        sealedBlocks = new bool[count];
        filledBlocks = new bool[count];
        entryDepths = new int?[count];
        entryDepths[0] = 0;
    }

    /// <summary>
    /// Builds the SSA form. Throws <see cref="TranslationException"/> for a value type that the
    /// program does not have, a merge with different stack depths or value types, and an address
    /// that the translator cannot follow.
    /// </summary>
    public static SsaMethod Build(ControlFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new SsaBuilder(graph).Run();
    }

    private static T[] NewArray<T>(int count)
        where T : new() => Enumerable.Range(0, count).Select(_ => new T()).ToArray();

    private SsaMethod Run()
    {
        foreach (var block in graph.ReversePostorder)
        {
            if (!sealedBlocks[block] && graph.Blocks[block].Predecessors.All(p => filledBlocks[p]))
            {
                Seal(block);
            }

            Fill(block);
            filledBlocks[block] = true;
            foreach (var successor in graph.Blocks[block].Successors)
            {
                if (!sealedBlocks[successor] && graph.Blocks[successor].Predecessors.All(p => filledBlocks[p]))
                {
                    Seal(successor);
                }
            }
        }

        RemoveTrivialPhis();
        var blocks = FinishBlocks();
        SsaTypes.Infer(method, blocks);
        return new SsaMethod(graph, blocks);
    }

    private void Fill(int block)
    {
        currentBlock = block;
        var basic = graph.Blocks[block];
        currentOffset = basic.StartOffset;
        var depth = entryDepths[block] ?? throw new InvalidOperationException($"the block {block} has no entry depth");
        var stack = new List<SsaValue>();
        for (var i = 0; i < depth; i++)
        {
            stack.Add(Read(new SsaVariable(SsaVariableKind.StackSlot, i), block));
        }

        terminators[block] = new SsaTerminator(null, []);
        for (var i = 0; i < basic.InstructionCount; i++)
        {
            var instruction = method.Instructions[basic.FirstInstruction + i];
            currentOffset = instruction.Offset;
            Apply(instruction, stack);
        }

        for (var i = 0; i < stack.Count; i++)
        {
            Write(new SsaVariable(SsaVariableKind.StackSlot, i), block, stack[i]);
        }

        foreach (var successor in basic.Successors)
        {
            if (entryDepths[successor] is { } expected && expected != stack.Count)
            {
                throw method.Refuse(graph.Blocks[successor].StartOffset, "a merge with different stack depths",
                    "the IL stack must have the same depth on each edge into a block");
            }

            entryDepths[successor] = stack.Count;
        }
    }

    private void Apply(IlInstruction instruction, List<SsaValue> stack)
    {
        var op = instruction.OpCode;
        switch (op)
        {
            case ILOpCode.Nop or ILOpCode.Tail:
                return;
            case ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                stack.Add(Read(new SsaVariable(SsaVariableKind.Argument, instruction.IntOperand), currentBlock));
                return;
            case ILOpCode.Starg or ILOpCode.Starg_s:
                Write(new SsaVariable(SsaVariableKind.Argument, instruction.IntOperand), currentBlock,
                    Coerce(Pop(stack), ArgumentType(instruction.IntOperand)));
                return;
            case ILOpCode.Ldloc or ILOpCode.Ldloc_s or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3:
                stack.Add(Read(new SsaVariable(SsaVariableKind.Local, instruction.IntOperand), currentBlock));
                return;
            case ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3:
                Write(new SsaVariable(SsaVariableKind.Local, instruction.IntOperand), currentBlock,
                    Coerce(Pop(stack), method.LocalTypes[instruction.IntOperand]));
                return;
            case ILOpCode.Ldarga or ILOpCode.Ldarga_s:
                stack.Add(NewAddress(op, new SsaAddress(new SsaVariable(SsaVariableKind.Argument, instruction.IntOperand), null, []),
                    ArgumentType(instruction.IntOperand), []));
                return;
            case ILOpCode.Ldloca or ILOpCode.Ldloca_s:
                stack.Add(NewAddress(op, new SsaAddress(new SsaVariable(SsaVariableKind.Local, instruction.IntOperand), null, []),
                    method.LocalTypes[instruction.IntOperand], []));
                return;
            case ILOpCode.Ldc_r4:
                stack.Add(NewConstant(instruction.Operand!, SsaType.Float));
                return;
            case ILOpCode.Dup:
                stack.Add(Peek(stack));
                return;
            case ILOpCode.Pop:
                Pop(stack);
                return;
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                or ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    stack.Add(NewOperation(op, null, [left, right]));
                    return;
                }

            case ILOpCode.Neg or ILOpCode.Conv_r4 or ILOpCode.Conv_i4 or ILOpCode.Ldlen:
                stack.Add(NewOperation(op, null, [Pop(stack)]));
                return;
            case ILOpCode.Ret:
                Terminate(instruction, ReturnType() == typeof(void) ? [] : [Coerce(Pop(stack), ReturnType())]);
                return;
            case ILOpCode.Switch or ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s:
                Terminate(instruction, [Pop(stack)]);
                return;
            case ILOpCode.Br or ILOpCode.Br_s:
                Terminate(instruction, []);
                return;
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                ApplyCall(instruction, stack);
                return;
            default:
                if (instruction.IsBranch)
                {
                    var right = Pop(stack);
                    var left = Pop(stack);
                    Terminate(instruction, [left, right]);
                    return;
                }

                if (instruction.Operand is int constant && instruction.Name.StartsWith("ldc.i4", StringComparison.Ordinal))
                {
                    stack.Add(NewConstant(constant, SsaType.Int));
                    return;
                }

                ApplyMemory(instruction, stack);
                return;
        }
    }

    private void ApplyMemory(IlInstruction instruction, List<SsaValue> stack)
    {
        var op = instruction.OpCode;
        switch (op)
        {
            case ILOpCode.Initobj:
                WriteThrough(PopAddress(stack), NewOperation(ILOpCode.Initobj, instruction.Operand, []));
                return;
            case ILOpCode.Ldobj:
                stack.Add(Dereference(PopAddress(stack)));
                return;
            case ILOpCode.Ldfld:
                {
                    var field = (FieldInfo)instruction.Operand!;
                    var source = Pop(stack);
                    stack.Add(source.Address is { } address
                        ? Dereference(address with { Fields = address.Fields.Add(field) })
                        : NewOperation(ILOpCode.Ldfld, field, [source]));
                    return;
                }

            case ILOpCode.Ldflda:
                {
                    var field = (FieldInfo)instruction.Operand!;
                    var source = Pop(stack);
                    var address = source.Address is { } inner
                        ? inner with { Fields = inner.Fields.Add(field) }
                        : new SsaAddress(null, source, [field]);
                    stack.Add(NewAddress(op, address, field.FieldType, source.Address is null ? [source] : []));
                    return;
                }

            case ILOpCode.Stfld:
                {
                    var field = (FieldInfo)instruction.Operand!;
                    var value = Coerce(Pop(stack), field.FieldType);
                    var target = Pop(stack);
                    if (target.Address is not { } address)
                    {
                        throw method.Refuse(currentOffset, "a field store into an object",
                            "the program changes only a field of a local or an argument");
                    }

                    WriteThrough(address with { Fields = address.Fields.Add(field) }, value);
                    return;
                }

            case ILOpCode.Ldsfld:
                stack.Add(NewOperation(op, instruction.Operand, []));
                return;
            case ILOpCode.Ldelema:
                {
                    var index = Pop(stack);
                    var array = Pop(stack);
                    stack.Add(NewAddress(op, new SsaAddress(null, NewOperation(ILOpCode.Ldelem, instruction.Operand, [array, index]), []),
                        (Type)instruction.Operand!, [array, index]));
                    return;
                }

            case ILOpCode.Newarr:
                stack.Add(NewOperation(op, instruction.Operand, [Pop(stack)]));
                return;
            case ILOpCode.Ldelem or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i1:
                {
                    var index = Pop(stack);
                    var array = Pop(stack);
                    stack.Add(NewOperation(op, instruction.Operand, [array, index]));
                    return;
                }

            case ILOpCode.Stelem or ILOpCode.Stelem_r4 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i1:
                {
                    var value = Pop(stack);
                    var index = Pop(stack);
                    var array = Pop(stack);
                    NewOperation(op, instruction.Operand, [array, index, value]);
                    return;
                }

            default:
                throw new InvalidOperationException($"the SSA builder has no rule for the accepted instruction {instruction.Name}");
        }
    }

    private void ApplyCall(IlInstruction instruction, List<SsaValue> stack)
    {
        var callee = (MethodBase)instruction.Operand!;
        var parameters = callee.GetParameters();
        var arguments = new SsaValue[parameters.Length];
        for (var i = parameters.Length - 1; i >= 0; i--)
        {
            var argument = Pop(stack);
            if (argument.Address is not null)
            {
                throw method.Refuse(currentOffset, "an address as a call argument", "the program passes values, not references");
            }

            arguments[i] = Coerce(argument, parameters[i].ParameterType);
        }

        if (instruction.OpCode == ILOpCode.Newobj)
        {
            stack.Add(NewOperation(ILOpCode.Newobj, callee, [.. arguments]));
            return;
        }

        var operands = new List<SsaValue>();
        if (!callee.IsStatic)
        {
            var receiver = Pop(stack);
            if (callee is ConstructorInfo && receiver.Address is { } target)
            {
                // A struct constructor on an address, as Roslyn writes "var p = new S(...)": a new value of the variable.
                WriteThrough(target, NewOperation(ILOpCode.Newobj, callee, [.. arguments]));
                return;
            }

            operands.Add(receiver.Address is { } address ? Dereference(address) : receiver);
        }

        operands.AddRange(arguments);
        var call = NewOperation(instruction.OpCode, callee, [.. operands]);
        if (callee is MethodInfo info && info.ReturnType != typeof(void))
        {
            stack.Add(call);
        }
    }

    private SsaValue Dereference(SsaAddress address)
    {
        var current = address.Root is { } root ? Read(root, currentBlock) : address.Base!;
        foreach (var field in address.Fields)
        {
            current = NewOperation(ILOpCode.Ldfld, field, [current]);
        }

        return current;
    }

    private void WriteThrough(SsaAddress address, SsaValue value)
    {
        if (address.Root is not { } root)
        {
            throw method.Refuse(currentOffset, "a store through an address of an object or an array element",
                "the program changes only a local or an argument");
        }

        if (address.Fields.IsEmpty)
        {
            Write(root, currentBlock, value);
            return;
        }

        var old = Read(root, currentBlock);
        Write(root, currentBlock, NewOperation(ILOpCode.Stfld, address.Fields, [old, value]));
    }

    private void Terminate(IlInstruction instruction, ImmutableArray<SsaValue> operands)
    {
        if (operands.Any(o => o.Address is not null))
        {
            throw method.Refuse(currentOffset, "an address as a branch or return value", "the program passes values, not references");
        }

        terminators[currentBlock] = new SsaTerminator(instruction, operands);
    }

    // Braun et al., algorithms 1 to 4.

    private void Write(SsaVariable variable, int block, SsaValue value) => currentDefinitions[block][variable] = value;

    private SsaValue Read(SsaVariable variable, int block) =>
        currentDefinitions[block].TryGetValue(variable, out var value) ? value.Resolve() : ReadRecursive(variable, block);

    private SsaValue ReadRecursive(SsaVariable variable, int block)
    {
        SsaValue value;
        var predecessors = graph.Blocks[block].Predecessors;
        if (!sealedBlocks[block])
        {
            value = NewPhi(block, variable);
            incompletePhis[block][variable] = value;
        }
        else if (block == 0)
        {
            value = EntryValue(variable);
        }
        else if (predecessors.Length == 1)
        {
            value = Read(variable, predecessors[0]);
        }
        else
        {
            var phi = NewPhi(block, variable);
            Write(variable, block, phi);
            value = AddPhiOperands(variable, phi);
        }

        Write(variable, block, value);
        return value;
    }

    private SsaValue AddPhiOperands(SsaVariable variable, SsaValue phi)
    {
        foreach (var predecessor in graph.Blocks[phi.Block].Predecessors)
        {
            var operand = Read(variable, predecessor);
            phi.OperandList.Add(operand);
            if (operand.Kind == SsaValueKind.Phi)
            {
                UsersOf(operand).Add(phi);
            }
        }

        return TryRemoveTrivialPhi(phi);
    }

    private SsaValue TryRemoveTrivialPhi(SsaValue phi)
    {
        if (phi.IsRemoved)
        {
            return phi.Resolve();
        }

        SsaValue? same = null;
        foreach (var operand in phi.OperandList.Select(o => o.Resolve()))
        {
            if (operand == same || operand == phi)
            {
                continue;
            }

            if (same is not null)
            {
                return phi;
            }

            same = operand;
        }

        if (same is null)
        {
            throw new InvalidOperationException($"the phi of {phi.Variable} in block {phi.Block} has no operand from the entry");
        }

        phi.ReplacedBy = same;
        if (phiUsers.Remove(phi, out var users))
        {
            if (same.Kind == SsaValueKind.Phi)
            {
                UsersOf(same).AddRange(users);
            }

            foreach (var user in users.Where(u => u != phi && !u.IsRemoved))
            {
                TryRemoveTrivialPhi(user);
            }
        }

        return same.Resolve();
    }

    private void Seal(int block)
    {
        sealedBlocks[block] = true;
        foreach (var (variable, phi) in incompletePhis[block])
        {
            AddPhiOperands(variable, phi);
        }

        incompletePhis[block].Clear();
    }

    private void RemoveTrivialPhis()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var phi in phis.SelectMany(p => p).Where(p => !p.IsRemoved).ToArray())
            {
                if (TryRemoveTrivialPhi(phi) != phi)
                {
                    changed = true;
                }
            }
        }
        while (changed);
    }

    private ImmutableArray<SsaBlock> FinishBlocks()
    {
        foreach (var value in phis.SelectMany(p => p).Concat(operations.SelectMany(o => o)))
        {
            value.Freeze();
        }

        return [.. graph.Blocks.Select(b => new SsaBlock(
            b,
            [.. phis[b.Index].Where(p => !p.IsRemoved)],
            [.. operations[b.Index]],
            terminators[b.Index] with { Operands = [.. terminators[b.Index].Operands.Select(o => o.Resolve())] }))];
    }

    private List<SsaValue> UsersOf(SsaValue phi)
    {
        if (!phiUsers.TryGetValue(phi, out var users))
        {
            users = [];
            phiUsers[phi] = users;
        }

        return users;
    }

    // Value creation.

    private SsaValue EntryValue(SsaVariable variable) => variable.Kind switch
    {
        SsaVariableKind.Argument => new SsaValue(nextId++, SsaValueKind.Argument, SsaTypes.FromClr(method, ArgumentType(variable.Index), currentOffset))
        {
            Index = variable.Index,
        },
        SsaVariableKind.Local => new SsaValue(nextId++, SsaValueKind.InitialLocal, SsaTypes.FromClr(method, method.LocalTypes[variable.Index], currentOffset))
        {
            Index = variable.Index,
        },
        _ => throw new InvalidOperationException($"the entry block reads the stack slot {variable.Index}"),
    };

    private SsaValue NewPhi(int block, SsaVariable variable)
    {
        var type = variable.Kind switch
        {
            SsaVariableKind.Argument => SsaTypes.FromClr(method, ArgumentType(variable.Index), graph.Blocks[block].StartOffset),
            SsaVariableKind.Local => SsaTypes.FromClr(method, method.LocalTypes[variable.Index], graph.Blocks[block].StartOffset),
            _ => SsaType.None,
        };
        var phi = new SsaValue(nextId++, SsaValueKind.Phi, type)
        {
            Block = block,
            Variable = variable,
            IlOffset = graph.Blocks[block].StartOffset,
        };
        phis[block].Add(phi);
        return phi;
    }

    private SsaValue NewConstant(object constant, SsaType type) => new(nextId++, SsaValueKind.Constant, type) { Constant = constant };

    private SsaValue NewOperation(ILOpCode op, object? member, ImmutableArray<SsaValue> operands)
    {
        var value = new SsaValue(nextId++, SsaValueKind.Operation, SsaType.None)
        {
            Op = op,
            Member = member,
            Block = currentBlock,
            IlOffset = currentOffset,
        };
        value.OperandList.AddRange(operands);
        operations[currentBlock].Add(value);
        return value;
    }

    private SsaValue NewAddress(ILOpCode op, SsaAddress address, Type pointee, ImmutableArray<SsaValue> operands)
    {
        var value = NewOperation(op, null, operands);
        value.Address = address;
        value.Type = new SsaType(SsaTypeKind.Address, pointee);
        return value;
    }

    // A bool destination turns the int constants 0 and 1 into bit constants.
    private SsaValue Coerce(SsaValue value, Type destination) =>
        destination == typeof(bool) && value.Kind == SsaValueKind.Constant && value.Constant is int and (0 or 1) && value.Type.Kind == SsaTypeKind.Int
            ? NewConstant(value.Constant, SsaType.Bit)
            : value;

    private Type ArgumentType(int index)
    {
        if (!method.Method.IsStatic)
        {
            if (index == 0)
            {
                return method.Method.DeclaringType!;
            }

            index--;
        }

        return method.Method.GetParameters()[index].ParameterType;
    }

    private Type ReturnType() => method.Method is MethodInfo info ? info.ReturnType : typeof(void);

    private SsaValue Pop(List<SsaValue> stack)
    {
        var value = Peek(stack);
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private SsaValue Peek(List<SsaValue> stack) => stack.Count > 0
        ? stack[^1].Resolve()
        : throw method.Refuse(currentOffset, "an IL stack with no value", "the IL of the method is not valid");

    private SsaAddress PopAddress(List<SsaValue> stack) => Pop(stack).Address
        ?? throw method.Refuse(currentOffset, "an object address", "the program reads and changes values through a local or an argument only");
}
