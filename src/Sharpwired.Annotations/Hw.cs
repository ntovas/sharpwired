// Handwired source: src/runtime/numeric-ui-core.ts (emitRelu, Circuit.boolean, emitApplySpringMatrix,
// emitAnimatedObject, emitFirstActive, and the index read with the zero rule), through the macros of
// Sharpwired.Core that port them. ReadOrZero follows the "zero" reads of src/json-search/json-step.ts
// and src/json-search/regex/data-program.ts.
// Each body is a plain .NET body in FP32. It has one statement for each operation of the macro,
// with the terms in the order that the reference evaluator (Circuit.EvaluateReference) adds them.
// An operation adds its terms to f(bias) one at a time, so a sum with the bias 0 starts at +0.
// The +0 start changes -0 to +0, so the bodies keep it. A coefficient of 1 is left out, because
// 1 × x is x in FP32.

using System.Globalization;

namespace Sharpwired.Annotations;

/// <summary>
/// The operations with no plain C# form. A plain method calls them, and the translator replaces each
/// call with its macro. Each body computes in FP32, operation for operation as the macro in
/// Sharpwired.Core. So the plain method gives the value of the program bit for bit.
/// </summary>
public static class Hw
{
    // The default settle thresholds of Macros.AnimatedObject, 0.01 px and 0.01 px/s, as FP32 values.
    private static readonly float SettleThreshold = (float)0.01;

    // 2^127, the lift of the rest test in Macros.AnimatedObject.
    private const float RestLift = 1.7014118346046923e38f;

    // 2^22, the scale of the rest bit.
    private const float RestScale = 4_194_304f;

    // 2^20, the scale of the exceedance in the settled score.
    private const float ExceedanceScale = 1_048_576f;

    // The odd base of the boolean atom (Fp32.EvaluateBoolean).
    private const float BooleanBase = 8_388_609f;

    /// <summary>
    /// Macros.AnimatedObject with one spring, the default thresholds 0.01 and 0.01, and a snap bit.
    /// The array matrix has length 4: displacement from displacement, displacement from velocity, velocity
    /// from displacement, velocity from velocity.
    /// </summary>
    /// <remarks>
    /// The body always rebases the displacement from destinationDuringSteps to destination. The macro
    /// skips the rebase when the two arguments are the same wire. For a finite destination the two
    /// forms give the same value bit for bit. The reason: destination - destination is +0, and the
    /// displacement after the spring step is never -0, because its sum starts at +0. Only a destination of
    /// ±Infinity gives a difference: the body computes Infinity - Infinity, which is NaN.
    /// </remarks>
    public static AnimatedValue AnimatedObject(
        float[] matrix,
        float displacement,
        float velocity,
        float destination,
        float destinationDuringSteps,
        bool settleCheck,
        bool snap)
    {
        var next = ApplySpringMatrix(matrix, displacement, velocity);
        float settleCheckValue = settleCheck ? 1f : 0f;
        float snapValue = snap ? 1f : 0f;
        // The rebase, one RoundAffine: f(f(during - destination) + displacement').
        float rebased = 0f + destinationDuringSteps + -destination + next.Displacement;
        // relu(value), relu(-value) and relu(relu(value) + relu(-value) - threshold), for each value.
        float displacementPositive = 1f * MathF.Max(0f, 0f + rebased);
        float displacementNegative = 1f * MathF.Max(0f, 0f + -rebased);
        float displacementExceedance = 1f * MathF.Max(0f, -SettleThreshold + displacementPositive + displacementNegative);
        float velocityPositive = 1f * MathF.Max(0f, 0f + next.Velocity);
        float velocityNegative = 1f * MathF.Max(0f, 0f + -next.Velocity);
        float velocityExceedance = 1f * MathF.Max(0f, -SettleThreshold + velocityPositive + velocityNegative);
        float exceedance = 0f + displacementExceedance + velocityExceedance;
        // Each lifted term is 0, at least 2^-22, or Infinity.
        float liftedRest = 1f * MathF.Max(
            0f,
            0f + RestLift * displacementPositive + RestLift * displacementNegative + RestLift * velocityPositive + RestLift * velocityNegative);
        float atRest = 1f * MathF.Max(0f, 1f + -RestScale * liftedRest);
        float settledScore = 1f * MathF.Max(0f, 0f + 0.5f * settleCheckValue + 0.5f * atRest + -ExceedanceScale * exceedance);
        float settled = BooleanAtom(0f + settledScore);
        float notSettled = 1f + -settled;
        float snapComplement = 1f + -snapValue;
        // Circuit.Multiply with a nonnegative right operand: one ReGLU.
        float active = (0f + notSettled) * MathF.Max(0f, 0f + snapComplement);
        float nextDisplacement = (0f + rebased) * MathF.Max(0f, 0f + active);
        float nextVelocity = (0f + next.Velocity) * MathF.Max(0f, 0f + active);
        float position = 0f + destination + nextDisplacement;
        return new AnimatedValue(position, nextDisplacement, nextVelocity, active != 0f);
    }

    /// <summary>
    /// Macros.ApplySpringMatrix: f(f(m0 × displacement) + f(m1 × velocity)) and
    /// f(f(m2 × displacement) + f(m3 × velocity)).
    /// </summary>
    /// <remarks>
    /// The macro writes each product with one ReGLU when a declared range proves an operand
    /// nonnegative, and with three operations otherwise. The two forms differ only in the sign of a
    /// zero product. The sum starts at +0, so the result is the same for both forms.
    /// </remarks>
    public static SpringValue ApplySpringMatrix(float[] matrix, float displacement, float velocity)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Length != 4) throw new ArgumentException($"a spring matrix needs 4 entries, got {matrix.Length}", nameof(matrix));
        float displacementFromDisplacement = Multiply(matrix[0], displacement);
        float displacementFromVelocity = Multiply(matrix[1], velocity);
        float nextDisplacement = 0f + displacementFromDisplacement + displacementFromVelocity;
        float velocityFromDisplacement = Multiply(matrix[2], displacement);
        float velocityFromVelocity = Multiply(matrix[3], velocity);
        float nextVelocity = 0f + velocityFromDisplacement + velocityFromVelocity;
        return new SpringValue(nextDisplacement, nextVelocity);
    }

    /// <summary>
    /// table[index], or 0 when index is outside 0..table.Length - 1: the index read with the zero rule
    /// on both sides (Circuit.IndexRead with OutOfRange.Zero). The translator gives one index read.
    /// </summary>
    public static int ReadOrZero(int[] table, int index)
    {
        ArgumentNullException.ThrowIfNull(table);
        return index >= 0 && index < table.Length ? table[index] : 0;
    }

    /// <summary>
    /// table[index] with no change of its bits, or +0 when index is outside 0..table.Length - 1: the
    /// index read with the zero rule on both sides. The read copies the value, so -0 stays -0.
    /// </summary>
    public static float ReadOrZero(float[] table, int index)
    {
        ArgumentNullException.ThrowIfNull(table);
        return index >= 0 && index < table.Length ? table[index] : 0f;
    }

    /// <summary>Macros.Relu: f(max(0, value)) in one operation. MathF.Max(0, value) costs a Compare.</summary>
    public static float Relu(float value) => 1f * MathF.Max(0f, 0f + value);

    /// <summary>
    /// Circuit.Boolean, the boolean atom: score within 0..1, false below 0.5, otherwise true. A score
    /// outside 0..1 gives an ArgumentException, as the macro refuses a range outside 0..1.
    /// </summary>
    public static bool Boolean(float score)
    {
        if (!(score >= 0f && score <= 1f))
        {
            throw new ArgumentException(
                $"a boolean score must be within 0..1, got {score.ToString("R", CultureInfo.InvariantCulture)}",
                nameof(score));
        }
        return BooleanAtom(0f + score) != 0f;
    }

    /// <summary>
    /// Macros.FirstActive: the first candidate that is true, gated by active, as a one-hot (or all
    /// false) array.
    /// </summary>
    /// <remarks>
    /// The body builds the same balanced tree as the macro. Each value is 0 or 1, so each sum and
    /// product is exact, and the composed sums of the macro give the same values.
    /// </remarks>
    public static bool[] FirstActive(bool active, bool[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Length == 0) return [];
        var level = new List<WinnerNode>(candidates.Length);
        for (var item = 0; item < candidates.Length; item++) level.Add(new WinnerNode(candidates[item] ? 1f : 0f, item, null, null));
        while (level.Count > 1)
        {
            var next = new List<WinnerNode>((level.Count + 1) / 2);
            for (var item = 0; item < level.Count; item += 2)
            {
                var left = level[item];
                next.Add(item + 1 == level.Count ? left : new WinnerNode(Maximum(left.Mass, level[item + 1].Mass), -1, left, level[item + 1]));
            }
            level = next;
        }
        var root = level[0];
        var selected = new bool[candidates.Length];
        float activeValue = active ? 1f : 0f;
        float rootRoute = (0f + activeValue) * MathF.Max(0f, 0f + root.Mass);
        RouteWinner(root, rootRoute, selected);
        return selected;
    }

    // Circuit.Multiply with no operand proven nonnegative: f(f(a × max(0, b)) - f(a × max(0, -b))).
    private static float Multiply(float left, float right)
    {
        float positive = (0f + left) * MathF.Max(0f, 0f + right);
        float negative = (0f + left) * MathF.Max(0f, 0f + -right);
        return 0f + positive + -negative;
    }

    // Fp32.EvaluateBoolean: f(f(score + 8_388_609) - 8_388_609).
    private static float BooleanAtom(float score)
    {
        float shifted = score + BooleanBase;
        return shifted - BooleanBase;
    }

    // Macros.Maximum: b + relu(a - b), with each sum as its own operation.
    private static float Maximum(float a, float b)
    {
        float difference = 0f + a + -b;
        float positive = 1f * MathF.Max(0f, 0f + difference);
        return 0f + b + positive;
    }

    // The route down the tree: route × leftMass to the left child, the rest to the right child.
    private static void RouteWinner(WinnerNode node, float route, bool[] selected)
    {
        if (node.Left is null || node.Right is null)
        {
            selected[node.Item] = route != 0f;
            return;
        }
        float leftRoute = (0f + route) * MathF.Max(0f, 0f + node.Left.Mass);
        float rightRoute = 0f + route + -leftRoute;
        RouteWinner(node.Left, leftRoute, selected);
        RouteWinner(node.Right, rightRoute, selected);
    }

    // A leaf has an item and no children. A pair has two children and the item -1.
    private sealed record WinnerNode(float Mass, int Item, WinnerNode? Left, WinnerNode? Right);
}
