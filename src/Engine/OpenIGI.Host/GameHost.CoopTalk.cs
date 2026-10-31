using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
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

    private readonly List<(string Name, string Text, int At)> _coopChat = [];
    private readonly List<(string Name, Vector3d Metres, int At)> _coopMarks = [];

    // The line being typed, or null when nobody is typing; and whether the key that opened it is still in the queue.
    private string? _chatEntry;
    private bool _chatJustOpened;

    private ISessionTalk? Talk => TickObserver as ISessionTalk;

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
            "Opens a line to type to the others in the co-op room; Enter says it, Escape lets it go.",
            _ =>
            {
                if (Talk is not null && _session.Mission is not null && !_menuFlow.MenuMode)
                {
                    _chatEntry = string.Empty;
                    _chatJustOpened = true;
                }
            });
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

        if (_chatEntry is { } entry)
        {
            KeyboardState keys = _window.Input.Keyboard;

            // The key that opened the line is in the queue as a character too; it is not part of what is said.
            while (keys.TryDequeueCharacter(out char typed))
            {
                if (!_chatJustOpened && !char.IsControl(typed) && entry.Length < SessionTalk.MaxChatLength)
                {
                    entry += typed;
                }
            }

            _chatJustOpened = false;
            bool say = false;
            bool cancel = _menuFlow.MenuMode;
            while (keys.TryDequeuePress(out KeyCode key))
            {
                switch (key)
                {
                    case KeyCode.Return or KeyCode.NumpadEnter:
                        say = true;
                        break;
                    case KeyCode.Escape:
                        cancel = true;
                        break;
                    case KeyCode.Backspace when entry.Length > 0:
                        entry = entry[..^1];
                        break;
                }
            }

            _chatEntry = say || cancel ? null : entry;
            if (say && entry.Trim().Length > 0)
            {
                talk.Say(entry);
            }

            // The game and its binds see an idle keyboard while a line is typed, Escape included.
            _window.Input.Captured = true;
        }

        ScoreboardLine[] lines = (TickObserver as ISessionScoreboard)?.CurrentScoreboard?.Lines.ToArray() ?? [];
        string NameOf(int entity) => lines.FirstOrDefault(l => l.Entity == entity).Name is { Length: > 0 } name ? name : "Teammate";
        foreach (ChatLine line in talk.TakeChat())
        {
            _coopChat.Add((NameOf(line.Entity), line.Text, now));
        }

        foreach (WorldMark mark in talk.TakeMarks())
        {
            Vector3d metres = mark.Position.ToVector() / WorldScene.UnitsPerMetre;
            _coopMarks.Add((NameOf(mark.Entity), metres, now));
            _audioOut.Sounds?.PlayDefinition("pickup", priority: 180);
        }

        _coopChat.RemoveAll(c => now - c.At > ChatLineMilliseconds);
        _coopMarks.RemoveAll(m => now - m.At > MarkMilliseconds);
    }

    // What the HUD shows of the talk: the lines still up, fading out at the end of their time, and the marks likewise.
    private (IReadOnlyList<HudChatLine> Chat, IReadOnlyList<HudMark> Marks) CoopTalkShown()
    {
        int now = _loop.TimeSource.Milliseconds;
        static float Fade(int age, int life) => Math.Clamp((life - age) / 1500f, 0f, 1f);
        return (
            [.. _coopChat.Select(c => new HudChatLine(c.Name, c.Text, Fade(now - c.At, ChatLineMilliseconds)))],
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
