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

    /// <summary>Gets the mode the session plays, a <see cref="Game.Multiplayer.Modes.GameModes"/> id; empty for co-op.</summary>
    string Mode => string.Empty;

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

    /// <summary>Gets the match's HUD as the local player sees it, or <see langword="null"/> for none.</summary>
    /// <param name="ownPosition">Where the local player stands, in world units, or <see langword="null"/>.</param>
    /// <returns>The HUD.</returns>
    Game.Multiplayer.Modes.MatchHud? MatchHud(Core.Numerics.Vector3d? ownPosition) => null;

    /// <summary>Gets who the local player, dead, watches: the next living player of its side for each time it asked.</summary>
    /// <param name="pick">How many times it asked for the next.</param>
    /// <returns>The watched player, or <see langword="null"/> for its own body.</returns>
    Game.Multiplayer.Modes.SpectateTarget? Spectate(int pick) => null;

    /// <summary>Gets the net graph's lines now (<c>net_graph</c>): the connection as this end sees it.</summary>
    IReadOnlyList<string> NetGraph => [];

    /// <summary>Gets the local player's combat feedback now -- its hit marker, where shots that struck it came from -- for a view.</summary>
    /// <param name="eye">Where the view is, in world units.</param>
    /// <param name="view">Which way it looks: rows right, up and forward.</param>
    /// <returns>The feedback, or <see langword="null"/> for nothing to show.</returns>
    Game.Multiplayer.Modes.CombatHud? Combat(Core.Numerics.Vector3d eye, Core.Numerics.Matrix3f view) => null;

    /// <summary>Takes in what the session told of the local player's fight, crediting its own tally with its hits on players.</summary>
    /// <param name="own">The local player's tally, or <see langword="null"/> while there is no player.</param>
    /// <remarks>Called every frame, whatever display is up: the crosshair's marks and sounds are not the remastered HUD's alone.</remarks>
    void CreditHits(Game.Player.ShotTally? own)
    {
    }

    /// <summary>Gets where a player of the match stands as this end sees it.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <returns>Its feet, in world units, or <see langword="null"/> when this end has it nowhere.</returns>
    Core.Numerics.Vector3d? PositionOfSlot(int slot) => null;

    /// <summary>Gets what the mode last said of itself (a bomb planted, and where), or <see langword="null"/>.</summary>
    Game.Multiplayer.Modes.ModeReport? ModeReport => null;

    /// <summary>Gets the kills of late, oldest first, as the feed in the corner lists them.</summary>
    IReadOnlyList<Game.Multiplayer.Modes.KillNote> KillFeed => [];
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

    /// <summary>Called every pass of the host's loop, ticks or not: a session reads its socket, so a packet is timed as it arrives.</summary>
    void Poll()
    {
    }
}

/// <summary>The host's observers.</summary>
public sealed partial class GameHost
{
    private IHostTickObserver? _tickObserver;

    /// <summary>Gets or sets what runs either side of each tick of the title's player, or <see langword="null"/>.</summary>
    /// <remarks>
    /// One set is a multiplayer session, and while it is set every cheat is held off (<see cref="Game.Player.PlayerCheatLock"/>)
    /// and the free camera is not offered: the session switches cheats off as it begins, and nothing turns them on until it
    /// ends. Every hit is marked and sounded while it runs, whatever the settings (<see cref="OpenIGI.Rendering.HudCVars.InSession"/>).
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
                OpenIGI.Rendering.HudCVars.InSession = true;
                _logger.Information("Coop", "Cheats and the free camera are off for the session.");
            }
            else if (value is null && was)
            {
                Game.Player.PlayerCheatLock.Release();
                OpenIGI.Rendering.HudCVars.InSession = false;
            }
        }
    }

    // A game that goes with its session still set gives the session's hold back, so it outlives nothing.
    private void ReleaseSessionCheatLock() => TickObserver = null;
}
