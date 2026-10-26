using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

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
    void CastRays(RetailRigidBody car, ReadOnlySpan<Vector3d> starts, ReadOnlySpan<Vector3d> ends, Span<RetailRayResult> results);

    /// <summary>Probes straight up from each point, as <c>0x4CB9F0</c>.</summary>
    /// <param name="car">The car probing.</param>
    /// <param name="points">The probe origins.</param>
    /// <param name="length">The probe length in world units.</param>
    /// <param name="results">One result per point.</param>
    void ProbeUp(RetailRigidBody car, ReadOnlySpan<Vector3d> points, double length, Span<RetailRayResult> results);

    /// <summary>Runs the struck object's contact handler; <see langword="true"/> when it refuses the contact.</summary>
    /// <param name="car">The car.</param>
    /// <param name="result">The hit on the object.</param>
    /// <returns>Whether the object vetoed the contact.</returns>
    bool VetoContact(RetailRigidBody car, in RetailRayResult result);
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
    public Wing[] Aero { get; set; } = [];

    /// <summary>The wheels (<c>R + 280</c>, <c>R + 284</c>).</summary>
    public CarWheelTypeDefinition[] Wheels { get; set; } = [];

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
/// original's machine code -- operation by operation, in the original's order, with each register value an <see cref="X87"/> and each float store truncated. That is what makes it
/// replay a captured retail tick bit for bit.
/// </para>
/// <para>Units are the original's: world units (1/4096 m) and ticks. The orientation <see cref="RetailRigidBody.M"/> maps body to world.</para>
/// </remarks>
public sealed class RetailCarBody : RetailRigidBody
{
    /// <summary>The most wheels the wheel pass holds.</summary>
    public const int MaxWheels = 16;

    /// <summary>The linear retention <c>Car_Tick</c> passes the body step.</summary>
    public const float LinearRetention = 0.97f;

    /// <summary>The angular retention <c>Car_Tick</c> passes the body step.</summary>
    public const float AngularRetention = 0.9f;

    /// <summary>Initializes a new instance of the <see cref="RetailCarBody"/> class.</summary>
    /// <param name="definition">The car's type and record.</param>
    public RetailCarBody(RetailCarDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        for (int i = 0; i < MaxWheels; i++)
        {
            Wheels[i] = new RetailCarWheel();
        }
    }

    /// <inheritdoc/>
    protected override float TypeMass => Definition.Mass;

    /// <inheritdoc/>
    protected override float SleepSpeed => Definition.SleepSpeed;

    /// <inheritdoc/>
    protected override float SleepSpin => Definition.SleepSpin;

    /// <inheritdoc/>
    protected override Wing[] AeroSurfaces => Definition.Aero;

    /// <summary>Gets the car's type and record.</summary>
    public RetailCarDefinition Definition { get; }

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

    /// <summary>How far each wheel lowers its body point, world units (record <c>+604</c>), which a drawn wheel rises back by.</summary>
    public float WheelDrop { get; set; }

    /// <summary>The speed a drawn wheel is carried at -- <c>Wheel_Run</c>, <c>0x4301C0</c>.</summary>
    /// <param name="wheel">The wheel's entry.</param>
    /// <returns>World units a tick.</returns>
    /// <remarks>
    /// A wheel whose body point touches is carried at that point's speed, the point handed over in the body's own frame as the
    /// original hands it; a driven one off the ground at its share of the engine's thrust; a crawler's track by its turn factor.
    /// </remarks>
    public double DrawnWheelSpeed(int wheel)
    {
        CarWheelTypeDefinition entry = Definition.Wheels[wheel];
        int point = DrawnWheelPoint(wheel);
        double speed = 0d;
        if (point < PointCount && Points[point].Contact)
        {
            float[] p = Points[point].Position;
            PointVelocity(p[0], p[1], p[2], out float x, out float y, out float z);
            speed = Math.Sqrt((double)(((X87)x * x) + ((X87)y * y) + ((X87)z * z)).Value);
        }
        else if (entry.IsEngine && DrivenWheels > 0)
        {
            speed = ((((X87)Throttle * 44.600887f) * Definition.EngineForce) / DrivenWheels * 0.6f).Value;
        }

        if (Definition.SkidSteer && point < PointCount)
        {
            speed *= SkidSteerFactor(point).Value;
        }

        return speed;
    }

    /// <summary>How far a drawn wheel's spring has it up -- the body point's compression less the wheel drop, <c>Wheel_Move</c>.</summary>
    /// <param name="wheel">The wheel's entry.</param>
    /// <returns>World units, along the body's up axis.</returns>
    public double DrawnWheelLift(int wheel)
    {
        int point = DrawnWheelPoint(wheel);
        return point < PointCount ? ((X87)Points[point].Compression - WheelDrop).Value : -WheelDrop;
    }

    // The body point a wheel reads: one stored below nought is -10 less it.
    private int DrawnWheelPoint(int wheel)
    {
        int point = Definition.Wheels[wheel].ContactPoint;
        return point < 0 ? -10 - point : point;
    }

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

            Step(world, Gravity, LinearRetention, AngularRetention);
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
            CarWheelTypeDefinition w = d.Wheels[i];
            int b = w.ContactPoint;
            bool enabled = Wheels[i].Enabled;
            if (b >= 0 && enabled)
            {
                X87 t2 = (X87)w.MaxSpringDistance + 2048.0;
                float[] l = Points[b].Position;
                double wx = ((X87)m[0] * l[0] + (X87)m[2] * l[2] + (X87)m[1] * l[1]).Value;
                double wy = ((X87)m[3] * l[0] + (X87)m[5] * l[2] + (X87)m[4] * l[1]).Value;
                double wz = ((X87)m[6] * l[0] + (X87)m[8] * l[2] + (X87)m[7] * l[1]).Value;
                double sx = ((X87)Position[0] + wx).Value;
                double sy = ((X87)Position[1] + wy).Value;
                X87 z = (X87)Position[2] + wz;
                double sz = ((X87)w.MaxSpringDistance + z + 4096.0).Value;
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
            CarWheelTypeDefinition w = d.Wheels[i];
            float travel = w.MaxSpringDistance;
            int b = w.ContactPoint;
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
                    float fz = ((X87)comp / travel * w.SpringStiffness * d.Mass * Gravity).F;
                    if (bottomed)
                    {
                        fz = (((X87)2.0f - (X87)dist * (1.0 / 4096.0)) * fz).F;
                    }

                    Push(Force(0.0f, 0.0f, fz, point.Position[0], point.Position[1], point.Position[2]));
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
        CarWheelTypeDefinition w = d.Wheels[i];
        RetailCarWheel wheel = Wheels[i];
        float[] m = M;
        float angle = ((X87)w.MaxAngle * 0.0174532924f * Steering).F;

        if (w.IsEngine && ((wheel.Contact && Throttle != 0.0f) || Planar))
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
            Push(Force(gx, gy, gz, wheel.Point[0], wheel.Point[1], wheel.Point[2]));
        }

        if (!wheel.Contact || b < 0)
        {
            return;
        }

        float a = w.IsSteering ? angle : 0.0f;
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

        Push(Force(fx, fy, fz, p[0], p[1], p[2]));
    }

}
