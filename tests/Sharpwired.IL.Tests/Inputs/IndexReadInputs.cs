// Handwired source: $REF/src/json-search/json-step.ts (the reads depthClass[depth], levelWeight[depth - 1]
// and classTable[codePoint + 1] with the clamp rule, objectWeight[depth × eventCount + event] with the
// zero rule) and $REF/src/json-search/regex/data-program.ts (createStepAnd: transitions[class][from][to]
// && bit as one read at class + classes × (1 - bit)). C# inputs for the rules 2 to 5 of the phase 6
// plan, task 1 (question 4), and for the counterexamples that the rules must not change.

using Sharpwired.Annotations;

namespace Sharpwired.IL.Tests.Inputs;

public static class IndexReadInputs
{
    public const int States = 8;
    public const int ClassSlots = 17;
    public const int EventCount = 25;

    // A table of 17 ints, as depthClass of json-step.ts.
    private static readonly int[] DepthClass = [.. Enumerable.Range(0, 17).Select(depth => depth * 3 - 5)];

    // A table of 17 × 25 ints, as objectWeight of json-step.ts.
    private static readonly int[] ObjectWeight = [.. Enumerable.Range(0, 17 * EventCount).Select(cell => (cell * 7919 % 2001) - 1000)];

    // Rule 2: t[Math.Clamp(i, 0, t.Length - 1)] is one index read with the clamp rule.
    [Handwired]
    public static float ClampRead([FixedLength(8), Range(-10, 10)] float[] t, [Range(-20, 20)] int i) => t[Math.Clamp(i, 0, t.Length - 1)];

    [Handwired]
    public static int ClampReadStatic([Range(-512, 512)] int depth) => DepthClass[Math.Clamp(depth, 0, 16)];

    // Rule 2: the line of the highlighter that the plan probe refused. The index reads the range of
    // Math.Clamp, 0..16, and not the inferred range of its operations.
    [Handwired]
    public static int ClampRange([Range(-512, 512)] int depth, [Range(0, 24)] int ev) =>
        ObjectWeight[Math.Clamp(depth, 0, 16) * EventCount + ev]; // marker: clamp range

    // Counterexample of rule 2: the bounds are not the table bounds.
    [Handwired]
    public static float ClampReadInner([FixedLength(8), Range(-10, 10)] float[] t, [Range(-20, 20)] int i) => t[Math.Clamp(i, 1, 6)];

    // Counterexample of rule 2: a second reader reads the Math.Clamp result.
    [Handwired]
    public static (float Value, int Index) ClampReadTwice([FixedLength(8), Range(-10, 10)] float[] t, [Range(-20, 20)] int i)
    {
        int clamped = Math.Clamp(i, 0, 7);
        return (t[clamped], clamped);
    }

    // Rule 3: Hw.ReadOrZero is one index read with the zero rule on both sides.
    [Handwired]
    public static float ZeroRead([FixedLength(8), Range(-10, 10)] float[] t, [Range(-20, 20)] int i) => Hw.ReadOrZero(t, i);

    [Handwired]
    public static int ZeroReadStatic([Range(-20, 40)] int depth, [Range(0, 24)] int ev) => Hw.ReadOrZero(ObjectWeight, depth * EventCount + ev);

    // Rule 3 with an index range outside the table: the constant +0.
    [Handwired]
    public static float ZeroReadOutside([FixedLength(8), Range(-10, 10)] float[] t, [Range(10, 20)] int i) => Hw.ReadOrZero(t, i);

    // Rule 3 with an int result in an int sum: the read is on the grid 1.
    [Handwired]
    public static int ZeroReadPlusOne([FixedLength(8), Range(-100, 100)] int[] t, [Range(-4, 12)] int i) => Hw.ReadOrZero(t, i) + 1;

    // Rule 4: bit && t[i] is one index read at i + 17 × (1 - bit) with the zero rule above.
    [Handwired]
    public static bool GatedRead(bool bit, [FixedLength(ClassSlots)] bool[] t, [Range(0, 16)] int c) => bit && t[c];

    // Rule 4 with an AND of two bits as the gate.
    [Handwired]
    public static bool GatedReadTwoBits(bool a, bool b, [FixedLength(ClassSlots)] bool[] t, [Range(0, 16)] int c) => a && !b && t[c];

    // Counterexample of rule 4: the index is not proven in 0..16, so the read clamps.
    [Handwired]
    public static bool GatedClampRead(bool bit, [FixedLength(ClassSlots)] bool[] t, [Range(-5, 20)] int c) => bit && t[Math.Clamp(c, 0, 16)];

    // Counterexample of rule 4: an int table. A whole value is no bit.
    [Handwired]
    public static int GatedIntRead(bool bit, [FixedLength(ClassSlots), Range(-9, 9)] int[] t, [Range(0, 16)] int c) => bit ? t[c] : 0;

    // Rule 5: the read at (3 × 8 + 5) × 17 + c has a candidate table of the 17 cells of its pair.
    [Handwired]
    public static bool CutRead([FixedLength(ClassSlots * States * States)] bool[] t, [Range(0, 16)] int c) => t[(3 * States + 5) * ClassSlots + c];

    // Rules 4 and 5: createStepAnd of data-program.ts.
    [Handwired]
    public static bool StepAnd(bool bit, [FixedLength(ClassSlots * States * States)] bool[] t, [Range(0, 16)] int c) =>
        bit && t[(3 * States + 5) * ClassSlots + c];

    // Rules 4 and 5 in a constant loop: the next reached bits of search-step.ts.
    [Handwired]
    public static bool[] StepRow([FixedLength(States)] bool[] live, [FixedLength(ClassSlots * States * States)] bool[] t, [Range(0, 16)] int c)
    {
        var next = new bool[States];
        for (int to = 0; to < States; to++)
        {
            bool any = false;
            for (int from = 0; from < States; from++)
            {
                any = any || (live[from] && t[((from * States) + to) * ClassSlots + c]);
            }

            next[to] = any;
        }

        return next;
    }
}
