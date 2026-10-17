// No Handwired source. Handwired has no translator. The reflection runner of a plain method on a
// source table (plan, section "Files", PlainInvoker.cs): it builds each parameter from the cells of
// its walk, calls the plain method, and reads the output leaves of the return value in walk order.
// The tests compare a translated program with this runner.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Sharpwired.IL;

/// <summary>Runs a plain method on the leaves of a source table.</summary>
internal static class PlainInvoker
{
    /// <summary>Calls the plain method with the parameters of the source table and returns its return value.</summary>
    public static object? Invoke(MethodInfo method, CellLayout layout, ReadOnlySpan<float> source)
    {
        var arguments = new object?[layout.Parameters.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = Build(layout.Parameters[index], source);
        }

        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(error.InnerException);
            throw;
        }
    }

    /// <summary>Appends the output leaves of a value in the order of the shape.</summary>
    public static void Read(OutputShape shape, object? value, List<float> leaves)
    {
        switch (shape)
        {
            case ScalarShape scalar:
                leaves.Add(scalar.Kind switch
                {
                    ScalarKind.Float => (float)value!,
                    ScalarKind.Int => (int)value!,
                    _ => (bool)value! ? 1f : 0f,
                });
                return;
            case MemberShape members:
                foreach (var (member, child) in members.Members)
                {
                    Read(child, member is FieldInfo field ? field.GetValue(value) : ((PropertyInfo)member).GetValue(value), leaves);
                }

                return;
            case ArrayShape array:
                var elements = (Array)value!;
                if (elements.Length != array.Elements.Length)
                {
                    throw new InvalidOperationException($"the plain method returned an array of {elements.Length} elements, and the program has {array.Elements.Length}");
                }

                for (var index = 0; index < elements.Length; index++)
                {
                    Read(array.Elements[index], elements.GetValue(index), leaves);
                }

                return;
            default:
                throw new InvalidOperationException($"unknown output shape {shape}");
        }
    }

    private static object? Build(LayoutNode node, ReadOnlySpan<float> source)
    {
        switch (node)
        {
            case LeafNode leaf:
                var value = source[leaf.Cell];
                return leaf.Kind switch
                {
                    ScalarKind.Float => value,
                    ScalarKind.Int => (int)value,
                    _ => value != 0,
                };
            case AggregateNode aggregate:
                // A boxed struct or an object with no constructor call; each backing field gets its cells.
                var instance = RuntimeHelpers.GetUninitializedObject(aggregate.Type);
                foreach (var (field, child) in aggregate.Members)
                {
                    field.SetValue(instance, Build(child, source));
                }

                return instance;
            case ArrayNode array:
                var elements = Array.CreateInstance(array.ElementType, array.Elements.Length);
                for (var index = 0; index < array.Elements.Length; index++)
                {
                    elements.SetValue(Build(array.Elements[index], source), index);
                }

                return elements;
            default:
                throw new InvalidOperationException($"unknown layout node {node}");
        }
    }
}
