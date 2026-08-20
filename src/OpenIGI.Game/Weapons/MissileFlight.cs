using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// How each kind of missile steers, and what it does when it arrives.
/// </summary>
/// <remarks>
/// <para>
/// The missile's per-tick (<c>0x00427970</c>) switches on the task config type at <c>+0x11c</c>
/// and calls one small steer function per kind. Type constants are registered at <c>0x004277EB</c>.
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

    /// <summary>The retail simulation tick rate used to turn muzzle speed into world units per tick.</summary>
    public const float TicksPerSecond = 30f;

    /// <summary>
    /// Launch speed used when the weapon table's muzzle velocity is the dummy 5 m/s that keeps
    /// rocket carriers out of <c>g_ballistics</c>.
    /// </summary>
    /// <remarks>
    /// Retail missiles are thrust bodies, not 5 m/s grenades. Forty metres a second is a conservative
    /// LAW/RPG visual speed that still crosses a 100 m courtyard in a few seconds.
    /// </remarks>
    public const float FallbackLaunchMetresPerSecond = 40f;

    /// <summary>Resolves the speed a launcher actually fires at.</summary>
    public static float LaunchSpeedMetresPerSecond(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.MuzzleVelocity >= 100f
            ? weapon.MuzzleVelocity
            : FallbackLaunchMetresPerSecond;
    }

    /// <summary>
    /// Ballistic lob factor stored as a double at <c>0x005316A8</c> (<c>0x3FC0000000000000</c> = 0.125).
    /// </summary>
    public const double BallisticLobFactorDouble = 0.125d;

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

    /// <summary>Selects the guidance regime from the launcher/projectile binding, not a hard-coded type.</summary>
    public static MissileType TypeForLauncher(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        foreach (float value in weapon.Weapon.Values)
        {
            int code = (int)value;
            if (code is >= (int)MissileType.MissileDirect and <= (int)MissileType.Bomb
                && value == code)
            {
                return (MissileType)code;
            }
        }

        string haystack = string.Join(
            ' ',
            weapon.ScriptId,
            weapon.DisplayName,
            weapon.Weapon.TypeName,
            weapon.Projectile.TypeName);

        if (Contains(haystack, "HIND"))
        {
            return MissileType.HindHelicopter;
        }

        if (Contains(haystack, "ANTITANK") || Contains(haystack, "ANTI-TANK") || Contains(haystack, "TOW"))
        {
            return MissileType.MissileAntiTank;
        }

        if (Contains(haystack, "BALLISTIC") || Contains(haystack, "MORTAR"))
        {
            return MissileType.MissileBallistic;
        }

        if (Contains(haystack, "BOMB"))
        {
            return MissileType.Bomb;
        }

        // Handheld LAW / RPG-18 / APC / T80 rockets fly along the barrel.
        return MissileType.MissileDirect;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

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
    /// Flattened range from <c>0x004280B0</c>: Z of the delta is zeroed before <c>0x004A9410</c>.
    /// </param>
    /// <param name="threeDimensionalDistance">
    /// Full range used by the near-regime fall-through into <c>0x00428040</c>, which multiplies the
    /// 3D length by the 0.125 double at <c>0x005316A8</c>.
    /// </param>
    /// <returns>The height to aim at.</returns>
    /// <remarks>
    /// <c>0x004280B0</c>: if flattened length <b>&gt;</b> <c>122880</c> (<c>0x005316B0</c>) raise
    /// target Z by <c>102400</c> (<c>0x00531548</c>); otherwise call the ballistic steer on the
    /// unflattened target. Exactly thirty metres still lobs.
    /// </remarks>
    public static float AntiTankAimHeight(
        float targetHeight,
        float horizontalDistance,
        float threeDimensionalDistance) =>
        horizontalDistance > AntiTankDirectRangeUnits
            ? targetHeight + AntiTankClimbUnits
            : BallisticAimHeight(targetHeight, threeDimensionalDistance);

    /// <summary>Near-regime helper when only the flattened range is known.</summary>
    public static float AntiTankAimHeight(float targetHeight, float horizontalDistance) =>
        AntiTankAimHeight(targetHeight, horizontalDistance, horizontalDistance);

    /// <summary>
    /// Builds the point a steered missile uses for its launch direction.
    /// </summary>
    /// <param name="origin">The launcher position, in world units.</param>
    /// <param name="target">The target position, in world units.</param>
    /// <param name="type">The missile guidance mode.</param>
    /// <param name="launcherAxis">The launcher's forward axis, required for the Hind mode.</param>
    /// <returns>The guidance point, in world units.</returns>
    /// <remarks>
    /// The anti-tank native first flattens the target vector before measuring its range; retaining
    /// that detail here is important because using the three-dimensional range changes the thirty
    /// metre regime for targets above or below the launcher. A bomb has no steering case, so its
    /// authored target is returned only as a convenient caller-owned fallback.
    /// </remarks>
    public static Vector3d AimPoint(
        Vector3d origin,
        Vector3d target,
        MissileType type,
        Vector3f launcherAxis = default)
    {
        if (type == MissileType.HindHelicopter)
        {
            Vector3f axis = launcherAxis == default ? Vector3f.UnitY : launcherAxis;
            return new Vector3d(
                origin.X + (axis.X * HindOffsetUnits),
                origin.Y + (axis.Y * HindOffsetUnits),
                origin.Z + (axis.Z * HindOffsetUnits));
        }

        if (type is MissileType.MissileDirect or MissileType.MissileKeyboard or MissileType.Bomb)
        {
            return target;
        }

        double dx = target.X - origin.X;
        double dy = target.Y - origin.Y;
        double horizontalDistance = Math.Sqrt((dx * dx) + (dy * dy));
        float height = type == MissileType.MissileAntiTank
            ? AntiTankAimHeight(
                (float)target.Z,
                (float)horizontalDistance,
                (float)Distance(origin, target))
            : BallisticAimHeight((float)target.Z, (float)Distance(origin, target));

        return new Vector3d(target.X, target.Y, height);
    }

    /// <summary>Converts a guidance point and muzzle speed into a launch velocity.</summary>
    /// <param name="origin">The launcher position, in world units.</param>
    /// <param name="target">The target position, in world units.</param>
    /// <param name="type">The missile guidance mode.</param>
    /// <param name="muzzleVelocityMetresPerSecond">The weapon record's muzzle velocity.</param>
    /// <param name="launcherAxis">The launcher's forward axis, required for the Hind mode.</param>
    /// <returns>A normalized launch direction scaled to world units per tick.</returns>
    public static Vector3f LaunchVelocity(
        Vector3d origin,
        Vector3d target,
        MissileType type,
        float muzzleVelocityMetresPerSecond,
        Vector3f launcherAxis = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(muzzleVelocityMetresPerSecond);

        Vector3d aim = AimPoint(origin, target, type, launcherAxis);
        Vector3d delta = aim - origin;
        double length = Distance(Vector3d.Zero, delta);
        if (length <= double.Epsilon)
        {
            return default;
        }

        float speedUnitsPerTick = muzzleVelocityMetresPerSecond * UnitsPerMetre / TicksPerSecond;
        return new Vector3f(
            (float)(delta.X / length) * speedUnitsPerTick,
            (float)(delta.Y / length) * speedUnitsPerTick,
            (float)(delta.Z / length) * speedUnitsPerTick);
    }

    private static double Distance(Vector3d left, Vector3d right)
    {
        double x = right.X - left.X;
        double y = right.Y - left.Y;
        double z = right.Z - left.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}

/// <summary>
/// What a missile's impact explosion is, from the <c>0x0047F350</c> call at the tail of
/// <c>0x00427970</c>.
/// </summary>
/// <remarks>
/// Unlike a grenade's, none of these come from level tags — a missile's blast is fixed in code.
/// </remarks>
public static class MissileExplosion
{
    /// <summary>The damage factor.</summary>
    /// <remarks>Fixed by the missile impact callback; it is independent of grenade weaponconfig tags.</remarks>
    public const float DamageFactor = 5f;

    /// <summary>The blast radius, in world units.</summary>
    /// <remarks><c>10240</c> — 2.5 metres.</remarks>
    public const float RadiusUnits = 10240f;

    /// <summary>The falloff distance, in world units.</summary>
    /// <remarks><c>7168</c> — 1.75 metres, from the missile impact callback.</remarks>
    public const float FalloffUnits = 7168f;

    /// <summary>How many bursts the explosion fires.</summary>
    /// <remarks>Ten, against a grenade's one.</remarks>
    public const int BurstCount = 10;

    /// <summary>The maximum random delay between bursts, in ticks.</summary>
    public const int BurstDelayTicks = 5;
}
