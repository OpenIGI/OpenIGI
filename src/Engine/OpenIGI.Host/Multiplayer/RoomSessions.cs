using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Time;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net.Rooms;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// A session through a room, the same on every build: the desktop and the browser differ only in whose WebRTC makes the
/// connections, so a browser hosts for desktops and joins them exactly as another desktop would.
/// </summary>
public static class RoomSessions
{
    /// <summary>Starts what the command line asks of a room: to host one, or to join one by its code.</summary>
    /// <param name="session">The multiplayer options.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="mission">The mission a host plays.</param>
    /// <param name="version">The build.</param>
    /// <param name="peers">Makes the WebRTC connections.</param>
    /// <param name="logger">Where the session's progress is told.</param>
    /// <param name="content">This game's <see cref="JoinRequest.Content"/> fingerprint, or empty.</param>
    /// <returns>The session, or <see langword="null"/> when the options ask for no room.</returns>
    public static IDisposable? Start(
        SessionArguments session, string titleId, int mission, string version, IRtcPeerFactory peers, IOpenIgiLogger logger, string content = "")
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(titleId);
        if (session.Join is { } code)
        {
            RoomTransport room = RoomTransport.Join(RoomTransport.DefaultMaster, code, peers, logger);
            return new SessionClient(room, RoomTransport.HostAddress, new JoinRequest(session.PlayerName, version, session.Password, content), titleId, logger);
        }

        if (!session.Host)
        {
            return null;
        }

        string map = mission.ToString(CultureInfo.InvariantCulture);
        int players = Math.Clamp(session.MaxPlayers, 1, 31);
        HostedSessionOptions options = new(
            session.ServerName, players, session.Password, session.Mode, map, version, IncludesHost: true, session.PlayerName, content);
        RoomRequest request = new(
            titleId,
            MultiplayerProtocol.IdFor(titleId).ToString("x16", CultureInfo.InvariantCulture),
            session.ServerName,
            map,
            session.Mode,
            version,
            players + 1,
            session.Public,
            session.Password.Length > 0);
        return new HostedSession(RoomTransport.Host(RoomTransport.DefaultMaster, request, peers, logger), options, titleId, GameClock.DefaultTickRate, logger);
    }
}
