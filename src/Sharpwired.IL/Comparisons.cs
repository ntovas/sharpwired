// No Handwired source. Handwired has no translator. The comparisons of the operation lowering, after
// the plan, section "Decisions", question 4. This file holds the comparison opcodes, and the comparison of
// two SSA values for an opcode and for a branch. It also holds the Compare macro at its instruction. The source rows are the
// rows "a >= b ..." of $REF/docs/expressions.md. This file holds a part of OperationLowering, so that
// OperationLowering.cs stays below 800 lines.

using System.Reflection.Metadata;
using Sharpwired.Core;

namespace Sharpwired.IL;

internal sealed partial class OperationLowering
{
    // The comparison opcodes (plan, "Decisions", question 4). "cgt; ldc.i4.0; ceq" and
    // "clt; ldc.i4.0; ceq" give the other relation, also in the .un forms.
    private ScalarValue Comparison(SsaValue operation)
    {
        var offset = operation.IlOffset;
        if (invertedComparisons.TryGetValue(operation, out var inner))
        {
            var relation = inner.Op is ILOpCode.Cgt or ILOpCode.Cgt_un ? Relation.LessOrEqual : Relation.GreaterOrEqual;
            return CompareOperands(inner.Operands[0], inner.Operands[1], relation, inner.Op is ILOpCode.Cgt_un or ILOpCode.Clt_un, inner.IlOffset);
        }

        var relationOfOp = operation.Op switch
        {
            ILOpCode.Ceq => Relation.Equal,
            ILOpCode.Cgt or ILOpCode.Cgt_un => Relation.Greater,
            _ => Relation.Less,
        };
        return CompareOperands(operation.Operands[0], operation.Operands[1], relationOfOp, operation.Op is ILOpCode.Cgt_un or ILOpCode.Clt_un, offset);
    }

    /// <summary>
    /// The comparison of two SSA values, for a comparison opcode and for a branch. An equality with
    /// the bit constant 0 or 1 gives the NOT of the bit or the bit. An unsigned comparison of int
    /// values is refused.
    /// </summary>
    public ScalarValue CompareOperands(SsaValue leftValue, SsaValue rightValue, Relation relation, bool unsigned, int offset)
    {
        var left = Scalar(leftValue, offset);
        var right = Scalar(rightValue, offset);
        if (unsigned && UnsignedZeroTest(left, right, relation) is { } notZero)
        {
            // Roslyn writes the int test x != 0 as cgt.un against 0 (final fix wave, item D). For
            // an int, x >u 0 is x != 0, and x <=u 0 is x == 0. Release writes b ? 1 : 0 as cgt.un of
            // the bool b against 0 (phase 4 carried item b). For a bit, b >u 0 is b, and b <=u 0 is
            // NOT b. So a bit gives no operation.
            if (notZero.Value.Kind == ScalarKind.Bit)
            {
                return notZero.IsNotEqual ? notZero.Value : Gates.Not(notZero.Value);
            }

            var equal = CompareValues(notZero.Value, new ConstantValue(0, ScalarKind.Int), Relation.Equal, offset);
            return notZero.IsNotEqual ? Gates.Not(equal) : equal;
        }

        if (unsigned && (left.Kind == ScalarKind.Int || right.Kind == ScalarKind.Int))
        {
            throw Refuse(offset, "an unsigned comparison", "the program has no unsigned int values");
        }

        if (relation == Relation.Equal)
        {
            foreach (var (bit, constant) in new[] { (left, right), (right, left) })
            {
                if (bit.Kind == ScalarKind.Bit && constant is ConstantValue { Number: 0 or 1 } value)
                {
                    return value.Number == 0 ? Gates.Not(bit) : bit;
                }
            }
        }

        return CompareValues(left, right, relation, offset);
    }

    // An unsigned comparison of an int or a bit with the constant 0 that is an equality test. The
    // tests x >u 0 and 0 <u x are x != 0. The tests x <=u 0 and 0 >=u x are x == 0. Null for each
    // other comparison.
    private static (ScalarValue Value, bool IsNotEqual)? UnsignedZeroTest(ScalarValue left, ScalarValue right, Relation relation) =>
        (left, right, relation) switch
        {
            ({ Kind: ScalarKind.Int or ScalarKind.Bit }, ConstantValue { Number: 0 }, Relation.Greater) => (left, true),
            ({ Kind: ScalarKind.Int or ScalarKind.Bit }, ConstantValue { Number: 0 }, Relation.LessOrEqual) => (left, false),
            (ConstantValue { Number: 0 }, { Kind: ScalarKind.Int or ScalarKind.Bit }, Relation.Less) => (right, true),
            (ConstantValue { Number: 0 }, { Kind: ScalarKind.Int or ScalarKind.Bit }, Relation.GreaterOrEqual) => (right, false),
            _ => null,
        };

    /// <summary>The comparison of two values at the instruction: a constant for two constants, otherwise the Compare macro.</summary>
    public ScalarValue CompareValues(ScalarValue left, ScalarValue right, Relation relation, int offset)
    {
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

        var bound = BoundOf(left, right, relation);
        if (DecideInLoop(bound) is { } decided)
        {
            return decided;
        }

        var leftWire = Emit(left, offset);
        var rightWire = Emit(right, offset);
        var bit = Macro(offset, "a comparison", () => Circuit.Compare(leftWire, relation, rightWire));
        RecordComparison(bit, bound);
        return new WireValue(bit, ScalarKind.Bit);
    }
}
