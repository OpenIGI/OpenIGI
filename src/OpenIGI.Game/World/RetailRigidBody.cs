using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The rigid body every physics object of the original shares -- the car's and the helicopter's -- with the step
/// <c>0x4FC0E0</c> and everything under it, transcribed with the original's exact arithmetic.
/// </summary>
/// <remarks>
/// Every field mirrors the physics object's layout; the subclasses add their
/// controls and their ground contact and call <see cref="Step"/> with their own constants.
/// </remarks>
public abstract class RetailRigidBody
{
    /// <summary>Initializes a new instance of the <see cref="RetailRigidBody"/> class.</summary>
    protected RetailRigidBody()
    {
        for (int k = 0; k < PointCount; k++)
        {
            Points[k] = new RetailCarPoint();
        }
    }

    /// <summary>Gets the type's mass, used by gravity and the springs (<c>TYPE + 8</c>).</summary>
    protected abstract float TypeMass { get; }

    /// <summary>Gets the sleep speed threshold (<c>TYPE + 716</c>).</summary>
    protected abstract float SleepSpeed { get; }

    /// <summary>Gets the sleep threshold on |ω|² (<c>TYPE + 720</c>).</summary>
    protected abstract float SleepSpin { get; }

    /// <summary>Gets the aerodynamic surfaces (<c>R + 44</c>).</summary>
    protected abstract RetailAeroSurface[] AeroSurfaces { get; }

    /// <summary>The number of body points.</summary>
    public const int PointCount = 22;

    /// <summary>Gravity per tick squared (<c>0x53346C</c>).</summary>
    public const float Gravity = 44.600887f;

    private static readonly int[] NextAxis = [1, 2, 0];

    private protected readonly Vector3d[] _starts = new Vector3d[PointCount];
    private protected readonly Vector3d[] _ends = new Vector3d[PointCount];
    private protected readonly RetailRayResult[] _results = new RetailRayResult[PointCount];
    private protected readonly int[] _indices = new int[PointCount];
    private protected readonly float[] _offsets = new float[PointCount * 3];
    private protected readonly float[] _pointVelocities = new float[PointCount * 3];

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

    /// <summary>Gets every force pushed during the last tick, in order (the subclass clears it as its tick begins).</summary>
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

    /// <summary>The velocity of a point at a world offset from the centre -- <c>0x4ECF80</c>.</summary>
    protected void PointVelocity(float rx, float ry, float rz, out float x, out float y, out float z)
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
    protected bool IsAsleep()
    {
        int contacts = ContactCount;
        bool wake = Wake;
        Wake = false;
        if (contacts <= 0 || wake || Forces.Count != 0)
        {
            return false;
        }

        if (!(Speed < SleepSpeed))
        {
            return false;
        }

        float[] w = AngularVelocity;
        X87 s = (X87)w[0] * w[0] + (X87)w[1] * w[1] + (X87)w[2] * w[2];
        return s.Value < SleepSpin;
    }

    /// <summary>The aerodynamic surfaces -- <c>0x4EC2C0</c>, handed the negated world velocity.</summary>
    protected void ApplyAero(float wx, float wy, float wz)
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
        foreach (RetailAeroSurface surface in AeroSurfaces)
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

    /// <summary>The shared rigid-body step -- <c>0x4FC0E0(body, g, kv, kL, World(), 12, &amp;excl)</c>.</summary>
    protected void Step(IRetailCarWorld world, float g, float kv, float kL)
    {
        float[] m = M;
        float s6 = m[6], s7 = m[7], s8 = m[8];
        double px = Position[0], py = Position[1], pz = Position[2];

        GravityIntegrateDamp(g, kv, kL);
        if (RunCollide)
        {
            Collide(world, g, kL);
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
    protected void GravityIntegrateDamp(float g, float kv, float kL)
    {
        float[] m = M;
        X87 gm = -((X87)g * TypeMass);
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
    protected void Integrate()
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
    protected void Push(RetailForce force)
    {
        Forces.Add(force);
        PushedForces.Add(force);
    }

    private protected static X87 Twice(X87 value) => value + value;

    /// <summary>The speed estimate and the velocity direction -- <c>0x4ECDB0</c>.</summary>
    protected void UpdateSpeed()
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
    protected static X87 ApproxLength(float vx, float vy, float vz)
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
    protected static void Normalise(float[] v)
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
    protected static void RotZ(Span<float> r, float angle)
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
    protected void Collide(IRetailCarWorld world, float g, float kL)
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
    protected int ApproachTest(float[] n, float rx, float ry, float rz, out float normalSpeed)
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
    protected void Impulse(float[] n, float rx, float ry, float rz, float d, float e)
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
    protected void ContactFriction(float[] n, float g, float mu, float kL)
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
    protected bool Clear(IRetailCarWorld world)
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
    protected float BroadPhaseRadius()
    {
        float[] dim = Dimensions;
        float mm = dim[2] < dim[1] ? dim[1] : dim[2];
        mm = dim[0] > mm ? dim[0] : mm;
        return ((X87)ModelRadius * mm).F;
    }

    /// <summary>Moves the car -- <c>0x4C6940</c> (the broad-phase relink is the world's business).</summary>
    protected void Commit(double x, double y, double z, float radius)
    {
        Position[0] = x;
        Position[1] = y;
        Position[2] = z;
        Radius = radius;
    }

}
