using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Covers the editing model shared by the console prompt, the variable browser's filter box and the
/// command palette's query.
/// </summary>
public sealed class TextEditStateTests
{
    private static TextEditState With(string text, int caret, int? anchor = null)
    {
        var state = new TextEditState();
        state.SetText(text);
        state.MoveTo(caret);
        if (anchor is { } a)
        {
            state.MoveTo(a);
            state.MoveTo(caret, extend: true);
        }

        return state;
    }

    [Fact]
    public void Setting_text_puts_the_caret_at_the_end_with_nothing_selected()
    {
        var state = new TextEditState();
        state.SetText("r_vsync");

        Assert.Equal("r_vsync", state.Text);
        Assert.Equal(7, state.Caret);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void Insert_places_text_at_the_caret()
    {
        TextEditState state = With("ac", 1);
        Assert.True(state.Insert("b"));

        Assert.Equal("abc", state.Text);
        Assert.Equal(2, state.Caret);
    }

    [Fact]
    public void Control_characters_are_dropped_so_a_pasted_block_still_yields_its_text()
    {
        var state = new TextEditState();
        Assert.True(state.Insert("a\r\nb\t"));

        Assert.Equal("ab", state.Text);
    }

    [Fact]
    public void Insert_replaces_the_selection()
    {
        TextEditState state = With("hello world", 11, anchor: 6);
        state.Insert("there");

        Assert.Equal("hello there", state.Text);
        Assert.False(state.HasSelection);
    }

    [Fact]
    public void Backspace_removes_the_selection_rather_than_one_character()
    {
        TextEditState state = With("hello world", 11, anchor: 5);
        Assert.True(state.Backspace());

        Assert.Equal("hello", state.Text);
        Assert.Equal(5, state.Caret);
    }

    [Fact]
    public void Backspace_at_the_start_does_nothing()
    {
        TextEditState state = With("abc", 0);
        Assert.False(state.Backspace());
        Assert.Equal("abc", state.Text);
    }

    [Fact]
    public void Delete_removes_the_character_after_the_caret()
    {
        TextEditState state = With("abc", 1);
        Assert.True(state.Delete());
        Assert.Equal("ac", state.Text);
    }

    [Fact]
    public void Selection_extends_in_both_directions_from_its_anchor()
    {
        TextEditState state = With("hello", 2);
        state.MoveTo(4, extend: true);
        Assert.Equal("ll", state.SelectedText);

        state.MoveTo(0, extend: true);
        Assert.Equal("he", state.SelectedText);
    }

    /// <summary>
    /// The behaviour every text field has: an arrow key with text selected collapses to the near edge
    /// instead of stepping from the caret.
    /// </summary>
    [Fact]
    public void An_arrow_collapses_a_selection_to_its_near_edge()
    {
        TextEditState state = With("hello world", 11, anchor: 6);

        state.Move(-1);
        Assert.False(state.HasSelection);
        Assert.Equal(6, state.Caret);
    }

    [Fact]
    public void Word_motion_steps_over_words_and_their_trailing_space()
    {
        TextEditState state = With("r_vsync 1 0", 0);

        state.MoveByWord(1);
        Assert.Equal(8, state.Caret);

        state.MoveByWord(-1);
        Assert.Equal(0, state.Caret);
    }

    [Fact]
    public void Select_all_covers_the_whole_line()
    {
        TextEditState state = With("r_vsync 1", 3);
        state.SelectAll();

        Assert.Equal("r_vsync 1", state.SelectedText);
    }

    [Fact]
    public void Select_word_covers_the_word_the_caret_sits_in()
    {
        TextEditState state = With("hello world", 8);
        state.SelectWord();

        Assert.Equal("world", state.SelectedText);
    }

    [Fact]
    public void The_caret_never_leaves_the_text()
    {
        TextEditState state = With("abc", 0);

        state.Move(-50);
        Assert.Equal(0, state.Caret);

        state.Move(50);
        Assert.Equal(3, state.Caret);
    }
}
