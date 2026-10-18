// No Handwired source. Handwired has no translator. These attributes mark a plain method and
// declare the cells of its parameters, for the IL translator of phase 4 (spec R4.2).

namespace Sharpwired.Annotations;

/// <summary>
/// Marks a static method that the IL translator can translate into a program. The method also runs
/// as a normal .NET method, and it is the reference of its translated program.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HandwiredAttribute : Attribute;

/// <summary>The declared range of a float or int leaf: the cell accepts a value from Minimum to Maximum.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RangeAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;

    public double Maximum { get; } = maximum;
}

/// <summary>The grid of a float leaf: each value is a multiple of Step, a power of two.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GridAttribute(double step) : Attribute
{
    public double Step { get; } = step;
}

/// <summary>Marks a bool leaf as an event cell: the host writes 1 for one evaluation, and the next value is 0.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EventAttribute : Attribute;

/// <summary>Marks a leaf, or each leaf of a property or parameter, as a state cell.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StateAttribute : Attribute;

/// <summary>The initial value of a cell.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class InitialAttribute(double value) : Attribute
{
    public double Value { get; } = value;
}

/// <summary>The length of an array leaf. An array type has no length, so each array leaf needs this attribute.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class FixedLengthAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

/// <summary>
/// Marks a bool array as a one-hot group: exactly one member is 1. With AllowAllZero, all members
/// can also be 0.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class OneHotAttribute : Attribute
{
    public bool AllowAllZero { get; init; }
}

/// <summary>The maximum number of iterations of a loop with a runtime count in the method.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MaxIterationsAttribute(int count) : Attribute
{
    public int Count { get; } = count;
}

/// <summary>
/// Marks a static [Handwired] method of two parameters as an associative combine: Combine(Combine(a,
/// b), c) gives the same value as Combine(a, Combine(b, c)) for each value in the declared ranges. The
/// translator then lowers a running total of the combine in a loop to a prefix network. The prefix
/// network groups the combines in another order (plan, task 6b). The translator does not check the
/// claim.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AssociativeAttribute : Attribute;
