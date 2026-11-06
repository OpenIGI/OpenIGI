using OpenIGI.Core.Configuration;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host;

/// <content>Invites: the room played in, as a link a friend opens to join it, put on the clipboard.</content>
public sealed partial class GameHost
{
    private void RegisterInviteCommand(ConsoleSystem console) =>
        console.Commands.Add(
            "invite",
            "Copies a link to the room you host or play in: a friend opens it to join, in the browser or the game.",
            context =>
            {
                string? code = TickObserver switch
                {
                    HostedSession hosted => hosted.RoomCode,
                    SessionClient joined => joined.RoomCode,
                    _ => null,
                };
                if (code is null)
                {
                    context.Error("Only a room has a link: host one, or join one, first.");
                    return;
                }

                string link = RoomTransport.InviteLink(code);
                _window.ClipboardText = link;
                context.Print($"Copied {link}");
            });
}
