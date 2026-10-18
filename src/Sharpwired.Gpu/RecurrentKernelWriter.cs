// Handwired source: src/json-search/loop-emit.ts (the for loop of a loop program, with each register as a
// var). The step kernel of a RecurrentStep (phase 5 plan, question 5): one invocation runs the step program
// of one lane until done is 1, until the step limit, until a cell check fails, or until the evaluations of
// one dispatch. The kernel checks each cell with the declarations of the CellValidator of the step table, so
// that a value past a cell range stops the lane and is never a silent wrong value (the phase 4 carried
// item).

using System.Globalization;
using System.Text;
using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL;
using ValidatorCell = (string Name, double Minimum, double Maximum, bool ExactBit, double? Grid);

namespace Sharpwired.Gpu;

/// <summary>The status of one lane of a step kernel, word 0 of the lane record.</summary>
internal enum LaneStatus : uint
{
    // The lane has no evaluation yet. The kernel reads its cells from the prefix outputs.
    Fresh = 0,

    // The lane has more evaluations to do.
    Running = 1,

    // An evaluation gave done = 1.
    Completed = 2,

    // The evaluation count reached the step limit.
    Limit = 3,

    // A cell check failed. Word 2 of the lane record tells which check.
    Invalid = 4,
}

/// <summary>
/// The layout of the prefix outputs in the buffer that the step kernel reads at the first evaluation.
/// The cells of lane t start at <c>t * Stride + Offset</c>. The prefix overflow bits follow the cells.
/// </summary>
internal sealed record RecurrentInitial(int Offset, int Stride, int OverflowCount);

/// <summary>The WGSL text of a step kernel and the layout of its buffers.</summary>
internal sealed record RecurrentKernel(string Code, int CellCount, RecurrentInitial Initial, int WorkgroupSize);

/// <summary>
/// The writer of the step kernel. Bindings: 0 <c>initial</c> (the prefix outputs), 1 <c>state</c> (the
/// cells of each lane), 2 <c>params</c>, 3 <c>lanes</c> (three words for each lane: the status, the
/// steps and the detail of a failed check), 4 <c>run</c> (the step limit and the evaluations of one
/// dispatch).
/// </summary>
/// <remarks>
/// <para>The kernel holds each state cell in a <c>var</c> and each invariant in a <c>let</c>. For each
/// lane it repeats the order of <see cref="RecurrentRunner.Run"/>: at the first evaluation, it checks
/// the prefix overflow bits and each cell. Then, for each evaluation: stop at the step limit, evaluate the
/// rows, check each next value of a state cell, check that done is finite, stop when done is not 0, and
/// copy the next values.</para>
/// <para>The checks use the bits of the values: <c>is_finite</c>, the order key <c>ordered</c> for the
/// range, <c>is_bit</c>, <c>on_grid</c> and <c>ones</c> for the one-hot and at-most-one rules. So a
/// subnormal flush and the fast math of a backend do not change a comparison. The WGSL specification
/// (section 15.7.2) does not guarantee <c>is_nan</c> and <c>is_finite</c> on a computed value, because
/// an implementation can assume that no NaN and no infinity occur. The tests measure them on each
/// adapter. Thus the GPU check only finds the lane: the CPU replay of <see cref="GpuRecurrentRunner"/> is
/// the authority for the error message.</para>
/// </remarks>
internal static class RecurrentKernelWriter
{
    /// <summary>The words of one lane record: the status, the steps and the detail.</summary>
    public const int LaneWords = 3;

    /// <summary>The detail of a done value that is not finite.</summary>
    public const uint DoneFault = 0xFFFFFFFE;

    /// <summary>The detail of a prefix overflow bit that is not 0.</summary>
    public const uint OverflowFault = 0xFFFFFFFD;

    private const string NaN = "bitcast<f32>(0x7fc00000u)";

    private const string Helpers = """
        struct Run {
            limit: u32,
            budget: u32,
        }

        // The order key of the bits of a value: a < b gives ordered(a) < ordered(b) for finite values,
        // and -0 is one key below +0.
        fn ordered(v: f32) -> i32 {
            let b: i32 = bitcast<i32>(v);
            return b ^ ((b >> 31u) & 0x7fffffffi);
        }

        fn is_bit(v: f32) -> bool {
            let b: u32 = bitcast<u32>(v);
            return (b & 0x7fffffffu) == 0u || b == 0x3f800000u;
        }

        fn ones(v: f32) -> u32 { return select(0u, 1u, bitcast<u32>(v) == 0x3f800000u); }

        fn is_nonzero(v: f32) -> bool { return (bitcast<u32>(v) & 0x7fffffffu) != 0u; }

        // True when the finite value v is a multiple of 2^e. The value is mantissa * 2^exponent, and the
        // lowest 1 bit of the mantissa gives the smallest power of two in the value. No f32 arithmetic.
        fn on_grid(v: f32, e: i32) -> bool {
            let b: u32 = bitcast<u32>(v) & 0x7fffffffu;
            if (b == 0u) {
                return true;
            }
            let field: u32 = b >> 23u;
            var mantissa: u32 = b & 0x7fffffu;
            var exponent: i32 = -149i;
            if (field != 0u) {
                mantissa = mantissa | 0x800000u;
                exponent = i32(field) - 150i;
            }
            let zeros: u32 = countTrailingZeros(mantissa);
            return exponent + i32(zeros) >= e;
        }


        """;

    /// <summary>The compiled step program of the CPU runner: the next value of each cell, then done.</summary>
    public static TensorProgram StepProgram(RecurrentStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return ProgramEvaluator.Create<IReadOnlyList<Wire>>(step.Table, [step.Done]).Program;
    }

    /// <summary>
    /// The layout of the outputs of the prefix program of the CPU runner. Its roots are the next values
    /// of the argument cells, the outputs, then the overflow bits (<see cref="TranslatedEvaluator.Program"/>).
    /// </summary>
    public static RecurrentInitial Initial(RecurrentFunction function, TensorProgram prefixProgram)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(prefixProgram);
        var offset = function.Prefix.Table.NextSource.Length;
        var cells = function.Prefix.Outputs.Length;
        var overflows = function.Prefix.LoopOverflowBits.Length;
        if (prefixProgram.Roots.Count != offset + cells + overflows)
        {
            throw new ArgumentException($"the prefix program has {prefixProgram.Roots.Count} roots, and the prefix has {offset + cells + overflows}", nameof(prefixProgram));
        }
        return new RecurrentInitial(offset, prefixProgram.Roots.Count, overflows);
    }

    /// <summary>Write the step kernel of the function, with the programs of the CPU runner.</summary>
    public static RecurrentKernel Write(RecurrentFunction function, GpuWriter writer)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Write(function.Step, StepProgram(function.Step), Initial(function, function.Prefix.CreateEvaluator().Program), writer);
    }

    /// <summary>Write the step kernel with the step program of the CPU runner.</summary>
    public static RecurrentKernel Write(RecurrentStep step, RecurrentInitial initial, GpuWriter writer) =>
        Write(step, StepProgram(step), initial, writer);

    /// <summary>
    /// Write the step kernel. The roots of <paramref name="program"/> are the next value of each cell,
    /// in cell order, then done.
    /// </summary>
    /// <exception cref="ArgumentException">The program is not a step program of the table, or a grid of a
    /// cell is not a form that the kernel can check exactly.</exception>
    public static RecurrentKernel Write(RecurrentStep step, TensorProgram program, RecurrentInitial initial, GpuWriter writer)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(initial);
        var circuit = step.Table.Circuit;
        var cellCount = circuit.SourceSize;
        ShaderRows.Validate(circuit, program);
        if (program.Roots.Count != cellCount + 1)
        {
            throw new ArgumentException($"the step program has {program.Roots.Count} roots, and a step of {cellCount} cells needs {cellCount + 1}", nameof(program));
        }
        if (initial.Offset < 0 || initial.OverflowCount < 0 || initial.Stride < initial.Offset + cellCount + initial.OverflowCount)
        {
            throw new ArgumentException($"the prefix layout {initial} does not hold {cellCount} cells", nameof(initial));
        }

        var cells = step.Table.Validator.Cells().ToArray();
        var grids = cells.Select(cell => cell.Grid is { } grid ? GridExponent(grid, cell.Name) : (int?)null).ToArray();
        var isState = new bool[cellCount];
        foreach (var cell in step.State) isState[cell.Wire.Position] = true;

        var rows = ShaderRows.FlattenProgram(program);
        var rootPositions = ShaderRows.RootPositions(program);
        FloatBody body;
        IntegerBody? integer = null;
        if (writer == GpuWriter.Float32)
        {
            body = new FloatBody(cellCount, cellName: CellName);
            foreach (var row in rows) body.Write(row);
        }
        else if (writer == GpuWriter.Integer)
        {
            var plan = IntegerPlan.Create(IntegerProof.Analyze(circuit, program, rows), rows, program.GatherSlotCount, rootPositions);
            integer = new IntegerBody(plan, cellCount, CellName);
            integer.WriteRows(rows);
            body = integer.Body;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(writer), writer, "unknown writer");
        }

        string FloatText(int position) => position < 0 ? NaN : body.Name(position);

        var nextLines = new List<string>();
        var nextChecks = new List<string>();
        for (var cell = 0; cell < cellCount; cell++)
        {
            if (!isState[cell]) continue;
            var position = rootPositions[cell];
            nextLines.Add($"let next{cell}: f32 = {FloatText(position)};");
            var check = position >= 0 && integer?.IntegerValue(position) is { } value
                ? IntegerCheck(value, cells[cell], grids[cell])
                : FloatCheck($"next{cell}", cells[cell], grids[cell]);
            nextChecks.Add(Stop(check, Text((uint)cell)));
        }
        var rules = step.Table.Validator.Rules;
        for (var rule = 0; rule < rules.Count; rule++)
        {
            if (rules[rule].Members.Any(member => isState[member.Position]))
            {
                nextChecks.Add(Stop(RuleCheck(rules[rule], position => isState[position] ? $"next{position}" : CellName(position)), Text((uint)(cellCount + rule))));
            }
        }
        var doneLine = $"let done: f32 = {FloatText(rootPositions[cellCount])};";

        var freshChecks = new List<string>();
        for (var bit = 0; bit < initial.OverflowCount; bit++)
        {
            freshChecks.Add($"if (is_nonzero(initial[origin + {WgslText.Uint(cellCount + bit)}])) {{ status = 4u; detail = {Text(OverflowFault)}; break; }}");
        }
        for (var cell = 0; cell < cellCount; cell++) freshChecks.Add(Stop(FloatCheck(CellName(cell), cells[cell], grids[cell]), Text((uint)cell)));
        for (var rule = 0; rule < rules.Count; rule++) freshChecks.Add(Stop(RuleCheck(rules[rule], CellName), Text((uint)(cellCount + rule))));

        // After each call of Name, so that the list holds each read of a cell.
        var rowStatements = body.BodyStatements();
        var code = new StringBuilder();
        code.Append("@group(0) @binding(0) var<storage, read> initial: array<f32>;\n");
        code.Append("@group(0) @binding(1) var<storage, read_write> state: array<f32>;\n");
        code.Append("@group(0) @binding(2) var<storage, read> params: Params;\n");
        code.Append("@group(0) @binding(3) var<storage, read_write> lanes: array<u32>;\n");
        code.Append("@group(0) @binding(4) var<storage, read> run: Run;\n\n");
        code.Append(WgslText.Prelude);
        code.Append(Helpers);
        foreach (var table in body.Tables) code.Append(table).Append('\n');
        code.Append('\n');
        code.Append($"@compute @workgroup_size({WgslFloatWriter.WorkgroupSize})\n");
        code.Append("fn main(@builtin(global_invocation_id) id: vec3<u32>) {\n");
        var main = new List<string>
        {
            "let t: u32 = id.x;",
            "if (t >= params.count) {",
            "    return;",
            "}",
            $"let lane: u32 = t * {WgslText.Uint(LaneWords)};",
            "var status: u32 = lanes[lane];",
            "if (status > 1u) {",
            "    return;",
            "}",
            "var steps: u32 = lanes[lane + 1u];",
            "var detail: u32 = 0u;",
            "let fresh: bool = status == 0u;",
            $"let base: u32 = t * {WgslText.Uint(cellCount)};",
            $"let origin: u32 = t * {WgslText.Uint(initial.Stride)} + {WgslText.Uint(initial.Offset)};",
        };
        for (var cell = 0; cell < cellCount; cell++)
        {
            var read = $"select(state[base + {WgslText.Uint(cell)}], initial[origin + {WgslText.Uint(cell)}], fresh)";
            main.Add($"{(isState[cell] ? "var" : "let")} {CellName(cell)}: f32 = {read};");
        }
        main.Add("if (fresh) {");
        main.Add("    status = 1u;");
        main.Add("    loop {");
        main.AddRange(freshChecks.Where(line => line.Length > 0).Select(line => "        " + line));
        main.Add("        break;");
        main.Add("    }");
        main.Add("}");
        main.Add("var evaluations: u32 = 0u;");
        main.Add("loop {");
        var loop = new List<string>
        {
            "if (status != 1u) { break; }",
            "if (steps >= run.limit) { status = 3u; break; }",
            "if (evaluations == run.budget) { break; }",
            "evaluations = evaluations + 1u;",
        };
        loop.AddRange(rowStatements);
        loop.AddRange(nextLines);
        loop.Add(doneLine);
        loop.AddRange(nextChecks);
        loop.Add($"if (!is_finite(done)) {{ status = 4u; detail = {Text(DoneFault)}; break; }}");
        loop.Add("if (is_nonzero(done)) { status = 2u; break; }");
        for (var cell = 0; cell < cellCount; cell++)
        {
            if (isState[cell]) loop.Add($"{CellName(cell)} = next{cell};");
        }
        loop.Add("steps = steps + 1u;");
        main.AddRange(loop.Where(line => line.Length > 0).Select(line => "    " + line));
        main.Add("}");
        main.Add("lanes[lane] = status;");
        main.Add("lanes[lane + 1u] = steps;");
        main.Add("lanes[lane + 2u] = detail;");
        for (var cell = 0; cell < cellCount; cell++) main.Add($"state[base + {WgslText.Uint(cell)}] = {CellName(cell)};");
        foreach (var line in main) code.Append("    ").Append(line).Append('\n');
        code.Append("}\n");
        return new RecurrentKernel(code.ToString(), cellCount, initial, WgslFloatWriter.WorkgroupSize);
    }

    private static string CellName(int cell) => $"cell{cell.ToString(CultureInfo.InvariantCulture)}";

    private static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture) + "u";

    // The statement that stops the lane with the status Invalid when the check fails. A check that is
    // always true gives no statement.
    private static string Stop(string check, string detail) =>
        check == "true" ? "" : $"if (!({check})) {{ status = 4u; detail = {detail}; break; }}";

    // CellValidator on an f32 value: finite and within the range, then an exact bit, then the grid. The
    // range test compares order keys with the nearest FP32 bounds inside the range. A lower bound of 0
    // becomes -0, so that -0 passes, as the validator lets it pass.
    private static string FloatCheck(string value, ValidatorCell cell, int? grid)
    {
        var parts = new List<string> { $"is_finite({value})" };
        if (cell.Minimum > double.NegativeInfinity)
        {
            if (cell.Minimum > float.MaxValue) return "false";
            var low = (float)cell.Minimum;
            if (low < cell.Minimum) low = MathF.BitIncrement(low);
            parts.Add($"ordered({value}) >= {WgslText.Int(Key(low == 0 ? -0f : low))}");
        }
        if (cell.Maximum < double.PositiveInfinity)
        {
            if (cell.Maximum < -float.MaxValue) return "false";
            var high = (float)cell.Maximum;
            if (high > cell.Maximum) high = MathF.BitDecrement(high);
            parts.Add($"ordered({value}) <= {WgslText.Int(Key(high == 0 ? 0f : high))}");
        }
        if (cell.ExactBit) parts.Add($"is_bit({value})");
        if (grid is { } exponent) parts.Add($"on_grid({value}, {WgslText.Int(exponent)})");
        return string.Join(" && ", parts);
    }

    // CellValidator on an i32 or a u32 value of the integer writer. Such a value is finite. A whole value
    // within a range has the whole bounds of the range. A u32 value is 0 or 1, so it needs no bit test,
    // and no range test for a range that holds 0 and 1. A grid of the form 2^-n holds each whole value.
    private static string IntegerCheck((WholeKind Kind, string Text) value, ValidatorCell cell, int? grid)
    {
        var bit = value.Kind == WholeKind.Bit;
        var number = bit ? $"i32({value.Text})" : value.Text;
        var parts = new List<string>();
        if (cell.Minimum > double.NegativeInfinity)
        {
            var low = Math.Ceiling(cell.Minimum);
            if (low > int.MaxValue) return "false";
            if (low > int.MinValue && !(bit && low <= 0)) parts.Add($"{number} >= {WgslText.Int((int)low)}");
        }
        if (cell.Maximum < double.PositiveInfinity)
        {
            var high = Math.Floor(cell.Maximum);
            if (high <= int.MinValue) return "false";
            if (high < int.MaxValue && !(bit && high >= 1)) parts.Add($"{number} <= {WgslText.Int((int)high)}");
        }
        if (cell.ExactBit && !bit) parts.Add($"({number} == 0i || {number} == 1i)");
        if (grid is > 0) parts.Add($"on_grid(f32({number}), {WgslText.Int(grid.Value)})");
        return parts.Count == 0 ? "true" : string.Join(" && ", parts);
    }

    // A one-hot rule: exactly one member is 1, or at most one when the rule allows all zeros. An
    // at-most-one rule: at most one member is 1. The members are exact bits, so the mass is a count.
    private static string RuleCheck(CellRule rule, Func<int, string> name)
    {
        var mass = string.Join(" + ", rule.Members.Select(member => $"ones({name(member.Position)})"));
        return rule.AtMostOne || rule.AllowAllZero ? $"{mass} <= 1u" : $"{mass} == 1u";
    }

    // The order key of WGSL ordered.
    private static int Key(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        return bits ^ ((bits >> 31) & 0x7fffffff);
    }

    // The exponent e of a grid 2^e. Circuit.AssumeGrid accepts a power of two from 2^-149 to 2^127 only,
    // and SourceTable.CreateCircuit declares each cell grid with it. The validator divides in float64,
    // which is exact for a power of two: value / 2^e is a whole number exactly when the lowest 1 bit
    // of the value is at 2^e or above.
    private static int GridExponent(double grid, string name)
    {
        var exponent = Math.ILogB(grid);
        if (!(grid > 0) || exponent < -149 || exponent > 127 || Math.ScaleB(1, exponent) != grid)
        {
            throw new ArgumentException($"the grid {Fp32.Format(grid)} of the cell {name} is not a power of two from 2^-149 to 2^127");
        }
        return exponent;
    }
}
