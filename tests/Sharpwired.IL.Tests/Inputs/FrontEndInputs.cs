// No Handwired source. Small C# methods for the tests of the IL front end: the decoder, the source
// locations, the control-flow graph and the SSA form. The tests find a line by its marker comment
// ("marker: <name>"), so an edit of this file does not break them. These inputs use the separate
// portable PDB of the test assembly.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public readonly record struct Pointer(bool Active, float X, float Y);

public sealed record State(bool On, float X);

public struct MutablePair
{
    public float A;
    public float B;

    public MutablePair(float a, float b)
    {
        A = a;
        B = b;
    }
}

public static class FrontEndInputs
{
    [Handwired]
    public static float AffineChain(float x) => x * 0.5f - 26f;

    [Handwired]
    public static float Conditional(bool c, float a, float b) => c ? a : b;

    [Handwired]
    public static float ConditionalPlusOne(bool c, float a, float b) => (c ? a : b) + 1f;

    [Handwired]
    public static bool InsideTrack(Pointer point, float trackLeft, float trackTop) =>
        point.X >= trackLeft && point.X < trackLeft + 52f && point.Y >= trackTop && point.Y < trackTop + 32f;

    [Handwired]
    public static float ConstantLoop(float x)
    {
        float s = 0f;
        for (int i = 0; i < 4; i++)
        {
            s += x;
        }

        return s;
    }

    [Handwired]
    public static float DoWhileAtStart(float x)
    {
        do
        {
            x *= 0.5f;
        }
        while (x > 1f);

        return x;
    }

    [Handwired]
    public static State MakeState(bool on, float x) => new(on, x);

    [Handwired]
    public static bool MinimumIsAtLeastZero(float a, float b) => MathF.Min(a, b) >= 0f;

    [Handwired]
    public static float TwoStatements(float x)
    {
        float y = x * 0.5f;
        return y - 26f; // marker: second-statement
    }

    [Handwired]
    public static float IfElse(bool c, float a, float b)
    {
        float r;
        if (c)
        {
            r = a * 2f;
        }
        else
        {
            r = b + 1f;
        }

        return r;
    }

    [Handwired]
    public static float NestedIf(bool c, bool d, float a, float b, float e)
    {
        float r = e;
        if (c)
        {
            if (d)
            {
                r = a;
            }
            else
            {
                r = b;
            }
        }

        return r;
    }

    [Handwired]
    public static float AndValue(float x, float y, float z, float a, float b)
    {
        bool all = x > 0f && y > 0f && z > 0f;
        return all ? a : b;
    }

    [Handwired]
    public static float OrValue(float x, float y, float z, float a, float b)
    {
        bool any = x > 0f || y > 0f || z > 0f;
        return any ? a : b;
    }

    [Handwired]
    public static float BoolLocal(float x, float a, float b)
    {
        bool flag = false;
        if (x > 0f)
        {
            flag = true;
        }

        return flag ? a : b;
    }

    [Handwired]
    public static float StoreField(MutablePair p, float x)
    {
        p.A = x;
        return p.A + p.B;
    }

    [Handwired]
    public static float ConstructLocal(float x)
    {
        var p = new MutablePair(x, 1f);
        return p.A;
    }

    // The refused inputs. Each refused construct is on the line of its marker.

    [Handwired]
    public static float WithExceptionClause(float x)
    {
        float r; try { r = x * 2f; } catch (ArithmeticException) { r = 0f; } return r; // marker: exception-clause
    }

    // An exception clause on more than one line. The refusal names the line of the first statement
    // in the try block, in Debug and in Release.
    [Handwired]
    public static float WithExceptionClauseOnLines(float x)
    {
        float r;
        try
        {
            r = x * 2f; // marker: exception-clause-lines
        }
        catch (ArithmeticException)
        {
            r = 0f;
        }

        return r;
    }

    [Handwired]
    public static float Boxed(float x) => (float)(object)x; // marker: box

    [Handwired]
    public static int Text(float x) => "abc".Length + (int)x; // marker: ldstr

    [Handwired]
    public static float Wide(float x) => (float)(x * 0.1); // marker: conv.r8

    [Handwired]
    public static uint UnsignedDivision(uint a, uint b) => a / b; // marker: div.un

    [Handwired]
    public static float Lambda(float x) { Func<float> f = () => x * 2f; return f(); } // marker: ldftn
}
