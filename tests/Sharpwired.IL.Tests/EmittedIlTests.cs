// No Handwired source. Handwired has no translator. Tests of the IL front end for IL forms that the
// C# and F# compilers do not write (final fix wave, item G). The forms are long-form branches, a
// switch, an unreachable block, and a merge with different stack depths or value types. The forms
// also include an address that depends on a branch, and the tail. prefix. Each input method is built
// with System.Reflection.Emit in a dynamic assembly. A dynamic assembly has no file and no PDB, so a
// refusal names the form "Type.Method+IL_offset". The tests also check that a resolve error becomes a refusal.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using Pointer = Sharpwired.IL.Tests.Inputs.Pointer;
using static Sharpwired.IL.Tests.FrontEndSupport;

namespace Sharpwired.IL.Tests;

public class EmittedIlTests
{
    [Fact]
    public void A_long_form_branch_gives_the_offset_of_its_target()
    {
        var method = EmittedIl.Method("LongBranch", typeof(float), [typeof(bool)], il =>
        {
            var target = il.DefineLabel();
            var end = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, target);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Br, end);
            il.MarkLabel(target);
            il.Emit(OpCodes.Ldc_R4, 2f);
            il.MarkLabel(end);
            il.Emit(OpCodes.Ret);
        });
        var decoded = IlDecoder.Decode(method);
        Assert.Equal(["ldarg.0", "brtrue", "ldc.r4 1", "br", "ldc.r4 2", "ret"], Listing(decoded));

        // ldarg.0 has 1 byte, and brtrue, ldc.r4 and br have 5 bytes each. So the target is at 16, and the end is at 21.
        Assert.Equal(16, decoded.Instructions[1].Operand);
        Assert.Equal(21, decoded.Instructions[3].Operand);
        Assert.Equal(4, ControlFlowGraph.Build(decoded).Blocks.Length);
    }

    [Fact]
    public void A_switch_gives_the_offset_of_each_target_and_an_edge_to_each_target()
    {
        var method = EmittedIl.Method("Switch", typeof(float), [typeof(int)], il =>
        {
            Label[] cases = [il.DefineLabel(), il.DefineLabel(), il.DefineLabel()];
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Switch, cases);
            il.Emit(OpCodes.Ldc_R4, 9f);
            il.Emit(OpCodes.Ret);
            for (var k = 0; k < cases.Length; k++)
            {
                il.MarkLabel(cases[k]);
                il.Emit(OpCodes.Ldc_R4, (float)k);
                il.Emit(OpCodes.Ret);
            }
        });
        var decoded = IlDecoder.Decode(method);
        var targets = Assert.IsType<ImmutableArray<int>>(decoded.Instructions[1].Operand);

        // ldarg.0 has 1 byte, and the switch with 3 targets has 17 bytes. The default code (6 bytes)
        // starts at 18. Each case has 6 bytes.
        Assert.Equal([24, 30, 36], targets.ToArray());
        Assert.Equal(["ldc.r4 0", "ldc.r4 1", "ldc.r4 2"], targets.Select(offset => Format(decoded.Instructions[decoded.IndexOf(offset)])));
        var graph = ControlFlowGraph.Build(decoded);
        Assert.Equal(5, graph.Blocks.Length);
        Assert.Equal([1, 2, 3, 4], graph.Blocks[0].Successors.Order());
    }

    [Fact]
    public void An_unreachable_block_is_removed_from_the_graph()
    {
        var method = EmittedIl.Method("Unreachable", typeof(float), [], il =>
        {
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Ret);
            il.Emit(OpCodes.Ldc_R4, 2f);
            il.Emit(OpCodes.Ret);
        });
        var block = Assert.Single(ControlFlowGraph.Build(IlDecoder.Decode(method)).Blocks);
        Assert.Equal(2, block.InstructionCount);
    }

    // The edge from block 0 has the depth 0 at the merge, and the fall-through edge has the depth 1.
    [Fact]
    public void A_merge_with_different_stack_depths_is_refused()
    {
        var method = EmittedIl.Method("DepthMerge", typeof(float), [typeof(bool)], il =>
        {
            var merge = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, merge);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.MarkLabel(merge);
            il.Emit(OpCodes.Ldc_R4, 2f);
            il.Emit(OpCodes.Ret);
        });
        var error = Assert.Throws<TranslationException>(() => SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(method))));
        Assert.Equal(8, error.IlOffset);
        Assert.Equal(
            $"{method.DeclaringType!.Name}.DepthMerge+IL_0008: a merge with different stack depths is not supported in a [Handwired] method: "
            + "the IL stack must have the same depth on each edge into a block",
            error.Message);
    }

    [Fact]
    public void A_merge_of_a_float_and_an_int_is_refused()
    {
        var method = EmittedIl.Method("TypeMerge", typeof(float), [typeof(bool)], il =>
        {
            var other = il.DefineLabel();
            var merge = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, other);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Br_S, merge);
            il.MarkLabel(other);
            il.Emit(OpCodes.Ldc_I4, 5);
            il.MarkLabel(merge);
            il.Emit(OpCodes.Ret);
        });
        var error = Assert.Throws<TranslationException>(() => SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(method))));
        Assert.Equal("a merge of values with different types", error.Construct);
        Assert.Equal("each edge into a block must give the same value type", error.Reason);
    }

    [Fact]
    public void An_address_that_depends_on_a_branch_is_refused()
    {
        var getX = typeof(Pointer).GetProperty(nameof(Pointer.X))!.GetMethod!;
        var method = EmittedIl.Method("AddressMerge", typeof(float), [typeof(bool), typeof(Pointer), typeof(Pointer)], il =>
        {
            var second = il.DefineLabel();
            var merge = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, second);
            il.Emit(OpCodes.Ldarga_S, (byte)1);
            il.Emit(OpCodes.Br_S, merge);
            il.MarkLabel(second);
            il.Emit(OpCodes.Ldarga_S, (byte)2);
            il.MarkLabel(merge);
            il.Emit(OpCodes.Call, getX);
            il.Emit(OpCodes.Ret);
        });
        var error = Assert.Throws<TranslationException>(() => SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(method))));
        Assert.Equal("an address that depends on a branch", error.Construct);
        Assert.Equal("the program reads a field through a fixed local or argument only", error.Reason);
    }

    [Fact]
    public void The_tail_prefix_is_accepted_only_before_a_call()
    {
        var callee = typeof(Math).GetMethod(nameof(Math.Abs), [typeof(float)])!;
        var accepted = EmittedIl.Method("TailCall", typeof(float), [typeof(float)], il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Tailcall);
            il.Emit(OpCodes.Call, callee);
            il.Emit(OpCodes.Ret);
        });
        Assert.Equal(["ldarg.0", "tail.", "call Abs", "ret"], Listing(IlDecoder.Decode(accepted)));
        var ssa = SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(accepted)));
        Assert.DoesNotContain(ssa.Blocks.SelectMany(block => block.Operations), operation => operation.Op == System.Reflection.Metadata.ILOpCode.Tail);

        var refused = EmittedIl.Method("TailNoCall", typeof(float), [typeof(float)], il =>
        {
            il.Emit(OpCodes.Tailcall);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
        });
        var error = Assert.Throws<TranslationException>(() => IlDecoder.Decode(refused));
        Assert.Equal("the IL instruction tail.", error.Construct);
        Assert.Equal("the tail. prefix must come before a call", error.Reason);
        Assert.Equal(0, error.IlOffset);
    }

    // A token whose type, member or assembly is missing gives a refusal with the file and the line.
    [Theory]
    [InlineData(typeof(TypeLoadException))]
    [InlineData(typeof(MissingMethodException))]
    [InlineData(typeof(MissingFieldException))]
    [InlineData(typeof(FileNotFoundException))]
    public void A_resolve_error_becomes_a_refusal_with_the_file_and_line(Type errorType)
    {
        var partial = IlDecoder.Decode(CSharpInput("AffineChain"));
        var error = Assert.Throws<TranslationException>(
            () => IlDecoder.Resolve(partial, 0, () => throw (Exception)Activator.CreateInstance(errorType, "the test error")!));
        Assert.Equal(CSharpInputPath(), error.SourceFile);
        Assert.NotNull(error.Line);
        Assert.Equal("a token", error.Construct);
        Assert.Equal("the module does not resolve the token: the test error", error.Reason);
    }
}

/// <summary>Static methods built with System.Reflection.Emit, each in its own type of one dynamic assembly.</summary>
internal static class EmittedIl
{
    private static readonly ModuleBuilder Module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Sharpwired.IL.EmittedInputs"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Sharpwired.IL.EmittedInputs");

    private static int count;

    public static MethodInfo Method(string name, Type returnType, Type[] parameters, Action<ILGenerator> body)
    {
        lock (Module)
        {
            var type = Module.DefineType($"Emitted{count++}", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, parameters);
            body(method.GetILGenerator());
            return type.CreateType().GetMethod(name)!;
        }
    }
}
