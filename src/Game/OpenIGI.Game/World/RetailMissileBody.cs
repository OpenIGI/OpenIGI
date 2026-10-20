using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>
/// A missile in flight as Project I.G.I. flies one (<c>Missile</c>, registered at <c>0x427FF0</c>): a physics object pushed by its
/// motor, steadied by its fins, steering as its type says, and set off by the first thing it touches.
/// </summary>
/// <remarks>
/// <para>
/// Fired (<c>0x429A30</c>) facing as its launcher faces, along that frame's forward column at the launcher's speed, its thrust full
/// and its fuel its type's; its wings are its type's with the first two mirrored across it as the third and fourth (<c>0x429EF0</c>).
/// </para>
/// <para>
/// Each tick (<c>0x428390</c>) it steers by its type (<c>+284</c>), lays smoke while it has fuel (<c>0x428630</c>, drawing the
/// missiles' own stream), then moves (<c>0x428D00</c>): its thrust eases toward full, its pitch, roll and turn rates toward their
/// ideals, its frame turns by pitch then turn, a burnt-out missile wobbles, its motor pushes it while it has fuel, it burns some, and
/// unless it may rest the air acts on its wings and the shared step moves it. Touching anything, it goes off (<see cref="IsStruck"/>).
/// </para>
/// <para>
/// The same engine module as I.G.I. 2's missile (<c>Missile_Move</c>), with the first game's own operand order and register use, and
/// two steering kinds the sequel dropped (the Hind's and the keyboard's).
/// </para>
/// </remarks>
public sealed class RetailMissileBody : RetailPhysicsObjectBody
{
    /// <summary>A metre a second, in units a tick: the float <c>136.53334</c>.</summary>
    public const double MetresPerSecond = 136.53334045410156;

    private readonly IgiRandom _random;
    private readonly Func<Vector3d?>? _targetTask;
    private Vector3d _target;
    private Vector3d _delta;
    private int _smokeTick;

    private RetailMissileBody(PhysicsObjectType type, MissileTypeDefinition missile, IgiRandom random, Func<Vector3d?>? targetTask)
        : base(type)
    {
        Missile = missile;
        _random = random;
        _targetTask = targetTask;
    }

    /// <summary>Gets its missile type: its handling, its fuel and how it steers.</summary>
    public MissileTypeDefinition Missile { get; }

    /// <summary>Gets the thrust it has, eased toward <see cref="IdealThrust"/> (<c>+1824</c>).</summary>
    public float RealThrust { get; private set; } = 1f;

    /// <summary>Gets the thrust it eases toward (<c>+1840</c>).</summary>
    public float IdealThrust { get; private set; } = 1f;

    /// <summary>Gets its pitch rate (<c>+1828</c>).</summary>
    public float RealDeltaAlpha { get; private set; }

    /// <summary>Gets its roll rate (<c>+1832</c>).</summary>
    public float RealDeltaBeta { get; private set; }

    /// <summary>Gets its turn rate (<c>+1836</c>).</summary>
    public float RealDeltaGamma { get; private set; }

    /// <summary>Gets the pitch it steers for (<c>+1844</c>).</summary>
    public float IdealDeltaAlpha { get; private set; }

    /// <summary>Gets the turn it steers for (<c>+1852</c>).</summary>
    public float IdealDeltaGamma { get; private set; }

    /// <summary>Gets the fuel it has left (<c>+2148</c>).</summary>
    public float Fuel { get; private set; }

    /// <summary>Gets the point it steers for (<c>+2088</c>).</summary>
    public Vector3d Target => _target;

    /// <summary>Gets or sets what the keyboard asks of a keyboard-steered missile: its turn and its pitch, each −1, 0 or 1 (<c>0x428A00</c>).</summary>
    public Func<(float Turn, float Pitch)>? Keyboard { get; set; }

    /// <summary>Gets a value indicating whether it touched something on its last move, which sets it off.</summary>
    public bool IsStruck => ContactCount != 0;

    /// <summary>Fires a missile (<c>0x429A30</c>).</summary>
    /// <param name="type">Its physics type.</param>
    /// <param name="missile">Its missile type, read from the physics type.</param>
    /// <param name="model">Its model, for its contact points and its radius.</param>
    /// <param name="position">Where it leaves the launcher.</param>
    /// <param name="orientation">The launcher's frame: its forward column is the second.</param>
    /// <param name="speedMetresPerSecond">The launcher's speed.</param>
    /// <param name="target">Where it was fired at, or <see langword="null"/>.</param>
    /// <param name="targetTask">Where the task it was fired at is now, for a missile that follows one; <see langword="null"/> for none.</param>
    /// <param name="random">The missiles' own stream (<see cref="MissileRandom"/>).</param>
    /// <returns>The missile.</returns>
    public static RetailMissileBody Fire(
        PhysicsObjectType type,
        MissileTypeDefinition missile,
        MefModel? model,
        Vector3d position,
        Matrix3f orientation,
        float speedMetresPerSecond,
        Vector3d? target,
        Func<Vector3d?>? targetTask,
        IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(missile);
        ArgumentNullException.ThrowIfNull(random);
        float[] m = new float[9];
        orientation.CopyTo(m);
        Vector3f velocity = new(
            ((X87)m[1] * speedMetresPerSecond * MetresPerSecond).F,
            ((X87)m[4] * speedMetresPerSecond * MetresPerSecond).F,
            ((X87)m[7] * speedMetresPerSecond * MetresPerSecond).F);

        RetailMissileBody body = new(type, missile, random, missile.Guidance is not (0 or 1) ? targetTask : null);
        body.Place(model, position, orientation, velocity);
        body.Fuel = missile.Fuel;

        // 0x429EF0: the type's wings, the first two mirrored across the missile as the third and fourth.
        IReadOnlyList<Wing> wings = missile.Vehicle.Wings;
        Wing[] own = new Wing[Math.Max(wings.Count, 4)];
        for (int i = 0; i < wings.Count; i++)
        {
            own[i] = wings[i];
        }

        own[2] = own[0] with { Position = new Vector3f(own[0].Position.X, -own[0].Position.Y, own[0].Position.Z) };
        own[3] = own[1] with { Position = new Vector3f(own[1].Position.X, -own[1].Position.Y, own[1].Position.Z) };
        body.Wings = own[..wings.Count];

        // Where it steers for, by its kind.
        switch (missile.Guidance)
        {
            case 0:
                body._target = new Vector3d(
                    ((X87)m[1] * 4096000.0 + position.X).Value,
                    ((X87)m[4] * 4096000.0 + position.Y).Value,
                    ((X87)m[7] * 4096000.0 + position.Z).Value);
                break;
            case 1 when target is { } at:
                body._delta = new Vector3d(((X87)at.X - position.X).Value, ((X87)at.Y - position.Y).Value, ((X87)at.Z - position.Z).Value);
                body._target = new Vector3d(
                    ((X87)body._delta.X + position.X).Value,
                    ((X87)body._delta.Y + position.Y).Value,
                    ((X87)body._delta.Z + position.Z).Value);
                break;
            default:
                if (target is { } aimed)
                {
                    body._target = aimed;
                }

                break;
        }

        return body;
    }

    /// <summary>Flies the missile a tick (<c>0x428390</c>): it steers, smokes, and moves.</summary>
    /// <param name="world">What its contact points' rays meet.</param>
    /// <remarks>Whether it then goes off is <see cref="IsStruck"/>.</remarks>
    public void Tick(IRetailCarWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (_targetTask?.Invoke() is { } followed)
        {
            _target = followed;
        }

        switch (Missile.Guidance)
        {
            case 0:
                IdealDeltaAlpha = 0f;
                IdealDeltaGamma = 0f;
                break;
            case 1:
                SteerHind();
                break;
            case 2:
                Lob(_target);
                break;
            case 3:
                SteerAntiTank(_target);
                break;
            case 4:
                (float turn, float pitch) = Keyboard?.Invoke() ?? (0f, 0f);
                IdealDeltaGamma = turn;
                IdealDeltaAlpha = pitch;
                break;
        }

        if (Fuel != 0f && Missile.Smoke != 0)
        {
            DrawSmoke();
        }

        Move(world);
    }

    // 0x428D00.
    private void Move(IRetailCarWorld world)
    {
        VehicleTypeDefinition v = Missile.Vehicle;
        float[] m = M;

        // The thrust eases toward the ideal by its inertia (the start-stop one below 0.7) and dies under a thousandth.
        float inertia = RealThrust < 0.699999988f ? v.ThrustStartStopInertia : v.ThrustInertia;
        X87 thrust = RealThrust;
        if (thrust.Value < IdealThrust)
        {
            thrust += inertia;
            if (thrust.Value > IdealThrust)
            {
                thrust = IdealThrust;
            }
        }
        else if (thrust.Value > IdealThrust)
        {
            thrust -= inertia;
            if (thrust.Value < IdealThrust)
            {
                thrust = IdealThrust;
            }
        }

        if (thrust.Value < 0.00100000005f)
        {
            thrust = 0.0f;
        }

        RealThrust = thrust.F;

        // The rates ease toward their ideals; pitch and roll stay in the registers, the turn is read back stored.
        X87 alpha = ((X87)1.0f - v.DeltaAlphaInertia) * IdealDeltaAlpha + (X87)v.DeltaAlphaInertia * RealDeltaAlpha;
        RealDeltaAlpha = alpha.F;
        X87 beta = ((X87)1.0f - v.DeltaBetaInertia) * 0.0f + (X87)v.DeltaBetaInertia * RealDeltaBeta;
        RealDeltaBeta = beta.F;
        float gamma = (((X87)1.0f - v.DeltaGammaInertia) * IdealDeltaGamma + (X87)v.DeltaGammaInertia * RealDeltaGamma).F;
        RealDeltaGamma = gamma;
        if (alpha.Value != 0.0 || beta.Value != 0.0 || gamma != 0.0f)
        {
            float turn = ((X87)gamma * v.MaxDeltaGamma * 0.0174532924f).F;
            float pitch = (alpha * v.MaxDeltaAlpha * 0.0174532924f).F;
            Span<float> r = stackalloc float[9];
            RotX(r, pitch);
            TurnBy(m, r);
            MatrixCounter++;
            RotZ(r, turn);
            TurnBy(m, r);
            MatrixCounter++;
        }

        if (Fuel == 0.0f)
        {
            Wobble(m);
        }

        if (RealThrust != 0.0f && Fuel != 0.0f)
        {
            float push = ((X87)v.MaxThrust * RealThrust * Gravity).F;
            Push(Force(0.0f, push, 0.0f, 0.0f, 0.0f, 0.0f));
        }

        X87 fuel = (X87)Fuel - Missile.Burn;
        Fuel = fuel.Value < 0.0 ? 0.0f : fuel.F;
        if (IsAsleep())
        {
            return;
        }

        if (v.Wings.Count != 0)
        {
            ApplyAero(-Velocity[0], -Velocity[1], -Velocity[2]);
        }

        Step(world, Gravity, 0.949999988f, 0.920000017f);
    }

    // The burnt-out wobble: three draws of the missiles' stream turn the frame by a tenth of a radian at most in each angle
    // (0x4B38E0), the frame times it.
    private void Wobble(float[] m)
    {
        float a = (((X87)0.5f - _random.NextDouble()) * 0.100000001f).F;
        float b = (((X87)0.699999988f - _random.NextDouble()) * 0.100000001f).F;
        float c = (((X87)0.300000012f - _random.NextDouble()) * 0.100000001f).F;
        Span<float> w = stackalloc float[9];
        Span<float> r = stackalloc float[9];
        RotZ(w, c);
        RotY(r, b);
        Compose(w, r);
        RotX(r, a);
        Compose(w, r);

        Span<float> t = stackalloc float[9];
        for (int row = 0; row < 9; row += 3)
        {
            t[row] = ((X87)w[3] * m[row + 1] + (X87)w[6] * m[row + 2] + (X87)w[0] * m[row]).F;
            t[row + 1] = ((X87)w[4] * m[row + 1] + (X87)w[7] * m[row + 2] + (X87)w[1] * m[row]).F;
            t[row + 2] = ((X87)w[5] * m[row + 1] + (X87)w[8] * m[row + 2] + (X87)w[2] * m[row]).F;
        }

        t.CopyTo(m);
        MatrixCounter++;
    }

    // The frame turned by a rotation, M R: each element's three products summed third, first, second; the middle row second,
    // third, first.
    private static void TurnBy(float[] m, ReadOnlySpan<float> r)
    {
        Span<float> t = stackalloc float[9];
        for (int col = 0; col < 3; col++)
        {
            t[col] = ((X87)r[6 + col] * m[2] + (X87)r[col] * m[0] + (X87)r[3 + col] * m[1]).F;
            t[3 + col] = ((X87)r[3 + col] * m[4] + (X87)r[6 + col] * m[5] + (X87)r[col] * m[3]).F;
            t[6 + col] = ((X87)r[6 + col] * m[8] + (X87)r[col] * m[6] + (X87)r[3 + col] * m[7]).F;
        }

        t.CopyTo(m);
    }

    // 0x4B38E0's composing step, A B: each element first, third, second.
    private static void Compose(Span<float> a, ReadOnlySpan<float> b)
    {
        Span<float> t = stackalloc float[9];
        for (int row = 0; row < 9; row += 3)
        {
            for (int col = 0; col < 3; col++)
            {
                t[row + col] = ((X87)b[col] * a[row] + (X87)b[6 + col] * a[row + 2] + (X87)b[3 + col] * a[row + 1]).F;
            }
        }

        t.CopyTo(a);
    }

    // 0x4B3B60: about X.
    private static void RotX(Span<float> r, float angle)
    {
        double s = Math.Sin(angle);
        float c = X87.ToFloat(Math.Cos(angle));
        r.Clear();
        r[0] = 1.0f;
        r[4] = c;
        r[5] = X87.ToFloat(-s);
        r[7] = X87.ToFloat(s);
        r[8] = c;
    }

    // 0x4B3BA0: about Y.
    private static void RotY(Span<float> r, float angle)
    {
        float c = X87.ToFloat(Math.Cos(angle));
        double s = Math.Sin(angle);
        r.Clear();
        r[0] = c;
        r[2] = X87.ToFloat(s);
        r[4] = 1.0f;
        r[6] = X87.ToFloat(-s);
        r[8] = c;
    }

    // 0x428930: the Hind's missile steers for the point a missile's frame-crossing projection makes, as the original computes it.
    private void SteerHind()
    {
        X87 dx = (X87)Position[0] - _target.X;
        X87 dy = (X87)Position[1] - _target.Y;
        X87 dz = (X87)Position[2] - _target.Z;
        X87 s = dz * _delta.Z + dy * _delta.Y + dx * _delta.X;
        Vector3d along = new((s * dx).Value, (s * dy).Value, (s * dz).Value);
        X87 inverse = (X87)1.0 / Real64x3Length(along);
        Vector3d point = new(
            ((X87)(inverse * along.X * 12288.0).Value + along.X).Value,
            ((X87)(inverse * along.Y * 12288.0).Value + along.Y).Value,
            ((X87)(inverse * along.Z * 12288.0).Value + along.Z).Value);
        TowardTarget(point);
    }

    // 0x428A60: toward a point an eighth of the way to the target above it.
    private void Lob(Vector3d target)
    {
        Vector3d to = new(((X87)target.X - Position[0]).Value, ((X87)target.Y - Position[1]).Value, ((X87)target.Z - Position[2]).Value);
        X87 rise = Real64x3Length(to) * 0.125;
        TowardTarget(new Vector3d(target.X, target.Y, (rise + target.Z).Value));
    }

    // 0x428AD0: from far off, toward a point twenty-five metres above the target; within thirty, a lob.
    private void SteerAntiTank(Vector3d target)
    {
        Vector3d flat = new(((X87)target.X - Position[0]).Value, ((X87)target.Y - Position[1]).Value, 0.0);
        if (Real64x3Length(flat).Value <= 122880.0)
        {
            Lob(target);
            return;
        }

        TowardTarget(new Vector3d(target.X, target.Y, ((X87)target.Z + 102400.0).Value));
    }

    // 0x428B60: the target in the missile's frame; its pitch (0x4148F0) and turn (0x414950) off the nose, each held to one.
    private void TowardTarget(Vector3d target)
    {
        float[] m = M;
        X87 dx = (X87)target.X - Position[0];
        X87 dy = (X87)target.Y - Position[1];
        X87 dz = (X87)target.Z - Position[2];
        double lx = ((X87)m[6] * dz + (X87)m[3] * dy + (X87)m[0] * dx).Value;
        double ly = ((X87)m[7] * dz + (X87)m[4] * dy + (X87)m[1] * dx).Value;
        double lz = ((X87)m[8] * dz + (X87)m[5] * dy + (X87)m[2] * dx).Value;
        double pitch = Math.Clamp(AngleOff(0.0, ly, lz, signByZ: true), -1.0, 1.0);
        double turn = Math.Clamp(AngleOff(lx, ly, 0.0, signByZ: false), -1.0, 1.0);
        IdealDeltaAlpha = X87.ToFloat(pitch);
        IdealDeltaGamma = X87.ToFloat(turn);
    }

    // 0x4148F0 (X dropped, signed by Z) and 0x414950 (Z dropped, negative when X is positive): the angle of the forward
    // component of the normalised vector.
    private static double AngleOff(double x, double y, double z, bool signByZ)
    {
        if (x == 0.0 && y == 0.0 && z == 0.0)
        {
            return 0.0;
        }

        X87 inverse = (X87)1.0 / Real64x3Length(new Vector3d(x, y, z));
        double nx = (inverse * x).Value, ny = (inverse * y).Value, nz = (inverse * z).Value;
        double angle = ny < -1.0 ? Math.Acos(-1.0) : Math.Acos(ny > 1.0 ? 1.0 : ny);
        return signByZ ? (nz < 0.0 ? -angle : angle) : (nx > 0.0 ? -angle : angle);
    }

    // 0x4B3130: sqrt(x*x + y*y + z*z).
    private static X87 Real64x3Length(Vector3d v) => X87.Sqrt((X87)v.X * v.X + (X87)v.Y * v.Y + (X87)v.Z * v.Z);

    // 0x428630's draws: every so many ticks one for the puffs' shade, then six a puff, the puffs one for each smoke size the
    // missile moved. The puffs are the presentation's; the draws keep the stream where the wobble expects it.
    private void DrawSmoke()
    {
        _smokeTick++;
        if (_smokeTick < (int)Missile.SmokeDensity)
        {
            return;
        }

        _random.NextDouble();
        _smokeTick = 0;
        int puffs = Missile.SmokeSize == 0.0f ? 1 : (int)((X87)Speed / Missile.SmokeSize).Value;
        if (puffs <= 1)
        {
            puffs = 1;
        }

        for (int i = 0; i < puffs; i++)
        {
            for (int draw = 0; draw < 6; draw++)
            {
                _random.NextDouble();
            }
        }
    }
}
