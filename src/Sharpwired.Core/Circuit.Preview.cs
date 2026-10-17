// No Handwired source. PreviewAffine and FormGrid show the form and the grid that Affine stores,
// for the IL translator of phase 4. They use the builder rules of Circuit.cs (normalizeTerms,
// foldConstantTerms, composeAffineTerms and inferFormGrid of src/runtime/numeric-ui-core.ts).

using System.Collections.Immutable;

namespace Sharpwired.Core;

public sealed partial class Circuit
{
    /// <summary>
    /// The form that Affine(terms, bias) stores: the terms sorted, merged and without zero
    /// coefficients, the constant wires folded into the bias, and the small affine producers
    /// composed. PreviewAffine appends nothing. For an identity, the form is the single term with
    /// the coefficient 1 and the bias 0, and Affine returns the wire of that term.
    /// </summary>
    public (ImmutableArray<Term> Terms, double Bias) PreviewAffine(IReadOnlyList<Term> terms, double bias)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var width = Width;
        foreach (var term in terms) Validation.AssertWire(term.Wire, width);
        // The steps of Append for an affine operation without preserveRounding, in the same order.
        var (folded, foldedBias) = FoldConstantTerms(NormalizeTerms(terms), bias, ConstantList);
        if (IdentityWire(folded, foldedBias) is not null) return (folded, foldedBias);
        return ComposeAffineTerms(folded, foldedBias, SourceSize, OperationList);
    }

    /// <summary>
    /// The grid of an affine form evaluated in FP32, or null when it is not known. The grid is
    /// known when each term reads a wire with a grid, and the declared ranges keep each product and
    /// each partial sum within 2^24 grid steps, in any term order. For the form of PreviewAffine,
    /// this is the grid of the operation that Affine stores.
    /// </summary>
    public double? FormGrid(IReadOnlyList<Term> terms, double bias)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var width = Width;
        foreach (var term in terms) Validation.AssertWire(term.Wire, width);
        return InferFormGrid([.. terms], bias, GridList, RangeList)?.Grid;
    }
}
