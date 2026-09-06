using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The mission-failed page: the two things that can be done to a mission the player did not survive —
/// start it again, or leave it.
/// </summary>
/// <remarks>
/// <para>
/// A sibling of <see cref="PauseScreen"/>, and deliberately built the same way. The failure result used
/// to be drawn as a bespoke card by the computer renderers, with its two choices offered only as key
/// hints. That card could not be clicked and drew no pointer of its own, so it had to reveal the
/// platform cursor — the one place in the front end that did, since everywhere else the shell draws its
/// own. Making it a shell page rather than a card is what gives it the drawn cursor, the clickable rows
/// and the pad/keyboard navigation that every other page already has.
/// </para>
/// <para>
/// There is no Resume here, which is the one way this differs from the pause menu: the mission is over,
/// so Back leaves rather than carrying on.
/// </para>
/// </remarks>
public sealed class MissionFailedScreen : ShellScreen
{
    /// <summary>Initializes a new instance of the <see cref="MissionFailedScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MissionFailedScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Home;

    /// <inheritdoc/>
    /// <remarks>The dead level is the picture, exactly as the frozen one is behind the pause menu.</remarks>
    protected override bool ContentOnBackdrop => false;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;

        var column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10f,
            Padding = new Thickness(34f, 30f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 400f,
        };

        column.AddChild(new Label(theme.Heading, "MISSION FAILED", theme.HeadingTextSize * 0.85f, theme.Text)
        {
            Shadow = theme.Shadow,
        });
        column.AddChild(new Panel
        {
            Background = theme.Danger,
            Height = 3f,
            Width = 110f,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0f, 2f, 0f, 16f),
        });

        column.AddChild(Action(theme, "RESTART MISSION", ButtonAccent.Solid, () => Context.Host.RestartMission()));
        column.AddChild(Action(theme, "ABANDON MISSION", ButtonAccent.Bar, () => Context.Host.AbandonMission()));

        column.AddChild(new Label(theme.Body, "Escape abandons.", theme.BodyTextSize * 0.8f, theme.TextMuted)
        {
            Shadow = theme.Shadow,
            Margin = new Thickness(0f, 18f, 0f, 0f),
        });

        var backdrop = new Panel
        {
            Background = theme.Panel.WithAlpha(170),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        backdrop.AddChild(column);
        return backdrop;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        // The same shape as the pause menu's: ShellScreen answers Back first, because an open title list
        // takes the press for itself. What it hands back is a back press nothing wanted, and here that
        // means leaving the mission — there is nothing to resume.
        if (base.HandleInput(nav, input))
        {
            Context.Host.AbandonMission();
        }

        return false;
    }

    private static Button Action(UiTheme theme, string text, ButtonAccent accent, Action activated)
    {
        var button = new Button(theme, text)
        {
            TextSize = theme.BodyTextSize * 1.1f,
            Padding = new Thickness(24f, 14f),
            Accent = accent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        button.Activated += _ => activated();
        return button;
    }
}
