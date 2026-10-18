// No Handwired source. The interface of an evaluator of one source table. TensorEvaluator implements it
// on the CPU, and the GPU evaluator of Sharpwired.Gpu implements it on a WebGPU device. ProgramEvaluator
// of Sharpwired.Cells evaluates through it.

namespace Sharpwired.Core;

/// <summary>An evaluator of one source table that returns the values of the program roots.</summary>
public interface IRootEvaluator
{
    /// <summary>The program that the evaluator runs.</summary>
    TensorProgram Program { get; }

    /// <summary>
    /// Check the source table against the circuit, run the program on it, and return the values of
    /// Program.Roots, in root order. A root that the program does not hold gives NaN.
    /// </summary>
    /// <returns>An array that the evaluator can use again: the next call can overwrite it. Copy the values to keep them.</returns>
    float[] Evaluate(ReadOnlySpan<float> source);
}
