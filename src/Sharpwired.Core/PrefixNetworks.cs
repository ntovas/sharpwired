// Handwired source: src/runtime/prefix-networks.ts (sklanskyPrefix, brentKungPrefix).

namespace Sharpwired.Core;

/// <summary>
/// Prefix networks: fixed sets of pairwise combines that compute each prefix. The prefixes are
/// items[0], combine(items[0], items[1]), combine(combine(items[0], items[1]), items[2]), and so on.
/// The longest chain of combines is much shorter than the chain of a loop.
/// </summary>
/// <remarks>
/// combine(earlier, later) must be associative: combine(combine(a, b), c) must give the same result
/// as combine(a, combine(b, c)). This is exact for selections, for example reads of tables of
/// integer states, and for sums of integers below 2^24. Other FP32 sums and products are
/// associative only up to rounding, and the last bits then depend on the grouping. combine need not
/// be commutative. For example, the bottom of each row from the row heights:
/// <c>PrefixNetworks.BrentKung(heights, (earlier, later) => circuit.RoundAffine([(earlier, 1), (later, 1)]))</c>.
/// The Handwired docs/patterns.md sections "Running totals" and "A state machine over queued
/// events" give the costs.
/// </remarks>
public static class PrefixNetworks
{
    /// <summary>
    /// Sklansky (1960): the network works on blocks of 2, 4, 8, ... items. In each block, it combines
    /// the last prefix of the left half into each prefix of the right half. For k items, with k a power of two, this is
    /// (k / 2) × log2(k) combines and a longest chain of log2(k).
    /// </summary>
    public static T[] Sklansky<T>(IReadOnlyList<T> items, Func<T, T, T> combine)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(combine);
        var prefixes = items.ToArray();
        for (var halfSize = 1; halfSize < prefixes.Length; halfSize *= 2)
        {
            for (var blockStart = 0; blockStart < prefixes.Length; blockStart += 2 * halfSize)
            {
                var leftHalfLast = blockStart + halfSize - 1;
                var blockEnd = Math.Min(blockStart + 2 * halfSize, prefixes.Length);
                for (var index = leftHalfLast + 1; index < blockEnd; index++)
                {
                    prefixes[index] = combine(prefixes[leftHalfLast], prefixes[index]);
                }
            }
        }
        return prefixes;
    }

    /// <summary>
    /// Brent-Kung (1982): an upward pass combines neighbors, then pairs of pairs, and so on. This
    /// completes the prefixes of the first 2, 4, 8, ... items. A downward pass then completes each
    /// other prefix: it combines one completed prefix with one smaller block. Let k be a power of two
    /// and at least 4. Then k items take 2k − 2 − log2(k) combines, and the longest chain is
    /// 2 × log2(k) − 2.
    /// </summary>
    public static T[] BrentKung<T>(IReadOnlyList<T> items, Func<T, T, T> combine)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(combine);
        var prefixes = items.ToArray();
        var span = 1;
        for (; span < prefixes.Length; span *= 2)
        {
            for (var index = 2 * span - 1; index < prefixes.Length; index += 2 * span)
            {
                prefixes[index] = combine(prefixes[index - span], prefixes[index]);
            }
        }
        // The downward pass starts at span / 2. With 0 or 1 item the upward pass does not run, span
        // stays 1, and span / 2 is 0 in int arithmetic. JavaScript gives 0.5, which also ends the
        // loop at once, so both forms run no combine.
        for (span /= 2; span >= 1; span /= 2)
        {
            for (var index = 3 * span - 1; index < prefixes.Length; index += 2 * span)
            {
                prefixes[index] = combine(prefixes[index - span], prefixes[index]);
            }
        }
        return prefixes;
    }
}
