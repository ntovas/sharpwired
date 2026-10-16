// Handwired source: no Handwired case. These tests check the C# shape of the public types: the
// rules that TypeScript types give Handwired, for example that a number is not a Wire.

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

    // Only Sharpwired.Core can derive from MacroRecord, so the switch over the macro records in
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
}
