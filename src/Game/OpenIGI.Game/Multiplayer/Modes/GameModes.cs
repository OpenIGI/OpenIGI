namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>The modes a server can run, by the ids servers are listed with.</summary>
public static class GameModes
{
    /// <summary>Everyone together against the level.</summary>
    public const string Coop = "coop";

    /// <summary>Two teams taking the map's objectives in order (I.G.I. 2's own game).</summary>
    public const string Objective = "objective";

    /// <summary>Two teams, a point a kill.</summary>
    public const string TeamDeathmatch = "tdm";

    /// <summary>Everyone for themselves, a point a kill.</summary>
    public const string FreeForAll = "ffa";

    /// <summary>Two teams, rounds of one life each, won by the team left standing.</summary>
    public const string Elimination = "elim";

    /// <summary>Two teams, rounds of one life each: one plants a bomb at the map's sites, the other stops it.</summary>
    public const string SearchAndDestroy = "sd";

    /// <summary>Gets every mode id, in the order a menu lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Coop, Objective, TeamDeathmatch, FreeForAll, Elimination, SearchAndDestroy];

    /// <summary>Gets whether a mode plays players against each other on a level made an arena.</summary>
    /// <param name="id">The mode's id.</param>
    /// <returns><see langword="true"/> for the versus modes.</returns>
    public static bool IsVersus(string? id) => id is TeamDeathmatch or FreeForAll or Elimination or SearchAndDestroy;

    /// <summary>Gets whether a versus mode plays two teams, rather than everyone for themselves.</summary>
    /// <param name="id">The mode's id.</param>
    /// <returns><see langword="true"/> for the team modes.</returns>
    public static bool HasTeams(string? id) => id is not FreeForAll;

    /// <summary>Gets the team a joining player is put on to keep the teams even: the smaller, as I.G.I. 2's <c>TeamManager_GetFairTeam</c>.</summary>
    /// <param name="host">The session.</param>
    /// <param name="slot">The player's slot.</param>
    /// <returns>The team.</returns>
    public static int FairTeam(IModeHost host, int slot)
    {
        ArgumentNullException.ThrowIfNull(host);
        TeamRoster roster = new((uint)slot);
        foreach ((int other, MatchPlayer player) in host.Match.Players)
        {
            if (other != slot)
            {
                roster.Assign(other, player.Team);
            }
        }

        return roster.FairTeam();
    }

    /// <summary>Makes the mode a server was asked for.</summary>
    /// <param name="id">The mode's id, or <see langword="null"/> or unknown for the world's own.</param>
    /// <param name="world">The world it runs in.</param>
    /// <param name="tickRate">The world's ticks per second.</param>
    /// <returns>The mode.</returns>
    /// <remarks>
    /// A versus mode where the world can be played so (<see cref="IReplicatedWorld.TryPlayVersus"/>); otherwise a world that
    /// holds the dead (co-op levels) plays co-op, and any other the objective game.
    /// </remarks>
    public static IGameMode For(string? id, IReplicatedWorld world, int tickRate)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (IsVersus(id) && world.TryPlayVersus(teams: HasTeams(id)))
        {
            return id switch
            {
                Elimination => new EliminationMode(tickRate),
                SearchAndDestroy => new SearchAndDestroyMode(tickRate),
                _ => new DeathmatchMode(tickRate, teams: id == TeamDeathmatch),
            };
        }

        if (world.HoldsTheDead)
        {
            return new CoopMode(tickRate);
        }

        return id switch
        {
            TeamDeathmatch => new DeathmatchMode(tickRate, teams: true),
            FreeForAll => new DeathmatchMode(tickRate, teams: false),
            _ => new ObjectiveMode(tickRate),
        };
    }

    /// <summary>Counts the deaths a tick brought into the match, and says whose they were.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="deaths">The deaths, by entity.</param>
    /// <returns>Each death as the slots of the victim and its killer (-1 for none).</returns>
    public static IReadOnlyList<(int Victim, int Killer)> CountDeaths(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(deaths);
        if (deaths.Count == 0)
        {
            return [];
        }

        double seconds = (double)tick / host.TickRate;
        List<(int Victim, int Killer)> counted = new(deaths.Count);
        foreach (WorldDeath death in deaths)
        {
            int victim = host.SlotOf(death.Victim);
            int killer = death.Killer < 0 ? -1 : host.SlotOf(death.Killer);
            host.Match.Kill(victim, killer, seconds);
            host.Announce(victim, killer);
            counted.Add((victim, killer));
        }

        return counted;
    }
}
