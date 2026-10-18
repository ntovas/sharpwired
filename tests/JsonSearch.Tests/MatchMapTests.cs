// Handwired source: tests/json-search/demo.test.ts ("the map of every document"). The tests cover the map of the
// window: the layout of the cells, the document under a point, and the nearest document with a match. They also
// cover the picked document, the steps from match to match, and the pixels of the cells. The tests use the values
// of demo.test.ts.

namespace JsonSearch.Tests;

public sealed class MatchMapTests
{
    private static readonly CellColors Colors = new(22, 11, 5);

    [Fact]
    public void Each_document_has_its_own_cell_inside_the_image_at_each_width_and_scale()
    {
        foreach (var (widthCss, scale, documentCount) in new[] { (630, 2, 100_000), (630, 1, 100_000), (630, 3, 100_000), (900, 2, 65_000), (324, 3, 20_000), (324, 2, 20_000), (630, 2, 500), (200, 1, 7) })
        {
            var layout = MatchMap.Layout(widthCss, scale, documentCount);
            Assert.Equal(widthCss * scale, layout.Width);
            Assert.True(layout.Columns * layout.Rows >= documentCount);
            Assert.True(layout.Columns * (layout.Rows - 1) < documentCount);
            var owner = new int[layout.Width * layout.Height];
            Array.Fill(owner, -1);
            for (var index = 0; index < documentCount; index++)
            {
                var x = index % layout.Columns * layout.Pitch;
                var y = index / layout.Columns * layout.Pitch;
                Assert.True(x + layout.Cell <= layout.Width);
                Assert.True(y + layout.Cell <= layout.Height);
                for (var row = 0; row < layout.Cell; row++)
                {
                    for (var column = 0; column < layout.Cell; column++)
                    {
                        var pixel = ((y + row) * layout.Width) + x + column;
                        Assert.Equal(-1, owner[pixel]);
                        owner[pixel] = index;
                    }
                }
                var center = MatchMap.CellCenter(layout, index);
                Assert.Equal(index, MatchMap.CellAt(layout, center.X, center.Y));
                Assert.Equal(index, MatchMap.CellAt(layout, (double)x / scale, (double)y / scale));
            }
            Assert.True(layout.Cell >= layout.Pitch - layout.Cell);
        }
    }

    [Fact]
    public void A_point_over_no_cell_gives_minus_1()
    {
        var layout = MatchMap.Layout(630, 2, 100_000);
        var lastRowCount = 100_000 - (layout.Columns * (layout.Rows - 1));
        Assert.InRange(lastRowCount, 1, layout.Columns - 1);
        var lastRowY = ((layout.Rows - 1) * layout.Pitch / 2.0) + 0.25;
        Assert.Equal(99_999, MatchMap.CellAt(layout, ((lastRowCount - 1) * layout.Pitch / 2.0) + 0.25, lastRowY));
        Assert.Equal(-1, MatchMap.CellAt(layout, (lastRowCount * layout.Pitch / 2.0) + 0.25, lastRowY));
        Assert.Equal(-1, MatchMap.CellAt(layout, -3, 40));
        Assert.Equal(-1, MatchMap.CellAt(layout, 40, -3));
        Assert.Equal(-1, MatchMap.CellAt(layout, 40, (layout.Height / 2.0) + 4));
    }

    [Fact]
    public void The_layout_uses_the_smallest_cell_with_a_gap_for_100000_documents_and_larger_cells_for_fewer()
    {
        (int, int) PitchAndCell(double widthCss, int scale, int documentCount)
        {
            var layout = MatchMap.Layout(widthCss, scale, documentCount);
            return (layout.Pitch, layout.Cell);
        }
        Assert.Equal((2, 1), PitchAndCell(630, 1, 100_000));
        Assert.Equal((3, 2), PitchAndCell(630, 2, 100_000));
        Assert.Equal((4, 3), PitchAndCell(630, 3, 100_000));
        Assert.Equal((4, 3), PitchAndCell(630, 2, 65_000));
        Assert.Equal((7, 6), PitchAndCell(324, 3, 20_000));
        Assert.Equal((24, 19), PitchAndCell(630, 2, 500));
        // The heights of json-search.css for the map of a full page: 358 CSS px at scale 2, 635 at scale 1.
        Assert.Equal(358, (int)Math.Ceiling(MatchMap.Layout(630, 2, 100_000).Height / 2.0));
        Assert.Equal(635, MatchMap.Layout(630, 1, 100_000).Height);
    }

    [Fact]
    public void The_pointer_picks_the_nearest_document_with_a_match_among_the_documents_searched_so_far()
    {
        var layout = MatchMap.Layout(100, 1, 1600);
        Assert.Equal((5, 20), (layout.Pitch, layout.Columns));
        var counts = new float[1600];
        int At(int column, int row) => (row * layout.Columns) + column;
        counts[At(3, 2)] = 1;
        counts[At(15, 9)] = 4;
        counts[At(16, 9)] = 1;
        (double X, double Y) Center(int index) => MatchMap.CellCenter(layout, index);
        Assert.Equal(At(3, 2), MatchMap.NearestMatch(layout, counts, 1600, Center(At(3, 2)).X, Center(At(3, 2)).Y));
        Assert.Equal(At(3, 2), MatchMap.NearestMatch(layout, counts, 1600, Center(At(0, 0)).X, Center(At(0, 0)).Y));
        Assert.Equal(At(16, 9), MatchMap.NearestMatch(layout, counts, 1600, Center(At(19, 79)).X, Center(At(19, 79)).Y));
        Assert.Equal(At(15, 9), MatchMap.NearestMatch(layout, counts, 1600, Center(At(14, 12)).X, Center(At(14, 12)).Y));
        Assert.Equal(At(15, 9), MatchMap.NearestMatch(layout, counts, 1600, (Center(At(15, 9)).X + Center(At(16, 9)).X) / 2, Center(At(15, 9)).Y));
        Assert.Equal(At(3, 2), MatchMap.NearestMatch(layout, counts, 100, Center(At(19, 79)).X, Center(At(19, 79)).Y));
        Assert.Equal(-1, MatchMap.NearestMatch(layout, counts, 40, Center(At(3, 2)).X, Center(At(3, 2)).Y));
        Assert.Equal(-1, MatchMap.NearestMatch(layout, new float[1600], 1600, 50, 50));
    }

    [Fact]
    public void The_pointer_picks_the_document_under_it_where_the_search_has_not_been_and_when_no_document_has_a_match()
    {
        var layout = MatchMap.Layout(100, 1, 1600);
        var counts = new float[1600];
        int At(int column, int row) => (row * layout.Columns) + column;
        counts[At(3, 2)] = 1;
        counts[At(15, 9)] = 4;
        int Picked(int searched, int index) =>
            MatchMap.PickedDocument(layout, counts, searched, MatchMap.CellCenter(layout, index).X, MatchMap.CellCenter(layout, index).Y);
        Assert.Equal(At(3, 2), Picked(1600, At(0, 0)));
        Assert.Equal(At(15, 9), Picked(1600, At(19, 79)));
        Assert.Equal(At(3, 2), Picked(100, At(10, 4)));
        Assert.Equal(At(0, 5), Picked(100, At(0, 5)));
        Assert.Equal(At(19, 79), Picked(100, At(19, 79)));
        Assert.Equal(At(7, 3), Picked(0, At(7, 3)));
        var center = MatchMap.CellCenter(layout, At(7, 3));
        Assert.Equal(At(7, 3), MatchMap.PickedDocument(layout, new float[1600], 1600, center.X, center.Y));
        Assert.Equal(At(3, 2), MatchMap.PickedDocument(layout, counts, 1600, -5, -5));
        Assert.Equal(-1, MatchMap.PickedDocument(layout, new float[1600], 1600, -5, -5));
    }

    [Fact]
    public void The_steps_go_to_the_next_and_the_previous_document_with_a_match_around_the_ends()
    {
        float[] counts = [0, 2, 0, 1, 0, 0, 3];
        Assert.Equal(3, MatchMap.NextMatch(counts, 7, 1, 1));
        Assert.Equal(6, MatchMap.NextMatch(counts, 7, 3, 1));
        Assert.Equal(1, MatchMap.NextMatch(counts, 7, 6, 1));
        Assert.Equal(6, MatchMap.NextMatch(counts, 7, 1, -1));
        Assert.Equal(6, MatchMap.NextMatch(counts, 7, 0, -1));
        Assert.Equal(3, MatchMap.NextMatch(counts, 7, 4, -1));
        Assert.Equal(2, MatchMap.NextMatch([0, 0, 4], 3, 2, 1));
        Assert.Equal(-1, MatchMap.NextMatch(new float[5], 5, 2, 1));
        Assert.Equal(1, MatchMap.NextMatch(counts, 4, 3, 1));
        Assert.Equal(3, MatchMap.NextMatch(counts, 4, 6, 1));
        Assert.Equal(-1, MatchMap.NextMatch(counts, 0, 2, 1));
        Assert.Throws<ArgumentException>(() => MatchMap.NextMatch(counts, 7, 1, 2));
    }

    [Fact]
    public void Paint_gives_a_cell_without_a_match_a_match_with_its_gap_a_waiting_cell_and_nothing_for_a_document_not_loaded()
    {
        foreach (var scale in new[] { 1, 2, 3 })
        {
            var layout = MatchMap.Layout(36, scale, 9);
            var gap = layout.Pitch - layout.Cell;
            Assert.Equal((3, 3), (layout.Columns, layout.Rows));
            var pixels = new uint[layout.Width * layout.Height];
            uint ColorOf(int index) => pixels[(index / layout.Columns * layout.Pitch * layout.Width) + (index % layout.Columns * layout.Pitch)];
            uint CornerOf(int index) => pixels[(((index / layout.Columns * layout.Pitch) - 1) * layout.Width) + (index % layout.Columns * layout.Pitch) - 1];
            int Painted(uint color) => pixels.Count(pixel => pixel == color);
            var every = Enumerable.Range(0, 9).ToArray();

            MatchMap.PaintCells(pixels, layout, 7, 5, [0, 2, 0, 0, 1], Colors, Colors.Waiting);
            Assert.Equal([11u, 22, 11, 11, 22, 5, 5, 0, 0], every.Select(ColorOf));
            Assert.Equal(22u, CornerOf(4));
            var markSide = layout.Cell + (2 * gap);
            Assert.Equal((markSide * markSide) + (markSide * layout.Cell), Painted(22));
            Assert.Equal((3 * layout.Cell * layout.Cell, 2 * layout.Cell * layout.Cell), (Painted(11), Painted(5)));
            Assert.Equal(pixels.Length - Painted(22) - Painted(11) - Painted(5), Painted(0));

            MatchMap.PaintCells(pixels, layout, 9, 0, [], Colors, Colors.Plain);
            Assert.All(every.Select(ColorOf), color => Assert.Equal(11u, color));
            Assert.Equal((9 * layout.Cell * layout.Cell, 0, 0u), (Painted(11), Painted(22), CornerOf(4)));
        }

        var smallest = new MapLayout(1, 9, 5, 5, 2, 1, 3, 3);
        var small = new uint[25];
        MatchMap.PaintCells(small, smallest, 9, 9, [0, 0, 0, 0, 1, 0, 0, 0, 3], Colors, Colors.Waiting);
        Assert.Equal(
            [
                11u, 0, 11, 0, 11,
                0, 0, 0, 0, 0,
                11, 0, 22, 22, 11,
                0, 0, 22, 22, 0,
                11, 0, 11, 0, 22,
            ],
            small);
    }

    // The plan asks for paint at 20 and 100,000 documents: each cell has the color of its count.
    [Fact]
    public void Paint_colors_each_cell_of_20_and_of_100000_documents_by_its_count()
    {
        foreach (var documentCount in new[] { 20, 100_000 })
        {
            var layout = MatchMap.Layout(600, 2, documentCount);
            var counts = new float[documentCount];
            for (var index = 0; index < documentCount; index += 7) counts[index] = 1;
            var pixels = new uint[layout.Width * layout.Height];
            MatchMap.PaintCells(pixels, layout, documentCount, documentCount, counts, Colors, Colors.Waiting);
            for (var index = 0; index < documentCount; index++)
            {
                var x = index % layout.Columns * layout.Pitch;
                var y = index / layout.Columns * layout.Pitch;
                var cellIsMatch = true;
                var cellIsPlain = true;
                for (var row = 0; row < layout.Cell; row++)
                {
                    for (var column = 0; column < layout.Cell; column++)
                    {
                        var pixel = pixels[((y + row) * layout.Width) + x + column];
                        cellIsMatch &= pixel == Colors.Match;
                        cellIsPlain &= pixel == Colors.Plain;
                    }
                }
                // A mark covers only its cell and the gap around it, so each other cell keeps its own color.
                Assert.True(counts[index] > 0 ? cellIsMatch : cellIsPlain, $"document {index} of {documentCount}");
            }
            Assert.Equal(0, pixels.Count(pixel => pixel == Colors.Waiting));
        }
    }
}
