// Handwired source: src/runtime/numeric-ui-core.ts (resolveIndexPosition, readIndexedValue).
// The reference evaluator and ValidateCircuit read index operations, so this file holds the
// part of the index read instruction that they use.

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    // floor(index) as a table position: 0..count-1 inside the table. Outside the table, the
    // nearest end, or -1 where that side reads +0. -2 for a NaN index. The floor and the
    // comparisons run in float64 on an FP32 value, so nothing rounds.
    internal static int ResolveIndexPosition(double index, int count, bool belowZero, bool aboveZero)
    {
        if (double.IsNaN(index)) return -2;
        var position = Math.Floor(index);
        if (position < 0) return belowZero ? -1 : 0;
        if (position > count - 1) return aboveZero ? -1 : count - 1;
        return (int)position;
    }

    // The index read instruction on one index value: copy the FP32 value of the selected
    // candidate, +0 where that side reads zero, and NaN for a NaN index.
    internal static float ReadIndexedValue(float[] values, IndexReadInstruction read, double index)
    {
        var position = ResolveIndexPosition(
            index,
            read.Table.Candidates.Length,
            read.Below == OutOfRange.Zero,
            read.Above == OutOfRange.Zero);
        if (position >= 0) return values[read.Table.Candidates[position].Position];
        return position == -1 ? 0f : float.NaN;
    }
}
