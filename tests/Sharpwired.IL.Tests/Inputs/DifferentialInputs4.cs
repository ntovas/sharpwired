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
    public static (bool, int, bool) Differential128(
        bool b0, bool b1, bool b2, [Range(-28, 17)] int i0, [Range(6, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[3 * 4 + s0] || b0;
        int v2 = Hw.ReadOrZero(ti, i1);
        bool c3 = b2 && tb[2 * 4 + s0];
        v2 = Increment(c3, v2);
        return (c1, v2, c3);
    }

    [Handwired]
    public static (float, bool, int, int) Differential129(
        bool b0, bool b1, bool b2, [Range(-4, 17)] int i0, [Range(-2, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = (float)n * 0.25f;
        bool c2 = b1 && tb[Math.Clamp(i1, 0, 11)];
        int v3 = n * -2;
        v3 = Increment(c2, v3);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v3 = v3 + 1;
        }
        if (Mode != 0) v3 = v3 - 1;
        int v4 = (b2 ? 1 : 0) + (c2 ? 1 : 0);
        return (x1, c2, v3, v4);
    }

    [Handwired]
    public static (int, float, int, int, float, int) Differential130(
        bool b0, bool b1, bool b2, [Range(-20, -10)] int i0, [Range(-20, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 - i1;
        float x2 = f1 + f0;
        int v3 = b2 ? n : 0;
        if (b0) v1 = v1 + 2;
        int v4 = (b2 ? 1 : 0);
        float x5 = b2 ? f1 : f1;
        int v6 = v3 + v3;
        return (v1, x2, v3, v4, x5, v6);
    }

    [Handwired]
    public static (bool, int, bool) Differential131(
        bool b0, bool b1, bool b2, [Range(-11, 35)] int i0, [Range(-28, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && !b2 && tb[0 * 3 + s0];
        int v2 = n - s0;
        if (c1) v2 = v2 + 1;
        if (Mode != 0) v2 = v2 - 1;
        bool c3 = i0 < s0;
        if (b0) v2 = v2 + 1;
        return (c1, v2, c3);
    }

    [Handwired]
    public static (int, float, float, bool, float) Differential132(
        bool b0, bool b1, bool b2, [Range(-14, 13)] int i0, [Range(-37, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 1, 6)];
        float x2 = f0 + (b1 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        if (b0) v1 = v1 + 1;
        float x3 = f1 - f1;
        bool c4 = StepBit(b0, tb, 1 * 5 + s0);
        float x5 = b1 ? x2 : 0.75f;
        return (v1, x2, x3, c4, x5);
    }

    [Handwired]
    public static (int, int, int) Differential133(
        bool b0, bool b1, bool b2, [Range(-15, -13)] int i0, [Range(-23, 19)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 0, 6)] + (b0 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b1, v1);
        if (b0) v1 = v1 + (b1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        int v2 = i0 * -2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v2 = v2 + 1;
        }
        int v3 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, bool) Differential134(
        bool b0, bool b1, bool b2, [Range(-2, 33)] int i0, [Range(-31, -10)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b1 ? 1 : 0);
        bool c2 = b2 && tb[1 * 5 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, int, int, int) Differential135(
        bool b0, bool b1, bool b2, [Range(-34, -13)] int i0, [Range(-17, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? -3 : 1;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = (b1 ? 1 : 0) + (b0 ? 1 : 0);
        int v3 = ti[Math.Clamp(v2, 0, 7)];
        int v4 = b1 ? s0 : 0;
        return (v1, v2, v3, v4);
    }

    [Handwired]
    public static (bool, float, int, int, bool) Differential136(
        bool b0, bool b1, bool b2, [Range(-40, -10)] int i0, [Range(-11, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && b2;
        float x2 = Hw.ReadOrZero(tf, i1);
        int v3 = Hw.ReadOrZero(ti, s0);
        int v4 = (b1 ? 1 : 0);
        bool c5 = b0 ^ b1;
        return (c1, x2, v3, v4, c5);
    }

    [Handwired]
    public static (int, int, bool, int) Differential137(
        bool b0, bool b1, bool b2, [Range(-27, -2)] int i0, [Range(-16, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        int v2 = ti[Math.Clamp(i0, 0, 6)];
        bool c3 = b1 || !b1;
        if (b2) v2 = v2 + (c3 ? 1 : 0);
        if (b2) v1 = v1 + (c3 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        int v4 = i1 * 3;
        return (v1, v2, c3, v4);
    }

    [Handwired]
    public static (int, bool, int, float, bool) Differential138(
        bool b0, bool b1, bool b2, [Range(-40, -29)] int i0, [Range(-13, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? 2 : 1;
        bool c2 = !tb[0 * 3 + s0] || b1;
        int v3 = ti[Math.Clamp(i1, 0, 5)] + (b2 ? 1 : 0);
        float x4 = (float)v3 * 0.25f;
        v1 = Increment(b2, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c2) v3 = v3 + 1;
        }
        bool c5 = b2 ^ c2;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, v3, x4, c5);
    }

    [Handwired]
    public static (int, float, int, bool) Differential139(
        bool b0, bool b1, bool b2, [Range(0, 23)] int i0, [Range(-33, 16)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? i1 : 0;
        float x2 = f1 + f1;
        int v3 = b2 ? s0 : s0;
        bool c4 = StepBit(b2, tb, 3 * 3 + s0);
        v1 = Increment(b1, v1);
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (int, bool, int, int) Differential140(
        bool b0, bool b1, bool b2, [Range(-25, -6)] int i0, [Range(-39, -11)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + s0, 0, 7)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        v1 = Increment(b0, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        bool c2 = b1 ^ b1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v3 = Hw.ReadOrZero(ti, n);
        int v4 = v1 * 3;
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, int, int, int, int) Differential141(
        bool b0, bool b1, bool b2, [Range(-37, -34)] int i0, [Range(-23, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1);
        if (b2) v1 = v1 + (b0 ? 1 : 0);
        int v2 = Math.Min(s0, n);
        int v3 = ti[Math.Clamp(n, 1, 6)];
        int v4 = (b0 ? 1 : 0) + (b2 ? 1 : 0);
        int v5 = Math.Clamp(s0, -1, -1);
        return (v1, v2, v3, v4, v5);
    }

    [Handwired]
    public static (int, float, int) Differential142(
        bool b0, bool b1, bool b2, [Range(-14, 20)] int i0, [Range(-5, 43)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        float x2 = (float)v1 * 0.25f;
        int v3 = ti[Math.Clamp(v1, 0, 6)];
        return (v1, x2, v3);
    }

    [Handwired]
    public static (int, bool, int, float) Differential143(
        bool b0, bool b1, bool b2, [Range(-30, -5)] int i0, [Range(-21, -7)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + i0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        bool c2 = i1 == -3;
        int v3 = (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v3 = v3 + 1;
        }
        if (Mode != 0) v3 = v3 - 1;
        v1 = Increment(b1, v1);
        float x4 = tf[Math.Clamp(i0, 0, 7)];
        return (v1, c2, v3, x4);
    }

    [Handwired]
    public static (float, float, int, bool, bool, bool) Differential144(
        bool b0, bool b1, bool b2, [Range(-19, -8)] int i0, [Range(-27, -5)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 * 3f;
        float x2 = Hw.ReadOrZero(tf, i0);
        int v3 = ti[Math.Clamp(s0 + n, 0, 6)];
        if (b0) v3 = v3 + (b2 ? 1 : 0);
        bool c4 = b1 || !b2;
        bool c5 = b2 && tb[1 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c4) v3 = v3 + 1;
        }
        bool c6 = StepBit(c5, tb, 3 * 3 + s0);
        return (x1, x2, v3, c4, c5, c6);
    }

    [Handwired]
    public static (int, bool, bool) Differential145(
        bool b0, bool b1, bool b2, [Range(-23, -21)] int i0, [Range(-13, 1)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 2 + -5);
        bool c2 = !tb[3 * 5 + s0] || b2;
        v1 = Increment(b1, v1);
        bool c3 = b1 || !b1;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int) Differential146(
        bool b0, bool b1, bool b2, [Range(-20, -5)] int i0, [Range(-4, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n);
        v1 = Increment(b0, v1);
        int v2 = b1 ? 1 : -5;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, int) Differential147(
        bool b0, bool b1, bool b2, [Range(-12, 20)] int i0, [Range(-12, 36)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i1 : n;
        bool c2 = b2 && tb[Math.Clamp(v1, 0, 8)];
        v1 = Increment(b1, v1);
        int v3 = ti[Math.Clamp(i0, 0, 5)] + (b1 ? 1 : 0);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, float, bool, bool) Differential148(
        bool b0, bool b1, bool b2, [Range(4, 29)] int i0, [Range(-19, 20)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        float x2 = f1 + (b1 ? 1 : 0);
        bool c3 = StepBit(b1, tb, 2 * 3 + s0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        bool c4 = b0 || !b1;
        return (v1, x2, c3, c4);
    }

    [Handwired]
    public static (int, int, int, float) Differential149(
        bool b0, bool b1, bool b2, [Range(-39, 11)] int i0, [Range(-17, 18)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + n, 0, 4)];
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        if (b0) v1 = v1 + (b0 ? 1 : 0);
        int v2 = Math.Clamp(i0, -1, 12);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        int v3 = Math.Min(i1, i1);
        float x4 = f1 * 3f;
        return (v1, v2, v3, x4);
    }

    [Handwired]
    public static (float, int) Differential150(
        bool b0, bool b1, bool b2, [Range(-25, -5)] int i0, [Range(-7, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = tf[Math.Clamp(i1, 0, 5)];
        int v2 = ti[Math.Clamp(i1, 1, 5)];
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b0 ? 1 : 0);
        }
        if (Mode != 0) v2 = v2 - 1;
        v2 = Increment(b0, v2);
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        v2 = Increment(b2, v2);
        return (x1, v2);
    }

    [Handwired]
    public static (int, bool, bool) Differential151(
        bool b0, bool b1, bool b2, [Range(-35, -5)] int i0, [Range(9, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b2 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = f1 >= f0;
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b2, v1);
        bool c3 = !tb[3 * 4 + s0] || c2;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (bool, int, float) Differential152(
        bool b0, bool b1, bool b2, [Range(-38, 10)] int i0, [Range(-19, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && tb[Math.Clamp(i0, 0, 8)];
        int v2 = Math.Min(i0, i0);
        float x3 = tf[Math.Clamp(s0, 0, 4)];
        if (Mode != 0) v2 = v2 - 1;
        if (b0) v2 = v2 + 1;
        return (c1, v2, x3);
    }

    [Handwired]
    public static (float, int) Differential153(
        bool b0, bool b1, bool b2, [Range(-13, 37)] int i0, [Range(-10, 38)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 - f0;
        int v2 = ti[Math.Clamp(i1, 0, 7)] + (b0 ? 1 : 0);
        if (Mode != 0) v2 = v2 - 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v2 = v2 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v2 = v2 + 1;
        }
        return (x1, v2);
    }

    [Handwired]
    public static (int, int, bool, bool) Differential154(
        bool b0, bool b1, bool b2, [Range(-21, -10)] int i0, [Range(-15, -12)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? s0 : i1;
        int v2 = b1 ? -1 : 4;
        v2 = Increment(b2, v2);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        bool c3 = v1 == 1;
        bool c4 = tb[3 * 5 + s0];
        if (c3) v1 = v1 + (c3 ? 1 : 0);
        return (v1, v2, c3, c4);
    }

    [Handwired]
    public static (int, int, int, bool) Differential155(
        bool b0, bool b1, bool b2, [Range(-21, -14)] int i0, [Range(-12, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, s0 * 2 + 0);
        int v2 = ti[Math.Clamp(n, 1, 5)];
        int v3 = Hw.ReadOrZero(ti, i0);
        bool c4 = b0 && tb[0 * 4 + s0];
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        if (Mode != 0) v3 = v3 - 1;
        return (v1, v2, v3, c4);
    }

    [Handwired]
    public static (int, bool, bool, bool) Differential156(
        bool b0, bool b1, bool b2, [Range(-25, 12)] int i0, [Range(-20, -12)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, -9, 4);
        bool c2 = b0 && tb[Math.Clamp(n, 0, 15)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        bool c3 = tb[3 * 4 + s0];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        bool c4 = b0 && b1;
        return (v1, c2, c3, c4);
    }

    [Handwired]
    public static (float, int) Differential157(
        bool b0, bool b1, bool b2, [Range(-17, 21)] int i0, [Range(8, 49)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = Hw.ReadOrZero(tf, i1);
        int v2 = b2 ? -1 : 2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v2 = v2 + 1;
        }
        if (Mode != 0) v2 = v2 - 1;
        return (x1, v2);
    }

    [Handwired]
    public static (int, float) Differential158(
        bool b0, bool b1, bool b2, [Range(-7, 37)] int i0, [Range(-10, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? s0 : s0;
        v1 = Increment(b2, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        float x2 = (float)i0 * 0.25f;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        return (v1, x2);
    }

    [Handwired]
    public static (int, int, int) Differential159(
        bool b0, bool b1, bool b2, [Range(-14, -2)] int i0, [Range(8, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + n;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = i1 * -2;
        v2 = Increment(b2, v2);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v2 = v2 + 1;
        }
        int v3 = ti[Math.Clamp(n + n, 0, 4)];
        return (v1, v2, v3);
    }
}
