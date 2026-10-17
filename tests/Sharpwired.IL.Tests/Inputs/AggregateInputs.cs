// No Handwired source. C# inputs for the cell layout tests and the aggregate tests of the translator
// (plan, task 3). The Layout types have the cells of the toggle in the plan, section "Files": the
// names, policies, ranges, grids and initial values. Some attributes are on the primary
// constructor parameter and some use the property target, so the tests cover both places.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public readonly record struct LayoutThumb(
    [property: Range(-40, 40)] float Displacement,
    [Range(-1280, 1280)] float Velocity,
    [Range(0, 20)] float Destination);

public sealed record LayoutState(bool On, bool Armed, LayoutThumb Thumb);

public readonly record struct LayoutPointer(
    [property: Event] bool Active,
    [Range(-16384, 16384), Grid(1.0 / 64)] float X,
    [Range(-16384, 16384), Grid(1.0 / 64)] float Y);

public sealed record LayoutInput(
    [Range(0, 16384), Grid(1), Initial(1280)] float ViewportWidth,
    [Range(0, 16384), Grid(1), Initial(800)] float ViewportHeight,
    LayoutPointer Down,
    LayoutPointer Up,
    [Event] bool Cancel,
    [FixedLength(4), Range(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound)] float[] SpringMatrix,
    bool SettleCheck,
    bool Snap);

public sealed record LayoutOutput(float TrackLeft, float TrackTop, float ThumbOffset, bool On, bool Armed, bool Active);

/// <summary>A record class with two floats, for the callvirt getter and the newobj of a record.</summary>
public sealed record Rect([Range(-1000, 1000)] float Left, [Range(0, 1000)] float Width);

/// <summary>A struct with an internal field behind a public property, for stfld into a struct argument.</summary>
public struct Accumulator
{
    [Range(-100, 100)]
    [Grid(0.25)]
    internal float total;

    public readonly float Total => total;
}

/// <summary>A struct whose constructor stores its one parameter into two fields (review of task 3).</summary>
public readonly struct ProbePair
{
    public ProbePair(float a)
    {
        A = a;
        B = a;
    }

    public float A { get; }

    public float B { get; }
}

/// <summary>A record struct with a [State] property, which TranslateFunction refuses.</summary>
public readonly record struct WithStateProperty([property: State, Range(0, 1)] float Level);

public static class AggregateInputs
{
    // SpringMatrix.SpringMatrixBound(new SpringConstants(650, 45)), plan, section "Files".
    public const double MatrixBound = 10.360516548156738;

    private static readonly float Scale = 0.75f;

    private static readonly float[] Table = [1f, 2f, 4f, 8f];

    // The toggle cells in straight-line code. The next state swaps On and Armed and halves the thumb
    // values, so each state cell has a next value that differs from the cell.
    [Handwired]
    public static (LayoutState Next, LayoutOutput Output) LayoutStep(LayoutState state, LayoutInput input) =>
        (new LayoutState(state.Armed, state.On,
                new LayoutThumb(state.Thumb.Displacement * 0.5f, state.Thumb.Velocity * 0.5f, state.Thumb.Destination * 0.5f)),
            new LayoutOutput(input.ViewportWidth * 0.5f - 26f, input.ViewportHeight * 0.5f - 16f,
            state.Thumb.Destination + state.Thumb.Displacement, state.On, state.Armed, input.SettleCheck));

    [Handwired]
    public static float OneHotInput([OneHot(AllowAllZero = true), FixedLength(3)] bool[] tabs, [Range(0, 1)] float x) => x;

    [Handwired]
    public static float StateInFunction(WithStateProperty value) => value.Level;

    [Handwired]
    public static float PointerSum(LayoutPointer point) => point.X + point.Y;

    [Handwired]
    public static float RectRight(Rect rect) => rect.Left + rect.Width;

    [Handwired]
    public static float AddToArgument(Accumulator accumulator, [Range(0, 10), Grid(1)] float x)
    {
        accumulator.total = accumulator.total + x;
        return accumulator.Total;
    }

    [Handwired]
    public static (Rect Shape, float Area) MakeRect([Range(-1000, 1000)] float left, [Range(0, 1000)] float width) =>
        (new Rect(left, width * 0.5f), left);

    [Handwired]
    public static float ZeroLocal([Range(-100, 100), Grid(1)] float x)
    {
        MutablePair pair = default;
        pair.A = x * 0.5f;
        return pair.A + pair.B + 1f;
    }

    [Handwired]
    public static float ConstantIndex([FixedLength(3), Range(-10, 10)] float[] w) => w[0] + w[2];

    [Handwired]
    public static float ComputedIndex([FixedLength(4), Range(-10, 10)] float[] w, [Range(0, 3)] int i) => w[i];

    [Handwired]
    public static int IntLookup([FixedLength(3), Range(-100, 100)] int[] t, [Range(0, 2)] int i) => t[i] + 1;

    [Handwired]
    public static float PlusLength([FixedLength(5), Range(-10, 10)] float[] w) => w[0] + w.Length;

    [Handwired]
    public static float PickBuilt([Range(-10, 10)] float a, [Range(-10, 10)] float b, [Range(0, 1)] int i)
    {
        var table = new float[2];
        table[0] = a;
        table[1] = b * 2f;
        return table[i];
    }

    [Handwired]
    public static float[] BuildArray([Range(-10, 10)] float a, [Range(-10, 10)] float b) => [a * 2f, b];

    [Handwired]
    public static float StaticLookup([Range(0, 3)] int i, [Range(-10, 10)] float x) => Table[i] + x * Scale;

    [Handwired]
    public static (float, float) BothFields([Range(-10, 10)] float x)
    {
        var pair = new ProbePair(x);
        return (pair.A, pair.B);
    }

    [Handwired]
    public static float FromInt([Range(-100, 100)] int i) => i * 0.5f;
}
