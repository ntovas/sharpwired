// Handwired source: src/runtime/numeric-ui-core.ts (GatherPlan, MaskedProduct, planGatherLowering,
// attemptGatherMatch, parseMaskedProduct, resolveAffineSide, hasFiniteRange).

using System.Collections.Immutable;

namespace Sharpwired.Core;

// A recognized one-hot-masked sum, keyed by its operation index. The terms of the affine are
// products of the bits of the group and these candidate wires, in the order of the group positions.
internal sealed record GatherPlan(int GroupIndex, ImmutableArray<Wire> CandidateWires);

// One parsed term of a masked sum: Coefficient × f(left × max(0, GateCoefficient × gate)). When
// ExactMultiply is true, the term is an exact Multiply of left and gate.
internal readonly record struct MaskedProduct(double Coefficient, Wire LeftWire, Wire GateWire, double GateCoefficient, bool ExactMultiply);

public sealed partial class Circuit
{
    // Find each masked read of a group. A masked read is an affine whose terms are products of the
    // bits of a declared one-hot group and value wires. Each group position occurs exactly one
    // time. Such a sum adds f(0 × value) = 0 for each position that the group does not select, and
    // the selected value. So a read of the selected candidate gives the same value bit for bit
    // (execution-model.md, "One-hot groups and gathers"). The result maps an operation index to its
    // plan.
    internal static Dictionary<int, GatherPlan> PlanGatherLowering(Circuit circuit)
    {
        var plans = new Dictionary<int, GatherPlan>();
        var groups = circuit.OneHotGroupList;
        if (groups.Count == 0) return plans;
        // The groups of each member wire, in group order.
        var groupsByWire = new Dictionary<Wire, List<int>>();
        var positionsByGroup = new Dictionary<Wire, int>[groups.Count];
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var members = groups[groupIndex].Members;
            var positions = new Dictionary<Wire, int>();
            for (var position = 0; position < members.Length; position++)
            {
                var member = members[position];
                positions[member] = position;
                if (groupsByWire.TryGetValue(member, out var memberGroups)) memberGroups.Add(groupIndex);
                else groupsByWire[member] = [groupIndex];
            }
            positionsByGroup[groupIndex] = positions;
        }
        var operations = circuit.OperationList;
        for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
        {
            var operation = operations[operationIndex];
            if (operation.Kind != OperationKind.Affine || operation.LeftTerms.Length == 0) continue;
            var products = new List<MaskedProduct>(operation.LeftTerms.Length);
            var parsed = true;
            foreach (var (wire, coefficient) in operation.LeftTerms)
            {
                if (coefficient != 1 && coefficient != -1)
                {
                    parsed = false;
                    break;
                }
                if (ParseMaskedProduct(circuit, wire, coefficient) is not { } product)
                {
                    parsed = false;
                    break;
                }
                products.Add(product);
            }
            if (!parsed) continue;
            // Each term of a sum that matches reads the group through its left wire or its gate wire.
            // So only the groups of the first product can match. The groups of the gate wire come
            // first, then the groups of the left wire, each group one time, as a JavaScript Set
            // iterates.
            var first = products[0];
            var candidateGroups = new List<int>();
            var seenGroups = new HashSet<int>();
            foreach (var wire in new[] { first.GateWire, first.LeftWire })
            {
                if (!groupsByWire.TryGetValue(wire, out var wireGroups)) continue;
                foreach (var groupIndex in wireGroups)
                {
                    if (seenGroups.Add(groupIndex)) candidateGroups.Add(groupIndex);
                }
            }
            foreach (var groupIndex in candidateGroups)
            {
                var candidateWires = AttemptGatherMatch(circuit, products, groups[groupIndex], positionsByGroup[groupIndex]);
                if (candidateWires is not null)
                {
                    plans[operationIndex] = new GatherPlan(groupIndex, candidateWires.Value);
                    break;
                }
            }
        }
        return plans;
    }

    // Find the wire of one side of a ReGLU. The builder copies a small affine wire (fewer than
    // AffineShareThreshold terms) into each consumer. So a product with such a wire stores the
    // expanded definition of the wire, not one term. The expression cache of the builder gives the
    // wire back: an expanded copy has the key under which the builder stored the wire. Sign -1 means
    // that the side is the negated wire (the negative arm of a mixed-sign Multiply stores the gate
    // terms times -1).
    private static (Wire Wire, int Sign)? ResolveAffineSide(Circuit circuit, ImmutableArray<Term> terms, double bias, bool allowNegated)
    {
        if (bias == 0 && terms.Length == 1)
        {
            var (wire, coefficient) = terms[0];
            if (coefficient == 1) return (wire, 1);
            if (coefficient == -1 && allowNegated) return (wire, -1);
            // Another coefficient can still be the whole definition of a wire, so continue to the
            // look-up in the expression cache.
        }
        if (terms.Length == 0) return null;
        if (circuit.ExpressionWires.TryGetValue(SerializeExpression(OperationKind.Affine, terms, bias, [], 0), out var direct))
        {
            return (direct, 1);
        }
        if (!allowNegated) return null;
        ImmutableArray<Term> negatedTerms = [.. terms.Select(term => term with { Coefficient = -term.Coefficient })];
        if (circuit.ExpressionWires.TryGetValue(SerializeExpression(OperationKind.Affine, negatedTerms, -bias, [], 0), out var negated))
        {
            return (negated, -1);
        }
        return null;
    }

    // Parse one term of a possible masked sum. A usable product is a ReGLU with two conditions. Its
    // left side is a wire or the copied definition of that wire. Its gate side is a wire or its
    // negation. These are the forms that Multiply builds for bit × value in each orientation.
    private static MaskedProduct? ParseMaskedProduct(Circuit circuit, Wire wire, double coefficient)
    {
        if (wire.Position < circuit.SourceSize) return null;
        var operation = circuit.OperationList[wire.Position - circuit.SourceSize];
        if (operation.Macro is MultiplyMacro multiply)
        {
            return new MaskedProduct(coefficient, multiply.Left, multiply.Right, 1, ExactMultiply: true);
        }
        if (operation.Kind != OperationKind.Reglu) return null;
        if (ResolveAffineSide(circuit, operation.LeftTerms, operation.LeftBias, allowNegated: false) is not { } left) return null;
        if (ResolveAffineSide(circuit, operation.GateTerms, operation.GateBias, allowNegated: true) is not { } gate) return null;
        return new MaskedProduct(coefficient, left.Wire, gate.Wire, gate.Sign, ExactMultiply: false);
    }

    // The lowering needs value wires with finite declared ranges. A wire whose range admits Infinity
    // can make the dense sum NaN. For a product that the group does not select, f(Infinity × 0) is
    // NaN. The gather skips that product, so such reads stay dense. Each bound rounds to FP32 before
    // the test. A bound that rounds to Infinity, for example 3.5e38, admits Infinity.
    private static bool HasFiniteRange(Circuit circuit, Wire wire)
    {
        var range = circuit.RangeList[wire.Position];
        return float.IsFinite((float)range.Minimum) && float.IsFinite((float)range.Maximum);
    }

    // The negative arm of a mixed-sign Multiply, and whether a positive arm used it.
    private sealed class NegativeArm
    {
        public bool Used { get; set; }
    }

    // Try to read the parsed products as one complete read of the group. Each group position must
    // give exactly one value wire. Each term must equal that masked read in FP32. Return the
    // candidate wires in the order of the group positions, or null when the sum is not a pure masked
    // read of this group.
    private static ImmutableArray<Wire>? AttemptGatherMatch(
        Circuit circuit,
        List<MaskedProduct> products,
        OneHotGroup group,
        Dictionary<Wire, int> positionByMember)
    {
        // First find the negative arms of the mixed-sign products. For a value with a signed range,
        // Multiply builds positive = reglu(bit; value) and negative = reglu(bit; -value) and
        // subtracts them. Only the pair reads the value exactly, so each arm must find its partner.
        // The key is the pair (left wire, gate wire).
        var negativeArms = new Dictionary<(Wire Left, Wire Gate), NegativeArm>();
        foreach (var product in products)
        {
            if (product.Coefficient == -1
                && product.GateCoefficient == -1
                && positionByMember.ContainsKey(product.LeftWire)
                && !positionByMember.ContainsKey(product.GateWire))
            {
                if (!negativeArms.TryAdd((product.LeftWire, product.GateWire), new NegativeArm())) return null;
            }
        }
        var optionsByTerm = new List<List<(int Position, Wire ValueWire)>>();
        foreach (var product in products)
        {
            var (coefficient, leftWire, gateWire, gateCoefficient, exactMultiply) = product;
            if (coefficient == 1 && gateCoefficient == 1)
            {
                var options = new List<(int Position, Wire ValueWire)>();
                // The bit in the gate: f(value × max(0, bit)) selects each finite value. Both ends of
                // the declared range of the value must be finite. With Infinity, a product that the
                // group does not select becomes f(Infinity × 0) = NaN and makes the dense sum NaN.
                // A gather does not give this NaN.
                if (positionByMember.TryGetValue(gateWire, out var gatePosition) && HasFiniteRange(circuit, leftWire))
                {
                    options.Add((gatePosition, leftWire));
                }
                if (positionByMember.TryGetValue(leftWire, out var leftPosition))
                {
                    negativeArms.TryGetValue((leftWire, gateWire), out var negativeArm);
                    var gateRange = circuit.RangeList[gateWire.Position];
                    if (exactMultiply && HasFiniteRange(circuit, gateWire))
                    {
                        options.Add((leftPosition, gateWire));
                    }
                    else if (negativeArm is not null && !negativeArm.Used && HasFiniteRange(circuit, gateWire))
                    {
                        negativeArm.Used = true;
                        options.Add((leftPosition, gateWire));
                    }
                    else if (gateRange.Minimum >= 0 && double.IsFinite(gateRange.Maximum))
                    {
                        // The bit on the left: f(bit × max(0, value)) reads the value only when the
                        // ReLU is the identity. So the value must be nonnegative. It must also be
                        // finite, for the NaN reason above.
                        options.Add((leftPosition, gateWire));
                    }
                }
                if (options.Count == 0) return null;
                optionsByTerm.Add(options);
            }
            else if (gateCoefficient == -1 && positionByMember.ContainsKey(gateWire))
            {
                // A bit behind a negated gate: max(0, -bit) is exactly 0 for a bit of 0 or 1. So the
                // term adds a signed zero that the sum ignores. When the left value can be infinite,
                // the reference computes f(Infinity × 0) = NaN, not a zero.
                if (!HasFiniteRange(circuit, leftWire)) return null;
            }
            else if (coefficient == -1 && gateCoefficient == -1 && positionByMember.ContainsKey(leftWire))
            {
                // The negative arm of a mixed-sign pair. The check below makes sure that a positive
                // arm used it.
            }
            else
            {
                return null;
            }
        }
        foreach (var arm in negativeArms.Values)
        {
            if (!arm.Used) return null;
        }
        // Give the positions. First place the terms with one option. Then, in term order, each term
        // whose bit can be on either side takes the first of its positions that is still open.
        var candidateWires = new Wire?[group.Members.Length];
        var flexibleTerms = new List<List<(int Position, Wire ValueWire)>>();
        foreach (var options in optionsByTerm)
        {
            if (options.Count == 1)
            {
                var (position, valueWire) = options[0];
                if (candidateWires[position] is not null) return null;
                candidateWires[position] = valueWire;
            }
            else
            {
                flexibleTerms.Add(options);
            }
        }
        foreach (var options in flexibleTerms)
        {
            var placed = false;
            foreach (var (position, valueWire) in options)
            {
                if (candidateWires[position] is null)
                {
                    candidateWires[position] = valueWire;
                    placed = true;
                    break;
                }
            }
            if (!placed) return null;
        }
        var result = ImmutableArray.CreateBuilder<Wire>(candidateWires.Length);
        foreach (var candidate in candidateWires)
        {
            if (candidate is not { } wire) return null;
            result.Add(wire);
        }
        return result.MoveToImmutable();
    }
}
