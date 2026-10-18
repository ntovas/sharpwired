// No Handwired source. These tests cover the keyboard members of WindowInput (phase 6 task 8): the phase 3
// constructor gives no text and no key, and the init members keep what the window read.

using System.Collections.Immutable;

namespace Sharpwired.Raylib.Tests;

public class WindowInputTests
{
    // No Handwired test. The phase 3 code constructs WindowInput with seven values. The new members then read as
    // empty, so that the phase 3 apps see no keyboard input.
    [Fact]
    public void The_phase_3_constructor_gives_an_empty_text_no_keys_and_a_pixel_scale_of_1()
    {
        var input = new WindowInput(10, 20, true, false, false, 1280, 800);
        Assert.Equal("", input.Text);
        Assert.True(input.Keys.IsEmpty);
        Assert.False(input.Keys.IsDefault);
        Assert.Equal(1.0, input.PixelScale);

        var defaultInput = default(WindowInput);
        Assert.Equal("", defaultInput.Text);
        Assert.True(defaultInput.Keys.IsEmpty);
        Assert.Equal(1.0, defaultInput.PixelScale);
    }

    // No Handwired test. A null text reads as "", and a default key array reads as empty.
    [Fact]
    public void A_null_text_and_a_default_key_array_read_as_empty()
    {
        var input = new WindowInput(0, 0, false, false, false, 100, 100) { Text = null!, Keys = default, PixelScale = 0 };
        Assert.Equal("", input.Text);
        Assert.True(input.Keys.IsEmpty);
        Assert.Equal(1.0, input.PixelScale);
    }

    // No Handwired test. The init members keep the typed text, the key presses in order, and the pixel scale.
    [Fact]
    public void The_init_members_keep_the_text_the_keys_and_the_pixel_scale()
    {
        var input = new WindowInput(0, 0, false, false, false, 100, 100)
        {
            Text = "ab美",
            Keys = [new KeyPress(Key.Backspace, false, false), new KeyPress(Key.Digit2, false, true), new KeyPress(Key.Enter, true, false)],
            PixelScale = 2,
        };
        Assert.Equal("ab美", input.Text);
        Assert.Equal([Key.Backspace, Key.Digit2, Key.Enter], input.Keys.Select(press => press.Key));
        Assert.True(input.Keys[1].Control);
        Assert.False(input.Keys[1].Shift);
        Assert.True(input.Keys[2].Shift);
        Assert.Equal(2.0, input.PixelScale);
    }

    // No Handwired test. The key set of the plan: the edit keys, Enter, the three digits and B.
    [Fact]
    public void The_key_set_holds_the_edit_keys_enter_three_digits_and_b()
    {
        Assert.Equal(
            ["Backspace", "Delete", "Left", "Right", "Home", "End", "Enter", "Digit1", "Digit2", "Digit3", "B"],
            Enum.GetNames<Key>());
        Assert.Equal(ImmutableArray<KeyPress>.Empty, new WindowInput(0, 0, false, false, false, 1, 1).Keys);
    }
}
