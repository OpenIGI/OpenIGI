using System.Globalization;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The line picked, on the right while browsing: its map's plate in its mode's colour, its kind and region with SAVE, its name
/// large, its mode, players and ping, its places as squares filled for who is in it, and JOIN -- FULL when it has no room. Under JOIN, a box to join a friend's room by its code:
/// ENTER in it joins, as its own JOIN does once there is a code to join by.
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

        // The list has not been heard yet: the panel says it is being asked for, not that nothing was picked.
        bool searching = room is null && coop.Listing is null;
        top.AddChild(new MissionPlate(theme, mission is null ? string.Empty : mission.Number.ToString("00", CultureInfo.InvariantCulture), room is not null ? "Map" : searching ? "Searching" : "Nothing picked", room is not null ? map : searching ? "Finding games" : "Pick a game", 156f)
        {
            Mode = room is { Mode.Length: > 0 } ? room.Mode : null,
            Picture = room is null ? null : Context.Host.MissionPicture(room.MissionId),
            Margin = new Thickness(20f, 20f, 20f, 0f),
        });

        if (room is null)
        {
            top.AddChild(new Label(theme.Body, searching ? "Asking for the servers and rooms that are open. A friend's room can be joined by its code below meanwhile." : "Pick a game in the list to see it here, or join a friend's room by its code below.", 16f, theme.TextSecondary) { Wrap = true, Margin = new Thickness(24f, 20f, 24f, 0f) });
            body.Add(top, 0, 0);
            body.Add(BuildCodeJoin(theme, coop), 2, 0);
            return Panel(theme, body, side: true);
        }

        ServerLine line = LineOf(room);
        var tags = new Grid { Margin = new Thickness(24f, 16f, 24f, 6f) };
        tags.RowDefinitions.Add(GridLength.Auto);
        tags.ColumnDefinitions.Add(GridLength.Auto);
        tags.ColumnDefinitions.Add(GridLength.Star);
        tags.ColumnDefinitions.Add(GridLength.Auto);
        var kind = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10f, VerticalAlignment = VerticalAlignment.Center };
        kind.AddChild(new Eyebrow(theme, line.Kind) { TextSize = 10f });
        kind.AddChild(new Label(theme.Mono, line.Region, 13f, theme.TextSecondary) { VerticalAlignment = VerticalAlignment.Center });
        tags.Add(kind, 0, 0);
        bool favourite = room.Favourite;
        string code = room.Code;
        var save = new Button(theme, favourite ? "SAVED" : "SAVE")
        {
            Accent = favourite ? ButtonAccent.Solid : ButtonAccent.Ghost,
            TextSize = 14f,
            Tracking = 0.12f,
            Padding = new Thickness(12f, 0f),
            Height = 32f,
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

        var stats = new Grid { Margin = new Thickness(24f, 0f) };
        stats.RowDefinitions.Add(GridLength.Auto);
        stats.ColumnSpacing = 40f;
        stats.ColumnDefinitions.Add(GridLength.Auto);
        stats.ColumnDefinitions.Add(GridLength.Auto);
        stats.ColumnDefinitions.Add(GridLength.Star);
        stats.Add(new Stat(theme, "Mode") { Value = line.Mode, Margin = new Thickness(0f, 12f) }, 0, 0);
        stats.Add(new Stat(theme, "Players") { Value = string.Create(CultureInfo.InvariantCulture, $"{room.Players} / {room.MaxPlayers}"), ValueColor = line.Full ? theme.Danger : null, Margin = new Thickness(0f, 12f) }, 0, 1);
        stats.Add(new Stat(theme, "Ping") { Value = room.PingMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : room.Dedicated ? "—" : "P2P", Margin = new Thickness(0f, 12f) }, 0, 2);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(24f, 14f, 24f, 0f) });
        top.AddChild(stats);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.07f), Margin = new Thickness(24f, 0f) });
        body.Add(top, 0, 0);

        var inside = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10f, Margin = new Thickness(24f, 14f, 24f, 12f) };
        inside.AddChild(new Eyebrow(theme, "In this game") { TextSize = 10f });
        inside.AddChild(new PlayerSlots { Players = room.Players, MaxPlayers = room.MaxPlayers, Colour = line.Full ? theme.Danger : ModeLook.Colour(room.Mode), Margin = new Thickness(0f, 2f, 0f, 4f) });

        // The squares say who is in it; a line under them only for what they can't: why it can't be joined, that it waits
        // for its first player, that it asks for a password.
        string? about = !room.Reachable ? "This server is listed, but it isn't answering: it may be behind a router that lets nothing in. It can't be joined from here."
            : !room.Compatible ? "This server runs another version of the game, so it can't be joined from this one."
            : room.Passworded ? "It asks for a password."
            : room.Players == 0 ? "Nobody here yet. Join and the match starts when players arrive."
            : null;
        if (about is not null)
        {
            inside.AddChild(Body(theme, about, room.Compatible && room.Reachable ? theme.TextSecondary : theme.Warning, 16f));
        }

        body.Add(inside, 1, 0);

        var foot = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12f };
        bool joinable = Joinable(room);
        var join = new PrimaryAction(theme, !room.Reachable ? "No answer" : !room.Compatible ? "Other version" : line.Full ? "Full" : "Join")
        {
            Subtitle = !room.Reachable ? "THE SERVER ISN'T ANSWERING: PICK ANOTHER GAME" : line.Full ? "WAIT FOR A SLOT OR PICK ANOTHER GAME" : $"{map.ToUpperInvariant()} · {line.Mode.ToUpperInvariant()}",
            IsEnabled = joinable,
            Key = Context.Host.TouchScreen ? string.Empty : "Enter",
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
        var code = new TextBox(theme) { Id = RoomCodeId, Placeholder = "HAVE A ROOM CODE?", MaxLength = 6, VerticalAlignment = VerticalAlignment.Center, Tooltip = "A friend's room code: six letters and digits." };
        code.Text = _code;
        var join = new Button(theme, "JOIN") { Accent = ButtonAccent.Ghost, TextSize = 15f, Tracking = 0.12f, Padding = new Thickness(20f, 0f), Height = 44f, IsEnabled = HasCode(_code) };
        code.TextChanged += text =>
        {
            _code = text;
            join.IsEnabled = HasCode(text);
        };
        join.Activated += _ => JoinByCode(coop);
        line.Add(code, 0, 0);
        line.Add(join, 0, 1);
        return line;
    }

    // Joins the room whose code is in the box, when one is.
    private void JoinByCode(ICoopRooms coop)
    {
        if (HasCode(_code))
        {
            coop.Join(_code.Trim());
        }
    }

    // Whether a code box holds anything to join by.
    private static bool HasCode(string code) => !string.IsNullOrWhiteSpace(code);
}
