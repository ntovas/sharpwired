// Handwired source: src/runtime/cells.ts (WireStruct, pairLeaves, collectLeaves).
// Handwired walks plain JavaScript objects. Here the walker reads a C# value by reflection, at
// build time only: SourceTable.Next and ProgramEvaluator.Create call it, and no evaluation does.

using System.Collections;
using System.Globalization;
using System.Reflection;
using Sharpwired.Core;

namespace Sharpwired.Cells;

/// <summary>
/// The shape of a value of wires, lists and objects, for example
/// <c>new { ThumbOffset = wire, Switches = new[] { new { On = wire } } }</c>:
/// <list type="bullet">
/// <item>A <see cref="Wire"/> is one leaf.</item>
/// <item>A value whose type implements <see cref="IReadOnlyList{T}"/> is a list. The path of element i is path[i].</item>
/// <item>
/// Another class or struct is an object. Its public instance properties are its fields. The name of a
/// field is the property name with its first letter in lower case, so ThumbOffset gives thumbOffset.
/// </item>
/// <item>Null, a number, an enum value and a string are refused.</item>
/// </list>
/// </summary>
internal static class WireStruct
{
    private enum Shape
    {
        Wire,
        List,
        Object,
        Other,
    }

    /// <summary>
    /// Handwired pairLeaves: walk two values at the same time by field name and index, and collect
    /// the leaf pairs. The fields go in ordinal order of their names, as Handwired sorts them.
    /// </summary>
    public static void PairLeaves(object? stored, object? next, string path, List<(Wire Stored, Wire Next)> pairs)
    {
        var at = path.Length == 0 ? "" : $" at {path}";
        switch (ShapeOf(stored))
        {
            case Shape.Wire:
                if (next is not Wire nextWire) throw new ArgumentException($"next state{at} must be a wire");
                pairs.Add(((Wire)stored!, nextWire));
                return;
            case Shape.List:
            {
                var storedElements = Elements(stored!);
                if (ShapeOf(next) != Shape.List || Elements(next!) is not { } nextElements || nextElements.Length != storedElements.Length)
                {
                    throw new ArgumentException($"next state{at} must be a list of {storedElements.Length.ToString(CultureInfo.InvariantCulture)}");
                }
                for (var index = 0; index < storedElements.Length; index++)
                {
                    PairLeaves(storedElements[index], nextElements[index], ElementPath(path, index), pairs);
                }
                return;
            }
            case Shape.Object:
            {
                if (ShapeOf(next) != Shape.Object) throw new ArgumentException($"next state{at} must be an object");
                var storedFields = Fields(stored!.GetType()).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
                var nextFields = Fields(next!.GetType()).OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
                if (!storedFields.Select(field => field.Name).SequenceEqual(nextFields.Select(field => field.Name), StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        $"next state{at} has fields {string.Join(", ", nextFields.Select(field => field.Name))} "
                        + $"but the stored struct has {string.Join(", ", storedFields.Select(field => field.Name))}");
                }
                for (var index = 0; index < storedFields.Length; index++)
                {
                    PairLeaves(
                        storedFields[index].Property.GetValue(stored),
                        nextFields[index].Property.GetValue(next),
                        FieldPath(path, storedFields[index].Name),
                        pairs);
                }
                return;
            }
            default:
                throw new ArgumentException("stored state holds wires, lists and objects");
        }
    }

    /// <summary>
    /// Handwired collectLeaves: each wire of a value, with its path, for example ("thumb.offset", 27).
    /// The fields go in declaration order, as Object.entries gives them in Handwired.
    /// </summary>
    public static List<(string Path, Wire Wire)> CollectLeaves(object? value, string path, List<(string Path, Wire Wire)> leaves)
    {
        switch (ShapeOf(value))
        {
            case Shape.Wire:
                leaves.Add((path, (Wire)value!));
                break;
            case Shape.List:
            {
                var elements = Elements(value!);
                for (var index = 0; index < elements.Length; index++) CollectLeaves(elements[index], ElementPath(path, index), leaves);
                break;
            }
            case Shape.Object:
                foreach (var field in Fields(value!.GetType())) CollectLeaves(field.Property.GetValue(value), FieldPath(path, field.Name), leaves);
                break;
            default:
                throw new ArgumentException("an output holds wires, lists and objects");
        }
        return leaves;
    }

    private static string ElementPath(string path, int index) => $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static string FieldPath(string path, string field) => path.Length == 0 ? field : $"{path}.{field}";

    private static Shape ShapeOf(object? value)
    {
        if (value is null) return Shape.Other;
        if (value is Wire) return Shape.Wire;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)) return Shape.Other;
        if (value is IEnumerable && IsReadOnlyList(type)) return Shape.List;
        return Shape.Object;
    }

    private static bool IsReadOnlyList(Type type) =>
        type.GetInterfaces().Any(face => face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));

    private static object?[] Elements(object list) => [.. ((IEnumerable)list).Cast<object?>()];

    // The public instance properties of a type, in declaration order (metadata token order), with
    // the first letter of each name in lower case.
    private static (string Name, PropertyInfo Property)[] Fields(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
            .OrderBy(property => property.MetadataToken)
            .Select(property => (FieldName(property.Name), property)),
    ];

    private static string FieldName(string propertyName) =>
        propertyName.Length == 0 ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
