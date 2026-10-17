// Handwired source: no Handwired case. These tests check the C# shape of the public types. They
// check the rules that TypeScript types give Handwired, for example that a number is not a Wire.

using System.Collections.Immutable;
using System.Reflection;

namespace Sharpwired.Core.Tests;

public class ApiShapeTests
{
    // R1.1: a plain int does not convert to a Wire, and only Wire.FromPosition makes a wire from
    // a number.
    [Fact]
    public void Wire_has_no_conversion_from_int_and_no_public_constructor()
    {
        var conversions = typeof(Wire)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit")
            .Where(method => method.GetParameters().Single().ParameterType == typeof(int));
        Assert.Empty(conversions);
        Assert.Empty(typeof(Wire).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    // Only Sharpwired.Core can derive from MacroRecord. So the switch over the macro records in
    // DescribeFirstNaN and the backward pass covers every record that a caller can make.
    [Fact]
    public void MacroRecord_has_no_public_or_protected_constructor()
    {
        var constructors = typeof(MacroRecord).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotEmpty(constructors);
        Assert.All(constructors, constructor => Assert.False(
            constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly,
            $"MacroRecord has the accessible constructor {constructor}"));
    }

    // A caller cannot cast a list property back to the list that the circuit changes.
    [Fact]
    public void The_list_properties_of_a_circuit_are_read_only_views()
    {
        var circuit = new Circuit(2);
        circuit.AssumeOneHot([circuit.Source(0), circuit.Source(1)]);
        circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)], 1);
        Assert.Null(circuit.Operations as List<Operation>);
        Assert.Null(circuit.Depths as List<int>);
        Assert.Null(circuit.Constants as List<double?>);
        Assert.Null(circuit.Ranges as List<NumericRange>);
        Assert.Null(circuit.Grids as List<double?>);
        Assert.Null(circuit.ExactBits as List<bool>);
        Assert.Null(circuit.HasReaders as List<bool>);
        Assert.Null(circuit.OneHotGroups as List<OneHotGroup>);
        Assert.Throws<NotSupportedException>(() => ((IList<Operation>)circuit.Operations).Clear());
        // The view follows the circuit.
        Assert.Single(circuit.Operations);
        circuit.Affine([(circuit.Source(0), 2), (circuit.Source(1), 1)], 1);
        Assert.Equal(2, circuit.Operations.Count);
    }

    // A caller cannot change a compiled program through a property of a program type. Each list is
    // an ImmutableArray<T> or an UnsignedIndices, and no program type has a public field.
    [Fact]
    public void The_program_types_expose_no_writable_array()
    {
        Type[] programTypes = [typeof(TensorGatherCompute), typeof(TensorGatherRead), typeof(TensorIndexRead), typeof(TensorLayer), typeof(TensorProgram)];
        Type[] allowedTypes =
        [
            typeof(int),
            typeof(bool),
            typeof(UnsignedIndices),
            typeof(ImmutableArray<float>),
            typeof(ImmutableArray<TensorGatherCompute>),
            typeof(ImmutableArray<TensorGatherRead>),
            typeof(ImmutableArray<TensorIndexRead>),
            typeof(ImmutableArray<TensorLayer>),
        ];
        foreach (var type in programTypes)
        {
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name != "EqualityContract");
            Assert.NotEmpty(properties);
            Assert.All(properties, property => Assert.True(
                allowedTypes.Contains(property.PropertyType),
                $"{type.Name}.{property.Name} has the type {property.PropertyType}"));
        }
    }

    // UnsignedIndices is read-only: no public setter, no public field, and no public member that
    // returns an array, a span or a memory over its storage.
    [Fact]
    public void UnsignedIndices_has_no_public_setter()
    {
        var type = typeof(UnsignedIndices);
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static),
            property => Assert.False(property.SetMethod?.IsPublic ?? false, $"UnsignedIndices.{property.Name} has a public setter"));
        Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.All(methods, method =>
        {
            var returnType = method.ReturnType;
            var exposesStorage = returnType.IsArray || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() is var definition
                && (definition == typeof(Span<>) || definition == typeof(Memory<>)));
            Assert.False(exposesStorage, $"UnsignedIndices.{method.Name} returns {returnType}");
        });
    }
}
