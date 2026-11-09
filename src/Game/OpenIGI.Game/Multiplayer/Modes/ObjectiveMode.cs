namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// I.G.I. 2's objective game: two teams take the map's objectives in order, a mission each, a dead player back once it can
/// pay for it; the map's mission won scores the round.
/// </summary>
/// <param name="tickRate">The world's ticks per second.</param>
/// <param name="respawnDelay">The seconds a dead player waits before it may buy its way back.</param>
public sealed class ObjectiveMode(int tickRate, double respawnDelay = 3) : IGameMode
{
    /// <inheritdoc/>
    public string Id => GameModes.Objective;

    /// <inheritdoc/>
    public bool IsTeamMode => true;

    /// <inheritdoc/>
    public MatchFlow Flow { get; } = new(tickRate);

    /// <summary>Gets the round of objectives being played, once the map has any.</summary>
    public ObjectiveRound? Round { get; private set; }

    /// <summary>Gets or sets the seconds a dead player waits before it may buy its way back.</summary>
    public double RespawnDelay { get; set; } = respawnDelay;

    /// <inheritdoc/>
    public void Start(IModeHost host, long tick) => Flow.Enter(MatchPhase.Live, tick, 0);

    /// <inheritdoc/>
    public void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(deaths);
        RunRound(host, tick);
        GameModes.CountDeaths(host, tick, deaths);
        double seconds = (double)tick / tickRate;
        foreach (int slot in host.PlayerSlots)
        {
            if (host.Match.Players.TryGetValue(slot, out MatchPlayer? player) && player.DiedAt is { } died
                && seconds - died >= RespawnDelay && host.Match.TrySpawn(slot, seconds))
            {
                host.World.Respawn(slot);
            }
        }
    }

    // The map's objectives, taken in order; a new round puts every player back at its team's spawn.
    private void RunRound(IModeHost host, long tick)
    {
        if (Round is null)
        {
            if (host.World.RoundObjectives.Count == 0)
            {
                return;
            }

            Round = new ObjectiveRound(host.World.RoundObjectives, host.Match, tickRate);
            Round.Start(tick);
            host.World.CurrentObjective = Round.Current;
            return;
        }

        RoundEvent happened = Round.Tick(tick);
        host.World.CurrentObjective = Round.Current;
        if (happened == RoundEvent.Restart)
        {
            foreach (int slot in host.ClientSlots)
            {
                host.Respawn(slot);
            }
        }
    }
}
