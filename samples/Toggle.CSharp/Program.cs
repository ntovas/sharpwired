// Handwired source: template/main.ts, which mounts the toggle on the page of template/index.html.
// This program opens the toggle in a Raylib window. By default the window runs the plain C# toggle
// (ToggleStep.Step) after the IL translator. The option --builder runs the builder toggle of
// Toggle.Builder. The option --frames N closes the window after N frames, and --screenshot path saves
// the last frame. With no --frames option, the person closes the window.

using Sharpwired.Cells;
using Sharpwired.Raylib;
using Toggle.Builder;
using Toggle.CSharp;

WindowRunOptions options;
bool builder;
try
{
    (builder, var rest) = CSharpToggle.SplitBuilderOption(args);
    options = WindowRunOptions.Parse(rest);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine($"{error.Message}. Usage: Toggle.CSharp [--builder] [--frames N] [--screenshot path]");
    return 2;
}

ToggleMachine machine;
if (builder)
{
    machine = new ToggleMachine();
}
else
{
    var built = CSharpToggle.Build();
    machine = new ToggleMachine(built, ProgramEvaluator.Create(built.Table, built.Output));
}

RaylibWindow.Run(new ToggleWindowApp(machine), new StopwatchFrameClock(), options);
return 0;
