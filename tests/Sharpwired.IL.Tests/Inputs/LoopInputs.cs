// No Handwired source. C# inputs for the loop tests of the translator (plan, task 5, and section
// "Decisions", question 5). They are loops of form 1 (a constant trip count) and form 2 (a runtime
// trip count with a maximum from [Range] or [MaxIterations]). Other inputs are refused loops. Each
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
    public static float CountedSum([Range(0, 8)] int n, [Range(-1000, 1000)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x;
        return s;
    }

    // The kept state of an inactive copy reads s + x. This sum can be Infinity when x has no range,
    // while C# does not compute it (review of task 5, round 2, finding A).
    [Handwired]
    public static float CountedSumOpen([Range(0, 8)] int n, float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x; // marker: counted-open
        return s;
    }

    // The inferred range of each kept state stays finite for x within 1E+35.
    [Handwired]
    public static float CountedSumWide([Range(0, 8)] int n, [Range(-1e35, 1e35)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x;
        return s;
    }

    // The header holds the first part of the loop test and no exit edge (review of task 5, round 2,
    // finding B). The last pass runs only the test blocks, so the unroll needs MaxIterations + 1 passes.
    [Handwired, MaxIterations(3)]
    public static float OrTestMaximum([Range(0, 3)] int n)
    {
        float s = 0f;
        int i = 0;
        while (i < 1 || i < n)
        {
            s += 1f;
            i++;
        }

        return s;
    }

    // In Debug, the && value is a phi after the header, so the header has no exit edge. The right
    // operand is a comparison: with a local or an argument, Roslyn writes the IL and instead.
    [Handwired, MaxIterations(3)]
    public static float LessOrEqualAnd([Range(0, 3)] int n, [Range(0, 100)] float x)
    {
        float s = 0f;
        int i = 1;
        while (i <= n && x > s)
        {
            s += 1f;
            i++;
        }

        return s;
    }

    // s and t are constants in each copy. 0.5 * 3^82 passes float.MaxValue, so the fold gives
    // Infinity (review of task 5, round 2, finding C).
    [Handwired]
    public static float FoldedInfinity()
    {
        float s = 0.5f, t = 0f;
        for (int i = 0; i != 150; i++)
        {
            for (int j = 0; j < 2; j++) s = s * 3f; // marker: folded-infinity
            t += s;
        }

        return s + t;
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

    // A state that grows in each copy (review of task 5, finding 1). With n up to 200 the range of
    // the state passes FP32. With n up to 60 the range stays finite. For the product, n goes up to 30,
    // because the inferred range of a product of two signed values grows faster than its value.
    [Handwired]
    public static float Doubling([Range(0, 200)] int n, [Range(-1, 1)] float s)
    {
        for (int i = 0; i < n; i++) s = s * 2f + 1f; // marker: doubling
        return s;
    }

    [Handwired]
    public static float SelfSum([Range(0, 200)] int n, [Range(-1, 1)] float s)
    {
        for (int i = 0; i < n; i++) s += s; // marker: self-sum
        return s;
    }

    [Handwired]
    public static float Power([Range(0, 200)] int n, [Range(-2, 2)] float x)
    {
        float s = 1f;
        for (int i = 0; i < n; i++) s = s * x; // marker: power
        return s;
    }

    [Handwired]
    public static float DoublingShort([Range(0, 60)] int n, [Range(-1, 1)] float s)
    {
        for (int i = 0; i < n; i++) s = s * 2f + 1f;
        return s;
    }

    [Handwired]
    public static float SelfSumShort([Range(0, 60)] int n, [Range(-1, 1)] float s)
    {
        for (int i = 0; i < n; i++) s += s;
        return s;
    }

    [Handwired]
    public static float PowerShort([Range(0, 30)] int n, [Range(-2, 2)] float x)
    {
        float s = 1f;
        for (int i = 0; i < n; i++) s = s * x;
        return s;
    }

    // The counter i changes in each second copy. Thus the continue bit of some copies is the
    // constant 1 while the active bit is a runtime bit (review of task 5, finding 2).
    [Handwired, MaxIterations(10)]
    public static int TwoStepCount([Range(0, 4)] int n)
    {
        int s = 0, i = 0, j = 0;
        while (i < n)
        {
            s += 1;
            j++;
            if (j >= 2)
            {
                i++;
                j = 0;
            }
        }

        return s;
    }

    [Handwired, MaxIterations(10)]
    public static float TwoStepSum([Range(0, 4)] int n, [Range(-100, 100)] float x)
    {
        float s = 0f;
        int i = 0, j = 0;
        while (i < n)
        {
            s += x;
            j++;
            if (j >= 2)
            {
                i++;
                j = 0;
            }
        }

        return s;
    }

    // Loop tests that span two or more blocks (review of task 5, finding 3).
    [Handwired, MaxIterations(20)]
    public static float OrCondition([Range(0, 8)] int n, [Range(0, 100)] float x)
    {
        int i = 0;
        while (x > 1f || i < n)
        {
            x *= 0.5f;
            i++;
        }

        return x;
    }

    [Handwired, MaxIterations(20)]
    public static float AndCondition([Range(0, 8)] int n, [Range(0, 100)] float x)
    {
        int i = 0;
        while (i < n && x > 1f)
        {
            x *= 0.5f;
            i++;
        }

        return x;
    }

    [Handwired]
    public static float ForAndCondition([Range(0, 8)] int n, [Range(-100, 100)] float x)
    {
        float s = 0f;
        int i = 0;
        for (; i < n && i < 3;)
        {
            s += x;
            i++;
        }

        return s;
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
    public static float ReturnInside([Range(0, 8)] int n, [Range(-100, 100)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++)
        {
            if (s > 50f) return s * 2f; // marker: return-inside
            s += x;
        }

        return s;
    }

    // A loop count from a gate (final fix wave, item J). The gate declares the range 0..9 with the
    // grid 1, so the range refinement finds 9 copies.
    [Handwired]
    public static float GateCount(bool c, [Range(0, 8)] int n, [Range(-10, 10)] float x)
    {
        if (c) n = n + 1;
        float s = 0f;
        for (int i = 0; i < n; i++) s += x;
        return s;
    }

    // A store of an int state, then a gate of the state (final fix wave, item J). In Release the
    // gate of copy 0 is Select(x[0] <= 0, -x[0], 0), with the constant 0 as a side.
    [Handwired]
    public static int[] StoreBeforeGate([FixedLength(4), Range(-10, 10)] int[] x)
    {
        var t = new int[4];
        int a = 0;
        for (int i = 0; i < 4; i++)
        {
            t[i] = a;
            if (x[i] > 0) a = a + (-x[i]);
        }

        return t;
    }

    // A break after a change of a value that the code after the loop reads. The exit edges give
    // different values to the exit block (final fix wave, item J).
    [Handwired]
    public static float BreakAfterChange([Range(0, 8)] int n, [Range(-100, 100)] float x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++)
        {
            s += x;
            if (s > 50f) break; // marker: break-after-change
        }

        return s;
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
