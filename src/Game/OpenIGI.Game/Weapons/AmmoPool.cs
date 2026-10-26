namespace OpenIGI.Game.Weapons;

/// <summary>
/// What a human is carrying loose, by ammunition type — the list a reload draws from.
/// </summary>
/// <remarks>
/// <para>
/// The original hangs this off the human as a list <c>0x47CEE0</c> searches by <c>Ammo ID</c>, with
/// each entry a <c>{ record, count }</c> pair whose count sits at <c>+4</c>. A gun reaches it through
/// its owner: <c>0x479AA0</c> asks the owner for the list, finds the entry matching its own ammunition
/// record, and reports the count as the reserve.
/// </para>
/// <para>
/// The lookup key is the <b><see cref="AmmoType.AmmoId"/></b>, which the ammunition table also uses as
/// the inventory slot — <c>0x46DC40</c> indexes with it, and the shipped ids run 0 to 13 with no gaps.
/// </para>
/// </remarks>
public sealed class AmmoPool
{
    private readonly Dictionary<int, int> _counts = [];
    private readonly Dictionary<int, int> _limits = [];

    /// <summary>Gets how many rounds of a type are carried.</summary>
    /// <param name="ammoId">The type's <see cref="AmmoType.AmmoId"/>.</param>
    /// <returns>The count, or zero when none is carried.</returns>
    /// <remarks>
    /// A type that has never been added reads as zero rather than as absent, which is what
    /// <c>0x479AA0</c> reports when the owner's list has no matching entry.
    /// </remarks>
    public int Count(int ammoId) => _counts.TryGetValue(ammoId, out int count) ? count : 0;

    /// <summary>Adds rounds of a type.</summary>
    /// <param name="ammoId">The type's <see cref="AmmoType.AmmoId"/>.</param>
    /// <param name="rounds">How many to add; a negative count removes.</param>
    /// <remarks>
    /// The total is floored at zero, as the transfer in <c>0x4798C0</c> keeps it, and held to the type's limit when it has
    /// one (<c>0x47CE00</c>).
    /// </remarks>
    public void Add(int ammoId, int rounds)
    {
        int total = Count(ammoId) + rounds;
        _counts[ammoId] = Limited(ammoId, total < 0 ? 0 : total);
    }

    /// <summary>Caps how many rounds of a type may be carried, cutting down what is carried now (<c>0x47CE70</c>).</summary>
    /// <param name="ammoId">The type's <see cref="AmmoType.AmmoId"/>.</param>
    /// <param name="limit">The most that may be carried, or <c>-1</c> for no limit.</param>
    public void SetLimit(int ammoId, int limit)
    {
        if (limit == -1)
        {
            _limits.Remove(ammoId);
            return;
        }

        _limits[ammoId] = limit;
        if (_counts.TryGetValue(ammoId, out int count))
        {
            _counts[ammoId] = Limited(ammoId, count < 0 ? 0 : count);
        }
    }

    /// <summary>Gets the most rounds of a type that may be carried, or <c>-1</c> when there is no limit.</summary>
    /// <param name="ammoId">The type's <see cref="AmmoType.AmmoId"/>.</param>
    public int Limit(int ammoId) => _limits.TryGetValue(ammoId, out int limit) ? limit : -1;

    private int Limited(int ammoId, int count) =>
        _limits.TryGetValue(ammoId, out int limit) && count > limit ? limit : count;

    /// <summary>Takes up to a number of rounds out.</summary>
    /// <param name="ammoId">The type's <see cref="AmmoType.AmmoId"/>.</param>
    /// <param name="wanted">How many are asked for.</param>
    /// <returns>How many were actually available, which may be fewer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="wanted"/> is negative.</exception>
    /// <remarks>
    /// The partial case is the one that matters: <c>0x4798C0</c> takes whatever is left when the pool
    /// cannot fill the magazine, rather than refusing the reload.
    /// </remarks>
    public int Take(int ammoId, int wanted)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wanted);

        int held = Count(ammoId);
        int taken = held < wanted ? held : wanted;
        _counts[ammoId] = held - taken;
        return taken;
    }

    /// <summary>Gets every type carried, with its count.</summary>
    public IReadOnlyDictionary<int, int> Contents => _counts;
}
