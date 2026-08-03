using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The shell's front page: the game title, a horizontal nav bar of Campaign / Settings / Profile /
/// Extras / Quit and a game-selector chip, in a translucent strip across the top of the slowly-orbiting
/// showcase level.
/// </summary>
/// <remarks>
/// The bar spans the width at the top so the showcase — drawn behind the shell by the app, with its heavy
/// depth of field and grading — fills the rest of the screen beneath it. Only the strip's own translucent
/// fill sits over the scene; the menu reads as a top navigation over a living world.
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

        var bar = new Panel { Background = theme.Panel };
        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 6f,
            Padding = new Thickness(48f, 0f),
            Justify = FlexJustify.Start,
        };

        // The title on the left, vertically centred in the bar.
        row.Add(new Label(theme.Heading, "PROJECT I.G.I.", theme.HeadingTextSize * 0.62f, theme.Accent)
        {
            Shadow = theme.Shadow,
            CenterTextVertically = true,
            Margin = new Thickness(0f, 0f, 40f, 0f),
        });

        // The nav entries as horizontal tabs; each button fills the bar height, so its focus fill reads as
        // a tab highlight.
        AddButton(row, "Campaign", () => _context.Stack.Push(new CampaignScreen(_context)));
        AddButton(row, "Settings", () => _context.Stack.Push(new SettingsScreen(_context)));
        AddButton(row, "Profile", () => _context.Stack.Push(new ProfilesScreen(_context)));
        AddButton(row, "Extras", () => _context.Stack.Push(new ExtrasScreen(_context)));
        AddButton(row, "Quit", _context.Host.Quit);

        // A growing spacer pushes the game selector to the right edge.
        row.Add(new Panel(), grow: 1f);

        string[] games = new string[_context.Games.Count];
        for (int i = 0; i < games.Length; i++)
        {
            games[i] = _context.Games[i].IsPlayable
                ? _context.Games[i].DisplayName
                : _context.Games[i].DisplayName + "  (unavailable)";
        }

        var selector = new Dropdown(theme, "Game", games) { Width = 340f };
        selector.SelectionChanged += index =>
        {
            if (index < 0 || index >= _context.Games.Count || !_context.Games[index].IsPlayable)
            {
                selector.SelectedIndex = 0;
            }
        };
        row.Add(selector);

        bar.AddChild(row);
        canvas.Add(bar, new Anchor(UiVec2.Zero, new UiVec2(1f, 0.14f)));
        return canvas;
    }

    /// <inheritdoc/>
    protected override void OnEnter() => _context.Host.PreviewMission(null); // show the default showcase

    private void AddButton(FlexPanel row, string text, Action onActivate)
    {
        var button = new Button(_context.Theme, text) { TextSize = _context.Theme.BodyTextSize * 1.15f };
        button.Activated += _ => onActivate();
        row.Add(button);
    }
}
