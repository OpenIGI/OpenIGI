namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>How a round-based match is played: rounds to win, how long a round lasts, and the pauses between.</summary>
public sealed record RoundSettings
{
    /// <summary>Gets the rounds a team must win to win the match.</summary>
    public int RoundsToWin { get; init; } = 6;

    /// <summary>Gets the seconds a round lasts before its clock decides it.</summary>
    public double RoundSeconds { get; init; } = 120;

    /// <summary>Gets the seconds a decided round's result is shown before the next begins.</summary>
    public double RoundOverSeconds { get; init; } = 5;

    /// <summary>Gets the seconds the scores are shown once the match is decided.</summary>
    public double IntermissionSeconds { get; init; } = 15;
}

/// <summary>
/// A match of rounds between two teams, as the tactical shooters play them: every player stands at its team's spawns when a
/// round begins and has one life in it; the round goes to the team left standing, or as the mode says when its clock runs
/// out; the first team to <see cref="RoundSettings.RoundsToWin"/> rounds wins the match.
/// </summary>
/// <remarks>
/// A round is decided only while both teams have a player: one player alone on the map plays on, as in a warmup, until an
/// opponent joins. A player joining during a round waits, dead, for the next (Source's round rules); the dead watch on.
/// </remarks>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="settings">How it is played, or the defaults.</param>
public abstract class RoundMode(int tickRate, RoundSettings? settings) : IGameMode
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public bool IsTeamMode => true;

    /// <inheritdoc/>
    public MatchFlow Flow { get; } = new(tickRate);

    /// <summary>Gets how it is played.</summary>
    public RoundSettings Settings { get; } = settings ?? new RoundSettings();

    /// <summary>Gets what keeps the players inside the map's bounds.</summary>
    public OutOfBounds Bounds { get; } = new();

    /// <summary>Gets a value indicating whether the match was decided and its scores shown, and the map is not played again.</summary>
    public bool IsMapOver { get; private set; }

    /// <summary>Gets how many matches were played to the end on this map.</summary>
    public int MatchesPlayed { get; private set; }

    /// <summary>Gets or sets a value indicating whether the same map is played again after the intermission.</summary>
    public bool PlaysAgain { get; set; } = true;

    /// <summary>Gets the world's ticks per second.</summary>
    protected int TickRate => tickRate;

    /// <inheritdoc/>
    public int TeamFor(IModeHost host, int slot) => GameModes.FairTeam(host, slot);

    /// <inheritdoc/>
    public virtual string Status(long tick) => string.Empty;

    /// <inheritdoc/>
    public virtual ModeReport Report(long tick) => new()
    {
        Target = Settings.RoundsToWin,
        Reason = Flow.Phase is MatchPhase.RoundOver or MatchPhase.Intermission ? EndReason : RoundEnd.None,
    };

    /// <summary>Gets or sets why the round was decided, which <see cref="RoundWinner"/> says as it decides it.</summary>
    protected RoundEnd EndReason { get; set; }

    /// <inheritdoc/>
    public void Start(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Match.IsFreeForAll = false;
        host.Match.NewMap();
        BeginRound(host, tick);
    }

    /// <inheritdoc/>
    public void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        GameModes.CountDeaths(host, tick, deaths);
        switch (Flow.Phase)
        {
            case MatchPhase.Live:
                Bounds.Tick(host, tick);
                TickRound(host, tick);
                if (RoundWinner(host, tick) is { } winner)
                {
                    EndRound(host, tick, winner);
                }

                break;
            case MatchPhase.RoundOver when Flow.IsDue(tick):
                if (MatchWinner(host) is { } champion)
                {
                    Flow.Enter(MatchPhase.Intermission, tick, Settings.IntermissionSeconds, champion);
                }
                else
                {
                    BeginRound(host, tick);
                }

                break;
            case MatchPhase.Intermission when Flow.IsDue(tick):
                MatchesPlayed++;
                if (PlaysAgain)
                {
                    host.Match.NewMap();
                    BeginRound(host, tick);
                }
                else
                {
                    IsMapOver = true;
                }

                break;
        }
    }

    /// <summary>Gets how many of a team's players are alive now.</summary>
    /// <param name="host">The session.</param>
    /// <param name="team">The team.</param>
    /// <returns>The count.</returns>
    protected static int AliveOn(IModeHost host, int team)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Match.Players.Values.Count(p => p.Team == team && p.IsAlive);
    }

    /// <summary>Gets how many players a team has.</summary>
    /// <param name="host">The session.</param>
    /// <param name="team">The team.</param>
    /// <returns>The count.</returns>
    protected static int PlayersOn(IModeHost host, int team)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Match.Players.Values.Count(p => p.Team == team);
    }

    /// <summary>Runs the mode's own part of a round in play (a bomb's plant, its fuse); nothing by default.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    protected virtual void TickRound(IModeHost host, long tick)
    {
    }

    /// <summary>Gets who the round goes to when its clock runs out with both teams standing.</summary>
    /// <param name="host">The session.</param>
    /// <returns>The team, or -1 for a draw.</returns>
    protected abstract int TimeoutWinner(IModeHost host);

    /// <summary>Gets who the round has gone to by now, or <see langword="null"/> while it is open.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    /// <returns>The winning team, -1 for a draw, or <see langword="null"/>.</returns>
    /// <remarks>The team left standing wins; both down together, a draw; the clock out, <see cref="TimeoutWinner"/>.</remarks>
    protected virtual int? RoundWinner(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (PlayersOn(host, TeamRoster.Good) == 0 || PlayersOn(host, TeamRoster.Bad) == 0)
        {
            return null;
        }

        int good = AliveOn(host, TeamRoster.Good);
        int bad = AliveOn(host, TeamRoster.Bad);
        if (good == 0 || bad == 0)
        {
            EndReason = RoundEnd.Eliminated;
            return good > 0 ? TeamRoster.Good : bad > 0 ? TeamRoster.Bad : -1;
        }

        if (!Flow.IsDue(tick))
        {
            return null;
        }

        EndReason = RoundEnd.TimeRanOut;
        return TimeoutWinner(host);
    }

    /// <summary>Called as a round begins, everyone standing again: a mode's own reset (the bomb back with the attackers).</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    protected virtual void OnRoundBegun(IModeHost host, long tick)
    {
    }

    private void BeginRound(IModeHost host, long tick)
    {
        foreach (int slot in host.PlayerSlots)
        {
            if (host.Match.Players.ContainsKey(slot))
            {
                host.Respawn(slot);
            }
        }

        Flow.Enter(MatchPhase.Live, tick, Settings.RoundSeconds);
        EndReason = RoundEnd.None;
        OnRoundBegun(host, tick);
    }

    private void EndRound(IModeHost host, long tick, int winner)
    {
        if (winner >= 0)
        {
            host.Match.AddTeamScore(winner, 1);
        }

        Flow.Enter(MatchPhase.RoundOver, tick, Settings.RoundOverSeconds, winner);
    }

    // The team that has won its rounds, once one has.
    private int? MatchWinner(IModeHost host)
    {
        foreach (int team in (int[])[TeamRoster.Good, TeamRoster.Bad])
        {
            if (host.Match.ScoreOf(team) >= Settings.RoundsToWin)
            {
                return team;
            }
        }

        return null;
    }
}

/// <summary>Elimination: rounds of one life each, won by the team left standing; a round the clock ends goes to the team with more alive.</summary>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="settings">How it is played, or the defaults.</param>
public sealed class EliminationMode(int tickRate, RoundSettings? settings = null) : RoundMode(tickRate, settings)
{
    /// <inheritdoc/>
    public override string Id => GameModes.Elimination;

    /// <inheritdoc/>
    protected override int TimeoutWinner(IModeHost host)
    {
        int good = AliveOn(host, TeamRoster.Good);
        int bad = AliveOn(host, TeamRoster.Bad);
        return good == bad ? -1 : good > bad ? TeamRoster.Good : TeamRoster.Bad;
    }
}
