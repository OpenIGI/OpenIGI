namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>What a game mode may read and do of the session it runs: its match, its world and its players.</summary>
public interface IModeHost
{
    /// <summary>Gets the match: its players, standings and money.</summary>
    Match Match { get; }

    /// <summary>Gets the world the players play in.</summary>
    IReplicatedWorld World { get; }

    /// <summary>Gets the world's ticks per second.</summary>
    int TickRate { get; }

    /// <summary>Gets the slots of every player in the match now, the listen server's own among them.</summary>
    IReadOnlyList<int> PlayerSlots { get; }

    /// <summary>Gets the slots of the players joined over the network, without the listen server's own.</summary>
    IReadOnlyList<int> ClientSlots { get; }

    /// <summary>Gets the slot of the player an entity is, or -1 for none (a soldier, the world).</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The slot.</returns>
    int SlotOf(int entity);

    /// <summary>Lists a counted death in every player's kill feed.</summary>
    /// <param name="victim">The victim's slot.</param>
    /// <param name="killer">The killer's slot, or -1 for none.</param>
    void Announce(int victim, int killer)
    {
    }

    /// <summary>Gets the layout of the map being played, or <see langword="null"/> when it has none.</summary>
    MpLayout? Layout => null;

    /// <summary>Puts a player back into the world, alive at a spawn point, and counts it alive.</summary>
    /// <param name="slot">The player's slot.</param>
    void Respawn(int slot);
}

/// <summary>
/// The rules of one kind of match: who plays whom, how a player comes back, what scores, and when it is won -- the session runs
/// the players, the mode decides what their deaths mean.
/// </summary>
/// <remarks>
/// Every title plays every mode it has a world for: a mode speaks to the world through <see cref="IReplicatedWorld"/> alone
/// (deaths, revivals, respawns, teams, objectives), as Source's game rules speak to the entities.
/// </remarks>
public interface IGameMode
{
    /// <summary>Gets the mode's id, as servers list it: <c>coop</c>, <c>objective</c>, <c>tdm</c>, <c>ffa</c>.</summary>
    string Id { get; }

    /// <summary>Gets whether players are split into two teams; otherwise everyone plays for themselves (or all together, co-op).</summary>
    bool IsTeamMode { get; }

    /// <summary>Gets the match's progress: its phase, its clock and its winner.</summary>
    MatchFlow Flow { get; }

    /// <summary>Gets the team a player joining is put on.</summary>
    /// <param name="host">The session.</param>
    /// <param name="slot">The player's slot.</param>
    /// <returns>The team; the world's own choice unless the mode makes teams of its own.</returns>
    int TeamFor(IModeHost host, int slot) => host.World.TeamOf(slot);

    /// <summary>Gets the mode's own word on the round in play, shown under the banner (a bomb planted and its fuse), or empty.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns>The line.</returns>
    string Status(long tick) => string.Empty;

    /// <summary>Gets what the mode tells the clients of itself, which their HUD is drawn from.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns>The report.</returns>
    ModeReport Report(long tick) => ModeReport.None;

    /// <summary>Begins the match on the tick given.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    void Start(IModeHost host, long tick);

    /// <summary>Runs the rules for a tick the world has just run.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="deaths">The players who died this tick.</param>
    void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths);
}
