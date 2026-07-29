namespace OpenIGI.Game.Player;

/// <summary>
/// How a hit becomes damage, and how damage becomes death.
/// </summary>
/// <remarks>
/// <para>
/// From the human's hit handler (<c>0x45F930</c>), which is what the bullet resolution dispatches with
/// the shot's <c>Damage Factor</c>. The whole model is four lines:
/// </para>
/// <code>
/// zone    = ZoneOf(hit);
/// damage  = zoneMultiplier[zone] * damageFactor;
/// perZone[zone] += damage;
/// total         += damage;
/// if (total >= maximumHealth) dead = true;
/// </code>
/// <para>
/// <b>The counter runs upward.</b> A human stores <i>accumulated damage</i> and a <i>maximum</i>, not a
/// remaining-health value — the HUD draws <c>1 − total/maximum</c> to turn it back into a bar
/// (<c>0x460400</c>). Nothing subtracts, so healing has to reduce the accumulator rather than add to a
/// pool.
/// </para>
/// </remarks>
public static class HumanDamage
{
    /// <summary>How many hit zones a human has.</summary>
    /// <remarks>
    /// Seventeen. Confirmed twice over: the mapping table names zones up to 16, and the human's
    /// per-zone array of <c>{accumulated, multiplier}</c> pairs starts at <c>+608</c> and runs exactly
    /// to <c>+743</c>, with the last-struck zone index stored at <c>+744</c> immediately after it.
    /// </remarks>
    public const int ZoneCount = 17;

    /// <summary>The zone a hit lands in when nothing more specific applies.</summary>
    /// <remarks>
    /// Zone 12 is both the table's default for the first four sub-objects and the value the direction
    /// tests refine — a hit that maps to anything else is taken at face value.
    /// </remarks>
    public const int DefaultZone = 12;

    /// <summary>The zone a hit from behind resolves to.</summary>
    public const int RearZone = 14;

    /// <summary>The zone a hit from the front resolves to.</summary>
    public const int FrontZone = 13;

    /// <summary>The zone a hit close enough to the reference bone resolves to.</summary>
    /// <remarks>
    /// Almost certainly the head: it is decided by proximity to a single bone rather than by geometry,
    /// and <see cref="HeadProximityUnits"/> is a 15-centimetre radius. Named for what it does rather
    /// than what it probably is, since the bone is only known by index.
    /// </remarks>
    public const int ProximityZone = 15;

    /// <summary>How close to the reference bone a hit must land to take <see cref="ProximityZone"/>.</summary>
    /// <remarks><c>614.4</c> world units — 15 centimetres.</remarks>
    public const float HeadProximityUnits = 614.4f;

    /// <summary>The bone the proximity test measures from.</summary>
    public const int ProximityBoneIndex = 4;

    /// <summary>How many sub-objects the mapping table covers.</summary>
    public const int MappedSubObjectCount = 30;

    /// <summary>
    /// Which zone each of a human's sub-objects belongs to, as the table at <c>0x5412A4</c> holds it.
    /// </summary>
    /// <remarks>
    /// The structure is four limb chains of three zones each — <c>{11, 10, 9}</c>, <c>{5, 4, 3}</c>,
    /// <c>{8, 7, 6}</c> and <c>{2, 1, 0}</c> — plus the torso default 12 and a further region 16.
    /// Zones 13, 14 and 15 appear nowhere here because they are only ever reached by refining 12.
    /// </remarks>
    private static readonly int[] ZoneBySubObject =
    [
        12, 12, 12, 12, 16, 16, 16, 16, 11, 10,
        9, 9, 9, 9, 5, 4, 3, 3, 3, 3,
        8, 7, 6, 6, 6, 2, 1, 0, 0, 0,
    ];

    /// <summary>Maps a struck sub-object to its hit zone.</summary>
    /// <param name="subObjectIndex">The index the ray reported.</param>
    /// <returns>The zone, which is <see cref="DefaultZone"/> for anything past the table.</returns>
    /// <remarks>
    /// The bound is a plain <c>&lt; 30</c> with no lower check, matching the original — a negative index
    /// would read behind the table there, so this returns the default instead rather than reproducing
    /// an out-of-bounds read.
    /// </remarks>
    public static int ZoneOfSubObject(int subObjectIndex) =>
        subObjectIndex >= 0 && subObjectIndex < MappedSubObjectCount
            ? ZoneBySubObject[subObjectIndex]
            : DefaultZone;

    /// <summary>Refines a default-zone hit by which way the round came in.</summary>
    /// <param name="zone">The zone from <see cref="ZoneOfSubObject"/>.</param>
    /// <param name="localForward">
    /// The reversed travel direction's component along the human's facing, in its own frame.
    /// </param>
    /// <param name="localSide">The same direction's sideways component.</param>
    /// <returns>The refined zone.</returns>
    /// <remarks>
    /// <para>
    /// Only a hit that already mapped to <see cref="DefaultZone"/> is refined; anything that named a
    /// limb keeps its zone. The test is a comparison of magnitudes, so a round arriving more from the
    /// front or back than from the side takes a directional zone and a glancing one does not.
    /// </para>
    /// <para>
    /// Both comparisons are <b>strict</b>, so a hit whose forward and sideways components are equal in
    /// magnitude keeps the default zone rather than picking one. A <paramref name="localForward"/> of
    /// exactly zero enters both of the original's branches and satisfies neither, so a perfectly
    /// perpendicular hit is never refined.
    /// </para>
    /// </remarks>
    public static int RefineByDirection(int zone, float localForward, float localSide)
    {
        if (zone != DefaultZone)
        {
            return zone;
        }

        float side = MathF.Abs(localSide);
        int refined = zone;

        if (localForward <= 0f && MathF.Abs(localForward) > side)
        {
            refined = RearZone;
        }

        if (localForward >= 0f && localForward > side)
        {
            refined = FrontZone;
        }

        return refined;
    }

    /// <summary>Whether a hit is close enough to the reference bone to take the proximity zone.</summary>
    /// <param name="distanceFromBoneUnits">How far the impact is from the bone, in world units.</param>
    /// <param name="localForward">The reversed direction's forward component.</param>
    /// <returns><see langword="true"/> when the proximity zone applies.</returns>
    /// <remarks>
    /// The original only runs this test when the round came from the front or square on
    /// (<c>localForward &gt;= 0</c>), so a hit from behind can never take it however close it lands.
    /// </remarks>
    public static bool TakesProximityZone(float distanceFromBoneUnits, float localForward) =>
        localForward >= 0f && distanceFromBoneUnits < HeadProximityUnits;

    /// <summary>The damage one hit does.</summary>
    /// <param name="zoneMultiplier">The struck zone's multiplier, from the human's own table.</param>
    /// <param name="damageFactor">The weapon's <c>Damage Factor</c>, carried by the round.</param>
    /// <returns>The damage to accumulate.</returns>
    /// <remarks>
    /// A plain product — nothing scales it by range, by the round's remaining energy, or by how many
    /// surfaces it has already been through.
    /// </remarks>
    public static float DamageOfHit(float zoneMultiplier, float damageFactor) =>
        zoneMultiplier * damageFactor;

    /// <summary>Whether a human with this much accumulated damage is dead.</summary>
    /// <param name="accumulatedDamage">The running total.</param>
    /// <param name="maximumHealth">The human's maximum.</param>
    /// <returns><see langword="true"/> when the human has died.</returns>
    /// <remarks>The comparison is <c>&gt;=</c>, so exactly reaching the maximum kills.</remarks>
    public static bool IsDead(float accumulatedDamage, float maximumHealth) =>
        accumulatedDamage >= maximumHealth;

    /// <summary>The fraction of health left, as the HUD draws it.</summary>
    /// <param name="accumulatedDamage">The running total.</param>
    /// <param name="maximumHealth">The human's maximum.</param>
    /// <returns>The fraction, floored at zero.</returns>
    /// <remarks>
    /// <c>0x460400</c> computes <c>1 − total/maximum</c> and clamps the low end only, so an
    /// over-killed human reads as zero rather than negative. There is no clamp above one.
    /// </remarks>
    public static float RemainingFraction(float accumulatedDamage, float maximumHealth)
    {
        float remaining = 1f - (accumulatedDamage / maximumHealth);
        return remaining < 0f ? 0f : remaining;
    }
}
