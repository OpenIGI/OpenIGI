using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The in-game menu: the shell's landing page while a mission is live, holding the three things that can
/// be done to a mission from inside it — carry on, start it again, or leave it.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no showcase here.</b> The landing page out of game orbits a streamed level because it has
/// nothing else to show; this page is drawn over the level the player is standing in, frozen mid-frame,
/// which is a better picture than any backdrop could be and is already loaded. So the page asks for no
/// preview at all — see <see cref="ShellContext.InGame"/> — and keeps itself to a narrow column against
/// the left edge so that the level stays visible around it.
/// </para>
/// <para>
/// Escape opens this page and Escape closes it again, which means Back has to be Resume rather than the
/// usual walk up to the landing page: on the pause menu the landing page <em>is</em> this page.
/// </para>
/// </remarks>
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
    /// <remarks>The frozen level is the picture; the column carries its own panel so the rest stays clear.</remarks>
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

        column.AddChild(new Label(theme.Heading, "PAUSED", theme.HeadingTextSize * 0.85f, theme.Text)
        {
            Shadow = theme.Shadow,
        });
        column.AddChild(new Panel
        {
            Background = theme.Accent,
            Height = 3f,
            Width = 110f,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0f, 2f, 0f, 16f),
        });

        column.AddChild(Action(theme, "RESUME", ButtonAccent.Solid, () => Context.Host.ResumeMission()));
        column.AddChild(Action(theme, "RESTART MISSION", ButtonAccent.Bar, () => Context.Host.RestartMission()));
        column.AddChild(Action(theme, "ABANDON MISSION", ButtonAccent.Bar, () => Context.Host.AbandonMission()));

        column.AddChild(new Label(theme.Body, "Escape resumes.", theme.BodyTextSize * 0.8f, theme.TextMuted)
        {
            Shadow = theme.Shadow,
            Margin = new Thickness(0f, 18f, 0f, 0f),
        });

        // Translucent, and only as large as the column it holds, so the frozen level reads both behind the
        // panel and all the way around it.
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
        // ShellScreen answers Back first, because an open title list takes the press for itself; what it
        // hands back is a back press nothing wanted, and on this page that means Resume rather than a pop.
        if (base.HandleInput(nav, input))
        {
            Context.Host.ResumeMission();
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
