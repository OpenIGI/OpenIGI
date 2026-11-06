using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Host;

/// <summary>A session the host's frame can show the scoreboard of.</summary>
public interface ISessionScoreboard
{
    /// <summary>Gets the scoreboard as this end knows it, or <see langword="null"/> before there is one.</summary>
    Game.Multiplayer.Scoreboard? CurrentScoreboard { get; }

    /// <summary>Gets this end's own player's entity, whose line is marked, or <see langword="null"/>.</summary>
    int? LocalEntity { get; }

    /// <summary>Gets what the board is headed with: the server's name and its map.</summary>
    string Title { get; }

    /// <summary>Gets the room's code, when the session is played through one.</summary>
    string? RoomCode => null;

    /// <summary>Gets where the session is played: the mission or map, as a player reads it.</summary>
    string Place => Title;

    /// <summary>Gets how far this end's own fallen player's revival has gone, in <c>[0,1]</c>, or <see langword="null"/> while it is up.</summary>
    float? OwnRevive => null;

    /// <summary>Gets whether this player's connection goes through the TURN relay rather than straight to the others.</summary>
    bool Relayed => false;

    /// <summary>Gets whether this player's connection is poor enough to tell them: a long round trip, or rounds of packets lost.</summary>
    bool PoorConnection => false;

    /// <summary>Gets what the player is shown of the match across the top of the screen, or <see langword="null"/> for nothing.</summary>
    Game.Multiplayer.Modes.MatchBanner? Banner => null;
}

/// <summary>Something run either side of each game tick: a multiplayer session reads before and sends after.</summary>
/// <remarks>
/// The dedicated server is one: before the tick it takes in what the clients sent, so their commands are there when the
/// level runs; after it, it captures the level and sends each client its snapshot.
/// </remarks>
public interface IHostTickObserver
{
    /// <summary>Called just before the game runs a tick.</summary>
    /// <param name="title">What is ticking: a title's own player (I.G.I. 2) or the hosted mission (I.G.I.).</param>
    void BeforeTitleTick(object title);

    /// <summary>Called just after the game has run a tick.</summary>
    /// <param name="title">What ticked.</param>
    void AfterTitleTick(object title);
}

/// <summary>The host's observers.</summary>
public sealed partial class GameHost
{
    private IHostTickObserver? _tickObserver;

    /// <summary>Gets or sets what runs either side of each tick of the title's player, or <see langword="null"/>.</summary>
    /// <remarks>
    /// One set is a multiplayer session, and while it is set every cheat is held off (<see cref="Game.Player.PlayerCheatLock"/>)
    /// and the free camera is not offered: the session switches cheats off as it begins, and nothing turns them on until it
    /// ends.
    /// </remarks>
    public IHostTickObserver? TickObserver
    {
        get => _tickObserver;
        set
        {
            bool was = _tickObserver is not null;
            _tickObserver = value;
            if (value is not null && !was)
            {
                Game.Player.PlayerCheatLock.Hold();
                Game.Player.PlayerCheatCVars.CheatsEnabled.Value = false;
                _logger.Information("Coop", "Cheats and the free camera are off for the session.");
            }
            else if (value is null && was)
            {
                Game.Player.PlayerCheatLock.Release();
            }
        }
    }

    // A game that goes with its session still set gives the session's hold back, so it outlives nothing.
    private void ReleaseSessionCheatLock() => TickObserver = null;
}
