using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// Browsing: every server and room heard of, in one panel -- which kind to show (each with how many there are), which mode,
/// whether full ones show, how many show, REFRESH; the columns, each heading ordering the list by it; the lines. A line is
/// picked by clicking it and shows on the right; ENTER on a line, or JOIN there, joins it.
/// </content>
public sealed partial class MultiplayerScreen
{
    // Which lines show: every one, or those of one kind.
    private static readonly string[] Kinds = ["ALL", "SERVERS", "ROOMS", "LAN", "SAVED", "RECENT"];

    // Which modes show: any, or one; the ids the lines carry.
    private static readonly string[] FilterModes = [string.Empty, GameModes.Coop, GameModes.TeamDeathmatch, GameModes.FreeForAll, GameModes.Elimination, GameModes.SearchAndDestroy];

    // Each line's room code, by its row, for ENTER on a focused line.
    private readonly Dictionary<ServerRow, string> _rowCodes = [];

    // The column the list is ordered by (0 name .. 5 region), and which way.
    private int _sortBy = 4;
    private bool _descending;

    private int _kind;
    private int _modeFilter;
    private bool _hideFull;

    // The line picked, by its code.
    private string? _selectedCode;

    private Grid BuildBrowseTab(UiTheme theme, ICoopRooms coop)
    {
        IReadOnlyList<CoopRoomInfo>? all = coop.Listing;
        List<CoopRoomInfo> passing = all is null ? [] : [.. all.Where(r => ModeShown(r) && !(_hideFull && r.Players >= r.MaxPlayers))];
        List<CoopRoomInfo> shown = Order([.. passing.Where(r => KindShown(r, _kind))]);
        CoopRoomInfo? picked = shown.FirstOrDefault(r => r.Code == _selectedCode) ?? shown.FirstOrDefault();
        _selectedCode = picked?.Code;

        var layout = new Grid { ColumnSpacing = PanelGap };
        layout.RowDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.ColumnDefinitions.Add(GridLength.Absolute(SideWidth));
        layout.Add(BuildList(theme, coop, all, passing, shown), 0, 0);
        layout.Add(BuildDetail(theme, coop, picked), 0, 1);
        return layout;
    }

    private GlassPanel BuildList(UiTheme theme, ICoopRooms coop, IReadOnlyList<CoopRoomInfo>? all, List<CoopRoomInfo> passing, List<CoopRoomInfo> shown)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.Add(BuildFilters(theme, coop, passing, shown.Count, all?.Count ?? 0), 0, 0);
        body.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f) }, 1, 0);
        body.Add(BuildColumns(theme), 2, 0);

        _rowCodes.Clear();
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        string? empty = all is null ? "Looking for servers…"
            : coop.ListingError is { } error && shown.Count == 0 ? $"Couldn't list servers: {error}."
            : shown.Count == 0 ? all.Count == 0 ? "No games are open right now. Host one, and it shows here for everyone." : "No games match these filters."
            : null;
        if (empty is not null)
        {
            rows.AddChild(new Label(theme.Body, empty, 16f, coop.ListingError is not null && all is not null ? theme.Danger : theme.TextSecondary)
            {
                Wrap = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(24f, 60f, 24f, 40f),
            });
        }

        foreach (CoopRoomInfo room in shown)
        {
            rows.AddChild(BuildRow(theme, room));
        }

        rows.AddChild(BuildHostCard(theme));
        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        body.Add(scroll, 3, 0);
        return Panel(theme, body);
    }

    // Over the list: the kinds with their counts, the mode, HIDE FULL; how many show, and REFRESH.
    private Grid BuildFilters(UiTheme theme, ICoopRooms coop, List<CoopRoomInfo> passing, int shown, int heard)
    {
        var bar = new Grid { ColumnSpacing = 20f, Margin = new Thickness(24f, 18f, 24f, 18f) };
        bar.RowDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Star);
        bar.ColumnDefinitions.Add(GridLength.Auto);

        var kinds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2f, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Kinds.Length; i++)
        {
            int kind = i;
            int count = passing.Count(r => KindShown(r, kind));
            var chip = new Button(theme, string.Create(CultureInfo.InvariantCulture, $"{Kinds[i]}  {count}"))
            {
                Accent = i == _kind ? ButtonAccent.Solid : ButtonAccent.Text,
                TextSize = 14f,
                Tracking = 0.22f,
                Padding = new Thickness(14f, 8f),
            };
            chip.Activated += _ =>
            {
                _kind = kind;
                Invalidate();
            };
            kinds.AddChild(chip);
        }

        bar.Add(kinds, 0, 0);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10f, VerticalAlignment = VerticalAlignment.Center };
        tools.AddChild(new Rule { Orientation = Orientation.Vertical, Length = 24f, Color = UiPalette.White(0.1f), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0f, 0f, 10f, 0f) });
        string modeName = _modeFilter == 0 ? "ANY" : GameModes.Title(FilterModes[_modeFilter]).ToUpperInvariant();
        var mode = new Button(theme, $"MODE  {modeName}  ›") { Accent = ButtonAccent.Ghost, TextSize = 14f, Tracking = 0.2f, Padding = new Thickness(12f, 8f), Tooltip = "Which mode the list shows: press for the next." };
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
        bar.Add(tools, 0, 1);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14f, VerticalAlignment = VerticalAlignment.Center };
        right.AddChild(new Label(theme.Mono, string.Create(CultureInfo.InvariantCulture, $"{shown} OF {heard}"), 12f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
        var refresh = new Button(theme, "REFRESH") { Accent = ButtonAccent.Ghost, TextSize = 14f, Tracking = 0.2f, Padding = new Thickness(12f, 8f) };
        refresh.Activated += _ => coop.RefreshListing();
        right.AddChild(refresh);
        bar.Add(right, 0, 3);
        return bar;
    }

    // The columns' headings, each a button ordering the list by it (again, the other way); the column ordered by is outlined.
    private Grid BuildColumns(UiTheme theme)
    {
        var header = new Grid { Margin = new Thickness(ServerColumns.Inset, 12f, ServerColumns.Inset, 6f) };
        header.RowDefinitions.Add(GridLength.Auto);
        foreach (float weight in ServerColumns.Weights)
        {
            header.ColumnDefinitions.Add(GridLength.Stars(weight));
        }

        for (int i = 0; i < ServerColumns.Headings.Length; i++)
        {
            int column = i;
            bool sorted = i == _sortBy;
            var heading = new Button(theme, ServerColumns.Headings[i])
            {
                Accent = sorted ? ButtonAccent.Ghost : ButtonAccent.Text,
                Font = theme.Mono,
                TextSize = 11f,
                Tracking = 0.3f,
                Padding = new Thickness(sorted ? 8f : 0f, 4f, sorted ? 8f : 0f, 4f),
                Margin = new Thickness(sorted ? -8f : 0f, 0f, 0f, 0f),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tooltip = sorted ? (_descending ? "Ordered by this, highest first: press for lowest first." : "Ordered by this, lowest first: press for highest first.") : "Order the list by this.",
            };
            heading.Activated += _ =>
            {
                _descending = _sortBy == column && !_descending;
                _sortBy = column;
                Invalidate();
            };
            header.Add(heading, 0, i);
        }

        return header;
    }

    // A line, picked by clicking it.
    private ServerRow BuildRow(UiTheme theme, CoopRoomInfo room)
    {
        var row = new ServerRow(theme, LineOf(room))
        {
            Selected = room.Code == _selectedCode,
            Saved = room.Favourite,
            Unjoinable = !room.Compatible,
            Tooltip = !room.Compatible ? "This server runs another version of the game."
                : room.Players >= room.MaxPlayers ? "This game is full."
                : room.Passworded ? "This game asks for a password." : "Pick it; ENTER or JOIN joins it.",
        };
        string code = room.Code;
        _rowCodes[row] = code;
        row.Activated += _ =>
        {
            _selectedCode = code;
            Invalidate();
        };
        return row;
    }

    // Under the lines: host your own.
    private GlassPanel BuildHostCard(UiTheme theme)
    {
        var card = new Grid { Margin = new Thickness(24f) };
        card.RowDefinitions.Add(GridLength.Auto);
        card.ColumnDefinitions.Add(GridLength.Star);
        card.ColumnDefinitions.Add(GridLength.Auto);
        var words = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f, Margin = new Thickness(24f, 22f, 0f, 22f), VerticalAlignment = VerticalAlignment.Center };
        words.AddChild(new Label(theme.Heading, "NOTHING YOU LIKE?", 20f, theme.Text));
        words.AddChild(new Label(theme.Body, "Host your own co-op or team game. Your party comes with you.", 15f, theme.TextSecondary));
        card.Add(words, 0, 0);
        var host = new Button(theme, "HOST A GAME  ›") { Accent = ButtonAccent.Outline, TextSize = 16f, Tracking = 0.3f, Padding = new Thickness(22f, 14f), Margin = new Thickness(0f, 0f, 24f, 0f), VerticalAlignment = VerticalAlignment.Center };
        host.Activated += _ =>
        {
            _tab = 1;
            RefreshHints();
            Invalidate();
        };
        card.Add(host, 0, 1);
        var panel = new GlassPanel(theme) { Tint = Bgra32.FromRgba(0, 0, 0, 0), Border = UiPalette.White(0.12f), Blur = 0f };
        panel.AddChild(card);
        return panel;
    }

    // What a line shows.
    private ServerLine LineOf(CoopRoomInfo room) => new(
        room.Name,
        room.Lan ? "LAN" : room.Dedicated ? "SERVER" : "ROOM",
        room.Mode.Length > 0 ? GameModes.Title(room.Mode) : "—",
        MapName(room.MissionId),
        room.Players,
        room.MaxPlayers,
        room.PingMs,
        room.Region.Length > 0 ? room.Region.ToUpperInvariant() : room.Lan ? "LAN" : room.Dedicated ? "—" : "P2P");

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

    private bool ModeShown(CoopRoomInfo room) =>
        _modeFilter == 0 || string.Equals(room.Mode, FilterModes[_modeFilter], StringComparison.OrdinalIgnoreCase);

    // The lines in the order chosen; ties by name. Lines not yet timed sort after the timed ones by ping.
    private List<CoopRoomInfo> Order(List<CoopRoomInfo> lines)
    {
        Func<CoopRoomInfo, IComparable> key = _sortBy switch
        {
            1 => r => GameModes.Title(r.Mode),
            2 => r => r.MissionId,
            3 => r => r.Players,
            4 => r => r.PingMs ?? int.MaxValue,
            5 => r => r.Region,
            _ => r => r.Name.ToUpperInvariant(),
        };
        IOrderedEnumerable<CoopRoomInfo> ordered = _descending ? lines.OrderByDescending(key) : lines.OrderBy(key);
        return [.. ordered.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
