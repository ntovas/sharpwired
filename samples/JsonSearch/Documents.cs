// Handwired source: src/json-search/documents.ts (documentAt, generateDocuments, codePointLength, validDocument,
// the hash lowbias32 and createRandom). Many small JSON documents for the batch measurements and the window.
// They are package manifests, user records, log events, editor settings, orders, map features and sensor
// readings. Each is at most 512 code points, the capacity of the highlighter. A known fraction has a defect on
// purpose, in one of 16 ways. Examples are a missing quote, a trailing comma, a bad escape, nesting past depth
// 16, or a text longer than 512 code points.
//
// DocumentAt(seed, index) is a pure function. Each choice is a hash of the seed, the index of the document and
// a counter (lowbias32, never a shared random generator). So a caller can make document 73,512 without the
// documents before it, and two runs time the same texts. The builders (DocumentBuilders.cs) and the defects
// (DocumentDefects.cs) make the same choices in the same order as documents.ts. The serializer writes the
// text of JSON.stringify, so the C# texts are the Handwired texts (DocumentTests pins the Handwired hash).
// Difference from Handwired: GeneratedDocument also gives the style of the text, for the test of R6.1 that each
// style occurs.

namespace JsonSearch;

/// <summary>The 7 shapes of a document.</summary>
public enum DocumentShape
{
    PackageManifest,
    UserRecord,
    LogEvent,
    Settings,
    Order,
    MapFeature,
    SensorReadings,
}

/// <summary>
/// The ways a document is broken. System.Text.Json refuses each one except TooDeep and TooLong. These two are
/// valid JSON that the highlighter does not hold. The highlighter reports TooDeep at the 17th nested opener and
/// TooLong at code point 512.
/// </summary>
public enum DocumentDefect
{
    None,

    /// <summary>A string loses one of its quotes, for example {"name: "ada"}.</summary>
    MissingQuote,

    /// <summary>A comma before a closing bracket, for example [1, 2,].</summary>
    TrailingComma,

    /// <summary>A backslash before a character that JSON does not escape, for example "\qada", or a \u with too few hex digits.</summary>
    BadEscape,

    /// <summary>The document is in 16 to 19 more containers, for example [[[[…{"id": 1}…]]]].</summary>
    TooDeep,

    /// <summary>Some records in one array, more than 512 code points.</summary>
    TooLong,

    /// <summary>A key loses its colon, for example {"id" 1}.</summary>
    MissingColon,

    /// <summary>A key without quotes, for example {id: 1}.</summary>
    UnquotedKey,

    /// <summary>The text stops early, for example {"id": 1, "na.</summary>
    TruncatedText,

    /// <summary>A string in single quotes, for example {'id': 1}.</summary>
    SingleQuotes,

    /// <summary>A literal from another language, for example True, None or undefined.</summary>
    BadLiteral,

    /// <summary>A number that JSON does not allow, for example +1 or 012.</summary>
    BadNumber,

    /// <summary>Two values without a comma between them, for example ["a" "b"].</summary>
    MissingComma,

    /// <summary>A closing bracket of the other kind, for example {"id": 1].</summary>
    MismatchedBracket,

    /// <summary>A raw line feed, tab or other control character in a string.</summary>
    ControlCharacter,

    /// <summary>A comment, for example // generated.</summary>
    Comment,

    /// <summary>More text after the value of the document, for example {"id": 1}}.</summary>
    ExtraText,
}

/// <summary>
/// The layout of a text: Compact (JSON.stringify), Spaced (", " and ": " on one line, as Python json.dumps),
/// Indented (2 spaces) and Tabbed (one tab).
/// </summary>
public enum DocumentStyle
{
    Compact,
    Spaced,
    Indented,
    Tabbed,
}

/// <summary>One generated document: its text, its shape, its defect and its style.</summary>
public sealed record GeneratedDocument(string Text, DocumentShape Shape, DocumentDefect Defect, DocumentStyle Style);

/// <summary>The document generator.</summary>
public static class Documents
{
    public const int DefaultSeed = 20261013;
    public const double DefaultInvalidFraction = 0.1;

    // The most code points that a defect adds to a text, for example the 13 of a comment line.
    private const int DefectMargin = 16;

    /// <summary>The document at the index for the seed. A fraction invalidFraction of the documents has a defect.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or the fraction is outside 0..1.</exception>
    public static GeneratedDocument DocumentAt(int seed, int index, double invalidFraction = DefaultInvalidFraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (!(invalidFraction >= 0 && invalidFraction <= 1)) throw new ArgumentOutOfRangeException(nameof(invalidFraction), invalidFraction, "the fraction must be in 0..1");
        var random = new DocumentRandom(seed, index);
        var shape = random.Pick(Enum.GetValues<DocumentShape>());
        if (!random.Chance(invalidFraction))
        {
            var valid = ValidDocument(random, shape, Kinds.Capacity);
            return new GeneratedDocument(valid.Text, shape, DocumentDefect.None, valid.Style);
        }
        var first = random.Integer(0, DefectCount - 1);
        for (var offset = 0; ; offset++)
        {
            var defect = (DocumentDefect)(1 + ((first + offset) % DefectCount));
            if (defect == DocumentDefect.TooDeep)
            {
                // 16 to 19 containers around a document that has at least one of its own, so the depth passes 16.
                var levels = random.Integer(Kinds.MaximumDepth, Kinds.MaximumDepth + 3);
                var objects = new bool[levels];
                for (var level = 0; level < levels; level++) objects[level] = random.Chance(0.3);
                var opening = string.Concat(objects.Select(isObject => isObject ? "{\"a\":" : "["));
                var closing = string.Concat(Enumerable.Reverse(objects).Select(isObject => isObject ? "}" : "]"));
                var inner = ValidDocument(random, shape, Kinds.Capacity - opening.Length - closing.Length);
                return new GeneratedDocument(opening + inner.Text + closing, shape, defect, inner.Style);
            }
            if (defect == DocumentDefect.TooLong)
            {
                // Records of the same shape in one array, until the text is longer than the capacity.
                var records = new JsonArray();
                var text = "";
                var style = DocumentStyle.Compact;
                while (CodePointLength(text) <= Kinds.Capacity)
                {
                    records.Items.Add(ValidDocument(random, shape, Kinds.Capacity).Value);
                    style = records.Items.Count % 2 == 0 ? DocumentStyle.Spaced : DocumentStyle.Indented;
                    text = JsonText.Serialize(records, style, escapeNonAscii: false);
                }
                return new GeneratedDocument(text, shape, defect, style);
            }
            var document = ValidDocument(random, shape, Kinds.Capacity - DefectMargin);
            if (DocumentDefects.Apply(random, defect, document.Text) is { } broken) return new GeneratedDocument(broken, shape, defect, document.Style);
        }
    }

    /// <summary>The documents 0..count - 1 of the seed.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative, or the fraction is outside 0..1.</exception>
    public static IReadOnlyList<GeneratedDocument> Generate(int count, int seed = DefaultSeed, double invalidFraction = DefaultInvalidFraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var documents = new GeneratedDocument[count];
        Parallel.For(0, count, index => documents[index] = DocumentAt(seed, index, invalidFraction));
        return documents;
    }

    /// <summary>The length of a text in code points: a surrogate pair is one code point, and a lone surrogate is its own.</summary>
    public static int CodePointLength(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var length = text.Length;
        for (var index = 0; index < text.Length - 1; index++)
        {
            if (char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1])) length--;
        }
        return length;
    }

    private static readonly int DefectCount = Enum.GetValues<DocumentDefect>().Length - 1;

    // A valid document of at most maximumLength code points. A document that is longer is built again with
    // fewer fields, and on one line after two tries.
    private static (JsonValue Value, string Text, DocumentStyle Style) ValidDocument(DocumentRandom random, DocumentShape shape, int maximumLength)
    {
        var size = random.Next();
        var style = random.Pick(Enum.GetValues<DocumentStyle>());
        var escapeNonAscii = random.Chance(0.15);
        for (var attempt = 0; ; attempt++)
        {
            var value = DocumentBuilders.Build(shape, random, size);
            var text = JsonText.Serialize(value, style, escapeNonAscii);
            if (CodePointLength(text) <= maximumLength) return (value, text, style);
            size = attempt >= 6 ? 0 : size * 0.6;
            if (attempt >= 1) style = DocumentStyle.Compact;
            if (attempt > 40) throw new InvalidOperationException($"no {shape} document fits in {maximumLength} code points");
        }
    }
}

/// <summary>
/// The choices of one document: hash3(seed, index, counter) with lowbias32, a number in [0, 1) for each call
/// (documents.ts createRandom).
/// </summary>
internal sealed class DocumentRandom(int seed, int index)
{
    private int counter;

    // lowbias32 of the "Pseudo-Random" rules of Handwired (ui.md), with the 32-bit integer operations of JavaScript.
    internal static uint LowBias32(uint n)
    {
        unchecked
        {
            n = ((n >> 16) ^ n) * 0x21f0aaad;
            n = ((n >> 15) ^ n) * 0xd35a2d97;
            return (n >> 15) ^ n;
        }
    }

    public double Next()
    {
        unchecked
        {
            var hash = LowBias32(LowBias32(LowBias32((uint)seed) ^ (uint)index) ^ (uint)counter++);
            return hash / 4_294_967_296.0;
        }
    }

    public int Integer(int minimum, int maximum) => minimum + (int)Math.Floor(Next() * (maximum - minimum + 1));

    public bool Chance(double probability) => Next() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[(int)Math.Floor(Next() * items.Count)];
}
