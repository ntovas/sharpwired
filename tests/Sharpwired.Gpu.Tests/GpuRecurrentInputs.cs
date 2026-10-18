// No Handwired source. The [Handwired] inputs of the GPU recurrent tests (phase 5 plan, task 5, and
// question 6, "Recurrent tests" and "Wide-cell test"), in addition to the inputs of RecurrentInputs.
// Each loop counts down from n. A loop "k < n" with a counter k from a constant has a declared maximum, and
// the translator unrolls it (loop form 2), so it is not a step program (RecurrentInputs, ChainLag).

using Sharpwired.Annotations;

namespace Sharpwired.Gpu.Tests;

public static class GpuRecurrentInputs
{
    // The wide-cell input of the plan: x doubles n times. x has no fixed point in 8 rounds, so it gets
    // the wide int range -2^23..2^23. For n = 24 and more, a next value of x passes 2^23, and the cell
    // check of the step stops the run.
    [Handwired]
    public static int DoubleUntil([Range(0, 40)] int n)
    {
        int x = 1;
        int k = n;
        while (k > 0)
        {
            x = x * 2;
            k--;
        }

        return x;
    }

    // A loop that needs n iterations, so n + 1 evaluations of the step. count gets the wide int range.
    [Handwired]
    public static int CountUp([Range(0, 3000)] int n)
    {
        int count = 0;
        int k = n;
        while (k > 0)
        {
            k--;
            count++;
        }

        return count;
    }

    // A bit state cell: b flips one time in each iteration.
    [Handwired]
    public static bool FlipUntil([Range(0, 40)] int n)
    {
        bool b = false;
        int k = n;
        while (k > 0)
        {
            b = !b;
            k--;
        }

        return b;
    }
}
