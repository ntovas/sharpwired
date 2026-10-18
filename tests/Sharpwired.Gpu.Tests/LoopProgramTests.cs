// Handwired source: src/json-search/loop-program.ts (createStage and its invariants). Task 2 of the phase 6
// plan: LoopProgram.Create refuses each bad program of its XML comment with ArgumentException. A good
// program gives its parts, its compiled stage programs and the cell map of each stage.

using System.Collections.Immutable;
using static Sharpwired.Gpu.Tests.LoopInputPrograms;

namespace Sharpwired.Gpu.Tests;

public sealed class LoopProgramTests
{
    private const int Positions = 16;

    public static TheoryData<string, string> Refusals => new()
    {
        { "positions", "positions must be at least 1" },
        { "buffer cells", "buffer documents must hold at least 1 cell for each document" },
        { "register initial", "register total must have a finite initial value" },
        { "field width", "field flags must have a width of at least 1" },
        { "group values", "output group total must hold at least 1 value for each document" },
        { "position group size", "the position group leaders must hold 16 values for each document" },
        { "shared size", "the shared table size must be 0 or more" },
        { "no stage", "a loop program needs at least one stage" },
        { "stage names", "two stages have the name Sum" },
        { "overflow bit", "stage Halve: a step method with a loop overflow bit is not supported" },
        { "unknown cell", "stage Sum: the function has no cell totl" },
        { "unknown output", "stage Sum: the function has no output item9" },
        { "cell without source", "stage Sum: the cell item has no source" },
        { "output without write", "stage Backward: the output item2 has no write" },
        { "item in once", "stage Total: a stage that runs once reads no item and no field" },
        { "field in once", "stage Total: a stage that runs once reads no item and no field" },
        { "item buffer", "stage Sum: the cell item reads buffer 1, and the program has 1 buffers" },
        { "item buffer size", "stage Sum: the cell item reads an item of buffer 0, which holds 8 cells for each document, fewer than the 16 positions" },
        { "document column", "stage End: the cell length reads column 17 of buffer 0, which holds 17 cells for each document" },
        { "register index", "stage Sum: the cell total reads register 3, and the program has 1 registers" },
        { "field index", "stage Forward: the cell largest reads field 2 component 0, and the program has 2 fields" },
        { "field component", "stage Forward: the cell flags[1] reads field 1 component 2, and the field flags has the width 2" },
        { "shared offset", "stage Step: the cell table[5] reads shared offset 9, and the shared table holds 9 cells" },
        { "register not written", "stage Total: the cell total reads register total, which no stage writes before it or in it" },
        { "field not written", "stage Forward: the cell largest reads field largest component 0, which no earlier stage writes" },
        { "write register index", "stage Sum: the output (return value) writes register 1, and the program has 1 registers" },
        { "write field component", "stage Backward: the output item3[1] writes field 1 component 2, and the field flags has the width 2" },
        { "field write in once", "stage Total: a stage that runs once writes no field and no position output" },
        { "position write in once", "stage Total: a stage that runs once writes no field and no position output" },
        { "document write in loop", "stage Sum: only a stage that runs once writes a document output" },
        { "document group kind", "stage Total: the output (return value) writes group 1 as a document output, and that group is a position group" },
        { "position group kind", "stage Forward: the output item2 writes group 0 as a position output, and that group is a document group" },
        { "group index", "stage Total: the output (return value) writes group 3, and the program has 3 output groups" },
        { "document column write", "stage Total: the output (return value) writes column 1 of group count, which holds 1 values for each document" },
        { "two writes", "stage Backward: the outputs item1 and item2 write the same register largest" },
        { "length cell", "the length cell reads column 17 of buffer 0, which holds 17 cells for each document" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Create_refuses_a_bad_program(string label, string message)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => Bad(label));
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void Create_keeps_the_parts_and_compiles_each_stage_for_its_outputs()
    {
        var program = Leaders(Positions);
        Assert.Equal(Positions, program.Positions);
        Assert.Equal(["documents"], program.Buffers.Select(buffer => buffer.Name));
        Assert.Equal(["largest", "count"], program.Registers.Select(register => register.Name));
        Assert.Equal(["largest", "flags"], program.Fields.Select(field => field.Name));
        Assert.Equal(["count", "leaders", "weighted"], program.OutputGroups.Select(group => group.Name));
        Assert.Equal(["Backward", "Forward", "Total"], program.Stages.Select(stage => stage.Name));
        Assert.Equal(0, program.SharedSize);
        Assert.Equal(new LengthCell(0, Positions), program.LengthCell);

        // The Backward step writes one wire as the register and as the field, so its program has two roots
        // for four outputs: the largest item and the two flags.
        var backward = program.Plans[0];
        Assert.Equal(4, backward.Writes.Length);
        Assert.Equal(3, backward.Program.Roots.Count);
        Assert.Equal(backward.OutputRoots[0], backward.OutputRoots[1]);

        // An array path maps its elements to consecutive field components.
        var forward = program.Plans[1];
        var flags = program.Stages[1].Function.Inputs.Select(input => (input.Path, cell: input.Wire.Position)).Where(pair => pair.Path.StartsWith("flags", StringComparison.Ordinal)).ToArray();
        Assert.Equal([new CellSource.Field(1, 0), new CellSource.Field(1, 1)], flags.Select(pair => forward.Sources[pair.cell]));
    }

    [Fact]
    public void An_array_path_maps_to_consecutive_shared_offsets_and_a_full_element_path_wins()
    {
        var program = Lookup(Positions);
        var step = program.Plans[0];
        var inputs = program.Stages[0].Function.Inputs;
        for (var element = 0; element < LoopInputs.TableLength; element++)
        {
            var cell = inputs.Single(input => input.Path == $"table[{element}]").Wire.Position;
            Assert.Equal(new CellSource.Shared(1 + element), step.Sources[cell]);
        }

        var stage = LookupStage();
        var withElement = stage with { Cells = stage.Cells.Add("table[2]", new CellSource.Shared(0)) };
        var changed = LoopProgram.Create(Positions, [Documents(Positions)], [new LoopRegister("above", 0)], [],
            [new LoopOutputGroup("kinds", OutputGroupKind.Position, Positions, 9)], [withElement], 9);
        var tableTwo = inputs.Single(input => input.Path == "table[2]").Wire.Position;
        var tableThree = inputs.Single(input => input.Path == "table[3]").Wire.Position;
        Assert.Equal(new CellSource.Shared(0), changed.Plans[0].Sources[tableTwo]);
        Assert.Equal(new CellSource.Shared(4), changed.Plans[0].Sources[tableThree]);
    }

    private static object Bad(string label)
    {
        var documents = Documents(Positions);
        LoopRegister[] total = [new LoopRegister("total", 0)];
        LoopOutputGroup[] totalGroup = [new LoopOutputGroup("total", OutputGroupKind.Document, 1, 0)];
        LoopProgram SumWith(params LoopStage[] stages) => LoopProgram.Create(Positions, [documents], total, [], totalGroup, stages, 0);
        LoopProgram LeadersWith(params LoopStage[] stages) =>
            LoopProgram.Create(Positions, [documents], LeadersRegisters(), LeadersFields(), LeadersGroups(Positions), stages, 0);
        LoopStage Cells(LoopStage stage, string path, CellSource source) => stage with { Cells = stage.Cells.SetItem(path, source) };
        LoopStage Writes(LoopStage stage, string path, StageWrite write) => stage with { Writes = stage.Writes.SetItem(path, write) };
        var once = SumTotalStage();
        return label switch
        {
            "positions" => LoopProgram.Create(0, [documents], total, [], totalGroup, [SumStage()], 0),
            "buffer cells" => LoopProgram.Create(Positions, [new LoopBuffer("documents", 0)], total, [], totalGroup, [SumStage()], 0),
            "register initial" => LoopProgram.Create(Positions, [documents], [new LoopRegister("total", float.NaN)], [], totalGroup, [SumStage()], 0),
            "field width" => LoopProgram.Create(Positions, [documents], LeadersRegisters(), [new LoopField("largest", 1, false), new LoopField("flags", 0, true)],
                LeadersGroups(Positions), [LeadersBackwardStage()], 0),
            "group values" => LoopProgram.Create(Positions, [documents], total, [], [new LoopOutputGroup("total", OutputGroupKind.Document, 0, 0)], [SumStage()], 0),
            "position group size" => LoopProgram.Create(Positions, [documents], LeadersRegisters(), LeadersFields(),
                [LeadersGroups(Positions)[0], new LoopOutputGroup("leaders", OutputGroupKind.Position, 8, 0), LeadersGroups(Positions)[2]], [LeadersBackwardStage()], 0),
            "shared size" => LoopProgram.Create(Positions, [documents], total, [], totalGroup, [SumStage()], -1),
            "no stage" => SumWith(),
            "stage names" => SumWith(SumStage(), SumStage()),
            "overflow bit" => SumWith(Stage("Halve", StageRepeat.Once, nameof(LoopInputs.Halving), [("x", new CellSource.Register(0))], [("", new StageWrite.Document(0, 0))])),
            "unknown cell" => SumWith(Cells(SumStage(), "totl", new CellSource.Register(0))),
            "unknown output" => SumWith(Writes(SumStage(), "item9", new StageWrite.Register(0))),
            "cell without source" => SumWith(SumStage() with { Cells = SumStage().Cells.Remove("item") }),
            "output without write" => LeadersWith(LeadersBackwardStage() with { Writes = LeadersBackwardStage().Writes.Remove("item2") }),
            "item in once" => SumWith(SumStage(), Cells(once, "total", new CellSource.Item(0))),
            "field in once" => SumWith(SumStage(), Cells(once, "total", new CellSource.Field(0, 0))),
            "item buffer" => SumWith(Cells(SumStage(), "item", new CellSource.Item(1))),
            "item buffer size" => LoopProgram.Create(Positions, [new LoopBuffer("documents", 8)], total, [], totalGroup, [SumStage()], 0),
            "document column" => LoopProgram.Create(Positions, [documents], [new LoopRegister("above", 0)], [],
                [new LoopOutputGroup("kinds", OutputGroupKind.Position, Positions, 9), new LoopOutputGroup("summary", OutputGroupKind.Document, 2, 0)],
                [LookupStage(), LookupEndStage(Positions + 1)], 9),
            "register index" => SumWith(Cells(SumStage(), "total", new CellSource.Register(3))),
            "field index" => LeadersWith(LeadersBackwardStage(), Cells(LeadersForwardStage(), "largest", new CellSource.Field(2, 0))),
            "field component" => LeadersWith(LeadersBackwardStage(), Cells(LeadersForwardStage(), "flags", new CellSource.Field(1, 1))),
            "shared offset" => LoopProgram.Create(Positions, [documents], [new LoopRegister("above", 0)], [],
                [new LoopOutputGroup("kinds", OutputGroupKind.Position, Positions, 9)], [Cells(LookupStage(), "table", new CellSource.Shared(4))], 9),
            "register not written" => SumWith(once),
            "field not written" => LeadersWith(LeadersForwardStage(), LeadersBackwardStage()),
            "write register index" => SumWith(Writes(SumStage(), "", new StageWrite.Register(1))),
            "write field component" => LeadersWith(Writes(LeadersBackwardStage(), "item3", new StageWrite.Field(1, 1))),
            "field write in once" => SumWith(SumStage(), Writes(once, "", new StageWrite.Field(0, 0))),
            "position write in once" => SumWith(SumStage(), Writes(once, "", new StageWrite.Position(0))),
            "document write in loop" => SumWith(Writes(SumStage(), "", new StageWrite.Document(0, 0))),
            "document group kind" => LeadersWith(LeadersBackwardStage(), LeadersForwardStage(),
                Writes(LeadersTotalStage(), "", new StageWrite.Document(1, 0))),
            "position group kind" => LeadersWith(LeadersBackwardStage(), Writes(LeadersForwardStage(), "item2", new StageWrite.Position(0))),
            "group index" => LeadersWith(LeadersBackwardStage(), LeadersForwardStage(), Writes(LeadersTotalStage(), "", new StageWrite.Document(3, 0))),
            "document column write" => LeadersWith(LeadersBackwardStage(), LeadersForwardStage(), Writes(LeadersTotalStage(), "", new StageWrite.Document(0, 1))),
            "two writes" => LeadersWith(Writes(LeadersBackwardStage(), "item2", new StageWrite.Register(0))),
            "length cell" => LoopProgram.Create(Positions, [documents], total, [], totalGroup, [SumStage(), once], 0, new LengthCell(0, Positions + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, "no such case"),
        };
    }
}
