// Handwired source: $REF/src/json-search/search-step.ts (the forward step: mark = highlighted + start,
// nextMatchCount = matchCount + start) and $REF/src/json-search/regex/data-program.ts (createStepAnd).
// C# inputs for the phase 6 plan, task 1. One input is a bool used as an int (carried items a and b
// of phase 4). One input is a branch on an int constant (carried item c). The inputs also include the forward
// step of the search at 8 states, alone and inlined in a loop over positions.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class BitArithmeticInputs
{
    public const int States = 8;

    // The class count of the search, plus one class that reads 0 (data-program.ts).
    public const int ClassSlots = 17;

    private const int Mode = 2;

    // Rule 1: a bool as an int is the bit wire. The program has no operation.
    [Handwired]
    public static int BitAsInt(bool b) => b ? 1 : 0;

    // Carried item a: a bool as an int in an int sum.
    [Handwired]
    public static int ConstantPlusBit([Range(-100, 100)] int k, bool b) => k + (b ? 1 : 0);

    [Handwired]
    public static int TwoBits(bool a, bool b) => (a ? 1 : 0) + (b ? 1 : 0);

    [Handwired]
    public static int IncrementAfterBit(bool c, bool b)
    {
        int x = c ? 1 : 0;
        if (b) x = x + 1;
        return x;
    }

    // A bool as an int that a product and a comparison read.
    [Handwired]
    public static (int Scaled, bool Positive) BitTimesInt([Range(-1000, 1000)] int k, bool b)
    {
        int bit = b ? 1 : 0;
        return (bit * k, bit > 0);
    }

    // A bool as a float: the same bit wire.
    [Handwired]
    public static float BitAsFloat([Range(-10, 10)] float x, bool b) => x + (b ? 1f : 0f);

    // A bool as an int with the constants 0 and 2: rule 5 of the gates, not a bit.
    [Handwired]
    public static int BitTimesTwo([Range(-100, 100)] int k, bool b) => k + (b ? 2 : 0);

    // Carried item c: a branch on an int constant other than 0 or 1.
    [Handwired]
    public static float ModeBranch([Range(-10, 10)] float x)
    {
        if (Mode != 0) x = x * 2f;
        return x;
    }

    // Carried item c: a constant int condition in a loop. Release writes "ldc.i4.2; brtrue".
    [Handwired]
    public static float ModeLoop([Range(-10, 10)] float x)
    {
        int k = Mode;
        while (k != 0)
        {
            x = x + 1f;
            k = k - 1;
        }

        return x;
    }

    // Carried item c: "s == 0 ? a : b" in a constant loop. Debug writes "brfalse" on the constant s.
    [Handwired]
    public static float FirstOfThree([Range(-10, 10)] float x, [Range(-10, 10)] float y)
    {
        float total = 0f;
        for (int s = 0; s < 3; s++)
        {
            total = total + (s == 0 ? x : y);
        }

        return total;
    }

    // The cause of the full-unroll refusal of the plan probe: count starts at the constant 0. So the
    // first copy gives the int gate ITE(b[0], 1, 0). Gate rule 5 gives the affine form b[0] with no
    // grid, and the form count + 1 of the next copy was not exact.
    [Handwired]
    public static int CountOfBits([FixedLength(4)] bool[] b)
    {
        int count = 0;
        for (int i = 0; i < 4; i++)
        {
            if (b[i]) count = count + 1;
        }

        return count;
    }

    // The forward step of search-step.ts at 8 states.
    [Handwired]
    public static (bool[] Reached, int Mark, int MatchCount) Forward(
        [Range(0, 16)] int codePointClass,
        [FixedLength(States)] bool[] notReach,
        [FixedLength(States)] bool[] reached,
        [Range(0, 512)] int matchCount,
        [FixedLength(ClassSlots * States * States)] bool[] transitions)
    {
        bool anyContinuing = false;
        var continuing = new bool[States];
        for (int s = 0; s < States; s++)
        {
            continuing[s] = reached[s] && !notReach[s];
            anyContinuing = anyContinuing || continuing[s];
        }

        bool start = !anyContinuing && !notReach[0];
        bool highlighted = anyContinuing ^ start;
        var live = new bool[States];
        for (int s = 0; s < States; s++)
        {
            live[s] = s == 0 ? continuing[s] ^ start : continuing[s];
        }

        int mark = (highlighted ? 1 : 0) + (start ? 1 : 0);
        var next = new bool[States];
        for (int to = 0; to < States; to++)
        {
            bool any = false;
            for (int from = 0; from < States; from++)
            {
                any = any || (live[from] && transitions[((from * States) + to) * ClassSlots + codePointClass]);
            }

            next[to] = any;
        }

        int nextCount = matchCount;
        if (start) nextCount = nextCount + 1;
        return (next, mark, nextCount);
    }

    // The forward step inlined at each of 6 positions, as in the full unroll of the plan probe.
    [Handwired]
    public static (int Count, int MarkSum) ForwardUnrolled(
        [FixedLength(6), Range(0, 16)] int[] classes,
        [FixedLength(6 * States)] bool[] notReach,
        [FixedLength(ClassSlots * States * States)] bool[] transitions)
    {
        var reached = new bool[States];
        int count = 0;
        int markSum = 0;
        for (int position = 0; position < 6; position++)
        {
            var notReachHere = new bool[States];
            for (int s = 0; s < States; s++)
            {
                notReachHere[s] = notReach[position * States + s];
            }

            var step = Forward(classes[position], notReachHere, reached, count, transitions);
            reached = step.Reached;
            count = step.MatchCount;
            markSum = markSum + step.Mark;
        }

        return (count, markSum);
    }
}
