using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>The grenade's physical constants, as the retail data and code carry them.</summary>
/// <remarks>
/// <para>
/// The numbers come from two reads. <c>physicsobj/physicsobj.qvm</c>'s <c>DefinePhysicsObjType</c>
/// rows for <c>TASKTYPE_GRENADE</c> (dumped through this port's own interpreter) give the mass
/// (<c>0.3</c> kg), the half-extents (<c>0.1 × 0.1 × 0.15</c> m), a per-surface elasticity of
/// <c>0.3</c> on every one of the five collision groups, and the tail pair every entry carries. The
/// same rows' trailing arguments are the grenade class's own: the <b>explode time, 4.0 seconds</b>,
/// and the explosion type — <c>0</c> for the frag model <c>135_01_1</c>, <c>1</c> for the flashbang
/// <c>137_01_1</c>. The task init (<c>0x477040</c>) turns the seconds into ticks at thirty and
/// counts down in the per-tick (<c>0x476AC0</c>), detonating at zero.
/// </para>
/// <para>
/// The throw itself carries no constant of its own: the round's velocity is the aim direction times
/// the weapon record's <c>Muzzle Velocity</c> — fifteen metres a second for both shipped grenades —
/// exactly as every projectile spawn receives it.
/// </para>
/// </remarks>
public static class GrenadePhysics
{
    /// <summary>The grenade's mass, in kilograms.</summary>
    public const float MassKilograms = 0.3f;

    /// <summary>How much of the impact-normal speed survives a bounce.</summary>
    /// <remarks>The elasticity every one of the grenade's five surface groups declares.</remarks>
    public const float Restitution = 0.3f;

    /// <summary>What fraction of the surface-tangential speed survives a bounce.</summary>
    /// <remarks>
    /// <b>A chosen number, not a read one.</b> The groups carry two further unit values whose use in
    /// the solver is unread; a modest tangential loss is what keeps a grenade from skating like a
    /// puck without pretending to be the original's friction model.
    /// </remarks>
    public const float TangentialKeep = 0.75f;

    /// <summary>The grenade's vertical half-extent, in world units.</summary>
    /// <remarks><c>0.15</c> m of the physics row, times 4096.</remarks>
    public const float HalfHeightUnits = 0.15f * 4096f;

    /// <summary>The fuse, in seconds — the physics row's own trailing argument.</summary>
    public const float FuseSeconds = 4f;

    /// <summary>The fuse in ticks: seconds times the thirty-hertz rate, as <c>0x477040</c> computes it.</summary>
    public const int FuseTicks = (int)(FuseSeconds * 30f);

    /// <summary>Gravity per tick², in world units — the engine's own 9.8 m/s² object gravity.</summary>
    public const float GravityPerTick = HumanMotion.LadderSlideGravityPerTick;

    /// <summary>Below this speed on a floor-like surface the grenade comes to rest, in units/tick.</summary>
    /// <remarks>A chosen threshold (~0.3 m/s); the retail rest test is part of the unread solver.</remarks>
    public const float RestSpeedUnitsPerTick = 40.96f;
}

/// <summary>One detonation the simulation produced, for the owner to apply and show.</summary>
/// <param name="Position">Where the grenade was when the fuse ran out, in world units.</param>
/// <param name="Type">Which explosion this is — the blast, or the flash.</param>
/// <param name="DamageFactor">The blast's damage factor; the shipped default is 1.</param>
/// <param name="RadiusUnits">The full-damage radius; the shipped default is five metres.</param>
/// <param name="FalloffUnits">The taper past it; the shipped default is zero — a step.</param>
public readonly record struct GrenadeDetonation(
    Vector3d Position,
    GrenadeExplosionType Type,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits);

/// <summary>One grenade in the air or at rest, waiting on its fuse.</summary>
public sealed class LiveGrenade
{
    /// <summary>Initializes a new instance of the <see cref="LiveGrenade"/> class.</summary>
    /// <param name="position">Where it starts, in world units.</param>
    /// <param name="velocity">Its initial velocity, in world units per tick.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">How many ticks until it does.</param>
    public LiveGrenade(Vector3d position, Vector3f velocity, GrenadeExplosionType type, int fuseTicks)
    {
        Position = position;
        PreviousPosition = position;
        Velocity = velocity;
        Type = type;
        FuseTicks = fuseTicks;
    }

    /// <summary>Gets or sets where the grenade is, in world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where it was last tick, for frame interpolation.</summary>
    public Vector3d PreviousPosition { get; set; }

    /// <summary>Gets or sets its velocity, in world units per tick.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets which explosion this grenade ends in.</summary>
    public GrenadeExplosionType Type { get; }

    /// <summary>Gets or sets the ticks left on the fuse.</summary>
    public int FuseTicks { get; set; }

    /// <summary>Gets or sets whether the grenade has settled and no longer integrates.</summary>
    public bool Resting { get; set; }

    /// <summary>Gets or sets how many ticks it has been moving — the renderer's tumble clock.</summary>
    /// <remarks>Stops with the grenade, so a settled one lies still instead of spinning in place.</remarks>
    public int TumbleTicks { get; set; }
}

/// <summary>
/// Flies every live grenade, bounces it off the world, and detonates it when its fuse runs out.
/// </summary>
/// <remarks>
/// <para>
/// The compact port of the grenade's physics-object flight: gravity at the engine's own object
/// 9.8 m/s², one world cast per tick along the tick's travel, a reflected bounce at
/// <see cref="GrenadePhysics.Restitution"/> when it meets a surface, and rest once it is slow on
/// something floor-like. The original's full rigid-body solver (tumbling, the per-surface groups)
/// is deliberately not reproduced — what is kept is every number that decides where a grenade ends
/// up and when it goes off, which is what the game is played against.
/// </para>
/// <para>
/// Detonations land in <see cref="Detonations"/> for the owner to apply — damage through
/// <see cref="ExplosionDamage"/>, the flash through <see cref="Flashbang"/>, and whatever is drawn
/// and heard. The simulation itself hurts nobody.
/// </para>
/// </remarks>
public sealed class GrenadeSimulation
{
    /// <summary>The most grenades in the air at once; one past it is dropped.</summary>
    public const int MaximumLive = 32;

    private readonly List<LiveGrenade> _grenades = [];
    private readonly List<GrenadeDetonation> _detonations = [];

    /// <summary>Gets the grenades in flight or at rest.</summary>
    public IReadOnlyList<LiveGrenade> Grenades => _grenades;

    /// <summary>Gets what the last <see cref="Tick"/> detonated, for the owner to apply.</summary>
    public IReadOnlyList<GrenadeDetonation> Detonations => _detonations;

    /// <summary>Puts one thrown grenade into flight.</summary>
    /// <param name="origin">Where it leaves the hand, in world units.</param>
    /// <param name="direction">The throw direction, unit length.</param>
    /// <param name="speedMetresPerSecond">The weapon's muzzle velocity — fifteen for both shipped grenades.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">The fuse; the retail four seconds by default.</param>
    /// <returns><see langword="false"/> when the pool is full.</returns>
    public bool Spawn(
        Vector3d origin,
        Vector3f direction,
        float speedMetresPerSecond,
        GrenadeExplosionType type,
        int fuseTicks = GrenadePhysics.FuseTicks)
    {
        float unitsPerTick = speedMetresPerSecond * ExplosionDamage.UnitsPerMetre / 30f;
        return SpawnWithVelocity(origin, direction * unitsPerTick, type, fuseTicks);
    }

    /// <summary>Puts a grenade into flight with an explicit velocity — the AI's computed lob.</summary>
    /// <param name="origin">Where it leaves the hand.</param>
    /// <param name="velocityUnitsPerTick">The initial velocity, in world units per tick.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">The fuse.</param>
    /// <returns><see langword="false"/> when the pool is full.</returns>
    public bool SpawnWithVelocity(
        Vector3d origin,
        Vector3f velocityUnitsPerTick,
        GrenadeExplosionType type,
        int fuseTicks = GrenadePhysics.FuseTicks)
    {
        if (_grenades.Count >= MaximumLive)
        {
            return false;
        }

        _grenades.Add(new LiveGrenade(origin, velocityUnitsPerTick, type, Math.Max(0, fuseTicks)));
        return true;
    }

    /// <summary>Advances every grenade by one game tick.</summary>
    /// <param name="walls">What the world stops a grenade with, or <see langword="null"/> for open air.</param>
    public void Tick(WorldRayCast? walls)
    {
        _detonations.Clear();

        for (int i = _grenades.Count - 1; i >= 0; i--)
        {
            LiveGrenade grenade = _grenades[i];

            if (--grenade.FuseTicks <= 0)
            {
                // The shipped tags: factor 1, radius five metres, falloff zero -- the same literals
                // the update passes when a level declares no block, so the two paths agree.
                _detonations.Add(new GrenadeDetonation(
                    grenade.Position,
                    grenade.Type,
                    ExplosionDamage.DefaultGrenadeDamageFactor,
                    ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeRadiusMetres),
                    ExplosionDamage.ToUnits(ExplosionDamage.DefaultGrenadeFalloffMetres)));
                _grenades.RemoveAt(i);
                continue;
            }

            if (grenade.Resting)
            {
                grenade.PreviousPosition = grenade.Position;
                continue;
            }

            grenade.PreviousPosition = grenade.Position;
            grenade.TumbleTicks++;

            Vector3f velocity = new(
                grenade.Velocity.X,
                grenade.Velocity.Y,
                grenade.Velocity.Z - GrenadePhysics.GravityPerTick);

            Vector3d candidate = new(
                grenade.Position.X + velocity.X,
                grenade.Position.Y + velocity.Y,
                grenade.Position.Z + velocity.Z);

            WorldRayHit hit = default;
            if (walls is not null && walls(grenade.Position, candidate, out hit))
            {
                Vector3f normal = hit.Normal;
                float alongNormal = (velocity.X * normal.X) + (velocity.Y * normal.Y) + (velocity.Z * normal.Z);

                // Reflect the normal component at the read 0.3 elasticity; the tangential part keeps
                // most of its speed. The five-millimetre step-off is the shot loop's own, and stops
                // the next tick's cast starting inside the face it just left.
                Vector3f reflected = new(
                    (velocity.X - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.X)) * GrenadePhysics.TangentialKeep,
                    (velocity.Y - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.Y)) * GrenadePhysics.TangentialKeep,
                    (velocity.Z - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.Z)) * GrenadePhysics.TangentialKeep);

                grenade.Position = new Vector3d(
                    hit.Position.X + (normal.X * GunShotBallistics.PenetrationStepOffUnits),
                    hit.Position.Y + (normal.Y * GunShotBallistics.PenetrationStepOffUnits),
                    hit.Position.Z + (normal.Z * GunShotBallistics.PenetrationStepOffUnits));

                float speed = MathF.Sqrt(
                    (reflected.X * reflected.X) + (reflected.Y * reflected.Y) + (reflected.Z * reflected.Z));

                if (speed < GrenadePhysics.RestSpeedUnitsPerTick && normal.Z > 0.7f)
                {
                    grenade.Resting = true;
                    grenade.Velocity = default;
                }
                else
                {
                    grenade.Velocity = reflected;
                }
            }
            else
            {
                grenade.Position = candidate;
                grenade.Velocity = velocity;
            }
        }
    }

    /// <summary>Removes every grenade, as changing level does.</summary>
    public void Clear()
    {
        _grenades.Clear();
        _detonations.Clear();
    }
}
