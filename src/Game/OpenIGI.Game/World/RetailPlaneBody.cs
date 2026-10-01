using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>The fixed data a plane steps with: its physics type and its Plane record (<c>R = TYPE + 724</c>).</summary>
public sealed class RetailPlaneDefinition
{
    /// <summary>Gets or sets the type's mass (<c>TYPE + 8</c>).</summary>
    public float Mass { get; set; }

    /// <summary>Gets or sets the sleep speed threshold (<c>TYPE + 716</c>).</summary>
    public float SleepSpeed { get; set; }

    /// <summary>Gets or sets the sleep threshold on |ω|² (<c>TYPE + 720</c>).</summary>
    public float SleepSpin { get; set; }

    /// <summary>
    /// Gets or sets the record's ten leading floats, <c>R + 0 … R + 36</c>: the take-off speed factor, the pitch/roll
    /// limit, the three channel retentions (<c>+8</c>, <c>+16</c>, <c>+24</c>) with the roll gain between them, the yaw
    /// gain, the speed scale and the two thrust steps.
    /// </summary>
    public float[] Record { get; set; } = new float[10];

    /// <summary>
    /// Gets or sets the record's surfaces (<c>R + 44</c>), as <c>0x433580</c> leaves them: surfaces 3 and 4 are the
    /// elevons it derives from surface 2 and writes back.
    /// </summary>
    public RetailAeroSurface[] Surfaces { get; set; } = [];
}

/// <summary>One landing gear leg: a <c>Gear</c> attachment (<c>0x435460</c>), 320 bytes.</summary>
public sealed class RetailPlaneGear
{
    private static readonly float DegreesToRadians = BitConverter.Int32BitsToSingle(0x3C8EFA35);
    private static readonly float Retention = BitConverter.Int32BitsToSingle(0x3F7AE148);
    private static readonly float Approach = BitConverter.Int32BitsToSingle(0x3CA3D700);
    private static readonly float Snap = BitConverter.Int32BitsToSingle(0x3C23D70A);
    private static readonly float Home = BitConverter.Int32BitsToSingle(0x3DCCCCCD);

    /// <summary>Initializes a new instance of the <see cref="RetailPlaneGear"/> class.</summary>
    /// <param name="local">The attach point in the plane's frame (<c>+240</c>).</param>
    /// <param name="baseOrientation">The attachment's orientation, row-major (<c>+264</c>).</param>
    /// <param name="record">The leg's record (<c>+312</c>).</param>
    /// <param name="vertex">The leg model's magic vertex 0: where its wheel touches.</param>
    public RetailPlaneGear(Vector3d local, float[] baseOrientation, MagicGear record, Vector3f vertex)
    {
        ArgumentNullException.ThrowIfNull(baseOrientation);
        Local = local;
        Array.Copy(baseOrientation, Base, 9);
        Record = record;
        Vertex = vertex;
    }

    /// <summary>Gets the attach point, plane frame (<c>+240</c>).</summary>
    public Vector3d Local { get; }

    /// <summary>Gets the attachment's orientation (<c>+264</c>).</summary>
    public float[] Base { get; } = new float[9];

    /// <summary>Gets the leg's record.</summary>
    public MagicGear Record { get; }

    /// <summary>Gets the wheel's contact vertex, model frame.</summary>
    public Vector3f Vertex { get; }

    /// <summary>Gets or sets the fold angle, radians (<c>+316</c>).</summary>
    public float Angle { get; set; }

    /// <summary>Gets the leg's world position (<c>+32</c>).</summary>
    public double[] World { get; } = new double[3];

    /// <summary>Gets the leg's world orientation (<c>+112</c>).</summary>
    public float[] M { get; } = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>The fold angle the leg starts at (<c>0x435460</c>): none with the gear down, the full fold with it up.</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    public void Seat(bool gearDown) =>
        Angle = gearDown ? 0.0f : ((X87)Record.FoldDegrees * DegreesToRadians).F;

    /// <summary>The leg's update (<c>0x435930</c>): the fold eases toward its target; returns the gear state byte.</summary>
    /// <param name="gearDown">The plane's gear byte (<c>+2093</c>).</param>
    /// <param name="gearArgument">The plane's <c>+2099</c>.</param>
    /// <returns>The value the update writes to the plane's <c>+2098</c>.</returns>
    public sbyte Update(bool gearDown, byte gearArgument)
    {
        X87 fold = (X87)Record.FoldDegrees * DegreesToRadians;
        X87 target = gearDown && gearArgument == 1 ? (X87)0.0f : fold;
        if (target.Value != Angle)
        {
            Angle = ((X87)Angle * Retention + target * Approach).F;
        }

        if (X87.Abs((X87)Angle - target).Value < Snap)
        {
            Angle = target.F;
        }

        if (Math.Abs(Angle) < Home)
        {
            return 1;
        }

        X87 travel = X87.Abs(fold) - Math.Abs(Angle);
        return travel.Value >= Home ? (sbyte)-1 : (sbyte)0;
    }

    /// <summary>
    /// Follows the plane (<c>0x435A80</c>): <c>M·l + pos</c> in doubles, then the leg's orientation from its fold, its
    /// steering and its attachment turn (<c>0x4355A0</c>).
    /// </summary>
    /// <param name="position">The plane's position.</param>
    /// <param name="m">The plane's orientation.</param>
    /// <param name="steer">The steering angle (yaw control × π/4 on a steering leg with the gear down, else 0).</param>
    public void Follow(double[] position, float[] m, float steer)
    {
        Vector3d l = Local;
        double wx = ((X87)m[2] * l.Z + (X87)m[1] * l.Y + (X87)m[0] * l.X).Value;
        double wy = ((X87)m[5] * l.Z + (X87)m[4] * l.Y + (X87)m[3] * l.X).Value;
        double wz = ((X87)m[8] * l.Z + (X87)m[7] * l.Y).Value;
        wz = ((X87)wz + (X87)m[6] * l.X).Value;
        World[0] = X87.Add(wx, position[0]);
        World[1] = X87.Add(wy, position[1]);
        World[2] = X87.Add(wz, position[2]);

        Span<float> r = stackalloc float[9];
        RetailPlaneBody.Euler(r, Angle, 0.0f, steer);
        float[] b = Base;
        X87 v3 = (X87)r[0] * b[0] + (X87)r[6] * b[2] + (X87)r[3] * b[1];
        float v11 = ((X87)r[1] * b[0] + (X87)r[7] * b[2] + (X87)r[4] * b[1]).F;
        float v12 = ((X87)r[2] * b[0] + (X87)r[8] * b[2] + (X87)r[5] * b[1]).F;
        X87 v4 = (X87)r[0] * b[3] + (X87)r[6] * b[5] + (X87)r[3] * b[4];
        float v13 = ((X87)r[1] * b[3] + (X87)r[7] * b[5] + (X87)r[4] * b[4]).F;
        float v14 = ((X87)r[2] * b[3] + (X87)r[8] * b[5] + (X87)r[5] * b[4]).F;
        X87 v5 = (X87)r[6] * b[8] + (X87)r[3] * b[7] + (X87)r[0] * b[6];
        float v15 = ((X87)r[7] * b[8] + (X87)r[4] * b[7] + (X87)r[1] * b[6]).F;
        float v16 = ((X87)r[8] * b[8] + (X87)r[5] * b[7] + (X87)r[2] * b[6]).F;
        float[] g = M;
        g[0] = (v5 * m[2] + v4 * m[1] + v3 * m[0]).F;
        g[1] = ((X87)v15 * m[2] + (X87)v13 * m[1] + (X87)v11 * m[0]).F;
        g[2] = ((X87)v16 * m[2] + (X87)v14 * m[1] + (X87)v12 * m[0]).F;
        g[3] = (v3 * m[3] + v5 * m[5] + v4 * m[4]).F;
        g[4] = ((X87)v11 * m[3] + (X87)v15 * m[5] + (X87)v13 * m[4]).F;
        g[5] = ((X87)v12 * m[3] + (X87)v16 * m[5] + (X87)v14 * m[4]).F;
        g[6] = (v3 * m[6] + v5 * m[8] + v4 * m[7]).F;
        g[7] = ((X87)v11 * m[6] + (X87)v15 * m[8] + (X87)v13 * m[7]).F;
        g[8] = ((X87)v12 * m[6] + (X87)v16 * m[8] + (X87)v14 * m[7]).F;
    }

    /// <summary>The wheel vertex turned by the leg (<c>0x4D0A50</c>, unit scale): world offset from the leg's origin.</summary>
    /// <returns>The offset.</returns>
    public Vector3d WheelOffset()
    {
        float[] g = M;
        X87 x = Vertex.X;
        float y = Vertex.Y;
        float z = Vertex.Z;
        double ox = X87.ToFloat(((X87)z * g[2] + (X87)y * g[1] + x * g[0]).Value);
        double oy = ((X87)z * g[5] + (X87)y * g[4] + x * g[3]).Value;
        double oz = X87.ToFloat(((X87)z * g[8] + (X87)y * g[7] + x * g[6]).Value);
        return new Vector3d(ox, oy, oz);
    }
}

/// <summary>
/// A plane as the original simulates it -- <c>Plane_Physics</c> (<c>0x433B50</c>), its landing gear contact
/// (<c>0x434310</c>), its channel receiver (<c>0x4337E0</c>) and its gear legs (<c>0x435930</c>, <c>0x435A80</c>) -- on the
/// shared rigid body.
/// </summary>
public sealed class RetailPlaneBody : RetailRigidBody
{
    /// <summary>The linear retention the plane passes the body step.</summary>
    public const float LinearRetention = 1.0f;

    /// <summary>The angular retention the plane passes the body step (<c>0x3F6B851F</c>).</summary>
    public const float AngularRetention = 0.92f;

    private static readonly float SpeedScale = BitConverter.Int32BitsToSingle(0x4217B426);
    private static readonly float DecayPerSpeed = BitConverter.Int32BitsToSingle(0x3A59945B);
    private static readonly float DecayBase = BitConverter.Int32BitsToSingle(0x3F7EB852);
    private static readonly float HalfDegree = BitConverter.Int32BitsToSingle(0x3C0EFA35);
    private static readonly float BoostScale = BitConverter.Int32BitsToSingle(0x45CACB6F);
    private static readonly double YawScale = BitConverter.Int64BitsToDouble(0x3F81DF46A2529D3B);
    private static readonly float AeroSpeed = BitConverter.Int32BitsToSingle(0x448E38E4);
    private static readonly float GroundGap = BitConverter.Int32BitsToSingle(0x3F19999A);
    private static readonly float HighThrust = BitConverter.Int32BitsToSingle(0x3F4CCCCD);
    private static readonly float ThrustFloor = BitConverter.Int32BitsToSingle(0x3A83126F);
    private static readonly double SpringRate = BitConverter.Int64BitsToDouble(0x3F5AAAAAAAAAAAAB);
    private static readonly float LoadScale = BitConverter.Int32BitsToSingle(0x418EB90C);
    private static readonly float BottomSlope = BitConverter.Int32BitsToSingle(0x390A60DD);
    private static readonly float BottomTop = BitConverter.Int32BitsToSingle(0x45ECCCCD);
    private static readonly double BottomDepth = BitConverter.Int64BitsToDouble(0x40BD99999999999A);
    private static readonly float RollBrake = BitConverter.Int32BitsToSingle(0x3F4CCCCD);
    private static readonly float SpinDamping = BitConverter.Int32BitsToSingle(0x3F7AE148);
    private static readonly float QuarterTurn = BitConverter.Int32BitsToSingle(0x3F490FDB);
    private const double Reach = 8192.0;

    private readonly Vector3d[] _gearStarts = new Vector3d[4];
    private readonly Vector3d[] _gearEnds = new Vector3d[4];
    private readonly float[] _gearPoints = new float[12];
    private readonly RetailRayResult[] _gearResults = new RetailRayResult[4];

    /// <summary>Initializes a new instance of the <see cref="RetailPlaneBody"/> class.</summary>
    /// <param name="definition">The type and record.</param>
    public RetailPlaneBody(RetailPlaneDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Surfaces = [.. definition.Surfaces];
    }

    /// <summary>Gets the type and record.</summary>
    public RetailPlaneDefinition Definition { get; }

    /// <summary>Gets the plane's own surfaces (<c>+1864</c>), rewritten every tick by the flaps and the elevons.</summary>
    public RetailAeroSurface[] Surfaces { get; }

    /// <summary>Gets or sets the thrust, live (<c>+1824</c>).</summary>
    public float Thrust { get; set; }

    /// <summary>Gets the three smoothed channels: pitch, roll, yaw (<c>+1828</c>).</summary>
    public float[] Controls { get; } = new float[3];

    /// <summary>Gets or sets the thrust target (<c>+1840</c>, channel 3).</summary>
    public float ThrustTarget { get; set; }

    /// <summary>Gets the three channel targets (<c>+1844</c>, channels 0-2).</summary>
    public float[] Targets { get; } = new float[3];

    /// <summary>Gets or sets the level's Original Thrust (<c>+1856</c>).</summary>
    public float OriginalThrust { get; set; }

    /// <summary>Gets or sets the flaps (<c>+1860</c>, channel 7 held).</summary>
    public bool Flaps { get; set; }

    /// <summary>Gets or sets the canopy byte (<c>+2092</c>, toggled by channel 5).</summary>
    public byte Canopy { get; set; }

    /// <summary>Gets or sets the gear byte (<c>+2093</c>, toggled by channel 6): non-zero with the gear down.</summary>
    public byte GearDown { get; set; }

    /// <summary>Gets or sets the boost (<c>+2094</c>, channel 8 held).</summary>
    public bool Boost { get; set; }

    /// <summary>Gets or sets the byte channel 9 toggles (<c>+2095</c>).</summary>
    public byte Toggle9 { get; set; }

    /// <summary>Gets or sets the level's Canopy (<c>+2096</c>).</summary>
    public byte InitialCanopy { get; set; }

    /// <summary>Gets or sets the level's Gear (<c>+2097</c>).</summary>
    public byte InitialGear { get; set; }

    /// <summary>Gets or sets the gear state the legs report (<c>+2098</c>).</summary>
    public sbyte GearState { get; set; }

    /// <summary>Gets or sets <c>+2099</c>, the level's Gear again, which the legs fold toward.</summary>
    public byte GearArgument { get; set; }

    /// <summary>Gets the landing gear legs, in attachment order (at most four touch the ground).</summary>
    public List<RetailPlaneGear> Gears { get; } = [];

    /// <inheritdoc/>
    protected override float TypeMass => Definition.Mass;

    /// <inheritdoc/>
    protected override float SleepSpeed => Definition.SleepSpeed;

    /// <inheritdoc/>
    protected override float SleepSpin => Definition.SleepSpin;

    /// <inheritdoc/>
    protected override RetailAeroSurface[] AeroSurfaces => Surfaces;

    /// <summary>Latches an AnimTask packet's channels (<c>0x4337E0</c>, every packet).</summary>
    /// <param name="channels">The programme's ten channels.</param>
    public void Receive(ReadOnlySpan<float> channels)
    {
        if (Held(channels[3]))
        {
            ThrustTarget = channels[3];
        }

        Targets[0] = channels[0];
        Targets[1] = channels[1];
        Targets[2] = channels[2];
        if (channels.Length > 5 && Held(channels[5]))
        {
            Canopy ^= (byte)(long)channels[5];
        }

        if (channels.Length > 6 && Held(channels[6]))
        {
            GearDown ^= (byte)(long)channels[6];
        }

        Flaps = channels.Length > 7 && channels[7] != -1.0f;
        Boost = channels.Length > 8 && channels[8] != -1.0f;
        if (channels.Length > 9 && Held(channels[9]))
        {
            Toggle9 ^= (byte)(long)channels[9];
        }

        static bool Held(float value) => BitConverter.SingleToInt32Bits(value) != unchecked((int)0xBF800000);
    }

    /// <summary>
    /// The mode-0 packet's reset (<c>0x4337E0</c>): the spawn pose committed, the body rebuilt from its type, the thrust
    /// at Original Thrust, the bytes and surfaces reset (<c>0x433530</c>), the level's Speed -- or, without one, the take-off
    /// speed along the nose -- turned into the velocity, and the legs placed.
    /// </summary>
    /// <param name="position">The spawn position (<c>+360</c>).</param>
    /// <param name="orientation">The spawn orientation (<c>+396</c>).</param>
    /// <param name="dimensions">The type's box, metres.</param>
    /// <param name="speed">The level's Speed (<c>+384</c>).</param>
    public void Reset(Vector3d position, float[] orientation, Vector3f dimensions, Vector3f speed)
    {
        ArgumentNullException.ThrowIfNull(orientation);
        Commit(position.X, position.Y, position.Z, BroadPhaseRadius());
        Array.Copy(orientation, M, 9);
        MatrixCounter = 0;
        InitFromType(dimensions);
        Thrust = OriginalThrust;
        ThrustTarget = OriginalThrust;
        Array.Clear(Targets);
        Array.Clear(Controls);

        // 0x433530.
        Canopy = InitialCanopy;
        GearDown = InitialGear;
        GearState = (sbyte)InitialGear;
        GearArgument = InitialGear;
        Flaps = false;
        Boost = false;
        Toggle9 = 0;
        Array.Copy(Definition.Surfaces, Surfaces, Surfaces.Length);

        float[] m = M;
        if (speed.X == 0f && speed.Y == 0f && speed.Z == 0f)
        {
            X87 v = (X87)Definition.Record[0] * Thrust * 30.34074f;
            Velocity[0] = (v * m[1]).F;
            Velocity[1] = (v * m[4]).F;
            Velocity[2] = (v * m[7]).F;
        }
        else
        {
            Velocity[0] = speed.X;
            Velocity[1] = speed.Y;
            Velocity[2] = speed.Z;
        }

        UpdateSpeed();
        FollowGears();
    }

    /// <summary>One plane tick -- <c>0x433B50</c>, then the legs' updates (<c>0x4F4660</c>).</summary>
    /// <param name="world">The world its wheel rays are cast in.</param>
    public void Tick(IRetailCarWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        PushedForces.Clear();
        float[] r = Definition.Record;
        RetailAeroSurface[] recordSurfaces = Definition.Surfaces;
        float mass = TypeMass;

        // A. The speed fraction, 0..1, and the control decay it sets.
        X87 fraction = (X87)Speed / ((X87)r[7] * SpeedScale);
        if (fraction.Value < 0.0f)
        {
            fraction = (X87)0.0f;
        }
        else if (fraction.Value > 1.0f)
        {
            fraction = (X87)1.0f;
        }

        // B. The thrust steps toward its target; on the wheels a target under 1 is taken 0.6 lower.
        float rate = Thrust < HighThrust ? r[9] : r[8];
        float target = ContactCount != 0 && ThrustTarget < 1.0f ? ((X87)ThrustTarget - GroundGap).F : ThrustTarget;
        X87 thrust = Thrust;
        if (thrust.Value < target)
        {
            thrust += rate;
            if (thrust.Value > target)
            {
                thrust = target;
            }
        }
        else if (thrust.Value > target)
        {
            thrust -= rate;
            if (thrust.Value < target)
            {
                thrust = target;
            }
        }

        if (thrust.Value < ThrustFloor)
        {
            thrust = (X87)0.0f;
        }

        Thrust = thrust.F;

        // C. The three channels blend toward their targets, and decay with speed.
        X87 decay = (X87)DecayBase - fraction * DecayPerSpeed;
        float k8 = r[2], k16 = r[4], k24 = r[6];
        float c1 = ((((X87)1.0f - k8) * Targets[0] + (X87)k8 * Controls[0]) * decay).F;
        Controls[0] = c1;
        float c2 = ((((X87)1.0f - k16) * Targets[1] + (X87)k16 * Controls[1]) * decay).F;
        Controls[1] = c2;
        X87 c3 = (((X87)1.0f - k24) * Targets[2] + (X87)k24 * Controls[2]) * decay;
        Controls[2] = c3.F;

        // D. Pitch and roll turn the elevons' normals about body X.
        if ((c1 != 0f || c2 != 0f || c3.Value != 0.0) && (ContactCount == 0 || !(Speed < 10.0f)))
        {
            float a = ((X87)c1 * r[1] * HalfDegree).F;
            X87 bx = (X87)c2 * r[3] * HalfDegree;
            float b = bx.F;
            X87 first = -(bx + a);
            float angle1 = (first.Value < r[1] ? first : (X87)r[1]).F;
            X87 second = (X87)b - a;
            float angle2 = (second.Value < r[1] ? second : (X87)r[1]).F;
            Span<float> m1 = stackalloc float[9];
            Span<float> m2 = stackalloc float[9];
            RotX(m1, angle1);
            RotX(m2, angle2);
            RetailAeroSurface s3 = recordSurfaces[3];
            RetailAeroSurface s4 = recordSurfaces[4];
            Surfaces[3] = Surfaces[3] with
            {
                Nx = ((X87)m1[0] * s3.Nx + (X87)m1[2] * s3.Nz + (X87)m1[1] * s3.Ny).F,
                Ny = ((X87)m1[5] * s3.Nz + (X87)m1[4] * s3.Ny + (X87)m1[3] * s3.Nx).F,
                Nz = ((X87)m1[8] * s3.Nz + (X87)m1[7] * s3.Ny + (X87)m1[6] * s3.Nx).F,
            };
            Surfaces[4] = Surfaces[4] with
            {
                Nx = ((X87)m2[0] * s4.Nx + (X87)m2[2] * s4.Nz + (X87)m2[1] * s4.Ny).F,
                Ny = ((X87)m2[3] * s4.Nx + (X87)m2[5] * s4.Nz + (X87)m2[4] * s4.Ny).F,
                Nz = ((X87)m2[6] * s4.Nx + (X87)m2[8] * s4.Nz + (X87)m2[7] * s4.Ny).F,
            };
        }

        // E. The flaps.
        if (Flaps)
        {
            Surfaces[1] = Surfaces[1] with { Coefficient = ((X87)recordSurfaces[1].Coefficient * 3.0f).F };
            Surfaces[3] = Surfaces[3] with { Coefficient = ((X87)recordSurfaces[2].Coefficient * 0.375f).F };
            Surfaces[4] = Surfaces[4] with { Coefficient = ((X87)recordSurfaces[2].Coefficient * 0.375f).F };
            Surfaces[2] = Surfaces[2] with { Coefficient = ((X87)recordSurfaces[2].Coefficient * 0.75f).F };
        }
        else
        {
            Surfaces[1] = Surfaces[1] with { Coefficient = recordSurfaces[1].Coefficient };
            Surfaces[3] = Surfaces[3] with { Coefficient = recordSurfaces[3].Coefficient };
            Surfaces[4] = Surfaces[4] with { Coefficient = recordSurfaces[4].Coefficient };
            Surfaces[2] = Surfaces[2] with { Coefficient = recordSurfaces[2].Coefficient };
        }

        // F. The boost.
        if (Boost)
        {
            Push(new RetailForce(0f, ((X87)mass * BoostScale).F, 0f, 0f, 0f, 0f));
        }

        // G. The thrust, 10 m ahead of the centre; airborne, the yaw control turns it.
        if (Thrust != 0.0f)
        {
            float fy = ((X87)mass * Thrust * Gravity).F;
            float fx = 0f, fz = 0f;
            if (ContactCount == 0)
            {
                Span<float> z = stackalloc float[9];
                RotZ(z, ((X87)r[5] * Controls[2] * YawScale).F);
                float nx = ((X87)z[2] * fz + (X87)z[1] * fy + (X87)z[0] * fx).F;
                float ny = ((X87)z[5] * fz + (X87)z[4] * fy + (X87)z[3] * fx).F;
                float nz = ((X87)z[8] * fz + (X87)z[7] * fy + (X87)z[6] * fx).F;
                fx = nx;
                fy = ny;
                fz = nz;
            }

            Push(new RetailForce(fx, fy, fz, 0f, 40960.0f, 0f));
        }

        // H. The landing gear.
        Wheels(world);

        // I. The physics.
        if (!IsAsleep())
        {
            if (Surfaces.Length != 0 && (ContactCount == 0 || Speed > AeroSpeed))
            {
                ApplyAero(-Velocity[0], -Velocity[1], -Velocity[2]);
            }

            Step(world, Gravity, LinearRetention, AngularRetention);
        }

        // J. The legs follow the new pose, then ease their fold (0x4F4660).
        FollowGears();
        foreach (RetailPlaneGear gear in Gears)
        {
            GearState = gear.Update(GearDown != 0, GearArgument);
        }
    }

    /// <summary>
    /// <c>0x4B38E0</c>: <c>Z(az) · Y(ay) · X(ax)</c>, each product's entries rounded to float.
    /// </summary>
    /// <param name="r">The nine entries, row-major.</param>
    /// <param name="ax">The angle about X.</param>
    /// <param name="ay">The angle about Y.</param>
    /// <param name="az">The angle about Z.</param>
    internal static void Euler(Span<float> r, float ax, float ay, float az)
    {
        RotZ(r, az);
        Span<float> t = stackalloc float[9];
        RotY(t, ay);
        Multiply(r, t);
        RotX(t, ax);
        Multiply(r, t);
    }

    /// <summary>
    /// <c>fsin</c> under the original's chop rounding: below a small angle's magnitude, where .NET rounds a tiny angle's
    /// sine to the angle itself.
    /// </summary>
    internal static double Sine(double angle)
    {
        double s = Math.Sin(angle);
        return s == angle && angle != 0.0 ? (angle > 0 ? Math.BitDecrement(s) : Math.BitIncrement(s)) : s;
    }

    /// <summary><c>fcos</c> under chop rounding: just under 1 for any non-zero angle, where .NET rounds up to 1.</summary>
    internal static double Cosine(double angle)
    {
        double c = Math.Cos(angle);
        return c == 1.0 && angle != 0.0 ? Math.BitDecrement(1.0) : c;
    }

    /// <summary><c>0x4B3BE0</c>: <c>{c, −s, 0, s, c, 0, 0, 0, 1}</c>, with the chop-rounded sine and cosine.</summary>
    private static new void RotZ(Span<float> r, float angle)
    {
        float s = X87.ToFloat(Sine(angle));
        float c = X87.ToFloat(Cosine(angle));
        r[0] = c;
        r[1] = X87.ToFloat(-Sine(angle));
        r[2] = 0.0f;
        r[3] = s;
        r[4] = c;
        r[5] = 0.0f;
        r[6] = 0.0f;
        r[7] = 0.0f;
        r[8] = 1.0f;
    }

    /// <summary><c>0x4B3B60</c>: <c>{1, 0, 0, 0, c, −s, 0, s, c}</c>.</summary>
    private static void RotX(Span<float> r, float angle)
    {
        float s = X87.ToFloat(Sine(angle));
        float c = X87.ToFloat(Cosine(angle));
        r[0] = 1f;
        r[1] = 0f;
        r[2] = 0f;
        r[3] = 0f;
        r[4] = c;
        r[5] = X87.ToFloat(-Sine(angle));
        r[6] = 0f;
        r[7] = s;
        r[8] = c;
    }

    /// <summary><c>0x4B3BA0</c>: <c>{c, 0, s, 0, 1, 0, −s, 0, c}</c>.</summary>
    private static void RotY(Span<float> r, float angle)
    {
        float s = X87.ToFloat(Sine(angle));
        float c = X87.ToFloat(Cosine(angle));
        r[0] = c;
        r[1] = 0f;
        r[2] = s;
        r[3] = 0f;
        r[4] = 1f;
        r[5] = 0f;
        r[6] = X87.ToFloat(-Sine(angle));
        r[7] = 0f;
        r[8] = c;
    }

    /// <summary><c>a = a · b</c>, as <c>0x4B38E0</c> multiplies: each entry summed then rounded to float.</summary>
    private static void Multiply(Span<float> a, ReadOnlySpan<float> b)
    {
        Span<float> n = stackalloc float[9];
        for (int row = 0; row < 3; row++)
        {
            float a0 = a[(3 * row) + 0], a1 = a[(3 * row) + 1], a2 = a[(3 * row) + 2];
            n[(3 * row) + 0] = ((X87)b[0] * a0 + (X87)b[6] * a2 + (X87)b[3] * a1).F;
            n[(3 * row) + 1] = ((X87)b[1] * a0 + (X87)b[7] * a2 + (X87)b[4] * a1).F;
            n[(3 * row) + 2] = ((X87)b[2] * a0 + (X87)b[8] * a2 + (X87)b[5] * a1).F;
        }

        n.CopyTo(a);
    }

    /// <summary><c>0x4FB700</c> for a body with no points: the box, the prev copies at the current pose.</summary>
    private void InitFromType(Vector3f dimensions)
    {
        float c = ((X87)dimensions.Z * 4096.0f).F;
        float b = ((X87)dimensions.Y * 4096.0f).F;
        float a = ((X87)dimensions.X * 4096.0f).F;
        RetailHeliBody.SetBox(this, Definition.Mass, a, b, c);
        for (int k = 0; k < 3; k++)
        {
            InverseInertia[k] = ((X87)InverseInertia[k] * 1.00999999f).F;
        }

        CollideEnabled = 0;
        Forces.Clear();
        IntegratedForceCount = 0;
        Array.Copy(Position, PreviousPosition, 3);
        Array.Clear(Displacement);
        Array.Copy(M, PreviousM, 9);
        PreviousMatrixCounter = MatrixCounter;
        ContactCount = 0;
        Wake = false;
        Array.Clear(DeltaM);
        DeltaM[0] = DeltaM[4] = DeltaM[8] = 1f;
        DeltaMatrixCounter = 0;
    }

    /// <summary>Places every leg at the body's pose (<c>0x435A80</c>).</summary>
    private void FollowGears()
    {
        foreach (RetailPlaneGear gear in Gears)
        {
            float steer = gear.Record.Steers && GearDown != 0 ? ((X87)Controls[2] * QuarterTurn).F : 0.0f;
            gear.Follow(Position, M, steer);
        }
    }

    /// <summary>
    /// The landing gear (<c>0x434310</c>): a ray down to each wheel; a wheel it reaches pushes a spring, the rolling
    /// brake scales the ground speed by the thrust, and a bottomed-out wheel lifts the plane out.
    /// </summary>
    private void Wheels(IRetailCarWorld world)
    {
        int n = Math.Min(Gears.Count, 4);
        if (n == 0)
        {
            return;
        }

        for (int k = 0; k < n; k++)
        {
            RetailPlaneGear gear = Gears[k];
            float[] g = gear.M;
            Vector3d w = gear.WheelOffset();
            // fst keeps the z the register holds: the turned point uses it unrounded.
            float px = X87.ToFloat(w.X), py = X87.ToFloat(w.Y);
            X87 pz = w.Z;
            float bx = (pz * g[6] + (X87)g[3] * py + (X87)g[0] * px).F;
            float by = (pz * g[7] + (X87)g[4] * py + (X87)g[1] * px).F;
            float bz = (pz * g[8] + (X87)g[5] * py + (X87)g[2] * px).F;
            _gearPoints[(3 * k) + 0] = ((X87)gear.Local.X + bx).F;
            _gearPoints[(3 * k) + 1] = ((X87)gear.Local.Y + by).F;
            _gearPoints[(3 * k) + 2] = ((X87)gear.Local.Z + bz).F;
            double sx = X87.Add(gear.World[0], w.X);
            double sy = X87.Add(gear.World[1], w.Y);
            double sz = X87.Add(X87.Add(gear.World[2], w.Z), Reach);
            _gearStarts[k] = new Vector3d(sx, sy, sz);
            _gearEnds[k] = new Vector3d(sx, sy, X87.Add(sz, -Reach));
        }

        Span<RetailRayResult> results = _gearResults.AsSpan(0, n);
        results.Clear();
        world.CastRays(this, _gearStarts.AsSpan(0, n), _gearEnds.AsSpan(0, n), results);

        bool bottom = false;
        double lift = double.MinValue;
        float mass = TypeMass;
        for (int k = 0; k < n; k++)
        {
            if (!results[k].Hit)
            {
                continue;
            }

            double d = results[k].Distance;
            if (Reach < d)
            {
                continue;
            }

            float fz;
            if (d < BottomDepth)
            {
                fz = (((((X87)BottomTop - d) * BottomSlope) + 1.0f) * mass * LoadScale).F;
                double depth = X87.Add(BottomDepth, -d);
                if (lift < depth)
                {
                    lift = depth;
                }

                bottom = true;
            }
            else
            {
                fz = (((X87)Reach - d) * SpringRate * mass * LoadScale).F;
            }

            Push(new RetailForce(0f, 0f, fz, _gearPoints[3 * k], _gearPoints[(3 * k) + 1], _gearPoints[(3 * k) + 2]));
            Velocity[0] = ((X87)Velocity[0] * Thrust * RollBrake).F;
            Velocity[1] = ((X87)Velocity[1] * Thrust * RollBrake).F;
            Momentum[0] = ((X87)Momentum[0] * SpinDamping).F;
            Momentum[1] = ((X87)Momentum[1] * SpinDamping).F;
            Momentum[2] = ((X87)Momentum[2] * SpinDamping).F;
        }

        if (bottom)
        {
            Commit(Position[0], Position[1], X87.Add(Position[2], lift), BroadPhaseRadius());
        }
    }
}
