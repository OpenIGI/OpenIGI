using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Presentation;

namespace OpenIGI.Host;

/// <summary>Co-op talk: lines of chat said and typed, and places marked for the others, as the HUD shows them.</summary>
public sealed partial class GameHost
{
    // How long a line of chat stays on screen, and a mark in the level, in milliseconds.
    private const int ChatLineMilliseconds = 10_000;
    private const int MarkMilliseconds = 8_000;

    // How far a mark is cast along the view before it lands in the open, and where it is put when nothing is met. Metres.
    private const double MarkReachMetres = 300;
    private const double MarkOpenMetres = 100;

    private readonly List<(string Name, Vector3d Metres, int At)> _coopMarks = [];

    // The line being typed, or null when nobody is typing; and whether the key that opened it is still in the queue.
    private string? _chatEntry;
    private bool _chatJustOpened;

    private ISessionTalk? Talk => TickObserver as ISessionTalk;

    // The server's remote console password, as rcon_password set it; held for the process, never written anywhere.
    private string _rconPassword = string.Empty;

    private void RegisterCoopTalk(ConsoleSystem console)
    {
        console.Commands.Add(
            "coop_say",
            "Says a line to the others in the co-op room.",
            context =>
            {
                if (Talk is not { } talk)
                {
                    context.Error("Not in a co-op room.");
                    return;
                }

                talk.Say(context.Rest(0));
            },
            "<text>");

        console.Commands.Add(
            "rcon_password",
            "Sets the password the server's remote console asks, for this session; it is never saved.",
            context => _rconPassword = context.Rest(0),
            "<password>");

        console.Commands.Add(
            "rcon",
            "Runs a command on the server's console (status, kick, ban, map, mode, next, say, ...); set rcon_password first.",
            context =>
            {
                if (TickObserver is not Multiplayer.SessionClient client || !client.SendRcon(_rconPassword, context.Rest(0)))
                {
                    context.Error("Not connected to a server.");
                }
            },
            "<command>");

        console.Commands.Add(
            "kill",
            "Ends the host's own player in a versus game, which comes back at a spawn as any player killed does.",
            context =>
            {
                if (TickObserver is not Multiplayer.HostedSession hosted || !hosted.KillOwn())
                {
                    context.Error("Only a versus game's host can do that.");
                }
            });

        console.Commands.Add(
            "coop_mark",
            "Marks the place under the crosshair for the others in the co-op room.",
            context =>
            {
                if (Talk is not { } talk || _session.Mission is null)
                {
                    context.Error("Not in a co-op room.");
                    return;
                }

                talk.Mark(Int3.From(AimedPoint() * WorldScene.UnitsPerMetre));
            });

        console.Commands.Add(
            "coop_kick",
            "Puts a player out of the co-op room this game hosts, by its name.",
            context =>
            {
                if (TickObserver is not HostedSession hosted)
                {
                    context.Error("Not hosting a co-op room.");
                    return;
                }

                hosted.Kick(context.Rest(0));
            },
            "<name>");

        console.Commands.Add(
            "coop_chat",
            "Opens a line to type to everyone in the room, or with 'team' to the team only; Enter says it, Tab switches, Escape lets it go.",
            context => OpenChat(string.Equals(context.Rest(0).Trim(), "team", StringComparison.OrdinalIgnoreCase)),
            "[team]");
    }

    // Once a frame, before the game reads its input: the line being typed takes the keyboard, and what was said and marked
    // comes in from the session to be shown.
    private void UpdateCoopTalk()
    {
        int now = _loop.TimeSource.Milliseconds;
        if (Talk is not { } talk)
        {
            _chatEntry = null;
            _coopChat.Clear();
            _coopMarks.Clear();
            return;
        }

        UpdateChatEntry(talk);

        ScoreboardLine[] lines = (TickObserver as ISessionScoreboard)?.CurrentScoreboard?.Lines.ToArray() ?? [];
        string NameOf(int entity) => lines.FirstOrDefault(l => l.Entity == entity).Name is { Length: > 0 } name ? name : "Teammate";
        foreach (ChatLine line in talk.TakeChat())
        {
            HearChat(line, lines);
        }

        foreach (WorldMark mark in talk.TakeMarks())
        {
            Vector3d metres = mark.Position.ToVector() / WorldScene.UnitsPerMetre;
            _coopMarks.Add((NameOf(mark.Entity), metres, now));
            _audioOut.Sounds?.PlayDefinition("pickup", priority: 180);
        }

        _coopMarks.RemoveAll(m => now - m.At > MarkMilliseconds);
    }

    // What the HUD shows of the talk: the lines still up, fading out at the end of their time, and the marks likewise.
    private (IReadOnlyList<HudChatLine> Chat, HudChatEntry? Entry, IReadOnlyList<HudMark> Marks) CoopTalkShown()
    {
        int now = _loop.TimeSource.Milliseconds;
        static float Fade(int age, int life) => Math.Clamp((life - age) / 1500f, 0f, 1f);
        return (
            ChatShown(now),
            ChatEntryShown(now),
            [.. _coopMarks.Select(m => new HudMark(m.Metres, m.Name, Fade(now - m.At, MarkMilliseconds)))]);
    }

    // The point the crosshair is on, in metres: the first thing the view meets, or a way out into the open.
    private Vector3d AimedPoint()
    {
        Camera3D eye = _inspectorEye;
        Vector3f look = eye.Orientation.Row3;
        Vector3d forward = new(look.X, look.Y, look.Z);
        Vector3d from = eye.Position * WorldScene.UnitsPerMetre;
        Vector3d to = from + (forward * (MarkReachMetres * WorldScene.UnitsPerMetre));
        return _mission.Collision.EffectWalls is { } walls && walls(from, to, out WorldRayHit hit)
            ? hit.Position / WorldScene.UnitsPerMetre
            : eye.Position + (forward * MarkOpenMetres);
    }
}
