using OpenIGI.Input;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests.Editor;

/// <summary>The editor kit's desktop input: focus, typing, scrubbing, clicks, menus and shortcuts, driven like a user.</summary>
public sealed class EditorInputTests
{
    private static (EditorHarness Ui, MenuBar Bar, TextField Text, NumberField Number, Button Button) Build()
    {
        EditorHarness ui = new();
        MenuBar bar = new(ui.Theme);
        TextField text = new(ui.Theme);
        NumberField number = new(ui.Theme) { Step = 0.5, Decimals = 2 };
        Button button = new(ui.Theme, "Go");
        StackPanel stack = new() { Orientation = Orientation.Vertical, Spacing = 8f };
        stack.AddChild(bar);
        stack.AddChild(text);
        stack.AddChild(number);
        stack.AddChild(new StackPanel { Orientation = Orientation.Horizontal }.AddChild(button).Parent!);
        ui.Root.Content = stack;
        ui.Frame();
        return (ui, bar, text, number, button);
    }

    /// <summary>Clicking a field focuses it; typing edits; Enter commits once; Escape reverts uncommitted typing.</summary>
    [Fact]
    public void A_text_field_types_commits_and_reverts()
    {
        (EditorHarness ui, _, TextField text, _, _) = Build();
        List<string> commits = [];
        text.Committed += commits.Add;

        ui.Click(text.Bounds.Center);
        Assert.Same(text, ui.Root.Focused);
        ui.Type("Crate");
        ui.Key(KeyCode.Return);
        Assert.Equal(["Crate"], commits);

        ui.Type("_2");
        ui.Key(KeyCode.Escape);
        Assert.Equal("Crate", text.Text);

        ui.Key(KeyCode.Left, KeyModifiers.Control);
        ui.Key(KeyCode.Backspace, KeyModifiers.None);
        Assert.Equal("Crate", text.Text);
        ui.Key(KeyCode.End);
        ui.Key(KeyCode.Backspace, KeyModifiers.Control);
        Assert.Equal(string.Empty, text.Text);
    }

    /// <summary>Leaving a field commits it, and the clipboard keys go through the root's clipboard.</summary>
    [Fact]
    public void Focus_loss_commits_and_the_clipboard_works()
    {
        (EditorHarness ui, _, TextField text, _, Button button) = Build();
        string? committed = null;
        text.Committed += value => committed = value;

        ui.Click(text.Bounds.Center);
        ui.Type("abc");
        ui.Key(KeyCode.A, KeyModifiers.Control);
        ui.Key(KeyCode.C, KeyModifiers.Control);
        Assert.Equal("abc", ui.Root.Clipboard.Text);
        ui.Key(KeyCode.End);
        ui.Key(KeyCode.V, KeyModifiers.Control);

        ui.Click(button.Bounds.Center);
        Assert.Equal("abcabc", committed);
        Assert.Null(ui.Root.Focused);
    }

    /// <summary>Dragging a number field scrubs it, live then final; a plain click starts typing instead.</summary>
    [Fact]
    public void A_number_field_scrubs_and_types()
    {
        (EditorHarness ui, _, _, NumberField number, _) = Build();
        List<(double Value, bool Final)> changes = [];
        number.ValueChanged += (value, final) => changes.Add((value, final));

        UiVec2 start = number.Bounds.Center;
        ui.Drag(start, new UiVec2(start.X + 20f, start.Y));
        Assert.Equal(10.0, number.Value);
        Assert.Contains(changes, static c => !c.Final);
        Assert.True(changes[^1].Final);
        Assert.Null(ui.Root.Focused);

        ui.Click(number.Bounds.Center);
        Assert.Same(number, ui.Root.Focused);
        ui.Type("2.5");
        ui.Key(KeyCode.Return);
        Assert.Equal(2.5, number.Value);
    }

    /// <summary>A click is a press and a release over the button; dragging off cancels it.</summary>
    [Fact]
    public void A_button_clicks_on_release_inside()
    {
        (EditorHarness ui, _, _, _, Button button) = Build();
        int clicks = 0;
        button.Clicked += () => clicks++;

        ui.Click(button.Bounds.Center);
        Assert.Equal(1, clicks);

        ui.Press(button.Bounds.Center);
        ui.MoveTo(new UiVec2(button.Bounds.Right + 50f, button.Bounds.Center.Y));
        ui.Release(new UiVec2(button.Bounds.Right + 50f, button.Bounds.Center.Y));
        Assert.Equal(1, clicks);
    }

    /// <summary>The bar opens a menu, a submenu opens on hover, and choosing an item runs it and closes the chain.</summary>
    [Fact]
    public void Menus_open_navigate_and_choose()
    {
        (EditorHarness ui, MenuBar bar, _, _, _) = Build();
        string? chosen = null;
        bar.Add("File", [
            MenuItem.Command("New", () => chosen = "new"),
            MenuItem.Separator,
            MenuItem.Submenu("Recent", [MenuItem.Command("m1", () => chosen = "m1")]),
        ]);
        ui.Frame();

        ui.Click(new UiVec2(bar.Bounds.X + 20f, bar.Bounds.Center.Y));
        _ = Assert.IsType<MenuPopup>(Assert.Single(ui.Root.Popups));

        ui.Key(KeyCode.Down);
        ui.Key(KeyCode.Down);
        ui.Key(KeyCode.Right);
        Assert.Equal(2, ui.Root.Popups.Count);
        ui.Key(KeyCode.Return);
        Assert.Equal("m1", chosen);
        Assert.Empty(ui.Root.Popups);

        ui.Click(new UiVec2(bar.Bounds.X + 20f, bar.Bounds.Center.Y));
        MenuPopup menu = Assert.IsType<MenuPopup>(Assert.Single(ui.Root.Popups));
        UiVec2 first = new(menu.Bounds.Center.X, menu.Bounds.Y + ui.Theme.Px(4f) + (ui.Theme.RowHeight * 0.5f));
        ui.Click(first);
        Assert.Equal("new", chosen);
    }

    /// <summary>A press outside an open popup closes it and is swallowed; Escape closes too.</summary>
    [Fact]
    public void Popups_dismiss_lightly()
    {
        (EditorHarness ui, _, _, _, Button button) = Build();
        int clicks = 0;
        button.Clicked += () => clicks++;

        MenuPopup.OpenAt(ui.Root, new UiVec2(600f, 300f), [MenuItem.Command("x", static () => { })]);
        ui.Frame();
        ui.Click(button.Bounds.Center);
        Assert.Empty(ui.Root.Popups);
        Assert.Equal(0, clicks);

        MenuPopup.OpenAt(ui.Root, new UiVec2(600f, 300f), [MenuItem.Command("x", static () => { })]);
        ui.Key(KeyCode.Escape);
        Assert.Empty(ui.Root.Popups);
    }

    /// <summary>Keys nothing focused uses fall through to the shortcut table; a focused field keeps its own keys.</summary>
    [Fact]
    public void Shortcuts_run_when_nothing_takes_the_key()
    {
        (EditorHarness ui, _, TextField text, _, _) = Build();
        int undos = 0;
        ui.Root.Actions.Add(new EditorAction("edit.undo", "Undo", () => undos++) { Shortcut = KeyChord.Parse("Ctrl+Z") });
        ui.Root.Actions.Add(new EditorAction("edit.select-all", "Select All", () => undos += 100) { Shortcut = KeyChord.Parse("Ctrl+A") });

        ui.Key(KeyCode.Z, KeyModifiers.Control);
        Assert.Equal(1, undos);

        ui.Click(text.Bounds.Center);
        ui.Key(KeyCode.A, KeyModifiers.Control);
        Assert.Equal(1, undos);
    }

    /// <summary>Two presses close together are a double click.</summary>
    [Fact]
    public void Double_click_selects_a_word()
    {
        (EditorHarness ui, _, TextField text, _, _) = Build();
        text.Text = "alpha beta";
        ui.Frame();
        UiVec2 onAlpha = new(text.Bounds.X + ui.Theme.Px(10f), text.Bounds.Center.Y);

        ui.Click(onAlpha);
        ui.Click(onAlpha);
        ui.Key(KeyCode.C, KeyModifiers.Control);
        Assert.Equal("alpha", ui.Root.Clipboard.Text);
    }

    /// <summary>Chords print and parse the way menus show them.</summary>
    [Theory]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("F2")]
    [InlineData("Del")]
    [InlineData("Ctrl+1")]
    public void Chords_round_trip(string text) => Assert.Equal(text, KeyChord.Parse(text).ToString());
}
