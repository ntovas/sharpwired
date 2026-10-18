// No Handwired source. These tests cover FontCoverage, which reads the character map (cmap) of a TrueType font
// file, so that the window knows which code points a font has before it draws them. The font bytes are written
// here: one cmap table with a format 4 and a format 12 subtable.

using System.Buffers.Binary;

namespace Sharpwired.Raylib.Tests;

public class FontCoverageTests
{
    // No Handwired test. Format 4 maps A..C with a delta and U+6771 through the glyph array. U+6772 maps to glyph
    // 0, the missing glyph. Format 12 maps U+1F600..U+1F601.
    [Fact]
    public void The_covered_code_points_come_from_the_format_4_and_the_format_12_subtables()
    {
        var font = FontWithCmap();
        var covered = FontCoverage.Covered(font, ['A', 'B', 'C', 'D', 0x6771, 0x6772, 0x1f600, 0x1f601, 0x1f602, 0xffff]);
        Assert.Equal([(int)'A', 'B', 'C', 0x6771, 0x1f600, 0x1f601], covered.Order());
    }

    // No Handwired test. Bytes that are not a font, or a font with no cmap table, give an ArgumentException.
    [Fact]
    public void A_file_with_no_character_map_gives_an_argument_exception()
    {
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered([1, 2, 3], ['A']));
        var noCmap = FontWithCmap();
        noCmap[12] = (byte)'x';
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered(noCmap, ['A']));
        var cut = FontWithCmap()[..40];
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered(cut, ['A']));
    }

    private static byte[] FontWithCmap()
    {
        var format4 = Format4();
        var format12 = Format12();
        const int cmapHeader = 4 + (2 * 8);
        var cmap = new byte[cmapHeader + format4.Length + format12.Length];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 2);
        WriteRecord(cmap.AsSpan(4), 3, 1, cmapHeader);
        WriteRecord(cmap.AsSpan(12), 3, 10, cmapHeader + format4.Length);
        format4.CopyTo(cmap, cmapHeader);
        format12.CopyTo(cmap, cmapHeader + format4.Length);

        const int directory = 12 + 16;
        var font = new byte[directory + cmap.Length];
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 1);
        "cmap"u8.CopyTo(font.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20), directory);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(24), (uint)cmap.Length);
        cmap.CopyTo(font, directory);
        return font;
    }

    private static void WriteRecord(Span<byte> record, ushort platform, ushort encoding, int offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(record, platform);
        BinaryPrimitives.WriteUInt16BigEndian(record[2..], encoding);
        BinaryPrimitives.WriteUInt32BigEndian(record[4..], (uint)offset);
    }

    // Three segments: 'A'..'C' with idDelta 1 - 'A', U+6771..U+6772 through the glyph array with idDelta 5 (glyph 7
    // gives 12, and glyph 0 stays the missing glyph), and
    // the last segment 0xFFFF.
    private static byte[] Format4()
    {
        ushort[] ends = ['C', 0x6772, 0xffff];
        ushort[] starts = ['A', 0x6771, 0xffff];
        short[] deltas = [(short)(1 - 'A'), 5, 1];
        ushort[] glyphIds = [7, 0];
        const int segments = 3;
        var length = 16 + (segments * 8) + (glyphIds.Length * 2);
        var table = new byte[length];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, 4);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], segments * 2);
        var endsAt = 14;
        var startsAt = endsAt + (segments * 2) + 2;
        var deltasAt = startsAt + (segments * 2);
        var rangesAt = deltasAt + (segments * 2);
        var glyphsAt = rangesAt + (segments * 2);
        for (var segment = 0; segment < segments; segment++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[(endsAt + (segment * 2))..], ends[segment]);
            BinaryPrimitives.WriteUInt16BigEndian(span[(startsAt + (segment * 2))..], starts[segment]);
            BinaryPrimitives.WriteInt16BigEndian(span[(deltasAt + (segment * 2))..], deltas[segment]);
        }
        // The range offset of segment 1 points from its own place to the first glyph id.
        BinaryPrimitives.WriteUInt16BigEndian(span[(rangesAt + 2)..], (ushort)(glyphsAt - (rangesAt + 2)));
        for (var index = 0; index < glyphIds.Length; index++) BinaryPrimitives.WriteUInt16BigEndian(span[(glyphsAt + (index * 2))..], glyphIds[index]);
        return table;
    }

    private static byte[] Format12()
    {
        var table = new byte[16 + 12];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, 12);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)table.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], 0x1f600);
        BinaryPrimitives.WriteUInt32BigEndian(span[20..], 0x1f601);
        BinaryPrimitives.WriteUInt32BigEndian(span[24..], 9);
        return table;
    }
}
