namespace OpenIGI.Game.Player;

/// <summary>
/// One row of a human class's hit-zone table.
/// </summary>
/// <param name="Multiplier">
/// What a hit in this zone multiplies the weapon's damage factor by.
/// </param>
/// <param name="LimbAnimation">
/// The animation this body part plays. Constant within each limb, so the three zones of an arm share
/// one.
/// </param>
/// <param name="ReactionA">The first of two per-zone animations.</param>
/// <param name="ReactionB">The second. Equal to <paramref name="ReactionA"/> for the last three zones.</param>
/// <remarks>
/// <b>All three are animation ids</b>, not bone indices — verified against the retail sets: every one of
/// the player table's five distinct values resolves in <c>000.iff</c> and none in a soldier set, and
/// the soldier table's five resolve in the soldier sets and not the player's. They cannot be bone
/// indices because the skeletons carry 33 bones and the values run to 97.
/// </remarks>
public readonly record struct HumanHitZone(float Multiplier, int LimbAnimation, int ReactionA, int ReactionB);

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
/// <para>
/// <b>The player's multipliers are script data, not compiled constants.</b> <c>DefineHumanPlayer</c>
/// (<c>0x410070</c>) writes <c>humanplayer.qvm</c>'s arguments 9–25 over the player's table and 26–42
/// over a second one. The values compiled into the executable are a <i>stale initialiser</i> the script
/// overwrites — reading them instead of the script gives the player a headshot multiplier it does not
/// have.
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
    /// 0.01 kills instantly there. Both of the zones carrying it are head-and-neck ones — and only on
    /// the <b>soldier</b> table; the player's head is worth 1.0.
    /// </remarks>
    public const float LethalMultiplier = 100f;

    /// <summary>The player's table, at <c>0x5389F8</c>, with the multipliers the script writes.</summary>
    /// <remarks>
    /// <para>
    /// <b>The player has no headshot bonus.</b> Its head and neck are worth <c>1.0</c>, the same as its
    /// torso — so nothing one-shots the player through the head, which is the opposite of what the
    /// executable's stale initialiser (100.0 there) suggests.
    /// </para>
    /// <para>
    /// The animation ids are <i>not</i> overwritten by the script, only the multipliers, so those are
    /// the compiled values.
    /// </para>
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
        new(1.0f, 63, 112, 112),
        new(1.0f, 63, 204, 204),
    ];

    /// <summary>
    /// The second table the script fills, at <c>0x538B08</c> — <b>written and never read</b>.
    /// </summary>
    /// <remarks>
    /// <c>DefineHumanPlayer</c> writes arguments 26–42 here and nothing in the executable ever reads
    /// it. Its head and neck carry <c>100000</c>, a thousand times the soldier table's already-lethal
    /// 100, which suggests it was an alternative the game stopped using. Kept because a reader
    /// comparing the two tables should know one of them is dead rather than assume they are variants
    /// in use.
    /// </remarks>
    public static IReadOnlyList<float> UnusedSecondTable { get; } =
    [
        0.5f, 0.75f, 1f, 0.5f, 0.75f, 1f, 0.5f, 0.75f, 1f, 0.5f, 0.75f, 1f, 1f, 1f, 1f, 100000f, 100000f,
    ];

    /// <summary>The soldiers' table, at <c>0x53F1D0</c> — compiled, and not overwritten.</summary>
    /// <remarks>
    /// <b>A soldier is not as tough as the player.</b> Limbs and torso match, but a soldier's head and
    /// neck carry <see cref="LethalMultiplier"/> where the player's carry 1.0 — so a headshot kills an
    /// AI outright and does nothing special to the player. <c>DefineHumanPlayer</c> never touches this
    /// table, so unlike the player's these really are the compiled values.
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

    /// <summary>The female soldiers' table, at <c>0x53E508</c>.</summary>
    /// <remarks>
    /// The soldier's multipliers with one set of clips for every zone: limb animation 6, reactions 13.
    /// A female soldier's death plays the struck zone's first reaction (<c>0x4546C0</c>), so she has one
    /// death clip wherever she is hit.
    /// </remarks>
    public static IReadOnlyList<HumanHitZone> FemaleSoldier { get; } =
        WithAnimations(static _ => (6, 13));

    /// <summary>The RPG soldiers' table, at <c>0x53E798</c>.</summary>
    /// <remarks>
    /// The soldier's multipliers with the RPG set's clips: limb animation 13 everywhere, and reaction 15
    /// except zone 14 (16), the head (18) and the neck (12).
    /// </remarks>
    public static IReadOnlyList<HumanHitZone> RpgSoldier { get; } =
        WithAnimations(static zone => (13, zone switch { 14 => 16, 15 => 18, 16 => 12, _ => 15 }));

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

    /// <summary>A table with the soldier's multipliers and a class's own clips.</summary>
    /// <remarks>
    /// Every soldier class's table carries the same seventeen multipliers; only the animation ids differ,
    /// and within a zone its two reactions are equal.
    /// </remarks>
    private static HumanHitZone[] WithAnimations(Func<int, (int Limb, int Reaction)> clips)
    {
        var table = new HumanHitZone[Soldier.Count];
        for (int zone = 0; zone < table.Length; zone++)
        {
            (int limb, int reaction) = clips(zone);
            table[zone] = new HumanHitZone(Soldier[zone].Multiplier, limb, reaction, reaction);
        }

        return table;
    }
}
