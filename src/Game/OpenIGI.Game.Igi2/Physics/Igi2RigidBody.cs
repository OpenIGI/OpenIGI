using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Physics;

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
/// <para>
/// The steps Project I.G.I.'s build compiled alike are the engine's shared ones (<see cref="RigidBodyKernels"/>); the
/// angular velocity, the quaternion step and the length estimate are this build's own.
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

    private readonly List<PointForce> _forces = [];

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
    public IReadOnlyList<PointForce> Forces => _forces;

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
    public void AddForce(in PointForce force) => _forces.Add(force);

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

        VelocityDirection = RigidBodyKernels.Normalise<Fpu>(v);
    }

    /// <summary>A point's velocity in the world, <c>Physics_GetPointVelocity</c> (<c>igi2 0x5AA730</c>): the spin about the centre plus the centre's.</summary>
    /// <param name="arm">The point, relative to the centre of mass, in the world.</param>
    /// <returns>Its velocity.</returns>
    public Vector3f PointVelocity(Vector3f arm) => RigidBodyKernels.PointVelocity<Fpu>(Velocity, AngularVelocity, arm);

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
        (float qx, float qy, float qz, float stored) = RigidBodyKernels.ToQuaternion<Fpu>(m);
        Fpu qw = stored;

        int count = _forces.Count;
        PreviousForceCount = count;
        if (count > 0)
        {
            Vector3f velocity = Velocity, momentum = AngularMomentum;
            TotalForce = RigidBodyKernels.SumForces<Fpu>(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_forces), m, InverseMass, ref velocity, ref momentum);
            _forces.Clear();
            Velocity = velocity;
            AngularMomentum = momentum;
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
        Fpu a = (hz - hy) * ((Fpu)qy - qz);
        Fpu yy = ((Fpu)qz + qy) * -hx;
        Fpu xw = (hz + hy) * (qw - qx);
        Fpu zz = (hz + hx) * ((Fpu)qy + qx);
        Fpu xz = (qw - qz) * hy;
        Fpu yz = ((Fpu)qz + qw) * -hy;
        Fpu yw = (yz + xz) + zz;
        Fpu t = (((hz - hx) * ((Fpu)qx - qy)) + yw) * 0.5f;
        Fpu dx = ((((Fpu)qx + qw) * hx) + t) - yw;
        Fpu dy = (yy + t) - yz;
        Fpu dz = (xw + t) - xz;
        qx = (dx + qx).F;
        qy = (dy + qy).F;
        qz = (dz + qz).F;
        qw = ((qw + t) + a) - zz;

        RigidBodyKernels.NormaliseQuaternion(ref qx, ref qy, ref qz, ref qw);
        RigidBodyKernels.NormaliseQuaternion(ref qx, ref qy, ref qz, ref qw);
        RigidBodyKernels.ToMatrix(qx, qy, qz, qw, m);
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

        return ((Fpu)largest + ((Fpu)smallest * SmallestWeight)) + ((Fpu)middle * MiddleWeight);
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
}
