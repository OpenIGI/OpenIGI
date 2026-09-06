namespace OpenIGI.Game.Weapons;

/// <summary>The <c>GRENADE_EXPLOSIONTYPE_*</c> constants, as <c>0x476730</c> registers them.</summary>
public enum GrenadeExplosionType
{
    /// <summary>A damaging blast, with fireballs and fragments.</summary>
    Normal = 0,

    /// <summary>A flashbang, which blinds rather than damages.</summary>
    Flashbang = 1,
}

/// <summary>
/// How an explosion's damage falls off with distance.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x416C90</c>, which the human's explosion handler (<c>0x45F9D0</c>) calls, and which is the
/// whole falloff model:
/// </para>
/// <code>
/// d = |explosion - target| - bodyRadius - radius;
/// if (d &lt; 0)       return damageFactor;                    // inside the blast
/// if (d &gt;= falloff) return 0;                               // past it
/// return (falloff - d) * damageFactor / falloff;            // linear between
/// </code>
/// <para>
/// <b>An explosion always applies to hit zone 13</b> — the human's handler hardcodes it. So a blast
/// never lands a headshot and never hits a limb; every target takes the same zone multiplier however it
/// is standing. That also confirms the per-zone array layout independently: zone 13's accumulator is at
/// <c>+712</c> and its multiplier at <c>+716</c>, which is exactly <c>608 + 8 × 13</c>.
/// </para>
/// </remarks>
public static class ExplosionDamage
{
    /// <summary>The hit zone every explosion applies to.</summary>
    /// <remarks>Hardcoded in <c>0x45F9D0</c> — the same zone a shot from the front resolves to.</remarks>
    public const int Zone = 13;

    /// <summary>What the target's own radius is scaled by before it is subtracted.</summary>
    /// <remarks>
    /// <c>0.33333334</c> — a third of the human's collision radius, so a body counts as rather narrower
    /// than it collides.
    /// </remarks>
    public const float BodyRadiusScale = 0.33333334f;

    /// <summary>World units in a metre, since the tags are written in metres.</summary>
    public const float UnitsPerMetre = 4096f;

    /// <summary>The retail <c>Grenade - Explosion damage factor</c>.</summary>
    public const float DefaultGrenadeDamageFactor = 1.3f;

    /// <summary>The retail <c>Grenade - Explosion radius (meters)</c>.</summary>
    public const float DefaultGrenadeRadiusMetres = 7f;

    /// <summary>The retail <c>Grenade - Explosion falloff (meters)</c>.</summary>
    public const float DefaultGrenadeFalloffMetres = 4f;

    /// <summary>Fallback radius matching the FlashBang child tags in weaponconfig.qvm.</summary>
    public const float DefaultFlashbangRadiusMetres = 10f;
    /// <summary>Fallback falloff matching the FlashBang child tags in weaponconfig.qvm.</summary>
    public const float DefaultFlashbangFalloffMetres = 15f;
    /// <summary>Fallback damage factor matching the FlashBang child tags in weaponconfig.qvm.</summary>
    public const float DefaultFlashbangDamageFactor = 1f;

    /// <summary>How much damage an explosion delivers to a target.</summary>
    /// <param name="distanceInUnits">Centre-to-centre distance from the blast to the target.</param>
    /// <param name="bodyRadiusInUnits">
    /// The target's own radius, <b>already scaled</b> by <see cref="BodyRadiusScale"/>.
    /// </param>
    /// <param name="radiusInUnits">The blast radius, inside which damage is undiminished.</param>
    /// <param name="falloffInUnits">How far past the radius the damage takes to reach zero.</param>
    /// <param name="damageFactor">The explosion's damage factor.</param>
    /// <returns>The dose, which the caller multiplies by the zone multiplier.</returns>
    /// <remarks>
    /// <para>
    /// <b>A falloff of zero makes this binary.</b> The retail grenade ships with exactly that, so a
    /// grenade does its full damage anywhere inside five metres and nothing at all a millimetre
    /// outside — there is no gradient in the shipped game. The order of the two tests is what saves the
    /// division: everything at or past the falloff returns before the divide, so a zero falloff can
    /// never divide by zero.
    /// </para>
    /// <para>
    /// Distance is measured to the target's origin and the radii are subtracted, so a target standing
    /// on the blast takes full damage regardless of the radius.
    /// </para>
    /// </remarks>
    public static float Dose(
        float distanceInUnits,
        float bodyRadiusInUnits,
        float radiusInUnits,
        float falloffInUnits,
        float damageFactor)
    {
        float d = distanceInUnits - bodyRadiusInUnits - radiusInUnits;

        if (d < 0f)
        {
            return damageFactor;
        }

        if (d >= falloffInUnits)
        {
            return 0f;
        }

        return (falloffInUnits - d) * damageFactor / falloffInUnits;
    }

    /// <summary>Scales a target's collision radius the way the explosion handler does.</summary>
    /// <param name="collisionRadiusInUnits">The target's radius.</param>
    /// <returns>The radius the falloff uses.</returns>
    public static float EffectiveBodyRadius(float collisionRadiusInUnits) =>
        collisionRadiusInUnits * BodyRadiusScale;

    /// <summary>Converts one of the explosion tags from metres to world units.</summary>
    /// <param name="metres">The tag's value.</param>
    /// <returns>The distance in world units.</returns>
    public static float ToUnits(float metres) => metres * UnitsPerMetre;
}
