// Handwired source: tests/json-search/demo.test.ts ("splits a document into runs by display kind and match") and the
// CSS of the document panel (assets/tool.css: the color of each display kind; assets/json-search.css: tab-size 2,
// the wash of a match, light and deep in turn). The window draws the document on a fixed grid of 80 columns:
// DocumentView gives the cell of each code point, its color, and the mark spans under the matches.

using Sharpwired.Raylib;

namespace JsonSearch.Tests;

public sealed class DocumentViewTests
{
    private const byte Whitespace = (byte)DisplayKind.Whitespace;
    private const byte Punctuation = (byte)DisplayKind.Punctuation;
    private const byte String = (byte)DisplayKind.String;
    private const byte Number = (byte)DisplayKind.Number;
    private const byte Key = (byte)DisplayKind.Key;

    [Fact]
    public void The_glyphs_take_rows_and_columns_with_line_feeds_tabs_a_wide_code_point_and_a_surrogate_pair()
    {
        // {\n\t"東":"😀a"\n}: a line feed starts a row, a tab goes to the next stop of 2, 東 takes two cells, and 😀
        // is one code point (a surrogate pair) of East Asian width "wide", so it also takes two cells.
        const string text = "{\n\t\"東\":\"😀a\"\n}";
        byte[] kinds = [Punctuation, Whitespace, Whitespace, Key, Key, Key, Punctuation, String, String, String, String, Whitespace, Punctuation];
        var layout = DocumentView.Lay(text, kinds, []);
        Assert.Equal(
            [('{', 0, 0), ('"', 2, 1), (0x6771, 3, 1), ('"', 5, 1), (':', 6, 1), ('"', 7, 1), (0x1f600, 8, 1), ('a', 10, 1), ('"', 11, 1), ('}', 0, 2)],
            layout.Glyphs.Select(glyph => (glyph.CodePoint, glyph.Column, glyph.Row)));
        Assert.Equal(3, layout.Rows);
        Assert.Empty(layout.Marks.ToArray());
        Assert.Equal(-1, layout.FirstMatchRow);
    }

    [Fact]
    public void A_row_wraps_at_80_columns_and_a_wide_code_point_that_does_not_fit_starts_the_next_row()
    {
        var text = new string('a', 79) + "東" + "b";
        var kinds = Enumerable.Repeat(String, 81).ToArray();
        var layout = DocumentView.Lay(text, kinds, []);
        Assert.Equal((78, 0), (layout.Glyphs[78].Column, layout.Glyphs[78].Row));
        Assert.Equal((0x6771, 0, 1), (layout.Glyphs[79].CodePoint, layout.Glyphs[79].Column, layout.Glyphs[79].Row));
        Assert.Equal(('b', 2, 1), ((char)layout.Glyphs[80].CodePoint, layout.Glyphs[80].Column, layout.Glyphs[80].Row));
        Assert.Equal(2, layout.Rows);
        Assert.Equal(2, DocumentView.CellWidthOf(0x6771));
        Assert.Equal(2, DocumentView.CellWidthOf(0x7f8e));
        Assert.Equal(2, DocumentView.CellWidthOf(0x1f600));
        Assert.Equal(1, DocumentView.CellWidthOf(0xf8));
        Assert.Equal(1, DocumentView.CellWidthOf('a'));
    }

    [Fact]
    public void Each_display_kind_has_the_color_of_the_page()
    {
        Assert.Equal(new Rgba(0x72, 0x67, 0x5c, 255), DocumentView.ColorOf(DisplayKind.Punctuation));
        Assert.Equal(new Rgba(0x2e, 0x6a, 0x4c, 255), DocumentView.ColorOf(DisplayKind.String));
        Assert.Equal(new Rgba(0x8a, 0x64, 0x12, 255), DocumentView.ColorOf(DisplayKind.Escape));
        Assert.Equal(new Rgba(0x33, 0x58, 0x9e, 255), DocumentView.ColorOf(DisplayKind.Number));
        Assert.Equal(new Rgba(0x7a, 0x33, 0x55, 255), DocumentView.ColorOf(DisplayKind.Literal));
        Assert.Equal(new Rgba(0xb3, 0x26, 0x1e, 255), DocumentView.ColorOf(DisplayKind.Error));
        Assert.Equal(new Rgba(0x1e, 0x1a, 0x16, 255), DocumentView.ColorOf(DisplayKind.Key));
        Assert.Equal(new Rgba(0x1e, 0x1a, 0x16, 255), DocumentView.ColorOf(DisplayKind.Whitespace));
        Assert.Equal(new Rgba(0x1e, 0x1a, 0x16, 255), DocumentView.ColorOf(DisplayKind.PastEnd));
        // The glyph of a code point has the color of its kind.
        var layout = DocumentView.Lay("[1]", [Punctuation, Number, Punctuation], []);
        Assert.Equal([DocumentView.ColorOf(DisplayKind.Punctuation), DocumentView.ColorOf(DisplayKind.Number), DocumentView.ColorOf(DisplayKind.Punctuation)], layout.Glyphs.Select(glyph => glyph.Color));
    }

    [Fact]
    public void The_marks_of_neighbor_matches_alternate_between_the_light_and_the_deep_wash()
    {
        // demo.test.ts: {"id":12,"the"} with a search for the|id over everything gives two matches.
        const string sample = "{\"id\":12,\"the\"}";
        byte[] kinds = [Punctuation, Key, Key, Key, Key, Punctuation, Number, Number, Punctuation, String, String, String, String, String, Punctuation];
        float[] marks = [0, 0, 2, 1, 0, 0, 0, 0, 0, 0, 2, 1, 1, 0, 0];
        var layout = DocumentView.Lay(sample, kinds, marks);
        Assert.Equal([new MarkSpan(2, 0, 2, 1), new MarkSpan(10, 0, 3, 2)], layout.Marks.ToArray());
        Assert.Equal(0, layout.FirstMatchRow);

        // Two matches that touch are two spans, since each starts with a 2.
        var touching = DocumentView.Lay("abab", [String, String, String, String], [2, 1, 2, 1]);
        Assert.Equal([new MarkSpan(0, 0, 2, 1), new MarkSpan(2, 0, 2, 2)], touching.Marks.ToArray());

        // A code point past U+FFFF is one position: the mark of position 2 is under the a, after the two cells of 😀.
        var pair = DocumentView.Lay("\"😀a\"", [String, String, String, String], [0, 0, 2, 0]);
        Assert.Equal([new MarkSpan(3, 0, 1, 1)], pair.Marks.ToArray());

        // A match that wraps has one span on each row, and a line feed in a match has no cell.
        var wrapped = DocumentView.Lay(new string('x', 79) + "yz\nw", [.. Enumerable.Repeat(String, 83)], [.. Enumerable.Repeat(0f, 78), 2, 1, 1, 1, 1]);
        Assert.Equal([new MarkSpan(78, 0, 2, 1), new MarkSpan(0, 1, 1, 1), new MarkSpan(0, 2, 1, 1)], wrapped.Marks.ToArray());
        Assert.Equal(0, wrapped.FirstMatchRow);
    }
}
