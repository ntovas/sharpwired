// Handwired source: src/runtime/cells.ts (createEvaluator, withFirstNaN, collectLeaves, mapLeaves).
// Handwired returns the outputs in the shape of the output object (mapLeaves). C# has no mapped
// types, so ProgramEvaluation holds the outputs in OutputPaths order, and Read gives the value of one
// output wire. ISourceTableEvaluator and ISourceTableEvaluation have no Handwired source. A host
// reads a program evaluator and a plain method runner of phase 4 through them.

using System.Collections.Immutable;
using Sharpwired.Core;

namespace Sharpwired.Cells;

/// <summary>An evaluator of a source table: a program evaluator, or a runner of a plain method.</summary>
public interface ISourceTableEvaluator
{
    /// <summary>Evaluate one source table, and return its next values and its outputs.</summary>
    ISourceTableEvaluation Evaluate(ReadOnlySpan<float> source);
}

/// <summary>The result of one call of <see cref="ISourceTableEvaluator.Evaluate"/>.</summary>
public interface ISourceTableEvaluation
{
    /// <summary>One value for each cell, in cell order.</summary>
    float[] NextValues { get; }

    /// <summary>The value of an output wire.</summary>
    float Read(Wire output);
}

/// <summary>Handwired createEvaluator.</summary>
public static class ProgramEvaluator
{
    /// <summary>
    /// Compile the program for the next values and the outputs, and return an evaluator. The output is
    /// a Wire, a list of outputs, or an object whose public properties are outputs, for example
    /// <c>new { ThumbLeft = thumbLeft }</c>. Create reads the shape of the output by reflection one
    /// time. Evaluate does not use reflection.
    /// </summary>
    /// <remarks>
    /// The outputs of an object go in the MetadataToken order of its properties. Within one type,
    /// the positional properties of a record come before the properties of its body. Across a base
    /// type and a derived type, the order follows the order of the type declarations in the source,
    /// and it is not defined for a base type in another assembly. A type with public instance
    /// fields, for example a ValueTuple, is refused, because the walker reads only properties.
    /// </remarks>
    public static ProgramEvaluator<TOutput> Create<TOutput>(FinishedTable program, TOutput output)
        where TOutput : notnull => new(program, output);
}

/// <summary>
/// Handwired createEvaluator and the function that it returns. Each call checks the source table,
/// evaluates it, checks the next values and that each output is finite, and returns the values. When
/// a check after the evaluation fails on a NaN value, the evaluator runs the same program again and
/// keeps each wire. Then the message also names the operation that produced the first NaN among the
/// wires that the value reads. A check that fails on another value, for example a next value past its
/// range next to a NaN output, gives its own message only.
/// </summary>
public sealed class ProgramEvaluator<TOutput> : ISourceTableEvaluator
    where TOutput : notnull
{
    private readonly Circuit circuit;
    private readonly CellValidator validator;
    private readonly ImmutableArray<Wire> roots;
    private readonly TensorEvaluator evaluator;
    private readonly int nextCount;
    private readonly ProgramEvaluation result;

    internal ProgramEvaluator(FinishedTable program, TOutput output)
    {
        ArgumentNullException.ThrowIfNull(program);
        var leaves = WireStruct.CollectLeaves(output, "", []);
        circuit = program.Circuit;
        validator = program.Validator;
        nextCount = program.NextSource.Length;
        roots = [.. program.NextSource, .. leaves.Select(leaf => leaf.Wire)];
        Program = circuit.CompileTensorProgram(roots);
        evaluator = circuit.CreateTensorProgramEvaluator(Program);
        OutputPaths = [.. leaves.Select(leaf => leaf.Path)];
        var firstLeaf = new Dictionary<Wire, int>();
        for (var index = 0; index < leaves.Count; index++) firstLeaf.TryAdd(leaves[index].Wire, index);
        result = new ProgramEvaluation(new float[nextCount], new float[leaves.Count], firstLeaf);
    }

    /// <summary>The compiled program. Its roots are the next values in cell order, then the output leaves.</summary>
    public TensorProgram Program { get; }

    /// <summary>The path of each output leaf, for example "thumbOffset", "speed.doubled" or "[1].count".</summary>
    public ImmutableArray<string> OutputPaths { get; }

    /// <summary>
    /// Check the source table, evaluate it, check the next values, then check that each output is
    /// finite. A source table that breaks a declaration throws <see cref="CellValueException"/> or
    /// <see cref="ArgumentException"/>. A failed check after the evaluation throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <returns>The same object on each call. The next call overwrites its arrays. The host decides whether to write NextValues back.</returns>
    public ProgramEvaluation Evaluate(ReadOnlySpan<float> source)
    {
        validator.Validate(source);
        var values = evaluator.Evaluate(source);
        try
        {
            validator.Validate(values.AsSpan(0, nextCount));
        }
        catch (CellValueException error)
        {
            // The position of a cell in the table is also the index of its next value in the roots.
            throw WithFirstNaN(error.Message, error, error.Position, source, values);
        }
        catch (ArgumentException error)
        {
            // The error of a rule names no single value, so it gets no NaN message.
            throw new InvalidOperationException(error.Message, error);
        }
        for (var index = 0; index < result.Outputs.Length; index++)
        {
            var value = values[nextCount + index];
            if (!float.IsFinite(value))
            {
                throw WithFirstNaN($"output {OutputPaths[index]} must be finite, got {Fp32.Format(value)}", null, nextCount + index, source, values);
            }
        }
        values.AsSpan(0, nextCount).CopyTo(result.NextValues);
        values.AsSpan(nextCount).CopyTo(result.Outputs);
        return result;
    }

    ISourceTableEvaluation ISourceTableEvaluator.Evaluate(ReadOnlySpan<float> source) => Evaluate(source);

    // Handwired withFirstNaN: add the operation that produced the first NaN to an error about
    // roots[index], when the value of that root is NaN.
    private InvalidOperationException WithFirstNaN(string message, Exception? inner, int index, ReadOnlySpan<float> source, float[] values)
    {
        if (!float.IsNaN(values[index])) return new InvalidOperationException(message, inner);
        // The same program again. EvaluateTensor leaves the wires that the program removed at 0, so the
        // message names an operation that ran.
        var firstNaN = circuit.DescribeFirstNaN(circuit.EvaluateTensor(source, Program), [roots[index]]);
        return new InvalidOperationException(firstNaN is null ? message : $"{message}. {firstNaN}", inner);
    }
}

/// <summary>
/// The result of one call of <see cref="ProgramEvaluator{TOutput}.Evaluate"/>. The next call of the same
/// evaluator overwrites both arrays, as in Handwired.
/// </summary>
public sealed class ProgramEvaluation : ISourceTableEvaluation
{
    private readonly Dictionary<Wire, int> firstLeaf;

    internal ProgramEvaluation(float[] nextValues, float[] outputs, Dictionary<Wire, int> firstLeaf)
    {
        NextValues = nextValues;
        Outputs = outputs;
        this.firstLeaf = firstLeaf;
    }

    /// <summary>One value for each cell, in cell order.</summary>
    public float[] NextValues { get; }

    /// <summary>One value for each output leaf, in OutputPaths order.</summary>
    public float[] Outputs { get; }

    /// <summary>The value of the first output leaf that holds the wire.</summary>
    public float Read(Wire output) =>
        firstLeaf.TryGetValue(output, out var index) ? Outputs[index] : throw new ArgumentException($"wire {output} is not an output");
}
