using System.Globalization;
using OpenIGI.UI.Kit.Widgets;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <content>
/// The line picked, on the right while browsing, under the accent along the panel's top: its map's picture with its mode's
/// badge, its kind and region over its name with SAVE beside, its players, ping and version, who is in it, and JOIN --
/// FULL when it has no room.
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
        top.AddChild(new ColourEdge { Colour = theme.Accent });
        top.AddChild(new MissionPlate(theme, mission is null ? string.Empty : mission.Number.ToString("00", CultureInfo.InvariantCulture), room is not null ? "Map" : searching ? "Searching" : "Nothing picked", room is not null ? map : searching ? "Finding games" : "Pick a game", Size == ShellSize.Wide ? 200f : 176f)
        {
            Mode = room is { Mode.Length: > 0 } ? room.Mode : null,
            Picture = room is null ? null : Context.Host.MissionPicture(room.MissionId),
        });

        if (room is null)
        {
            top.AddChild(new Label(theme.Body, searching ? "Asking for the servers and rooms that are open. A friend's room can be joined by its code meanwhile: ENTER CODE, above." : "Pick a game in the list to see it here, or join a friend's room by its code: ENTER CODE, above.", 17f, theme.TextSecondary) { Wrap = true, Margin = new Thickness(20f, 20f, 20f, 0f) });
            body.Add(top, 0, 0);
            return Panel(theme, body, side: true);
        }

        ServerLine line = LineOf(room);
        var head = new Grid { ColumnSpacing = 12f, Margin = new Thickness(20f, 20f, 20f, 18f) };
        head.RowDefinitions.Add(GridLength.Auto);
        head.ColumnDefinitions.Add(GridLength.Star);
        head.ColumnDefinitions.Add(GridLength.Auto);
        var names = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6f };
        names.AddChild(new Eyebrow(theme, line.Region.Length > 0 ? $"{line.Kind} · {line.Region}" : line.Kind) { TextSize = 14f });
        names.AddChild(new Label(theme.Heading, room.Name, 30f, theme.Text) { Wrap = true });
        head.Add(names, 0, 0);
        bool favourite = room.Favourite;
        string code = room.Code;
        var save = new Button(theme, favourite ? "SAVED" : "SAVE")
        {
            Accent = favourite ? ButtonAccent.Link : ButtonAccent.Text,
            LeadingGlyph = Glyph(OpenIGI.UI.Kit.KitIcon.Star),
            GlyphSize = 18f,
            TextSize = ButtonText,
            Tracking = ButtonTracking,
            Padding = new Thickness(10f, 0f),
            Height = 40f,
            VerticalAlignment = VerticalAlignment.Top,
            Tooltip = favourite ? "Take it off your saved servers." : "Keep it in your saved servers, asked after even when it isn't listed.",
        };
        save.Activated += _ =>
        {
            coop.SetFavourite(code, !favourite);
            Invalidate();
        };
        head.Add(save, 0, 1);
        top.AddChild(head);

        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32f, Margin = new Thickness(20f, 14f) };
        stats.AddChild(new Stat(theme, "Players") { Value = string.Create(CultureInfo.InvariantCulture, $"{room.Players} / {room.MaxPlayers}"), ValueColor = line.Full ? theme.Warning : null, ValueSize = 20f });
        stats.AddChild(new Stat(theme, "Ping") { Value = room.PingMs is { } ms ? string.Create(CultureInfo.InvariantCulture, $"{ms} ms") : room.Dedicated ? "—" : "P2P", ValueColor = room.PingMs is not null && line.Bars <= 1 ? theme.Warning : null, ValueSize = 20f });
        stats.AddChild(new Stat(theme, "Version") { Value = room.Compatible ? "Same as yours" : "Another", ValueColor = room.Compatible ? theme.Success : theme.Warning, ValueSize = 20f });
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(20f, 0f) });
        top.AddChild(stats);
        top.AddChild(new Rule { Orientation = Orientation.Horizontal, Length = 0f, Color = UiPalette.White(0.12f), Margin = new Thickness(20f, 0f) });
        body.Add(top, 0, 0);

        var inside = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8f, Margin = new Thickness(20f, 18f, 20f, 12f) };
        inside.AddChild(new Eyebrow(theme, "In this game") { TextSize = 14f, Color = theme.TextMuted });

        // Squares for who is in it; a line in their place for what they can't say: why it can't be joined, that it waits
        // for its first player, that it asks for a password.
        string? about = !room.Reachable ? "This server is listed, but it isn't answering: it may be behind a router that lets nothing in. It can't be joined from here."
            : !room.Compatible ? "This server runs another version of the game, so it can't be joined from this one."
            : room.Players == 0 ? "Nobody yet. You and your party would be first in."
            : null;
        if (about is null)
        {
            inside.AddChild(new PlayerSlots { Players = room.Players, MaxPlayers = room.MaxPlayers, Colour = line.Full ? theme.Warning : ModeLook.Colour(room.Mode), Margin = new Thickness(0f, 2f, 0f, 4f) });
        }
        else
        {
            inside.AddChild(Body(theme, about, room.Compatible && room.Reachable ? theme.TextSecondary : theme.Warning, 17f));
        }

        if (room.Passworded && room.Compatible && room.Reachable)
        {
            inside.AddChild(Body(theme, "It asks for a password.", theme.TextSecondary, 17f));
        }

        body.Add(inside, 1, 0);

        bool joinable = Joinable(room);
        var join = new PrimaryAction(theme, !room.Reachable ? "No answer" : !room.Compatible ? "Other version" : line.Full ? "Full" : "Join")
        {
            Subtitle = !room.Reachable ? "THE SERVER ISN'T ANSWERING: PICK ANOTHER GAME" : line.Full ? "WAIT FOR A SLOT OR PICK ANOTHER GAME" : $"{map.ToUpperInvariant()} · {line.Mode.ToUpperInvariant()}",
            IsEnabled = joinable,
            Key = Context.Host.TouchScreen ? string.Empty : "Enter",
            Margin = new Thickness(20f, 0f, 20f, 20f),
        };
        join.Activated += _ => coop.Join(code);
        body.Add(join, 2, 0);
        return Panel(theme, body, side: true);
    }

    // Whether a code box holds anything to join by.
    private static bool HasCode(string code) => !string.IsNullOrWhiteSpace(code);
}
