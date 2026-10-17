// No Handwired source. C# inputs for the loop tests of the translator (plan, task 5, and section
// "Decisions", question 5): loop form 1 (a constant trip count), loop form 2 (a runtime trip count
// with a maximum from [Range] or [MaxIterations]), and the loops that the translator refuses. Each
// refused loop is on the line of its marker comment ("marker: <name>").

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class LoopInputs
{
    // Form 1.

    [Handwired]
    public static float WeightedSum([Range(-100, 100)] float x, [FixedLength(4), Range(-100, 100)] float[] w)
    {
        float s = 0f;
        for (int i = 0; i < 4; i++) s += x * w[i];
        return s;
    }

    [Handwired]
    public static float RepeatAdd(float x)
    {
        float s = 0f;
        for (int i = 0; i < 4; i++) s += x;
        return s;
    }

    [Handwired]
    public static float RepeatAddWhile(float x)
    {
        float s = 0f;
        int i = 0;
        while (i < 4)
        {
            s += x;
            i++;
        }

        return s;
    }

    // The condition is at the bottom of the body, so the body runs before the first test.
    [Handwired]
    public static float RepeatAddDoWhile(float x)
    {
        float s = 0f;
        int i = 0;
        do
        {
            s += x;
            i++;
        }
        while (i < 4);
        return s;
    }

    [Handwired]
    public static float Nested([Range(-100, 100)] float x, [FixedLength(6), Range(-100, 100)] float[] w)
    {
        float s = 0f;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                s += x * w[i * 2 + j];
            }
        }

        return s;
    }

    [Handwired]
    public static float StepTwo([Range(-100, 100)] float x, [FixedLength(8), Range(-100, 100)] float[] w)
    {
        float s = 0f;
        for (int i = 0; i < 8; i += 2) s += x * w[i];
        return s;
    }

    [Handwired]
    public static float OverLength([FixedLength(5), Range(-100, 100)] float[] w)
    {
        float s = 0f;
        for (int i = 0; i < w.Length; i++) s += w[i];
        return s;
    }

    // A loop inside a branch, and a value after the loop.
    [Handwired]
    public static float LoopUnderBranch(bool c, [Range(-100, 100)] float x)
    {
        float s = 1f;
        if (c)
        {
            for (int i = 0; i < 3; i++) s = s * 0.5f + x;
        }

        return s - x;
    }

    // Form 2.

    [Handwired]
    public static float CountedSum([Range(0, 8)] int n, float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x;
        return s;
    }

    [Handwired]
    public static float CountedWeights([Range(0, 4)] int n, [Range(-100, 100)] float x, [FixedLength(4), Range(-100, 100)] float[] w)
    {
        float s = x;
        for (int i = 0; i < n; i++) s = s * 0.5f + w[i];
        return s;
    }

    [Handwired, MaxIterations(12)]
    public static float Halve([Range(0, 4096)] float x)
    {
        while (x > 1f) x *= 0.5f;
        return x;
    }

    [Handwired, MaxIterations(12)]
    public static float HalveWide([Range(0, 1048576)] float x)
    {
        while (x > 1f) x *= 0.5f; // marker: halve-wide
        return x;
    }

    // A loop with [MaxIterations] in a [Handwired] method that the caller runs only when c is true.
    [Handwired]
    public static float HalveWhen(bool c, [Range(0, 100)] float x) => c ? HalveTwice(x) : x;

    [Handwired, MaxIterations(2)]
    private static float HalveTwice(float x)
    {
        while (x > 1f) x *= 0.5f; // marker: halve-twice
        return x;
    }

    // An array store in each copy of a constant loop.
    [Handwired]
    public static float StoreInConstantLoop([Range(-100, 100)] float x)
    {
        var a = new float[3];
        for (int i = 0; i < 3; i++) a[i] = x * (i + 1);
        return a[0] + a[2];
    }

    // Refused loops.

    [Handwired]
    public static float StoreInCountedLoop([Range(0, 3)] int n, [Range(-100, 100)] float x)
    {
        var a = new float[3];
        for (int i = 0; i < n; i++) a[i] = x; // marker: store-counted
        return a[0];
    }

    [Handwired, MaxIterations(12)]
    public static (HalveState Next, HalveOutput Output) HalveStep(HalveState state, HalveInput input)
    {
        float x = state.Level;
        while (x > 1f) x *= 0.5f; // marker: halve-step
        return (new HalveState(x), new HalveOutput(x + input.Offset));
    }

    [Handwired]
    public static float NoExit(float x)
    {
        while (true) x *= 0.5f; // marker: no-exit
    }

    [Handwired]
    public static float TooMany(float x)
    {
        float s = 0f;
        for (int i = 0; i < 70000; i++) s += x; // marker: too-many
        return s;
    }

    [Handwired]
    public static float NoMaximum([Range(0, 1000000)] float x)
    {
        while (x > 1f) x *= 0.5f; // marker: no-maximum
        return x;
    }

    [Handwired]
    public static float Break([Range(0, 8)] int n, [Range(-100, 100)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++)
        {
            if (s > 50f) break; // marker: break
            s += x;
        }

        return s;
    }
}

public readonly record struct HalveState([Range(0, 4096)] float Level);

public readonly record struct HalveInput([Range(0, 1)] float Offset);

public readonly record struct HalveOutput(float Level);
