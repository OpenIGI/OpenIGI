using System.Collections;

namespace OpenIGI.Game.Player;

/// <summary>The players standing in a level, as one of the level's systems sees them.</summary>
/// <typeparam name="TPlayer">How that system sees a player.</typeparam>
/// <remarks>
/// <para>
/// Both originals have exactly one player, and their systems each kept a pointer to it. A session of several players
/// keeps them here instead. A single-player level is a roster of one, and every system that reads a roster behaves for
/// it exactly as it did with the pointer: the soldier listening at a jammed door hears the one player, the smoke burns
/// the one player, an area counts the one player.
/// </para>
/// <para>
/// <see cref="Primary"/> is the first player added, the one the original's pointer was: in a single-player level the
/// player, in a session the host's own or the first to join. What has no meaning for several players (the level script's
/// <c>HumanPlayer_*</c> natives) addresses it.
/// </para>
/// </remarks>
public sealed class PlayerRoster<TPlayer> : IReadOnlyList<TPlayer>
    where TPlayer : class
{
    private readonly List<TPlayer> _players = [];

    /// <summary>Gets the first player, or <see langword="null"/> when the level has none.</summary>
    public TPlayer? Primary => _players.Count > 0 ? _players[0] : null;

    /// <inheritdoc/>
    public int Count => _players.Count;

    /// <inheritdoc/>
    public TPlayer this[int index] => _players[index];

    /// <summary>Makes the roster hold one player, or none.</summary>
    /// <param name="player">The level's only player, or <see langword="null"/> for none.</param>
    public void Set(TPlayer? player)
    {
        _players.Clear();
        if (player is not null)
        {
            _players.Add(player);
        }
    }

    /// <summary>Adds a player who has joined, unless already present.</summary>
    /// <param name="player">The player.</param>
    public void Add(TPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!_players.Contains(player))
        {
            _players.Add(player);
        }
    }

    /// <summary>Takes out a player who has left.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> when it was present.</returns>
    public bool Remove(TPlayer player) => _players.Remove(player);

    /// <summary>Takes out every player.</summary>
    public void Clear() => _players.Clear();

    /// <summary>Gets where a player stands in the roster.</summary>
    /// <param name="player">The player.</param>
    /// <returns>Its index, or -1.</returns>
    public int IndexOf(TPlayer? player) => player is null ? -1 : _players.IndexOf(player);

    /// <summary>Gets the player a measure ranks lowest, among those it does not rule out.</summary>
    /// <param name="measure">The rank of a player, or <see langword="null"/> to pass it over.</param>
    /// <returns>The lowest-ranked player, the earliest added on a tie, or <see langword="null"/>.</returns>
    public TPlayer? Nearest(Func<TPlayer, double?> measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        TPlayer? best = null;
        double bestRank = double.PositiveInfinity;
        foreach (TPlayer player in _players)
        {
            if (measure(player) is { } rank && rank < bestRank)
            {
                best = player;
                bestRank = rank;
            }
        }

        return best;
    }

    /// <summary>Gets the players in the order they were added.</summary>
    /// <returns>An enumerator that allocates nothing.</returns>
    public List<TPlayer>.Enumerator GetEnumerator() => _players.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<TPlayer> IEnumerable<TPlayer>.GetEnumerator() => _players.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => _players.GetEnumerator();
}
