// Handwired source: the idea of the WGSL interpreter of the json-search tests (wgsl-interpreter.ts): run
// the rows of a shader on the CPU and compare them with the reference. RowInterpreter runs the rows of
// ShaderRows in the f32 form of WgslFloatWriter, with C# float arithmetic: one FP32 round for each
// product and each partial sum, in the stored term order. It also records whether a value of the
// evaluation is a nonzero subnormal value. The GPU tests use this record on a device that flushes
// subnormal values (decision 4 of the phase 5 plan).

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
        var values = new float[program.ValueCount];
        source.CopyTo(values);
        var subnormal = false;
        foreach (var value in source) subnormal |= float.IsSubnormal(value);
        var gatherIndices = new int[program.GatherSlotCount];
        var gatherMasses = new float[program.GatherSlotCount];

        float Note(float value)
        {
            subnormal |= float.IsSubnormal(value);
            return value;
        }

        // The left fold of WgslFloatWriter: a weight of 1 adds the value, a weight of -1 subtracts it,
        // and another weight adds or subtracts the product of its magnitude, after the sign bit.
        float Sum(float bias, ImmutableArray<ShaderTerm> terms)
        {
            var sum = Note(bias);
            foreach (var term in terms)
            {
                var value = values[term.Position];
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

        float Candidate(ShaderCandidates candidates, int index) => values[candidates.PositionAt(index)];

        foreach (var row in rows)
        {
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
                        gatherIndices[compute.Slot] = (int)index;
                        gatherMasses[compute.Slot] = mass;
                        break;
                    }
                case AffineRow affine:
                    {
                        var value = Sum(affine.Bias, affine.Terms);
                        foreach (var gather in affine.Gathers)
                        {
                            var candidate = Candidate(gather.Candidates, Math.Clamp(gatherIndices[gather.Slot], 0, gather.Candidates.Count - 1));
                            if (gather.Masked) candidate = Note((float)(gatherMasses[gather.Slot] * candidate));
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
                        var value = Candidate(read.Candidates, selected);
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
        sawSubnormal = subnormal;
        return values;
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
}
