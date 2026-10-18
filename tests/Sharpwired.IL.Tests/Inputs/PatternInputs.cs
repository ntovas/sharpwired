// Handwired source: $REF/docs/expressions.md, rows 13 to 15 of the table "Familiar expressions"
// (Math.floor, threshold counts, an index read over a table of results) and line 48 (a floor
// division by a runtime value through its one-hot), $REF/lab/ui-exploration/guide/patterns/
// threshold-counts.md and $REF/lab/ui-exploration/guide/sections/loops.md, sections 3 and 4. C#
// inputs for the pattern tests of the translator (plan, task 6b): running totals in loops of form 1
// and form 2, loops that look like running totals and keep their normal form, and threshold counts
// for a floor, an int division and an int remainder. Each refused input is on the line of its marker
// comment ("marker: <name>").

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class PatternInputs
{
    // Running totals, form 1.

    // A scalar accumulator whose value at each step goes to an array element (an inclusive scan).
    [Handwired]
    public static float[] RunningSum([FixedLength(64), Range(-1000, 1000), Grid(0.25)] float[] x)
    {
        var totals = new float[64];
        float s = 0f;
        for (int i = 0; i < 64; i++)
        {
            s += x[i];
            totals[i] = s;
        }

        return totals;
    }

    // minimums[i] = Math.Min(minimums[i - 1], x[i]): the accumulator is the array element of the step before.
    [Handwired]
    public static float[] RunningMinimumInArray([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var minimums = new float[8];
        minimums[0] = x[0];
        for (int i = 1; i < 8; i++) minimums[i] = Math.Min(minimums[i - 1], x[i]);
        return minimums;
    }

    // The value before the step goes to the array element (an exclusive scan).
    [Handwired]
    public static int[] RowTops([FixedLength(16), Range(0, 300)] int[] heights)
    {
        var tops = new int[16];
        int top = 0;
        for (int i = 0; i < 16; i++)
        {
            tops[i] = top;
            top += heights[i];
        }

        return tops;
    }

    [Handwired]
    public static float[] RunningMinimum([FixedLength(16), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var minimums = new float[16];
        float m = 1000f;
        for (int i = 0; i < 16; i++)
        {
            m = Math.Min(m, x[i]);
            minimums[i] = m;
        }

        return minimums;
    }

    [Handwired]
    public static float[] RunningMaximumFirst([FixedLength(16), Range(-1000, 1000)] float[] x)
    {
        var maximums = new float[16];
        float m = x[0];
        for (int i = 0; i < 16; i++)
        {
            m = MathF.Max(x[i], m);
            maximums[i] = m;
        }

        return maximums;
    }

    [Handwired, Associative]
    public static float Larger(float earlier, float later) => Math.Max(earlier, later);

    [Handwired]
    public static float[] RunningLarger([FixedLength(16), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var largest = new float[16];
        float m = -1000f;
        for (int i = 0; i < 16; i++)
        {
            m = Larger(m, x[i]);
            largest[i] = m;
        }

        return largest;
    }

    // A [Handwired] combine with no [Associative] attribute.
    [Handwired]
    public static float Smaller(float earlier, float later) => Math.Min(earlier, later);

    [Handwired]
    public static float[] RunningSmallerUnmarked([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var smallest = new float[8];
        float m = 1000f;
        for (int i = 0; i < 8; i++)
        {
            m = Smaller(m, x[i]);
            smallest[i] = m;
        }

        return smallest;
    }

    // An associative combine that is not commutative: the first value that is not 0.
    [Handwired, Associative]
    public static float FirstNonZero(float earlier, float later) => earlier != 0f ? earlier : later;

    // The accumulator is the first operand: the first value that is not 0 up to each step.
    [Handwired]
    public static float[] RunningFirstNonZero([FixedLength(16), Range(-3, 3), Grid(1)] float[] x)
    {
        var firsts = new float[16];
        float m = 0f;
        for (int i = 0; i < 16; i++)
        {
            m = FirstNonZero(m, x[i]);
            firsts[i] = m;
        }

        return firsts;
    }

    // The accumulator is the second operand: the last value that is not 0 up to each step.
    [Handwired]
    public static float[] RunningLastNonZero([FixedLength(16), Range(-3, 3), Grid(1)] float[] x)
    {
        var lasts = new float[16];
        float m = 0f;
        for (int i = 0; i < 16; i++)
        {
            m = FirstNonZero(x[i], m);
            lasts[i] = m;
        }

        return lasts;
    }

    // Loops that look like running totals and keep their normal form.

    // s - x[i] is not associative.
    [Handwired]
    public static float[] RunningDifference([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var totals = new float[8];
        float s = 0f;
        for (int i = 0; i < 8; i++)
        {
            s -= x[i];
            totals[i] = s;
        }

        return totals;
    }

    // With no grid, FP32 rounds the partial sums, and another grouping gives other values.
    [Handwired]
    public static float[] RunningSumInexact([FixedLength(8), Range(-1000, 1000)] float[] x)
    {
        var totals = new float[8];
        float s = 0f;
        for (int i = 0; i < 8; i++)
        {
            s += x[i];
            totals[i] = s;
        }

        return totals;
    }

    // 64 values up to 10^6 on the grid 1: the total can pass 2^24, so FP32 rounds some partial sums.
    [Handwired]
    public static float[] RunningSumPastExact([FixedLength(64), Range(-1000000, 1000000), Grid(1)] float[] x)
    {
        var totals = new float[64];
        float s = 0f;
        for (int i = 0; i < 64; i++)
        {
            s += x[i];
            totals[i] = s;
        }

        return totals;
    }

    [Handwired]
    public static bool[] RunningAll([FixedLength(8)] bool[] x)
    {
        var all = new bool[8];
        bool a = true;
        for (int i = 0; i < 8; i++)
        {
            a &= x[i];
            all[i] = a;
        }

        return all;
    }

    // The accumulator also goes into the loop body: the step adds s to itself.
    [Handwired]
    public static float[] RunningDouble([FixedLength(8), Range(-10, 10), Grid(1)] float[] x)
    {
        var totals = new float[8];
        float s = 1f;
        for (int i = 0; i < 8; i++)
        {
            s = s + s * 0.5f + x[i];
            totals[i] = s;
        }

        return totals;
    }

    // totals[i] = totals[i - 1] + x[i]. The normal form already is one wide sum for each element.
    [Handwired]
    public static float[] RunningSumInArray([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var totals = new float[8];
        totals[0] = x[0];
        for (int i = 1; i < 8; i++) totals[i] = totals[i - 1] + x[i];
        return totals;
    }

    // minimums[i] = Math.Min(minimums[i - 2], x[i]) reads an element that is not the element of the step before.
    [Handwired]
    public static float[] RunningMinimumSkip([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var minimums = new float[8];
        minimums[0] = x[0];
        minimums[1] = x[1];
        for (int i = 2; i < 8; i++) minimums[i] = Math.Min(minimums[i - 2], x[i]);
        return minimums;
    }

    // totals[i] = totals[i - 2] + x[i] reads an element that is not the element of the step before.
    [Handwired]
    public static float[] RunningSumSkip([FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var totals = new float[8];
        totals[0] = x[0];
        totals[1] = x[1];
        for (int i = 2; i < 8; i++) totals[i] = totals[i - 2] + x[i];
        return totals;
    }

    // Running totals, form 2.

    [Handwired]
    public static float CountedTotal([Range(0, 16)] int n, [FixedLength(16), Range(-1000, 1000), Grid(1)] float[] x)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x[i];
        return s;
    }

    // A value before the loop that is not 0: an inactive copy must add 0, not this value (review of
    // task 6b, round 1).
    [Handwired]
    public static float CountedTotalFromFive([Range(0, 8)] int n, [FixedLength(8), Range(-10, 10), Grid(1)] float[] x)
    {
        float s = 5f;
        for (int i = 0; i < n; i++) s += x[i];
        return s;
    }

    [Handwired]
    public static int CountedIntTotalFromThree([Range(0, 8)] int n, [FixedLength(8), Range(-10, 10)] int[] x)
    {
        int s = 3;
        for (int i = 0; i < n; i++) s += x[i];
        return s;
    }

    [Handwired]
    public static float CountedTotalFromValue([Range(0, 8)] int n, [Range(-10, 10), Grid(1)] float y, [FixedLength(8), Range(-10, 10), Grid(1)] float[] x)
    {
        float s = y;
        for (int i = 0; i < n; i++) s += x[i];
        return s;
    }

    // 200 copies. The normal form keeps the state with a Select in each copy, and the store under the
    // active bit is refused.
    [Handwired]
    public static float[] RunningSumCounted([Range(0, 200)] int n, [FixedLength(200), Range(-1000, 1000), Grid(1)] float[] x)
    {
        var totals = new float[200];
        float s = 0f;
        for (int i = 0; i < n; i++) // marker: running-counted
        {
            s += x[i];
            totals[i] = s; // marker: running-counted-store
        }

        return totals;
    }

    [Handwired]
    public static float CountedMaximum([Range(0, 16)] int n, [FixedLength(16), Range(-1000, 1000)] float[] x)
    {
        float m = -1000f;
        for (int i = 0; i < n; i++) m = Math.Max(m, x[i]);
        return m;
    }

    [Handwired]
    public static bool CountedAny([Range(0, 16)] int n, [FixedLength(16)] bool[] x)
    {
        bool any = false;
        for (int i = 0; i < n; i++) any |= x[i];
        return any;
    }

    [Handwired]
    public static bool CountedAll([Range(0, 16)] int n, [FixedLength(16)] bool[] x)
    {
        bool all = true;
        for (int i = 0; i < n; i++)
        {
            bool hit = x[i];
            all = all && hit;
        }

        return all;
    }

    // Gated Min and Max totals whose value before the loop lies outside the element ranges (final fix
    // wave, item A). With n = 0 the plain method returns the value before the loop.
    [Handwired]
    public static float CountedMinimumFromLarge([Range(0, 4)] int n, [FixedLength(4), Range(-1, 1)] float[] x)
    {
        float s = 1000f;
        for (int i = 0; i < n; i++) s = Math.Min(s, x[i]);
        return s;
    }

    [Handwired]
    public static float CountedMinimumFromValue([Range(0, 4)] int n, [Range(-1000, 1000)] float y, [FixedLength(4), Range(-1, 1)] float[] x)
    {
        float s = y;
        for (int i = 0; i < n; i++) s = Math.Min(s, x[i]);
        return s;
    }

    [Handwired]
    public static float CountedMaximumFromLow([Range(0, 4)] int n, [FixedLength(4), Range(0, 10)] float[] x)
    {
        float s = -1000f;
        for (int i = 0; i < n; i++) s = Math.Max(s, x[i]);
        return s;
    }

    [Handwired]
    public static float CountedMaximumFromValue([Range(0, 4)] int n, [Range(-1000, 1000)] float y, [FixedLength(4), Range(0, 10)] float[] x)
    {
        float s = y;
        for (int i = 0; i < n; i++) s = Math.Max(s, x[i]);
        return s;
    }

    [Handwired]
    public static int CountedIntMinimumFromValue([Range(0, 4)] int n, [Range(-100, 100)] int y, [FixedLength(4), Range(0, 20)] int[] x)
    {
        int s = y;
        for (int i = 0; i < n; i++) s = Math.Min(s, x[i]);
        return s;
    }

    [Handwired]
    public static int CountedIntMaximumFromValue([Range(0, 4)] int n, [Range(-100, 100)] int y, [FixedLength(4), Range(0, 20)] int[] x)
    {
        int s = y;
        for (int i = 0; i < n; i++) s = Math.Max(s, x[i]);
        return s;
    }

    // An [Associative] combine has no identity value, so a loop of form 2 keeps its normal form.
    [Handwired]
    public static float CountedLarger([Range(0, 8)] int n, [FixedLength(8), Range(-1000, 1000), Grid(1)] float[] x)
    {
        float m = -1000f;
        for (int i = 0; i < n; i++) m = Larger(m, x[i]);
        return m;
    }

    // Threshold counts.

    // The column count of a responsive grid (threshold-counts.md): 7 possible results.
    [Handwired]
    public static int ColumnCount([Range(244, 1708), Grid(1)] float width) => (int)MathF.Floor(width / 244f); // marker: column-count

    // The gallery form of the same count: the thresholds are on the width itself.
    [Handwired]
    public static float GalleryColumns([Range(268, 1732), Grid(1)] float width) => MathF.Floor((width - 24f) / 244f);

    [Handwired]
    public static float FloorQuarter([Range(-8, 8), Grid(0.25)] float x) => MathF.Floor(x);

    [Handwired]
    public static int Truncated([Range(-100, 100), Grid(1)] float x) => (int)(x / 12f);

    [Handwired]
    public static int Row([Range(0, 63)] int item) => item / 8; // marker: row

    [Handwired]
    public static int Column([Range(0, 63)] int item) => item % 8; // marker: column

    [Handwired]
    public static int NegativeColumn([Range(-50, 50)] int item) => item % -7;

    // 600001 grid points: the translator finds the 50 change points by bisection.
    [Handwired]
    public static int WideColumnCount([Range(0, 150000), Grid(0.25)] float width) => (int)MathF.Floor(width / 3000f);

    // Results up to 2^27: refused, as an int form past 2^24 is refused.
    [Handwired]
    public static float FloorPastExact([Range(0, 134217728), Grid(4194304)] float x) => MathF.Floor(x); // marker: floor-past-exact

    [Handwired]
    public static int NegativeRow([Range(-50, 50)] int item) => item / -7 + item % 7;

    // The row of item 10 for a runtime column count (loops.md, section 4).
    [Handwired]
    public static int RowOfItem([Range(1, 7)] int columns) => 10 / columns + 10 % columns;

    // The same count written as a loop, with no division.
    [Handwired]
    public static int RowByLoop([Range(0, 63)] int item)
    {
        int row = 0;
        for (int threshold = 8; threshold <= 63; threshold += 8)
        {
            if (item >= threshold) row++;
        }

        return row;
    }

    // 157 possible results: an index read over a table of the results.
    [Handwired]
    public static int Slot([Range(0, 9999)] int row) => row / 64 + row % 64;

    // 1001 possible results of a float: refused.
    [Handwired]
    public static float FloorWide([Range(0, 1000), Grid(0.25)] float x) => MathF.Floor(x); // marker: floor-wide

    // No grid: refused.
    [Handwired]
    public static float FloorNoGrid([Range(0, 10)] float x) => MathF.Floor(x / 3f); // marker: floor-no-grid

    // Two runtime operands: refused (task 6b moved the refusal of rem from the decoder to the lowering).
    [Handwired]
    public static int RemainderOfValues([Range(0, 100)] int a, [Range(1, 100)] int b) => a % b; // marker: rem-values

    // The divisor can be 0: C# throws, so the translator refuses it.
    [Handwired]
    public static int DivideByCount([Range(0, 4)] int count) => 12 / count; // marker: divide-by-count

    // A constant divisor in a loop copy folds.
    [Handwired]
    public static int HalvesInLoop([Range(-100, 100)] int x)
    {
        int s = x;
        for (int i = 0; i < 5; i++) s += i / 2 - i % 2;
        return s;
    }
}
