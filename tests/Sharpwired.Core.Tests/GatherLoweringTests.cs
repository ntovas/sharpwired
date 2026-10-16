// Handwired source: tests/runtime/gather-lowering.test.ts (the reference parts of the 'recognition'
// cases that use emitArgmax, emitFirstActive, inlineCircuit and validateOneHotGroups). Each test
// name holds the Handwired test name. The gather statistics and the tensor agreement of these
// tests, and the other tests of the file, need the compiler. PHASE2-TENSOR-ASSERTIONS.md lists them.

namespace Sharpwired.Core.Tests;

public class GatherLoweringTests
{
    // 'recognition' / 'recognition survives the affine inlining of argmax selection wires' (reference part).
    [Fact]
    public void Recognition_survives_the_affine_inlining_of_argmax_selection_wires()
    {
        const int ScoreCount = 3;
        var circuit = new Circuit(ScoreCount + ScoreCount + 1);
        var scores = Enumerable.Range(0, ScoreCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(ScoreCount, ScoreCount).Select(circuit.Source).ToArray();
        var unit = circuit.Source(ScoreCount + ScoreCount);
        foreach (var score in scores) circuit.AssumeRange(score, 0, 1);
        foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
        circuit.AssumeRange(unit, 1, 1);
        var winner = circuit.Argmax(scores, unit);
        var read = circuit.Affine([.. winner.Selected.Select((bit, candidate) => new Term(circuit.Multiply(bit, values[candidate]), 1))]);
        circuit.ValidateCircuit();
        foreach (var scoreValues in new[] { new[] { 0.2f, 0.9f, 0.5f }, [0.9f, 0.2f, 0.5f], [0.5f, 0.5f, 0.5f], [0.1f, 0.2f, 0.9f] })
        {
            float[] source = [.. scoreValues, 17.5f, 23.25f, 31.125f, 1];
            var reference = circuit.EvaluateReference(source);
            var winnerIndex = winner.Selected.ToList().FindIndex(bit => reference[bit.Position] == 1);
            Js.Is(source[ScoreCount + winnerIndex], reference[read.Position]);
        }
    }

    // 'recognition' / 'emitArgmax declares its group; emitFirstActive only for proven bits'.
    [Fact]
    public void Argmax_declares_its_group_and_first_active_only_for_proven_bits()
    {
        var circuit = new Circuit(5);
        Wire[] scores = [circuit.Source(0), circuit.Source(1), circuit.Source(2)];
        var gate = circuit.Source(3);
        var unit = circuit.Source(4);
        foreach (var wire in scores.Append(gate)) circuit.AssumeRange(wire, 0, 1);
        circuit.AssumeRange(unit, 1, 1);
        // Argmax declares its group without a condition: fractional scores only feed the boolean
        // comparisons, and the tree still routes the unit down one path.
        var winner = circuit.Argmax(scores, unit);
        Assert.Single(circuit.OneHotGroups);
        Assert.Equal(winner.Selected, circuit.OneHotGroups[0].Members);
        Assert.False(circuit.OneHotGroups[0].AllowAllZero);
        // A 0..1 range admits fractional values, so this selection is not a proven one-hot and stays undeclared.
        circuit.FirstActive(gate, scores);
        Assert.Single(circuit.OneHotGroups);
        // Boolean atoms give exact bits, and the range of the unit wire pins it to 1, so this
        // selection is a proven one-hot or all zeros, and FirstActive declares it.
        var bits = scores.Select(circuit.Boolean).ToArray();
        var selected = circuit.FirstActive(unit, bits);
        Assert.Equal(2, circuit.OneHotGroups.Count);
        Assert.Equal(selected, circuit.OneHotGroups[1].Members);
        Assert.True(circuit.OneHotGroups[1].AllowAllZero);
        circuit.ValidateOneHotGroups(circuit.EvaluateReference([0.5f, 0.75f, 0.25f, 0, 1]));
    }

    // 'recognition' / 'fractional first-active selections never lower a masked sum' (reference part).
    [Fact]
    public void Fractional_first_active_selections_declare_no_group()
    {
        // The preconditions of FirstActive only bound its inputs to 0..1, and fractional scores are
        // legal: candidates [0.5, 0] route the mass 0.25 to each output, so the selection is not a
        // one-hot. In Handwired, a declaration without a condition once lowered this read to a gather
        // that returned 50, while the reference evaluator computes 75.
        var circuit = new Circuit(5);
        var gate = circuit.Source(0);
        Wire[] candidates = [circuit.Source(1), circuit.Source(2)];
        Wire[] values = [circuit.Source(3), circuit.Source(4)];
        foreach (var wire in candidates.Prepend(gate)) circuit.AssumeRange(wire, 0, 1);
        foreach (var value in values) circuit.AssumeRange(value, 0, 1000);
        var selected = circuit.FirstActive(gate, candidates);
        var read = circuit.Affine([.. selected.Select((bit, item) => new Term(circuit.Multiply(bit, values[item]), 1))]);
        Assert.Empty(circuit.OneHotGroups);
        circuit.ValidateCircuit();
        Js.Is(75, circuit.EvaluateReference([1, 0.5f, 0, 100, 200])[read.Position]);
    }

    // 'recognition' / 'emitFirstActive accepts candidates that fold to one shared constant'.
    [Fact]
    public void First_active_accepts_candidates_that_fold_to_one_shared_constant()
    {
        // The candidates share a constant, but each multiplication keeps its own routing operation.
        // Their group that allows all zeros is legal, and the validator can check it.
        var circuit = new Circuit(1);
        var unit = circuit.Source(0);
        circuit.AssumeRange(unit, 1, 1);
        var zero = circuit.Constant(0);
        var selected = circuit.FirstActive(unit, [zero, zero]);
        Assert.Equal(2, selected.Length);
        Assert.Single(circuit.OneHotGroups);
        var values = circuit.EvaluateReference([1]);
        Assert.Equal([0.0, 0.0], selected.Select(wire => (double)values[wire.Position]));
        circuit.ValidateOneHotGroups(values);
    }

    // 'recognition' / 'inlineCircuit carries declared groups into the target circuit' (reference part).
    [Fact]
    public void Inline_circuit_carries_declared_groups_into_the_target_circuit()
    {
        var child = new Circuit(4);
        Wire[] childBits = [child.Source(0), child.Source(1)];
        Wire[] childValues = [child.Source(2), child.Source(3)];
        child.AssumeOneHot(childBits, allowAllZero: true);
        foreach (var value in childValues) child.AssumeRange(value, 0, 100);
        var childRead = child.Affine([.. childBits.Select((bit, slot) => new Term(child.Multiply(bit, childValues[slot]), 1))]);
        var parent = new Circuit(5);
        Wire[] parentBits = [parent.Source(1), parent.Source(2)];
        Wire[] parentValues = [parent.Source(3), parent.Source(4)];
        foreach (var wire in parentBits) parent.AssumeRange(wire, 0, 1);
        foreach (var wire in parentValues) parent.AssumeRange(wire, 0, 100);
        var wireMap = parent.InlineCircuit(child, [.. parentBits, .. parentValues]);

        var group = Assert.Single(parent.OneHotGroups);
        Assert.Equal<Wire>(parentBits, group.Members);
        Assert.True(group.AllowAllZero);
        var read = wireMap[childRead.Position];
        parent.ValidateCircuit();
        var values = parent.EvaluateReference([9, 0, 1, 5, 7]);
        Js.Is(7, values[read.Position]);
        parent.ValidateOneHotGroups(values);
    }

    // 'recognition' / 'a violated one-hot contract diverges and validation catches it' (reference part).
    [Fact]
    public void A_violated_one_hot_contract_is_caught_by_validation()
    {
        // Two bits set at once: the reference evaluator adds both values, while the gather of phase 2
        // reads the slot of the corrupted index. The lowering contract is the declared invariant, so
        // ValidateOneHotGroups is the check that a trainer must run before it writes bits back.
        const int SlotCount = 4;
        var circuit = new Circuit(SlotCount * 2);
        var bits = Enumerable.Range(0, SlotCount).Select(circuit.Source).ToArray();
        var values = Enumerable.Range(SlotCount, SlotCount).Select(circuit.Source).ToArray();
        circuit.AssumeOneHot(bits);
        foreach (var value in values) circuit.AssumeRange(value, 0, 4096);
        var read = circuit.Affine([.. bits.Select((bit, slot) => new Term(circuit.Multiply(bit, values[slot]), 1))]);
        var source = new float[SlotCount * 2];
        source[0] = 1;
        for (var slot = 0; slot < SlotCount; slot++) source[SlotCount + slot] = (float)(10.5 + slot * 3.25);
        circuit.ValidateOneHotGroups(circuit.EvaluateReference(source));
        // A second active bit breaks the declaration.
        source[2] = 1;
        var reference = circuit.EvaluateReference(source);
        Js.Is(Js.F(10.5 + 17), reference[read.Position]);
        Js.Throws(() => circuit.ValidateOneHotGroups(reference), "one-hot group");
    }
}
