using OpenIGI.Core.Titles;
using System.Diagnostics;
using OpenIGI.Core.Configuration;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net.Discord;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host;

/// <summary>What the player is doing, on their Discord profile: the game, the mode and map or the mission, and a way in.</summary>
public sealed partial class GameHost
{
    private static readonly TimeSpan PresenceEvery = TimeSpan.FromSeconds(3);

    private readonly Stopwatch _presenceClock = Stopwatch.StartNew();
    private TimeSpan? _presenceAt;
    private DiscordPresence? _presence;
    private string _presencePlace = string.Empty;
    private DateTimeOffset _presenceSince = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets whether this build shows the player's activity in Discord: the desktop's, where the Discord app runs beside it.</summary>
    public bool ShowsPresence { get; set; }

    // Every few seconds: the activity worked out and handed over; Discord gets it when it changes. Off, or a build without
    // it, lets the connection go.
    private void UpdatePresence()
    {
        // Never yet: at once. (TimeSpan.MinValue here overflowed the subtraction and stopped the game on its first frame.)
        if (_presenceAt is { } last && _presenceClock.Elapsed - last < PresenceEvery)
        {
            return;
        }

        _presenceAt = _presenceClock.Elapsed;
        if (!ShowsPresence || !PresenceCVars.Enabled.Value || !_options.SavesUserFiles)
        {
            _presence?.Dispose();
            _presence = null;
            return;
        }

        _presence ??= new DiscordPresence(PresenceCVars.ClientId.Value);
        _presence.Show(Activity());
    }

    private void ReleasePresence()
    {
        _presence?.Dispose();
        _presence = null;
    }

    // What the player is doing now, the clock restarting whenever where they are changes.
    private PresenceActivity Activity()
    {
        (string details, string state, int size, int max, string? join) = TickObserver switch
        {
            HostedSession hosted => (
                $"{MatchModes.Title(hosted.Mode)} · {hosted.Place}",
                hosted.RoomCode is null ? "Hosting a game" : "Hosting a room",
                Players(hosted), hosted.MaxPlayers, hosted.IsListedRoom && hosted.RoomCode is { } code ? RoomTransport.InviteLink(code) : null),
            SessionClient { Playback: null } joined => (
                $"{MatchModes.Title(joined.Mode)} · {joined.Place}",
                "In a multiplayer game",
                Players(joined), 0, null),
            _ when _session.Mission is { } mission && !_menuFlow.MenuMode => (
                $"Campaign · {mission.Name}",
                $"{ActiveTitle.DisplayName} · {OpenIGI.Game.Ai.AiDifficultyNames.Of(_configuration.Active?.Difficulty ?? 1)}",
                0, 0, null),
            _ => ("In the menus", ActiveTitle.DisplayName, 0, 0, null),
        };

        if (details != _presencePlace)
        {
            _presencePlace = details;
            _presenceSince = DateTimeOffset.UtcNow;
        }

        List<PresenceButton> buttons = [];
        if (join is not null)
        {
            buttons.Add(new PresenceButton("Join the game", join));
        }

        buttons.Add(new PresenceButton("Play OpenIGI free", "https://openigi.com"));
        return new PresenceActivity(details, state, _presenceSince, size, Math.Max(max, size), buttons);
    }

    private static int Players(ISessionScoreboard session) => session.CurrentScoreboard?.Lines.Length ?? 0;
}

/// <summary>The player's activity in Discord.</summary>
[CVarContainer]
public static class PresenceCVars
{
    /// <summary>Whether the activity is shown at all.</summary>
    public static readonly BoolCVar Enabled = CVar.Bool(
        "cl_discord_presence", true, CVarOptions.Archive,
        "Shows what you are playing on your Discord profile, with a button to join a public room you host. Needs the Discord app running.");

    /// <summary>The Discord application the activity is shown as.</summary>
    public static readonly StringCVar ClientId = CVar.Text(
        "cl_discord_app", "1534229764519497768", CVarOptions.None,
        "The Discord application your activity is shown as.");
}
