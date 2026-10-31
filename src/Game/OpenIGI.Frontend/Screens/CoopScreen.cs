using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The co-op section, laid out as the campaign's: down the left, the room this player is in, a friend's room to join by
/// its code and the public rooms as list cards; on the right, the look this player plays in standing in the level, and
/// at its foot one strip of glass choosing what to host beside the button that hosts it. The same on the desktop and in
/// the browser, which play each other.
/// </summary>
public sealed class CoopScreen : ShellScreen
{
    // A cycler fills the height it is given; the strip's cells are the campaign's height.
    private const float CellHeight = 96f;

    private static readonly string[] Difficulties = AiDifficultyNames.All;
    private static readonly string[] Listings = ["Public", "Private"];
    private static readonly string[] OffOn = ["Off", "On"];
    private static readonly int[] ReviveChoices = [3, 5, 10];
    private static readonly int[] ToughnessChoices = [0, 25, 50, 100];

    private CoopStatus _shown;
    private IReadOnlyList<CoopRoomInfo>? _shownListing;
    private string _code = string.Empty;
    private int _mission;
    private int _difficulty = -1;
    private int _listing;

    /// <summary>Initializes a new instance of the <see cref="CoopScreen"/> class.</summary>
    /// <param name="context">The shared shell context.</param>
    public CoopScreen(ShellContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override ShellTab Tab => ShellTab.Coop;

    /// <inheritdoc/>
    /// <remarks>The level stays sharp: the look being chosen stands in it.</remarks>
    public override float BackdropBlur => 0f;

    private ICoopRooms? Coop => Context.Host.Coop;

    /// <inheritdoc/>
    protected override UiElement? BuildContent(LayoutContext context)
    {
        UiTheme theme = Context.Theme;
        if (Coop is not { } coop)
        {
            var notHere = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f, Margin = new Thickness(SideMargin, 56f) };
            notHere.AddChild(new SectionTitle(theme, "Together", "Co-op") { HorizontalAlignment = HorizontalAlignment.Left });
            notHere.AddChild(Body(theme, "Co-op needs this build's WebRTC library, which isn't beside the game.", theme.TextMuted));
            return notHere;
        }

        _shown = coop.Status;
        _shownListing = coop.Listing;

        var page = new Canvas();
        page.Add(BuildRooms(theme, coop), new Anchor(UiVec2.Zero, new UiVec2(0f, 1f), new Thickness(SideMargin, 56f, -(SideMargin + 560f), 48f)));
        page.Add(BuildHosting(theme, coop), new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(720f, 0f, SideMargin, 48f)));
        return page;
    }

    /// <inheritdoc/>
    protected override void OnEnter()
    {
        Coop?.RefreshListing();
        Coop?.PreviewLook(PlayerSkins.Of(PlayerSkins.Chosen.Value).Id);
    }

    /// <inheritdoc/>
    protected override void OnExit() => Coop?.PreviewLook(null);

    /// <inheritdoc/>
    /// <remarks>The room and the list change in the background; the page is rebuilt when either does.</remarks>
    protected override void OnUpdate(float elapsedSeconds)
    {
        if (Coop is { } coop && (coop.Status != _shown || !ReferenceEquals(coop.Listing, _shownListing)))
        {
            Invalidate();
        }
    }

    // The left column: the page's title, the room this player is in, a code to join by, and the open rooms.
    private Grid BuildRooms(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 28f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        layout.Add(new SectionTitle(theme, "Together", "Co-op") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        layout.Add(BuildStatus(theme, coop.Status), 1, 0);
        layout.Add(BuildJoin(theme, coop), 2, 0);
        layout.Add(BuildList(theme, coop), 3, 0);
        return layout;
    }

    // Where this player is: nowhere yet, opening or finding a room, in one (its code large when it hosts), or turned away.
    private GlassPanel BuildStatus(UiTheme theme, CoopStatus status)
    {
        var (panel, content) = Group(theme, "Your room");
        switch (status.Phase)
        {
            case CoopPhase.Playing when status.Hosting:
                content.AddChild(new Label(theme.Heading, status.Code ?? string.Empty, 44f, theme.Accent) { Margin = new Thickness(0f, 2f, 0f, 0f) });
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
            var leave = new Button(theme, "LEAVE ROOM") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), HorizontalAlignment = HorizontalAlignment.Left };
            leave.Activated += _ => Coop?.Leave();
            content.AddChild(leave);
        }

        return panel;
    }

    // A friend's room by its code: the box and its button on one line.
    private StackPanel BuildJoin(UiTheme theme, ICoopRooms coop)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        column.AddChild(new GroupHeader(theme, "Join with a code"));
        var line = new Grid { ColumnSpacing = 12f };
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Auto);
        var code = new TextBox(theme) { Id = "room-code", Placeholder = "ROOM CODE", MaxLength = 6, VerticalAlignment = VerticalAlignment.Center };
        code.Text = _code;
        code.TextChanged += text => _code = text;
        var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Solid, CutCorner = 10f, TextSize = 17f, Tracking = 0.18f, Padding = new Thickness(34f, 14f) };
        join.Activated += _ => coop.Join(_code);
        line.Add(code, 0, 0);
        line.Add(join, 0, 1);
        column.AddChild(line);
        return column;
    }

    // The public rooms, each a card as the campaign's missions are: how full on the badge, its name and mission.
    private Grid BuildList(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 8f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.Add(new GroupHeader(theme, "Open rooms"), 0, 0);
        var refresh = new Button(theme, "REFRESH") { Accent = ButtonAccent.Text, TextSize = 15f, Tracking = 0.18f, Padding = new Thickness(8f, 4f), VerticalAlignment = VerticalAlignment.Center };
        refresh.Activated += _ => coop.RefreshListing();
        layout.Add(refresh, 0, 1);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (coop.Listing is not { } rooms)
        {
            rows.AddChild(Body(theme, "Looking for rooms…", theme.TextMuted));
        }
        else if (coop.ListingError is { } error)
        {
            rows.AddChild(Body(theme, $"Couldn't list rooms: {error}.", theme.Danger));
        }
        else if (rooms.Count == 0)
        {
            rows.AddChild(Body(theme, "No public rooms right now. Host one.", theme.TextMuted));
        }
        else
        {
            foreach (CoopRoomInfo room in rooms)
            {
                string mission = Context.Missions.FirstOrDefault(m => m.Id == room.MissionId) is { } m
                    ? m.Name
                    : string.Create(CultureInfo.InvariantCulture, $"Mission {room.MissionId}");
                var card = new ListCard(
                    theme,
                    string.Create(CultureInfo.InvariantCulture, $"{room.Players}/{room.MaxPlayers}"),
                    $"{room.Name}  ·  {mission}")
                {
                    IsEnabled = room.Players < room.MaxPlayers,
                };
                string join = room.Code;
                card.Activated += _ => coop.Join(join);
                rows.AddChild(card);
            }
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        layout.Add(scroll, 1, 0, columnSpan: 2);
        return layout;
    }

    // The right: the look, standing in the level above and turned by dragging it round; at its foot the operative strip,
    // then what to host beside HOST.
    private Grid BuildHosting(UiTheme theme, ICoopRooms coop)
    {
        var column = new Grid { RowSpacing = 16f };
        column.ColumnDefinitions.Add(GridLength.Star);
        column.RowDefinitions.Add(GridLength.Star);
        column.RowDefinitions.Add(GridLength.Auto);
        column.RowDefinitions.Add(GridLength.Auto);
        column.RowDefinitions.Add(GridLength.Auto);

        var turn = new TurnArea(theme, "Drag to turn") { Tooltip = "Drag, or press left and right, to turn your look round." };
        turn.Turned += coop.TurnLook;
        column.Add(turn, 0, 0);

        List<PlayerSkin> looks = [.. PlayerSkins.All];
        int chosen = Math.Max(0, looks.FindIndex(s => s.Id == PlayerSkins.Of(PlayerSkins.Chosen.Value).Id));
        var operative = new OptionCycler(theme, "Operative", [.. looks.Select(static s => s.Name)], chosen)
        {
            Height = CellHeight,
            ContentWidth = 240f,
            Tooltip = "How you appear to the others in co-op, shown standing here.",
        };
        operative.SelectionChanged += index =>
        {
            PlayerSkins.Chosen.Value = looks[index].Id;
            Context.Host.SaveSettings();
            coop.PreviewLook(looks[index].Id);
        };
        column.Add(Strip(theme, operative), 1, 0);

        IReadOnlyList<MissionInfo> missions = Context.Missions;
        if (missions.Count == 0)
        {
            return column;
        }

        _mission = Math.Clamp(_mission, 0, missions.Count - 1);
        if (_difficulty < 0)
        {
            _difficulty = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
        }

        var mission = new OptionCycler(
            theme, "Mission", [.. missions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Number}. {m.Name}"))], _mission)
        {
            Height = CellHeight,
            ContentWidth = 230f,
        };
        mission.SelectionChanged += index => _mission = index;
        var difficulty = new OptionCycler(theme, "Difficulty", Difficulties, _difficulty) { Height = CellHeight };
        difficulty.SelectionChanged += index => _difficulty = index;
        var listing = new OptionCycler(theme, "Listing", Listings, _listing)
        {
            Height = CellHeight,
            ContentWidth = 120f,
            Tooltip = "Public rooms show under Open rooms; a private one only lets in those you give the code to.",
        };
        listing.SelectionChanged += index => _listing = index;

        var host = new PrimaryAction(theme, "Host") { Width = 300f };
        host.Activated += _ => coop.Host(missions[_mission].Id, _difficulty, listed: _listing == 0);

        var actions = new FlexPanel { Orientation = Orientation.Horizontal, Height = CellHeight, HorizontalAlignment = HorizontalAlignment.Left };
        actions.Add(Strip(theme, mission, difficulty, listing));
        actions.Add(host);
        column.Add(BuildRules(theme), 2, 0);
        column.Add(actions, 3, 0);
        return column;
    }

    // How the room plays, the host's to set and saved with its settings: whether rounds hurt teammates, and how long a revival takes.
    private GlassPanel BuildRules(UiTheme theme)
    {
        var friendlyFire = new OptionCycler(theme, "Friendly fire", OffOn, CoopCVars.FriendlyFire.Value ? 1 : 0)
        {
            Height = CellHeight,
            ContentWidth = 120f,
            Tooltip = "Whether your rounds hurt your teammates, and theirs you.",
        };
        friendlyFire.SelectionChanged += index =>
        {
            CoopCVars.FriendlyFire.Value = index == 1;
            Context.Host.SaveSettings();
        };

        int revive = Math.Max(0, Array.IndexOf(ReviveChoices, CoopCVars.ReviveSeconds.Value));
        var reviveTime = new OptionCycler(
            theme, "Revive", [.. ReviveChoices.Select(static s => string.Create(CultureInfo.InvariantCulture, $"{s} s"))], revive)
        {
            Height = CellHeight,
            ContentWidth = 100f,
            Tooltip = "How long a teammate stands beside a downed player to bring them back.",
        };
        reviveTime.SelectionChanged += index =>
        {
            CoopCVars.ReviveSeconds.Value = ReviveChoices[index];
            Context.Host.SaveSettings();
        };

        int tough = Math.Max(0, Array.IndexOf(ToughnessChoices, CoopCVars.EnemyHealthPerPlayer.Value));
        var toughness = new OptionCycler(
            theme,
            "Enemies",
            [.. ToughnessChoices.Select(static p => p == 0 ? "As alone" : string.Create(CultureInfo.InvariantCulture, $"+{p}%"))],
            tough)
        {
            Height = CellHeight,
            ContentWidth = 130f,
            Tooltip = "How much more health the guards have for each player past the first.",
        };
        toughness.SelectionChanged += index =>
        {
            CoopCVars.EnemyHealthPerPlayer.Value = ToughnessChoices[index];
            Context.Host.SaveSettings();
        };

        return Strip(theme, friendlyFire, reviveTime, toughness);
    }

    // One strip of glass holding cyclers side by side, as the campaign's conditions are held.
    private static GlassPanel Strip(UiTheme theme, params OptionCycler[] cells)
    {
        var strip = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(18, 20, 22, 184),
            TintBottom = Bgra32.FromRgba(10, 12, 13, 184),
            Blur = 20f,
            BorderEdges = UiEdges.Top | UiEdges.Left | UiEdges.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var row = new FlexPanel { Orientation = Orientation.Horizontal };
        foreach (OptionCycler cell in cells)
        {
            row.Add(cell);
        }

        strip.AddChild(row);
        return strip;
    }

    private static Label Body(UiTheme theme, string text, Bgra32 colour) =>
        new(theme.Body, text, 17f, colour) { Wrap = true, Margin = new Thickness(0f, 4f, 0f, 8f) };

    private static (GlassPanel Panel, StackPanel Content) Group(UiTheme theme, string title)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        content.AddChild(new GroupHeader(theme, title));
        var panel = new GlassPanel(theme) { Padding = new Thickness(28f, 24f, 28f, 18f), VerticalAlignment = VerticalAlignment.Top };
        panel.AddChild(content);
        return (panel, content);
    }
}
