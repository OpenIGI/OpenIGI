using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The server browser: every server and room heard of, in columns -- name, mode, map, players, ping, region -- with which kind
/// to show, which mode, whether full ones show, and the order, chosen over the list; a line is joined by clicking it.
/// </content>
public sealed partial class MultiplayerScreen
{
    // Which lines show: every one, or those of one kind.
    private static readonly string[] Kinds = ["ALL", "SERVERS", "ROOMS", "LAN", "SAVED", "RECENT"];

    // Which modes show: any, or one; the ids the lines carry, and how the filter names them.
    private static readonly string[] FilterModes = [string.Empty, GameModes.Coop, GameModes.TeamDeathmatch, GameModes.FreeForAll, GameModes.Elimination, GameModes.SearchAndDestroy];

    // The column the list is ordered by (0 name .. 5 region), and which way.
    private int _sortBy = 4;
    private bool _descending;

    private int _kind;
    private int _modeFilter;
    private bool _hideFull;

    private Grid BuildBrowser(UiTheme theme, ICoopRooms coop)
    {
        var layout = new Grid { RowSpacing = 10f };
        layout.ColumnDefinitions.Add(GridLength.Star);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Auto);
        layout.RowDefinitions.Add(GridLength.Star);

        IReadOnlyList<CoopRoomInfo>? all = coop.Listing;
        List<CoopRoomInfo> shown = all is null ? [] : Order([.. all.Where(Shown)]);
        layout.Add(BuildFilters(theme, coop, shown.Count, all?.Count ?? 0), 0, 0);
        layout.Add(BuildHeader(theme), 1, 0);

        var rows = new StackPanel { Orientation = Orientation.Vertical };
        var empty = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(16f, 10f, 16f, 10f) };
        if (shown.Count == 0)
        {
            rows.AddChild(empty);
        }

        if (all is null)
        {
            empty.AddChild(Body(theme, "Looking for servers…", theme.TextMuted));
        }
        else if (coop.ListingError is { } error && shown.Count == 0)
        {
            empty.AddChild(Body(theme, $"Couldn't list servers: {error}.", theme.Danger));
        }
        else if (shown.Count == 0)
        {
            empty.AddChild(Body(
                theme,
                all.Count == 0 ? "No games are open right now. Host one, and it shows here for everyone." : "No games match these filters.",
                theme.TextMuted));
        }

        foreach (CoopRoomInfo line in shown)
        {
            rows.AddChild(BuildLine(theme, coop, line));
        }

        var scroll = new ScrollView(theme);
        scroll.SetContent(rows);
        var panel = new GlassPanel(theme)
        {
            Padding = new Thickness(0f, 4f, 0f, 4f),
            Tint = Bgra32.FromRgba(14, 15, 17, 170),
            TintBottom = Bgra32.FromRgba(10, 11, 12, 170),
        };
        panel.AddChild(scroll);
        layout.Add(panel, 2, 0);
        return layout;
    }

    // Over the list: its name and how many show, which kind and mode, full ones or not, and REFRESH.
    private Grid BuildFilters(UiTheme theme, ICoopRooms coop, int shown, int heard)
    {
        var bar = new Grid { ColumnSpacing = 6f };
        bar.RowDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Auto);
        bar.ColumnDefinitions.Add(GridLength.Star);
        bar.ColumnDefinitions.Add(GridLength.Auto);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f, VerticalAlignment = VerticalAlignment.Center };
        title.AddChild(new Label(theme.Heading, "SERVER BROWSER", 20f, theme.TextBright) { VerticalAlignment = VerticalAlignment.Center });
        title.AddChild(new Label(theme.Mono, string.Create(CultureInfo.InvariantCulture, $"{shown} OF {heard}"), 12f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
        bar.Add(title, 0, 0);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4f, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Kinds.Length; i++)
        {
            int kind = i;
            var chip = new Button(theme, Kinds[i]) { Accent = i == _kind ? ButtonAccent.Solid : ButtonAccent.Text, TextSize = 13f, Tracking = 0.16f, Padding = new Thickness(10f, 6f) };
            chip.Activated += _ =>
            {
                _kind = kind;
                Invalidate();
            };
            tools.AddChild(chip);
        }

        string modeName = _modeFilter == 0 ? "ANY MODE" : GameModes.Title(FilterModes[_modeFilter]).ToUpperInvariant();
        var mode = new Button(theme, modeName) { Margin = new Thickness(14f, 0f, 0f, 0f), Accent = ButtonAccent.Ghost, TextSize = 13f, Tracking = 0.16f, Padding = new Thickness(10f, 6f), Tooltip = "Which mode the list shows: press for the next." };
        mode.Activated += _ =>
        {
            _modeFilter = (_modeFilter + 1) % FilterModes.Length;
            Invalidate();
        };
        tools.AddChild(mode);
        var full = new Button(theme, _hideFull ? "FULL HIDDEN" : "FULL SHOWN") { Accent = ButtonAccent.Ghost, TextSize = 13f, Tracking = 0.16f, Padding = new Thickness(10f, 6f), Tooltip = "Whether games with no room left show." };
        full.Activated += _ =>
        {
            _hideFull = !_hideFull;
            Invalidate();
        };
        tools.AddChild(full);
        var refresh = new Button(theme, "REFRESH") { Accent = ButtonAccent.Text, TextSize = 13f, Tracking = 0.16f, Padding = new Thickness(10f, 6f) };
        refresh.Activated += _ => coop.RefreshListing();
        tools.AddChild(refresh);
        bar.Add(tools, 0, 2);
        return bar;
    }

    // The columns' headings, each a button ordering the list by it (again, the other way).
    private Grid BuildHeader(UiTheme theme)
    {
        var header = new Grid();
        header.RowDefinitions.Add(GridLength.Auto);
        foreach (float weight in ServerColumns.Weights)
        {
            header.ColumnDefinitions.Add(GridLength.Stars(weight));
        }

        header.ColumnDefinitions.Add(GridLength.Absolute(SaveWidth));
        for (int i = 0; i < ServerColumns.Headings.Length; i++)
        {
            int column = i;
            bool sorted = i == _sortBy;
            var heading = new Button(theme, ServerColumns.Headings[i])
            {
                Accent = sorted ? ButtonAccent.Ghost : ButtonAccent.Text,
                TextSize = 12f,
                Tracking = 0.18f,
                Padding = new Thickness(sorted ? 8f : 0f, 4f, sorted ? 8f : 0f, 4f),
                Margin = new Thickness(i == 0 ? 14f - (sorted ? 8f : 0f) : sorted ? -8f : 0f, 0f, 0f, 0f),
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

    private const float SaveWidth = 76f;

    // A line: the row, joined by clicking it, and SAVE beside it.
    private Grid BuildLine(UiTheme theme, ICoopRooms coop, CoopRoomInfo room)
    {
        string map = Context.Missions.FirstOrDefault(m => m.Id == room.MissionId) is { } mission
            ? mission.Name
            : string.Create(CultureInfo.InvariantCulture, $"Mission {room.MissionId}");
        bool full = room.Players >= room.MaxPlayers;
        string kind = room.Lan ? "LAN" : room.Dedicated ? "SERVER" : "ROOM";
        string tag = room.Passworded ? $"{kind} · LOCKED" : kind;
        string[] cells =
        [
            room.Name,
            room.Mode.Length > 0 ? GameModes.Title(room.Mode) : "—",
            map,
            string.Create(CultureInfo.InvariantCulture, $"{room.Players}/{room.MaxPlayers}"),
            room.PingMs is { } ms ? ms.ToString(CultureInfo.InvariantCulture) : "—",
            room.Region.Length > 0 ? room.Region.ToUpperInvariant() : "—",
        ];
        var row = new ServerRow(theme, cells, tag, room.PingMs, full)
        {
            IsEnabled = !full && room.Compatible,
            Highlighted = room.Recent,
            Tooltip = !room.Compatible ? "This server runs another version of the game."
                : full ? "This game is full."
                : room.Passworded ? "This game asks for a password." : "Join this game.",
        };
        string code = room.Code;
        row.Activated += _ => coop.Join(code);

        var line = new Grid();
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Absolute(SaveWidth));
        line.Add(row, 0, 0);
        bool favourite = room.Favourite;
        var keep = new Button(theme, favourite ? "SAVED" : "SAVE")
        {
            Accent = favourite ? ButtonAccent.Solid : ButtonAccent.Text,
            TextSize = 12f,
            Tracking = 0.16f,
            Padding = new Thickness(8f, 5f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = favourite ? "Take it off your saved servers." : "Keep it in your saved servers, asked after even when it isn't listed.",
        };
        keep.Activated += _ =>
        {
            coop.SetFavourite(code, !favourite);
            Invalidate();
        };
        line.Add(keep, 0, 1);
        return line;
    }

    // Whether the filters show a line.
    private bool Shown(CoopRoomInfo room)
    {
        bool kind = _kind switch
        {
            1 => room.Dedicated,
            2 => !room.Dedicated && !room.Lan,
            3 => room.Lan,
            4 => room.Favourite,
            5 => room.Recent,
            _ => true,
        };
        bool mode = _modeFilter == 0 || string.Equals(room.Mode, FilterModes[_modeFilter], StringComparison.OrdinalIgnoreCase);
        return kind && mode && !(_hideFull && room.Players >= room.MaxPlayers);
    }

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
