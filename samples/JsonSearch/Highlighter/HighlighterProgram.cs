// Handwired source: src/json-search/json-step.ts (the LoopProgram of buildJsonLoop). The highlighter as a loop
// program over the 512 positions of a document: buffer 0 holds the document cells (512 code points and the length).
// The registers are the lexer state, the depth, the container code, the grammar state, the error count and the
// first error kind. Each register has the initial value 0. The stage Step runs forward and writes the display kind of each
// position to the position group displayKinds (fill: PastEnd). The stage End runs one time, reads the length cell
// (column 512), and writes valid, firstErrorPosition, truncated and firstErrorKind to the document group summary.
// The program has no length cell, as in Handwired. The step keeps its registers after the text only when the
// error count does not grow. So the loops must run each position (see HighlighterStep.cs).

using System.Collections.Immutable;
using Sharpwired.Gpu;
using Sharpwired.IL;

namespace JsonSearch;

/// <summary>The loop program of the highlighter.</summary>
public static class HighlighterProgram
{
    private static readonly string[] RegisterNames = ["lexerState", "depth", "code", "grammarState", "errorCount", "firstErrorKind"];

    /// <summary>The names of the 4 summary values, in column order.</summary>
    public static readonly ImmutableArray<string> SummaryNames = ["valid", "firstErrorPosition", "truncated", "firstErrorKind"];

    /// <summary>Translate the step methods and compose them into the loop program.</summary>
    public static LoopProgram Build()
    {
        var step = HandwiredTranslator.TranslateFunction(typeof(HighlighterStep).GetMethod(nameof(HighlighterStep.Step))!);
        var end = HandwiredTranslator.TranslateFunction(typeof(HighlighterStep).GetMethod(nameof(HighlighterStep.End))!);
        var stateCells = RegisterNames.Select((name, index) => KeyValuePair.Create<string, CellSource>(name, new CellSource.Register(index))).ToArray();

        var stepStage = new LoopStage(
            "Step",
            StageRepeat.Forward,
            step,
            ImmutableDictionary.CreateRange(stateCells.Append(KeyValuePair.Create<string, CellSource>("codePoint", new CellSource.Item(0)))),
            ImmutableDictionary.CreateRange(RegisterNames
                .Select((name, index) => KeyValuePair.Create<string, StageWrite>($"item1.{name}", new StageWrite.Register(index)))
                .Append(KeyValuePair.Create<string, StageWrite>("item2", new StageWrite.Position(1)))));
        var endStage = new LoopStage(
            "End",
            StageRepeat.Once,
            end,
            ImmutableDictionary.CreateRange(stateCells.Append(KeyValuePair.Create<string, CellSource>("textLength", new CellSource.DocumentCell(0, Kinds.Capacity)))),
            ImmutableDictionary.CreateRange(SummaryNames.Select((_, column) => KeyValuePair.Create<string, StageWrite>($"item{column + 1}", new StageWrite.Document(0, column)))));

        return LoopProgram.Create(
            Kinds.Capacity,
            [new LoopBuffer("documents", DocumentCells.CellsPerDocument)],
            [.. RegisterNames.Select(name => new LoopRegister(name, 0))],
            [],
            [
                new LoopOutputGroup("summary", OutputGroupKind.Document, SummaryNames.Length, 0),
                new LoopOutputGroup("displayKinds", OutputGroupKind.Position, Kinds.Capacity, (int)DisplayKind.PastEnd),
            ],
            [stepStage, endStage],
            0);
    }
}
