using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.Net.Browsing;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Kit.Servers;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <content>
/// Browsing: the list's panel -- the kinds with how many of each, how many show and REFRESH; the search, the mode and HIDE
/// FULL; the columns' headings; the lines -- beside the line picked. The filters stay put while the lines are rebuilt
/// under them, so the search keeps the keyboard as it is typed into.
/// </content>
internal sealed partial class ServersPage
{
    private static readonly string[] Kinds = ["All", "Servers", "Rooms", "LAN", "Saved", "Recent"];
    private static readonly string[] FilterModes = [string.Empty, MatchModes.Coop, MatchModes.TeamDeathmatch, MatchModes.FreeForAll, MatchModes.Elimination, MatchModes.SearchAndDestroy];
    private const int SkeletonRows = 7;
    private const long DoubleClickMs = 400;

    private readonly StackPanel _rows = new() { Orientation = Orientation.Vertical };
    private readonly Dictionary<string, ServerRow> _rowOf = new(StringComparer.Ordinal);
    private readonly Panel _columns = new();
    private readonly Panel _side = new();
    private TabStrip? _kinds;
    private Label? _count;
    private Button? _modeButton;
    private BrowserFilter _filter = new() { HideIncompatible = false };
    private int _kind;
    private int _mode;
    private int _sortBy = ServerColumns.Ping;
    private bool _descending;
    private string? _selected;
    private string? _clicked;
    private long _clickedAt;
    private string? _note;

    // What the list and the headings were last built for: the rows are made anew only when the list's makeup changes (which
    // servers, in what order, which picked); players and pings coming in update the rows already there.
    private string? _builtList;
    private (int SortBy, bool Descending)? _builtHeader;

    private Grid BuildBrowse()
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.Add(BuildFilters(), 0, 0);
        body.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f) }, 1, 0);
        body.Add(_columns, 2, 0);
        body.Add(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f) }, 3, 0);
        var scroll = new ScrollView(_theme);
        scroll.SetContent(_rows);
        body.Add(scroll, 4, 0);

        var layout = new Grid { ColumnSpacing = PanelGap };
        layout.RowDefinitions.Add(GridLength.Star);
        // The list and the side share the width, the side about a third: some 340 units in the smallest window, as wide
        // as the game's own side on a large one. (The launcher draws at one scale, where the game's page scales instead.)
        layout.ColumnDefinitions.Add(GridLength.Stars(2.4f));
        layout.ColumnDefinitions.Add(GridLength.Stars(1f));
        layout.Add(GlassPanel.Card(_theme, body), 0, 0);
        layout.Add(_side, 0, 1);
        return layout;
    }

    // Two lines over the list: the kinds with their counts, how many show and REFRESH; then the search, the mode and HIDE FULL.
    private StackPanel BuildFilters()
    {
        _kinds = new TabStrip(_theme, [.. Kinds.Select(static k => k.ToUpperInvariant())])
        {
            SegmentHeight = 36f,
            TabPadding = 16f,
            TextSize = ButtonText,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _kinds.SelectionChanged += index =>
        {
            _kind = index;
            RefreshBrowse();
        };
        _count = new Label(_theme.Mono, string.Empty, 14f, _theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center };
        var refresh = new Button(_theme, string.Empty)
        {
            Accent = ButtonAccent.Ghost,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Refresh),
            GlyphSize = 18f,
            Padding = new Thickness(12f, 0f),
            Height = 44f,
            VerticalAlignment = VerticalAlignment.Center,
            Tooltip = "Ask for the list again. It is asked for by itself every few seconds.",
        };
        refresh.Activated += _ => _model.RefreshServers();

        var first = new Grid { ColumnSpacing = 12f };
        first.RowDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Star);
        first.ColumnDefinitions.Add(GridLength.Auto);
        first.ColumnDefinitions.Add(GridLength.Auto);
        first.Add(_kinds, 0, 0);
        first.Add(_count, 0, 2);
        first.Add(refresh, 0, 3);

        var search = new TextBox(_theme) { Placeholder = "Search name or map", MaxLength = 32, Width = 260f, Height = 44f, LeadingGlyph = KitIcons.Glyph(KitIcon.Search), VerticalAlignment = VerticalAlignment.Center, Tooltip = "Show only the games whose name or map has this in it." };
        search.TextChanged += text =>
        {
            _filter = _filter with { Text = text.Trim() };
            RefreshBrowse();
        };
        _modeButton = new Button(_theme, "Mode: any")
        {
            Accent = ButtonAccent.Ghost,
            TrailingGlyph = static (painter, centre, size, ink) => painter.DrawChevron(centre, size, OpenIGI.UI.Drawing.UiDirection.Down, MathF.Max(1f, size * 0.16f), ink),
            GlyphSize = 12f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(20f, 0f),
            Height = 44f,
            Tooltip = "Which mode the list shows: press for the next.",
        };
        _modeButton.Activated += _ =>
        {
            _mode = (_mode + 1) % FilterModes.Length;
            _filter = _filter with { Mode = FilterModes[_mode] };
            _modeButton.Text = _mode == 0 ? "Mode: any" : $"Mode: {MatchModes.Title(FilterModes[_mode])}";
            RefreshBrowse();
        };
        var full = new CheckChip(_theme, "Hide full") { Tooltip = "Whether games with no room left show." };
        full.Activated += _ =>
        {
            full.Checked = !full.Checked;
            _filter = _filter with { HideFull = full.Checked };
            RefreshBrowse();
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12f, VerticalAlignment = VerticalAlignment.Center };
        tools.AddChild(search);
        tools.AddChild(_modeButton);
        tools.AddChild(full);

        var block = new StackPanel { Orientation = Orientation.Vertical, Spacing = 16f, Margin = new Thickness(16f) };
        block.AddChild(first);
        block.AddChild(tools);
        return block;
    }

    // The list again: what passes the filters, of the kind picked, in the order picked; the counts, the headings and the side.
    private void RefreshBrowse()
    {
        List<BrowserServer> passing = [.. _filter.Apply(_model.Servers)];
        List<BrowserServer> shown = ServerColumns.Order(passing.Where(s => KindShown(s, _kind)), LineOf, _sortBy, _descending);
        BrowserServer? picked = shown.FirstOrDefault(s => s.Key == _selected) ?? shown.FirstOrDefault();
        _selected = picked?.Key;

        if (_kinds is not null)
        {
            _kinds.SelectedIndex = _kind;
            _kinds.Notes = [.. Enumerable.Range(0, Kinds.Length).Select(kind => passing.Count(s => KindShown(s, kind)).ToString(CultureInfo.InvariantCulture))];
        }

        int heard = _model.Servers.Count();
        if (_count is not null)
        {
            _count.Text = string.Create(CultureInfo.InvariantCulture, $"{shown.Count} of {heard}");
        }

        if (_builtHeader != (_sortBy, _descending))
        {
            _builtHeader = (_sortBy, _descending);
            _columns.ClearChildren();
            Grid header = ServerColumns.Header(_theme, _sortBy, _descending, wide: false, (column, descending) =>
            {
                _sortBy = column;
                _descending = descending;
                RefreshBrowse();
            });
            _columns.AddChild(header);
            Screen.PlayEntrance(header);
        }

        bool waiting = heard == 0 && _model.ListingServers && _model.ServerListError is null;
        string makeup = string.Join('|', [waiting ? "waiting" : heard.ToString(CultureInfo.InvariantCulture), _model.ServerListError ?? string.Empty, _selected ?? string.Empty, .. shown.Select(static s => s.Key)]);
        RefreshSide(picked, waiting);
        if (makeup == _builtList)
        {
            foreach (BrowserServer server in shown)
            {
                if (_rowOf.TryGetValue(server.Key, out ServerRow? kept))
                {
                    Describe(kept, server);
                }
            }

            return;
        }

        _builtList = makeup;
        _rows.ClearChildren();
        _rowOf.Clear();
        if (waiting)
        {
            // Not heard yet: the list's shape in place of its lines, so the panel is not a blank while it waits.
            for (int i = 0; i < SkeletonRows; i++)
            {
                _rows.AddChild(new ServerRowSkeleton(i, SkeletonRows));
            }
        }
        else if (shown.Count == 0)
        {
            bool failed = _model.ServerListError is not null;
            string heading = failed ? "No answer" : heard == 0 ? "No games open" : "Nothing matches";
            string text = failed ? $"Couldn't reach the server list: {_model.ServerListError}."
                : heard == 0 ? "No games are open right now. Host one from the game's Multiplayer page, and it shows here for everyone."
                : "No games match these filters.";
            var nothing = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24f, 72f, 24f, 56f) };
            nothing.AddChild(new Label(_theme.Display, heading.ToUpperInvariant(), 34f, failed ? _theme.Danger : _theme.Text) { HorizontalAlignment = HorizontalAlignment.Center });
            nothing.AddChild(new Label(_theme.Body, text, 17f, _theme.TextSecondary) { HorizontalAlignment = HorizontalAlignment.Center });
            _rows.AddChild(nothing);
        }

        foreach (BrowserServer server in shown)
        {
            ServerRow row = Row(server);
            _rowOf[server.Key] = row;
            _rows.AddChild(row);
        }

        Screen.PlayEntrance(_rows);
    }

    // A line: a click picks it, and a second click soon after joins it.
    private ServerRow Row(BrowserServer server)
    {
        var row = new ServerRow(_theme, LineOf(server)) { Selected = server.Key == _selected, Wide = false };
        Describe(row, server);
        string key = server.Key;
        row.Activated += _ =>
        {
            long now = Environment.TickCount64;
            bool again = key == _clicked && now - _clickedAt <= DoubleClickMs;
            _clicked = again ? null : key;
            _clickedAt = now;
            _selected = key;
            if (again && Joinable(server))
            {
                Join(server);
                return;
            }

            RefreshBrowse();
        };
        return row;
    }

    // A row's look from its server, made or kept: the line, the marks, and why it can't be joined.
    private void Describe(ServerRow row, BrowserServer server)
    {
        BrowserInfo info = server.Info;
        bool reachable = server.Answered || server.Room is not null;
        row.Line = LineOf(server);
        row.Saved = server.Sources.HasFlag(BrowserSource.Favourite);
        row.Locked = info.Passworded;
        row.Unjoinable = !info.Compatible || !reachable;
        row.Reason = !reachable ? "NOT RESPONDING" : !info.Compatible ? "OTHER VERSION OF THE GAME" : server.IsFull ? "FULL" : string.Empty;
        row.ReasonIsFailure = !reachable;
        row.Tooltip = !reachable ? "This server is listed but isn't answering, so it can't be joined from here."
            : !info.Compatible ? "This server runs another version of the game."
            : server.IsFull ? "This game is full."
            : "Double-click joins it.";
    }

    // What a server shows as a line: its kind as the line under its name says it, its mode, its map, how full and how near.
    private ServerLine LineOf(BrowserServer server)
    {
        BrowserInfo info = server.Info;
        string kind = server.Sources.HasFlag(BrowserSource.Lan) ? "LAN" : server.Address is null ? "ROOM" : "SERVER";
        int? ping = server.Ping is { } seconds ? (int)Math.Round(seconds * 1000) : null;
        return new ServerLine(
            info.Name,
            kind,
            info.Mode,
            info.Mode.Length > 0 ? MatchModes.Title(info.Mode) : "—",
            MapName(server),
            info.Players,
            info.MaxPlayers,
            ping,
            server.Region.ToUpperInvariant(),
            MapNumber(server));
    }

    // Whether a server is of the kind shown.
    private static bool KindShown(BrowserServer server, int kind) => kind switch
    {
        1 => server.Address is not null && server.Sources.HasFlag(BrowserSource.Internet),
        2 => server.Address is null,
        3 => server.Sources.HasFlag(BrowserSource.Lan),
        4 => server.Sources.HasFlag(BrowserSource.Favourite),
        5 => server.Sources.HasFlag(BrowserSource.History),
        _ => true,
    };

    private static bool Joinable(BrowserServer server) =>
        server.Info.Compatible && !server.IsFull && (server.Answered || server.Room is not null);
}
