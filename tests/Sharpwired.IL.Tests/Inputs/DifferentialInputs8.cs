// No Handwired source. Generated inputs for the seeded differential test of the phase 6 plan, task 1.
// Each method mixes the new translator forms with the forms of phase 4. The new forms are a bool as an
// int, Math.Clamp reads, Hw.ReadOrZero, gated reads, the candidate cut, and a branch on an int constant.
// The forms of phase 4 are affine forms, gates, comparisons, Min and Max, loops of form 1 and 2, and
// inlined calls. A script with the fixed seed
// 20261018 wrote the files DifferentialInputs0.cs to DifferentialInputs9.cs one time. Do not edit them by hand.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static partial class DifferentialInputs
{
    [Handwired]
    public static (int, bool, bool) Differential256(
        bool b0, bool b1, bool b2, [Range(-10, 30)] int i0, [Range(-6, 30)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 3;
        v1 = Increment(b1, v1);
        v1 = Increment(b0, v1);
        bool c2 = StepBit(b0, tb, 1 * 5 + s0);
        bool c3 = f1 >= f0;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, bool, float, int, float, int) Differential257(
        bool b0, bool b1, bool b2, [Range(2, 17)] int i0, [Range(10, 48)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(i0, 4, 17);
        bool c2 = b0 && tb[0 * 5 + s0];
        float x3 = (float)s0 * 0.25f;
        int v4 = Math.Max(n, i0);
        float x5 = tf[Math.Clamp(i1, 0, 6)];
        if (Mode != 0) v4 = v4 - 1;
        int v6 = i1 - v4;
        for (int k = 0; k < n; k++)
        {
            v6 = v6 + (c2 ? 1 : 0);
        }
        return (v1, c2, x3, v4, x5, v6);
    }

    [Handwired]
    public static (bool, bool, int, int) Differential258(
        bool b0, bool b1, bool b2, [Range(-28, -15)] int i0, [Range(-9, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b0 ^ b1;
        bool c2 = c1 || !b0;
        int v3 = ti[Math.Clamp(n, 0, 7)];
        int v4 = v3 - n;
        return (c1, c2, v3, v4);
    }

    [Handwired]
    public static (bool, float, int, bool, float, int) Differential259(
        bool b0, bool b1, bool b2, [Range(-17, 21)] int i0, [Range(-5, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && b1;
        bool c2 = f0 >= f1;
        float x3 = f0 + (b2 ? 1 : 0);
        int v4 = n - n;
        bool c5 = v4 < n;
        float x6 = c5 ? f1 : f1;
        int v7 = s0 + (c2 ? 1 : 0);
        return (c2, x3, v4, c5, x6, v7);
    }

    [Handwired]
    public static (int, int, bool) Differential260(
        bool b0, bool b1, bool b2, [Range(-12, 25)] int i0, [Range(-20, -12)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? n : i1;
        if (b1) v1 = v1 + 2;
        int v2 = Math.Max(i1, i0);
        v1 = Increment(b0, v1);
        bool c3 = b0 && tb[2 * 5 + s0];
        if (c3) v2 = v2 + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c3) v2 = v2 + 1;
        }
        return (v1, v2, c3);
    }

    [Handwired]
    public static (float, float, int, bool) Differential261(
        bool b0, bool b1, bool b2, [Range(-12, 37)] int i0, [Range(-33, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = b1 ? f1 : f0;
        float x2 = x1 * 0.5f;
        int v3 = n + (b0 ? 1 : 0);
        bool c4 = b2 || !b2;
        return (x1, x2, v3, c4);
    }

    [Handwired]
    public static (int, bool, bool) Differential262(
        bool b0, bool b1, bool b2, [Range(-40, -1)] int i0, [Range(-24, 1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? s0 : 0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = StepBit(b2, tb, 0 * 4 + s0);
        bool c3 = i1 == 2;
        v1 = Increment(b0, v1);
        return (v1, c2, c3);
    }

    [Handwired]
    public static (bool, int, float) Differential263(
        bool b0, bool b1, bool b2, [Range(-39, -18)] int i0, [Range(-36, -35)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b1, tb, 2 * 3 + s0);
        int v2 = i0 - i1;
        if (Mode != 0) v2 = v2 - 1;
        float x3 = f1 + f1;
        if (c1) v2 = v2 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c1) v2 = v2 + 1;
        }
        return (c1, v2, x3);
    }

    [Handwired]
    public static (int, bool, bool, int, int, int) Differential264(
        bool b0, bool b1, bool b2, [Range(-27, -18)] int i0, [Range(-32, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + i1;
        bool c2 = f0 >= f1;
        bool c3 = f0 >= f1;
        int v4 = ti[Math.Clamp(i0, 1, 7)];
        v4 = Increment(c3, v4);
        int v5 = s0 * 3;
        int v6 = c2 ? v4 : s0;
        if (b2) v4 = v4 + (b0 ? 1 : 0);
        return (v1, c2, c3, v4, v5, v6);
    }

    [Handwired]
    public static (int, int, bool) Differential265(
        bool b0, bool b1, bool b2, [Range(-3, 7)] int i0, [Range(-17, 15)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n * 2;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        if (b0) v1 = v1 + 2;
        int v2 = Math.Clamp(i1, 2, 5);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        bool c3 = tb[0 * 5 + s0];
        v1 = Increment(c3, v1);
        return (v1, v2, c3);
    }

    [Handwired]
    public static (int, bool, bool, bool) Differential266(
        bool b0, bool b1, bool b2, [Range(-18, -7)] int i0, [Range(-7, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n * 3;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = StepBit(b0, tb, 0 * 3 + s0);
        if (b1) v1 = v1 + (c2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c2) v1 = v1 + 1;
        }
        bool c3 = n == -2;
        bool c4 = b0 && tb[0 * 3 + s0];
        return (v1, c2, c3, c4);
    }

    [Handwired]
    public static (int, bool) Differential267(
        bool b0, bool b1, bool b2, [Range(-3, 28)] int i0, [Range(10, 58)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + i1, 0, 4)];
        if (Mode != 0) v1 = v1 - 1;
        if (b1) v1 = v1 + 2;
        if (b0) v1 = v1 + 1;
        v1 = Increment(b1, v1);
        bool c2 = b1 && b2;
        if (c2) v1 = v1 + (b1 ? 1 : 0);
        return (v1, c2);
    }

    [Handwired]
    public static (int, float, int, int) Differential268(
        bool b0, bool b1, bool b2, [Range(-30, -5)] int i0, [Range(-13, 35)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? 4 : -3;
        float x2 = Math.Min(f0, f1);
        int v3 = Hw.ReadOrZero(ti, s0 * 3 + 1);
        int v4 = Hw.ReadOrZero(ti, v3);
        return (v1, x2, v3, v4);
    }

    [Handwired]
    public static (float, int, int, int) Differential269(
        bool b0, bool b1, bool b2, [Range(-20, -14)] int i0, [Range(-17, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 + (b0 ? 1f : 0f);
        int v2 = b2 ? s0 : s0;
        int v3 = ti[Math.Clamp(n, 0, 8)] + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b0 ? 1 : 0);
        }
        if (Mode != 0) v3 = v3 - 1;
        int v4 = b2 ? -3 : 0;
        return (x1, v2, v3, v4);
    }

    [Handwired]
    public static (bool, int, float, bool) Differential270(
        bool b0, bool b1, bool b2, [Range(8, 54)] int i0, [Range(-33, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b2, tb, 1 * 4 + s0);
        int v2 = (c1 ? 1 : 0) + (b1 ? 1 : 0);
        float x3 = Hw.ReadOrZero(tf, i0);
        if (Mode != 0) v2 = v2 - 1;
        bool c4 = b1 ^ b1;
        return (c1, v2, x3, c4);
    }

    [Handwired]
    public static (int, int) Differential271(
        bool b0, bool b1, bool b2, [Range(-40, -8)] int i0, [Range(-6, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + i0, 0, 7)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        int v2 = i0 - n;
        return (v1, v2);
    }

    [Handwired]
    public static (float, float, int, bool) Differential272(
        bool b0, bool b1, bool b2, [Range(0, 19)] int i0, [Range(-25, -1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = Math.Min(f1, f0);
        float x2 = b2 ? f1 : x1;
        int v3 = b1 ? 2 : -2;
        if (b2) v3 = v3 + 1;
        bool c4 = !tb[1 * 4 + s0] || b0;
        return (x1, x2, v3, c4);
    }

    [Handwired]
    public static (int, bool, float) Differential273(
        bool b0, bool b1, bool b2, [Range(-33, -23)] int i0, [Range(-22, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? i0 : 0;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        bool c2 = b1 || !b2;
        float x3 = b1 ? f1 : 0.75f;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, int, int, int) Differential274(
        bool b0, bool b1, bool b2, [Range(-29, -14)] int i0, [Range(-24, -11)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 - i0;
        v1 = Increment(b0, v1);
        int v2 = Hw.ReadOrZero(ti, v1);
        v2 = Increment(b1, v2);
        int v3 = Math.Clamp(s0, 1, 12);
        int v4 = ti[Math.Clamp(v2, 0, 5)] + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        if (Mode != 0) v4 = v4 - 1;
        return (v1, v2, v3, v4);
    }

    [Handwired]
    public static (bool, bool, int) Differential275(
        bool b0, bool b1, bool b2, [Range(-24, -10)] int i0, [Range(1, 24)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 && tb[2 * 3 + s0];
        bool c2 = b2 ^ b1;
        int v3 = b2 ? n : n;
        v3 = Increment(c2, v3);
        return (c1, c2, v3);
    }

    [Handwired]
    public static (int, int, float) Differential276(
        bool b0, bool b1, bool b2, [Range(-10, 7)] int i0, [Range(-30, -27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + i0;
        int v2 = ti[Math.Clamp(s0, 0, 4)] + (b0 ? 1 : 0);
        v2 = Increment(b0, v2);
        v1 = Increment(b0, v1);
        float x3 = Hw.ReadOrZero(tf, i0);
        v1 = Increment(b2, v1);
        return (v1, v2, x3);
    }

    [Handwired]
    public static (int, int, bool, int) Differential277(
        bool b0, bool b1, bool b2, [Range(-13, -8)] int i0, [Range(-30, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        int v2 = s0 - i0;
        bool c3 = b0 || !b1;
        int v4 = (c3 ? 1 : 0) + (c3 ? 1 : 0);
        return (v1, v2, c3, v4);
    }

    [Handwired]
    public static (bool, bool, int, float, int) Differential278(
        bool b0, bool b1, bool b2, [Range(-7, 17)] int i0, [Range(5, 11)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i1 < n;
        bool c2 = b0 && tb[1 * 4 + s0];
        int v3 = b1 ? 2 : -4;
        float x4 = f0 + (c2 ? 1f : 0f);
        int v5 = c2 ? s0 : 0;
        return (c1, c2, v3, x4, v5);
    }

    [Handwired]
    public static (int, int) Differential279(
        bool b0, bool b1, bool b2, [Range(-38, -10)] int i0, [Range(8, 44)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 0, 5)] + (b0 ? 1 : 0);
        int v2 = Hw.ReadOrZero(ti, s0);
        if (b2) v1 = v1 + 2;
        v1 = Increment(b2, v1);
        return (v1, v2);
    }

    [Handwired]
    public static (bool, int, float) Differential280(
        bool b0, bool b1, bool b2, [Range(8, 20)] int i0, [Range(-3, 37)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = tb[2 * 4 + s0];
        int v2 = ti[Math.Clamp(i1, 0, 6)] + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v2 = v2 + 1;
        }
        float x3 = b0 ? f0 : 0.75f;
        return (c1, v2, x3);
    }

    [Handwired]
    public static (int, bool, float, bool, float) Differential281(
        bool b0, bool b1, bool b2, [Range(-24, 3)] int i0, [Range(-12, 5)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? s0 : s0;
        bool c2 = tb[1 * 3 + s0];
        float x3 = f0 + (c2 ? 1f : 0f);
        bool c4 = n < i0;
        float x5 = (float)i1 * 0.25f;
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, x3, c4, x5);
    }

    [Handwired]
    public static (bool, int, float) Differential282(
        bool b0, bool b1, bool b2, [Range(3, 36)] int i0, [Range(-16, 5)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i1 < n;
        int v2 = ti[Math.Clamp(s0, 0, 8)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v2 = v2 + 1;
        }
        float x3 = Hw.ReadOrZero(tf, i0);
        v2 = Increment(b1, v2);
        if (Mode != 0) v2 = v2 - 1;
        return (c1, v2, x3);
    }

    [Handwired]
    public static (int, float) Differential283(
        bool b0, bool b1, bool b2, [Range(-36, -20)] int i0, [Range(-15, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, -1, 3);
        float x2 = (float)i0 * 0.25f;
        v1 = Increment(b1, v1);
        if (Mode != 0) v1 = v1 - 1;
        return (v1, x2);
    }

    [Handwired]
    public static (float, int, bool) Differential284(
        bool b0, bool b1, bool b2, [Range(-14, 29)] int i0, [Range(9, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 - f1;
        int v2 = (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v2 = v2 + 1;
        }
        v2 = Increment(b2, v2);
        bool c3 = tb[0 * 3 + s0];
        return (x1, v2, c3);
    }

    [Handwired]
    public static (int, int, int) Differential285(
        bool b0, bool b1, bool b2, [Range(-3, 9)] int i0, [Range(-3, 45)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Max(i0, i0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v2 = (b2 ? 1 : 0);
        int v3 = (b1 ? 1 : 0) + (b2 ? 1 : 0);
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, bool, bool) Differential286(
        bool b0, bool b1, bool b2, [Range(8, 21)] int i0, [Range(-8, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? n : 0;
        bool c2 = v1 < i0;
        bool c3 = c2 && b0;
        if (c3) v1 = v1 + 1;
        if (Mode != 0) v1 = v1 - 1;
        if (b2) v1 = v1 + 1;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (float, float, int, bool, float, bool) Differential287(
        bool b0, bool b1, bool b2, [Range(-3, 28)] int i0, [Range(-33, -29)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = b1 ? f0 : 0.75f;
        float x2 = (float)i0 * 0.25f;
        int v3 = (b0 ? 1 : 0) + (b1 ? 1 : 0);
        bool c4 = tb[1 * 4 + s0];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v3 = v3 + 1;
        }
        float x5 = tf[Math.Clamp(n, 0, 5)];
        if (Mode != 0) v3 = v3 - 1;
        bool c6 = x1 >= x5;
        return (x1, x2, v3, c4, x5, c6);
    }
}
