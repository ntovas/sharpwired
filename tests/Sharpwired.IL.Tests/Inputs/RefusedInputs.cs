// No Handwired source. C# inputs that the translator refuses (plan, task 3, and the items carried
// from the reviews of tasks 1 and 2). Each refused construct is on the line of its marker comment
// ("marker: <name>"). A method with a refused parameter is one line, because the location of a
// parameter is the first sequence point of the method. A refused property is on the line of its
// getter.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public record BaseShape([Range(0, 1)] float X);

public sealed record DerivedShape([Range(0, 1)] float X, [Range(0, 1)] float Y) : BaseShape(X);

public readonly record struct InvertedRange(
    [property: Range(1, 0)] float Level); // marker: inverted-range

public readonly record struct BadGrid(
    [Range(0, 1), Grid(0.3)] float Level); // marker: bad-grid

public readonly record struct OutsideInitial(
    [Range(0, 1), Initial(2)] float Level); // marker: outside-initial

public static class RefusedInputs
{
    // The method checks.

    [Handwired]
    public static T Generic<T>(T x) => x;

    public static float NoAttribute(float x) => x;

    // The cell layout refusals.

    [Handwired]
    public static float PublicFieldParameter(MutablePair pair) => pair.A; // marker: public-field

    [Handwired]
    public static float BaseRecord(DerivedShape shape) => shape.Y; // marker: base-record

    [Handwired]
    public static float NoLength([Range(0, 1)] float[] w) => w[0]; // marker: no-length

    [Handwired]
    public static float ByteLeaf(byte b) => b; // marker: byte-leaf

    [Handwired]
    public static int IntWithoutRange(int i) => i; // marker: int-without-range

    [Handwired]
    public static float InvertedRangeProperty(InvertedRange value) => value.Level;

    [Handwired]
    public static float BadGridProperty(BadGrid value) => value.Level;

    [Handwired]
    public static float OutsideInitialProperty(OutsideInitial value) => value.Level;

    [Handwired]
    public static float ZeroLength([FixedLength(0), Range(0, 1)] float[] w) => 0f; // marker: zero-length

    [Handwired, MaxIterations(-1)]
    public static float NegativeIterations([Range(0, 1)] float x) => x; // marker: negative-iterations

    // The operation refusals.

    [Handwired]
    public static float DoubleStep([Range(0, 1)] float x) => (float)(x * 0.1); // marker: double

    [Handwired]
    public static int IntDivision([Range(0, 100)] int a, [Range(1, 100)] int b) => a / b; // marker: int-division

    [Handwired]
    public static float DivideByThree([Range(0, 1)] float x) => x / 3f; // marker: divide-by-three

    [Handwired]
    public static float DivideByValue([Range(0, 1)] float x, [Range(1, 2)] float y) => x / y; // marker: divide-by-value

    [Handwired]
    public static float SquareRoot([Range(0, 1)] float x) => (float)Math.Sqrt(x); // marker: math-sqrt

    [Handwired]
    public static float SquareRootF([Range(0, 1)] float x) => MathF.Sqrt(x); // marker: mathf-sqrt

    [Handwired]
    public static float Print([Range(0, 1)] float x) { Console.WriteLine(x); return x; } // marker: console

    [Handwired]
    public static int WideInt([Range(0, 10_000_000)] int a, [Range(0, 10_000_000)] int b) => a + b; // marker: wide-int

    [Handwired]
    public static int WideProduct([Range(0, 10_000)] int a, [Range(0, 10_000)] int b) => a * b; // marker: wide-product

    // The constant is not on the first line of the method, so a refusal at IL offset 0 gives another line.
    [Handwired]
    public static int WideConstant([Range(0, 10)] int a)
    {
        int b = a + 1;
        return b + 20_000_000; // marker: wide-constant
    }

    [Handwired]
    public static float IndexPastEnd([FixedLength(4), Range(0, 1)] float[] w, [Range(0, 4)] int i) => w[i]; // marker: index-past-end

    [Handwired]
    public static float ConstantIndexPastEnd([FixedLength(2), Range(0, 1)] float[] w) => w[2]; // marker: constant-index-past-end

    [Handwired]
    public static bool OpenCompare(float a, float b) => a < b; // marker: open-compare

    [Handwired]
    public static float CrossedClamp([Range(0, 1)] float v, [Range(0, 5)] float lo, [Range(2, 3)] float hi) => Math.Clamp(v, lo, hi); // marker: crossed-clamp

    [Handwired]
    public static float Fused([Range(0, 1)] float a, [Range(0, 1)] float b, [Range(0, 1)] float c) => MathF.FusedMultiplyAdd(a, b, c); // marker: fma

    [Handwired]
    public static float FloatToInt([Range(0, 10)] float x) => (int)x; // marker: float-to-int

    [Handwired]
    public static float Loop(float x)
    {
        float s = 0f;
        for (int i = 0; i < 4; i++) s += x; // marker: loop
        return s;
    }

    // A static readonly int past 2^24, and an int table with an element past 2^24 (review of task 3).
    private static readonly int Big = 16_777_217;

    private static readonly int[] BigTable = [1, 16_777_217];

    [Handwired]
    public static bool StaticBig([Range(0, 16777216)] int i) => i < Big; // marker: static-big

    [Handwired]
    public static int StaticBigTable([Range(0, 1)] int i) => BigTable[i]; // marker: static-big-table

    // The constant and the refused instruction on different lines (review of task 3).
    [Handwired]
    public static float DivideByStoredThree([Range(0, 1)] float y)
    {
        float d = 3f;
        return y / d; // marker: divide-by-stored-three
    }

    [Handwired]
    public static float StoredIndexPastEnd([FixedLength(2), Range(0, 1)] float[] w)
    {
        int i = 2;
        return w[i]; // marker: stored-index-past-end
    }

    [Handwired]
    public static float StoreIndexPastEnd([Range(0, 1)] float x)
    {
        var table = new float[2];
        int i = 2;
        table[i] = x; // marker: store-index-past-end
        return table[0];
    }

    [Handwired]
    public static AnimatedValue OpenAnimation(
        [FixedLength(4), Range(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound)] float[] matrix,
        float displacement,
        [Range(-1280, 1280)] float velocity,
        [Range(0, 20)] float destination,
        [Range(0, 20)] float during,
        bool settleCheck,
        bool snap) =>
        Hw.AnimatedObject(matrix, displacement, velocity, destination, during, settleCheck, snap); // marker: open-animation
}

public sealed class InstanceHolder
{
    [Handwired]
    public float Instance(float x) => x;
}
