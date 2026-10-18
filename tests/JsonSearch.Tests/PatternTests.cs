// Handwired source: tests/json-search/regex/regex-search.test.ts (the syntax errors, the capacity errors, the
// hand-written cases and the random patterns against both references). The pattern compiler: the language, the
// syntax errors with their code point positions and the Handwired messages, the Glushkov automaton, the class
// partition and the capacity errors. The two plain references (RegexReference) find the hand-written matches,
// and AutomatonMatches equals DotNetMatches on the seeded random patterns and texts, so .NET Regex checks the
// compiler.

using JsonSearch;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

public sealed class PatternTests(ITestOutputHelper output)
{
    private static readonly SearchCapacity Small = new(8);

    public static TheoryData<string, int, string> SyntaxErrors => new()
    {
        { "a**", 2, "nothing to repeat" },
        { "*a", 0, "nothing to repeat" },
        { "a|+", 2, "nothing to repeat" },
        { "a{2}", 1, "{ needs a backslash before it" },
        { "a}", 1, "} needs a backslash before it" },
        { "]", 0, "] needs a backslash before it" },
        { "^a", 0, "the anchors ^ and $ aren’t supported" },
        { "a$", 1, "the anchors ^ and $ aren’t supported" },
        { "(a", 0, "this group is never closed" },
        { "ab(c|d", 2, "this group is never closed" },
        { "a)", 1, "this ) closes no group" },
        { "[a", 0, "this set of characters is never closed" },
        { "x[^", 1, "this set of characters is never closed" },
        { "[z-a]", 2, "this range is out of order" },
        { "\\q", 0, "the escape \\q isn’t supported" },
        { "a\\-", 1, "the escape \\- isn’t supported" },
        { "a*?", 2, "*? is a lazy quantifier, which this language doesn’t have" },
        { "a+?", 2, "+? is a lazy quantifier, which this language doesn’t have" },
        { "(?:a)", 0, "groups such as (?: aren’t supported" },
        { "[\\d-a]", 3, "a range needs one character at each end" },
        { "[a-\\w]", 2, "a range needs one character at each end" },
        { "a\\", 1, "a pattern can’t end with a backslash" },
        { "[a\\", 2, "a pattern can’t end with a backslash" },
        // The position counts code points: the emoji is one code point and two UTF-16 units.
        { "😀😀(", 2, "this group is never closed" },
    };

    [Theory]
    [MemberData(nameof(SyntaxErrors))]
    public void Syntax_outside_the_language_throws_a_pattern_syntax_exception(string pattern, int position, string message)
    {
        var error = Assert.Throws<PatternSyntaxException>(() => PatternCompiler.Compile(pattern));
        Assert.Equal((message, position), (error.Message, error.Position));
        Assert.IsAssignableFrom<ArgumentException>(error);
        // DotNetPattern throws the same exception first.
        var translated = Assert.Throws<PatternSyntaxException>(() => DotNetPattern.Translate(pattern));
        Assert.Equal((message, position), (translated.Message, translated.Position));
    }

    // Task 5 review: a lone surrogate after a backslash threw ArgumentOutOfRangeException from the message text. It is
    // an escape that the language does not have, so it gives the syntax error at the backslash. A Fact and not a row
    // of the theory above, so that no test name holds a lone surrogate.
    [Fact]
    public void A_lone_surrogate_after_a_backslash_is_a_syntax_error_at_the_backslash()
    {
        foreach (var (pattern, position) in new[] { ("a\\\ud800", 1), ("\\\udc00b", 0), ("[x\\\udbff]", 2), ("😀\\\ud800", 1) })
        {
            var error = Assert.Throws<PatternSyntaxException>(() => PatternCompiler.Compile(pattern));
            Assert.Equal(position, error.Position);
            Assert.StartsWith("the escape \\", error.Message, StringComparison.Ordinal);
            Assert.EndsWith(" isn’t supported", error.Message, StringComparison.Ordinal);
            var translated = Assert.Throws<PatternSyntaxException>(() => DotNetPattern.Translate(pattern));
            Assert.Equal((error.Message, position), (translated.Message, translated.Position));
            Assert.IsType<PatternAnalysis.Refused>(Sentences.Analyze(pattern));
        }
    }

    [Fact]
    public void The_language_accepts_each_construct()
    {
        foreach (var pattern in new[] { "", "a", "é", "😀", ".", "\\d", "\\w", "[a-z_]", "[^0-9]", "[]", "[^]", "a?", "a*", "a+", "(ab)?", "[a]*", "a|b", "(a|)", "()", "[-a]", "[a-]", "[\\-]", "[\\]]", "\\^\\$\\\\\\.\\*\\+\\?\\(\\)\\[\\]\\{\\}\\|\\/" })
        {
            Assert.Equal(pattern, PatternCompiler.Compile(pattern).Pattern);
        }
    }

    [Fact]
    public void The_automaton_is_the_position_automaton_of_glushkov()
    {
        // (ab)*c has the states initial, a, b and c.
        var compiled = PatternCompiler.Compile("(ab)*c");
        Assert.Equal(4, compiled.StateCount);
        Assert.Equal([[1, 3], [2], [1, 3], []], compiled.Targets.Select(targets => targets.ToArray()).ToArray());
        Assert.Equal([false, false, false, true], compiled.Accepting.ToArray());
        Assert.Empty(compiled.StateSets[0]);
        Assert.Equal([new CodePointRange('a', 'a')], compiled.StateSets[1].ToArray());

        // The empty pattern has one state, which accepts, and no step.
        var empty = PatternCompiler.Compile("");
        Assert.Equal((1, true, 0, 0), (empty.StateCount, empty.Accepting[0], empty.Targets[0].Length, empty.ClassCount));

        // . excludes \n, \r, U+2028 and U+2029, and [^] is each code point.
        Assert.Equal([new(0, 9), new(11, 12), new(14, 0x2027), new(0x202a, 0x10ffff)], PatternCompiler.Compile(".").StateSets[1].ToArray());
        Assert.Equal([new CodePointRange(0, 0x10ffff)], PatternCompiler.Compile("[^]").StateSets[1].ToArray());
        Assert.Empty(PatternCompiler.Compile("[]").StateSets[1]);
        Assert.Equal([new('-', '-'), new('a', 'c')], PatternCompiler.Compile("[a-c-]").StateSets[1].ToArray());
        Assert.Equal([new('0', '9'), new('A', 'Z'), new('_', '_'), new('a', 'z')], PatternCompiler.Compile("\\w").StateSets[1].ToArray());
    }

    [Fact]
    public void The_partition_gives_one_class_to_the_code_points_that_each_step_reads_the_same_way()
    {
        // (the|an?) [a-z]+ has 7 classes: t, h, e, a, n, the space, and the other letters of a..z.
        var compiled = PatternCompiler.Compile("(the|an?) [a-z]+");
        Assert.Equal(7, compiled.ClassCount);

        var range = PatternCompiler.Compile("[a-c]");
        Assert.Equal([97, 100], range.Boundaries.ToArray());
        Assert.Equal([-1, 0, -1], range.RegionClasses.ToArray());
        Assert.Equal([[false, true]], range.ClassReads.Select(reads => reads.ToArray()).ToArray());

        // Neighbor regions have different classes, and each region class is -1 or a class.
        foreach (var pattern in new[] { "[a-c][b-d]", "a|b|c", "\\w+@\\d", "[^a]", "." })
        {
            var partition = PatternCompiler.Compile(pattern);
            Assert.Equal(partition.Boundaries.Length + 1, partition.RegionClasses.Length);
            Assert.All(partition.RegionClasses, value => Assert.InRange(value, -1, partition.ClassCount - 1));
            for (var region = 1; region < partition.RegionClasses.Length; region++) Assert.NotEqual(partition.RegionClasses[region - 1], partition.RegionClasses[region]);
        }
    }

    [Fact]
    public void Capacity_errors_name_what_the_pattern_needs()
    {
        var capacity = new SearchCapacity(32);
        Assert.Equal(
            [
                "the pattern needs 33 states, and the program holds 32",
                "the pattern needs 32 character classes, and the program holds 16",
                // a..z are neighbors, so their 26 classes need 27 boundaries, and A..F need 7.
                "the pattern needs 34 class boundaries, and the program holds 32",
            ],
            PatternCompiler.CapacityErrors(PatternCompiler.Compile("abcdefghijklmnopqrstuvwxyzABCDEF"), capacity).Select(error => error.Message));
        // One class that is 22 separate letters: 1 class, 44 boundaries.
        var spread = PatternCompiler.CapacityErrors(PatternCompiler.Compile("[acegikmoqsuwyACEGIKMOQ]"), capacity);
        Assert.Equal([new CapacityError("boundaries", 44, 32, "the pattern needs 44 class boundaries, and the program holds 32")], spread.ToArray());
        Assert.Empty(PatternCompiler.CapacityErrors(PatternCompiler.Compile("(the|an?|some|this|that|these) [a-z]+(ing|ed|s)? "), capacity));
        // 33 literal characters need 34 states.
        Assert.Equal("the pattern needs 34 states, and the program holds 32", PatternCompiler.CapacityErrors(PatternCompiler.Compile(new string('a', 33)), capacity)[0].Message);
        Assert.Equal([8, 16, 32], SearchCapacity.All.Select(each => each.States));
        Assert.All(SearchCapacity.All, each => Assert.Equal((16, 32), (each.Classes, each.Boundaries)));
    }

    [Fact]
    public void Both_references_find_the_hand_written_matches()
    {
        foreach (var (pattern, text, expected) in TestPatterns.HandWrittenCases)
        {
            var compiled = PatternCompiler.Compile(pattern);
            var codePoints = CodePoints.Of(text);
            Assert.Empty(PatternCompiler.CapacityErrors(compiled, Small));
            Assert.Equal((pattern, text, TestPatterns.Show(expected)), (pattern, text, TestPatterns.Pairs(RegexReference.AutomatonMatches(compiled, codePoints))));
            Assert.Equal((pattern, text, TestPatterns.Show(expected)), (pattern, text, TestPatterns.Pairs(RegexReference.DotNetMatches(pattern, codePoints))));
        }
        Assert.Equal(26, TestPatterns.HandWrittenCases.Length);
    }

    [Fact]
    public void On_the_random_patterns_and_texts_the_automaton_equals_dotnet_regex()
    {
        var random = Js.CreateRandom(20260917);
        var fitting = 0;
        var texts = 0;
        var matches = 0;
        for (var index = 0; index < 400; index++)
        {
            var pattern = TestPatterns.RandomPattern(random, 3);
            var compiled = PatternCompiler.Compile(pattern);
            if (PatternCompiler.CapacityErrors(compiled, Small).IsEmpty) fitting++;
            var patternTexts = Enumerable.Range(0, 4).Select(_ => TestPatterns.CodePointsOf(TestPatterns.RandomText(random, 16)))
                .Concat(Enumerable.Range(0, 2).Select(_ => TestPatterns.BoundaryText(random, compiled, 16)))
                .ToList();
            foreach (var codePoints in patternTexts)
            {
                var expected = TestPatterns.Pairs(RegexReference.DotNetMatches(pattern, codePoints));
                Assert.Equal((pattern, TestPatterns.Show(codePoints), expected), (pattern, TestPatterns.Show(codePoints), TestPatterns.Pairs(RegexReference.AutomatonMatches(compiled, codePoints))));
                texts++;
                matches += RegexReference.AutomatonMatches(compiled, codePoints).Count;
            }
        }
        output.WriteLine($"{fitting} of 400 patterns fit 8 states, {texts} texts, {matches} matches");
        Assert.True(fitting > 250, $"{fitting} patterns fit");
        Assert.True(matches > texts, $"{matches} matches in {texts} texts");
    }

    [Fact]
    public void Each_string_of_syntax_characters_that_the_compiler_accepts_matches_as_dotnet_regex_does()
    {
        var random = Js.CreateRandom(99);
        string[] alphabet = ["a", "b", "(", ")", "[", "]", "|", "*", "+", "?", ".", "^", "-", "\\", "d", "w", "{", "}", "é", "$"];
        var accepted = 0;
        for (var index = 0; index < 20_000; index++)
        {
            var length = (int)Math.Floor(random() * 9);
            var pattern = string.Concat(Enumerable.Range(0, length).Select(_ => TestPatterns.Pick(random, alphabet)));
            CompiledPattern compiled;
            try
            {
                compiled = PatternCompiler.Compile(pattern);
            }
            catch (PatternSyntaxException)
            {
                continue;
            }
            var codePoints = TestPatterns.CodePointsOf(TestPatterns.RandomText(random, 10).Replace('c', 'd'));
            Assert.Equal((pattern, TestPatterns.Pairs(RegexReference.DotNetMatches(pattern, codePoints))), (pattern, TestPatterns.Pairs(RegexReference.AutomatonMatches(compiled, codePoints))));
            accepted++;
        }
        output.WriteLine($"{accepted} of 20,000 strings accepted");
        Assert.True(accepted > 2_000, $"{accepted} strings accepted");
    }

    [Fact]
    public void Dotnet_pattern_reads_code_points()
    {
        // A plain class of the basic plane stays a .NET class.
        var simple = DotNetPattern.Translate("[a-z0-9.]+@[a-z.]+");
        Assert.DoesNotContain("(?:", simple, StringComparison.Ordinal);
        Assert.Matches(@"\A(?:" + simple + @")\z", "ann.lee@example.com");

        // An emoji is one code point, so [^a] matches it whole, and . does not match half of it.
        Assert.Equal("(0,1)", TestPatterns.Pairs(RegexReference.DotNetMatches("[^a]", CodePoints.Of("😀"))));
        Assert.Equal("(0,2)", TestPatterns.Pairs(RegexReference.DotNetMatches(".+", CodePoints.Of("😀é"))));
        // A lone surrogate is its own code point.
        Assert.Equal("(0,1) (1,2)", TestPatterns.Pairs(RegexReference.DotNetMatches(".", [0xd800, 'a'])));
        // \d and \w read only ASCII, and [] matches nothing.
        Assert.Empty(RegexReference.DotNetMatches("\\d|\\w|[]", CodePoints.Of("٣é")));
        // A line feed at the end does not satisfy the end of the whole pattern.
        Assert.Empty(RegexReference.DotNetMatches("a", CodePoints.Of("\n")));
        Assert.Empty(PatternCompiler.Compile("").Targets[0]);
    }
}
