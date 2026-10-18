// Handwired source: src/json-search/generated-js.ts (compileChunks). The rows of a compiled program as
// .NET code, one statement group for each packed position, compiled with System.Linq.Expressions into
// delegates of a limited size. Each delegate reads and writes one array of all packed values: the source
// table first, then the value of each row, as TensorExecution keeps them. The rows come from
// ShaderRows.FlattenProgram, which gives the values of TensorExecution, and each statement keeps the FP32
// steps of TensorExecution:
// - A sum is f(bias), then f(value + f(weight × input)) for each stored term, in stored order. Each step
//   is one float operation, so the result rounds after each step. The rule of generated-js.ts gives the
//   same bits with fewer operations: f(1 × x) is x, f(-1 × x) is -x, and f(-0 + x) is x.
// - The JIT does not contract a float multiply and a float add into a fused multiply-add. The expression
//   gives the same IL as Circuit.EvaluateTerms: one mul and one add on float32 values. A Release test
//   (CompiledStageTests) checks the bits on the inputs where a fused multiply-add differs.
// - A ReGLU is f(left × MathF.Max(0, gate)). A boolean atom is f(f(score + 8388609) - 8388609).
// - An index read floors its index, reads +0 or the end candidate outside the table, NaN for a NaN index,
//   and copies the selected value with no arithmetic.
// - A gather compute sums position × bit and the bits in double, and stores ToInt32 of the index and
//   f(mass). A gather read clamps the index into the table, multiplies a masked candidate by the mass,
//   and adds it to the row.

using System.Linq.Expressions;
using System.Reflection;
using Sharpwired.Core;

namespace Sharpwired.Gpu;

/// <summary>
/// The compiled delegate of one stage, from the rows of its program. Exact FP32: each value has the bits
/// of <see cref="TensorEvaluator"/>, also the sign of zero and NaN.
/// </summary>
/// <remarks>
/// <para>
/// The compiled delegates are immutable and can run on more threads. One instance holds one value array,
/// so one instance must run on one thread only. <see cref="CreateCopy"/> gives an instance with its own
/// array and the same delegates.
/// </para>
/// <para>
/// Evaluate does not check the source table against the declared ranges. The loop runner checks each
/// cell before it writes the cell into the table.
/// </para>
/// </remarks>
public sealed class CompiledStage
{
    // A chunk ends at the first row after this many operations (terms, candidates and steps), so that
    // the JIT optimizes each delegate. A larger delegate can pass the size limits of the JIT optimizer.
    // Measured on Reverse32 of LoopInputs (Release, Apple M5 Pro): 700 gave 5.5 us, 2,000 gave 4.5 us,
    // and 6,000 gave 9.4 us for one evaluation.
    internal const int OperationsPerChunk = 2_000;

    private static readonly MethodInfo MaximumMethod = typeof(MathF).GetMethod(nameof(MathF.Max), [typeof(float), typeof(float)])!;
    private static readonly MethodInfo FloorMethod = typeof(MathF).GetMethod(nameof(MathF.Floor), [typeof(float)])!;
    private static readonly MethodInfo ToInt32Method = typeof(CompiledStage).GetMethod(nameof(JsToInt32), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly StageChunk[] chunks;
    private readonly int[] rootPositions;
    private readonly int[] slotIndices;
    private readonly float[] slotMasses;

    private CompiledStage(TensorProgram program, StageChunk[] chunks, int[] rootPositions)
    {
        Program = program;
        this.chunks = chunks;
        this.rootPositions = rootPositions;
        Values = new float[program.ValueCount];
        slotIndices = new int[program.GatherSlotCount];
        slotMasses = new float[program.GatherSlotCount];
    }

    // One delegate: it writes the values of its rows into values. A gather compute writes the index and
    // the mass of its slot into the two slot arrays, and a gather read of a later chunk reads them.
    internal delegate void StageChunk(float[] values, int[] slotIndices, float[] slotMasses);

    /// <summary>The program that the delegates run.</summary>
    public TensorProgram Program { get; }

    /// <summary>The number of roots, the length of the roots span of <see cref="Evaluate"/>.</summary>
    public int RootCount => rootPositions.Length;

    /// <summary>The number of compiled delegates.</summary>
    public int ChunkCount => chunks.Length;

    // The packed values of the last evaluation. The loop runner writes the source cells here and calls Run.
    internal float[] Values { get; }

    // The packed position of each root, or -1 for a root that the program does not hold.
    internal int[] RootPositions => rootPositions;

    /// <summary>Compile the rows of the program into delegates.</summary>
    /// <exception cref="ArgumentException">The program is not a valid program of the circuit.</exception>
    public static CompiledStage Compile(Circuit circuit, TensorProgram program)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(program);
        ShaderRows.Validate(circuit, program);
        var rows = ShaderRows.FlattenProgram(program);
        var chunks = new List<StageChunk>();
        var start = 0;
        while (start < rows.Length)
        {
            var end = start;
            var operations = 0;
            while (end < rows.Length && (end == start || operations < OperationsPerChunk))
            {
                operations += Cost(rows[end]);
                end++;
            }
            chunks.Add(new ChunkBuilder().Build(rows.AsSpan(start, end - start)));
            start = end;
        }
        return new CompiledStage(program, [.. chunks], ShaderRows.RootPositions(program));
    }

    /// <summary>
    /// Run the program on the source table, and write the value of each root into roots, in root order.
    /// A root that the program does not hold gives NaN.
    /// </summary>
    /// <exception cref="ArgumentException">A span has the wrong length.</exception>
    public void Evaluate(ReadOnlySpan<float> source, Span<float> roots)
    {
        if (source.Length != Program.SourceSize)
        {
            throw new ArgumentException($"the source table holds {source.Length} values, and the program has {Program.SourceSize} source cells", nameof(source));
        }
        if (roots.Length != rootPositions.Length)
        {
            throw new ArgumentException($"roots holds {roots.Length} values, and the program has {rootPositions.Length} roots", nameof(roots));
        }
        source.CopyTo(Values);
        Run();
        for (var root = 0; root < roots.Length; root++)
        {
            var position = rootPositions[root];
            roots[root] = position >= 0 ? Values[position] : float.NaN;
        }
    }

    /// <summary>An instance with its own value array and the same compiled delegates, for another thread.</summary>
    public CompiledStage CreateCopy() => new(Program, chunks, rootPositions);

    // Run the delegates on Values, whose source cells the caller wrote.
    internal void Run()
    {
        var values = Values;
        for (var chunk = 0; chunk < chunks.Length; chunk++) chunks[chunk](values, slotIndices, slotMasses);
    }

    // ECMAScript ToInt32, the conversion of an Int32Array store, as TensorExecution applies it to a
    // gather index: NaN and the infinities give 0, and other values truncate toward zero and wrap
    // modulo 2^32.
    private static int JsToInt32(double value)
    {
        if (!double.IsFinite(value)) return 0;
        var wrapped = Math.Truncate(value) % 4_294_967_296.0;
        if (wrapped < 0) wrapped += 4_294_967_296.0;
        return unchecked((int)(uint)wrapped);
    }

    // The size of the code of one row, for the chunk limit.
    private static int Cost(ShaderRow row) => row switch
    {
        GatherComputeRow compute => 2 + compute.BitPositions.Length,
        AffineRow affine => 1 + affine.Terms.Length + affine.Gathers.Sum(gather => 4 + gather.Candidates.Count),
        BooleanRow boolean => 3 + boolean.Terms.Length,
        RegluRow reglu => 3 + reglu.Terms.Length + reglu.GateTerms.Length,
        IndexRow index => 6 + index.IndexTerms.Length + index.Candidates.Count,
        _ => throw new InvalidOperationException($"unknown row {row}"),
    };

    // The expression of one chunk. The locals are reused by each row of the chunk.
    private sealed class ChunkBuilder
    {
        private readonly ParameterExpression values = Expression.Parameter(typeof(float[]), "values");
        private readonly ParameterExpression slotIndices = Expression.Parameter(typeof(int[]), "slotIndices");
        private readonly ParameterExpression slotMasses = Expression.Parameter(typeof(float[]), "slotMasses");
        private readonly ParameterExpression sum = Expression.Variable(typeof(float), "sum");
        private readonly ParameterExpression gate = Expression.Variable(typeof(float), "gate");
        private readonly ParameterExpression floor = Expression.Variable(typeof(float), "floor");
        private readonly ParameterExpression slot = Expression.Variable(typeof(int), "slot");
        private readonly ParameterExpression gatherIndex = Expression.Variable(typeof(double), "gatherIndex");
        private readonly ParameterExpression gatherMass = Expression.Variable(typeof(double), "gatherMass");
        private readonly List<Expression> body = [];

        public StageChunk Build(ReadOnlySpan<ShaderRow> rows)
        {
            foreach (var row in rows) Add(row);
            body.Add(Expression.Empty());
            var block = Expression.Block([sum, gate, floor, slot, gatherIndex, gatherMass], body);
            return Expression.Lambda<StageChunk>(block, values, slotIndices, slotMasses).Compile();
        }

        private void Add(ShaderRow row)
        {
            switch (row)
            {
                case GatherComputeRow compute:
                    AddGatherCompute(compute);
                    break;
                case AffineRow affine:
                    AddSum(sum, affine.Bias, affine.Terms);
                    foreach (var gather in affine.Gathers) AddGatherRead(gather);
                    Store(affine.Position, sum);
                    break;
                case BooleanRow boolean:
                    {
                        AddSum(sum, boolean.Bias, boolean.Terms);
                        // The boolean atom of Fp32.EvaluateBoolean: two float steps.
                        var shifted = Expression.Add(sum, Expression.Constant(8_388_609f));
                        Store(boolean.Position, Expression.Subtract(shifted, Expression.Constant(8_388_609f)));
                        break;
                    }
                case RegluRow reglu:
                    AddSum(gate, reglu.GateBias, reglu.GateTerms);
                    AddSum(sum, reglu.Bias, reglu.Terms);
                    Store(reglu.Position, Expression.Multiply(sum, Expression.Call(MaximumMethod, Expression.Constant(0f), gate)));
                    break;
                case IndexRow index:
                    AddIndexRead(index);
                    break;
                default:
                    throw new InvalidOperationException($"unknown row {row}");
            }
        }

        private Expression Load(int position) => Expression.ArrayIndex(values, Expression.Constant(position));

        private void Store(int position, Expression value) =>
            body.Add(Expression.Assign(Expression.ArrayAccess(values, Expression.Constant(position)), value));

        // f(weight × input) as one float multiply. Weight 1 gives x and weight -1 gives -x, which are the
        // same bits (generated-js.ts).
        private Expression Product(ShaderTerm term)
        {
            var input = Load(term.Position);
            if (term.Weight == 1f) return input;
            if (term.Weight == -1f) return Expression.Negate(input);
            return Expression.Multiply(Expression.Constant(term.Weight), input);
        }

        // target = f(bias), then target = f(target + f(weight × input)) for each term in stored order.
        // f(-0 + x) is x for each x, so a bias of -0 starts with the first product.
        private void AddSum(ParameterExpression target, float bias, IReadOnlyList<ShaderTerm> terms)
        {
            if (terms.Count == 0)
            {
                body.Add(Expression.Assign(target, Expression.Constant(bias)));
                return;
            }
            var negativeZero = bias == 0 && float.IsNegative(bias);
            body.Add(Expression.Assign(target, negativeZero ? Product(terms[0]) : Expression.Add(Expression.Constant(bias), Product(terms[0]))));
            for (var index = 1; index < terms.Count; index++)
            {
                body.Add(Expression.Assign(target, Expression.Add(target, Product(terms[index]))));
            }
        }

        // The value of candidate k: a strided run of source cells, or one case for each list position.
        // k lies in 0..Count - 1 at each use.
        private Expression Candidate(ShaderCandidates candidates, Expression k)
        {
            if (candidates.Form == CandidateForm.Strided)
            {
                var position = Expression.Add(Expression.Constant(candidates.Base), Expression.Multiply(k, Expression.Constant(candidates.Stride)));
                return Expression.ArrayIndex(values, position);
            }
            if (candidates.Count == 1) return Load(candidates.PositionAt(0));
            var cases = Enumerable.Range(0, candidates.Count)
                .GroupBy(candidates.PositionAt)
                .Select(group => Expression.SwitchCase(Load(group.Key), group.Select(index => (Expression)Expression.Constant(index))))
                .ToArray();
            return Expression.Switch(typeof(float), k, Expression.Constant(float.NaN), null, cases);
        }

        // Circuit.ResolveIndexPosition and TensorExecution.ApplyIndexReads: NaN for a NaN index, +0 or
        // the end candidate outside the table, else the candidate at floor(index).
        private void AddIndexRead(IndexRow row)
        {
            AddSum(sum, row.IndexBias, row.IndexTerms);
            var last = row.Candidates.Count - 1;
            body.Add(Expression.Assign(floor, Expression.Call(FloorMethod, sum)));
            var below = row.BelowZero ? Expression.Constant(0f) : Candidate(row.Candidates, Expression.Constant(0));
            var above = row.AboveZero ? Expression.Constant(0f) : Candidate(row.Candidates, Expression.Constant(last));
            var inside = Candidate(row.Candidates, Expression.Convert(floor, typeof(int)));
            var read = Expression.Condition(
                Expression.LessThan(floor, Expression.Constant(0f)),
                below,
                Expression.Condition(Expression.GreaterThan(floor, Expression.Constant((float)last)), above, inside));
            // sum != sum holds only for NaN.
            Store(row.Position, Expression.Condition(Expression.NotEqual(sum, sum), Expression.Constant(float.NaN), read));
        }

        // TensorExecution.ApplyGathers, the compute: index += k × bit and mass += bit in double.
        private void AddGatherCompute(GatherComputeRow compute)
        {
            body.Add(Expression.Assign(gatherIndex, Expression.Constant(0.0)));
            body.Add(Expression.Assign(gatherMass, Expression.Constant(0.0)));
            for (var k = 0; k < compute.BitPositions.Length; k++)
            {
                var bit = Expression.Convert(Load(compute.BitPositions[k]), typeof(double));
                body.Add(Expression.Assign(gatherIndex, Expression.Add(gatherIndex, Expression.Multiply(Expression.Constant((double)k), bit))));
                body.Add(Expression.Assign(gatherMass, Expression.Add(gatherMass, bit)));
            }
            body.Add(Expression.Assign(Expression.ArrayAccess(slotIndices, Expression.Constant(compute.Slot)), Expression.Call(ToInt32Method, gatherIndex)));
            body.Add(Expression.Assign(Expression.ArrayAccess(slotMasses, Expression.Constant(compute.Slot)), Expression.Convert(gatherMass, typeof(float))));
        }

        // TensorExecution.ApplyGathers, the read: clamp the index into the table, read the candidate,
        // multiply a masked candidate by the mass, and add it to the row with one FP32 step.
        private void AddGatherRead(GatherTerm gather)
        {
            var last = gather.Candidates.Count - 1;
            body.Add(Expression.Assign(slot, Expression.ArrayIndex(slotIndices, Expression.Constant(gather.Slot))));
            body.Add(Expression.Assign(slot, Expression.Condition(Expression.LessThan(slot, Expression.Constant(0)), Expression.Constant(0), slot)));
            body.Add(Expression.Assign(slot, Expression.Condition(Expression.GreaterThan(slot, Expression.Constant(last)), Expression.Constant(last), slot)));
            var contribution = Candidate(gather.Candidates, slot);
            if (gather.Masked)
            {
                contribution = Expression.Multiply(Expression.ArrayIndex(slotMasses, Expression.Constant(gather.Slot)), contribution);
            }
            body.Add(Expression.Assign(sum, Expression.Add(sum, contribution)));
        }
    }
}
