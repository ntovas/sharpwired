// No Handwired source. These tests cover the draw commands of phase 6 task 8 that make no Raylib call. They
// cover the checks of DrawPixels and DrawGlyphs, the pixel word of a color, and the default font members of
// IWindowApp. They also cover the code points that the window loads, and the choice of a font for each code point.

using System.Collections.Immutable;

namespace Sharpwired.Raylib.Tests;

public class DrawCommandTests
{
    private static readonly Rgba Ink = new(0x1e, 0x1a, 0x16, 255);

    // No Handwired test. A DrawPixels command holds one pixel for each texture pixel.
    [Fact]
    public void Draw_pixels_needs_one_pixel_for_each_texture_pixel_and_a_positive_size_and_scale()
    {
        var pixels = ImmutableArray.Create(new uint[6]);
        var command = new DrawPixels(10, 20, 3, 2, pixels, 7);
        Assert.Equal(1.0, command.Scale);
        Assert.Equal(3.0, command.DrawnWidth);
        Assert.Equal(2.0, command.DrawnHeight);
        var scaled = new DrawPixels(10, 20, 3, 2, pixels, 7, 2);
        Assert.Equal(1.5, scaled.DrawnWidth);
        Assert.Equal(1.0, scaled.DrawnHeight);

        Assert.Throws<ArgumentException>(() => new DrawPixels(0, 0, 4, 2, pixels, 0));
        Assert.Throws<ArgumentException>(() => new DrawPixels(0, 0, 0, 2, [], 0));
        Assert.Throws<ArgumentException>(() => new DrawPixels(0, 0, 3, 2, default, 0));
        Assert.Throws<ArgumentException>(() => new DrawPixels(0, 0, 3, 2, pixels, 0, 0));
    }

    // No Handwired test. The pixel word of a color holds the bytes R, G, B, A in memory order, as a texture of
    // 8-bit channels reads them.
    [Fact]
    public void The_pixel_word_of_a_color_holds_red_green_blue_alpha_in_memory_order()
    {
        var word = DrawPixels.Word(new Rgba(0x11, 0x22, 0x33, 0x44));
        Assert.Equal(0x44332211u, word);
        Assert.Equal([0x11, 0x22, 0x33, 0x44], BitConverter.GetBytes(word).Select(value => (int)value));
    }

    // No Handwired test. A default glyph array reads as empty, and a cell needs a positive size.
    [Fact]
    public void Draw_glyphs_reads_a_default_array_as_empty_and_needs_a_positive_cell()
    {
        var glyphs = new DrawGlyphs(0, 0, 8, 16, 13, default);
        Assert.True(glyphs.Glyphs.IsEmpty);
        var one = new DrawGlyphs(5, 6, 8, 16, 13, [new Glyph('a', 2, 3, Ink)]);
        Assert.Equal(new Glyph('a', 2, 3, Ink), Assert.Single(one.Glyphs));
        Assert.Throws<ArgumentException>(() => new DrawGlyphs(0, 0, 0, 16, 13, []));
        Assert.Throws<ArgumentException>(() => new DrawGlyphs(0, 0, 8, 0, 13, []));
        Assert.Throws<ArgumentException>(() => new DrawGlyphs(0, 0, 8, 16, 0, []));
    }

    // No Handwired test. The phase 3 apps name no font. The window then loads the ASCII code points.
    [Fact]
    public void An_app_with_no_font_members_names_no_path_and_no_code_point()
    {
        IWindowApp app = new NoFontApp();
        Assert.Empty(app.FontPaths);
        Assert.Empty(app.FontCodePoints);
    }

    // No Handwired test. The window loads ASCII 32..126 and the code points of the app, once each and in order.
    [Fact]
    public void The_loaded_code_points_are_ascii_and_the_code_points_of_the_app_in_order_with_no_repeat()
    {
        var ascii = GlyphFonts.CodePointsToLoad([]);
        Assert.Equal(Enumerable.Range(32, 95), ascii);
        var loaded = GlyphFonts.CodePointsToLoad([0x6771, 'a', 0x00f8, 0x6771, 0x10, 0x110000, 0xd800]);
        Assert.Equal([.. Enumerable.Range(32, 95), 0x6771, 0x00f8], loaded);
    }

    // No Handwired test. A code point goes to the first font that has it. A code point that no font has goes to
    // the replacement glyph "?" of the first font. With no font file, the Raylib default font draws ASCII.
    [Fact]
    public void Each_code_point_goes_to_the_first_font_that_has_it_or_to_the_replacement()
    {
        var fonts = new GlyphFonts([new HashSet<int> { 'a', '?', 0xf8 }, new HashSet<int> { 'a', 0x6771 }]);
        Assert.Equal((0, (int)'a'), fonts.Resolve('a'));
        Assert.Equal((0, 0xf8), fonts.Resolve(0xf8));
        Assert.Equal((1, 0x6771), fonts.Resolve(0x6771));
        Assert.Equal((0, '?'), fonts.Resolve(0x1f600));
        Assert.Equal([0x1f600], fonts.Missing([0x1f600, 'a', 0x6771]));

        var fallback = GlyphFonts.DefaultOnly();
        Assert.Equal((0, (int)'z'), fallback.Resolve('z'));
        Assert.Equal((0, '?'), fallback.Resolve(0x6771));
    }

    private sealed class NoFontApp : IWindowApp
    {
        public string Title => "no font";

        public int InitialWidth => 100;

        public int InitialHeight => 100;

        public Rgba Background => Ink;

        public IReadOnlyList<DrawCommand> Frame(WindowInput input, double frameTimeMs) => [];
    }
}
