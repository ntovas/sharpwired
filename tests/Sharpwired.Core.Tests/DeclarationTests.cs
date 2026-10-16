// Handwired source: src/runtime/numeric-ui-core.ts (assumeRange, assumeOneHot, assumeGrid,
// assumeBit, assertWire). The Handwired tests of these methods are in NumericSemanticsTests.cs
// and InferenceTests.cs. This file covers the other error conditions.

namespace Sharpwired.Core.Tests;

public class DeclarationTests
{
    [Fact]
    public void Assume_range_narrows_the_known_range()
    {
        var circuit = new Circuit(1);
        circuit.AssumeRange(circuit.Source(0), -10, 10);
        circuit.AssumeRange(circuit.Source(0), -20, 5);
        Assert.Equal(new NumericRange(-10, 5), circuit.Ranges[0]);
    }

    [Fact]
    public void Assume_range_refuses_an_inverted_range_a_contradiction_and_an_unknown_wire()
    {
        var circuit = new Circuit(1);
        Js.Throws(() => circuit.AssumeRange(circuit.Source(0), 2, 1), "numeric range is inverted");
        Js.Throws(() => circuit.AssumeRange(circuit.Source(0), double.NaN, 1), "numeric range is inverted");
        circuit.AssumeRange(circuit.Source(0), 0, 1);
        Js.Throws(() => circuit.AssumeRange(circuit.Source(0), 2, 3), "numeric range contradicts known bounds");
        Js.Throws(() => circuit.AssumeRange(Wire.FromPosition(1), 0, 1), "wire is outside the compiled circuit");
    }

    [Fact]
    public void The_existing_readers_message_names_the_wire_and_both_ranges()
    {
        var circuit = new Circuit(1);
        circuit.Affine([(circuit.Source(0), 2)]);
        var exception = Js.Throws(() => circuit.AssumeRange(circuit.Source(0), 0, 0.5), "already read wire 0");
        Assert.Equal(
            "operations that already read wire 0 were built with its range -Infinity..Infinity, "
                + "and declaring 0..0.5 now doesn't change them. Declare the range before the first operation "
                + "that reads the wire, or pass {allowExistingReaders: true}",
            exception.Message);
    }

    [Fact]
    public void Assume_grid_names_the_existing_grid_in_the_existing_readers_message()
    {
        var circuit = new Circuit(1);
        circuit.AssumeGrid(circuit.Source(0), 1.0 / 64);
        circuit.Affine([(circuit.Source(0), 2)]);
        Js.Throws(() => circuit.AssumeGrid(circuit.Source(0), 1), "already read wire 0 were built with its grid 0.015625, and declaring the grid 1 now");
    }

    [Fact]
    public void Assume_one_hot_records_the_group_and_narrows_each_member_to_zero_to_one()
    {
        var circuit = new Circuit(3);
        circuit.AssumeOneHot([circuit.Source(0), circuit.Source(2)], allowAllZero: true);
        var group = Assert.Single(circuit.OneHotGroups);
        Assert.Equal<Wire>([circuit.Source(0), circuit.Source(2)], group.Members);
        Assert.True(group.AllowAllZero);
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[0]);
        Assert.Equal(new NumericRange(double.NegativeInfinity, double.PositiveInfinity), circuit.Ranges[1]);
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[2]);
    }

    [Fact]
    public void Assume_one_hot_refuses_an_empty_group_repeated_members_and_late_narrowing()
    {
        var circuit = new Circuit(2);
        Js.Throws(() => circuit.AssumeOneHot([]), "a one-hot group needs at least one member");
        Js.Throws(() => circuit.AssumeOneHot([circuit.Source(0), circuit.Source(0)]), "one-hot group members must be distinct wires");
        circuit.Affine([(circuit.Source(1), 2)]);
        Js.Throws(() => circuit.AssumeOneHot([circuit.Source(1)]), "already read wire 1");
        circuit.AssumeOneHot([circuit.Source(1)], allowExistingReaders: true);
        // Members that already have 0..1 ranges do not throw.
        circuit.AssumeOneHot([circuit.Source(1)]);
    }

    [Fact]
    public void Assume_bit_records_an_exact_bit_and_narrows_the_range()
    {
        var circuit = new Circuit(2);
        circuit.AssumeBit(circuit.Source(1));
        Assert.Equal([false, true], circuit.ExactBits);
        Assert.Equal(new NumericRange(0, 1), circuit.Ranges[1]);
        // The declaration changes no operation: a boolean still reads the wire.
        var bit = circuit.Boolean(circuit.Source(1));
        Assert.False(circuit.ExactBits[bit.Position]);
        // The wire has a reader now, but a repeated declaration does not narrow the range, so it does not throw.
        circuit.AssumeBit(circuit.Source(1));
    }

    [Fact]
    public void A_late_assume_bit_throws_unless_the_caller_opts_in()
    {
        var circuit = new Circuit(1);
        circuit.Affine([(circuit.Source(0), 2)]);
        Js.Throws(() => circuit.AssumeBit(circuit.Source(0)), "already read wire 0");
        circuit.AssumeBit(circuit.Source(0), allowExistingReaders: true);
        Assert.True(circuit.ExactBits[0]);
    }

    [Fact]
    public void Has_readers_marks_the_wires_that_operations_read()
    {
        var circuit = new Circuit(3);
        var sum = circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1)]);
        Assert.Equal([true, true, false, false], circuit.HasReaders);
        // A composed producer counts as read, though the reader stores the producer's terms.
        circuit.Reglu([(sum, 1)], 0, [(circuit.Source(2), 1)], 0);
        Assert.Equal([true, true, true, true, false], circuit.HasReaders);
    }
}
