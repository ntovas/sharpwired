// Handwired source: tests/runtime/prefix-networks.test.ts (runStrings, buildStateMachineScan and
// the describe block "prefix networks"). Each test name holds the Handwired test name.

namespace Sharpwired.Core.Tests;

public class PrefixNetworkTests
{
    private delegate T[] Network<T>(IReadOnlyList<T> items, Func<T, T, T> combine);

    // Combines strings, so a wrong grouping or order shows in the text. Each item also holds the
    // length of the longest chain of combines that made it.
    private readonly record struct Tracked(string Text, int Depth);

    private sealed record StringRun(Tracked[] Prefixes, int Combines, int Depth);

    private sealed record StateMachineScan(Circuit Circuit, Wire[] StatesAfter, int Combines, int LiveOperations);

    private static StringRun RunStrings(Network<Tracked> network, int count)
    {
        var combines = 0;
        var items = Enumerable.Range(0, count).Select(index => new Tracked($"{index},", 0)).ToArray();
        var prefixes = network(items, (earlier, later) =>
        {
            combines++;
            return new Tracked(earlier.Text + later.Text, Math.Max(earlier.Depth, later.Depth) + 1);
        });
        return new StringRun(prefixes, combines, prefixes.Max(prefix => prefix.Depth));
    }

    // A state machine over queued events as a prefix scan. Each event is a table of S index reads
    // from state to next state. Two tables combine as later[earlier[state]]. With the initial
    // state in a cell, the test reads each prefix at that cell. With the constant initial state 0,
    // the test reads each prefix at entry 0.
    private static StateMachineScan BuildStateMachineScan(
        Network<Wire[]> network, int stateCount, int eventCount, int symbolCount, bool initialInCell)
    {
        var tableSize = stateCount * symbolCount;
        var circuit = new Circuit(1 + tableSize + eventCount);
        Wire Cell(int index, double maximum)
        {
            var wire = circuit.Source(index);
            circuit.AssumeRange(wire, 0, maximum);
            circuit.AssumeGrid(wire, 1);
            return wire;
        }
        var initialState = Cell(0, stateCount - 1);
        var next = Enumerable.Range(0, tableSize).Select(entry => Cell(1 + entry, stateCount - 1)).ToArray();
        var symbols = Enumerable.Range(0, eventCount).Select(eventIndex => Cell(1 + tableSize + eventIndex, symbolCount - 1)).ToArray();
        var tables = symbols.Select(symbol => Enumerable.Range(0, stateCount)
            .Select(state => circuit.IndexReadAffine([new Term(symbol, 1)], state * symbolCount, next, OutOfRange.Clamp, OutOfRange.Clamp))
            .ToArray()).ToArray();
        var combines = 0;
        var prefixes = network(tables, (earlier, later) =>
        {
            combines++;
            return earlier.Select(middle => circuit.IndexRead(middle, later)).ToArray();
        });
        var statesAfter = prefixes.Select(prefix => initialInCell ? circuit.IndexRead(initialState, prefix) : prefix[0]).ToArray();
        var program = circuit.CompileTensorProgram(statesAfter);
        return new StateMachineScan(circuit, statesAfter, combines, program.ValueCount - program.SourceSize);
    }

    [Fact]
    public void Every_prefix_is_the_ordered_combination_of_its_items_for_1_to_70_items()
    {
        for (var count = 1; count <= 70; count++)
        {
            var expected = Enumerable.Range(0, count)
                .Select(index => string.Concat(Enumerable.Range(0, index + 1).Select(item => $"{item},")))
                .ToArray();
            Assert.Equal(expected, RunStrings(PrefixNetworks.Sklansky, count).Prefixes.Select(prefix => prefix.Text));
            Assert.Equal(expected, RunStrings(PrefixNetworks.BrentKung, count).Prefixes.Select(prefix => prefix.Text));
        }
    }

    [Fact]
    public void Combines_and_longest_chains_follow_the_formulas_for_powers_of_two()
    {
        foreach (var count in new[] { 4, 16, 64, 256 })
        {
            var log = (int)Math.Log2(count);
            var sklansky = RunStrings(PrefixNetworks.Sklansky, count);
            Assert.Equal(count / 2 * log, sklansky.Combines);
            Assert.Equal(log, sklansky.Depth);
            var brentKung = RunStrings(PrefixNetworks.BrentKung, count);
            Assert.Equal(2 * count - 2 - log, brentKung.Combines);
            Assert.Equal(2 * log - 2, brentKung.Depth);
        }
    }

    [Fact]
    public void A_state_machine_scan_gives_the_loops_states_in_S_times_k_plus_combines_plus_k_live_operations_or_k_plus_S_times_combines_from_a_constant_initial_state()
    {
        const int stateCount = 4;
        const int eventCount = 32;
        const int symbolCount = 3;
        // The LCG of the Handwired test: (seed * 1664525 + 1013904223) >>> 0. The float64 product
        // stays below 2^53, so it is exact, and unchecked uint arithmetic gives the same seed.
        var seed = 7u;
        double Random()
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            return seed / 4294967296.0;
        }
        Network<Wire[]>[] networks = [PrefixNetworks.Sklansky, PrefixNetworks.BrentKung];
        foreach (var network in networks)
        {
            foreach (var initialInCell in new[] { true, false })
            {
                var scan = BuildStateMachineScan(network, stateCount, eventCount, symbolCount, initialInCell);
                Assert.Equal(
                    initialInCell
                        ? stateCount * (eventCount + scan.Combines) + eventCount
                        : eventCount + stateCount * scan.Combines,
                    scan.LiveOperations);
                var evaluator = scan.Circuit.CreateTensorEvaluator(scan.StatesAfter);
                for (var trial = 0; trial < 10; trial++)
                {
                    var source = new float[scan.Circuit.SourceSize];
                    source[0] = initialInCell ? (float)Math.Floor(Random() * stateCount) : 0;
                    for (var entry = 0; entry < stateCount * symbolCount; entry++) source[1 + entry] = (float)Math.Floor(Random() * stateCount);
                    for (var eventIndex = 0; eventIndex < eventCount; eventIndex++)
                    {
                        source[1 + stateCount * symbolCount + eventIndex] = (float)Math.Floor(Random() * symbolCount);
                    }
                    var statesAfter = evaluator.Evaluate(source);
                    double state = source[0];
                    for (var eventIndex = 0; eventIndex < eventCount; eventIndex++)
                    {
                        state = source[1 + (int)state * symbolCount + (int)source[1 + stateCount * symbolCount + eventIndex]];
                        Js.Is(state, statesAfter[eventIndex]);
                    }
                }
            }
        }
    }

    [Fact]
    public void Both_networks_return_an_empty_array_for_no_items_and_call_combine_zero_times()
    {
        var combines = 0;
        int Combine(int earlier, int later)
        {
            combines++;
            return earlier + later;
        }
        Assert.Empty(PrefixNetworks.Sklansky([], (Func<int, int, int>)Combine));
        Assert.Empty(PrefixNetworks.BrentKung([], (Func<int, int, int>)Combine));
        Assert.Equal(0, combines);
    }
}
