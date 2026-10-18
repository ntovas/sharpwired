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
    public static (int, int, int, int) Differential192(
        bool b0, bool b1, bool b2, [Range(-29, 4)] int i0, [Range(-11, 1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? s0 : n;
        int v2 = Hw.ReadOrZero(ti, i1 * 2 + 3);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v2 = v2 + 1;
        }
        int v3 = b1 ? -4 : 0;
        int v4 = ti[Math.Clamp(n, 0, 7)];
        return (v1, v2, v3, v4);
    }

    [Handwired]
    public static (int, int) Differential193(
        bool b0, bool b1, bool b2, [Range(-9, 28)] int i0, [Range(-28, -8)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n - i0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (b1) v1 = v1 + (b2 ? 1 : 0);
        int v2 = b0 ? -2 : 5;
        return (v1, v2);
    }

    [Handwired]
    public static (bool, int, float) Differential194(
        bool b0, bool b1, bool b2, [Range(-25, 23)] int i0, [Range(-34, 13)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b1 && b1;
        int v2 = (c1 ? 1 : 0);
        v2 = Increment(b0, v2);
        float x3 = f1 * 3f;
        return (c1, v2, x3);
    }

    [Handwired]
    public static (int, float, int, bool) Differential195(
        bool b0, bool b1, bool b2, [Range(-28, -7)] int i0, [Range(-16, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b1 ? 1 : 0);
        float x2 = f1 * 3f;
        int v3 = Hw.ReadOrZero(ti, s0 * 3 + -1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        if (b2) v1 = v1 + 1;
        if (b2) v1 = v1 + (b0 ? 1 : 0);
        bool c4 = StepBit(b0, tb, 2 * 4 + s0);
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (int, float) Differential196(
        bool b0, bool b1, bool b2, [Range(-24, -19)] int i0, [Range(-31, 2)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 5)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        float x2 = f1 + f0;
        return (v1, x2);
    }

    [Handwired]
    public static (int, int, float) Differential197(
        bool b0, bool b1, bool b2, [Range(7, 28)] int i0, [Range(-14, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 + (b0 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        int v2 = ti[Math.Clamp(i0 + s0, 0, 4)];
        float x3 = f0 * 3f;
        return (v1, v2, x3);
    }

    [Handwired]
    public static (bool, int, int, int) Differential198(
        bool b0, bool b1, bool b2, [Range(-40, -31)] int i0, [Range(-11, -6)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 || !b0;
        int v2 = n - s0;
        if (Mode != 0) v2 = v2 - 1;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        int v3 = b0 ? -4 : 5;
        int v4 = s0 * -2;
        return (c1, v2, v3, v4);
    }

    [Handwired]
    public static (int, float, bool) Differential199(
        bool b0, bool b1, bool b2, [Range(-5, 2)] int i0, [Range(-29, 14)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? i0 : n;
        float x2 = f0 - f0;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c3 = StepBit(b2, tb, 0 * 4 + s0);
        return (v1, x2, c3);
    }

    [Handwired]
    public static (int, int) Differential200(
        bool b0, bool b1, bool b2, [Range(-39, -22)] int i0, [Range(1, 25)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 1, 3)];
        v1 = Increment(b0, v1);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b1) v1 = v1 + 1;
        }
        int v2 = (b1 ? 1 : 0);
        return (v1, v2);
    }

    [Handwired]
    public static (int, int) Differential201(
        bool b0, bool b1, bool b2, [Range(-38, -37)] int i0, [Range(-14, -1)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n + s0, 0, 8)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        int v2 = ti[Math.Clamp(v1 + v1, 0, 8)];
        return (v1, v2);
    }

    [Handwired]
    public static (int, int, bool, int) Differential202(
        bool b0, bool b1, bool b2, [Range(2, 47)] int i0, [Range(-35, 1)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + n;
        int v2 = Hw.ReadOrZero(ti, v1);
        v2 = Increment(b2, v2);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v2 = v2 + 1;
        }
        bool c3 = tb[0 * 5 + s0];
        int v4 = n + v2;
        return (v1, v2, c3, v4);
    }

    [Handwired]
    public static (int, int, int, float, float, int) Differential203(
        bool b0, bool b1, bool b2, [Range(-2, 36)] int i0, [Range(-1, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n * 1 + -3);
        int v2 = b0 ? i1 : 0;
        int v3 = Math.Max(n, s0);
        float x4 = f0 - f1;
        float x5 = f1 * 3f;
        int v6 = Math.Min(i1, i1);
        return (v1, v2, v3, x4, x5, v6);
    }

    [Handwired]
    public static (float, int, bool) Differential204(
        bool b0, bool b1, bool b2, [Range(-4, 19)] int i0, [Range(-20, -2)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 * 3f;
        int v2 = b0 ? n : 0;
        if (Mode != 0) v2 = v2 - 1;
        if (b1) v2 = v2 + 2;
        bool c3 = s0 == -2;
        return (x1, v2, c3);
    }

    [Handwired]
    public static (bool, int, bool, bool) Differential205(
        bool b0, bool b1, bool b2, [Range(4, 43)] int i0, [Range(-6, 17)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = !tb[0 * 4 + s0] || b1;
        int v2 = n + (b1 ? 1 : 0);
        bool c3 = b0 && b0;
        bool c4 = StepBit(b0, tb, 3 * 4 + s0);
        return (c1, v2, c3, c4);
    }

    [Handwired]
    public static (int, int) Differential206(
        bool b0, bool b1, bool b2, [Range(5, 34)] int i0, [Range(7, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? s0 : 0;
        int v2 = ti[Math.Clamp(s0, 1, 3)];
        if (Mode != 0) v2 = v2 - 1;
        if (b2) v2 = v2 + (b2 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v2 = v2 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        return (v1, v2);
    }

    [Handwired]
    public static (int, int, bool) Differential207(
        bool b0, bool b1, bool b2, [Range(-25, 18)] int i0, [Range(-17, -9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? s0 : 0;
        int v2 = (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v2 = v2 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        bool c3 = tb[1 * 5 + s0];
        return (v1, v2, c3);
    }

    [Handwired]
    public static (int, int) Differential208(
        bool b0, bool b1, bool b2, [Range(7, 35)] int i0, [Range(-13, 7)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, s0 * 1 + 4);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        if (b2) v1 = v1 + (b1 ? 1 : 0);
        if (Mode != 0) v1 = v1 - 1;
        if (b1) v1 = v1 + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b1, v1);
        int v2 = ti[Math.Clamp(i0, 0, 6)];
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, bool, int) Differential209(
        bool b0, bool b1, bool b2, [Range(-12, 30)] int i0, [Range(-24, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(n, i1);
        bool c2 = b2 && b0;
        bool c3 = StepBit(b2, tb, 0 * 5 + s0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b0) v1 = v1 + 1;
        }
        int v4 = c3 ? s0 : i1;
        for (int k = 0; k < n; k++)
        {
            v4 = v4 + (b2 ? 1 : 0);
        }
        return (v1, c2, c3, v4);
    }

    [Handwired]
    public static (int, bool, int) Differential210(
        bool b0, bool b1, bool b2, [Range(-12, -10)] int i0, [Range(-20, 12)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 + i1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c2 = b2 && tb[Math.Clamp(n, 0, 15)];
        int v3 = ti[Math.Clamp(n, 1, 7)];
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, float, int, bool) Differential211(
        bool b0, bool b1, bool b2, [Range(-10, -6)] int i0, [Range(-2, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, n);
        float x2 = Math.Max(f1, f0);
        int v3 = n + s0;
        bool c4 = b1 && !b1 && tb[1 * 5 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && c4) v3 = v3 + 1;
        }
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (float, bool, int, float, int) Differential212(
        bool b0, bool b1, bool b2, [Range(-8, 40)] int i0, [Range(-18, 6)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 - f0;
        bool c2 = !tb[2 * 4 + s0] || b0;
        int v3 = ti[Math.Clamp(i0, 0, 7)] + (b0 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v3 = v3 + 1;
        }
        float x4 = x1 + f1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v3 = v3 + 1;
        }
        int v5 = c2 ? n : s0;
        return (x1, c2, v3, x4, v5);
    }

    [Handwired]
    public static (bool, bool, bool, int, bool) Differential213(
        bool b0, bool b1, bool b2, [Range(-25, 23)] int i0, [Range(-14, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = f1 >= f0;
        bool c2 = i0 == -3;
        bool c3 = f1 >= f0;
        int v4 = s0 - i1;
        v4 = Increment(c2, v4);
        bool c5 = f1 >= f0;
        return (c1, c2, c3, v4, c5);
    }

    [Handwired]
    public static (int, bool, int, bool, int, int) Differential214(
        bool b0, bool b1, bool b2, [Range(7, 19)] int i0, [Range(-22, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b1 ? s0 : i1;
        bool c2 = StepBit(b0, tb, 2 * 5 + s0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        int v3 = (b1 ? 1 : 0) + (b0 ? 1 : 0);
        bool c4 = StepBit(b2, tb, 2 * 5 + s0);
        if (c4) v1 = v1 + (c4 ? 1 : 0);
        int v5 = n + v1;
        int v6 = ti[Math.Clamp(s0 + s0, 0, 5)];
        return (v1, c2, v3, c4, v5, v6);
    }

    [Handwired]
    public static (int, bool, int, float) Differential215(
        bool b0, bool b1, bool b2, [Range(-29, 2)] int i0, [Range(-20, 21)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0);
        if (b0) v1 = v1 + 1;
        bool c2 = v1 < n;
        if (Mode != 0) v1 = v1 - 1;
        int v3 = b1 ? v1 : v1;
        float x4 = f0 + f0;
        return (v1, c2, v3, x4);
    }

    [Handwired]
    public static (float, bool, int, int) Differential216(
        bool b0, bool b1, bool b2, [Range(-18, 11)] int i0, [Range(-33, -7)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f0 - f1;
        bool c2 = b2 || !b2;
        int v3 = b1 ? -3 : 0;
        v3 = Increment(c2, v3);
        if (b1) v3 = v3 + (c2 ? 1 : 0);
        if (Mode != 0) v3 = v3 - 1;
        int v4 = n + i0;
        return (x1, c2, v3, v4);
    }

    [Handwired]
    public static (bool, bool, float, int, float) Differential217(
        bool b0, bool b1, bool b2, [Range(7, 44)] int i0, [Range(-14, -13)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = n < i1;
        bool c2 = tb[2 * 5 + s0];
        float x3 = f0 + f0;
        int v4 = i1 + s0;
        for (int k = 0; k < n; k++)
        {
            v4 = v4 + (b0 ? 1 : 0);
        }
        v4 = Increment(c1, v4);
        if (Mode != 0) v4 = v4 - 1;
        float x5 = b1 ? f0 : 0.75f;
        return (c1, c2, x3, v4, x5);
    }

    [Handwired]
    public static (int, float, float, float, int, int) Differential218(
        bool b0, bool b1, bool b2, [Range(-28, 6)] int i0, [Range(5, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = StepBit(b2, tb, 0 * 4 + s0);
        int v2 = ti[Math.Clamp(n, 1, 5)];
        float x3 = tf[Math.Clamp(i0, 0, 4)];
        float x4 = Hw.ReadOrZero(tf, i0);
        float x5 = x3 * 3f;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v2 = v2 + 1;
        }
        int v6 = ti[Math.Clamp(v2 + v2, 0, 6)];
        int v7 = ti[Math.Clamp(s0, 1, 5)];
        return (v2, x3, x4, x5, v6, v7);
    }

    [Handwired]
    public static (int, bool, float, int, bool) Differential219(
        bool b0, bool b1, bool b2, [Range(-19, 8)] int i0, [Range(-23, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 1, 4)];
        bool c2 = !tb[2 * 4 + s0] || b1;
        float x3 = f0 + (c2 ? 1f : 0f);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        if (c2) v1 = v1 + 1;
        int v4 = b2 ? i1 : s0;
        for (int k = 0; k < n; k++)
        {
            v4 = v4 + (b1 ? 1 : 0);
        }
        bool c5 = !tb[1 * 4 + s0] || c2;
        return (v1, c2, x3, v4, c5);
    }

    [Handwired]
    public static (int, bool, bool) Differential220(
        bool b0, bool b1, bool b2, [Range(-11, -4)] int i0, [Range(-37, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? 3 : -4;
        bool c2 = StepBit(b2, tb, 1 * 3 + s0);
        bool c3 = c2 && !b0 && tb[2 * 3 + s0];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b2) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int, bool, bool) Differential221(
        bool b0, bool b1, bool b2, [Range(0, 10)] int i0, [Range(-19, 30)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 4)];
        int v2 = i1 * -2;
        bool c3 = i1 < s0;
        bool c4 = StepBit(b0, tb, 2 * 4 + s0);
        if (b2) v2 = v2 + 1;
        if (b1) v1 = v1 + 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v2 = v2 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        return (v1, v2, c3, c4);
    }

    [Handwired]
    public static (int, int) Differential222(
        bool b0, bool b1, bool b2, [Range(-12, 5)] int i0, [Range(-24, 8)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(n, i1);
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        int v2 = ti[Math.Clamp(n, 0, 4)];
        return (v1, v2);
    }

    [Handwired]
    public static (bool, int, bool) Differential223(
        bool b0, bool b1, bool b2, [Range(-28, -21)] int i0, [Range(-9, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b0 || !b2;
        int v2 = Math.Min(i1, n);
        bool c3 = b1 && tb[Math.Clamp(s0, 0, 14)];
        if (c3) v2 = v2 + 1;
        if (b0) v2 = v2 + (b0 ? 1 : 0);
        return (c1, v2, c3);
    }
}
