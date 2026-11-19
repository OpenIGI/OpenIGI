namespace OpenIGI.Game.Player;

/// <summary>What a player's rounds did to the level's soldiers since a tally was last looked at.</summary>
/// <param name="Hits">How many new hits on soldiers.</param>
/// <param name="Kills">How many soldiers newly killed.</param>
/// <param name="Head">Whether the last of them -- the last kill where there was one, else the last hit -- was to the head.</param>
public readonly record struct ShotTallyNews(int Hits, int Kills, bool Head)
{
    /// <summary>Gets a value indicating whether a soldier was killed.</summary>
    public bool Killed => Kills > 0;
}

/// <summary>
/// Watches players' tallies (<see cref="ShotTally"/>) for their hits and kills on the level's soldiers, telling each only once:
/// a session's world reporting them to its players, and a single player's own kill feed, read the same counts the same way.
/// </summary>
/// <remarks>A tally seen for the first time is taken as it stands, so what was counted before it was watched is never told.</remarks>
public sealed class ShotTallyWatch
{
    private readonly Dictionary<ShotTally, (int Hits, int Kills)> _seen = new(ReferenceEqualityComparer.Instance);

    /// <summary>Takes what a tally has counted on soldiers since it was last taken.</summary>
    /// <param name="tally">The tally.</param>
    /// <returns>The news, or <see langword="null"/> when there is none.</returns>
    public ShotTallyNews? Take(ShotTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        (int hits, int kills) = _seen.GetValueOrDefault(tally, (tally.SoldierHits, tally.SoldierKills));
        _seen[tally] = (tally.SoldierHits, tally.SoldierKills);
        int newHits = tally.SoldierHits - hits;
        int newKills = tally.SoldierKills - kills;
        if (newHits <= 0 && newKills <= 0)
        {
            return null;
        }

        return new ShotTallyNews(Math.Max(0, newHits), Math.Max(0, newKills), newKills > 0 ? tally.LastKillHead : tally.LastHitHead);
    }
}
