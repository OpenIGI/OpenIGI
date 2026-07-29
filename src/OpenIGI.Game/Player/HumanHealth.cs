namespace OpenIGI.Game.Player;

/// <summary>
/// One human's accumulated damage, and whether it has killed them.
/// </summary>
/// <remarks>
/// <para>
/// The original counts <b>damage taken</b> rather than health remaining: a human dies when the total
/// reaches <see cref="HumanHitZoneTable.MaximumHealth"/>, which is <b>1.0</b> for everybody. So a
/// weapon's <c>Damage Factor</c> and a zone's multiplier are both fractions of a whole person, and the
/// arithmetic is a plain sum with nothing scaling it by range or by what the round has already been
/// through.
/// </para>
/// <para>
/// The lethal multiplier is what makes a head shot a head shot: zone 15's row carries
/// <see cref="HumanHitZoneTable.LethalMultiplier"/> = 100, so any weapon at all reaches 1.0 in one hit.
/// </para>
/// </remarks>
public sealed class HumanHealth
{
    /// <summary>Initializes a new instance of the <see cref="HumanHealth"/> class.</summary>
    /// <param name="zones">
    /// The human's own hit-zone table — <see cref="HumanHitZoneTable.Player"/> or
    /// <see cref="HumanHitZoneTable.Soldier"/>. They are byte-for-byte identical in the retail data,
    /// but which one applies is a property of the human, so it is asked for rather than assumed.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="zones"/> is <see langword="null"/>.</exception>
    public HumanHealth(IReadOnlyList<HumanHitZone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        Zones = zones;
    }

    /// <summary>Gets the table this human's zones are read from.</summary>
    public IReadOnlyList<HumanHitZone> Zones { get; }

    /// <summary>Gets how much damage has been taken, out of a whole person.</summary>
    public float Damage { get; private set; }

    /// <summary>Gets whether this human is dead.</summary>
    public bool IsDead => HumanDamage.IsDead(Damage, HumanHitZoneTable.MaximumHealth);

    /// <summary>Gets how much of the human is left, from one down to zero.</summary>
    public float RemainingFraction =>
        HumanDamage.RemainingFraction(Damage, HumanHitZoneTable.MaximumHealth);

    /// <summary>Gets how many hits have landed, for diagnostics.</summary>
    public int Hits { get; private set; }

    /// <summary>Applies one round.</summary>
    /// <param name="zone">The zone struck, from <see cref="HumanHitTest"/>.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>, which the round carries.</param>
    /// <returns>The damage this hit added.</returns>
    /// <remarks>
    /// A hit on a zone the table does not cover adds nothing rather than throwing — the original indexes
    /// its table without a bound check, and reproducing that read is not worth it.
    /// </remarks>
    public float Apply(int zone, float damageFactor)
    {
        if (zone < 0 || zone >= Zones.Count)
        {
            return 0f;
        }

        float dealt = HumanDamage.DamageOfHit(Zones[zone].Multiplier, damageFactor);
        Damage += dealt;
        Hits++;
        return dealt;
    }

    /// <summary>Brings the human back to untouched, as respawning does.</summary>
    public void Reset()
    {
        Damage = 0f;
        Hits = 0;
    }
}
