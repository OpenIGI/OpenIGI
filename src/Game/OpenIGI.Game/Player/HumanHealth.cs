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
        BaseMaximumHealth = maximumHealth;
        MaximumHealth = maximumHealth;
    }

    /// <summary>Gets the table this human's zones are read from.</summary>
    public IReadOnlyList<HumanHitZone> Zones { get; }

    /// <summary>Gets the maximum this human was built with, before any cheat scaled it.</summary>
    public float BaseMaximumHealth { get; }

    /// <summary>Gets how much damage kills this human — <c>Human+600</c>.</summary>
    /// <remarks>
    /// <see cref="BaseMaximumHealth"/> unless the player's health-multiplier cheat has rescaled it.
    /// </remarks>
    public float MaximumHealth { get; private set; }

    /// <summary>Changes the maximum health, keeping the fraction of health that remains.</summary>
    /// <param name="maximumHealth">The new maximum, in the same units as the constructor's.</param>
    /// <remarks>
    /// The health-multiplier cheat can be moved mid-mission. Keeping the fraction rather than the raw
    /// damage means a player at two thirds stays at two thirds, and setting the multiplier back to 1
    /// returns exactly the health they would have had without it.
    /// </remarks>
    public void RescaleMaximumHealth(float maximumHealth)
    {
        if (maximumHealth <= 0f || MaximumHealth == maximumHealth)
        {
            return;
        }

        float remaining = RemainingFraction;
        MaximumHealth = maximumHealth;
        Damage = Math.Clamp(maximumHealth * (1f - remaining), 0f, maximumHealth);
    }

    /// <summary>
    /// Gets the scale every hit on this human is multiplied by — the <b>player's difficulty scale</b>.
    /// </summary>
    /// <remarks>
    /// The player's own hit handler (<c>0x412440</c>, HumanPlayer's override of the hit message — not
    /// the base <c>0x45F930</c> and not the soldiers' <c>0x45D3C0</c>) multiplies
    /// <c>zoneMultiplier × damageFactor</c> by the active profile's difficulty: <b>×0.75 on GD_1
    /// (Easy), ×1.0 on GD_2 (Normal), ×1.25 on GD_3 (Hard)</b> — the branch even logs
    /// <c>"Difficulty level not defined %d"</c> for anything else. Soldiers take unscaled damage, so
    /// this defaults to 1 and is set only on the player's instance.
    /// </remarks>
    public float DamageScale { get; init; } = 1f;

    /// <summary>Gets or sets whether every hit is ignored — the infinite-health cheat.</summary>
    /// <remarks>Not the AI's <c>SetInvulnerability</c>, which the soldiers keep on their own brain.</remarks>
    public bool Invulnerable { get; set; }

    /// <summary>The player's difficulty damage scale — <c>0x412440</c>'s three-way branch.</summary>
    /// <param name="difficulty">The active profile's difficulty, 0..2.</param>
    /// <returns>×0.75, ×1.0 or ×1.25.</returns>
    public static float DifficultyDamageScale(int difficulty) => difficulty switch
    {
        0 => 0.75f,
        2 => 1.25f,
        _ => 1f,
    };

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
    /// zone's lethal multiplier — the branch <c>0x45D3C0</c> takes on this flag. Defaults to
    /// <see langword="false"/>, so by default those zones keep their ×100 multiplier and one-shot.
    /// </param>
    /// <returns>The damage this hit added.</returns>
    /// <remarks>
    /// A hit on a zone the table does not cover adds nothing rather than throwing — the original indexes
    /// its table without a bound check, and reproducing that read is not worth it.
    /// </remarks>
    public float Apply(int zone, float damageFactor, bool instantDeathDisabled = false)
    {
        if (Invulnerable)
        {
            return 0f;
        }

        if (zone < 0 || zone >= Zones.Count)
        {
            return 0f;
        }

        bool headOrNeck = zone is HumanDamage.ProximityZone or HumanDamage.NeckZone;
        float dealt = instantDeathDisabled && headOrNeck
            ? damageFactor
            : HumanDamage.DamageOfHit(Zones[zone].Multiplier, damageFactor);
        dealt *= DamageScale;
        Damage += dealt;
        Hits++;
        return dealt;
    }

    /// <summary>Adds damage that is already expressed in health units.</summary>
    /// <param name="zone">The accumulator bucket to add it to.</param>
    /// <param name="damage">The amount to add, without a zone or difficulty multiplier.</param>
    /// <returns>The amount actually added.</returns>
    /// <remarks>
    /// Falling uses this path. <c>0x408460</c> selects zone 13 for bookkeeping, then adds its computed
    /// amount straight to the total; player difficulty does not scale it. This compact health model
    /// retains the selected zone validation but, unlike the retail object, stores only the total.
    /// </remarks>
    public float ApplyDirect(int zone, float damage)
    {
        if (Invulnerable)
        {
            return 0f;
        }

        if (zone < 0 || zone >= Zones.Count || damage <= 0f)
        {
            return 0f;
        }

        Damage += damage;
        Hits++;
        return damage;
    }

    /// <summary>Brings the human back to untouched, as respawning does.</summary>
    public void Reset()
    {
        Damage = 0f;
        Hits = 0;
    }

    /// <summary>Restores a fraction of this human's maximum health.</summary>
    /// <param name="fraction">The fraction of <see cref="MaximumHealth"/> to restore.</param>
    /// <returns>The damage removed from the accumulator.</returns>
    public float HealFraction(float fraction)
    {
        if (fraction <= 0f || Damage <= 0f)
        {
            return 0f;
        }

        float before = Damage;
        Damage = Math.Max(0f, Damage - (MaximumHealth * fraction));
        return before - Damage;
    }
}
