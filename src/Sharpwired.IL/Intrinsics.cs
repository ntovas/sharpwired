// Handwired source: $REF/docs/expressions.md, table "Familiar expressions", row "Math.min(a, b),
// Math.max(a, b), clamps and Math.abs(x)", and the plan, section "Lowering table", rows Math.Min to
// Hw.FirstActive. The known calls of a plain method: Math.Min, Math.Max, Math.Clamp, Math.Abs,
// MathF.Min, MathF.Max and MathF.Abs give MinimumExact, MaximumExact, ClampExact and Absolute. Each
// Hw method gives its macro (Relu, Boolean, ApplySpringMatrix, FirstActive, AnimatedObject). Hw.ReadOrZero
// gives one index read with the zero rule (phase 6 plan, task 1, rule 3; IndexReads.cs).

using System.Collections.Immutable;
using System.Reflection;
using Sharpwired.Annotations;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>The map from the known methods to the builder calls and macros.</summary>
internal static class Intrinsics
{
    /// <summary>The value of a call to a known method, or null when the method is not known.</summary>
    public static Value? TryLower(OperationLowering lowering, MethodInfo callee, ImmutableArray<Value> operands, int offset)
    {
        if (callee.DeclaringType == typeof(Hw))
        {
            return LowerHw(lowering, callee, operands, offset);
        }

        if (callee.DeclaringType != typeof(Math) && callee.DeclaringType != typeof(MathF))
        {
            return null;
        }

        var parameters = callee.GetParameters().Select(p => p.ParameterType).ToArray();
        var kind = parameters.All(p => p == typeof(float)) ? ScalarKind.Float : parameters.All(p => p == typeof(int)) ? ScalarKind.Int : (ScalarKind?)null;
        if (kind is null)
        {
            return null;
        }

        var construct = OperationLowering.CallText(callee);
        switch (callee.Name, parameters.Length, kind)
        {
            case ("Min", 2, _):
                {
                    var (a, b) = (lowering.Emit(operands[0], offset), lowering.Emit(operands[1], offset));
                    return new WireValue(lowering.Macro(offset, construct, () => lowering.Circuit.MinimumExact(a, b)), kind.Value);
                }

            case ("Max", 2, _):
                {
                    var (a, b) = (lowering.Emit(operands[0], offset), lowering.Emit(operands[1], offset));
                    return new WireValue(lowering.Macro(offset, construct, () => lowering.Circuit.MaximumExact(a, b)), kind.Value);
                }

            case ("Clamp", 3, _):
                {
                    var value = lowering.Emit(operands[0], offset);
                    var minimum = lowering.Emit(operands[1], offset);
                    var maximum = lowering.Emit(operands[2], offset);
                    if (!(lowering.Circuit.Ranges[minimum.Position].Maximum <= lowering.Circuit.Ranges[maximum.Position].Minimum))
                    {
                        throw lowering.Refuse(offset, construct,
                            "the declared range of the minimum can pass the maximum; .NET throws, and ClampExact returns the maximum");
                    }

                    if (lowering.Emitter.RecognizePatterns)
                    {
                        // Rule 2 of the phase 6 plan, task 1: a read at this value can give one index read
                        // with the clamp rule (IndexReads.cs). Another reader emits ClampExact.
                        return new ClampValue(kind.Value, value, minimum, maximum,
                            () => lowering.Macro(offset, construct, () => lowering.Circuit.ClampExact(value, minimum, maximum)));
                    }

                    return new WireValue(lowering.Macro(offset, construct, () => lowering.Circuit.ClampExact(value, minimum, maximum)), kind.Value);
                }

            case ("Floor", 1, ScalarKind.Float) when callee.DeclaringType == typeof(MathF) && lowering.Emitter.RecognizePatterns:
                return operands[0] is ScalarValue operand ? lowering.Floor(operand, offset) : null;

            case ("Abs", 1, ScalarKind.Float):
                {
                    var value = lowering.Emit(operands[0], offset);
                    return new WireValue(lowering.Macro(offset, construct, () => lowering.Circuit.Absolute(value)), ScalarKind.Float);
                }

            default:
                return null;
        }
    }

    /// <summary>The reason of the refusal of a call to a method that is not known.</summary>
    public static string RefusalReason(MethodInfo callee)
    {
        if (callee.Name == "FusedMultiplyAdd" && (callee.DeclaringType == typeof(Math) || callee.DeclaringType == typeof(MathF)))
        {
            return "a fused multiply-add rounds one time, and the program rounds the product and the sum one at a time";
        }

        if (callee.DeclaringType == typeof(Math) || callee.DeclaringType == typeof(MathF))
        {
            return "the lowering table has no exact construction for this method";
        }

        return "the program calls only [Handwired] methods, property getters, the Hw methods and the known methods of the lowering table";
    }

    private static Value LowerHw(OperationLowering lowering, MethodInfo callee, ImmutableArray<Value> operands, int offset)
    {
        var construct = OperationLowering.CallText(callee);
        var circuit = lowering.Circuit;
        switch (callee.Name)
        {
            case nameof(Hw.Relu):
                {
                    var value = lowering.Emit(operands[0], offset);
                    return new WireValue(circuit.Relu(value), ScalarKind.Float);
                }

            case nameof(Hw.Boolean):
                {
                    var score = lowering.Emit(operands[0], offset);
                    return new WireValue(lowering.Macro(offset, construct, () => circuit.Boolean(score)), ScalarKind.Bit);
                }

            case nameof(Hw.ApplySpringMatrix):
                {
                    var matrix = Matrix(lowering, operands[0], construct, offset);
                    var displacement = lowering.Emit(operands[1], offset);
                    var velocity = lowering.Emit(operands[2], offset);
                    var state = lowering.Macro(offset, construct, () => circuit.ApplySpringMatrix(matrix, displacement, velocity));
                    return Result(typeof(SpringValue), new()
                    {
                        [nameof(SpringValue.Displacement)] = new WireValue(state.Displacement, ScalarKind.Float),
                        [nameof(SpringValue.Velocity)] = new WireValue(state.Velocity, ScalarKind.Float),
                    });
                }

            case nameof(Hw.FirstActive):
                {
                    var active = lowering.Emit(operands[0], offset);
                    var candidates = operands[1] as ArrayValue
                        ?? throw lowering.Refuse(offset, construct, "the candidates are a fixed-length bool array");
                    var wires = candidates.Elements.Select(candidate => lowering.Emit(candidate, offset)).ToArray();
                    var selected = lowering.Macro(offset, construct, () => circuit.FirstActive(active, wires));
                    return new ArrayValue(typeof(bool), [.. selected.Select(wire => (Value)new WireValue(wire, ScalarKind.Bit))], isLocal: true);
                }

            case nameof(Hw.AnimatedObject):
                return AnimatedObject(lowering, operands, construct, offset);
            case nameof(Hw.ReadOrZero):
                return lowering.ReadOrZero(operands, construct, offset);
            default:
                throw lowering.Refuse(offset, construct, "the translator has no macro for this Hw method");
        }
    }

    // Hw.AnimatedObject: Macros.AnimatedObject with one spring and the snap bit. An operand with no
    // finite declared range can overflow, and then the macro and the plain body give other values.
    private static AggregateValue AnimatedObject(OperationLowering lowering, ImmutableArray<Value> operands, string construct, int offset)
    {
        var circuit = lowering.Circuit;
        var matrix = Matrix(lowering, operands[0], construct, offset);
        var numbers = operands.Skip(1).Take(4).Select(operand => lowering.Emit(operand, offset)).ToArray();
        var settleCheck = lowering.Emit(operands[5], offset);
        var snap = lowering.Emit(operands[6], offset);
        foreach (var wire in matrix.ToArray().Concat(numbers))
        {
            var range = circuit.Ranges[wire.Position];
            if (!(double.IsFinite(range.Minimum) && double.IsFinite(range.Maximum)))
            {
                throw lowering.Refuse(offset, construct, "each float operand of Hw.AnimatedObject needs a finite declared range");
            }
        }

        var spring = new AnimatedSpring(matrix, numbers[0], numbers[1], numbers[2]) { DestinationDuringSteps = numbers[3] };
        var animated = lowering.Macro(offset, construct, () => circuit.AnimatedObject([spring], new AnimatedObjectOptions(settleCheck) { Snap = snap }));
        return Result(typeof(AnimatedValue), new()
        {
            [nameof(AnimatedValue.Position)] = new WireValue(animated.Positions[0], ScalarKind.Float),
            [nameof(AnimatedValue.Displacement)] = new WireValue(animated.Displacements[0], ScalarKind.Float),
            [nameof(AnimatedValue.Velocity)] = new WireValue(animated.Velocities[0], ScalarKind.Float),
            [nameof(AnimatedValue.Active)] = new WireValue(animated.Active, ScalarKind.Bit),
        });
    }

    private static SpringMatrixWires Matrix(OperationLowering lowering, Value operand, string construct, int offset)
    {
        if (operand is not ArrayValue { Elements.Length: 4 } matrix)
        {
            throw lowering.Refuse(offset, construct, "a spring matrix is a fixed-length float array of 4 entries");
        }

        var wires = matrix.Elements.Select(entry => lowering.Emit(entry, offset)).ToArray();
        return new SpringMatrixWires(wires[0], wires[1], wires[2], wires[3]);
    }

    // The result of an Hw method as an aggregate of its record struct, by the backing field of each property.
    private static AggregateValue Result(Type type, Dictionary<string, Value> byProperty) => new(
        type,
        byProperty.ToImmutableDictionary(
            entry => DataTypes.BackingField(type.GetProperty(entry.Key)!)?.Name
                ?? throw new InvalidOperationException($"the property {type.Name}.{entry.Key} has no backing field"),
            entry => entry.Value,
            StringComparer.Ordinal));
}
