// Handwired source: src/runtime/numeric-ui-core.ts (Circuit.tensorProgram, Circuit.evaluateTensor,
// Circuit.createTensorEvaluator, Circuit.createTensorProgramEvaluator).

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    /// <summary>
    /// The program that EvaluateTensor runs when the caller gives no program. The caller can set it,
    /// for example to a program of the roots of a machine. Otherwise the first EvaluateTensor call
    /// compiles a program of every operation and keeps it here.
    /// </summary>
    /// <remarks>
    /// AssumeOneHot, Append and IndexReadAffine set it to null when they add a group or an
    /// operation. AssumeRange, AssumeGrid, AssumeBit and MarkRoundingPoint keep it. The compiler
    /// reads no grid, no exact bit and no rounding point record. A narrower range does not make a
    /// compiled program wrong.
    /// </remarks>
    public TensorProgram? TensorProgram { get; set; }

    /// <summary>
    /// Check the source table, run a compiled program on it, and return the value of each wire. The
    /// value is 0 for each wire that the program removed. Without a program, run <see cref="TensorProgram"/>,
    /// and compile a program of every operation into it when it is null.
    /// </summary>
    /// <remarks>
    /// Without a program, the call can write <see cref="TensorProgram"/>. Do not call it from
    /// parallel threads on one circuit. A program that fails
    /// <see cref="Validation.ValidateTensorProgram"/> can make the call throw an exception, for
    /// example <see cref="IndexOutOfRangeException"/>. Handwired reads undefined in the same case.
    /// </remarks>
    /// <returns>A new array of Width values.</returns>
    public float[] EvaluateTensor(ReadOnlySpan<float> source, TensorProgram? program = null)
    {
        ValidateSourceRanges(source);
        return TensorExecution.EvaluateTensorProgram(source, program ?? (TensorProgram ??= CompileTensorProgram()));
    }

    /// <summary>
    /// Compile the roots with gather lowering and return a reusable evaluator of the program. The
    /// evaluator returns the values of the roots in one array that the next call overwrites.
    /// </summary>
    public TensorEvaluator CreateTensorEvaluator(IReadOnlyList<Wire> roots, bool recomputeDepths = false) =>
        CreateTensorProgramEvaluator(CompileTensorProgram(roots, lowerGathers: true, recomputeDepths: recomputeDepths));

    /// <summary>
    /// Return a reusable evaluator of a program that CompileTensorProgram already returned. Use it
    /// for example for a program that the caller also validates or reads counts from. Then the
    /// circuit compiles one time. The evaluator returns the values of the program roots.
    /// </summary>
    public TensorEvaluator CreateTensorProgramEvaluator(TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new TensorEvaluator(this, program);
    }
}
