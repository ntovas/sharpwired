// Handwired source: the pattern field of src/json-search/assets/json-search.html, an input element that the browser
// edits. A Raylib window has no input element, so this class holds the text and the caret and applies the typed text
// and the edit keys. The caret is an index between two code points, so a surrogate pair is one code point for the
// caret and for each edit key. This file makes no Raylib call.

using System.Text;
using Sharpwired.Raylib;

namespace JsonSearch;

/// <summary>The text of the pattern field and its caret.</summary>
public sealed class PatternField
{
    /// <summary>The text of the field.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The caret as a UTF-16 index into the text. It is never inside a surrogate pair.</summary>
    public int Caret { get; private set; }

    /// <summary>The grid column of the caret: the cells of the code points before it.</summary>
    public int CaretColumn
    {
        get
        {
            var column = 0;
            foreach (var rune in Text.AsSpan(0, Caret).EnumerateRunes()) column += DocumentView.CellWidthOf(rune.Value);
            return column;
        }
    }

    /// <summary>Replace the text, and put the caret at its end. A lone surrogate is left out.</summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = Clean(text, keepControls: true);
        Caret = Text.Length;
    }

    /// <summary>
    /// Put the typed text in at the caret, and move the caret past it. Control characters and lone surrogates are not
    /// text, so they are left out. Returns true when the text changed.
    /// </summary>
    public bool Insert(string typed)
    {
        ArgumentNullException.ThrowIfNull(typed);
        var clean = Clean(typed, keepControls: false);
        if (clean.Length == 0) return false;
        Text = Text.Insert(Caret, clean);
        Caret += clean.Length;
        return true;
    }

    /// <summary>
    /// Apply an edit key. Backspace and Delete remove the code point before and after the caret. Left and Right move
    /// the caret by one code point, and Home and End move it to the ends. Returns true when the text or the caret
    /// changed. The other keys change nothing.
    /// </summary>
    public bool Apply(Key key)
    {
        switch (key)
        {
            case Key.Backspace when Caret > 0:
                {
                    var before = Before(Caret);
                    Text = Text.Remove(before, Caret - before);
                    Caret = before;
                    return true;
                }
            case Key.Delete when Caret < Text.Length:
                Text = Text.Remove(Caret, After(Caret) - Caret);
                return true;
            case Key.Left when Caret > 0:
                Caret = Before(Caret);
                return true;
            case Key.Right when Caret < Text.Length:
                Caret = After(Caret);
                return true;
            case Key.Home when Caret > 0:
                Caret = 0;
                return true;
            case Key.End when Caret < Text.Length:
                Caret = Text.Length;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Each code point of the text and its grid column.</summary>
    public IEnumerable<(int CodePoint, int Column)> Columns()
    {
        var column = 0;
        foreach (var rune in Text.EnumerateRunes())
        {
            yield return (rune.Value, column);
            column += DocumentView.CellWidthOf(rune.Value);
        }
    }

    private int Before(int index) => index >= 2 && char.IsLowSurrogate(Text[index - 1]) && char.IsHighSurrogate(Text[index - 2]) ? index - 2 : index - 1;

    private int After(int index) => index + 1 < Text.Length && char.IsHighSurrogate(Text[index]) && char.IsLowSurrogate(Text[index + 1]) ? index + 2 : index + 1;

    // The text with each lone surrogate left out, and each control character left out unless keepControls.
    private static string Clean(string text, bool keepControls)
    {
        var clean = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                clean.Append(character).Append(text[index + 1]);
                index++;
                continue;
            }
            if (char.IsSurrogate(character)) continue;
            if (!keepControls && char.IsControl(character)) continue;
            clean.Append(character);
        }
        return clean.ToString();
    }
}
