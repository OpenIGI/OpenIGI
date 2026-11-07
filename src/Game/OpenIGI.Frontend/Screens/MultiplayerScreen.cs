using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// Multiplayer, every way of playing with others on one page. Down the left: the page's title and where this player is,
/// a bar to join by a code, play the best open game or go back to the last, and the server browser -- every dedicated server,
/// room, server on this network, saved and recent one, in columns, filtered and sorted. On the right: the party, and one
/// panel hosting a game -- the mode first (co-op or a versus mode), the mission, where it runs (this game, or a dedicated
/// server started here), then the rules of that mode. The same on the desktop and in the browser, which play each other.
/// </summary>
public sealed partial class MultiplayerScreen : ShellScreen
{
    // The right column's width, and the room between the columns. Layout pixels.
    private const float SideWidth = 540f;
    private const float ColumnGap = 40f;

    // How often the list is asked for again while the page is up and this player plays nowhere.
    private const float RelistSeconds = 5f;

    private CoopStatus _shown;
    private IReadOnlyList<CoopRoomInfo>? _shownListing;
    private float _sinceListed;
    private string _code = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="MultiplayerScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public MultiplayerScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Coop;

    private ICoopRooms? Coop => Context.Host.Coop;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
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

        var page = new Canvas();
        page.Add(BuildBrowsing(theme, coop), new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(SideMargin, 40f, SideMargin + SideWidth + ColumnGap, 40f)));
        page.Add(BuildSide(theme, coop), new Anchor(new UiVec2(1f, 0f), UiVec2.One, new Thickness(-(SideMargin + SideWidth), 40f, SideMargin, 40f)));
        return page;
    }

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        _sinceListed = 0f;
        Coop?.RefreshListing();
    }

    /// <inheritdoc/>
    protected override void OnExit() => _copied = null;

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

    // The left: the title and where this player is, the bar to join, and the browser taking the rest.
    private Grid BuildBrowsing(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 22f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        heading.AddChild(new SectionTitle(theme, "Together", "Multiplayer") { HorizontalAlignment = HorizontalAlignment.Left });
        heading.AddChild(BuildStatus(theme, coop.Status));
        layout.Add(heading, 0, 0);
        layout.Add(BuildJoinBar(theme, coop), 1, 0);
        layout.Add(BuildBrowser(theme, coop), 2, 0);
        return layout;
    }

    // The right: the party above, hosting below.
    private Grid BuildSide(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 20f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        if (Parties is { } parties)
        {
            layout.Add(BuildParty(theme, parties), 0, 0);
        }

        layout.Add(BuildHosting(theme, coop), 1, 0);
        return layout;
    }

    // Where this player is: what the page is for while it plays nowhere; once it does, the game -- a hosted room's code large --
    // or why it couldn't, with the way out.
    private UiElement BuildStatus(UiTheme theme, CoopStatus status)
    {
        if (status.Phase == CoopPhase.None)
        {
            return Body(theme, "Join a server or a friend's room, or host a game of your own: co-op against the mission, or one side against the other.", UiPalette.TextBody);
        }

        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18f };
        switch (status.Phase)
        {
            case CoopPhase.Playing when status.Hosting:
                line.AddChild(new Label(theme.Body, "HOSTING ROOM", 15f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
                line.AddChild(new Label(theme.Heading, status.Code ?? string.Empty, 34f, theme.Accent) { VerticalAlignment = VerticalAlignment.Center });
                line.AddChild(new Label(theme.Body, "Friends join with this code, on the desktop or at openigi.com.", 15f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
                break;
            case CoopPhase.Playing:
                line.AddChild(Body(theme, status.Code is { } room ? $"In room {room}." : "On a server.", UiPalette.TextBody));
                break;
            case CoopPhase.Connecting:
                line.AddChild(Body(theme, status.Hosting ? "Opening a room…" : status.Code is { } finding ? $"Finding room {finding}…" : "Reaching the server…", UiPalette.TextBody));
                break;
            default:
                line.AddChild(Body(theme, $"Couldn't play together: {status.Error}.", theme.Danger));
                break;
        }

        if (status.Phase is CoopPhase.Playing or CoopPhase.Connecting)
        {
            var leave = new Button(theme, "LEAVE") { Accent = ButtonAccent.Ghost, TextSize = 14f, Tracking = 0.18f, Padding = new Thickness(16f, 8f), VerticalAlignment = VerticalAlignment.Center };
            leave.Activated += _ => Coop?.Leave();
            line.AddChild(leave);
        }

        return line;
    }

    // One line: a code and JOIN, then QUICK PLAY and RECONNECT.
    private Grid BuildJoinBar(UiTheme theme, ICoopRooms coop)
    {
        var line = new Grid { ColumnSpacing = 12f };
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Absolute(260f));
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Auto);
        var code = new TextBox(theme) { Id = "room-code", Placeholder = "ROOM CODE", MaxLength = 6, VerticalAlignment = VerticalAlignment.Center, Tooltip = "A friend's room code: six letters and digits." };
        code.Text = _code;
        code.TextChanged += text => _code = text;
        var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(30f, 13f) };
        join.Activated += _ => coop.Join(_code);
        var quick = new Button(theme, "QUICK PLAY")
        {
            Accent = ButtonAccent.Ghost,
            TextSize = 16f,
            Tracking = 0.18f,
            Padding = new Thickness(22f, 12f),
            Tooltip = "Join the best open game listed: near, with people in it, room for you.",
        };
        quick.Activated += _ => coop.QuickPlay();
        line.Add(code, 0, 0);
        line.Add(join, 0, 1);
        line.Add(quick, 0, 3);
        if (coop.LastPlayed is { } last)
        {
            var reconnect = new Button(theme, "RECONNECT") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), Tooltip = $"Join {last} again, where you last played." };
            reconnect.Activated += _ => coop.Reconnect();
            line.Add(reconnect, 0, 4);
        }

        return line;
    }

    private static Label Body(UiTheme theme, string text, Bgra32 colour) =>
        new(theme.Body, text, 17f, colour) { Wrap = true, Margin = new Thickness(0f, 4f, 0f, 4f) };

    private static (GlassPanel Panel, StackPanel Content) Group(UiTheme theme, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        content.AddChild(new GroupHeader(theme, title));
        var panel = new GlassPanel(theme) { Padding = new Thickness(24f, 20f, 24f, 16f), VerticalAlignment = VerticalAlignment.Top };
        panel.AddChild(content);
        return (panel, content);
    }
}
