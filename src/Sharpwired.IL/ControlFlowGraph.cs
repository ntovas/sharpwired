// No Handwired source. Handwired has no translator. The control-flow graph of the plan, section
// "Decisions", question 3: a block starts at offset 0, at each branch target, and after each branch,
// ret and switch; the edges come from the branches, the fall-through and the switch targets. The
// dominators use the iterative algorithm of Cooper, Harvey and Kennedy, "A Simple, Fast Dominance
// Algorithm" (2001). A natural loop (question 5) has a back edge: an edge whose target dominates
// its source. A backward branch outside a natural loop is refused (section "Emission rules").

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Sharpwired.IL;

/// <summary>
/// One basic block. Jump is the target block of a branch, Next the block of the fall-through edge
/// (null after br and ret). SwitchTargets are the case blocks of a switch, in case order.
/// </summary>
internal sealed class BasicBlock
{
    internal BasicBlock(int index, int startOffset, int firstInstruction, int instructionCount)
    {
        Index = index;
        StartOffset = startOffset;
        FirstInstruction = firstInstruction;
        InstructionCount = instructionCount;
    }

    public int Index { get; internal set; }

    public int StartOffset { get; }

    public int FirstInstruction { get; }

    public int InstructionCount { get; }

    /// <summary>The last instruction when it is a branch, a switch or ret; otherwise null.</summary>
    public IlInstruction? Terminator { get; internal set; }

    public int? Jump { get; internal set; }

    public int? Next { get; internal set; }

    public ImmutableArray<int> SwitchTargets { get; internal set; } = [];

    /// <summary>The successor blocks, each one time.</summary>
    public ImmutableArray<int> Successors { get; internal set; } = [];

    /// <summary>The predecessor blocks, each one time, in block order.</summary>
    public ImmutableArray<int> Predecessors { get; internal set; } = [];
}

/// <summary>A natural loop: the header, the blocks of the body (with the header), the sources of the back edges and the exit edges.</summary>
internal sealed record NaturalLoop(int Header, ImmutableArray<int> Body, ImmutableArray<int> BackEdgeSources, ImmutableArray<(int From, int To)> ExitEdges);

/// <summary>The control-flow graph of a decoded method. Block 0 is the entry and has no predecessor.</summary>
internal sealed class ControlFlowGraph
{
    private readonly int[] immediateDominators;
    private readonly int[] reversePostorderNumbers;

    private ControlFlowGraph(DecodedMethod method, ImmutableArray<BasicBlock> blocks)
    {
        Method = method;
        Blocks = blocks;
        ReversePostorder = ComputeReversePostorder(blocks);
        reversePostorderNumbers = new int[blocks.Length];
        for (var i = 0; i < ReversePostorder.Length; i++)
        {
            reversePostorderNumbers[ReversePostorder[i]] = i;
        }

        immediateDominators = ComputeDominators();
        Loops = FindLoops();
        CheckBackwardBranches();
    }

    public DecodedMethod Method { get; }

    public ImmutableArray<BasicBlock> Blocks { get; }

    /// <summary>The blocks in reverse postorder from the entry. Each forward edge goes to a later block.</summary>
    public ImmutableArray<int> ReversePostorder { get; }

    /// <summary>The natural loops, one for each header, in header order.</summary>
    public ImmutableArray<NaturalLoop> Loops { get; }

    /// <summary>The immediate dominator of a block, or -1 for the entry.</summary>
    public int ImmediateDominator(int block) => immediateDominators[block];

    /// <summary>True when each path from the entry to block b goes through block a. A block dominates itself.</summary>
    public bool Dominates(int a, int b)
    {
        for (var current = b; current >= 0; current = immediateDominators[current])
        {
            if (current == a)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the graph. Throws <see cref="TranslationException"/> for a branch into an instruction or a body with no end.</summary>
    public static ControlFlowGraph Build(DecodedMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var instructions = method.Instructions;
        if (instructions.IsEmpty)
        {
            throw new ArgumentException($"the method {IlDecoder.Describe(method.Method)} has no instructions", nameof(method));
        }

        var leaders = new SortedSet<int> { 0 };
        var targetOffsets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            var targets = TargetsOf(instruction);
            foreach (var target in targets)
            {
                if (method.IndexOf(target) < 0)
                {
                    throw method.Refuse(instruction.Offset, "a branch", "the branch target is not the start of an instruction");
                }

                leaders.Add(method.IndexOf(target));
                targetOffsets.Add(target);
            }

            if ((instruction.IsBranch || instruction.OpCode is ILOpCode.Switch or ILOpCode.Ret) && method.IndexOf(instruction.NextOffset) >= 0)
            {
                leaders.Add(method.IndexOf(instruction.NextOffset));
            }
        }

        var blocks = new List<BasicBlock>();
        var blockOfOffset = new Dictionary<int, BasicBlock>();
        if (targetOffsets.Contains(instructions[0].Offset))
        {
            // The entry must have no predecessor, so a branch to offset 0 gets an empty entry block before it.
            blocks.Add(new BasicBlock(0, instructions[0].Offset, 0, 0));
        }

        var starts = leaders.ToArray();
        for (var i = 0; i < starts.Length; i++)
        {
            var end = i + 1 < starts.Length ? starts[i + 1] : instructions.Length;
            var block = new BasicBlock(blocks.Count, instructions[starts[i]].Offset, starts[i], end - starts[i]);
            blocks.Add(block);
            blockOfOffset[block.StartOffset] = block;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            ConnectEdges(method, blocks, i, blockOfOffset);
        }

        return new ControlFlowGraph(method, RemoveUnreachable(blocks));
    }

    private static IEnumerable<int> TargetsOf(IlInstruction instruction) => instruction.OpCode == ILOpCode.Switch
        ? instruction.SwitchTargets
        : instruction.IsBranch ? [instruction.BranchTarget] : [];

    private static void ConnectEdges(DecodedMethod method, List<BasicBlock> blocks, int index, Dictionary<int, BasicBlock> blockOfOffset)
    {
        var block = blocks[index];
        BasicBlock? following = index + 1 < blocks.Count ? blocks[index + 1] : null;
        if (block.InstructionCount == 0)
        {
            block.Next = following!.Index;
            return;
        }

        var last = method.Instructions[block.FirstInstruction + block.InstructionCount - 1];
        if (last.IsBranch || last.OpCode is ILOpCode.Switch or ILOpCode.Ret)
        {
            block.Terminator = last;
        }

        if (last.OpCode == ILOpCode.Ret)
        {
            return;
        }

        if (last.OpCode == ILOpCode.Switch)
        {
            block.SwitchTargets = [.. last.SwitchTargets.Select(t => blockOfOffset[t].Index)];
        }
        else if (last.IsBranch)
        {
            block.Jump = blockOfOffset[last.BranchTarget].Index;
            if (last.IsJump)
            {
                return;
            }
        }

        if (following is null)
        {
            throw method.Refuse(last.Offset, "the end of the method body", "the IL runs past the last instruction");
        }

        block.Next = following.Index;
    }

    private static IEnumerable<int> SuccessorsOf(BasicBlock block)
    {
        var successors = new List<int>();
        if (block.Jump is { } jump)
        {
            successors.Add(jump);
        }

        successors.AddRange(block.SwitchTargets);
        if (block.Next is { } next)
        {
            successors.Add(next);
        }

        return successors.Distinct();
    }

    private static ImmutableArray<BasicBlock> RemoveUnreachable(List<BasicBlock> blocks)
    {
        var reached = new bool[blocks.Count];
        var stack = new Stack<int>([0]);
        reached[0] = true;
        while (stack.Count > 0)
        {
            foreach (var successor in SuccessorsOf(blocks[stack.Pop()]))
            {
                if (!reached[successor])
                {
                    reached[successor] = true;
                    stack.Push(successor);
                }
            }
        }

        var kept = blocks.Where(b => reached[b.Index]).ToList();
        var newIndex = new int[blocks.Count];
        for (var i = 0; i < kept.Count; i++)
        {
            newIndex[kept[i].Index] = i;
        }

        foreach (var block in kept)
        {
            block.Jump = block.Jump is { } jump ? newIndex[jump] : null;
            block.Next = block.Next is { } next ? newIndex[next] : null;
            block.SwitchTargets = [.. block.SwitchTargets.Select(t => newIndex[t])];
        }

        for (var i = 0; i < kept.Count; i++)
        {
            kept[i].Index = i;
        }

        var predecessors = kept.Select(_ => new SortedSet<int>()).ToArray();
        foreach (var block in kept)
        {
            block.Successors = [.. SuccessorsOf(block)];
            foreach (var successor in block.Successors)
            {
                predecessors[successor].Add(block.Index);
            }
        }

        foreach (var block in kept)
        {
            block.Predecessors = [.. predecessors[block.Index]];
        }

        return [.. kept];
    }

    private static ImmutableArray<int> ComputeReversePostorder(ImmutableArray<BasicBlock> blocks)
    {
        var postorder = new List<int>();
        var visited = new bool[blocks.Length];
        var stack = new Stack<(int Block, int NextSuccessor)>();
        stack.Push((0, 0));
        visited[0] = true;
        while (stack.Count > 0)
        {
            var (block, next) = stack.Pop();
            var successors = blocks[block].Successors;
            if (next < successors.Length)
            {
                stack.Push((block, next + 1));
                var successor = successors[next];
                if (!visited[successor])
                {
                    visited[successor] = true;
                    stack.Push((successor, 0));
                }
            }
            else
            {
                postorder.Add(block);
            }
        }

        postorder.Reverse();
        return [.. postorder];
    }

    private int[] ComputeDominators()
    {
        var idom = Enumerable.Repeat(-1, Blocks.Length).ToArray();
        idom[0] = 0;
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in ReversePostorder.Skip(1))
            {
                var candidate = -1;
                foreach (var predecessor in Blocks[block].Predecessors)
                {
                    if (idom[predecessor] < 0)
                    {
                        continue;
                    }

                    candidate = candidate < 0 ? predecessor : Intersect(idom, predecessor, candidate);
                }

                if (idom[block] != candidate)
                {
                    idom[block] = candidate;
                    changed = true;
                }
            }
        }

        idom[0] = -1;
        return idom;
    }

    private int Intersect(int[] idom, int a, int b)
    {
        while (a != b)
        {
            while (reversePostorderNumbers[a] > reversePostorderNumbers[b])
            {
                a = idom[a];
            }

            while (reversePostorderNumbers[b] > reversePostorderNumbers[a])
            {
                b = idom[b];
            }
        }

        return a;
    }

    // The plan, section "Emission rules": a backward branch that is not a back edge of a natural loop
    // is refused. Two checks find it:
    // - A retreating edge (the target comes at or before the source in reverse postorder) whose
    //   target does not dominate its source. Such an edge closes a cycle with two entries (an
    //   irreducible cycle, or a jump into a loop body). In a reducible graph, each retreating edge is
    //   a back edge, in each depth-first order.
    // - An edge to a block at or before its source in IL order, with no natural loop that holds the
    //   two blocks. The lowering emits the blocks of an acyclic region in IL order, so each edge of
    //   such a region must go forward.
    private void CheckBackwardBranches()
    {
        foreach (var block in Blocks)
        {
            foreach (var successor in block.Successors)
            {
                var retreating = reversePostorderNumbers[successor] <= reversePostorderNumbers[block.Index] && !Dominates(successor, block.Index);
                var backward = successor <= block.Index && !Loops.Any(loop => loop.Body.Contains(block.Index) && loop.Body.Contains(successor));
                if (retreating || backward)
                {
                    var last = Method.Instructions[block.FirstInstruction + Math.Max(block.InstructionCount - 1, 0)];
                    throw Method.Refuse(last.Offset, "a backward branch outside a natural loop",
                        "the program accepts a backward branch only as the back edge of a loop with one entry");
                }
            }
        }
    }

    private ImmutableArray<NaturalLoop> FindLoops()
    {
        var sourcesOfHeader = new SortedDictionary<int, SortedSet<int>>();
        foreach (var block in Blocks)
        {
            foreach (var successor in block.Successors)
            {
                if (Dominates(successor, block.Index))
                {
                    if (!sourcesOfHeader.TryGetValue(successor, out var sources))
                    {
                        sources = [];
                        sourcesOfHeader[successor] = sources;
                    }

                    sources.Add(block.Index);
                }
            }
        }

        var loops = ImmutableArray.CreateBuilder<NaturalLoop>();
        foreach (var (header, sources) in sourcesOfHeader)
        {
            var body = new SortedSet<int> { header };
            var work = new Stack<int>(sources);
            while (work.Count > 0)
            {
                var block = work.Pop();
                if (body.Add(block))
                {
                    foreach (var predecessor in Blocks[block].Predecessors)
                    {
                        work.Push(predecessor);
                    }
                }
            }

            var exits = body.SelectMany(b => Blocks[b].Successors.Where(s => !body.Contains(s)).Select(s => (b, s)))
                .OrderBy(e => e.b).ThenBy(e => e.s).ToImmutableArray();
            loops.Add(new NaturalLoop(header, [.. body], [.. sources], exits));
        }

        return loops.ToImmutable();
    }
}
