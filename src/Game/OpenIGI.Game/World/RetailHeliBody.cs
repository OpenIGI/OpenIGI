using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

/// <summary>The fixed data a helicopter steps with: its physics type and its Heli record (<c>R = TYPE + 724</c>).</summary>
public sealed class RetailHeliDefinition
{
    /// <summary>Gets or sets the type's mass (<c>TYPE + 8</c>).</summary>
    public float Mass { get; set; }

    /// <summary>Gets or sets the sleep speed threshold (<c>TYPE + 716</c>).</summary>
    public float SleepSpeed { get; set; }

    /// <summary>Gets or sets the sleep threshold on |ω|² (<c>TYPE + 720</c>).</summary>
    public float SleepSpin { get; set; }

    /// <summary>Gets or sets the record's ten leading floats, <c>R + 0 … R + 36</c> (§5.3: gains, retentions, collective rates).</summary>
    public float[] Record { get; set; } = new float[10];

    /// <summary>Gets or sets the aerodynamic surfaces (<c>R + 40</c>, <c>R + 44</c>): point × 4096, normal normalised.</summary>
    public Wing[] Aero { get; set; } = [];
}

/// <summary>One skid: a <c>HeliWheel</c> attachment (<c>0x426D90</c>).</summary>
/// <param name="local">The attach point in the heli's body frame (<c>+240</c>), the model's <c>ATTA</c> float widened.</param>
/// <param name="travel">L0 (<c>record + 0</c>).</param>
/// <param name="bottomOut">L1 (<c>record + 4</c>).</param>
public sealed class RetailHeliSkid(Vector3d local, float travel, float bottomOut)
{
    /// <summary>Gets the attach point, body frame.</summary>
    public Vector3d Local { get; } = local;

    /// <summary>Gets L0.</summary>
    public float Travel { get; } = travel;

    /// <summary>Gets L1.</summary>
    public float BottomOut { get; } = bottomOut;

    /// <summary>Gets or sets the skid's world position (<c>+32</c>), re-seated after each step (<c>0x426FA0</c>).</summary>
    public Vector3d World { get; set; }
}

/// <summary>One rotor child (<c>0x42D2B0</c>): which pushes it makes.</summary>
/// <param name="Main">Record byte <c>+0</c>: pushes the lift and a positive yaw record.</param>
/// <param name="Tail">Record byte <c>+1</c>: pushes a negative yaw record.</param>
public readonly record struct RetailHeliRotor(bool Main, bool Tail);

/// <summary>
/// A helicopter as the original simulates it -- <c>Heli_Tick</c> (<c>0x431E30</c>), its skids (<c>0x432550</c>), its
/// channel receiver (<c>0x431B70</c>) and its rotors (<c>0x42D440</c>) -- on the shared rigid body, exactly
///.
/// </summary>
/// <remarks>
/// A helicopter has no collision points: it stands on its skids, rays of its own that push springs and lift it
/// out of the ground. Its lift is not part of its tick: each rotor pushes it from the heli's run op every frame,
/// and the next tick integrates it (<see cref="RotorTick"/>).
/// </remarks>
public sealed class RetailHeliBody : RetailRigidBody
{
    /// <summary>The linear retention the heli passes the body step: none.</summary>
    public const float LinearRetention = 1.0f;

    /// <summary>The angular retention the heli passes the body step.</summary>
    public const float AngularRetention = 0.92f;

    /// <summary>Gravity per tick squared, a quarter per skid (<c>0x4132674F</c>).</summary>
    private const float QuarterGravity = 11.1502218f;

    private static readonly double StabiliserScale = BitConverter.Int64BitsToDouble(0x3F33104B57CF96A5);
    private static readonly float PiF = BitConverter.Int32BitsToSingle(0x40490FDB);

    private readonly Vector3d[] _skidStarts = new Vector3d[4];
    private readonly Vector3d[] _skidEnds = new Vector3d[4];
    private readonly RetailRayResult[] _skidResults = new RetailRayResult[4];

    /// <summary>Initializes a new instance of the <see cref="RetailHeliBody"/> class.</summary>
    /// <param name="definition">The type and record.</param>
    public RetailHeliBody(RetailHeliDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>Gets the type and record.</summary>
    public RetailHeliDefinition Definition { get; }

    /// <summary>Gets or sets the collective, live (<c>+1824</c>).</summary>
    public float Collective { get; set; }

    /// <summary>Gets the three smoothed channels: pitch, roll, yaw (<c>+1828</c>).</summary>
    public float[] Controls { get; } = new float[3];

    /// <summary>Gets or sets the collective target (<c>+1840</c>, channel 3).</summary>
    public float CollectiveTarget { get; set; }

    /// <summary>Gets the three channel targets (<c>+1844</c>, channels 0-2).</summary>
    public float[] Targets { get; } = new float[3];

    /// <summary>Gets or sets the level's Original Thrust (<c>+1856</c>).</summary>
    public float OriginalThrust { get; set; } = 1.0f;

    /// <summary>Gets the skids, in attachment order (at most four take part).</summary>
    public List<RetailHeliSkid> Skids { get; } = [];

    /// <summary>Gets the rotors, in child order.</summary>
    public List<RetailHeliRotor> Rotors { get; } = [];

    /// <inheritdoc/>
    protected override float TypeMass => Definition.Mass;

    /// <inheritdoc/>
    protected override float SleepSpeed => Definition.SleepSpeed;

    /// <inheritdoc/>
    protected override float SleepSpin => Definition.SleepSpin;

    /// <inheritdoc/>
    protected override Wing[] AeroSurfaces => Definition.Aero;

    /// <summary>Latches an AnimTask packet's channels (<c>0x431B70</c>, every packet).</summary>
    /// <param name="channels">The programme's channels 0-3: pitch, roll, yaw targets, the collective (−1 holds).</param>
    public void Receive(ReadOnlySpan<float> channels)
    {
        if (BitConverter.SingleToInt32Bits(channels[3]) != unchecked((int)0xBF800000))
        {
            CollectiveTarget = channels[3];
        }

        Targets[0] = channels[0];
        Targets[1] = channels[1];
        Targets[2] = channels[2];
    }

    /// <summary>One helicopter tick -- <c>0x431E30</c>.</summary>
    /// <param name="world">The world its skid rays are cast in.</param>
    public void Tick(IRetailCarWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        PushedForces.Clear();
        float[] r = Definition.Record;
        float mass = TypeMass;

        // A. The collective steps toward its target.
        float rate = Collective < 0.8 ? r[9] : r[8];
        X87 x = Collective;
        float target = CollectiveTarget;
        if (x.Value < target)
        {
            x += rate;
            if (x.Value > target)
            {
                x = target;
            }
        }
        else if (x.Value > target)
        {
            x -= rate;
            if (x.Value < target)
            {
                x = target;
            }
        }

        if (x.Value < 0.001f && target == 0.0f)
        {
            x = 0.0;
        }

        Collective = x.F;

        // B. The three channels blend toward their targets.
        float k8 = r[2], k16 = r[4], k24 = r[6];
        float c1 = (((X87)1.0f - k8) * Targets[0] + (X87)k8 * Controls[0]).F;
        Controls[0] = c1;
        float c2 = (((X87)1.0f - k16) * Targets[1] + (X87)k16 * Controls[1]).F;
        Controls[1] = c2;
        float c3 = (((X87)1.0f - k24) * Targets[2] + (X87)k24 * Controls[2]).F;
        Controls[2] = c3;

        // C. The control forces, while airborne or moving.
        if ((c1 != 0f || c2 != 0f || c3 != 0f) && (ContactCount == 0 || Speed > 2.0f))
        {
            float a1 = ((X87)c1 * r[1]).F;
            float a2 = ((X87)c2 * r[3]).F;
            X87 y = (X87)c3 * r[5];
            X87 z = x * y;
            Push(Force(0f, ((X87)a1 * mass * -0.8f).F, 0f, 0f, 0f, 819.2f));
            Push(Force(((X87)a2 * mass).F, 0f, 0f, 0f, 0f, 409.6f));
            Push(Force((z * mass * 0.08f).F, 0f, 0f, 0f, 12288f, 0f));
        }

        // D. The stabiliser turns the body about its X to take out the bank.
        if (ContactCount == 0 && Speed > 2.0f)
        {
            Stabilise(r[5]);
        }

        // E. The physics.
        if (!IsAsleep())
        {
            Skid(world);
            if (AeroSurfaces.Length != 0)
            {
                ApplyAero(-Velocity[0], -Velocity[1], -Velocity[2]);
            }

            Step(world, Gravity, LinearRetention, AngularRetention);
        }

        // F. The skids follow the new pose.
        SeatSkids();
    }

    /// <summary>The rotors' pushes from the heli's run op (<c>0x42D440</c>), integrated by the next tick.</summary>
    public void RotorTick()
    {
        float[] m = M;
        float mass = TypeMass;
        foreach (RetailHeliRotor rotor in Rotors)
        {
            if (rotor.Main)
            {
                float lift = ((X87)mass * Collective * Gravity).F;
                if (lift != 0.0f)
                {
                    double angle = 0.0;
                    if (!(m[1] == 0.0f && m[4] == 0.0f))
                    {
                        angle = AngleBetween(m[1], m[4], 0.0f, m[2], m[5], m[8]);
                    }

                    Push(Force(0f, ((X87)Math.Cos(angle) * lift).F, lift, 0f, 0f, 0f));
                }
            }

            if (Collective != 0.0f && (rotor.Tail || rotor.Main) && ContactCount == 0)
            {
                X87 yaw = (X87)mass * Collective * 2.5f;
                float fx = rotor.Tail ? (-yaw).F : yaw.F;
                Push(Force(fx, 0f, 0f, 0f, 8192.0f, 0f));
            }
        }
    }

    /// <summary>
    /// The mode-0 packet's reset (<c>0x431B70</c>): the spawn pose committed, the body rebuilt from its type, the
    /// controls cleared, the collective at Original Thrust, and the level's Speed turned into the velocity.
    /// </summary>
    /// <param name="position">The spawn position (<c>+360</c>).</param>
    /// <param name="orientation">The spawn orientation (<c>+396</c>).</param>
    /// <param name="dimensions">The type's box, metres.</param>
    /// <param name="speed">The level's Speed, body frame (<c>+384</c>).</param>
    public void Reset(Vector3d position, float[] orientation, OpenIGI.Core.Numerics.Vector3f dimensions, OpenIGI.Core.Numerics.Vector3f speed)
    {
        ArgumentNullException.ThrowIfNull(orientation);
        Commit(position.X, position.Y, position.Z, BroadPhaseRadius());
        Array.Copy(orientation, M, 9);
        MatrixCounter = 0;
        InitFromType(dimensions);
        Collective = OriginalThrust;
        CollectiveTarget = OriginalThrust;
        Array.Clear(Controls);
        Array.Clear(Targets);

        float[] m = M;
        Velocity[0] = ((X87)m[2] * speed.Z + (X87)m[1] * speed.Y + (X87)speed.X * m[0]).F;
        Velocity[1] = ((X87)m[5] * speed.Z + (X87)m[4] * speed.Y + (X87)m[3] * speed.X).F;
        Velocity[2] = ((X87)m[8] * speed.Z + (X87)m[7] * speed.Y + (X87)m[6] * speed.X).F;
        UpdateSpeed();
        SeatSkids();
    }

    /// <summary><c>0x4FB700</c> for a body with no points: the box, the prev copies at the current pose.</summary>
    private void InitFromType(OpenIGI.Core.Numerics.Vector3f dimensions)
    {
        float c = ((X87)dimensions.Z * 4096.0f).F;
        float b = ((X87)dimensions.Y * 4096.0f).F;
        float a = ((X87)dimensions.X * 4096.0f).F;
        SetBox(this, Definition.Mass, a, b, c);
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

    /// <summary><c>Body_SetBox</c> (<c>0x4ECED0</c>) and <c>Body_SetMassInertia</c> (<c>0x4EC5E0</c>), with the motion cleared.</summary>
    internal static void SetBox(RetailRigidBody body, float m, float a, float b, float c)
    {
        float b2 = ((X87)b * b).F;
        float c2 = ((X87)c * c).F;
        X87 a2 = (X87)a * a;
        float iz = (((X87)b2 + a2) * m * 0.0833333358f).F;
        float iy = (((X87)c2 + a2) * m * 0.0833333358f).F;
        float ix = (((X87)c2 + b2) * m * 0.0833333358f).F;
        body.Mass = m;
        body.InverseInertia[0] = ((X87)1.0f / ix).F;
        body.InverseInertia[1] = ((X87)1.0f / iy).F;
        body.InverseInertia[2] = ((X87)1.0f / iz).F;
        body.InverseMass = ((X87)1.0f / m).F;
        body.Speed = 0f;
        Array.Clear(body.Velocity);
        Array.Clear(body.Direction);
        Array.Clear(body.Momentum);
        Array.Clear(body.LastForce);
        Array.Clear(body.AngularVelocity);
    }

    /// <summary>§2.1 D: <c>M = M · RotX(a)</c>, the bank taken out about body X.</summary>
    private void Stabilise(float gain)
    {
        float[] m = M;
        X87 s;
        if (m[0] == 0.0f && m[3] == 0.0f)
        {
            s = 0.0;
        }
        else
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)m[0] * m[0] + (X87)m[3] * m[3]);
            float hx = ((X87)m[0] * inverse).F;
            X87 hy = (X87)m[3] * inverse;
            X87 dot = (X87)0.0f * m[8] + hy * m[5] + (X87)hx * m[2];
            s = m[8] > 0.0f ? dot * -0.7f : dot * 0.7f;
        }

        float a = (s * gain * StabiliserScale).F;
        float sine = X87.ToFloat(Math.Sin(a)), negSine = X87.ToFloat(-Math.Sin(a)), cosine = X87.ToFloat(Math.Cos(a));
        float[] xr = [1f, 0f, 0f, 0f, cosine, negSine, 0f, sine, cosine];
        float[] n =
        [
            ((X87)xr[0] * m[0] + (X87)xr[6] * m[2] + (X87)xr[3] * m[1]).F,
            ((X87)xr[1] * m[0] + (X87)xr[7] * m[2] + (X87)xr[4] * m[1]).F,
            ((X87)xr[2] * m[0] + (X87)xr[8] * m[2] + (X87)xr[5] * m[1]).F,
            ((X87)xr[0] * m[3] + (X87)xr[6] * m[5] + (X87)xr[3] * m[4]).F,
            ((X87)xr[1] * m[3] + (X87)xr[7] * m[5] + (X87)xr[4] * m[4]).F,
            ((X87)xr[2] * m[3] + (X87)xr[8] * m[5] + (X87)xr[5] * m[4]).F,
            ((X87)xr[6] * m[8] + (X87)xr[0] * m[6] + (X87)xr[3] * m[7]).F,
            ((X87)xr[7] * m[8] + (X87)xr[1] * m[6] + (X87)xr[4] * m[7]).F,
            ((X87)xr[8] * m[8] + (X87)xr[2] * m[6] + (X87)xr[5] * m[7]).F,
        ];
        Array.Copy(n, m, 9);
        MatrixCounter++;
    }

    /// <summary>§2.2: the skids' rays, springs, contact damping and bottom-out lift -- <c>0x432550</c>.</summary>
    private void Skid(IRetailCarWorld world)
    {
        ContactCount = 0;
        int n = Math.Min(Skids.Count, 4);
        if (n == 0)
        {
            return;
        }

        Span<double> negative = stackalloc double[4];
        for (int k = 0; k < n; k++)
        {
            RetailHeliSkid skid = Skids[k];
            double travel = skid.Travel;
            Vector3d s = skid.World;
            s = new Vector3d(s.X, s.Y, X87.Add(s.Z, 8192.0));
            negative[k] = (-((X87)travel + 8192.0)).Value;
            _skidStarts[k] = s;
            _skidEnds[k] = new Vector3d(s.X, s.Y, X87.Add(negative[k], s.Z));
        }

        Span<RetailRayResult> results = _skidResults.AsSpan(0, n);
        results.Clear();
        world.CastRays(this, _skidStarts.AsSpan(0, n), _skidEnds.AsSpan(0, n), results);

        bool bottom = false;
        double maxLift = double.MinValue;
        float mass = TypeMass;
        for (int k = 0; k < n; k++)
        {
            if (!results[k].Hit)
            {
                continue;
            }

            double d = results[k].Distance;
            if (Math.Abs(negative[k]) < d)
            {
                continue;
            }

            RetailHeliSkid skid = Skids[k];
            double travel = skid.Travel, bottomOut = skid.BottomOut;
            double comp = ((X87)travel - ((X87)Math.Abs(d) - 8192.0)).Value;
            float fz;
            if (((X87)bottomOut + 8192.0).Value > d)
            {
                bottom = true;
                float top = ((X87)bottomOut + 8192.0f).F;
                fz = (((((X87)top - Math.Abs(d)) / top) + 1.0f) * mass * QuarterGravity).F;
                X87 lift = (X87)bottomOut + 8192.0 - d;
                if (lift.Value > maxLift)
                {
                    maxLift = lift.Value;
                }
            }
            else
            {
                bottom = false;
                fz = ((X87)comp / travel * mass * QuarterGravity).F;
            }

            Push(Force(
                0f,
                0f,
                fz,
                X87.ToFloat(skid.Local.X),
                X87.ToFloat(skid.Local.Y),
                X87.ToFloat(skid.Local.Z)));
            ContactCount++;
            Velocity[0] = ((X87)Velocity[0] * 0.95f).F;
            Velocity[1] = ((X87)Velocity[1] * 0.95f).F;
            Velocity[2] = ((X87)Velocity[2] * 0.97f).F;
            Momentum[0] = ((X87)Momentum[0] * 0.98f).F;
            Momentum[1] = ((X87)Momentum[1] * 0.98f).F;
            Momentum[2] = ((X87)Momentum[2] * 0.98f).F;
        }

        if (bottom)
        {
            Commit(Position[0], Position[1], X87.Add(Position[2], maxLift), BroadPhaseRadius());
        }
    }

    /// <summary><c>0x426FA0</c> for every skid: <c>M·l + pos</c>, each sum stored as a double.</summary>
    private void SeatSkids()
    {
        float[] m = M;
        foreach (RetailHeliSkid skid in Skids)
        {
            Vector3d l = skid.Local;
            double wx = ((X87)m[2] * l.Z + (X87)m[1] * l.Y + (X87)m[0] * l.X).Value;
            double wy = ((X87)m[5] * l.Z + (X87)m[4] * l.Y + (X87)m[3] * l.X).Value;
            double wz = ((X87)m[8] * l.Z + (X87)m[7] * l.Y + (X87)m[6] * l.X).Value;
            skid.World = new Vector3d(X87.Add(wx, Position[0]), X87.Add(wy, Position[1]), X87.Add(wz, Position[2]));
        }
    }

    /// <summary><c>0x4B2B30</c>: the angle between two float vectors, 0 for a zero one, retail's π for opposite ones.</summary>
    private static double AngleBetween(float ax, float ay, float az, float bx, float by, float bz)
    {
        if ((ax == 0f && ay == 0f && az == 0f) || (bx == 0f && by == 0f && bz == 0f))
        {
            return 0.0;
        }

        X87 dot = (X87)az * bz + (X87)ay * by + (X87)ax * bx;
        X87 la = X87.Sqrt((X87)ax * ax + (X87)ay * ay + (X87)az * az);
        X87 lb = X87.Sqrt((X87)bx * bx + (X87)by * by + (X87)bz * bz);
        double c = (dot / (la * lb)).Value;
        if (c >= 1.0)
        {
            return 0.0;
        }

        return c <= -1.0 ? PiF : Math.Atan2(Math.Sqrt((1.0 + c) * (1.0 - c)), c);
    }
}
