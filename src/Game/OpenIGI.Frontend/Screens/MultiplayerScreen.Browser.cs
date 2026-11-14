using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// Browsing: every server and room heard of, in one panel -- which kind to show (each with how many there are), how many
/// show, REFRESH; a box to search names and maps, which mode, whether full ones show; the columns, each heading ordering
/// the list by it; the lines; and under them the way to hosting one's own. A line is picked by clicking it or by moving
/// onto it with the keys, and shows on the right; a second click, ENTER on it, or JOIN there, joins it.
/// </content>
public sealed partial class MultiplayerScreen
{
    // Which lines show: every one, or those of one kind.
    private static readonly string[] Kinds = ["ALL", "SERVERS", "ROOMS", "LAN", "SAVED", "RECENT"];

    // Which modes show: any, or one; the ids the lines carry.
    private static readonly string[] FilterModes = [string.Empty, GameModes.Coop, GameModes.TeamDeathmatch, GameModes.FreeForAll, GameModes.Elimination, GameModes.SearchAndDestroy];

    // How many lines' shapes stand in for the list while it has not been heard yet.
    private const int SkeletonRows = 7;

    // How soon a second click on a line must follow the first to join it. Seconds.
    private const float DoubleClickSeconds = 0.4f;

    // Each row's line, for the keys on a focused row.
    private readonly Dictionary<ServerRow, CoopRoomInfo> _rows = [];

    // The line last clicked and when, on the page's clock, to tell a second click.
    private string? _clickedCode;
    private float _clickedAt;

    // The column the list is ordered by (see ServerColumns.Headings), and which way.
    private int _sortBy = ServerColumns.Ping;
    private bool _descending;

    private int _kind;

    // The kind shown before the one shown now, while the page is rebuilt for the change: where the strip's block slides from.
    private int? _kindBefore;
    private int _modeFilter;
    private bool _hideFull;

    // What the search box holds: lines whose name or map has it show.
    private string _search = string.Empty;

    // The line picked, by its code.
    private string? _selectedCode;

    private Grid BuildBrowseTab(UiTheme theme, ICoopRooms coop)
    {
        IReadOnlyList<CoopRoomInfo>? all = coop.Listing;
        List<CoopRoomInfo> passing = Passing(all);
        List<CoopRoomInfo> shown = Order([.. passing.Where(r => KindShown(r, _kind))]);
        CoopRoomInfo? picked = shown.FirstOrDefault(r => r.Code == _selectedCode) ?? shown.FirstOrDefault();
        _selectedCode = picked?.Code;

        var layout = new Grid { ColumnSpacing = PanelGap };
        layout.RowDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Absolute(SideWidth));
        layout.Add(BuildList(theme, coop, all, passing, shown), 0, 0);

        // The right column: QUICK PLAY and RECONNECT over the line picked.
        var side = new Grid { RowSpacing = 12f };
        side.ColumnDefinitions.Add(GridLength.Star);
        side.RowDefinitions.Add(GridLength.Auto);
        side.RowDefinitions.Add(GridLength.Star);
        side.Add(BuildQuickActions(theme, coop), 0, 0);
        GlassPanel detail = BuildDetail(theme, coop, picked);
        detail.VerticalAlignment = VerticalAlignment.Top;
        side.Add(detail, 1, 0);
        layout.Add(side, 0, 1);
        return layout;
    }

    private GlassPanel BuildList(UiTheme theme, ICoopRooms coop, IReadOnlyList<CoopRoomInfo>? all, List<CoopRoomInfo> passing, List<CoopRoomInfo> shown)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.Add(BuildFilters(theme, coop, passing, shown.Count, all?.Count ?? 0), 0, 0);
        body.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f) }, 1, 0);
        body.Add(BuildColumns(theme), 2, 0);
        body.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f) }, 3, 0);

        _rows.Clear();
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        if (all is null)
        {
            // Not heard yet: the list's shape in place of its lines, so the panel is not a blank while it waits.
            for (int i = 0; i < SkeletonRows; i++)
            {
                rows.AddChild(new ServerRowSkeleton(i, SkeletonRows));
            }
        }

        string? empty = all is null ? null
            : coop.ListingError is { } error && shown.Count == 0 ? $"Couldn't list servers: {error}."
            : shown.Count == 0 ? all.Count == 0 ? "No games are open right now. Host one, and it shows here for everyone." : "No games match these filters."
            : null;
        if (empty is not null)
        {
            bool failed = coop.ListingError is not null && all is not null;
            string heading = failed ? "No answer" : all is { Count: 0 } ? "No games open" : "Nothing matches";
            var nothing = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24f, 72f, 24f, 56f) };
            nothing.AddChild(new Label(theme.Display, heading.ToUpperInvariant(), 34f, failed ? theme.Danger : theme.Text) { HorizontalAlignment = HorizontalAlignment.Center });
            nothing.AddChild(new Label(theme.Body, empty, 17f, theme.TextSecondary) { HorizontalAlignment = HorizontalAlignment.Center });
            rows.AddChild(nothing);
        }

        foreach (CoopRoomInfo room in shown)
        {
            rows.AddChild(BuildRow(theme, room));
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        _listScroll = scroll;
        body.Add(scroll, 4, 0);
        body.Add(BuildHostCard(theme), 5, 0);
        return Panel(theme, body);
    }

    // Over the list. On the wide page two lines: the kinds with their counts, how many show and REFRESH; then the search box,
    // the mode and HIDE FULL. On a narrower one, one line: the kind as a button that steps through them, then the same.
    private StackPanel BuildFilters(UiTheme theme, ICoopRooms coop, List<CoopRoomInfo> passing, int shown, int heard)
    {
        bool wide = Size == ShellSize.Wide;
        var block = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f, Margin = new Thickness(16f) };

        var refresh = new Button(theme, string.Empty)
        {
            Accent = ButtonAccent.Ghost,
            Key = Context.Host.TouchScreen ? string.Empty : "R",
            LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Refresh),
            GlyphSize = 18f,
            Padding = new Thickness(12f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Ask for the list again. It is asked for by itself every few seconds.",
        };
        refresh.Activated += _ => Refresh(coop);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f, VerticalAlignment = VerticalAlignment.Center };
        if (!wide)
        {
            int count = passing.Count(r => KindShown(r, _kind));
            var show = new Button(theme, string.Create(CultureInfo.InvariantCulture, $"SHOW: {Kinds[_kind]} {count}"))
            {
                Accent = ButtonAccent.Ghost,
                TrailingGlyph = ChevronDown,
                GlyphSize = 12f,
                TextSize = ButtonText,
                Tracking = ButtonTracking,
                Padding = new Thickness(20f, 0f),
                Height = 44f,
                Tooltip = "Which kind of game the list shows: press for the next.",
            };
            show.Activated += _ =>
            {
                _kind = (_kind + 1) % Kinds.Length;
                Invalidate();
            };
            tools.AddChild(show);
        }

        var search = new TextBox(theme) { Id = SearchId, Placeholder = wide ? "Search name or map" : "Search", MaxLength = 32, Width = wide ? 250f : 190f, Height = 44f, LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Search), VerticalAlignment = VerticalAlignment.Center, Tooltip = "Show only the games whose name or map has this in it." };
        search.Text = _search;
        search.TextChanged += text =>
        {
            _search = text;
            _focusNext = SearchId;
            Invalidate();
        };
        tools.AddChild(search);

        string modeName = _modeFilter == 0 ? "ANY" : GameModes.Title(FilterModes[_modeFilter]).ToUpperInvariant();
        var mode = new Button(theme, $"MODE: {modeName}")
        {
            Accent = ButtonAccent.Ghost,
            TrailingGlyph = ChevronDown,
            GlyphSize = 12f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(20f, 0f),
            Height = 44f,
            Tooltip = "Which mode the list shows: press for the next.",
        };
        mode.Activated += _ =>
        {
            _modeFilter = (_modeFilter + 1) % FilterModes.Length;
            Invalidate();
        };
        tools.AddChild(mode);
        var full = new CheckChip(theme, "Hide full") { Checked = _hideFull, Tooltip = "Whether games with no room left show." };
        full.Activated += _ =>
        {
            _hideFull = !_hideFull;
            Invalidate();
        };
        tools.AddChild(full);

        var first = new Grid { ColumnSpacing = 12f };
        first.RowDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Star);
        first.ColumnDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Auto);
        if (!wide)
        {
            first.Add(tools, 0, 0);
            first.Add(refresh, 0, 3);
            block.AddChild(first);
            return block;
        }

        // The kinds as one strip, like BROWSE and HOST above it, its block sliding to the kind picked. The page is rebuilt
        // for the pick, so the new strip is told where the old one's block stood and the slide plays across the rebuild.
        var kinds = new TabStrip(theme, Kinds)
        {
            Notes = [.. Enumerable.Range(0, Kinds.Length).Select(kind => passing.Count(r => KindShown(r, kind)).ToString(CultureInfo.InvariantCulture))],
            SegmentHeight = 36f,
            TabPadding = 18f,
            TextSize = ButtonText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        kinds.SelectedIndex = _kind;
        if (_kindBefore is { } before)
        {
            kinds.SlideFrom(before);
            _kindBefore = null;
        }

        kinds.SelectionChanged += index =>
        {
            _kindBefore = _kind;
            _kind = index;
            Invalidate();
        };
        first.Add(kinds, 0, 0);
        first.Add(new Label(theme.Mono, string.Create(CultureInfo.InvariantCulture, $"{shown} of {heard}"), 14f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        first.Add(refresh, 0, 3);
        block.AddChild(first);
        block.AddChild(tools);
        return block;
    }

    // A chevron pointing down, as a button's mark: what the button holds can be changed.
    private static void ChevronDown(UiPainter painter, UiVec2 centre, float size, Bgra32 ink) =>
        painter.DrawChevron(centre, size, OpenIGI.UI.Drawing.UiDirection.Down, MathF.Max(1f, size * 0.16f), ink);

    // The columns' headings, each ordering the list by it (again, the other way); the column ordered by is brighter, marked
    // with the way it runs.
    private Grid BuildColumns(UiTheme theme)
    {
        bool wide = Size == ShellSize.Wide;
        var header = new Grid { ColumnSpacing = ServerColumns.Gap, Margin = new Thickness(ServerColumns.PadLeft, 0f, ServerColumns.PadRight, 0f) };
        header.RowDefinitions.Add(GridLength.Auto);
        header.ColumnDefinitions.Add(GridLength.Absolute(ServerColumns.StarWidth));
        for (int place = 0; place < ServerColumns.Across.Length; place++)
        {
            int column = ServerColumns.Across[place];
            float width = ServerColumns.Width(column, wide);
            header.ColumnDefinitions.Add(width > 0f ? GridLength.Absolute(width) : GridLength.Star);
            bool sorted = column == _sortBy;
            bool last = column == ServerColumns.Ping;
            var heading = new SortHeading(theme, ServerColumns.Headings[column])
            {
                Order = !sorted ? 0 : _descending ? -1 : 1,
                AlignRight = last,
                HorizontalAlignment = last ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                Tooltip = sorted ? (_descending ? "Ordered by this, highest first: press for lowest first." : "Ordered by this, lowest first: press for highest first.") : "Order the list by this.",
            };
            heading.Activated += _ =>
            {
                _descending = _sortBy == column && !_descending;
                _sortBy = column;
                Invalidate();
            };
            header.Add(heading, 0, place + 1);
        }

        return header;
    }

    // A line: a click picks it, and a second click soon after joins it.
    private ServerRow BuildRow(UiTheme theme, CoopRoomInfo room)
    {
        string code = room.Code;
        var row = new ServerRow(theme, LineOf(room))
        {
            Id = RowIdPrefix + code,
            Selected = code == _selectedCode,
            Saved = room.Favourite,
            Locked = room.Passworded,
            Wide = Size == ShellSize.Wide,
            Unjoinable = !room.Compatible || !room.Reachable,
            Reason = !room.Reachable ? "NOT RESPONDING" : !room.Compatible ? "OTHER VERSION OF THE GAME" : room.Players >= room.MaxPlayers ? "FULL" : string.Empty,
            ReasonIsFailure = !room.Reachable,
            Tooltip = !room.Reachable ? "This server is listed but isn't answering, so it can't be joined from here."
                : !room.Compatible ? "This server runs another version of the game."
                : room.Players >= room.MaxPlayers ? "This game is full."
                : room.Passworded ? "This game asks for a password. Double-click or ENTER joins it." : "Double-click or ENTER joins it.",
        };
        _rows[row] = room;
        row.Activated += _ =>
        {
            // On the compact page a press raises the line's sheet, which holds Join.
            if (Size == ShellSize.Compact)
            {
                _selectedCode = code;
                _sheet = true;
                Invalidate();
                return;
            }

            bool again = code == _clickedCode && _clock - _clickedAt <= DoubleClickSeconds;
            _clickedCode = again ? null : code;
            _clickedAt = _clock;
            if (again && Joinable(room))
            {
                Coop?.Join(code);
                return;
            }

            Select(code);
        };
        return row;
    }

    // Picks a line, which then shows on the right.
    private void Select(string code)
    {
        if (code != _selectedCode)
        {
            _selectedCode = code;
            Invalidate();
        }
    }

    // Whether a line can be joined: this build's version, within reach, and room in it.
    private static bool Joinable(CoopRoomInfo room) => room.Compatible && room.Reachable && room.Players < room.MaxPlayers;

    // Asks for the list again, and counts the wait for the next asking from now.
    private void Refresh(ICoopRooms coop)
    {
        _sinceListed = 0f;
        coop.RefreshListing();
    }

    // Under the lines, at the panel's foot: host your own.
    private Grid BuildHostCard(UiTheme theme)
    {
        var card = new Grid { ColumnSpacing = 16f };
        card.RowDefinitions.Add(GridLength.Auto);
        card.RowDefinitions.Add(GridLength.Auto);
        card.ColumnDefinitions.Add(GridLength.Star);
        card.ColumnDefinitions.Add(GridLength.Auto);
        var rule = new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f) };
        card.Add(rule, 0, 0, columnSpan: 2);
        card.Add(new Label(theme.Body, "Nothing you like? Host your own game. Your party comes with you.", 17f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24f, 16f, 0f, 16f) }, 1, 0);
        var host = new Button(theme, "HOST A GAME")
        {
            Accent = ButtonAccent.Ghost,
            Key = Context.Host.TouchScreen ? string.Empty : "Tab",
            TrailingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.ArrowRight),
            GlyphSize = 16f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(20f, 0f),
            Height = 44f,
            Margin = new Thickness(0f, 16f, 24f, 16f),
            VerticalAlignment = VerticalAlignment.Center,
        };
        host.Activated += _ => SwitchTab(1);
        card.Add(host, 1, 1);
        return card;
    }

    // What a line shows.
    private ServerLine LineOf(CoopRoomInfo room) => new(
        room.Name,
        room.Lan ? "LAN" : room.Dedicated ? "SERVER" : "ROOM",
        room.Mode,
        room.Mode.Length > 0 ? GameModes.Title(room.Mode) : "—",
        MapName(room.MissionId),
        room.Players,
        room.MaxPlayers,
        room.PingMs,
        room.Region.Length > 0 ? room.Region.ToUpperInvariant() : room.Lan || room.Dedicated ? string.Empty : room.Passworded ? "password" : "P2P");

    private string MapName(int missionId) => Context.Missions.FirstOrDefault(m => m.Id == missionId) is { } mission
        ? mission.Name
        : string.Create(CultureInfo.InvariantCulture, $"Mission {missionId}");

    // Whether a line is of the kind shown.
    private static bool KindShown(CoopRoomInfo room, int kind) => kind switch
    {
        1 => room.Dedicated,
        2 => !room.Dedicated && !room.Lan,
        3 => room.Lan,
        4 => room.Favourite,
        5 => room.Recent,
        _ => true,
    };

    // The lines the mode, the search box and HIDE FULL let through, of every kind.
    private List<CoopRoomInfo> Passing(IReadOnlyList<CoopRoomInfo>? all) =>
        all is null ? [] : [.. all.Where(r => ModeShown(r) && Searched(r) && !(_hideFull && r.Players >= r.MaxPlayers))];

    // Whether a line's name or map has what the search box holds.
    private bool Searched(CoopRoomInfo room) =>
        _search.Length == 0
        || room.Name.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase)
        || MapName(room.MissionId).Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool ModeShown(CoopRoomInfo room) =>
        _modeFilter == 0 || string.Equals(room.Mode, FilterModes[_modeFilter], StringComparison.OrdinalIgnoreCase);

    // The lines in the order chosen; ties by name. Lines not yet timed sort after the timed ones by ping.
    private List<CoopRoomInfo> Order(List<CoopRoomInfo> lines)
    {
        Func<CoopRoomInfo, IComparable> key = _sortBy switch
        {
            0 => r => GameModes.Title(r.Mode),
            2 => r => r.MissionId,
            3 => r => r.Players,
            ServerColumns.Ping => r => r.PingMs ?? int.MaxValue,
            _ => r => r.Name.ToUpperInvariant(),
        };
        IOrderedEnumerable<CoopRoomInfo> ordered = _descending ? lines.OrderByDescending(key) : lines.OrderBy(key);
        return [.. ordered.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
