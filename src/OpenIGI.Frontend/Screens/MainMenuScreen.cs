using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The shell's front page: a game-selector chip and the Campaign / Settings / Profile / Extras / Quit
/// entries in a translucent panel over the slowly-orbiting showcase level.
/// </summary>
/// <remarks>
/// The panel is anchored to the left of the screen so the showcase — drawn behind the shell by the app,
/// with its heavy depth-of-field and grading — fills the rest. The panel is transparent-backed apart from
/// its own translucent fill, so nothing here hides the scene; the menu reads as a HUD over a living
/// world rather than a page.
/// </remarks>
public sealed class MainMenuScreen : Screen
{
    private readonly ShellContext _context;

    /// <summary>Initializes a new instance of the <see cref="MainMenuScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public MainMenuScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        Name = "MainMenu";
    }

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        UiTheme theme = _context.Theme;
        var canvas = new Canvas();

        var panel = new Panel
        {
            Background = theme.Panel,
            Padding = new Thickness(48f, 52f),
        };

        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4f };

        column.AddChild(new Label(theme.Heading, "PROJECT I.G.I.", theme.HeadingTextSize, theme.Accent)
        {
            Shadow = theme.Shadow,
        });
        column.AddChild(new Label(theme.Body, "OpenIGI — cinematic shell", theme.BodyTextSize, theme.TextMuted)
        {
            Margin = new Thickness(0f, 0f, 0f, 28f),
        });

        // Game selector: every known title, the unavailable ones flagged. Selecting one that cannot be
        // played snaps back to the active title rather than pretending to switch.
        string[] games = new string[_context.Games.Count];
        for (int i = 0; i < games.Length; i++)
        {
            games[i] = _context.Games[i].IsPlayable
                ? _context.Games[i].DisplayName
                : _context.Games[i].DisplayName + "  (unavailable)";
        }

        var selector = new Dropdown(theme, "Game", games) { Margin = new Thickness(0f, 0f, 0f, 28f) };
        selector.SelectionChanged += index =>
        {
            if (index < 0 || index >= _context.Games.Count || !_context.Games[index].IsPlayable)
            {
                selector.SelectedIndex = 0;
            }
        };
        column.AddChild(selector);

        AddButton(column, "Campaign", () => _context.Stack.Push(new CampaignScreen(_context)));
        AddButton(column, "Settings", () => _context.Stack.Push(new SettingsScreen(_context)));
        AddButton(column, "Profile", () => _context.Stack.Push(new ProfilesScreen(_context)));
        AddButton(column, "Extras", () => _context.Stack.Push(new ExtrasScreen(_context)));
        AddButton(column, "Quit", _context.Host.Quit);

        panel.AddChild(column);
        canvas.Add(panel, new Anchor(UiVec2.Zero, new UiVec2(0.42f, 1f)));
        return canvas;
    }

    /// <inheritdoc/>
    protected override void OnEnter() => _context.Host.PreviewMission(null); // show the default showcase

    private void AddButton(StackPanel column, string text, Action onActivate)
    {
        var button = new Button(_context.Theme, text) { TextSize = _context.Theme.BodyTextSize * 1.25f };
        button.Activated += _ => onActivate();
        column.AddChild(button);
    }
}
