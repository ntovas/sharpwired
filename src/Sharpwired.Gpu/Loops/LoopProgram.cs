// Handwired source: src/json-search/loop-program.ts (Binding, Write, Stage, LoopProgram, createStage). A
// program as loops over the positions of one document. Each stage is one translated step method. It runs
// one time (Once), for the positions 0, 1, 2 and up (Forward), or from the last position down to 0
// (Backward). Each input cell of a stage reads an item (the document cell at the position). Or it reads one
// fixed document cell, a register, a field or a shared cell. Each output of a stage writes a register, a
// field, a document output or a position output. Handwired binds a source cell of a stage circuit to a
// source. Sharpwired binds a cell path of the translated function, so the step method names the cells.
// Differences from Handwired:
// - A cell with no source is refused. Handwired reads a cell with no binding as a shared cell at the
//   offset of the stage; here each shared cell names its offset (CellSource.Shared).
// - Handwired "results" writes registers as document outputs after the last stage. Here a stage that
//   runs once reads the registers and writes the document outputs.
// - EvaluatePlain runs the plain step methods by reflection over each position, as the reference of the
//   translation (phase 6 plan, question 1).

using System.Collections.Immutable;
using System.Globalization;
using Sharpwired.Cells;
using Sharpwired.Core;
using Sharpwired.IL;

namespace Sharpwired.Gpu;

/// <summary>How a stage runs: one time, from position 0 up, or from the last position down.</summary>
public enum StageRepeat
{
    Once,
    Forward,
    Backward,
}

/// <summary>What one input cell of a stage reads.</summary>
public abstract record CellSource
{
    private CellSource()
    {
    }

    /// <summary>The cell of buffer Buffer at the position of the loop.</summary>
    public sealed record Item(int Buffer) : CellSource;

    /// <summary>One fixed cell of the document.</summary>
    public sealed record DocumentCell(int Buffer, int Column) : CellSource;

    /// <summary>A register.</summary>
    public sealed record Register(int Index) : CellSource;

    /// <summary>One component of a field at the position of the loop.</summary>
    public sealed record Field(int Index, int Component) : CellSource;

    /// <summary>One shared cell: one value for all documents.</summary>
    public sealed record Shared(int Offset) : CellSource;
}

/// <summary>Where one output of a stage goes.</summary>
public abstract record StageWrite
{
    private StageWrite()
    {
    }

    /// <summary>A register.</summary>
    public sealed record Register(int Index) : StageWrite;

    /// <summary>One component of a field at the position of the loop.</summary>
    public sealed record Field(int Index, int Component) : StageWrite;

    /// <summary>One column of a document group. Only a stage that runs once writes it.</summary>
    public sealed record Document(int Group, int Column) : StageWrite;

    /// <summary>The value of a position group at the position of the loop. Only a looped stage writes it.</summary>
    public sealed record Position(int Group) : StageWrite;
}

/// <summary>A buffer of document cells that the stages read, for example 513 cells for each document.</summary>
public sealed record LoopBuffer(string Name, int CellsPerDocument);

/// <summary>One value that a stage carries from one position to the next, with its value before the first stage.</summary>
public sealed record LoopRegister(string Name, float Initial);

/// <summary>Width values for each position, that one stage writes and a later stage reads. Bits: each value is 0 or 1.</summary>
public sealed record LoopField(string Name, int Width, bool Bits);

/// <summary>The kind of an output group: values for each document, or one value for each position.</summary>
public enum OutputGroupKind
{
    Document,
    Position,
}

/// <summary>
/// An output group. A document group holds ValuesPerDocument values for each document. A position group
/// holds one value for each position, so ValuesPerDocument is the number of positions. Fill is the value
/// before a stage writes the value. Fill is also the value of a position past the end of the text when the
/// loops stop at the length.
/// </summary>
public sealed record LoopOutputGroup(string Name, OutputGroupKind Kind, int ValuesPerDocument, float Fill);

/// <summary>
/// One stage: a translated step method, what each of its cells reads, and where each of its outputs goes.
/// A key is a cell path or an output path of the function, for example "state.depth" or "item1". An array
/// path, for example "transitions", maps its elements to consecutive shared offsets, register indices,
/// field components or document columns from the given start. A key of one element, for example
/// "transitions[3]", has priority over the array path.
/// </summary>
public sealed record LoopStage(
    string Name,
    StageRepeat Repeat,
    TranslatedFunction Function,
    ImmutableDictionary<string, CellSource> Cells,
    ImmutableDictionary<string, StageWrite> Writes);

/// <summary>The cell that holds the length of the text, for StopAtLength.</summary>
public readonly record struct LengthCell(int Buffer, int Column);

/// <summary>
/// The outputs of one document: the document groups in group order, then the position groups in group
/// order, each with ValuesPerDocument values.
/// </summary>
public sealed record LoopDocumentValues(ImmutableArray<float> DocumentOutputs, ImmutableArray<float> PositionOutputs);

/// <summary>
/// The resolved form of one stage: the compiled program of its outputs and the source of each cell in cell
/// order. It also holds the write of each output leaf, and the root of each output leaf in the program.
/// </summary>
internal sealed record StagePlan(
    LoopStage Stage,
    TensorProgram Program,
    ImmutableArray<CellSource> Sources,
    ImmutableArray<StageWrite> Writes,
    ImmutableArray<int> OutputRoots);

/// <summary>A program as loops over the positions of one document (Handwired LoopProgram).</summary>
public sealed class LoopProgram
{
    private LoopProgram(
        int positions, ImmutableArray<LoopBuffer> buffers, ImmutableArray<LoopRegister> registers, ImmutableArray<LoopField> fields,
        ImmutableArray<LoopOutputGroup> outputGroups, ImmutableArray<LoopStage> stages, int sharedSize, LengthCell? lengthCell,
        ImmutableArray<StagePlan> plans)
    {
        Positions = positions;
        Buffers = buffers;
        Registers = registers;
        Fields = fields;
        OutputGroups = outputGroups;
        Stages = stages;
        SharedSize = sharedSize;
        LengthCell = lengthCell;
        Plans = plans;
    }

    /// <summary>The number of positions of the looped stages, for example 512 code points.</summary>
    public int Positions { get; }

    public ImmutableArray<LoopBuffer> Buffers { get; }

    public ImmutableArray<LoopRegister> Registers { get; }

    public ImmutableArray<LoopField> Fields { get; }

    public ImmutableArray<LoopOutputGroup> OutputGroups { get; }

    public ImmutableArray<LoopStage> Stages { get; }

    /// <summary>The number of shared cells.</summary>
    public int SharedSize { get; }

    /// <summary>The cell that holds the length of the text, or null.</summary>
    public LengthCell? LengthCell { get; }

    // The resolved form of each stage, in stage order.
    internal ImmutableArray<StagePlan> Plans { get; }

    /// <summary>
    /// Check and resolve the program, and compile each stage for its outputs. Throws ArgumentException for
    /// a cell with no source, an output with no write, or a path that the function does not have. It also
    /// throws for a register that no stage writes before or in the stage that reads it. It also throws for
    /// a field component that no earlier stage writes, or an index, a column or an offset outside its part.
    /// It also throws for an item or a field that a stage that runs once reads. It also throws for a field
    /// or a position output that a stage that runs once writes. It also throws for a document output that a
    /// looped stage writes, or two outputs of one stage that write the same place. It also throws for a stage
    /// that writes some but not all components of a field of bits. It also throws for two stages with the same name, a
    /// step method with a loop overflow bit, or a size mismatch.
    /// </summary>
    public static LoopProgram Create(
        int positions, IReadOnlyList<LoopBuffer> buffers, IReadOnlyList<LoopRegister> registers,
        IReadOnlyList<LoopField> fields, IReadOnlyList<LoopOutputGroup> outputGroups,
        IReadOnlyList<LoopStage> stages, int sharedSize, LengthCell? lengthCell = null)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        ArgumentNullException.ThrowIfNull(registers);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(outputGroups);
        ArgumentNullException.ThrowIfNull(stages);
        var shape = new ProgramShape(positions, [.. buffers], [.. registers], [.. fields], [.. outputGroups], sharedSize);
        shape.Check();
        if (stages.Count == 0) throw new ArgumentException("a loop program needs at least one stage", nameof(stages));
        var registerWritten = new bool[shape.Registers.Length];
        var fieldWritten = shape.Fields.Select(field => new bool[field.Width]).ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var plans = ImmutableArray.CreateBuilder<StagePlan>(stages.Count);
        foreach (var stage in stages)
        {
            ArgumentNullException.ThrowIfNull(stage);
            if (!names.Add(stage.Name)) throw new ArgumentException($"two stages have the name {stage.Name}", nameof(stages));
            plans.Add(new StageResolver(shape, stage).Resolve(registerWritten, fieldWritten));
        }
        if (lengthCell is { } length) shape.CheckColumn(length.Buffer, length.Column, "the length cell");
        return new LoopProgram(positions, shape.Buffers, shape.Registers, shape.Fields, shape.OutputGroups, [.. stages], sharedSize, lengthCell, plans.MoveToImmutable());
    }

    /// <summary>
    /// Run the plain step methods by reflection in a loop over one document, at each position, as the
    /// reference of the translation. The length cell does not stop the loops.
    /// </summary>
    /// <param name="documentCells">The cells of the document in each buffer, in buffer order.</param>
    /// <param name="shared">The shared cells.</param>
    /// <exception cref="ArgumentException">A span has the wrong length.</exception>
    /// <exception cref="InvalidOperationException">A source table breaks a declaration, with the stage, the position and the message of the check.</exception>
    public LoopDocumentValues EvaluatePlain(IReadOnlyList<ReadOnlyMemory<float>> documentCells, ReadOnlySpan<float> shared)
    {
        ArgumentNullException.ThrowIfNull(documentCells);
        if (documentCells.Count != Buffers.Length) throw new ArgumentException($"the document has {documentCells.Count} buffers, and the program has {Buffers.Length}", nameof(documentCells));
        for (var buffer = 0; buffer < Buffers.Length; buffer++)
        {
            if (documentCells[buffer].Length != Buffers[buffer].CellsPerDocument)
            {
                throw new ArgumentException($"buffer {Buffers[buffer].Name} holds {documentCells[buffer].Length} cells, and the program needs {Buffers[buffer].CellsPerDocument}", nameof(documentCells));
            }
        }
        if (shared.Length != SharedSize) throw new ArgumentException($"the shared table holds {shared.Length} cells, and the program needs {SharedSize}", nameof(shared));

        var registers = Registers.Select(register => register.Initial).ToArray();
        var fields = Fields.Select(field => new float[Positions * field.Width]).ToArray();
        var outputs = OutputGroups.Select(group => Enumerable.Repeat(group.Fill, group.ValuesPerDocument).ToArray()).ToArray();
        foreach (var plan in Plans)
        {
            var count = plan.Stage.Repeat == StageRepeat.Once ? 1 : Positions;
            var source = new float[plan.Sources.Length];
            for (var step = 0; step < count; step++)
            {
                var position = plan.Stage.Repeat == StageRepeat.Backward ? Positions - 1 - step : step;
                for (var cell = 0; cell < source.Length; cell++)
                {
                    source[cell] = plan.Sources[cell] switch
                    {
                        CellSource.Item item => documentCells[item.Buffer].Span[position],
                        CellSource.DocumentCell fixedCell => documentCells[fixedCell.Buffer].Span[fixedCell.Column],
                        CellSource.Register register => registers[register.Index],
                        CellSource.Field field => fields[field.Index][(position * Fields[field.Index].Width) + field.Component],
                        CellSource.Shared sharedCell => shared[sharedCell.Offset],
                        _ => throw new InvalidOperationException("unknown cell source"),
                    };
                }
                float[] values;
                try
                {
                    values = plan.Stage.Function.EvaluatePlain(source);
                }
                catch (ArgumentException error)
                {
                    throw new InvalidOperationException(FaultPrefix(plan.Stage, position) + error.Message, error);
                }
                for (var output = 0; output < plan.Writes.Length; output++)
                {
                    var value = values[output];
                    switch (plan.Writes[output])
                    {
                        case StageWrite.Register register:
                            registers[register.Index] = value;
                            break;
                        case StageWrite.Field field:
                            if (BitsFault(field, value) is { } message) throw new InvalidOperationException(FaultPrefix(plan.Stage, position) + message);
                            fields[field.Index][(position * Fields[field.Index].Width) + field.Component] = value;
                            break;
                        case StageWrite.Document document:
                            outputs[document.Group][document.Column] = value;
                            break;
                        case StageWrite.Position target:
                            outputs[target.Group][position] = value;
                            break;
                    }
                }
            }
        }
        var documentOutputs = OutputGroups.Select((group, index) => (group, index)).Where(pair => pair.group.Kind == OutputGroupKind.Document).SelectMany(pair => outputs[pair.index]);
        var positionOutputs = OutputGroups.Select((group, index) => (group, index)).Where(pair => pair.group.Kind == OutputGroupKind.Position).SelectMany(pair => outputs[pair.index]);
        return new LoopDocumentValues([.. documentOutputs], [.. positionOutputs]);
    }

    // The start of the message of a fault: the stage, and the position of a looped stage.
    internal static string FaultPrefix(LoopStage stage, int position) =>
        stage.Repeat == StageRepeat.Once
            ? $"stage {stage.Name}: "
            : $"stage {stage.Name}, position {position.ToString(CultureInfo.InvariantCulture)}: ";

    // The message of a value of a field of bits that is not 0 or 1, or null.
    internal string? BitsFault(StageWrite.Field field, float value) =>
        Fields[field.Index].Bits && value != 0 && value != 1
            ? $"field {Fields[field.Index].Name} component {field.Component.ToString(CultureInfo.InvariantCulture)} must be exactly 0 or 1, got {Fp32.Format(value)}"
            : null;

    // The parts of a program without its stages, and the checks of their sizes.
    private sealed record ProgramShape(
        int Positions, ImmutableArray<LoopBuffer> Buffers, ImmutableArray<LoopRegister> Registers, ImmutableArray<LoopField> Fields,
        ImmutableArray<LoopOutputGroup> OutputGroups, int SharedSize)
    {
        public void Check()
        {
            if (Positions < 1) throw new ArgumentException("positions must be at least 1", "positions");
            foreach (var buffer in Buffers)
            {
                ArgumentNullException.ThrowIfNull(buffer, "buffers");
                if (buffer.CellsPerDocument < 1) throw new ArgumentException($"buffer {buffer.Name} must hold at least 1 cell for each document", "buffers");
            }
            foreach (var register in Registers)
            {
                ArgumentNullException.ThrowIfNull(register, "registers");
                if (!float.IsFinite(register.Initial)) throw new ArgumentException($"register {register.Name} must have a finite initial value", "registers");
            }
            foreach (var field in Fields)
            {
                ArgumentNullException.ThrowIfNull(field, "fields");
                if (field.Width < 1) throw new ArgumentException($"field {field.Name} must have a width of at least 1", "fields");
            }
            foreach (var group in OutputGroups)
            {
                ArgumentNullException.ThrowIfNull(group, "outputGroups");
                if (group.ValuesPerDocument < 1) throw new ArgumentException($"output group {group.Name} must hold at least 1 value for each document", "outputGroups");
                if (group.Kind == OutputGroupKind.Position && group.ValuesPerDocument != Positions)
                {
                    throw new ArgumentException($"the position group {group.Name} must hold {Text(Positions)} values for each document", "outputGroups");
                }
            }
            if (SharedSize < 0) throw new ArgumentException("the shared table size must be 0 or more", "sharedSize");
        }

        public void CheckColumn(int buffer, int column, string what)
        {
            if (buffer < 0 || buffer >= Buffers.Length) throw new ArgumentException($"{what} reads buffer {Text(buffer)}, and the program has {Text(Buffers.Length)} buffers");
            var cells = Buffers[buffer].CellsPerDocument;
            if (column < 0 || column >= cells)
            {
                throw new ArgumentException($"{what} reads column {Text(column)} of buffer {Text(buffer)}, which holds {Text(cells)} cells for each document");
            }
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    // The display form of a path in a message. The return value of one leaf has the path "".
    private static string Display(string path) => path.Length == 0 ? "(return value)" : path;

    // The checks and the resolution of one stage.
    private sealed class StageResolver(ProgramShape shape, LoopStage stage)
    {
        private readonly string prefix = $"stage {stage.Name}: ";

        private bool Once => stage.Repeat == StageRepeat.Once;

        public StagePlan Resolve(bool[] registerWritten, bool[][] fieldWritten)
        {
            ArgumentNullException.ThrowIfNull(stage.Function, "stages");
            ArgumentNullException.ThrowIfNull(stage.Cells, "stages");
            ArgumentNullException.ThrowIfNull(stage.Writes, "stages");
            var function = stage.Function;
            if (function.LoopOverflowBits.Length > 0)
            {
                throw Error("a step method with a loop overflow bit is not supported; declare [Range] on the loop count");
            }
            var circuit = function.Table.Circuit;
            if (function.Inputs.Length != circuit.SourceSize)
            {
                throw Error($"the function has {Text(circuit.SourceSize)} source cells and {Text(function.Inputs.Length)} cell paths");
            }
            CheckKeys(stage.Cells.Keys, function.Inputs, "cell");
            CheckKeys(stage.Writes.Keys, function.Outputs, "output");

            var sources = new CellSource[circuit.SourceSize];
            foreach (var input in function.Inputs)
            {
                var source = Find(stage.Cells, input.Path, OffsetSource) ?? throw Error($"the cell {Display(input.Path)} has no source");
                CheckSource(input.Path, source);
                if (source is CellSource.Field read && !fieldWritten[read.Index][read.Component])
                {
                    throw Error($"the cell {Display(input.Path)} reads field {shape.Fields[read.Index].Name} component {Text(read.Component)}, which no earlier stage writes");
                }
                sources[input.Wire.Position] = source;
            }

            var writes = new StageWrite[function.Outputs.Length];
            var targets = new Dictionary<StageWrite, string>();
            for (var output = 0; output < writes.Length; output++)
            {
                var path = function.Outputs[output].Path;
                var write = Find(stage.Writes, path, OffsetWrite) ?? throw Error($"the output {Display(path)} has no write");
                CheckWrite(path, write);
                if (!targets.TryAdd(write, path)) throw Error($"the outputs {Display(targets[write])} and {Display(path)} write the same {Describe(write)}");
                writes[output] = write;
            }
            foreach (var write in writes)
            {
                if (write is StageWrite.Register register) registerWritten[register.Index] = true;
                if (write is StageWrite.Field field) fieldWritten[field.Index][field.Component] = true;
            }
            // The integer writer keeps a field of bits as one word for each position, and a stage writes the whole word.
            foreach (var group in writes.OfType<StageWrite.Field>().GroupBy(field => field.Index))
            {
                var field = shape.Fields[group.Key];
                var count = group.Select(write => write.Component).Distinct().Count();
                if (field.Bits && count < field.Width)
                {
                    throw Error($"the stage writes {Text(count)} of the {Text(field.Width)} components of the field of bits {field.Name}; a stage that writes a field of bits writes each component of it");
                }
            }
            foreach (var input in function.Inputs)
            {
                if (sources[input.Wire.Position] is CellSource.Register read && !registerWritten[read.Index])
                {
                    throw Error($"the cell {Display(input.Path)} reads register {shape.Registers[read.Index].Name}, which no stage writes before it or in it");
                }
            }

            // The program computes each distinct output wire one time.
            var roots = function.Outputs.Select(output => output.Wire).Distinct().ToList();
            var program = circuit.CompileTensorProgram(roots);
            var outputRoots = function.Outputs.Select(output => roots.IndexOf(output.Wire)).ToImmutableArray();
            return new StagePlan(stage, program, [.. sources], [.. writes], outputRoots);
        }

        private ArgumentException Error(string message) => new(prefix + message, "stages");

        // Each key must be a path of the function, or the array path of an element path.
        private void CheckKeys(IEnumerable<string> keys, ImmutableArray<NamedWire> leaves, string kind)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var leaf in leaves)
            {
                known.Add(leaf.Path);
                if (ArrayElement(leaf.Path) is { } element) known.Add(element.Array);
            }
            foreach (var key in keys)
            {
                if (!known.Contains(key)) throw Error($"the function has no {kind} {Display(key)}");
            }
        }

        // The value of the path, or of its array path moved by the element index.
        private static T? Find<T>(ImmutableDictionary<string, T> map, string path, Func<T, int, T> offset)
            where T : class
        {
            if (map.TryGetValue(path, out var exact)) return exact;
            return ArrayElement(path) is { } element && map.TryGetValue(element.Array, out var start) ? offset(start, element.Index) : null;
        }

        // "name[3]" gives ("name", 3). A path with no index at its end gives null.
        private static (string Array, int Index)? ArrayElement(string path)
        {
            if (!path.EndsWith(']')) return null;
            var open = path.LastIndexOf('[');
            if (open < 0 || !int.TryParse(path.AsSpan(open + 1, path.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index)) return null;
            return (path[..open], index);
        }

        private static CellSource OffsetSource(CellSource source, int index) => source switch
        {
            CellSource.DocumentCell cell => cell with { Column = cell.Column + index },
            CellSource.Register register => register with { Index = register.Index + index },
            CellSource.Field field => field with { Component = field.Component + index },
            CellSource.Shared shared => shared with { Offset = shared.Offset + index },
            _ => source,
        };

        private static StageWrite OffsetWrite(StageWrite write, int index) => write switch
        {
            StageWrite.Register register => register with { Index = register.Index + index },
            StageWrite.Field field => field with { Component = field.Component + index },
            StageWrite.Document document => document with { Column = document.Column + index },
            _ => write,
        };

        private string Describe(StageWrite write) => write switch
        {
            StageWrite.Register register => $"register {shape.Registers[register.Index].Name}",
            StageWrite.Field field => $"field {shape.Fields[field.Index].Name} component {Text(field.Component)}",
            StageWrite.Document document => $"column {Text(document.Column)} of group {shape.OutputGroups[document.Group].Name}",
            StageWrite.Position target => $"position group {shape.OutputGroups[target.Group].Name}",
            _ => "place",
        };

        private void CheckSource(string path, CellSource source)
        {
            var cell = $"the cell {Display(path)}";
            if (Once && source is CellSource.Item or CellSource.Field) throw Error("a stage that runs once reads no item and no field");
            switch (source)
            {
                case CellSource.Item item:
                    CheckBuffer(cell, item.Buffer);
                    var cells = shape.Buffers[item.Buffer].CellsPerDocument;
                    if (cells < shape.Positions)
                    {
                        throw Error($"{cell} reads an item of buffer {Text(item.Buffer)}, which holds {Text(cells)} cells for each document, fewer than the {Text(shape.Positions)} positions");
                    }
                    break;
                case CellSource.DocumentCell fixedCell:
                    CheckBuffer(cell, fixedCell.Buffer);
                    try
                    {
                        shape.CheckColumn(fixedCell.Buffer, fixedCell.Column, cell);
                    }
                    catch (ArgumentException error)
                    {
                        throw Error(error.Message);
                    }
                    break;
                case CellSource.Register register:
                    CheckRegister($"{cell} reads", register.Index);
                    break;
                case CellSource.Field field:
                    CheckField($"{cell} reads", field.Index, field.Component);
                    break;
                case CellSource.Shared shared:
                    if (shared.Offset < 0 || shared.Offset >= shape.SharedSize)
                    {
                        throw Error($"{cell} reads shared offset {Text(shared.Offset)}, and the shared table holds {Text(shape.SharedSize)} cells");
                    }
                    break;
                default:
                    throw Error($"{cell} has no source");
            }
        }

        private void CheckWrite(string path, StageWrite write)
        {
            var output = $"the output {Display(path)} writes";
            switch (write)
            {
                case StageWrite.Register register:
                    CheckRegister(output, register.Index);
                    break;
                case StageWrite.Field field:
                    if (Once) throw Error("a stage that runs once writes no field and no position output");
                    CheckField(output, field.Index, field.Component);
                    break;
                case StageWrite.Document document:
                    if (!Once) throw Error("only a stage that runs once writes a document output");
                    CheckGroup(output, document.Group, OutputGroupKind.Document);
                    var group = shape.OutputGroups[document.Group];
                    if (document.Column < 0 || document.Column >= group.ValuesPerDocument)
                    {
                        throw Error($"{output} column {Text(document.Column)} of group {group.Name}, which holds {Text(group.ValuesPerDocument)} values for each document");
                    }
                    break;
                case StageWrite.Position target:
                    if (Once) throw Error("a stage that runs once writes no field and no position output");
                    CheckGroup(output, target.Group, OutputGroupKind.Position);
                    break;
                default:
                    throw Error($"the output {Display(path)} has no write");
            }
        }

        private void CheckBuffer(string cell, int buffer)
        {
            if (buffer < 0 || buffer >= shape.Buffers.Length) throw Error($"{cell} reads buffer {Text(buffer)}, and the program has {Text(shape.Buffers.Length)} buffers");
        }

        private void CheckRegister(string what, int index)
        {
            if (index < 0 || index >= shape.Registers.Length) throw Error($"{what} register {Text(index)}, and the program has {Text(shape.Registers.Length)} registers");
        }

        private void CheckField(string what, int index, int component)
        {
            if (index < 0 || index >= shape.Fields.Length) throw Error($"{what} field {Text(index)} component {Text(component)}, and the program has {Text(shape.Fields.Length)} fields");
            var field = shape.Fields[index];
            if (component < 0 || component >= field.Width)
            {
                throw Error($"{what} field {Text(index)} component {Text(component)}, and the field {field.Name} has the width {Text(field.Width)}");
            }
        }

        private void CheckGroup(string what, int group, OutputGroupKind kind)
        {
            if (group < 0 || group >= shape.OutputGroups.Length) throw Error($"{what} group {Text(group)}, and the program has {Text(shape.OutputGroups.Length)} output groups");
            if (shape.OutputGroups[group].Kind != kind)
            {
                var (wanted, actual) = kind == OutputGroupKind.Document ? ("document", "position") : ("position", "document");
                throw Error($"{what} group {Text(group)} as a {wanted} output, and that group is a {actual} group");
            }
        }
    }
}
