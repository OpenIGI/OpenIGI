using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Multiplayer, every way of playing with others on one page. Across the top, the page's title with BROWSE and HOST beside
/// it, and the party: a square for each member, the leader's underlined, open places to invite into, INVITE and ENTER CODE.
/// Browsing, the list of every server and room heard of fills the left -- filtered by kind, name and mode, in columns -- and
/// on the right QUICK PLAY and RECONNECT stand over the line picked, with JOIN. Hosting, the game's settings and its mode's
/// rules stand in two panels, and the game as it will be hosted on the right with HOST. The same on the desktop and in the browser.
/// </summary>
public sealed partial class MultiplayerScreen : ShellScreen
{
    // The side panel's width, and the room between panels. Layout pixels.
    private float SideWidth => Size == ShellSize.Wide ? 520f : 470f;

    private const float PanelGap = 24f;

    // The design's button text: its size and letterspacing.
    private const float ButtonText = 17f;
    private const float ButtonTracking = 0.12f;

    // How often the list is asked for again while the page is up and this player plays nowhere.
    private const float RelistSeconds = 5f;

    private CoopStatus _shown;
    private IReadOnlyList<CoopRoomInfo>? _shownListing;
    private float _sinceListed;

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
        ? [new("↑ ↓", "Select"), new("Esc", "Back")]
        : [new("↑ ↓", "Select"), new("← →", "Adjust"), new("Tab", "Browse"), new("Esc", "Back")];

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

        bool roomy = Size == ShellSize.Wide;
        var page = new Grid { RowSpacing = 0f, Margin = new Thickness(SideMargin, roomy ? 32f : 26f, SideMargin, roomy ? 32f : 20f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(BuildHeader(theme, coop), 0, 0);
        if (BuildNotes(theme, coop) is { } notes)
        {
            page.Add(notes, 1, 0);
        }

        _browseTab = BuildBrowseTab(theme, coop);
        _hostTab = BuildHostTab(theme, coop);
        page.Add(_browseTab, 2, 0);
        page.Add(_hostTab, 2, 0);
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

    // The title, BROWSE and HOST beside it, and the party against the right edge: all on the title's baseline.
    private Grid BuildHeader(UiTheme theme, ICoopRooms coop)
    {
        var header = new Grid { ColumnSpacing = Size == ShellSize.Wide ? 32f : 24f, Margin = new Thickness(0f, 0f, 0f, Size == ShellSize.Wide ? 24f : 21f) };
        header.RowDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Star);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.Add(new SectionTitle(theme, "Together", "Multiplayer") { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom }, 0, 0);

        int games = coop.Listing?.Count ?? 0;
        var tabs = new TabStrip(theme, ["Browse", "Host"])
        {
            Notes = [games.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty],
            SegmentHeight = 36f,
            TabPadding = 18f,
            TextSize = ButtonText,
            VerticalAlignment = VerticalAlignment.Bottom,
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
        header.Add(tabs, 0, 1);

        if (Parties is { } parties)
        {
            UiElement party = BuildPartyCluster(theme, parties);
            party.HorizontalAlignment = HorizontalAlignment.Right;
            party.VerticalAlignment = VerticalAlignment.Bottom;
            header.Add(party, 0, 3);
        }

        return header;
    }

    // QUICK PLAY and RECONNECT, side by side over the line picked, and under them where RECONNECT leads.
    private StackPanel BuildQuickActions(UiTheme theme, ICoopRooms coop)
    {
        var block = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f };
        var row = new Grid { ColumnSpacing = 12f };
        row.RowDefinitions.Add(GridLength.Auto);
        row.ColumnDefinitions.Add(GridLength.Star);
        var quick = new Button(theme, "QUICK PLAY")
        {
            Accent = ButtonAccent.Ghost,
            Key = Context.Host.TouchScreen ? string.Empty : "F",
            LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Play),
            GlyphSize = 16f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(12f, 0f),
            Height = 44f,
            Tooltip = "Join the best open game listed: near, with people in it, room for you.",
        };
        quick.Activated += _ => coop.QuickPlay();
        row.Add(quick, 0, 0);
        if (coop.LastPlayed is { } last)
        {
            row.ColumnDefinitions.Add(GridLength.Star);
            var reconnect = new Button(theme, "RECONNECT")
            {
                Accent = ButtonAccent.Ghost,
                LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.History),
                GlyphSize = 18f,
                TextSize = ButtonText,
                Tracking = ButtonTracking,
                Padding = new Thickness(12f, 0f),
                Height = 44f,
                Tooltip = $"Join {last} again, where you last played.",
            };
            reconnect.Activated += _ => coop.Reconnect();
            row.Add(reconnect, 0, 1);
            block.AddChild(row);
            string when = coop.LastPlayedAt is { } at ? $"Reconnect: {last} · {Ago(at)}" : $"Reconnect: {last}";
            block.AddChild(new Label(theme.Mono, ServerRow.Fit(when, theme.Mono, 13f, SideWidth - 4f), 13f, theme.TextMuted) { Margin = new Thickness(2f, 0f, 0f, 0f) });
            return block;
        }

        block.AddChild(row);
        return block;
    }

    // An icon as a button's mark.
    private static Action<UiPainter, UiVec2, float, Bgra32> Glyph(OpenIGI.UI.Kit.KitIcon icon) =>
        (painter, centre, size, ink) => OpenIGI.UI.Kit.KitIcons.Draw(painter, icon, centre, size, ink);

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

    // Between the header and the panels: where this player plays, when it does; the party member picked, when one is.
    // Nothing when there is nothing to say, so the panels stand straight under the header.
    private StackPanel? BuildNotes(UiTheme theme, ICoopRooms coop)
    {
        var notes = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(0f, -8f, 0f, 16f) };
        if (BuildStatus(theme, coop.Status) is { } status)
        {
            notes.AddChild(status);
        }

        if (Parties is { } parties && BuildPartyNote(theme, parties) is { } party)
        {
            notes.AddChild(party);
        }

        return notes.Children.Count > 0 ? notes : null;
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
            : (int)since.TotalDays == 1 ? "1 day ago"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)since.TotalDays} days ago");
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
