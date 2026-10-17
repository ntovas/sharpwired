// No Handwired source. Handwired has no translator. The refusal tests of task 3 (plan, section
// "Error message rules"): each refused construct gives the full message
// "<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>" with the line of
// the marker comment in Inputs/RefusedInputs.cs (a separate portable PDB). The errors of the method
// itself are plain ArgumentException values with no location (rule 8).

using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class RefusalTests
{
    private const string Refused = "RefusedInputs.cs";

    private const string IntRangeReason = "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24";

    [Theory]
    [InlineData(nameof(RefusedInputs.DoubleStep), "double", "double arithmetic", "the program computes in FP32; write the expression in float")]
    [InlineData(nameof(RefusedInputs.SquareRoot), "math-sqrt", "double arithmetic", "the program computes in FP32; write the expression in float")]
    [InlineData(nameof(RefusedInputs.IntDivision), "int-division", "int division", "C# rounds toward zero, and the program has no exact integer division")]
    [InlineData(nameof(RefusedInputs.DivideByThree), "divide-by-three", "float division by 3",
        "C# rounds x / 3 one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / 3)")]
    [InlineData(nameof(RefusedInputs.DivideByValue), "divide-by-value", "a division by a runtime value",
        "Handwired has only inexact constructions for a division by a runtime value")]
    [InlineData(nameof(RefusedInputs.SquareRootF), "mathf-sqrt", "a call to System.MathF.Sqrt(Single)",
        "the lowering table has no exact construction for this method")]
    [InlineData(nameof(RefusedInputs.Print), "console", "a call to System.Console.WriteLine(Single)",
        "the program calls only property getters, the Hw methods and the known methods of the lowering table")]
    [InlineData(nameof(RefusedInputs.WideInt), "wide-int", "an int value with a range past 2^24", IntRangeReason)]
    [InlineData(nameof(RefusedInputs.WideProduct), "wide-product", "an int value with a range past 2^24", IntRangeReason)]
    [InlineData(nameof(RefusedInputs.WideConstant), "wide-constant", "the int constant 20000000",
        "FP32 holds each int exactly only within -2^24..2^24")]
    [InlineData(nameof(RefusedInputs.IndexPastEnd), "index-past-end", "an array index with a range past the array",
        "the index range 0..4 must lie in 0..3")]
    [InlineData(nameof(RefusedInputs.ConstantIndexPastEnd), "constant-index-past-end", "the array index 2", "the index must lie in 0..1")]
    [InlineData(nameof(RefusedInputs.OpenCompare), "open-compare", "a comparison",
        "Compare needs finite declared ranges on both operands, got -Infinity..Infinity")]
    [InlineData(nameof(RefusedInputs.CrossedClamp), "crossed-clamp", "a call to System.Math.Clamp(Single, Single, Single)",
        "the declared range of the minimum can pass the maximum; .NET throws, and ClampExact returns the maximum")]
    [InlineData(nameof(RefusedInputs.Fused), "fma", "a call to System.MathF.FusedMultiplyAdd(Single, Single, Single)",
        "a fused multiply-add rounds one time, and the program rounds the product and the sum one at a time")]
    [InlineData(nameof(RefusedInputs.FloatToInt), "float-to-int", "a conversion from float to int",
        "the program converts a float to int only for a value on a grid of 1 within -2^24..2^24")]
    [InlineData(nameof(RefusedInputs.Branch), "branch", "a branch", "this version of the translator accepts only straight-line code")]
    [InlineData(nameof(RefusedInputs.OpenAnimation), "open-animation",
        "a call to Sharpwired.Annotations.Hw.AnimatedObject(Single[], Single, Single, Single, Single, Boolean, Boolean)",
        "each float operand of Hw.AnimatedObject needs a finite declared range")]
    [InlineData(nameof(RefusedInputs.StaticBig), "static-big", "the int constant 16777217", "FP32 holds each int exactly only within -2^24..2^24")]
    [InlineData(nameof(RefusedInputs.StaticBigTable), "static-big-table", "the int constant 16777217", "FP32 holds each int exactly only within -2^24..2^24")]
    [InlineData(nameof(RefusedInputs.DivideByStoredThree), "divide-by-stored-three", "float division by 3",
        "C# rounds x / 3 one time, and a product with the rounded reciprocal gives another value for some x; write x * (1f / 3)")]
    [InlineData(nameof(RefusedInputs.StoredIndexPastEnd), "stored-index-past-end", "the array index 2", "the index must lie in 0..1")]
    [InlineData(nameof(RefusedInputs.StoreIndexPastEnd), "store-index-past-end", "the array index 2", "the index must lie in 0..1")]
    public void A_refused_construct_gives_the_file_and_the_line_of_its_marker(string method, string marker, string construct, string reason) =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), method)), Refused, marker, construct, reason);

    [Fact]
    public void A_method_with_no_Handwired_attribute_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), nameof(RefusedInputs.NoAttribute))));
        Assert.Equal("the method RefusedInputs.NoAttribute has no [Handwired] attribute", error.Message);
    }

    [Fact]
    public void An_instance_method_is_refused()
    {
        var method = typeof(InstanceHolder).GetMethod(nameof(InstanceHolder.Instance))!;
        var error = Assert.Throws<ArgumentException>(() => HandwiredTranslator.TranslateFunction(method));
        Assert.Equal("a [Handwired] method must be static", error.Message);
    }

    [Fact]
    public void A_generic_method_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), nameof(RefusedInputs.Generic))));
        Assert.Equal("a [Handwired] method must not be generic", error.Message);
        var closed = Input(typeof(RefusedInputs), nameof(RefusedInputs.Generic)).MakeGenericMethod(typeof(float));
        Assert.Equal("a [Handwired] method must not be generic", Assert.Throws<ArgumentException>(() => HandwiredTranslator.TranslateFunction(closed)).Message);
    }

    [Fact]
    public void TranslateStep_refuses_a_method_with_another_signature()
    {
        var error = Assert.Throws<ArgumentException>(() => HandwiredTranslator.TranslateStep(Input(typeof(ArithmeticInputs), nameof(ArithmeticInputs.InexactChain))));
        Assert.Equal("TranslateStep needs two parameters and a (Next, Output) tuple", error.Message);
    }

    [Fact]
    public void A_refusal_names_the_source_file_line_and_offset()
    {
        var error = AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(RefusedInputs), nameof(RefusedInputs.WideConstant))),
            Refused, "wide-constant", "the int constant 20000000", "FP32 holds each int exactly only within -2^24..2^24");
        Assert.Equal(InputPath(Refused), error.SourceFile);
        Assert.Equal(FrontEndSupport.MarkerLine(InputPath(Refused), "wide-constant"), error.Line);
        Assert.True(error.IlOffset > 0);
        Assert.IsAssignableFrom<ArgumentException>(error);
    }
}
