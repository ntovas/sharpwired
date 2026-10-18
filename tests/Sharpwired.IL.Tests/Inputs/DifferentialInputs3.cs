// No Handwired source. Generated inputs for the seeded differential test of the phase 6 plan, task 1.
// Each method mixes the new translator forms (a bool as an int, Math.Clamp reads, Hw.ReadOrZero, gated
// reads, the candidate cut, a branch on an int constant) with the forms of phase 4 (affine forms,
// gates, comparisons, Min and Max, loops of form 1 and 2, inlined calls). A script with the fixed seed
// 20261018 wrote the files DifferentialInputs0.cs to DifferentialInputs9.cs one time. Do not edit them by hand.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static partial class DifferentialInputs
{
    [Handwired]
    public static (int, int, bool, float) Differential096(
        bool b0, bool b1, bool b2, [Range(-40, 1)] int i0, [Range(-32, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(n, 2, 22);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        int v2 = ti[Math.Clamp(i0, 0, 8)] + (b2 ? 1 : 0);
        bool c3 = b1 && tb[1 * 3 + s0];
        float x4 = (float)i0 * 0.25f;
        v2 = Increment(c3, v2);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c3 ? 1 : 0);
        }
        return (v1, v2, c3, x4);
    }

    [Handwired]
    public static (int, bool, bool, bool, bool) Differential097(
        bool b0, bool b1, bool b2, [Range(-1, 22)] int i0, [Range(-4, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? s0 : 0;
        v1 = Increment(b2, v1);
        bool c2 = !tb[0 * 3 + s0] || b0;
        v1 = Increment(b1, v1);
        bool c3 = b0 ^ b0;
        v1 = Increment(c3, v1);
        bool c4 = c3 || !b0;
        bool c5 = b1 ^ b0;
        return (v1, c2, c3, c4, c5);
    }

    [Handwired]
    public static (int, bool, bool, bool, int, bool) Differential098(
        bool b0, bool b1, bool b2, [Range(-10, 8)] int i0, [Range(-40, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = (float)n * 0.25f;
        bool c2 = b2 || !b0;
        int v3 = (b0 ? 1 : 0);
        bool c4 = b0 && tb[Math.Clamp(v3, 0, 11)];
        bool c5 = StepBit(c2, tb, 0 * 4 + s0);
        bool c6 = c2 ^ b1;
        int v7 = b0 ? i0 : i1;
        bool c8 = b1 && tb[2 * 4 + s0];
        return (v3, c4, c5, c6, v7, c8);
    }

    [Handwired]
    public static (int, bool, int) Differential099(
        bool b0, bool b1, bool b2, [Range(-25, -17)] int i0, [Range(5, 25)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b1 ? 1 : 0);
        bool c2 = !tb[1 * 4 + s0] || b1;
        int v3 = v1 + s0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && c2) v3 = v3 + 1;
        }
        v1 = Increment(b2, v1);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (bool, int, float, float) Differential100(
        bool b0, bool b1, bool b2, [Range(-10, 2)] int i0, [Range(-33, -1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = s0 < n;
        int v2 = Hw.ReadOrZero(ti, i1);
        if (Mode != 0) v2 = v2 - 1;
        if (Mode != 0) v2 = v2 - 1;
        if (b0) v2 = v2 + (c1 ? 1 : 0);
        float x3 = f1 * 0.5f;
        float x4 = f1 * 0.5f;
        return (c1, v2, x3, x4);
    }

    [Handwired]
    public static (int, bool, int) Differential101(
        bool b0, bool b1, bool b2, [Range(-27, -20)] int i0, [Range(-9, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 4)];
        bool c2 = b2 && tb[0 * 4 + s0];
        int v3 = n + n;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, int) Differential102(
        bool b0, bool b1, bool b2, [Range(-10, 26)] int i0, [Range(-18, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 * -2;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = Hw.ReadOrZero(ti, s0);
        if (b1) v1 = v1 + (b0 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        return (v1, v2);
    }

    [Handwired]
    public static (int, float) Differential103(
        bool b0, bool b1, bool b2, [Range(10, 47)] int i0, [Range(-38, 6)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i0 : 0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        v1 = Increment(b1, v1);
        if (b0) v1 = v1 + 2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        float x2 = b2 ? f1 : 0.75f;
        return (v1, x2);
    }

    [Handwired]
    public static (int, float) Differential104(
        bool b0, bool b1, bool b2, [Range(-17, 16)] int i0, [Range(-27, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 - i1;
        if (b0) v1 = v1 + (b1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        float x2 = f1 + (b1 ? 1 : 0);
        return (v1, x2);
    }

    [Handwired]
    public static (int, bool, bool) Differential105(
        bool b0, bool b1, bool b2, [Range(10, 39)] int i0, [Range(-15, -9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0);
        if (b0) v1 = v1 + 2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = b0 ^ b2;
        bool c3 = c2 && !b1 && tb[2 * 5 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c3) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c2) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (float, int, int, bool) Differential106(
        bool b0, bool b1, bool b2, [Range(-19, 10)] int i0, [Range(-28, -21)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = Math.Max(f0, f1);
        int v2 = n + (b1 ? 1 : 0);
        if (Mode != 0) v2 = v2 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v2 = v2 + 1;
        }
        int v3 = n + (b0 ? 1 : 0);
        bool c4 = b0 || !b1;
        return (x1, v2, v3, c4);
    }

    [Handwired]
    public static (bool, int, float) Differential107(
        bool b0, bool b1, bool b2, [Range(-7, -4)] int i0, [Range(-31, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 && b1;
        int v2 = i1 + (b0 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v2 = v2 + 1;
        }
        float x3 = f0 + (b2 ? 1f : 0f);
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        return (c1, v2, x3);
    }

    [Handwired]
    public static (int, bool, float) Differential108(
        bool b0, bool b1, bool b2, [Range(-9, 40)] int i0, [Range(-22, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = tb[1 * 5 + s0];
        float x3 = f0 * 0.5f;
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, int, float, int) Differential109(
        bool b0, bool b1, bool b2, [Range(-1, 49)] int i0, [Range(-11, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 4)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        int v2 = n - v1;
        float x3 = (float)s0 * 0.25f;
        int v4 = Math.Clamp(v2, -6, 12);
        return (v1, v2, x3, v4);
    }

    [Handwired]
    public static (int, bool, bool, int, bool) Differential110(
        bool b0, bool b1, bool b2, [Range(-10, -3)] int i0, [Range(-14, -9)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = b2 && !b1 && tb[2 * 4 + s0];
        bool c3 = b1 && c2;
        int v4 = c2 ? -4 : 4;
        bool c5 = c3 && !b0 && tb[0 * 4 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && c3) v1 = v1 + 1;
        }
        return (v1, c2, c3, v4, c5);
    }

    [Handwired]
    public static (int, int, float) Differential111(
        bool b0, bool b1, bool b2, [Range(-5, 16)] int i0, [Range(-19, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i0 : i1;
        int v2 = ti[Math.Clamp(n + v1, 0, 4)];
        float x3 = f1 + (b1 ? 1 : 0);
        v1 = Increment(b0, v1);
        return (v1, v2, x3);
    }

    [Handwired]
    public static (int, float) Differential112(
        bool b0, bool b1, bool b2, [Range(-25, 12)] int i0, [Range(-20, 24)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 1, 4)];
        float x2 = b2 ? f0 : f1;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        v1 = Increment(b2, v1);
        if (b1) v1 = v1 + (b1 ? 1 : 0);
        return (v1, x2);
    }

    [Handwired]
    public static (int, int, float, int) Differential113(
        bool b0, bool b1, bool b2, [Range(-19, 11)] int i0, [Range(-2, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b1 ? 1 : 0);
        int v2 = b2 ? s0 : s0;
        float x3 = f0 + (b2 ? 1f : 0f);
        v1 = Increment(b0, v1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        int v4 = v2 - v1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        return (v1, v2, x3, v4);
    }

    [Handwired]
    public static (int, int) Differential114(
        bool b0, bool b1, bool b2, [Range(9, 42)] int i0, [Range(6, 55)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Max(n, s0);
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        int v2 = ti[Math.Clamp(i0, 0, 7)];
        return (v1, v2);
    }

    [Handwired]
    public static (int, float, float) Differential115(
        bool b0, bool b1, bool b2, [Range(-27, -24)] int i0, [Range(-23, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n * -2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        float x2 = tf[Math.Clamp(i0, 0, 3)];
        float x3 = f0 * 0.5f;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        return (v1, x2, x3);
    }

    [Handwired]
    public static (int, float, int, int, float) Differential116(
        bool b0, bool b1, bool b2, [Range(-39, -18)] int i0, [Range(-1, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 1, 7)];
        float x2 = Hw.ReadOrZero(tf, s0);
        int v3 = ti[Math.Clamp(s0 + s0, 0, 8)];
        int v4 = ti[Math.Clamp(n + s0, 0, 8)];
        if (Mode != 0) v3 = v3 - 1;
        float x5 = f0 + (b0 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v4 = v4 + 1;
        }
        return (v1, x2, v3, v4, x5);
    }

    [Handwired]
    public static (int, float, bool) Differential117(
        bool b0, bool b1, bool b2, [Range(-25, 0)] int i0, [Range(-4, 34)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        float x2 = f0 + f0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        bool c3 = tb[0 * 5 + s0];
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c3 ? 1 : 0);
        }
        return (v1, x2, c3);
    }

    [Handwired]
    public static (bool, int, bool) Differential118(
        bool b0, bool b1, bool b2, [Range(-23, -15)] int i0, [Range(9, 28)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = tb[2 * 4 + s0];
        int v2 = Math.Min(s0, n);
        v2 = Increment(b1, v2);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && c1) v2 = v2 + 1;
        }
        bool c3 = c1 ^ b1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v2 = v2 + 1;
        }
        return (c1, v2, c3);
    }

    [Handwired]
    public static (int, float, int) Differential119(
        bool b0, bool b1, bool b2, [Range(-37, 4)] int i0, [Range(-18, -2)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 5)] + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        float x2 = (float)s0 * 0.25f;
        v1 = Increment(b0, v1);
        int v3 = ti[Math.Clamp(i1, 1, 4)];
        return (v1, x2, v3);
    }

    [Handwired]
    public static (int, float, bool, bool, int) Differential120(
        bool b0, bool b1, bool b2, [Range(-40, -15)] int i0, [Range(-40, -30)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1 * 1 + 0);
        float x2 = f0 + (b0 ? 1f : 0f);
        bool c3 = b0 && !b1 && tb[2 * 4 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c4 = b2 || !b2;
        if (b0) v1 = v1 + (c4 ? 1 : 0);
        int v5 = ti[Math.Clamp(s0 + n, 0, 7)];
        return (v1, x2, c3, c4, v5);
    }

    [Handwired]
    public static (int, float, float, float) Differential121(
        bool b0, bool b1, bool b2, [Range(-20, -17)] int i0, [Range(-31, -2)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + i1, 0, 6)];
        float x2 = b0 ? f0 : f1;
        float x3 = Math.Min(f1, x2);
        float x4 = f1 - f0;
        return (v1, x2, x3, x4);
    }

    [Handwired]
    public static (int, bool, int, int) Differential122(
        bool b0, bool b1, bool b2, [Range(6, 44)] int i0, [Range(-27, -8)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 2 + 4);
        if (b2) v1 = v1 + 1;
        bool c2 = b0 && tb[Math.Clamp(i0, 0, 8)];
        int v3 = s0 + s0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v3 = v3 + 1;
        }
        int v4 = (b0 ? 1 : 0) + (b0 ? 1 : 0);
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, bool) Differential123(
        bool b0, bool b1, bool b2, [Range(-27, 5)] int i0, [Range(-11, 6)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        bool c2 = b2 && tb[Math.Clamp(n, 0, 11)];
        if (c2) v1 = v1 + 1;
        if (c2) v1 = v1 + 2;
        return (v1, c2);
    }

    [Handwired]
    public static (int, int, int) Differential124(
        bool b0, bool b1, bool b2, [Range(-8, 0)] int i0, [Range(0, 15)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0 + s0, 0, 7)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        int v2 = s0 + v1;
        int v3 = Math.Clamp(n, 2, 4);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v3 = v3 + 1;
        }
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, int, bool) Differential125(
        bool b0, bool b1, bool b2, [Range(-21, 17)] int i0, [Range(-10, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b2 ? 1 : 0);
        int v2 = b0 ? n : 0;
        if (Mode != 0) v2 = v2 - 1;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v2 = v2 + 1;
        }
        bool c3 = b2 && tb[Math.Clamp(n, 0, 11)];
        return (v1, v2, c3);
    }

    [Handwired]
    public static (int, bool, float) Differential126(
        bool b0, bool b1, bool b2, [Range(1, 15)] int i0, [Range(-25, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + i0, 0, 8)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        bool c2 = b2 ^ b1;
        float x3 = f0 * 0.5f;
        v1 = Increment(b0, v1);
        v1 = Increment(b2, v1);
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, bool) Differential127(
        bool b0, bool b1, bool b2, [Range(2, 49)] int i0, [Range(-14, 19)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 * -2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        bool c2 = b2 && !b2 && tb[0 * 5 + s0];
        v1 = Increment(c2, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c2) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2);
    }
}
