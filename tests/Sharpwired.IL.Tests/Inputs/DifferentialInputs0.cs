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
    private const int Mode = 2;

    [Handwired]
    private static int Increment(bool bit, int value) => value + (bit ? 1 : 0);

    [Handwired]
    private static bool StepBit(bool bit, bool[] table, int index) => bit && table[index];

    [Handwired]
    public static (int, bool) Differential000(
        bool b0, bool b1, bool b2, [Range(-8, 27)] int i0, [Range(10, 18)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 4)];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        bool c2 = b2 ^ b0;
        v1 = Increment(b0, v1);
        return (v1, c2);
    }

    [Handwired]
    public static (int, int) Differential001(
        bool b0, bool b1, bool b2, [Range(-36, 0)] int i0, [Range(-34, 11)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i0 - n;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        int v2 = i0 - s0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        return (v1, v2);
    }

    [Handwired]
    public static (int, float, int, bool) Differential002(
        bool b0, bool b1, bool b2, [Range(-7, 6)] int i0, [Range(-39, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(i1, i0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        float x2 = b1 ? f0 : f0;
        if (b2) v1 = v1 + 1;
        int v3 = Math.Clamp(i1, -8, 8);
        if (b1) v1 = v1 + 2;
        bool c4 = b0 && tb[Math.Clamp(s0, 0, 15)];
        return (v1, x2, v3, c4);
    }

    [Handwired]
    public static (int, int) Differential003(
        bool b0, bool b1, bool b2, [Range(-20, -15)] int i0, [Range(-39, -24)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1 * 2 + -6);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 5 + s0] && b2) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        int v2 = b0 ? i0 : s0;
        return (v1, v2);
    }

    [Handwired]
    public static (int, bool, int) Differential004(
        bool b0, bool b1, bool b2, [Range(-28, -18)] int i0, [Range(-30, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? i1 : 0;
        v1 = Increment(b2, v1);
        if (Mode != 0) v1 = v1 - 1;
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b2 ? 1 : 0);
        }
        bool c2 = b2 && b2;
        v1 = Increment(b2, v1);
        int v3 = ti[Math.Clamp(n + i0, 0, 4)];
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, float, bool, int, float) Differential005(
        bool b0, bool b1, bool b2, [Range(-26, 20)] int i0, [Range(1, 46)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 - n;
        float x2 = f1 + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b0 ? 1 : 0);
        }
        bool c3 = StepBit(b2, tb, 0 * 4 + s0);
        int v4 = i0 * -2;
        float x5 = (float)i0 * 0.25f;
        return (v1, x2, c3, v4, x5);
    }

    [Handwired]
    public static (int, int, int, bool, bool) Differential006(
        bool b0, bool b1, bool b2, [Range(-29, 19)] int i0, [Range(3, 13)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b2 ? 1 : 0);
        int v2 = ti[Math.Clamp(i0, 0, 7)] + (b0 ? 1 : 0);
        int v3 = b1 ? v1 : i0;
        v2 = Increment(b2, v2);
        bool c4 = b0 || !b2;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v2 = v2 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b1) v3 = v3 + 1;
        }
        bool c5 = i0 == -2;
        return (v1, v2, v3, c4, c5);
    }

    [Handwired]
    public static (int, bool, bool, bool, bool) Differential007(
        bool b0, bool b1, bool b2, [Range(-30, -18)] int i0, [Range(-40, -33)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b2 ? 1 : 0) + (b2 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = i0 < n;
        bool c3 = c2 && b0;
        v1 = Increment(b0, v1);
        bool c4 = StepBit(b2, tb, 1 * 4 + s0);
        if (Mode != 0) v1 = v1 - 1;
        bool c5 = i0 == -1;
        return (v1, c2, c3, c4, c5);
    }

    [Handwired]
    public static (int, bool) Differential008(
        bool b0, bool b1, bool b2, [Range(-35, -8)] int i0, [Range(-21, -3)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i1);
        if (b2) v1 = v1 + (b0 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        v1 = Increment(b2, v1);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        bool c2 = b0 && tb[1 * 4 + s0];
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        return (v1, c2);
    }

    [Handwired]
    public static (int, bool) Differential009(
        bool b0, bool b1, bool b2, [Range(-25, -22)] int i0, [Range(-39, -12)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 6)] + (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v1 = v1 + 1;
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v1 = v1 + 1;
        }
        bool c2 = i0 < v1;
        if (b2) v1 = v1 + (b1 ? 1 : 0);
        return (v1, c2);
    }

    [Handwired]
    public static (int, float) Differential010(
        bool b0, bool b1, bool b2, [Range(-39, -38)] int i0, [Range(8, 9)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(20)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = i1 * 3;
        if (b1) v1 = v1 + 2;
        float x2 = f0 * 0.5f;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 5 + s0]) v1 = v1 + 1;
        }
        v1 = Increment(b1, v1);
        return (v1, x2);
    }

    [Handwired]
    public static (int, bool, int, bool) Differential011(
        bool b0, bool b1, bool b2, [Range(8, 55)] int i0, [Range(1, 46)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Min(s0, i1);
        bool c2 = b0 && b0;
        if (Mode != 0) v1 = v1 - 1;
        int v3 = b2 ? i0 : i0;
        bool c4 = f1 >= f0;
        return (v1, c2, v3, c4);
    }

    [Handwired]
    public static (int, bool, int, int) Differential012(
        bool b0, bool b1, bool b2, [Range(-26, -24)] int i0, [Range(-27, 3)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Hw.ReadOrZero(ti, i0 * 1 + -5);
        bool c2 = b2 && tb[Math.Clamp(s0, 0, 8)];
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c2) v1 = v1 + 1;
        }
        if (Mode != 0) v1 = v1 - 1;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v1 = v1 + 1;
        }
        int v3 = Hw.ReadOrZero(ti, v1);
        int v4 = i1 + i1;
        return (v1, c2, v3, v4);
    }

    [Handwired]
    public static (int, int, bool, int, bool, int) Differential013(
        bool b0, bool b1, bool b2, [Range(-17, 2)] int i0, [Range(-11, 33)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 && tb[2 * 3 + s0];
        int v2 = Math.Min(i0, i0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && c1) v2 = v2 + 1;
        }
        int v3 = i0 * -2;
        bool c4 = v2 == 0;
        int v5 = ti[Math.Clamp(s0, 0, 5)] + (c1 ? 1 : 0);
        bool c6 = c4 && !c4 && tb[2 * 3 + s0];
        int v7 = (c6 ? 1 : 0) + (c1 ? 1 : 0);
        return (v2, v3, c4, v5, c6, v7);
    }

    [Handwired]
    public static (bool, int, int) Differential014(
        bool b0, bool b1, bool b2, [Range(-20, -6)] int i0, [Range(-38, 4)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b0 && b1;
        int v2 = s0 + n;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b0) v2 = v2 + 1;
        }
        int v3 = ti[Math.Clamp(i1 + v2, 0, 6)];
        return (c1, v2, v3);
    }

    [Handwired]
    public static (int, bool, float) Differential015(
        bool b0, bool b1, bool b2, [Range(-38, -9)] int i0, [Range(-2, 27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b2 ? n : i0;
        if (b0) v1 = v1 + (b0 ? 1 : 0);
        bool c2 = tb[2 * 3 + s0];
        if (Mode != 0) v1 = v1 - 1;
        float x3 = b0 ? f0 : f0;
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b0, v1);
        return (v1, c2, x3);
    }

    [Handwired]
    public static (int, int, bool) Differential016(
        bool b0, bool b1, bool b2, [Range(-19, 21)] int i0, [Range(-39, -20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = s0 + s0;
        int v2 = v1 + n;
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 4 + s0]) v2 = v2 + 1;
        }
        v1 = Increment(b0, v1);
        v1 = Increment(b1, v1);
        bool c3 = tb[1 * 4 + s0];
        return (v1, v2, c3);
    }

    [Handwired]
    public static (int, float, int) Differential017(
        bool b0, bool b1, bool b2, [Range(-40, -35)] int i0, [Range(-10, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(s0, 0, 7)] + (b1 ? 1 : 0);
        float x2 = f1 - f0;
        int v3 = i0 + (b1 ? 1 : 0);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b0 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 4 + s0] && b2) v1 = v1 + 1;
        }
        return (v1, x2, v3);
    }

    [Handwired]
    public static (float, int, bool) Differential018(
        bool b0, bool b1, bool b2, [Range(9, 31)] int i0, [Range(-13, 10)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(4), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = (float)i1 * 0.25f;
        int v2 = s0 * 2;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v2 = v2 + 1;
        }
        bool c3 = f1 >= f0;
        return (x1, v2, c3);
    }

    [Handwired]
    public static (int, int, bool) Differential019(
        bool b0, bool b1, bool b2, [Range(-28, -20)] int i0, [Range(-30, 19)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = Math.Clamp(i1, 5, 24);
        int v2 = Math.Clamp(i0, -4, 13);
        bool c3 = b2 && b1;
        if (Mode != 0) v1 = v1 - 1;
        v2 = Increment(b2, v2);
        return (v1, v2, c3);
    }

    [Handwired]
    public static (bool, int, float, bool) Differential020(
        bool b0, bool b1, bool b2, [Range(-18, -14)] int i0, [Range(-9, 22)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(9), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = tb[2 * 4 + s0];
        int v2 = s0 + (c1 ? 1 : 0);
        float x3 = f1 * 3f;
        bool c4 = tb[1 * 4 + s0];
        if (c1) v2 = v2 + 2;
        return (c1, v2, x3, c4);
    }

    [Handwired]
    public static (int, int, float) Differential021(
        bool b0, bool b1, bool b2, [Range(0, 13)] int i0, [Range(-35, -33)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0);
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        int v2 = Math.Min(s0, v1);
        float x3 = b1 ? f0 : f1;
        return (v1, v2, x3);
    }

    [Handwired]
    public static (float, int, int, bool, int, bool) Differential022(
        bool b0, bool b1, bool b2, [Range(-10, 27)] int i0, [Range(-14, 23)] int i1, [Range(0, 4)] int n,
        [Range(0, 4)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(7), Range(-50, 50)] int[] ti, [FixedLength(15)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        float x1 = f1 + (b1 ? 1f : 0f);
        int v2 = Hw.ReadOrZero(ti, i1);
        int v3 = b1 ? i1 : 0;
        bool c4 = !tb[2 * 5 + s0] || b2;
        int v5 = v3 + i0;
        bool c6 = c4 && !b1 && tb[2 * 5 + s0];
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b1 ? 1 : 0);
        }
        return (x1, v2, v3, c4, v5, c6);
    }

    [Handwired]
    public static (int, float, bool, int) Differential023(
        bool b0, bool b1, bool b2, [Range(3, 48)] int i0, [Range(-15, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(n, 0, 7)];
        float x2 = f1 + f0;
        for (int k = 0; k < n; k++)
        {
            v1 = v1 + (b1 ? 1 : 0);
        }
        bool c3 = b0 && b1;
        if (b1) v1 = v1 + (b0 ? 1 : 0);
        int v4 = Hw.ReadOrZero(ti, s0 * 3 + -6);
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b0) v4 = v4 + 1;
        }
        return (v1, x2, c3, v4);
    }

    [Handwired]
    public static (int, int, bool, int) Differential024(
        bool b0, bool b1, bool b2, [Range(-14, 17)] int i0, [Range(-13, 26)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b2 ? 1 : 0);
        int v2 = ti[Math.Clamp(v1, 0, 4)];
        if (b1) v1 = v1 + (b1 ? 1 : 0);
        if (Mode != 0) v2 = v2 - 1;
        bool c3 = StepBit(b0, tb, 1 * 3 + s0);
        int v4 = (b0 ? 1 : 0);
        return (v1, v2, c3, v4);
    }

    [Handwired]
    public static (int, bool, float, int, bool, int) Differential025(
        bool b0, bool b1, bool b2, [Range(-22, 10)] int i0, [Range(-6, 27)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(9)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = n + s0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        bool c2 = tb[2 * 3 + s0];
        float x3 = f0 * 3f;
        int v4 = n + s0;
        bool c5 = b0 && b0;
        for (int k = 0; k < 3; k++)
        {
            if (tb[k * 3 + s0] && b1) v1 = v1 + 1;
        }
        int v6 = (c5 ? 1 : 0);
        return (v1, c2, x3, v4, c5, v6);
    }

    [Handwired]
    public static (bool, bool, int, int, bool) Differential026(
        bool b0, bool b1, bool b2, [Range(9, 24)] int i0, [Range(-39, -29)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.5)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = i1 == 3;
        bool c2 = b0 && tb[Math.Clamp(s0, 0, 11)];
        int v3 = (c1 ? 1 : 0) + (c2 ? 1 : 0);
        int v4 = Hw.ReadOrZero(ti, i0 * 2 + -4);
        bool c5 = n < i0;
        for (int k = 0; k < n; k++)
        {
            v4 = v4 + (b2 ? 1 : 0);
        }
        for (int k = 0; k < n; k++)
        {
            v3 = v3 + (b0 ? 1 : 0);
        }
        return (c1, c2, v3, v4, c5);
    }

    [Handwired]
    public static (int, bool, int) Differential027(
        bool b0, bool b1, bool b2, [Range(-24, 9)] int i0, [Range(-8, 29)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(8), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = b0 ? s0 : 0;
        if (b2) v1 = v1 + 1;
        bool c2 = b2 && tb[1 * 4 + s0];
        int v3 = Hw.ReadOrZero(ti, s0);
        return (v1, c2, v3);
    }

    [Handwired]
    public static (int, float) Differential028(
        bool b0, bool b1, bool b2, [Range(-2, 39)] int i0, [Range(-29, 20)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(5), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(7), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1 + n, 0, 4)];
        if (b1) v1 = v1 + 1;
        float x2 = f1 + (b1 ? 1f : 0f);
        v1 = Increment(b2, v1);
        v1 = Increment(b0, v1);
        return (v1, x2);
    }

    [Handwired]
    public static (int, bool, bool) Differential029(
        bool b0, bool b1, bool b2, [Range(-22, 15)] int i0, [Range(-1, 15)] int i1, [Range(0, 4)] int n,
        [Range(0, 2)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = (b1 ? 1 : 0) + (b2 ? 1 : 0);
        if (b2) v1 = v1 + 1;
        v1 = Increment(b0, v1);
        bool c2 = StepBit(b0, tb, 0 * 3 + s0);
        bool c3 = c2 && tb[2 * 3 + s0];
        for (int k = 0; k < n; k++)
        {
            if (tb[k * 3 + s0]) v1 = v1 + 1;
        }
        return (v1, c2, c3);
    }

    [Handwired]
    public static (int, int) Differential030(
        bool b0, bool b1, bool b2, [Range(-6, 40)] int i0, [Range(-4, 27)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(1)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(8), Range(-50, 50)] int[] ti, [FixedLength(16)] bool[] tb,
        [FixedLength(6), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        int v1 = ti[Math.Clamp(i1, 0, 7)];
        v1 = Increment(b0, v1);
        if (Mode != 0) v1 = v1 - 1;
        v1 = Increment(b1, v1);
        if (b2) v1 = v1 + (b0 ? 1 : 0);
        v1 = Increment(b0, v1);
        if (b1) v1 = v1 + (b0 ? 1 : 0);
        int v2 = Hw.ReadOrZero(ti, i0 * 2 + -5);
        return (v1, v2);
    }

    [Handwired]
    public static (bool, int, int, float) Differential031(
        bool b0, bool b1, bool b2, [Range(-21, 1)] int i0, [Range(-19, 16)] int i1, [Range(0, 4)] int n,
        [Range(0, 3)] int s0, [Range(-8, 8), Grid(0.25)] float f0, [Range(-8, 8)] float f1,
        [FixedLength(6), Range(-50, 50)] int[] ti, [FixedLength(12)] bool[] tb,
        [FixedLength(5), Range(-10, 10), Grid(0.5)] float[] tf)
    {
        bool c1 = b2 || !b0;
        int v2 = ti[Math.Clamp(i0, 1, 4)];
        if (Mode != 0) v2 = v2 - 1;
        for (int k = 0; k < n; k++)
        {
            v2 = v2 + (b1 ? 1 : 0);
        }
        int v3 = ti[Math.Clamp(s0, 1, 4)];
        float x4 = tf[Math.Clamp(v2, 0, 4)];
        if (c1) v3 = v3 + (b2 ? 1 : 0);
        return (c1, v2, v3, x4);
    }
}
