// No Handwired source. C# inputs for the tests of loop form 3 (plan, task 6, and section
// "Decisions", question 5, the rows of form 3): a loop with no maximum that TranslateRecurrent
// splits into a prefix, a step program and a suffix, and the form 3 loops that the translator
// refuses. Each refused loop is on the line of its marker comment ("marker: <name>").

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class RecurrentInputs
{
    [Handwired]
    public static float Halve([Range(0, 1000000)] float x)
    {
        while (x > 1f) x *= 0.5f; // marker: halve
        return x;
    }

    [Handwired]
    public static int CountDown([Range(0, 100)] int n)
    {
        int count = 0;
        while (n > 0)
        {
            n -= 3;
            count++;
        }

        return count;
    }

    // The loop needs about 2000 steps for x = 1000000.
    [Handwired]
    public static float SlowDecay([Range(0, 1000000)] float x)
    {
        while (x > 0.001f) x *= 0.99f;
        return x;
    }

    // limit is a value of the code before the loop that the loop and the code after it read.
    [Handwired]
    public static (float Remainder, int Count) Invariant([Range(0, 1000)] float x, [Range(1, 4)] float scale)
    {
        float limit = scale * scale;
        int count = 0;
        while (x > limit)
        {
            x *= 0.5f;
            count++;
        }

        return (x - limit, count);
    }

    // The body runs one time before the first test.
    [Handwired]
    public static float HalveAtLeastOnce([Range(0, 1000000)] float x)
    {
        do
        {
            x *= 0.5f;
        }
        while (x > 1f);
        return x;
    }

    // Refused loops.

    [Handwired]
    public static float TwoLoops([Range(0, 1000000)] float x, [Range(0, 1000000)] float y)
    {
        while (x > 1f) x *= 0.5f;
        while (y > 1f) y *= 0.5f; // marker: second-loop
        return x + y;
    }

    [Handwired]
    public static float LoopInBranch(bool c, [Range(0, 1000000)] float x)
    {
        if (c)
        {
            while (x > 1f) x *= 0.5f; // marker: loop-in-branch
        }

        return x;
    }

    [Handwired]
    public static float LoopInLoop([Range(0, 4)] int n, [Range(0, 1000000)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++)
        {
            float y = x;
            while (y > 1f) y *= 0.5f; // marker: loop-in-loop
            s += y;
        }

        return s;
    }

    // A loop with no maximum whose state range has no fixed point in 8 rounds, and whose body passes
    // the FP32 range in the wide range.
    [Handwired]
    public static float Grow([Range(1, 2)] float x)
    {
        while (x < 1000000f) x *= 2f; // marker: grow
        return x;
    }

    // A method with no loop of form 3.
    [Handwired]
    public static float NoLoop(float x) => x * 2f;
}
