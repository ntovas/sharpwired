// No Handwired source. C# inputs for the gate range tests of the final fix wave of phase 4 (items B
// and C): gates in long loops, whose inferred Select range grew in each copy, and int gates, whose
// result had no grid.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class GateRangeInputs
{
    // Form 1 with 512 copies. Before the fix, 26 copies were refused.
    [Handwired]
    public static int BraceDepth([FixedLength(512), Range(0, 1114111)] int[] cps)
    {
        int depth = 0;
        for (int i = 0; i < 512; i++)
        {
            if (cps[i] == 123) depth++;
        }

        return depth;
    }

    // The float version. Before the fix, 128 copies were refused.
    [Handwired]
    public static float BraceDepthFloat([FixedLength(512), Range(0, 1114111)] int[] cps)
    {
        float depth = 0f;
        for (int i = 0; i < 512; i++)
        {
            if (cps[i] == 123) depth += 1f;
        }

        return depth;
    }

    // Form 2 with a bool state machine over up to 512 code points: the string state of a JSON scan.
    [Handwired]
    public static (bool InString, bool Escape) StringState([Range(0, 512)] int n, [FixedLength(512), Range(0, 127)] int[] cps)
    {
        bool str = false;
        bool escape = false;
        for (int i = 0; i < n; i++)
        {
            int c = cps[i];
            bool quote = c == 34 && !escape;
            str ^= quote;
            escape = str && !escape && c == 92;
        }

        return (str, escape);
    }

    // An int gate of two values, then an int affine form of it.
    [Handwired]
    public static int ChosenMinusThree(bool b, [Range(-3, 3)] int i0, [Range(-50, 50)] int i1)
    {
        int v = b ? i0 : i1;
        return v - 3;
    }

    // An int form with no terms.
    [Handwired]
    public static int SelfDifference([Range(-3, 3)] int i0) => i0 - i0;

    // Form 2 with a kept int state (the ledger of task 5). Copy 0 keeps the state as
    // Multiply(active, back), and back is an int gate whose inferred range passed 2^24 grid steps. So
    // the product had no grid, and the int form s - 1 was refused.
    [Handwired]
    public static int KeptChoice([Range(0, 4)] int n, [FixedLength(4)] bool[] c, [Range(-10000000, 10000000)] int y, [Range(-10000000, 10000000)] int z)
    {
        int s = 0;
        for (int i = 0; i < n; i++) s = c[i] ? y : z;
        return s - 1;
    }
}
