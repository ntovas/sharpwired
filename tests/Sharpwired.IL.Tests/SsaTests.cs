// No Handwired source. Handwired has no translator. The tests check the SSA form of the plan,
// section "Decisions", question 3: the abstract stack, the phis of Braun et al. (2013) for locals,
// arguments and stack slots at a merge, and the value types (Float, Int, Bit, Aggregate, Address).
// The Debug IL keeps more locals than the Release IL, so the tests check the phi leaves and types.

using System.Reflection.Metadata;
using static Sharpwired.IL.Tests.FrontEndSupport;

namespace Sharpwired.IL.Tests;

public class SsaTests
{
    private static SsaMethod Ssa(string name, bool fsharp = false) =>
        SsaBuilder.Build(ControlFlowGraph.Build(IlDecoder.Decode(fsharp ? FSharpInput(name) : CSharpInput(name))));

    // The values that a value reads through phis: a phi gives the leaves of its operands.
    private static HashSet<SsaValue> Leaves(SsaValue value)
    {
        var leaves = new HashSet<SsaValue>();
        var visited = new HashSet<SsaValue>();
        var work = new Stack<SsaValue>([value]);
        while (work.Count > 0)
        {
            var current = work.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Kind == SsaValueKind.Phi)
            {
                foreach (var operand in current.Operands)
                {
                    work.Push(operand);
                }
            }
            else
            {
                leaves.Add(current);
            }
        }

        return leaves;
    }

    private static string Describe(SsaValue value) => value.Kind switch
    {
        SsaValueKind.Argument => $"arg{value.Index}",
        SsaValueKind.InitialLocal => $"init{value.Index}",
        SsaValueKind.Constant => $"const {value.Constant}",
        SsaValueKind.Operation => IlDecoder.NameOf(value.Op),
        _ => "phi",
    };

    private static string[] Sorted(IEnumerable<SsaValue> values) => values.Select(Describe).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<SsaValue> Operations(SsaMethod method) => method.Blocks.SelectMany(b => b.Operations);

    private static IEnumerable<SsaValue> ReturnValues(SsaMethod method) => method.Blocks
        .Where(b => b.Terminator.Instruction?.OpCode == ILOpCode.Ret)
        .Select(b => Assert.Single(b.Terminator.Operands));

    private static IEnumerable<SsaValue> BranchConditions(SsaMethod method) => method.Blocks
        .Where(b => b.Terminator.Instruction is { } i && i.OpCode is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
        .Select(b => Assert.Single(b.Terminator.Operands));

    [Fact]
    public void A_conditional_expression_gives_one_stack_phi()
    {
        var method = Ssa("ConditionalPlusOne");
        var phi = Assert.Single(method.Phis);
        Assert.Equal(new SsaVariable(SsaVariableKind.StackSlot, 0), phi.Variable);
        Assert.Equal(SsaTypeKind.Float, phi.Type.Kind);
        Assert.Equal(3, phi.Block);
        // The predecessors are block 1 (the else side, first in IL) and block 2 (the then side).
        Assert.Equal(["arg2", "arg1"], phi.Operands.Select(Describe));
        var add = Assert.Single(Operations(method), v => v.Op == ILOpCode.Add);
        Assert.Contains(phi, add.Operands);
        Assert.Equal(SsaTypeKind.Bit, Assert.Single(BranchConditions(method)).Type.Kind);
    }

    [Fact]
    public void An_if_else_statement_gives_one_phi_of_the_two_assignments()
    {
        var method = Ssa("IfElse");
        var phi = Assert.Single(method.Phis);
        Assert.Equal(SsaTypeKind.Float, phi.Type.Kind);
        Assert.Equal(["add", "mul"], Sorted(Leaves(phi)));
        Assert.Equal(["add", "mul"], Sorted(Leaves(Assert.Single(ReturnValues(method)))));
    }

    [Fact]
    public void A_nested_if_gives_phis_of_the_three_values()
    {
        var method = Ssa("NestedIf");
        Assert.InRange(method.Phis.Count(), 1, 2);
        Assert.All(method.Phis, p => Assert.Equal(SsaTypeKind.Float, p.Type.Kind));
        Assert.Equal(["arg2", "arg3", "arg4"], Sorted(Leaves(Assert.Single(ReturnValues(method)))));
    }

    [Fact]
    public void An_and_chain_value_gives_a_bit_phi_of_the_last_comparison_and_zero()
    {
        var method = Ssa("AndValue");
        var phi = Assert.Single(method.Phis, p => p.Type.Kind == SsaTypeKind.Bit);
        Assert.Equal(new SsaVariable(SsaVariableKind.StackSlot, 0), phi.Variable);
        Assert.Equal(["cgt", "const 0"], Sorted(Leaves(phi)));
        Assert.Equal(phi, Assert.Single(BranchConditions(method)));
        Assert.Equal(["arg3", "arg4"], Sorted(ReturnValues(method).SelectMany(Leaves)));
    }

    [Fact]
    public void An_or_chain_value_gives_a_bit_phi_of_the_last_comparison_and_one()
    {
        var method = Ssa("OrValue");
        var phi = Assert.Single(method.Phis, p => p.Type.Kind == SsaTypeKind.Bit);
        Assert.Equal(["cgt", "const 1"], Sorted(Leaves(phi)));
        Assert.Equal(phi, Assert.Single(BranchConditions(method)));
    }

    [Fact]
    public void The_fsharp_debug_and_chain_gives_bit_phis_that_branches_read()
    {
        var method = Ssa("andChain", fsharp: true);
        var branchPhis = BranchConditions(method).Where(v => v.Kind == SsaValueKind.Phi).ToArray();
        Assert.All(branchPhis, p => Assert.Equal(SsaTypeKind.Bit, p.Type.Kind));
        Assert.All(branchPhis, p => Assert.Equal(["cgt", "const 0"], Sorted(Leaves(p))));
        Assert.Equal(["arg3", "arg4"], Sorted(ReturnValues(method).SelectMany(Leaves)));
#if DEBUG
        // The F# compiler writes each partial result of && as a value in Debug only, and the next
        // test branches on it. In Release it writes three brfalse instructions and no phi.
        Assert.Equal(2, branchPhis.Length);
#else
        Assert.Empty(branchPhis);
#endif
    }

    [Fact]
    public void A_loop_gives_header_phis_of_the_initial_values_and_the_back_edge_values()
    {
        var method = Ssa("ConstantLoop");
        var loop = Assert.Single(method.Graph.Loops);
        var header = method.Blocks[loop.Header];
        Assert.Equal(2, header.Phis.Length);
        Assert.Equal([SsaTypeKind.Float, SsaTypeKind.Int], header.Phis.Select(p => p.Type.Kind).Order());
        Assert.Equal(method.Phis.Count(), header.Phis.Length);
        var entrySide = method.Graph.Blocks[loop.Header].Predecessors.Single(p => !loop.Body.Contains(p));
        foreach (var phi in header.Phis)
        {
            Assert.Equal(SsaVariableKind.Local, phi.Variable.Kind);
            var predecessors = method.Graph.Blocks[loop.Header].Predecessors;
            Assert.Equal(predecessors.Length, phi.Operands.Length);
            for (var i = 0; i < predecessors.Length; i++)
            {
                Assert.Equal(predecessors[i] == entrySide ? "const 0" : "add", Describe(phi.Operands[i]));
            }
        }
    }

    [Fact]
    public void A_loop_at_the_method_start_gives_a_phi_of_the_argument()
    {
        var method = Ssa("DoWhileAtStart");
        var phi = Assert.Single(method.Phis);
        Assert.Equal(new SsaVariable(SsaVariableKind.Argument, 0), phi.Variable);
        Assert.Equal(SsaTypeKind.Float, phi.Type.Kind);
        Assert.Equal(["arg0", "mul"], Sorted(phi.Operands));
    }

    [Fact]
    public void A_bool_local_gives_bit_values()
    {
        var method = Ssa("BoolLocal");
        var condition = Assert.Single(BranchConditions(method), v => v.Kind == SsaValueKind.Phi);
        Assert.Equal(SsaTypeKind.Bit, condition.Type.Kind);
        Assert.Equal(new SsaVariable(SsaVariableKind.Local, 0), condition.Variable);
        Assert.Equal(["const 0", "const 1"], Sorted(condition.Operands));
        Assert.All(condition.Operands, v => Assert.Equal(SsaTypeKind.Bit, v.Type.Kind));
    }

    [Fact]
    public void A_property_read_through_an_argument_address_reads_the_argument()
    {
        var method = Ssa("InsideTrack");
        var getters = Operations(method).Where(v => v.Op == ILOpCode.Call).ToArray();
        Assert.Equal(4, getters.Length);
        foreach (var getter in getters)
        {
            var receiver = Assert.Single(getter.Operands);
            Assert.Equal("arg0", Describe(receiver));
            Assert.Equal(new SsaType(SsaTypeKind.Aggregate, typeof(Inputs.Pointer)), receiver.Type);
            Assert.Equal(SsaTypeKind.Float, getter.Type.Kind);
        }

        Assert.Equal(4, method.Blocks.SelectMany(b => b.Operations).Count(v => v.Op is ILOpCode.Ldarga or ILOpCode.Ldarga_s));
        Assert.All(Operations(method).Where(v => v.Op is ILOpCode.Ldarga or ILOpCode.Ldarga_s),
            v => Assert.Equal(SsaTypeKind.Address, v.Type.Kind));
    }

    [Fact]
    public void A_field_store_through_an_argument_address_gives_a_new_argument_value()
    {
        var method = Ssa("StoreField");
        var store = Assert.Single(Operations(method), v => v.Op == ILOpCode.Stfld);
        Assert.Equal(["arg0", "arg1"], store.Operands.Select(Describe));
        Assert.Equal(new SsaType(SsaTypeKind.Aggregate, typeof(Inputs.MutablePair)), store.Type);
        var reads = Operations(method).Where(v => v.Op == ILOpCode.Ldfld).ToArray();
        Assert.Equal(["A", "B"], reads.Select(r => ((System.Reflection.FieldInfo)r.Member!).Name).Order(StringComparer.Ordinal));
        Assert.All(reads, r => Assert.Same(store, Assert.Single(r.Operands)));
        Assert.All(reads, r => Assert.Equal(SsaTypeKind.Float, r.Type.Kind));
    }

    [Fact]
    public void A_constructor_call_on_a_local_address_gives_a_new_aggregate()
    {
        var method = Ssa("ConstructLocal");
        var created = Assert.Single(Operations(method), v => v.Op == ILOpCode.Newobj);
        Assert.Equal(["arg0", "const 1"], created.Operands.Select(Describe));
        var read = Assert.Single(Operations(method), v => v.Op == ILOpCode.Ldfld);
        Assert.Same(created, Assert.Single(read.Operands));
    }

    [Fact]
    public void Each_operand_is_a_value_with_a_type()
    {
        foreach (var name in new[] { "AffineChain", "ConditionalPlusOne", "InsideTrack", "ConstantLoop", "MakeState", "MinimumIsAtLeastZero", "IfElse", "NestedIf", "AndValue", "OrValue", "BoolLocal", "StoreField", "ConstructLocal" })
        {
            var method = Ssa(name);
            var values = method.Phis.Concat(Operations(method)).ToArray();
            Assert.All(values.SelectMany(v => v.Operands).Concat(method.Blocks.SelectMany(b => b.Terminator.Operands)), operand =>
            {
                // A trivial phi is removed, and each of its uses reads the value that replaced it.
                Assert.False(operand.IsRemoved);
                Assert.NotEqual(SsaTypeKind.None, operand.Type.Kind);
            });
        }
    }
}
