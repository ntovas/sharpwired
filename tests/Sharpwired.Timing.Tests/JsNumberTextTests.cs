// Handwired source: the JavaScript String(number) rule (ECMAScript Number::toString), which the
// Handwired host-timing.ts messages use. No Handwired test file.
// The cases are the cases of tests/Sharpwired.Core.Tests/Fp32FormatTests.cs, plus 1.5e-6,
// -1.5e20 and -1e22. Node v24 gave each expected text.

namespace Sharpwired.Timing.Tests;

public class JsNumberTextTests
{
    public static TheoryData<double, string> JavaScriptTexts => new()
    {
        { 1e-7, "1e-7" },
        { 1.5e-6, "0.0000015" },
        { 1.5e21, "1.5e+21" },
        { 1e21, "1e+21" },
        { -1.5e20, "-150000000000000000000" },
        { -1e22, "-1e+22" },
        { 123456789012345680000.0, "123456789012345680000" },
        { 0.000001, "0.000001" },
        { 0.00001, "0.00001" },
        { -0.0, "0" },
        { 0.0, "0" },
        { double.NaN, "NaN" },
        { double.PositiveInfinity, "Infinity" },
        { double.NegativeInfinity, "-Infinity" },
        { Math.ScaleB(1.0, -149), "1.401298464324817e-45" },
        { float.MaxValue, "3.4028234663852886e+38" },
        { -2.5e-8, "-2.5e-8" },
        { (double)1e-7f, "1.0000000116860974e-7" },
        { (double)1e-6f, "9.999999974752427e-7" },
        { 1e15, "1000000000000000" },
        { 1e16, "10000000000000000" },
        { 1e20, "100000000000000000000" },
        { 9.999999999999999e20, "999999999999999900000" },
        { 123.456, "123.456" },
        { -0.5, "-0.5" },
        { 0.1, "0.1" },
        { 100, "100" },
        { double.Epsilon, "5e-324" },
        { double.MaxValue, "1.7976931348623157e+308" },
    };

    [Theory]
    [MemberData(nameof(JavaScriptTexts))]
    public void Format_gives_the_javascript_text_of_a_number(double value, string expected)
    {
        Assert.Equal(expected, JsNumberText.Format(value));
    }
}
