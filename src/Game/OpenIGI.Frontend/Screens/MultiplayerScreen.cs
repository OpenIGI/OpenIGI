using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Multiplayer, every way of playing with others on one page. Across the top, the page's title and the party: a square for each
/// member, the leader's underlined, open places to invite into, the party's code. Under them, BROWSE and HOST, with QUICK PLAY
/// and RECONNECT beside. Browsing, the list of every server and room heard of fills the left -- filtered by kind and mode, in
/// columns -- and the line picked shows on the right with JOIN. Hosting, the game's settings and its mode's rules stand in two
/// panels, and the game as it will be hosted on the right with HOST. The same on the desktop and in the browser.
/// </summary>
public sealed partial class MultiplayerScreen : ShellScreen
{
    // The side panel's width, and the room between panels. Layout pixels.
    private float SideWidth => Size == ShellSize.Wide ? 520f : 440f;

    private const float PanelGap = 20f;

    // How often the list is asked for again while the page is up and this player plays nowhere.
    private const float RelistSeconds = 5f;

    private CoopStatus _shown;
    private IReadOnlyList<CoopRoomInfo>? _shownListing;
    private float _sinceListed;
    private string _code = string.Empty;

    // The control that held the keys on the page this one replaces, as KeyOf names it, or null for none.
    private string? _keptFocus;

    // Browsing (0) or hosting (1); the strip that picks it and the two tabs' content, kept so a switch slides the strip's block
    // and swaps the content without a rebuild.
    private int _tab;

    /// <summary>Sets whether the page opens on HOST rather than BROWSE, for a capture.</summary>
    public bool StartOnHost { init => _tab = value ? 1 : 0; }
    private TabStrip? _tabs;
    private UiElement? _browseTab;
    private UiElement? _hostTab;

    /// <summary>Initializes a new instance of the <see cref="MultiplayerScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MultiplayerScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Coop;

    /// <inheritdoc/>
    protected override IReadOnlyList<KeyHint> Hints => _tab == 0
        ? [new("↑↓", "Select"), new("ENTER", "Join"), new("F", "Quick play"), new("R", "Refresh"), new("TAB", "Host"), new("ESC", "Back")]
        : [new("↑↓", "Select"), new("←→", "Adjust"), new("TAB", "Browse"), new("ESC", "Back")];

    private ICoopRooms? Coop => Context.Host.Coop;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        (_keptFocus, float? scrolled) = LeavePlace();
        _page = null;
        _listScroll = null;
        _tabs = null;
        _rows.Clear();
        if (Coop is not { } coop)
        {
            var notHere = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f, Margin = new Thickness(SideMargin, 56f) };
            notHere.AddChild(new SectionTitle(theme, "Together", "Multiplayer") { HorizontalAlignment = HorizontalAlignment.Left });
            notHere.AddChild(Body(theme, "Multiplayer needs this build's WebRTC library, which isn't beside the game.", theme.TextMuted));
            return notHere;
        }

        _shown = coop.Status;
        _shownListing = coop.Listing;
        _shownParty = PartyKey;

        if (Size == ShellSize.Compact)
        {
            Grid compact = BuildCompact(theme, coop);
            if (scrolled is { } at)
            {
                _listScroll?.RestoreOffset(at);
            }

            _page = compact;
            return compact;
        }

        var page = new Grid { RowSpacing = 0f, Margin = new Thickness(SideMargin, 32f, SideMargin, 16f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(BuildHeader(theme), 0, 0);
        page.Add(BuildModeBar(theme, coop), 1, 0);
        page.Add(BuildNotes(theme, coop), 2, 0);
        _browseTab = BuildBrowseTab(theme, coop);
        _hostTab = BuildHostTab(theme, coop);
        page.Add(_browseTab, 3, 0);
        page.Add(_hostTab, 3, 0);
        ShowContent();
        if (scrolled is { } offset)
        {
            _listScroll?.RestoreOffset(offset);
        }

        _page = page;
        return page;
    }

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        _sinceListed = 0f;
        Coop?.RefreshListing();
    }

    /// <inheritdoc/>
    protected override void OnExit()
    {
        _copied = null;
        _picked = null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The room, the list and the party change in the background; the page is rebuilt when any does. Out of a game the list is
    /// asked for again every few seconds, so a server that opens while the page is up shows without a press of REFRESH.
    /// </remarks>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (Coop is not { } coop)
        {
            return;
        }

        _clock += elapsedSeconds;
        _sinceListed += elapsedSeconds;
        if (coop.Status.Phase == CoopPhase.None && _sinceListed >= RelistSeconds)
        {
            _sinceListed = 0f;
            coop.RefreshListing();
        }

        if (coop.Status != _shown || !ReferenceEquals(coop.Listing, _shownListing) || PartyKey != _shownParty)
        {
            Invalidate();
        }
    }

    // The title on the left, the party on the right.
    private Grid BuildHeader(UiTheme theme)
    {
        var header = new Grid();
        header.RowDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Star);
        header.Add(new SectionTitle(theme, "Together", "Multiplayer") { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom }, 0, 0);
        if (Parties is { } parties)
        {
            UiElement party = BuildPartyCluster(theme, parties);
            party.HorizontalAlignment = HorizontalAlignment.Right;
            party.VerticalAlignment = VerticalAlignment.Bottom;
            header.Add(party, 0, 1);
        }

        return header;
    }

    // BROWSE and HOST on the left; QUICK PLAY and RECONNECT on the right.
    private Grid BuildModeBar(UiTheme theme, ICoopRooms coop)
    {
        var bar = new Grid { Margin = new Thickness(0f, 20f, 0f, 0f) };
        bar.RowDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Star);
        bar.ColumnDefinitions.Add(GridLength.Auto);

        int games = coop.Listing?.Count ?? 0;
        var tabs = new TabStrip(theme, ["Browse", "Host"])
        {
            Notes = [string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{games} {(games == 1 ? "GAME" : "GAMES")}"), "CREATE"],
            SegmentHeight = 44f,
            TabPadding = 28f,
            TextSize = 17f,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tabs.SelectedIndex = _tab;
        tabs.SelectionChanged += index =>
        {
            _tab = index;
            ShowContent();

            // The tab shown now was collapsed when this frame was laid out, so it has no place yet -- or last time's. Laid
            // out again before the draw, it is never seen at the corner of the screen for a frame.
            RequestLayout();
            Focus?.Validate();
            RefreshHints();
        };
        _tabs = tabs;
        bar.Add(tabs, 0, 0);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10f, VerticalAlignment = VerticalAlignment.Center };
        var quick = new Button(theme, "QUICK PLAY")
        {
            Accent = ButtonAccent.Outline,
            Key = "F",
            TextSize = 17f,
            Tracking = 0.12f,
            Padding = new Thickness(22f, 0f),
            Height = 52f,
            Tooltip = "Join the best open game listed: near, with people in it, room for you.",
        };
        quick.Activated += _ => coop.QuickPlay();
        actions.AddChild(quick);
        if (coop.LastPlayed is { } last)
        {
            var reconnect = new Button(theme, "RECONNECT")
            {
                Accent = ButtonAccent.Ghost,
                TextSize = 17f,
                Tracking = 0.12f,
                Padding = new Thickness(22f, 0f),
                Height = 52f,
                SubCaption = coop.LastPlayedAt is { } at ? $"{last} · {Ago(at)}" : last,
                Tooltip = $"Join {last} again, where you last played.",
            };
            reconnect.Activated += _ => coop.Reconnect();
            actions.AddChild(reconnect);
        }

        bar.Add(actions, 0, 2);
        return bar;
    }

    // Shows the tab picked and hides the other.
    private void ShowContent()
    {
        if (_browseTab is not null && _hostTab is not null)
        {
            _browseTab.Visibility = _tab == 0 ? Visibility.Visible : Visibility.Collapsed;
            _hostTab.Visibility = _tab == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Moves to a tab as its strip does, the block sliding across; the keys go to the strip, off the tab left behind.
    private void SwitchTab(int index)
    {
        if (_tabs is { } tabs)
        {
            tabs.SelectedIndex = index;
            Focus?.Focus(tabs);
        }
    }

    // Between the bar and the panels: where this player plays, when it does; the party member picked, when one is.
    private StackPanel BuildNotes(UiTheme theme, ICoopRooms coop)
    {
        var notes = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(0f, 12f, 0f, 12f) };
        if (BuildStatus(theme, coop.Status) is { } status)
        {
            notes.AddChild(status);
        }

        if (Parties is { } parties && BuildPartyNote(theme, parties) is { } party)
        {
            notes.AddChild(party);
        }

        return notes;
    }

    // Where this player is, once it plays: a hosted room's code large, the room or server joined, or why it couldn't, with
    // the way out. Nothing while it plays nowhere.
    private StackPanel? BuildStatus(UiTheme theme, CoopStatus status)
    {
        if (status.Phase == CoopPhase.None)
        {
            return null;
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18f };
        switch (status.Phase)
        {
            case CoopPhase.Playing when status.Hosting:
                line.AddChild(new Label(theme.Heading, "HOSTING ROOM", 14f, theme.Accent) { VerticalAlignment = VerticalAlignment.Center });
                line.AddChild(new Label(theme.Heading, status.Code ?? string.Empty, 30f, theme.TextBright) { VerticalAlignment = VerticalAlignment.Center });
                line.AddChild(new Label(theme.Body, "Friends join with this code, on the desktop or at openigi.com.", 16f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center });
                break;
            case CoopPhase.Playing:
                line.AddChild(StatusText(theme, status.Code is { } room ? $"In room {room}." : "On a server.", UiPalette.TextBody));
                break;
            case CoopPhase.Connecting:
                line.AddChild(StatusText(theme, status.Hosting ? "Opening a room…" : status.Code is { } finding ? $"Finding room {finding}…" : "Reaching the server…", UiPalette.TextBody));
                break;
            default:
                line.AddChild(StatusText(theme, $"Couldn't play together: {status.Error}.", theme.Danger));
                break;
        }

        if (status.Phase is CoopPhase.Playing or CoopPhase.Connecting)
        {
            var leave = new Button(theme, "LEAVE") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.12f, Padding = new Thickness(16f, 0f), Height = 40f, VerticalAlignment = VerticalAlignment.Center };
            leave.Activated += _ => Coop?.Leave();
            line.AddChild(leave);
        }

        return line;
    }

    // How long ago, as the design's RECONNECT says it: "just now", "4m ago", "2h ago", "3d ago".
    private static string Ago(DateTimeOffset at)
    {
        TimeSpan since = DateTimeOffset.UtcNow - at;
        return since.TotalMinutes < 1 ? "just now"
            : since.TotalHours < 1 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)since.TotalMinutes}m ago")
            : since.TotalDays < 1 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)since.TotalHours}h ago")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)since.TotalDays}d ago");
    }

    // A part of a status or party line: as wide as its words, so what follows it -- LEAVE, KICK -- stands beside it. A wrapping label
    // in a row takes the row's whole width and pushes the rest off the page.
    private static Label StatusText(UiTheme theme, string text, Bgra32 colour, float size = 16f) =>
        new(theme.Body, text, size, colour) { VerticalAlignment = VerticalAlignment.Center };

    private static Label Body(UiTheme theme, string text, Bgra32 colour, float size = 16f) =>
        new(theme.Body, text, size, colour) { Wrap = true, Margin = new Thickness(0f, 2f, 0f, 2f) };

    // A panel of the page: the glass the design draws its groups on, the accent along its top when it is the side panel.
    private static GlassPanel Panel(UiTheme theme, UiElement content, bool side = false, Thickness? padding = null)
    {
        _ = side;
        var panel = new GlassPanel(theme)
        {
            Padding = padding ?? new Thickness(0f),
            Tint = Bgra32.FromRgba(12, 14, 15, ShellSizes.IsCompact(theme) ? (byte)235 : (byte)199),
            Border = UiPalette.White(0.12f),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        panel.AddChild(content);
        return panel;
    }

    // A group's name inside a panel: small, in the accent, with a hairline running on after it.
    private static GroupHeader GroupTitle(UiTheme theme, string title, Thickness margin) => new(theme, title) { Margin = margin };
}
