using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One force pushed onto a rigid body (<c>0x4ECF50</c>): force and application point, both in the body frame.</summary>
/// <param name="Fx">The force, body X.</param>
/// <param name="Fy">The force, body Y.</param>
/// <param name="Fz">The force, body Z.</param>
/// <param name="Px">The application point, body X.</param>
/// <param name="Py">The application point, body Y.</param>
/// <param name="Pz">The application point, body Z.</param>
public readonly record struct RetailForce(float Fx, float Fy, float Fz, float Px, float Py, float Pz);

/// <summary>What a world query (<c>0x4CC2A0</c> or <c>0x4CB9F0</c>) reports for one ray.</summary>
public struct RetailRayResult
{
    /// <summary>The struck object (<c>+0</c>), or <see langword="null"/> for terrain.</summary>
    public object? Struck { get; set; }

    /// <summary>The hit point (<c>+8</c>); the query starts it at the ray's end.</summary>
    public Vector3d Point { get; set; }

    /// <summary>Whether the front-face material slot is set (<c>+76</c> non-null).</summary>
    public bool HasFrontMaterial { get; set; }

    /// <summary>Whether the ray hit anything (<c>+88</c>).</summary>
    public bool Hit { get; set; }

    /// <summary>The distance from the ray start to the hit (<c>+96</c>), in world units.</summary>
    public double Distance { get; set; }

    /// <summary>The surface normal (<c>+56</c>), not guaranteed unit length.</summary>
    public float NormalX { get; set; }

    /// <summary>The surface normal, Y.</summary>
    public float NormalY { get; set; }

    /// <summary>The surface normal, Z.</summary>
    public float NormalZ { get; set; }

    /// <summary>Whether the hit carries a surface record (<c>+80</c> non-null).</summary>
    public bool HasSurface { get; set; }

    /// <summary>The level material row the hit face names (<c>w[0xD3C + 4g]</c>): 1 for terrain and for a face with none.</summary>
    public int Material { get; set; }

    /// <summary>The surface record's <c>+12</c>; non-zero forces a collision contact to be accepted.</summary>
    public float SurfaceForce { get; set; }
}

/// <summary>The world a <see cref="RetailCarBody"/> steps in: its two ray queries and the struck-object handlers.</summary>
public interface IRetailCarWorld
{
    /// <summary>Casts segments against the world, as <c>0x4CC2A0</c> with mask 12 and the car excluded.</summary>
    /// <param name="car">The car casting.</param>
    /// <param name="starts">The segment starts.</param>
    /// <param name="ends">The segment ends.</param>
    /// <param name="results">One result per segment.</param>
    void CastRays(RetailCarBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results);

    /// <summary>Probes straight up from each point, as <c>0x4CB9F0</c>.</summary>
    /// <param name="car">The car probing.</param>
    /// <param name="points">The probe origins.</param>
    /// <param name="length">The probe length in world units.</param>
    /// <param name="results">One result per point.</param>
    void ProbeUp(RetailCarBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results);

    /// <summary>Runs the struck object's contact handler; <see langword="true"/> when it refuses the contact.</summary>
    /// <param name="car">The car.</param>
    /// <param name="result">The hit on the object.</param>
    /// <returns>Whether the object vetoed the contact.</returns>
    bool VetoContact(RetailCarBody car, in RetailRayResult result);
}

/// <summary>One of a car's 22 body points (<c>car + 444 + 56b</c>).</summary>
public sealed class RetailCarPoint
{
    /// <summary>The side for skid steer, 0 or 1 (<c>+444</c>).</summary>
    public int Side { get; set; }

    /// <summary>The restitution of an impact at this point (<c>+452</c>).</summary>
    public float Restitution { get; set; }

    /// <summary>The point, body frame (<c>+456</c>).</summary>
    public float[] Position { get; } = new float[3];

    /// <summary>Takes part in generic collision this tick (<c>+468</c>).</summary>
    public bool Collides { get; set; }

    /// <summary>Is a collision point at all (<c>+469</c>).</summary>
    public bool IsCollisionPoint { get; set; }

    /// <summary>Had a contact this tick (<c>+470</c>).</summary>
    public bool Contact { get; set; }

    /// <summary>The tyre's wheel-space X friction (<c>+472</c>).</summary>
    public float FrictionX { get; set; }

    /// <summary>The tyre's wheel-space Y friction, and the generic contact friction (<c>+476</c>).</summary>
    public float FrictionY { get; set; }

    /// <summary>The last collision normal (<c>+480</c>).</summary>
    public float[] Normal { get; } = new float[3];

    /// <summary>The last collision distance (<c>+492</c>).</summary>
    public float Distance { get; set; }

    /// <summary>The smoothed suspension compression (<c>+496</c>).</summary>
    public float Compression { get; set; }
}

/// <summary>One wheel's per-tick state (<c>car + 1884 + 28i</c>).</summary>
public sealed class RetailCarWheel
{
    /// <summary>The suspension is enabled (<c>+1884</c>).</summary>
    public bool Enabled { get; set; }

    /// <summary>The wheel touched the ground this tick (<c>+1885</c>).</summary>
    public bool Contact { get; set; }

    /// <summary>The contact normal, world, unit (<c>+1888</c>).</summary>
    public float[] Normal { get; } = new float[3];

    /// <summary>The force point, body frame: the body point lifted 0.4 m (<c>+1900</c>).</summary>
    public float[] Point { get; } = new float[3];
}

/// <summary>One wheel of the physics record (<c>R + 284 + 20i</c>).</summary>
/// <param name="Steerable">The wheel turns with the steering.</param>
/// <param name="Driven">The engine drives the wheel.</param>
/// <param name="MaximumSteerDegrees">The steering lock.</param>
/// <param name="Body">The body point the wheel hangs from; negative for none.</param>
/// <param name="Travel">The suspension travel, world units.</param>
/// <param name="Stiffness">The spring stiffness, a share of the car's weight.</param>
public readonly record struct RetailCarWheelDefinition(
    bool Steerable,
    bool Driven,
    float MaximumSteerDegrees,
    int Body,
    float Travel,
    float Stiffness);

/// <summary>One aerodynamic surface of the physics record (<c>R + 44 + 28j</c>).</summary>
/// <param name="Coefficient">The drag coefficient.</param>
/// <param name="Nx">The surface normal, X.</param>
/// <param name="Ny">The surface normal, Y.</param>
/// <param name="Nz">The surface normal, Z.</param>
/// <param name="Px">The application point, X.</param>
/// <param name="Py">The application point, Y.</param>
/// <param name="Pz">The application point, Z.</param>
public readonly record struct RetailAeroSurface(float Coefficient, float Nx, float Ny, float Nz, float Px, float Py, float Pz);

/// <summary>The fixed data a car steps with: the physics type (<c>car + 436</c>) and its car record (<c>TYPE + 724</c>).</summary>
public sealed class RetailCarDefinition
{
    /// <summary>The type's mass, used by the springs and gravity (<c>TYPE + 8</c>).</summary>
    public float Mass { get; set; }

    /// <summary>The sleep speed threshold (<c>TYPE + 716</c>).</summary>
    public float SleepSpeed { get; set; }

    /// <summary>The sleep threshold on |ω|² (<c>TYPE + 720</c>).</summary>
    public float SleepSpin { get; set; }

    /// <summary>The steering retention (<c>R + 24</c>).</summary>
    public float SteeringRetention { get; set; }

    /// <summary>The engine force (<c>R + 28</c>).</summary>
    public float EngineForce { get; set; }

    /// <summary>The throttle retention (<c>R + 32</c>).</summary>
    public float ThrottleRetention { get; set; }

    /// <summary>The aerodynamic surfaces (<c>R + 40</c>, <c>R + 44</c>).</summary>
    public RetailAeroSurface[] Aero { get; set; } = [];

    /// <summary>The wheels (<c>R + 280</c>, <c>R + 284</c>).</summary>
    public RetailCarWheelDefinition[] Wheels { get; set; } = [];

    /// <summary>Tracked drive (<c>R + 609</c>).</summary>
    public bool SkidSteer { get; set; }
}

/// <summary>
/// A car as the original simulates it: a free 3D rigid body on sprung wheels, stepped by <c>Car_Tick</c>
/// (<c>0x42ECA0</c>) with the original's exact arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// Every field mirrors a field of the original car object, and the step is a transcription of the
/// disassembly in the original -- operation by operation, in the original's order,
/// with each register value an <see cref="X87"/> and each float store truncated. That is what makes it
/// replay a captured retail tick bit for bit.
/// </para>
/// <para>Units are the original's: world units (1/4096 m) and ticks. The orientation <see cref="M"/> maps body to world.</para>
/// </remarks>
public sealed class RetailCarBody
{
    /// <summary>The number of body points.</summary>
    public const int PointCount = 22;

    /// <summary>The most wheels the wheel pass holds.</summary>
    public const int MaxWheels = 16;

    /// <summary>Gravity per tick squared (<c>0x53346C</c>).</summary>
    public const float Gravity = 44.600887f;

    /// <summary>The linear retention <c>Car_Tick</c> passes the body step.</summary>
    public const float LinearRetention = 0.97f;

    /// <summary>The angular retention <c>Car_Tick</c> passes the body step.</summary>
    public const float AngularRetention = 0.9f;

    private static readonly int[] NextAxis = [1, 2, 0];

    private readonly Vector3d[] _starts = new Vector3d[PointCount];
    private readonly Vector3d[] _ends = new Vector3d[PointCount];
    private readonly RetailRayResult[] _results = new RetailRayResult[PointCount];
    private readonly int[] _indices = new int[PointCount];
    private readonly float[] _offsets = new float[PointCount * 3];
    private readonly float[] _pointVelocities = new float[PointCount * 3];

    /// <summary>Initializes a new instance of the <see cref="RetailCarBody"/> class.</summary>
    /// <param name="definition">The car's type and record.</param>
    public RetailCarBody(RetailCarDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        for (int k = 0; k < PointCount; k++)
        {
            Points[k] = new RetailCarPoint();
        }

        for (int i = 0; i < MaxWheels; i++)
        {
            Wheels[i] = new RetailCarWheel();
        }
    }

    /// <summary>Gets the car's type and record.</summary>
    public RetailCarDefinition Definition { get; }

    /// <summary>Gets or sets the task the car is, which its own rays skip (the query's exclusion block).</summary>
    public object? Owner { get; set; }

    /// <summary>Gets or sets the car as other rays see it: its collision model, kept at the body's pose.</summary>
    public RetailRayModel? RayModel { get; set; }

    /// <summary>The position, world (<c>+32</c>).</summary>
    public double[] Position { get; } = new double[3];

    /// <summary>The broad-phase radius (<c>+68</c>).</summary>
    public float Radius { get; set; }

    /// <summary>The orientation, row-major, body to world, then the re-orthonormalisation counter as bits (<c>+112</c>).</summary>
    public float[] M { get; } = new float[9];

    /// <summary>The re-orthonormalisation counter (<c>+148</c>).</summary>
    public int MatrixCounter { get; set; }

    /// <summary>The scale factors (<c>+152</c>); the largest times <see cref="ModelRadius"/> is the broad-phase radius.</summary>
    public float[] Dimensions { get; } = new float[3];

    /// <summary>The model's bounding radius (<c>0x4D0950</c>: model → +12 → +12 → <c>f[+120]</c>).</summary>
    public float ModelRadius { get; set; } = 1f;

    /// <summary>The body mass (<c>+240</c>).</summary>
    public float Mass { get; set; }

    /// <summary>The inverse mass (<c>+244</c>).</summary>
    public float InverseMass { get; set; }

    /// <summary>The speed estimate, <c>ApproxLength(v)</c> (<c>+248</c>).</summary>
    public float Speed { get; set; }

    /// <summary>The inverse inertia, body frame, diagonal (<c>+252</c>).</summary>
    public float[] InverseInertia { get; } = new float[3];

    /// <summary>The linear velocity, world, units per tick (<c>+264</c>).</summary>
    public float[] Velocity { get; } = new float[3];

    /// <summary>The velocity direction (<c>+276</c>).</summary>
    public float[] Direction { get; } = new float[3];

    /// <summary>The angular momentum, body frame (<c>+288</c>).</summary>
    public float[] Momentum { get; } = new float[3];

    /// <summary>The last integrated force sum, body frame (<c>+300</c>).</summary>
    public float[] LastForce { get; } = new float[3];

    /// <summary>The angular velocity, world frame after integration (<c>+312</c>).</summary>
    public float[] AngularVelocity { get; } = new float[3];

    /// <summary>The pending forces (<c>+324</c>, count <c>+332</c>).</summary>
    public List<RetailForce> Forces { get; } = [];

    /// <summary>Gets every force pushed during the last <see cref="Tick"/>, in order.</summary>
    public List<RetailForce> PushedForces { get; } = [];

    /// <summary>The force count at the last integration (<c>+336</c>).</summary>
    public int IntegratedForceCount { get; set; }

    /// <summary>The contact counter (<c>+440</c>).</summary>
    public int ContactCount { get; set; }

    /// <summary>The body points (<c>+444</c>).</summary>
    public RetailCarPoint[] Points { get; } = new RetailCarPoint[PointCount];

    /// <summary>Non-zero lets <c>Body_Collide</c> run (<c>+1676</c>).</summary>
    public int CollideEnabled { get; set; }

    /// <summary>The wake flag, read and cleared by the sleep test (<c>+1680</c>).</summary>
    public bool Wake { get; set; }

    /// <summary>The previous tick's orientation (<c>+1684</c>).</summary>
    public float[] PreviousM { get; } = new float[9];

    /// <summary>The previous tick's counter (<c>+1720</c>).</summary>
    public int PreviousMatrixCounter { get; set; }

    /// <summary>The previous tick's position (<c>+1728</c>).</summary>
    public double[] PreviousPosition { get; } = new double[3];

    /// <summary>Run the generic collision pass (<c>+1752</c>).</summary>
    public bool RunCollide { get; set; }

    /// <summary>Planar mode (<c>+1753</c>).</summary>
    public bool Planar { get; set; }

    /// <summary>This tick's rotation delta, <c>M · prevᵀ</c> (<c>+1756</c>).</summary>
    public float[] DeltaM { get; } = new float[9];

    /// <summary>This tick's rotation delta counter (<c>+1792</c>).</summary>
    public int DeltaMatrixCounter { get; set; }

    /// <summary>This tick's displacement (<c>+1800</c>).</summary>
    public double[] Displacement { get; } = new double[3];

    /// <summary>The smoothed throttle (<c>+1824</c>).</summary>
    public float Throttle { get; set; }

    /// <summary>The smoothed steering (<c>+1836</c>).</summary>
    public float Steering { get; set; }

    /// <summary>The throttle target (<c>+1840</c>).</summary>
    public float ThrottleTarget { get; set; }

    /// <summary>The steering target (<c>+1852</c>).</summary>
    public float SteeringTarget { get; set; }

    /// <summary>The handbrake (<c>+1860</c>).</summary>
    public bool Handbrake { get; set; }

    /// <summary>The number of driven wheels (<c>+1880</c>).</summary>
    public int DrivenWheels { get; set; }

    /// <summary>The wheels' per-tick state (<c>+1884</c>).</summary>
    public RetailCarWheel[] Wheels { get; } = new RetailCarWheel[MaxWheels];

    /// <summary>Runs one car tick -- <c>0x42ECA0</c>.</summary>
    /// <param name="world">The world the rays are cast in.</param>
    public void Tick(IRetailCarWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        RetailCarDefinition d = Definition;
        PushedForces.Clear();

        Throttle = (((X87)1.0f - d.ThrottleRetention) * ThrottleTarget + (X87)d.ThrottleRetention * Throttle).F;
        Steering = (((X87)1.0f - d.SteeringRetention) * SteeringTarget + (X87)Steering * d.SteeringRetention).F;
        if (ThrottleTarget == 0.0f && MathF.Abs(Throttle) < 0.01f)
        {
            Throttle = 0.0f;
        }

        UpdateWheels(world);
        if (!IsAsleep())
        {
            if (d.Aero.Length != 0)
            {
                ApplyAero(-Velocity[0], -Velocity[1], -Velocity[2]);
            }

            Step(world);
        }
    }

    /// <summary>The wheel pass -- <c>0x42EF00</c>.</summary>
    private void UpdateWheels(IRetailCarWorld world)
    {
        RetailCarDefinition d = Definition;
        float[] m = M;
        int n = 0;
        bool anyBottomed = false;
        double maxLift = double.MinValue;

        int wheelCount = Math.Min(d.Wheels.Length, MaxWheels);
        for (int i = 0; i < wheelCount; i++)
        {
            RetailCarWheelDefinition w = d.Wheels[i];
            int b = w.Body;
            bool enabled = Wheels[i].Enabled;
            if (b >= 0 && enabled)
            {
                X87 t2 = (X87)w.Travel + 2048.0;
                float[] l = Points[b].Position;
                double wx = ((X87)m[0] * l[0] + (X87)m[2] * l[2] + (X87)m[1] * l[1]).Value;
                double wy = ((X87)m[3] * l[0] + (X87)m[5] * l[2] + (X87)m[4] * l[1]).Value;
                double wz = ((X87)m[6] * l[0] + (X87)m[8] * l[2] + (X87)m[7] * l[1]).Value;
                double sx = ((X87)Position[0] + wx).Value;
                double sy = ((X87)Position[1] + wy).Value;
                X87 z = (X87)Position[2] + wz;
                double sz = ((X87)w.Travel + z + 4096.0).Value;
                double ez = ((X87)sz - t2 - 4096.0).Value;
                Points[b].Collides = false;
                _indices[n] = i;
                _starts[n] = new Vector3d(sx, sy, sz);
                _ends[n] = new Vector3d(sx, sy, ez);
                n++;
            }

            if (!enabled && b >= 0)
            {
                // The original also writes here for a negative index -- outside the point table.
                Points[b].Collides = true;
            }
        }

        Span<RetailRayResult> results = _results.AsSpan(0, n);
        results.Clear();
        world.CastRays(this, _starts.AsSpan(0, n), _ends.AsSpan(0, n), results);

        for (int k = 0; k < n; k++)
        {
            int i = _indices[k];
            RetailCarWheelDefinition w = d.Wheels[i];
            float travel = w.Travel;
            int b = w.Body;
            RetailCarPoint point = Points[b];
            float comp = 0.0f;
            bool bottomed = false;
            bool contact = false;
            ref RetailRayResult hit = ref results[k];
            if (hit.Hit)
            {
                double dist = hit.Distance;
                if (dist < 4096.0)
                {
                    X87 lift = (X87)4096.0 - dist;
                    comp = travel;
                    bottomed = true;
                    anyBottomed = true;
                    contact = true;
                    if (maxLift < lift.Value)
                    {
                        maxLift = lift.Value;
                    }
                }
                else if (dist > 4096.0)
                {
                    X87 top = (X87)travel + 4096.0f;
                    if (top.Value > dist)
                    {
                        contact = true;
                        comp = (top - dist).F;
                    }
                }

                if (contact)
                {
                    float fz = ((X87)comp / travel * w.Stiffness * d.Mass * Gravity).F;
                    if (bottomed)
                    {
                        fz = (((X87)2.0f - (X87)dist * (1.0 / 4096.0)) * fz).F;
                    }

                    Push(new RetailForce(0.0f, 0.0f, fz, point.Position[0], point.Position[1], point.Position[2]));
                }
            }

            float old = point.Compression;
            X87 diff = X87.Abs((X87)old - comp);
            if (diff.Value > 0.001f)
            {
                point.Compression = comp > old ? comp : ((X87)old * 0.05f + (X87)comp * 0.95f).F;
            }

            RetailCarWheel wheel = Wheels[i];
            wheel.Contact = contact;
            point.Contact = contact;
            if (contact)
            {
                float[] nrm = wheel.Normal;
                nrm[0] = hit.NormalX;
                nrm[1] = hit.NormalY;
                nrm[2] = hit.NormalZ;
                Normalise(nrm);
                wheel.Point[0] = point.Position[0];
                wheel.Point[1] = point.Position[1];
                wheel.Point[2] = ((X87)point.Position[2] + 1638.4f).F;
                ContactCount = 1;
                WheelDrive(i, b);
            }
        }

        if (anyBottomed)
        {
            double qz = ((X87)Position[2] + maxLift).Value;
            Commit(Position[0], Position[1], qz, BroadPhaseRadius());
        }
    }

    /// <summary>The drive force and the tyre -- <c>0x430D10</c>.</summary>
    private void WheelDrive(int i, int b)
    {
        RetailCarDefinition d = Definition;
        RetailCarWheelDefinition w = d.Wheels[i];
        RetailCarWheel wheel = Wheels[i];
        float[] m = M;
        float angle = ((X87)w.MaximumSteerDegrees * 0.0174532924f * Steering).F;

        if (w.Driven && ((wheel.Contact && Throttle != 0.0f) || Planar))
        {
            X87 drive = (X87)d.EngineForce * Throttle * Gravity / (double)DrivenWheels;
            float driveF = drive.F;
            float speed = Speed;
            float[] n = wheel.Normal;
            float fx = m[1], fy = m[4], fz = m[7];
            float dx = 0f, dy = 0f, dz = 0f;
            if (!(n[0] == fx && n[1] == fy && n[2] == fz))
            {
                X87 cx = (X87)fz * n[1] - (X87)fy * n[2];
                X87 cy = -((X87)fz * n[0] - (X87)fx * n[2]);
                X87 cz = (X87)fy * n[0] - (X87)fx * n[1];
                dx = (cy * n[2] - cz * n[1]).F;
                dy = (-(cx * n[2] - cz * n[0])).F;
                dz = (cx * n[1] - cy * n[0]).F;
            }

            if (d.SkidSteer)
            {
                drive = SkidSteerFactor(b) * driveF;
            }

            X87 mag = drive - speed;
            float ex = (mag * dx).F;
            float ey = (mag * dy).F;
            X87 ez = mag * dz;
            float bx = ((X87)ex * m[0] + ez * m[6] + (X87)ey * m[3]).F;
            float by = ((X87)ey * m[4] + (X87)ex * m[1] + ez * m[7]).F;
            float bz = ((X87)ex * m[2] + ez * m[8] + (X87)ey * m[5]).F;
            Span<float> r = stackalloc float[9];
            RotZ(r, -angle);
            float gx = ((X87)r[0] * bx + (X87)r[2] * bz + (X87)r[1] * by).F;
            float gy = ((X87)r[3] * bx + (X87)r[5] * bz + (X87)r[4] * by).F;
            float gz = ((X87)r[6] * bx + (X87)r[8] * bz + (X87)r[7] * by).F;
            Push(new RetailForce(gx, gy, gz, wheel.Point[0], wheel.Point[1], wheel.Point[2]));
        }

        if (!wheel.Contact || b < 0)
        {
            return;
        }

        float a = w.Steerable ? angle : 0.0f;
        float k1 = Points[b].FrictionX;
        float k2 = Points[b].FrictionY;
        if (Handbrake)
        {
            Throttle = ((X87)Throttle * 0.95f).F;
            k2 = k1;
        }

        TyreFriction(wheel.Point, wheel.Normal, a, k1, k2);
    }

    /// <summary>The tracked-drive factor -- <c>0x4304B0</c>.</summary>
    private X87 SkidSteerFactor(int b)
    {
        float t = Steering;
        int side = Points[b].Side;
        if (t > 0.01f)
        {
            if (side == 1)
            {
                X87 x = (X87)0.5f - t;
                return x + x;
            }

            return 1.0f;
        }

        if (t < -0.01f && side == 0)
        {
            X87 x = (X87)t + 0.5f;
            return x + x;
        }

        return 1.0f;
    }

    /// <summary>The tyre -- <c>0x430960</c>.</summary>
    private void TyreFriction(float[] p, float[] n, float angle, float k1, float k2)
    {
        float[] m = M;
        float load = ((X87)Mass * Gravity * n[2]).F;
        float rx = ((X87)m[0] * p[0] + (X87)p[1] * m[1] + (X87)p[2] * m[2]).F;
        float ry = ((X87)m[5] * p[2] + (X87)m[3] * p[0] + (X87)m[4] * p[1]).F;
        float rz = ((X87)p[2] * m[8] + (X87)p[1] * m[7] + (X87)p[0] * m[6]).F;
        PointVelocity(rx, ry, rz, out float vx, out float vy, out float vz);
        X87 vn = (X87)vz * n[2] + (X87)vy * n[1] + (X87)vx * n[0];
        float qx = (vn * n[0]).F;
        float qy = (vn * n[1]).F;
        float qz = (vn * n[2]).F;
        X87 tx = (X87)qx - vx;
        X87 ty = (X87)qy - vy;
        X87 tz = (X87)qz - vz;
        float slip = X87.Sqrt(tx * tx + ty * ty + tz * tz).F;
        if (!(tx.Value == 0 && ty.Value == 0 && tz.Value == 0))
        {
            X87 inv = (X87)1.0 / slip;
            tx *= inv;
            ty *= inv;
            tz *= inv;
        }

        float lx = (ty * m[3] + tx * m[0] + tz * m[6]).F;
        float ly = (ty * m[4] + tz * m[7] + tx * m[1]).F;
        float lz = (ty * m[5] + tx * m[2] + tz * m[8]).F;
        Span<float> r = stackalloc float[9];
        RotZ(r, angle);
        float wx = ((X87)r[2] * lz + (X87)r[1] * ly + (X87)r[0] * lx).F;
        float wy = ((X87)r[5] * lz + (X87)r[4] * ly + (X87)r[3] * lx).F;
        float wz = ((X87)r[8] * lz + (X87)r[7] * ly + (X87)r[6] * lx).F;
        X87 fa = (X87)wx * k1;
        X87 fb = (X87)wy * k2;
        float gx = ((X87)r[6] * wz + (X87)r[3] * fb + (X87)r[0] * fa).F;
        float gy = ((X87)r[7] * wz + (X87)r[4] * fb + (X87)r[1] * fa).F;
        float gz = ((X87)r[8] * wz + (X87)r[5] * fb + (X87)r[2] * fa).F;

        float fx, fy, fz;
        if (((X87)load * 4.0f / Mass).Value < slip)
        {
            fx = ((X87)gx * load * 0.24f).F;
            fy = ((X87)gy * load * 0.24f).F;
            fz = ((X87)gz * load * 0.24f).F;
        }
        else
        {
            fx = ((X87)gx * slip * Mass * 0.1f).F;
            fy = ((X87)slip * gy * Mass * 0.1f).F;
            fz = ((X87)slip * gz * Mass * 0.1f).F;
        }

        Push(new RetailForce(fx, fy, fz, p[0], p[1], p[2]));
    }

    /// <summary>The velocity of a point at a world offset from the centre -- <c>0x4ECF80</c>.</summary>
    private void PointVelocity(float rx, float ry, float rz, out float x, out float y, out float z)
    {
        float[] w = AngularVelocity;
        float cx = ((X87)rz * w[1] - (X87)w[2] * ry).F;
        float cy = (-((X87)rz * w[0] - (X87)w[2] * rx)).F;
        float cz = ((X87)w[0] * ry - (X87)rx * w[1]).F;
        x = ((X87)Velocity[0] + cx).F;
        y = ((X87)Velocity[1] + cy).F;
        z = ((X87)Velocity[2] + cz).F;
    }

    /// <summary>The sleep test -- <c>0x4FC7B0</c>.</summary>
    private bool IsAsleep()
    {
        int contacts = ContactCount;
        bool wake = Wake;
        Wake = false;
        if (contacts <= 0 || wake || Forces.Count != 0)
        {
            return false;
        }

        if (!(Speed < Definition.SleepSpeed))
        {
            return false;
        }

        float[] w = AngularVelocity;
        X87 s = (X87)w[0] * w[0] + (X87)w[1] * w[1] + (X87)w[2] * w[2];
        return s.Value < Definition.SleepSpin;
    }

    /// <summary>The aerodynamic surfaces -- <c>0x4EC2C0</c>, handed the negated world velocity.</summary>
    private void ApplyAero(float wx, float wy, float wz)
    {
        float[] m = M;
        float[] l =
        [
            ((X87)m[6] * wz + (X87)m[3] * wy + (X87)m[0] * wx).F,
            ((X87)m[7] * wz + (X87)m[1] * wx + (X87)m[4] * wy).F,
            ((X87)m[8] * wz + (X87)m[2] * wx + (X87)m[5] * wy).F,
        ];
        X87 s = ApproxLength(l[0], l[1], l[2]);
        float q = (s * s * 0.000326999987f).F;
        if (q <= 0.0f)
        {
            return;
        }

        float[] dir = [l[0], l[1], l[2]];
        Normalise(dir);
        foreach (RetailAeroSurface surface in Definition.Aero)
        {
            if (surface.Coefficient <= 0.0f)
            {
                continue;
            }

            X87 f = ((X87)dir[1] * surface.Ny + (X87)dir[2] * surface.Nz + (X87)dir[0] * surface.Nx) * surface.Coefficient * q;
            Push(new RetailForce(
                ((X87)surface.Nx * f).F,
                ((X87)surface.Ny * f).F,
                ((X87)surface.Nz * f).F,
                surface.Px,
                surface.Py,
                surface.Pz));
        }
    }

    /// <summary>The shared rigid-body step -- <c>0x4FC0E0</c> with the car's constants.</summary>
    private void Step(IRetailCarWorld world)
    {
        float[] m = M;
        float s6 = m[6], s7 = m[7], s8 = m[8];
        double px = Position[0], py = Position[1], pz = Position[2];

        GravityIntegrateDamp(Gravity, LinearRetention, AngularRetention);
        if (RunCollide)
        {
            Collide(world, Gravity, AngularRetention);
        }

        if (Planar)
        {
            Velocity[2] = 0.0f;
            UpdateSpeed();
        }

        if (Speed > 40.96f)
        {
            px = ((X87)Velocity[0] + px).Value;
            py = ((X87)Velocity[1] + py).Value;
            pz = ((X87)Velocity[2] + pz).Value;
        }

        if (Planar)
        {
            m[0] = ((X87)s8 * m[4] - (X87)s7 * m[5]).F;
            m[1] = (-((X87)s8 * m[3] - (X87)s6 * m[5])).F;
            m[2] = ((X87)s7 * m[3] - (X87)s6 * m[4]).F;
            m[3] = ((X87)s7 * m[2] - (X87)s8 * m[1]).F;
            m[4] = (-((X87)s6 * m[2] - (X87)s8 * m[0])).F;
            m[5] = ((X87)s6 * m[1] - (X87)s7 * m[0]).F;
            m[6] = s6;
            m[7] = s7;
            m[8] = s8;
        }

        Commit(px, py, pz, BroadPhaseRadius());
        if (!Clear(world))
        {
            Commit(PreviousPosition[0], PreviousPosition[1], PreviousPosition[2], BroadPhaseRadius());
            Array.Copy(PreviousM, m, 9);
            MatrixCounter = PreviousMatrixCounter;
        }

        Displacement[0] = ((X87)Position[0] - PreviousPosition[0]).Value;
        Displacement[1] = ((X87)Position[1] - PreviousPosition[1]).Value;
        Displacement[2] = ((X87)Position[2] - PreviousPosition[2]).Value;

        float[] q = PreviousM;
        float t0 = q[0], t1 = q[3], t2 = q[6], t3 = q[1], t4 = q[4], t5 = q[7], t6 = q[2], t7 = q[5], t8 = q[8];
        float[] dm = DeltaM;
        dm[0] = ((X87)m[2] * t6 + (X87)m[0] * t0 + (X87)m[1] * t3).F;
        dm[1] = ((X87)m[2] * t7 + (X87)t1 * m[0] + (X87)m[1] * t4).F;
        dm[2] = ((X87)t5 * m[1] + (X87)t8 * m[2] + (X87)m[0] * t2).F;
        dm[3] = ((X87)m[3] * t0 + (X87)t6 * m[5] + (X87)m[4] * t3).F;
        dm[4] = ((X87)m[5] * t7 + (X87)m[4] * t4 + (X87)m[3] * t1).F;
        dm[5] = ((X87)t8 * m[5] + (X87)t5 * m[4] + (X87)m[3] * t2).F;
        dm[6] = ((X87)t3 * m[7] + (X87)m[6] * t0 + (X87)t6 * m[8]).F;
        dm[7] = ((X87)m[8] * t7 + (X87)m[7] * t4 + (X87)m[6] * t1).F;
        dm[8] = ((X87)m[6] * t2 + (X87)t8 * m[8] + (X87)t5 * m[7]).F;
        DeltaMatrixCounter = PreviousMatrixCounter + MatrixCounter + 1;

        Array.Copy(Position, PreviousPosition, 3);
        Array.Copy(m, PreviousM, 9);
        PreviousMatrixCounter = MatrixCounter;
    }

    /// <summary>Gravity, integration and damping -- <c>0x4FBF90</c>.</summary>
    private void GravityIntegrateDamp(float g, float kv, float kL)
    {
        float[] m = M;
        X87 gm = -((X87)g * Definition.Mass);
        Push(new RetailForce((gm * m[6]).F, (gm * m[7]).F, (gm * m[8]).F, 0.0f, 0.0f, 0.0f));
        Integrate();
        for (int k = 0; k < 3; k++)
        {
            Momentum[k] = ((X87)Momentum[k] * kL).F;
        }

        for (int k = 0; k < 3; k++)
        {
            Velocity[k] = ((X87)Velocity[k] * kv).F;
        }

        UpdateSpeed();
    }

    /// <summary>Sums the forces into v and L, then turns the orientation by ω -- <c>0x4EC6A0</c>.</summary>
    private void Integrate()
    {
        float[] m = M;

        // A. The orientation as a quaternion.
        float qx, qy, qz, qw;
        X87 tr = (X87)m[4] + m[0] + m[8];
        if (tr.Value > 0.0f)
        {
            X87 s = X87.Sqrt(tr + 1.0f);
            qw = (s * 0.5f).F;
            X87 k = (X87)0.5f / s;
            qx = (-(((X87)m[5] - m[7]) * k)).F;
            qy = (-(((X87)m[6] - m[2]) * k)).F;
            qz = (-(((X87)m[1] - m[3]) * k)).F;
        }
        else
        {
            int i = m[4] > m[0] ? 1 : 0;
            if (m[8] > m[4 * i])
            {
                i = 2;
            }

            int j = NextAxis[i];
            int k = NextAxis[j];
            Span<float> q = stackalloc float[3];
            X87 s = X87.Sqrt((X87)m[4 * i] - m[4 * j] - m[4 * k] + 1.0f);
            q[i] = (s * 0.5f).F;
            if (s.Value != 0.0)
            {
                s = (X87)0.5f / s;
            }

            q[j] = (((X87)m[(3 * j) + i] + m[(3 * i) + j]) * s).F;
            q[k] = (((X87)m[(3 * k) + i] + m[(3 * i) + k]) * s).F;
            qw = (-(((X87)m[(3 * j) + k] - m[(3 * k) + j]) * s)).F;
            qx = q[0];
            qy = q[1];
            qz = q[2];
        }

        // B. The forces.
        int count = Forces.Count;
        IntegratedForceCount = count;
        if (count > 0)
        {
            X87 sumX = 0.0, sumY = 0.0;
            float sumZ = 0.0f;
            X87 torqueX = 0.0, torqueY = 0.0, torqueZ = 0.0;
            foreach (RetailForce e in Forces)
            {
                sumX += e.Fx;
                sumY += e.Fy;
                sumZ = ((X87)sumZ + e.Fz).F;
                if (e.Px == 0.0f && e.Py == 0.0f && e.Pz == 0.0f)
                {
                    continue;
                }

                if (e.Fx == 0.0f && e.Fy == 0.0f && e.Fz == 0.0f)
                {
                    continue;
                }

                X87 t1 = (X87)e.Fz * e.Py - (X87)e.Pz * e.Fy;
                float t2 = (-((X87)e.Fz * e.Px - (X87)e.Fx * e.Pz)).F;
                float t3 = ((X87)e.Fy * e.Px - (X87)e.Fx * e.Py).F;
                torqueX += t1;
                torqueY += t2;
                torqueZ += t3;
            }

            float fy = sumY.F;
            float fx = sumX.F;
            float tz = torqueZ.F;
            float ty = torqueY.F;
            LastForce[0] = fx;
            LastForce[1] = fy;
            LastForce[2] = sumZ;
            float im = InverseMass;
            float ax = ((X87)im * fx).F;
            float ay = ((X87)im * fy).F;
            X87 az = (X87)im * sumZ;
            float dvx = (az * m[2] + (X87)ay * m[1] + (X87)ax * m[0]).F;
            float dvy = (az * m[5] + (X87)ax * m[3] + (X87)ay * m[4]).F;
            X87 dvz = (X87)ay * m[7] + (X87)ax * m[6] + az * m[8];
            Forces.Clear();
            Velocity[0] = ((X87)dvx + Velocity[0]).F;
            Velocity[1] = ((X87)dvy + Velocity[1]).F;
            Velocity[2] = (dvz + Velocity[2]).F;
            Momentum[0] = (torqueX + Momentum[0]).F;
            Momentum[1] = ((X87)ty + Momentum[1]).F;
            Momentum[2] = ((X87)tz + Momentum[2]).F;
        }

        // C. The rotation, only while there is angular momentum.
        float[] l = Momentum;
        if (!(l[0] == 0.0f && l[1] == 0.0f && l[2] == 0.0f))
        {
            float[] ii = InverseInertia;
            const float Zero = 0.0f;
            float wbx = ((X87)Zero * l[2] + (X87)Zero * l[1] + (X87)ii[0] * l[0]).F;
            float wby = ((X87)Zero * l[2] + (X87)ii[1] * l[1] + (X87)Zero * l[0]).F;
            float wbz = ((X87)ii[2] * l[2] + (X87)Zero * l[1] + (X87)Zero * l[0]).F;
            float[] w = AngularVelocity;
            w[0] = ((X87)wby * m[1] + (X87)m[0] * wbx + (X87)wbz * m[2]).F;
            w[1] = ((X87)m[3] * wbx + (X87)wbz * m[5] + (X87)wby * m[4]).F;
            w[2] = ((X87)m[7] * wby + (X87)wbz * m[8] + (X87)wbx * m[6]).F;

            X87 a = (X87)w[0] * 0.5f;
            X87 b = (X87)w[1] * 0.5f;
            X87 c = (X87)w[2] * 0.5f;
            float cf = c.F;
            float t1 = ((c - b) * ((X87)qy - qz)).F;
            float t2 = (((X87)qz + qy) * -a).F;
            float t3 = (((X87)cf + b) * ((X87)qw - qx)).F;
            float t4 = (((X87)cf + a) * ((X87)qy + qx)).F;
            float t5 = (((X87)qw - qz) * b).F;
            X87 t6r = ((X87)qz + qw) * -b;
            float t6 = t6r.F;
            X87 sum = t6r + t5 + t4;
            float t7 = ((((X87)cf - a) * ((X87)qx - qy) + sum) * 0.5f).F;
            X87 dw = ((X87)qx + qw) * a + t7 - sum;
            float d1 = ((X87)t7 + t2 - t6).F;
            float d2 = ((X87)t7 + t3 - t5).F;
            float d3 = ((X87)t7 + t1 - t4).F;
            qx = (dw + qx).F;
            qy = ((X87)d1 + qy).F;
            qz = ((X87)d2 + qz).F;
            X87 qwr = (X87)d3 + qw;

            for (int pass = 0; pass < 2; pass++)
            {
                X87 len = X87.Sqrt((X87)qz * qz + (X87)qy * qy + (X87)qx * qx + qwr * qwr);
                X87 inv = (X87)1.0 / len;
                qx = ((X87)qx * inv).F;
                qy = ((X87)qy * inv).F;
                qz = ((X87)qz * inv).F;
                qwr *= inv;
            }

            float aa = Twice((X87)qx * qx).F;
            float b2 = Twice((X87)qy * qx).F;
            float cc = Twice((X87)qz * qx).F;
            float dq = Twice((X87)qx * qwr).F;
            float ee = Twice((X87)qy * qy).F;
            float fq = Twice((X87)qz * qy).F;
            float gg = Twice((X87)qy * qwr).F;
            float hh = Twice((X87)qz * qz).F;
            X87 trr = Twice(qwr * qz);

            m[0] = ((X87)1.0f - ee - hh).F;
            m[1] = ((X87)b2 - trr).F;
            m[2] = ((X87)gg + cc).F;
            m[3] = (trr + b2).F;
            X87 x = (X87)1.0f - aa;
            m[4] = (x - hh).F;
            m[5] = ((X87)fq - dq).F;
            m[6] = ((X87)cc - gg).F;
            m[7] = ((X87)fq + dq).F;
            m[8] = (x - ee).F;
            MatrixCounter = 0;
        }

        // D.
        UpdateSpeed();
    }

    /// <summary>Pushes a force onto the body -- <c>0x4ECF50</c>.</summary>
    private void Push(RetailForce force)
    {
        Forces.Add(force);
        PushedForces.Add(force);
    }

    private static X87 Twice(X87 value) => value + value;

    /// <summary>The speed estimate and the velocity direction -- <c>0x4ECDB0</c>.</summary>
    private void UpdateSpeed()
    {
        float[] v = Velocity;
        X87 s = ApproxLength(v[0], v[1], v[2]);
        Speed = s.F;
        if (s.Value > 0.0)
        {
            Direction[0] = v[0];
            Direction[1] = v[1];
            Direction[2] = v[2];
            Normalise(Direction);
        }
        else
        {
            Direction[0] = 0.0f;
            Direction[1] = 0.0f;
            Direction[2] = 0.0f;
        }
    }

    /// <summary>The length estimate without a square root -- <c>0x4B2A10</c>.</summary>
    private static X87 ApproxLength(float vx, float vy, float vz)
    {
        float x = MathF.Abs(vx), y = MathF.Abs(vy), z = MathF.Abs(vz);
        float hi, mid, lo;
        if (x > y && x > z)
        {
            hi = x;
            (mid, lo) = y > z ? (y, z) : (z, y);
        }
        else if (y > z)
        {
            hi = y;
            (mid, lo) = x > z ? (x, z) : (z, x);
        }
        else
        {
            hi = z;
            (mid, lo) = x > y ? (x, y) : (y, x);
        }

        return (X87)hi + ((X87)lo * 0.317837238f + (X87)mid * 0.414213568f);
    }

    /// <summary>Normalises a float vector in place unless it is all zero: <c>(float)(x * (1.0 / len))</c>.</summary>
    private static void Normalise(float[] v)
    {
        if (v[0] == 0.0f && v[1] == 0.0f && v[2] == 0.0f)
        {
            return;
        }

        X87 len = X87.Sqrt((X87)v[0] * v[0] + (X87)v[1] * v[1] + (X87)v[2] * v[2]);
        X87 inv = (X87)1.0 / len;
        v[0] = ((X87)v[0] * inv).F;
        v[1] = ((X87)v[1] * inv).F;
        v[2] = ((X87)v[2] * inv).F;
    }

    /// <summary>A rotation about Z -- <c>0x4B3BE0</c>: <c>{c, −s, 0, s, c, 0, 0, 0, 1}</c> from <c>fsin</c>/<c>fcos</c>.</summary>
    private static void RotZ(Span<float> r, float angle)
    {
        float s = X87.ToFloat(Math.Sin(angle));
        float c = X87.ToFloat(Math.Cos(angle));
        r[0] = c;
        r[1] = X87.ToFloat(-Math.Sin(angle));
        r[2] = 0.0f;
        r[3] = s;
        r[4] = c;
        r[5] = 0.0f;
        r[6] = 0.0f;
        r[7] = 0.0f;
        r[8] = 1.0f;
    }

    /// <summary>The generic collision pass -- <c>0x4FB9A0</c>; at most one impact per tick.</summary>
    private void Collide(IRetailCarWorld world, float g, float kL)
    {
        if (CollideEnabled == 0)
        {
            return;
        }

        float[] m = M;
        int n = 0;
        for (int k = 0; k < PointCount; k++)
        {
            RetailCarPoint point = Points[k];
            if (!point.IsCollisionPoint || !point.Collides)
            {
                continue;
            }

            point.Contact = false;
            float[] l = point.Position;
            float rx = ((X87)m[2] * l[2] + (X87)m[1] * l[1] + (X87)m[0] * l[0]).F;
            float ry = ((X87)m[5] * l[2] + (X87)m[4] * l[1] + (X87)m[3] * l[0]).F;
            float rz = ((X87)m[8] * l[2] + (X87)m[7] * l[1] + (X87)m[6] * l[0]).F;
            double sx = ((X87)Position[0] + rx).Value;
            double sy = ((X87)Position[1] + ry).Value;
            double sz = ((X87)Position[2] + rz).Value;
            PointVelocity(rx, ry, rz, out float ux, out float uy, out float uz);
            _offsets[(3 * n) + 0] = rx;
            _offsets[(3 * n) + 1] = ry;
            _offsets[(3 * n) + 2] = rz;
            _pointVelocities[(3 * n) + 0] = ux;
            _pointVelocities[(3 * n) + 1] = uy;
            _pointVelocities[(3 * n) + 2] = uz;
            _indices[n] = k;
            _starts[n] = new Vector3d(sx, sy, sz);
            _ends[n] = new Vector3d(((X87)ux + sx).Value, ((X87)uy + sy).Value, ((X87)uz + sz).Value);
            n++;
        }

        if (n == 0)
        {
            return;
        }

        ContactCount = 0;
        Span<RetailRayResult> results = _results.AsSpan(0, n);
        results.Clear();
        world.CastRays(this, _starts.AsSpan(0, n), _ends.AsSpan(0, n), results);

        bool veto = false;
        for (int j = 0; j < n; j++)
        {
            ref RetailRayResult hit = ref results[j];
            if (!hit.Hit)
            {
                continue;
            }

            bool force = hit.HasSurface && hit.SurfaceForce != 0.0f;
            if (hit.Struck is not null)
            {
                veto = world.VetoContact(this, hit);
                if (veto)
                {
                    hit.Hit = false;
                    continue;
                }
            }

            bool accept;
            if (!force)
            {
                float ux = _pointVelocities[3 * j], uy = _pointVelocities[(3 * j) + 1], uz = _pointVelocities[(3 * j) + 2];
                X87 len = X87.Sqrt((X87)ux * ux + (X87)uz * uz + (X87)uy * uy);
                accept = (len + 40.96f).Value > hit.Distance;
            }
            else
            {
                accept = true;
            }

            hit.Hit = accept;
            if (!accept)
            {
                continue;
            }

            RetailCarPoint point = Points[_indices[j]];
            point.Distance = X87.ToFloat(hit.Distance);
            float[] nrm = [hit.NormalX, hit.NormalY, hit.NormalZ];
            Normalise(nrm);
            Array.Copy(nrm, point.Normal, 3);
            float rx = _offsets[3 * j], ry = _offsets[(3 * j) + 1], rz = _offsets[(3 * j) + 2];
            if (ApproachTest(nrm, rx, ry, rz, out float vn) != 2)
            {
                continue;
            }

            ContactCount++;
            point.Contact = true;
            Impulse(nrm, rx, ry, rz, vn, point.Restitution);
            ContactFriction(nrm, g, point.FrictionY, kL);
            if (veto)
            {
                for (int k = 0; k < 3; k++)
                {
                    Velocity[k] = ((X87)Velocity[k] * 0.25f).F;
                }

                UpdateSpeed();
            }

            return;
        }
    }

    /// <summary>Classifies a contact by its normal speed -- <c>0x4FC870</c>.</summary>
    private int ApproachTest(float[] n, float rx, float ry, float rz, out float normalSpeed)
    {
        PointVelocity(rx, ry, rz, out float ux, out float uy, out float uz);
        X87 vn = (X87)uz * n[2] + (X87)uy * n[1] + (X87)ux * n[0];
        normalSpeed = vn.F;
        if (vn.Value > 1.0f)
        {
            return 0;
        }

        return vn.Value > -1.0f ? 1 : 2;
    }

    /// <summary>The impact impulse -- <c>0x4FC8E0</c>.</summary>
    private void Impulse(float[] n, float rx, float ry, float rz, float d, float e)
    {
        float[] m = M;
        float im = InverseMass;
        float i0 = InverseInertia[0], i4 = InverseInertia[1], i8 = InverseInertia[2];
        const float Zero = 0.0f;
        X87 cx = (X87)ry * n[2] - (X87)n[1] * rz;
        X87 cy = -((X87)rx * n[2] - (X87)n[0] * rz);
        X87 cz = (X87)n[1] * rx - (X87)n[0] * ry;
        float cbx = (cx * m[0] + cz * m[6] + cy * m[3]).F;
        float cby = (cx * m[1] + cz * m[7] + cy * m[4]).F;
        X87 cbz = cx * m[2] + cz * m[8] + cy * m[5];
        float ux = ((X87)Zero * cbz + (X87)Zero * cby + (X87)i0 * cbx).F;
        float uy = ((X87)Zero * cbz + (X87)i4 * cby + (X87)Zero * cbx).F;
        X87 uz = cbz * i8 + (X87)Zero * cby + (X87)Zero * cbx;
        float wvx = ((X87)uy * m[1] + (X87)ux * m[0] + uz * m[2]).F;
        float wvy = ((X87)uy * m[4] + (X87)ux * m[3] + uz * m[5]).F;
        X87 wvz = (X87)uy * m[7] + (X87)ux * m[6] + uz * m[8];
        float kx = ((X87)wvy * rz - wvz * ry).F;
        X87 ky = -((X87)wvx * rz - wvz * rx);
        X87 kz = (X87)wvx * ry - (X87)wvy * rx;
        X87 impulse = -(((X87)e + 1.0f) * d);
        X87 den = ky * n[1] + (X87)kx * n[0] + kz * n[2] + im;
        X87 j = impulse / den;
        float px = (j * n[0]).F, py = (j * n[1]).F, pz = (j * n[2]).F;
        X87 tx = (X87)pz * ry - (X87)py * rz;
        X87 ty = -((X87)pz * rx - (X87)px * rz);
        X87 tz = (X87)py * rx - (X87)px * ry;
        float tbx = (tz * m[6] + ty * m[3] + tx * m[0]).F;
        float tby = (tz * m[7] + ty * m[4] + tx * m[1]).F;
        X87 tbz = tx * m[2] + tz * m[8] + ty * m[5];
        float lx = ((X87)tbx + Momentum[0]).F;
        Momentum[0] = lx;
        float ly = ((X87)tby + Momentum[1]).F;
        Momentum[1] = ly;
        X87 lzr = tbz + Momentum[2];
        Momentum[2] = lzr.F;
        float wbx = (lzr * Zero + (X87)ly * Zero + (X87)lx * i0).F;
        float wby = (lzr * Zero + (X87)ly * i4 + (X87)lx * Zero).F;
        float wbz = (lzr * i8 + (X87)ly * Zero + (X87)lx * Zero).F;
        float[] w = AngularVelocity;
        w[0] = ((X87)m[1] * wby + (X87)m[0] * wbx + (X87)m[2] * wbz).F;
        w[1] = ((X87)m[4] * wby + (X87)wbz * m[5] + (X87)wbx * m[3]).F;
        w[2] = ((X87)m[7] * wby + (X87)wbz * m[8] + (X87)wbx * m[6]).F;
        X87 dvx = (X87)px * im;
        float dvy = ((X87)py * im).F;
        float dvz = ((X87)pz * im).F;
        Velocity[0] = ((X87)Velocity[0] + dvx).F;
        Velocity[1] = ((X87)Velocity[1] + dvy).F;
        Velocity[2] = ((X87)Velocity[2] + dvz).F;
        UpdateSpeed();
    }

    /// <summary>The Coulomb clamp on the tangential velocity -- <c>0x4FCCA0</c>.</summary>
    private void ContactFriction(float[] n, float g, float mu, float kL)
    {
        X87 load = X87.Abs((X87)g * n[2]);
        if (load.Value == 0.0)
        {
            return;
        }

        load *= mu;
        float[] d = Direction;
        float cx = ((X87)d[1] * n[2] - (X87)d[2] * n[1]).F;
        float cy = (-((X87)d[0] * n[2] - (X87)d[2] * n[0])).F;
        float cz = ((X87)d[0] * n[1] - (X87)d[1] * n[0]).F;
        float len = X87.Sqrt((X87)cy * cy + (X87)cz * cz + (X87)cx * cx).F;
        float tx = 0.0f, ty = 0.0f, tz = 0.0f;
        if (!(len < 0.00001f))
        {
            X87 czr = cz;
            if (!(cx == 0.0f && cy == 0.0f && cz == 0.0f))
            {
                X87 inv = (X87)1.0 / len;
                cx = ((X87)cx * inv).F;
                cy = ((X87)cy * inv).F;
                czr = (X87)cz * inv;
            }

            tx = ((X87)cy * n[2] - czr * n[1]).F;
            ty = (-((X87)cx * n[2] - czr * n[0])).F;
            tz = ((X87)cx * n[1] - (X87)cy * n[0]).F;
        }

        X87 p = (X87)tx * Velocity[0] + (X87)tz * Velocity[2] + (X87)ty * Velocity[1];
        float pf = p.F;
        X87 k = load.Value > Math.Abs(p.Value) ? (X87)MathF.Abs(pf) : load;
        float dx = ((X87)tx * k).F;
        float dy = ((X87)ty * k).F;
        X87 dz = (X87)tz * k;
        Velocity[0] = ((X87)dx + Velocity[0]).F;
        Velocity[1] = ((X87)dy + Velocity[1]).F;
        Velocity[2] = (dz + Velocity[2]).F;
        UpdateSpeed();
        for (int i = 0; i < 3; i++)
        {
            Momentum[i] = ((X87)Momentum[i] * kL).F;
        }
    }

    /// <summary>Whether no collision point is buried -- <c>0x4FC620</c>.</summary>
    private bool Clear(IRetailCarWorld world)
    {
        float[] m = M;
        int n = 0;
        for (int k = 0; k < PointCount; k++)
        {
            RetailCarPoint point = Points[k];
            if (!point.IsCollisionPoint || !point.Collides)
            {
                continue;
            }

            _indices[n] = k;
            float[] l = point.Position;
            X87 px = (X87)m[2] * l[2] + (X87)m[1] * l[1] + (X87)m[0] * l[0];
            float py = ((X87)m[5] * l[2] + (X87)m[4] * l[1] + (X87)m[3] * l[0]).F;
            float pz = ((X87)m[8] * l[2] + (X87)m[7] * l[1] + (X87)m[6] * l[0]).F;
            _starts[n] = new Vector3d(
                ((X87)Position[0] + px).Value,
                ((X87)Position[1] + py).Value,
                ((X87)Position[2] + pz).Value);
            n++;
        }

        if (n == 0)
        {
            return true;
        }

        Span<RetailRayResult> results = _results.AsSpan(0, n);
        results.Clear();
        world.ProbeUp(this, _starts.AsSpan(0, n), 20480.0, results);
        for (int j = 0; j < n; j++)
        {
            if (results[j].Hit && results[j].HasSurface)
            {
                Points[_indices[j]].Contact = true;
                ContactCount++;
                return false;
            }
        }

        return true;
    }

    /// <summary>The broad-phase radius: the largest scale factor times the model radius.</summary>
    private float BroadPhaseRadius()
    {
        float[] dim = Dimensions;
        float mm = dim[2] < dim[1] ? dim[1] : dim[2];
        mm = dim[0] > mm ? dim[0] : mm;
        return ((X87)ModelRadius * mm).F;
    }

    /// <summary>Moves the car -- <c>0x4C6940</c> (the broad-phase relink is the world's business).</summary>
    private void Commit(double x, double y, double z, float radius)
    {
        Position[0] = x;
        Position[1] = y;
        Position[2] = z;
        Radius = radius;
    }
}
