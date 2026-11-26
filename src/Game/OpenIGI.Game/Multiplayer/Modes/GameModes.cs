using OpenIGI.Core.Titles;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>How the modes a server can run (named by <see cref="MatchModes"/>) seat and count their players.</summary>
public static class GameModes
{
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
            // A player kept for its return (a negative slot) is not playing, so it does not make its side the bigger.
            if (other != slot && other >= 0)
            {
                roster.Assign(other, player.Team);
            }
        }

        return roster.FairTeam();
    }

    /// <summary>Deals the players into two sides when a team mode begins with every one of them on the same side.</summary>
    /// <param name="host">The session.</param>
    /// <remarks>
    /// The sides a match was played with are kept (a team mode after another keeps its teams); only a match carried over from
    /// a free-for-all, which puts everyone on the one side, is dealt again, alternately in slot order. Left so, a team mode had
    /// no enemy side: its rounds could only run out, and in search and destroy, with nobody attacking, nobody had the bomb.
    /// </remarks>
    public static void DealSidesIfOne(IModeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        MatchPlayer[] playing = [.. host.Match.Players.Values.Where(static p => p.Slot >= 0).OrderBy(static p => p.Slot)];
        if (playing.Length < 2 || playing.Select(static p => p.NextTeam ?? p.Team).Distinct().Count() > 1)
        {
            return;
        }

        for (int i = 0; i < playing.Length; i++)
        {
            playing[i].Team = i % 2 == 0 ? TeamRoster.Good : TeamRoster.Bad;
            playing[i].NextTeam = null;
            host.World.SetTeam(playing[i].Slot, playing[i].Team);
        }
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
        return Made(id, world, tickRate);
    }

    // A deathmatch's settings, its clock the server's mp_timelimit when one is set.
    private static DeathmatchSettings Deathmatch() =>
        NetCVars.TimeLimit.Value > 0 ? new DeathmatchSettings { TimeLimitMinutes = NetCVars.TimeLimit.Value } : new DeathmatchSettings();

    // A round's settings: its buy time and the pauses after it the server's mp_buytime, mp_roundendtime and mp_bombendtime.
    private static RoundSettings Rounds(RoundSettings? settings) =>
        (settings ?? new RoundSettings()) with
        {
            BuySeconds = NetCVars.BuyTime.Value,
            RoundOverSeconds = NetCVars.RoundEndTime.Value,
            DetonatedRoundOverSeconds = NetCVars.BombEndTime.Value,
        };

    private static IGameMode Made(string? id, IReplicatedWorld world, int tickRate)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (MatchModes.IsVersus(id) && world.TryPlayVersus(teams: MatchModes.HasTeams(id)))
        {
            return id switch
            {
                MatchModes.Elimination => new EliminationMode(tickRate, Rounds(null)) { SettingsEachRound = Rounds },
                MatchModes.SearchAndDestroy => new SearchAndDestroyMode(tickRate, Rounds(null)) { SettingsEachRound = Rounds },
                _ => new DeathmatchMode(tickRate, teams: id == MatchModes.TeamDeathmatch, Deathmatch()),
            };
        }

        if (world.HoldsTheDead)
        {
            return new CoopMode(tickRate);
        }

        return id switch
        {
            MatchModes.TeamDeathmatch => new DeathmatchMode(tickRate, teams: true, Deathmatch()),
            MatchModes.FreeForAll => new DeathmatchMode(tickRate, teams: false, Deathmatch()),
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
            host.Announce(death, victim, killer);
            counted.Add((victim, killer));
        }

        return counted;
    }
}
