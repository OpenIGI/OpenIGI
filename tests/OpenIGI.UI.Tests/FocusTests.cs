using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Covers directional focus traversal and the interactive controls' input handling, without GL.</summary>
public sealed class FocusTests
{
    private static UiTheme Theme()
    {
        // A measurement-only font is enough: focus and control logic never draw.
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Saira-Regular.ttf");
        return new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
    }

    private static void Layout(UiElement root)
    {
        var context = new LayoutContext(1920f, 1080f);
        root.Measure(context, context.SafeRect.Size);
        root.Arrange(context.SafeRect);
    }

    [Fact]
    public void Down_moves_focus_to_the_next_row_of_a_vertical_menu()
    {
        UiTheme theme = Theme();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        var a = new Button(theme, "Campaign");
        var b = new Button(theme, "Settings");
        var c = new Button(theme, "Quit");
        stack.AddChild(a);
        stack.AddChild(b);
        stack.AddChild(c);
        Layout(stack);

        var focus = new FocusManager(stack);
        focus.FocusFirst();
        Assert.Same(a, focus.Focused);

        Assert.True(focus.Move(NavDirection.Down));
        Assert.Same(b, focus.Focused);

        Assert.True(focus.Move(NavDirection.Down));
        Assert.Same(c, focus.Focused);

        // Nothing below the last row.
        Assert.False(focus.Move(NavDirection.Down));
        Assert.Same(c, focus.Focused);
    }

    [Fact]
    public void Up_returns_to_the_previous_row()
    {
        UiTheme theme = Theme();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        var a = new Button(theme, "One");
        var b = new Button(theme, "Two");
        stack.AddChild(a);
        stack.AddChild(b);
        Layout(stack);

        var focus = new FocusManager(stack);
        focus.Focus(b);
        Assert.True(focus.Move(NavDirection.Up));
        Assert.Same(a, focus.Focused);
    }

    [Fact]
    public void Traversal_prefers_the_neighbour_dead_ahead_over_a_nearer_one_to_the_side()
    {
        UiTheme theme = Theme();
        // A 2x2 grid: from top-left, Down should reach bottom-left, not bottom-right.
        var grid = new Grid { Width = 800f, Height = 400f };
        grid.ColumnDefinitions.Add(GridLength.Star);
        grid.ColumnDefinitions.Add(GridLength.Star);
        grid.RowDefinitions.Add(GridLength.Star);
        grid.RowDefinitions.Add(GridLength.Star);
        var topLeft = new Button(theme, "TL");
        var topRight = new Button(theme, "TR");
        var bottomLeft = new Button(theme, "BL");
        var bottomRight = new Button(theme, "BR");
        grid.Add(topLeft, 0, 0);
        grid.Add(topRight, 0, 1);
        grid.Add(bottomLeft, 1, 0);
        grid.Add(bottomRight, 1, 1);
        Layout(grid);

        var focus = new FocusManager(grid);
        focus.Focus(topLeft);
        focus.Move(NavDirection.Down);
        Assert.Same(bottomLeft, focus.Focused);

        focus.Focus(topLeft);
        focus.Move(NavDirection.Right);
        Assert.Same(topRight, focus.Focused);
    }

    [Fact]
    public void Disabled_controls_are_skipped()
    {
        UiTheme theme = Theme();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        var a = new Button(theme, "A");
        var disabled = new Button(theme, "B") { IsEnabled = false };
        var c = new Button(theme, "C");
        stack.AddChild(a);
        stack.AddChild(disabled);
        stack.AddChild(c);
        Layout(stack);

        var focus = new FocusManager(stack);
        focus.Focus(a);
        focus.Move(NavDirection.Down);
        Assert.Same(c, focus.Focused);
    }

    [Fact]
    public void Pointer_focuses_the_control_under_it()
    {
        UiTheme theme = Theme();
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        var a = new Button(theme, "A");
        var b = new Button(theme, "B");
        stack.AddChild(a);
        stack.AddChild(b);
        Layout(stack);

        var focus = new FocusManager(stack);
        IFocusable? hit = focus.FocusAt(b.Bounds.Center);
        Assert.Same(b, hit);
        Assert.Same(b, focus.Focused);
    }

    [Fact]
    public void Button_activation_raises_its_event()
    {
        UiTheme theme = Theme();
        var button = new Button(theme, "Go");
        int count = 0;
        button.Activated += _ => count++;
        button.Activate();
        Assert.Equal(1, count);
    }

    [Fact]
    public void Slider_consumes_left_and_right_and_steps_its_value()
    {
        UiTheme theme = Theme();
        var slider = new Slider(theme, "Volume", 0f, 10f, 5f, step: 1f);
        Assert.True(slider.HandleNavigate(NavDirection.Right));
        Assert.Equal(6f, slider.Value, 3);
        Assert.True(slider.HandleNavigate(NavDirection.Left));
        Assert.Equal(5f, slider.Value, 3);
        Assert.False(slider.HandleNavigate(NavDirection.Up));
    }

    [Fact]
    public void Slider_clamps_to_its_range()
    {
        UiTheme theme = Theme();
        var slider = new Slider(theme, "X", 0f, 1f, 1f, step: 1f);
        slider.HandleNavigate(NavDirection.Right);
        Assert.Equal(1f, slider.Value, 3);
    }

    [Fact]
    public void Toggle_flips_on_activate_and_takes_a_definite_value_from_left_right()
    {
        UiTheme theme = Theme();
        var toggle = new Toggle(theme, "Fullscreen", value: false);
        toggle.Activate();
        Assert.True(toggle.Value);
        Assert.True(toggle.HandleNavigate(NavDirection.Left));
        Assert.False(toggle.Value);
        Assert.False(toggle.HandleNavigate(NavDirection.Left)); // already off, nothing to consume
    }

    [Fact]
    public void Dropdown_cycles_and_wraps()
    {
        UiTheme theme = Theme();
        var dropdown = new Dropdown(theme, "Preset", ["Low", "High", "Ultra"], selected: 0);
        dropdown.HandleNavigate(NavDirection.Right);
        Assert.Equal(1, dropdown.SelectedIndex);
        dropdown.HandleNavigate(NavDirection.Left);
        dropdown.HandleNavigate(NavDirection.Left);
        Assert.Equal(2, dropdown.SelectedIndex); // wrapped past the start
        Assert.Equal("Ultra", dropdown.SelectedOption);
    }

    [Fact]
    public void ListView_moves_selection_and_activates()
    {
        UiTheme theme = Theme();
        var list = new ListView(theme, ["Slot 1", "Slot 2", "Slot 3"]);
        int activated = -1;
        list.ItemActivated += i => activated = i;

        Assert.True(list.HandleNavigate(NavDirection.Down));
        Assert.Equal(1, list.SelectedIndex);
        list.Activate();
        Assert.Equal(1, activated);
    }

    [Fact]
    public void KeyBinding_row_captures_the_next_key()
    {
        UiTheme theme = Theme();
        var row = new KeyBindingRow(theme, "Fire", OpenIGI.Input.KeyCode.LeftControl);
        row.Activate();
        Assert.True(row.IsListening);
        Assert.True(row.TryCaptureKey(OpenIGI.Input.KeyCode.Space));
        Assert.False(row.IsListening);
        Assert.Equal(OpenIGI.Input.KeyCode.Space, row.BoundKey);
    }

    [Fact]
    public void KeyBinding_row_escape_cancels_without_changing()
    {
        UiTheme theme = Theme();
        var row = new KeyBindingRow(theme, "Fire", OpenIGI.Input.KeyCode.LeftControl);
        row.Activate();
        Assert.True(row.TryCaptureKey(OpenIGI.Input.KeyCode.Escape));
        Assert.Equal(OpenIGI.Input.KeyCode.LeftControl, row.BoundKey);
    }
}
