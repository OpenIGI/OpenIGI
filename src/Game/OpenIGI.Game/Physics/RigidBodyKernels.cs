using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Physics;

/// <summary>A force on a rigid body and where on it it acts, <c>PointForce_s</c>: both in the body's own frame.</summary>
/// <param name="Force">The force.</param>
/// <param name="Point">Where it acts, from the centre of mass; zero for a force through the centre.</param>
public readonly record struct PointForce(Vector3f Force, Vector3f Point);

/// <summary>
/// The rigid-body steps both games compiled to the same arithmetic, written once over the precision each ran at.
/// </summary>
/// <remarks>
/// <para>
/// Project I.G.I.'s rigid body (<c>0x4EC6A0</c> and the helpers under <c>0x4FC0E0</c>) and I.G.I. 2's
/// (<c>Physics_Update</c>, <c>igi2 0x5A9580</c>, and its helpers) are one engine module. These steps match term for
/// term in both builds' disassembly; each is transcribed with the first game's store points, which change nothing at
/// the sequel's 24-bit precision (<see cref="IX87{TSelf}"/>).
/// </para>
/// <para>
/// What the two compilers ordered differently stays with each game: the length estimate (<c>0x4B2A10</c> against
/// <c>Real32x3_FastLen</c>, <c>igi2 0x561820</c>), the angular velocity and the quaternion step of the integrator, and
/// the contact response.
/// </para>
/// </remarks>
public static class RigidBodyKernels
{
    private static readonly int[] NextAxis = [1, 2, 0];

    /// <summary>
    /// The orientation as a unit quaternion: from the trace when it is positive, else from the largest diagonal element
    /// (the integrator's first step, <c>0x4EC6A0</c> / <c>Physics_Update</c>).
    /// </summary>
    /// <param name="m">The orientation, nine floats row by row.</param>
    /// <returns>The quaternion, each part as stored.</returns>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static (float X, float Y, float Z, float W) ToQuaternion<TFpu>(ReadOnlySpan<float> m)
        where TFpu : struct, IX87<TFpu>
    {
        TFpu trace = (TFpu)m[4] + m[0] + m[8];
        if (trace.Value > 0.0f)
        {
            TFpu s = TFpu.Sqrt(trace + 1.0f);
            float w = (s * 0.5f).F;
            TFpu k = (TFpu)0.5f / s;
            return (
                (-(((TFpu)m[5] - m[7]) * k)).F,
                (-(((TFpu)m[6] - m[2]) * k)).F,
                (-(((TFpu)m[1] - m[3]) * k)).F,
                w);
        }

        int i = m[4] > m[0] ? 1 : 0;
        if (m[8] > m[4 * i])
        {
            i = 2;
        }

        int j = NextAxis[i];
        int n = NextAxis[j];
        Span<float> q = stackalloc float[3];
        TFpu root = TFpu.Sqrt((TFpu)m[4 * i] - m[4 * j] - m[4 * n] + 1.0f);
        q[i] = (root * 0.5f).F;
        if (root.Value != 0.0)
        {
            root = (TFpu)0.5f / root;
        }

        q[j] = (((TFpu)m[(3 * j) + i] + m[(3 * i) + j]) * root).F;
        q[n] = (((TFpu)m[(3 * n) + i] + m[(3 * i) + n]) * root).F;
        float qw = (-(((TFpu)m[(3 * j) + n] - m[(3 * n) + j]) * root)).F;
        return (q[0], q[1], q[2], qw);
    }

    /// <summary>
    /// Divides the quaternion by its length, <c>1.0 / fsqrt(((z*z + y*y) + x*x) + w*w)</c>: x, y and z stored, w left in
    /// the register, as the integrator inlines it.
    /// </summary>
    /// <param name="x">The x part.</param>
    /// <param name="y">The y part.</param>
    /// <param name="z">The z part.</param>
    /// <param name="w">The w part, unstored.</param>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static void NormaliseQuaternion<TFpu>(ref float x, ref float y, ref float z, ref TFpu w)
        where TFpu : struct, IX87<TFpu>
    {
        TFpu length = TFpu.Sqrt((TFpu)z * z + (TFpu)y * y + (TFpu)x * x + w * w);
        TFpu inverse = (TFpu)1.0 / length;
        x = ((TFpu)x * inverse).F;
        y = ((TFpu)y * inverse).F;
        z = ((TFpu)z * inverse).F;
        w *= inverse;
    }

    /// <summary>A unit quaternion back into the orientation, each element from the doubled products.</summary>
    /// <param name="x">The x part.</param>
    /// <param name="y">The y part.</param>
    /// <param name="z">The z part.</param>
    /// <param name="w">The w part, unstored.</param>
    /// <param name="m">Receives the orientation, nine floats row by row.</param>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static void ToMatrix<TFpu>(float x, float y, float z, TFpu w, Span<float> m)
        where TFpu : struct, IX87<TFpu>
    {
        float xx = Twice((TFpu)x * x).F;
        float xy = Twice((TFpu)y * x).F;
        float xz = Twice((TFpu)z * x).F;
        float xw = Twice((TFpu)x * w).F;
        float yy = Twice((TFpu)y * y).F;
        float yz = Twice((TFpu)z * y).F;
        float yw = Twice((TFpu)y * w).F;
        float zz = Twice((TFpu)z * z).F;
        TFpu zw = Twice(w * z);

        m[0] = ((TFpu)1.0f - yy - zz).F;
        m[1] = ((TFpu)xy - zw).F;
        m[2] = ((TFpu)yw + xz).F;
        m[3] = (zw + xy).F;
        TFpu oneLessXx = (TFpu)1.0f - xx;
        m[4] = (oneLessXx - zz).F;
        m[5] = ((TFpu)yz - xw).F;
        m[6] = ((TFpu)xz - yw).F;
        m[7] = ((TFpu)yz + xw).F;
        m[8] = (oneLessXx - yy).F;
    }

    /// <summary>
    /// A point's velocity in the world: the spin about the centre plus the centre's (<c>0x4ECF80</c> /
    /// <c>Physics_GetPointVelocity</c>, <c>igi2 0x5AA730</c>).
    /// </summary>
    /// <param name="velocity">The centre's velocity.</param>
    /// <param name="spin">The angular velocity, in the world.</param>
    /// <param name="arm">The point, relative to the centre, in the world.</param>
    /// <returns>Its velocity.</returns>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static Vector3f PointVelocity<TFpu>(Vector3f velocity, Vector3f spin, Vector3f arm)
        where TFpu : struct, IX87<TFpu>
    {
        float x = ((TFpu)arm.Z * spin.Y - (TFpu)spin.Z * arm.Y).F;
        float y = (-((TFpu)arm.Z * spin.X - (TFpu)spin.Z * arm.X)).F;
        float z = ((TFpu)spin.X * arm.Y - (TFpu)arm.X * spin.Y).F;
        return new Vector3f(((TFpu)velocity.X + x).F, ((TFpu)velocity.Y + y).F, ((TFpu)velocity.Z + z).F);
    }

    /// <summary>
    /// Sums the queued forces and their moments, the integrator's second step: the sum over the mass, turned into the
    /// world, onto the velocity; the moments onto the angular momentum.
    /// </summary>
    /// <param name="forces">The queued forces, in the body's frame.</param>
    /// <param name="m">The orientation, nine floats row by row.</param>
    /// <param name="inverseMass">The inverse of the mass.</param>
    /// <param name="velocity">The velocity, in the world.</param>
    /// <param name="momentum">The angular momentum, in the body's frame.</param>
    /// <returns>The forces' sum, in the body's frame.</returns>
    /// <remarks>A force acting at the centre, or a force of nothing, adds no moment.</remarks>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static Vector3f SumForces<TFpu>(
        ReadOnlySpan<PointForce> forces,
        ReadOnlySpan<float> m,
        float inverseMass,
        ref Vector3f velocity,
        ref Vector3f momentum)
        where TFpu : struct, IX87<TFpu>
    {
        TFpu sumX = 0.0, sumY = 0.0;
        float sumZ = 0.0f;
        TFpu torqueX = 0.0, torqueY = 0.0, torqueZ = 0.0;
        foreach (PointForce each in forces)
        {
            Vector3f f = each.Force, p = each.Point;
            sumX += f.X;
            sumY += f.Y;
            sumZ = ((TFpu)sumZ + f.Z).F;
            if (p.X == 0.0f && p.Y == 0.0f && p.Z == 0.0f)
            {
                continue;
            }

            if (f.X == 0.0f && f.Y == 0.0f && f.Z == 0.0f)
            {
                continue;
            }

            TFpu t1 = (TFpu)f.Z * p.Y - (TFpu)p.Z * f.Y;
            float t2 = (-((TFpu)f.Z * p.X - (TFpu)f.X * p.Z)).F;
            float t3 = ((TFpu)f.Y * p.X - (TFpu)f.X * p.Y).F;
            torqueX += t1;
            torqueY += t2;
            torqueZ += t3;
        }

        float fy = sumY.F;
        float fx = sumX.F;
        float tz = torqueZ.F;
        float ty = torqueY.F;
        float ax = ((TFpu)inverseMass * fx).F;
        float ay = ((TFpu)inverseMass * fy).F;
        TFpu az = (TFpu)inverseMass * sumZ;
        float dvx = (az * m[2] + (TFpu)ay * m[1] + (TFpu)ax * m[0]).F;
        float dvy = (az * m[5] + (TFpu)ax * m[3] + (TFpu)ay * m[4]).F;
        TFpu dvz = (TFpu)ay * m[7] + (TFpu)ax * m[6] + az * m[8];
        velocity = new Vector3f(((TFpu)dvx + velocity.X).F, ((TFpu)dvy + velocity.Y).F, (dvz + velocity.Z).F);
        momentum = new Vector3f((torqueX + momentum.X).F, ((TFpu)ty + momentum.Y).F, ((TFpu)tz + momentum.Z).F);
        return new Vector3f(fx, fy, sumZ);
    }

    /// <summary>
    /// The unit vector, or the vector itself when it is zero: <c>x * (1.0 / fsqrt((x*x + y*y) + z*z))</c>, each part
    /// stored.
    /// </summary>
    /// <param name="v">The vector.</param>
    /// <returns>The normalised vector.</returns>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static Vector3f Normalise<TFpu>(Vector3f v)
        where TFpu : struct, IX87<TFpu>
    {
        if (v.X == 0.0f && v.Y == 0.0f && v.Z == 0.0f)
        {
            return v;
        }

        TFpu inverse = (TFpu)1.0 / TFpu.Sqrt((TFpu)v.X * v.X + (TFpu)v.Y * v.Y + (TFpu)v.Z * v.Z);
        return new Vector3f(((TFpu)v.X * inverse).F, ((TFpu)v.Y * inverse).F, ((TFpu)v.Z * inverse).F);
    }

    /// <summary>
    /// Whether a body may come to rest (<c>0x4FC7B0</c> / <c>PhysicsObj_IsIdle</c>): touching something, not woken, with
    /// nothing pushing it, slower than its type's rest speed and spinning less than its rest spin.
    /// </summary>
    /// <param name="contacts">How many of its points touched last tick.</param>
    /// <param name="woken">Whether something woke it this tick.</param>
    /// <param name="queuedForces">How many forces wait for the next step.</param>
    /// <param name="speed">Its speed estimate.</param>
    /// <param name="restSpeed">The type's rest speed.</param>
    /// <param name="spin">Its angular velocity.</param>
    /// <param name="restSpinSquared">The type's rest spin, squared.</param>
    /// <returns><see langword="true"/> when it may rest.</returns>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    public static bool MayRest<TFpu>(int contacts, bool woken, int queuedForces, float speed, float restSpeed, Vector3f spin, float restSpinSquared)
        where TFpu : struct, IX87<TFpu>
    {
        if (contacts <= 0 || woken || queuedForces != 0 || !(speed < restSpeed))
        {
            return false;
        }

        TFpu s = (TFpu)spin.X * spin.X + (TFpu)spin.Y * spin.Y + (TFpu)spin.Z * spin.Z;
        return s.Value < restSpinSquared;
    }

    private static TFpu Twice<TFpu>(TFpu value)
        where TFpu : struct, IX87<TFpu>
        => value + value;
}
