// No Handwired source. The browser picks a font for each character from the font list of the CSS. A Raylib
// window loads its fonts itself. This file holds the parts of that choice that make no Raylib call. These are
// the code points that the window loads and the font of each code point. They also include the replacement
// glyph for a code point that no font has (phase 6 decision 4). FontCoverage.cs reads which code points a font file has.

namespace Sharpwired.Raylib;

/// <summary>The fonts of a window, as the sets of code points that each font has, in order of preference.</summary>
public sealed class GlyphFonts
{
    /// <summary>The glyph that the window draws for a code point that no font has.</summary>
    public const int Replacement = '?';

    private const int FirstAscii = 32;
    private const int LastAscii = 126;

    private readonly IReadOnlyList<IReadOnlySet<int>> fonts;

    /// <summary>Make the choice for fonts that have these code points, in order of preference.</summary>
    /// <exception cref="ArgumentException">No font.</exception>
    public GlyphFonts(IReadOnlyList<IReadOnlySet<int>> fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        if (fonts.Count == 0) throw new ArgumentException("the window needs at least one font", nameof(fonts));
        this.fonts = fonts;
    }

    /// <summary>The number of fonts.</summary>
    public int Count => fonts.Count;

    /// <summary>The choice with only the Raylib default font, which has ASCII 32..126.</summary>
    public static GlyphFonts DefaultOnly() => new([Ascii().ToHashSet()]);

    /// <summary>
    /// The code points to load from each font. ASCII 32..126 come first, then the other code points of the app
    /// in their first order, each one time. The method leaves out a value that is not a Unicode scalar value
    /// above ASCII. Examples are a control code, a surrogate or a value past U+10FFFF.
    /// </summary>
    public static int[] CodePointsToLoad(IReadOnlyList<int> appCodePoints)
    {
        ArgumentNullException.ThrowIfNull(appCodePoints);
        var seen = new HashSet<int>(Ascii());
        var loaded = new List<int>(Ascii());
        foreach (var codePoint in appCodePoints)
        {
            if (codePoint <= LastAscii || codePoint > 0x10FFFF || (codePoint >= 0xd800 && codePoint <= 0xdfff)) continue;
            if (seen.Add(codePoint)) loaded.Add(codePoint);
        }
        return [.. loaded];
    }

    /// <summary>
    /// The font that draws the code point and the code point that it draws. This is the first font that has
    /// the code point. When no font has it, this is the replacement glyph of the first font.
    /// </summary>
    public (int Font, int CodePoint) Resolve(int codePoint)
    {
        for (var font = 0; font < fonts.Count; font++)
        {
            if (fonts[font].Contains(codePoint)) return (font, codePoint);
        }
        return (0, Replacement);
    }

    /// <summary>The code points that no font has, in the order of the list.</summary>
    public IReadOnlyList<int> Missing(IEnumerable<int> codePoints)
    {
        ArgumentNullException.ThrowIfNull(codePoints);
        return [.. codePoints.Where(codePoint => !fonts.Any(font => font.Contains(codePoint)))];
    }

    private static IEnumerable<int> Ascii() => Enumerable.Range(FirstAscii, LastAscii - FirstAscii + 1);
}
