// Handwired source: src/runtime/numeric-ui-core.ts (Circuit.indexReadAffine, reachableCandidates), the
// read of src/json-search/json-step.ts (a read at a computed index with the clamp or the zero rule), and
// createStepAnd of src/json-search/regex/data-program.ts (transitions[class][from][to] && bit as one
// read at class + classes × (1 - bit) with the zero rule above). This file holds the array reads of
// the operation lowering, after the plan of phase 4 ("Lowering table", rows "array[i]") and the rules 2
// to 5 of the phase 6 plan, task 1 (question 4):
// - Rule 2: t[Math.Clamp(i, 0, t.Length - 1)] is one index read with the clamp rule at i.
// - Rule 3: Hw.ReadOrZero(t, i) is one index read with the zero rule on both sides.
// - Rule 4: bit && t[i], with t a bool[] and i proven in 0..t.Length - 1, is one index read at
//   i + n × (1 - bit) with the zero rule above. The read of t[i] has no wire until its reader needs
//   it (TableReadValue), so that the AND of the bit and the read can give the gated read (Emitter.cs).
// - Rule 5: an index read registers only the candidates that its index range reaches, and the index
//   form gets the offset of the first one as bias.
// The range of an index form comes from the declared ranges of the wires that the form reads. A
// reader composes a small affine producer, for example the Select of ClampExact, into its own form.
// The composed form has the same value, but the inferred range of its terms can be wider (the plan
// probe: 0..528 for Math.Clamp(depth, 0, 16)). With RecognizePatterns = false, a read has the form of
// phase 4, and Hw.ReadOrZero reads the full table.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>
/// A Math.Clamp result with no wire yet (rule 2). The operands already have wires. The emit gives
/// ClampExact. A read whose index is this value and whose bounds are the table bounds gives one index
/// read with the clamp rule in place of ClampExact.
/// </summary>
internal sealed class ClampValue(ScalarKind kind, Wire operand, Wire minimum, Wire maximum, Func<Wire> build) : ScalarValue(kind)
{
    public Wire Operand { get; } = operand;

    public Wire Minimum { get; } = minimum;

    public Wire Maximum { get; } = maximum;

    /// <summary>Emits ClampExact.</summary>
    public Wire Build() => build();
}

/// <summary>
/// A read of a bool table at an index proven in the table, with no wire yet (rule 4). The emit gives
/// the read. An AND of this read and other bits gives the gated read.
/// </summary>
internal sealed class TableReadValue(Func<Wire?, Wire> build) : ScalarValue(ScalarKind.Bit)
{
    /// <summary>Emits the read.</summary>
    public Wire Build() => build(null);

    /// <summary>Emits the read at i + n × (1 - gate) with the zero rule above: t[i] when the gate is 1, and +0 when it is 0.</summary>
    public Wire BuildGated(Wire gate) => build(gate);
}

internal sealed partial class OperationLowering
{
    private const string ComputedRead = "an array read at a computed index";

    // An index read needs an index range strictly inside ±2^24 (Circuit.IndexReadAffine). A clamp
    // whose operand range reaches 2^24 keeps ClampExact.
    private const double IndexLimit = 16_777_216;

    // An index form: Bias + Σ coefficient × wire, with the range of its values.
    private readonly record struct IndexForm(ImmutableArray<Term> Terms, double Bias, NumericRange Range);

    // ldelem: a constant index gives the element. A computed index gives an index read with the range
    // of the index in 0..length - 1, or the clamp read of rule 2.
    private Value ReadElement(ArrayValue array, SsaValue indexValue, SsaValue operation)
    {
        var offset = operation.IlOffset;
        var count = array.Elements.Length;
        var index = Scalar(indexValue, offset);
        if (index is ConstantValue constant)
        {
            return constant.Number >= 0 && constant.Number < count
                ? array.Elements[(int)constant.Number]
                : throw Refuse(offset, $"the array index {Fp32.Format(constant.Number)}", $"the index must lie in 0..{count - 1}");
        }

        // A bit as the index, for example b ? 1 : 0, is the int 0 or 1 (rule 1).
        if (index.Kind is not (ScalarKind.Int or ScalarKind.Bit) || array.Elements.Any(element => element is not ScalarValue))
        {
            throw Refuse(offset, ComputedRead, "a computed index is an int or a bit, and the array holds float, int or bool values");
        }

        if (!Emitter.RecognizePatterns)
        {
            return Phase4Read(array, index, offset);
        }

        // The elements now: a later store into a local array does not change this read.
        ScalarValue[] elements = [.. array.Elements.Cast<ScalarValue>()];
        var kind = elements[0].Kind;
        if (index is ClampValue { Wire: null } clamp && IsConstant(clamp.Minimum, 0) && IsConstant(clamp.Maximum, count - 1)
            && Circuit.Ranges[clamp.Operand.Position] is { Minimum: > -IndexLimit, Maximum: < IndexLimit })
        {
            return Read(elements, kind, new IndexForm([new Term(clamp.Operand, 1)], 0, Circuit.Ranges[clamp.Operand.Position]), OutOfRange.Clamp, offset);
        }

        var form = IndexFormOf(index, offset);
        if (!(form.Range.Minimum >= 0 && form.Range.Maximum <= count - 1))
        {
            throw Refuse(offset, "an array index with a range past the array",
                $"the index range {Fp32.Format(form.Range.Minimum)}..{Fp32.Format(form.Range.Maximum)} must lie in 0..{count - 1}");
        }

        if (kind == ScalarKind.Bit && uses.GetValueOrDefault(operation) == 1 && form.Range.Minimum < form.Range.Maximum)
        {
            return new TableReadValue(gate => BuildRead(elements, kind, form, OutOfRange.Clamp, gate, cut: true, offset));
        }

        return Read(elements, kind, form, OutOfRange.Clamp, offset);
    }

    /// <summary>Hw.ReadOrZero(table, index): one index read with the zero rule on both sides (rule 3).</summary>
    public Value ReadOrZero(ImmutableArray<Value> operands, string construct, int offset)
    {
        if (operands[0] is not ArrayValue array || array.Elements.Length == 0 || array.Elements.Any(element => element is not ScalarValue))
        {
            throw Refuse(offset, construct, "the table is a fixed-length int or float array");
        }

        ScalarValue[] elements = [.. array.Elements.Cast<ScalarValue>()];
        var kind = elements[0].Kind;
        var index = operands[1] as ScalarValue;
        if (index is ConstantValue constant)
        {
            return constant.Number >= 0 && constant.Number < elements.Length ? elements[(int)constant.Number] : new ConstantValue(0, kind);
        }

        if (index is not { Kind: ScalarKind.Int or ScalarKind.Bit })
        {
            throw Refuse(offset, construct, "the index is an int with a declared range, or a bit");
        }

        var form = IndexFormOf(index, offset);
        return Emitter.RecognizePatterns
            ? Read(elements, kind, form, OutOfRange.Zero, offset)
            : new WireValue(BuildRead(elements, kind, form, OutOfRange.Zero, null, cut: false, offset), kind);
    }

    // The read of phase 4: the index as a wire, and IndexRead over all candidates.
    private WireValue Phase4Read(ArrayValue array, ScalarValue index, int offset)
    {
        var count = array.Elements.Length;
        var indexWire = Emit(index, offset);
        var range = Circuit.Ranges[indexWire.Position];
        if (!(range.Minimum >= 0 && range.Maximum <= count - 1))
        {
            throw Refuse(offset, "an array index with a range past the array",
                $"the index range {Fp32.Format(range.Minimum)}..{Fp32.Format(range.Maximum)} must lie in 0..{count - 1}");
        }

        var candidates = array.Elements.Select(element => Emit(element, offset)).ToArray();
        var kind = ((ScalarValue)array.Elements[0]).Kind;
        var read = Macro(offset, ComputedRead, () => Circuit.IndexRead(indexWire, candidates, OutOfRange.Clamp));
        if (kind == ScalarKind.Int)
        {
            // An index read infers no grid. Each element of an int array is an integer. So the read is
            // on the grid 1, and an int form that reads it stays exact.
            Circuit.AssumeGrid(read, 1);
        }

        return new WireValue(read, kind);
    }

    // A read with the candidate cut (rule 5). An index range that reaches one candidate gives that
    // element with no operation. An index range outside the table gives the end element with the
    // clamp rule, and +0 with the zero rule.
    private ScalarValue Read(ScalarValue[] elements, ScalarKind kind, IndexForm form, OutOfRange rule, int offset)
    {
        var count = elements.Length;
        var lowest = Math.Floor(form.Range.Minimum);
        var highest = Math.Floor(form.Range.Maximum);
        if (highest < 0 || lowest > count - 1)
        {
            return rule == OutOfRange.Zero ? new ConstantValue(0, kind) : elements[highest < 0 ? 0 : count - 1];
        }

        var first = Math.Max(0, lowest);
        var last = Math.Min(count - 1, highest);
        if (first == last && (rule == OutOfRange.Clamp || lowest == highest))
        {
            return elements[(int)first];
        }

        return new WireValue(BuildRead(elements, kind, form, rule, null, cut: true, offset), kind);
    }

    // The index read. With the cut, the candidates are the elements first..last that the index range
    // reaches, and the form gets the bias -first. An index below first can only occur when first is
    // 0, and an index above last only when last is the last element. So the rule of each side keeps
    // the value. With a gate g, the form adds n × (1 - g), with n the count of the candidates: g = 1
    // reads the candidate, and g = 0 reads past the last candidate, which is +0 by the zero rule.
    private Wire BuildRead(ScalarValue[] elements, ScalarKind kind, IndexForm form, OutOfRange rule, Wire? gate, bool cut, int offset)
    {
        var first = cut ? (int)Math.Max(0, Math.Floor(form.Range.Minimum)) : 0;
        var last = cut ? (int)Math.Min(elements.Length - 1, Math.Floor(form.Range.Maximum)) : elements.Length - 1;
        var candidates = elements[first..(last + 1)].Select(element => Emit(element, offset)).ToArray();
        var terms = form.Terms;
        var bias = form.Bias - first;
        var range = new NumericRange(form.Range.Minimum - first, form.Range.Maximum - first);
        var above = rule;
        if (gate is { } bit)
        {
            var size = candidates.Length;
            terms = [.. terms, new Term(bit, -size)];
            bias += size;
            range = new NumericRange(range.Minimum, range.Maximum + size);
            above = OutOfRange.Zero;
        }

        var read = Macro(offset, ComputedRead, () => Circuit.IndexReadAffine(terms, bias, candidates, rule, above, range));
        if (kind == ScalarKind.Int)
        {
            // An index read infers no grid. Each element of an int array is an integer, and so is +0.
            Circuit.AssumeGrid(read, 1);
        }

        return read;
    }

    // The form of an index: the terms of an open affine form, or the wire of another value. The range
    // comes from the declared ranges of the wires that the form reads. An int form is exact (plan of
    // phase 4, "Emission rules", step 5), so its value lies in this range.
    private IndexForm IndexFormOf(ScalarValue index, int offset)
    {
        if (index is AffineValue { Wire: null } open)
        {
            var minimum = open.Bias;
            var maximum = open.Bias;
            foreach (var term in open.Terms)
            {
                var range = Circuit.Ranges[term.Wire.Position];
                minimum += term.Coefficient >= 0 ? term.Coefficient * range.Minimum : term.Coefficient * range.Maximum;
                maximum += term.Coefficient >= 0 ? term.Coefficient * range.Maximum : term.Coefficient * range.Minimum;
            }

            return new IndexForm(open.Terms, open.Bias, new NumericRange(minimum, maximum));
        }

        var wire = Emit(index, offset);
        return new IndexForm([new Term(wire, 1)], 0, Circuit.Ranges[wire.Position]);
    }

    private bool IsConstant(Wire wire, double value) => Circuit.Constants[wire.Position] is { } constant && constant == value;
}
