// No Handwired source. Handwired has no translator. The tests check the control-flow graph of the
// plan, section "Decisions", question 3 (blocks and edges) and question 5 (dominators and natural
// loops). Each test accepts the Debug and the Release graph where the two IL forms differ.

using static Sharpwired.IL.Tests.FrontEndSupport;

namespace Sharpwired.IL.Tests;

public class ControlFlowTests
{
    // A graph in a compact form: the edges as "from>to", sorted, and the immediate dominators.
    private sealed record Shape(string[] Edges, int[] Dominators);

    private static Shape ShapeOf(ControlFlowGraph graph) => new(
        graph.Blocks.SelectMany(b => b.Successors.Select(s => $"{b.Index}>{s}")).Order(StringComparer.Ordinal).ToArray(),
        graph.Blocks.Select(b => graph.ImmediateDominator(b.Index)).ToArray());

    private static void AssertShape(ControlFlowGraph graph, params Shape[] accepted)
    {
        var actual = ShapeOf(graph);
        var text = $"edges {string.Join(" ", actual.Edges)}; dominators {string.Join(" ", actual.Dominators)}";
        Assert.True(accepted.Any(s => s.Edges.Order(StringComparer.Ordinal).SequenceEqual(actual.Edges) && s.Dominators.SequenceEqual(actual.Dominators)), text);
        foreach (var block in graph.Blocks)
        {
            foreach (var successor in block.Successors)
            {
                Assert.Contains(block.Index, graph.Blocks[successor].Predecessors);
            }
        }
    }

    [Fact]
    public void A_conditional_expression_gives_a_diamond()
    {
        var graph = ControlFlowGraph.Build(IlDecoder.Decode(CSharpInput("ConditionalPlusOne")));
        Assert.Equal(4, graph.Blocks.Length);
        AssertShape(graph, new Shape(["0>1", "0>2", "1>3", "2>3"], [-1, 0, 0, 0]));
        Assert.Empty(graph.Loops);
        Assert.Equal(["brtrue.s", "br.s", "ldarg.1", "ret"], graph.Blocks.Select(b => graph.Method.Instructions[b.FirstInstruction + b.InstructionCount - 1].Name));
    }

    [Fact]
    public void An_and_chain_gives_one_false_block_for_each_test()
    {
        var graph = ControlFlowGraph.Build(IlDecoder.Decode(CSharpInput("InsideTrack")));
        AssertShape(graph,
            new Shape(["0>1", "0>4", "1>2", "1>4", "2>3", "2>4"], [-1, 0, 1, 2, 0]),
            new Shape(["0>1", "0>4", "1>2", "1>4", "2>3", "2>4", "3>5", "4>5"], [-1, 0, 1, 2, 0, 0]));
        Assert.Empty(graph.Loops);
        Assert.True(graph.Dominates(0, 3));
        Assert.True(graph.Dominates(1, 3));
        Assert.False(graph.Dominates(1, 4));
    }

    [Fact]
    public void A_csharp_for_loop_has_one_natural_loop_at_its_condition()
    {
        var graph = ControlFlowGraph.Build(IlDecoder.Decode(CSharpInput("ConstantLoop")));
        AssertShape(graph,
            new Shape(["0>2", "1>2", "2>1", "2>3"], [-1, 2, 0, 2]),
            new Shape(["0>2", "1>2", "2>1", "2>3", "3>4"], [-1, 2, 0, 2, 3]));
        var loop = Assert.Single(graph.Loops);
        Assert.Equal(2, loop.Header);
        Assert.Equal(new[] { 1, 2 }, loop.Body);
        Assert.Equal(new[] { 1 }, loop.BackEdgeSources);
        Assert.Equal(new[] { (2, 3) }, loop.ExitEdges);
    }

    [Fact]
    public void An_fsharp_for_in_loop_has_one_natural_loop_at_its_condition()
    {
        var graph = ControlFlowGraph.Build(IlDecoder.Decode(FSharpInput("constantLoop")));
        AssertShape(graph, new Shape(["0>2", "1>2", "2>1", "2>3"], [-1, 2, 0, 2]));
        var loop = Assert.Single(graph.Loops);
        Assert.Equal(2, loop.Header);
        Assert.Equal(new[] { 1, 2 }, loop.Body);
        Assert.Equal(new[] { 1 }, loop.BackEdgeSources);
        Assert.Equal(new[] { (2, 3) }, loop.ExitEdges);
    }

    [Fact]
    public void A_loop_at_the_method_start_gets_an_entry_block_with_no_predecessor()
    {
        // In Release the do-while body starts at offset 0, so offset 0 is a branch target. The graph
        // then starts with an empty entry block. In Debug a nop comes first.
        var graph = ControlFlowGraph.Build(IlDecoder.Decode(CSharpInput("DoWhileAtStart")));
        Assert.Empty(graph.Blocks[0].Predecessors);
        var loop = Assert.Single(graph.Loops);
        Assert.NotEqual(0, loop.Header);
        Assert.Equal(new[] { loop.Header }, loop.BackEdgeSources);
        Assert.Equal(0, graph.ImmediateDominator(loop.Header));
        Assert.Single(loop.ExitEdges);
    }

    [Fact]
    public void The_blocks_cover_each_instruction_one_time_in_il_order()
    {
        foreach (var name in new[] { "ConditionalPlusOne", "InsideTrack", "ConstantLoop", "IfElse", "NestedIf", "AndValue", "DoWhileAtStart" })
        {
            var method = IlDecoder.Decode(CSharpInput(name));
            var graph = ControlFlowGraph.Build(method);
            var covered = graph.Blocks.SelectMany(b => Enumerable.Range(b.FirstInstruction, b.InstructionCount)).ToArray();
            Assert.Equal(Enumerable.Range(0, method.Instructions.Length), covered);
            foreach (var block in graph.Blocks.Where(b => b.InstructionCount > 0))
            {
                Assert.Equal(method.Instructions[block.FirstInstruction].Offset, block.StartOffset);
            }
        }
    }
}
