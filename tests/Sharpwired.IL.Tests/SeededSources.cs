// No Handwired source. Seeded source tables within the declarations of a finished table, for the
// value tests of the translator (plan, task 3). The tables hold these values:
// - bits 0 or 1;
// - int values within the range;
// - float values on the grid;
// - otherwise, random FP32 values in the range, with the range ends, 0 and -0.
// An open range also gets values up to +-3E+38 and +-float.MaxValue. These values let a value test
// reach the inputs that overflow FP32 (review of task 5, round 2, finding A).
// A one-hot group gets exactly one member at 1, or no member when the group allows all zeros. Each
// table passes the validator of the finished table.

using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.Core.Tests;

namespace Sharpwired.IL.Tests;

internal static class SeededSources
{
    // The magnitudes of most values in an open range: 2^-12 to 2^12.
    private const int OpenRangeExponent = 12;

    // The largest exponent of a large value in an open range: 2^127 is near float.MaxValue.
    private const int LargeExponent = 127;

    /// <summary>Creates count source tables from the seed. Each table passes the validator of the table.</summary>
    public static List<float[]> Create(FinishedTable table, int count, int seed)
    {
        var circuit = table.Circuit;
        var random = Js.CreateRandom(seed);
        var groupOf = new Dictionary<int, OneHotGroup>();
        foreach (var group in circuit.OneHotGroups)
        {
            foreach (var member in group.Members) groupOf[member.Position] = group;
        }

        var sources = new List<float[]>(count);
        for (var item = 0; item < count; item++)
        {
            var source = new float[circuit.SourceSize];
            for (var cell = 0; cell < source.Length; cell++)
            {
                if (!groupOf.ContainsKey(cell)) source[cell] = Sample(random, circuit.Ranges[cell], circuit.Grids[cell], circuit.ExactBits[cell]);
            }

            foreach (var group in circuit.OneHotGroups)
            {
                var choices = group.Members.Length + (group.AllowAllZero ? 1 : 0);
                var chosen = (int)(random() * choices);
                for (var member = 0; member < group.Members.Length; member++) source[group.Members[member].Position] = member == chosen ? 1f : 0f;
            }

            table.Validator.Validate(source);
            sources.Add(source);
        }

        return sources;
    }

    private static float Sample(Func<double> random, NumericRange range, double? grid, bool exactBit)
    {
        if (exactBit) return random() < 0.5 ? 0f : 1f;
        if (grid is { } step)
        {
            var low = Math.Ceiling(range.Minimum / step);
            var high = Math.Floor(range.Maximum / step);
            var steps = Math.Min(high, low + (high - low) * random());
            return (float)(Math.Round(steps) * step);
        }

        var choice = random();
        if (!double.IsFinite(range.Minimum) || !double.IsFinite(range.Maximum))
        {
            if (choice < 0.1) return random() < 0.5 ? 0f : -0f;
            float magnitude;
            if (choice < 0.15)
            {
                magnitude = random() < 0.5 ? 3e38f : float.MaxValue;
            }
            else if (choice < 0.3)
            {
                // A large value: 2^12 to float.MaxValue.
                var exponent = OpenRangeExponent + (int)(random() * (LargeExponent - OpenRangeExponent + 1));
                magnitude = (float)Math.Min((1 + random()) * Math.ScaleB(1.0, exponent), float.MaxValue);
            }
            else
            {
                magnitude = (float)((1 + random()) * Math.ScaleB(1.0, (int)(random() * (2 * OpenRangeExponent + 1)) - OpenRangeExponent));
            }

            var signed = random() < 0.5 ? -magnitude : magnitude;
            return signed >= range.Minimum && signed <= range.Maximum ? signed : magnitude;
        }

        // The FP32 range ends inside the declared range.
        var lowest = (float)range.Minimum < range.Minimum ? MathF.BitIncrement((float)range.Minimum) : (float)range.Minimum;
        var highest = (float)range.Maximum > range.Maximum ? MathF.BitDecrement((float)range.Maximum) : (float)range.Maximum;
        if (choice < 0.2)
        {
            float[] edges = [lowest, highest, 0f, -0f];
            var edge = edges[(int)(random() * edges.Length)];
            return edge >= lowest && edge <= highest ? edge : lowest;
        }

        var value = (float)(range.Minimum + (range.Maximum - range.Minimum) * random());
        return Math.Clamp(value, lowest, highest);
    }
}
