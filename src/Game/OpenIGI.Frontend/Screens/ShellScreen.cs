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

    /// <summary>Multiplayer: the server browser, joining by code, parties and hosting every mode.</summary>
    Coop,

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
/// That is the frame on a monitor and a laptop. On a phone held sideways, or a small window, height is what there is
/// least of and there are no keys to hint at: the sections become a rail down the left edge, the header and footer go,
/// and the page has the rest (<see cref="ShellSize.Compact"/>). A page asks <see cref="Size"/> which it is building for.
/// </para>
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
    protected const float HeaderHeight = 72f;

    /// <summary>The footer's height, in design units.</summary>
    protected const float FooterHeight = 56f;

    /// <summary>The width of the rail of sections a compact page has down its left edge, in design units.</summary>
    protected const float RailWidth = 80f;

    /// <summary>Gets the arrangement the page was last built for.</summary>
    protected ShellSize Size { get; private set; }

    /// <summary>Gets the margin either side of the header, footer and pages, in design units.</summary>
    protected float SideMargin => ShellSizes.SideMargin(Size);

    private static readonly ShellTab[] Order = [ShellTab.Campaign, ShellTab.Coop, ShellTab.Settings, ShellTab.Profile, ShellTab.Extras];

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
    protected virtual IReadOnlyList<KeyHint> Hints => [new("Esc", "Back")];

    /// <summary>Gets a quiet line set at the footer's left, or empty for none: the landing page's says which build this is.</summary>
    protected virtual string FooterNote => string.Empty;

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
        Size = ShellSizes.Of(context);
        ShellSizes.Apply(Context.Theme, Size);
        var canvas = new Canvas();
        bool compact = Size == ShellSize.Compact;

        // Content first in tree order so focus starts on it rather than on a tab.
        if (BuildContent(context) is { } content)
        {
            canvas.Add(content, new Anchor(UiVec2.Zero, UiVec2.One, compact
                ? new Thickness(RailWidth, 0f, 0f, 0f)
                : new Thickness(0f, HeaderHeight, 0f, FooterHeight)));
        }

        // The chrome spans the screen but only draws at its edges, so it lets the pointer through to the
        // page between them.
        _chrome = new Canvas { IsHitTestTransparent = true };
        if (compact)
        {
            _hints = null;
            _title = null;
            _chrome.Add(BuildRail(), new Anchor(UiVec2.Zero, new UiVec2(0f, 1f), new Thickness(0f, 0f, -RailWidth, 0f)));
        }
        else
        {
            _chrome.Add(BuildFooter(), new Anchor(new UiVec2(0f, 1f), UiVec2.One, new Thickness(0f, -FooterHeight, 0f, 0f)));
            _chrome.Add(BuildHeader(context), new Anchor(UiVec2.Zero, new UiVec2(1f, 0f), new Thickness(0f, 0f, 0f, -HeaderHeight)));
        }

        canvas.Add(_chrome);
        return canvas;
    }

    /// <summary>Puts a compact page's content in a viewport that scrolls, inside the page's margins.</summary>
    /// <param name="content">The content, as tall as it measures.</param>
    /// <returns>The viewport.</returns>
    /// <remarks>A phone on its side shows some 390 units of height: a compact page is a column the finger drags.</remarks>
    protected ScrollView ScrollPage(UiElement content)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.Margin = new Thickness(SideMargin, 10f, SideMargin, 24f);
        var scroll = new ScrollView(Context.Theme);
        scroll.SetContent(content);
        return scroll;
    }

    /// <summary>Gets how far the active profile is through the game's campaign.</summary>
    /// <returns>The missions done, and how many there are.</returns>
    protected (int Done, int Total) CampaignProgress()
    {
        List<MissionInfo> missions = [.. Context.Missions.Where(static mission => mission.Campaign is null)];
        int active = Context.Config.Active?.ActiveMission ?? 0;
        int done = Math.Max(0, missions.FindIndex(mission => mission.Id == active));
        return (done, missions.Count);
    }

    /// <summary>Gets the active profile's difficulty, by name.</summary>
    /// <returns>The name, or an empty string with no profile.</returns>
    protected string DifficultyName()
    {
        string[] names = OpenIGI.Game.Ai.AiDifficultyNames.All;
        return Context.Config.Active is { } profile ? names[Math.Clamp(profile.Difficulty, 0, names.Length - 1)] : string.Empty;
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

    /// <summary>Gets a value indicating whether a text box was entered this frame: accept, but not the space bar, which in a box is a character.</summary>
    /// <param name="nav">The frame's navigation.</param>
    /// <param name="input">The frame's input.</param>
    /// <returns><see langword="true"/> when what the box holds is to be acted on.</returns>
    protected static bool EnteredText(UiNavigation nav, InputState input)
    {
        ArgumentNullException.ThrowIfNull(nav);
        ArgumentNullException.ThrowIfNull(input);
        return nav.Accept && !input.Keyboard.IsDown(KeyCode.Space) && !input.Keyboard.WasPressed(KeyCode.Space);
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

    /// <summary>The canvas width, in design units, under which the header draws its parts closer together.</summary>
    /// <remarks>
    /// The title, the five sections between their keys, the operative and Quit are some 1,520 units across at their full
    /// spacing. A 4:3 screen is 1,440 and a 5:4 one 1,350: there the two key caps go and the gaps close up.
    /// </remarks>
    private const float RoomyHeaderWidth = 1600f;

    private static string TabName(ShellTab tab) => tab == ShellTab.Coop ? "Multiplayer" : tab.ToString();

    private EdgePanel BuildHeader(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        var bar = new EdgePanel(lineAtBottom: true);

        bool roomy = ShellSizes.Canvas(context).X >= RoomyHeaderWidth;
        var row = new FlexPanel
        {
            Orientation = Orientation.Horizontal,
            Gap = roomy ? 28f : 16f,
            Padding = new Thickness(roomy ? SideMargin : 40f, 0f),
        };

        row.Add(BuildTitle(theme));

        var nav = new SectionTabs(theme, Context.SectionUnderline)
        {
            Orientation = Orientation.Horizontal,
            Gap = 4f,
            Margin = new Thickness(roomy ? 24f : 0f, 0f, 0f, 0f),
        };
        if (roomy)
        {
            nav.Add(new KeyCapLabel(theme, "Q") { VerticalAlignment = VerticalAlignment.Center });
        }

        foreach (ShellTab tab in Order)
        {
            var button = new HeaderTab(theme, TabName(tab));
            if (!roomy)
            {
                button.Padding = new Thickness(14f, 0f);
            }

            ShellTab target = tab;
            button.Activated += _ => GoTo(target);
            nav.AddTab(button, current: tab == Tab);
        }

        if (roomy)
        {
            nav.Add(new KeyCapLabel(theme, "E") { VerticalAlignment = VerticalAlignment.Center });
        }

        row.Add(nav);
        row.Add(new Panel(), grow: 1f); // pushes what follows to the right edge

        var right = new FlexPanel { Orientation = Orientation.Horizontal, Gap = roomy ? 28f : 16f };
        (int done, int total) = CampaignProgress();
        var operative = new OperativeBadge(theme, Context.Config.Active?.Name ?? "No profile")
        {
            Caption = Context.Config.Active is null || total == 0
                ? string.Empty
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{DifficultyName().ToUpperInvariant()} · {done}/{total}"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        operative.Activated += _ => GoTo(ShellTab.Profile);
        right.Add(operative);
        right.Add(new Rule { VerticalAlignment = VerticalAlignment.Center });

        // Back to the mission from any section. A keyboard has Escape for it; a touch screen has nothing else, and a
        // player who opened the settings from the pause menu was otherwise stuck in the menu.
        if (Context.InGame && Context.Host.TouchScreen && Tab != ShellTab.Home)
        {
            Button resume = HeaderButton(theme, "Resume");
            resume.Activated += _ => Resume();
            right.Add(resume);
        }

        Button quit = HeaderButton(theme, "Quit");
        quit.LeadingGlyph = static (painter, centre, size, ink) => KitIcons.Draw(painter, KitIcon.Power, centre, size, ink);
        quit.GlyphSize = 18f;
        quit.Activated += _ => Context.Host.Quit();
        right.Add(quit);
        row.Add(right);

        bar.AddChild(row);
        return bar;
    }

    private static Button HeaderButton(UiTheme theme, string text) => new(theme, text)
    {
        Accent = ButtonAccent.Text,
        TextSize = 17f,
        Tracking = 0.12f,
        Padding = new Thickness(0f, 12f),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Goes back to the mission behind the menu, unless the page holds the player first.</summary>
    protected void Resume()
    {
        void Go() => Context.Host.ResumeMission();
        if (!HoldDeparture(Go))
        {
            Go();
        }
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
        if (FooterNote.Length > 0)
        {
            bar.AddChild(new Label(Context.Theme.Mono, FooterNote, 13f, Context.Theme.TextMuted)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(SideMargin, 0f),
            });
        }

        bar.AddChild(_hints);
        return bar;
    }

    // The compact page's sections: Home and the five, an icon over a word each, down the left edge.
    private SectionRail BuildRail()
    {
        var rail = new SectionRail(Context.Theme);
        rail.Add(KitIcon.None, "Home", Tab == ShellTab.Home, () => GoTo(ShellTab.Home));
        rail.Add(KitIcon.Target, "Campaign", Tab == ShellTab.Campaign, () => GoTo(ShellTab.Campaign));
        rail.Add(KitIcon.Globe, "Online", Tab == ShellTab.Coop, () => GoTo(ShellTab.Coop));
        rail.Add(KitIcon.Sliders, "Settings", Tab == ShellTab.Settings, () => GoTo(ShellTab.Settings));
        rail.Add(KitIcon.Person, "Profile", Tab == ShellTab.Profile, () => GoTo(ShellTab.Profile));
        rail.Add(KitIcon.Archive, "Extras", Tab == ShellTab.Extras, () => GoTo(ShellTab.Extras));
        return rail;
    }

    /// <summary>Builds the selector of which game is running, for a page that shows it itself.</summary>
    /// <returns>The selector.</returns>
    /// <remarks>The header holds it on a monitor; a compact page has no header, and its landing page carries it.</remarks>
    protected GameSwitcher BuildGameSwitcher() => BuildTitle(Context.Theme);

    private GameSwitcher BuildTitle(UiTheme theme)
    {
        // The editor is the list's last entry, after the games: from the front end only, where opening it releases whatever
        // the menu is drawing over.
        bool editor = Context.Host.CanOpenEditor && !Context.InGame && !Context.Host.TouchScreen;
        int count = Context.Games.Count;
        string[] games = new string[count + (editor ? 1 : 0)];
        int current = 0;
        if (editor)
        {
            games[count] = "Editor";
        }

        for (int i = 0; i < count; i++)
        {
            games[i] = ShortName(Context.Games[i].DisplayName);
            if (Context.Games[i].Id == Context.ActiveTitleId)
            {
                current = i;
            }
        }

        var title = new GameSwitcher(theme, count > 0 ? games[current] : string.Empty, games)
        {
            VerticalAlignment = VerticalAlignment.Center,
            TitleSize = 22f,

            // The one running, not the first listed: opening the list after a switch must show the switch.
            CurrentIndex = current,
            ItemDetail = index => index >= 0 && index < Context.Games.Count
                ? Tagline(Context.Games[index].Id, Context.Games[index].DisplayName)
                : index == count && editor ? "MISSIONS AND MODS" : null,

            // A title is takeable when this build supports it AND the player has it installed. An entry
            // greyed out for the second reason is worth showing rather than hiding.
            ItemEnabled = index => (index >= 0 && index < Context.Games.Count && Context.Games[index].IsPlayable) || (editor && index == count),
        };

        title.ItemSelected += index =>
        {
            if (editor && index == count)
            {
                // Not a game: the selector stays on the one running.
                title.CurrentIndex = current;
                Context.Host.OpenEditor();
                return;
            }

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
        ShellTab.Coop => new MultiplayerScreen(context),
        ShellTab.Settings => new SettingsScreen(context),
        ShellTab.Profile => new ProfilesScreen(context),
        ShellTab.Extras => new ExtrasScreen(context),
        _ => context.InGame ? new PauseScreen(context) : new MainMenuScreen(context),
    };

    /// <summary>A bar that darkens toward the screen's edge, with a hairline along its inner one: the header's bottom, the footer's top.</summary>
    /// <remarks>Dark enough to read the sections over a level in daylight, without a hard-edged slab across it.</remarks>
    private sealed class EdgePanel(bool lineAtBottom) : Panel
    {
        protected override void OnDraw(UiPainter painter)
        {
            Span<OpenIGI.UI.Drawing.UiGradientStop> stops = lineAtBottom
                ? [new(0f, UiPalette.InkAt(0.94f)), new(1f, UiPalette.InkAt(0.78f))]
                : [new(0f, UiPalette.InkAt(0.55f)), new(1f, UiPalette.InkAt(0.9f))];
            painter.FillGradient(Bounds, stops, OpenIGI.UI.Drawing.UiGradientDirection.Vertical);
            float t = painter.Context.Px(1f);
            painter.FillRect(
                new UiRect(Bounds.X, lineAtBottom ? Bounds.Bottom - t : Bounds.Y, Bounds.Width, t),
                UiPalette.White(lineAtBottom ? 0.12f : 0.07f));
        }
    }
}
