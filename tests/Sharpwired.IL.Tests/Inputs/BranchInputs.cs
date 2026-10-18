// No Handwired source. C# inputs for the branch tests of the translator (plan, task 4, and section
// "Decisions", question 4). They give the gates of c ? x : y, of an if statement, of && and || chains.
// They give the backward branches that the control-flow graph refuses, and a choice between two local arrays.
// Each refused construct is on the line of its marker comment ("marker: <name>").

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class BranchInputs
{
    [Handwired]
    public static float Choose(bool c, float x, float y) => c ? x : y;

    [Handwired]
    public static float ChooseConstants(bool c) => c ? 20f : 0f;

    [Handwired]
    public static float ChooseOffsetConstants(bool c) => c ? 3f : 1.5f;

    [Handwired]
    public static float ChooseOrZero(bool c, [Range(-100, 100)] float x) => c ? x : 0f;

    [Handwired]
    public static float DoubleWhen(bool c, [Range(-100, 100)] float x)
    {
        float y = x;
        if (c)
        {
            y = x * 2f;
        }

        return y;
    }

    [Handwired]
    public static bool AllThree([Range(-100, 100)] float x, [Range(-100, 100)] float y, [Range(-100, 100)] float z) =>
        x > 0f && y > 0f && z > 0f;

    [Handwired]
    public static float AllThreeBranch([Range(-100, 100)] float x, [Range(-100, 100)] float y, [Range(-100, 100)] float z, float a, float b)
    {
        if (x > 0f && y > 0f && z > 0f)
        {
            return a;
        }

        return b;
    }

    [Handwired]
    public static bool AndNot(bool a, bool b) => a && !b;

    [Handwired]
    public static bool EitherAbove([Range(-100, 100)] float x, [Range(-100, 100)] float y) => x > 0f || y > 0f;

    // The else side comes first in the IL of C#.
    [Handwired]
    public static float BothSides(bool c, [Range(-100, 100)] float x, [Range(-100, 100)] float y) => c ? x * y : MathF.Max(x, y);

    // A struct value that a branch changes: one gate for each field that changes.
    [Handwired]
    public static Pair SwapWhen(bool c, Pair pair)
    {
        var result = pair;
        if (c)
        {
            result = new Pair(pair.B, pair.A);
        }

        return result;
    }

    // An int equality branch. Roslyn writes "i < n" as bge in Release and as clt in Debug, so the
    // relation of an ordered int branch depends on the configuration. An equality gives Equal in both.
    [Handwired]
    public static int IntEqual([Range(0, 8)] int i, [Range(0, 8)] int n)
    {
        if (i == n)
        {
            return i + 1;
        }

        return n;
    }

    // The gate statements of the plain toggle (plan, section "Decisions", question 1), with a helper
    // call in place of InsideTrack.
    [Handwired]
    private static bool Positive([Range(-100, 100)] float v) => v > 0f;

    [Handwired]
    public static (bool On, bool Armed, float Destination) ToggleGates(
        bool downActive, [Range(-100, 100)] float downX, bool upActive, [Range(-100, 100)] float upX, bool cancel, bool stateOn, bool stateArmed)
    {
        bool armedAfterDown = downActive ? Positive(downX) : stateArmed;
        bool on = upActive && armedAfterDown && Positive(upX) ? !stateOn : stateOn;
        bool armed = armedAfterDown && !upActive && !cancel;
        float destination = on ? 20f : 0f;
        return (on, armed, destination);
    }

    // The backward branches outside a natural loop (review of task 2). Each cycle has two entries:
    // a bottom-tested cycle with a jump to its second statement, and a top-tested loop with a jump
    // into its body. C# has no jump into a while block, so the loop uses labels. The blocks of each
    // cycle are on the marker line.
    [Handwired]
    public static float IrreducibleCycle(bool c, [Range(0, 100)] float x)
    {
        if (c) goto second;
        first: x = x * 0.5f; second: x = x + 1f; if (x < 10f) goto first; // marker: irreducible
        return x;
    }

    [Handwired]
    public static float JumpIntoLoop(bool c, [Range(0, 100)] float x)
    {
        if (c) goto inside;
        top: if (x >= 100f) goto done; x = x * 0.5f; inside: x = x + 3f; goto top; // marker: into-loop
        done: return x;
    }

    // int != 0 (final fix wave, item D). Roslyn writes the value form as cgt.un against 0, and the
    // branch form as brtrue or brfalse on the int.
    [Handwired]
    public static bool IntNotZero([Range(-5, 5)] int x) => x != 0;

    [Handwired]
    public static bool ZeroNotInt([Range(-5, 5)] int x) => 0 != x;

    [Handwired]
    public static float ChooseOnIntNotZero([Range(-5, 5)] int x, [Range(-100, 100)] float a, [Range(-100, 100)] float b) => x != 0 ? a : b;

    [Handwired]
    public static float IfIntNotZero([Range(-5, 5)] int x, [Range(-100, 100)] float a, [Range(-100, 100)] float b)
    {
        if (x != 0)
        {
            return a;
        }

        return b;
    }

    [Handwired]
    public static float IfIntIsZero([Range(-5, 5)] int x, [Range(-100, 100)] float a, [Range(-100, 100)] float b)
    {
        if (x == 0)
        {
            return a;
        }

        return b;
    }

    // A choice with a side whose range is not finite (the product of two open cells). The refusal
    // names the line of the branch (final fix wave, item E).
    [Handwired]
    public static float OpenSideChoice(bool c, float x, float y)
    {
        float r = y;
        if (c) // marker: open-side
        {
            r = x * y;
        }

        return r + 1f;
    }

    // An array store that runs only when c is true (final fix wave, item I).
    [Handwired]
    public static float StoreUnderBranch(bool c, [Range(-100, 100)] float x)
    {
        var a = new float[1];
        if (c)
        {
            a[0] = x; // marker: store-under-branch
        }

        return a[0];
    }

    // A choice between two local arrays, and a store through the chosen array (review of task 4,
    // finding 2).
    [Handwired]
    public static float ChooseArray(bool c, [Range(-100, 100)] float x)
    {
        var p = new float[1];
        var q = new float[1];
        var a = c ? p : q; // marker: choose-array
        a[0] = x;
        return p[0];
    }

    // A backward branch with no cycle.
    [Handwired]
    public static float BackwardJump(bool c, float x, float y)
    {
        goto test;
    answer:
        return x;
    test:
        if (c) goto answer; // marker: backward-jump
        return y;
    }
}

public readonly record struct Pair([Range(-100, 100)] float A, [Range(-100, 100)] float B);
