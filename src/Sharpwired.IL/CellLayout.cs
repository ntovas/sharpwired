// No Handwired source. Handwired has no translator. The cell walk of the plan, section "Decisions",
// question 2: the parameters in order, then the public instance properties of each type in
// MetadataToken order; a fixed-length array gives one cell for each element; a cell name is the path
// of property names with the first letter in lower case. The attributes come from the property, the
// backing field or the primary constructor parameter with the same name. The walk checks each
// attribute argument and refuses a type with public instance fields, a base type other than object
// or ValueType, an array without [FixedLength] and a leaf type other than float, int and bool. The
// output walk gives the output leaves of a return value in the same order.

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Sharpwired.Annotations;
using Sharpwired.Cells;
using Sharpwired.Core;

namespace Sharpwired.IL;

/// <summary>A node of the cell walk of a parameter.</summary>
internal abstract record LayoutNode;

/// <summary>One cell. Cell is its position in the source table.</summary>
internal sealed record LeafNode(string Path, ScalarKind Kind, CellSpec Spec, int Cell) : LayoutNode;

/// <summary>A struct or a record: one child for each property, with the backing field of the property.</summary>
internal sealed record AggregateNode(Type Type, ImmutableArray<(FieldInfo Field, LayoutNode Child)> Members) : LayoutNode;

/// <summary>A fixed-length array: the element type and one node for each element.</summary>
internal sealed record ArrayNode(Type ElementType, ImmutableArray<LayoutNode> Elements) : LayoutNode;

/// <summary>The role of a parameter in the walk.</summary>
internal enum ParameterRole
{
    /// <summary>A parameter of TranslateFunction: input and event cells.</summary>
    Function,

    /// <summary>The first parameter of TranslateStep: state cells.</summary>
    State,

    /// <summary>The second parameter of TranslateStep: input and event cells.</summary>
    StepInput,
}

/// <summary>The shape of a return value: the output leaves in walk order.</summary>
internal abstract record OutputShape;

internal sealed record ScalarShape(string Path, ScalarKind Kind) : OutputShape;

/// <summary>A tuple (fields) or a record (properties): one child for each member, in walk order.</summary>
internal sealed record MemberShape(ImmutableArray<(MemberInfo Member, OutputShape Child)> Members) : OutputShape;

internal sealed record ArrayShape(ImmutableArray<OutputShape> Elements) : OutputShape;

/// <summary>The cells of the parameters of a plain method, and their declaration in a source table.</summary>
internal sealed class CellLayout
{
    // FP32 holds each integer exactly up to 2^24.
    private const double IntLimit = 16_777_216;

    private readonly MethodInfo method;
    private readonly List<LeafNode> leaves = [];
    // The one-hot groups by the walk index of their first member.
    private readonly Dictionary<int, (string Name, OneHotSpec Group)> groups = [];
    private readonly HashSet<string> paths = new(StringComparer.Ordinal);

    private CellLayout(MethodInfo method, IReadOnlyList<ParameterRole> roles)
    {
        this.method = method;
        var parameters = method.GetParameters();
        Parameters = [.. parameters.Select((parameter, index) => WalkParameter(parameter, roles[index]))];
    }

    /// <summary>One node for each parameter, in parameter order.</summary>
    public ImmutableArray<LayoutNode> Parameters { get; }

    /// <summary>The cells in walk order.</summary>
    public IReadOnlyList<LeafNode> Leaves => leaves;

    /// <summary>Walks the parameters. Throws <see cref="TranslationException"/> with the location of the member.</summary>
    public static CellLayout Walk(MethodInfo method, IReadOnlyList<ParameterRole> roles) => new(method, roles);

    /// <summary>Declares the cells in walk order and creates the circuit. A one-hot array declares its group.</summary>
    public (SourceTable Table, Circuit Circuit) Declare()
    {
        var table = new SourceTable();
        for (var index = 0; index < leaves.Count; index++)
        {
            if (groups.TryGetValue(index, out var group))
            {
                table.Declare(group.Name, group.Group);
                index += group.Group.Members.Length - 1;
                continue;
            }

            table.Declare(leaves[index].Path, leaves[index].Spec);
        }

        return (table, table.CreateCircuit());
    }

    /// <summary>The value of a parameter: the cell wires in the shape of its type.</summary>
    public static Value ValueOf(LayoutNode node, Circuit circuit) => node switch
    {
        LeafNode leaf => new WireValue(circuit.Source(leaf.Cell), leaf.Kind),
        AggregateNode aggregate => new AggregateValue(
            aggregate.Type,
            aggregate.Members.ToImmutableDictionary(member => member.Field.Name, member => ValueOf(member.Child, circuit), StringComparer.Ordinal)),
        ArrayNode array => new ArrayValue(array.ElementType, [.. array.Elements.Select(element => ValueOf(element, circuit))], isLocal: false),
        _ => throw new InvalidOperationException($"unknown layout node {node}"),
    };

    /// <summary>The location of the first visible sequence point of a method, for a refusal of a member.</summary>
    public static SourceLocation MemberLocation(MethodBase member)
    {
        var locations = SourceLocations.Read(member);
        var first = locations.Points.FirstOrDefault(point => !point.IsHidden);
        return locations.Locate(locations.HasPdb ? first.Offset : 0);
    }

    /// <summary>The name of a property as a path part: the first letter in lower case.</summary>
    public static string FieldName(string propertyName) =>
        propertyName.Length == 0 ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

    /// <summary>
    /// The public instance properties of a data type in MetadataToken order, each with its backing
    /// field. Throws <see cref="TranslationException"/> at the location for a type that the walk refuses.
    /// </summary>
    public static (PropertyInfo Property, FieldInfo Field)[] DataProperties(Type type, SourceLocation location)
    {
        if (type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length != 0)
        {
            throw Refuse(location, $"a public field in {type.Name}", "a cell type holds its values in properties; make each public field a property");
        }

        if (type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
        {
            throw Refuse(location, $"a base type of {type.Name}",
                "a cell type derives only from object or ValueType, because the property order of a base type is not defined");
        }

        return
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
                .OrderBy(property => property.MetadataToken)
                .Select(property => (property, DataTypes.BackingField(property)
                    ?? throw Refuse(MemberLocation(property.GetMethod!), $"the computed property {type.Name}.{property.Name}",
                        "each property of a cell type returns its backing field"))),
        ];
    }

    /// <summary>
    /// The output shape of a return value of a type, and its leaves in walk order. A ValueTuple is
    /// accepted only at the top. The length of an array comes from the value.
    /// </summary>
    public static OutputShape Output(Type type, Value value, string path, bool top, SourceLocation location, List<(string Path, ScalarValue Value)> leaves)
    {
        if (DataTypes.KindOf(type) is { } kind)
        {
            var scalar = value as ScalarValue ?? throw new InvalidOperationException($"the output {path} holds no number");
            leaves.Add((path, scalar));
            return new ScalarShape(path, kind);
        }

        if (type.IsSZArray)
        {
            var array = value as ArrayValue ?? throw new InvalidOperationException($"the output {path} holds no array");
            var element = type.GetElementType()!;
            return new ArrayShape([.. array.Elements.Select((item, index) => Output(element, item, ElementPath(path, index), false, location, leaves))]);
        }

        var aggregate = value as AggregateValue ?? throw new InvalidOperationException($"the output {path} holds no aggregate");
        if (top && IsValueTuple(type))
        {
            var items = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.Name, StringComparer.Ordinal);
            return new MemberShape([.. items.Select(field => ((MemberInfo)field, Output(field.FieldType, aggregate.Fields[field.Name], Join(path, FieldName(field.Name)), false, location, leaves)))]);
        }

        if (IsValueTuple(type))
        {
            throw Refuse(location, $"a public field in {type.Name}", "a ValueTuple is an output only as the return type itself");
        }

        return new MemberShape([.. DataProperties(type, location).Select(member => ((MemberInfo)member.Property,
            Output(member.Property.PropertyType, aggregate.Fields[member.Field.Name], Join(path, FieldName(member.Property.Name)), false, location, leaves)))]);
    }

    public static bool IsValueTuple(Type type) =>
        type.IsValueType && type.IsGenericType && type.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true;

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string ElementPath(string path, int index) => $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]";

    private static string Text(double value) => Fp32.Format(value);

    private static TranslationException Refuse(SourceLocation location, string construct, string reason) => new(location, 0, construct, reason);

    private LayoutNode WalkParameter(ParameterInfo parameter, ParameterRole role)
    {
        var location = MemberLocation(method);
        var type = parameter.ParameterType;
        var name = FieldName(parameter.Name ?? "");
        var root = DataTypes.KindOf(type) is not null || type.IsArray ? name : "";
        return WalkType(type, root, [parameter], role == ParameterRole.State, role, location, element: false);
    }

    private LayoutNode WalkType(Type type, string path, ICustomAttributeProvider[] sources, bool state, ParameterRole role, SourceLocation location, bool element)
    {
        CheckAttributes(type, sources, location, element);
        if (Find<StateAttribute>(sources) is not null)
        {
            if (role == ParameterRole.Function)
            {
                throw Refuse(location, "a [State] leaf in a TranslateFunction parameter", "TranslateFunction declares no state cells; use TranslateStep");
            }

            if (role == ParameterRole.StepInput)
            {
                throw Refuse(location, "a [State] leaf in the input parameter", "the state cells of TranslateStep come from its first parameter");
            }

            state = true;
        }

        if (DataTypes.KindOf(type) is { } kind)
        {
            return Leaf(path, kind, sources, state, location);
        }

        if (type.IsArray)
        {
            return WalkArray(type, path, sources, state, role, location);
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string) || type.IsPointer || type.IsByRef)
        {
            throw Refuse(location, $"a cell of type {type.Name}", "a cell holds a float, an int or a bool");
        }

        if (IsValueTuple(type))
        {
            throw Refuse(location, $"a public field in {type.Name}", "a cell type holds its values in properties; use a record");
        }

        var members = ImmutableArray.CreateBuilder<(FieldInfo, LayoutNode)>();
        foreach (var (property, field) in DataProperties(type, location))
        {
            var parameter = type.GetConstructors().SelectMany(c => c.GetParameters())
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase) && p.ParameterType == property.PropertyType);
            ICustomAttributeProvider[] memberSources = parameter is null ? [property, field] : [property, field, parameter];
            var child = WalkType(property.PropertyType, Join(path, FieldName(property.Name)), memberSources, state, role, MemberLocation(property.GetMethod!), element: false);
            members.Add((field, child));
        }

        return new AggregateNode(type, members.ToImmutable());
    }

    private ArrayNode WalkArray(Type type, string path, ICustomAttributeProvider[] sources, bool state, ParameterRole role, SourceLocation location)
    {
        if (!type.IsSZArray)
        {
            throw Refuse(location, $"a cell of type {type.Name}", "a cell array has one dimension");
        }

        var length = Find<FixedLengthAttribute>(sources)?.Length
            ?? throw Refuse(location, "an array without [FixedLength]", "an array type has no length; add [FixedLength]");
        var element = type.GetElementType()!;
        if (DataTypes.KindOf(element) is null)
        {
            throw Refuse(location, $"an array of {element.Name}", "a cell array holds float, int or bool values");
        }

        var oneHot = Find<OneHotAttribute>(sources);
        var start = leaves.Count;
        var elements = Enumerable.Range(0, length).Select(index => WalkType(element, ElementPath(path, index), sources, state, role, location, element: true)).ToImmutableArray();
        if (oneHot is not null)
        {
            groups[start] = (path, Cell.OneHot([.. leaves.Skip(start).Select(leaf => leaf.Spec)], oneHot.AllowAllZero));
        }

        return new ArrayNode(element, elements);
    }

    private LeafNode Leaf(string path, ScalarKind kind, ICustomAttributeProvider[] sources, bool state, SourceLocation location)
    {
        if (!paths.Add(path))
        {
            throw Refuse(MemberLocation(method), $"two cells with the path {path}", "each cell needs its own path; rename a parameter or a property");
        }

        var range = Find<RangeAttribute>(sources);
        var grid = Find<GridAttribute>(sources)?.Step;
        var initialAttribute = Find<InitialAttribute>(sources);
        var initial = initialAttribute?.Value ?? 0;
        var isEvent = Find<EventAttribute>(sources) is not null;
        if (isEvent && (kind != ScalarKind.Bit || state))
        {
            throw Refuse(location, kind == ScalarKind.Bit ? "an [Event] state cell" : $"[Event] on a cell of type {kind}", "an event cell is a bool input");
        }

        CellSpec spec;
        switch (kind)
        {
            case ScalarKind.Bit:
                if (range is not null || grid is not null)
                {
                    throw Refuse(location, range is not null ? "[Range] on a bool cell" : "[Grid] on a bool cell", "a bool cell holds 0 or 1");
                }

                if (initial is not (0 or 1) || (isEvent && initial != 0))
                {
                    throw Refuse(location, $"[Initial({Text(initial)})]", isEvent ? "an event cell starts at 0" : "the initial value of a bool cell is 0 or 1");
                }

                spec = isEvent ? Cell.Event() : state ? Cell.StateBit(initial) : Cell.InputBit(initial);
                break;
            case ScalarKind.Int:
                if (range is null || !(range.Minimum >= -IntLimit && range.Maximum <= IntLimit))
                {
                    throw Refuse(location, range is null ? "an int cell without [Range]" : $"[Range({Text(range.Minimum)}, {Text(range.Maximum)})] on an int cell",
                        "an int cell needs a finite [Range] within -2^24..2^24");
                }

                if (grid is not null && grid != 1)
                {
                    throw Refuse(location, $"[Grid({Text(grid.Value)})] on an int cell", "an int cell has the grid 1");
                }

                spec = state ? Cell.State(new NumericRange(range.Minimum, range.Maximum), initial, 1) : Cell.Input(new NumericRange(range.Minimum, range.Maximum), initial, 1);
                break;
            default:
                var numbers = range is null ? Cell.AnyNumber : new NumericRange(range.Minimum, range.Maximum);
                spec = state ? Cell.State(numbers, initial, grid) : Cell.Input(numbers, initial, grid);
                break;
        }

        // With no [Initial], a cell starts at the value of its range and grid nearest to 0.
        if (initialAttribute is null && kind != ScalarKind.Bit)
        {
            initial = NearestToZero(spec.Range, spec.Grid);
            spec = spec with { Initial = initial };
        }

        if (!(initial >= spec.Range.Minimum && initial <= spec.Range.Maximum))
        {
            throw Refuse(location, $"[Initial({Text(initial)})]", $"the initial value must lie in the range {Text(spec.Range.Minimum)}..{Text(spec.Range.Maximum)}");
        }

        if (spec.Grid is { } step && Math.Floor(initial / step) != initial / step)
        {
            throw Refuse(location, $"[Initial({Text(initial)})]", $"the initial value must be a multiple of the grid {Text(step)}");
        }

        var leaf = new LeafNode(path, kind, spec, leaves.Count);
        leaves.Add(leaf);
        return leaf;
    }

    internal static double NearestToZero(NumericRange range, double? grid)
    {
        // The initial table holds FP32 values, so the value is the FP32 value inside the range.
        var step = grid ?? 0;
        if (range.Minimum > 0)
        {
            var low = (float)range.Minimum;
            return step != 0 ? Math.Ceiling(range.Minimum / step) * step : low < range.Minimum ? MathF.BitIncrement(low) : low;
        }

        if (range.Maximum < 0)
        {
            var high = (float)range.Maximum;
            return step != 0 ? Math.Floor(range.Maximum / step) * step : high > range.Maximum ? MathF.BitDecrement(high) : high;
        }

        return 0;
    }

    // The checks of the attribute arguments. The attribute constructors do not check them.
    private static void CheckAttributes(Type type, ICustomAttributeProvider[] sources, SourceLocation location, bool element)
    {
        if (Find<RangeAttribute>(sources) is { } range && !(range.Minimum <= range.Maximum))
        {
            throw Refuse(location, $"[Range({Text(range.Minimum)}, {Text(range.Maximum)})]", "the minimum of a range must not be greater than the maximum");
        }

        if (Find<GridAttribute>(sources) is { } grid && !Fp32.IsPowerOfTwoGrid(grid.Step))
        {
            throw Refuse(location, $"[Grid({Text(grid.Step)})]", "a grid must be a power of two from 2^-149 to 2^127");
        }

        if (Find<FixedLengthAttribute>(sources) is { } fixedLength)
        {
            if (fixedLength.Length < 1)
            {
                throw Refuse(location, $"[FixedLength({fixedLength.Length.ToString(CultureInfo.InvariantCulture)})]", "the length of an array must be at least 1");
            }

            if (!type.IsArray && !element)
            {
                throw Refuse(location, $"[FixedLength] on a cell of type {type.Name}", "[FixedLength] applies to an array");
            }
        }

        if (Find<OneHotAttribute>(sources) is not null && type != typeof(bool[]) && !(element && type == typeof(bool)))
        {
            throw Refuse(location, $"[OneHot] on a cell of type {type.Name}", "[OneHot] applies to a bool array");
        }
    }

    private static T? Find<T>(ICustomAttributeProvider[] sources)
        where T : Attribute =>
        sources.SelectMany(source => source.GetCustomAttributes(typeof(T), false)).OfType<T>().FirstOrDefault();
}
