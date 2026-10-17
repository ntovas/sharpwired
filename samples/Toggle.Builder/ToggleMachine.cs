// Handwired source: template/toggle.ts, lines 86-139 (ToggleEvent, ToggleOutput,
// createToggleMachine). The queue, the spring clock and the advanceFrame call are in
// Sharpwired.Raylib.HostLoop. This file holds the cell writes of the evaluate callback.

using Sharpwired.Cells;
using Sharpwired.Raylib;
using Sharpwired.Timing;

namespace Toggle.Builder;

/// <summary>Handwired ToggleEvent kind: 'down', 'up' or 'cancel'.</summary>
public enum ToggleEventKind
{
    Down,
    Up,
    Cancel,
}

/// <summary>Handwired ToggleEvent: a pointer event with its coordinates on the 1/64 px grid and its timestamp.</summary>
public readonly record struct ToggleEvent(ToggleEventKind Kind, double X, double Y, double TimeStamp) : ITimedEvent;

/// <summary>Handwired ToggleOutput: the output values of one evaluation, as double.</summary>
public readonly record struct ToggleOutput(double TrackLeft, double TrackTop, double ThumbOffset, double On, double Armed, double Active);

/// <summary>
/// Handwired createToggleMachine: the host of the toggle. Event handlers only queue events, and each
/// step plans the evaluations of the frame with AdvanceFrame, which steps the spring to the timestamp
/// of each event before the event applies. With <see cref="WriteBack.EventsAndSettleChecks"/>, the
/// frame evaluation runs on a copy of the table, so the stored state depends only on the events and
/// not on the frame rate.
/// </summary>
public sealed class ToggleMachine
{
    private readonly ProgramEvaluator<ToggleOutputWires> evaluator;
    private readonly HostLoop<ToggleEvent> loop;
    // The spring matrix of each step count that a frame can plan, 0 to MaxStepsPerFrame.
    private readonly SpringMatrix[] matrices;
    private readonly float[] copy;

    /// <summary>Build the toggle program and its evaluator, and compute the spring matrices one time.</summary>
    public ToggleMachine(WriteBack writeBack = WriteBack.EventsAndSettleChecks)
    {
        Built = ToggleProgram.Build();
        evaluator = ProgramEvaluator.Create(Built.Table, Built.Output);
        var step = SpringMatrix.SpringStepMatrix(ToggleLayout.Spring);
        matrices = [.. Enumerable.Range(0, SpringClockConfig.Default.MaxStepsPerFrame + 1).Select(steps => SpringMatrix.SpringMatrixPower(step, steps))];
        Source = [.. Built.Table.InitialSource];
        copy = new float[Source.Length];
        loop = new HostLoop<ToggleEvent>(writeBack);
    }

    /// <summary>The cells, the output wires and the finished table of the toggle program.</summary>
    public BuiltToggle Built { get; }

    /// <summary>The stored source table. Each evaluation that writes back copies its next values into it.</summary>
    public float[] Source { get; }

    /// <summary>The spring clock. The host requests frames while it runs.</summary>
    public SpringClock Clock => loop.Clock;

    /// <summary>Add an event to the queue. The next step applies it.</summary>
    public void Queue(ToggleEvent toggleEvent) => loop.Queue(toggleEvent);

    /// <summary>
    /// Run the evaluations of one frame, and return the output of the last one, which is the frame
    /// evaluation.
    /// </summary>
    public ToggleOutput Step(double viewportWidth, double viewportHeight, double frameTime)
    {
        ToggleOutput? frameOutput = null;
        var cells = Built.Cells;
        var output = Built.Output;
        loop.Step(frameTime, evaluation =>
        {
            var values = evaluation.WritesBack ? Source : copy;
            if (!evaluation.WritesBack) Source.CopyTo(copy, 0);
            var matrix = matrices[evaluation.Steps];
            // FP32 stores: each cast rounds to nearest even, as a Float32Array store does.
            for (var index = 0; index < cells.SpringMatrix.Length; index++) values[cells.SpringMatrix[index].Position] = (float)matrix[index];
            values[cells.SettleCheck.Position] = evaluation.SettleCheck ? 1f : 0f;
            values[cells.Snap.Position] = evaluation.Snap ? 1f : 0f;
            values[cells.ViewportWidth.Position] = (float)viewportWidth;
            values[cells.ViewportHeight.Position] = (float)viewportHeight;
            // The last event of each kind wins, because a later event overwrites the cells.
            foreach (var item in evaluation.Events)
            {
                if (item.Kind == ToggleEventKind.Cancel)
                {
                    values[cells.Cancel.Position] = 1f;
                }
                else
                {
                    var pointer = item.Kind == ToggleEventKind.Down ? cells.Down : cells.Up;
                    values[pointer.Active.Position] = 1f;
                    values[pointer.X.Position] = (float)item.X;
                    values[pointer.Y.Position] = (float)item.Y;
                }
            }
            var result = evaluator.Evaluate(values);
            if (evaluation.WritesBack) result.NextValues.CopyTo(Source, 0);
            // Copy the outputs now: the next evaluation overwrites the arrays of the result.
            var current = new ToggleOutput(
                result.Read(output.TrackLeft),
                result.Read(output.TrackTop),
                result.Read(output.ThumbOffset),
                result.Read(output.On),
                result.Read(output.Armed),
                result.Read(output.Active));
            frameOutput = current;
            return current.Active > 0;
        });
        return frameOutput ?? throw new InvalidOperationException("AdvanceFrame ran no evaluation");
    }
}
