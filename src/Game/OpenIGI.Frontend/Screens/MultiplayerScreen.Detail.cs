using System.Globalization;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The line picked, on the right while browsing: its map's plate, its kind and region with SAVE, its name large, its mode,
/// players and ping, who is in it, and JOIN -- FULL when it has no room. Under JOIN, a box to join a friend's room by its code.
/// </content>
public sealed partial class MultiplayerScreen
{
    private GlassPanel BuildDetail(UiTheme theme, ICoopRooms coop, CoopRoomInfo? room)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);
        body.RowDefinitions.Add(GridLength.Star);
        body.RowDefinitions.Add(GridLength.Auto);

        var top = new StackPanel { Orientation = Orientation.Vertical };
        MissionInfo? mission = room is null ? null : Context.Missions.FirstOrDefault(m => m.Id == room.MissionId);
        string map = room is null ? string.Empty : MapName(room.MissionId);
        top.AddChild(new MissionPlate(theme, mission is null ? string.Empty : mission.Number.ToString("00", CultureInfo.InvariantCulture), room is null ? "No game picked" : $"Map · {map}", 130f) { Margin = new Thickness(20f, 20f, 20f, 0f) });

        if (room is null)
        {
            top.AddChild(new Label(theme.Body, "Pick a game in the list to see it here, or join a friend's room by its code below.", 16f, theme.TextSecondary) { Wrap = true, Margin = new Thickness(24f, 20f, 24f, 0f) });
            body.Add(top, 0, 0);
            body.Add(BuildCodeJoin(theme, coop), 2, 0);
            return Panel(theme, body, side: true);
        }

        ServerLine line = LineOf(room);
        var tags = new Grid { Margin = new Thickness(24f, 20f, 24f, 8f) };
        tags.RowDefinitions.Add(GridLength.Auto);
        tags.ColumnDefinitions.Add(GridLength.Auto);
        tags.ColumnDefinitions.Add(GridLength.Star);
        tags.ColumnDefinitions.Add(GridLength.Auto);
        var kind = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10f, VerticalAlignment = VerticalAlignment.Center };
        kind.AddChild(new Eyebrow(theme, line.Kind) { TextSize = 10f });
        kind.AddChild(new Label(theme.Mono, line.Region, 11f, theme.TextMuted) { VerticalAlignment = VerticalAlignment.Center });
        tags.Add(kind, 0, 0);
        bool favourite = room.Favourite;
        string code = room.Code;
        var save = new Button(theme, favourite ? "SAVED" : "SAVE")
        {
            Accent = favourite ? ButtonAccent.Solid : ButtonAccent.Ghost,
            TextSize = 13f,
            Tracking = 0.25f,
            Padding = new Thickness(10f, 4f),
            Tooltip = favourite ? "Take it off your saved servers." : "Keep it in your saved servers, asked after even when it isn't listed.",
        };
        save.Activated += _ =>
        {
            coop.SetFavourite(code, !favourite);
            Invalidate();
        };
        tags.Add(save, 0, 2);
        top.AddChild(tags);
        top.AddChild(new Label(theme.Heading, room.Name, 34f, theme.TextBright) { Wrap = true, Margin = new Thickness(24f, 0f, 24f, 0f) });

        var stats = new Grid { Margin = new Thickness(24f, 18f, 24f, 0f) };
        stats.RowDefinitions.Add(GridLength.Auto);
        stats.ColumnSpacing = 40f;
        stats.ColumnDefinitions.Add(GridLength.Auto);
        stats.ColumnDefinitions.Add(GridLength.Auto);
        stats.ColumnDefinitions.Add(GridLength.Star);
        stats.Add(new Stat(theme, "Mode") { Value = line.Mode, Margin = new Thickness(0f, 14f) }, 0, 0);
        stats.Add(new Stat(theme, "Players") { Value = string.Create(CultureInfo.InvariantCulture, $"{room.Players} / {room.MaxPlayers}"), ValueColor = line.Full ? theme.Danger : null, Margin = new Thickness(0f, 14f) }, 0, 1);
        stats.Add(new Stat(theme, "Ping") { Value = room.PingMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : room.Dedicated ? "—" : "P2P", Margin = new Thickness(0f, 14f) }, 0, 2);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(24f, 18f, 24f, 0f) });
        top.AddChild(stats);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(24f, 0f) });
        body.Add(top, 0, 0);

        var inside = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(24f, 16f, 24f, 16f) };
        inside.AddChild(new Eyebrow(theme, "In this game") { TextSize = 10f });
        string about = !room.Compatible ? "This server runs another version of the game, so it can't be joined from this one."
            : room.Players == 0 ? "Nobody here yet. Join and the match starts when players arrive."
            : room.Players == 1 ? "One player in it." : string.Create(CultureInfo.InvariantCulture, $"{room.Players} players in it.");
        inside.AddChild(Body(theme, about, room.Compatible ? theme.TextSecondary : theme.Danger, 15f));
        if (room.Passworded)
        {
            inside.AddChild(Body(theme, "It asks for a password.", theme.TextMuted, 15f));
        }

        body.Add(inside, 1, 0);

        var foot = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        bool joinable = room.Compatible && !line.Full;
        var join = new PrimaryAction(theme, line.Full ? "Full" : "Join")
        {
            Subtitle = line.Full ? "WAIT FOR A SLOT OR PICK ANOTHER GAME" : $"{map.ToUpperInvariant()} · {line.Mode.ToUpperInvariant()}",
            IsEnabled = joinable,
            Margin = new Thickness(20f, 0f, 20f, 0f),
        };
        join.Activated += _ => coop.Join(code);
        foot.AddChild(join);
        foot.AddChild(BuildCodeJoin(theme, coop));
        body.Add(foot, 2, 0);
        return Panel(theme, body, side: true);
    }

    // A friend's room by its code.
    private Grid BuildCodeJoin(UiTheme theme, ICoopRooms coop)
    {
        var line = new Grid { ColumnSpacing = 6f, Margin = new Thickness(20f, 0f, 20f, 20f) };
        line.RowDefinitions.Add(GridLength.Auto);
        line.ColumnDefinitions.Add(GridLength.Star);
        line.ColumnDefinitions.Add(GridLength.Auto);
        var code = new TextBox(theme) { Id = "room-code", Placeholder = "HAVE A ROOM CODE?", MaxLength = 6, VerticalAlignment = VerticalAlignment.Center, Tooltip = "A friend's room code: six letters and digits." };
        code.Text = _code;
        code.TextChanged += text => _code = text;
        var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.3f, Padding = new Thickness(20f, 13f) };
        join.Activated += _ => coop.Join(_code);
        line.Add(code, 0, 0);
        line.Add(join, 0, 1);
        return line;
    }
}
