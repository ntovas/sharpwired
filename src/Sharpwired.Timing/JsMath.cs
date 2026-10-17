// Handwired source: the JavaScript Math.round that src/runtime/host-timing.ts (quantizeTimeMs) and
// template/toggle.ts (the pointer coordinates) call. The body is the body of the test helper
// Js.Round in tests/Sharpwired.Core.Tests/Js.cs.

namespace Sharpwired.Timing;

/// <summary>The JavaScript Math functions that host code needs, with the JavaScript results.</summary>
public static class JsMath
{
    /// <summary>
    /// JavaScript Math.round: the nearest integer, and the integer above at a half. A negative value
    /// that rounds to 0 gives -0. NaN and the infinities stay the same. The .NET Math.Round rounds
    /// half to even, so host code does not use it.
    /// </summary>
    public static double Round(double value)
    {
        if (!double.IsFinite(value)) return value;
        var rounded = Math.Floor(value);
        if (value - rounded >= 0.5) rounded += 1;
        return rounded == 0 && double.IsNegative(value) ? -0.0 : rounded;
    }
}
