using OpenIGI.Core.Configuration;
using OpenIGI.Desktop;
using OpenIGI.Host.DevTools;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI.Layout;
using OpenIGI.UI;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers picking scrollback text out with the pointer, the text that comes back from it, and the
/// right-click menu that acts on it.
/// </summary>
public sealed class LogSelectionTests
{
    // ---- The range model ----

    [Fact]
    public void Nothing_is_selected_until_a_drag_has_moved()
    {
        var selection = new LogSelection();
        Assert.False(selection.IsActive);

        selection.Begin(2, 4);
        Assert.False(selection.IsActive);

        selection.Extend(2, 9);
        Assert.True(selection.IsActive);
    }

    [Fact]
    public void A_selection_within_one_row_covers_the_columns_between()
    {
        var selection = new LogSelection();
        selection.Begin(1, 3);
        selection.Extend(1, 8);

        Assert.True(selection.TryRangeFor(1, length: 20, out int from, out int to));
        Assert.Equal(3, from);
        Assert.Equal(8, to);
    }

    /// <summary>
    /// Anchor-and-focus rather than start-and-end is what makes an upward drag work without the ends
    /// ever swapping roles.
    /// </summary>
    [Fact]
    public void Dragging_backwards_selects_the_same_range()
    {
        var forward = new LogSelection();
        forward.Begin(1, 3);
        forward.Extend(4, 6);

        var backward = new LogSelection();
        backward.Begin(4, 6);
        backward.Extend(1, 3);

        Assert.Equal(forward.Normalised(), backward.Normalised());
        Assert.Equal(forward.FirstRow, backward.FirstRow);
        Assert.Equal(forward.LastRow, backward.LastRow);
    }

    [Fact]
    public void A_row_in_the_middle_of_a_selection_is_covered_end_to_end()
    {
        var selection = new LogSelection();
        selection.Begin(0, 5);
        selection.Extend(3, 2);

        Assert.True(selection.TryRangeFor(1, length: 40, out int from, out int to));
        Assert.Equal(0, from);
        Assert.Equal(40, to);
    }

    [Fact]
    public void The_first_and_last_rows_are_cut_at_their_columns()
    {
        var selection = new LogSelection();
        selection.Begin(0, 5);
        selection.Extend(2, 3);

        Assert.True(selection.TryRangeFor(0, 40, out int firstFrom, out int firstTo));
        Assert.Equal(5, firstFrom);
        Assert.Equal(40, firstTo);

        Assert.True(selection.TryRangeFor(2, 40, out int lastFrom, out int lastTo));
        Assert.Equal(0, lastFrom);
        Assert.Equal(3, lastTo);
    }

    /// <summary>
    /// A drag that ran off the right of a short line must not paint a wash over empty space.
    /// </summary>
    [Fact]
    public void A_range_is_clamped_to_the_row_it_is_on()
    {
        var selection = new LogSelection();
        selection.Begin(0, 0);
        selection.Extend(0, 500);

        Assert.True(selection.TryRangeFor(0, length: 12, out int from, out int to));
        Assert.Equal(0, from);
        Assert.Equal(12, to);
    }

    [Fact]
    public void Rows_outside_the_selection_report_nothing()
    {
        var selection = new LogSelection();
        selection.Begin(2, 0);
        selection.Extend(3, 5);

        Assert.False(selection.TryRangeFor(1, 40, out _, out _));
        Assert.False(selection.TryRangeFor(9, 40, out _, out _));
    }

    [Fact]
    public void Clearing_drops_everything()
    {
        var selection = new LogSelection();
        selection.Begin(0, 0);
        selection.Extend(2, 2);
        selection.Clear();

        Assert.False(selection.IsActive);
        Assert.Equal(-1, selection.FirstRow);
    }

    // ---- Selecting real scrollback ----

    private static (ConsoleSystem System, ConsoleModel Model, ConsoleBody Body) Build(float width = 800f, float height = 400f)
    {
        var device = new RecordingGraphicsDevice();
        var system = new ConsoleSystem(new CVarRegistry());
        var model = new ConsoleModel(system);
        var theme = new DevConsoleTheme(new MonoFontSet(device, UiFontLibrary.Mono));
        var body = new ConsoleBody(model, theme);

        var context = new LayoutContext(width, height, reference: new UiVec2(width, height));
        body.Measure(context, new UiVec2(width, height));
        body.Arrange(new UiRect(0f, 0f, width, height));

        var batch = new SpriteBatch(device);
        var painter = new UiPainter(batch, context);
        batch.Begin();
        body.Draw(painter);
        batch.End();

        return (system, model, body);
    }

    [Fact]
    public void Selecting_everything_yields_every_visible_line()
    {
        (ConsoleSystem system, _, ConsoleBody body) = Build();
        system.Write("alpha");
        system.Write("bravo");
        system.Write("charlie");

        body.SelectAllLines();
        string text = body.SelectedText();

        Assert.Contains("alpha", text, StringComparison.Ordinal);
        Assert.Contains("bravo", text, StringComparison.Ordinal);
        Assert.Contains("charlie", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// What is copied has to be character-for-character what was drawn, which is only true because one
    /// composition serves both.
    /// </summary>
    [Fact]
    public void A_copied_line_carries_its_category_exactly_as_shown()
    {
        (ConsoleSystem system, _, ConsoleBody body) = Build();
        system.Write(new ConsoleLine("built 4 cascades", ConsoleSeverity.Output, "Render"));

        body.SelectAllLines();

        Assert.Equal("[Render] built 4 cascades", body.SelectedText());
    }

    [Fact]
    public void Copying_everything_ignores_the_selection()
    {
        (ConsoleSystem system, _, ConsoleBody body) = Build();
        system.Write("one");
        system.Write("two");

        string all = body.AllVisibleText();

        Assert.Contains("one", all, StringComparison.Ordinal);
        Assert.Contains("two", all, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filtered_log_only_selects_what_is_shown()
    {
        (ConsoleSystem system, ConsoleModel model, ConsoleBody body) = Build();
        system.Write(new ConsoleLine("kept", ConsoleSeverity.Error));
        system.Write(new ConsoleLine("hidden", ConsoleSeverity.Output));

        model.Filter.SetSeverity(ConsoleSeverity.Output, false);
        body.SelectAllLines();

        Assert.DoesNotContain("hidden", body.SelectedText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_an_empty_log_yields_nothing()
    {
        (_, _, ConsoleBody body) = Build();

        body.SelectAllLines();

        Assert.False(body.Selection.IsActive);
        Assert.Equal(string.Empty, body.SelectedText());
    }

    [Fact]
    public void A_point_outside_the_log_does_not_begin_a_selection()
    {
        (ConsoleSystem system, _, ConsoleBody body) = Build();
        system.Write("a line");

        Assert.False(body.BeginSelect(new UiVec2(-50f, -50f)));
        Assert.False(body.Selection.IsDragging);
    }

    // ---- The context menu ----

    [Fact]
    public void An_item_runs_once_and_closes_the_menu()
    {
        var menu = new ContextMenu();
        int ran = 0;
        menu.Open(new UiVec2(10f, 10f), [new ContextMenuItem("Copy", () => ran++)]);

        Assert.True(menu.IsOpen);
        Assert.True(menu.Invoke(0));
        Assert.Equal(1, ran);
        Assert.False(menu.IsOpen);
    }

    /// <summary>
    /// Disabled items stay in the list so the menu keeps its shape; the same entry has to sit in the
    /// same place whatever the reader happened to do a moment earlier.
    /// </summary>
    [Fact]
    public void A_disabled_item_does_nothing_and_leaves_the_menu_open()
    {
        var menu = new ContextMenu();
        int ran = 0;
        menu.Open(new UiVec2(10f, 10f), [new ContextMenuItem("Copy", () => ran++, IsEnabled: false)]);

        Assert.False(menu.Invoke(0));
        Assert.Equal(0, ran);
        Assert.True(menu.IsOpen);
    }

    [Fact]
    public void Opening_with_no_items_opens_nothing()
    {
        var menu = new ContextMenu();
        menu.Open(new UiVec2(10f, 10f), []);

        Assert.False(menu.IsOpen);
    }
}
