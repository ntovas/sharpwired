// No Handwired source. C# inputs for the arithmetic, comparison, bit and Math tests and the Hw
// intrinsic tests of the translator (plan, task 3). Each method is straight-line code.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class ArithmeticInputs
{
    [Handwired]
    public static float ExactChain([Range(0, 16384), Grid(1)] float x) => x * 0.5f - 26f;

    [Handwired]
    public static float InexactChain(float a, float b, float c) => a + b + c;

    [Handwired]
    public static float ScaleByPointThree(float x) => x * 0.3f;

    [Handwired]
    public static float DivideByFour(float x) => x / 4f;

    [Handwired]
    public static float NegateAndShift(float x) => -x + 3f;

    [Handwired]
    public static float SharedForm([Range(-100, 100)] float x, [Range(-100, 100)] float y)
    {
        float shared = x * 0.3f;
        float product = x * y;
        return shared + product * shared;
    }

    [Handwired]
    public static float FloatProduct([Range(-100, 100)] float a, [Range(-100, 100)] float b) => a * b;

    [Handwired]
    public static int IntProduct([Range(-1000, 1000)] int a, [Range(0, 1000)] int b) => a * b;

    [Handwired]
    public static (bool, bool, bool, bool, bool, bool) FloatRelations([Range(-100, 100)] float a, [Range(-100, 100)] float b) =>
        (a > b, a >= b, a < b, a <= b, a == b, a != b);

    [Handwired]
    public static (bool, bool, bool, bool, bool, bool) IntRelations([Range(-100, 100)] int a, [Range(-100, 100)] int b) =>
        (a > b, a >= b, a < b, a <= b, a == b, a != b);

    [Handwired]
    public static (bool, bool, bool, bool, bool) BitOperations(bool a, bool b) => (!a, a & b, a | b, a ^ b, a == b);

    [Handwired]
    public static bool ThreeWayAnd(bool a, bool b, bool c) => a & !b & c;

    [Handwired]
    public static (float, float, float, float, float, float) MathCalls(
        [Range(-100, 100)] float a, [Range(-100, 100)] float b, [Range(-10, 0)] float lo, [Range(1, 10)] float hi) =>
        (Math.Min(a, b), Math.Max(a, b), Math.Clamp(a, lo, hi), Math.Abs(a), MathF.Min(a, b), MathF.Max(a, b));

    [Handwired]
    public static int IntMinimum([Range(-100, 100)] int a, [Range(-100, 100)] int b) => Math.Min(a, b);

    // Forms whose stored form is exact while a C# intermediate value rounds (review of task 3).
    [Handwired]
    public static float CancelledSum([Range(0, 10), Grid(1)] float a, [Range(-1e9, 1e9)] float b) => (a + b) - b;

    [Handwired]
    public static float CancelledConstant([Range(0, 1e6)] float x) => (x + 0.1f) - x;

    [Handwired]
    public static float CancelledProducts([Range(0, 16777216), Grid(1)] float x) => x * 3f - x * 2f;

    // Each intermediate value is exact: the chain stays one affine.
    [Handwired]
    public static float ExactTwoSteps([Range(0, 1000), Grid(1)] float x, [Range(0, 1000), Grid(1)] float y) => (x + y) * 0.5f - 3f;

    [Handwired]
    public static float Relu([Range(-100, 100)] float x) => Hw.Relu(x);

    [Handwired]
    public static bool Boolean([Range(0, 1)] float score) => Hw.Boolean(score);

    [Handwired]
    public static SpringValue ApplySpringMatrix(
        [FixedLength(4), Range(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound)] float[] matrix,
        [Range(-40, 40)] float displacement,
        [Range(-1280, 1280)] float velocity) =>
        Hw.ApplySpringMatrix(matrix, displacement, velocity);

    [Handwired]
    public static bool[] FirstActive(bool active, [FixedLength(3)] bool[] candidates) => Hw.FirstActive(active, candidates);

    [Handwired]
    public static AnimatedValue AnimatedObject(
        [FixedLength(4), Range(-AggregateInputs.MatrixBound, AggregateInputs.MatrixBound)] float[] matrix,
        [Range(-40, 40)] float displacement,
        [Range(-1280, 1280)] float velocity,
        [Range(0, 20)] float destination,
        [Range(0, 20)] float during,
        bool settleCheck,
        bool snap) =>
        Hw.AnimatedObject(matrix, displacement, velocity, destination, during, settleCheck, snap);
}
