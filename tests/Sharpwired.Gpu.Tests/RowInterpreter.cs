// Handwired source: the idea of the WGSL interpreter of the json-search tests (wgsl-interpreter.ts). The
// idea is to run the rows of a shader on the CPU and compare them with the reference. RowInterpreter runs
// the rows of ShaderRows in the f32 form of WgslFloatWriter, with C# float arithmetic. It does one FP32
// round for each product and each partial sum, in the stored term order. It also records whether a value
// of the evaluation is a nonzero subnormal value. The GPU tests use this record on a device that flushes
// subnormal values (decision 4 of the phase 5 plan). RunInteger runs the forms of WgslIntegerWriter
// (IntegerPlan) with C# int and uint arithmetic, which wraps as WGSL i32 and u32 do. It runs the f32
// form for each f32 row.

using System.Collections.Immutable;
using Sharpwired.Core;
using Xunit.Abstractions;
using static Sharpwired.Core.Tests.RandomPrograms;

namespace Sharpwired.Gpu.Tests;

internal static class RowInterpreter
{
    /// <summary>
    /// Run the rows on one source table. Return the packed values. <paramref name="sawSubnormal"/> is
    /// true when a source cell, a literal, a product, a partial sum or a row value is a nonzero value
    /// of magnitude below 2^-126.
    /// </summary>
    public static float[] Run(TensorProgram program, ImmutableArray<ShaderRow> rows, ReadOnlySpan<float> source, out bool sawSubnormal)
    {
        var machine = new FloatMachine(program, source);
        foreach (var row in rows) machine.Evaluate(row);
        sawSubnormal = machine.SawSubnormal;
        return machine.Values;
    }

    /// <summary>
    /// Run the forms of the integer writer on one source table. Return, for each packed position, the
    /// f32 value that the shader stores for it. This is f32(w) or f32(b) for an integer, and the f32
    /// value for an f32 row.
    /// </summary>
    public static float[] RunInteger(TensorProgram program, ImmutableArray<ShaderRow> rows, IntegerPlan plan, ReadOnlySpan<float> source)
    {
        var machine = new FloatMachine(program, source);
        var proofs = plan.Proofs;
        // The i32 value, or the u32 value as an int, of each proven position.
        var integers = new int[program.ValueCount];
        var values = machine.Values;
        // The buffer of the source cells, which a candidate read converts.
        var cells = source.ToArray();

        void Set(int position, int value)
        {
            integers[position] = proofs[position].IsBit ? (int)(uint)value : value;
            values[position] = proofs[position].IsBit ? (uint)value : value;
        }

        int Sum(float bias, IEnumerable<ShaderTerm> terms)
        {
            var sum = (int)bias;
            foreach (var term in terms) sum = unchecked(sum + ((int)term.Weight * integers[term.Position]));
            return sum;
        }

        // The candidate as the shader reads it. A source cell converts from the buffer with i32 or u32,
        // and a list of computed values selects over first..final.
        float Candidate(ShaderCandidates candidates, int index, WholeKind kind, int first, int final)
        {
            float Typed(int position) => kind == WholeKind.Float ? values[position] : integers[position];
            if (candidates.Count == 1) return Typed(candidates.PositionAt(0));
            if (candidates.Form != CandidateForm.Computed)
            {
                // An index outside the table reads past the stored list: the test fails.
                var value = cells[candidates.PositionAt(index)];
                return kind switch
                {
                    WholeKind.Whole => (int)value,
                    WholeKind.Bit => (uint)value,
                    _ => value,
                };
            }
            var selected = Typed(candidates.Positions[first]);
            for (var candidate = first + 1; candidate <= final; candidate++)
            {
                if (index == candidate) selected = Typed(candidates.Positions[candidate]);
            }
            return selected;
        }

        for (var cell = 0; cell < program.SourceSize; cell++)
        {
            if (proofs[cell].IsBit) Set(cell, source[cell] > 0.5f ? 1 : 0);
            else if (proofs[cell].IsProven) Set(cell, (int)source[cell]);
        }
        foreach (var row in rows)
        {
            if (row is GatherComputeRow)
            {
                machine.Evaluate(row);
                continue;
            }
            var position = IntegerPlan.PositionOf(row);
            switch (plan.Forms[position])
            {
                case FloatForm:
                    machine.Evaluate(row);
                    // The conversion of a proven value that the range arithmetic does not reach.
                    if (proofs[position].IsProven) integers[position] = (int)values[position];
                    break;
                case SumForm { Row: var affine }:
                    {
                        var sum = Sum(affine.Bias, affine.Terms);
                        foreach (var gather in affine.Gathers)
                        {
                            var last = gather.Candidates.Count - 1;
                            var candidate = (int)Candidate(gather.Candidates, Math.Clamp(machine.GatherIndices[gather.Slot], 0, last), WholeKind.Whole, 0, last);
                            sum = unchecked(sum + (gather.Masked ? (int)machine.GatherMasses[gather.Slot] * candidate : candidate));
                        }
                        Set(position, sum);
                        break;
                    }
                case ProductForm { Row: var reglu } product:
                    {
                        var gate = Sum(reglu.GateBias, reglu.GateTerms);
                        Set(position, unchecked(Sum(reglu.Bias, reglu.Terms) * (product.GateNonNegative ? gate : Math.Max(gate, 0))));
                        break;
                    }
                case ConjunctionForm conjunction:
                    {
                        var bit = 1u;
                        foreach (var operand in conjunction.All) bit &= (uint)integers[operand];
                        foreach (var test in conjunction.Tests)
                        {
                            var zero = test switch
                            {
                                ComparisonTest { Inner: var inner } => Sum(MathF.Max(inner.GateBias, 0), inner.GateTerms.Where(term => term.Weight > 0))
                                    <= Sum(MathF.Max(-inner.GateBias, 0), inner.GateTerms.Where(term => term.Weight < 0).Select(term => term with { Weight = -term.Weight })),
                                NumberTest number => integers[number.Position] == 0,
                                _ => throw new InvalidOperationException("unknown test"),
                            };
                            bit &= zero ? 1u : 0u;
                        }
                        var any = 0u;
                        foreach (var operand in conjunction.ZeroBits) any |= (uint)integers[operand];
                        if (conjunction.ZeroBits.Length > 0) bit &= any ^ 1u;
                        Set(position, (int)bit);
                        break;
                    }
                case GateForm { Row: var reglu }:
                    {
                        var gate = Sum(reglu.GateBias, reglu.GateTerms);
                        Set(position, proofs[position].IsBit ? (gate > 0 ? 1 : 0) : Math.Max(gate, 0));
                        break;
                    }
                case ScoreForm { Row: var boolean }:
                    Set(position, Sum(boolean.Bias, boolean.Terms));
                    break;
                case IndexForm { Row: var read } form:
                    {
                        var kind = proofs[position].Kind;
                        var last = read.Candidates.Count - 1;
                        var index = Sum(read.IndexBias, read.IndexTerms);
                        var value = Candidate(read.Candidates, form.Clamp ? Math.Clamp(index, 0, last) : index, kind, form.First, form.Final);
                        if ((form.ZeroAbove && index > last) || (form.ZeroBelow && index < 0)) value = 0f;
                        if (kind == WholeKind.Float) values[position] = value;
                        else Set(position, (int)value);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"no form for position {position}");
            }
        }
        return values;
    }

    // The f32 form of each row, on the packed values.
    private sealed class FloatMachine
    {
        public FloatMachine(TensorProgram program, ReadOnlySpan<float> source)
        {
            Values = new float[program.ValueCount];
            source.CopyTo(Values);
            foreach (var value in source) SawSubnormal |= float.IsSubnormal(value);
            GatherIndices = new int[program.GatherSlotCount];
            GatherMasses = new float[program.GatherSlotCount];
        }

        public float[] Values { get; }

        public int[] GatherIndices { get; }

        public float[] GatherMasses { get; }

        public bool SawSubnormal { get; private set; }

        public void Evaluate(ShaderRow row)
        {
            var values = Values;
            switch (row)
            {
                case GatherComputeRow compute:
                    {
                        var index = 0f;
                        var mass = 0f;
                        for (var bit = 0; bit < compute.BitPositions.Length; bit++)
                        {
                            var value = values[compute.BitPositions[bit]];
                            if (bit > 0) index = (float)(index + (float)(bit * value));
                            mass = (float)(mass + value);
                        }
                        // An exact one-hot gives a small whole number, which converts exactly.
                        GatherIndices[compute.Slot] = (int)index;
                        GatherMasses[compute.Slot] = mass;
                        break;
                    }
                case AffineRow affine:
                    {
                        var value = Sum(affine.Bias, affine.Terms);
                        foreach (var gather in affine.Gathers)
                        {
                            var candidate = values[gather.Candidates.PositionAt(Math.Clamp(GatherIndices[gather.Slot], 0, gather.Candidates.Count - 1))];
                            if (gather.Masked) candidate = Note((float)(GatherMasses[gather.Slot] * candidate));
                            value = Note((float)(value + candidate));
                        }
                        values[affine.Position] = Note(value);
                        break;
                    }
                case BooleanRow boolean:
                    {
                        var shifted = Note((float)(Sum(boolean.Bias, boolean.Terms) + 8_388_609f));
                        values[boolean.Position] = Note((float)(shifted - 8_388_609f));
                        break;
                    }
                case RegluRow reglu:
                    {
                        var left = Sum(reglu.Bias, reglu.Terms);
                        var gate = Sum(reglu.GateBias, reglu.GateTerms);
                        var relu = float.IsNaN(gate) ? gate : gate > 0f ? gate : 0f;
                        values[reglu.Position] = Note((float)(left * relu));
                        break;
                    }
                case IndexRow read:
                    {
                        var index = Sum(read.IndexBias, read.IndexTerms);
                        var floor = MathF.Floor(index);
                        var last = read.Candidates.Count - 1;
                        var selected = float.IsNaN(index) ? 0 : (int)Math.Clamp(floor, 0f, last);
                        var value = values[read.Candidates.PositionAt(selected)];
                        if (read.BelowZero && floor < 0f) value = 0f;
                        if (read.AboveZero && floor > last) value = 0f;
                        if (float.IsNaN(index)) value = float.NaN;
                        values[read.Position] = Note(value);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"unknown row {row.GetType().Name}");
            }
        }

        private float Note(float value)
        {
            SawSubnormal |= float.IsSubnormal(value);
            return value;
        }

        // The left fold of WgslFloatWriter. A weight of 1 adds the value, and a weight of -1 subtracts
        // it. Another weight adds or subtracts the product of its magnitude, after the sign bit.
        private float Sum(float bias, ImmutableArray<ShaderTerm> terms)
        {
            var sum = Note(bias);
            foreach (var term in terms)
            {
                var value = Values[term.Position];
                if (term.Weight == 1f) sum = (float)(sum + value);
                else if (term.Weight == -1f) sum = (float)(sum - value);
                else
                {
                    var product = Note((float)(Note(MathF.Abs(term.Weight)) * value));
                    sum = float.IsNegative(term.Weight) ? (float)(sum - product) : (float)(sum + product);
                }
                Note(sum);
            }
            return sum;
        }
    }
}

public sealed class RowInterpreterTests(ITestOutputHelper output)
{
    // The two compile modes of the GPU differential test, both with every wire as a root.
    private static readonly (string Label, bool LowersGathers)[] Modes =
    [
        ("every wire as a root", true),
        ("no gather lowering, every wire as a root", false),
    ];

    // The rows of the f32 form give the reference value on each live wire of 200 seeded circuits. The
    // comparison is Object.is without gather lowering, and sameNumericValue with it: a gather can
    // change the sign of an exactly zero result (TensorDifferentialTests header). The writer also
    // writes each program, so each row form of the generator passes through it.
    [Fact]
    public void The_f32_rows_give_the_reference_value_on_every_live_wire_of_seeded_programs()
    {
        var random = Js.CreateRandom(0x5a17);
        const int CircuitCount = 200;
        const int SourcesPerCircuit = 4;
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];
        var comparedWires = 0;
        var subnormalEvaluations = 0;
        var rowKinds = new Dictionary<string, int>();
        var candidateForms = new int[Enum.GetValues<CandidateForm>().Length];
        var maskedGathers = 0;

        for (var circuitIndex = 0; circuitIndex < CircuitCount; circuitIndex++)
        {
            var (circuit, cells, groups, _) = BuildCircuit(random, builderCalls);
            var sources = Enumerable.Range(0, SourcesPerCircuit).Select(_ => CreateSource(random, cells, groups)).ToArray();
            var references = sources.Select(source => circuit.EvaluateReference(source)).ToArray();
            foreach (var (label, lowersGathers) in Modes)
            {
                var program = circuit.CompileTensorProgram(lowerGathers: lowersGathers);
                var rows = ShaderRows.FlattenProgram(program);
                Assert.False(string.IsNullOrEmpty(WgslFloatWriter.Write(circuit, program).Code));
                foreach (var row in rows)
                {
                    var kind = row.GetType().Name;
                    rowKinds[kind] = rowKinds.GetValueOrDefault(kind) + 1;
                    if (row is IndexRow read) candidateForms[(int)read.Candidates.Form]++;
                    if (row is AffineRow affine)
                    {
                        foreach (var gather in affine.Gathers)
                        {
                            candidateForms[(int)gather.Candidates.Form]++;
                            if (gather.Masked) maskedGathers++;
                        }
                    }
                }
                for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    var values = RowInterpreter.Run(program, rows, sources[sourceIndex], out var sawSubnormal);
                    if (sawSubnormal) subnormalEvaluations++;
                    for (var position = 0; position < program.WireOrder.Count; position++)
                    {
                        var wire = program.WireOrder[position];
                        var expected = references[sourceIndex][wire];
                        var actual = values[position];
                        var agrees = lowersGathers ? Js.SameNumericValue(expected, actual) : Js.ObjectIs(expected, actual);
                        Assert.True(agrees, $"circuit {circuitIndex}, source {sourceIndex}, {label}: wire {wire} got {actual:R}, reference {expected:R}");
                        comparedWires++;
                    }
                }
            }
        }

        var kinds = string.Join(" ", rowKinds.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
        var forms = string.Join(" ", Enum.GetValues<CandidateForm>().Select(form => $"{form}={candidateForms[(int)form]}"));
        output.WriteLine($"[rows] circuits={CircuitCount} comparedWires={comparedWires} subnormalEvaluations={subnormalEvaluations} maskedGathers={maskedGathers}");
        output.WriteLine($"[rows] row kinds: {kinds}");
        output.WriteLine($"[rows] candidate forms: {forms}");

        // Each row kind and each candidate form occurs, so the comparison covers each form.
        foreach (var kind in new[] { nameof(AffineRow), nameof(BooleanRow), nameof(RegluRow), nameof(IndexRow), nameof(GatherComputeRow) })
        {
            Assert.True(rowKinds.GetValueOrDefault(kind) > 0, $"no {kind}");
        }
        foreach (var form in Enum.GetValues<CandidateForm>()) Assert.True(candidateForms[(int)form] > 0, $"no candidate form {form}");
        Assert.True(maskedGathers > 0, "no masked gather");
    }

    // The forms of the integer writer give the reference value on each live wire of the same 200
    // seeded circuits, in both compile modes. The comparison is sameNumericValue: an i32 has no -0
    // (question 3 of the phase 5 plan). The writer also writes each program.
    [Fact]
    public void The_integer_forms_give_the_reference_value_on_every_live_wire_of_seeded_programs()
    {
        var random = Js.CreateRandom(0x5a17);
        const int CircuitCount = 200;
        const int SourcesPerCircuit = 8;
        var builderCalls = new int[Enum.GetValues<BuilderCall>().Length];
        var comparedWires = 0;
        var (positions, wholePositions, bitPositions) = (0, 0, 0);
        var forms = new Dictionary<string, int>();
        var (comparisons, numberTests, zeroBits, convertedRows, wholeGathers) = (0, 0, 0, 0, 0);

        for (var circuitIndex = 0; circuitIndex < CircuitCount; circuitIndex++)
        {
            var (circuit, cells, groups, _) = BuildCircuit(random, builderCalls);
            var sources = Enumerable.Range(0, SourcesPerCircuit).Select(_ => CreateSource(random, cells, groups)).ToArray();
            var references = sources.Select(source => circuit.EvaluateReference(source)).ToArray();
            // The generator writes each source cell on its grid and each exact bit as 0, -0 or 1.
            foreach (var source in sources) IntegerProof.ValidateSourceDeclarations(circuit, source);
            foreach (var (label, lowersGathers) in Modes)
            {
                var program = circuit.CompileTensorProgram(lowerGathers: lowersGathers);
                var rows = ShaderRows.FlattenProgram(program);
                var plan = IntegerPlan.Create(IntegerProof.Analyze(circuit, program, rows), rows, program.GatherSlotCount, ShaderRows.RootPositions(program));
                Assert.False(string.IsNullOrEmpty(WgslIntegerWriter.Write(circuit, program).Code));
                foreach (var proof in plan.Proofs)
                {
                    positions++;
                    if (proof.Kind == WholeKind.Whole) wholePositions++;
                    if (proof.Kind == WholeKind.Bit) bitPositions++;
                }
                foreach (var form in plan.Forms)
                {
                    if (form is null) continue;
                    var name = form.GetType().Name;
                    forms[name] = forms.GetValueOrDefault(name) + 1;
                    if (form is ConjunctionForm conjunction)
                    {
                        comparisons += conjunction.Tests.Count(test => test is ComparisonTest);
                        numberTests += conjunction.Tests.Count(test => test is NumberTest);
                        zeroBits += conjunction.ZeroBits.Length;
                    }
                    if (form is FloatForm floatForm && plan.Proofs[IntegerPlan.PositionOf(floatForm.Row)].IsProven) convertedRows++;
                    if (form is SumForm { Row.Gathers.Length: > 0 }) wholeGathers++;
                }
                for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
                {
                    var values = RowInterpreter.RunInteger(program, rows, plan, sources[sourceIndex]);
                    for (var position = 0; position < program.WireOrder.Count; position++)
                    {
                        var wire = program.WireOrder[position];
                        var expected = references[sourceIndex][wire];
                        var actual = values[position];
                        Assert.True(
                            Js.SameNumericValue(expected, actual),
                            $"circuit {circuitIndex}, source {sourceIndex}, {label}: wire {wire} ({plan.Proofs[position]}, {plan.Forms[position]?.GetType().Name}) got {actual:R}, reference {expected:R}");
                        comparedWires++;
                    }
                }
            }
        }

        var formCounts = string.Join(" ", forms.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
        output.WriteLine($"[integer rows] circuits={CircuitCount} comparedWires={comparedWires}");
        output.WriteLine($"[integer rows] positions={positions} whole={wholePositions} bit={bitPositions} "
            + $"({100.0 * (wholePositions + bitPositions) / positions:F1}% held as i32 or u32)");
        output.WriteLine($"[integer rows] forms: {formCounts}");
        output.WriteLine($"[integer rows] comparisons={comparisons} numberTests={numberTests} zeroBits={zeroBits} convertedRows={convertedRows} wholeGathers={wholeGathers}");

        // Each form occurs, so the comparison covers each form.
        foreach (var name in new[] { nameof(FloatForm), nameof(SumForm), nameof(ProductForm), nameof(ConjunctionForm), nameof(GateForm), nameof(ScoreForm), nameof(IndexForm) })
        {
            Assert.True(forms.GetValueOrDefault(name) > 0, $"no {name}");
        }
        Assert.True(comparisons > 0 && numberTests > 0 && zeroBits > 0 && convertedRows > 0 && wholeGathers > 0, "a gate test or a conversion does not occur");
    }
}
