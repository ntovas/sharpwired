// Handwired source: src/json-search/demo-worker.ts (a worker that runs one request at a time, where a newer search
// replaces the search in progress) and demo.ts (the page ends the RegExp worker to stop a timing that it left). A
// .NET thread that runs only the newest job: a new job replaces the job that waits, and cancels the token of the job
// in progress.

namespace JsonSearch;

/// <summary>A thread that runs the newest posted job. The job delegate gets a token that the next post cancels.</summary>
internal sealed class LatestJobWorker<TJob> : IDisposable
{
    private readonly Action<TJob, CancellationToken> run;
    private readonly Thread thread;
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0);
    private bool hasJob;
    private TJob? pending;
    private CancellationTokenSource? current;
    private bool stopping;

    /// <summary>Start the thread. The delegate runs on it, one job at a time, and must not throw.</summary>
    public LatestJobWorker(string name, Action<TJob, CancellationToken> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        this.run = run;
        thread = new Thread(Loop) { IsBackground = true, Name = name };
        thread.Start();
    }

    /// <summary>Replace the job that waits with this job, and cancel the job in progress.</summary>
    public void Post(TJob job)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            pending = job;
            hasJob = true;
            current?.Cancel();
        }
        signal.Release();
    }

    /// <summary>Drop the job that waits, and cancel the job in progress.</summary>
    public void Cancel()
    {
        lock (gate)
        {
            hasJob = false;
            pending = default;
            current?.Cancel();
        }
    }

    /// <summary>Cancel the job in progress, and wait for the thread to stop.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (stopping) return;
            stopping = true;
            hasJob = false;
            pending = default;
            current?.Cancel();
        }
        signal.Release();
        thread.Join();
        signal.Dispose();
    }

    private void Loop()
    {
        while (true)
        {
            signal.Wait();
            TJob job;
            CancellationTokenSource cancel;
            lock (gate)
            {
                if (stopping) return;
                if (!hasJob) continue;
                job = pending!;
                hasJob = false;
                pending = default;
                cancel = new CancellationTokenSource();
                current = cancel;
            }
            try
            {
                run(job, cancel.Token);
            }
            finally
            {
                lock (gate) current = null;
                cancel.Dispose();
            }
        }
    }
}
