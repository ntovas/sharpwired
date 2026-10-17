// No Handwired source. Handwired has no translator. The inlining of the plan, section "Decisions",
// question 3, "Inlining decision": the translator inlines each call to a non-generic [Handwired]
// method, static or an instance method of a struct or a sealed class. The value of the call emits
// at the call, so a helper result is one wire, as a builder local function returns one wire.
// Recursion is refused. A refusal in the callee gives the location in the callee, and each call site
// follows, innermost first (section "Error message rules", rule 4). Property getters and data
// constructors are not inlined: OperationLowering reads their backing fields.

using System.Collections.Immutable;
using System.Reflection;

namespace Sharpwired.IL;

/// <summary>The inlining of the calls to [Handwired] methods.</summary>
internal static class Inliner
{
    /// <summary>Lowers the body of the callee with the operands as its arguments, and emits its return value at the call.</summary>
    public static Value Inline(OperationLowering caller, MethodInfo callee, ImmutableArray<Value> operands, int offset)
    {
        if (callee.IsGenericMethod || callee.ContainsGenericParameters)
        {
            throw caller.Refuse(offset, OperationLowering.CallText(callee), "a [Handwired] method must not be generic");
        }

        if (caller.CallStack.Any(method => method.HasSameMetadataDefinitionAs(callee)))
        {
            throw caller.Refuse(offset, "recursion", "the program has a fixed size, so a [Handwired] method must not call itself");
        }

        try
        {
            var decoded = IlDecoder.Decode(callee);
            var ssa = SsaBuilder.Build(ControlFlowGraph.Build(decoded));
            var arguments = operands.Select(Pass).ToArray();
            var (result, _) = OperationLowering.Lower(decoded, ssa, caller.Emitter, arguments, caller.CallStack.Add(callee));
            EmitLeaves(caller.Emitter, result);
            return result;
        }
        catch (TranslationException error)
        {
            throw error.CalledFrom(caller.Locate(offset));
        }
    }

    // An array argument: the callee reads it, and a store into it is refused, because the lowering
    // changes an array value in place, and the call can be under a branch of the caller.
    private static Value Pass(Value value) =>
        value is ArrayValue { IsLocal: true } array ? new ArrayValue(array.ElementType, [.. array.Elements], isLocal: false) : value;

    // Each number or bit leaf of the return value emits at the call. A constant needs no operation.
    private static void EmitLeaves(Emitter emitter, Value value)
    {
        switch (value)
        {
            case ConstantValue:
                return;
            case ScalarValue scalar:
                emitter.Emit(scalar);
                return;
            case AggregateValue aggregate:
                foreach (var field in DataTypes.InstanceFields(aggregate.Type).Where(field => aggregate.Fields.ContainsKey(field.Name)))
                {
                    EmitLeaves(emitter, aggregate.Fields[field.Name]);
                }

                return;
            case ArrayValue array:
                foreach (var element in array.Elements)
                {
                    EmitLeaves(emitter, element);
                }

                return;
        }
    }
}
