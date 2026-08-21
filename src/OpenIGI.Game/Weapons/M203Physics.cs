using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>One M203 launch request produced by the M16A2 alternate-fire path.</summary>
public readonly record struct M203Projectile(
    Vector3d Origin,
    Vector3f Direction,
    float MuzzleVelocityKilometresPerHour,
    float ExplosionRadiusMetres,
    float ActivateRadiusMetres,
    string ModelName = "108_05_1",
    Matrix3f Orientation = default);

/// <summary>An M203 impact emitted by the retail projectile task.</summary>
public readonly record struct M203Detonation(
    Vector3d Position,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits,
    Vector3f Normal = default,
    float ActivateRadiusUnits = 0f);

/// <summary>The M203 projectile physics read from the native M16A2/M203 callbacks.</summary>
public static class M203Physics
{
    /// <summary>The per-tick horizontal and vertical drag, <c>0x3f7eb852</c>.</summary>
    public const float Drag = 0.995f;

    /// <summary>The native gravity subtraction per tick, <c>0x420eb90c</c>.</summary>
    public const float GravityPerTick = 35.68070983886719f;

    /// <summary>The first launch-speed multiplier used by the native M16A2 callback.</summary>
    private const float NativeMuzzleSpeedMultiplier = 4096000f;

    /// <summary>The final launch-speed multiplier used by the native M16A2 callback.</summary>
    private const float NativeVelocityMultiplier = 1f / 108000f;

    /// <summary>Converts the M203 config's km/h value into engine units per 30 Hz tick.</summary>
    /// <remarks>
    /// <c>0x473510</c> evaluates each component as
    /// <c>((muzzleVelocity * 4096000) * directionComponent) * (1 / 108000)</c>.
    /// Keep the operation order instead of collapsing it into a single rounded constant.
    /// </remarks>
    public const float KilometresPerHourToUnitsPerTick = NativeMuzzleSpeedMultiplier * NativeVelocityMultiplier;

    /// <summary>Converts a metres value from the task binding into engine units.</summary>
    public const float UnitsPerMetre = 4096f;

    /// <summary>The M16A2/M203 task's shipped muzzle speed, in kilometres per hour.</summary>
    /// <remarks>Retail <c>M203 - Muzzle velocity (km/h)</c> tag in <c>WEAPONS/weaponconfig.qvm</c>.</remarks>
    public const float DefaultMuzzleVelocityKilometresPerHour = 270f;

    /// <summary>The M16A2/M203 task's shipped damaging blast radius, in metres.</summary>
    /// <remarks>Retail <c>M203 - Explosion radius (meters)</c> tag in <c>WEAPONS/weaponconfig.qvm</c>.</remarks>
    public const float DefaultExplosionRadiusMetres = 5f;

    /// <summary>The M16A2/M203 task's shipped activation radius, in metres.</summary>
    /// <remarks>Final tail of <c>WEAPONS/M16A2/WEAPON.QVM</c>, passed through the impact callback.</remarks>
    public const float DefaultActivateRadiusMetres = 1f;

    /// <summary>Native M203 explosion damage factor used by the task's explosion message.</summary>
    public const float DamageFactor = 1f;

    /// <summary>Native M203 explosions have no additional falloff parameter.</summary>
    public const float FalloffMetres = 0f;

    /// <summary>
    /// Reconstructs the orientation made by the retail launch-basis helper at <c>0x414A60</c>.
    /// </summary>
    /// <remarks>
    /// The helper keeps the launch direction in matrix column two and chooses its reference axis
    /// at a 0.95 vertical threshold. The native projectile constructor stores this matrix once; it
    /// is not rebuilt from the changing velocity during flight.
    /// </remarks>
    public static Matrix3f LaunchOrientation(Vector3f direction)
    {
        float local30 = direction.X;
        float local2c = direction.Y;
        float local28 = direction.Z;
        float local24;
        float local20;
        float local1c;
        float local18;
        float local14;
        float fVar1 = 0f;

        float verticalMagnitude = direction.Z < 0f ? -direction.Z : direction.Z;
        if (verticalMagnitude <= 0.95f)
        {
            local18 = local2c;
            local14 = -local30;
            float horizontalLength = MathF.Sqrt((local14 * local14) + (local18 * local18));
            if (local18 == 0f && local14 == 0f)
            {
                fVar1 = 0f;
            }
            else
            {
                fVar1 = 1f / horizontalLength;
                local18 *= fVar1;
                local14 *= fVar1;
                // The native helper explicitly clears this component after the normalization.
                fVar1 *= 0f;
            }

            local24 = (local28 * local14) - (local2c * fVar1);
            local20 = -((local28 * local18) - (local30 * fVar1));
            local1c = (local2c * local18) - (local30 * local14);
            NormalizeBasisVector(ref local24, ref local20, ref local1c);
        }
        else
        {
            local24 = 0f;
            local20 = -local28;
            local1c = local2c;
            float horizontalLength = MathF.Sqrt((local1c * local1c) + (local20 * local20));
            if (local20 != 0f || local1c != 0f)
            {
                fVar1 = 1f / horizontalLength;
                local24 *= fVar1;
                local20 *= fVar1;
                local1c *= fVar1;
            }

            local18 = (local2c * local1c) - (local28 * local20);
            local14 = -((local30 * local1c) - (local28 * local24));
            fVar1 = (local30 * local20) - (local2c * local24);
            NormalizeBasisVector(ref local18, ref local14, ref fVar1);
        }

        return Matrix3f.FromRows(
            new Vector3f(local18, local30, local24),
            new Vector3f(local14, local2c, local20),
            new Vector3f(fVar1, local28, local1c));
    }

    /// <summary>Returns the exact launch velocity used by <c>0x473510</c>.</summary>
    public static Vector3f LaunchVelocity(Vector3f direction, float muzzleVelocityKilometresPerHour)
    {
        float scaledMuzzleVelocity = muzzleVelocityKilometresPerHour * NativeMuzzleSpeedMultiplier;
        return new Vector3f(
            (scaledMuzzleVelocity * direction.X) * NativeVelocityMultiplier,
            (scaledMuzzleVelocity * direction.Y) * NativeVelocityMultiplier,
            (scaledMuzzleVelocity * direction.Z) * NativeVelocityMultiplier);
    }

    private static void NormalizeBasisVector(ref float x, ref float y, ref float z)
    {
        if (x == 0f && y == 0f && z == 0f)
        {
            return;
        }

        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        float inverseLength = 1f / length;
        x *= inverseLength;
        y *= inverseLength;
        z *= inverseLength;
    }
}

/// <summary>Advances M203 rounds using the native 30 Hz projectile integrator.</summary>
public sealed class M203Simulation
{
    /// <summary>The maximum number of M203 projectiles alive at once.</summary>
    public const int MaximumLive = 16;

    /// <summary>The safety lifetime for a round that never reaches a traced surface, in 30 Hz ticks.</summary>
    /// <remarks>Thirty seconds prevents missed rounds from permanently exhausting the native-style pool.</remarks>
    public const int MaximumLifetimeTicks = 30 * 30;

    private readonly List<M203ProjectileState> _projectiles = [];
    private readonly List<M203Detonation> _detonations = [];

    /// <summary>Gets the projectiles currently in flight.</summary>
    public IReadOnlyList<M203ProjectileState> Projectiles => _projectiles;

    /// <summary>Gets impacts emitted by the most recent tick.</summary>
    public IReadOnlyList<M203Detonation> Detonations => _detonations;

    /// <summary>Spawns one M203 round.</summary>
    public bool Spawn(in M203Projectile projectile)
    {
        if (_projectiles.Count >= MaximumLive
            || projectile.MuzzleVelocityKilometresPerHour <= 0f
            || projectile.ExplosionRadiusMetres <= 0f
            || projectile.ActivateRadiusMetres < 0f)
        {
            return false;
        }

        _projectiles.Add(new M203ProjectileState(
            projectile.Origin,
            M203Physics.LaunchVelocity(projectile.Direction, projectile.MuzzleVelocityKilometresPerHour),
            projectile.ModelName,
            projectile.ExplosionRadiusMetres * M203Physics.UnitsPerMetre,
            projectile.ActivateRadiusMetres * M203Physics.UnitsPerMetre,
            projectile.Orientation == default
                ? M203Physics.LaunchOrientation(projectile.Direction)
                : projectile.Orientation));
        return true;
    }

    /// <summary>Advances every M203 round one fixed game tick.</summary>
    /// <param name="walls">The world collision cast, including terrain.</param>
    /// <param name="humans">The non-mutating human segment cast.</param>
    public void Tick(WorldRayCast? walls, HumanRayCast? humans)
    {
        _detonations.Clear();

        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            M203ProjectileState projectile = _projectiles[i];
            projectile.PreviousPosition = projectile.Position;

            // Native 0x476340: vx *= .995, vy *= .995, vz = (vz - 35.6807098) * .995.
            projectile.Velocity = new Vector3f(
                projectile.Velocity.X * M203Physics.Drag,
                projectile.Velocity.Y * M203Physics.Drag,
                (projectile.Velocity.Z - M203Physics.GravityPerTick) * M203Physics.Drag);

            Vector3d candidate = new(
                projectile.Position.X + projectile.Velocity.X,
                projectile.Position.Y + projectile.Velocity.Y,
                projectile.Position.Z + projectile.Velocity.Z);

            WorldRayHit wallHit = default;
            bool struckWall = walls is not null
                && walls(projectile.Position, candidate, out wallHit);
            HumanHit humanHit = default;
            bool struckHuman = humans is not null
                && humans(projectile.Position, candidate, out humanHit)
                && (!struckWall || humanHit.Distance < wallHit.Distance);

            if (struckHuman || struckWall)
            {
                Vector3f normal = struckHuman
                    ? Normalize(projectile.Velocity * -1f)
                    : wallHit.Normal;
                _detonations.Add(new M203Detonation(
                    struckHuman ? humanHit.Position : wallHit.Position,
                    M203Physics.DamageFactor,
                    projectile.ExplosionRadiusUnits,
                    M203Physics.FalloffMetres * M203Physics.UnitsPerMetre,
                    normal,
                    projectile.ActivateRadiusUnits));
                _projectiles.RemoveAt(i);
                continue;
            }

            projectile.Position = candidate;
            projectile.Age++;
            if (projectile.Age >= MaximumLifetimeTicks)
            {
                _projectiles.RemoveAt(i);
            }
        }
    }

    /// <summary>Removes live rounds and pending impacts when a mission ends.</summary>
    public void Clear()
    {
        _projectiles.Clear();
        _detonations.Clear();
    }

    private static Vector3f Normalize(Vector3f value)
    {
        float length = MathF.Sqrt(
            (value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > float.Epsilon ? value * (1f / length) : value;
    }
}

/// <summary>Mutable presentation and physics state for one M203 round.</summary>
public sealed class M203ProjectileState
{
    internal M203ProjectileState(
        Vector3d position,
        Vector3f velocity,
        string modelName,
        float explosionRadiusUnits,
        float activateRadiusUnits,
        Matrix3f orientation)
    {
        Position = position;
        PreviousPosition = position;
        Velocity = velocity;
        ModelName = modelName;
        ExplosionRadiusUnits = explosionRadiusUnits;
        ActivateRadiusUnits = activateRadiusUnits;
        Orientation = orientation;
    }

    /// <summary>Gets or sets the current engine-unit position.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the previous fixed-tick position.</summary>
    public Vector3d PreviousPosition { get; set; }

    /// <summary>Gets or sets the current native velocity.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets the authored projectile model.</summary>
    public string ModelName { get; }

    /// <summary>Gets the explosion radius in engine units.</summary>
    public float ExplosionRadiusUnits { get; }

    /// <summary>Gets the native activation radius in engine units.</summary>
    public float ActivateRadiusUnits { get; }

    /// <summary>
    /// Gets the launch orientation captured by the native projectile constructor.
    /// </summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the number of completed ticks.</summary>
    public int Age { get; set; }
}
