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
    /// <param name="maximumHealth">
    /// How much damage kills. Defaults to <see cref="HumanHitZoneTable.MaximumHealth"/>, which is what
    /// <c>0x489C30</c> gives every class — but a soldier with a brain has it <b>overwritten</b> by its
    /// settings row's "Damage scale" when <c>sub_406F70</c> configures the AI, so a guard is worth 0.3
    /// on easy and 0.8 on hard.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="zones"/> is <see langword="null"/>.</exception>
    public HumanHealth(IReadOnlyList<HumanHitZone> zones, float maximumHealth = HumanHitZoneTable.MaximumHealth)
    {
        ArgumentNullException.ThrowIfNull(zones);
        Zones = zones;
        MaximumHealth = maximumHealth;
    }

    /// <summary>Gets the table this human's zones are read from.</summary>
    public IReadOnlyList<HumanHitZone> Zones { get; }

    /// <summary>Gets how much damage kills this human — <c>Human+600</c>.</summary>
    public float MaximumHealth { get; }

    /// <summary>Gets how much damage has been taken, out of a whole person.</summary>
    public float Damage { get; private set; }

    /// <summary>Gets whether this human is dead.</summary>
    public bool IsDead => HumanDamage.IsDead(Damage, MaximumHealth);

    /// <summary>Gets how much of the human is left, from one down to zero.</summary>
    public float RemainingFraction =>
        HumanDamage.RemainingFraction(Damage, MaximumHealth);

    /// <summary>Gets how many hits have landed, for diagnostics.</summary>
    public int Hits { get; private set; }

    /// <summary>Applies one round.</summary>
    /// <param name="zone">The zone struck, from <see cref="HumanHitTest"/>.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>, which the round carries.</param>
    /// <param name="instantDeathDisabled">
    /// Whether the head/neck one-shot is disabled on this human — <c>HumanAI+15037</c>, set by
    /// <c>AIFunction_SetInstantDeath(FALSE)</c>. When set, a hit on the head (<see cref="HumanDamage.ProximityZone"/>)
    /// or neck (<see cref="HumanDamage.NeckZone"/>) takes the weapon's plain damage factor instead of the
    /// zone's lethal multiplier — the branch <c>0x45D3C0</c> takes on <c>v13</c>. Defaults to
    /// <see langword="false"/>, so by default those zones keep their ×100 multiplier and one-shot.
    /// </param>
    /// <returns>The damage this hit added.</returns>
    /// <remarks>
    /// A hit on a zone the table does not cover adds nothing rather than throwing — the original indexes
    /// its table without a bound check, and reproducing that read is not worth it.
    /// </remarks>
    public float Apply(int zone, float damageFactor, bool instantDeathDisabled = false)
    {
        if (zone < 0 || zone >= Zones.Count)
        {
            return 0f;
        }

        bool headOrNeck = zone is HumanDamage.ProximityZone or HumanDamage.NeckZone;
        float dealt = instantDeathDisabled && headOrNeck
            ? damageFactor
            : HumanDamage.DamageOfHit(Zones[zone].Multiplier, damageFactor);
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
