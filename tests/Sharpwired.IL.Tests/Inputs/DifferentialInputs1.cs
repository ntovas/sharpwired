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
    public static (int, bool, int, int) Differential032(
        bool b0, bool b1, bool b2, [Range(-26, 19)] int i0, [Range(-27, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i1 : n;
        bool c2 = StepBit(b0, tb, 2 * 5 + s0);
        int v3 = b2 ? i1 : i1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c2) v1 = v1 + 1;
        }
        int v4 = s0 * 3;
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (bool, int, int, float) Differential033(
        bool b0, bool b1, bool b2, [Range(-32, 18)] int i0, [Range(-28, -10)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b2, tb, 0 * 5 + s0);
        int v2 = b0 ? -1 : -4;
        v2 = Increment(b2, v2);
        int v3 = i0 + v2;
        float x4 = b1 ? f0 : f1;
        return (c1, v2, v3, x4);
    }

    [Handwired]
    public static (float, int, bool, int, bool, int) Differential034(
        bool b0, bool b1, bool b2, [Range(-20, -2)] int i0, [Range(-28, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 * 0.5f;
        float x2 = Hw.ReadOrZero(tf, i1);
        int v3 = i0 * 3;
        bool c4 = b0 && tb[2 * 4 + s0];
        int v5 = b0 ? n : 0;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v5 = v5 + 1;
        }
        bool c6 = b0 && c4;
        int v7 = Hw.ReadOrZero(ti, v3);
        return (x2, v3, c4, v5, c6, v7);
    }

    [Handwired]
    public static (float, int, bool, float, float) Differential035(
        bool b0, bool b1, bool b2, [Range(2, 32)] int i0, [Range(-18, -5)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = b0 ? f1 : 0.75f;
        int v2 = ti[Math.Clamp(s0, 1, 6)];
        bool c3 = b0 || !b2;
        float x4 = x1 + (c3 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c3) v2 = v2 + 1;
        }
        v2 = Increment(b0, v2);
        float x5 = x4 + (b2 ? 1 : 0);
        return (x1, v2, c3, x4, x5);
    }

    [Handwired]
    public static (bool, int, int, int) Differential036(
        bool b0, bool b1, bool b2, [Range(-33, 6)] int i0, [Range(-10, -6)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[1 * 4 + s0] || b0;
        int v2 = ti[Math.Clamp(i0, 1, 5)];
        int v3 = s0 - v2;
        int v4 = ti[Math.Clamp(v3 + s0, 0, 6)];
        v2 = Increment(b2, v2);
        return (c1, v2, v3, v4);
    }

    [Handwired]
    public static (int, int, float, int) Differential037(
        bool b0, bool b1, bool b2, [Range(-14, 25)] int i0, [Range(-20, 11)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(s0, i1);
        int v2 = Hw.ReadOrZero(ti, v1);
        float x3 = f1 - f0;
        int v4 = Math.Min(v2, v1);
        return (v1, v2, x3, v4);
    }

    [Handwired]
    public static (bool, bool, bool, bool, bool) Differential038(
        bool b0, bool b1, bool b2, [Range(-33, -5)] int i0, [Range(9, 48)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = f0 >= f1;
        bool c2 = c1 ^ c1;
        bool c3 = c1 || !c2;
        bool c4 = s0 < i0;
        bool c5 = tb[1 * 3 + s0];
        return (c1, c2, c3, c4, c5);
    }

    [Handwired]
    public static (int, bool, int) Differential039(
        bool b0, bool b1, bool b2, [Range(-3, 30)] int i0, [Range(-3, 46)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 1 + -2);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c2 = f0 >= f1;
        int v3 = ti[Math.Clamp(i1, 1, 6)];
        v1 = Increment(b1, v1);
        if (Mode != 0) v1 = v1 - 1;
        if (b2) v1 = v1 + (b2 ? 1 : 0);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, float, int, bool) Differential040(
        bool b0, bool b1, bool b2, [Range(-36, 2)] int i0, [Range(-9, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? 2 : 4;
        float x2 = f1 * 3f;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        int v3 = (b0 ? 1 : 0);
        bool c4 = b2 || !b0;
        if (Mode != 0) v3 = v3 - 1;
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (int, bool, bool, bool) Differential041(
        bool b0, bool b1, bool b2, [Range(1, 37)] int i0, [Range(-30, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n + s0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        bool c2 = n < v1;
        bool c3 = b0 && tb[Math.Clamp(i1, 0, 8)];
        bool c4 = StepBit(b2, tb, 2 * 3 + s0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c3) v1 = v1 + 1;
        }
        v1 = Increment(c3, v1);
        return (v1, c2, c3, c4);
    }

    [Handwired]
    public static (int, bool, bool) Differential042(
        bool b0, bool b1, bool b2, [Range(3, 19)] int i0, [Range(-11, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 1, 6)];
        bool c2 = b2 ^ b2;
        bool c3 = v1 == 0;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int) Differential043(
        bool b0, bool b1, bool b2, [Range(-37, 4)] int i0, [Range(-19, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n * -2;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        if (b0) v1 = v1 + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v2 = b0 ? v1 : n;
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b2, v1);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        return (v1, v2);
    }

    [Handwired]
    public static (int, float) Differential044(
        bool b0, bool b1, bool b2, [Range(9, 59)] int i0, [Range(4, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(i0, i1);
        v1 = Increment(b0, v1);
        float x2 = Hw.ReadOrZero(tf, n);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (b2) v1 = v1 + 1;
        if (b2) v1 = v1 + (b0 ? 1 : 0);
        v1 = Increment(b2, v1);
        return (v1, x2);
    }

    [Handwired]
    public static (int, float, int, int, float, bool) Differential045(
        bool b0, bool b1, bool b2, [Range(4, 14)] int i0, [Range(6, 42)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? -2 : -4;
        float x2 = Hw.ReadOrZero(tf, i1);
        int v3 = ti[Math.Clamp(i0, 0, 4)];
        v1 = Increment(b2, v1);
        int v4 = i1 + (b2 ? 1 : 0);
        float x5 = f1 - f1;
        bool c6 = b2 || !b1;
        return (v1, x2, v3, v4, x5, c6);
    }

    [Handwired]
    public static (int, bool, int, bool, bool) Differential046(
        bool b0, bool b1, bool b2, [Range(10, 39)] int i0, [Range(-39, -35)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n + n;
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = StepBit(b1, tb, 1 * 3 + s0);
        int v3 = ti[Math.Clamp(i1, 0, 5)] + (c2 ? 1 : 0);
        bool c4 = b2 && !c2 && tb[2 * 3 + s0];
        bool c5 = tb[2 * 3 + s0];
        return (v1, c2, v3, c4, c5);
    }

    [Handwired]
    public static (int, bool, int, float) Differential047(
        bool b0, bool b1, bool b2, [Range(-38, -16)] int i0, [Range(-21, 29)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + i0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        v1 = Increment(b0, v1);
        bool c2 = b1 ^ b0;
        int v3 = i1 + (b1 ? 1 : 0);
        float x4 = b0 ? f1 : 0.75f;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c2 ? 1 : 0);
        }
        return (v1, c2, v3, x4);
    }

    [Handwired]
    public static (float, int) Differential048(
        bool b0, bool b1, bool b2, [Range(-23, -3)] int i0, [Range(-18, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = b2 ? f1 : 0.75f;
        int v2 = Hw.ReadOrZero(ti, i0 * 3 + -3);
        v2 = Increment(b2, v2);
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b0 ? 1 : 0);
        }
        return (x1, v2);
    }

    [Handwired]
    public static (int, int, bool, float) Differential049(
        bool b0, bool b1, bool b2, [Range(-2, 22)] int i0, [Range(-27, -9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i1 : i0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        int v2 = ti[Math.Clamp(n + i1, 0, 6)];
        bool c3 = b1 && b2;
        float x4 = f1 - f1;
        return (v1, v2, c3, x4);
    }

    [Handwired]
    public static (int, bool) Differential050(
        bool b0, bool b1, bool b2, [Range(6, 18)] int i0, [Range(6, 51)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + n;
        if (b0) v1 = v1 + 2;
        bool c2 = b2 && !b1 && tb[2 * 4 + s0];
        v1 = Increment(b0, v1);
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool) Differential051(
        bool b0, bool b1, bool b2, [Range(-30, 18)] int i0, [Range(-36, -18)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(i0, i1);
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b2, v1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        bool c2 = b1 && !b1 && tb[1 * 3 + s0];
        if (b0) v1 = v1 + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool, int) Differential052(
        bool b0, bool b1, bool b2, [Range(9, 58)] int i0, [Range(-25, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 - i1;
        bool c2 = b2 ^ b2;
        if (b1) v1 = v1 + 2;
        if (Mode != 0) v1 = v1 - 1;
        int v3 = b2 ? 2 : -1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        v3 = Increment(b0, v3);
        v1 = Increment(b2, v1);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, bool, float, bool) Differential053(
        bool b0, bool b1, bool b2, [Range(-17, 31)] int i0, [Range(-6, -1)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(n, -3, 7);
        bool c2 = b1 && tb[Math.Clamp(i0, 0, 14)];
        float x3 = f0 - f1;
        bool c4 = b0 && b2;
        return (v1, c2, x3, c4);
    }

    [Handwired]
    public static (bool, int, float, bool) Differential054(
        bool b0, bool b1, bool b2, [Range(-15, 10)] int i0, [Range(6, 16)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && b2;
        int v2 = ti[Math.Clamp(i0, 1, 4)];
        float x3 = f1 + f1;
        v2 = Increment(b1, v2);
        bool c4 = b1 && tb[2 * 4 + s0];
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        return (c1, v2, x3, c4);
    }

    [Handwired]
    public static (int, float) Differential055(
        bool b0, bool b1, bool b2, [Range(-14, 34)] int i0, [Range(-5, 28)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + s0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        float x2 = f1 + (b0 ? 1f : 0f);
        return (v1, x2);
    }

    [Handwired]
    public static (float, int, int, int) Differential056(
        bool b0, bool b1, bool b2, [Range(-10, 38)] int i0, [Range(-32, -27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = tf[Math.Clamp(i0, 0, 5)];
        int v2 = ti[Math.Clamp(n + i0, 0, 6)];
        int v3 = Hw.ReadOrZero(ti, n * 2 + 6);
        int v4 = Math.Min(i0, v3);
        return (x1, v2, v3, v4);
    }

    [Handwired]
    public static (float, bool, int, bool, int, bool) Differential057(
        bool b0, bool b1, bool b2, [Range(-40, -10)] int i0, [Range(-8, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = Math.Max(f0, f1);
        bool c2 = b1 ^ b0;
        int v3 = s0 * -2;
        v3 = Increment(b2, v3);
        bool c4 = i0 == -2;
        int v5 = ti[Math.Clamp(s0 + i0, 0, 8)];
        bool c6 = tb[2 * 5 + s0];
        v5 = Increment(b1, v5);
        return (x1, c2, v3, c4, v5, c6);
    }

    [Handwired]
    public static (int, bool, bool, int, bool) Differential058(
        bool b0, bool b1, bool b2, [Range(-7, -2)] int i0, [Range(-28, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 + (b2 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = b2 || !b2;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        bool c3 = b1 && tb[Math.Clamp(v1, 0, 15)];
        int v4 = b0 ? -4 : 5;
        bool c5 = b1 && tb[Math.Clamp(v4, 0, 15)];
        return (v1, c2, c3, v4, c5);
    }

    [Handwired]
    public static (bool, int, int) Differential059(
        bool b0, bool b1, bool b2, [Range(-6, 32)] int i0, [Range(-4, 28)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && tb[Math.Clamp(i1, 0, 19)];
        int v2 = b1 ? i0 : 0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v2 = v2 + 1;
        }
        int v3 = i1 + s0;
        return (c1, v2, v3);
    }

    [Handwired]
    public static (bool, bool, bool, float, int, int) Differential060(
        bool b0, bool b1, bool b2, [Range(-22, -21)] int i0, [Range(-11, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[2 * 4 + s0] || b0;
        bool c2 = b1 && !c1 && tb[2 * 4 + s0];
        bool c3 = StepBit(b0, tb, 1 * 4 + s0);
        float x4 = b0 ? f0 : f0;
        int v5 = Math.Clamp(s0, 3, 21);
        int v6 = ti[Math.Clamp(i0, 1, 6)];
        return (c1, c2, c3, x4, v5, v6);
    }

    [Handwired]
    public static (int, float, bool, int) Differential061(
        bool b0, bool b1, bool b2, [Range(0, 42)] int i0, [Range(1, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(n, i1);
        if (b1) v1 = v1 + 1;
        float x2 = f0 - f0;
        bool c3 = f1 >= f0;
        int v4 = ti[Math.Clamp(v1, 0, 8)];
        return (v1, x2, c3, v4);
    }

    [Handwired]
    public static (int, bool, int) Differential062(
        bool b0, bool b1, bool b2, [Range(-33, -19)] int i0, [Range(-37, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + i0, 0, 7)];
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        bool c2 = i1 < n;
        int v3 = ti[Math.Clamp(i0 + n, 0, 7)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && c2) v3 = v3 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, v3);
    }

    [Handwired]
    public static (bool, int, bool, int, int) Differential063(
        bool b0, bool b1, bool b2, [Range(2, 42)] int i0, [Range(0, 43)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = n < i1;
        int v2 = i1 * -2;
        bool c3 = StepBit(c1, tb, 2 * 5 + s0);
        int v4 = ti[Math.Clamp(n, 0, 5)] + (c1 ? 1 : 0);
        int v5 = Math.Max(n, n);
        return (c1, v2, c3, v4, v5);
    }
}
