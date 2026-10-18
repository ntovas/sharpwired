// No Handwired source. The executable of the JSON search sample. The options:
// - --bench: write the bench table of SearchBench (the four example patterns in each scope, the program on the GPU
//   and on the CPU, and .NET Regex) and exit with 0.
// - --documents N: the number of generated documents, 100,000 by default.
// - --cpu: no GPU. Without it, the app opens the default adapter, and with no adapter it writes the reason to the
//   error output and runs the CPU only.
// - --frames N and --screenshot path (the window only): close the window after N frames, and save the last frame.
// Without --bench, the app opens the JSON search window. The window documents are the generated documents with no
// defect, as the Handwired page loads them (demo-text.ts documentText). The searcher runs the highlighter over each
// document before the window opens.

using System.Diagnostics;
using JsonSearch;
using Sharpwired.Gpu;
using Sharpwired.Raylib;

return JsonSearchCommand.Run(args, Console.Out, Console.Error);

/// <summary>The options of the command line, the bench mode and the window mode.</summary>
internal static class JsonSearchCommand
{
    private const string Usage = "Usage: JsonSearch.App [--bench] [--documents N] [--cpu] [--frames N] [--screenshot path]";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Parse the options and run. Returns the exit code: 0, or 2 for a bad option.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var options = Parse(args, error);
        if (options is null) return 2;
        if (options.Bench)
        {
            RunBench(options.Documents, options.CpuOnly, output, error);
        }
        else
        {
            RunWindow(options, error);
        }
        return 0;
    }

    /// <summary>The options, or null after a message on the error output for a bad option.</summary>
    public static CommandOptions? Parse(string[] args, TextWriter error)
    {
        var bench = false;
        var cpuOnly = false;
        var documents = 100_000;
        var windowArgs = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--bench":
                    bench = true;
                    break;
                case "--cpu":
                    cpuOnly = true;
                    break;
                case "--documents" when index + 1 < args.Length && int.TryParse(args[index + 1], out var count) && count >= 1:
                    documents = count;
                    index++;
                    break;
                case "--frames" or "--screenshot":
                    windowArgs.Add(args[index]);
                    if (index + 1 < args.Length) windowArgs.Add(args[++index]);
                    break;
                default:
                    error.WriteLine($"the option {args[index]} is not valid. {Usage}");
                    return null;
            }
        }
        if (bench && windowArgs.Count > 0)
        {
            error.WriteLine($"--frames and --screenshot are options of the window, not of the bench. {Usage}");
            return null;
        }
        try
        {
            return new CommandOptions(bench, cpuOnly, documents, WindowRunOptions.Parse(windowArgs));
        }
        catch (ArgumentException exception)
        {
            error.WriteLine($"{exception.Message}. {Usage}");
            return null;
        }
    }

    private static void RunBench(int documentCount, bool cpuOnly, TextWriter output, TextWriter error)
    {
        var texts = Documents.Generate(documentCount).Select(document => document.Text).ToArray();
        using var device = OpenDevice(cpuOnly, "bench", output, error);
        using var gpu = device is null ? null : new GpuLoopBackend(device);
        using var cpu = new CpuLoopBackend();
        List<BenchBackend> backends = gpu is null ? [] : [new BenchBackend("GPU", gpu)];
        backends.Add(new BenchBackend("CPU", cpu));
        var rows = SearchBench.Run(texts, backends, Limit, line => output.WriteLine($"[bench] {line}"));
        output.Write(SearchBench.Format(rows, documentCount));
    }

    private static void RunWindow(CommandOptions options, TextWriter error)
    {
        var watch = Stopwatch.StartNew();
        var texts = Documents.Generate(options.Documents, Documents.DefaultSeed, 0).Select(document => document.Text).ToArray();
        using var device = OpenDevice(options.CpuOnly, "window", error, error);
        using var gpu = device is null ? null : new GpuLoopBackend(device);
        using var cpu = new CpuLoopBackend();
        using var service = SearchService.Create(texts, gpu, cpu);
        error.WriteLine($"[window] {texts.Length:N0} documents ready in {watch.Elapsed.TotalMilliseconds:F0} ms");
        RaylibWindow.Run(new JsonSearchApp(service, texts), new StopwatchFrameClock(), options.Window);
    }

    // The default adapter, or null with --cpu or with no adapter.
    private static GpuDevice? OpenDevice(bool cpuOnly, string mode, TextWriter output, TextWriter error)
    {
        if (cpuOnly) return null;
        try
        {
            var device = GpuDevice.Open();
            output.WriteLine($"[{mode}] GPU: {device.Adapter.Name}, {device.Adapter.Backend}");
            return device;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or ArgumentException)
        {
            error.WriteLine($"[{mode}] no GPU, the programs run on the CPU only: {exception.Message}");
            return null;
        }
    }
}

/// <summary>The options of the command line. Window holds --frames and --screenshot.</summary>
internal sealed record CommandOptions(bool Bench, bool CpuOnly, int Documents, WindowRunOptions Window);
