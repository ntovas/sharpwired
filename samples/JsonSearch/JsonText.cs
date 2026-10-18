// Handwired source: src/json-search/documents.ts (the Json type, spaced and serialize) and the JSON.stringify
// and Number.prototype.toString rules of ECMAScript that documents.ts calls. The values of a generated document
// and their text: JSON.stringify for the compact, the indented (2 spaces) and the tabbed style, the spaced style,
// and the escape of each UTF-16 code unit past U+007F. A number is written as JavaScript writes it, for example
// 2.5e-7, 6.02e+23, 1500 and 0.0001, and an object keeps the order in which its keys were first set.

using System.Globalization;
using System.Text;

namespace JsonSearch;

/// <summary>A JSON value of a generated document.</summary>
internal abstract class JsonValue
{
    public static implicit operator JsonValue(string value) => new JsonString(value);

    public static implicit operator JsonValue(double value) => new JsonNumber(value);

    public static implicit operator JsonValue(bool value) => new JsonBool(value);
}

internal sealed class JsonString(string value) : JsonValue
{
    public string Value { get; } = value;
}

internal sealed class JsonNumber(double value) : JsonValue
{
    public double Value { get; } = value;
}

internal sealed class JsonBool(bool value) : JsonValue
{
    public bool Value { get; } = value;
}

internal sealed class JsonNull : JsonValue
{
    public static JsonNull Instance { get; } = new();
}

internal sealed class JsonArray : JsonValue
{
    public JsonArray(params IEnumerable<JsonValue> items) => Items = [.. items];

    public List<JsonValue> Items { get; }
}

/// <summary>An object. A key that is set again keeps its first place and gets the new value, as in JavaScript.</summary>
internal sealed class JsonObject : JsonValue
{
    public List<KeyValuePair<string, JsonValue>> Members { get; } = [];

    public JsonObject Set(string key, JsonValue value)
    {
        var index = Members.FindIndex(member => member.Key == key);
        if (index >= 0) Members[index] = KeyValuePair.Create(key, value);
        else Members.Add(KeyValuePair.Create(key, value));
        return this;
    }
}

/// <summary>The text of a JSON value in one of the 4 styles.</summary>
internal static class JsonText
{
    public static string Serialize(JsonValue value, DocumentStyle style, bool escapeNonAscii)
    {
        var text = new StringBuilder();
        switch (style)
        {
            case DocumentStyle.Compact:
                Stringify(text, value, "", "");
                break;
            case DocumentStyle.Spaced:
                Spaced(text, value);
                break;
            case DocumentStyle.Indented:
                Stringify(text, value, "  ", "");
                break;
            default:
                Stringify(text, value, "\t", "");
                break;
        }
        if (!escapeNonAscii) return text.ToString();
        // The characters past U+007F occur only in strings, so an escape of them in the whole text escapes them in the strings.
        var escaped = new StringBuilder(text.Length);
        foreach (var unit in text.ToString())
        {
            if (unit >= 0x80) escaped.Append("\\u").Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
            else escaped.Append(unit);
        }
        return escaped.ToString();
    }

    // JSON.stringify(value, null, gap): an empty container is [] or {}, and each item of a container is on its own line.
    private static void Stringify(StringBuilder text, JsonValue value, string gap, string indent)
    {
        var inner = indent + gap;
        var separator = gap.Length == 0 ? "," : ",\n" + inner;
        switch (value)
        {
            case JsonArray array when array.Items.Count > 0:
                text.Append('[');
                if (gap.Length > 0) text.Append('\n').Append(inner);
                for (var item = 0; item < array.Items.Count; item++)
                {
                    if (item > 0) text.Append(separator);
                    Stringify(text, array.Items[item], gap, inner);
                }
                if (gap.Length > 0) text.Append('\n').Append(indent);
                text.Append(']');
                break;
            case JsonObject record when record.Members.Count > 0:
                text.Append('{');
                if (gap.Length > 0) text.Append('\n').Append(inner);
                for (var member = 0; member < record.Members.Count; member++)
                {
                    if (member > 0) text.Append(separator);
                    Quote(text, record.Members[member].Key);
                    text.Append(gap.Length == 0 ? ":" : ": ");
                    Stringify(text, record.Members[member].Value, gap, inner);
                }
                if (gap.Length > 0) text.Append('\n').Append(indent);
                text.Append('}');
                break;
            default:
                Scalar(text, value);
                break;
        }
    }

    // ", " and ": " on one line, as Python json.dumps writes.
    private static void Spaced(StringBuilder text, JsonValue value)
    {
        switch (value)
        {
            case JsonArray array:
                text.Append('[');
                for (var item = 0; item < array.Items.Count; item++)
                {
                    if (item > 0) text.Append(", ");
                    Spaced(text, array.Items[item]);
                }
                text.Append(']');
                break;
            case JsonObject record:
                text.Append('{');
                for (var member = 0; member < record.Members.Count; member++)
                {
                    if (member > 0) text.Append(", ");
                    Quote(text, record.Members[member].Key);
                    text.Append(": ");
                    Spaced(text, record.Members[member].Value);
                }
                text.Append('}');
                break;
            default:
                Scalar(text, value);
                break;
        }
    }

    private static void Scalar(StringBuilder text, JsonValue value)
    {
        switch (value)
        {
            case JsonString textValue:
                Quote(text, textValue.Value);
                break;
            case JsonNumber number:
                text.Append(double.IsFinite(number.Value) ? NumberText(number.Value) : "null");
                break;
            case JsonBool flag:
                text.Append(flag.Value ? "true" : "false");
                break;
            case JsonArray:
                text.Append("[]");
                break;
            case JsonObject:
                text.Append("{}");
                break;
            default:
                text.Append("null");
                break;
        }
    }

    // QuoteJSONString: the escapes of ", \ and the control characters, and of each lone surrogate.
    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var unit = value[index];
            switch (unit)
            {
                case '\b': text.Append("\\b"); break;
                case '\t': text.Append("\\t"); break;
                case '\n': text.Append("\\n"); break;
                case '\f': text.Append("\\f"); break;
                case '\r': text.Append("\\r"); break;
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                default:
                    var pairStart = char.IsHighSurrogate(unit) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
                    var pairEnd = char.IsLowSurrogate(unit) && index > 0 && char.IsHighSurrogate(value[index - 1]);
                    if (unit < 0x20 || (char.IsSurrogate(unit) && !pairStart && !pairEnd))
                    {
                        text.Append("\\u").Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(unit);
                    }
                    break;
            }
        }
        text.Append('"');
    }

    /// <summary>
    /// Number::toString of ECMAScript for a finite value: the shortest digits that give the value again, in
    /// fixed notation for an exponent from -7 to 20, and else in exponential notation, for example 2.5e-7.
    /// </summary>
    internal static string NumberText(double value)
    {
        if (value == 0) return "0";
        if (value < 0) return "-" + NumberText(-value);
        // "R" gives the shortest digits that give the value again, as ECMAScript does.
        var shortest = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = shortest.IndexOf('E', StringComparison.Ordinal);
        var exponent = exponentAt < 0 ? 0 : int.Parse(shortest.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = exponentAt < 0 ? shortest : shortest[..exponentAt];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        // n: the value is 0.d1d2d3... × 10^n.
        var n = (point < 0 ? mantissa.Length : point) + exponent;
        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits.Trim('0');
        n -= leading;
        var k = digits.Length;
        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
        var e = n - 1;
        var sign = e < 0 ? "-" : "+";
        var head = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return head + "e" + sign + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
    }
}
