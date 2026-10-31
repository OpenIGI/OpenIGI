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
/// The co-op section: down the left, the page's title, the room this player is in, a friend's room to join by its code and
/// the public rooms; on the right, one panel hosting a mission -- the look this player plays in, the mission and how it is
/// played, then HOST; and between the two, the look itself standing in the level, turned by dragging it round. The same on
/// the desktop and in the browser, which play each other.
/// </summary>
public sealed class CoopScreen : ShellScreen
{
    // The two columns' width, and the room the look stands in between them. Layout pixels.
    private const float ColumnWidth = 560f;
    private const float ColumnGap = 40f;

    private static readonly string[] Difficulties = AiDifficultyNames.All;
    private static readonly string[] Listings = ["Public", "Private"];
    private static readonly string[] OffOn = ["Off", "On"];
    private static readonly int[] ReviveChoices = [3, 5, 10];
    private static readonly int[] ToughnessChoices = [0, 50, 100, 150];

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

        float inner = SideMargin + ColumnWidth + ColumnGap;
        var turn = new TurnArea(theme, string.Empty) { Tooltip = "Drag, or press left and right, to turn your look round." };
        turn.Turned += coop.TurnLook;

        var page = new Canvas();
        page.Add(turn, new Anchor(UiVec2.Zero, UiVec2.One, new Thickness(inner, 0f, inner, 48f)));
        page.Add(BuildRooms(theme, coop), new Anchor(UiVec2.Zero, new UiVec2(0f, 1f), new Thickness(SideMargin, 56f, -(SideMargin + ColumnWidth), 48f)));
        page.Add(BuildHosting(theme, coop), new Anchor(new UiVec2(1f, 0f), UiVec2.One, new Thickness(-(SideMargin + ColumnWidth), 0f, SideMargin, 48f)));
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

    // The left column: the page's title, where this player is, a code to join by, and the open rooms.
    private Grid BuildRooms(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 32f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        var heading = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f };
        heading.AddChild(new SectionTitle(theme, "Together", "Co-op") { HorizontalAlignment = HorizontalAlignment.Left });
        heading.AddChild(BuildStatus(theme, coop.Status));
        layout.Add(heading, 0, 0);
        layout.Add(BuildJoin(theme, coop), 1, 0);
        layout.Add(BuildList(theme, coop), 2, 0);
        return layout;
    }

    // Where this player is: a line on what the page is for while in no room; once in one, opening or finding it, in it -- its
    // code large when it hosts -- or turned away, on a panel with the way out of it.
    private UiElement BuildStatus(UiTheme theme, CoopStatus status)
    {
        if (status.Phase == CoopPhase.None)
        {
            return Body(theme, "Host a mission for friends to join, or join theirs with the code they send you.", UiPalette.TextBody);
        }

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
            default:
                content.AddChild(Body(theme, $"Couldn't play together: {status.Error}.", theme.Danger));
                break;
        }

        if (status.Phase is CoopPhase.Playing or CoopPhase.Connecting)
        {
            var leave = new Button(theme, "LEAVE ROOM") { Accent = ButtonAccent.Ghost, TextSize = 16f, Tracking = 0.18f, Padding = new Thickness(22f, 12f), HorizontalAlignment = HorizontalAlignment.Left };
            leave.Activated += _ => Coop?.Leave();
            content.AddChild(leave);
        }

        panel.Margin = new Thickness(0f, 8f, 0f, 0f);
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

    // The public rooms, each a card as the campaign's missions are: how full on the badge, its name and mission; with none, a
    // line saying so.
    private Grid BuildList(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 12f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);
        layout.Add(new GroupHeader(theme, "Open rooms"), 0, 0);
        var refresh = new Button(theme, "REFRESH") { Accent = ButtonAccent.Text, TextSize = 15f, Tracking = 0.18f, Padding = new Thickness(8f, 4f), VerticalAlignment = VerticalAlignment.Center };
        refresh.Activated += _ => coop.RefreshListing();
        layout.Add(refresh, 0, 1);

        if (coop.Listing is { Count: 0 } && coop.ListingError is null)
        {
            layout.Add(BuildNoRooms(theme), 1, 0, columnSpan: 2);
            return layout;
        }

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (coop.Listing is not { } rooms)
        {
            rows.AddChild(Body(theme, "Looking for rooms…", theme.TextMuted));
        }
        else if (coop.ListingError is { } error)
        {
            rows.AddChild(Body(theme, $"Couldn't list rooms: {error}.", theme.Danger));
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

    // No public rooms: said on a faint panel.
    private static GlassPanel BuildNoRooms(UiTheme theme)
    {
        var panel = new GlassPanel(theme)
        {
            Padding = new Thickness(20f, 16f, 20f, 16f),
            Tint = Bgra32.FromRgba(14, 15, 17, 150),
            TintBottom = Bgra32.FromRgba(10, 11, 12, 150),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.AddChild(new Label(theme.Body, "No public rooms right now.", 16f, UiPalette.TextBody));
        return panel;
    }

    // The right: one panel hosting a mission -- its title and how to turn the look, the look and what to play, how the room
    // plays, and HOST across its foot.
    private GlassPanel BuildHosting(UiTheme theme, ICoopRooms coop)
    {
        var column = new StackPanel { Orientation = Orientation.Vertical };

        var header = new Grid { Margin = new Thickness(20f, 18f, 20f, 16f) };
        header.RowDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Star);
        header.ColumnDefinitions.Add(GridLength.Auto);
        header.Add(new Label(theme.Heading, "HOST A MISSION", 22f, theme.TextBright) { VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        header.Add(new Label(theme.Mono, "DRAG MODEL TO TURN", 11f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center }, 0, 1);
        column.AddChild(header);
        column.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.08f) });

        column.AddChild(Section(theme, "Loadout"));
        List<PlayerSkin> looks = [.. PlayerSkins.All];
        int chosen = Math.Max(0, looks.FindIndex(s => s.Id == PlayerSkins.Of(PlayerSkins.Chosen.Value).Id));
        OptionCycler operative = Row(theme, "Operative", [.. looks.Select(static s => s.Name)], chosen, "How you appear to the others in co-op, shown standing here.");
        operative.SelectionChanged += index =>
        {
            PlayerSkins.Chosen.Value = looks[index].Id;
            Context.Host.SaveSettings();
            coop.PreviewLook(looks[index].Id);
        };
        column.AddChild(operative);

        IReadOnlyList<MissionInfo> missions = Context.Missions;
        if (missions.Count > 0)
        {
            _mission = Math.Clamp(_mission, 0, missions.Count - 1);
            if (_difficulty < 0)
            {
                _difficulty = Math.Clamp(Context.Config.Active?.Difficulty ?? 1, 0, Difficulties.Length - 1);
            }

            OptionCycler mission = Row(theme, "Mission", [.. missions.Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Number}. {m.Name}"))], _mission);
            mission.SelectionChanged += index => _mission = index;
            OptionCycler difficulty = Row(theme, "Difficulty", Difficulties, _difficulty);
            difficulty.SelectionChanged += index => _difficulty = index;
            column.AddChild(mission);
            column.AddChild(difficulty);
        }

        column.AddChild(Section(theme, "Rules"));
        OptionCycler listing = Row(theme, "Listing", Listings, _listing, "Public rooms show under Open rooms; a private one only lets in those you give the code to.");
        listing.SelectionChanged += index => _listing = index;
        column.AddChild(listing);

        OptionCycler friendlyFire = Row(theme, "Friendly fire", OffOn, CoopCVars.FriendlyFire.Value ? 1 : 0, "Whether your rounds hurt your teammates, and theirs you.");
        friendlyFire.SelectionChanged += index =>
        {
            CoopCVars.FriendlyFire.Value = index == 1;
            Context.Host.SaveSettings();
        };
        column.AddChild(friendlyFire);

        OptionCycler reviveTime = Row(
            theme,
            "Revive time",
            [.. ReviveChoices.Select(static s => string.Create(CultureInfo.InvariantCulture, $"{s} s"))],
            Math.Max(0, Array.IndexOf(ReviveChoices, CoopCVars.ReviveSeconds.Value)),
            "How long a teammate stands beside a downed player to bring them back.");
        reviveTime.SelectionChanged += index =>
        {
            CoopCVars.ReviveSeconds.Value = ReviveChoices[index];
            Context.Host.SaveSettings();
        };
        column.AddChild(reviveTime);

        OptionCycler toughness = Row(
            theme,
            "Enemies",
            [.. ToughnessChoices.Select(static p => p == 0 ? "As alone" : string.Create(CultureInfo.InvariantCulture, $"+{p}%"))],
            Math.Max(0, Array.IndexOf(ToughnessChoices, CoopCVars.EnemyHealthPerPlayer.Value)),
            "How much more health the guards have for each player past the first.");
        toughness.SelectionChanged += index =>
        {
            CoopCVars.EnemyHealthPerPlayer.Value = ToughnessChoices[index];
            Context.Host.SaveSettings();
        };
        column.AddChild(toughness);

        if (missions.Count > 0)
        {
            var host = new PrimaryAction(theme, "Host") { Margin = new Thickness(20f, 16f, 20f, 20f) };
            host.Activated += _ => Host(coop);
            column.AddChild(host);
        }

        var panel = new GlassPanel(theme)
        {
            Tint = Bgra32.FromRgba(16, 17, 19, 214),
            TintBottom = Bgra32.FromRgba(11, 12, 13, 214),
            Blur = 20f,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        panel.AddChild(column);
        return panel;
    }

    // HOST: the mission, difficulty and listing chosen.
    private void Host(ICoopRooms coop)
    {
        IReadOnlyList<MissionInfo> missions = Context.Missions;
        if (missions.Count > 0)
        {
            coop.Host(missions[Math.Clamp(_mission, 0, missions.Count - 1)].Id, Math.Max(0, _difficulty), listed: _listing == 0);
        }
    }

    // A form's line in the hosting panel: its label on the left, its value between arrows on the right.
    private static OptionCycler Row(UiTheme theme, string label, IEnumerable<string> options, int selected, string tooltip = "") =>
        new(theme, label, options, selected) { Inline = true, ContentWidth = 170f, Tooltip = tooltip };

    // A section's name in the hosting panel, small and in the accent.
    private static Eyebrow Section(UiTheme theme, string name) =>
        new(theme, name) { Margin = new Thickness(20f, 18f, 20f, 4f), HorizontalAlignment = HorizontalAlignment.Left };

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
