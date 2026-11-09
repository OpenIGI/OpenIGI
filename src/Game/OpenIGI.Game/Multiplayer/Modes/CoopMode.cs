namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>Co-op: everyone on one side against the level, a fallen player down until a teammate stands beside it.</summary>
/// <remarks>The match never ends of itself: the mission does, which the host's level decides.</remarks>
/// <param name="tickRate">The world's ticks per second.</param>
public sealed class CoopMode(int tickRate) : IGameMode
{
    /// <inheritdoc/>
    public string Id => GameModes.Coop;

    /// <inheritdoc/>
    public bool IsTeamMode => false;

    /// <inheritdoc/>
    public MatchFlow Flow { get; } = new(tickRate);

    /// <inheritdoc/>
    public void Start(IModeHost host, long tick) => Flow.Enter(MatchPhase.Live, tick, 0);

    /// <inheritdoc/>
    public void Tick(IModeHost host, long tick, IReadOnlyList<WorldDeath> deaths)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(deaths);
        GameModes.CountDeaths(host, tick, deaths);

        // Down until someone comes for them: the world says who was revived, and the match counts them alive.
        foreach (int slot in host.World.TakeRevivals())
        {
            if (host.Match.Players.TryGetValue(slot, out MatchPlayer? revived))
            {
                revived.IsAlive = true;
                revived.DiedAt = null;
            }
        }
    }
}
