// No Handwired source. Handwired has no translator. The refusal tests of tasks 3 and 4 (plan, section
// "Error message rules"): each refused construct gives the full message
// "<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>" with the line of
// the marker comment in Inputs/RefusedInputs.cs, Inputs/BranchInputs.cs or Inputs/CallInputs.cs (a
// separate portable PDB) or in the F# inputs (an embedded portable PDB). A refusal in an inlined
// method has the call sites after it (rule 4). The errors of the method itself are plain
// ArgumentException values with no location (rule 8).

using Sharpwired.IL.FSharpInputs;
using Sharpwired.IL.Tests.Inputs;
using static Sharpwired.IL.Tests.TranslationSupport;

namespace Sharpwired.IL.Tests;

public class RefusalTests
{
    private const string Refused = "RefusedInputs.cs";

    private const string IntRangeReason = "C# int arithmetic is exact up to 2^31, and FP32 only up to 2^24";

    private const string PlainCallReason = "the program calls only [Handwired] methods, property getters, the Hw methods and the known methods of the lowering table";

    private const string BackwardReason = "the program accepts a backward branch only as the back edge of a loop with one entry";

    private const string ClosureReason = "the program has no function values; call a [Handwired] method";

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
        PlainCallReason)]
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
    [InlineData(nameof(RefusedInputs.PastFloatRange), "past-float-range", "a value past the FP32 range",
        "the value can overflow FP32; declare smaller ranges")]
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

    // Final fix wave, item E: the refusal names the line of the branch, not the line of the reader.
    [Fact]
    public void A_choice_with_an_open_side_is_refused_at_the_line_of_its_branch() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(BranchInputs), nameof(BranchInputs.OpenSideChoice))), "BranchInputs.cs",
            "open-side", "a choice with a side that has no finite range",
            "a side of this choice can overflow FP32 when C# does not compute it; declare finite ranges");

    // Task 4: the backward branches outside a natural loop (review of task 2), with C# goto.
    [Theory]
    [InlineData(nameof(BranchInputs.IrreducibleCycle), "irreducible")]
    [InlineData(nameof(BranchInputs.JumpIntoLoop), "into-loop")]
    [InlineData(nameof(BranchInputs.BackwardJump), "backward-jump")]
    public void A_backward_branch_outside_a_natural_loop_is_refused(string method, string marker) =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(BranchInputs), method)), "BranchInputs.cs", marker,
            "a backward branch outside a natural loop", BackwardReason);

    // Review of task 4, finding 2: a gate of two local arrays makes an alias, and a later store
    // through the chosen array changes the wrong array.
    [Fact]
    public void A_choice_between_two_local_arrays_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(BranchInputs), nameof(BranchInputs.ChooseArray))), "BranchInputs.cs",
            "choose-array", "a choice between two arrays", "the program changes an array in place, so a branch must not choose between two arrays");

    // Final fix wave, item I.
    [Fact]
    public void An_array_store_under_a_branch_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(BranchInputs), nameof(BranchInputs.StoreUnderBranch))), "BranchInputs.cs",
            "store-under-branch", "an array store under a branch", "the program stores into an array only in code that runs on each call");

    [Fact]
    public void A_call_to_a_generic_Handwired_method_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), nameof(CallInputs.CallsGeneric))), "CallInputs.cs", "generic-call",
            "a call to Sharpwired.IL.Tests.Inputs.CallInputs.Same(Single)", "a [Handwired] method must not be generic");

    [Fact]
    public void A_call_to_a_method_without_Handwired_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), nameof(CallInputs.CallsPlain))), "CallInputs.cs", "plain-call",
            "a call to Sharpwired.IL.Tests.Inputs.CallInputs.Plain(Single)", PlainCallReason);

    [Fact]
    public void A_CSharp_lambda_is_refused() =>
        AssertRefused(() => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), nameof(CallInputs.Lambda))), "CallInputs.cs", "lambda",
            "the IL instruction ldftn", "the program has no delegates and no indirect calls");

    // The refusal is in the callee, and the call site follows (plan, "Error message rules", rule 4).
    [Fact]
    public void Recursion_through_a_helper_is_refused_at_the_inner_call_with_the_outer_call_site()
    {
        const string construct = "recursion";
        const string reason = "the program has a fixed size, so a [Handwired] method must not call itself";
        var path = InputPath("CallInputs.cs");
        AssertRefusedWith(() => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), nameof(CallInputs.Ping))),
            $"{RefusalMessage(path, "pong", construct, reason)} (called from {Site(path, "ping")})", construct, reason);
    }

    // Review of task 2: a call on an address keeps only the value of the receiver, so the inliner
    // refuses an instance method that changes this.
    [Fact]
    public void An_instance_method_that_writes_to_this_is_refused_with_the_call_site()
    {
        const string construct = "a store into this";
        const string reason = "the translator passes the receiver as a value, so a [Handwired] method must not change this";
        var path = InputPath("CallInputs.cs");
        AssertRefusedWith(() => HandwiredTranslator.TranslateFunction(Input(typeof(CallInputs), nameof(CallInputs.TallyTwice))),
            $"{RefusalMessage(path, "store-this", construct, reason)} (called from {Site(path, "call-tally")})", construct, reason);
    }

    // The F# inputs have an embedded portable PDB.
    [Fact]
    public void An_FSharp_closure_that_a_NoInlining_function_receives_is_refused() =>
        AssertRefusedWith(() => HandwiredTranslator.TranslateFunction(Input(typeof(Translation), "closure")),
            RefusalMessage(FrontEndSupport.FSharpInputPath(), "fs-closure", "a closure", ClosureReason), "a closure", ClosureReason);

    [Fact]
    public void An_FSharp_seq_is_refused() =>
        AssertRefusedWith(() => HandwiredTranslator.TranslateFunction(Input(typeof(Translation), "sequence")),
            RefusalMessage(FrontEndSupport.FSharpInputPath(), "fs-seq", "an exception clause", "the program has no exceptions; remove the try statement"),
            "an exception clause", "the program has no exceptions; remove the try statement");
}
