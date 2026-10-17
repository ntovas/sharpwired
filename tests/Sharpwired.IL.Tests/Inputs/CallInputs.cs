// No Handwired source. C# inputs for the inlining tests of the translator (plan, task 4, and section
// "Decisions", question 3, "Inlining decision"): calls of [Handwired] methods, an instance method of
// a struct, and the calls that the translator refuses. Each refused construct is on the line of its
// marker comment ("marker: <name>").

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

/// <summary>A struct with a [Handwired] instance method that reads this.</summary>
public readonly record struct Span([Range(-100, 100)] float Low, [Range(-100, 100)] float High)
{
    [Handwired]
    public float Width() => High - Low;
}

/// <summary>A struct with a [Handwired] instance method that writes to this.</summary>
public struct Tally
{
    private float total;

    public Tally(float start) => total = start;

    [Handwired]
    public float Add(float x)
    {
        total = total + x; // marker: store-this
        return total;
    }
}

public static class CallInputs
{
    // Two ret instructions in Release: the AND of 4 comparisons.
    [Handwired]
    public static bool InsideBox([Range(-100, 100)] float x, [Range(-100, 100)] float y) => x >= 0f && x < 10f && y >= 0f && y < 10f;

    [Handwired]
    public static bool ArmedInside(bool armed, [Range(-100, 100)] float x, [Range(-100, 100)] float y) => armed && InsideBox(x, y);

    [Handwired]
    private static float Half(float x) => x * 0.5f;

    // The helper result is one wire. The builder would compose that wire into the sum, so the sum is
    // a RoundAffine.
    [Handwired]
    public static float HalfPlusOne(float x) => Half(x) + 1f;

    [Handwired]
    public static float SpanWidth(Span span) => span.Width();

    // The refusals.

    [Handwired]
    public static float TallyTwice(float x)
    {
        var tally = new Tally(x);
        return tally.Add(x); // marker: call-tally
    }

    [Handwired]
    public static float Ping(float x) => Pong(x) + 1f; // marker: ping

    [Handwired]
    public static float Pong(float x) => Ping(x) * 0.5f; // marker: pong

    public static float Plain(float x) => x * 2f;

    [Handwired]
    public static float CallsPlain(float x) => Plain(x) + 1f; // marker: plain-call

    [Handwired]
    public static float Lambda(float x)
    {
        Func<float, float> twice = v => v * 2f; // marker: lambda
        return twice(x);
    }
}
