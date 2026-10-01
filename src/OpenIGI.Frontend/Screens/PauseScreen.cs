using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The pause menu: the shell's home page while a mission is running. The frozen level blurs behind it, and
/// the page offers the mission back — RESUME, large — or a restart, or a way out.
/// </summary>
public sealed class PauseScreen : ShellScreen
{
    /// <summary>Initializes a new instance of the <see cref="PauseScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public PauseScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => [new("ENTER", "Select"), new("Q E", "Sections"), new("ESC", "Resume")];

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context) =>
        MissionActions.Build(
            Context.Theme,
            "Operation",
            "Paused",
            Context.Theme.Accent,
            ("Resume", () => Context.Host.ResumeMission()),
            [("RESTART MISSION", ButtonAccent.Ghost, () => Context.Host.RestartMission()), ("ABANDON MISSION", ButtonAccent.Destructive, () => Context.Host.AbandonMission())]);

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // ShellScreen answers Back first, because an open title list takes the press for itself; what it
        // hands back is a back press nothing wanted, and on this page that means Resume rather than a pop.
        if (base.HandleInput(nav, input))
        {
            Context.Host.ResumeMission();
        }

        return false;
    }
}

/// <summary>The block the pause and mission-failed pages share: a title over one primary action and a column of secondary ones.</summary>
internal static class MissionActions
{
    public static UiElement Build(
        UiTheme theme,
        string eyebrow,
        string title,
        Core.Graphics.Bgra32 eyebrowColor,
        (string Text, Action Activated) primary,
        IReadOnlyList<(string Text, ButtonAccent Accent, Action Activated)> secondary)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(secondary);

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12f,
            Width = 420f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(64f, 0f, 0f, 0f),
        };

        column.AddChild(new SectionTitle(theme, eyebrow, title)
        {
            TitleSize = 112f,
            EyebrowColor = eyebrowColor,
            Margin = new Thickness(0f, 0f, 0f, 28f),
        });

        var main = new PrimaryAction(theme, primary.Text) { Width = 420f, Height = 96f, Margin = new Thickness(0f, 0f, 0f, 8f) };
        main.Activated += _ => primary.Activated();
        column.AddChild(main);

        foreach ((string text, ButtonAccent accent, Action activated) in secondary)
        {
            var button = new Button(theme, text)
            {
                Accent = accent,
                TextSize = 17f,
                Tracking = 0.18f,
                Padding = new Thickness(24f, 16f),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Activated += _ => activated();
            column.AddChild(button);
        }

        return column;
    }
}
