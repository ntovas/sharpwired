// Handwired source: the JavaScript String(number) rule (ECMAScript Number::toString), which the
// Handwired messages use, and src/runtime/numeric-ui-core.ts (validateGrids, describeFirstNaN,
// emitCompare, emitAnimatedObject) for the message text.
// Node v24 gave each expected text below.

namespace Sharpwired.Core.Tests;

public class Fp32FormatTests
{
    public static TheoryData<double, string> JavaScriptTexts => new()
    {
        { 1e-7, "1e-7" },
        { 1.5e21, "1.5e+21" },
        { 1e21, "1e+21" },
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
        Assert.Equal(expected, Fp32.Format(value));
    }

    [Fact]
    public void Format_gives_a_different_text_for_each_different_value_except_the_two_zeros()
    {
        // The expression cache keys hold Fp32.Format text, so two different values must not
        // share a text. The shortest round-trip digits identify one double, and the JavaScript
        // text holds the sign, the digits and the exponent.
        var random = Js.CreateRandom(20261017);
        var textToBits = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var sample = 0; sample < 200_000; sample++)
        {
            var bits = (long)(random() * 4_294_967_296.0) << 32 | (long)(random() * 4_294_967_296.0);
            var value = BitConverter.Int64BitsToDouble(bits);
            if (double.IsNaN(value) || value == 0) continue;
            var text = Fp32.Format(value);
            Assert.Equal(value, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
            if (textToBits.TryGetValue(text, out var earlierBits)) Assert.Equal(earlierBits, bits);
            textToBits[text] = bits;
        }
    }

    [Fact]
    public void ValidateGrids_writes_the_numbers_as_javascript_does()
    {
        // Handwired: `wire ${wire} must be a multiple of ${grid}, got ${value}`.
        var circuit = new Circuit(1);
        circuit.AssumeGrid(circuit.Source(0), Math.ScaleB(1.0, -20));
        Js.Throws(
            () => circuit.ValidateGrids([1e-7f]),
            "wire 0 must be a multiple of 9.5367431640625e-7, got 1.0000000116860974e-7");
    }

    [Fact]
    public void DescribeFirstNaN_writes_the_index_terms_as_javascript_does()
    {
        // Handwired: `an index read whose index form is NaN, from ${describeTerms(operation.gateTerms)}`.
        var circuit = new Circuit(3);
        // Cells 1 and 2 have an open range, so they can hold ±Infinity. The declared index range
        // closes the range of the index form.
        circuit.AssumeRange(circuit.Source(0), -1, 1);
        var candidate = circuit.Constant(5);
        circuit.IndexReadAffine(
            [new Term(circuit.Source(0), 1), new Term(circuit.Source(1), 1), new Term(circuit.Source(2), 1)],
            0,
            [candidate, candidate],
            OutOfRange.Clamp,
            OutOfRange.Clamp,
            new NumericRange(-3, 3));
        var values = circuit.EvaluateReference([1e-6f, float.PositiveInfinity, float.NegativeInfinity]);
        Assert.Equal(
            "wire 4 is the first NaN: an index read whose index form is NaN, from wire 0 = 9.999999974752427e-7, wire 1 = Infinity, wire 2 = -Infinity",
            circuit.DescribeFirstNaN(values));
    }

    [Fact]
    public void Compare_writes_an_open_range_as_javascript_does()
    {
        // Handwired: `emitCompare needs finite declared ranges on both operands, got ${minimum}..${maximum}`.
        // The "Compare needs" prefix is a ruling of the port.
        var circuit = new Circuit(2);
        circuit.AssumeRange(circuit.Source(0), 0.000001, double.PositiveInfinity);
        circuit.AssumeRange(circuit.Source(1), 0, 1);
        Js.Throws(
            () => circuit.Compare(circuit.Source(0), Relation.Greater, circuit.Source(1)),
            "Compare needs finite declared ranges on both operands, got 0.000001..Infinity");
    }

    [Fact]
    public void AnimatedObject_writes_a_small_settle_threshold_as_javascript_does()
    {
        // Handwired: `settle thresholds must be finite and at least 2^-22, got ${threshold}`.
        var circuit = new Circuit(8);
        var cells = Enumerable.Range(0, 8).Select(circuit.Source).ToArray();
        var spring = new AnimatedSpring(new SpringMatrixWires(cells[0], cells[1], cells[2], cells[3]), cells[4], cells[5], cells[6]);
        Js.Throws(
            () => circuit.AnimatedObject([spring], new AnimatedObjectOptions(cells[7]) { SettleDistance = 1e-7, SettleSpeed = 1 }),
            "settle thresholds must be finite and at least 2^-22, got 1.0000000116860974e-7");
    }
}
