// Handwired source: tests/json-search/demo.test.ts ("the page's worker": a search on the CPU answers in pieces, a
// newer search replaces the one in progress, and the worker answers with the kinds and the marks of one document and
// with the RegExp time). The search service of the window on the CPU: each answer carries the ID of its request, a
// newer request cancels the older one, each exception becomes a Failed answer, and Dispose stops the workers.

using System.Collections.Immutable;
using System.Diagnostics;
using Sharpwired.Gpu;

namespace JsonSearch.Tests;

public sealed class SearchServiceTests
{
    private static readonly string[] Texts = [.. Documents.Generate(300, Documents.DefaultSeed, 0).Select(document => document.Text)];

    [Fact]
    public void A_search_a_document_and_a_dotnet_time_give_their_answers_with_their_ids()
    {
        using var cpu = new CpuLoopBackend();
        using var service = SearchService.Create(Texts, null, cpu);
        Assert.False(service.HasGpu);
        Assert.Equal(300, service.DocumentCount);
        using var reference = Searcher.Create(Texts, cpu);
        var expected = Assert.IsType<SearchResult.Searched>(reference.Search(new SearchRequest("[a-z]+Id", SearchScope.Keys, 41, 1)));
        Assert.True(expected.Matches > 0);

        service.Search(1, "[a-z]+Id", SearchScope.Keys, SearchBackend.Cpu);
        var found = WaitFor<SearchAnswer.Found>(service, answer => answer.Id == 1 && answer.Result.DocumentsDone == 300);
        Assert.Equal(expected.MatchCounts.ToArray(), found.Result.MatchCounts.ToArray());

        service.ShowDocument(2, 41, "[a-z]+Id", SearchScope.Keys);
        var document = WaitFor<SearchAnswer.Document>(service, answer => answer.Id == 2);
        Assert.Equal(41, document.Index);
        Assert.Equal(reference.Kinds(41, 1).ToArray(), document.Kinds.ToArray());
        Assert.Equal(expected.WindowMarks.ToArray(), document.Marks.ToArray());

        service.ShowDocument(3, 41, null, SearchScope.Keys);
        var plain = WaitFor<SearchAnswer.Document>(service, answer => answer.Id == 3);
        Assert.Equal(reference.Kinds(41, 1).ToArray(), plain.Kinds.ToArray());
        Assert.True(plain.Marks.IsDefault);

        service.TimeDotNet(4, "[a-z]+Id", SearchScope.Keys);
        var time = WaitFor<SearchAnswer.DotNetTime>(service, answer => answer.Id == 4);
        Assert.False(time.Time.Stopped);
        Assert.True(time.Time.Ms >= 0);
    }

    [Fact]
    public void A_newer_search_replaces_the_older_one_and_each_answer_carries_the_id_of_its_own_search()
    {
        using var cpu = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        using var service = SearchService.Create(Texts, null, cpu, documentsPerSlice: 50);
        using var reference = Searcher.Create(Texts, cpu);
        var first = Assert.IsType<SearchResult.Searched>(reference.Search(new SearchRequest("[a-z]+", SearchScope.All, 0, 0)));
        var second = Assert.IsType<SearchResult.Searched>(reference.Search(new SearchRequest("[0-9]+", SearchScope.All, 0, 0)));

        service.Search(1, "[a-z]+", SearchScope.All, SearchBackend.Cpu);
        service.Search(2, "[0-9]+", SearchScope.All, SearchBackend.Cpu);
        var answers = new List<SearchAnswer.Found>();
        var last = WaitFor<SearchAnswer.Found>(service, answer => answer.Id == 2 && answer.Result.DocumentsDone == 300, answers);
        Assert.Equal(second.MatchCounts.ToArray(), last.Result.MatchCounts.ToArray());
        // A progress answer holds the counts of its own pattern for the documents that it reached.
        foreach (var answer in answers)
        {
            var expected = answer.Id == 1 ? first : second;
            Assert.Equal(expected.MatchCounts.Take(answer.Result.DocumentsDone), answer.Result.MatchCounts.Take(answer.Result.DocumentsDone));
        }
        Assert.Contains(answers, answer => answer.Id == 2 && answer.Result.DocumentsDone < 300);
    }

    [Fact]
    public void Each_error_of_a_request_becomes_a_failed_answer_with_a_sentence()
    {
        using var cpu = new CpuLoopBackend();
        using var service = SearchService.Create(Texts, null, cpu);
        service.Search(1, "(", SearchScope.All, SearchBackend.Cpu);
        Assert.Equal("Character 1 of the pattern: this group is never closed.", WaitFor<SearchAnswer.Failed>(service, answer => answer.Id == 1).Message);
        service.Search(2, new string('a', 33), SearchScope.All, SearchBackend.Cpu);
        Assert.Equal("This pattern needs 34 states, and the largest search program holds 32.", WaitFor<SearchAnswer.Failed>(service, answer => answer.Id == 2).Message);
        service.ShowDocument(3, 300, null, SearchScope.All);
        Assert.Contains("300", WaitFor<SearchAnswer.Failed>(service, answer => answer.Id == 3).Message, StringComparison.Ordinal);
        service.TimeDotNet(4, "a\\\ud800", SearchScope.All);
        Assert.StartsWith("Character 2 of the pattern: the escape", WaitFor<SearchAnswer.Failed>(service, answer => answer.Id == 4).Message, StringComparison.Ordinal);
        // A GPU search with no GPU is an error of the request.
        service.Search(5, "a", SearchScope.All, SearchBackend.Gpu);
        Assert.Equal("The search stopped with an error: this window has no GPU.", WaitFor<SearchAnswer.Failed>(service, answer => answer.Id == 5).Message);
    }

    [Fact]
    public void Dispose_cancels_the_search_in_progress_and_stops_the_workers()
    {
        using var cpu = new CpuLoopBackend(new CpuLoopOptions(BlockSize: 16));
        var service = SearchService.Create(Texts, null, cpu, documentsPerSlice: 10);
        service.Search(1, "([a-z]+)+x", SearchScope.All, SearchBackend.Cpu);
        var watch = Stopwatch.StartNew();
        service.Dispose();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
        service.Dispose();
        Assert.Throws<ObjectDisposedException>(() => service.Search(2, "a", SearchScope.All, SearchBackend.Cpu));
        Assert.Throws<ObjectDisposedException>(() => service.Poll());
    }

    [Fact]
    public void The_latest_job_worker_runs_only_the_newest_job_and_cancels_the_job_in_progress()
    {
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var ran = new List<int>();
        var cancelled = new List<int>();
        using (var worker = new LatestJobWorker<int>("test", (job, cancel) =>
        {
            lock (ran) ran.Add(job);
            if (job == 1)
            {
                started.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                if (cancel.IsCancellationRequested) lock (cancelled) cancelled.Add(job);
            }
        }))
        {
            worker.Post(1);
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
            worker.Post(2);
            worker.Post(3);
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => { lock (ran) return ran.Contains(3); }, TimeSpan.FromSeconds(30)));
        }
        Assert.Equal([1, 3], ran);
        Assert.Equal([1], cancelled);
    }

    // Poll until an answer of the type passes the test. The other answers of the type go to the list.
    private static T WaitFor<T>(SearchService service, Func<T, bool> test, List<T>? seen = null)
        where T : SearchAnswer
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            foreach (var answer in service.Poll())
            {
                if (answer is not T typed) continue;
                seen?.Add(typed);
                if (test(typed)) return typed;
            }
            Thread.Sleep(5);
        }
        throw new TimeoutException($"no {typeof(T).Name} answer passed the test in 60 s");
    }
}
