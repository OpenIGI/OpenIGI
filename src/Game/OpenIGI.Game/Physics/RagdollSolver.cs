using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>
/// The pure mathematics a ragdoll is solved with: rigid-body integration, the swing-twist split, and
/// the one-dimensional angle limit every joint constraint is built out of.
/// </summary>
/// <remarks>
/// <para>
/// Extended position-based dynamics, after Müller, Macklin, Chentanez, Jeschke and Kim (2020),
/// <i>Detailed Rigid Body Simulation with Extended Position Based Dynamics</i>. The shape of it: take
/// many small substeps and solve each constraint <b>once</b> per substep, rather than few large steps
/// with many iterations. Constraint stiffness then follows from the substep size instead of from an
/// iteration count, which is why a ragdoll built this way holds together without tuning.
/// </para>
/// <para>
/// Everything here is a free function over values. What holds the arrays and does the stepping is
/// <c>RagdollBody</c>; this is the part worth testing on its own.
/// </para>
/// </remarks>
public static class RagdollSolver
{
    /// <summary>How near two directions must be before the turn between them counts as nothing.</summary>
    public const float ParallelEpsilon = 1e-6f;

    /// <summary>Advances an orientation by an angular velocity.</summary>
    /// <param name="orientation">The current orientation.</param>
    /// <param name="angularVelocity">The angular velocity, in radians per unit of <paramref name="step"/>.</param>
    /// <param name="step">How long to advance for.</param>
    /// <returns>The new orientation, normalized.</returns>
    /// <remarks>
    /// <c>q += ½ h (ω ⊗ q)</c>, then normalize — the standard first-order quaternion update, with the
    /// angular velocity as a pure quaternion on the <b>left</b>, because ω is in world space and q takes
    /// body space to world.
    /// </remarks>
    public static Quaternion32 Advance(Quaternion32 orientation, Vector3d angularVelocity, double step)
    {
        Quaternion32 spin = new(
            (float)(angularVelocity.X * step * 0.5),
            (float)(angularVelocity.Y * step * 0.5),
            (float)(angularVelocity.Z * step * 0.5),
            0f);

        Quaternion32 delta = spin * orientation;
        return new Quaternion32(
            orientation.X + delta.X,
            orientation.Y + delta.Y,
            orientation.Z + delta.Z,
            orientation.W + delta.W).Normalized();
    }

    /// <summary>Recovers the angular velocity a substep's orientation change implies.</summary>
    /// <param name="previous">Where the body was pointing.</param>
    /// <param name="current">Where it is pointing now.</param>
    /// <param name="step">How long the substep was.</param>
    /// <returns>The angular velocity, in radians per unit of <paramref name="step"/>.</returns>
    /// <remarks>
    /// <c>ω = 2 (q q_prev⁻¹).xyz / h</c>, with the sign taken from the scalar part. <b>The sign matters
    /// and is easy to miss</b>: a quaternion and its negation are the same orientation but opposite
    /// halves of a turn, so reading the vector part without checking <c>w</c> makes a body that rotated
    /// a little past a half turn appear to have spun almost the whole way back.
    /// </remarks>
    public static Vector3d Recover(Quaternion32 previous, Quaternion32 current, double step)
    {
        Quaternion32 delta = current * previous.Conjugate();
        double scale = 2.0 / step;
        Vector3d axis = new(delta.X * scale, delta.Y * scale, delta.Z * scale);
        return delta.W >= 0f ? axis : -axis;
    }

    /// <summary>Splits a rotation into a twist about an axis and the swing that carries the axis.</summary>
    /// <param name="rotation">The rotation to split.</param>
    /// <param name="axis">The twist axis, in the same frame as the rotation, unit length.</param>
    /// <returns>The swing and the twist, whose product in that order is the rotation.</returns>
    /// <remarks>
    /// <c>q = swing · twist</c>, with the twist's vector part parallel to the axis and the swing's
    /// perpendicular to it. Splitting the two is what lets a joint limit them differently, which every
    /// real joint does: an elbow swings through most of a half-circle and barely twists at all.
    /// </remarks>
    public static (Quaternion32 Swing, Quaternion32 Twist) SwingTwist(Quaternion32 rotation, Vector3f axis)
    {
        float along = (rotation.X * axis.X) + (rotation.Y * axis.Y) + (rotation.Z * axis.Z);
        Quaternion32 twist = new(axis.X * along, axis.Y * along, axis.Z * along, rotation.W);

        float length = twist.Length;
        if (length < ParallelEpsilon)
        {
            // A half turn about something square to the axis: the twist is genuinely undefined, and
            // calling it nothing puts the whole rotation into the swing, which is where it belongs.
            return (rotation, Quaternion32.Identity);
        }

        twist = twist.Normalized();
        return (rotation * twist.Conjugate(), twist);
    }

    /// <summary>The rotation vector that brings a violated angle limit back inside its range.</summary>
    /// <param name="axis">The axis the angle is measured about, unit length.</param>
    /// <param name="from">Where the angle is measured from, perpendicular to the axis, unit length.</param>
    /// <param name="to">Where it is measured to, perpendicular to the axis, unit length.</param>
    /// <param name="low">The lowest the angle may be, in radians.</param>
    /// <param name="high">The highest, in radians.</param>
    /// <param name="correction">Receives the correction as an axis scaled by its angle.</param>
    /// <returns><see langword="false"/> when the angle is already inside its range.</returns>
    /// <remarks>
    /// <para>
    /// The <c>limitAngle</c> routine of the XPBD rigid-body paper. The angle comes from the signed sine
    /// rather than from a dot product, because a sine keeps its sign and a cosine does not — and the
    /// <c>from · to &lt; 0</c> test is what extends it past a quarter turn, which every real joint range
    /// needs.
    /// </para>
    /// <para>
    /// The correction it returns turns <paramref name="to"/> to exactly the limit it crossed, never
    /// further: a limit is a wall, not a spring, and a joint pushed back past its limit oscillates.
    /// </para>
    /// </remarks>
    public static bool LimitAngle(
        Vector3f axis,
        Vector3f from,
        Vector3f to,
        float low,
        float high,
        out Vector3f correction)
    {
        correction = Vector3f.Zero;

        float angle = MathF.Asin(Math.Clamp(Vector3f.Dot(Vector3f.Cross(from, to), axis), -1f, 1f));
        if (Vector3f.Dot(from, to) < 0f)
        {
            angle = MathF.PI - angle;
        }

        if (angle > MathF.PI)
        {
            angle -= 2f * MathF.PI;
        }
        else if (angle < -MathF.PI)
        {
            angle += 2f * MathF.PI;
        }

        if (angle >= low && angle <= high)
        {
            return false;
        }

        float clamped = Math.Clamp(angle, low, high);

        // Turn `from` to where the limit says `to` should be, then the correction is the turn that takes
        // the real `to` onto it. Building it this way rather than scaling the axis by the overshoot is
        // what keeps it exact when the joint is far outside its range.
        Quaternion32 turn = Quaternion32.FromAxisAngle(axis, clamped);
        Vector3f target = Rotate(BonePose.ToMatrix(turn), from);
        correction = Vector3f.Cross(to, target);
        return correction.LengthSquared > ParallelEpsilon * ParallelEpsilon;
    }

    /// <summary>The inverse inertia tensor of a body in world space.</summary>
    /// <param name="orientation">The body's orientation.</param>
    /// <param name="localInverseInertia">Its inverse inertia in its own frame.</param>
    /// <returns>The world-space inverse inertia.</returns>
    /// <remarks><c>R I⁻¹ Rᵀ</c>, and it has to be rebuilt whenever the body turns.</remarks>
    public static Matrix3f WorldInverseInertia(Quaternion32 orientation, Matrix3f localInverseInertia)
    {
        Matrix3f rotation = BonePose.ToMatrix(orientation);
        return rotation * localInverseInertia * rotation.Transposed();
    }

    /// <summary>Turns a vector by a rotation matrix, in the convention the bone palette uses.</summary>
    /// <param name="m">The rotation.</param>
    /// <param name="v">The vector.</param>
    /// <returns>The turned vector.</returns>
    /// <remarks>
    /// <c>M · v</c> — the rows dotted with the vector. <see cref="Matrix3f.Transform(Vector3f)"/> is the
    /// engine's other convention and is the inverse rotation; the mix-up is the transpose trap
    /// <see cref="BonePose"/> carries its own warning about.
    /// </remarks>
    public static Vector3f Rotate(in Matrix3f m, in Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    /// <summary>Turns a double-precision vector by a rotation matrix.</summary>
    /// <param name="m">The rotation.</param>
    /// <param name="v">The vector.</param>
    /// <returns>The turned vector.</returns>
    public static Vector3d Rotate(in Matrix3f m, in Vector3d v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));
}
