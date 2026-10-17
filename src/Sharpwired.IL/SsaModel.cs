// No Handwired source. Handwired has no translator. The model of the SSA form of the plan, section
// "Decisions", question 3: variables, values, phis, blocks, and the value types. Bit comes from a
// bool field, local, parameter or return type, from a comparison, from and, or, xor of bits, and
// from a phi whose inputs are bits or the constants 0 and 1. SsaBuilder.cs builds the form.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Sharpwired.IL;

internal enum SsaTypeKind
{
    /// <summary>No value: a void call or an array store.</summary>
    None,
    Float,
    Int,
    Bit,
    Aggregate,
    Array,
    Address,
}

/// <summary>
/// The type of an SSA value. ClrType is the .NET type of an aggregate or an array, or the type that
/// an address points to. Element and Length describe an array; Length is null when it is not a constant.
/// </summary>
internal sealed record SsaType(SsaTypeKind Kind, Type? ClrType = null, SsaType? Element = null, int? Length = null)
{
    public static readonly SsaType None = new(SsaTypeKind.None);
    public static readonly SsaType Float = new(SsaTypeKind.Float);
    public static readonly SsaType Int = new(SsaTypeKind.Int);
    public static readonly SsaType Bit = new(SsaTypeKind.Bit);

    public override string ToString() => Kind switch
    {
        SsaTypeKind.Aggregate or SsaTypeKind.Address => $"{Kind}({ClrType?.Name})",
        SsaTypeKind.Array => $"Array({Element}, {Length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"})",
        _ => Kind.ToString(),
    };
}

internal enum SsaVariableKind
{
    Argument,
    Local,
    StackSlot,
}

/// <summary>A variable of the SSA construction: an argument, a local or a slot of the IL stack at a block boundary.</summary>
internal readonly record struct SsaVariable(SsaVariableKind Kind, int Index);

internal enum SsaValueKind
{
    /// <summary>The value of an argument at the method entry.</summary>
    Argument,

    /// <summary>The zero value of a local at the method entry (the IL locals are zero initialized).</summary>
    InitialLocal,
    Constant,
    Operation,
    Phi,
}

/// <summary>
/// The location of an address: a variable (Root) or a value (Base, for a field of an object or an
/// array element), and the fields that follow.
/// </summary>
internal sealed record SsaAddress(SsaVariable? Root, SsaValue? Base, ImmutableArray<FieldInfo> Fields);

/// <summary>
/// One SSA value. An operation has the opcode of its IL instruction (Op), the resolved member
/// (Member) and its operands. A field read through an address gives an ldfld operation; a field
/// store through an address of a variable gives an stfld operation with the new value of the
/// variable (Member is the field path); a constructor call on an address gives a newobj operation.
/// A phi has one operand for each predecessor of its block, in predecessor order.
/// </summary>
internal sealed class SsaValue
{
    internal SsaValue(int id, SsaValueKind kind, SsaType type)
    {
        Id = id;
        Kind = kind;
        Type = type;
    }

    public int Id { get; }

    public SsaValueKind Kind { get; }

    public SsaType Type { get; internal set; }

    /// <summary>The argument or local index of an entry value, otherwise -1.</summary>
    public int Index { get; init; } = -1;

    /// <summary>The int or float value of a constant. A bit constant holds the int 0 or 1.</summary>
    public object? Constant { get; init; }

    public ILOpCode Op { get; init; }

    public object? Member { get; init; }

    /// <summary>The block of an operation, a phi or a constant, otherwise -1.</summary>
    public int Block { get; init; } = -1;

    /// <summary>The variable of a phi.</summary>
    public SsaVariable Variable { get; init; }

    /// <summary>
    /// The IL offset of an operation or of the instruction that loads a constant, the start offset of
    /// the block of a phi, or the offset of the first read of an entry value.
    /// </summary>
    public int IlOffset { get; init; }

    public ImmutableArray<SsaValue> Operands { get; private set; } = [];

    /// <summary>True for a trivial phi that the construction replaced with another value.</summary>
    public bool IsRemoved => ReplacedBy is not null;

    internal SsaAddress? Address { get; set; }

    internal List<SsaValue> OperandList { get; } = [];

    internal SsaValue? ReplacedBy { get; set; }

    internal SsaValue Resolve()
    {
        var value = this;
        while (value.ReplacedBy is { } replacement)
        {
            value = replacement;
        }

        return value;
    }

    internal void Freeze() => Operands = [.. OperandList.Select(o => o.Resolve())];

    public override string ToString() => Kind switch
    {
        SsaValueKind.Argument => $"v{Id} = arg{Index} : {Type}",
        SsaValueKind.InitialLocal => $"v{Id} = init{Index} : {Type}",
        SsaValueKind.Constant => $"v{Id} = {Constant} : {Type}",
        SsaValueKind.Phi => $"v{Id} = phi {Variable} ({string.Join(", ", Operands.Select(o => "v" + o.Id))}) : {Type}",
        _ => $"v{Id} = {IlDecoder.NameOf(Op)} ({string.Join(", ", Operands.Select(o => "v" + o.Id))}) : {Type}",
    };
}

/// <summary>The last instruction of a block when it is a branch, a switch or ret, with the values that it reads.</summary>
internal sealed record SsaTerminator(IlInstruction? Instruction, ImmutableArray<SsaValue> Operands);

/// <summary>One block of the SSA form: its phis, its operations in IL order and its terminator.</summary>
internal sealed record SsaBlock(BasicBlock Block, ImmutableArray<SsaValue> Phis, ImmutableArray<SsaValue> Operations, SsaTerminator Terminator);

/// <summary>The SSA form of a method.</summary>
internal sealed record SsaMethod(ControlFlowGraph Graph, ImmutableArray<SsaBlock> Blocks)
{
    public IEnumerable<SsaValue> Phis => Blocks.SelectMany(b => b.Phis);
}

/// <summary>The value types: the map from .NET types and the inference for operations and stack phis.</summary>
internal static class SsaTypes
{
    private const int MaximumRounds = 64;

    /// <summary>The SSA type of a .NET type. Throws <see cref="TranslationException"/> for a type that the program does not have.</summary>
    public static SsaType FromClr(DecodedMethod method, Type type, int ilOffset)
    {
        if (type == typeof(float))
        {
            return SsaType.Float;
        }

        if (type == typeof(int))
        {
            return SsaType.Int;
        }

        if (type == typeof(bool))
        {
            return SsaType.Bit;
        }

        if (type == typeof(void))
        {
            return SsaType.None;
        }

        if (type == typeof(double))
        {
            throw method.Refuse(ilOffset, "double arithmetic", "the program computes in FP32; write the expression in float");
        }

        if (type.IsByRef || type.IsPointer)
        {
            throw method.Refuse(ilOffset, $"a reference of type {type.Name}", "the program has no references; pass the value");
        }

        if (type.IsPrimitive || type == typeof(decimal) || type.IsEnum)
        {
            throw method.Refuse(ilOffset, $"a value of type {type.Name}", "the program has float, int and bool values only");
        }

        if (type.IsSZArray)
        {
            return new SsaType(SsaTypeKind.Array, type, FromClr(method, type.GetElementType()!, ilOffset));
        }

        return new SsaType(SsaTypeKind.Aggregate, type);
    }

    /// <summary>
    /// Gives a type to each operation and each stack phi. A loop phi can read a value that comes
    /// later, so the inference repeats until no type changes.
    /// </summary>
    public static void Infer(DecodedMethod method, ImmutableArray<SsaBlock> blocks)
    {
        var values = blocks.SelectMany(b => b.Phis.Concat(b.Operations)).ToArray();
        var known = new HashSet<SsaValue>(values.Where(v => v.Type.Kind != SsaTypeKind.None));
        for (var round = 0; ; round++)
        {
            if (round == MaximumRounds)
            {
                throw new InvalidOperationException($"the type inference of {IlDecoder.Describe(method.Method)} does not end");
            }

            var changed = false;
            foreach (var value in values)
            {
                var isStackPhi = value.Kind == SsaValueKind.Phi && value.Variable.Kind == SsaVariableKind.StackSlot;
                if (value.Kind == SsaValueKind.Phi && !isStackPhi)
                {
                    continue;
                }

                if (value.Kind == SsaValueKind.Operation && value.Address is not null)
                {
                    continue;
                }

                var type = isStackPhi ? JoinPhi(method, value, known) : OperationType(method, value, known);
                if (type is not null && (!known.Contains(value) || type != value.Type))
                {
                    value.Type = type;
                    known.Add(value);
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var unknown = values.FirstOrDefault(v => !known.Contains(v) && !(v.Kind == SsaValueKind.Operation && IsVoid(v)));
        if (unknown is not null)
        {
            throw new InvalidOperationException($"the value {unknown} of {IlDecoder.Describe(method.Method)} has no type");
        }
    }

    private static bool IsVoid(SsaValue value) =>
        value.Op is ILOpCode.Stelem or ILOpCode.Stelem_r4 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i1
        || (value.Op is ILOpCode.Call or ILOpCode.Callvirt && value.Member is MethodInfo { ReturnType: var r } && r == typeof(void));

    private static bool IsKnown(SsaValue value, HashSet<SsaValue> known) =>
        value.Kind is SsaValueKind.Argument or SsaValueKind.InitialLocal or SsaValueKind.Constant || known.Contains(value);

    private static bool IsBitLike(SsaValue value) =>
        value.Type.Kind == SsaTypeKind.Bit || (value.Kind == SsaValueKind.Constant && value.Constant is int and (0 or 1));

    private static SsaType? JoinPhi(DecodedMethod method, SsaValue phi, HashSet<SsaValue> known)
    {
        var operands = phi.Operands.Where(o => o != phi && IsKnown(o, known)).ToArray();
        if (operands.Length == 0)
        {
            return null;
        }

        if (operands.Any(o => o.Type.Kind == SsaTypeKind.Address))
        {
            throw method.Refuse(phi.IlOffset, "an address that depends on a branch", "the program reads a field through a fixed local or argument only");
        }

        if (operands.All(IsBitLike))
        {
            return SsaType.Bit;
        }

        if (operands.All(o => o.Type.Kind == SsaTypeKind.Float))
        {
            return SsaType.Float;
        }

        if (operands.All(o => o.Type.Kind is SsaTypeKind.Int or SsaTypeKind.Bit))
        {
            return SsaType.Int;
        }

        var first = operands[0].Type;
        if (first.Kind is SsaTypeKind.Aggregate or SsaTypeKind.Array && operands.All(o => o.Type.Kind == first.Kind && o.Type.ClrType == first.ClrType))
        {
            return operands.All(o => o.Type == first) ? first : first with { Length = null };
        }

        throw method.Refuse(phi.IlOffset, "a merge of values with different types", "each edge into a block must give the same value type");
    }

    private static SsaType? OperationType(DecodedMethod method, SsaValue value, HashSet<SsaValue> known)
    {
        var operands = value.Operands;
        if (operands.Any(o => !IsKnown(o, known)) && value.Op is not (ILOpCode.Ldfld or ILOpCode.Ldsfld or ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj))
        {
            return null;
        }

        switch (value.Op)
        {
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Neg:
                return operands.Any(o => o.Type.Kind == SsaTypeKind.Float) ? SsaType.Float : SsaType.Int;
            case ILOpCode.And or ILOpCode.Or or ILOpCode.Xor:
                return operands.All(IsBitLike) && operands.Any(o => o.Type.Kind == SsaTypeKind.Bit) ? SsaType.Bit : SsaType.Int;
            case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
                return SsaType.Bit;
            case ILOpCode.Conv_r4:
                return SsaType.Float;
            case ILOpCode.Conv_i4 or ILOpCode.Ldlen:
                return SsaType.Int;
            case ILOpCode.Ldfld or ILOpCode.Ldsfld:
                return FromClr(method, ((FieldInfo)value.Member!).FieldType, value.IlOffset);
            case ILOpCode.Call or ILOpCode.Callvirt:
                return FromClr(method, value.Member is MethodInfo info ? info.ReturnType : typeof(void), value.IlOffset);
            case ILOpCode.Newobj:
                return FromClr(method, ((MethodBase)value.Member!).DeclaringType!, value.IlOffset);
            case ILOpCode.Initobj:
                return FromClr(method, (Type)value.Member!, value.IlOffset);
            case ILOpCode.Stfld:
                return operands[0].Type;
            case ILOpCode.Newarr:
                return new SsaType(SsaTypeKind.Array, ((Type)value.Member!).MakeArrayType(), FromClr(method, (Type)value.Member!, value.IlOffset),
                    operands[0].Kind == SsaValueKind.Constant ? (int?)operands[0].Constant : null);
            case ILOpCode.Ldelem or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i1:
                if (operands[0].Type is { Kind: SsaTypeKind.Array, Element: { } element })
                {
                    return element;
                }

                return value.Op switch
                {
                    ILOpCode.Ldelem_r4 => SsaType.Float,
                    ILOpCode.Ldelem => FromClr(method, (Type)value.Member!, value.IlOffset),
                    _ => SsaType.Int,
                };
            case ILOpCode.Stelem or ILOpCode.Stelem_r4 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i1:
                return SsaType.None;
            default:
                throw new InvalidOperationException($"the type inference has no rule for {IlDecoder.NameOf(value.Op)}");
        }
    }
}
