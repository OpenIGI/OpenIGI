using OpenIGI.Core.Titles;
using OpenIGI.Launcher.Services;
using OpenIGI.Releases;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's settings: two columns of glass panels, each a group of rows — the same toggle, slider and
/// select rows the game's own settings use, in the launcher's taller ruled style. Every row is saved the
/// moment it changes.
/// </summary>
internal static class SettingsPage
{
    public static UiElement Build(UiTheme theme, LauncherModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        LauncherSettings settings = model.Settings;

        var columns = new Grid { ColumnSpacing = 20f, VerticalAlignment = VerticalAlignment.Top };
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.RowDefinitions.Add(GridLength.Auto);

        var channel = new Dropdown(theme, "Update channel", ["Stable", "Beta"], settings.Channel == ReleaseChannel.Beta ? 1 : 0)
        {
            Hint = "Beta gets every build from develop first, and may be unstable",
        };
        channel.SelectionChanged += index => model.ChangeSettings(s => s.Channel = index == 1 ? ReleaseChannel.Beta : ReleaseChannel.Stable);

        var automatic = new Toggle(theme, "Automatic updates", settings.AutomaticUpdates) { Hint = "Download updates as soon as they are released" };
        automatic.ValueChanged += on => model.ChangeSettings(s => s.AutomaticUpdates = on);

        var notify = new Toggle(theme, "Update notifications", settings.Notifications)
        {
            Hint = "A desktop notification when a release comes out while the launcher is in the background",
        };
        notify.ValueChanged += on => model.ChangeSettings(s => s.Notifications = on);

        var library = new TextRow(theme, "Library folder", settings.Library) { Hint = "Where OpenIGI's builds are installed" };
        library.TextChanged += text => model.ChangeSettings(s => s.Library = text);

        var repair = new ActionRow(theme, "Repair installation", "Verify now") { Hint = "Re-checks every installed file against the release" };
        repair.Activated += _ => model.Verify();

        columns.Add(
            Column(
                Group(theme, "Updates", channel, automatic, notify),
                Group(theme, "Installation", library, repair)),
            0,
            0);

        columns.Add(
            Column(
                Group(theme, "Game files", GameFolder(theme, model, GameTitle.Igi1, "Project I.G.I. folder"), GameFolder(theme, model, GameTitle.Igi2, "I.G.I. 2 folder")),
                Group(theme, "Game launch", LaunchArguments(theme, model), CloseOnLaunch(theme, model))),
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

    // OpenIGI ships none of the original game; this is where the player's own copy is. The hint says at once
    // whether the folder typed is one, by the same file the game itself checks for.
    private static TextRow GameFolder(UiTheme theme, LauncherModel model, GameTitle title, string caption)
    {
        string chosen = model.Settings.GameFolders.GetValueOrDefault(title.Id) ?? GameStarter.Locate(title, null) ?? string.Empty;
        var row = new TextRow(theme, caption, chosen, "Paste the folder holding " + title.SignatureFile);
        void Describe(string text) => row.Hint = text.Length == 0
            ? $"Your own copy of the game: the folder holding {title.SignatureFile}"
            : GameStarter.IsInstallation(title, text) ? "Found" : $"No {title.SignatureFile} in that folder";

        Describe(chosen);
        row.TextChanged += text =>
        {
            Describe(text);
            model.ChangeSettings(s => s.GameFolders[title.Id] = text.Trim().Trim('"'));
        };
        return row;
    }

    private static TextRow LaunchArguments(UiTheme theme, LauncherModel model)
    {
        var row = new TextRow(theme, "Launch arguments", model.Settings.LaunchArguments, "-windowed") { FieldWidth = 300f };
        row.TextChanged += text => model.ChangeSettings(s => s.LaunchArguments = text);
        return row;
    }

    private static Toggle CloseOnLaunch(UiTheme theme, LauncherModel model)
    {
        var toggle = new Toggle(theme, "Close launcher when game starts", model.Settings.CloseOnLaunch);
        toggle.ValueChanged += on => model.ChangeSettings(s => s.CloseOnLaunch = on);
        return toggle;
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
}
