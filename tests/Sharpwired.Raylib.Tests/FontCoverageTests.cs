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

    // No Handwired test. A table offset or a subtable offset above int.MaxValue, and each cut of the file, give an
    // ArgumentException and no other exception.
    [Fact]
    public void An_offset_past_the_file_and_each_truncated_table_give_an_argument_exception()
    {
        // The probe file: 64 bytes, one table record "cmap" at offset 0x80000000.
        var probe = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(probe.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(probe.AsSpan(4), 1);
        "cmap"u8.CopyTo(probe.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(probe.AsSpan(20), 0x80000000);
        BinaryPrimitives.WriteUInt32BigEndian(probe.AsSpan(24), 4);
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered(probe, ['A']));

        // A subtable offset of 0xFFFFFFF0 in the first encoding record.
        var farSubtable = FontWithCmap();
        BinaryPrimitives.WriteUInt32BigEndian(farSubtable.AsSpan(28 + 4 + 4), 0xfffffff0);
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered(farSubtable, ['A']));

        // A format 12 group count that is past the end of the file.
        var font = FontWithCmap();
        var manyGroups = font.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(manyGroups.AsSpan(font.Length - 28 + 12), 0xffffffff);
        Assert.Throws<ArgumentException>(() => FontCoverage.Covered(manyGroups, [0x1f602]));

        // Each cut of the file. The table record keeps the length of the full cmap table.
        for (var length = 0; length < font.Length; length++)
        {
            var cut = font[..length];
            Assert.Throws<ArgumentException>(() => FontCoverage.Covered(cut, ['A', 0x6771, 0x1f600, 0x1f602]));
        }
    }

    // No Handwired test. The file reader gives the covered code points of a font file. A missing file, a directory,
    // and a file that is not a font give null and a reason. The window then skips that font.
    [Fact]
    public void The_file_reader_gives_null_and_a_reason_for_each_file_that_it_cannot_read()
    {
        var directory = Directory.CreateTempSubdirectory("sharpwired-font-");
        try
        {
            var good = Path.Combine(directory.FullName, "good.ttf");
            File.WriteAllBytes(good, FontWithCmap());
            Assert.Equal([(int)'A'], FontCoverage.CoveredInFile(good, ['A', 'D'], out var noError)!.Order());
            Assert.Null(noError);

            var bad = Path.Combine(directory.FullName, "bad.ttf");
            File.WriteAllBytes(bad, [1, 2, 3]);
            Assert.Null(FontCoverage.CoveredInFile(bad, ['A'], out var badError));
            Assert.NotNull(badError);
            Assert.Null(FontCoverage.CoveredInFile(Path.Combine(directory.FullName, "missing.ttf"), ['A'], out var missingError));
            Assert.NotNull(missingError);
            Assert.Null(FontCoverage.CoveredInFile(directory.FullName, ['A'], out var directoryError));
            Assert.NotNull(directoryError);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
