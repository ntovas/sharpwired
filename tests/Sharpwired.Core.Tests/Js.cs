// Handwired source: the node:assert/strict and bun:test conventions of tests/runtime/*.test.ts.
// assert.equal and toBe compare with Object.is: NaN equals NaN, and +0 differs from -0.

namespace Sharpwired.Core.Tests;

internal static class Js
{
    // Math.fround.
    public static double F(double value) => (float)value;

    // Assert Object.is(expected, actual).
    public static void Is(double expected, double actual)
    {
        if (double.IsNaN(expected) && double.IsNaN(actual)) return;
        var same = expected == actual && (expected != 0 || double.IsNegative(expected) == double.IsNegative(actual));
        Assert.True(same, $"expected {expected:R}, got {actual:R}");
    }

    // Assert toEqual on two number arrays: the same length, and Object.is for each element.
    public static void AllIs(IEnumerable<double> expected, IEnumerable<double> actual)
    {
        var expectedValues = expected.ToArray();
        var actualValues = actual.ToArray();
        Assert.Equal(expectedValues.Length, actualValues.Length);
        for (var index = 0; index < expectedValues.Length; index++) Is(expectedValues[index], actualValues[index]);
    }

    // Math.round: the nearest integer, and the integer above at a half. A negative value that
    // rounds to 0 gives -0.
    public static double Round(double value)
    {
        if (!double.IsFinite(value)) return value;
        var rounded = Math.Floor(value);
        if (value - rounded >= 0.5) rounded += 1;
        return rounded == 0 && double.IsNegative(value) ? -0.0 : rounded;
    }

    // Handwired createRandom: the seeded hash of numeric-ui-core.test.ts, with the JavaScript
    // 32-bit integer operations.
    public static Func<double> CreateRandom(int seed) => () =>
    {
        unchecked
        {
            seed += 0x6d2b79f5;
            var value = (seed ^ (int)((uint)seed >> 15)) * (1 | seed);
            value = (value + (value ^ (int)((uint)value >> 7)) * (61 | value)) ^ value;
            return (uint)(value ^ (int)((uint)value >> 14)) / 4_294_967_296.0;
        }
    };

    // Assert that the action throws an exception whose message contains the text.
    public static Exception Throws(Action action, string text)
    {
        var exception = Assert.ThrowsAny<Exception>(action);
        Assert.True(
            exception is ArgumentException or InvalidOperationException,
            $"expected ArgumentException or InvalidOperationException, got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains(text, exception.Message);
        return exception;
    }
}
