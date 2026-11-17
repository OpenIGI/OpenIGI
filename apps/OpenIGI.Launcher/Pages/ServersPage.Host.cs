using System.Globalization;
using OpenIGI.Core.Titles;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Launcher.Pages;

/// <content>
/// Hosting: a dedicated server of this build, started on this machine from one panel -- its name, the map, the mode, how
/// many it takes, a password, whether it is listed -- and HOST; while one runs, what it is doing and STOP.
/// </content>
internal sealed partial class ServersPage
{
    private static readonly string[] HostModes = [MatchModes.TeamDeathmatch, MatchModes.FreeForAll, MatchModes.Elimination, MatchModes.SearchAndDestroy, MatchModes.Coop];
    private static readonly int[] HostSizes = [8, 16, 24, 32];

    // The form's choices, kept across rebuilds.
    private string _hostName = "My OpenIGI server";
    private int _hostMission = 1;
    private int _hostMode;
    private int _hostSize = 1;
    private string _hostPassword = string.Empty;
    private bool _hostPublic;

    private void BuildHost()
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0f, Margin = new Thickness(28f, 24f, 28f, 28f) };
        column.AddChild(new Eyebrow(_theme, "Host a dedicated server") { TextSize = 14f });
        column.AddChild(new Label(_theme.Heading, _model.Current.Title, 30f, _theme.Text) { Margin = new Thickness(0f, 6f, 0f, 6f) });

        if (!_model.CanHostServer)
        {
            column.AddChild(Body("This build has no dedicated server beside the game. Install a build that ships one, or run OpenIGI.Server by hand (see its server.cfg.example).", _theme.TextSecondary, 17f));
        }
        else if (_model.HostedServerStatus is { } status)
        {
            column.AddChild(Body(status, _theme.Text, 17f));
            var stop = new PrimaryAction(_theme, "Stop") { Subtitle = "IT LEAVES THE SERVER LIST AT ONCE", Margin = new Thickness(0f, 20f, 0f, 0f) };
            stop.Activated += _ => _model.StopHostedServer();
            column.AddChild(stop);
        }
        else
        {
            column.AddChild(Body("Players reach it on UDP port 26001. Open that port on your router to play over the internet.", _theme.TextSecondary, 16f));
            column.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(0f, 16f, 0f, 4f) });
            foreach (UiElement row in HostRows())
            {
                column.AddChild(row);
            }

            var start = new PrimaryAction(_theme, "Host")
            {
                Subtitle = string.Create(CultureInfo.InvariantCulture, $"MISSION {_hostMission} · {MatchModes.Title(HostModes[_hostMode])}"),
                Margin = new Thickness(0f, 24f, 0f, 0f),
            };
            start.Activated += _ => _model.HostServer(new DedicatedServerLaunch
            {
                Name = _hostName.Trim().Length > 0 ? _hostName.Trim() : "My OpenIGI server",
                Mission = _hostMission,
                Mode = HostModes[_hostMode],
                MaxPlayers = HostSizes[_hostSize],
                Password = _hostPassword,
                Public = _hostPublic,
            });
            column.AddChild(start);
        }

        GlassPanel panel = GlassPanel.Card(_theme, column);
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Width = 760f;
        _host.ClearChildren();
        _host.AddChild(panel);
        Screen.PlayEntrance(panel);
    }

    // The form's lines: a field or a value between arrows, each on the right of its name.
    private IEnumerable<UiElement> HostRows()
    {
        var name = new TextRow(_rowTheme, "Name", _hostName, "My OpenIGI server") { FieldWidth = 300f };
        name.TextChanged += text => _hostName = text;
        yield return name;

        int missions = Math.Max(1, _model.Current.Retail?.MissionCount ?? 1);
        var mission = Cycler("Map", Enumerable.Range(1, missions).Select(static m => string.Create(CultureInfo.InvariantCulture, $"Mission {m}")), Math.Clamp(_hostMission - 1, 0, missions - 1));
        mission.SelectionChanged += index =>
        {
            _hostMission = index + 1;
            BuildHost();
        };
        yield return mission;

        var mode = Cycler("Mode", HostModes.Select(MatchModes.Title), _hostMode);
        mode.SelectionChanged += index =>
        {
            _hostMode = index;
            BuildHost();
        };
        yield return mode;

        var size = Cycler("Players", HostSizes.Select(static n => n.ToString(CultureInfo.InvariantCulture)), _hostSize);
        size.SelectionChanged += index => _hostSize = index;
        yield return size;

        var password = new TextRow(_rowTheme, "Password", _hostPassword, "none") { FieldWidth = 300f };
        password.TextChanged += text => _hostPassword = text;
        yield return password;

        var listed = new Toggle(_rowTheme, "List in the server browser", _hostPublic) { Hint = "Off: only those you give the address to can find it" };
        listed.ValueChanged += on => _hostPublic = on;
        yield return listed;
    }

    private OptionCycler Cycler(string label, IEnumerable<string> options, int selected) =>
        new(_rowTheme, label, options, selected) { Inline = true, ContentWidth = 300f, Margin = new Thickness(-10f, 0f, 0f, 0f) };
}
