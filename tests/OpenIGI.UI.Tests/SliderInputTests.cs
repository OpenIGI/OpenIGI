using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Covers a slider taken hold of with the pointer and a number typed into its readout.</summary>
public sealed class SliderInputTests
{
    private static Slider LaidOut(float minimum = 0f, float maximum = 10f, float value = 5f, float step = 1f)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        var theme = new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
        var slider = new Slider(theme, "Volume", minimum, maximum, value, step);
        var context = new LayoutContext(1920f, 1080f);
        slider.Measure(context, context.SafeRect.Size);
        slider.Arrange(context.SafeRect);
        return slider;
    }

    private static UiVec2 OnTrack(Slider slider, float t) =>
        new(slider.TrackBounds.X + (slider.TrackBounds.Width * t), slider.TrackBounds.Center.Y);

    [Fact]
    public void A_press_on_the_track_takes_hold_of_the_thumb_and_it_follows_the_pointer()
    {
        Slider slider = LaidOut();

        Assert.True(slider.CapturesPointer(OnTrack(slider, 0.2f)));
        Assert.Equal(2f, slider.Value, 3);

        slider.PointerDragged(OnTrack(slider, 0.9f));
        Assert.Equal(9f, slider.Value, 3);

        // Past the end it stays at the end.
        slider.PointerDragged(new UiVec2(slider.TrackBounds.Right + 500f, slider.TrackBounds.Center.Y));
        Assert.Equal(10f, slider.Value, 3);

        slider.PointerReleased(OnTrack(slider, 1f), cancelled: false);
        Assert.Equal(10f, slider.Value, 3);
    }

    [Fact]
    public void An_abandoned_drag_puts_the_value_back()
    {
        Slider slider = LaidOut();
        Assert.True(slider.CapturesPointer(OnTrack(slider, 0f)));
        slider.PointerDragged(OnTrack(slider, 0.7f));
        slider.PointerReleased(OnTrack(slider, 0.7f), cancelled: true);
        Assert.Equal(5f, slider.Value, 3);
    }

    [Fact]
    public void A_press_on_the_caption_is_not_a_drag()
    {
        Slider slider = LaidOut();
        Assert.False(slider.CapturesPointer(new UiVec2(slider.Bounds.X + 4f, slider.Bounds.Center.Y)));
        Assert.Equal(5f, slider.Value, 3);
    }

    [Fact]
    public void A_click_on_the_readout_opens_it_for_typing()
    {
        Slider slider = LaidOut();
        slider.ActivateAt(new UiVec2(slider.Bounds.Right - 4f, slider.Bounds.Center.Y));
        Assert.True(slider.IsEditing);
        Assert.True(((ITextEntry)slider).IsTakingText);
    }

    [Fact]
    public void A_typed_number_replaces_the_value_and_is_kept_on_enter()
    {
        Slider slider = LaidOut(step: 2f);
        slider.BeginEdit();
        Assert.Equal("5", slider.EntryText);

        // The first key replaces the number shown; letters are not part of a number.
        Assert.True(slider.Type('7'));
        Assert.False(slider.Type('x'));
        Assert.True(slider.Type(','));
        Assert.True(slider.Type('5'));
        Assert.Equal("7.5", slider.EntryText);

        slider.EndEntry(commit: true);
        Assert.False(slider.IsEditing);
        Assert.Equal(7.5f, slider.Value, 3); // not snapped to the step
    }

    [Fact]
    public void A_typed_number_outside_the_range_is_held_to_the_nearer_end()
    {
        Slider slider = LaidOut();
        slider.BeginEdit();
        slider.Type('9');
        slider.Type('9');
        slider.EndEntry(commit: true);
        Assert.Equal(10f, slider.Value, 3);

        slider.BeginEdit();
        slider.Type('-');
        slider.Type('3');
        slider.EndEntry(commit: true);
        Assert.Equal(0f, slider.Value, 3);
    }

    [Fact]
    public void Escape_drops_a_typed_number_and_nonsense_changes_nothing()
    {
        Slider slider = LaidOut();
        slider.BeginEdit();
        slider.Type('8');
        slider.EndEntry(commit: false);
        Assert.Equal(5f, slider.Value, 3);

        slider.BeginEdit();
        slider.Type('-');
        slider.EndEntry(commit: true);
        Assert.Equal(5f, slider.Value, 3);
    }

    [Fact]
    public void Backspace_edits_the_typed_number()
    {
        Slider slider = LaidOut();
        slider.BeginEdit();
        Assert.True(slider.Backspace()); // the shown number goes as a whole
        Assert.Equal(string.Empty, slider.EntryText);
        slider.Type('4');
        slider.Type('2');
        slider.Backspace();
        slider.EndEntry(commit: true);
        Assert.Equal(4f, slider.Value, 3);
    }

    [Fact]
    public void A_readout_that_is_not_the_value_takes_what_it_shows_typed()
    {
        Slider slider = LaidOut();
        slider.FromTyped = shown => shown / 10f;
        slider.BeginEdit();
        slider.Type('6');
        slider.Type('0');
        slider.EndEntry(commit: true);
        Assert.Equal(6f, slider.Value, 3);
    }

    [Fact]
    public void Losing_the_focus_keeps_a_number_being_typed()
    {
        Slider slider = LaidOut();
        slider.SetFocused(true);
        slider.BeginEdit();
        slider.Type('3');
        slider.SetFocused(false);
        Assert.False(slider.IsEditing);
        Assert.Equal(3f, slider.Value, 3);
    }

    [Fact]
    public void While_a_number_is_typed_left_and_right_do_not_step_the_value()
    {
        Slider slider = LaidOut();
        slider.BeginEdit();
        Assert.True(slider.HandleNavigate(Focus.NavDirection.Right));
        Assert.Equal(5f, slider.Value, 3);
    }
}
