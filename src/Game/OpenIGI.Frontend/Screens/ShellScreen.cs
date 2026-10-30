using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>Which top-level section of the shell a screen is.</summary>
public enum ShellTab
{
    /// <summary>The landing page — the bar over the showcase, nothing selected.</summary>
    Home,

    /// <summary>The campaign / mission select.</summary>
    Campaign,

    /// <summary>The settings.</summary>
    Settings,

    /// <summary>Profiles and saves.</summary>
    Profile,

    /// <summary>The extras.</summary>
    Extras,
}

/// <summary>
/// The common frame every shell page shares: a header across the top — the game switcher, the section tabs
/// between their Q and E keys, the operative and Quit — a footer of key hints across the bottom, and the
/// page's own content between them.
/// </summary>
/// <remarks>
/// <para>
/// Navigation is by <em>replacement</em>, not a stack: selecting a tab resets the shell to that section,
/// so there is only ever one page on screen. The header and footer are the page's chrome: drawn outside the
/// page's fade, so they hold still while one page gives way to the next. Back returns to the home page.
/// </para>
/// <para>
/// The title doubles as the game selector, opening a list of the titles the shell knows about: the name at
/// the top left <em>is</em> the statement of which game is running.
/// </para>
/// <para>
/// Behind it all is the level, blurred (<see cref="BackdropBlur"/>) and vignetted by
/// <see cref="Shell.DrawBackdrop"/>; the home page leaves it sharp, because there the level is the point.
/// </para>
/// </remarks>
public abstract class ShellScreen : Screen
{
    /// <summary>The header's height, in design units.</summary>
    protected const float HeaderHeight = 96f;

    /// <summary>The footer's height, in design units.</summary>
    protected const float FooterHeight = 72f;

    /// <summary>The margin either side of the header, footer and pages, in design units.</summary>
    protected const float SideMargin = 64f;

    private static readonly ShellTab[] Order = [ShellTab.Campaign, ShellTab.Settings, ShellTab.Profile, ShellTab.Extras];

    private Canvas? _chrome;
    private GameSwitcher? _title;
    private KeyHintBar? _hints;

    /// <summary>Initializes a new instance of the <see cref="ShellScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    protected ShellScreen(ShellContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Name = GetType().Name;
    }

    /// <summary>Gets how blurred the level behind the page is, in design units; zero leaves it sharp.</summary>
    public virtual float BackdropBlur => 4f;

    /// <summary>Gets the scale the level behind the page is drawn at, for a page that animates it.</summary>
    public virtual float BackdropScale => 1f;

    /// <summary>Gets the shared shell context.</summary>
    protected ShellContext Context { get; }

    /// <summary>Gets which section this page is, for the selected tab.</summary>
    protected abstract ShellTab Tab { get; }

    /// <summary>Gets the key hints the footer shows on this page.</summary>
    protected virtual IReadOnlyList<KeyHint> Hints => [new("ESC", "Back")];

    /// <inheritdoc/>
    protected override UiElement? Chrome => _chrome;

    /// <summary>Builds the page's content, laid out between the header and the footer, or <see langword="null"/> for none.</summary>
    /// <param name="context">The layout context.</param>
    /// <returns>The content element, or <see langword="null"/>.</returns>
    protected abstract UiElement? BuildContent(LayoutContext context);

    /// <summary>Replaces the footer's hints, for a page whose keys change as it is used.</summary>
    protected void RefreshHints() => _hints?.SetHints(Hints);

    /// <inheritdoc/>
    protected override UiElement BuildRoot(LayoutContext context)
    {
        var canvas = new Canvas();

        // Content first in tree order so focus starts on it rather than on a tab.
        if (BuildContent(context) is { } content)
        {
            canvas.Add(content, new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(0f, HeaderHeight, 0f, FooterHeight)));
        }

        // The chrome spans the screen but only draws at its edges, so it lets the pointer through to the
        // page between them.
        _chrome = new Canvas { IsHitTestTransparent = true };
        _chrome.Add(BuildFooter(), new Anchor(new UiVec2(0f, 1f), UiVec2.One, new Thickness(0f, -FooterHeight, 0f, 0f)));
        _chrome.Add(BuildHeader(), new Anchor(UiVec2.Zero, new UiVec2(1f, 0f), new Thickness(0f, 0f, 0f, -HeaderHeight)));
        canvas.Add(_chrome);
        return canvas;
    }

    /// <inheritdoc/>
    protected override bool HandleInput(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);

        // An open menu takes back for itself: shutting it is what the player meant, not leaving the page.
        if (nav.Back && _title is { IsOpen: true } openTitle)
        {
            openTitle.Close();
            return false;
        }

        if (nav.Back && Tab != ShellTab.Home)
        {
            Leave(ShellTab.Home);
            return false;
        }

        // Q and E step through the sections, as the keys either side of the tabs say. Not while a text
        // field has focus: there the letters are being typed.
        if (Focus?.Focused is not TextBox)
        {
            if (input.Keyboard.WasPressed(KeyCode.Q))
            {
                StepTab(-1);
                return false;
            }

            if (input.Keyboard.WasPressed(KeyCode.E))
            {
                StepTab(1);
                return false;
            }
        }

        return base.HandleInput(nav, input);
    }

    /// <summary>Switches to another section.</summary>
    /// <param name="tab">The section.</param>
    protected void GoTo(ShellTab tab)
    {
        if (tab == Tab)
        {
            return;
        }

        Leave(tab);
    }

    /// <summary>Lets the page hold the player on it before they leave for another section.</summary>
    /// <param name="leave">Leaves, as asked; the page calls it later if it holds the player now.</param>
    /// <returns>
    /// <see langword="true"/> when the page has taken the departure over -- to ask about changes it has not
    /// applied -- and <paramref name="leave"/> has not run; <see langword="false"/> to leave straight away.
    /// </returns>
    protected virtual bool HoldDeparture(Action leave) => false;

    private void Leave(ShellTab tab)
    {
        void Go() => Context.Stack.Reset(CreateScreen(tab, Context));
        if (!HoldDeparture(Go))
        {
            Go();
        }
    }

    private void StepTab(int direction)
    {
        int index = Array.IndexOf(Order, Tab);
        int next = index < 0
            ? (direction > 0 ? 0 : Order.Length - 1)
            : (index + direction + Order.Length) % Order.Length;
        GoTo(Order[next]);
    }

    private EdgePanel BuildHeader()
    {
        UiTheme theme = Context.Theme;
        var bar = new EdgePanel(lineAtBottom: true);

        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = 64f,
            Padding = new Thickness(SideMargin, 0f),
        };

        row.Add(BuildTitle(theme));

        var nav = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 8f };
        nav.Add(new KeyCapLabel(theme, "Q") { Margin = new Thickness(0f, 0f, 12f, 0f) });
        foreach (ShellTab tab in Order)
        {
            var button = new HeaderTab(theme, tab.ToString()) { Selected = tab == Tab };
            ShellTab target = tab;
            button.Activated += _ => GoTo(target);
            nav.Add(button);
        }

        nav.Add(new KeyCapLabel(theme, "E") { Margin = new Thickness(12f, 0f, 0f, 0f) });
        row.Add(nav);
        row.Add(new Panel(), grow: 1f); // pushes what follows to the right edge

        var right = new FlexPanel { Orientation = Orientation.Horizontal, Gap = 36f };
        var operative = new OperativeBadge(theme, Context.Config.Active?.Name ?? "No profile") { VerticalAlignment = VerticalAlignment.Center };
        operative.Activated += _ => GoTo(ShellTab.Profile);
        right.Add(operative);
        right.Add(new Rule { VerticalAlignment = VerticalAlignment.Center });

        // Back to the mission from any section. A keyboard has Escape for it; a touch screen has nothing else, and a
        // player who opened the settings from the pause menu was otherwise stuck in the menu.
        if (Context.InGame && Context.Host.TouchScreen && Tab != ShellTab.Home)
        {
            var resume = new Button(theme, "Resume")
            {
                Accent = ButtonAccent.Text,
                TextSize = 19f,
                Padding = new Thickness(0f, 8f),
                VerticalAlignment = VerticalAlignment.Center,
            };
            resume.Activated += _ =>
            {
                void Go() => Context.Host.ResumeMission();
                if (!HoldDeparture(Go))
                {
                    Go();
                }
            };
            right.Add(resume);
        }

        var quit = new Button(theme, "Quit")
        {
            Accent = ButtonAccent.Text,
            TextSize = 19f,
            Padding = new Thickness(0f, 8f),
            VerticalAlignment = VerticalAlignment.Center,
        };
        quit.Activated += _ => Context.Host.Quit();
        right.Add(quit);
        row.Add(right);

        bar.AddChild(row);
        return bar;
    }

    private EdgePanel BuildFooter()
    {
        var bar = new EdgePanel(lineAtBottom: false);
        _hints = new KeyHintBar(Context.Theme)
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(SideMargin, 0f),
        };
        _hints.SetHints(Hints);
        bar.AddChild(_hints);
        return bar;
    }

    private GameSwitcher BuildTitle(UiTheme theme)
    {
        string[] games = new string[Context.Games.Count];
        int current = 0;
        for (int i = 0; i < games.Length; i++)
        {
            games[i] = ShortName(Context.Games[i].DisplayName);
            if (Context.Games[i].Id == Context.ActiveTitleId)
            {
                current = i;
            }
        }

        var title = new GameSwitcher(theme, games.Length > 0 ? games[current] : string.Empty, games)
        {
            Subtitle = games.Length > 0 ? Tagline(Context.Games[current].Id, Context.Games[current].DisplayName) : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,

            // The one running, not the first listed: opening the list after a switch must show the switch.
            CurrentIndex = current,
            ItemDetail = index => index >= 0 && index < Context.Games.Count
                ? Tagline(Context.Games[index].Id, Context.Games[index].DisplayName)
                : null,

            // A title is takeable when this build supports it AND the player has it installed. An entry
            // greyed out for the second reason is worth showing rather than hiding.
            ItemEnabled = index => index >= 0 && index < Context.Games.Count && Context.Games[index].IsPlayable,
        };

        title.ItemSelected += index =>
        {
            if (index < 0 || index >= Context.Games.Count)
            {
                return;
            }

            // Picking the game already running is a way back to the landing page rather than a reload.
            if (Context.Games[index].Id == Context.ActiveTitleId)
            {
                GoTo(ShellTab.Home);
                return;
            }

            // The app takes the swap up at a frame boundary and rebuilds this shell around the new game;
            // false means it will not happen, and the selector goes back to what is actually running.
            if (!Context.Host.SwitchTitle(Context.Games[index].Id))
            {
                title.CurrentIndex = current;
            }
        };

        _title = title;
        return title;
    }

    /// <summary>A game's name without its subtitle: <c>I.G.I. 2: Covert Strike</c> is <c>I.G.I. 2</c>.</summary>
    private static string ShortName(string displayName)
    {
        int colon = displayName.IndexOf(':', StringComparison.Ordinal);
        return colon < 0 ? displayName : displayName[..colon].Trim();
    }

    /// <summary>A game's tagline, set under its name in the switcher.</summary>
    private static string Tagline(string id, string displayName)
    {
        int colon = displayName.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            return displayName[(colon + 1)..].Trim().ToUpperInvariant();
        }

        return id switch
        {
            "igi1" => "I'M GOING IN",
            "igi2" => "COVERT STRIKE",
            _ => string.Empty,
        };
    }

    // Home is the landing page out of game and the pause menu in it: the tabs, the title and Back all lead
    // to "the page this section hangs off", and inside a mission that page is the one holding Resume.
    private static Screen CreateScreen(ShellTab tab, ShellContext context) => tab switch
    {
        ShellTab.Campaign => new CampaignScreen(context),
        ShellTab.Settings => new SettingsScreen(context),
        ShellTab.Profile => new ProfilesScreen(context),
        ShellTab.Extras => new ExtrasScreen(context),
        _ => context.InGame ? new PauseScreen(context) : new MainMenuScreen(context),
    };

    /// <summary>A transparent bar with a hairline along one edge: the header's bottom, the footer's top.</summary>
    private sealed class EdgePanel(bool lineAtBottom) : Panel
    {
        protected override void OnDraw(UiPainter painter)
        {
            float t = painter.Context.Px(1f);
            painter.FillRect(new UiRect(Bounds.X, lineAtBottom ? Bounds.Bottom - t : Bounds.Y, Bounds.Width, t), UiPalette.Rule);
        }
    }
}
