// Handwired source: src/json-search/demo.ts and tests/json-search/demo.test.ts. From demo.ts, the source is the frame
// of the page. It has the answers that arrived, the pattern, the toggles, the map, the document and the line of numbers.
// The window app with scripted WindowInput frames and a fake search service whose answers the test releases at chosen
// frames. The tests read the draw commands: the field text and the caret, the selected segments, and the line. They
// also read the map pixels, and the glyphs and the marks of the document.

using System.Collections.Immutable;
using Sharpwired.Gpu;
using Sharpwired.Raylib;

namespace JsonSearch.Tests;

public sealed class JsonSearchAppTests
{
    private const int Width = 1280;
    private const int Height = 860;
    private static readonly string[] Texts = [.. Enumerable.Range(0, 20).Select(index => $"{{\"id\":{index},\"note\":\"error {index}\"}}")];
    private static readonly WindowLayout Layout = WindowLayout.For(Width, Height, 1, Texts.Length);

    [Fact]
    public void The_first_frame_has_the_focus_in_the_empty_field_all_and_gpu_selected_and_the_line_for_no_pattern()
    {
        var (app, service) = Start();
        var commands = Frame(app);
        Assert.Equal("", FieldText(commands));
        Assert.Equal(Layout.FieldText.X, Caret(commands).X);
        Assert.Equal(["All", "GPU"], SelectedSegments(commands));
        Assert.Equal("Type a pattern to search the 20 documents.", Line(commands));
        Assert.Empty(service.Searches);
        // The panel asks for document 0 with no search.
        Assert.Equal((0, (string?)null), (service.Documents[^1].Index, service.Documents[^1].Pattern));
        // The map shows each cell in the plain color.
        var pixels = Pixels(commands);
        Assert.All(Enumerable.Range(0, 20), index => Assert.Equal(Palette.CellPlain, CellPixel(pixels, index)));
        Assert.Equal("JSON search", app.Title);
        Assert.Equal((1280, 860), (app.InitialWidth, app.InitialHeight));
    }

    [Fact]
    public void Error_typed_one_character_in_each_frame_sends_one_request_in_each_frame_and_the_newest_result_shows()
    {
        var (app, service) = Start();
        Frame(app);
        foreach (var character in "error") Frame(app, Typed(character.ToString()));
        Assert.Equal(["e", "er", "err", "erro", "error"], service.Searches.Select(search => search.Pattern));
        Assert.All(service.Searches, search => Assert.Equal((SearchScope.All, SearchBackend.Gpu), (search.Scope, search.Backend)));
        Assert.Equal(5, service.Searches.Select(search => search.Id).Distinct().Count());
        Assert.Equal(["e", "er", "err", "erro", "error"], service.DotNet.Select(timing => timing.Pattern));
        var commands = Frame(app);
        Assert.Equal("error", FieldText(commands));
        Assert.Equal(Layout.FieldText.X + (5 * Layout.FieldText.CellWidth), Caret(commands).X);
        Assert.Equal("Searching 20 documents…", Line(commands));

        // The answer of an older request does not show. The answer of the newest request shows.
        service.Release(new SearchAnswer.Found(service.Searches[2].Id, Searched(Counts((3, 9)), 20, 4.2)));
        Assert.Equal("Searching 20 documents…", Line(Frame(app)));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts((5, 1), (7, 2)), 20, 1.04)));
        commands = Frame(app);
        Assert.Equal("2 of 20 documents match. This program: 1.0 ms on the GPU.", Line(commands));
        var pixels = Pixels(commands);
        Assert.Equal(Palette.CellMatch, CellPixel(pixels, 5));
        Assert.Equal(Palette.CellMatch, CellPixel(pixels, 7));
        Assert.Equal(Palette.CellPlain, CellPixel(pixels, 6));
        Assert.Equal(Palette.CellPlain, CellPixel(pixels, 3));

        // The .NET Regex time of an older pattern does not show, and the time of the pattern in the field does.
        service.Release(new SearchAnswer.DotNetTime(service.DotNet[1].Id, new DotNetTime(99, false)));
        Assert.Equal("2 of 20 documents match. This program: 1.0 ms on the GPU.", Line(Frame(app)));
        service.Release(new SearchAnswer.DotNetTime(service.DotNet[^1].Id, new DotNetTime(5, false)));
        Assert.Equal("2 of 20 documents match. This program: 1.0 ms on the GPU. .NET Regex: 5.0 ms.", Line(Frame(app)));

        // The panel shows the first document with a match, with the marks of the search.
        var request = service.Documents[^1];
        Assert.Equal((5, "error", SearchScope.All), (request.Index, request.Pattern, request.Scope));
        var marks = new float[Kinds.Capacity];
        marks[14] = 2;
        for (var position = 15; position < 19; position++) marks[position] = 1;
        service.Release(new SearchAnswer.Document(request.Id, 5, KindsOf(Texts[5]), [.. marks]));
        commands = Frame(app);
        Assert.Equal("Document 6", DocumentName(commands));
        Assert.Equal(Texts[5], DocumentText(commands));
        var wash = Assert.Single(commands.OfType<FillRectangle>(), rectangle => rectangle.Color == Palette.WashLight);
        Assert.Equal(Layout.DocumentText.X + (14 * Layout.DocumentText.CellWidth), wash.X, 6);
        Assert.Equal(5 * Layout.DocumentText.CellWidth, wash.Width, 6);
    }

    [Fact]
    public void Control_2_selects_keys_and_sends_a_keys_request_and_control_b_changes_the_backend()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("id"));
        // The "2" that the keyboard also types with Control is not text.
        var commands = Frame(app, Typed("2") with { Keys = [new KeyPress(Key.Digit2, false, true)] });
        Assert.Equal("id", FieldText(commands));
        Assert.Equal(["Keys", "GPU"], SelectedSegments(commands));
        Assert.Equal(("id", SearchScope.Keys, SearchBackend.Gpu), (service.Searches[^1].Pattern, service.Searches[^1].Scope, service.Searches[^1].Backend));

        commands = Frame(app, Idle() with { Keys = [new KeyPress(Key.B, false, true)] });
        Assert.Equal(["Keys", "CPU"], SelectedSegments(commands));
        Assert.Equal((SearchScope.Keys, SearchBackend.Cpu), (service.Searches[^1].Scope, service.Searches[^1].Backend));
        // A change of the backend does not time .NET Regex again: the time is for the pattern and the scope.
        Assert.Equal([SearchScope.All, SearchScope.Keys], service.DotNet.Select(timing => timing.Scope));

        // Control+3 and Control+1 select String values and All. A click on a segment does the same.
        Assert.Equal(["String values", "CPU"], SelectedSegments(Frame(app, Idle() with { Keys = [new KeyPress(Key.Digit3, false, true)] })));
        Assert.Equal(["All", "CPU"], SelectedSegments(Frame(app, Idle() with { Keys = [new KeyPress(Key.Digit1, false, true)] })));
        Assert.Equal(["Keys", "CPU"], SelectedSegments(Click(app, Layout.Scopes[1])));
        Assert.Equal(["Keys", "GPU"], SelectedSegments(Click(app, Layout.Backends[0])));
        Assert.Equal((SearchScope.Keys, SearchBackend.Gpu), (service.Searches[^1].Scope, service.Searches[^1].Backend));
    }

    [Fact]
    public void Without_a_gpu_the_backend_stays_on_the_cpu()
    {
        var (app, service) = Start(hasGpu: false);
        Assert.Equal(["All", "CPU"], SelectedSegments(Frame(app)));
        Frame(app, Typed("a"));
        Assert.Equal(["All", "CPU"], SelectedSegments(Frame(app, Idle() with { Keys = [new KeyPress(Key.B, false, true)] })));
        Assert.Equal(["All", "CPU"], SelectedSegments(Click(app, Layout.Backends[0])));
        Assert.All(service.Searches, search => Assert.Equal(SearchBackend.Cpu, search.Backend));
    }

    [Fact]
    public void A_click_on_an_example_sets_the_pattern_and_the_scope()
    {
        var (app, service) = Start();
        Frame(app);
        var commands = Click(app, Layout.Examples[1]);
        Assert.Equal("[a-z]+Id", FieldText(commands));
        Assert.Equal(["Keys", "GPU"], SelectedSegments(commands));
        Assert.Equal(("[a-z]+Id", SearchScope.Keys), (service.Searches[^1].Pattern, service.Searches[^1].Scope));
        commands = Click(app, Layout.Examples[0]);
        Assert.Equal(("[a-z0-9.]+@[a-z.]+", SearchScope.StringValues), (service.Searches[^1].Pattern, service.Searches[^1].Scope));
        Assert.Equal(["String values", "GPU"], SelectedSegments(commands));
        // The field has the focus after the click, with the caret at the end.
        commands = Frame(app, Typed("x"));
        Assert.Equal("[a-z0-9.]+@[a-z.]+x", FieldText(commands));
    }

    [Fact]
    public void The_pointer_over_the_map_picks_the_nearest_match_a_click_keeps_it_and_enter_steps_around_the_ends()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("error"));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts((3, 1), (11, 1), (17, 2)), 20, 2)));
        Frame(app);
        Assert.Equal(3, service.Documents[^1].Index);

        // The pointer over document 0 picks document 3, the nearest one with a match. Over document 12, document 11.
        var map = Layout.MapLayout;
        var (x0, y0) = MapPoint(0);
        Frame(app, Pointer(x0, y0));
        Assert.Equal(3, service.Documents[^1].Index);
        var (x12, y12) = MapPoint(12);
        Frame(app, Pointer(x12, y12));
        Assert.Equal(11, service.Documents[^1].Index);
        // Away from the map, the panel goes back to the first match.
        Frame(app, Pointer(5, Height - 5));
        Assert.Equal(3, service.Documents[^1].Index);

        // A click keeps document 11, also when the pointer goes away.
        Frame(app, Pointer(x12, y12));
        Frame(app, Pointer(x12, y12) with { PointerPressed = true });
        Frame(app, Pointer(5, Height - 5));
        Assert.Equal(11, service.Documents[^1].Index);
        service.Release(new SearchAnswer.Document(service.Documents[^1].Id, 11, KindsOf(Texts[11]), [.. new float[Kinds.Capacity]]));
        Assert.Equal("Document 12", DocumentName(Frame(app, Pointer(5, Height - 5))));

        // Enter goes to 17, then around the end to 3. Shift+Enter goes back to 17.
        Frame(app, Pointer(5, Height - 5) with { Keys = [new KeyPress(Key.Enter, false, false)] });
        Assert.Equal(17, service.Documents[^1].Index);
        Frame(app, Pointer(5, Height - 5) with { Keys = [new KeyPress(Key.Enter, false, false)] });
        Assert.Equal(3, service.Documents[^1].Index);
        Frame(app, Pointer(5, Height - 5) with { Keys = [new KeyPress(Key.Enter, true, false)] });
        Assert.Equal(17, service.Documents[^1].Index);
        Assert.Equal(map.DocumentCount, Texts.Length);
    }

    [Fact]
    public void A_syntax_error_and_a_pattern_of_33_states_show_their_sentences_and_send_no_search()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("ab"));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts((2, 1)), 20, 1)));
        Frame(app);
        var searches = service.Searches.Count;
        var commands = Frame(app, Typed("("));
        Assert.Equal("Character 3 of the pattern: this group is never closed.", Line(commands));
        Assert.Equal(searches, service.Searches.Count);
        // The map keeps the last result.
        Assert.Equal(Palette.CellMatch, CellPixel(Pixels(commands), 2));
        // Backspace gives the pattern of the last result back.
        Assert.Equal("1 of 20 documents matches. This program: 1.0 ms on the GPU.", Line(Frame(app, Idle() with { Keys = [new KeyPress(Key.Backspace, false, false)] })));

        Frame(app, Idle() with { Keys = [new KeyPress(Key.Home, false, false), new KeyPress(Key.Delete, false, false), new KeyPress(Key.Delete, false, false)] });
        commands = Frame(app, Typed(new string('a', 33)));
        Assert.Equal("This pattern needs 34 states, and the largest search program holds 32.", Line(commands));
        Assert.Equal(searches, service.Searches.Count(search => search.Pattern != ""));
    }

    [Fact]
    public void Backspace_to_a_pattern_with_a_full_answer_drops_the_answer_of_the_longer_pattern()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("ab"));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts((2, 1)), 20, 1)));
        Frame(app);
        Frame(app, Typed("c"));
        var longer = service.Searches[^1];
        Assert.Equal("abc", longer.Pattern);
        var cancels = service.Cancels;
        var searches = service.Searches.Count;
        // The answer of "ab" is here, so Backspace sends no search, and it cancels the search of "abc".
        var commands = Frame(app, Idle() with { Keys = [new KeyPress(Key.Backspace, false, false)] });
        Assert.Equal("1 of 20 documents matches. This program: 1.0 ms on the GPU.", Line(commands));
        Assert.Equal(searches, service.Searches.Count);
        Assert.Equal(cancels + 1, service.Cancels);
        service.Release(new SearchAnswer.Found(longer.Id, Searched(Counts((4, 1), (9, 1)), 20, 2)));
        // The frames past the 250 ms of a long search keep the answer of "ab".
        for (var frame = 0; frame < 20; frame++) Assert.Equal("1 of 20 documents matches. This program: 1.0 ms on the GPU.", Line(Frame(app)));
        Assert.Equal(Palette.CellMatch, CellPixel(Pixels(Frame(app)), 2));
    }

    [Fact]
    public void A_pattern_with_no_search_cancels_the_dotnet_timing_in_progress()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("ab"));
        Assert.Equal("ab", service.DotNet[^1].Pattern);
        Assert.Equal(0, service.DotNetCancels);
        // The syntax error gives no .NET Regex timing, so the timing of "ab" stops.
        Frame(app, Typed("("));
        Assert.Equal(1, service.DotNetCancels);
        // Backspace times "ab" again, because its timing did not arrive.
        Frame(app, Idle() with { Keys = [new KeyPress(Key.Backspace, false, false)] });
        Assert.Equal(["ab", "ab"], service.DotNet.Select(timing => timing.Pattern));
        service.Release(new SearchAnswer.DotNetTime(service.DotNet[^1].Id, new DotNetTime(5, false)));
        Frame(app);
        // An empty field also stops no timing that is complete.
        Frame(app, Idle() with { Keys = [new KeyPress(Key.Backspace, false, false), new KeyPress(Key.Backspace, false, false)] });
        Assert.Equal(1, service.DotNetCancels);
    }

    [Fact]
    public void A_cpu_search_in_progress_shows_the_of_the_first_sentence_and_waiting_cells_past_its_front()
    {
        var (app, service) = Start();
        Frame(app, Idle() with { Keys = [new KeyPress(Key.B, false, true)] });
        Frame(app, Typed("error"));
        var id = service.Searches[^1].Id;
        Assert.Equal(SearchBackend.Cpu, service.Searches[^1].Backend);
        service.Release(new SearchAnswer.Found(id, Searched(Counts((1, 1), (6, 3)), 8, 3120)));
        var commands = Frame(app);
        Assert.Equal("2 of the first 8 documents match. This program: 3.1 s on the CPU so far.", Line(commands));
        var pixels = Pixels(commands);
        Assert.Equal(Palette.CellMatch, CellPixel(pixels, 1));
        Assert.Equal(Palette.CellPlain, CellPixel(pixels, 0));
        Assert.All(Enumerable.Range(8, 12), index => Assert.Equal(Palette.CellWaiting, CellPixel(pixels, index)));

        // A new character starts a new search at once, and the progress of the old search does not show.
        Frame(app, Typed("s"));
        Assert.NotEqual(id, service.Searches[^1].Id);
        service.Release(new SearchAnswer.Found(id, Searched(Counts((1, 1), (6, 3), (12, 1)), 16, 4000)));
        Assert.Equal("2 of the first 8 documents match. This program: 3.1 s on the CPU so far.", Line(Frame(app)));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts(), 20, 5000)));
        Assert.Equal("None of the 20 documents matches. This program: 5.0 s on the CPU.", Line(Frame(app)));
    }

    [Fact]
    public void A_failed_search_shows_the_failure_sentence_and_the_window_goes_on()
    {
        var (app, service) = Start();
        Frame(app);
        Frame(app, Typed("a"));
        service.Release(new SearchAnswer.Failed(service.Searches[^1].Id, Sentences.Failure("the device was lost")));
        Assert.Equal("The search stopped with an error: the device was lost.", Line(Frame(app)));
        Frame(app, Typed("b"));
        service.Release(new SearchAnswer.Found(service.Searches[^1].Id, Searched(Counts((4, 1)), 20, 1)));
        Assert.Equal("1 of 20 documents matches. This program: 1.0 ms on the GPU.", Line(Frame(app)));
    }

    [Fact]
    public void The_font_members_name_the_system_fonts_and_the_code_points_of_the_documents()
    {
        var app = new JsonSearchApp(new FakeSearchService(2, true), ["{\"name\":\"美咲\"}", "[\"Tromsø\"]"]);
        Assert.Contains("/System/Library/Fonts/Supplemental/Arial Unicode.ttf", app.FontPaths);
        Assert.Contains("/System/Library/Fonts/Monaco.ttf", app.FontPaths);
        Assert.Contains(0x7f8e, app.FontCodePoints);
        Assert.Contains(0x54b2, app.FontCodePoints);
        Assert.Contains(0xf8, app.FontCodePoints);
        // The curved apostrophe and the ellipsis of the sentences.
        Assert.Contains(0x2019, app.FontCodePoints);
        Assert.Contains(0x2026, app.FontCodePoints);
    }

    [Fact]
    public void A_smaller_window_keeps_the_map_and_the_document_inside_it()
    {
        var (app, _) = Start();
        var commands = Frame(app, new WindowInput(0, 0, false, false, false, 900, 600));
        var map = Assert.Single(commands.OfType<DrawPixels>());
        Assert.True(map.X + map.DrawnWidth <= 900);
        Assert.True(map.Y + map.DrawnHeight <= 600);
        var sheets = commands.OfType<FillRectangle>().Where(rectangle => rectangle.Color == Palette.Sheet).ToList();
        Assert.All(sheets, sheet => Assert.True(sheet.X + sheet.Width <= 900 && sheet.Y + sheet.Height <= 600, $"{sheet}"));
        var small = WindowLayout.For(900, 600, 1, Texts.Length);
        Assert.True(small.DocumentText.X + (DocumentView.Columns * small.DocumentText.CellWidth) <= 900);
    }

    // The app, its fake service, and the frame clock of the test.
    private static (JsonSearchApp App, FakeSearchService Service) Start(bool hasGpu = true)
    {
        var service = new FakeSearchService(Texts.Length, hasGpu);
        return (new JsonSearchApp(service, Texts), service);
    }

    private static double clock;

    private static IReadOnlyList<DrawCommand> Frame(JsonSearchApp app, WindowInput? input = null)
    {
        clock += 16;
        return app.Frame(input ?? Idle(), clock);
    }

    private static IReadOnlyList<DrawCommand> Click(JsonSearchApp app, Box box)
    {
        var (x, y) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
        return Frame(app, Pointer(x, y) with { PointerPressed = true });
    }

    private static WindowInput Idle() => new(0, 0, false, false, false, Width, Height);

    private static WindowInput Pointer(double x, double y) => new(x, y, false, false, false, Width, Height);

    private static WindowInput Typed(string text) => Idle() with { Text = text };

    private static (double X, double Y) MapPoint(int index)
    {
        var center = MatchMap.CellCenter(Layout.MapLayout, index);
        return (Layout.Map.X + center.X, Layout.Map.Y + center.Y);
    }

    private static ImmutableArray<float> Counts(params (int Index, int Count)[] matches)
    {
        var counts = new float[Texts.Length];
        foreach (var (index, count) in matches) counts[index] = count;
        return [.. counts];
    }

    private static SearchResult.Searched Searched(ImmutableArray<float> counts, int done, double ms)
    {
        var withMatch = counts.Take(done).Count(count => count > 0);
        var matches = (long)counts.Take(done).Sum();
        return new SearchResult.Searched(8, counts, done, withMatch, matches, [], new LoopTimes(0, 0, 0, 0, ms));
    }

    private static ImmutableArray<byte> KindsOf(string text) =>
        [.. Enumerable.Range(0, Kinds.Capacity).Select(position => position < text.Length ? (byte)DisplayKind.String : (byte)DisplayKind.PastEnd)];

    private static string FieldText(IReadOnlyList<DrawCommand> commands) =>
        GlyphText(commands.OfType<DrawGlyphs>().Single(glyphs => glyphs.X == Layout.FieldText.X && glyphs.Y == Layout.FieldText.Y));

    private static string DocumentText(IReadOnlyList<DrawCommand> commands) =>
        GlyphText(commands.OfType<DrawGlyphs>().Single(glyphs => glyphs.X == Layout.DocumentText.X && glyphs.Y == Layout.DocumentText.Y));

    // The glyphs of one row, with a space for each empty cell between two glyphs.
    private static string GlyphText(DrawGlyphs glyphs)
    {
        var text = new System.Text.StringBuilder();
        var column = 0;
        foreach (var glyph in glyphs.Glyphs.OrderBy(glyph => glyph.Row).ThenBy(glyph => glyph.Column))
        {
            text.Append(' ', Math.Max(0, glyph.Column - column));
            text.Append(char.ConvertFromUtf32(glyph.CodePoint));
            column = glyph.Column + DocumentView.CellWidthOf(glyph.CodePoint);
        }
        return text.ToString();
    }

    private static FillRectangle Caret(IReadOnlyList<DrawCommand> commands) =>
        commands.OfType<FillRectangle>().Single(rectangle => rectangle.Color == Palette.Caret);

    private static string Line(IReadOnlyList<DrawCommand> commands) =>
        commands.OfType<DrawText>().Single(text => text.X == Layout.Line.X && text.Y == Layout.Line.Y).Text;

    private static string DocumentName(IReadOnlyList<DrawCommand> commands) =>
        commands.OfType<DrawText>().Single(text => text.X == Layout.DocumentName.X && text.Y == Layout.DocumentName.Y).Text;

    // The labels of the selected segments, scope first.
    private static string[] SelectedSegments(IReadOnlyList<DrawCommand> commands)
    {
        var selected = commands.OfType<FillRectangle>().Where(rectangle => rectangle.Color == Palette.SegmentSelected).ToList();
        string[] scopes = ["All", "Keys", "String values"];
        string[] backends = ["GPU", "CPU"];
        var names = new List<string>();
        for (var index = 0; index < scopes.Length; index++)
        {
            if (selected.Any(rectangle => Layout.Scopes[index].Contains(rectangle.X + 1, rectangle.Y + 1))) names.Add(scopes[index]);
        }
        for (var index = 0; index < backends.Length; index++)
        {
            if (selected.Any(rectangle => Layout.Backends[index].Contains(rectangle.X + 1, rectangle.Y + 1))) names.Add(backends[index]);
        }
        return [.. names];
    }

    private static DrawPixels Pixels(IReadOnlyList<DrawCommand> commands) => Assert.Single(commands.OfType<DrawPixels>());

    // The color of the first pixel of the cell of a document.
    private static uint CellPixel(DrawPixels pixels, int index)
    {
        var map = Layout.MapLayout;
        Assert.Equal((map.Width, Layout.MapPixelHeight), (pixels.Width, pixels.Height));
        return pixels.Pixels[(index / map.Columns * map.Pitch * map.Width) + (index % map.Columns * map.Pitch)];
    }
}

/// <summary>A search service that records the requests and gives the answers that the test releases.</summary>
internal sealed class FakeSearchService(int documentCount, bool hasGpu) : ISearchService
{
    private readonly Queue<SearchAnswer> released = new();

    public int DocumentCount { get; } = documentCount;

    public bool HasGpu { get; } = hasGpu;

    public List<(int Id, string Pattern, SearchScope Scope, SearchBackend Backend)> Searches { get; } = [];

    public List<(int Id, int Index, string? Pattern, SearchScope Scope)> Documents { get; } = [];

    public List<(int Id, string Pattern, SearchScope Scope)> DotNet { get; } = [];

    public int Cancels { get; private set; }

    public void Search(int id, string pattern, SearchScope scope, SearchBackend backend) => Searches.Add((id, pattern, scope, backend));

    public void CancelSearch() => Cancels++;

    public void ShowDocument(int id, int index, string? pattern, SearchScope scope) => Documents.Add((id, index, pattern, scope));

    public void TimeDotNet(int id, string pattern, SearchScope scope) => DotNet.Add((id, pattern, scope));

    public int DotNetCancels { get; private set; }

    public void CancelDotNet() => DotNetCancels++;

    public IReadOnlyList<SearchAnswer> Poll()
    {
        var answers = released.ToList();
        released.Clear();
        return answers;
    }

    public void Release(SearchAnswer answer) => released.Enqueue(answer);
}
