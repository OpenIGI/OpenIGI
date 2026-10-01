using System.Globalization;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's settings: two columns of glass panels, each a group of rows — the same toggle, slider and
/// select rows the game's own settings use, in the launcher's taller ruled style.
/// </summary>
internal static class SettingsPage
{
    public static UiElement Build(UiTheme theme)
    {
        var columns = new Grid { ColumnSpacing = 20f, VerticalAlignment = VerticalAlignment.Top };
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Auto);

        var bandwidth = new Slider(theme, "Bandwidth limit", 0f, 200f, 0f, 5f)
        {
            Hint = "Applies to game and content downloads",
            Format = v => v <= 0f ? "UNLIMITED" : string.Create(CultureInfo.InvariantCulture, $"{v:0} MB/s"),
        };

        columns.Add(
            Column(
                Group(
                    theme,
                    "Downloads",
                    bandwidth,
                    new Toggle(theme, "Automatic updates", true) { Hint = "Download updates as soon as they are released" },
                    new Toggle(theme, "Download while playing"),
                    new Dropdown(theme, "Update channel", ["Stable", "Beta"]) { Hint = "Beta builds may be unstable" }),
                Group(
                    theme,
                    "Installation",
                    new ActionRow(theme, "Library folder", "Change") { Value = DefaultLibrary() },
                    new Toggle(theme, "Verify files after update", true),
                    new ActionRow(theme, "Repair installation", "Verify now") { Hint = "Re-checks every file and redownloads damaged ones" })),
            0,
            0);

        columns.Add(
            Column(
                Group(
                    theme,
                    "Game launch",
                    new ActionRow(theme, "Launch arguments", "Edit") { Value = "-windowed -skipintro", FieldWidth = 300f },
                    new Toggle(theme, "Close launcher when game starts")),
                Group(
                    theme,
                    "Launcher",
                    new Toggle(theme, "Start with system"),
                    new Toggle(theme, "Minimise to tray on close", true),
                    new Dropdown(theme, "Language", ["English", "Deutsch", "Français", "Italiano", "Español"]))),
            0,
            1);

        var scroll = new ScrollView(theme);
        scroll.SetContent(columns);

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Launcher", "Settings") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(scroll, 1, 0);
        return page;
    }

    private static StackPanel Column(params UiElement[] groups)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 20f, VerticalAlignment = VerticalAlignment.Top };
        foreach (UiElement group in groups)
        {
            column.AddChild(group);
        }

        return column;
    }

    private static GlassPanel Group(UiTheme theme, string title, params UiElement[] rows)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f };
        stack.AddChild(new GroupHeader(theme, title) { Margin = new Thickness(0f, 0f, 0f, 8f) });
        foreach (UiElement row in rows)
        {
            row.Margin = new Thickness(-10f, 0f);
            stack.AddChild(row);
        }

        var panel = new GlassPanel(theme) { Tint = UiPalette.Ink.WithAlpha(179), Padding = new Thickness(26f, 22f, 26f, 14f), VerticalAlignment = VerticalAlignment.Top };
        panel.AddChild(stack);
        return panel;
    }

    private static string DefaultLibrary() =>
        OperatingSystem.IsWindows() ? @"C:\Games\OpenIGI\Library" : "~/Games/OpenIGI/Library";
}
