namespace OpenIGI.Game.Weapons;

/// <summary>
/// How each kind of missile steers, and what it does when it arrives.
/// </summary>
/// <remarks>
/// <para>
/// The missile's per-tick (<c>0x428390</c>) switches on the type from its config and calls one small
/// function per kind. They are genuinely small — the whole guidance model is four expressions and a
/// deliberate gap.
/// </para>
/// </remarks>
public static class MissileFlight
{
    /// <summary>World units in a metre.</summary>
    public const float UnitsPerMetre = 4096f;

    /// <summary>How far along the launcher's own axis a <see cref="MissileType.HindHelicopter"/> aims.</summary>
    /// <remarks><c>12288</c> units — three metres.</remarks>
    public const float HindOffsetUnits = 12288f;

    /// <summary>How high a <see cref="MissileType.MissileBallistic"/> lobs, as a fraction of the range.</summary>
    /// <remarks>
    /// <c>0.125</c>. The aim point is the target raised by an eighth of the distance to it, so the arc
    /// grows with the shot and a point-blank one is nearly flat.
    /// </remarks>
    public const float BallisticLobFactor = 0.125f;

    /// <summary>Inside this horizontal range an anti-tank missile behaves ballistically.</summary>
    /// <remarks><c>122880</c> units — thirty metres.</remarks>
    public const float AntiTankDirectRangeUnits = 122880f;

    /// <summary>How high an anti-tank missile climbs beyond that range.</summary>
    /// <remarks><c>102400</c> units — twenty-five metres, a fixed height rather than a fraction.</remarks>
    public const float AntiTankClimbUnits = 102400f;

    /// <summary>Whether a type has a steering function at all.</summary>
    /// <param name="type">The missile type.</param>
    /// <returns><see langword="false"/> only for <see cref="MissileType.Bomb"/>.</returns>
    /// <remarks>
    /// <b>The bomb has no case in the switch.</b> It is not steered badly — it is not steered at all,
    /// and falls under the physics it inherits. <see cref="MissileType.MissileDirect"/> does have a
    /// case, but that case only clears both steering inputs, so it flies straight on purpose rather
    /// than by omission.
    /// </remarks>
    public static bool IsSteered(MissileType type) => type != MissileType.Bomb;

    /// <summary>The aim point a ballistic missile steers toward.</summary>
    /// <param name="targetHeight">The target's height.</param>
    /// <param name="distanceToTarget">The straight-line distance to it.</param>
    /// <returns>The height to aim at.</returns>
    /// <remarks>
    /// The horizontal components are the target's own; only the height is raised. Distance is the full
    /// three-dimensional one, so a target far below still gets a lob.
    /// </remarks>
    public static float BallisticAimHeight(float targetHeight, float distanceToTarget) =>
        targetHeight + (distanceToTarget * BallisticLobFactor);

    /// <summary>The aim point an anti-tank missile steers toward.</summary>
    /// <param name="targetHeight">The target's height.</param>
    /// <param name="horizontalDistance">
    /// The distance to the target <b>with the height difference zeroed</b> — the original flattens the
    /// vector before measuring, so a target directly above counts as being at zero range.
    /// </param>
    /// <returns>The height to aim at.</returns>
    /// <remarks>
    /// Two regimes with a hard boundary. Within <see cref="AntiTankDirectRangeUnits"/> it falls through
    /// to the ballistic lob, using the <i>horizontal</i> distance rather than the true one. Beyond it,
    /// the missile aims a fixed twenty-five metres above the target and dives — a top-attack profile.
    /// The boundary test is <c>&lt;=</c>, so exactly thirty metres still lobs.
    /// </remarks>
    public static float AntiTankAimHeight(float targetHeight, float horizontalDistance) =>
        horizontalDistance <= AntiTankDirectRangeUnits
            ? BallisticAimHeight(targetHeight, horizontalDistance)
            : targetHeight + AntiTankClimbUnits;
}

/// <summary>
/// What a missile's impact explosion is, from the literals at the tail of <c>0x428390</c>.
/// </summary>
/// <remarks>
/// Unlike a grenade's, none of these come from level tags — a missile's blast is fixed in code.
/// </remarks>
public static class MissileExplosion
{
    /// <summary>The damage factor.</summary>
    /// <remarks><b>Five times</b> a grenade's, which is 1.0.</remarks>
    public const float DamageFactor = 5f;

    /// <summary>The blast radius, in world units.</summary>
    /// <remarks><c>10240</c> — 2.5 metres, and so only <b>half</b> a grenade's five.</remarks>
    public const float RadiusUnits = 10240f;

    /// <summary>The falloff distance, in world units.</summary>
    /// <remarks>
    /// <c>7168</c> — 1.75 metres. A grenade ships with a falloff of zero, so a missile is the one that
    /// actually tapers: its damage falls off over the 1.75 metres past its radius instead of stopping
    /// dead.
    /// </remarks>
    public const float FalloffUnits = 7168f;

    /// <summary>How many bursts the explosion fires.</summary>
    /// <remarks>Ten, against a grenade's one.</remarks>
    public const int BurstCount = 10;

    /// <summary>The maximum random delay between bursts, in ticks.</summary>
    public const int BurstDelayTicks = 5;
}
