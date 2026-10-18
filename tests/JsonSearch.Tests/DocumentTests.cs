// Handwired source: tests/json-search/json-search.test.ts ("the generated documents"). The generator (R6.1):
// DocumentAt is a pure function of the seed and the index; the valid documents parse with System.Text.Json, fit
// the capacity and have each shape and each style; each defect occurs, and each defect text fails
// System.Text.Json except TooDeep and TooLong, which the reference reports. The SHA-256 hash of the first 2,000
// texts is pinned. The Handwired test pins a lowbias32 hash of the same texts and the statistics of the first
// 20,000 documents; the C# texts give the same values, so the C# generator writes the Handwired texts.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using JsonSearch;
using Xunit.Abstractions;

namespace JsonSearch.Tests;

public sealed class DocumentTests(ITestOutputHelper output)
{
    private const int Capacity = Kinds.Capacity;

    // The SHA-256 hash of the first 2,000 texts with the default seed and the default fraction: for each text,
    // its length in UTF-16 code units as a little-endian int32, then its UTF-16 code units, little-endian.
    private const string PinnedSha256 = "E33D69F4D4D2784641E7D9B9B2C423F1BE612265AB70B8FA1B0E8076D239896E";

    // json-search.test.ts pinnedDocumentsHash: hash = lowbias32(hash ^ unit) over each UTF-16 code unit of the first 2,000 texts.
    private const uint HandwiredHash = 3_798_448_091;

    private static readonly Lazy<IReadOnlyList<GeneratedDocument>> First20000 = new(() => Documents.Generate(20_000));

    internal static string Sha256(IEnumerable<string> texts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var text in texts)
        {
            hash.AppendData(BitConverter.GetBytes(text.Length));
            hash.AppendData(MemoryMarshal.AsBytes(text.AsSpan()));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static uint LowBias32(uint n)
    {
        unchecked
        {
            n = ((n >> 16) ^ n) * 0x21f0aaad;
            n = ((n >> 15) ^ n) * 0xd35a2d97;
            return (n >> 15) ^ n;
        }
    }

    [Fact]
    public void A_document_depends_only_on_the_seed_and_its_index()
    {
        int[] indices = [0, 1, 7, 1999, 19_999, 73_512];
        var forward = indices.Select(index => Documents.DocumentAt(Documents.DefaultSeed, index)).ToArray();
        var backward = indices.Reverse().Select(index => Documents.DocumentAt(Documents.DefaultSeed, index)).Reverse().ToArray();
        var threads = new ConcurrentDictionary<int, GeneratedDocument>();
        Parallel.ForEach(indices.Concat(indices), new ParallelOptions { MaxDegreeOfParallelism = 2 }, index =>
        {
            var document = Documents.DocumentAt(Documents.DefaultSeed, index);
            threads.AddOrUpdate(index, document, (_, earlier) => earlier == document ? document : throw new InvalidOperationException($"document {index} differs on two threads"));
        });
        Assert.Equal(forward, backward);
        Assert.Equal(forward, indices.Select(index => threads[index]));
        Assert.Equal(forward, indices.Select(index => First20000.Value.Count > index ? First20000.Value[index] : Documents.DocumentAt(Documents.DefaultSeed, index)));
        Assert.NotEqual(forward[2].Text, Documents.DocumentAt(Documents.DefaultSeed + 1, 7).Text);
    }

    [Fact]
    public void The_hashes_of_the_first_2000_texts_are_pinned()
    {
        var texts = First20000.Value.Take(2000).Select(document => document.Text).ToArray();
        var sha256 = Sha256(texts);
        uint hash = 0;
        foreach (var text in texts)
        {
            foreach (var unit in text) hash = LowBias32(hash ^ unit);
        }
        output.WriteLine($"SHA-256 {sha256}, lowbias32 {hash}");
        Assert.Equal(HandwiredHash, hash);
        Assert.Equal(PinnedSha256, sha256);
    }

    [Fact]
    public void The_valid_documents_parse_fit_the_capacity_and_have_each_shape_and_each_style()
    {
        const int Count = 100_000;
        var shapes = new int[Enum.GetValues<DocumentShape>().Length];
        var styles = new int[Enum.GetValues<DocumentStyle>().Length];
        var failures = new ConcurrentBag<string>();
        var documents = new GeneratedDocument[Count];
        Parallel.For(0, Count, index =>
        {
            var document = documents[index] = Documents.DocumentAt(Documents.DefaultSeed, index, invalidFraction: 0);
            if (document.Defect != DocumentDefect.None) failures.Add($"document {index} has the defect {document.Defect}");
            if (Documents.CodePointLength(document.Text) > Capacity) failures.Add($"document {index} has {Documents.CodePointLength(document.Text)} code points");
            if (!JsonReferenceTests.Parses(document.Text)) failures.Add($"System.Text.Json refuses document {index}: {document.Text}");
            if (!JsonReference.Highlight(document.Text).Valid) failures.Add($"the reference refuses document {index}: {document.Text}");
        });
        foreach (var document in documents)
        {
            shapes[(int)document.Shape]++;
            styles[(int)document.Style]++;
        }
        Assert.Empty(failures.Take(5));
        output.WriteLine($"shapes {string.Join(", ", shapes)}; styles {string.Join(", ", styles)}");
        Assert.All(shapes, count => Assert.True(count > 10_000, $"a shape has {count} documents"));
        Assert.All(styles, count => Assert.True(count > 5_000, $"a style has {count} documents"));
    }

    [Fact]
    public void Each_defect_occurs_and_each_defect_text_fails_system_text_json_except_too_deep_and_too_long()
    {
        var defects = new int[Enum.GetValues<DocumentDefect>().Length];
        foreach (var document in First20000.Value.Take(2000))
        {
            defects[(int)document.Defect]++;
            var reference = JsonReference.Highlight(document.Text);
            var parses = JsonReferenceTests.Parses(document.Text);
            var expectValid = document.Defect == DocumentDefect.None;
            var expectParses = expectValid || document.Defect is DocumentDefect.TooDeep or DocumentDefect.TooLong;
            var fits = Documents.CodePointLength(document.Text) <= Capacity;
            Assert.True(
                reference.Valid == expectValid && parses == expectParses && fits == (document.Defect != DocumentDefect.TooLong),
                $"{document.Defect}: reference valid {reference.Valid}, parses {parses}, fits {fits}: {document.Text}");
            if (document.Defect == DocumentDefect.TooDeep) Assert.Equal(ErrorKind.TooDeep, reference.FirstErrorKind);
            if (document.Defect == DocumentDefect.TooLong) Assert.Equal((ErrorKind.TooLong, Capacity, true), (reference.FirstErrorKind, reference.FirstErrorPosition, reference.Truncated));
        }
        output.WriteLine($"defects in the first 2,000 documents: {string.Join(", ", Enum.GetValues<DocumentDefect>().Select(defect => $"{defect} {defects[(int)defect]}"))}");
        Assert.All(defects, count => Assert.True(count > 0));
    }

    // json-search.test.ts: "every shape and every defect appears, about 1 document in 10 is broken, and the
    // lengths spread up to the capacity", with the Handwired values.
    [Fact]
    public void The_first_20000_documents_have_the_handwired_statistics()
    {
        var documents = First20000.Value;
        var lengths = documents.Select(document => Documents.CodePointLength(document.Text)).Order().ToArray();
        int At(double fraction) => lengths[Math.Min(lengths.Length - 1, (int)Math.Floor(fraction * lengths.Length))];
        var mean = Js.Round(lengths.Sum(length => (double)length) / lengths.Length * 10) / 10;
        Assert.Equal(1979, documents.Count(document => document.Defect != DocumentDefect.None));
        Assert.Equal((5, 215, 300, 387, 1382, 301.8), (lengths[0], At(0.25), At(0.5), At(0.75), lengths[^1], mean));
        foreach (var shape in Enum.GetValues<DocumentShape>()) Assert.True(documents.Count(document => document.Shape == shape) > 2000);
        foreach (var defect in Enum.GetValues<DocumentDefect>().Skip(1)) Assert.True(documents.Count(document => document.Defect == defect) > 60);
        Assert.True(documents.Count(document => document.Text.Any(unit => unit >= 0x80)) > 4000);
        var kindsSeen = documents.Take(500).SelectMany(document => JsonReference.Highlight(document.Text).DisplayKinds).ToHashSet();
        Assert.Equal(Enum.GetValues<DisplayKind>().ToHashSet(), kindsSeen);
    }

    [Fact]
    public void Generate_gives_the_documents_of_document_at()
    {
        var documents = Documents.Generate(50, 977, 1);
        Assert.Equal(Enumerable.Range(0, 50).Select(index => Documents.DocumentAt(977, index, 1)), documents);
        Assert.DoesNotContain(documents, document => document.Defect == DocumentDefect.None);
    }

    [Fact]
    public void Code_point_length_counts_a_surrogate_pair_as_one()
    {
        Assert.Equal(0, Documents.CodePointLength(""));
        Assert.Equal(3, Documents.CodePointLength("a😀b"));
        Assert.Equal(2, Documents.CodePointLength("a\ud83d"));
        Assert.Equal(2, Documents.CodePointLength("\ude00\ud83d"));
    }

    [Fact]
    public void A_bad_argument_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Documents.DocumentAt(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Documents.DocumentAt(1, 0, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Documents.Generate(-1));
    }
}
