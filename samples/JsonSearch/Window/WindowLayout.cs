// Handwired source: src/json-search/assets/json-search.html and json-search.css (the order of the parts: the pattern
// field with the two segment groups at its right, the examples under it, the line of numbers, the map on its sheet
// at the left and the document panel at the right). The page lets the browser place the parts. The window places
// them itself, in screen pixels, from the size of the window: a window of 1,280 × 860 gives a field of 640 × 36 px
// and a map of 600 px. The glyph grids of the field, the examples and the document have a fixed cell width, so the
// layout needs no font metrics.

using System.Collections.Immutable;

namespace JsonSearch;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    /// <summary>True when the point is inside the rectangle (the right and the bottom edges are outside).</summary>
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>A glyph grid: the top left corner of its first cell, the cell width, the line height and the glyph size.</summary>
public readonly record struct GridPlace(double X, double Y, double CellWidth, double LineHeight, double Size);

/// <summary>The places of the parts of the window for one window size and pixel scale.</summary>
public sealed record WindowLayout(
    Box Field,
    GridPlace FieldText,
    ImmutableArray<Box> Scopes,
    ImmutableArray<Box> Backends,
    Box TryLabel,
    ImmutableArray<Box> Examples,
    GridPlace ExampleText,
    Box Line,
    Box MapSheet,
    Box Map,
    MapLayout MapLayout,
    int MapPixelHeight,
    Box DocumentSheet,
    Box DocumentName,
    GridPlace DocumentText,
    int DocumentRows)
{
    /// <summary>The space between the window edge and the parts, and between two panels.</summary>
    public const double Margin = 24;

    /// <summary>The labels of the scope segments, in the order of SearchScope.</summary>
    public static readonly ImmutableArray<string> ScopeLabels = ["All", "Keys", "String values"];

    /// <summary>The labels of the backend segments, in the order of SearchBackend.</summary>
    public static readonly ImmutableArray<string> BackendLabels = ["GPU", "CPU"];

    private const double FieldWidth = 640;
    private const double FieldHeight = 36;
    private const double FieldPadding = 12;
    private const double FieldCell = 8.4;
    private const double LargestMapWidth = 600;
    private const double SmallestDocumentWidth = 560;
    private const double SheetPadding = 8;
    private const double PanelTop = 146;
    private const double NameHeight = 28;
    private const double DocumentLine = 16;
    private const double ExampleCell = 7.8;
    private static readonly double[] ScopeWidths = [56, 64, 120];
    private static readonly double[] BackendWidths = [64, 64];

    /// <summary>The layout of a window of width × height screen pixels with the pixel scale and the documents.</summary>
    public static WindowLayout For(int width, int height, double pixelScale, int documentCount)
    {
        var scale = Math.Max(1, Math.Min(4, (int)Math.Round(pixelScale, MidpointRounding.AwayFromZero)));
        var fieldWidth = Math.Max(120, Math.Min(FieldWidth, width - (2 * Margin)));
        var field = new Box(Margin, Margin, fieldWidth, FieldHeight);
        var fieldText = new GridPlace(field.X + FieldPadding, field.Y, FieldCell, FieldHeight, 15);
        var scopes = Segments(field.X + field.Width + 16, field.Y, ScopeWidths);
        var backends = Segments(scopes[^1].X + scopes[^1].Width + 16, field.Y, BackendWidths);

        var examplesTop = field.Y + field.Height + 12;
        var tryLabel = new Box(Margin, examplesTop, 36, 26);
        var examples = ImmutableArray.CreateBuilder<Box>();
        var exampleX = tryLabel.X + tryLabel.Width;
        foreach (var (pattern, _) in SearchBench.Examples)
        {
            var exampleWidth = (pattern.EnumerateRunes().Count() * ExampleCell) + 16;
            examples.Add(new Box(exampleX, examplesTop, exampleWidth, 26));
            exampleX += exampleWidth + 12;
        }
        var exampleText = new GridPlace(8, 0, ExampleCell, 26, 13);
        var line = new Box(Margin, examplesTop + 26 + 10, Math.Max(1, width - (2 * Margin)), 20);

        // The map takes at most 600 px, and leaves the document panel at least 560 px. The image keeps only the rows
        // that fit the window, so the map stays inside a small window.
        var mapWidth = Math.Max(120, Math.Min(LargestMapWidth, width - (3 * Margin) - (2 * SheetPadding) - SmallestDocumentWidth));
        var mapLayout = MatchMap.Layout(mapWidth, scale, documentCount);
        var room = Math.Max(1, (int)Math.Floor((height - PanelTop - Margin - (2 * SheetPadding)) * scale));
        var mapPixelHeight = Math.Min(mapLayout.Height, room);
        var map = new Box(Margin + SheetPadding, PanelTop + SheetPadding, mapLayout.Width / (double)scale, mapPixelHeight / (double)scale);
        var mapSheet = new Box(Margin, PanelTop, map.Width + (2 * SheetPadding), map.Height + (2 * SheetPadding));
        var documentX = mapSheet.X + mapSheet.Width + Margin;
        var documentSheet = new Box(documentX, PanelTop, Math.Max(40, width - documentX - Margin), Math.Max(60, height - PanelTop - Margin));
        var name = new Box(documentSheet.X + 12, documentSheet.Y + 8, documentSheet.Width - 24, 16);
        var cell = Math.Max(1, (documentSheet.Width - 24) / DocumentView.Columns);
        var documentText = new GridPlace(documentSheet.X + 12, documentSheet.Y + NameHeight + 8, cell, DocumentLine, Math.Min(13, Math.Max(4, cell * 1.75)));
        var rows = Math.Max(1, (int)Math.Floor((documentSheet.Height - NameHeight - 16) / DocumentLine));
        return new WindowLayout(field, fieldText, scopes, backends, tryLabel, examples.ToImmutable(), exampleText, line, mapSheet, map, mapLayout, mapPixelHeight, documentSheet, name, documentText, rows);
    }

    private static ImmutableArray<Box> Segments(double x, double y, double[] widths)
    {
        var boxes = ImmutableArray.CreateBuilder<Box>();
        foreach (var segmentWidth in widths)
        {
            boxes.Add(new Box(x, y, segmentWidth, FieldHeight));
            x += segmentWidth;
        }
        return boxes.ToImmutable();
    }
}
