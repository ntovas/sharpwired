// No Handwired source. Generated inputs for the seeded differential test of the affine emission
// rule (plan, section "Emission rules"; review of task 4, finding 1). Each method is a random chain of
// 2 to 6 instructions (+, -, * k, / 2^n) over 1 to 3 float parameters with random ranges and grids. Each
// local has two or more uses: the next instruction reads it, and the return tuple holds it. A script
// with the fixed seed 20261017 wrote this file one time. Do not edit it by hand.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class ChainInputs
{
    [Handwired]
    public static (float, float, float, float, float) Chain00([Range(-100, 100), Grid(0.015625)] float p0, [Range(0, 10)] float p1)
    {
        float t0 = p1 / 4f;
        float t1 = t0 * 1.5f;
        float t2 = t1 * 1.5f;
        float t3 = t2 / 2f;
        float t4 = p1 - t3;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float) Chain01([Range(-5e7, 1e8)] float p0, [Range(-100, 100), Grid(0.25)] float p1)
    {
        float t0 = p0 / 2f;
        float t1 = t0 - -2f;
        float t2 = t1 * 0.1f;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float) Chain02([Range(0, 10), Grid(0.25)] float p0)
    {
        float t0 = p0 * 7f;
        float t1 = t0 - t0;
        float t2 = t1 - t0;
        float t3 = t2 - t2;
        return (t3, t0, t1, t2);
    }

    [Handwired]
    public static (float, float) Chain03([Range(-1e6, 1e6), Grid(1)] float p0, [Range(0, 10), Grid(0.25)] float p1, [Range(-5e7, 1e8)] float p2)
    {
        float t0 = p0 * 3f;
        float t1 = t0 - t0;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float) Chain04([Range(0, 10)] float p0, [Range(-100, 100), Grid(0.015625)] float p1)
    {
        float t0 = p1 + p1;
        float t1 = t0 + 0.75f;
        float t2 = t1 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float) Chain05([Range(-100, 100), Grid(1)] float p0, [Range(-5e7, 1e8)] float p1, [Range(-1e9, 1e9)] float p2)
    {
        float t0 = p0 / 0.5f;
        float t1 = t0 / 0.25f;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float) Chain06([Range(-100, 100)] float p0)
    {
        float t0 = p0 + p0;
        float t1 = t0 * 0.1f;
        float t2 = t1 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain07([Range(-100, 100), Grid(0.015625)] float p0, [Range(-5e7, 1e8)] float p1)
    {
        float t0 = 0.1f + p1;
        float t1 = t0 - p1;
        float t2 = t0 - t1;
        float t3 = t2 / 2f;
        float t4 = t3 + t2;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float) Chain08([Range(-100, 100), Grid(0.25)] float p0, [Range(-5e7, 1e8)] float p1)
    {
        float t0 = p0 + 3f;
        float t1 = t0 * 7f;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain09([Range(-1e9, 1e9)] float p0)
    {
        float t0 = 1000f - p0;
        float t1 = t0 / 8f;
        float t2 = t1 - 3f;
        float t3 = t2 * 0.1f;
        float t4 = t3 / 4f;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain10([Range(-1e6, 1e6), Grid(0.25)] float p0, [Range(-1e9, 1e9)] float p1, [Range(-1e9, 1e9)] float p2)
    {
        float t0 = p0 - p2;
        float t1 = t0 / 8f;
        float t2 = t1 - p2;
        float t3 = t2 * 1.5f;
        float t4 = t3 * 1.5f;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float) Chain11([Range(-5e7, 1e8)] float p0, [Range(-100, 100), Grid(1)] float p1)
    {
        float t0 = p1 / 2f;
        float t1 = 1000f + t0;
        float t2 = t0 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float) Chain12([Range(-1e9, 1e9)] float p0, [Range(-5e7, 1e8)] float p1, [Range(-5e7, 1e8)] float p2)
    {
        float t0 = p1 - p1;
        float t1 = t0 - 0.75f;
        float t2 = t1 * 0.5f;
        float t3 = t2 / 4f;
        return (t3, t0, t1, t2);
    }

    [Handwired]
    public static (float, float, float) Chain13([Range(-5e7, 1e8)] float p0)
    {
        float t0 = p0 * 0.1f;
        float t1 = t0 - t0;
        float t2 = t1 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain14([Range(-5e7, 1e8)] float p0)
    {
        float t0 = p0 * 0.1f;
        float t1 = t0 + t0;
        float t2 = t1 / 2f;
        float t3 = t2 * 3f;
        float t4 = t3 * 3f;
        float t5 = t4 * 0.1f;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain15([Range(-1e6, 1e6), Grid(0.25)] float p0, [Range(0, 10)] float p1, [Range(0, 10), Grid(0.015625)] float p2)
    {
        float t0 = p0 * 1.5f;
        float t1 = t0 + p1;
        float t2 = t1 * 0.5f;
        float t3 = t2 - p0;
        float t4 = t3 / 8f;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain16([Range(-1e6, 1e6), Grid(1)] float p0, [Range(-100, 100), Grid(0.25)] float p1, [Range(-5e7, 1e8)] float p2)
    {
        float t0 = p2 - p0;
        float t1 = t0 + t0;
        float t2 = t1 - t1;
        float t3 = t2 + p1;
        float t4 = p1 - t3;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain17([Range(-1e6, 1e6), Grid(1)] float p0, [Range(-1e6, 1e6), Grid(0.25)] float p1)
    {
        float t0 = p0 - p0;
        float t1 = t0 / 0.5f;
        float t2 = p0 + t1;
        float t3 = p1 + t2;
        float t4 = t3 * 0.1f;
        float t5 = t4 - t0;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float) Chain18([Range(-100, 100)] float p0, [Range(0, 10)] float p1)
    {
        float t0 = p0 / 8f;
        float t1 = t0 * 0.75f;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain19([Range(-5e7, 1e8)] float p0, [Range(-1e9, 1e9)] float p1)
    {
        float t0 = p1 - p0;
        float t1 = 3f - t0;
        float t2 = t1 * 0.75f;
        float t3 = t2 - p0;
        float t4 = t3 - p0;
        float t5 = t4 + t4;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain20([Range(-100, 100), Grid(1)] float p0, [Range(0, 10), Grid(0.25)] float p1)
    {
        float t0 = 0.3f - p0;
        float t1 = p1 + t0;
        float t2 = t1 / 0.5f;
        float t3 = t2 / 2f;
        float t4 = t0 - t3;
        float t5 = t4 - -2f;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float) Chain21([Range(-5e7, 1e8)] float p0, [Range(-1e9, 1e9)] float p1, [Range(-100, 100)] float p2)
    {
        float t0 = p2 - p2;
        float t1 = t0 / 2f;
        float t2 = t1 / 2f;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float) Chain22([Range(0, 10), Grid(1)] float p0)
    {
        float t0 = p0 - 1000f;
        float t1 = 1000f + t0;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain23([Range(-1e9, 1e9)] float p0, [Range(-5e7, 1e8)] float p1)
    {
        float t0 = p1 * 1.5f;
        float t1 = t0 - 3f;
        float t2 = t1 * 0.1f;
        float t3 = t2 * 3f;
        float t4 = t3 * -2f;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float) Chain24([Range(0, 10), Grid(1)] float p0, [Range(-5e7, 1e8)] float p1)
    {
        float t0 = p1 * -2f;
        float t1 = t0 / 8f;
        float t2 = p0 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float) Chain25([Range(0, 10), Grid(0.25)] float p0)
    {
        float t0 = p0 - p0;
        float t1 = t0 / 0.5f;
        float t2 = t1 * 0.75f;
        float t3 = p0 + t2;
        return (t3, t0, t1, t2);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain26([Range(-1e6, 1e6), Grid(0.25)] float p0, [Range(-1e9, 1e9)] float p1, [Range(-1e6, 1e6)] float p2)
    {
        float t0 = p2 / 4f;
        float t1 = p1 - t0;
        float t2 = t1 - t1;
        float t3 = t2 + t0;
        float t4 = t3 * 7f;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float) Chain27([Range(-5e7, 1e8)] float p0)
    {
        float t0 = p0 + p0;
        float t1 = t0 / 0.25f;
        float t2 = t1 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float, float, float) Chain28([Range(-5e7, 1e8)] float p0, [Range(0, 10), Grid(1)] float p1, [Range(-1e9, 1e9)] float p2)
    {
        float t0 = p1 - p0;
        float t1 = t0 - p2;
        float t2 = t1 * 0.5f;
        float t3 = t1 - t2;
        return (t3, t0, t1, t2);
    }

    [Handwired]
    public static (float, float) Chain29([Range(-100, 100)] float p0, [Range(-1e9, 1e9)] float p1, [Range(-100, 100), Grid(0.25)] float p2)
    {
        float t0 = p1 * 0.75f;
        float t1 = t0 * 3f;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain30([Range(-1e6, 1e6), Grid(1)] float p0, [Range(-1e9, 1e9)] float p1)
    {
        float t0 = p0 / 0.25f;
        float t1 = t0 + t0;
        float t2 = t1 - t0;
        float t3 = t2 + p1;
        float t4 = t3 / 2f;
        float t5 = t4 - t1;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain31([Range(-1e6, 1e6), Grid(0.25)] float p0)
    {
        float t0 = p0 * 0.75f;
        float t1 = p0 - t0;
        float t2 = t1 - t0;
        float t3 = t2 * 0.1f;
        float t4 = t3 + t1;
        float t5 = t4 / 2f;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain32([Range(-100, 100)] float p0, [Range(-100, 100)] float p1)
    {
        float t0 = p0 - -2f;
        float t1 = p0 - t0;
        float t2 = t1 - t1;
        float t3 = t2 * 0.75f;
        float t4 = t3 / 4f;
        float t5 = t4 * 1.5f;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float) Chain33([Range(-1e6, 1e6), Grid(1)] float p0, [Range(-1e9, 1e9)] float p1)
    {
        float t0 = 0.75f - p0;
        float t1 = t0 * 0.5f;
        float t2 = t1 - t1;
        return (t2, t0, t1);
    }

    [Handwired]
    public static (float, float) Chain34([Range(-100, 100), Grid(0.25)] float p0, [Range(-100, 100), Grid(1)] float p1)
    {
        float t0 = p1 / 0.25f;
        float t1 = t0 / 8f;
        return (t1, t0);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain35([Range(0, 10), Grid(0.015625)] float p0, [Range(-100, 100)] float p1)
    {
        float t0 = p0 + p1;
        float t1 = p0 - t0;
        float t2 = t1 - t1;
        float t3 = t0 - t2;
        float t4 = t3 * 1.5f;
        float t5 = t1 - t4;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain36([Range(-1e6, 1e6), Grid(0.25)] float p0)
    {
        float t0 = p0 / 0.25f;
        float t1 = t0 * 7f;
        float t2 = t0 + t1;
        float t3 = t2 - 0.75f;
        float t4 = t3 + t3;
        return (t4, t0, t1, t2, t3);
    }

    [Handwired]
    public static (float, float, float, float) Chain37([Range(-1e6, 1e6), Grid(0.25)] float p0)
    {
        float t0 = p0 * 1.5f;
        float t1 = t0 / 0.25f;
        float t2 = t1 - t0;
        float t3 = t2 * 1.5f;
        return (t3, t0, t1, t2);
    }

    [Handwired]
    public static (float, float, float, float, float, float) Chain38([Range(-100, 100)] float p0, [Range(-5e7, 1e8)] float p1, [Range(-100, 100)] float p2)
    {
        float t0 = p0 + p1;
        float t1 = p0 - t0;
        float t2 = t1 * 7f;
        float t3 = p0 - t2;
        float t4 = t3 / 0.5f;
        float t5 = p2 + t4;
        return (t5, t0, t1, t2, t3, t4);
    }

    [Handwired]
    public static (float, float, float, float, float) Chain39([Range(-5e7, 1e8)] float p0, [Range(0, 10), Grid(0.015625)] float p1)
    {
        float t0 = p0 - p1;
        float t1 = t0 + 0.3f;
        float t2 = p0 - t1;
        float t3 = t2 / 0.25f;
        float t4 = t3 * 3f;
        return (t4, t0, t1, t2, t3);
    }
}
