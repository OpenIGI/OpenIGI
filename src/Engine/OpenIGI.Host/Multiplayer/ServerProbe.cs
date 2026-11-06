using System.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// Asks a server what it plays before its level is loaded here: a joiner that brings up the wrong map, or the right map
/// for the wrong kind of match (an arena's level has no soldiers), would load it again once welcomed.
/// </summary>
public static class ServerProbe
{
    /// <summary>Asks a server about itself and waits a moment for the answer.</summary>
    /// <param name="transport">A transport to ask on; it is disposed with the query.</param>
    /// <param name="server">The server.</param>
    /// <param name="titleId">The game, as <c>igi1</c>.</param>
    /// <param name="timeoutSeconds">How long to wait; a request is sent again every quarter of a second.</param>
    /// <returns>What the server said, or <see langword="null"/> when it did not answer in time or is not this game's.</returns>
    public static ServerInfo? Ask(INetTransport transport, NetAddress server, string titleId, double timeoutSeconds = 1.5)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(titleId);
        using NetQueryClient query = new(transport, MultiplayerProtocol.IdFor(titleId));
        Stopwatch clock = Stopwatch.StartNew();
        double askedAt = double.NegativeInfinity;
        while (clock.Elapsed.TotalSeconds < timeoutSeconds)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (now - askedAt >= 0.25)
            {
                query.Query(server, now);
                askedAt = now;
            }

            query.Update(now);
            while (query.TryReceive(out NetServerInfo answer))
            {
                if (ServerInfo.Decode(answer.Info) is { } info && string.Equals(info.TitleId, titleId, StringComparison.Ordinal))
                {
                    return info;
                }
            }

            Thread.Sleep(10);
        }

        return null;
    }

    /// <summary>Reads the mission a server's map names, as the campaign's number.</summary>
    /// <param name="info">What the server said.</param>
    /// <returns>The mission, or <see langword="null"/> for a map that is not one.</returns>
    public static int? MissionOf(ServerInfo info) =>
        int.TryParse(info?.Map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission) && mission > 0
            ? mission
            : null;
}
