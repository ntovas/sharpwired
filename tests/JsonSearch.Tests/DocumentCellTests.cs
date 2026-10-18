// Handwired source: tests/json-search/json-search.test.ts ("a document's cells"). DocumentCells.Write gives the
// first 512 code points, -1 after the text, and a surrogate pair as one code point. It gives a lone surrogate as
// its own code point, and the length of the text in code points, at most 1,048,576.

using JsonSearch;

namespace JsonSearch.Tests;

public sealed class DocumentCellTests
{
    private const int Capacity = Kinds.Capacity;

    [Fact]
    public void Write_gives_the_first_512_code_points_minus_one_after_them_and_the_length()
    {
        var cells = new float[DocumentCells.CellsPerDocument];
        // The test texts, an emoji, a lone high surrogate and a lone low one, and 300 emoji, which are 600 UTF-16 code units.
        foreach (var text in Corpus.All.Concat(["😀", "a\ud83d", "\ude00b", "\"" + string.Concat(Enumerable.Repeat("😀", 300)) + "\""]))
        {
            DocumentCells.Write(cells, text);
            var codePoints = CodePoints.Of(text);
            var expected = Enumerable.Range(0, Capacity).Select(position => position < codePoints.Length ? (float)codePoints[position] : -1f).Append(codePoints.Length).ToArray();
            Assert.True(expected.SequenceEqual(cells), $"the cells of {text} differ");
        }
        Assert.Equal([0x22, 0x1f600, 0x1f600, 302], [cells[0], cells[1], cells[2], cells[Capacity]]);
        DocumentCells.Write(cells, "a\ud83d");
        Assert.Equal([0x61, 0xd83d, -1], cells.Take(3));
        DocumentCells.Write(cells, "\ude00b");
        Assert.Equal([0xde00, 0x62, -1], cells.Take(3));
    }

    [Fact]
    public void A_text_of_more_than_1048576_code_points_writes_that_length()
    {
        var cells = new float[DocumentCells.CellsPerDocument];
        DocumentCells.Write(cells, new string('a', 1_048_600));
        Assert.Equal([0x61, 0x61, 1_048_576], [cells[0], cells[Capacity - 1], cells[Capacity]]);
    }

    [Fact]
    public void Write_all_writes_513_cells_for_each_text_in_order()
    {
        string[] texts = ["[1]", "", "\"é\""];
        var cells = DocumentCells.WriteAll(texts);
        Assert.Equal(3 * DocumentCells.CellsPerDocument, cells.Length);
        var one = new float[DocumentCells.CellsPerDocument];
        for (var row = 0; row < texts.Length; row++)
        {
            DocumentCells.Write(one, texts[row]);
            Assert.Equal(one, cells.AsSpan(row * DocumentCells.CellsPerDocument, DocumentCells.CellsPerDocument).ToArray());
        }
    }

    [Fact]
    public void A_span_of_the_wrong_length_throws()
    {
        Assert.Throws<ArgumentException>(() => DocumentCells.Write(new float[DocumentCells.CellsPerDocument - 1], "1"));
        Assert.Throws<ArgumentNullException>(() => DocumentCells.Write(new float[DocumentCells.CellsPerDocument], null!));
        Assert.Equal((512, 513, 1_048_576), (DocumentCells.Capacity, DocumentCells.CellsPerDocument, DocumentCells.LengthLimit));
    }
}
