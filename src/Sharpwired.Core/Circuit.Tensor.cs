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
    /// operation. AssumeRange, AssumeGrid, AssumeBit and MarkRoundingPoint keep it: the compiler reads
    /// no grid, no exact bit and no rounding point record, and a narrower range does not make a
    /// compiled program wrong.
    /// </remarks>
    public TensorProgram? TensorProgram { get; set; }

    /// <summary>
    /// Check the source table, run a compiled program on it, and return the value of each wire, with
    /// 0 for each wire that the program removed. Without a program, run <see cref="TensorProgram"/>,
    /// and compile a program of every operation into it when it is null.
    /// </summary>
    /// <returns>A new array of Width values.</returns>
    public float[] EvaluateTensor(ReadOnlySpan<float> source, TensorProgram? program = null)
    {
        ValidateSourceRanges(source);
        return TensorExecution.EvaluateTensorProgram(source, program ?? (TensorProgram ??= CompileTensorProgram()));
    }

    /// <summary>
    /// Compile the roots and return a reusable evaluator of the program. The evaluator returns the
    /// values of the roots in one array that the next call overwrites.
    /// </summary>
    public TensorEvaluator CreateTensorEvaluator(IReadOnlyList<Wire> roots, bool recomputeDepths = false) =>
        CreateTensorProgramEvaluator(CompileTensorProgram(roots, recomputeDepths));

    /// <summary>
    /// Return a reusable evaluator of a program that CompileTensorProgram already returned, for
    /// example a program that the caller also validates or reads counts from, so that the circuit
    /// compiles one time. The evaluator returns the values of the program roots.
    /// </summary>
    public TensorEvaluator CreateTensorProgramEvaluator(TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new TensorEvaluator(this, program);
    }
}
