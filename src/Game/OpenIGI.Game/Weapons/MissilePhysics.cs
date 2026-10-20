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
/// <see cref="SourceWeaponId"/> is the firing weapon's id: the missile keeps its weapon record
/// (<c>+0x894</c>, from the spawn message's <c>+0x20</c>) and hands it to <c>Explosion0</c> as the blast's
/// source (<c>+0x84</c>). A car's explosion handler (<c>0x42FB40</c>) reads that record's first field, the
/// weapon id, and only the LAW 80's destroys it.
/// </remarks>
public readonly record struct MissileDetonation(
    Vector3d Position,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits,
    Vector3f Normal = default,
    GameTask? DirectHitOwner = null,
    WorldPlacement? DirectHitPlacement = null,
    int SourceWeaponId = 0);

/// <summary>Tests one missile segment and preserves the exact world object struck.</summary>
public delegate bool MissileWorldTraceCast(
    LiveMissile missile,
    Vector3d origin,
    Vector3d end,
    out WorldTraceHit hit);

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
    /// <summary>Gets the id of the weapon that fired it — the weapon record the task keeps at <c>+0x894</c>.</summary>
    public int SourceWeaponId { get; init; }
    /// <summary>Gets or sets the throttle, ramped from launch toward one (<c>0x00428D00</c>).</summary>
    public float Throttle { get; set; }
    /// <summary>Gets or sets how many ticks of motor burn remain (<c>a1+2148</c>).</summary>
    public int MotorRemainingTicks { get; set; }
    /// <summary>Gets or sets the missile's age in ticks.</summary>
    public int Age { get; set; }
    /// <summary>Gets whether the motor is still burning — thrusting and laying smoke.</summary>
    public bool Powered => MotorRemainingTicks > 0;

    /// <summary>Gets or sets its body, when it flies as the original's missile does; <see langword="null"/> for one that does not.</summary>
    public RetailMissileBody? Body { get; set; }
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
    private int _tick;

    /// <summary>Gets the missiles currently in flight.</summary>
    public IReadOnlyList<LiveMissile> Missiles => _missiles;
    /// <summary>Gets the detonations emitted during the most recent tick.</summary>
    public IReadOnlyList<MissileDetonation> Detonations => _detonations;

    /// <summary>
    /// Gets or sets what the missiles' contact points are cast through, the world the cars step in; <see langword="null"/> flies them
    /// the simple way.
    /// </summary>
    public Func<IRetailCarWorld?>? World { get; set; }

    /// <summary>
    /// Gets or sets what fires a missile's body from its model's physics type (<c>0x429A30</c>): the model, where it leaves, the
    /// launcher's frame, the launcher's speed in metres a second and where it was fired at. <see langword="null"/>, or no body, flies
    /// it the simple way.
    /// </summary>
    public Func<string, Vector3d, Matrix3f, float, Vector3d, RetailMissileBody?>? Bodies { get; set; }

    /// <summary>Spawns one missile from an authored launcher binding.</summary>
    /// <param name="origin">The muzzle position in engine units.</param>
    /// <param name="target">The aim point in engine units.</param>
    /// <param name="type">The native missile flight type.</param>
    /// <param name="muzzleVelocityMetresPerSecond">The authored launch speed; a positive value gates the launch.</param>
    /// <param name="launcherAxis">The launcher's forward axis.</param>
    /// <param name="modelName">The authored projectile model name.</param>
    /// <param name="config">The flight parameters; the type's retail preset when omitted.</param>
    /// <param name="collisionOwner">The firing task excluded from this missile's world trace.</param>
    /// <param name="sourceWeaponId">The id of the weapon that fired it, carried to the blast.</param>
    /// <param name="launcherMetresPerSecond">
    /// The launcher's own speed, which the original's missile leaves at and its motor builds on; <see langword="null"/> for
    /// <paramref name="muzzleVelocityMetresPerSecond"/>.
    /// </param>
    /// <returns><see langword="true"/> when the missile entered the simulation.</returns>
    public bool Spawn(
        Vector3d origin,
        Vector3d target,
        MissileType type,
        float muzzleVelocityMetresPerSecond,
        Vector3f launcherAxis = default,
        string modelName = "110_02_1",
        MissileConfig? config = null,
        GameTask? collisionOwner = null,
        int sourceWeaponId = 0,
        float? launcherMetresPerSecond = null)
    {
        if (_missiles.Count >= MaximumLive || muzzleVelocityMetresPerSecond <= 0f)
        {
            return false;
        }

        // The original's missile: its own type's body, fired facing the launcher at the launcher's own speed.
        Vector3f axis = launcherAxis == default ? (target - origin).ToVector3f().ReciprocalNormalizedOrSelf(float.Epsilon) : launcherAxis;
        if (Bodies?.Invoke(modelName, origin, LauncherFrame(axis), launcherMetresPerSecond ?? muzzleVelocityMetresPerSecond, target) is { } body)
        {
            _missiles.Add(new LiveMissile(
                _nextId++,
                origin,
                new Vector3f(body.Velocity[0], body.Velocity[1], body.Velocity[2]),
                target,
                config ?? MissileConfig.ForType(type),
                modelName,
                collisionOwner)
            {
                SourceWeaponId = sourceWeaponId,
                Body = body,
            });
            return true;
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
            _nextId++, origin, velocity, target, config, modelName, collisionOwner)
        {
            SourceWeaponId = sourceWeaponId,
        });
        return true;
    }

    /// <summary>Advances every missile one fixed simulation tick.</summary>
    /// <param name="walls">The world cast, including terrain where available.</param>
    /// <param name="humans">A non-mutating human intersection used for nearest-hit resolution.</param>
    public void Tick(WorldRayCast? walls, HumanRayCast? humans)
    {
        MissileWorldTraceCast? contextualWalls = walls is null
            ? null
            : (LiveMissile _, Vector3d from, Vector3d to, out WorldTraceHit hit) =>
            {
                bool found = walls(from, to, out WorldRayHit rayHit);
                hit = found
                    ? WorldTraceHit.FromRayHit(
                        new WorldTraceRequest(from, to, WorldTraceMask.World),
                        rayHit,
                        WorldTraceHitKind.StaticObject)
                    : default;
                return found;
            };
        TickWithMissileContext(contextualWalls, humans);
    }

    /// <summary>Advances missiles while exposing each launch owner to the world collision query.</summary>
    public void TickWithMissileContext(MissileWorldTraceCast? walls, HumanRayCast? humans)
    {
        _detonations.Clear();

        _tick++;
        IRetailCarWorld? world = World?.Invoke();
        for (int i = _missiles.Count - 1; i >= 0; i--)
        {
            LiveMissile missile = _missiles[i];
            missile.PreviousPosition = missile.Position;
            missile.Age++;
            if (missile.Body is { } body && world is not null)
            {
                if (Fly(missile, body, world, humans))
                {
                    _missiles.RemoveAt(i);
                }

                continue;
            }

            Advance(missile);

            Vector3d candidate = new(
                missile.Position.X + missile.Velocity.X,
                missile.Position.Y + missile.Velocity.Y,
                missile.Position.Z + missile.Velocity.Z);

            WorldTraceHit wallHit = default;
            bool struckWall = walls is not null
                && walls(missile, missile.Position, candidate, out wallHit);
            HumanHit humanHit = default;
            bool struckHuman = humans is not null
                && humans(missile.Position, candidate, out humanHit)
                && (!struckWall || humanHit.Distance < wallHit.Distance);

            if (struckHuman || struckWall)
            {
                // The scorch faces off the surface for a wall, or back along the flight for a body.
                Vector3f normal = struckHuman ? (missile.Velocity * -1f).ReciprocalNormalizedOrSelf(float.Epsilon) : wallHit.Normal;
                _detonations.Add(new MissileDetonation(
                    struckHuman ? humanHit.Position : wallHit.Position,
                    MissileExplosion.DamageFactor,
                    MissileExplosion.RadiusUnits,
                    MissileExplosion.FalloffUnits,
                    normal,
                    struckHuman ? null : wallHit.Owner,
                    struckHuman ? null : wallHit.Placement,
                    missile.SourceWeaponId));
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
    // 0x428390 after the move: struck, it goes off where it is; every thirty-two ticks one with nothing within a thousand metres
    // below it is gone. A human its path crossed is struck too, which the cars' world, holding no humans, cannot say.
    private bool Fly(LiveMissile missile, RetailMissileBody body, IRetailCarWorld world, HumanRayCast? humans)
    {
        body.Tick(world);
        Vector3d at = new(body.Position[0], body.Position[1], body.Position[2]);
        missile.Velocity = new Vector3f(body.Velocity[0], body.Velocity[1], body.Velocity[2]);
        HumanHit humanHit = default;
        bool struckHuman = humans is not null && humans(missile.Position, at, out humanHit);
        missile.Position = at;
        if (body.IsStruck || struckHuman)
        {
            _detonations.Add(new MissileDetonation(
                struckHuman ? humanHit.Position : at,
                MissileExplosion.DamageFactor,
                MissileExplosion.RadiusUnits,
                MissileExplosion.FalloffUnits,
                (missile.Velocity * -1f).ReciprocalNormalizedOrSelf(float.Epsilon),
                SourceWeaponId: missile.SourceWeaponId));
            return true;
        }

        if ((_tick & 0x1F) == 0)
        {
            Span<RetailRayResult> below = new RetailRayResult[1];
            world.CastRays(body, [at], [new Vector3d(at.X, at.Y, at.Z - 4096000.0)], below);
            return !below[0].Hit;
        }

        return false;
    }

    /// <summary>The launcher's frame for a direction: its forward column the direction, its side level, as a view is built.</summary>
    /// <param name="forward">The unit direction.</param>
    /// <returns>The frame, <c>Z(yaw) X(pitch)</c>.</returns>
    public static Matrix3f LauncherFrame(Vector3f forward)
    {
        float pitch = MathF.Asin(Math.Clamp(forward.Z, -1f, 1f));
        float yaw = MathF.Atan2(-forward.X, forward.Y);
        return Matrix3f.FromEngineEulerAngles(pitch, 0f, yaw);
    }

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

        Vector3f facing = missile.Velocity.ReciprocalNormalizedOrSelf(float.Epsilon);

        // Guided types re-aim at the type's loft/dive point each tick; Direct and keyboard rockets hold
        // the launch line their steering was cleared to.
        if (config.Type is MissileType.MissileBallistic
            or MissileType.MissileAntiTank
            or MissileType.HindHelicopter)
        {
            float speed = missile.Velocity.Length;
            if (speed > 0f)
            {
                Vector3f steered = MissileFlight.LaunchVelocity(
                    missile.Position,
                    missile.Target,
                    config.Type,
                    speed * MissileFlight.TicksPerSecond / MissileFlight.UnitsPerMetre);
                if (steered != default)
                {
                    facing = steered.ReciprocalNormalizedOrSelf(float.Epsilon);
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
        float currentSpeed = missile.Velocity.Length;

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

    /// <summary>Removes all live missiles and pending detonations.</summary>
    public void Clear()
    {
        _missiles.Clear();
        _detonations.Clear();
    }
}
