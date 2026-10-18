// No Handwired source. The code points that a TrueType or OpenType font file has, from its character map (the
// cmap table). Raylib loads a glyph for each code point that a window asks for, also for a code point that the
// file does not have. For such a code point, Raylib draws an empty glyph. So the window reads the character map first, and draws a
// replacement glyph for a code point that no font has (phase 6 decision 4). The reader knows the subtables of
// format 4 (the basic plane) and format 12 (all planes), for the Unicode platform and the Windows Unicode
// encodings. This file makes no Raylib call.

using System.Buffers.Binary;

namespace Sharpwired.Raylib;

/// <summary>The reader of the character map of a font file.</summary>
public static class FontCoverage
{
    private const uint CmapTag = 0x636d6170; // "cmap"

    /// <summary>The code points of the list that the font maps to a glyph other than glyph 0, the missing glyph.</summary>
    /// <exception cref="ArgumentException">
    /// The bytes are not a font with a character map that the reader knows. The method also throws it when an offset
    /// or a length is past the end of the file.
    /// </exception>
    public static HashSet<int> Covered(ReadOnlySpan<byte> font, IEnumerable<int> codePoints)
    {
        ArgumentNullException.ThrowIfNull(codePoints);
        var subtables = Subtables(font);
        var covered = new HashSet<int>();
        foreach (var codePoint in codePoints)
        {
            foreach (var subtable in subtables)
            {
                if (GlyphOf(font, subtable, codePoint) == 0) continue;
                covered.Add(codePoint);
                break;
            }
        }
        return covered;
    }

    /// <summary>
    /// The covered code points of the font file at the path, or null when the reader cannot read the file. Then error
    /// holds the reason: the file does not exist or cannot be read, or it is not a font that the reader knows.
    /// </summary>
    public static HashSet<int>? CoveredInFile(string path, IEnumerable<int> codePoints, out string? error)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(codePoints);
        try
        {
            var covered = Covered(File.ReadAllBytes(path), codePoints);
            error = null;
            return covered;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return null;
        }
    }

    // The offsets of the subtables of format 4 and 12 for Unicode, from the start of the file.
    private static List<int> Subtables(ReadOnlySpan<byte> font)
    {
        var tableCount = U16(font, 4);
        var cmap = -1;
        var cmapLength = 0L;
        for (var table = 0; table < tableCount; table++)
        {
            var record = 12 + (table * 16);
            if (U32(font, record) != CmapTag) continue;
            var offset = U32(font, record + 8);
            cmapLength = U32(font, record + 12);
            if (offset + cmapLength > font.Length) throw new ArgumentException("the cmap table of the font is past the end of the file");
            cmap = (int)offset;
            break;
        }
        if (cmap < 0) throw new ArgumentException("the font has no cmap table");
        var subtables = new List<int>();
        var encodingCount = U16(font, cmap + 2);
        for (var encoding = 0; encoding < encodingCount; encoding++)
        {
            var record = cmap + 4 + (encoding * 8);
            var platform = U16(font, record);
            var id = U16(font, record + 2);
            if (!(platform == 0 || (platform == 3 && (id == 1 || id == 10)))) continue;
            var subtableOffset = cmap + (long)U32(font, record + 4);
            if (subtableOffset >= font.Length) throw new ArgumentException("a subtable of the cmap table is past the end of the file");
            var subtable = (int)subtableOffset;
            var format = U16(font, subtable);
            if ((format == 4 || format == 12) && !subtables.Contains(subtable)) subtables.Add(subtable);
        }
        if (subtables.Count == 0) throw new ArgumentException("the cmap table of the font has no Unicode subtable of format 4 or 12");
        return subtables;
    }

    private static int GlyphOf(ReadOnlySpan<byte> font, int subtable, int codePoint) =>
        U16(font, subtable) == 4 ? Format4Glyph(font, subtable, codePoint) : Format12Glyph(font, subtable, codePoint);

    // Format 4: segments of the basic plane, each with an end code, a start code, a delta and a range offset.
    private static int Format4Glyph(ReadOnlySpan<byte> font, int subtable, int codePoint)
    {
        if (codePoint > 0xffff) return 0;
        var segments = U16(font, subtable + 6) / 2;
        var ends = subtable + 14;
        var starts = ends + (segments * 2) + 2;
        var deltas = starts + (segments * 2);
        var ranges = deltas + (segments * 2);
        for (var segment = 0; segment < segments; segment++)
        {
            if (codePoint > U16(font, ends + (segment * 2))) continue;
            var start = U16(font, starts + (segment * 2));
            if (codePoint < start) return 0;
            var delta = U16(font, deltas + (segment * 2));
            var rangeAt = ranges + (segment * 2);
            var range = U16(font, rangeAt);
            if (range == 0) return (codePoint + delta) & 0xffff;
            var glyph = U16(font, rangeAt + range + ((codePoint - start) * 2));
            return glyph == 0 ? 0 : (glyph + delta) & 0xffff;
        }
        return 0;
    }

    // Format 12: groups of code points, each with a start, an end and the glyph of the start.
    private static int Format12Glyph(ReadOnlySpan<byte> font, int subtable, int codePoint)
    {
        var groups = U32(font, subtable + 12);
        if (subtable + 16 + (groups * 12L) > font.Length) throw new ArgumentException("the groups of a format 12 subtable are past the end of the file");
        for (var group = 0; group < groups; group++)
        {
            var at = subtable + 16 + (group * 12);
            var start = U32(font, at);
            var end = U32(font, at + 4);
            if (codePoint < start || codePoint > end) continue;
            return (int)(U32(font, at + 8) + ((uint)codePoint - start));
        }
        return 0;
    }

    private static int U16(ReadOnlySpan<byte> font, int at)
    {
        if (at < 0 || at + 2 > font.Length) throw new ArgumentException("the font file ends inside a table");
        return BinaryPrimitives.ReadUInt16BigEndian(font[at..]);
    }

    private static uint U32(ReadOnlySpan<byte> font, int at)
    {
        if (at < 0 || at + 4 > font.Length) throw new ArgumentException("the font file ends inside a table");
        return BinaryPrimitives.ReadUInt32BigEndian(font[at..]);
    }
}
