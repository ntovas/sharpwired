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
    public static (int, bool, int, int, int) Differential064(
        bool b0, bool b1, bool b2, [Range(-28, -6)] int i0, [Range(-20, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + n, 0, 4)];
        bool c2 = b2 && !b2 && tb[0 * 5 + s0];
        v1 = Increment(b0, v1);
        int v3 = v1 * 3;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v3 = v3 + 1;
        }
        int v4 = Math.Clamp(v1, -8, 10);
        int v5 = ti[Math.Clamp(v3, 0, 4)];
        return (v1, c2, v3, v4, v5);
    }

    [Handwired]
    public static (int, int, int) Differential065(
        bool b0, bool b1, bool b2, [Range(-14, 1)] int i0, [Range(-34, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 6)];
        int v2 = ti[Math.Clamp(v1, 0, 6)];
        int v3 = Math.Clamp(i0, -8, -3);
        if (b1) v3 = v3 + 2;
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, float, int, int) Differential066(
        bool b0, bool b1, bool b2, [Range(-33, 15)] int i0, [Range(-17, 24)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n - i0;
        if (b2) v1 = v1 + 2;
        float x2 = tf[Math.Clamp(i0, 0, 7)];
        if (b0) v1 = v1 + (b0 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        int v3 = ti[Math.Clamp(i0, 1, 6)];
        int v4 = s0 + v1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v3 = v3 + 1;
        }
        return (v1, x2, v3, v4);
    }

    [Handwired]
    public static (bool, float, int, float) Differential067(
        bool b0, bool b1, bool b2, [Range(-24, -7)] int i0, [Range(-26, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && !b2 && tb[2 * 4 + s0];
        float x2 = f0 + f0;
        int v3 = s0 * -2;
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (c1 ? 1 : 0);
        }
        if (Mode != 0) v3 = v3 - 1;
        if (b2) v3 = v3 + 1;
        float x4 = f0 + (b0 ? 1 : 0);
        return (c1, x2, v3, x4);
    }

    [Handwired]
    public static (int, bool) Differential068(
        bool b0, bool b1, bool b2, [Range(-3, 44)] int i0, [Range(-14, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n);
        if (b2) v1 = v1 + 2;
        bool c2 = b0 && b1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (bool, int, bool) Differential069(
        bool b0, bool b1, bool b2, [Range(-38, -32)] int i0, [Range(-13, -5)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[2 * 5 + s0] || b0;
        int v2 = ti[Math.Clamp(n, 0, 8)] + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        bool c3 = i1 == -1;
        return (c1, v2, c3);
    }

    [Handwired]
    public static (int, int, int) Differential070(
        bool b0, bool b1, bool b2, [Range(-10, -7)] int i0, [Range(-14, 31)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? i0 : n;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        int v2 = b1 ? 1 : -4;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        int v3 = b2 ? -5 : -2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        if (Mode != 0) v3 = v3 - 1;
        return (v1, v2, v3);
    }

    [Handwired]
    public static (int, bool, float) Differential071(
        bool b0, bool b1, bool b2, [Range(-3, 23)] int i0, [Range(2, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = b2 ^ b1;
        float x3 = Math.Max(f1, f1);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        return (v1, c2, x3);
    }

    [Handwired]
    public static (bool, int) Differential072(
        bool b0, bool b1, bool b2, [Range(-23, -15)] int i0, [Range(-11, 13)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 && tb[Math.Clamp(s0, 0, 11)];
        int v2 = n + s0;
        if (Mode != 0) v2 = v2 - 1;
        if (Mode != 0) v2 = v2 - 1;
        return (c1, v2);
    }

    [Handwired]
    public static (int, float) Differential073(
        bool b0, bool b1, bool b2, [Range(-22, -20)] int i0, [Range(-36, -25)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? n : n;
        v1 = Increment(b0, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        if (b1) v1 = v1 + 1;
        if (b0) v1 = v1 + 2;
        float x2 = Hw.ReadOrZero(tf, v1);
        if (Mode != 0) v1 = v1 - 1;
        return (v1, x2);
    }

    [Handwired]
    public static (bool, int, bool) Differential074(
        bool b0, bool b1, bool b2, [Range(-15, 15)] int i0, [Range(-9, -8)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 ^ b1;
        int v2 = (b1 ? 1 : 0) + (c1 ? 1 : 0);
        bool c3 = tb[3 * 4 + s0];
        v2 = Increment(b1, v2);
        return (c1, v2, c3);
    }

    [Handwired]
    public static (int, bool, float) Differential075(
        bool b0, bool b1, bool b2, [Range(-20, 18)] int i0, [Range(3, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(s0, 4, 8);
        bool c2 = b2 && tb[2 * 5 + s0];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c2) v1 = v1 + 1;
        }
        float x3 = f1 + (b0 ? 1f : 0f);
        v1 = Increment(b0, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        v1 = Increment(b2, v1);
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, bool) Differential076(
        bool b0, bool b1, bool b2, [Range(4, 12)] int i0, [Range(-34, 1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Max(n, i0);
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        bool c2 = b2 && !b2 && tb[2 * 4 + s0];
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool, float) Differential077(
        bool b0, bool b1, bool b2, [Range(-20, 1)] int i0, [Range(-18, -2)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, s0);
        bool c2 = b0 || !b1;
        float x3 = b2 ? f1 : 0.75f;
        if (Mode != 0) v1 = v1 - 1;
        if (c2) v1 = v1 + 2;
        if (Mode != 0) v1 = v1 - 1;
        return (v1, c2, x3);
    }

    [Handwired]
    public static (float, int, float, bool) Differential078(
        bool b0, bool b1, bool b2, [Range(-17, 15)] int i0, [Range(-15, 18)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 * 3f;
        int v2 = Hw.ReadOrZero(ti, n);
        float x3 = tf[Math.Clamp(v2, 0, 7)];
        bool c4 = b0 && !b0 && tb[0 * 3 + s0];
        if (c4) v2 = v2 + (b2 ? 1 : 0);
        return (x1, v2, x3, c4);
    }

    [Handwired]
    public static (int, bool, bool, bool, bool) Differential079(
        bool b0, bool b1, bool b2, [Range(-7, 36)] int i0, [Range(-17, 31)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i0 + i1, 0, 7)];
        bool c2 = n == 3;
        bool c3 = b2 && !b1 && tb[3 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        if (b1) v1 = v1 + 2;
        bool c4 = c3 && tb[Math.Clamp(v1, 0, 11)];
        bool c5 = c3 && !b2 && tb[0 * 3 + s0];
        return (v1, c2, c3, c4, c5);
    }

    [Handwired]
    public static (int, float, bool) Differential080(
        bool b0, bool b1, bool b2, [Range(-29, -13)] int i0, [Range(-25, -14)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 * -2;
        float x2 = tf[Math.Clamp(i0, 0, 5)];
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (b2) v1 = v1 + (b1 ? 1 : 0);
        bool c3 = b2 && !b2 && tb[0 * 4 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        return (v1, x2, c3);
    }

    [Handwired]
    public static (bool, int, int, int) Differential081(
        bool b0, bool b1, bool b2, [Range(-23, -16)] int i0, [Range(-27, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && b0;
        int v2 = (c1 ? 1 : 0) + (b1 ? 1 : 0);
        if (Mode != 0) v2 = v2 - 1;
        int v3 = Hw.ReadOrZero(ti, s0);
        v3 = Increment(b1, v3);
        v2 = Increment(c1, v2);
        int v4 = v3 * 3;
        return (c1, v2, v3, v4);
    }

    [Handwired]
    public static (int, bool, int, bool) Differential082(
        bool b0, bool b1, bool b2, [Range(-19, -16)] int i0, [Range(-31, -1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1 * 2 + -3);
        bool c2 = f1 >= f0;
        if (b0) v1 = v1 + 2;
        v1 = Increment(b2, v1);
        v1 = Increment(b1, v1);
        if (b1) v1 = v1 + 1;
        int v3 = b2 ? i1 : 0;
        bool c4 = n == 2;
        return (v1, c2, v3, c4);
    }

    [Handwired]
    public static (int, int, bool, bool) Differential083(
        bool b0, bool b1, bool b2, [Range(-28, 10)] int i0, [Range(-25, -23)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + s0, 0, 6)];
        v1 = Increment(b0, v1);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v2 = s0 - s0;
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = i0 == 1;
        bool c4 = tb[1 * 4 + s0];
        if (c3) v2 = v2 + (b0 ? 1 : 0);
        return (v1, v2, c3, c4);
    }

    [Handwired]
    public static (bool, int, float, float, bool) Differential084(
        bool b0, bool b1, bool b2, [Range(-17, -9)] int i0, [Range(-1, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i0 < s0;
        int v2 = Math.Min(i1, n);
        float x3 = f0 + (b1 ? 1 : 0);
        float x4 = f0 + (c1 ? 1 : 0);
        bool c5 = tb[0 * 4 + s0];
        v2 = Increment(b0, v2);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        if (Mode != 0) v2 = v2 - 1;
        return (c1, v2, x3, x4, c5);
    }

    [Handwired]
    public static (bool, bool, int, float) Differential085(
        bool b0, bool b1, bool b2, [Range(-30, 0)] int i0, [Range(-23, -17)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && !b2 && tb[0 * 3 + s0];
        bool c2 = b0 && c1;
        int v3 = i0 - n;
        float x4 = f0 + (c1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v3 = v3 + 1;
        }
        if (Mode != 0) v3 = v3 - 1;
        return (c1, c2, v3, x4);
    }

    [Handwired]
    public static (int, float, int, bool) Differential086(
        bool b0, bool b1, bool b2, [Range(1, 5)] int i0, [Range(-11, 27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b0 ? 1 : 0);
        v1 = Increment(b1, v1);
        float x2 = f0 + (b1 ? 1 : 0);
        v1 = Increment(b0, v1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        int v3 = ti[Math.Clamp(v1, 1, 5)];
        bool c4 = b2 && tb[0 * 3 + s0];
        v3 = Increment(b2, v3);
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (int, bool) Differential087(
        bool b0, bool b1, bool b2, [Range(3, 30)] int i0, [Range(9, 30)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + (b1 ? 1 : 0);
        bool c2 = b2 && tb[Math.Clamp(i1, 0, 11)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        if (c2) v1 = v1 + 2;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool) Differential088(
        bool b0, bool b1, bool b2, [Range(-13, 33)] int i0, [Range(-14, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0 + n, 0, 8)];
        v1 = Increment(b2, v1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        bool c2 = b1 && tb[2 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c2) v1 = v1 + 1;
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool, int, int) Differential089(
        bool b0, bool b1, bool b2, [Range(4, 25)] int i0, [Range(-28, -18)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? 4 : 2;
        v1 = Increment(b0, v1);
        bool c2 = StepBit(b0, tb, 0 * 3 + s0);
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        int v3 = i1 + (c2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        int v4 = c2 ? 1 : -4;
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, int) Differential090(
        bool b0, bool b1, bool b2, [Range(-10, -8)] int i0, [Range(-11, 34)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b2 ? 1 : 0);
        v1 = Increment(b1, v1);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        int v2 = i0 + n;
        return (v1, v2);
    }

    [Handwired]
    public static (int, int, float) Differential091(
        bool b0, bool b1, bool b2, [Range(-18, 17)] int i0, [Range(-5, 31)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + i1;
        int v2 = ti[Math.Clamp(n, 0, 7)] + (b1 ? 1 : 0);
        if (b2) v2 = v2 + (b1 ? 1 : 0);
        v1 = Increment(b1, v1);
        v1 = Increment(b2, v1);
        float x3 = Math.Max(f1, f1);
        v1 = Increment(b2, v1);
        return (v1, v2, x3);
    }

    [Handwired]
    public static (int, float) Differential092(
        bool b0, bool b1, bool b2, [Range(-24, -8)] int i0, [Range(-40, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + n;
        if (b1) v1 = v1 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        v1 = Increment(b0, v1);
        float x2 = f1 * 0.5f;
        v1 = Increment(b1, v1);
        return (v1, x2);
    }

    [Handwired]
    public static (int, bool) Differential093(
        bool b0, bool b1, bool b2, [Range(-7, 2)] int i0, [Range(-2, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1);
        v1 = Increment(b2, v1);
        if (Mode != 0) v1 = v1 - 1;
        bool c2 = s0 < i1;
        return (v1, c2);
    }

    [Handwired]
    public static (int, int) Differential094(
        bool b0, bool b1, bool b2, [Range(-30, -14)] int i0, [Range(-35, -28)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n);
        if (b0) v1 = v1 + 2;
        if (b0) v1 = v1 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        int v2 = Hw.ReadOrZero(ti, s0);
        return (v1, v2);
    }

    [Handwired]
    public static (int, int, int) Differential095(
        bool b0, bool b1, bool b2, [Range(-2, 12)] int i0, [Range(-4, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 1 + 2);
        int v2 = ti[Math.Clamp(v1 + n, 0, 5)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v2 = v2 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        int v3 = Math.Clamp(v2, -5, 8);
        return (v1, v2, v3);
    }
}
