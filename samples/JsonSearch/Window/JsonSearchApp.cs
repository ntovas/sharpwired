// Handwired source: src/json-search/demo.ts (mountJsonSearch and its render function: the answers that arrived since
// the last frame, the pattern and the two toggles, the examples, the map and its pointer, the Enter steps, the
// document that the panel shows, and the line of numbers) and src/json-search/assets/json-search.html. The window app
// of the JSON search. Each frame takes in the answers of the search service, applies the input, decides what the
// window shows, asks the service for the work that is due, and returns the draw commands. It makes no Raylib call.
// Differences from Handwired:
// - The window starts with an empty pattern (the page starts with "error"), as the manual check of the plan types it.
// - A refused pattern keeps the last map (the plan, manual check step 9). The page shows the plain map.
// - The service gives a timing of .NET Regex for each new pattern and scope, and cancels the one before it.

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Sharpwired.Raylib;

namespace JsonSearch;

/// <summary>The JSON search window: the pattern field, the scope and backend toggles, the line, the map and one document.</summary>
public sealed class JsonSearchApp : IWindowApp
{
    // A search in flight for this long gets the "Searching" line, and the map shows no matches.
    private const double LongSearchMs = 250;
    private const double LabelSize = 14;

    // The code points of the sentences and the labels past ASCII: the curved apostrophe, the ellipsis and the
    // no-break space.
    private static readonly int[] SentenceCodePoints = [0x2019, 0x2026, 0x00a0];

    private readonly ISearchService service;
    private readonly IReadOnlyList<string> texts;
    private readonly PatternField field = new();
    private readonly Lazy<IReadOnlyList<int>> codePoints;
    private bool focused = true;
    private SearchScope scope = SearchScope.All;
    private SearchBackend backend;
    private int nextId;

    // The search for the pattern in the field, the newest accepted answer, and the line.
    private string wantedKey = "\0";
    private double wantedSince;
    private int searchId;
    private string searchKey = "";
    private (string Key, SearchResult.Searched Result)? found;
    private bool hadResult;
    private string? failure;
    private string line = "";
    private bool lineIsError;

    // The .NET Regex timing of the pattern and the scope.
    private string dotNetKey = "";
    private int dotNetId;
    private (string Key, DotNetTime Time)? dotNet;

    // The map: the answer that it shows and whether the documents past the answer wait for a search.
    private SearchResult.Searched? mapResult;
    private bool mapWaiting;
    private object? paintedKey;
    private ImmutableArray<uint> mapPixels;
    private int mapVersion;

    // The document: the request, the answer that the panel shows, and the document that a click or Enter keeps.
    private (int Index, string Key)? requestedDocument;
    private int documentId;
    private (int Index, ImmutableArray<byte> Kinds, ImmutableArray<float> Marks)? shown;
    private int? kept;

    /// <summary>Make the app over the service and the texts of its documents.</summary>
    /// <exception cref="ArgumentException">The service and the texts have a different document count.</exception>
    public JsonSearchApp(ISearchService service, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);
        if (service.DocumentCount != texts.Count) throw new ArgumentException($"the service holds {service.DocumentCount} documents and the app {texts.Count}", nameof(texts));
        this.service = service;
        this.texts = texts;
        backend = service.HasGpu ? SearchBackend.Gpu : SearchBackend.Cpu;
        codePoints = new Lazy<IReadOnlyList<int>>(CodePointsOfTexts);
    }

    public string Title => "JSON search";

    public int InitialWidth => 1280;

    public int InitialHeight => 860;

    /// <summary>--paper of site.css.</summary>
    public Rgba Background => Palette.Paper;

    /// <summary>
    /// The monospaced Monaco first, for the grid, then Arial Unicode for the CJK code points, then two Linux fonts.
    /// </summary>
    public IReadOnlyList<string> FontPaths { get; } =
    [
        "/System/Library/Fonts/Monaco.ttf",
        "/System/Library/Fonts/Supplemental/Arial Unicode.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/truetype/noto/NotoSansMono-Regular.ttf",
    ];

    /// <summary>The code points of the documents past ASCII, and the code points of the sentences.</summary>
    public IReadOnlyList<int> FontCodePoints => codePoints.Value;

    public IReadOnlyList<DrawCommand> Frame(WindowInput input, double frameTimeMs)
    {
        var layout = WindowLayout.For(input.ViewportWidth, input.ViewportHeight, input.PixelScale, texts.Count);
        TakeAnswers();
        var (mapClick, steps) = ApplyInput(input, layout);
        if (!service.HasGpu) backend = SearchBackend.Cpu;

        // The pattern, the scope and the backend give the search that is due.
        var pattern = field.Text;
        var analysis = Sentences.Analyze(pattern);
        var fits = analysis is PatternAnalysis.Fits;
        var key = fits ? $"{backend}\n{scope}\n{pattern}" : "";
        if (key != wantedKey) Request(key, fits, pattern, frameTimeMs);
        var current = fits && found is { } answer && answer.Key == key ? answer.Result : null;
        var searchPending = fits && current is null && failure is null;
        var runsLong = searchPending && frameTimeMs - wantedSince >= LongSearchMs;
        var dotNetTime = fits && dotNet is { } time && time.Key == dotNetKey ? time.Time : (DotNetTime?)null;

        // The map: the answer for the pattern, the plain map with no pattern, and the last answer while a search is in
        // flight or the pattern is refused, until the search runs long.
        if (current is not null) (mapResult, mapWaiting) = (current, true);
        else if (analysis is PatternAnalysis.Empty) (mapResult, mapWaiting) = (null, false);
        else if (runsLong) (mapResult, mapWaiting) = (null, true);

        // The pointer over the map picks a document, a click keeps it, and Enter steps from match to match.
        var counts = mapResult is null ? ReadOnlySpan<float>.Empty : mapResult.MatchCounts.AsSpan();
        var searched = mapResult?.DocumentsDone ?? 0;
        int? hovered = null;
        if (layout.MapSheet.Contains(input.PointerX, input.PointerY))
        {
            var picked = MatchMap.PickedDocument(layout.MapLayout, counts, searched, input.PointerX - layout.Map.X, input.PointerY - layout.Map.Y);
            if (picked >= 0) hovered = picked;
        }
        if (mapClick && hovered is { } clicked) kept = kept == clicked ? null : clicked;
        for (var step = 0; mapResult is not null && step < Math.Abs(steps); step++)
        {
            var next = MatchMap.NextMatch(counts, searched, kept ?? shown?.Index ?? 0, Math.Sign(steps));
            if (next >= 0) kept = next;
        }
        if (current is not null && kept is { } keptIndex && keptIndex < current.DocumentsDone && current.MatchCounts[keptIndex] == 0) kept = null;
        var firstMatch = current is null ? -1 : FirstMatch(current);
        var target = hovered ?? kept ?? (firstMatch >= 0 ? firstMatch : shown?.Index ?? 0);
        if (!searchPending)
        {
            var documentKey = fits ? $"{scope}\n{pattern}" : "";
            if (requestedDocument != (target, documentKey))
            {
                documentId = ++nextId;
                requestedDocument = (target, documentKey);
                service.ShowDocument(documentId, target, fits ? pattern : null, scope);
            }
        }

        UpdateLine(analysis, current, dotNetTime, runsLong);
        return Draw(layout);
    }

    // Take in the answers that arrived. An answer to a request that the app replaced is dropped.
    private void TakeAnswers()
    {
        foreach (var answer in service.Poll())
        {
            switch (answer)
            {
                case SearchAnswer.Found result when result.Id == searchId && searchId != 0:
                    found = (searchKey, result.Result);
                    hadResult = true;
                    break;
                case SearchAnswer.Document document when document.Id == documentId:
                    shown = (document.Index, document.Kinds, document.Marks);
                    break;
                case SearchAnswer.DotNetTime timing when timing.Id == dotNetId && dotNetId != 0:
                    dotNet = (dotNetKey, timing.Time);
                    break;
                case SearchAnswer.Failed failed when failed.Id != 0 && (failed.Id == searchId || failed.Id == documentId || failed.Id == dotNetId):
                    failure = failed.Message;
                    break;
            }
        }
    }

    // The pointer press, the keys and the typed text. Returns whether the press is on the map, and the Enter steps.
    private (bool MapClick, int Steps) ApplyInput(WindowInput input, WindowLayout layout)
    {
        var mapClick = false;
        if (input.PointerPressed) mapClick = Press(input.PointerX, input.PointerY, layout);
        var steps = 0;
        var control = false;
        foreach (var press in input.Keys)
        {
            if (press.Control)
            {
                control = true;
                switch (press.Key)
                {
                    case Key.Digit1: scope = SearchScope.All; break;
                    case Key.Digit2: scope = SearchScope.Keys; break;
                    case Key.Digit3: scope = SearchScope.StringValues; break;
                    case Key.B when service.HasGpu: backend = backend == SearchBackend.Gpu ? SearchBackend.Cpu : SearchBackend.Gpu; break;
                }
            }
            else if (press.Key == Key.Enter)
            {
                steps += press.Shift ? -1 : 1;
            }
            else if (focused)
            {
                field.Apply(press.Key);
            }
        }
        // A frame with Control or Command gives no text: the key of a shortcut is not text.
        if (focused && !control) field.Insert(input.Text);
        return (mapClick, steps);
    }

    // A press on a part of the window. Returns true for a press on the map.
    private bool Press(double x, double y, WindowLayout layout)
    {
        if (layout.Field.Contains(x, y))
        {
            focused = true;
            return false;
        }
        for (var index = 0; index < layout.Scopes.Length; index++)
        {
            if (layout.Scopes[index].Contains(x, y))
            {
                scope = (SearchScope)index;
                return false;
            }
        }
        for (var index = 0; index < layout.Backends.Length; index++)
        {
            if (layout.Backends[index].Contains(x, y))
            {
                if (service.HasGpu) backend = (SearchBackend)index;
                return false;
            }
        }
        for (var index = 0; index < layout.Examples.Length; index++)
        {
            if (layout.Examples[index].Contains(x, y))
            {
                field.SetText(SearchBench.Examples[index].Pattern);
                scope = SearchBench.Examples[index].Scope;
                focused = true;
                return false;
            }
        }
        focused = false;
        return layout.MapSheet.Contains(x, y);
    }

    // A new search key: ask for the search and the .NET Regex time, or stop the search for a pattern with no search.
    private void Request(string key, bool fits, string pattern, double frameTimeMs)
    {
        wantedKey = key;
        wantedSince = frameTimeMs;
        failure = null;
        if (fits)
        {
            // The answer of each document for this key is here already, for example after a Backspace that gives the
            // pattern of the last answer back.
            if (found is not { } last || last.Key != key || last.Result.DocumentsDone < texts.Count)
            {
                searchId = ++nextId;
                searchKey = key;
                service.Search(searchId, pattern, scope, backend);
            }
        }
        else if (searchId != 0)
        {
            searchId = 0;
            searchKey = "";
            service.CancelSearch();
        }
        var timingKey = fits ? $"{scope}\n{pattern}" : "";
        if (timingKey == dotNetKey) return;
        dotNetKey = timingKey;
        dotNetId = 0;
        if (fits && (dotNet is not { } time || time.Key != timingKey))
        {
            dotNetId = ++nextId;
            service.TimeDotNet(dotNetId, pattern, scope);
        }
    }

    private void UpdateLine(PatternAnalysis analysis, SearchResult.Searched? current, DotNetTime? dotNetTime, bool runsLong)
    {
        if (failure is not null)
        {
            (line, lineIsError) = (failure, true);
            return;
        }
        switch (analysis)
        {
            case PatternAnalysis.Empty:
                (line, lineIsError) = (Sentences.Empty(texts.Count), false);
                break;
            case PatternAnalysis.Refused refused:
                (line, lineIsError) = (refused.Sentence, true);
                break;
            case PatternAnalysis.Fits when current is not null:
                (line, lineIsError) = (Sentences.ResultLine(current.DocumentsWithMatch, current.DocumentsDone, current.Times.TotalMs, texts.Count, backend, dotNetTime), false);
                break;
            case PatternAnalysis.Fits when runsLong || !hadResult || lineIsError:
                (line, lineIsError) = (Sentences.Searching(texts.Count), false);
                break;
        }
    }

    private List<DrawCommand> Draw(WindowLayout layout)
    {
        var commands = new List<DrawCommand>();
        DrawField(commands, layout);
        DrawSegments(commands, layout.Scopes, WindowLayout.ScopeLabels, (int)scope, enabled: true);
        DrawSegments(commands, layout.Backends, WindowLayout.BackendLabels, (int)backend, service.HasGpu);
        commands.Add(new DrawText(layout.TryLabel.X, layout.TryLabel.Y + 7, "Try", 12, Palette.Ink3));
        for (var index = 0; index < layout.Examples.Length; index++)
        {
            var box = layout.Examples[index];
            commands.Add(new FillRectangle(box.X, box.Y, box.Width, box.Height, Palette.ExampleWash));
            var glyphs = SearchBench.Examples[index].Pattern.EnumerateRunes().Select((rune, column) => new Glyph(rune.Value, column, 0, Palette.Copper));
            commands.Add(new DrawGlyphs(box.X + layout.ExampleText.X, box.Y, layout.ExampleText.CellWidth, layout.ExampleText.LineHeight, layout.ExampleText.Size, [.. glyphs]));
        }
        commands.Add(new DrawText(layout.Line.X, layout.Line.Y, line, 15, lineIsError ? Palette.Red : Palette.Ink2));
        DrawMap(commands, layout);
        DrawDocument(commands, layout);
        return commands;
    }

    private void DrawField(List<DrawCommand> commands, WindowLayout layout)
    {
        var box = layout.Field;
        var outline = focused ? 2 : 1;
        commands.Add(new FillRectangle(box.X - outline, box.Y - outline, box.Width + (2 * outline), box.Height + (2 * outline), focused ? Palette.Copper : Palette.Rule));
        commands.Add(new FillRectangle(box.X, box.Y, box.Width, box.Height, Palette.Sheet));
        var grid = layout.FieldText;
        var visible = Math.Max(1, (int)Math.Floor((box.Width - (2 * (grid.X - box.X))) / grid.CellWidth));
        var caretColumn = field.CaretColumn;
        var first = Math.Max(0, caretColumn - visible + 1);
        var glyphs = field.Columns()
            .Where(cell => cell.Column >= first && cell.Column - first < visible)
            .Select(cell => new Glyph(cell.CodePoint, cell.Column - first, 0, Palette.Ink));
        commands.Add(new DrawGlyphs(grid.X, grid.Y, grid.CellWidth, grid.LineHeight, grid.Size, [.. glyphs]));
        if (focused) commands.Add(new FillRectangle(grid.X + ((caretColumn - first) * grid.CellWidth), box.Y + 8, 1.5, box.Height - 16, Palette.Caret));
    }

    private static void DrawSegments(List<DrawCommand> commands, ImmutableArray<Box> boxes, ImmutableArray<string> labels, int selected, bool enabled)
    {
        var first = boxes[0];
        var last = boxes[^1];
        commands.Add(new FillRectangle(first.X, first.Y, last.X + last.Width - first.X, first.Height, Palette.SegmentTrack));
        for (var index = 0; index < boxes.Length; index++)
        {
            var box = boxes[index];
            if (index == selected) commands.Add(new FillRectangle(box.X + 3, box.Y + 3, box.Width - 6, box.Height - 6, Palette.SegmentSelected));
            var color = index == selected ? Palette.Ink : enabled ? Palette.Ink2 : Palette.Ink3 with { A = 140 };
            commands.Add(new DrawText(box.X + 12, box.Y + ((box.Height - LabelSize) / 2), labels[index], LabelSize, color));
        }
    }

    private void DrawMap(List<DrawCommand> commands, WindowLayout layout)
    {
        var sheet = layout.MapSheet;
        commands.Add(new FillRectangle(sheet.X - 1, sheet.Y - 1, sheet.Width + 2, sheet.Height + 2, Palette.Rule));
        commands.Add(new FillRectangle(sheet.X, sheet.Y, sheet.Width, sheet.Height, Palette.Sheet));
        var map = layout.MapLayout;
        var paintKey = (mapResult, mapWaiting, map, layout.MapPixelHeight);
        if (!Equals(paintedKey, paintKey))
        {
            var pixels = new uint[map.Width * map.Height];
            var counts = mapResult is null ? ReadOnlySpan<float>.Empty : mapResult.MatchCounts.AsSpan();
            MatchMap.PaintCells(pixels, map, texts.Count, mapResult?.DocumentsDone ?? 0, counts, Palette.Cells, mapWaiting ? Palette.CellWaiting : Palette.CellPlain);
            mapPixels = layout.MapPixelHeight == map.Height
                ? ImmutableCollectionsMarshal.AsImmutableArray(pixels)
                : ImmutableArray.Create(pixels.AsSpan(0, map.Width * layout.MapPixelHeight));
            paintedKey = paintKey;
            mapVersion++;
        }
        commands.Add(new DrawPixels(layout.Map.X, layout.Map.Y, map.Width, layout.MapPixelHeight, mapPixels, mapVersion, map.Scale));
        // The ring around the cell of the document in the panel, heavier for a kept document.
        if (shown is not { } document) return;
        var center = MatchMap.CellCenter(map, document.Index);
        var (x, y) = (layout.Map.X + center.X, layout.Map.Y + center.Y);
        if (!layout.Map.Contains(x, y)) return;
        var weight = document.Index == kept ? 2.0 : 1.0;
        const double Radius = 7;
        commands.Add(new FillRectangle(x - Radius, y - Radius, 2 * Radius, weight, Palette.Ink));
        commands.Add(new FillRectangle(x - Radius, y + Radius - weight, 2 * Radius, weight, Palette.Ink));
        commands.Add(new FillRectangle(x - Radius, y - Radius, weight, 2 * Radius, Palette.Ink));
        commands.Add(new FillRectangle(x + Radius - weight, y - Radius, weight, 2 * Radius, Palette.Ink));
    }

    private void DrawDocument(List<DrawCommand> commands, WindowLayout layout)
    {
        var sheet = layout.DocumentSheet;
        commands.Add(new FillRectangle(sheet.X - 1, sheet.Y - 1, sheet.Width + 2, sheet.Height + 2, Palette.Rule));
        commands.Add(new FillRectangle(sheet.X, sheet.Y, sheet.Width, sheet.Height, Palette.Sheet));
        if (shown is not { } document) return;
        commands.Add(new DrawText(layout.DocumentName.X, layout.DocumentName.Y, $"Document {Sentences.Count(document.Index + 1)}", 13, Palette.Ink2));
        var marks = document.Marks.IsDefault ? ReadOnlySpan<float>.Empty : document.Marks.AsSpan();
        var view = DocumentView.Lay(texts[document.Index], document.Kinds.AsSpan(), marks);
        // The text scrolls so that its first match stands three rows under the top, as the page scrolls it.
        var first = view.FirstMatchRow < 0 ? 0 : Math.Max(0, Math.Min(view.FirstMatchRow - 3, view.Rows - layout.DocumentRows));
        var grid = layout.DocumentText;
        foreach (var span in view.Marks)
        {
            var row = span.Row - first;
            if (row < 0 || row >= layout.DocumentRows) continue;
            var (x, y, width) = (grid.X + (span.Column * grid.CellWidth), grid.Y + (row * grid.LineHeight), span.Width * grid.CellWidth);
            commands.Add(new FillRectangle(x, y, width, grid.LineHeight, span.Match == 1 ? Palette.WashLight : Palette.WashDeep));
            commands.Add(new FillRectangle(x, y + grid.LineHeight - 1, width, 1, Palette.Copper));
        }
        var glyphs = view.Glyphs
            .Where(glyph => glyph.Row >= first && glyph.Row - first < layout.DocumentRows)
            .Select(glyph => glyph with { Row = glyph.Row - first });
        commands.Add(new DrawGlyphs(grid.X, grid.Y, grid.CellWidth, grid.LineHeight, grid.Size, [.. glyphs]));
    }

    private static int FirstMatch(SearchResult.Searched result)
    {
        var counts = result.MatchCounts.AsSpan(0, result.DocumentsDone);
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] > 0) return index;
        }
        return -1;
    }

    private IReadOnlyList<int> CodePointsOfTexts()
    {
        var seen = new HashSet<int>(SentenceCodePoints);
        foreach (var text in texts)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (rune.Value > 126) seen.Add(rune.Value);
            }
        }
        return [.. seen.Order()];
    }
}
