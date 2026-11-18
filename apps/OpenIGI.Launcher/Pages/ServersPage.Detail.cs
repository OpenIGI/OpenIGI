using System.Globalization;
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
/// The line picked, on the right: its map's picture with its mode's badge, its kind and region over its name with SAVE beside,
/// its players, ping and version, who is in it, a password where it asks one, and JOIN -- FULL when it has no room.
/// </content>
internal sealed partial class ServersPage
{
    private string _password = string.Empty;

    // What the side was last built for, and the figures in it that change as the server answers, set in place.
    private string? _builtSide;
    private Stat? _playersStat;
    private Stat? _pingStat;
    private PlayerSlots? _slots;

    // The side again when the server picked or what can be done with it changed; else its players and ping, in place.
    private void RefreshSide(BrowserServer? server, bool searching)
    {
        string makeup = server is null
            ? $"none|{searching}|{_note}"
            : $"{server.Key}|{server.Info.Compatible}|{server.Answered}|{server.IsFull}|{server.Info.Passworded}|{server.Sources}|{server.Info.Players == 0}|{server.Info.Mode}|{server.Info.Map}|{server.Info.Name}|{server.Official}";
        if (makeup != _builtSide)
        {
            _builtSide = makeup;
            _side.ClearChildren();
            GlassPanel detail = BuildDetail(server, searching);
            _side.AddChild(detail);
            Screen.PlayEntrance(detail);
            return;
        }

        if (server is null)
        {
            return;
        }

        ServerLine line = LineOf(server);
        if (_playersStat is not null)
        {
            _playersStat.Value = string.Create(CultureInfo.InvariantCulture, $"{line.Players} / {line.MaxPlayers}");
        }

        if (_pingStat is not null)
        {
            _pingStat.Value = PingText(line, server);
            _pingStat.ValueColor = line.Ping is not null && line.Bars <= 1 ? _theme.Warning : null;
        }

        if (_slots is not null)
        {
            _slots.Players = line.Players;
            _slots.MaxPlayers = line.MaxPlayers;
        }
    }

    private static string PingText(ServerLine line, BrowserServer server) =>
        line.Ping is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : server.Address is null ? "Relay" : "—";

    private GlassPanel BuildDetail(BrowserServer? server, bool searching)
    {
        _playersStat = null;
        _pingStat = null;
        _slots = null;
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);

        var top = new StackPanel { Orientation = Orientation.Vertical };
        top.AddChild(new ColourEdge { Colour = _theme.Accent });
        int number = server is null ? 0 : MapNumber(server);
        string? picture = number > 0 ? _model.MissionPicturePath(number) : null;
        top.AddChild(new MissionPlate(
            _theme,
            number > 0 ? number.ToString("00", CultureInfo.InvariantCulture) : string.Empty,
            server is not null ? "Map" : searching ? "Searching" : "Nothing picked",
            server is not null ? MapName(server) : searching ? "Finding games" : "Pick a game",
            200f)
        {
            Mode = server is { Info.Mode.Length: > 0 } ? server.Info.Mode : null,
            Picture = picture is not null && File.Exists(picture) ? _pictures(picture) : null,
        });

        if (server is null)
        {
            Label hint = Body(_note ?? (searching ? "Asking for the servers and rooms that are open." : "Pick a game in the list to see it here."), _theme.TextSecondary, 17f);
            hint.Margin = new Thickness(20f, 20f, 20f, 0f);
            top.AddChild(hint);
            body.Add(top, 0, 0);
            return Card(body);
        }

        BrowserInfo info = server.Info;
        ServerLine line = LineOf(server);
        var head = new Grid { ColumnSpacing = 12f, Margin = new Thickness(20f, 20f, 20f, 18f) };
        head.RowDefinitions.Add(GridLength.Auto);
        head.ColumnDefinitions.Add(GridLength.Star);
        head.ColumnDefinitions.Add(GridLength.Auto);
        var names = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f };
        names.AddChild(new Eyebrow(_theme, line.Heading) { TextSize = 14f });
        names.AddChild(new Label(_theme.Heading, info.Name, 28f, _theme.Text) { Wrap = true });
        head.Add(names, 0, 0);
        bool favourite = server.Sources.HasFlag(BrowserSource.Favourite);
        var save = new Button(_theme, favourite ? "Saved" : "Save")
        {
            Accent = favourite ? ButtonAccent.Link : ButtonAccent.Text,
            LeadingGlyph = KitIcons.Glyph(KitIcon.Star),
            GlyphSize = 18f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(10f, 0f),
            Height = 40f,
            VerticalAlignment = VerticalAlignment.Top,
            Tooltip = favourite ? "Take it off your saved servers." : "Keep it in your saved servers, asked after even when it isn't listed.",
        };
        save.Activated += _ => _model.SetFavouriteServer(server, !favourite);
        head.Add(save, 0, 1);
        top.AddChild(head);

        bool reachable = server.Answered || server.Room is not null;
        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32f, Margin = new Thickness(20f, 14f) };
        _playersStat = new Stat(_theme, "Players") { Value = string.Create(CultureInfo.InvariantCulture, $"{info.Players} / {info.MaxPlayers}"), ValueColor = line.Full ? _theme.Warning : null, ValueSize = 20f };
        _pingStat = new Stat(_theme, "Ping") { Value = PingText(line, server), ValueColor = line.Ping is not null && line.Bars <= 1 ? _theme.Warning : null, ValueSize = 20f };
        stats.AddChild(_playersStat);
        stats.AddChild(_pingStat);
        stats.AddChild(new Stat(_theme, "Version") { Value = info.Compatible ? "Same as yours" : "Another", ValueColor = info.Compatible ? _theme.Success : _theme.Warning, ValueSize = 20f });
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(20f, 0f) });
        top.AddChild(stats);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(20f, 0f) });
        body.Add(top, 0, 0);

        var inside = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(20f, 18f, 20f, 12f) };
        inside.AddChild(new Eyebrow(_theme, "In this game") { TextSize = 14f, Color = _theme.TextMuted });
        string? about = !reachable ? "This server is listed, but it isn't answering: it may be behind a router that lets nothing in. It can't be joined from here."
            : !info.Compatible ? "This server runs another version of the game. Update from Home, or wait for it to update."
            : info.Players == 0 ? "Nobody yet. You would be first in."
            : null;
        if (about is null)
        {
            _slots = new PlayerSlots { Players = info.Players, MaxPlayers = info.MaxPlayers, Colour = line.Full ? _theme.Warning : ModeLook.Colour(info.Mode), Margin = new Thickness(0f, 2f, 0f, 4f) };
            inside.AddChild(_slots);
        }
        else
        {
            inside.AddChild(Body(about, info.Compatible && reachable ? _theme.TextSecondary : _theme.Warning, 17f));
        }

        if (info.Passworded && info.Compatible && reachable)
        {
            var password = new TextBox(_theme) { Placeholder = "Password", MaxLength = 64, Height = 44f, LeadingGlyph = KitIcons.Glyph(KitIcon.Lock), Text = _password, Margin = new Thickness(0f, 8f, 0f, 0f) };
            password.TextChanged += text => _password = text;
            inside.AddChild(password);
        }

        body.Add(inside, 1, 0);

        var join = new PrimaryAction(_theme, !reachable ? "No answer" : !info.Compatible ? "Other version" : line.Full ? "Full" : "Join")
        {
            Subtitle = !reachable ? "THE SERVER ISN'T ANSWERING: PICK ANOTHER GAME" : line.Full ? "WAIT FOR A SLOT OR PICK ANOTHER GAME" : $"{line.Map.ToUpperInvariant()} · {line.Mode.ToUpperInvariant()}",
            IsEnabled = Joinable(server),
            Margin = new Thickness(20f, 0f, 20f, 20f),
        };
        join.Activated += _ => Join(server);
        body.Add(join, 2, 0);
        return Card(body);
    }

    private void Join(BrowserServer server) => _model.JoinServer(server, server.Info.Passworded ? _password : string.Empty);

    private GlassPanel Card(UiElement body)
    {
        GlassPanel panel = GlassPanel.Card(_theme, body);
        panel.VerticalAlignment = VerticalAlignment.Stretch;
        return panel;
    }
}
