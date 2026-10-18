// No Handwired source. Handwired has no translator. This file holds the value kinds of the plan,
// section "Files" (Values.cs). The kinds are a wire, a constant, an affine form, NOT, AND, OR and XOR
// of bits, a gate, an aggregate and an array. A symbolic value (an affine form, NOT, AND, OR, XOR, a
// gate) has no wire until the emit (Emitter.cs). DataTypes reads the backing field of a property and
// the field stores of a data constructor from their IL.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The type of a number or a bit value.</summary>
internal enum ScalarKind
{
    Float,
    Int,
    Bit,
}

/// <summary>A value of the translation: a number or a bit, an aggregate or an array.</summary>
internal abstract class Value;

/// <summary>A number or a bit. Wire is the wire after the emit, or null for a symbolic value that has no wire yet.</summary>
internal abstract class ScalarValue(ScalarKind kind) : Value
{
    public ScalarKind Kind { get; } = kind;

    public Wire? Wire { get; set; }
}

/// <summary>A value that already is a wire: a cell or the result of an emitted operation.</summary>
internal sealed class WireValue : ScalarValue
{
    public WireValue(Wire wire, ScalarKind kind)
        : base(kind) => Wire = wire;
}

/// <summary>A constant. A bit constant holds 0 or 1. The emit gives circuit.Constant(Number).</summary>
internal sealed class ConstantValue(double number, ScalarKind kind) : ScalarValue(kind)
{
    public double Number { get; } = number;
}

/// <summary>The IL instruction of an affine form.</summary>
internal enum AffineOp
{
    /// <summary>add: Left + Right.</summary>
    Add,

    /// <summary>sub: Left - Right.</summary>
    Subtract,

    /// <summary>neg: -Left.</summary>
    Negate,

    /// <summary>mul by a constant, or div by a power of two: Factor × Left.</summary>
    Scale,

    /// <summary>conv.r4 of an int form: the same value as a float.</summary>
    Convert,
}

/// <summary>
/// An affine form from one IL instruction: Bias + Σ coefficient × wire. Left and Right are the
/// operands of the instruction in C# order. An operand is a leaf (a wire or a constant) or an
/// affine form with no wire. Terms holds the form after the composition of the operands.
/// </summary>
internal sealed class AffineValue(
    ScalarKind kind, AffineOp op, ScalarValue left, ScalarValue? right, double factor, ImmutableArray<Term> terms, double bias, int ilOffset)
    : ScalarValue(kind)
{
    public AffineOp Op { get; } = op;

    public ScalarValue Left { get; } = left;

    public ScalarValue? Right { get; } = right;

    public double Factor { get; } = factor;

    public ImmutableArray<Term> Terms { get; } = terms;

    public double Bias { get; } = bias;

    public int IlOffset { get; } = ilOffset;

    /// <summary>True when the form came from one IL instruction: no operand is an affine form with no wire.</summary>
    public bool IsOneInstruction => !IsOpenForm(Left) && (Right is null || !IsOpenForm(Right));

    private static bool IsOpenForm(ScalarValue value) => value is AffineValue { Wire: null };
}

/// <summary>NOT of a bit: 1 - bit.</summary>
internal sealed class NotValue(ScalarValue operand) : ScalarValue(ScalarKind.Bit)
{
    public ScalarValue Operand { get; } = operand;
}

/// <summary>AND of bit literals. A literal is a bit or the NOT of a bit.</summary>
internal sealed class AndValue(ImmutableArray<ScalarValue> literals) : ScalarValue(ScalarKind.Bit)
{
    public ImmutableArray<ScalarValue> Literals { get; } = literals;
}

/// <summary>OR of bit literals.</summary>
internal sealed class OrValue(ImmutableArray<ScalarValue> literals) : ScalarValue(ScalarKind.Bit)
{
    public ImmutableArray<ScalarValue> Literals { get; } = literals;
}

/// <summary>XOR of two bits.</summary>
internal sealed class XorValue(ScalarValue left, ScalarValue right) : ScalarValue(ScalarKind.Bit)
{
    public ScalarValue Left { get; } = left;

    public ScalarValue Right { get; } = right;
}

/// <summary>
/// A gate ITE(Condition, On, Off) of two numbers or two bits, with no wire (plan, section "Decisions",
/// question 4). Condition is an exact bit. Gates.cs builds a gate with the gate rules 1 to 4; the emit
/// applies the rules 5 to 7.
/// </summary>
internal sealed class GateValue(ScalarValue condition, ScalarValue on, ScalarValue off, ScalarKind kind, int ilOffset = -1, OperationLowering? owner = null)
    : ScalarValue(kind)
{
    public ScalarValue Condition { get; } = condition;

    public ScalarValue On { get; } = on;

    public ScalarValue Off { get; } = off;

    /// <summary>The IL offset of the branch of the gate, or -1. A refusal of the gate names its line.</summary>
    public int IlOffset { get; } = ilOffset;

    /// <summary>The lowering of the method that holds the branch, or null.</summary>
    public OperationLowering? Owner { get; } = owner;
}

/// <summary>A struct, a record or a tuple: one value for each instance field, by field name.</summary>
internal sealed class AggregateValue(Type type, ImmutableDictionary<string, Value> fields) : Value
{
    public Type Type { get; } = type;

    public ImmutableDictionary<string, Value> Fields { get; } = fields;

    /// <summary>A copy with the value at the field path replaced.</summary>
    public AggregateValue With(ReadOnlySpan<string> path, Value value)
    {
        if (path.IsEmpty) throw new InvalidOperationException("a field store needs a field path");
        var head = path[0];
        if (path.Length == 1) return new AggregateValue(Type, Fields.SetItem(head, value));
        if (!Fields.TryGetValue(head, out var inner) || inner is not AggregateValue nested)
        {
            throw new InvalidOperationException($"the field {head} of {Type.Name} holds no aggregate");
        }

        return new AggregateValue(Type, Fields.SetItem(head, nested.With(path[1..], value)));
    }
}

/// <summary>
/// An array with a fixed length. A local array comes from newarr or from an Hw method in this
/// method, and stelem changes its elements in IL order. A cell array or a static array is fixed.
/// </summary>
internal sealed class ArrayValue(Type elementType, Value[] elements, bool isLocal) : Value
{
    public Type ElementType { get; } = elementType;

    public Value[] Elements { get; } = elements;

    public bool IsLocal { get; } = isLocal;
}

/// <summary>The data types of the translation: backing fields, data constructors and zero values.</summary>
internal static class DataTypes
{
    /// <summary>The ScalarKind of float, int and bool, or null.</summary>
    public static ScalarKind? KindOf(Type type) =>
        type == typeof(float) ? ScalarKind.Float : type == typeof(int) ? ScalarKind.Int : type == typeof(bool) ? ScalarKind.Bit : null;

    /// <summary>The instance fields of a type, public and not public.</summary>
    public static FieldInfo[] InstanceFields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// The field that the getter of the property returns. The getter body must only read one field of
    /// this (an auto-property, a record property, or "get => field"). Otherwise null.
    /// </summary>
    public static FieldInfo? BackingField(PropertyInfo property)
    {
        if (property.GetMethod is not { IsStatic: false } getter || getter.GetMethodBody() is null)
        {
            return null;
        }

        SsaMethod ssa;
        try
        {
            ssa = SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(getter)));
        }
        catch (TranslationException)
        {
            return null;
        }

        var returns = ssa.Blocks.Where(b => b.Terminator.Instruction?.OpCode == ILOpCode.Ret).ToArray();
        if (returns.Length != 1 || returns[0].Terminator.Operands is not [var result])
        {
            return null;
        }

        return result is { Kind: SsaValueKind.Operation, Op: ILOpCode.Ldfld, Member: FieldInfo field, Operands: [{ Kind: SsaValueKind.Argument, Index: 0 }] }
            && field.DeclaringType == property.DeclaringType && !field.IsStatic
            ? field
            : null;
    }

    /// <summary>
    /// The parameter index that each field of a data constructor gets, by field name. A data
    /// constructor only stores its parameters into fields of its type and calls the constructor of
    /// object or ValueType. One parameter can go to more than one field. When the constructor stores
    /// into a field two times, the last store sets the field. Returns null for another constructor.
    /// </summary>
    public static IReadOnlyDictionary<string, int>? ConstructorFields(ConstructorInfo constructor)
    {
        if (constructor.GetMethodBody() is null)
        {
            return null;
        }

        DecodedMethod decoded;
        try
        {
            decoded = IlDecoder.Decode(constructor);
        }
        catch (TranslationException)
        {
            return null;
        }

        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        var instructions = decoded.Instructions.Where(i => i.OpCode != ILOpCode.Nop).ToArray();
        var index = 0;
        while (index < instructions.Length)
        {
            var instruction = instructions[index];
            if (instruction.OpCode == ILOpCode.Ret && index == instructions.Length - 1)
            {
                return fields;
            }

            if (instruction.OpCode != ILOpCode.Ldarg_0 || index + 1 >= instructions.Length)
            {
                return null;
            }

            var next = instructions[index + 1];
            if (next.OpCode == ILOpCode.Call && next.Operand is ConstructorInfo baseConstructor
                && (baseConstructor.DeclaringType == typeof(object) || baseConstructor.DeclaringType == typeof(ValueType)))
            {
                index += 2;
                continue;
            }

            if (index + 2 < instructions.Length && IsArgumentLoad(next.OpCode) && next.IntOperand >= 1
                && instructions[index + 2] is { OpCode: ILOpCode.Stfld, Operand: FieldInfo field }
                && field.DeclaringType == constructor.DeclaringType && !field.IsStatic)
            {
                fields[field.Name] = next.IntOperand - 1;
                index += 3;
                continue;
            }

            return null;
        }

        return null;
    }

    private static bool IsArgumentLoad(ILOpCode op) =>
        op is ILOpCode.Ldarg or ILOpCode.Ldarg_s or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3;

    /// <summary>
    /// The zero value of a type, as initobj and the zero initialized locals give it. Returns null for
    /// a type with a reference field or another type that has no zero value in the program.
    /// </summary>
    public static Value? Zero(Type type)
    {
        if (KindOf(type) is { } kind)
        {
            return new ConstantValue(0, kind);
        }

        if (!type.IsValueType || type.IsPrimitive || type.IsEnum)
        {
            return null;
        }

        var fields = ImmutableDictionary.CreateBuilder<string, Value>();
        foreach (var field in InstanceFields(type))
        {
            if (Zero(field.FieldType) is not { } zero)
            {
                return null;
            }

            fields[field.Name] = zero;
        }

        return new AggregateValue(type, fields.ToImmutable());
    }
}
