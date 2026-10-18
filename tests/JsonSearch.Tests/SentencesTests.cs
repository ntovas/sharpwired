// Handwired source: tests/json-search/demo.test.ts ("the page's sentences"). The numbers, the times, the line of
// numbers and the sentences for a pattern that the window does not search. The window names .NET Regex where the
// page names the native RegExp of the browser.

namespace JsonSearch.Tests;

public sealed class SentencesTests
{
    [Fact]
    public void Numbers_have_separators_and_times_have_a_unit_that_fits_their_size()
    {
        Assert.Equal("1,203,712", Sentences.Count(1_203_712));
        Assert.Equal(
            ["under 0.1 ms", "0.8 ms", "8.1 ms", "18 ms", "65 ms", "994 ms", "1.0 s", "2.2 s", "12 s"],
            new[] { 0.02, 0.84, 8.14, 17.6, 64.9, 994, 996, 2154, 12_300 }.Select(Sentences.Duration));
    }

    [Fact]
    public void The_line_gives_the_documents_with_a_match_the_program_time_and_the_dotnet_time()
    {
        var measured = new DotNetTime(23.4, false);
        Assert.Equal("10,541 of 100,000 documents match. This program: 23 ms on the GPU. .NET Regex: 23 ms.", Sentences.ResultLine(10_541, 100_000, 22.6, 100_000, SearchBackend.Gpu, measured));
        Assert.Equal("10,541 of 100,000 documents match. This program: 23 ms on the GPU.", Sentences.ResultLine(10_541, 100_000, 22.6, 100_000, SearchBackend.Gpu, null));
        Assert.Equal("1 of 20,000 documents matches. This program: 8.1 ms on the GPU. .NET Regex: 2.1 s.", Sentences.ResultLine(1, 20_000, 8.14, 20_000, SearchBackend.Gpu, new DotNetTime(2143, false)));
        Assert.Equal("None of the 65,000 documents matches. This program: 871 ms on the GPU. .NET Regex: 29 ms.", Sentences.ResultLine(0, 65_000, 871, 65_000, SearchBackend.Gpu, new DotNetTime(28.9, false)));
        Assert.Equal("10,541 of 100,000 documents match. This program: 16 s on the CPU. .NET Regex: 23 ms.", Sentences.ResultLine(10_541, 100_000, 15_820, 100_000, SearchBackend.Cpu, measured));
        Assert.Equal("1,304 of the first 12,400 documents match. This program: 3.1 s on the CPU so far. .NET Regex: 23 ms.", Sentences.ResultLine(1304, 12_400, 3120, 100_000, SearchBackend.Cpu, measured));
        Assert.Equal("0 of the first 2 documents match. This program: 0.4 ms on the CPU so far.", Sentences.ResultLine(0, 2, 0.4, 100_000, SearchBackend.Cpu, null));
        Assert.Equal("47,577 of 100,000 documents match. This program: 17 ms on the GPU. .NET Regex: more than 10 s.", Sentences.ResultLine(47_577, 100_000, 17.2, 100_000, SearchBackend.Gpu, new DotNetTime(10_000, true)));
    }

    [Fact]
    public void A_pattern_with_a_syntax_error_or_one_that_no_program_holds_gets_a_sentence_and_the_others_name_the_program()
    {
        Assert.IsType<PatternAnalysis.Empty>(Sentences.Analyze(""));
        Assert.Equal(new PatternAnalysis.Fits(8), Sentences.Analyze("error"));
        Assert.Equal(new PatternAnalysis.Fits(8), Sentences.Analyze("(the|an?) [a-z]+"));
        Assert.Equal(new PatternAnalysis.Fits(16), Sentences.Analyze(@"\d\d\d\d-\d\d-\d\d"));
        Assert.Equal(new PatternAnalysis.Fits(32), Sentences.Analyze("connection reset"));
        Assert.Equal(new PatternAnalysis.Refused("Character 1 of the pattern: this group is never closed."), Sentences.Analyze("(the"));
        Assert.Equal(new PatternAnalysis.Refused("Character 4 of the pattern: nothing to repeat."), Sentences.Analyze("ab**"));
        Assert.Equal(new PatternAnalysis.Refused("This pattern needs 33 states, and the largest search program holds 32."), Sentences.Analyze(new string('a', 32)));
        Assert.Equal(new PatternAnalysis.Refused("This pattern needs 34 states, and the largest search program holds 32."), Sentences.Analyze(new string('a', 33)));
    }

    [Fact]
    public void Each_capacity_that_a_pattern_needs_past_the_program_has_its_own_sentence()
    {
        Assert.Equal("This pattern needs 40 states, and the largest search program holds 32.", Sentences.Limit(new CapacityError("states", 40, 32, "")));
        Assert.Equal("This pattern tells 17 classes of characters apart, and the search programs hold 16.", Sentences.Limit(new CapacityError("classes", 17, 16, "")));
        Assert.Equal("This pattern’s classes of characters change at 33 places, and the search programs hold 32.", Sentences.Limit(new CapacityError("boundaries", 33, 32, "")));
    }

    [Fact]
    public void The_window_has_a_sentence_for_no_pattern_a_search_in_progress_and_a_failure()
    {
        Assert.Equal("Type a pattern to search the 100,000 documents.", Sentences.Empty(100_000));
        Assert.Equal("Searching 100,000 documents…", Sentences.Searching(100_000));
        Assert.Equal("The search stopped with an error: the device was lost.", Sentences.Failure("the device was lost"));
    }
}
