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
    public static (int, int, int) Differential224(
        bool b0, bool b1, bool b2, [Range(-24, 2)] int i0, [Range(-4, 35)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? 5 : -3;
        v1 = Increment(b1, v1);
        int v2 = ti[Math.Clamp(n + n, 0, 8)];
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        int v3 = b0 ? i0 : i1;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b0 ? 1 : 0);
        }
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, float) Differential225(
        bool b0, bool b1, bool b2, [Range(9, 18)] int i0, [Range(-29, -16)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n + (b2 ? 1 : 0);
        float x2 = f0 * 3f;
        v1 = Increment(b2, v1);
        v1 = Increment(b2, v1);
        return (v1, x2);
    }

    [Handwired]
    public static (int, bool, bool, int) Differential226(
        bool b0, bool b1, bool b2, [Range(-13, 17)] int i0, [Range(1, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 7)] + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = StepBit(b0, tb, 2 * 3 + s0);
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = b1 && c2;
        int v4 = ti[Math.Clamp(v1, 1, 6)];
        return (v1, c2, c3, v4);
    }

    [Handwired]
    public static (bool, bool, int) Differential227(
        bool b0, bool b1, bool b2, [Range(-15, -14)] int i0, [Range(-20, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && tb[Math.Clamp(i1, 0, 8)];
        bool c2 = b0 || !c1;
        int v3 = b2 ? i0 : i0;
        if (b0) v3 = v3 + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (c2 ? 1 : 0);
        }
        return (c1, c2, v3);
    }

    [Handwired]
    public static (int, bool, int, int) Differential228(
        bool b0, bool b1, bool b2, [Range(-37, 7)] int i0, [Range(-40, 0)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n - i1;
        bool c2 = b0 && tb[Math.Clamp(v1, 0, 15)];
        if (b1) v1 = v1 + 1;
        int v3 = v1 - s0;
        int v4 = b1 ? 0 : 4;
        if (c2) v3 = v3 + (b2 ? 1 : 0);
        v1 = Increment(b0, v1);
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, float, bool) Differential229(
        bool b0, bool b1, bool b2, [Range(-22, 3)] int i0, [Range(-26, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 4)];
        float x2 = b1 ? f1 : 0.75f;
        bool c3 = x2 >= f1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        if (b1) v1 = v1 + 1;
        return (v1, x2, c3);
    }

    [Handwired]
    public static (int, float, int) Differential230(
        bool b0, bool b1, bool b2, [Range(-24, 5)] int i0, [Range(-17, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, s0);
        if (b0) v1 = v1 + 1;
        float x2 = (float)s0 * 0.25f;
        if (b0) v1 = v1 + 1;
        v1 = Increment(b2, v1);
        v1 = Increment(b1, v1);
        if (Mode != 0) v1 = v1 - 1;
        int v3 = Hw.ReadOrZero(ti, n * 3 + 6);
        return (v1, x2, v3);
    }

    [Handwired]
    public static (int, bool) Differential231(
        bool b0, bool b1, bool b2, [Range(-18, -16)] int i0, [Range(0, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(n, 3, 8);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = StepBit(b2, tb, 2 * 4 + s0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool, bool, int) Differential232(
        bool b0, bool b1, bool b2, [Range(-38, -18)] int i0, [Range(2, 25)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 3;
        bool c2 = b2 ^ b0;
        v1 = Increment(b1, v1);
        bool c3 = i0 < n;
        int v4 = b2 ? i1 : i1;
        return (v1, c2, c3, v4);
    }

    [Handwired]
    public static (int, int, int, bool, float) Differential233(
        bool b0, bool b1, bool b2, [Range(-13, 37)] int i0, [Range(-11, 32)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, -3, 8);
        int v2 = ti[Math.Clamp(i1 + i0, 0, 7)];
        int v3 = Hw.ReadOrZero(ti, i1);
        bool c4 = i1 < s0;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v2 = v2 + 1;
        }
        if (Mode != 0) v2 = v2 - 1;
        float x5 = f1 + (b1 ? 1f : 0f);
        return (v1, v2, v3, c4, x5);
    }

    [Handwired]
    public static (int, int, int) Differential234(
        bool b0, bool b1, bool b2, [Range(-19, -10)] int i0, [Range(10, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 * -2;
        v1 = Increment(b0, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        int v2 = ti[Math.Clamp(s0, 0, 7)];
        int v3 = ti[Math.Clamp(v1, 1, 6)];
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, int, int) Differential235(
        bool b0, bool b1, bool b2, [Range(4, 29)] int i0, [Range(-2, 46)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? -2 : -5;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        int v2 = b0 ? i0 : i0;
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v2 = v2 + 1;
        }
        int v3 = Math.Clamp(i1, -1, 4);
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, bool, float) Differential236(
        bool b0, bool b1, bool b2, [Range(8, 38)] int i0, [Range(1, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 0, 5)];
        v1 = Increment(b0, v1);
        if (b1) v1 = v1 + (b0 ? 1 : 0);
        bool c2 = b0 ^ b2;
        if (c2) v1 = v1 + (c2 ? 1 : 0);
        float x3 = f0 - f0;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, int, int, float) Differential237(
        bool b0, bool b1, bool b2, [Range(-13, 25)] int i0, [Range(-28, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, 1, 13);
        int v2 = Hw.ReadOrZero(ti, v1);
        if (Mode != 0) v1 = v1 - 1;
        int v3 = (b2 ? 1 : 0) + (b1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v3 = v3 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        float x4 = f1 * 0.5f;
        return (v1, v2, v3, x4);
    }

    [Handwired]
    public static (bool, int, int, float, float) Differential238(
        bool b0, bool b1, bool b2, [Range(-22, 25)] int i0, [Range(-11, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b1, tb, 0 * 4 + s0);
        int v2 = b2 ? n : 0;
        int v3 = b0 ? -3 : -5;
        float x4 = Math.Max(f1, f0);
        if (b2) v2 = v2 + 1;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        float x5 = b1 ? f1 : f0;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b0 ? 1 : 0);
        }
        return (c1, v2, v3, x4, x5);
    }

    [Handwired]
    public static (bool, bool, int, int) Differential239(
        bool b0, bool b1, bool b2, [Range(-4, 34)] int i0, [Range(10, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b0 && tb[2 * 5 + s0];
        bool c2 = !tb[2 * 5 + s0] || b1;
        int v3 = Math.Clamp(i1, -9, 1);
        v3 = Increment(b0, v3);
        if (Mode != 0) v3 = v3 - 1;
        int v4 = b0 ? 1 : 0;
        return (c1, c2, v3, v4);
    }

    [Handwired]
    public static (bool, int, int, bool, bool) Differential240(
        bool b0, bool b1, bool b2, [Range(-4, 15)] int i0, [Range(-11, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 && tb[1 * 3 + s0];
        int v2 = ti[Math.Clamp(s0, 0, 8)];
        int v3 = b0 ? i1 : i0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c1) v2 = v2 + 1;
        }
        v2 = Increment(b0, v2);
        bool c4 = f1 >= f0;
        bool c5 = b1 ^ b1;
        return (c1, v2, v3, c4, c5);
    }

    [Handwired]
    public static (int, bool, int) Differential241(
        bool b0, bool b1, bool b2, [Range(-32, 7)] int i0, [Range(-11, -10)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(n, s0);
        bool c2 = s0 == 1;
        v1 = Increment(b0, v1);
        int v3 = ti[Math.Clamp(i1 + s0, 0, 7)];
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, bool, bool) Differential242(
        bool b0, bool b1, bool b2, [Range(-22, 26)] int i0, [Range(-19, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 1, 6)];
        bool c2 = b2 && tb[1 * 5 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c2) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c2 ? 1 : 0);
        }
        bool c3 = tb[1 * 5 + s0];
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int, int) Differential243(
        bool b0, bool b1, bool b2, [Range(7, 15)] int i0, [Range(3, 52)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i0 : 0;
        int v2 = ti[Math.Clamp(n, 0, 4)];
        int v3 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b1 ? 1 : 0);
        }
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b0 ? 1 : 0);
        }
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, bool, int) Differential244(
        bool b0, bool b1, bool b2, [Range(-18, 31)] int i0, [Range(-31, 6)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 + (b1 ? 1 : 0);
        if (b0) v1 = v1 + 2;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        bool c2 = b2 || !b1;
        if (Mode != 0) v1 = v1 - 1;
        int v3 = b2 ? i1 : i0;
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, int, float, bool) Differential245(
        bool b0, bool b1, bool b2, [Range(3, 53)] int i0, [Range(-38, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b0 ? 1 : 0) + (b1 ? 1 : 0);
        int v2 = Hw.ReadOrZero(ti, i1 * 1 + 1);
        float x3 = f0 * 3f;
        bool c4 = n < v2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c4 ? 1 : 0);
        }
        return (v1, v2, x3, c4);
    }

    [Handwired]
    public static (int, bool, float, int, int) Differential246(
        bool b0, bool b1, bool b2, [Range(-21, 22)] int i0, [Range(-18, 6)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 * 3;
        bool c2 = b0 && !b0 && tb[2 * 5 + s0];
        float x3 = Math.Max(f1, f1);
        int v4 = Hw.ReadOrZero(ti, s0 * 3 + 1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v4 = v4 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v4 = v4 + 1;
        }
        int v5 = Hw.ReadOrZero(ti, i0);
        return (v1, c2, x3, v4, v5);
    }

    [Handwired]
    public static (int, bool, int, bool) Differential247(
        bool b0, bool b1, bool b2, [Range(-18, 28)] int i0, [Range(-11, 25)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(n, s0);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = b1 && !b0 && tb[1 * 3 + s0];
        int v3 = b2 ? -1 : -3;
        bool c4 = c2 || !b2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (c4 ? 1 : 0);
        }
        if (Mode != 0) v3 = v3 - 1;
        return (v1, c2, v3, c4);
    }

    [Handwired]
    public static (int, bool, float, int) Differential248(
        bool b0, bool b1, bool b2, [Range(-8, 8)] int i0, [Range(-7, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1);
        v1 = Increment(b1, v1);
        bool c2 = StepBit(b2, tb, 2 * 3 + s0);
        float x3 = tf[Math.Clamp(s0, 0, 4)];
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        int v4 = Hw.ReadOrZero(ti, v1 * 3 + -4);
        if (c2) v4 = v4 + 2;
        return (v1, c2, x3, v4);
    }

    [Handwired]
    public static (int, bool, bool, int, float) Differential249(
        bool b0, bool b1, bool b2, [Range(-17, 23)] int i0, [Range(-14, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        bool c2 = b0 || !b2;
        bool c3 = tb[0 * 4 + s0];
        int v4 = ti[Math.Clamp(i1, 1, 7)];
        float x5 = f0 * 3f;
        return (v1, c2, c3, v4, x5);
    }

    [Handwired]
    public static (int, int) Differential250(
        bool b0, bool b1, bool b2, [Range(-2, 24)] int i0, [Range(-22, 27)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? n : i0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        int v2 = Math.Max(n, s0);
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, bool) Differential251(
        bool b0, bool b1, bool b2, [Range(-1, 28)] int i0, [Range(-23, 18)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0 + i0, 0, 6)];
        bool c2 = tb[0 * 5 + s0];
        if (b1) v1 = v1 + 1;
        bool c3 = i1 == -3;
        return (v1, c2, c3);
    }

    [Handwired]
    public static (bool, int, bool, float) Differential252(
        bool b0, bool b1, bool b2, [Range(-2, 14)] int i0, [Range(-35, -32)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && !b1 && tb[0 * 5 + s0];
        int v2 = ti[Math.Clamp(n, 1, 7)];
        bool c3 = v2 == -3;
        float x4 = f0 - f1;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (c1 ? 1 : 0);
        }
        return (c1, v2, c3, x4);
    }

    [Handwired]
    public static (int, int, int) Differential253(
        bool b0, bool b1, bool b2, [Range(-2, 28)] int i0, [Range(-8, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b2 ? 1 : 0);
        int v2 = s0 + v1;
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v2 = v2 + 1;
        }
        int v3 = ti[Math.Clamp(v1, 0, 4)] + (b1 ? 1 : 0);
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, int, bool, bool) Differential254(
        bool b0, bool b1, bool b2, [Range(-24, -18)] int i0, [Range(-36, -15)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0 + i0, 0, 6)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v2 = Hw.ReadOrZero(ti, n * 2 + -2);
        if (Mode != 0) v2 = v2 - 1;
        bool c3 = !tb[3 * 4 + s0] || b1;
        bool c4 = b2 && !c3 && tb[3 * 4 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && c4) v2 = v2 + 1;
        }
        v1 = Increment(c4, v1);
        return (v1, v2, c3, c4);
    }

    [Handwired]
    public static (int, bool, bool, float, bool) Differential255(
        bool b0, bool b1, bool b2, [Range(7, 24)] int i0, [Range(-20, 28)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? -5 : 4;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = b1 && tb[Math.Clamp(i1, 0, 11)];
        bool c3 = b1 && tb[0 * 4 + s0];
        float x4 = f1 * 0.5f;
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        bool c5 = tb[0 * 4 + s0];
        return (v1, c2, c3, x4, c5);
    }
}
