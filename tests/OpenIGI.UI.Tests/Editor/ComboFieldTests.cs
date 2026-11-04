using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The combo field: a text field offering the valid values in a dropdown, taking anything typed too.</summary>
public sealed class ComboFieldTests
{
    private static readonly ComboItem[] Models = [new("106_01_1", "pistol"), new("113_01_1", "casing"), new("003_01_1", "guard"), new("210_01_1", "crate")];

    /// <summary>Typing narrows the list to what matches; the arrow keys and Enter pick from it.</summary>
    [Fact]
    public void Typing_filters_and_enter_picks()
    {
        (EditorHarness ui, ComboField combo) = Mount();
        string? taken = null;
        combo.Committed += value => taken = value;

        ui.Click(new UiVec2(combo.Bounds.X + 20, combo.Bounds.Center.Y));
        ui.Key(KeyCode.A, KeyModifiers.Control);
        ui.Type("01_1");
        Assert.Equal(4, combo.Shown.Count); // every model here holds 01_1
        ui.Key(KeyCode.A, KeyModifiers.Control);
        ui.Type("13");
        Assert.True(combo.Shown.Count > 0, $"text '{combo.Text}' focused {ui.Root.Focused?.GetType().Name}");
        Assert.Equal("113_01_1", combo.Shown[0].Value);

        ui.Key(KeyCode.Return);
        Assert.Equal("113_01_1", taken);
        Assert.Equal("113_01_1", combo.Text);
        Assert.Empty(combo.Shown);
    }

    /// <summary>What matches nothing is still taken as typed.</summary>
    [Fact]
    public void Anything_typed_is_taken()
    {
        (EditorHarness ui, ComboField combo) = Mount();
        string? taken = null;
        combo.Committed += value => taken = value;

        ui.Click(new UiVec2(combo.Bounds.X + 20, combo.Bounds.Center.Y));
        ui.Key(KeyCode.A, KeyModifiers.Control);
        ui.Type("my_own_model");
        Assert.Empty(combo.Shown);
        ui.Key(KeyCode.Return);

        Assert.Equal("my_own_model", taken);
    }

    /// <summary>The arrow opens every value; a click on one takes it.</summary>
    [Fact]
    public void The_arrow_opens_every_value_and_a_click_picks()
    {
        (EditorHarness ui, ComboField combo) = Mount();
        string? taken = null;
        combo.Committed += value => taken = value;

        ui.Click(new UiVec2(combo.Bounds.Right - 6, combo.Bounds.Center.Y));
        Assert.Equal(Models.Length, combo.Shown.Count);

        // The third row, under the field.
        ui.Click(new UiVec2(combo.Bounds.X + 30, combo.Bounds.Bottom + ui.Theme.Px(3f) + (ui.Theme.RowHeight * 2.5f)));
        Assert.Equal("003_01_1", taken);
    }

    private static (EditorHarness, ComboField) Mount()
    {
        EditorHarness ui = new();
        ComboField combo = new(ui.Theme) { Text = "106_01_1", Suggest = () => Models };
        StackHost host = new(ui.Theme, combo);
        ui.Root.Content = host;
        ui.Frame();
        ui.Frame();
        return (ui, combo);
    }

    // Puts the field at the top of the window, a usual width.
    private sealed class StackHost : EditorElement
    {
        private readonly ComboField _combo;

        public StackHost(OpenIGI.UI.Editor.Theme.EditorTheme theme, ComboField combo)
            : base(theme)
        {
            _combo = combo;
            AddChild(combo);
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
            _combo.Place(context, new UiRect(contentRect.X + 20, contentRect.Y + 20, 300, Theme.ControlHeight));
    }
}
