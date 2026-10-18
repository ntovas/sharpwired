// Handwired source: tests/json-search/highlighter/reference.test.ts. The plain C# reference alone: against
// System.Text.Json, against the definitions of the first error on texts cut short, and against first errors
// and display kinds written by hand. The highlighter tests compare the program with this reference, so these
// tests check the reference itself on broken texts. System.Text.Json takes the place of JSON.parse.

using System.Text;
using System.Text.Json;
using JsonSearch;

namespace JsonSearch.Tests;

public sealed class JsonReferenceTests
{
    private const int Capacity = Kinds.Capacity;

    // JsonDocument.Parse with the default options: no comments and no trailing commas. JSON.parse reads
    // UTF-16, and System.Text.Json reads UTF-8. A lone surrogate has no UTF-8 form, so Encoding.UTF8 writes
    // U+FFFD in its place. This keeps the grammar: in a string both are string characters, and outside a
    // string both start no token.
    internal static bool Parses(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetBytes(text));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [Fact]
    public void The_corpus_has_2509_texts_and_each_error_kind()
    {
        Assert.Equal(2509, Corpus.All.Count);
        Assert.Equal(129, Corpus.EdgeCases.Count);
        var kinds = Corpus.All.Select(text => JsonReference.Highlight(text).FirstErrorKind).ToHashSet();
        Assert.Equal(Enum.GetValues<ErrorKind>().ToHashSet(), kinds);
    }

    [Fact]
    public void Is_valid_exactly_when_system_text_json_parses_except_past_the_maximum_depth_or_the_capacity()
    {
        var (valid, invalid, tooDeep, tooLong) = (0, 0, 0, 0);
        foreach (var text in Corpus.All)
        {
            var result = JsonReference.Highlight(text);
            if (result.FirstErrorKind == ErrorKind.TooLong)
            {
                Assert.True(CodePoints.Of(text).Length > Capacity);
                tooLong++;
            }
            else if (result.FirstErrorKind == ErrorKind.TooDeep)
            {
                tooDeep++;
            }
            else
            {
                var parses = Parses(text);
                if (result.Valid != parses)
                {
                    Assert.Fail($"System.Text.Json {(parses ? "accepts" : "refuses")} {JsonSerializer.Serialize(text)}, and the reference says {result.FirstErrorKind} at {result.FirstErrorPosition}");
                }
                if (parses) valid++;
                else invalid++;
            }
        }
        // Enough of each kind for the agreement to have a meaning.
        Assert.True(valid > 700, $"valid {valid}");
        Assert.True(invalid > 1200, $"invalid {invalid}");
        Assert.True(tooDeep > 30, $"tooDeep {tooDeep}");
        Assert.True(tooLong > 20, $"tooLong {tooLong}");
    }

    [Fact]
    public void A_text_that_system_text_json_accepts_past_depth_16_reports_too_deep_at_the_17th_opener()
    {
        var text = "{\"a\":" + new string('[', Kinds.MaximumDepth) + "1" + new string(']', Kinds.MaximumDepth) + "}";
        Assert.True(Parses(text));
        var result = JsonReference.Highlight(text);
        Assert.Equal((false, ErrorKind.TooDeep, 5 + 15), (result.Valid, result.FirstErrorKind, result.FirstErrorPosition));
    }

    [Fact]
    public void Each_prefix_of_a_valid_text_is_incomplete_or_valid_and_a_text_cut_at_its_first_error_has_no_earlier_error()
    {
        foreach (var text in Corpus.All.Take(1200))
        {
            var codePoints = CodePoints.Split(text);
            var result = JsonReference.Highlight(text);
            if (result.Valid)
            {
                for (var cut = 0; cut < codePoints.Count; cut++) CheckPrefix(codePoints, cut);
            }
            else if (result.FirstErrorPosition <= codePoints.Count)
            {
                CheckPrefix(codePoints, result.FirstErrorPosition);
            }
        }
    }

    private static void CheckPrefix(List<string> codePoints, int cut)
    {
        var prefix = JsonReference.Highlight(string.Concat(codePoints.Take(cut)));
        if (!prefix.Valid) Assert.Equal((ErrorKind.UnexpectedEnd, cut), (prefix.FirstErrorKind, prefix.FirstErrorPosition));
    }

    public static TheoryData<string, int, ErrorKind> FirstErrors() => new()
    {
        { "[1 2]", 3, ErrorKind.ExpectedCommaOrClose }, { "{\"a\" 1}", 5, ErrorKind.ExpectedColon }, { "[1}", 2, ErrorKind.MismatchedBracket },
        { "01", 1, ErrorKind.BadNumber }, { "1.", 2, ErrorKind.UnexpectedEnd }, { "1.]", 2, ErrorKind.BadNumber }, { "\"\\q\"", 2, ErrorKind.BadEscape },
        { "\"\\u12x4\"", 5, ErrorKind.BadUnicodeEscape }, { "tru", 3, ErrorKind.UnexpectedEnd }, { "trux", 3, ErrorKind.BadLiteral },
        { "[x]", 1, ErrorKind.UnexpectedCharacter }, { "\"a\tb\"", 2, ErrorKind.ControlCharacterInString }, { "1 1", 2, ErrorKind.ExtraAfterValue },
        { "[1,]", 3, ErrorKind.ExpectedValue }, { "{1:2}", 1, ErrorKind.ExpectedKey }, { "{\"a\":1,}", 7, ErrorKind.ExpectedKey }, { "", 0, ErrorKind.UnexpectedEnd },
        { "[x\n1]", 1, ErrorKind.UnexpectedCharacter }, { new string('[', 17), 16, ErrorKind.TooDeep }, { "[" + new string(' ', Capacity) + "]", Capacity, ErrorKind.TooLong },
    };

    [Theory]
    [MemberData(nameof(FirstErrors))]
    public void Reports_the_first_error_where_the_definitions_say(string text, int position, ErrorKind kind)
    {
        var result = JsonReference.Highlight(text);
        Assert.Equal((position, kind), (result.FirstErrorPosition, result.FirstErrorKind));
    }

    // One letter for each code point, written by hand from the rules at the top of JsonReference.cs and in Kinds.cs.
    public static TheoryData<string, string> HandWrittenKinds() => new()
    {
        // A string in a key position is a key, and a string value is a string.
        { "{\"a\": \"b\"}", "pkkkpwsssp" },
        { "[\"a\"]", "psssp" },
        { "[1, true, null, -2.5e3]", "pnpwllllpwllllpwnnnnnnp" },
        { " \t\r\n1", "wwwwn" },
        // An escape is its own kind, in a value and in a key.
        { "\"a\\n\\u00e9\"", "sseeeeeeees" },
        { "{\"a\\tb\": 1}", "pkkeekkpwnp" },
        // After a lexer error, the rest of the line is an error, and the next line is tokenized again.
        { "[x y\n1]", "pxxxwnp" },
        { "[01]", "pnxx" },
        { "[nul!]", "plllxx" },
        { "\"\\u12x4\"", "seeeexxx" },
        { "\"\\q\"", "sexx" },
        // A line feed that is itself the error starts the tokenizer again at once, here at a b that starts no token.
        { "\"a\nb\"", "ssxxx" },
        // A key is found only before the first error, which is the second string here.
        { "{\"a\": 1 \"b\": 2, \"c\": 3}", "pkkkpwnwssspwnpwssspwnp" },
    };

    [Theory]
    [MemberData(nameof(HandWrittenKinds))]
    public void Shows_each_character_as_the_kind_that_the_rules_say(string text, string letters)
    {
        var lettersOfKind = new Dictionary<DisplayKind, char>
        {
            [DisplayKind.Whitespace] = 'w', [DisplayKind.Punctuation] = 'p', [DisplayKind.String] = 's', [DisplayKind.Key] = 'k',
            [DisplayKind.Escape] = 'e', [DisplayKind.Number] = 'n', [DisplayKind.Literal] = 'l', [DisplayKind.Error] = 'x',
        };
        var result = JsonReference.Highlight(text);
        var length = CodePoints.Of(text).Length;
        var shown = new string([.. result.DisplayKinds.Take(length).Select(kind => lettersOfKind.GetValueOrDefault(kind, '?'))]);
        Assert.Equal(letters, shown);
        // The cell after the text is PastEnd.
        Assert.Equal(DisplayKind.PastEnd, result.DisplayKinds[length]);
        Assert.Equal(Capacity, result.DisplayKinds.Length);
    }
}
