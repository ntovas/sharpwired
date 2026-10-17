// Handwired source: src/runtime/cells.ts (createSourceTable with declare, atMostOne, size, nameOf,
// policyOf, createCircuit, next and finish, declareSpec, DeclaredCell, Rule, and the return value of
// finish).
//
// The source table is the float[] that the host writes before an evaluation, and that the host
// writes the next-state values back into. Each cell is declared one time, with its policy, its range
// and its initial value. The table derives the position of each cell (in declaration order), the
// range, grid, exact bit and one-hot declarations of the circuit, the initial table, a validator,
// and the wire that holds the next value of each cell.
//
// A build has two phases, because a Circuit needs its source size before the first operation:
// 1. Declare each cell, then call CreateCircuit.
// 2. Build the behavior, pair each state cell with its next value through Next, then call Finish.
//
// Handwired declares a nested object literal and gets the same shape back with wires (the mapped type
// CellWires). C# has no mapped types, so the caller writes a record for the wires and fills it with
// ICellScope calls, one for each field. A component exports a method that declares its cells in a
// scope, and a build method that takes the circuit, the table, its wires and the wires of the parent.

using System.Collections.Immutable;
using System.Globalization;
using Sharpwired.Core;

namespace Sharpwired.Cells;

/// <summary>
/// The declare methods of a table, or of a part of a table under a name prefix. Handwired
/// table.declare(spec, name) with the recursion of declareSpec. Each method declares its cells after
/// the cells declared before, in the order of the call. The name of a cell is the prefix, a dot and
/// the name, or the name alone when the prefix is empty. An empty name adds no part to the prefix.
/// </summary>
public interface ICellScope
{
    /// <summary>Declare one cell, for example "down.x".</summary>
    Wire Declare(string name, CellSpec cell);

    /// <summary>Declare a list of cells: "name[0]", "name[1]", and so on.</summary>
    Wire[] Declare(string name, IReadOnlyList<CellSpec> cells);

    /// <summary>Declare the members "name[i]", then add one oneHot rule with the name "name".</summary>
    Wire[] Declare(string name, OneHotSpec group);

    /// <summary>
    /// Declare the cells "name[rank][item]" row by row, then add the rules "name rank r" for each
    /// row, then the rules "name item i" for each column. The result is indexed [rank][item].
    /// </summary>
    Wire[][] Declare(string name, PermutationSpec permutation);

    /// <summary>Declare a struct or a component under the prefix "name", for example "name.field".</summary>
    T Declare<T>(string name, Func<ICellScope, T> declare);

    /// <summary>Declare count structs or components under the prefixes "name[i]", for example "name[i].field".</summary>
    T[] DeclareList<T>(string name, int count, Func<ICellScope, int, T> declare);
}

/// <summary>
/// The return value of Handwired finish: the circuit, the wire that holds the next value of each cell
/// (in cell order), the initial table, and the validator.
/// </summary>
public sealed record FinishedTable(Circuit Circuit, ImmutableArray<Wire> NextSource, ImmutableArray<float> InitialSource, CellValidator Validator);

/// <summary>Handwired createSourceTable: the cell declarations of the source table of a program.</summary>
public sealed class SourceTable : ICellScope
{
    private readonly List<DeclaredCell> cells = [];
    private readonly List<CellRule> rules = [];
    private readonly CellScope root;
    private Circuit? circuit;
    private bool finished;

    public SourceTable()
    {
        root = new CellScope(this, "");
    }

    /// <summary>Handwired table.size: the number of cells declared so far.</summary>
    public int Size => cells.Count;

    public Wire Declare(string name, CellSpec cell) => root.Declare(name, cell);

    public Wire[] Declare(string name, IReadOnlyList<CellSpec> cells) => root.Declare(name, cells);

    public Wire[] Declare(string name, OneHotSpec group) => root.Declare(name, group);

    public Wire[][] Declare(string name, PermutationSpec permutation) => root.Declare(name, permutation);

    public T Declare<T>(string name, Func<ICellScope, T> declare) => root.Declare(name, declare);

    public T[] DeclareList<T>(string name, int count, Func<ICellScope, int, T> declare) => root.DeclareList(name, count, declare);

    /// <summary>
    /// Handwired table.atMostOne: a rule that only the validator checks. The members add up to at most
    /// 1, for example at most one of four pointer event cells that have coordinate cells between them.
    /// </summary>
    public void AtMostOne(string name, IReadOnlyList<Wire> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (circuit is not null) throw new InvalidOperationException("add rules before createCircuit");
        if (!members.All(member => member.Position < cells.Count && cells[member.Position].Spec.ExactBit))
        {
            throw new ArgumentException($"{name}: members must be exact bits");
        }
        rules.Add(new CellRule(name, AtMostOne: true, [.. members], AllowAllZero: true, DeclareToCircuit: false));
    }

    /// <summary>Handwired table.nameOf: the name of a cell, or "wire N" for a wire that is not a cell.</summary>
    public string NameOf(Wire wire) =>
        wire.Position < cells.Count ? cells[wire.Position].Name : $"wire {wire.Position.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Handwired table.policyOf: the policy of a cell, or null for a wire that is not a cell.</summary>
    public CellPolicy? PolicyOf(Wire wire) => wire.Position < cells.Count ? cells[wire.Position].Spec.Policy : null;

    /// <summary>
    /// Handwired table.createCircuit: make the circuit. For each cell in order, declare its range, then
    /// its grid when it has one, then the exact bit when it is one. Then declare each rule that has
    /// DeclareToCircuit, in rule order, with AssumeOneHot.
    /// </summary>
    public Circuit CreateCircuit()
    {
        if (circuit is not null) throw new InvalidOperationException("createCircuit may only run once");
        var created = new Circuit(cells.Count);
        for (var index = 0; index < cells.Count; index++)
        {
            var spec = cells[index].Spec;
            var wire = created.Source(index);
            created.AssumeRange(wire, spec.Range.Minimum, spec.Range.Maximum);
            if (spec.Grid is { } grid) created.AssumeGrid(wire, grid);
            // The circuit keeps the declaration and builds nothing differently for it, see Circuit.ExactBits.
            if (spec.ExactBit) created.AssumeBit(wire);
        }
        foreach (var rule in rules)
        {
            if (rule.DeclareToCircuit) created.AssumeOneHot(rule.Members, rule.AllowAllZero);
        }
        circuit = created;
        return created;
    }

    /// <summary>
    /// Handwired table.next: pair each state cell in stored with the wire under the same field name or
    /// index in next, for example table.Next(cells.Spring, new SpringCells(velocity, position)). The
    /// walk visits the fields in ordinal order of their names. It checks the whole shape and each pair
    /// before it records a pair. The pair checks run in this order: a source cell, a state cell, no
    /// earlier next value, a wire of the circuit.
    /// </summary>
    public void Next<T>(T stored, T next)
        where T : notnull
    {
        if (circuit is null) throw new InvalidOperationException("call createCircuit before table.next");
        if (finished) throw new InvalidOperationException("table.next after finish has no effect");
        var width = circuit.SourceSize + circuit.Operations.Count;
        var pairs = new List<(Wire Stored, Wire Next)>();
        WireStruct.PairLeaves(stored, next, "", pairs);
        var paired = new HashSet<Wire>();
        foreach (var (wire, nextWire) in pairs)
        {
            if (wire.Position >= cells.Count)
            {
                throw new ArgumentException($"wire {wire.Position.ToString(CultureInfo.InvariantCulture)} is not a source cell");
            }
            var declared = cells[wire.Position];
            if (declared.Spec.Policy != CellPolicy.State) throw new ArgumentException($"{declared.Name} is not a state cell");
            if (declared.Next is not null || paired.Contains(wire)) throw new InvalidOperationException($"{declared.Name} already has a next value");
            if (!(nextWire.Position >= 0 && nextWire.Position < width))
            {
                throw new ArgumentException($"the next value of {declared.Name} is not a wire of the circuit");
            }
            paired.Add(wire);
        }
        foreach (var (wire, nextWire) in pairs) cells[wire.Position].Next = nextWire;
    }

    /// <summary>
    /// Handwired table.finish: return the circuit, the wire that holds the next value of each cell, the
    /// initial table and the validator. A constant or an input cell holds itself, an event holds the
    /// wire of Constant(0), and a state cell holds its next value. Finish validates the initial table
    /// before it returns.
    /// </summary>
    public FinishedTable Finish()
    {
        if (circuit is null) throw new InvalidOperationException("call createCircuit before finish");
        if (finished) throw new InvalidOperationException("finish may only run once");
        var missing = cells.Where(declared => declared.Spec.Policy == CellPolicy.State && declared.Next is null).Select(declared => declared.Name).ToArray();
        if (missing.Length != 0) throw new InvalidOperationException($"{string.Join(", ", missing)} never received a next value");
        finished = true;
        var built = circuit;
        Wire? zero = cells.Any(declared => declared.Spec.Policy == CellPolicy.Event) ? built.Constant(0) : null;
        var nextSource = cells.Select((declared, index) => declared.Spec.Policy == CellPolicy.Event ? zero!.Value : declared.Next ?? built.Source(index)).ToImmutableArray();
        // A Float32Array store rounds each initial value to FP32.
        var initialSource = cells.Select(declared => (float)declared.Spec.Initial).ToImmutableArray();
        var validator = new CellValidator([.. cells.Select(declared => (declared.Name, declared.Spec))], rules);
        validator.Validate(initialSource.AsSpan());
        return new FinishedTable(built, nextSource, initialSource, validator);
    }

    private void AssertDeclaring()
    {
        if (circuit is not null) throw new InvalidOperationException("declare every cell before createCircuit");
    }

    private Wire DeclareCell(string name, CellSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cells.Add(new DeclaredCell(name, spec));
        return Wire.FromPosition(cells.Count - 1);
    }

    // Handwired DeclaredCell.
    private sealed class DeclaredCell(string name, CellSpec spec)
    {
        public string Name { get; } = name;

        public CellSpec Spec { get; } = spec;

        public Wire? Next { get; set; }
    }

    // Handwired declareSpec, one method for each kind of spec, under one name prefix.
    private sealed class CellScope(SourceTable table, string prefix) : ICellScope
    {
        public Wire Declare(string name, CellSpec cell)
        {
            table.AssertDeclaring();
            return table.DeclareCell(Qualify(name), cell);
        }

        public Wire[] Declare(string name, IReadOnlyList<CellSpec> cells)
        {
            ArgumentNullException.ThrowIfNull(cells);
            table.AssertDeclaring();
            var listName = Qualify(name);
            return [.. cells.Select((cell, index) => table.DeclareCell(ElementName(listName, index), cell))];
        }

        public Wire[] Declare(string name, OneHotSpec group)
        {
            ArgumentNullException.ThrowIfNull(group);
            table.AssertDeclaring();
            var groupName = Qualify(name);
            Wire[] members = [.. group.Members.Select((member, index) => table.DeclareCell(ElementName(groupName, index), member))];
            table.rules.Add(new CellRule(groupName, AtMostOne: false, [.. members], group.AllowAllZero, group.DeclareToCircuit));
            return members;
        }

        public Wire[][] Declare(string name, PermutationSpec permutation)
        {
            ArgumentNullException.ThrowIfNull(permutation);
            table.AssertDeclaring();
            var matrixName = Qualify(name);
            Wire[][] matrix =
            [
                .. permutation.Cells.Select((row, rank) =>
                    row.Select((member, item) => table.DeclareCell(ElementName(ElementName(matrixName, rank), item), member)).ToArray()),
            ];
            for (var rank = 0; rank < matrix.Length; rank++)
            {
                table.rules.Add(new CellRule($"{matrixName} rank {Text(rank)}", AtMostOne: false, [.. matrix[rank]], AllowAllZero: false, DeclareToCircuit: true));
            }
            for (var item = 0; item < matrix.Length; item++)
            {
                table.rules.Add(new CellRule(
                    $"{matrixName} item {Text(item)}", AtMostOne: false, [.. matrix.Select(row => row[item])], AllowAllZero: false, DeclareToCircuit: true));
            }
            return matrix;
        }

        public T Declare<T>(string name, Func<ICellScope, T> declare)
        {
            ArgumentNullException.ThrowIfNull(declare);
            table.AssertDeclaring();
            return declare(new CellScope(table, Qualify(name)));
        }

        public T[] DeclareList<T>(string name, int count, Func<ICellScope, int, T> declare)
        {
            ArgumentNullException.ThrowIfNull(declare);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            table.AssertDeclaring();
            var listName = Qualify(name);
            var items = new T[count];
            for (var index = 0; index < count; index++) items[index] = declare(new CellScope(table, ElementName(listName, index)), index);
            return items;
        }

        private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string ElementName(string name, int index) => $"{name}[{Text(index)}]";

        private string Qualify(string name) => prefix.Length == 0 ? name : name.Length == 0 ? prefix : $"{prefix}.{name}";
    }
}
