using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Titles;
using OpenIGI.Net.Browsing;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <summary>
/// The launcher's Servers section: the chosen game's server browser -- searched, filtered and sorted, a line a server with its
/// players and ping -- joined by starting the game at it; and a dedicated server of this build hosted from a form beside it.
/// </summary>
/// <remarks>The list is the same one the game's menu shows: the same browser, the same saved and recent servers.</remarks>
internal sealed class ServersPage
{
    private static readonly string[] Kinds = ["All", "Dedicated servers", "Rooms", "This network", "Saved", "Recent"];
    private static readonly string[] Modes = ["Any mode", "coop", "tdm", "ffa", "elim", "sd"];
    private static readonly string[] Sorts = ["Ping", "Players", "Name", "Map"];
    private static readonly string[] HostModes = ["tdm", "ffa", "elim", "sd", "coop"];
    private static readonly string[] HostModeNames = ["Team deathmatch", "Free for all", "Elimination", "Search & destroy", "Co-op"];
    private static readonly int[] HostSizes = [8, 16, 24, 32];

    private readonly UiTheme _theme;
    private readonly LauncherModel _model;
    private readonly StackPanel _rows = new() { Orientation = Orientation.Vertical, Spacing = 8f };
    private readonly Label _summary;
    private readonly StackPanel _host = new() { Orientation = Orientation.Vertical, Spacing = 0f };
    private BrowserFilter _filter = new() { HideIncompatible = false };
    private int _kind;
    private string _password = string.Empty;
    private int _shownRevision = -1;
    private string? _shownHosted;
    private bool _shownCanHost;
    private bool _entered;

    // The host form's choices, kept across rebuilds.
    private string _hostName = "My OpenIGI server";
    private int _hostMission = 1;
    private int _hostMode;
    private int _hostSize = 1;
    private string _hostPassword = string.Empty;
    private bool _hostPublic;

    public ServersPage(UiTheme theme, LauncherModel model)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _summary = new Label(theme.Body, string.Empty, 15f, theme.TextMuted) { Wrap = true };

        var search = new TextBox(theme) { Placeholder = "Search name, map or mode", MaxLength = 64, Height = 44f };
        search.TextChanged += text => Filter(_filter with { Text = text.Trim() });
        var kind = new Dropdown(theme, "Show", Kinds) { Width = 230f };
        kind.SelectionChanged += index =>
        {
            _kind = index;
            Filter(_filter);
        };
        var mode = new Dropdown(theme, "Mode", Modes) { Width = 170f };
        mode.SelectionChanged += index => Filter(_filter with { Mode = index == 0 ? string.Empty : Modes[index] });
        var sort = new Dropdown(theme, "Sort", Sorts) { Width = 150f };
        sort.SelectionChanged += index => Filter(_filter with { Sort = (BrowserSort)index });

        var hideFull = new Toggle(theme, "Hide full");
        hideFull.ValueChanged += on => Filter(_filter with { HideFull = on });
        var hideEmpty = new Toggle(theme, "Hide empty");
        hideEmpty.ValueChanged += on => Filter(_filter with { HideEmpty = on });
        var hideLocked = new Toggle(theme, "Hide passworded");
        hideLocked.ValueChanged += on => Filter(_filter with { HidePassworded = on });

        Button refresh = new(theme, "Refresh") { Accent = ButtonAccent.Ghost };
        refresh.Activated += _ => model.RefreshServers();
        Button quick = new(theme, "Quick play") { Accent = ButtonAccent.Solid, Tooltip = "Join the best open game listed: near, with people in it, room for you." };
        quick.Activated += _ => QuickPlay();

        var tools = new FlexPanel { Gap = 10f };
        tools.Add(search, grow: 1f);
        tools.Add(kind);
        tools.Add(mode);
        tools.Add(sort);
        var toggles = new FlexPanel { Gap = 18f };
        toggles.Add(hideFull);
        toggles.Add(hideEmpty);
        toggles.Add(hideLocked);
        var password = new TextBox(theme) { Placeholder = "Password, for a server that asks one", MaxLength = 64, Height = 40f, Width = 320f };
        password.TextChanged += text => _password = text;
        toggles.Add(password);
        toggles.Add(refresh);
        toggles.Add(quick);

        var scroll = new ScrollView(theme);
        scroll.SetContent(_rows);

        var list = new Grid { RowSpacing = 12f };
        list.ColumnDefinitions.Add(GridLength.Star);
        list.RowDefinitions.Add(GridLength.Auto);
        list.RowDefinitions.Add(GridLength.Auto);
        list.RowDefinitions.Add(GridLength.Auto);
        list.RowDefinitions.Add(GridLength.Star);
        list.Add(tools, 0, 0);
        list.Add(toggles, 1, 0);
        list.Add(_summary, 2, 0);
        list.Add(scroll, 3, 0);

        var hostScroll = new ScrollView(theme);
        hostScroll.SetContent(_host);

        var body = new Grid { ColumnSpacing = 24f };
        body.RowDefinitions.Add(GridLength.Star);
        body.ColumnDefinitions.Add(GridLength.Star);
        body.ColumnDefinitions.Add(GridLength.Absolute(420f));
        body.Add(list, 0, 0);
        body.Add(hostScroll, 0, 1);

        var page = new Grid { RowSpacing = 24f, Padding = new Thickness(48f, 36f, 48f, 28f) };
        page.ColumnDefinitions.Add(GridLength.Star);
        page.RowDefinitions.Add(GridLength.Auto);
        page.RowDefinitions.Add(GridLength.Star);
        page.Add(new SectionTitle(theme, "Multiplayer", "Servers") { HorizontalAlignment = HorizontalAlignment.Left }, 0, 0);
        page.Add(body, 1, 0);
        Root = page;
    }

    /// <summary>Gets the page.</summary>
    public UiElement Root { get; }

    /// <summary>Takes in the browser's news and the hosted server's; asks after the servers the first time the page is shown.</summary>
    /// <param name="shown">Whether the page is the one up.</param>
    public void Update(bool shown)
    {
        if (shown && !_entered)
        {
            _entered = true;
            _model.RefreshServers();
        }

        if (_model.ServersRevision != _shownRevision)
        {
            _shownRevision = _model.ServersRevision;
            BuildRows();
        }

        if (_model.HostedServerStatus != _shownHosted || _model.CanHostServer != _shownCanHost || _host.Children.Count == 0)
        {
            _shownHosted = _model.HostedServerStatus;
            _shownCanHost = _model.CanHostServer;
            BuildHost();
        }
    }

    private void Filter(BrowserFilter filter)
    {
        _filter = filter;
        BuildRows();
    }

    private IReadOnlyList<BrowserServer> Shown() =>
        _filter.Apply(_model.Servers.Where(s => _kind switch
        {
            1 => s.Address is not null && s.Sources.HasFlag(BrowserSource.Internet),
            2 => s.Address is null,
            3 => s.Sources.HasFlag(BrowserSource.Lan),
            4 => s.Sources.HasFlag(BrowserSource.Favourite),
            5 => s.Sources.HasFlag(BrowserSource.History),
            _ => true,
        }));

    private void QuickPlay()
    {
        if (BrowserFilter.QuickPlay(_model.Servers, _filter.Mode) is { } best)
        {
            _model.JoinServer(best);
        }
        else
        {
            _summary.Text = "No open game to join right now. Refresh, or host one.";
        }
    }

    // A line a server: its name and what it plays, how full and how near, and the ways to join and save it.
    private void BuildRows()
    {
        _rows.ClearChildren();
        IReadOnlyList<BrowserServer> shown = Shown();
        int players = shown.Sum(static s => s.Info.Players);
        _summary.Text = _model.ServerListError is { } error && shown.Count == 0
            ? $"Couldn't reach the server list: {error}."
            : _model.ListingServers && shown.Count == 0
                ? "Looking for servers…"
                : string.Create(CultureInfo.InvariantCulture, $"{shown.Count} {(shown.Count == 1 ? "server" : "servers")}, {players} {(players == 1 ? "player" : "players")}.");

        foreach (BrowserServer server in shown)
        {
            _rows.AddChild(Row(server));
        }
    }

    private GlassPanel Row(BrowserServer server)
    {
        BrowserInfo info = server.Info;
        string kind = server.Sources.HasFlag(BrowserSource.Lan) ? "LAN" : server.Address is null ? "ROOM" : "SERVER";
        string where = server.Region.Length > 0 ? $"  ·  {server.Region.ToUpperInvariant()}" : string.Empty;
        string what = string.Join("  ·  ", new[] { info.Mode.ToUpperInvariant(), info.Map.Length > 0 ? $"MISSION {info.Map}" : string.Empty, kind }.Where(static p => p.Length > 0));
        string locked = info.Passworded ? "  ·  PASSWORD" : string.Empty;

        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2f, VerticalAlignment = VerticalAlignment.Center };
        text.AddChild(new Label(_theme.Heading, info.Name, 18f, _theme.TextBright));
        text.AddChild(new Label(_theme.Mono, $"{what}{where}{locked}  ·  {info.Version}", 12f, _theme.TextMuted));

        string ping = server.Ping is { } seconds ? string.Create(CultureInfo.InvariantCulture, $"{seconds * 1000:0} ms") : "…";
        string count = info.MaxPlayers > 0 ? string.Create(CultureInfo.InvariantCulture, $"{info.Players}/{info.MaxPlayers}") : "?";
        bool favourite = server.Sources.HasFlag(BrowserSource.Favourite);
        Button join = new(_theme, "Join") { Accent = ButtonAccent.Solid, IsEnabled = !server.IsFull, VerticalAlignment = VerticalAlignment.Center };
        join.Activated += _ => _model.JoinServer(server, _password);
        Button save = new(_theme, favourite ? "Saved" : "Save") { Accent = ButtonAccent.Ghost, VerticalAlignment = VerticalAlignment.Center };
        save.Activated += _ => _model.SetFavouriteServer(server, !favourite);

        var line = new Grid { ColumnSpacing = 16f };
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Absolute(70f));
        line.ColumnDefinitions.Add(GridLength.Absolute(70f));
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Auto);
        line.Add(text, 0, 0);
        line.Add(new Label(_theme.Mono, count, 15f, _theme.TextBright) { VerticalAlignment = VerticalAlignment.Center }, 0, 1);
        line.Add(new Label(_theme.Mono, ping, 15f, _theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        line.Add(save, 0, 3);
        line.Add(join, 0, 4);

        var panel = new GlassPanel(_theme) { Tint = UiPalette.Ink.WithAlpha(170), Padding = new Thickness(20f, 12f) };
        panel.AddChild(line);
        return panel;
    }

    // The host form: what a dedicated server of this build plays and how it is listed; or, while one runs, what it does.
    private void BuildHost()
    {
        _host.ClearChildren();
        _host.AddChild(new GroupHeader(_theme, "Host a dedicated server") { Margin = new Thickness(0f, 0f, 0f, 8f) });
        if (!_model.CanHostServer)
        {
            _host.AddChild(new Label(_theme.Body, "This build has no dedicated server beside the game. Install a build that ships one, or run OpenIGI.Server by hand (see its server.cfg.example).", 15f, _theme.TextMuted) { Wrap = true });
            return;
        }

        if (_model.HostedServerStatus is { } status)
        {
            _host.AddChild(new Label(_theme.Body, status, 15f, UiPalette.TextBody) { Wrap = true, Margin = new Thickness(0f, 0f, 0f, 12f) });
            var stop = new ActionRow(_theme, "Your server", "Stop") { Hint = "Tells it to quit, so it leaves the server list at once" };
            stop.Activated += _ => _model.StopHostedServer();
            _host.AddChild(stop);
            return;
        }

        var name = new TextRow(_theme, "Name", _hostName, "My OpenIGI server") { FieldWidth = 220f };
        name.TextChanged += text => _hostName = text;
        int missions = _model.Current.Retail?.Id == "igi2" ? 19 : 14;
        var mission = new Dropdown(_theme, "Mission", Enumerable.Range(1, missions).Select(static m => string.Create(CultureInfo.InvariantCulture, $"Mission {m}")), Math.Clamp(_hostMission - 1, 0, missions - 1));
        mission.SelectionChanged += index => _hostMission = index + 1;
        var mode = new Dropdown(_theme, "Mode", HostModeNames, _hostMode);
        mode.SelectionChanged += index => _hostMode = index;
        var size = new Dropdown(_theme, "Players", HostSizes.Select(static n => n.ToString(CultureInfo.InvariantCulture)), _hostSize);
        size.SelectionChanged += index => _hostSize = index;
        var password = new TextRow(_theme, "Password", _hostPassword, "none") { FieldWidth = 220f };
        password.TextChanged += text => _hostPassword = text;
        var listed = new Toggle(_theme, "List in the server browser", _hostPublic) { Hint = "Off: only those you give the address (or the room's code) to find it" };
        listed.ValueChanged += on => _hostPublic = on;
        var start = new ActionRow(_theme, "Start it on this machine", "Host") { Hint = "Desktop players reach it on UDP port 26001; open that port to play over the internet" };
        start.Activated += _ => _model.HostServer(new DedicatedServerLaunch
        {
            Name = _hostName.Trim().Length > 0 ? _hostName.Trim() : "My OpenIGI server",
            Mission = _hostMission,
            Mode = HostModes[_hostMode],
            MaxPlayers = HostSizes[_hostSize],
            Password = _hostPassword,
            Public = _hostPublic,
        });

        foreach (UiElement row in new UiElement[] { name, mission, mode, size, password, listed, start })
        {
            row.Margin = new Thickness(-10f, 0f);
            _host.AddChild(row);
        }
    }
}
