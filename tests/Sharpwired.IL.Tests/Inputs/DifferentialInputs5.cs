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
    public static (int, bool, bool) Differential160(
        bool b0, bool b1, bool b2, [Range(-10, 16)] int i0, [Range(6, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 5)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c2 = f1 >= f0;
        bool c3 = b1 && tb[3 * 4 + s0];
        return (v1, c2, c3);
    }

    [Handwired]
    public static (bool, int, float, int, bool) Differential161(
        bool b0, bool b1, bool b2, [Range(-36, -24)] int i0, [Range(-39, -11)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = tb[3 * 3 + s0];
        int v2 = Hw.ReadOrZero(ti, n);
        float x3 = f0 + f1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v2 = v2 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v2 = v2 + 1;
        }
        int v4 = (b1 ? 1 : 0);
        if (b0) v4 = v4 + 2;
        bool c5 = b2 || !b0;
        return (c1, v2, x3, v4, c5);
    }

    [Handwired]
    public static (int, bool, bool, bool) Differential162(
        bool b0, bool b1, bool b2, [Range(-34, -19)] int i0, [Range(10, 52)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 5)] + (b2 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = f1 >= f0;
        if (Mode != 0) v1 = v1 - 1;
        if (b2) v1 = v1 + 1;
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = c2 && tb[0 * 3 + s0];
        bool c4 = b0 ^ b0;
        return (v1, c2, c3, c4);
    }

    [Handwired]
    public static (int, bool, bool, bool) Differential163(
        bool b0, bool b1, bool b2, [Range(-28, 13)] int i0, [Range(-39, -28)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? s0 : 0;
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = b0 && tb[Math.Clamp(s0, 0, 8)];
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = b2 && tb[0 * 3 + s0];
        bool c4 = c2 && !c3 && tb[1 * 3 + s0];
        return (v1, c2, c3, c4);
    }

    [Handwired]
    public static (int, bool) Differential164(
        bool b0, bool b1, bool b2, [Range(-39, -34)] int i0, [Range(-32, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, -5, 12);
        if (b1) v1 = v1 + 1;
        v1 = Increment(b0, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = b0 || !b0;
        if (b1) v1 = v1 + 2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool, int, float, int) Differential165(
        bool b0, bool b1, bool b2, [Range(-6, 29)] int i0, [Range(-35, 0)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? s0 : 0;
        bool c2 = b0 && tb[0 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        int v3 = b2 ? n : s0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        float x4 = (float)i0 * 0.25f;
        int v5 = b0 ? i0 : 0;
        return (v1, c2, v3, x4, v5);
    }

    [Handwired]
    public static (int, int, float) Differential166(
        bool b0, bool b1, bool b2, [Range(-6, 2)] int i0, [Range(-17, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? n : 0;
        int v2 = ti[Math.Clamp(i1, 1, 7)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        float x3 = f0 * 3f;
        v1 = Increment(b2, v1);
        return (v1, v2, x3);
    }

    [Handwired]
    public static (int, bool, bool, bool, int) Differential167(
        bool b0, bool b1, bool b2, [Range(-34, -31)] int i0, [Range(-1, 41)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + (b1 ? 1 : 0);
        bool c2 = f1 >= f0;
        bool c3 = b2 && !c2 && tb[2 * 3 + s0];
        bool c4 = b0 && tb[Math.Clamp(i1, 0, 8)];
        int v5 = ti[Math.Clamp(i1, 0, 5)] + (c4 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v5 = v5 + 1;
        }
        return (v1, c2, c3, c4, v5);
    }

    [Handwired]
    public static (int, bool, int, int) Differential168(
        bool b0, bool b1, bool b2, [Range(7, 36)] int i0, [Range(-19, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n * -2;
        bool c2 = b1 && b1;
        int v3 = b1 ? n : n;
        if (c2) v3 = v3 + 2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v3 = v3 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        int v4 = Hw.ReadOrZero(ti, v1 * 3 + 3);
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, int) Differential169(
        bool b0, bool b1, bool b2, [Range(-19, 31)] int i0, [Range(-17, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 3;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        int v2 = i1 * 3;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        return (v1, v2);
    }

    [Handwired]
    public static (bool, int, int, bool) Differential170(
        bool b0, bool b1, bool b2, [Range(-21, -7)] int i0, [Range(-5, -4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[2 * 4 + s0] || b2;
        int v2 = Hw.ReadOrZero(ti, s0);
        v2 = Increment(b0, v2);
        int v3 = Math.Min(i0, i0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v3 = v3 + 1;
        }
        v2 = Increment(b2, v2);
        bool c4 = b1 && tb[Math.Clamp(s0, 0, 11)];
        return (c1, v2, v3, c4);
    }

    [Handwired]
    public static (int, bool, int, bool) Differential171(
        bool b0, bool b1, bool b2, [Range(-19, 18)] int i0, [Range(-34, -29)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? s0 : 0;
        v1 = Increment(b0, v1);
        bool c2 = b1 && !b0 && tb[1 * 4 + s0];
        int v3 = i1 * 3;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v3 = v3 + 1;
        }
        bool c4 = i1 == -1;
        v3 = Increment(b0, v3);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v3 = v3 + 1;
        }
        return (v1, c2, v3, c4);
    }

    [Handwired]
    public static (int, bool, float, int, int, bool) Differential172(
        bool b0, bool b1, bool b2, [Range(-20, 28)] int i0, [Range(-37, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b0 ? 1 : 0);
        bool c2 = b2 && tb[Math.Clamp(v1, 0, 11)];
        float x3 = Math.Max(f1, f0);
        if (Mode != 0) v1 = v1 - 1;
        int v4 = b2 ? i0 : 0;
        int v5 = ti[Math.Clamp(s0, 1, 6)];
        bool c6 = StepBit(c2, tb, 2 * 3 + s0);
        return (v1, c2, x3, v4, v5, c6);
    }

    [Handwired]
    public static (int, float, int, bool, int) Differential173(
        bool b0, bool b1, bool b2, [Range(-35, -29)] int i0, [Range(-15, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 0, 7)] + (b2 ? 1 : 0);
        float x2 = b1 ? f0 : f1;
        int v3 = b2 ? v1 : 0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        bool c4 = b2 || !b0;
        v3 = Increment(c4, v3);
        int v5 = s0 + s0;
        return (v1, x2, v3, c4, v5);
    }

    [Handwired]
    public static (int, int, int, int) Differential174(
        bool b0, bool b1, bool b2, [Range(-33, -10)] int i0, [Range(-30, 5)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(i0, -7, -3);
        int v2 = b1 ? n : s0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        v1 = Increment(b0, v1);
        if (b0) v1 = v1 + 1;
        int v3 = b1 ? 0 : 4;
        int v4 = Hw.ReadOrZero(ti, n * 2 + 0);
        return (v1, v2, v3, v4);
    }

    [Handwired]
    public static (int, int, int, bool) Differential175(
        bool b0, bool b1, bool b2, [Range(-4, 10)] int i0, [Range(-24, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + i0, 0, 6)];
        int v2 = Hw.ReadOrZero(ti, n * 1 + 5);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        v1 = Increment(b1, v1);
        int v3 = b1 ? -4 : 5;
        bool c4 = i0 < v1;
        if (b1) v3 = v3 + 1;
        return (v1, v2, v3, c4);
    }

    [Handwired]
    public static (int, bool, bool) Differential176(
        bool b0, bool b1, bool b2, [Range(7, 49)] int i0, [Range(-7, 38)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? i0 : i1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        bool c2 = b1 && !b1 && tb[0 * 5 + s0];
        bool c3 = b1 && tb[2 * 5 + s0];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int, bool, int) Differential177(
        bool b0, bool b1, bool b2, [Range(-14, -2)] int i0, [Range(-38, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Max(s0, n);
        if (b0) v1 = v1 + 2;
        int v2 = s0 - n;
        bool c3 = b0 && !b1 && tb[1 * 5 + s0];
        if (Mode != 0) v2 = v2 - 1;
        v2 = Increment(b0, v2);
        int v4 = Hw.ReadOrZero(ti, i0);
        if (Mode != 0) v1 = v1 - 1;
        return (v1, v2, c3, v4);
    }

    [Handwired]
    public static (float, int, int, bool) Differential178(
        bool b0, bool b1, bool b2, [Range(-34, 12)] int i0, [Range(-40, -22)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = (float)s0 * 0.25f;
        int v2 = ti[Math.Clamp(n, 1, 3)];
        if (Mode != 0) v2 = v2 - 1;
        int v3 = Hw.ReadOrZero(ti, i0 * 3 + -3);
        if (Mode != 0) v3 = v3 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v3 = v3 + 1;
        }
        v2 = Increment(b0, v2);
        bool c4 = v3 == -2;
        return (x1, v2, v3, c4);
    }

    [Handwired]
    public static (int, int) Differential179(
        bool b0, bool b1, bool b2, [Range(-20, 8)] int i0, [Range(-38, -19)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 3;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        v1 = Increment(b1, v1);
        if (b1) v1 = v1 + 1;
        v1 = Increment(b2, v1);
        int v2 = Math.Clamp(s0, -2, 16);
        return (v1, v2);
    }

    [Handwired]
    public static (bool, bool, bool, bool) Differential180(
        bool b0, bool b1, bool b2, [Range(-14, -4)] int i0, [Range(-27, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i0 == 3;
        bool c2 = !tb[0 * 3 + s0] || b1;
        bool c3 = b0 ^ b0;
        bool c4 = b2 && tb[0 * 3 + s0];
        return (c1, c2, c3, c4);
    }

    [Handwired]
    public static (float, int, int, bool) Differential181(
        bool b0, bool b1, bool b2, [Range(-24, -5)] int i0, [Range(-32, -7)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 + (b1 ? 1f : 0f);
        int v2 = ti[Math.Clamp(s0 + s0, 0, 6)];
        int v3 = ti[Math.Clamp(s0, 1, 5)];
        bool c4 = !tb[2 * 4 + s0] || b0;
        return (x1, v2, v3, c4);
    }

    [Handwired]
    public static (bool, float, bool, int, bool) Differential182(
        bool b0, bool b1, bool b2, [Range(-40, 0)] int i0, [Range(-37, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = f0 >= f1;
        float x2 = Math.Max(f1, f1);
        bool c3 = !tb[3 * 3 + s0] || b0;
        int v4 = c3 ? 3 : 4;
        bool c5 = b0 && b0;
        return (c1, x2, c3, v4, c5);
    }

    [Handwired]
    public static (int, float, int) Differential183(
        bool b0, bool b1, bool b2, [Range(-24, 24)] int i0, [Range(-16, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 1 + 2);
        float x2 = b1 ? f0 : 0.75f;
        if (b0) v1 = v1 + 2;
        int v3 = i1 * -2;
        if (b1) v1 = v1 + 2;
        return (v1, x2, v3);
    }

    [Handwired]
    public static (int, int) Differential184(
        bool b0, bool b1, bool b2, [Range(-36, -24)] int i0, [Range(-15, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(n, -4, 16);
        if (b0) v1 = v1 + 1;
        if (b1) v1 = v1 + 2;
        v1 = Increment(b0, v1);
        if (Mode != 0) v1 = v1 - 1;
        int v2 = ti[Math.Clamp(i0 + i0, 0, 4)];
        return (v1, v2);
    }

    [Handwired]
    public static (int, int) Differential185(
        bool b0, bool b1, bool b2, [Range(3, 43)] int i0, [Range(-24, -11)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, 0, 17);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        int v2 = i0 + (b1 ? 1 : 0);
        v2 = Increment(b1, v2);
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, bool, bool, int, int) Differential186(
        bool b0, bool b1, bool b2, [Range(-17, 19)] int i0, [Range(-39, -21)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b0 ? 1 : 0);
        bool c2 = f1 >= f0;
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = b0 && tb[0 * 3 + s0];
        bool c4 = b0 && tb[Math.Clamp(i1, 0, 11)];
        int v5 = b1 ? -3 : 1;
        int v6 = Hw.ReadOrZero(ti, n * 1 + 4);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v5 = v5 + 1;
        }
        return (v1, c2, c3, c4, v5, v6);
    }

    [Handwired]
    public static (int, bool, bool, bool, float) Differential187(
        bool b0, bool b1, bool b2, [Range(5, 24)] int i0, [Range(-33, -4)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 - n;
        bool c2 = v1 < n;
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = c2 && b0;
        bool c4 = StepBit(b2, tb, 2 * 3 + s0);
        v1 = Increment(b2, v1);
        float x5 = f0 + (c2 ? 1 : 0);
        return (v1, c2, c3, c4, x5);
    }

    [Handwired]
    public static (int, bool, float, int, float) Differential188(
        bool b0, bool b1, bool b2, [Range(-27, -10)] int i0, [Range(-5, 13)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 2;
        bool c2 = tb[3 * 5 + s0];
        float x3 = f1 + (b2 ? 1 : 0);
        int v4 = i1 + s0;
        float x5 = b0 ? x3 : 0.75f;
        v1 = Increment(b0, v1);
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, x3, v4, x5);
    }

    [Handwired]
    public static (bool, int, bool, bool) Differential189(
        bool b0, bool b1, bool b2, [Range(-7, 15)] int i0, [Range(-4, 35)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = n < i1;
        int v2 = Hw.ReadOrZero(ti, n * 2 + 2);
        if (c1) v2 = v2 + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        v2 = Increment(b1, v2);
        bool c3 = StepBit(c1, tb, 1 * 5 + s0);
        bool c4 = c1 ^ b2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v2 = v2 + 1;
        }
        return (c1, v2, c3, c4);
    }

    [Handwired]
    public static (bool, int) Differential190(
        bool b0, bool b1, bool b2, [Range(7, 52)] int i0, [Range(-33, -31)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = tb[0 * 5 + s0];
        int v2 = ti[Math.Clamp(s0, 0, 7)] + (b2 ? 1 : 0);
        if (b2) v2 = v2 + 2;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b1 ? 1 : 0);
        }
        return (c1, v2);
    }

    [Handwired]
    public static (float, int, int, bool) Differential191(
        bool b0, bool b1, bool b2, [Range(-25, 20)] int i0, [Range(-15, 19)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 + f0;
        int v2 = i1 * 3;
        int v3 = v2 + (b2 ? 1 : 0);
        bool c4 = b0 || !b1;
        return (x1, v2, v3, c4);
    }
}
