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

    [Fact]
    public void Clicking_a_tab_selects_that_tab_rather_than_the_current_one()
    {
        UiTheme theme = Theme();
        var tabs = new TabStrip(theme, ["Graphics", "Display", "Audio", "Controls"])
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Layout(tabs);

        int changedTo = -1;
        tabs.SelectionChanged += i => changedTo = i;

        // Every tab must be reachable by pointing at it — the whole strip being one control is what made
        // clicking Audio only ever re-activate Graphics.
        for (int i = 0; i < tabs.Tabs.Count; i++)
        {
            Assert.Equal(i, tabs.IndexAt(tabs.RectOf(i).Center));
        }

        tabs.ActivateAt(tabs.RectOf(2).Center);
        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Equal(2, changedTo);
    }

    [Fact]
    public void The_wheel_scrolls_a_viewport_and_a_row_scrolled_out_of_it_cannot_be_clicked()
    {
        UiTheme theme = Theme();
        var scroll = new ScrollView(theme) { Height = 200f };
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        var buttons = new List<Button>();
        for (int i = 0; i < 20; i++)
        {
            var button = new Button(theme, $"Row {i}");
            buttons.Add(button);
            rows.AddChild(button);
        }

        scroll.SetContent(rows);
        Layout(scroll);

        Assert.True(scroll.MaxScroll > 0f);
        Assert.True(scroll.ScrollByWheel(-2f));   // pulling the wheel towards you scrolls down
        Assert.True(scroll.ScrollOffset > 0f);

        // The last row is laid out well below the viewport, so it is still a candidate for directional
        // traversal but must not answer a click at coordinates the viewport does not show.
        Button offScreen = buttons[^1];
        Assert.True(offScreen.Bounds.Bottom > scroll.Bounds.Bottom);
        Assert.True(offScreen.VisibleBounds.IsEmpty);
        Assert.True(offScreen.CanFocus);

        var focus = new FocusManager(scroll);
        Assert.Null(focus.FocusAt(offScreen.Bounds.Center));
    }

    [Fact]
    public void A_viewport_scrolls_a_newly_focused_row_back_into_view()
    {
        UiTheme theme = Theme();
        var scroll = new ScrollView(theme) { Height = 200f };
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        var buttons = new List<Button>();
        for (int i = 0; i < 20; i++)
        {
            var button = new Button(theme, $"Row {i}");
            buttons.Add(button);
            rows.AddChild(button);
        }

        scroll.SetContent(rows);
        Layout(scroll);

        scroll.EnsureVisible(buttons[^1].Bounds);
        Assert.Equal(scroll.MaxScroll, scroll.ScrollOffset, 2);
    }

    [Fact]
    public void A_dropdown_button_lists_a_disabled_choice_but_will_not_pick_it()
    {
        UiTheme theme = Theme();
        var menu = new DropdownButton(theme, "PROJECT I.G.I.", ["Project I.G.I.", "I.G.I. 2"])
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            CurrentIndex = 0,
            ItemEnabled = index => index == 0,
        };
        Layout(menu);

        int picked = -1;
        menu.ItemSelected += i => picked = i;

        // Shut, it answers only for itself; open, it answers for its list too — which is what lets a click
        // below the bar reach it at all.
        UiRect shut = menu.PointerBounds;
        menu.Activate();
        Assert.True(menu.IsOpen);
        Assert.True(menu.PointerBounds.Height > shut.Height);
        Layout(menu);

        // The second row is the unavailable title. Clicking it must do nothing at all — not select it, and
        // not quietly shut the menu as though it had worked.
        var second = new UiVec2(menu.Bounds.X + 10f, menu.Bounds.Bottom + (menu.PointerBounds.Height - menu.Bounds.Height) * 0.75f);
        menu.ActivateAt(second);
        Assert.Equal(-1, picked);
        Assert.True(menu.IsOpen);
        Assert.Equal(0, menu.CurrentIndex);
    }

    [Fact]
    public void A_dropdown_steps_over_options_that_cannot_be_chosen()
    {
        UiTheme theme = Theme();
        var dropdown = new Dropdown(theme, "Game", ["Installed", "Missing", "Also installed"])
        {
            OptionEnabled = index => index != 1,
        };

        dropdown.Step(1);
        Assert.Equal(2, dropdown.SelectedIndex); // the middle option is not landed on
        dropdown.Step(-1);
        Assert.Equal(0, dropdown.SelectedIndex);
    }
}
