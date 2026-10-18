// Handwired source: the pattern field of src/json-search/assets/json-search.html, an input element that the browser
// edits. The window has no input element, so PatternField edits the text. It handles typed text at the caret, the
// edit keys, and a surrogate pair as one code point.

using Sharpwired.Raylib;

namespace JsonSearch.Tests;

public sealed class PatternFieldTests
{
    [Fact]
    public void Typed_text_goes_in_at_the_caret_and_the_caret_moves_past_it()
    {
        var field = new PatternField();
        Assert.Equal(("", 0, 0), (field.Text, field.Caret, field.CaretColumn));
        Assert.True(field.Insert("eror"));
        Assert.Equal(("eror", 4), (field.Text, field.Caret));
        Assert.True(field.Apply(Key.Left));
        Assert.True(field.Apply(Key.Left));
        Assert.True(field.Insert("r"));
        Assert.Equal(("error", 3), (field.Text, field.Caret));
        Assert.False(field.Insert(""));
        // Control characters are not text: a tab, a line feed, an escape, a delete.
        Assert.False(field.Insert("\t\n\u001b\u007f"));
        Assert.Equal("error", field.Text);
    }

    [Fact]
    public void Each_edit_key_changes_the_text_or_the_caret()
    {
        var field = new PatternField();
        field.SetText("abcd");
        Assert.Equal(4, field.Caret);
        Assert.True(field.Apply(Key.Backspace));
        Assert.Equal(("abc", 3), (field.Text, field.Caret));
        Assert.True(field.Apply(Key.Home));
        Assert.Equal(0, field.Caret);
        Assert.True(field.Apply(Key.Delete));
        Assert.Equal(("bc", 0), (field.Text, field.Caret));
        Assert.True(field.Apply(Key.Right));
        Assert.Equal(1, field.Caret);
        Assert.True(field.Apply(Key.End));
        Assert.Equal(2, field.Caret);
        // A key that the field does not use changes nothing.
        Assert.False(field.Apply(Key.Enter));
        Assert.False(field.Apply(Key.Digit1));
        Assert.Equal(("bc", 2), (field.Text, field.Caret));
    }

    [Fact]
    public void At_each_end_the_keys_that_go_past_the_end_change_nothing()
    {
        var field = new PatternField();
        field.SetText("ab");
        Assert.False(field.Apply(Key.Right));
        Assert.False(field.Apply(Key.Delete));
        Assert.False(field.Apply(Key.End));
        Assert.Equal(("ab", 2), (field.Text, field.Caret));
        field.Apply(Key.Home);
        Assert.False(field.Apply(Key.Left));
        Assert.False(field.Apply(Key.Backspace));
        Assert.False(field.Apply(Key.Home));
        Assert.Equal(("ab", 0), (field.Text, field.Caret));

        var empty = new PatternField();
        foreach (var key in new[] { Key.Backspace, Key.Delete, Key.Left, Key.Right, Key.Home, Key.End }) Assert.False(empty.Apply(key));
    }

    [Fact]
    public void A_surrogate_pair_is_one_code_point_for_the_caret_and_each_edit_key()
    {
        var field = new PatternField();
        field.Insert("a😀b");
        Assert.Equal(4, field.Caret);
        // 😀 is one code point of two UTF-16 units, and of East Asian width "wide", so it takes two columns.
        Assert.Equal(4, field.CaretColumn);
        field.Apply(Key.Left);
        Assert.Equal(3, field.Caret);
        field.Apply(Key.Left);
        Assert.Equal(1, field.Caret);
        Assert.Equal(1, field.CaretColumn);
        field.Apply(Key.Delete);
        Assert.Equal(("ab", 1), (field.Text, field.Caret));
        field.SetText("😀😀");
        field.Apply(Key.Backspace);
        Assert.Equal(("😀", 2), (field.Text, field.Caret));
        // A lone surrogate is not a code point that a person types, so the field leaves it out.
        field.Insert("\ud800x\udc00");
        Assert.Equal("😀x", field.Text);
    }

    [Fact]
    public void A_wide_code_point_takes_two_columns_before_the_caret()
    {
        var field = new PatternField();
        field.Insert("東京a");
        Assert.Equal(5, field.CaretColumn);
        Assert.Equal([(0x6771, 0), (0x4eac, 2), ('a', 4)], field.Columns().Select(cell => (cell.CodePoint, cell.Column)));
    }
}
