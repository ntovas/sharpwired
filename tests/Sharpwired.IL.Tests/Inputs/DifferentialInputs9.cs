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
    public static (float, int, int, bool, bool) Differential288(
        bool b0, bool b1, bool b2, [Range(-38, -12)] int i0, [Range(-9, 37)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 * 0.5f;
        int v2 = b0 ? i0 : 0;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b0 ? 1 : 0);
        }
        int v3 = Hw.ReadOrZero(ti, i1);
        bool c4 = b1 && tb[Math.Clamp(v3, 0, 14)];
        bool c5 = c4 && b2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v2 = v2 + 1;
        }
        return (x1, v2, v3, c4, c5);
    }

    [Handwired]
    public static (int, bool) Differential289(
        bool b0, bool b1, bool b2, [Range(7, 23)] int i0, [Range(-29, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? n : 0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = b0 ^ b0;
        return (v1, c2);
    }

    [Handwired]
    public static (bool, int, int, int, bool) Differential290(
        bool b0, bool b1, bool b2, [Range(-30, -11)] int i0, [Range(-37, -27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i0 == -1;
        int v2 = ti[Math.Clamp(i1 + n, 0, 6)];
        int v3 = v2 - v2;
        int v4 = ti[Math.Clamp(i0, 0, 6)] + (b0 ? 1 : 0);
        bool c5 = b1 ^ b2;
        return (c1, v2, v3, v4, c5);
    }

    [Handwired]
    public static (int, int) Differential291(
        bool b0, bool b1, bool b2, [Range(-28, -4)] int i0, [Range(8, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(s0, i1);
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = b1 ? 3 : -2;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, v2);
    }

    [Handwired]
    public static (float, int, bool) Differential292(
        bool b0, bool b1, bool b2, [Range(-17, 12)] int i0, [Range(4, 34)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 + (b2 ? 1 : 0);
        int v2 = (b0 ? 1 : 0);
        if (Mode != 0) v2 = v2 - 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v2 = v2 + 1;
        }
        bool c3 = StepBit(b2, tb, 3 * 4 + s0);
        return (x1, v2, c3);
    }

    [Handwired]
    public static (bool, int, bool, bool) Differential293(
        bool b0, bool b1, bool b2, [Range(-27, -4)] int i0, [Range(-30, -17)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i1 < s0;
        int v2 = (b0 ? 1 : 0) + (b1 ? 1 : 0);
        bool c3 = b1 && b1;
        bool c4 = c1 && tb[3 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c3) v2 = v2 + 1;
        }
        return (c1, v2, c3, c4);
    }

    [Handwired]
    public static (float, bool, bool, bool, bool) Differential294(
        bool b0, bool b1, bool b2, [Range(4, 53)] int i0, [Range(-26, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 + (b1 ? 1 : 0);
        bool c2 = b0 || !b1;
        bool c3 = b0 && tb[2 * 3 + s0];
        bool c4 = c3 ^ c3;
        bool c5 = tb[1 * 3 + s0];
        return (x1, c2, c3, c4, c5);
    }

    [Handwired]
    public static (int, bool, bool) Differential295(
        bool b0, bool b1, bool b2, [Range(-33, -9)] int i0, [Range(6, 41)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i0, 0, 6)] + (b1 ? 1 : 0);
        bool c2 = b0 ^ b2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        bool c3 = f0 >= f1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c2) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int, float, bool, int) Differential296(
        bool b0, bool b1, bool b2, [Range(3, 25)] int i0, [Range(-9, 36)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Max(i1, i1);
        int v2 = b1 ? v1 : i1;
        if (b2) v1 = v1 + (b2 ? 1 : 0);
        float x3 = (float)i1 * 0.25f;
        if (Mode != 0) v1 = v1 - 1;
        bool c4 = b1 && !b1 && tb[3 * 5 + s0];
        int v5 = v2 - s0;
        return (v1, v2, x3, c4, v5);
    }

    [Handwired]
    public static (bool, int, int, int, int) Differential297(
        bool b0, bool b1, bool b2, [Range(7, 36)] int i0, [Range(7, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 ^ b0;
        int v2 = Hw.ReadOrZero(ti, n);
        if (b1) v2 = v2 + 2;
        int v3 = c1 ? n : 0;
        int v4 = v2 + n;
        int v5 = Math.Max(v3, i0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v3 = v3 + 1;
        }
        return (c1, v2, v3, v4, v5);
    }

    [Handwired]
    public static (int, int, float) Differential298(
        bool b0, bool b1, bool b2, [Range(-39, -36)] int i0, [Range(-16, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0);
        int v2 = v1 + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        float x3 = f1 * 0.5f;
        return (v1, v2, x3);
    }

    [Handwired]
    public static (bool, int, int) Differential299(
        bool b0, bool b1, bool b2, [Range(-23, -15)] int i0, [Range(0, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b0, tb, 0 * 5 + s0);
        int v2 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c1) v2 = v2 + 1;
        }
        v2 = Increment(b0, v2);
        int v3 = v2 * -2;
        return (c1, v2, v3);
    }

    [Handwired]
    public static (int, float) Differential300(
        bool b0, bool b1, bool b2, [Range(-39, -16)] int i0, [Range(-21, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i0 * 3 + -5);
        if (b2) v1 = v1 + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        v1 = Increment(b0, v1);
        float x2 = f0 - f0;
        return (v1, x2);
    }

    [Handwired]
    public static (int, int, bool) Differential301(
        bool b0, bool b1, bool b2, [Range(-38, -12)] int i0, [Range(-38, -26)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 - i1;
        if (b2) v1 = v1 + 1;
        if (b0) v1 = v1 + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        int v2 = ti[Math.Clamp(i0 + i0, 0, 5)];
        bool c3 = tb[1 * 3 + s0];
        if (c3) v2 = v2 + 2;
        return (v1, v2, c3);
    }

    [Handwired]
    public static (int, int, float, int) Differential302(
        bool b0, bool b1, bool b2, [Range(-38, -16)] int i0, [Range(-17, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0);
        int v2 = b2 ? v1 : v1;
        float x3 = b2 ? f0 : f1;
        int v4 = n + v2;
        return (v1, v2, x3, v4);
    }

    [Handwired]
    public static (int, float, float, int) Differential303(
        bool b0, bool b1, bool b2, [Range(-4, 45)] int i0, [Range(-28, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? -5 : -3;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        float x2 = f0 + (b1 ? 1 : 0);
        float x3 = f1 + (b0 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        int v4 = i0 * 2;
        return (v1, x2, x3, v4);
    }

    [Handwired]
    public static (int, int) Differential304(
        bool b0, bool b1, bool b2, [Range(-22, 15)] int i0, [Range(-31, -5)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 - i0;
        if (b1) v1 = v1 + 1;
        if (b2) v1 = v1 + 2;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = Hw.ReadOrZero(ti, n);
        return (v1, v2);
    }

    [Handwired]
    public static (int, int) Differential305(
        bool b0, bool b1, bool b2, [Range(-9, -5)] int i0, [Range(-25, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 1, 6)];
        if (Mode != 0) v1 = v1 - 1;
        int v2 = i1 + (b2 ? 1 : 0);
        v2 = Increment(b1, v2);
        return (v1, v2);
    }

    [Handwired]
    public static (int, int) Differential306(
        bool b0, bool b1, bool b2, [Range(-3, 26)] int i0, [Range(-36, -4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1);
        int v2 = Hw.ReadOrZero(ti, i1);
        if (b0) v2 = v2 + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, int) Differential307(
        bool b0, bool b1, bool b2, [Range(-21, -8)] int i0, [Range(-12, 15)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + s0, 0, 5)];
        bool c2 = s0 == -3;
        if (c2) v1 = v1 + 2;
        int v3 = i0 + (b0 ? 1 : 0);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, int, float, float, bool) Differential308(
        bool b0, bool b1, bool b2, [Range(-7, 14)] int i0, [Range(-9, 34)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? i0 : 0;
        int v2 = b0 ? i1 : 0;
        float x3 = b1 ? f0 : f1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        if (b1) v2 = v2 + 2;
        float x4 = x3 * 0.5f;
        bool c5 = b0 && b0;
        return (v1, v2, x3, x4, c5);
    }

    [Handwired]
    public static (int, float) Differential309(
        bool b0, bool b1, bool b2, [Range(-2, 23)] int i0, [Range(1, 31)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 7)] + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        float x2 = Math.Min(f0, f0);
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        return (v1, x2);
    }

    [Handwired]
    public static (int, int, int) Differential310(
        bool b0, bool b1, bool b2, [Range(-4, 30)] int i0, [Range(-19, -6)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i0 : s0;
        int v2 = b1 ? n : 0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v2 = v2 + 1;
        }
        int v3 = Math.Max(i0, v1);
        return (v1, v2, v3);
    }

    [Handwired]
    public static (float, int, int, bool) Differential311(
        bool b0, bool b1, bool b2, [Range(-30, 11)] int i0, [Range(-33, -15)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 * 3f;
        int v2 = n + (b1 ? 1 : 0);
        int v3 = (b0 ? 1 : 0);
        bool c4 = b1 && tb[Math.Clamp(n, 0, 11)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v3 = v3 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c4) v2 = v2 + 1;
        }
        if (b2) v3 = v3 + 1;
        if (Mode != 0) v2 = v2 - 1;
        return (x1, v2, v3, c4);
    }

    [Handwired]
    public static (int, float, float, float) Differential312(
        bool b0, bool b1, bool b2, [Range(-28, 20)] int i0, [Range(-32, -5)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i1 : 0;
        if (b0) v1 = v1 + 1;
        float x2 = f0 + f0;
        float x3 = Hw.ReadOrZero(tf, i1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        float x4 = f0 + (b2 ? 1 : 0);
        return (v1, x2, x3, x4);
    }

    [Handwired]
    public static (int, int) Differential313(
        bool b0, bool b1, bool b2, [Range(1, 10)] int i0, [Range(-7, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i0 : s0;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        v1 = Increment(b1, v1);
        int v2 = (b2 ? 1 : 0) + (b1 ? 1 : 0);
        return (v1, v2);
    }

    [Handwired]
    public static (int, int, int, bool) Differential314(
        bool b0, bool b1, bool b2, [Range(-32, -15)] int i0, [Range(-37, 0)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? 2 : 3;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        int v2 = b2 ? 0 : 5;
        int v3 = ti[Math.Clamp(v1, 1, 5)];
        bool c4 = tb[1 * 3 + s0];
        v1 = Increment(b1, v1);
        return (v1, v2, v3, c4);
    }

    [Handwired]
    public static (int, int, int, int, bool, int) Differential315(
        bool b0, bool b1, bool b2, [Range(-27, -6)] int i0, [Range(0, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i1 : i1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        int v2 = i0 + v1;
        int v3 = b1 ? v2 : n;
        int v4 = Math.Min(i0, v1);
        bool c5 = b2 || !b1;
        int v6 = b2 ? i1 : v1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v2 = v2 + 1;
        }
        return (v1, v2, v3, v4, c5, v6);
    }

    [Handwired]
    public static (int, float, bool) Differential316(
        bool b0, bool b1, bool b2, [Range(-10, 25)] int i0, [Range(-11, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? n : 0;
        float x2 = tf[Math.Clamp(v1, 0, 5)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c3 = b2 || !b1;
        if (b1) v1 = v1 + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        v1 = Increment(b0, v1);
        return (v1, x2, c3);
    }

    [Handwired]
    public static (int, float, int, bool, int) Differential317(
        bool b0, bool b1, bool b2, [Range(-38, 9)] int i0, [Range(-38, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0);
        float x2 = Math.Max(f0, f0);
        int v3 = (b1 ? 1 : 0);
        if (Mode != 0) v3 = v3 - 1;
        bool c4 = b0 && tb[0 * 3 + s0];
        int v5 = b2 ? n : 0;
        return (v1, x2, v3, c4, v5);
    }

    [Handwired]
    public static (int, bool, int, int, float) Differential318(
        bool b0, bool b1, bool b2, [Range(-23, 4)] int i0, [Range(-22, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 * -2;
        bool c2 = StepBit(b0, tb, 0 * 4 + s0);
        int v3 = ti[Math.Clamp(s0, 1, 5)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        if (b0) v3 = v3 + 2;
        int v4 = Math.Clamp(v1, -1, 5);
        if (c2) v3 = v3 + 1;
        float x5 = f0 - f0;
        return (v1, c2, v3, v4, x5);
    }

    [Handwired]
    public static (float, int, bool, int) Differential319(
        bool b0, bool b1, bool b2, [Range(-27, -9)] int i0, [Range(-20, 0)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 + (b0 ? 1f : 0f);
        int v2 = Math.Max(i1, i1);
        if (b2) v2 = v2 + 2;
        bool c3 = b2 ^ b2;
        if (Mode != 0) v2 = v2 - 1;
        if (Mode != 0) v2 = v2 - 1;
        int v4 = (b0 ? 1 : 0) + (b1 ? 1 : 0);
        if (b2) v4 = v4 + 2;
        return (x1, v2, c3, v4);
    }
}
