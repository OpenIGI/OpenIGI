using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using System.Collections.Generic;

namespace OpenIGI.Game.Weapons;

/// <summary>A missile impact produced by the retail missile task.</summary>
/// <remarks>
/// <see cref="Normal"/> is the struck surface's, for the scorch decal the retail impact projects onto
/// the object it hits (<c>burn1.tex</c>, <c>dword_5C1164</c>); a body impact faces back along the
/// missile's own path.
/// </remarks>
public readonly record struct MissileDetonation(
    Vector3d Position,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits,
    Vector3f Normal = default);

/// <summary>Tests one missile segment against the world with access to that missile's launch owner.</summary>
public delegate bool MissileWorldRayCast(
    LiveMissile missile,
    Vector3d origin,
    Vector3d end,
    out WorldRayHit hit);

/// <summary>A missile in flight, before its impact explosion.</summary>
public sealed class LiveMissile
{
    /// <summary>Initializes one missile at its launch position.</summary>
    /// <param name="id">A stable identity the presentation layer tracks its trail by.</param>
    /// <param name="position">The launch position in engine units.</param>
    /// <param name="velocity">The launch velocity in engine units per tick.</param>
    /// <param name="target">The launch aim point in engine units.</param>
    /// <param name="config">The retail flight parameters for this missile's guidance type.</param>
    /// <param name="modelName">The authored projectile model name.</param>
    /// <param name="collisionOwner">The firing task excluded from this missile's world trace.</param>
    public LiveMissile(
        long id,
        Vector3d position,
        Vector3f velocity,
        Vector3d target,
        MissileConfig config,
        string modelName,
        GameTask? collisionOwner = null)
    {
        Id = id;
        Position = position;
        PreviousPosition = position;
        Velocity = velocity;
        Target = target;
        Config = config;
        ModelName = modelName;
        CollisionOwner = collisionOwner;
        MotorRemainingTicks = config.MotorBurnTicks;
    }

    /// <summary>Gets the identity the smoke trail is keyed by; unique for the simulation's lifetime.</summary>
    public long Id { get; }
    /// <summary>Gets or sets the current position in engine units.</summary>
    public Vector3d Position { get; set; }
    /// <summary>Gets or sets the position at the previous simulation tick.</summary>
    public Vector3d PreviousPosition { get; set; }
    /// <summary>Gets or sets the current fixed-tick velocity in engine units.</summary>
    public Vector3f Velocity { get; set; }
    /// <summary>Gets the aim point selected at launch.</summary>
    public Vector3d Target { get; }
    /// <summary>Gets the retail flight parameters for this missile.</summary>
    public MissileConfig Config { get; }
    /// <summary>Gets the retail missile type from the task config <c>+0x11c</c> switch.</summary>
    public MissileType Type => Config.Type;
    /// <summary>Gets the authored model used by the projectile renderer.</summary>
    public string ModelName { get; }
    /// <summary>Gets the firing task excluded from this missile's own world trace.</summary>
    public GameTask? CollisionOwner { get; }
    /// <summary>Gets or sets the throttle, ramped from launch toward one (<c>0x00428D00</c>).</summary>
    public float Throttle { get; set; }
    /// <summary>Gets or sets how many ticks of motor burn remain (<c>a1+2148</c>).</summary>
    public int MotorRemainingTicks { get; set; }
    /// <summary>Gets or sets the missile's age in ticks.</summary>
    public int Age { get; set; }
    /// <summary>Gets whether the motor is still burning — thrusting and laying smoke.</summary>
    public bool Powered => MotorRemainingTicks > 0;
}

/// <summary>Advances launcher missiles and emits an explosion when the missile meets the world.</summary>
/// <remarks>
/// Retail <c>0x00428390</c> steers every tick from the task type, <c>0x00428D00</c> ramps a throttle and
/// drives the body forward under thrust, and <c>0x00428630</c> lays smoke while the motor burns. A
/// <see cref="MissileType.MissileDirect"/> rocket (the LAW 80 / RPG-18) has its steering cleared at
/// <c>0x00428910</c>, so it holds the launch line and never falls under grenade gravity.
/// </remarks>
public sealed class MissileSimulation
{
    /// <summary>Gets the maximum number of live launcher missiles.</summary>
    public const int MaximumLive = 16;

    /// <summary>Maximum open-air lifetime before a missile is discarded.</summary>
    /// <remarks>The retail task cannot occupy the fixed-size live-missile pool indefinitely.</remarks>
    public const int MaximumLifetimeTicks = 30 * 30;

    /// <summary>Gravity per tick², in world units — the ragdoll's own 9.8 m/s², for falling bombs.</summary>
    private const float GravityPerTick = 44.600887f;

    private readonly List<LiveMissile> _missiles = [];
    private readonly List<MissileDetonation> _detonations = [];
    private long _nextId;

    /// <summary>Gets the missiles currently in flight.</summary>
    public IReadOnlyList<LiveMissile> Missiles => _missiles;
    /// <summary>Gets the detonations emitted during the most recent tick.</summary>
    public IReadOnlyList<MissileDetonation> Detonations => _detonations;

    /// <summary>Spawns one missile from an authored launcher binding.</summary>
    /// <param name="origin">The muzzle position in engine units.</param>
    /// <param name="target">The aim point in engine units.</param>
    /// <param name="type">The native missile flight type.</param>
    /// <param name="muzzleVelocityMetresPerSecond">The authored launch speed; a positive value gates the launch.</param>
    /// <param name="launcherAxis">The launcher's forward axis.</param>
    /// <param name="modelName">The authored projectile model name.</param>
    /// <param name="config">The flight parameters; the type's retail preset when omitted.</param>
    /// <param name="collisionOwner">The firing task excluded from this missile's world trace.</param>
    /// <returns><see langword="true"/> when the missile entered the simulation.</returns>
    public bool Spawn(
        Vector3d origin,
        Vector3d target,
        MissileType type,
        float muzzleVelocityMetresPerSecond,
        Vector3f launcherAxis = default,
        string modelName = "110_02_1",
        MissileConfig? config = null,
        GameTask? collisionOwner = null)
    {
        if (_missiles.Count >= MaximumLive || muzzleVelocityMetresPerSecond <= 0f)
        {
            return false;
        }

        config ??= MissileConfig.ForType(type);

        // The rocket leaves the tube at the config's launch speed and thrust builds it up from there; the
        // dummy 5 m/s the weapon table carries to keep carriers out of g_ballistics is not a real speed.
        Vector3f velocity = MissileFlight.LaunchVelocity(
            origin, target, type, config.LaunchMetresPerSecond, launcherAxis);
        if (velocity == default && !config.Gravity)
        {
            return false;
        }

        _missiles.Add(new LiveMissile(
            _nextId++, origin, velocity, target, config, modelName, collisionOwner));
        return true;
    }

    /// <summary>Advances every missile one fixed simulation tick.</summary>
    /// <param name="walls">The world cast, including terrain where available.</param>
    /// <param name="humans">A non-mutating human intersection used for nearest-hit resolution.</param>
    public void Tick(WorldRayCast? walls, HumanRayCast? humans)
    {
        MissileWorldRayCast? contextualWalls = walls is null
            ? null
            : (LiveMissile _, Vector3d from, Vector3d to, out WorldRayHit hit) =>
                walls(from, to, out hit);
        TickWithMissileContext(contextualWalls, humans);
    }

    /// <summary>Advances missiles while exposing each launch owner to the world collision query.</summary>
    public void TickWithMissileContext(MissileWorldRayCast? walls, HumanRayCast? humans)
    {
        _detonations.Clear();

        for (int i = _missiles.Count - 1; i >= 0; i--)
        {
            LiveMissile missile = _missiles[i];
            missile.PreviousPosition = missile.Position;
            missile.Age++;

            Advance(missile);

            Vector3d candidate = new(
                missile.Position.X + missile.Velocity.X,
                missile.Position.Y + missile.Velocity.Y,
                missile.Position.Z + missile.Velocity.Z);

            WorldRayHit wallHit = default;
            bool struckWall = walls is not null
                && walls(missile, missile.Position, candidate, out wallHit);
            HumanHit humanHit = default;
            bool struckHuman = humans is not null
                && humans(missile.Position, candidate, out humanHit)
                && (!struckWall || humanHit.Distance < wallHit.Distance);

            if (struckHuman || struckWall)
            {
                // The scorch faces off the surface for a wall, or back along the flight for a body.
                Vector3f normal = struckHuman ? Normalize(missile.Velocity * -1f) : wallHit.Normal;
                _detonations.Add(new MissileDetonation(
                    struckHuman ? humanHit.Position : wallHit.Position,
                    MissileExplosion.DamageFactor,
                    MissileExplosion.RadiusUnits,
                    MissileExplosion.FalloffUnits,
                    normal));
                _missiles.RemoveAt(i);
                continue;
            }

            if (missile.Age >= MaximumLifetimeTicks)
            {
                _missiles.RemoveAt(i);
                continue;
            }

            missile.Position = candidate;
        }
    }

    /// <summary>
    /// The per-tick integrator (<c>0x00428D00</c>): re-point a guided body at its aim, ramp the throttle,
    /// drive the speed to cruise under thrust and hold it there with drag. Direct rockets keep their
    /// launch line; a bomb falls.
    /// </summary>
    private static void Advance(LiveMissile missile)
    {
        MissileConfig config = missile.Config;

        if (config.Gravity)
        {
            // The one falling body: no thrust, gravity integrates onto the descent.
            Vector3f v = missile.Velocity;
            missile.Velocity = new Vector3f(
                v.X * config.Drag,
                v.Y * config.Drag,
                (v.Z - GravityPerTick) * config.Drag);
            if (missile.MotorRemainingTicks > 0)
            {
                missile.MotorRemainingTicks--;
            }

            return;
        }

        Vector3f facing = Normalize(missile.Velocity);

        // Guided types re-aim at the type's loft/dive point each tick; Direct and keyboard rockets hold
        // the launch line their steering was cleared to.
        if (config.Type is MissileType.MissileBallistic
            or MissileType.MissileAntiTank
            or MissileType.HindHelicopter)
        {
            float speed = Length(missile.Velocity);
            if (speed > 0f)
            {
                Vector3f steered = MissileFlight.LaunchVelocity(
                    missile.Position,
                    missile.Target,
                    config.Type,
                    speed * MissileFlight.TicksPerSecond / MissileFlight.UnitsPerMetre);
                if (steered != default)
                {
                    facing = Normalize(steered);
                }
            }
        }

        bool powered = missile.MotorRemainingTicks > 0;
        if (powered)
        {
            float rate = missile.Throttle >= 0.7f ? config.ThrottleRampFast : config.ThrottleRampSlow;
            missile.Throttle = MathF.Min(1f, missile.Throttle + rate);
            missile.MotorRemainingTicks--;
        }

        float cruiseUnitsPerTick = config.CruiseMetresPerSecond
            * MissileFlight.UnitsPerMetre / MissileFlight.TicksPerSecond;
        float currentSpeed = Length(missile.Velocity);

        // While the motor burns, thrust drives the speed up toward cruise — faster as the throttle
        // climbs — so the rocket visibly accelerates away from the tube. After burnout it coasts on drag.
        float newSpeed;
        if (powered)
        {
            float response = missile.Throttle * (1f - config.Drag);
            newSpeed = currentSpeed + ((cruiseUnitsPerTick - currentSpeed) * response);
        }
        else
        {
            newSpeed = currentSpeed * config.Drag;
        }

        missile.Velocity = new Vector3f(
            facing.X * newSpeed,
            facing.Y * newSpeed,
            facing.Z * newSpeed);
    }

    private static float Length(Vector3f v) =>
        MathF.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static Vector3f Normalize(Vector3f v)
    {
        float length = Length(v);
        return length > float.Epsilon ? v * (1f / length) : v;
    }

    /// <summary>Removes all live missiles and pending detonations.</summary>
    public void Clear()
    {
        _missiles.Clear();
        _detonations.Clear();
    }
}
