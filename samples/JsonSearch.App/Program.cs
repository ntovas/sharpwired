// No Handwired source. The executable of the JSON search sample. The options:
// - --bench: write the bench table of SearchBench (the four example patterns in each scope, the program on the GPU
//   and on the CPU, and .NET Regex) and exit with 0.
// - --documents N: the number of generated documents, 100,000 by default.
// - --cpu: no GPU. Without it, the bench opens the default adapter, and with no adapter it writes the reason to the
//   error output and runs the CPU only.
// The window mode comes in a later task. Until then a run without --bench writes the usage and exits with 2.

using JsonSearch;
using Sharpwired.Gpu;

return JsonSearchCommand.Run(args, Console.Out, Console.Error);

/// <summary>The options of the command line, and the bench mode.</summary>
internal static class JsonSearchCommand
{
    private const string Usage = "Usage: JsonSearch.App --bench [--documents N] [--cpu]";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Parse the options and run. Returns the exit code: 0, or 2 for a bad option.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var bench = false;
        var cpuOnly = false;
        var documents = 100_000;
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
                default:
                    error.WriteLine($"the option {args[index]} is not valid. {Usage}");
                    return 2;
            }
        }
        if (!bench)
        {
            error.WriteLine($"this version has only the bench mode. {Usage}");
            return 2;
        }
        RunBench(documents, cpuOnly, output, error);
        return 0;
    }

    private static void RunBench(int documentCount, bool cpuOnly, TextWriter output, TextWriter error)
    {
        var texts = Documents.Generate(documentCount).Select(document => document.Text).ToArray();
        GpuDevice? device = null;
        if (!cpuOnly)
        {
            try
            {
                device = GpuDevice.Open();
                output.WriteLine($"[bench] GPU: {device.Adapter.Name}, {device.Adapter.Backend}");
            }
            catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or ArgumentException)
            {
                error.WriteLine($"[bench] no GPU, the bench runs the CPU only: {exception.Message}");
            }
        }
        using (device)
        {
            using var gpu = device is null ? null : new GpuLoopBackend(device);
            using var cpu = new CpuLoopBackend();
            List<BenchBackend> backends = gpu is null ? [] : [new BenchBackend("GPU", gpu)];
            backends.Add(new BenchBackend("CPU", cpu));
            var rows = SearchBench.Run(texts, backends, Limit, line => output.WriteLine($"[bench] {line}"));
            output.Write(SearchBench.Format(rows, documentCount));
        }
    }
}
