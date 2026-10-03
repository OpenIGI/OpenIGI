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
    public static UiElement Build(UiTheme theme, LauncherModel model, ILauncherWindow window)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(window);
        LauncherSettings settings = model.Settings;

        // Two columns where each row has room for its caption and its control; one, stacked, on a narrower window.
        var columns = new AdaptiveColumns { MinColumnWidth = 640f, Spacing = 20f, VerticalAlignment = VerticalAlignment.Top };

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

        var library = new TextRow(theme, "Library folder", settings.Library, browse: current => window.PickFolder("Choose where OpenIGI's builds are installed", current))
        {
            Hint = "Where OpenIGI's builds are installed",
        };
        library.TextChanged += text => model.ChangeSettings(s => s.Library = text);

        var repair = new ActionRow(theme, "Repair installation", "Verify now") { Hint = "Re-checks every installed file against the release" };
        repair.Activated += _ => model.Verify();

        columns.Add(
            Column(
                Group(theme, "Updates", channel, automatic, notify),
                Group(theme, "Installation", library, repair),
                Group(theme, "Launcher window", new CloseButtonRow(theme, model))));

        columns.Add(
            Column(
                // One folder row per released game; I.G.I. 2's appears when it is playable.
                Group(theme, "Game files", [.. model.Games
                    .Where(static g => g.Released && g.Retail is not null)
                    .Select(g => (UiElement)GameFolder(theme, model, window, g.Retail!, $"{g.Title} folder"))]),
                Group(theme, "Game launch", LaunchArguments(theme, model), MinimizeOnLaunch(theme, model))));

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
    private static TextRow GameFolder(UiTheme theme, LauncherModel model, ILauncherWindow window, GameTitle title, string caption)
    {
        string chosen = model.Settings.GameFolders.GetValueOrDefault(title.Id) ?? GameStarter.Locate(title, null) ?? string.Empty;
        var row = new TextRow(
            theme,
            caption,
            chosen,
            "Choose the folder holding " + title.SignatureFile,
            browse: current => window.PickFolder($"Choose the folder holding {title.SignatureFile}", current));
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

    private static Toggle MinimizeOnLaunch(UiTheme theme, LauncherModel model)
    {
        var toggle = new Toggle(theme, $"{TrayWording} when game starts", model.Settings.MinimizeOnLaunch)
        {
            Hint = "It comes back when the game exits",
        };
        toggle.ValueChanged += on => model.ChangeSettings(s => s.MinimizeOnLaunch = on);
        return toggle;
    }

    // Only Windows has a notification area the launcher can go to; elsewhere it minimises instead.
    private static string TrayWording => OperatingSystem.IsWindows() ? "Minimize to tray" : "Minimize";

    /// <summary>What the close button does. Follows the setting, which the close question's "remember" can change too.</summary>
    private sealed class CloseButtonRow : Dropdown
    {
        private readonly LauncherModel _model;

        public CloseButtonRow(UiTheme theme, LauncherModel model)
            : base(theme, "Close button", ["Ask every time", TrayWording, "Exit"], (int)model.Settings.CloseButton)
        {
            _model = model;
            Hint = "What the launcher's close button does";
            SelectionChanged += index => model.ChangeSettings(s => s.CloseButton = (CloseAction)index);
        }

        protected override void OnUpdate(float elapsedSeconds)
        {
            base.OnUpdate(elapsedSeconds);
            int chosen = (int)_model.Settings.CloseButton;
            if (SelectedIndex != chosen)
            {
                SelectedIndex = chosen;
            }
        }
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
