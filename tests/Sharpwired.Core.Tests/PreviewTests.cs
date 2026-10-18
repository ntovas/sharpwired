// No Handwired source. Circuit.PreviewAffine and Circuit.FormGrid show the form and the grid that
// Circuit.Affine stores, for the translator of phase 4. Handwired has no translator.

namespace Sharpwired.Core.Tests;

public class PreviewTests
{
    // A circuit with source cells on grids and without grids, and with constant wires. It also has
    // small affine producers that Affine composes into a reader, and a wide producer that it does not
    // compose.
    private static (Circuit Circuit, Wire[] Wires) BuildCircuit()
    {
        var circuit = new Circuit(5);
        circuit.AssumeRange(circuit.Source(0), 0, 16_384);
        circuit.AssumeGrid(circuit.Source(0), 1);
        circuit.AssumeRange(circuit.Source(1), -8, 8);
        circuit.AssumeGrid(circuit.Source(1), 0.25);
        circuit.AssumeRange(circuit.Source(2), -1, 1);
        circuit.AssumeRange(circuit.Source(3), 0, 1);
        circuit.AssumeGrid(circuit.Source(3), 1);
        var wires = new List<Wire>(Enumerable.Range(0, 5).Select(circuit.Source))
        {
            circuit.Constant(3),
            circuit.Constant(0.5),
            circuit.Affine([(circuit.Source(0), 0.5), (circuit.Source(1), 2)], -26),
            circuit.Affine([(circuit.Source(1), 1), (circuit.Source(3), -1)]),
            circuit.Affine([(circuit.Source(0), 1), (circuit.Source(1), 1), (circuit.Source(2), 1), (circuit.Source(3), 1)]),
            circuit.RoundAffine([(circuit.Source(0), 1), (circuit.Source(1), 1)]),
            circuit.Multiply(circuit.Source(1), circuit.Source(2)),
        };
        return (circuit, [.. wires]);
    }

    // A seeded form of 2 to 5 terms. A term can repeat a wire, and a coefficient can be 0 or a value
    // with no short FP32 form. The bias is 0, a grid value or a value with no short form.
    private static (Term[] Terms, double Bias) SeededForm(Func<double> random, Wire[] wires)
    {
        double[] coefficients = [1, -1, 0.5, 2, 0.3, -0.25, 0, 3];
        double[] biases = [0, 1, -26, 0.1];
        var count = 2 + (int)(random() * 4);
        var terms = Enumerable.Range(0, count)
            .Select(_ => new Term(wires[(int)(random() * wires.Length)], coefficients[(int)(random() * coefficients.Length)]))
            .ToArray();
        return (terms, biases[(int)(random() * biases.Length)]);
    }

    [Fact]
    public void PreviewAffine_gives_the_form_that_Affine_stores_and_appends_no_operation()
    {
        var random = Js.CreateRandom(46);
        var checkedForms = 0;
        var gridForms = 0;
        var composedForms = 0;
        var attempts = 0;
        while (checkedForms < 10)
        {
            Assert.True(++attempts < 1_000, "the seeded forms give too few operations");
            var (circuit, wires) = BuildCircuit();
            var (terms, bias) = SeededForm(random, wires);
            var width = circuit.Width;
            var preview = circuit.PreviewAffine(terms, bias);
            var grid = circuit.FormGrid(preview.Terms, preview.Bias);
            Assert.Equal(width, circuit.Width);
            var wire = circuit.Affine(terms, bias);
            // A form that gives an existing wire stores no operation. The test then checks another form.
            if (wire.Position < width) continue;
            var stored = circuit.Operations[wire.Position - circuit.SourceSize];
            Assert.Equal(OperationKind.Affine, stored.Kind);
            Assert.Equal<Term>(stored.LeftTerms, preview.Terms);
            Assert.Equal(stored.LeftBias, preview.Bias);
            Assert.Equal(circuit.Grids[wire.Position], grid);
            checkedForms++;
            if (grid is not null) gridForms++;
            if (terms.Any(term => term.Wire.Position >= 7 && term.Wire.Position <= 8 && term.Coefficient != 0)) composedForms++;
        }
        // The forms include forms with a grid, forms without a grid, and forms that compose a producer.
        Assert.InRange(gridForms, 1, 9);
        Assert.InRange(composedForms, 1, 10);
    }

    [Fact]
    public void PreviewAffine_folds_constants_composes_a_small_producer_and_keeps_a_wide_producer()
    {
        var (circuit, wires) = BuildCircuit();
        var width = circuit.Width;
        // 2 × constant 3 folds into the bias. The producer [(x, 0.5), (y, 2)] - 26 composes. The wide producer stays.
        var preview = circuit.PreviewAffine([(wires[5], 2), (wires[7], 1), (wires[9], 1), (wires[2], 1)], 1);
        Assert.Equal<Term>([new(wires[0], 0.5), new(wires[1], 2), new(wires[2], 1), new(wires[9], 1)], preview.Terms);
        Assert.Equal(1 + 6 - 26, preview.Bias);
        Assert.Null(circuit.FormGrid(preview.Terms, preview.Bias));
        // An identity gives the single term, and Affine returns the wire itself.
        var identity = circuit.PreviewAffine([(wires[1], 1)], 0);
        Assert.Equal<Term>([new(wires[1], 1)], identity.Terms);
        Assert.Equal(0, identity.Bias);
        Assert.Equal(0.25, circuit.FormGrid([(wires[0], 0.5), (wires[1], 1)], -26));
        Assert.Equal(width, circuit.Width);
        Js.Throws(() => circuit.PreviewAffine([(Wire.FromPosition(99), 1)], 0), "outside the compiled circuit");
        Js.Throws(() => circuit.FormGrid([(Wire.FromPosition(99), 1)], 0), "outside the compiled circuit");
    }
}
