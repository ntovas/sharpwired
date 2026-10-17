// No Handwired source. Handwired has no translator and no attributes. The tests check that each
// attribute keeps its arguments and that each attribute has the targets of the plan.

using System.Reflection;

namespace Sharpwired.Annotations.Tests;

public class AttributeTests
{
    private const AttributeTargets CellTargets = AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter;

    [Theory]
    [InlineData(typeof(HandwiredAttribute), AttributeTargets.Method)]
    [InlineData(typeof(MaxIterationsAttribute), AttributeTargets.Method)]
    [InlineData(typeof(RangeAttribute), CellTargets)]
    [InlineData(typeof(GridAttribute), CellTargets)]
    [InlineData(typeof(EventAttribute), CellTargets)]
    [InlineData(typeof(StateAttribute), CellTargets)]
    [InlineData(typeof(InitialAttribute), CellTargets)]
    [InlineData(typeof(FixedLengthAttribute), CellTargets)]
    [InlineData(typeof(OneHotAttribute), CellTargets)]
    public void Each_attribute_has_the_targets_of_the_plan_and_is_sealed(Type attribute, AttributeTargets targets)
    {
        var usage = attribute.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(usage);
        Assert.Equal(targets, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.True(attribute.IsSealed);
        Assert.Equal("Sharpwired.Annotations", attribute.Namespace);
    }

    private sealed record Sample(
        [property: Range(-16384, 16384), Grid(0.015625), Initial(1280)] float X,
        [property: Event] bool Pressed,
        [property: State] bool On,
        [property: FixedLength(4)] float[] Matrix,
        [property: OneHot(AllowAllZero = true)] bool[] Choice,
        [property: OneHot] bool[] Strict);

    [Handwired, MaxIterations(12)]
    private static float Halve([Range(0, 4096)] float x) => x * 0.5f;

    [Fact]
    public void Each_attribute_keeps_its_arguments()
    {
        var x = typeof(Sample).GetProperty(nameof(Sample.X))!;
        var range = x.GetCustomAttribute<RangeAttribute>()!;
        Assert.Equal(-16384, range.Minimum);
        Assert.Equal(16384, range.Maximum);
        Assert.Equal(0.015625, x.GetCustomAttribute<GridAttribute>()!.Step);
        Assert.Equal(1280, x.GetCustomAttribute<InitialAttribute>()!.Value);
        Assert.NotNull(typeof(Sample).GetProperty(nameof(Sample.Pressed))!.GetCustomAttribute<EventAttribute>());
        Assert.NotNull(typeof(Sample).GetProperty(nameof(Sample.On))!.GetCustomAttribute<StateAttribute>());
        Assert.Equal(4, typeof(Sample).GetProperty(nameof(Sample.Matrix))!.GetCustomAttribute<FixedLengthAttribute>()!.Length);
        Assert.True(typeof(Sample).GetProperty(nameof(Sample.Choice))!.GetCustomAttribute<OneHotAttribute>()!.AllowAllZero);
        Assert.False(typeof(Sample).GetProperty(nameof(Sample.Strict))!.GetCustomAttribute<OneHotAttribute>()!.AllowAllZero);

        var halve = typeof(AttributeTests).GetMethod(nameof(Halve), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.NotNull(halve.GetCustomAttribute<HandwiredAttribute>());
        Assert.Equal(12, halve.GetCustomAttribute<MaxIterationsAttribute>()!.Count);
        var parameterRange = halve.GetParameters()[0].GetCustomAttribute<RangeAttribute>()!;
        Assert.Equal(0, parameterRange.Minimum);
        Assert.Equal(4096, parameterRange.Maximum);
        // A plain method runs as a normal .NET method.
        Assert.Equal(3f, Halve(6f));
    }

    [Fact]
    public void The_annotations_assembly_references_only_the_framework()
    {
        var references = typeof(HandwiredAttribute).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        Assert.NotEmpty(references);
        Assert.All(references, name => Assert.StartsWith("System.", name));
    }
}
