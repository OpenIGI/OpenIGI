namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>How a deathmatch is played: to how many, for how long, and how quickly the dead come back.</summary>
public sealed record DeathmatchSettings
{
    /// <summary>Gets the kills (a player's, or a team's) that win the match; 0 for none.</summary>
    public int ScoreLimit { get; init; } = 30;

    /// <summary>Gets the minutes the match lasts; 0 for no clock.</summary>
    public double TimeLimitMinutes { get; init; } = 10;

    /// <summary>Gets the seconds a dead player waits before it is put back.</summary>
    public double RespawnSeconds { get; init; } = 3;

    /// <summary>Gets the seconds played before the match begins, scoring nothing; 0 for none.</summary>
    public double WarmupSeconds { get; init; }

    /// <summary>Gets the seconds the scores are shown once the match is decided.</summary>
    public double IntermissionSeconds { get; init; } = 15;
}

/// <summary>Deathmatch: a point for each kill of an enemy, back again a few seconds after dying, until the limit or the clock.</summary>
/// <remarks>
/// With teams, the team's score is its kills less its team kills, and the better team wins (Source's team deathmatch); alone,
/// the player with the most kills. A draw at the clock is a draw. After the intermission the map is over
/// (<see cref="IsMapOver"/>): a server with a rotation goes on to the next, one without plays the same again.
/// </remarks>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="teams">Whether two teams play, else everyone for themselves.</param>
/// <param name="settings">How it is played, or the defaults.</param>
public sealed class DeathmatchMode(int tickRate, bool teams, DeathmatchSettings? settings = null) : IGameMode
{
    /// <inheritdoc/>
    public string Id => teams ? GameModes.TeamDeathmatch : GameModes.FreeForAll;

    /// <inheritdoc/>
    public ModeReport Report(long tick) => new() { Target = Settings.ScoreLimit };

    /// <inheritdoc/>
    public bool IsTeamMode => teams;

    /// <inheritdoc/>
    public MatchFlow Flow { get; } = new(tickRate);

    /// <summary>Gets how it is played.</summary>
    public DeathmatchSettings Settings { get; } = settings ?? new DeathmatchSettings();

    /// <summary>Gets a value indicating whether the match was decided and its scores shown, and the map is not played again: time for the next.</summary>
    public bool IsMapOver { get; private set; }

    /// <summary>Gets how many matches were played to the end on this map.</summary>
    public int MatchesPlayed { get; private set; }

    /// <summary>Gets what keeps the players inside the map's bounds.</summary>
    public OutOfBounds Bounds { get; } = new();

    /// <summary>Gets or sets a value indicating whether the same map is played again after the intermission, for a server with no rotation.</summary>
    public bool PlaysAgain { get; set; } = true;

    /// <inheritdoc/>
    /// <remarks>With teams, the smaller team, as I.G.I. 2's <c>TeamManager_GetFairTeam</c> chooses; alone, everyone the same.</remarks>
    public int TeamFor(IModeHost host, int slot) => teams ? GameModes.FairTeam(host, slot) : TeamRoster.Good;

    /// <inheritdoc/>
    public void Start(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Match.IsFreeForAll = !teams;
        if (Settings.WarmupSeconds > 0)
        {
            Flow.Enter(MatchPhase.Warmup, tick, Settings.WarmupSeconds);
            return;
        }

        BeginLive(host, tick);
    }

    /// <inheritdoc/>
    public void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        IReadOnlyList<(int Victim, int Killer)> counted = GameModes.CountDeaths(host, tick, deaths);
        switch (Flow.Phase)
        {
            case MatchPhase.Warmup when Flow.IsDue(tick):
                BeginLive(host, tick);
                break;
            case MatchPhase.Warmup:
                RespawnTheDead(host, tick);
                break;
            case MatchPhase.Live:
                Bounds.Tick(host, tick);
                ScoreTeams(host, counted);
                RespawnTheDead(host, tick);
                if (Decided(host, tick) is { } winner)
                {
                    Flow.Enter(MatchPhase.Intermission, tick, Settings.IntermissionSeconds, winner);
                }

                break;
            case MatchPhase.Intermission when Flow.IsDue(tick):
                MatchesPlayed++;
                if (PlaysAgain)
                {
                    BeginLive(host, tick);
                }
                else
                {
                    IsMapOver = true;
                }

                break;
        }
    }

    // The match from nothing: scores cleared, everyone put back at a spawn, the clock started.
    private void BeginLive(IModeHost host, long tick)
    {
        host.ResetLevel();
        host.Match.NewMap();
        foreach (int slot in host.PlayerSlots)
        {
            if (host.Match.Players.ContainsKey(slot))
            {
                host.Respawn(slot);
            }
        }

        Flow.Enter(MatchPhase.Live, tick, Settings.TimeLimitMinutes * 60);
    }

    // A team scores its kills of the other, and loses a point for each of its own.
    private void ScoreTeams(IModeHost host, IReadOnlyList<(int Victim, int Killer)> counted)
    {
        if (!teams)
        {
            return;
        }

        foreach ((int victim, int killer) in counted)
        {
            if (killer == victim
                || !host.Match.Players.TryGetValue(killer, out MatchPlayer? shooter)
                || !host.Match.Players.TryGetValue(victim, out MatchPlayer? dead))
            {
                continue;
            }

            host.Match.AddTeamScore(shooter.Team, shooter.Team == dead.Team ? -1 : 1);
        }
    }

    private void RespawnTheDead(IModeHost host, long tick)
    {
        double seconds = (double)tick / tickRate;
        foreach (int slot in host.PlayerSlots)
        {
            if (host.Match.Players.TryGetValue(slot, out MatchPlayer? player) && player.DiedAt is { } died && seconds - died >= Settings.RespawnSeconds)
            {
                host.Respawn(slot);
            }
        }
    }

    // Who won, once someone has: the limit reached, or the leader when the clock runs out (-1 for a draw); null while open.
    private int? Decided(IModeHost host, long tick)
    {
        (int leader, int best, bool tied) = teams ? LeadingTeam(host.Match) : LeadingPlayer(host.Match);
        if (Settings.ScoreLimit > 0 && best >= Settings.ScoreLimit && !tied)
        {
            return leader;
        }

        return Flow.IsDue(tick) ? (tied ? -1 : leader) : null;
    }

    private static (int Leader, int Best, bool Tied) LeadingTeam(Match match)
    {
        int first = match.ScoreOf(0);
        int second = match.ScoreOf(1);
        return first == second ? (-1, first, true) : first > second ? (0, first, false) : (1, second, false);
    }

    private static (int Leader, int Best, bool Tied) LeadingPlayer(Match match)
    {
        int leader = -1;
        int best = int.MinValue;
        bool tied = true;
        foreach ((int slot, MatchPlayer player) in match.Players)
        {
            if (player.Kills > best)
            {
                (leader, best, tied) = (slot, player.Kills, false);
            }
            else if (player.Kills == best)
            {
                tied = true;
            }
        }

        return (leader, Math.Max(best, 0), tied);
    }
}
