// No Handwired source. Handwired has no translator. The IL decoder of the plan, section "Decisions",
// question 3, and the opcode table of section "IL opcode table": the method body bytes, read with
// System.Reflection.Metadata.BlobReader and the ILOpCode enum; the tokens, resolved through the
// module with the generic context of the method; the refusal of an exception clause and of each
// opcode that the table does not accept.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace Sharpwired.IL;

/// <summary>
/// One decoded IL instruction. The operand is normalized: an int for an argument, a local, an int
/// constant (also of the forms with an implicit operand) and the absolute target of a branch; a
/// float for ldc.r4; an <see cref="ImmutableArray{T}"/> of absolute targets for switch; the
/// resolved <see cref="MethodBase"/>, <see cref="FieldInfo"/> or <see cref="Type"/> for a token;
/// otherwise null.
/// </summary>
internal sealed record IlInstruction(int Offset, int Size, ILOpCode OpCode, object? Operand)
{
    /// <summary>The IL name of the opcode, for example "ldarg.0" or "blt.un.s".</summary>
    public string Name => IlDecoder.NameOf(OpCode);

    /// <summary>The offset of the next instruction.</summary>
    public int NextOffset => Offset + Size;

    /// <summary>True for br, brtrue, brfalse and the conditional branches, in the long and the short form.</summary>
    public bool IsBranch => OpCode.IsBranch();

    /// <summary>True for an unconditional branch (br, br.s).</summary>
    public bool IsJump => OpCode is ILOpCode.Br or ILOpCode.Br_s;

    /// <summary>The absolute target of a branch.</summary>
    public int BranchTarget => IsBranch && Operand is int target
        ? target
        : throw new InvalidOperationException($"the instruction {Name} at IL_{Offset:x4} is not a branch");

    /// <summary>The absolute targets of a switch.</summary>
    public ImmutableArray<int> SwitchTargets => Operand is ImmutableArray<int> targets
        ? targets
        : throw new InvalidOperationException($"the instruction {Name} at IL_{Offset:x4} is not a switch");

    /// <summary>The int operand: an argument, a local or an int constant.</summary>
    public int IntOperand => Operand is int value
        ? value
        : throw new InvalidOperationException($"the instruction {Name} at IL_{Offset:x4} has no int operand");
}

/// <summary>A decoded method body: the instructions, the local types and the source locations.</summary>
internal sealed class DecodedMethod
{
    private readonly FrozenDictionary<int, int> indexOfOffset;

    public DecodedMethod(MethodBase method, ImmutableArray<IlInstruction> instructions, ImmutableArray<Type> localTypes, SourceLocations locations)
    {
        Method = method;
        Instructions = instructions;
        LocalTypes = localTypes;
        Locations = locations;
        indexOfOffset = Enumerable.Range(0, instructions.Length).ToFrozenDictionary(i => instructions[i].Offset);
    }

    public MethodBase Method { get; }

    public ImmutableArray<IlInstruction> Instructions { get; }

    public ImmutableArray<Type> LocalTypes { get; }

    public SourceLocations Locations { get; }

    /// <summary>The index of the instruction at the offset, or -1 when no instruction starts there.</summary>
    public int IndexOf(int offset) => indexOfOffset.TryGetValue(offset, out var index) ? index : -1;

    /// <summary>Creates the refusal of a construct at an IL offset of this method.</summary>
    public TranslationException Refuse(int ilOffset, string construct, string reason) =>
        new(Locations.Locate(ilOffset), ilOffset, construct, reason);
}

/// <summary>The decoder of a method body.</summary>
internal static class IlDecoder
{
    private static readonly FrozenDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToFrozenDictionary(code => unchecked((ushort)code.Value));

    // The accepted opcodes of the plan, section "IL opcode table". The conditions of the table
    // (types, constant operands, the target of a call) are checked where the values are known.
    private static readonly FrozenSet<ILOpCode> Accepted = new[]
    {
        ILOpCode.Nop,
        ILOpCode.Ldarg, ILOpCode.Ldarg_s, ILOpCode.Ldarg_0, ILOpCode.Ldarg_1, ILOpCode.Ldarg_2, ILOpCode.Ldarg_3,
        ILOpCode.Starg, ILOpCode.Starg_s,
        ILOpCode.Ldloc, ILOpCode.Ldloc_s, ILOpCode.Ldloc_0, ILOpCode.Ldloc_1, ILOpCode.Ldloc_2, ILOpCode.Ldloc_3,
        ILOpCode.Stloc, ILOpCode.Stloc_s, ILOpCode.Stloc_0, ILOpCode.Stloc_1, ILOpCode.Stloc_2, ILOpCode.Stloc_3,
        ILOpCode.Ldarga, ILOpCode.Ldarga_s, ILOpCode.Ldloca, ILOpCode.Ldloca_s,
        ILOpCode.Ldc_i4_m1, ILOpCode.Ldc_i4_0, ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4_2, ILOpCode.Ldc_i4_3, ILOpCode.Ldc_i4_4,
        ILOpCode.Ldc_i4_5, ILOpCode.Ldc_i4_6, ILOpCode.Ldc_i4_7, ILOpCode.Ldc_i4_8, ILOpCode.Ldc_i4, ILOpCode.Ldc_i4_s,
        ILOpCode.Ldc_r4,
        ILOpCode.Dup, ILOpCode.Pop,
        ILOpCode.Add, ILOpCode.Sub, ILOpCode.Neg, ILOpCode.Mul, ILOpCode.Div,
        ILOpCode.And, ILOpCode.Or, ILOpCode.Xor,
        ILOpCode.Ceq, ILOpCode.Cgt, ILOpCode.Cgt_un, ILOpCode.Clt, ILOpCode.Clt_un,
        ILOpCode.Br, ILOpCode.Br_s, ILOpCode.Brtrue, ILOpCode.Brtrue_s, ILOpCode.Brfalse, ILOpCode.Brfalse_s,
        ILOpCode.Beq, ILOpCode.Beq_s, ILOpCode.Bge, ILOpCode.Bge_s, ILOpCode.Bgt, ILOpCode.Bgt_s,
        ILOpCode.Ble, ILOpCode.Ble_s, ILOpCode.Blt, ILOpCode.Blt_s, ILOpCode.Bne_un, ILOpCode.Bne_un_s,
        ILOpCode.Bge_un, ILOpCode.Bge_un_s, ILOpCode.Bgt_un, ILOpCode.Bgt_un_s,
        ILOpCode.Ble_un, ILOpCode.Ble_un_s, ILOpCode.Blt_un, ILOpCode.Blt_un_s,
        ILOpCode.Switch, ILOpCode.Ret,
        ILOpCode.Conv_r4, ILOpCode.Conv_i4,
        ILOpCode.Call, ILOpCode.Callvirt, ILOpCode.Newobj,
        ILOpCode.Initobj, ILOpCode.Ldobj,
        ILOpCode.Ldfld, ILOpCode.Ldflda, ILOpCode.Stfld, ILOpCode.Ldsfld,
        ILOpCode.Ldelem_r4, ILOpCode.Ldelem_i4, ILOpCode.Ldelem_u1, ILOpCode.Ldelem_i1, ILOpCode.Ldelem, ILOpCode.Ldelema,
        ILOpCode.Newarr, ILOpCode.Stelem_r4, ILOpCode.Stelem_i4, ILOpCode.Stelem_i1, ILOpCode.Stelem, ILOpCode.Ldlen,
        ILOpCode.Tail,
    }.ToFrozenSet();

    /// <summary>The IL name of an opcode.</summary>
    public static string NameOf(ILOpCode opCode) =>
        OpCodesByValue.TryGetValue((ushort)opCode, out var code) ? code.Name! : $"0x{(ushort)opCode:x4}";

    /// <summary>
    /// Decodes the body of a method. Throws <see cref="TranslationException"/> for an exception
    /// clause and for the first instruction that the opcode table does not accept.
    /// </summary>
    public static DecodedMethod Decode(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var body = method.GetMethodBody() ?? throw new ArgumentException($"the method {Describe(method)} has no IL body", nameof(method));
        var bytes = body.GetILAsByteArray() ?? throw new ArgumentException($"the method {Describe(method)} has no IL body", nameof(method));
        var locations = SourceLocations.Read(method);
        var localTypes = body.LocalVariables.OrderBy(l => l.LocalIndex).Select(l => l.LocalType).ToImmutableArray();
        var partial = new DecodedMethod(method, [], localTypes, locations);
        if (body.ExceptionHandlingClauses.Count > 0)
        {
            throw partial.Refuse(body.ExceptionHandlingClauses.Min(c => c.TryOffset), "an exception clause",
                "the program has no exceptions; remove the try statement");
        }

        return new DecodedMethod(method, DecodeInstructions(method, bytes, partial), localTypes, locations);
    }

    private static unsafe ImmutableArray<IlInstruction> DecodeInstructions(MethodBase method, byte[] bytes, DecodedMethod partial)
    {
        var instructions = ImmutableArray.CreateBuilder<IlInstruction>();
        fixed (byte* start = bytes)
        {
            var reader = new BlobReader(start, bytes.Length);
            while (reader.RemainingBytes > 0)
            {
                var offset = reader.Offset;
                var value = (ushort)reader.ReadByte();
                if (value == 0xFE && reader.RemainingBytes > 0)
                {
                    value = (ushort)(0xFE00 | reader.ReadByte());
                }

                var opCode = (ILOpCode)value;
                if (!OpCodesByValue.TryGetValue(value, out var code))
                {
                    // The prefix no. (0xFE19) has no ILOpCode member and no OpCodes field.
                    throw value == 0xFE19
                        ? partial.Refuse(offset, "the IL instruction no.", "the program does not accept this prefix")
                        : partial.Refuse(offset, $"the IL instruction 0x{value:x4}", "the IL instruction is not a valid opcode");
                }

                if (!Accepted.Contains(opCode))
                {
                    throw Refused(partial, offset, opCode);
                }

                var operand = ReadOperand(method, ref reader, opCode, code.OperandType, partial, offset);
                instructions.Add(new IlInstruction(offset, reader.Offset - offset, opCode, operand));
            }
        }

        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode == ILOpCode.Tail && (i + 1 == instructions.Count || instructions[i + 1].OpCode != ILOpCode.Call))
            {
                throw partial.Refuse(instructions[i].Offset, "the IL instruction tail.", "the tail. prefix must come before a call");
            }
        }

        return instructions.ToImmutable();
    }

    private static object? ReadOperand(MethodBase method, ref BlobReader reader, ILOpCode opCode, OperandType type, DecodedMethod partial, int offset)
    {
        var implicitValue = ImplicitOperand(opCode);
        if (implicitValue is not null)
        {
            return implicitValue;
        }

        switch (type)
        {
            case OperandType.InlineNone:
                return null;
            case OperandType.ShortInlineBrTarget:
                {
                    var delta = reader.ReadSByte();
                    return reader.Offset + delta;
                }

            case OperandType.InlineBrTarget:
                {
                    var delta = reader.ReadInt32();
                    return reader.Offset + delta;
                }

            case OperandType.ShortInlineI:
                return (int)reader.ReadSByte();
            case OperandType.InlineI:
                return reader.ReadInt32();
            case OperandType.ShortInlineR:
                return reader.ReadSingle();
            case OperandType.ShortInlineVar:
                return (int)reader.ReadByte();
            case OperandType.InlineVar:
                return (int)reader.ReadUInt16();
            case OperandType.InlineSwitch:
                {
                    var count = reader.ReadUInt32();
                    var deltas = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        deltas[i] = reader.ReadInt32();
                    }

                    var next = reader.Offset;
                    return deltas.Select(d => next + d).ToImmutableArray();
                }

            case OperandType.InlineMethod:
                {
                    var token = reader.ReadInt32();
                    return Resolve(partial, offset, () => method.Module.ResolveMethod(token, TypeArguments(method), MethodArguments(method)));
                }

            case OperandType.InlineField:
                {
                    var token = reader.ReadInt32();
                    return Resolve(partial, offset, () => method.Module.ResolveField(token, TypeArguments(method), MethodArguments(method)));
                }

            case OperandType.InlineType:
                {
                    var token = reader.ReadInt32();
                    return Resolve(partial, offset, () => method.Module.ResolveType(token, TypeArguments(method), MethodArguments(method)));
                }

            default:
                throw partial.Refuse(offset, $"the IL instruction {NameOf(opCode)}", "the translator does not read this operand form");
        }
    }

    private static object Resolve(DecodedMethod partial, int offset, Func<object?> resolve)
    {
        try
        {
            return resolve() ?? throw partial.Refuse(offset, "a token", "the module does not resolve the token");
        }
        catch (Exception error) when (error is ArgumentException or BadImageFormatException && error is not TranslationException)
        {
            throw partial.Refuse(offset, "a token", $"the module does not resolve the token: {error.Message}");
        }
    }

    private static Type[]? TypeArguments(MethodBase method) =>
        method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;

    private static Type[]? MethodArguments(MethodBase method) =>
        method is MethodInfo { IsGenericMethod: true } info ? info.GetGenericArguments() : null;

    private static int? ImplicitOperand(ILOpCode opCode) => opCode switch
    {
        ILOpCode.Ldarg_0 or ILOpCode.Ldloc_0 or ILOpCode.Stloc_0 or ILOpCode.Ldc_i4_0 => 0,
        ILOpCode.Ldarg_1 or ILOpCode.Ldloc_1 or ILOpCode.Stloc_1 or ILOpCode.Ldc_i4_1 => 1,
        ILOpCode.Ldarg_2 or ILOpCode.Ldloc_2 or ILOpCode.Stloc_2 or ILOpCode.Ldc_i4_2 => 2,
        ILOpCode.Ldarg_3 or ILOpCode.Ldloc_3 or ILOpCode.Stloc_3 or ILOpCode.Ldc_i4_3 => 3,
        ILOpCode.Ldc_i4_4 => 4,
        ILOpCode.Ldc_i4_5 => 5,
        ILOpCode.Ldc_i4_6 => 6,
        ILOpCode.Ldc_i4_7 => 7,
        ILOpCode.Ldc_i4_8 => 8,
        ILOpCode.Ldc_i4_m1 => -1,
        _ => null,
    };

    private static TranslationException Refused(DecodedMethod partial, int offset, ILOpCode opCode)
    {
        var name = NameOf(opCode);
        if (opCode is ILOpCode.Ldc_r8 or ILOpCode.Conv_r8 or ILOpCode.Conv_r_un)
        {
            return partial.Refuse(offset, "double arithmetic", "the program computes in FP32; write the expression in float");
        }

        var reason = opCode switch
        {
            ILOpCode.Ldc_i8 or ILOpCode.Conv_i8 or ILOpCode.Conv_u8 or ILOpCode.Conv_i or ILOpCode.Conv_u =>
                "the program has 32-bit values only; use int or float",
            ILOpCode.Rem or ILOpCode.Rem_un or ILOpCode.Div_un =>
                "the program has no exact integer division or remainder",
            ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Not =>
                "the program has no exact form for bit operations on int values",
            ILOpCode.Ldnull or ILOpCode.Ldstr or ILOpCode.Box or ILOpCode.Unbox or ILOpCode.Unbox_any
                or ILOpCode.Isinst or ILOpCode.Castclass =>
                "the program has no object references; use float, int and bool values",
            ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Leave or ILOpCode.Leave_s or ILOpCode.Endfinally or ILOpCode.Endfilter =>
                "the program has no exceptions",
            ILOpCode.Calli or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp =>
                "the program has no delegates and no indirect calls",
            ILOpCode.Stsfld or ILOpCode.Ldsflda =>
                "the program cannot change or address a static field",
            ILOpCode.Localloc or ILOpCode.Cpblk or ILOpCode.Initblk =>
                "the program has no pointers",
            ILOpCode.Constrained or ILOpCode.Volatile or ILOpCode.Unaligned or ILOpCode.Readonly =>
                "the program does not accept this prefix",
            _ when name.StartsWith("ldind.", StringComparison.Ordinal) || name.StartsWith("stind.", StringComparison.Ordinal) =>
                "the program has no pointers",
            _ when name.Contains(".ovf", StringComparison.Ordinal) =>
                "the program has no checked arithmetic",
            _ when name.EndsWith(".r8", StringComparison.Ordinal) || name.EndsWith(".i8", StringComparison.Ordinal) || name.EndsWith(".u8", StringComparison.Ordinal) =>
                "the program has 32-bit values only; use int or float",
            _ => "the translator has no lowering for this IL instruction",
        };
        return partial.Refuse(offset, $"the IL instruction {name}", reason);
    }

    internal static string Describe(MethodBase method) => $"{method.DeclaringType?.Name}.{method.Name}";
}
