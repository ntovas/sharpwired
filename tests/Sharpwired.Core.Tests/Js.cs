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
