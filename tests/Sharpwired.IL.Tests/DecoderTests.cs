// No Handwired source. Handwired has no translator. The tests check the IL decoder of the plan,
// section "IL opcode table". They check the instruction list of input methods in Debug and in
// Release. They also check the refusal of an exception clause and of refused opcodes with the
// source file and line.

using static Sharpwired.IL.Tests.FrontEndSupport;

namespace Sharpwired.IL.Tests;

public class DecoderTests
{
    // Each row gives the listing without nop: the Release listing, and the Debug listing when it is
    // different. The two differ in the return form and in the loop condition.
    public static TheoryData<string, bool, string[], string[]> Listings => new()
    {
        { "AffineChain", false, ["ldarg.0", "ldc.r4 0.5", "mul", "ldc.r4 26", "sub", "ret"], [] },
        { "Conditional", false, ["ldarg.0", "brtrue.s", "ldarg.2", "ret", "ldarg.1", "ret"], ["ldarg.0", "brtrue.s", "ldarg.2", "br.s", "ldarg.1", "ret"] },
        { "ConditionalPlusOne", false, ["ldarg.0", "brtrue.s", "ldarg.2", "br.s", "ldarg.1", "ldc.r4 1", "add", "ret"], [] },
        {
            "InsideTrack", false,
            [
                "ldarga.s 0", "call get_X", "ldarg.1", "blt.un.s",
                "ldarga.s 0", "call get_X", "ldarg.1", "ldc.r4 52", "add", "bge.un.s",
                "ldarga.s 0", "call get_Y", "ldarg.2", "blt.un.s",
                "ldarga.s 0", "call get_Y", "ldarg.2", "ldc.r4 32", "add", "clt", "ret",
                "ldc.i4.0", "ret",
            ],
            [
                "ldarga.s 0", "call get_X", "ldarg.1", "blt.un.s",
                "ldarga.s 0", "call get_X", "ldarg.1", "ldc.r4 52", "add", "bge.un.s",
                "ldarga.s 0", "call get_Y", "ldarg.2", "blt.un.s",
                "ldarga.s 0", "call get_Y", "ldarg.2", "ldc.r4 32", "add", "clt", "br.s",
                "ldc.i4.0", "ret",
            ]
        },
        {
            "ConstantLoop", false,
            [
                "ldc.r4 0", "stloc.0", "ldc.i4.0", "stloc.1", "br.s",
                "ldloc.0", "ldarg.0", "add", "stloc.0", "ldloc.1", "ldc.i4.1", "add", "stloc.1",
                "ldloc.1", "ldc.i4.4", "blt.s", "ldloc.0", "ret",
            ],
            [
                "ldc.r4 0", "stloc.0", "ldc.i4.0", "stloc.1", "br.s",
                "ldloc.0", "ldarg.0", "add", "stloc.0", "ldloc.1", "ldc.i4.1", "add", "stloc.1",
                "ldloc.1", "ldc.i4.4", "clt", "stloc.2", "ldloc.2", "brtrue.s", "ldloc.0", "stloc.3", "br.s", "ldloc.3", "ret",
            ]
        },
        { "MakeState", false, ["ldarg.0", "ldarg.1", "newobj .ctor", "ret"], [] },
        { "MinimumIsAtLeastZero", false, ["ldarg.0", "ldarg.1", "call Min", "ldc.r4 0", "clt.un", "ldc.i4.0", "ceq", "ret"], [] },
        { "conditional", true, ["ldarg.0", "brfalse.s", "ldarg.1", "ret", "ldarg.2", "ret"], [] },
    };

    [Theory]
    [MemberData(nameof(Listings))]
    public void The_decoder_gives_the_instruction_list_of_the_input(string name, bool fsharp, string[] release, string[] debug)
    {
        var method = IlDecoder.Decode(fsharp ? FSharpInput(name) : CSharpInput(name));
        var actual = Listing(method);
        var accepted = debug.Length == 0 ? new[] { release } : [release, debug];
        Assert.True(accepted.Any(expected => expected.SequenceEqual(actual)), $"{name}: {Text(actual)}");
    }

    [Fact]
    public void The_decoder_resolves_the_operands()
    {
        var method = IlDecoder.Decode(CSharpInput("InsideTrack"));
        var call = method.Instructions.First(i => i.Name == "call");
        Assert.Equal(typeof(Inputs.Pointer).GetProperty("X")!.GetMethod, call.Operand);
        var branch = method.Instructions.First(i => i.Name == "blt.un.s");
        var target = Assert.IsType<int>(branch.Operand);
        Assert.Contains(method.Instructions, i => i.Offset == target);
        Assert.Equal(method.Instructions[^1].Offset, method.Instructions.Max(i => i.Offset));
    }

    [Theory]
    [InlineData("Boxed", "box", "the IL instruction box")]
    [InlineData("Text", "ldstr", "the IL instruction ldstr")]
    [InlineData("Wide", "conv.r8", "double arithmetic")]
    [InlineData("UnsignedDivision", "div.un", "the IL instruction div.un")]
    [InlineData("Lambda", "ldftn", "the IL instruction ldftn")]
    [InlineData("WithExceptionClause", "exception-clause", "an exception clause")]
    [InlineData("WithExceptionClauseOnLines", "exception-clause-lines", "an exception clause")]
    public void A_refused_construct_gives_the_source_file_and_line(string name, string marker, string construct)
    {
        var path = CSharpInputPath();
        var line = MarkerLine(path, marker);
        var error = Assert.Throws<TranslationException>(() => IlDecoder.Decode(CSharpInput(name)));
        Assert.Equal(path, error.SourceFile);
        Assert.Equal(line, error.Line);
        Assert.Equal(construct, error.Construct);
        Assert.False(string.IsNullOrWhiteSpace(error.Reason));
        Assert.Equal($"{path}:{line}: {construct} is not supported in a [Handwired] method: {error.Reason}", error.Message);
        Assert.Null(error.ParamName);
        Assert.IsAssignableFrom<ArgumentException>(error);
    }

    [Fact]
    public void A_refused_opcode_gives_the_offset_of_its_instruction()
    {
        var error = Assert.Throws<TranslationException>(() => IlDecoder.Decode(CSharpInput("UnsignedDivision")));
        // ldarg.0 and ldarg.1 are one byte each, and the Debug and the Release IL put no nop before div.un.
        Assert.Equal(2, error.IlOffset);
    }

    [Fact]
    public void A_method_with_no_body_is_a_bad_argument()
    {
        var method = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!;
        var error = Assert.Throws<ArgumentException>(() => IlDecoder.Decode(method));
        Assert.IsNotType<TranslationException>(error);
    }
}
