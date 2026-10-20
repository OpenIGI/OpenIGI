using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>A lifting or steadying surface on a vehicle, <c>Wing_t</c>: its area, its normal and where it acts, in the vehicle's frame.</summary>
/// <param name="Area">Its area, <c>vArea</c>; nought for a wing that does nothing.</param>
/// <param name="Normal">Its unit normal, <c>tNormal</c>.</param>
/// <param name="Position">Where its force acts, <c>tPos</c>.</param>
public readonly record struct Igi2Wing(float Area, Vector3f Normal, Vector3f Position)
{
    /// <summary>Makes a wing, <c>Wing_Init</c> (<c>igi2 0x5A4A10</c>): the normal made unit unless it is nought.</summary>
    /// <param name="area">Its area.</param>
    /// <param name="position">Where it acts.</param>
    /// <param name="normal">Its normal, any length.</param>
    /// <returns>The wing.</returns>
    public static Igi2Wing Create(float area, Vector3f position, Vector3f normal)
    {
        if (normal.X != 0f || normal.Y != 0f || normal.Z != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((Fpu)normal.X * normal.X) + ((Fpu)normal.Y * normal.Y) + ((Fpu)normal.Z * normal.Z));
            normal = new Vector3f(((Fpu)normal.X * inverse).F, ((Fpu)normal.Y * inverse).F, ((Fpu)normal.Z * inverse).F);
        }

        return new Igi2Wing(area, normal, position);
    }
}

/// <summary>
/// What a missile's physics type says of it: the vehicle's handling (<c>VehicleType_DefineVehicleType</c>, <c>igi2 0x5A51E0</c>)
/// then the missile's own (<c>Missile_ParsePhysicsObjTypeHandler</c>, <c>0x52EE70</c>), read from the type's own arguments in order.
/// </summary>
public sealed class Igi2MissileType
{
    private Igi2MissileType()
    {
    }

    /// <summary>Gets how far it may pitch a tick at full deflection, degrees, <c>vMaxDeltaAlpha</c>.</summary>
    public float MaxDeltaAlpha { get; private init; }

    /// <summary>Gets how much of its last pitch rate it keeps, <c>vDeltaAlphaInertia</c>.</summary>
    public float DeltaAlphaInertia { get; private init; }

    /// <summary>Gets how much of its last roll rate it keeps, <c>vDeltaBetaInertia</c>.</summary>
    public float DeltaBetaInertia { get; private init; }

    /// <summary>Gets how far it may turn a tick at full deflection, degrees, <c>vMaxDeltaGamma</c>.</summary>
    public float MaxDeltaGamma { get; private init; }

    /// <summary>Gets how much of its last turn rate it keeps, <c>vDeltaGammaInertia</c>.</summary>
    public float DeltaGammaInertia { get; private init; }

    /// <summary>Gets its motor's full thrust, <c>vMaxThrust</c>.</summary>
    public float MaxThrust { get; private init; }

    /// <summary>Gets how fast its thrust changes near full, <c>vThrustInertia</c>.</summary>
    public float ThrustInertia { get; private init; }

    /// <summary>Gets how fast its thrust changes starting or stopping, <c>vThrustStartStopInertia</c>.</summary>
    public float ThrustStartStopInertia { get; private init; }

    /// <summary>Gets how many of its wings the air acts on, <c>nWing</c>.</summary>
    public int WingCount { get; private init; }

    /// <summary>Gets its wings as defined.</summary>
    public IReadOnlyList<Igi2Wing> Wings { get; private init; } = [];

    /// <summary>Gets whether it smokes, <c>MISSILE_SMOKE_*</c>.</summary>
    public int Smoke { get; private init; }

    /// <summary>Gets how it steers, <c>MISSILE_TYPE_*</c>.</summary>
    public int Guidance { get; private init; }

    /// <summary>Gets its fuel when fired.</summary>
    public float Fuel { get; private init; }

    /// <summary>Gets the fuel it burns a tick: its rate a second over thirty.</summary>
    public float Burn { get; private init; }

    /// <summary>Gets its smoke's size when made, units.</summary>
    public float SmokeSize { get; private init; }

    /// <summary>Gets its smoke's size when gone, units.</summary>
    public float SmokeEndSize { get; private init; }

    /// <summary>Gets the ticks between its puffs of smoke.</summary>
    public float SmokeDensity { get; private init; }

    /// <summary>Gets a puff's life, seconds.</summary>
    public float SmokeLifeTime { get; private init; }

    /// <summary>Gets its smoke's sprite.</summary>
    public string SmokeSprite { get; private init; } = string.Empty;

    /// <summary>Gets the sound it flies with.</summary>
    public string Sound { get; private init; } = string.Empty;

    /// <summary>Reads a physics type's arguments as a missile's.</summary>
    /// <param name="type">The physics type.</param>
    /// <returns>The missile type, or <see langword="null"/> when its arguments are too few.</returns>
    public static Igi2MissileType? Of(PhysicsObjectType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        IReadOnlyList<OpenIGI.Scripting.Runtime.QvmValue> p = type.Parameters;
        if (p.Count < 11)
        {
            return null;
        }

        int wings = p[10].ToInt32();
        int at = 11 + (7 * Math.Max(0, wings)) + 3;
        if (p.Count < at + 11)
        {
            return null;
        }

        Igi2Wing[] defined = new Igi2Wing[Math.Max(0, wings)];
        for (int i = 0; i < defined.Length; i++)
        {
            int w = 11 + (7 * i);
            defined[i] = Igi2Wing.Create(
                p[w].ToSingle(),
                new Vector3f(((Fpu)p[w + 1].ToSingle() * 4096.0).F, ((Fpu)p[w + 2].ToSingle() * 4096.0).F, ((Fpu)p[w + 3].ToSingle() * 4096.0).F),
                new Vector3f(p[w + 4].ToSingle(), p[w + 5].ToSingle(), p[w + 6].ToSingle()));
        }

        return new Igi2MissileType
        {
            MaxDeltaAlpha = p[1].ToSingle(),
            DeltaAlphaInertia = p[2].ToSingle(),
            DeltaBetaInertia = p[4].ToSingle(),
            MaxDeltaGamma = p[5].ToSingle(),
            DeltaGammaInertia = p[6].ToSingle(),
            MaxThrust = p[7].ToSingle(),
            ThrustInertia = p[8].ToSingle(),
            ThrustStartStopInertia = p[9].ToSingle(),
            WingCount = wings,
            Wings = defined,
            Smoke = p[at].ToInt32(),
            Guidance = p[at + 1].ToInt32(),
            Fuel = p[at + 2].ToSingle(),
            Burn = ((Fpu)p[at + 3].ToSingle() * 0.03333333507180214).F,
            SmokeSize = ((Fpu)p[at + 5].ToSingle() * 4096.0).F,
            SmokeEndSize = ((Fpu)p[at + 6].ToSingle() * 4096.0).F,
            SmokeDensity = p[at + 7].ToSingle(),
            SmokeLifeTime = p[at + 8].ToSingle(),
            SmokeSprite = p[at + 9].ToText(),
            Sound = p[at + 10].ToText(),
        };
    }
}

/// <summary>
/// A missile in flight, <c>Missile_s</c>: a physics object pushed by its motor, steadied by its fins, steering as its type says,
/// and set off by the first thing it touches.
/// </summary>
/// <remarks>
/// <para>
/// Fired (<c>Missile_New</c>, <c>igi2 0x52F1D0</c>) as its weapon's <c>Missile - Model</c>, along the shot's forward column at the gun's
/// speed, its thrust full, its fuel its type's; a missile that steers straight aims four thousand metres ahead.
/// </para>
/// <para>
/// Each tick (<c>Missile_RunHandler</c>, <c>0x52E620</c>, and <c>Missile_Move</c>, <c>0x52DB70</c>): it steers, smokes while it has fuel,
/// turns by its smoothed pitch and turn rates, wobbles once its fuel is out, is pushed by its motor while it has fuel and burns some,
/// takes the air on its fins and moves as a physics object whose touch a weapon's line would make. Touching anything it goes off where
/// it is, over what lies under it; and every thirty-two ticks one with nothing at all beneath it is gone.
/// </para>
/// <para>
/// Its wobble and smoke draw from the missiles' own stream (<c>Missile_tRandomState</c>), never the run stream. The sound it flies
/// with is the presentation's.
/// </para>
/// </remarks>
public sealed class Igi2Missile : IIgi2DynamicTask
{
    /// <summary>The gravity a missile falls by, a tick squared.</summary>
    public const float Gravity = 44.600887298583984f;

    /// <summary>A metre a second, in units a tick.</summary>
    public const double MetresPerSecond = 136.53334045410156;

    /// <summary>Its blast's fragments' model.</summary>
    public const string FragmentModel = "1009_01_1";

    /// <summary>Its blast's sound.</summary>
    public const string BlastSound = "explo_03_l";

    /// <summary>The steering of a missile that flies straight, <c>MISSILE_TYPE_MISSILE_DIRECT</c>.</summary>
    public const int Direct = 0;

    /// <summary>A helicopter's missile, <c>MISSILE_TYPE_HIND_HELICOPTER</c>.</summary>
    public const int Hind = 1;

    /// <summary>A missile that lobs, <c>MISSILE_TYPE_MISSILE_BALLISTIC</c>.</summary>
    public const int Ballistic = 2;

    /// <summary>A missile that climbs then lobs, <c>MISSILE_TYPE_MISSILE_ANTITANK</c>.</summary>
    public const int AntiTank = 3;

    private const double AirResistanceCoefficient = 0.00032699998700991273;

    private readonly Igi2Wing[] _wings = new Igi2Wing[8];
    private readonly Igi2LineTrace _below = new(1);
    private readonly Igi2ExplosionWorld _explosions;
    private readonly Igi2PhysicsWorld _world;
    private readonly IgiRandom _stream;
    private readonly IIgi2WeaponOwner? _shooter;
    private readonly Igi2WeaponTypeTask? _weaponType;
    private readonly Func<int> _levelTick;
    private readonly MefModel? _model;
    private readonly Igi2Particle2DSystem? _smoke;
    private readonly Vector3d _target;
    private float _realThrust = 1f;
    private float _realDeltaAlpha;
    private float _realDeltaBeta;
    private float _realDeltaGamma;
    private float _idealDeltaAlpha;
    private float _idealDeltaGamma;
    private int _smokeTick;

    /// <summary>Initializes a new instance of the <see cref="Igi2Missile"/> class: fired, <c>Missile_New</c>.</summary>
    /// <param name="launch">The shot.</param>
    /// <param name="model">The missile's model, its physics type's name.</param>
    /// <param name="type">Its physics type.</param>
    /// <param name="missile">Its physics type read as a missile's.</param>
    /// <param name="drawn">Its model, whose magic vertices its smoke leaves from and its contact points sit on.</param>
    /// <param name="world">What it flies through: the grenades' world, its line filter replaced by a missile's.</param>
    /// <param name="explosions">Where its blast goes off.</param>
    /// <param name="stream">The missiles' own stream.</param>
    /// <param name="levelTick">The level's tick, which times its look for ground.</param>
    public Igi2Missile(
        in Igi2ProjectileLaunch launch,
        string model,
        PhysicsObjectType type,
        Igi2MissileType missile,
        MefModel? drawn,
        Igi2PhysicsWorld world,
        Igi2ExplosionWorld explosions,
        IgiRandom stream,
        Func<int> levelTick)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(missile);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(explosions);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(levelTick);
        Model = model;
        Type = missile;
        _model = drawn;
        _explosions = explosions;
        _stream = stream;
        _levelTick = levelTick;
        _shooter = launch.Thrower;
        _weaponType = launch.WeaponType;
        IIgi2WeaponOwner? shooter = _shooter;
        Func<object, bool> lines = Igi2GunShot.WeaponCollisionIncludes(new WeaponCollisionQuery(shooter, shooter, 1f));
        _world = world with { Includes = owner => lines(owner) && owner is not Igi2Missile };

        float[] m = launch.Orientation;
        Body = new Igi2PhysicsObject(type, drawn, launch.Position, Vector3f.One);
        m.AsSpan(0, 9).CopyTo(Body.Orientation);
        Body.ResetLast();

        // Missile_InitWings: the type's wings, the first two mirrored across the missile as the third and fourth.
        for (int i = 0; i < missile.Wings.Count && i < _wings.Length; i++)
        {
            _wings[i] = missile.Wings[i];
        }

        _wings[2] = _wings[0] with { Position = new Vector3f(_wings[0].Position.X, -_wings[0].Position.Y, _wings[0].Position.Z) };
        _wings[3] = _wings[1] with { Position = new Vector3f(_wings[1].Position.X, -_wings[1].Position.Y, _wings[1].Position.Z) };
        Fuel = missile.Fuel;

        // A missile that flies straight aims along its forward column, four thousand metres off.
        Vector3d p = launch.Position;
        _target = missile.Guidance == Direct
            ? new Vector3d(((Fpu)m[1] * 4096000.0).Value + p.X, ((Fpu)m[4] * 4096000.0).Value + p.Y, ((Fpu)m[7] * 4096000.0).Value + p.Z)
            : new Vector3d(((Fpu)m[1] * 40960000.0).Value + p.X, ((Fpu)m[4] * 40960000.0).Value + p.Y, ((Fpu)m[7] * 40960000.0).Value + p.Z);

        Fpu speed = (Fpu)launch.Speed;
        Body.Body.Velocity = new Vector3f(
            (((Fpu)m[1] * speed) * MetresPerSecond).F,
            (((Fpu)m[4] * speed) * MetresPerSecond).F,
            (((Fpu)m[7] * speed) * MetresPerSecond).F);
        Body.Body.UpdateSpeedApproximation();

        // Missile_InitSmoke: a grey trail that grows and rises.
        if (missile.SmokeSize != 0f && missile.SmokeLifeTime != 0f)
        {
            float growth = (((Fpu)missile.SmokeEndSize / missile.SmokeSize - 1.0) / ((Fpu)missile.SmokeLifeTime * 30.0)).F;
            _smoke = explosions.Particles?.New(
                missile.SmokeSprite, false, 0, new Vector3f(0f, 0f, 0.02177777700126171f), new Vector3f(12.7f, 12.7f, 12.7f),
                missile.SmokeSize, missile.SmokeSize, 2048000f, 0x1000008, 0, growth, 0f, 2000, null);
        }
    }

    /// <summary>Gets its model.</summary>
    public string Model { get; }

    /// <summary>Gets its type.</summary>
    public Igi2MissileType Type { get; }

    /// <summary>Gets its physics object.</summary>
    public Igi2PhysicsObject Body { get; }

    /// <summary>Gets its fuel left, <c>vFuel</c>.</summary>
    public float Fuel { get; private set; }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position => Body.Position;

    /// <inheritdoc/>
    public bool Run()
    {
        Steer();
        if (Fuel != 0f && Type.Smoke != 0)
        {
            AddSmoke();
        }

        Move();
        if (Body.ContactCount != 0)
        {
            Vector3d at = Body.Position;
            Igi2Intersection ground = Below(at, -12288.0);
            bool ballistic = Type.Guidance == Ballistic;
            _explosions.New(
                at,
                1,
                1,
                ballistic ? 0.6f : 5f,
                ballistic ? 40960f : 8192f,
                ballistic ? 40960f : 12288f,
                6144f,
                10,
                5,
                ground,
                FragmentModel,
                BlastSound,
                20,
                0x402,
                307200f,
                _weaponType,
                _shooter,
                Igi2ExplosionFalloff.Linear);
            return false;
        }

        if ((_levelTick() & 0x1F) == 0 && !Below(Body.Position, -4096000.0).IsHit)
        {
            return false;
        }

        return true;
    }

    private Igi2Intersection Below(Vector3d at, double distance)
    {
        _below.Starts[0] = at;
        _below.Count = 1;
        _world.Query.NextZ(_below, [distance], uint.MaxValue);
        Igi2Intersection hit = _below.Intersections[0];
        return new Igi2Intersection
        {
            HitObject = hit.HitObject,
            Position = hit.Position,
            Normal = hit.Normal,
            IsHit = hit.IsHit,
            Distance = hit.Distance,
            EnterMaterial = hit.EnterMaterial,
            LeaveMaterial = hit.LeaveMaterial,
            Face = hit.Face,
        };
    }

    // Missile_RunHandler's steering: straight on, or toward the target as the type steers.
    private void Steer()
    {
        switch (Type.Guidance)
        {
            case Direct:
                _idealDeltaAlpha = 0f;
                _idealDeltaGamma = 0f;
                break;
            case Ballistic:
                Lob(_target);
                break;
            case AntiTank:
                Vector3d p = Body.Position;
                Fpu dx = (Fpu)_target.X - p.X, dy = (Fpu)_target.Y - p.Y;
                if (Fpu.Sqrt((dy * dy) + (dx * dx)).Value <= 122880.0)
                {
                    Lob(_target);
                }
                else
                {
                    TowardTarget(new Vector3d(_target.X, _target.Y, ((Fpu)_target.Z + 102400.0).Value));
                }

                break;
        }
    }

    // Missile_MoveBallisticCB: toward a point above the target by an eighth of the distance to it.
    private void Lob(Vector3d target)
    {
        Vector3d p = Body.Position;
        Fpu dx = (Fpu)target.X - p.X, dy = (Fpu)target.Y - p.Y, dz = (Fpu)target.Z - p.Z;
        double rise = (Fpu.Sqrt((dy * dy) + (dz * dz) + (dx * dx)) * 0.125).Value;
        TowardTarget(new Vector3d(target.X, target.Y, rise + target.Z));
    }

    // Missile_MoveTowardsTarget: the target in the missile's frame, its pitch and turn off the nose, each held to one.
    private void TowardTarget(Vector3d target)
    {
        Vector3d p = Body.Position;
        float[] m = Body.Orientation;
        double dx = target.X - p.X, dy = target.Y - p.Y, dz = target.Z - p.Z;
        double lx = (m[6] * dz) + (m[3] * dy) + (m[0] * dx);
        double ly = (m[7] * dz) + (m[4] * dy) + (m[1] * dx);
        double lz = (m[8] * dz) + (m[5] * dy) + (m[2] * dx);
        double alpha = Igi2Pose.CalcAlpha(ly, lz);
        double gamma = Igi2Pose.CalcGamma(lx, ly);
        _idealDeltaAlpha = (float)Math.Clamp(alpha, -1.0, 1.0);
        _idealDeltaGamma = (float)Math.Clamp(gamma, -1.0, 1.0);
    }

    // Missile_Move.
    private void Move()
    {
        Igi2MissileType t = Type;
        float inertia = _realThrust >= 0.699999988079071f ? t.ThrustInertia : t.ThrustStartStopInertia;
        Fpu thrust = _realThrust;
        const float ideal = 1f;
        if (thrust.Value < ideal)
        {
            thrust += inertia;
            if (thrust.Value > ideal)
            {
                thrust = ideal;
            }
        }
        else if (thrust.Value > ideal)
        {
            thrust -= inertia;
            if (thrust.Value < ideal)
            {
                thrust = ideal;
            }
        }

        if (thrust.Value < 0.0010000000474974513)
        {
            thrust = 0.0;
        }

        _realThrust = thrust.F;
        _realDeltaAlpha = (((Fpu)_idealDeltaAlpha * (1.0 - t.DeltaAlphaInertia)) + ((Fpu)t.DeltaAlphaInertia * _realDeltaAlpha)).F;
        _realDeltaBeta = (((Fpu)0f * (1.0 - t.DeltaBetaInertia)) + ((Fpu)t.DeltaBetaInertia * _realDeltaBeta)).F;
        _realDeltaGamma = (((Fpu)_idealDeltaGamma * (1.0 - t.DeltaGammaInertia)) + ((Fpu)t.DeltaGammaInertia * _realDeltaGamma)).F;
        float[] m = Body.Orientation;
        if (_realDeltaAlpha != 0f || _realDeltaBeta != 0f || _realDeltaGamma != 0f)
        {
            Fpu a = ((Fpu)_realDeltaAlpha * t.MaxDeltaAlpha) * 0.01745329238474369;
            Fpu g = ((Fpu)_realDeltaGamma * t.MaxDeltaGamma) * 0.01745329238474369;
            Fpu s = Fpu.Sin(a), c = Fpu.Cos(a);
            for (int row = 0; row < 9; row += 3)
            {
                float x = m[row], y = m[row + 1], z = m[row + 2];
                m[row] = ((((Fpu)z + y) * 0.0) + x).F;
                m[row + 1] = ((c * y) + (s * z) + ((Fpu)x * 0.0)).F;
                m[row + 2] = ((-s * y) + (c * z) + ((Fpu)x * 0.0)).F;
            }

            Fpu sg = Fpu.Sin(g), cg = Fpu.Cos(g);
            for (int row = 0; row < 9; row += 3)
            {
                float x = m[row], y = m[row + 1], z = m[row + 2];
                m[row] = ((cg * x) + (sg * y) + ((Fpu)z * 0.0)).F;
                m[row + 1] = ((-sg * x) + (cg * y) + ((Fpu)z * 0.0)).F;
                m[row + 2] = ((((Fpu)x + y) * 0.0) + z).F;
            }
        }

        if (Fuel == 0f)
        {
            Wobble(m);
        }

        if (_realThrust != 0f && Fuel != 0f)
        {
            float push = (((Fpu)t.MaxThrust * _realThrust) * 44.600887298583984).F;
            Body.Body.AddForce(new PointForce(new Vector3f(0f, push, 0f), default));
        }

        float fuel = ((Fpu)Fuel - t.Burn).F;
        Fuel = fuel < 0f ? 0f : fuel;
        if (!Body.IsIdle())
        {
            if (t.WingCount != 0)
            {
                AirOnWings(m);
            }

            Body.Update(Gravity, 0.949999988079071f, 0.9200000166893005f, _world);
        }
    }

    // The burnt-out wobble: three draws turn the missile by a little in each of its angles.
    private void Wobble(float[] m)
    {
        Fpu first = (Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625;
        Fpu second = (Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625;
        Fpu third = (Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625;
        float alpha = (((Fpu)0.5 - first) * 0.10000000149011612).F;
        float beta = (((Fpu)0.699999988079071 - second) * 0.10000000149011612).F;
        float gamma = (((Fpu)0.30000001192092896 - third) * 0.10000000149011612).F;
        float[] r = Igi2Pose.Rotation(alpha, beta, gamma);
        float[] turned = new float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                turned[(3 * row) + col] = ((((Fpu)r[6 + col] * m[(3 * row) + 2]) + ((Fpu)r[3 + col] * m[(3 * row) + 1])) + ((Fpu)r[col] * m[3 * row])).F;
            }
        }

        turned.CopyTo(m, 0);
    }

    // Wing_ApplyAirResistance: the air against the missile, in its frame, pushing on each wing along its normal.
    private void AirOnWings(float[] m)
    {
        Vector3f v = Body.Body.Velocity;
        float wx = -v.X, wy = -v.Y, wz = -v.Z;
        Vector3f local = new(
            ((((Fpu)m[6] * wz) + ((Fpu)m[3] * wy)) + ((Fpu)m[0] * wx)).F,
            ((((Fpu)m[7] * wz) + ((Fpu)m[1] * wx)) + ((Fpu)m[4] * wy)).F,
            ((((Fpu)m[8] * wz) + ((Fpu)m[2] * wx)) + ((Fpu)m[5] * wy)).F);
        Fpu length = Igi2RigidBody.FastLength(local);
        float coefficient = ((length * length) * AirResistanceCoefficient).F;
        if (!(coefficient > 0f))
        {
            return;
        }

        Vector3f dir = local;
        if (local.X != 0f || local.Y != 0f || local.Z != 0f)
        {
            Fpu inverse = (Fpu)1.0 / Fpu.Sqrt(((Fpu)local.X * local.X) + ((Fpu)local.Y * local.Y) + ((Fpu)local.Z * local.Z));
            dir = new Vector3f(((Fpu)local.X * inverse).F, ((Fpu)local.Y * inverse).F, ((Fpu)local.Z * inverse).F);
        }

        for (int i = 0; i < Type.WingCount && i < _wings.Length; i++)
        {
            Igi2Wing wing = _wings[i];
            if (!(wing.Area > 0f))
            {
                continue;
            }

            Vector3f n = wing.Normal;
            Fpu f = ((((Fpu)dir.Y * n.Y) + ((Fpu)dir.Z * n.Z) + ((Fpu)dir.X * n.X)) * wing.Area) * coefficient;
            Body.Body.AddForce(new PointForce(new Vector3f(((Fpu)n.X * f).F, ((Fpu)n.Y * f).F, ((Fpu)n.Z * f).F), wing.Position));
        }
    }

    // Missile_AddSmoke: every so many ticks a run of puffs along the last tick's path, six draws each.
    private void AddSmoke()
    {
        _smokeTick++;
        if (_smokeTick < (int)Type.SmokeDensity)
        {
            return;
        }

        _smokeTick = 0;
        float intensity = (((Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625 * 0.30000001192092896) + 0.15000000596046448).F;
        Vector3d from = SmokePoint();
        Igi2RigidBody body = Body.Body;
        int puffs = (int)(body.Speed / Type.SmokeSize);
        if (puffs <= 1)
        {
            puffs = 1;
        }

        Fpu share = (Fpu)1.0 / (double)puffs;
        Vector3f v = body.Velocity;
        double sx = (share * v.X).Value, sy = (share * v.Y).Value, sz = (share * v.Z).Value;
        for (int i = 0; i < puffs; i++)
        {
            _stream.NextRaw();
            _stream.NextRaw();
            _stream.NextRaw();
            int fade = (int)(Type.SmokeLifeTime * 3.0);
            int life = (int)(((((Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625 * 0.30000001192092896) + 0.6000000238418579)
                * Type.SmokeLifeTime) * 30.0).Value;
            float angle = ((Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625 * 6.2831854820251465).F;
            float spin = (((Fpu)0.5 - ((Fpu)(double)(ushort)_stream.NextRaw() * 0.0000152587890625)) * 0.03769911453127861).F;
            _smoke?.AddParticle(from, body.VelocityDirection, intensity, spin, angle, 1f, fade, life);
            from = new Vector3d(sx + from.X, sy + from.Y, sz + from.Z);
        }
    }

    // Where the smoke leaves: its position with no magic vertex; the first, its Y reversed, turned by its frame; else the second.
    private Vector3d SmokePoint()
    {
        Vector3d p = Body.Position;
        int count = _model?.MagicVertexCount ?? 0;
        float[] m = Body.Orientation;
        if (count == 0 || _model is null)
        {
            return p;
        }

        bool first = count == 1;
        if (!_model.TryGetMagicVertex(first ? 0 : 1, out Vector3f v))
        {
            return p;
        }

        float y = first ? -v.Y : v.Y;
        return new Vector3d(
            ((((Fpu)v.Z * m[2]) + ((Fpu)v.X * m[0])) + ((Fpu)y * m[1])).Value + p.X,
            ((((Fpu)v.Z * m[5]) + ((Fpu)v.X * m[3])) + ((Fpu)y * m[4])).Value + p.Y,
            ((((Fpu)v.Z * m[8]) + ((Fpu)v.X * m[6])) + ((Fpu)y * m[7])).Value + p.Z);
    }
}
