using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Host.Multiplayer;

/// <summary>A player on the server as its operator sees it.</summary>
/// <param name="Slot">Its slot, which commands name it by as <c>#3</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="PlayerId">Its verified key's id, which bans follow.</param>
/// <param name="Ping">Its round trip, in milliseconds.</param>
/// <param name="Team">Its team, or -1.</param>
/// <param name="Kills">Its kills.</param>
/// <param name="Deaths">Its deaths.</param>
public readonly record struct ServerPlayer(int Slot, string Name, string PlayerId, int Ping, int Team, int Kills, int Deaths);

/// <content>What a server's operator does: the mode played next, the map's end, the players and their removal.</content>
public sealed partial class HostedSession
{
    // The mode played: the one asked for, or the one the operator put in since.
    private string _mode;

    /// <summary>Gets or sets what says whether a player's key is banned, and why; set before the level is up.</summary>
    public Func<string, string?>? Banned { get; set; }

    /// <summary>Gets or sets what answers the remote console (slot, password, command); set before the level is up.</summary>
    public Func<int, string, string, IReadOnlyList<string>>? Rcon { get; set; }

    /// <summary>Gets or sets a value indicating whether the server goes on to other maps, so a decided match ends its map rather than playing again.</summary>
    public bool RotatesMaps { get; set; }

    /// <summary>Gets or sets what changes the map a players' vote carried (<see cref="ServerSession.ChangeMap"/>).</summary>
    public Func<int, string?, string?>? ChangeMap { get; set; }

    /// <summary>Gets or sets how long a joined player may stand idle before it is put off, in seconds; 0 never.</summary>
    public double IdleKickSeconds { get; set; }

    /// <summary>Gets a value indicating whether the match on this map is over and the next map is due.</summary>
    public bool IsMapOver => _session?.Mode.IsMapOver == true;

    /// <summary>Gets the map played.</summary>
    public string Map => _session?.Map ?? _options.Map;

    /// <summary>Gets the server's name.</summary>
    public string Name => _options.Name;

    /// <summary>Gets the most players it takes.</summary>
    public int MaxPlayers => _options.MaxPlayers;

    /// <summary>Gets the players on the server.</summary>
    public IReadOnlyList<ServerPlayer> ServerPlayers
    {
        get
        {
            if (_session is not { } session)
            {
                return [];
            }

            List<ServerPlayer> players = [];
            foreach (int slot in session.Clients)
            {
                MatchPlayer? standing = session.Match.Players.GetValueOrDefault(slot);
                players.Add(new ServerPlayer(
                    slot,
                    session.NameOf(slot) ?? "?",
                    session.PlayerIdOf(slot) ?? string.Empty,
                    session.PingOf(slot),
                    standing?.Team ?? -1,
                    standing?.Kills ?? 0,
                    standing?.Deaths ?? 0));
            }

            return players;
        }
    }

    /// <summary>Puts the mode played from the next map on: the operator's <c>mode</c>, or the rotation's next entry.</summary>
    /// <param name="mode">The mode's id.</param>
    /// <returns><see langword="true"/> when it is a mode this server plays.</returns>
    public bool UseMode(string mode)
    {
        if (!GameModes.All.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _mode = mode.ToLowerInvariant();
        return true;
    }

    /// <summary>Puts a player out, telling it why.</summary>
    /// <param name="slot">Its slot.</param>
    /// <param name="reason">What it is told.</param>
    /// <returns><see langword="true"/> when there was such a player.</returns>
    public bool Remove(int slot, string reason)
    {
        if (_session is not { } session || session.NameOf(slot) is not { } name)
        {
            return false;
        }

        _logger.Information(LogCategory, $"{name} was removed: {reason}.");
        session.Remove(slot, reason);
        return true;
    }

    /// <inheritdoc/>
    public void Poll() => _session?.Poll(_clock.Elapsed.TotalSeconds);

    // The mode the session plays on a level now up, made for its world; a server with a rotation ends the map with the match.
    private IGameMode NewMode(IReplicatedWorld world)
    {
        IGameMode mode = GameModes.For(_mode, world, _tickRate);
        mode.PlaysAgain = !RotatesMaps;
        return mode;
    }
}
