// No Handwired source. Handwired has no translator. The emit of a symbolic value, after the plan,
// section "Emission rules": the C#-equal rule of an affine form (exact form, one C# operation, or a
// RoundAffine chain in C# order), and the bit forms and the gate rules 5 to 7 of section
// "Decisions", question 4. The bit forms follow $REF/docs/expressions.md, row "NOT, AND and OR": NOT
// is 1 - bit, AND of N bits is relu(sum - (N - 1)), OR is 1 - relu(1 - sum). XOR is
// relu(a - b) + relu(b - a). A gate follows the row "condition ? on : off". The emitter also keeps
// the overflow bits of the loops with [MaxIterations] (LoopForms.cs), because the inlined methods
// share it.
//
// The circuit computes both sides of each gate, and each inactive loop copy, where C# computes only
// the side that it takes. So a side whose range is not finite can be Infinity in the circuit while C#
// does not compute it, and Select(0, kept, Infinity) gives NaN. The emitter refuses such a side
// (review of task 5, round 2, finding A). A cell holds a finite value (the cell validator checks it),
// and an emitted gate holds one of its checked sides, so these sides are accepted.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The overflow bit of a loop with [MaxIterations] (plan, "Decisions", question 5, form 2).</summary>
internal sealed record LoopOverflow(Wire Bit, SourceLocation Location, int Copies);

/// <summary>
/// A refusal of <see cref="Emitter.CheckRanges"/>: the construct, the reason, and for a gate the IL
/// offset of its branch (-1 for none) in the lowering that holds it.
/// </summary>
internal sealed record RangeFault(string Construct, string Reason, int IlOffset, OperationLowering? Owner);

/// <summary>Emits symbolic values into the circuit. After the emit, a value holds its wire.</summary>
internal sealed class Emitter(Circuit circuit, bool recognizePatterns = true)
{
    public Circuit Circuit { get; } = circuit;

    /// <summary>
    /// True when the lowering recognizes the Handwired patterns (RunningTotals.cs, ThresholdCounts.cs).
    /// The inlined methods share the emitter, so they share the option.
    /// </summary>
    public bool RecognizePatterns { get; } = recognizePatterns;

    /// <summary>
    /// The overflow bits of the loops of form 2 with [MaxIterations], in the order of the unroll, also
    /// of the loops in inlined methods. A bit is 1 when the plain loop needs more copies.
    /// </summary>
    public List<LoopOverflow> Overflows { get; } = [];

    // The wires of the emitted gates. Each one holds a side with a finite value.
    private readonly HashSet<Wire> gateWires = [];

    // True when an emitted value passed the FP32 range after the last CheckRanges.
    private bool pastRange;

    // True when an emitted gate read a side that can be Infinity after the last CheckRanges.
    private bool openSide;

    // The first gate with a side that can be Infinity after the last CheckRanges, or null.
    private GateValue? openGate;

    /// <summary>
    /// Returns the construct and the reason of a refusal, or null. A value that the translator emitted
    /// after the last check can overflow FP32 when its range is not finite while each wire that it
    /// reads has a finite range, or when it is a constant that is not finite. The rule checks the value
    /// wires (an affine form, a gate, a product), and not the operations in a macro, because a macro
    /// can use a saturation with a range that is not finite. A gate is refused when a side that is not
    /// a cell or a gate has a range that is not finite. For such a gate the fault also gives the
    /// IL offset of its branch and the lowering that holds it, so the refusal names the line of the
    /// branch (final fix wave, item E).
    /// </summary>
    public RangeFault? CheckRanges()
    {
        RangeFault? fault = pastRange ? new RangeFault(OperationLowering.OverflowConstruct, OperationLowering.OverflowReason, -1, null)
            : openSide ? new RangeFault(OperationLowering.OpenSideConstruct, OperationLowering.OpenSideReason, openGate?.IlOffset ?? -1, openGate?.Owner)
            : null;
        pastRange = false;
        openSide = false;
        openGate = null;
        return fault;
    }

    // A side of a gate. A cell and a gate hold finite values; another wire with a range that is not
    // finite can hold Infinity.
    private Wire Side(Wire side, GateValue gate)
    {
        if (!IsFinite(Circuit.Ranges[side.Position]) && side.Position >= Circuit.SourceSize && !gateWires.Contains(side))
        {
            openSide = true;
            openGate ??= gate;
        }

        return side;
    }

    private Wire Gate(Wire gate)
    {
        gateWires.Add(gate);
        return gate;
    }

    /// <summary>Notes a value wire and the wires that it reads, for <see cref="CheckRanges"/>.</summary>
    public void NoteValue(Wire value, IEnumerable<Wire> operands)
    {
        if (!IsFinite(Circuit.Ranges[value.Position]) && operands.All(operand => IsFinite(Circuit.Ranges[operand.Position])))
        {
            pastRange = true;
        }
    }

    private static bool IsFinite(NumericRange range) => double.IsFinite(range.Minimum) && double.IsFinite(range.Maximum);

    /// <summary>Emits the value and returns its wire. A value that has a wire gives that wire.</summary>
    public Wire Emit(ScalarValue value)
    {
        if (value.Wire is { } wire)
        {
            return wire;
        }

        if (value is ConstantValue { Number: var number } && !double.IsFinite(number))
        {
            pastRange = true;
        }

        var emitted = value switch
        {
            ConstantValue constant => Circuit.Constant(constant.Number),
            AffineValue affine => EmitAffine(affine),
            NotValue negation => Circuit.Affine([(Emit(negation.Operand), -1)], 1),
            AndValue conjunction => EmitAnd(conjunction.Literals),
            OrValue disjunction => EmitOr(disjunction.Literals),
            XorValue xor => EmitXor(Emit(xor.Left), Emit(xor.Right)),
            GateValue gate => EmitGate(gate),
            _ => throw new InvalidOperationException($"the value {value.GetType().Name} has no emit rule"),
        };

        // A constant has no single wire: each emit asks the circuit, which keeps one wire for each value.
        if (value is not ConstantValue)
        {
            value.Wire = emitted;
        }

        switch (value)
        {
            case AffineValue affine:
                NoteValue(emitted, affine.Terms.Select(term => term.Wire));
                break;
            case GateValue gate:
                NoteValue(emitted, new[] { gate.Condition, gate.On, gate.Off }.Select(operand => operand.Wire).OfType<Wire>());
                break;
        }

        return emitted;
    }

    /// <summary>
    /// True when the affine form is exact: each coefficient of the stored form and its bias are FP32
    /// values, and the stored form has a grid. Then each partial sum in any order is an exact FP32
    /// value, and so is each intermediate value of the C# expression (plan, "Emission rules", step 2).
    /// A form that reads a rounded affine wire, which the builder composes, is not exact.
    /// </summary>
    public bool IsExact(IReadOnlyList<Term> terms, double bias)
    {
        if (terms.Any(term => IsRoundedProducer(term.Wire)))
        {
            return false;
        }

        var (storedTerms, storedBias) = Circuit.PreviewAffine(terms, bias);
        return storedTerms.All(term => (double)(float)term.Coefficient == term.Coefficient)
            && (double)(float)storedBias == storedBias
            && Circuit.FormGrid(storedTerms, storedBias) is not null;
    }

    // True for a wire that Circuit.Affine composes into its reader (an ordinary affine operation with
    // few terms) and that has no grid. Such a wire holds a rounded value, and the composition puts
    // its unrounded form into the reader, so a form that reads the wire is not exact (review of
    // task 4, finding 1). A constant folds with no change of its value, so it is not in this rule.
    private bool IsRoundedProducer(Wire wire)
    {
        if (Circuit.Grids[wire.Position] is not null)
        {
            return false;
        }

        var (terms, bias) = Circuit.PreviewAffine([new Term(wire, 2)], 0);
        return !terms.IsEmpty && !(terms is [var only] && only.Wire == wire && only.Coefficient == 2 && bias == 0);
    }

    /// <summary>
    /// True when the form and each C# intermediate value on the way to it are exact. A stored form
    /// can be exact when its terms cancel or its constants fold, while C# rounds an intermediate
    /// value, for example (a + b) - b. So the rule checks the form of each IL instruction.
    /// </summary>
    public bool IsExactChain(AffineValue form) =>
        IsExact(form.Terms, form.Bias)
        && (form.Left is not AffineValue { Wire: null } left || IsExactChain(left))
        && (form.Right is not AffineValue { Wire: null } right || IsExactChain(right));

    // The C#-equal rule of the plan, section "Emission rules".
    private Wire EmitAffine(AffineValue form)
    {
        if (IsExactChain(form))
        {
            return Circuit.Affine(form.Terms, form.Bias);
        }

        if (form.IsOneInstruction)
        {
            var (storedTerms, storedBias) = Circuit.PreviewAffine(form.Terms, form.Bias);
            var (normalTerms, normalBias) = Normalize(form.Terms, form.Bias);
            var elements = storedTerms.Length + (storedBias != 0 ? 1 : 0);
            if (elements <= 2 && SameForm(storedTerms, storedBias, normalTerms, normalBias))
            {
                return Circuit.Affine(form.Terms, form.Bias);
            }
        }

        if (form.Kind == ScalarKind.Int)
        {
            throw new InvalidOperationException("an int form that is not exact reached the emit; the lowering refuses it at its instruction");
        }

        return EmitRounded(form);
    }

    // Step 4: one RoundAffine operation for each IL instruction, with the operands in C# order. A
    // RoundAffine operation is never composed into a reader.
    private Wire EmitRounded(AffineValue form)
    {
        if (form.Wire is { } done)
        {
            return done;
        }

        var terms = new List<Term>();
        var bias = 0.0;
        void Operand(ScalarValue operand, double coefficient)
        {
            if (operand is ConstantValue constant)
            {
                bias += coefficient * constant.Number;
                return;
            }

            terms.Add(new Term(operand is AffineValue { Wire: null } open ? EmitRounded(open) : Emit(operand), coefficient));
        }

        switch (form.Op)
        {
            case AffineOp.Add:
                Operand(form.Left, 1);
                Operand(form.Right!, 1);
                break;
            case AffineOp.Subtract:
                Operand(form.Left, 1);
                Operand(form.Right!, -1);
                break;
            case AffineOp.Negate:
                Operand(form.Left, -1);
                break;
            case AffineOp.Scale:
                Operand(form.Left, form.Factor);
                break;
            case AffineOp.Convert:
                // conv.r4 of an exact int form changes no value.
                form.Wire = form.Left is AffineValue { Wire: null } inner ? EmitRounded(inner) : Emit(form.Left);
                return form.Wire.Value;
            default:
                throw new InvalidOperationException($"unknown affine instruction {form.Op}");
        }

        var wire = Circuit.RoundAffine(terms, bias);
        form.Wire = wire;
        return wire;
    }

    // The gate rules 5 to 7 of the plan, section "Decisions", question 4. The emit order is the
    // condition, then the off value, then the on value, then the gate operations, as the arguments of
    // circuit.Select(gate, off, on) in the builder toggle.
    private Wire EmitGate(GateValue gate)
    {
        if (gate.Kind != ScalarKind.Bit && gate.On is ConstantValue on && gate.Off is ConstantValue off)
        {
            // Rule 5: ITE(c, k1, k0) = [(c, k1 - k0)] + k0 when k1 - k0 is an FP32 value and f(k0 + (k1 - k0)) == k1.
            var condition = Emit(gate.Condition);
            var step = on.Number - off.Number;
            if ((double)(float)step == step && (float)(off.Number + step) == (float)on.Number)
            {
                return Circuit.Affine([(condition, step)], off.Number);
            }

            var width = Circuit.Width;
            var offWire = Circuit.Constant(off.Number);
            var onWire = Circuit.Constant(on.Number);
            return Chosen(gate, Circuit.Select(condition, offWire, onWire), width, ChosenSide(offWire), ChosenSide(onWire));
        }

        if (gate.Kind != ScalarKind.Bit && gate.Off is ConstantValue { Number: 0 })
        {
            // Rule 6: ITE(c, x, 0) = Multiply(c, x), the Handwired form of "condition ? value : 0".
            var condition = Emit(gate.Condition);
            var onWire = Side(Emit(gate.On), gate);
            var width = Circuit.Width;
            return Gate(Chosen(gate, Circuit.Multiply(condition, onWire), width, ChosenSide(null), ChosenSide(onWire)));
        }

        // Rule 7: Select(c, off, on).
        var gateWire = Emit(gate.Condition);
        var offValue = Side(Emit(gate.Off), gate);
        var onValue = Side(Emit(gate.On), gate);
        var start = Circuit.Width;
        return Gate(Chosen(gate, Circuit.Select(gateWire, offValue, onValue), start, ChosenSide(offValue), ChosenSide(onValue)));
    }

    // The range and the grid of a gate side. Null is the constant 0 of rule 6: range 0..0 and no
    // grid limit. A constant wire has the grid of its value, and 0 has no grid limit.
    private (NumericRange Range, double Grid) ChosenSide(Wire? side) => side is { } wire
        ? (Circuit.Ranges[wire.Position], Circuit.Constants[wire.Position] switch
        {
            0 => double.PositiveInfinity,
            { } number when double.IsFinite(number) => Fp32.LowestPowerOfTwo(number),
            _ => Circuit.Grids[wire.Position] ?? double.NaN,
        })
        : (new NumericRange(0, 0), double.PositiveInfinity);

    // Declares the range, the grid and the bit fact of a gate result (final fix wave, items B and C).
    // The gate returns exactly one side, up to the sign of zero (execution-model.md, "Macro
    // contracts": Select "Returns exactly one operand"). So its range is the union of the side ranges,
    // and its grid is the finer side grid. The inferred range of Select is the sum of two product
    // ranges. In a loop that range grows in each copy, so without the declaration a gate in a long
    // loop passes the FP32 range. A new result (a wire at or after start) has no readers. The
    // circuit can give back an existing wire for the same operation. That wire already has the same
    // declaration, or it is a product of the same bit and the same side, so the declaration is true
    // for its readers too.
    private Wire Chosen(GateValue gate, Wire result, int start, (NumericRange Range, double Grid) off, (NumericRange Range, double Grid) on)
    {
        var existing = result.Position < start;
        var minimum = Math.Min(off.Range.Minimum, on.Range.Minimum);
        var maximum = Math.Max(off.Range.Maximum, on.Range.Maximum);
        if (gate.Kind == ScalarKind.Bit && gate.On.Kind == ScalarKind.Bit && gate.Off.Kind == ScalarKind.Bit && minimum >= 0 && maximum <= 1)
        {
            Circuit.AssumeBit(result, existing);
            return result;
        }

        if (double.IsFinite(minimum) && double.IsFinite(maximum))
        {
            Circuit.AssumeRange(result, minimum, maximum, existing);
        }

        var grid = Math.Min(off.Grid, on.Grid);
        if (Fp32.IsPowerOfTwoGrid(grid))
        {
            Circuit.AssumeGrid(result, grid, existing);
        }

        return result;
    }

    // AND of literals l1..ln: Reglu([], 1, [(l1, 1), ..., (ln, 1)], 1 - n). Each literal emits first, in order.
    private Wire EmitAnd(ImmutableArray<ScalarValue> literals)
    {
        var wires = literals.Select(Emit).ToArray();
        return Circuit.Reglu([], 1, [.. wires.Select(wire => new Term(wire, 1))], 1 - wires.Length);
    }

    // OR of literals: 1 - relu(1 - sum).
    private Wire EmitOr(ImmutableArray<ScalarValue> literals)
    {
        var wires = literals.Select(Emit).ToArray();
        var none = Circuit.Reglu([], 1, [.. wires.Select(wire => new Term(wire, -1))], 1);
        return Circuit.Affine([(none, -1)], 1);
    }

    // XOR of two bits: relu(a - b) + relu(b - a).
    private Wire EmitXor(Wire left, Wire right) => Circuit.Affine([
        (Circuit.Reglu([], 1, [(left, 1), (right, -1)], 0), 1),
        (Circuit.Reglu([], 1, [(right, 1), (left, -1)], 0), 1),
    ]);

    // The terms sorted by wire, the terms of one wire merged, and the zero coefficients dropped, as
    // the builder normalizes a form before it folds constants and composes producers.
    private static (ImmutableArray<Term> Terms, double Bias) Normalize(IReadOnlyList<Term> terms, double bias)
    {
        var merged = new List<Term>();
        foreach (var term in terms.OrderBy(term => term.Wire.Position))
        {
            if (merged.Count > 0 && merged[^1].Wire == term.Wire)
            {
                merged[^1] = merged[^1] with { Coefficient = merged[^1].Coefficient + term.Coefficient };
            }
            else
            {
                merged.Add(term);
            }
        }

        return ([.. merged.Where(term => term.Coefficient != 0)], bias);
    }

    private static bool SameForm(ImmutableArray<Term> left, double leftBias, ImmutableArray<Term> right, double rightBias) =>
        leftBias == rightBias && left.Length == right.Length
        && left.Zip(right).All(pair => pair.First.Wire == pair.Second.Wire && pair.First.Coefficient == pair.Second.Coefficient);
}
