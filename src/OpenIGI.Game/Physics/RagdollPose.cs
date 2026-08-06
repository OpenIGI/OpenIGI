using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>
/// Reading orientations back out of the matrices the bone palette stores.
/// </summary>
/// <remarks>
/// All that is left of what used to be a whole pose-reconstruction stage. When every bone was a single
/// particle the solver knew where a bone <em>was</em> but not which way it <em>faced</em>, and the
/// facing had to be recovered by fitting a rotation to where the bone's children had ended up — a
/// least-squares problem, solved per bone per frame, that could only ever approximate the simulation it
/// was reading. A rigid body carries its own orientation, so the drawn pose is now a direct read and the
/// fitting is gone. This is what remains: the conversion between the two ways the port stores a
/// rotation.
/// </remarks>
public static class RagdollPose
{
    /// <summary>Reads a rotation matrix back out as the quaternion the bone palette stores.</summary>
    /// <param name="m">The rotation, in the <c>M · v</c> convention.</param>
    /// <returns>The quaternion, such that <see cref="BonePose.ToMatrix"/> returns the same rotation.</returns>
    /// <remarks>
    /// The inverse of <see cref="BonePose.ToMatrix"/>, and it has to be exactly that and not the inverse
    /// of <see cref="Quaternion32.ToMatrix"/> — the two differ by a transpose, which is the trap
    /// <c>BonePose</c> carries its own warning about. The largest-diagonal branch is the standard
    /// numerically stable form; taking the trace branch alone loses all precision at a half turn.
    /// </remarks>
    public static Quaternion32 QuaternionOf(Matrix3f m)
    {
        float trace = m.M11 + m.M22 + m.M33;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            return new Quaternion32(
                (m.M32 - m.M23) / s,
                (m.M13 - m.M31) / s,
                (m.M21 - m.M12) / s,
                0.25f * s);
        }

        if (m.M11 > m.M22 && m.M11 > m.M33)
        {
            float s = MathF.Sqrt(1f + m.M11 - m.M22 - m.M33) * 2f;
            return new Quaternion32(
                0.25f * s,
                (m.M12 + m.M21) / s,
                (m.M13 + m.M31) / s,
                (m.M32 - m.M23) / s);
        }

        if (m.M22 > m.M33)
        {
            float s = MathF.Sqrt(1f + m.M22 - m.M11 - m.M33) * 2f;
            return new Quaternion32(
                (m.M12 + m.M21) / s,
                0.25f * s,
                (m.M23 + m.M32) / s,
                (m.M13 - m.M31) / s);
        }

        float t = MathF.Sqrt(1f + m.M33 - m.M11 - m.M22) * 2f;
        return new Quaternion32(
            (m.M13 + m.M31) / t,
            (m.M23 + m.M32) / t,
            0.25f * t,
            (m.M21 - m.M12) / t);
    }
}
