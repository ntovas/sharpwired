// Handwired source: src/json-search/demo-text.ts, assets/tool.css and assets/json-search.css. In demo-text.ts,
// documentRuns and runClassName give a document as runs of one display kind and one match. The two washes go in
// turn from match to match. The file tool.css gives the color of each display kind. The file json-search.css gives
// white-space: pre-wrap and tab-size: 2 of the document text. The page lets the browser lay out the runs.
// The window has no font metrics, so it puts each code point in the cells of a fixed grid of 80 columns. A line
// feed starts a new row, and a tab goes to the next stop of 2 columns. A code point of East Asian width "wide" or
// "fullwidth" takes two cells. A code point that does not fit the row starts the next row.

using System.Collections.Immutable;
using Sharpwired.Raylib;

namespace JsonSearch;

/// <summary>
/// The cells under one part of a match on one row: Width cells from (Column, Row). Match is 1 for the light wash and
/// 2 for the deep wash, in turn from match to match.
/// </summary>
public readonly record struct MarkSpan(int Column, int Row, int Width, int Match);

/// <summary>
/// A document on the glyph grid: one glyph for each code point that is not whitespace, and the mark spans. It also
/// holds the row count and the row of the first match (-1 with no match).
/// </summary>
public sealed record DocumentLayout(ImmutableArray<Glyph> Glyphs, ImmutableArray<MarkSpan> Marks, int Rows, int FirstMatchRow);

/// <summary>The layout of a document on the glyph grid of the window.</summary>
public static class DocumentView
{
    /// <summary>The columns of the grid.</summary>
    public const int Columns = 80;

    /// <summary>The distance between two tab stops, in columns (tab-size: 2).</summary>
    public const int TabSize = 2;

    // The ranges of East Asian width W and F of Unicode EastAsianWidth.txt that the documents and the patterns can
    // hold. They are Hangul Jamo, the CJK blocks, Hangul syllables, the compatibility forms and the fullwidth forms.
    // They are also the emoji blocks and the CJK planes. It is not the full table.
    private static readonly (int Low, int High)[] WideRanges =
    [
        (0x1100, 0x115f), (0x231a, 0x231b), (0x2329, 0x232a), (0x23e9, 0x23ec), (0x23f0, 0x23f0), (0x23f3, 0x23f3),
        (0x25fd, 0x25fe), (0x2614, 0x2615), (0x2648, 0x2653), (0x267f, 0x267f), (0x2693, 0x2693), (0x26a1, 0x26a1),
        (0x26aa, 0x26ab), (0x26bd, 0x26be), (0x26c4, 0x26c5), (0x26ce, 0x26ce), (0x26d4, 0x26d4), (0x26ea, 0x26ea),
        (0x26f2, 0x26f3), (0x26f5, 0x26f5), (0x26fa, 0x26fa), (0x26fd, 0x26fd), (0x2705, 0x2705), (0x270a, 0x270b),
        (0x2728, 0x2728), (0x274c, 0x274c), (0x274e, 0x274e), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
        (0x27b0, 0x27b0), (0x27bf, 0x27bf), (0x2b1b, 0x2b1c), (0x2b50, 0x2b50), (0x2b55, 0x2b55), (0x2e80, 0x303e),
        (0x3041, 0x33ff), (0x3400, 0x4dbf), (0x4e00, 0x9fff), (0xa000, 0xa4cf), (0xa960, 0xa97f), (0xac00, 0xd7a3),
        (0xf900, 0xfaff), (0xfe10, 0xfe19), (0xfe30, 0xfe6f), (0xff00, 0xff60), (0xffe0, 0xffe6), (0x16fe0, 0x18aff),
        (0x1b000, 0x1b2ff), (0x1f004, 0x1f004), (0x1f0cf, 0x1f0cf), (0x1f18e, 0x1f18e), (0x1f191, 0x1f19a),
        (0x1f200, 0x1f251), (0x1f300, 0x1f64f), (0x1f680, 0x1f6ff), (0x1f900, 0x1f9ff), (0x1fa70, 0x1faff),
        (0x20000, 0x2fffd), (0x30000, 0x3fffd),
    ];

    /// <summary>The cells of a code point: 2 for East Asian width "wide" or "fullwidth", else 1.</summary>
    public static int CellWidthOf(int codePoint)
    {
        int low = 0, high = WideRanges.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (codePoint < WideRanges[middle].Low) high = middle - 1;
            else if (codePoint > WideRanges[middle].High) low = middle + 1;
            else return 2;
        }
        return 1;
    }

    /// <summary>
    /// The color of a display kind (tool.css): punctuation in ink-3, strings green, escapes ochre, numbers indigo,
    /// literals plum, errors red, and keys in ink. Whitespace and past end have the ink of the document text.
    /// </summary>
    public static Rgba ColorOf(DisplayKind kind) => kind switch
    {
        DisplayKind.Punctuation => Palette.Ink3,
        DisplayKind.String => new Rgba(0x2e, 0x6a, 0x4c, 255),
        DisplayKind.Escape => new Rgba(0x8a, 0x64, 0x12, 255),
        DisplayKind.Number => new Rgba(0x33, 0x58, 0x9e, 255),
        DisplayKind.Literal => new Rgba(0x7a, 0x33, 0x55, 255),
        DisplayKind.Error => Palette.Red,
        _ => Palette.Ink,
    };

    /// <summary>
    /// Lay out the text on a grid of the given columns. Kinds and marks hold one value for each code point position;
    /// a position past their end has the kind PastEnd and no mark. Empty marks are a document with no search.
    /// </summary>
    /// <exception cref="ArgumentException">The column count is less than 2.</exception>
    public static DocumentLayout Lay(string text, ReadOnlySpan<byte> kinds, ReadOnlySpan<float> marks, int columns = Columns)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (columns < 2) throw new ArgumentException($"the grid needs at least 2 columns, got {columns}", nameof(columns));
        var glyphs = ImmutableArray.CreateBuilder<Glyph>();
        var spans = new List<MarkSpan>();
        int column = 0, row = 0, position = 0, matchIndex = 0, firstMatchRow = -1;
        foreach (var rune in text.EnumerateRunes())
        {
            var codePoint = rune.Value;
            var mark = position < marks.Length ? marks[position] : 0;
            if (mark == 2) matchIndex++;
            var match = mark == 0 ? 0 : matchIndex % 2 == 1 ? 1 : 2;
            var kind = position < kinds.Length ? (DisplayKind)kinds[position] : DisplayKind.PastEnd;
            position++;
            if (codePoint == '\n')
            {
                row++;
                column = 0;
                continue;
            }
            var width = codePoint == '\t' ? TabSize - (column % TabSize) : CellWidthOf(codePoint);
            if (column + width > columns)
            {
                row++;
                column = 0;
                if (codePoint == '\t') width = TabSize;
            }
            if (codePoint is not (' ' or '\t' or '\r')) glyphs.Add(new Glyph(codePoint, column, row, ColorOf(kind)));
            if (match != 0)
            {
                if (firstMatchRow < 0) firstMatchRow = row;
                var last = spans.Count - 1;
                if (last >= 0 && spans[last].Row == row && spans[last].Column + spans[last].Width == column && spans[last].Match == match && mark != 2)
                {
                    spans[last] = spans[last] with { Width = spans[last].Width + width };
                }
                else
                {
                    spans.Add(new MarkSpan(column, row, width, match));
                }
            }
            column += width;
        }
        return new DocumentLayout(glyphs.ToImmutable(), [.. spans], row + 1, firstMatchRow);
    }
}
