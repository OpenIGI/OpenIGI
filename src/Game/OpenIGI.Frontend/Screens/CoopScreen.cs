using System.Globalization;
using OpenIGI.Game.Ai;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The co-op section: host a mission for friends to join by its room's code, join a friend's by theirs, or pick a public
/// room from the list. The same on the desktop and in the browser, which play each other.
/// </summary>
public sealed class CoopScreen : ShellScreen
{
    private static readonly string[] Difficulties = AiDifficultyNames.All;

    private CoopStatus _shown;
    private IReadOnlyList<CoopRoomInfo>? _shownListing;
    private string _code = string.Empty;
    private int _mission;
    private int _difficulty = -1;
    private bool _listed = true;
    private OptionCycler? _missionCycler;
    private OptionCycler? _difficultyCycler;

    /// <summary>Initializes a new instance of the <see cref="CoopScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public CoopScreen(ShellContext context)
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
        var page = new Grid { RowSpacing = 28f, Padding = new Thickness(SideMargin, 48f, SideMargin, 0f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Together", "Co-op") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);

        if (Coop is not { } coop)
        {
            var (unavailable, text) = Group(theme, "Not on this build");
            text.AddChild(Body(theme, "Co-op needs this build's WebRTC library, which isn't beside the game.", theme.TextMuted));
            page.Add(unavailable, 1, 0);
            return page;
        }

        _shown = coop.Status;
        _shownListing = coop.Listing;
        page.Add(BuildStatus(theme, _shown), 1, 0);

        var columns = new Grid { ColumnSpacing = 24f, VerticalAlignment = VerticalAlignment.Top };
        columns.RowDefinitions.Add(GridLength.Auto);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.ColumnDefinitions.Add(GridLength.Star);
        columns.Add(BuildHost(theme, coop), 0, 0);
        columns.Add(BuildJoin(theme, coop), 0, 1);
        columns.Add(BuildRooms(theme, coop), 0, 2);

        var scroll = new ScrollView(theme);
        scroll.SetContent(columns);
        page.Add(scroll, 2, 0);
        return page;
    }

    /// <inheritdoc/>
    protected override void OnEnter() => Coop?.RefreshListing();

    /// <inheritdoc/>
    /// <remarks>The room and the list change in the background; the page is rebuilt when either does.</remarks>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (Coop is { } coop && (coop.Status != _shown || !ReferenceEquals(coop.Listing, _shownListing)))
        {
            Invalidate();
        }
    }

    private GlassPanel BuildStatus(UiTheme theme, CoopStatus status)
    {
        var (panel, content) = Group(theme, "Your room");
        switch (status.Phase)
        {
            case CoopPhase.Playing when status.Hosting:
                content.AddChild(new Label(theme.Heading, $"ROOM  {status.Code}", 40f, theme.Accent) { Margin = new Thickness(0f, 4f) });
                content.AddChild(Body(theme, "Friends join with this code, on the desktop or at openigi.com.", theme.TextMuted));
                break;
            case CoopPhase.Playing:
                content.AddChild(Body(theme, $"In room {status.Code}.", UiPalette.TextBody));
                break;
            case CoopPhase.Connecting:
                content.AddChild(Body(theme, status.Hosting ? "Opening a room…" : $"Finding room {status.Code}…", UiPalette.TextBody));
                break;
            case CoopPhase.Failed:
                content.AddChild(Body(theme, $"Couldn't play together: {status.Error}.", theme.Danger));
                break;
            default:
                content.AddChild(Body(theme, "Host a mission for friends to join, or join theirs with the code they send you.", theme.TextMuted));
                break;
        }

        if (status.Phase is CoopPhase.Playing or CoopPhase.Connecting)
        {
            var leave = new Button(theme, "LEAVE ROOM") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f) };
            leave.Activated += _ => Coop?.Leave();
            content.AddChild(leave);
        }

        return panel;
    }

    private GlassPanel BuildHost(UiTheme theme, ICoopRooms coop)
    {
        var (panel, content) = Group(theme, "Host");
        IReadOnlyList<MissionInfo> missions = Context.Missions;
        _mission = Math.Clamp(_mission, 0, Math.Max(0, missions.Count - 1));
        if (_difficulty < 0)
        {
            _difficulty = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
        }

        if (missions.Count == 0)
        {
            content.AddChild(Body(theme, "No missions to host.", theme.TextMuted));
            return panel;
        }

        _missionCycler = new OptionCycler(
            theme, "Mission", [.. missions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Number}. {m.Name}"))], _mission);
        content.AddChild(_missionCycler);
        _difficultyCycler = new OptionCycler(theme, "Difficulty", Difficulties, _difficulty);
        content.AddChild(_difficultyCycler);
        var listed = new Toggle(theme, "List publicly", _listed) { Tooltip = "Anyone can find a listed room; otherwise only those you give the code to." };
        listed.ValueChanged += value => _listed = value;
        content.AddChild(listed);

        var host = new Button(theme, "HOST MISSION") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        host.Activated += _ =>
        {
            _mission = _missionCycler.SelectedIndex;
            _difficulty = _difficultyCycler.SelectedIndex;
            coop.Host(missions[_mission].Id, _difficulty, _listed);
        };
        content.AddChild(host);
        return panel;
    }

    private GlassPanel BuildJoin(UiTheme theme, ICoopRooms coop)
    {
        var (panel, content) = Group(theme, "Join");
        content.AddChild(Body(theme, "The six-letter code your friend's game shows.", theme.TextMuted));
        var code = new TextBox(theme) { Id = "room-code", Placeholder = "ROOM CODE", MaxLength = 6 };
        code.Text = _code;
        code.TextChanged += text => _code = text;
        content.AddChild(code);
        var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(26f, 14f) };
        join.Activated += _ => coop.Join(_code);
        content.AddChild(join);
        return panel;
    }

    private GlassPanel BuildRooms(UiTheme theme, ICoopRooms coop)
    {
        var (panel, content) = Group(theme, "Open rooms");
        if (coop.Listing is not { } rooms)
        {
            content.AddChild(Body(theme, "Looking…", theme.TextMuted));
        }
        else if (coop.ListingError is { } error)
        {
            content.AddChild(Body(theme, $"Couldn't list rooms: {error}.", theme.Danger));
        }
        else if (rooms.Count == 0)
        {
            content.AddChild(Body(theme, "No public rooms right now. Host one!", theme.TextMuted));
        }
        else
        {
            foreach (CoopRoomInfo room in rooms)
            {
                string mission = Context.Missions.FirstOrDefault(m => m.Id == room.MissionId) is { } m
                    ? m.Name
                    : string.Create(CultureInfo.InvariantCulture, $"Mission {room.MissionId}");
                var entry = new Button(theme, string.Create(CultureInfo.InvariantCulture, $"{room.Name}  ·  {mission}  ·  {room.Players}/{room.MaxPlayers}"))
                {
                    Accent = ButtonAccent.Ghost,
                    TextSize = 16f,
                    Padding = new Thickness(18f, 12f),
                    IsEnabled = room.Players < room.MaxPlayers,
                };
                string code = room.Code;
                entry.Activated += _ => coop.Join(code);
                content.AddChild(entry);
            }
        }

        var refresh = new Button(theme, "REFRESH") { Accent = ButtonAccent.Text, TextSize = 15f, Tracking = 0.18f, Padding = new Thickness(0f, 10f) };
        refresh.Activated += _ => coop.RefreshListing();
        content.AddChild(refresh);
        return panel;
    }

    private static Label Body(UiTheme theme, string text, OpenIGI.Core.Graphics.Bgra32 colour) =>
        new(theme.Body, text, 17f, colour) { Wrap = true, Margin = new Thickness(0f, 4f, 0f, 8f) };

    private static (GlassPanel Panel, StackPanel Content) Group(UiTheme theme, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        content.AddChild(new GroupHeader(theme, title));
        var panel = new GlassPanel(theme) { Padding = new Thickness(28f, 28f, 28f, 20f), VerticalAlignment = VerticalAlignment.Top };
        panel.AddChild(content);
        return (panel, content);
    }
}
