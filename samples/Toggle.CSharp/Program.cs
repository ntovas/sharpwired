// Handwired source: template/main.ts, which mounts the toggle on the page of template/index.html.
// This program opens the toggle in a Raylib window. The option --frames N closes the window after
// N frames, and --screenshot path saves the last frame. With no option, the person closes the window.

using Sharpwired.Raylib;
using Toggle.Builder;

WindowRunOptions options;
try
{
    options = WindowRunOptions.Parse(args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine($"{error.Message}. Usage: Toggle.CSharp [--frames N] [--screenshot path]");
    return 2;
}
RaylibWindow.Run(new ToggleWindowApp(new ToggleMachine()), new StopwatchFrameClock(), options);
return 0;
