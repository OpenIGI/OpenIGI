namespace OpenIGI.Game.Player;

/// <summary>
/// One row of a human class's hit-zone table.
/// </summary>
/// <param name="Multiplier">
/// What a hit in this zone multiplies the weapon's damage factor by.
/// </param>
/// <param name="BoneIndex">
/// The bone this zone hangs off. Constant within each limb, so the three zones of an arm share one.
/// </param>
/// <param name="ReactionA">The first of two per-zone ids, believed to be reaction or death animations.</param>
/// <param name="ReactionB">The second. Equal to <paramref name="ReactionA"/> for the last three zones.</param>
public readonly record struct HumanHitZone(float Multiplier, int BoneIndex, int ReactionA, int ReactionB);

/// <summary>
/// The per-class hit-zone tables, and what they say about how much a hit hurts.
/// </summary>
/// <remarks>
/// <para>
/// <c>0x489C30</c> is what fills a human's damage state, and every derived class calls it from its
/// constructor with a table of its own: <c>HumanPlayer</c> passes <c>0x5389F8</c>, the soldiers
/// <c>0x53F1D0</c>. It sets the maximum health to <see cref="MaximumHealth"/>, stores the table
/// pointer, and then <c>0x489E30</c> copies one multiplier per zone into the human's own array —
/// <b>seventeen iterations</b>, which is the third independent confirmation of the zone count.
/// </para>
/// <para>
/// <b>Maximum health is 1.0</b>, so a zone multiplier is literally the fraction of a life one point of
/// damage factor takes. A Glock's 0.25 into a torso worth 1.0 is a quarter of a human, which is why it
/// takes four.
/// </para>
/// </remarks>
public static class HumanHitZoneTable
{
    /// <summary>Every human's maximum health.</summary>
    /// <remarks>
    /// Literally <c>1.0</c>, set by <c>0x489C30</c> for every class alike. Humans do not differ in
    /// toughness — they differ only in their zone multipliers, and in retail those are identical too.
    /// </remarks>
    public const float MaximumHealth = 1f;

    /// <summary>The multiplier that makes a zone a one-hit kill.</summary>
    /// <remarks>
    /// <c>100.0</c> against a maximum of 1.0, so any weapon in the game with a damage factor above
    /// 0.01 kills instantly there. Both of the zones carrying it are head-and-neck ones.
    /// </remarks>
    public const float LethalMultiplier = 100f;

    /// <summary>The player's table, at <c>0x5389F8</c>.</summary>
    /// <remarks>
    /// The table at <c>0x538B08</c> is byte-for-byte identical to this one — the two 17-row tables that
    /// sat unexplained since M8 are both hit-zone tables, and they hold the same data.
    /// </remarks>
    public static IReadOnlyList<HumanHitZone> Player { get; } =
    [
        new(0.5f, 93, 201, 202),
        new(0.75f, 93, 201, 202),
        new(1.0f, 93, 201, 202),
        new(0.5f, 97, 201, 202),
        new(0.75f, 97, 201, 202),
        new(1.0f, 97, 201, 202),
        new(0.5f, 95, 201, 202),
        new(0.75f, 95, 201, 202),
        new(1.0f, 95, 201, 202),
        new(0.5f, 96, 201, 202),
        new(0.75f, 96, 201, 202),
        new(1.0f, 96, 201, 202),
        new(1.0f, 63, 201, 202),
        new(1.0f, 63, 201, 202),
        new(1.0f, 63, 215, 215),
        new(LethalMultiplier, 63, 112, 112),
        new(LethalMultiplier, 63, 204, 204),
    ];

    /// <summary>The soldiers' table, at <c>0x53F1D0</c>.</summary>
    /// <remarks>
    /// The multipliers are <b>identical</b> to the player's; only the bone indices and the reaction ids
    /// differ, because the soldier skeleton is not the player's. So a soldier is exactly as tough as the
    /// player and takes damage in exactly the same proportions.
    /// </remarks>
    public static IReadOnlyList<HumanHitZone> Soldier { get; } =
    [
        new(0.5f, 44, 23, 23),
        new(0.75f, 44, 23, 23),
        new(1.0f, 44, 23, 23),
        new(0.5f, 47, 28, 28),
        new(0.75f, 47, 28, 28),
        new(1.0f, 47, 28, 28),
        new(0.5f, 45, 23, 23),
        new(0.75f, 45, 23, 23),
        new(1.0f, 45, 23, 23),
        new(0.5f, 46, 28, 28),
        new(0.75f, 46, 28, 28),
        new(1.0f, 46, 28, 28),
        new(1.0f, 27, 28, 28),
        new(1.0f, 27, 28, 28),
        new(1.0f, 27, 29, 29),
        new(LethalMultiplier, 27, 30, 30),
        new(LethalMultiplier, 27, 22, 22),
    ];

    /// <summary>How many hits of a given weapon a zone takes to kill.</summary>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>.</param>
    /// <param name="zoneMultiplier">The struck zone's multiplier.</param>
    /// <returns>The number of hits, or <see cref="int.MaxValue"/> when a hit does nothing at all.</returns>
    /// <remarks>
    /// Death is at <c>accumulated &gt;= maximum</c>, so this is a ceiling rather than a rounding.
    /// </remarks>
    public static int HitsToKill(float damageFactor, float zoneMultiplier)
    {
        float perHit = damageFactor * zoneMultiplier;
        if (perHit <= 0f)
        {
            return int.MaxValue;
        }

        return (int)MathF.Ceiling(MaximumHealth / perHit);
    }
}
