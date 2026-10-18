// Handwired source: src/json-search/demo-map.ts (mapLayout, cellAt, cellCenterCss, nearestMatch, pickedDocument,
// nextMatch, paintCells). The map of the window: one small square cell for each document, in document order, row
// after row from the top left. This file is the arithmetic of the map, with no window. It gives the size of a
// cell, the document that a point picks, and the pixels of the cells.
// A length in screen pixels has the suffix Css in Handwired, and here a parameter in screen pixels says so. Each
// other length is a pixel of the image, which has Scale pixels for each screen pixel. Thus the edges of a cell
// fall on device pixels.

namespace JsonSearch;

/// <summary>
/// The layout of the map: the scale, the document count, the image size, and the distance from one cell to the
/// next (Pitch). It also holds the side of a cell, and the columns and the rows.
/// </summary>
public readonly record struct MapLayout(int Scale, int DocumentCount, int Width, int Height, int Pitch, int Cell, int Columns, int Rows);

/// <summary>
/// The colors of a cell, each a pixel word (DrawPixels.Word). The colors are for a document with a match, one
/// without, and one that a search in progress has not reached.
/// </summary>
public readonly record struct CellColors(uint Match, uint Plain, uint Waiting);

/// <summary>The functions of demo-map.ts.</summary>
public static class MatchMap
{
    // The cells take at most this height (screen pixels) when a larger cell still fits. With the smallest cell the
    // map can be higher, for example 100,000 documents at scale 1.
    private const int TallestCss = 420;
    private const int LargestPitchCss = 12;

    /// <summary>
    /// The largest cell with which the documents fit the height above. The cell is at least the smallest cell that
    /// still shows a gap to the next one. The smallest cell is 1 image pixel of each 2 at scale 1, and 2 of each 3
    /// at scale 2. At scale 3 it is 3 of each 4.
    /// </summary>
    /// <exception cref="ArgumentException">The scale is less than 1, or the document count is negative.</exception>
    public static MapLayout Layout(double widthCss, int scale, int documentCount)
    {
        if (scale < 1) throw new ArgumentException($"the scale must be at least 1, got {scale}", nameof(scale));
        if (documentCount < 0) throw new ArgumentException($"the document count must not be negative, got {documentCount}", nameof(documentCount));
        var width = Math.Max(1, (int)Math.Floor(widthCss * scale));
        var smallestPitch = scale + 1;
        MapLayout chosen = default;
        for (var pitch = LargestPitchCss * scale; pitch >= smallestPitch; pitch--)
        {
            var cell = pitch - GapOf(pitch);
            var columns = Math.Max(1, ((width - cell) / pitch) + 1);
            var rows = (documentCount + columns - 1) / columns;
            chosen = new MapLayout(scale, documentCount, width, ((rows - 1) * pitch) + cell, pitch, cell, columns, rows);
            if (chosen.Height <= TallestCss * scale) break;
        }
        return chosen;
    }

    /// <summary>
    /// The document whose cell a point (screen pixels from the top left of the map) is over, or -1 where there is no
    /// cell. A cell answers for itself and for the gap to its right and below it.
    /// </summary>
    public static int CellAt(MapLayout layout, double xCss, double yCss)
    {
        var column = (long)Math.Floor(xCss * layout.Scale / layout.Pitch);
        var row = (long)Math.Floor(yCss * layout.Scale / layout.Pitch);
        if (column < 0 || column >= layout.Columns || row < 0) return -1;
        var index = (row * layout.Columns) + column;
        return index < layout.DocumentCount ? (int)index : -1;
    }

    /// <summary>The center of the cell of a document, in screen pixels from the top left of the map.</summary>
    public static (double X, double Y) CellCenter(MapLayout layout, int index) =>
        ((((index % layout.Columns) * layout.Pitch) + (layout.Cell / 2.0)) / layout.Scale,
         (((index / layout.Columns) * layout.Pitch) + (layout.Cell / 2.0)) / layout.Scale);

    /// <summary>
    /// Of the first searched documents, the document with a match whose cell is nearest to a point, or -1 when none
    /// has a match. Between two cells at the same distance, the first one.
    /// </summary>
    public static int NearestMatch(MapLayout layout, ReadOnlySpan<float> matchCounts, int searched, double xCss, double yCss)
    {
        // The point in cells, from the center of the first cell.
        var x = ((xCss * layout.Scale) - (layout.Cell / 2.0)) / layout.Pitch;
        var y = ((yCss * layout.Scale) - (layout.Cell / 2.0)) / layout.Pitch;
        var nearest = -1;
        var nearestDistance = double.PositiveInfinity;
        for (int index = 0, column = 0, row = 0; index < searched; index++)
        {
            if (matchCounts[index] > 0)
            {
                var distance = ((column - x) * (column - x)) + ((row - y) * (row - y));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = index;
                }
            }
            if (++column == layout.Columns)
            {
                column = 0;
                row++;
            }
        }
        return nearest;
    }

    /// <summary>
    /// The document that a point picks, or -1: the nearest one with a match. Where the search has not reached the
    /// document under the point, the point picks that document. Where no document has a match, the point picks the
    /// document under it.
    /// </summary>
    public static int PickedDocument(MapLayout layout, ReadOnlySpan<float> matchCounts, int searched, double xCss, double yCss)
    {
        var under = CellAt(layout, xCss, yCss);
        if (under >= searched) return under;
        var nearest = NearestMatch(layout, matchCounts, searched, xCss, yCss);
        return nearest >= 0 ? nearest : under;
    }

    /// <summary>
    /// The next document with a match after from, of the first searched documents, or the one before it with step -1,
    /// around the ends. -1 when none has a match. From is the answer only when it is the one document with a match.
    /// </summary>
    /// <exception cref="ArgumentException">The step is not 1 or -1.</exception>
    public static int NextMatch(ReadOnlySpan<float> matchCounts, int searched, int from, int step)
    {
        if (step is not (1 or -1)) throw new ArgumentException($"the step must be 1 or -1, got {step}", nameof(step));
        for (var distance = 1; distance <= searched; distance++)
        {
            var index = ((((long)from + ((long)step * distance)) % searched) + searched) % searched;
            if (matchCounts[(int)index] > 0) return (int)index;
        }
        return -1;
    }

    /// <summary>
    /// Write the pixels of the cells of the first loaded documents. Of those, the first searched documents have the
    /// color of their count, and the others have the color rest. A document with a match gets a mark larger than its
    /// cell. The mark covers the cell and the gap around it, at most twice the side of the cell. The gap belongs to no other cell, so
    /// each other cell keeps its color. The pixels of the last paint are cleared first.
    /// </summary>
    public static void PaintCells(Span<uint> pixels, MapLayout layout, int loaded, int searched, ReadOnlySpan<float> matchCounts, CellColors colors, uint rest)
    {
        var (cell, pitch, width, height, columns) = (layout.Cell, layout.Pitch, layout.Width, layout.Height, layout.Columns);
        var mark = Math.Min(cell + (2 * (pitch - cell)), 2 * cell);
        var before = (mark - cell) >> 1;
        pixels.Clear();
        for (int index = 0, column = 0, row = 0; index < loaded; index++)
        {
            var x = column * pitch;
            var y = row * pitch;
            if (index < searched && matchCounts[index] > 0)
            {
                var right = Math.Min(width, x - before + mark);
                var bottom = Math.Min(height, y - before + mark);
                var left = Math.Max(0, x - before);
                for (var pixelY = Math.Max(0, y - before); pixelY < bottom; pixelY++) pixels.Slice((pixelY * width) + left, right - left).Fill(colors.Match);
            }
            else
            {
                var color = index < searched ? colors.Plain : rest;
                for (var pixelY = y; pixelY < y + cell; pixelY++) pixels.Slice((pixelY * width) + x, cell).Fill(color);
            }
            if (++column == columns)
            {
                column = 0;
                row++;
            }
        }
    }

    private static int GapOf(int pitch) => Math.Max(1, (int)Math.Round(pitch / 5.0, MidpointRounding.AwayFromZero));
}
