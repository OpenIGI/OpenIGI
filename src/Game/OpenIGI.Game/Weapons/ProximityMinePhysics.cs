namespace OpenIGI.Game.Weapons;

/// <summary>The native proximity-mine timing and radius values.</summary>
/// <remarks>
/// The mine tail read at <c>0x475960</c> carries four values: two seconds before arming, a half-second
/// trigger countdown, a 1.5 metre activation radius, and a three metre explosion radius. The values
/// are kept in ticks here because the native task runs at the same thirty-hertz clock as grenades.
/// </remarks>
public static class ProximityMinePhysics
{
    /// <summary>Time before the mine can detect a human.</summary>
    public const int ArmingDelayTicks = 2 * 30;

    /// <summary>Time between detecting a human and detonating.</summary>
    public const int TriggerCountdownTicks = 15;

    /// <summary>Distance at which an armed mine detects a human.</summary>
    public const float ActivationRadiusUnits = 1.5f * ExplosionDamage.UnitsPerMetre;

    /// <summary>Mine blast radius.</summary>
    public const float ExplosionRadiusUnits = 3f * ExplosionDamage.UnitsPerMetre;
}
