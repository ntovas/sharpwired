// No Handwired source for the walk. The rows follow the plan, sections "IL opcode table" and
// "Lowering table", whose source is $REF/docs/expressions.md, table "Familiar expressions": the rows
// "a >= b, a > b, a === b" (Compare), "NOT, AND and OR", "Math.min(a, b), Math.max(a, b), clamps and
// Math.abs(x)" (MinimumExact, MaximumExact, ClampExact, Absolute) and "table[index] at a computed
// integer index" (IndexRead). The arithmetic follows $REF/docs/execution-model.md, section "FP32".
// The lowering runs one time over the SSA form of straight-line code, in IL order, and gives the
// value of the return instruction. A branch is refused.

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using Sharpwired.Annotations;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The lowering of the operations of a straight-line method into builder calls.</summary>
internal sealed partial class OperationLowering
{
    private readonly DecodedMethod method;
    private readonly SsaMethod ssa;
    private readonly IReadOnlyList<Value> arguments;
    private readonly Dictionary<SsaValue, Value> values = [];
    private readonly Dictionary<SsaValue, int> uses = [];

    // A cgt or clt whose only use is "ldc.i4.0; ceq": the ceq gives the comparison with the other relation.
    private readonly Dictionary<SsaValue, SsaValue> invertedComparisons = [];

    private OperationLowering(DecodedMethod method, SsaMethod ssa, Emitter emitter, IReadOnlyList<Value> arguments)
    {
        this.method = method;
        this.ssa = ssa;
        Emitter = emitter;
        this.arguments = arguments;
    }

    public Emitter Emitter { get; }

    public Circuit Circuit => Emitter.Circuit;

    /// <summary>
    /// Lowers the operations of a straight-line method and returns the value of its return
    /// instruction with the IL offset of that instruction. Throws <see cref="TranslationException"/>
    /// for a refused construct.
    /// </summary>
    public static (Value Value, int IlOffset) Lower(DecodedMethod method, SsaMethod ssa, Emitter emitter, IReadOnlyList<Value> arguments)
    {
        var lowering = new OperationLowering(method, ssa, emitter, arguments);
        return lowering.Run();
    }

    public TranslationException Refuse(int ilOffset, string construct, string reason) => method.Refuse(ilOffset, construct, reason);

    /// <summary>Emits a number or a bit value and returns its wire.</summary>
    public Wire Emit(Value value, int ilOffset) => value is ScalarValue scalar
        ? Emitter.Emit(scalar)
        : throw Refuse(ilOffset, "an aggregate as a number", "the operation reads a float, an int or a bool");

    /// <summary>Runs a macro call and turns its ArgumentException into a refusal of the construct.</summary>
    public T Macro<T>(int ilOffset, string construct, Func<T> build)
    {
        try
        {
            return build();
        }
        catch (ArgumentException error) when (error is not TranslationException)
        {
            throw Refuse(ilOffset, construct, error.Message);
        }
    }

    /// <summary>The text of a call construct, for example "a call to System.Console.WriteLine(Single)".</summary>
    public static string CallText(MethodBase callee) =>
        $"a call to {callee.DeclaringType?.FullName}.{callee.Name}({string.Join(", ", callee.GetParameters().Select(p => p.ParameterType.Name))})";

    private (Value Value, int IlOffset) Run()
    {
        foreach (var block in ssa.Blocks)
        {
            var terminator = block.Terminator.Instruction;
            if (terminator is { IsBranch: true, IsJump: false } || terminator?.OpCode == ILOpCode.Switch || !block.Phis.IsEmpty)
            {
                throw Refuse(terminator?.Offset ?? block.Block.StartOffset, "a branch", "this version of the translator accepts only straight-line code");
            }
        }

        CountUses();
        foreach (var index in ssa.Graph.ReversePostorder)
        {
            foreach (var operation in ssa.Blocks[index].Operations)
            {
                if (operation.Address is not null || invertedComparisons.ContainsValue(operation))
                {
                    continue;
                }

                if (LowerOperation(operation) is { } value)
                {
                    values[operation] = value;
                }
            }
        }

        var returns = ssa.Blocks.Where(b => b.Terminator.Instruction?.OpCode == ILOpCode.Ret).ToArray();
        if (returns is not [var exit] || exit.Terminator.Operands is not [var result])
        {
            throw Refuse(returns.FirstOrDefault()?.Terminator.Instruction?.Offset ?? 0, "a method with no return value",
                "the outputs of the program are the return value of the method");
        }

        return (Get(result), exit.Terminator.Instruction!.Offset);
    }

    private void CountUses()
    {
        foreach (var block in ssa.Blocks)
        {
            foreach (var operand in block.Operations.Concat(block.Phis).SelectMany(v => v.Operands).Concat(block.Terminator.Operands))
            {
                uses[operand] = uses.GetValueOrDefault(operand) + 1;
            }
        }

        var users = ssa.Blocks.SelectMany(b => b.Operations).SelectMany(user => user.Operands.Select(operand => (operand, user)))
            .GroupBy(pair => pair.operand).ToDictionary(group => group.Key, group => group.Select(pair => pair.user).ToArray());
        foreach (var comparison in ssa.Blocks.SelectMany(b => b.Operations))
        {
            if (comparison.Op is ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un
                && uses.GetValueOrDefault(comparison) == 1
                && users.TryGetValue(comparison, out var found) && found is [{ Op: ILOpCode.Ceq } user]
                && user.Operands is [var left, { Kind: SsaValueKind.Constant, Constant: 0 }] && left == comparison)
            {
                invertedComparisons[user] = comparison;
            }
        }
    }

    private Value Get(SsaValue value) => value.Kind switch
    {
        SsaValueKind.Argument => arguments[value.Index],
        SsaValueKind.InitialLocal => DataTypes.Zero(method.LocalTypes[value.Index])
            ?? throw Refuse(value.IlOffset, $"a local of type {method.LocalTypes[value.Index].Name} with no value", "the program reads a local only after a store"),
        SsaValueKind.Constant => Constant(value),
        _ => values.TryGetValue(value, out var found)
            ? found
            : throw new InvalidOperationException($"the value {value} has no lowering"),
    };

    // A constant of the IL. A refusal of the constant gives the line of the instruction that loads it.
    private ConstantValue Constant(SsaValue value)
    {
        var number = Convert.ToDouble(value.Constant, CultureInfo.InvariantCulture);
        var kind = KindOf(value);
        return kind == ScalarKind.Int ? IntConstant(number, value.IlOffset) : new ConstantValue(number, kind);
    }

    // An int constant of the IL or of a static readonly field. FP32 holds an int exactly only within 2^24.
    private ConstantValue IntConstant(double number, int ilOffset) => Math.Abs(number) > 16_777_216
        ? throw Refuse(ilOffset, $"the int constant {number.ToString(CultureInfo.InvariantCulture)}", "FP32 holds each int exactly only within -2^24..2^24")
        : new ConstantValue(number, ScalarKind.Int);

    private ScalarValue Scalar(SsaValue value, int ilOffset) => Get(value) as ScalarValue
        ?? throw Refuse(ilOffset, "an aggregate as a number", "the operation reads a float, an int or a bool");

    private ScalarKind KindOf(SsaValue value) => value.Type.Kind switch
    {
        SsaTypeKind.Float => ScalarKind.Float,
        SsaTypeKind.Int => ScalarKind.Int,
        SsaTypeKind.Bit => ScalarKind.Bit,
        _ => throw Refuse(value.IlOffset, $"a value of type {value.Type}", "the operation reads a float, an int or a bool"),
    };

    private Value? LowerOperation(SsaValue operation)
    {
        var offset = operation.IlOffset;
        switch (operation.Op)
        {
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Neg:
                return Arithmetic(operation);
            case ILOpCode.And or ILOpCode.Or or ILOpCode.Xor:
                return BitOperation(operation);
            case ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un:
                return Comparison(operation);
            case ILOpCode.Conv_r4:
                return ToFloat(Scalar(operation.Operands[0], offset), offset);
            case ILOpCode.Conv_i4:
                return ToInt(Scalar(operation.Operands[0], offset), offset);
            case ILOpCode.Ldfld:
                return ReadField(Get(operation.Operands[0]), (FieldInfo)operation.Member!, offset);
            case ILOpCode.Ldsfld:
                return ReadStatic((FieldInfo)operation.Member!, offset);
            case ILOpCode.Call or ILOpCode.Callvirt:
                return Call(operation);
            case ILOpCode.Newobj:
                return Construct((ConstructorInfo)operation.Member!, [.. operation.Operands.Select(Get)], offset);
            case ILOpCode.Initobj:
                return DataTypes.Zero((Type)operation.Member!)
                    ?? throw Refuse(offset, $"initobj of {((Type)operation.Member!).Name}", "the program has no zero value of a type with a reference field");
            case ILOpCode.Stfld:
                return StoreField(operation);
            case ILOpCode.Newarr:
                return NewArray(operation);
            case ILOpCode.Ldelem or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i1:
                return ReadElement(Array(operation.Operands[0], offset), operation.Operands[1], offset);
            case ILOpCode.Stelem or ILOpCode.Stelem_r4 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i1:
                StoreElement(operation);
                return null;
            case ILOpCode.Ldlen:
                return new ConstantValue(Array(operation.Operands[0], offset).Elements.Length, ScalarKind.Int);
            default:
                throw Refuse(offset, $"the IL instruction {IlDecoder.NameOf(operation.Op)}", "the translator has no lowering for this IL instruction");
        }
    }

    // add, sub, neg, mul and div on float or int (plan, "Lowering table", rows 1 to 3).
    private Value Arithmetic(SsaValue operation)
    {
        var offset = operation.IlOffset;
        var kind = KindOf(operation);
        var left = Scalar(operation.Operands[0], offset);
        var right = operation.Op == ILOpCode.Neg ? null : Scalar(operation.Operands[1], offset);
        if (operation.Op == ILOpCode.Div)
        {
            return Divide(left, right!, kind, operation);
        }

        if (left is ConstantValue l && (right is null || right is ConstantValue))
        {
            return Fold(operation.Op, l.Number, (right as ConstantValue)?.Number ?? 0, kind, offset);
        }

        return operation.Op switch
        {
            ILOpCode.Add => NewAffine(kind, AffineOp.Add, left, right, 1, operation),
            ILOpCode.Sub => NewAffine(kind, AffineOp.Subtract, left, right, 1, operation),
            ILOpCode.Neg => NewAffine(kind, AffineOp.Negate, left, null, -1, operation),
            _ when right is ConstantValue factor => NewAffine(kind, AffineOp.Scale, left, null, factor.Number, operation),
            _ when left is ConstantValue factor => NewAffine(kind, AffineOp.Scale, right!, null, factor.Number, operation),
            _ => Multiply(left, right!, kind, offset),
        };
    }

    private Value Divide(ScalarValue left, ScalarValue right, ScalarKind kind, SsaValue operation)
    {
        var offset = operation.IlOffset;
        if (kind == ScalarKind.Int)
        {
            throw Refuse(offset, "int division", "C# rounds toward zero, and the program has no exact integer division");
        }

        if (right is not ConstantValue divisor)
        {
            throw Refuse(offset, "a division by a runtime value", "Handwired has only inexact constructions for a division by a runtime value");
        }

        var reciprocal = 1 / divisor.Number;
        if (!Fp32.IsPowerOfTwoGrid(Math.Abs(divisor.Number)) || (double)(float)reciprocal != reciprocal || !double.IsFinite(reciprocal))
        {
            throw Refuse(offset, $"float division by {Fp32.Format(divisor.Number)}",
                $"C# rounds x / {Fp32.Format(divisor.Number)} one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / {Fp32.Format(divisor.Number)})");
        }

        return left is ConstantValue constant
            ? new ConstantValue((float)(constant.Number / divisor.Number), kind)
            : NewAffine(kind, AffineOp.Scale, left, null, reciprocal, operation);
    }

    // Two constants: the C# operation on the FP32 or int values.
    private ConstantValue Fold(ILOpCode op, double left, double right, ScalarKind kind, int offset)
    {
        if (kind == ScalarKind.Int)
        {
            long exact = op switch
            {
                ILOpCode.Add => (long)left + (long)right,
                ILOpCode.Sub => (long)left - (long)right,
                ILOpCode.Mul => (long)left * (long)right,
                _ => -(long)left,
            };
            if (Math.Abs(exact) > 16_777_216)
            {
                throw Refuse(offset, "an int value with a range past 2^24", "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24");
            }

            return new ConstantValue(exact, kind);
        }

        float a = (float)left, b = (float)right;
        float result = op switch
        {
            ILOpCode.Add => a + b,
            ILOpCode.Sub => a - b,
            ILOpCode.Mul => a * b,
            _ => -a,
        };
        return new ConstantValue(result, kind);
    }

    // An affine form of one IL instruction. An int form must be exact; a form with two or more uses
    // emits at its definition (plan, "Emission rules", rule 2).
    private ScalarValue NewAffine(ScalarKind kind, AffineOp op, ScalarValue left, ScalarValue? right, double factor, SsaValue operation)
    {
        var (leftTerms, leftBias) = FormOf(left, operation.IlOffset);
        var (rightTerms, rightBias) = right is null ? ([], 0) : FormOf(right, operation.IlOffset);
        (IEnumerable<Term> terms, double bias) = op switch
        {
            AffineOp.Add => (leftTerms.Concat(rightTerms), leftBias + rightBias),
            AffineOp.Subtract => (leftTerms.Concat(rightTerms.Select(t => t with { Coefficient = -t.Coefficient })), leftBias - rightBias),
            AffineOp.Negate => (leftTerms.Select(t => t with { Coefficient = -t.Coefficient }), -leftBias),
            AffineOp.Scale => (leftTerms.Select(t => t with { Coefficient = t.Coefficient * factor }), leftBias * factor),
            _ => (leftTerms, leftBias),
        };
        var form = new AffineValue(kind, op, left, right, factor, [.. terms], bias, operation.IlOffset);
        if (kind == ScalarKind.Int && !Emitter.IsExact(form.Terms, form.Bias))
        {
            throw Refuse(operation.IlOffset, "an int value with a range past 2^24", "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24");
        }

        if (uses.GetValueOrDefault(operation) >= 2)
        {
            Emitter.Emit(form);
        }

        return form;
    }

    private (ImmutableArray<Term> Terms, double Bias) FormOf(ScalarValue value, int offset) => value switch
    {
        ConstantValue constant => ([], constant.Number),
        AffineValue { Wire: null } open => (open.Terms, open.Bias),
        _ => ([new Term(Emit(value, offset), 1)], 0),
    };

    // a * b with two wires: Multiply. An int product must stay within 2^24.
    private WireValue Multiply(ScalarValue left, ScalarValue right, ScalarKind kind, int offset)
    {
        var leftWire = Emit(left, offset);
        var rightWire = Emit(right, offset);
        var product = Macro(offset, "a product", () => Circuit.Multiply(leftWire, rightWire));
        if (kind == ScalarKind.Int)
        {
            var range = Circuit.Ranges[product.Position];
            if (!(range.Minimum >= -16_777_216 && range.Maximum <= 16_777_216))
            {
                throw Refuse(offset, "an int value with a range past 2^24", "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24");
            }
        }

        return new WireValue(product, kind);
    }

    // and, or, xor of bits: AND, OR, XOR (plan, "Decisions", question 4).
    private ScalarValue BitOperation(SsaValue operation)
    {
        var offset = operation.IlOffset;
        if (KindOf(operation) != ScalarKind.Bit)
        {
            throw Refuse(offset, $"the IL instruction {IlDecoder.NameOf(operation.Op)} on int values", "the program has no exact form for bit operations on int values");
        }

        var left = Scalar(operation.Operands[0], offset);
        var right = Scalar(operation.Operands[1], offset);
        foreach (var (constant, other) in new[] { (left, right), (right, left) })
        {
            if (constant is ConstantValue { Number: var bit })
            {
                return (operation.Op, bit) switch
                {
                    (ILOpCode.And, 0) or (ILOpCode.Or, 1) => new ConstantValue(bit, ScalarKind.Bit),
                    (ILOpCode.Xor, 1) => Not(other),
                    _ => other,
                };
            }
        }

        return operation.Op switch
        {
            ILOpCode.And => new AndValue([.. Literals<AndValue>(left), .. Literals<AndValue>(right)]),
            ILOpCode.Or => new OrValue([.. Literals<OrValue>(left), .. Literals<OrValue>(right)]),
            _ => new XorValue(left, right),
        };
    }

    // An AND that reads an AND with no wire takes its literals in order. The same holds for OR.
    private static ImmutableArray<ScalarValue> Literals<T>(ScalarValue value)
        where T : ScalarValue => value switch
        {
            AndValue { Wire: null } conjunction when typeof(T) == typeof(AndValue) => conjunction.Literals,
            OrValue { Wire: null } disjunction when typeof(T) == typeof(OrValue) => disjunction.Literals,
            _ => [value],
        };

    private static ScalarValue Not(ScalarValue value) => value switch
    {
        ConstantValue constant => new ConstantValue(1 - constant.Number, ScalarKind.Bit),
        NotValue { Wire: null } negation => negation.Operand,
        _ => new NotValue(value),
    };

    // The comparison opcodes (plan, "Decisions", question 4). "cgt; ldc.i4.0; ceq" and
    // "clt; ldc.i4.0; ceq" give the other relation, also in the .un forms.
    private ScalarValue Comparison(SsaValue operation)
    {
        var offset = operation.IlOffset;
        if (invertedComparisons.TryGetValue(operation, out var inner))
        {
            var relation = inner.Op is ILOpCode.Cgt or ILOpCode.Cgt_un ? Relation.LessOrEqual : Relation.GreaterOrEqual;
            return Compare(inner, relation, inner.IlOffset);
        }

        if (operation.Op == ILOpCode.Ceq)
        {
            var left = Scalar(operation.Operands[0], offset);
            var right = Scalar(operation.Operands[1], offset);
            foreach (var (bit, constant) in new[] { (left, right), (right, left) })
            {
                if (bit.Kind == ScalarKind.Bit && constant is ConstantValue { Number: 0 or 1 } value)
                {
                    return value.Number == 0 ? Not(bit) : bit;
                }
            }

            return Compare(operation, Relation.Equal, offset);
        }

        return Compare(operation, operation.Op is ILOpCode.Cgt or ILOpCode.Cgt_un ? Relation.Greater : Relation.Less, offset);
    }

    private ScalarValue Compare(SsaValue comparison, Relation relation, int offset)
    {
        var left = Scalar(comparison.Operands[0], offset);
        var right = Scalar(comparison.Operands[1], offset);
        if (comparison.Op is ILOpCode.Cgt_un or ILOpCode.Clt_un && (left.Kind == ScalarKind.Int || right.Kind == ScalarKind.Int))
        {
            throw Refuse(offset, "an unsigned comparison", "the program has no unsigned int values");
        }

        if (left is ConstantValue a && right is ConstantValue b)
        {
            var holds = relation switch
            {
                Relation.Greater => a.Number > b.Number,
                Relation.GreaterOrEqual => a.Number >= b.Number,
                Relation.Less => a.Number < b.Number,
                Relation.LessOrEqual => a.Number <= b.Number,
                _ => a.Number == b.Number,
            };
            return new ConstantValue(holds ? 1 : 0, ScalarKind.Bit);
        }

        var leftWire = Emit(left, offset);
        var rightWire = Emit(right, offset);
        return new WireValue(Macro(offset, "a comparison", () => Circuit.Compare(leftWire, relation, rightWire)), ScalarKind.Bit);
    }

    // conv.r4: an int value as a float, with no change of the value. An int form is exact within 2^24.
    private ScalarValue ToFloat(ScalarValue value, int offset) => value switch
    {
        _ when value.Kind == ScalarKind.Float => value,
        ConstantValue constant => new ConstantValue((float)constant.Number, ScalarKind.Float),
        AffineValue { Wire: null } open => new AffineValue(ScalarKind.Float, AffineOp.Convert, open, null, 1, open.Terms, open.Bias, offset),
        _ => new WireValue(Emit(value, offset), ScalarKind.Float),
    };

    // conv.i4: a float on a grid of 1, with no change of the value.
    private ScalarValue ToInt(ScalarValue value, int offset)
    {
        if (value.Kind != ScalarKind.Float)
        {
            return value;
        }

        if (value is ConstantValue constant)
        {
            return Math.Abs(constant.Number) <= 16_777_216
                ? new ConstantValue(Math.Truncate(constant.Number), ScalarKind.Int)
                : throw Refuse(offset, "a conversion from float to int", "the int value must lie within -2^24..2^24");
        }

        var wire = Emit(value, offset);
        var range = Circuit.Ranges[wire.Position];
        if (Circuit.Grids[wire.Position] is not >= 1 || !(range.Minimum >= -16_777_216 && range.Maximum <= 16_777_216))
        {
            throw Refuse(offset, "a conversion from float to int",
                "the program converts a float to int only for a value on a grid of 1 within -2^24..2^24");
        }

        return new WireValue(wire, ScalarKind.Int);
    }
    // A field of an aggregate (plan, "IL opcode table", row ldfld).
    private Value ReadField(Value source, FieldInfo field, int offset) =>
        source is AggregateValue aggregate && aggregate.Fields.TryGetValue(field.Name, out var value)
            ? value
            : throw Refuse(offset, $"a read of the field {field.DeclaringType?.Name}.{field.Name}", "the program reads a field of a struct, a record or a tuple value");

    // A field store through the address of a local or an argument: a new value of the variable.
    private Value StoreField(SsaValue operation)
    {
        var path = (ImmutableArray<FieldInfo>)operation.Member!;
        if (Get(operation.Operands[0]) is not AggregateValue aggregate)
        {
            throw Refuse(operation.IlOffset, $"a store into the field {path[^1].Name}", "the program changes a field of a struct value only");
        }

        return aggregate.With([.. path.Select(field => field.Name)], Get(operation.Operands[1]));
    }

    // An initonly static field of type float, int, bool, float[] or int[]: constants, read one time
    // at translation (plan, "Behavior with no direct form", row "a static readonly field").
    private Value ReadStatic(FieldInfo field, int offset)
    {
        if (!field.IsInitOnly || field.IsLiteral)
        {
            throw Refuse(offset, $"the static field {field.DeclaringType?.Name}.{field.Name}", "the program reads only a static readonly field; mark the field readonly");
        }

        var value = field.GetValue(null);
        return value switch
        {
            float number => new ConstantValue(number, ScalarKind.Float),
            int number => IntConstant(number, offset),
            bool bit => new ConstantValue(bit ? 1 : 0, ScalarKind.Bit),
            float[] numbers => new ArrayValue(typeof(float), [.. numbers.Select(n => (Value)new ConstantValue(n, ScalarKind.Float))], isLocal: false),
            int[] numbers => new ArrayValue(typeof(int), [.. numbers.Select(n => (Value)IntConstant(n, offset))], isLocal: false),
            _ => throw Refuse(offset, $"the static field {field.DeclaringType?.Name}.{field.Name} of type {field.FieldType.Name}",
                "a static readonly field holds a float, an int, a bool, a float[] or an int[]"),
        };
    }

    private Value? Call(SsaValue operation)
    {
        var offset = operation.IlOffset;
        var callee = (MethodInfo)operation.Member!;
        if (operation.Op == ILOpCode.Callvirt && callee.IsVirtual && !callee.IsFinal)
        {
            throw Refuse(offset, $"a virtual {CallText(callee)[2..]}", "the program has no virtual calls");
        }

        var operands = operation.Operands.Select(Get).ToImmutableArray();
        if (Intrinsics.TryLower(this, callee, operands, offset) is { } known)
        {
            return known;
        }

        if (!callee.IsStatic && callee.IsSpecialName && callee.Name.StartsWith("get_", StringComparison.Ordinal) && operands is [var receiver])
        {
            var property = callee.DeclaringType!.GetProperty(callee.Name[4..], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && DataTypes.BackingField(property) is { } field)
            {
                return ReadField(receiver, field, offset);
            }

            throw Refuse(offset, $"a call to the computed property {callee.DeclaringType.Name}.{callee.Name[4..]}",
                "straight-line translation reads only a property that returns its backing field");
        }

        if (callee.GetCustomAttribute<HandwiredAttribute>() is not null)
        {
            throw Refuse(offset, CallText(callee), "straight-line translation does not inline a call to a [Handwired] method");
        }

        throw Refuse(offset, CallText(callee), Intrinsics.RefusalReason(callee));
    }

    // newobj of a data constructor, a ValueTuple or a Tuple: a new aggregate value. A field that the
    // constructor does not set holds its zero value.
    private AggregateValue Construct(ConstructorInfo constructor, ImmutableArray<Value> parameters, int offset)
    {
        var type = constructor.DeclaringType!;
        var fields = type.BaseType == typeof(object) || type.BaseType == typeof(ValueType) ? DataTypes.ConstructorFields(constructor) : null;
        if (fields is null)
        {
            throw Refuse(offset, $"the constructor of {type.Name}", "a data constructor only stores its parameters into fields of its type");
        }

        var values = ImmutableDictionary.CreateBuilder<string, Value>(StringComparer.Ordinal);
        foreach (var field in DataTypes.InstanceFields(type))
        {
            values[field.Name] = fields.TryGetValue(field.Name, out var index)
                ? parameters[index]
                : DataTypes.Zero(field.FieldType)
                    ?? throw Refuse(offset, $"the constructor of {type.Name}", $"the constructor does not set the field {field.Name}, which has no zero value");
        }

        return new AggregateValue(type, values.ToImmutable());
    }

    private ArrayValue NewArray(SsaValue operation)
    {
        var offset = operation.IlOffset;
        var element = (Type)operation.Member!;
        if (Scalar(operation.Operands[0], offset) is not ConstantValue { Number: var length } || length < 0)
        {
            throw Refuse(offset, "an array with a runtime length", "the program has arrays with a constant length only");
        }

        var zero = DataTypes.Zero(element)
            ?? throw Refuse(offset, $"an array of {element.Name}", "an array holds float, int or bool values, or structs of them");
        return new ArrayValue(element, [.. Enumerable.Repeat(zero, (int)length)], isLocal: true);
    }

    private ArrayValue Array(SsaValue value, int offset) => Get(value) as ArrayValue
        ?? throw Refuse(offset, "an array that is not a fixed-length value", "the program reads an array of a cell, a static readonly field or newarr");

    // ldelem: a constant index gives the element; a computed index gives IndexRead with the range
    // of the index in 0..length - 1 (plan, "Lowering table", rows "array[i]").
    private Value ReadElement(ArrayValue array, SsaValue indexValue, int offset)
    {
        var count = array.Elements.Length;
        var index = Scalar(indexValue, offset);
        if (index is ConstantValue constant)
        {
            return constant.Number >= 0 && constant.Number < count
                ? array.Elements[(int)constant.Number]
                : throw Refuse(offset, $"the array index {Fp32.Format(constant.Number)}", $"the index must lie in 0..{count - 1}");
        }

        if (index.Kind != ScalarKind.Int || array.Elements.Any(element => element is not ScalarValue))
        {
            throw Refuse(offset, "an array read at a computed index", "a computed index is an int, and the array holds float, int or bool values");
        }

        var indexWire = Emit(index, offset);
        var range = Circuit.Ranges[indexWire.Position];
        if (!(range.Minimum >= 0 && range.Maximum <= count - 1))
        {
            throw Refuse(offset, "an array index with a range past the array",
                $"the index range {Fp32.Format(range.Minimum)}..{Fp32.Format(range.Maximum)} must lie in 0..{count - 1}");
        }

        var candidates = array.Elements.Select(element => Emit(element, offset)).ToArray();
        var kind = ((ScalarValue)array.Elements[0]).Kind;
        var read = Macro(offset, "an array read at a computed index", () => Circuit.IndexRead(indexWire, candidates, OutOfRange.Clamp));
        if (kind == ScalarKind.Int)
        {
            // An index read infers no grid. Each element of an int array is an integer, so the read is
            // on the grid 1, and an int form that reads it stays exact.
            Circuit.AssumeGrid(read, 1);
        }

        return new WireValue(read, kind);
    }

    private void StoreElement(SsaValue operation)
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

        array.Elements[(int)index] = Get(operation.Operands[2]);
    }
}
