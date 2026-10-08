using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Physics;

/// <summary>A force on a body and where on it it acts, <c>PointForce_s</c>: both in the body's own frame.</summary>
/// <param name="Force">The force, <c>tForce</c>.</param>
/// <param name="Point">Where it acts, relative to the centre of mass, <c>tPoint</c>.</param>
public readonly record struct Igi2PointForce(Vector3f Force, Vector3f Point);

/// <summary>
/// A rigid body's mass, inertia and motion, <c>Physics_s</c>, and the integrator that moves it a tick
/// (<c>Physics_Update</c>, <c>igi2 0x5A9580</c>), with the client's 24-bit truncating FPU arithmetic in disassembly order.
/// </summary>
/// <remarks>
/// <para>
/// Forces are gathered in the body's frame and summed once a tick: their sum, over the mass, turned into the world by the
/// orientation, is the change of velocity; the moments they make about the centre are added to the angular momentum, which
/// is kept in the body's frame. The angular velocity is the momentum through the inverse inertia, turned into the world.
/// The orientation is stepped as a quaternion and written back as a matrix.
/// </para>
/// <para>
/// An orientation is nine floats row by row; a body vector goes into the world as each row's dot with it.
/// </para>
/// </remarks>
public sealed class Igi2RigidBody
{
    /// <summary>The <c>Real32x3_FastLen</c> weight of the middle component, <c>0x3ED413CD</c>.</summary>
    private static readonly float MiddleWeight = BitConverter.Int32BitsToSingle(0x3ED413CD);

    /// <summary>The <c>Real32x3_FastLen</c> weight of the smallest component, <c>0x3EA2BB90</c>.</summary>
    private static readonly float SmallestWeight = BitConverter.Int32BitsToSingle(0x3EA2BB90);

    /// <summary>A box's moment factor, one twelfth as the float <c>0x3DAAAAAB</c>.</summary>
    private static readonly float Twelfth = BitConverter.Int32BitsToSingle(0x3DAAAAAB);

    /// <summary>The axis after each, as the largest-diagonal step walks them.</summary>
    private static readonly int[] Next = [1, 2, 0];

    private readonly List<Igi2PointForce> _forces = [];

    /// <summary>Gets the mass, <c>vM</c>.</summary>
    public float Mass { get; private set; }

    /// <summary>Gets the inverse of the mass, <c>vMInv</c>.</summary>
    public float InverseMass { get; private set; }

    /// <summary>Gets the approximate speed of the centre of mass, <c>vCMSpeed</c>: <c>Real32x3_FastLen</c> of the velocity.</summary>
    public float Speed { get; private set; }

    /// <summary>Gets or sets the inverse moments of inertia about the body's axes, <c>vIxxInv</c>, <c>vIyyInv</c> and <c>vIzzInv</c>.</summary>
    public Vector3f InverseInertia { get; set; }

    /// <summary>Gets or sets the velocity of the centre of mass in the world, <c>tCMVel</c>: units a tick.</summary>
    /// <remarks>Setting it does not refresh <see cref="Speed"/>; <see cref="UpdateSpeedApproximation"/> does.</remarks>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets the direction of <see cref="Velocity"/> at its last approximation, <c>tCMVelDir</c>.</summary>
    public Vector3f VelocityDirection { get; private set; }

    /// <summary>Gets or sets the angular momentum in the body's frame, <c>tAngMom</c>.</summary>
    public Vector3f AngularMomentum { get; set; }

    /// <summary>Gets the forces summed at the last update, <c>tForceTot</c>, in the body's frame.</summary>
    public Vector3f TotalForce { get; private set; }

    /// <summary>Gets or sets the angular velocity in the world, <c>tAngVel</c>.</summary>
    public Vector3f AngularVelocity { get; set; }

    /// <summary>Gets the forces waiting for the next update, <c>ptForces</c>.</summary>
    public IReadOnlyList<Igi2PointForce> Forces => _forces;

    /// <summary>Gets how many forces the last update summed, <c>nPreviousForces</c>.</summary>
    public int PreviousForceCount { get; private set; }

    /// <summary>Sets the mass and moments and stops the body, <c>Physics_Init</c> (<c>igi2 0x5A9460</c>).</summary>
    /// <param name="mass">The mass.</param>
    /// <param name="ixx">The moment about the first axis.</param>
    /// <param name="iyy">The moment about the second axis.</param>
    /// <param name="izz">The moment about the third axis.</param>
    public void Init(float mass, float ixx, float iyy, float izz)
    {
        Mass = mass;
        Velocity = default;
        VelocityDirection = default;
        AngularMomentum = default;
        TotalForce = default;
        AngularVelocity = default;
        Speed = 0f;
        _forces.Clear();
        PreviousForceCount = 0;
        InverseInertia = new Vector3f(((Fpu)1f / ixx).F, ((Fpu)1f / iyy).F, ((Fpu)1f / izz).F);
        InverseMass = ((Fpu)1f / mass).F;
    }

    /// <summary>Sets a solid box's mass and moments, <c>Physics_InitBox</c> (<c>igi2 0x5AA0B0</c>).</summary>
    /// <param name="mass">The mass.</param>
    /// <param name="x">The box's extent along the first axis.</param>
    /// <param name="y">Along the second.</param>
    /// <param name="z">Along the third.</param>
    public void InitBox(float mass, float x, float y, float z)
    {
        Fpu x2 = (Fpu)x * x, y2 = (Fpu)y * y, z2 = (Fpu)z * z;
        float izz = (((y2 + x2) * mass) * Twelfth).F;
        float iyy = (((z2 + x2) * mass) * Twelfth).F;
        float ixx = (((z2 + y2) * mass) * Twelfth).F;
        Init(mass, ixx, iyy, izz);
    }

    /// <summary>Queues a force for the next update.</summary>
    /// <param name="force">The force and where it acts, in the body's frame.</param>
    public void AddForce(in Igi2PointForce force) => _forces.Add(force);

    /// <summary>
    /// Refreshes <see cref="Speed"/> and <see cref="VelocityDirection"/> from the velocity,
    /// <c>Physics_UpdateSpeedApproximation</c> (<c>igi2 0x5A9F70</c>).
    /// </summary>
    /// <remarks>
    /// The speed is <c>Real32x3_FastLen</c> (<c>igi2 0x561820</c>): the largest component's magnitude plus weighted
    /// shares of the other two. A body with no speed has no direction.
    /// </remarks>
    public void UpdateSpeedApproximation()
    {
        Vector3f v = Velocity;
        Speed = FastLength(v).F;
        if (!(Speed > 0f))
        {
            VelocityDirection = default;
            return;
        }

        VelocityDirection = IsZero(v) ? v : Scale(v, InverseLength(v));
    }

    /// <summary>A point's velocity in the world, <c>Physics_GetPointVelocity</c> (<c>igi2 0x5AA730</c>): the spin about the centre plus the centre's.</summary>
    /// <param name="arm">The point, relative to the centre of mass, in the world.</param>
    /// <returns>Its velocity.</returns>
    public Vector3f PointVelocity(Vector3f arm)
    {
        Vector3f w = AngularVelocity;
        float x = (((Fpu)arm.Z * w.Y) - ((Fpu)w.Z * arm.Y)).F;
        float y = (-(((Fpu)arm.Z * w.X) - ((Fpu)w.Z * arm.X))).F;
        float z = (((Fpu)w.X * arm.Y) - ((Fpu)arm.X * w.Y)).F;
        Vector3f c = Velocity;
        return new Vector3f(((Fpu)c.X + x).F, ((Fpu)c.Y + y).F, ((Fpu)c.Z + z).F);
    }

    /// <summary>Moves the body a tick, <c>Physics_Update</c> (<c>igi2 0x5A9580</c>).</summary>
    /// <param name="m">The orientation, nine floats row by row; turned by the body's spin.</param>
    /// <returns>Whether the body turned: <see langword="false"/> when it had no angular momentum.</returns>
    /// <remarks>
    /// <para>
    /// The orientation becomes a quaternion first. The queued forces are summed, and each one acting off the centre adds its
    /// moment; the sum over the mass, turned into the world, is added to the velocity, the moments to the angular momentum,
    /// and the queue is emptied.
    /// </para>
    /// <para>
    /// With angular momentum, the angular velocity is it through the inverse inertia, turned into the world. The quaternion
    /// takes a step of half the angular velocity times itself, is normalised twice and is written back as the orientation.
    /// </para>
    /// </remarks>
    public bool Update(float[] m)
    {
        ArgumentNullException.ThrowIfNull(m);
        (Fpu qx, Fpu qy, Fpu qz, Fpu qw) = ToQuaternion(m);

        int count = _forces.Count;
        PreviousForceCount = count;
        if (count > 0)
        {
            SumForces(m);
        }

        Vector3f l = AngularMomentum;
        if (IsZero(l))
        {
            UpdateSpeedApproximation();
            return false;
        }

        Vector3f inertia = InverseInertia;
        Fpu lz0 = (Fpu)l.Z * 0f, ly0 = (Fpu)l.Y * 0f;
        float bx = ((((Fpu)inertia.X * l.X) + ly0) + lz0).F;
        Fpu lx0 = (Fpu)l.X * 0f;
        float by = ((((Fpu)inertia.Y * l.Y) + lx0) + lz0).F;
        float bz = ((((Fpu)inertia.Z * l.Z) + lx0) + ly0).F;
        float wx = ((((Fpu)m[1] * by) + ((Fpu)bz * m[2])) + ((Fpu)m[0] * bx)).F;
        float wy = ((((Fpu)m[5] * bz) + ((Fpu)m[4] * by)) + ((Fpu)m[3] * bx)).F;
        float wz = ((((Fpu)bz * m[8]) + ((Fpu)by * m[7])) + ((Fpu)m[6] * bx)).F;
        AngularVelocity = new Vector3f(wx, wy, wz);

        Fpu hx = (Fpu)wx * 0.5f, hy = (Fpu)wy * 0.5f, hz = (Fpu)wz * 0.5f;
        Fpu a = (hz - hy) * (qy - qz);
        Fpu yy = (qz + qy) * -hx;
        Fpu xw = (hz + hy) * (qw - qx);
        Fpu zz = (hz + hx) * (qy + qx);
        Fpu xz = (qw - qz) * hy;
        Fpu yz = (qz + qw) * -hy;
        Fpu yw = (yz + xz) + zz;
        Fpu t = (((hz - hx) * (qx - qy)) + yw) * 0.5f;
        Fpu dx = (((qx + qw) * hx) + t) - yw;
        Fpu dy = (yy + t) - yz;
        Fpu dz = (xw + t) - xz;
        Fpu dw = (t + a) - zz;
        qx = new Fpu((dx + qx).F);
        qy = new Fpu((dy + qy).F);
        qz = new Fpu((dz + qz).F);
        qw = dw + qw;

        Normalise(ref qx, ref qy, ref qz, ref qw);
        Normalise(ref qx, ref qy, ref qz, ref qw);
        ToMatrix(qx, qy, qz, qw, m);
        UpdateSpeedApproximation();
        return true;
    }

    /// <summary><c>Real32x3_FastLen</c> (<c>igi2 0x561820</c>): the largest magnitude plus the middle and smallest, weighted.</summary>
    /// <param name="v">The vector.</param>
    /// <returns>The approximate length.</returns>
    public static Fpu FastLength(Vector3f v)
    {
        float x = v.X < 0f ? -v.X : v.X, y = v.Y < 0f ? -v.Y : v.Y, z = v.Z < 0f ? -v.Z : v.Z;
        float largest, middle, smallest;
        if (x > y && x > z)
        {
            (largest, middle, smallest) = y > z ? (x, y, z) : (x, z, y);
        }
        else if (y > z)
        {
            (largest, middle, smallest) = x > z ? (y, x, z) : (y, z, x);
        }
        else
        {
            (largest, middle, smallest) = x > y ? (z, x, y) : (z, y, x);
        }

        return (Fpu)largest + (((Fpu)middle * MiddleWeight) + ((Fpu)smallest * SmallestWeight));
    }

    /// <summary>The reciprocal of a vector's length, as the client normalises: <c>1.0 / fsqrt((x*x + y*y) + z*z)</c>.</summary>
    /// <param name="v">The vector.</param>
    /// <returns>The reciprocal length.</returns>
    public static Fpu InverseLength(Vector3f v) =>
        (Fpu)1.0 / Fpu.Sqrt((((Fpu)v.X * v.X) + ((Fpu)v.Y * v.Y)) + ((Fpu)v.Z * v.Z));

    /// <summary>A vector with each component times a factor, each stored.</summary>
    /// <param name="v">The vector.</param>
    /// <param name="s">The factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector3f Scale(Vector3f v, Fpu s) => new(((Fpu)v.X * s).F, ((Fpu)v.Y * s).F, ((Fpu)v.Z * s).F);

    /// <summary>Whether every component is zero, the client's <c>fcomp</c> against zero: a NaN is not zero.</summary>
    /// <param name="v">The vector.</param>
    /// <returns><see langword="true"/> when all three are zero.</returns>
    public static bool IsZero(Vector3f v) => v.X == 0f && v.Y == 0f && v.Z == 0f;

    /// <summary>The unit vector, or the vector itself when it is zero, as <c>Real32x3_Normalize</c> leaves it.</summary>
    /// <param name="v">The vector.</param>
    /// <returns>The normalised vector.</returns>
    public static Vector3f Normalised(Vector3f v) => IsZero(v) ? v : Scale(v, InverseLength(v));

    /// <summary>
    /// The orientation as a quaternion, the client's matrix-to-quaternion step: from the trace when it is positive, else
    /// from the largest diagonal element.
    /// </summary>
    private static (Fpu X, Fpu Y, Fpu Z, Fpu W) ToQuaternion(float[] m)
    {
        Fpu trace = ((Fpu)m[4] + m[0]) + m[8];
        if (trace.Value > 0.0)
        {
            float s = Fpu.Sqrt(trace + 1f).F;
            Fpu w = (Fpu)s * 0.5f;
            Fpu r = (Fpu)0.5f / s;
            return (
                new Fpu((-(((Fpu)m[5] - m[7]) * r)).F),
                new Fpu((-(((Fpu)m[6] - m[2]) * r)).F),
                new Fpu((-(((Fpu)m[1] - m[3]) * r)).F),
                w);
        }

        int i = m[4] > m[0] ? 1 : 0;
        if (m[8] > m[4 * i])
        {
            i = 2;
        }

        int j = Next[i], k = Next[j];
        Fpu root = Fpu.Sqrt((((Fpu)m[4 * i] - m[4 * j]) - m[4 * k]) + 1f);
        float[] q = new float[3];
        q[i] = ((Fpu)0.5f * root).F;
        if (root.Value != 0.0)
        {
            root = (Fpu)0.5f / root;
        }

        q[j] = (((Fpu)m[(3 * j) + i] + m[(3 * i) + j]) * root).F;
        q[k] = (((Fpu)m[(3 * k) + i] + m[(3 * i) + k]) * root).F;
        Fpu qw = -(((Fpu)m[(3 * j) + k] - m[(3 * k) + j]) * root);
        return (new Fpu(q[0]), new Fpu(q[1]), new Fpu(q[2]), qw);
    }

    /// <summary>A unit quaternion back into the orientation, each element from the doubled products.</summary>
    private static void ToMatrix(Fpu x, Fpu y, Fpu z, Fpu w, float[] m)
    {
        Fpu xx = Twice(x * x), xy = Twice(y * x), xz = Twice(z * x), xw = Twice(x * w);
        Fpu yy = Twice(y * y), yz = Twice(z * y), yw = Twice(y * w);
        Fpu zz = Twice(z * z), zw = Twice(z * w);
        m[0] = (((Fpu)1f - yy) - zz).F;
        m[1] = (xy - zw).F;
        m[2] = (yw + xz).F;
        m[3] = (zw + xy).F;
        Fpu oneLessXx = (Fpu)1f - xx;
        m[4] = (oneLessXx - zz).F;
        m[5] = (yz - xw).F;
        m[6] = (xz - yw).F;
        m[7] = (yz + xw).F;
        m[8] = (oneLessXx - yy).F;

        static Fpu Twice(Fpu v) => v + v;
    }

    /// <summary>Divides the quaternion by its length: <c>1.0 / fsqrt(((z*z + y*y) + x*x) + w*w)</c>.</summary>
    private static void Normalise(ref Fpu x, ref Fpu y, ref Fpu z, ref Fpu w)
    {
        Fpu s = (Fpu)1.0 / Fpu.Sqrt((((z * z) + (y * y)) + (x * x)) + (w * w));
        x = new Fpu((x * s).F);
        y = new Fpu((y * s).F);
        z = new Fpu((z * s).F);
        w *= s;
    }

    /// <summary>Sums the queued forces and their moments into the velocity and the angular momentum, and empties the queue.</summary>
    private void SumForces(float[] m)
    {
        Fpu fx = 0f, fy = 0f, fz = 0f, tx = 0f, ty = 0f, tz = 0f;
        foreach (Igi2PointForce force in _forces)
        {
            Vector3f f = force.Force, p = force.Point;
            fx += f.X;
            fy += f.Y;
            fz += f.Z;
            if (!IsZero(p) && !IsZero(f))
            {
                tx += ((Fpu)p.Y * f.Z) - ((Fpu)p.Z * f.Y);
                ty += -(((Fpu)f.Z * p.X) - ((Fpu)p.Z * f.X));
                tz += ((Fpu)f.Y * p.X) - ((Fpu)p.Y * f.X);
            }
        }

        _forces.Clear();
        TotalForce = new Vector3f(fx.F, fy.F, fz.F);
        Fpu ax = fx * InverseMass, ay = (Fpu)InverseMass * fy.F, az = (Fpu)InverseMass * fz.F;
        float rx = (((az * m[2]) + (ay * m[1])) + (ax * m[0])).F;
        float ry = (((az * m[5]) + (ax * m[3])) + (ay * m[4])).F;
        float rz = (((ay * m[7]) + (ax * m[6])) + (az * m[8])).F;
        Vector3f v = Velocity;
        Velocity = new Vector3f(((Fpu)rx + v.X).F, ((Fpu)ry + v.Y).F, ((Fpu)rz + v.Z).F);
        Vector3f l = AngularMomentum;
        AngularMomentum = new Vector3f((tx + l.X).F, (ty + l.Y).F, (tz + l.Z).F);
    }
}
