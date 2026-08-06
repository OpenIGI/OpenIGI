using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Physics;

/// <summary>How far one joint may swing and twist.</summary>
/// <param name="SwingAxis">
/// The direction the child bone's own axis points, in its parent's frame, at the middle of the joint's
/// range — the cone's centre line, which for a knee is not the rest direction.
/// </param>
/// <param name="SwingLimit">The cone's half-angle, in radians.</param>
/// <param name="TwistLow">The furthest the joint twists one way about its own axis, in radians.</param>
/// <param name="TwistHigh">And the other way.</param>
public readonly record struct RagdollJointRange(
    Vector3f SwingAxis,
    float SwingLimit,
    float TwistLow,
    float TwistHigh);

/// <summary>
/// The range each joint of a rig actually moves through, measured from the animations the game ships
/// for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes a corpse fold like a body rather than like a rag, and none of it is invented.</b>
/// A ragdoll needs to know that a knee bends one way and a shoulder rotates freely, and the usual way to
/// supply that is a hand-written table naming bones by index — which for these rigs would be a guess,
/// because their joint roles have never been read.
/// </para>
/// <para>
/// They do not need to be. The animators already answered the question: every clip the game ships is a
/// demonstration of how far each joint is meant to move, so sweeping the rotation tracks and taking the
/// extent of each bone's local rotation <em>measures</em> the limits instead of assuming them. A knee's
/// tracks only ever bend it one way, so the cone that comes out is narrow and centred on a half-bent
/// pose; a shoulder's are wide and centred near its rest.
/// </para>
/// <para>
/// The cone is deliberately widened past what was measured. A clip shows what a living body does, and a
/// dead one goes further than that — the margin is the difference between a limit and a demonstration.
/// </para>
/// </remarks>
public sealed class RagdollLimits
{
    /// <summary>How much wider than the measured range a joint is allowed to be.</summary>
    /// <remarks>
    /// Half as much again. The clips show a living body's working range; a corpse exceeds it, and a limit
    /// set exactly at what was demonstrated would make every fall look braced.
    /// </remarks>
    public const float Margin = 1.5f;

    /// <summary>The narrowest cone a measurement may produce, in radians — 8°.</summary>
    /// <remarks>
    /// A bone that never moves in any clip measures a cone of zero, which would weld it to its parent.
    /// Some are genuinely rigid and should be nearly welded; none should be exactly so, because a solver
    /// cannot satisfy an exact weld and a joint at its limit every substep is a joint that jitters.
    /// </remarks>
    public const float MinimumSwing = 8f * MathF.PI / 180f;

    /// <summary>The widest, in radians — 100°.</summary>
    public const float MaximumSwing = 100f * MathF.PI / 180f;

    /// <summary>The widest a twist may be, in radians — 90° either way.</summary>
    public const float MaximumTwist = 90f * MathF.PI / 180f;

    private readonly RagdollJointRange[] _ranges;
    private readonly bool[] _measured;

    private RagdollLimits(RagdollJointRange[] ranges, bool[] measured)
    {
        _ranges = ranges;
        _measured = measured;
    }

    /// <summary>Gets how many bones were measured.</summary>
    public int BoneCount => _ranges.Length;

    /// <summary>Gets one bone's measured range.</summary>
    /// <param name="bone">The bone index.</param>
    /// <param name="range">Receives the range.</param>
    /// <returns><see langword="false"/> when nothing in the corpus ever moved that bone.</returns>
    public bool TryGet(int bone, out RagdollJointRange range)
    {
        if (bone < 0 || bone >= _ranges.Length || !_measured[bone])
        {
            range = default;
            return false;
        }

        range = _ranges[bone];
        return true;
    }

    /// <summary>Measures every joint's range from the clips a rig ships with.</summary>
    /// <param name="set">The animation set, whose skeleton the measurement is against.</param>
    /// <returns>The limits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A bone's local rotation is split into the <b>swing</b> that takes its own axis somewhere else and
    /// the <b>twist</b> that spins it about that axis, because a joint limits the two quite differently:
    /// an elbow swings through most of a half-circle and twists barely at all, and a wrist is the other
    /// way round. The split is the standard one — project the rotation's vector part onto the axis to get
    /// the twist, and whatever is left over is the swing.
    /// </para>
    /// <para>
    /// The cone's axis is the <em>average</em> swung direction rather than the rest one. That is the part
    /// that matters for a knee: its clips never straighten it and never bend it forwards, so the average
    /// sits half-bent and the cone around it excludes straight and excludes forwards — which is a hinge,
    /// arrived at by measurement, with no bone named and no axis assumed.
    /// </para>
    /// </remarks>
    public static RagdollLimits Measure(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);

        int count = set.Skeleton.BoneCount;
        Vector3f[] axis = new Vector3f[count];
        Vector3f[] swungSum = new Vector3f[count];
        int[] samples = new int[count];
        float[] twistLow = new float[count];
        float[] twistHigh = new float[count];

        for (int bone = 0; bone < count; bone++)
        {
            // The bone's own axis is where its children sit, the same direction the rig gives its capsule.
            Vector3f reach = Vector3f.Zero;
            int kids = 0;
            for (int child = 0; child < count; child++)
            {
                if (set.Skeleton.Parents[child] == bone)
                {
                    reach += set.Skeleton.RestTranslations[child];
                    kids++;
                }
            }

            axis[bone] = kids > 0 && reach.LengthSquared > 0f ? reach.Normalized() : Vector3f.UnitZ;
        }

        float[] widest = new float[count];

        foreach (BoneAnimation clip in set.Animations)
        {
            int shared = Math.Min(clip.BoneCount, count);
            for (int bone = 0; bone < shared; bone++)
            {
                foreach (BoneRotationKey key in clip.Rotations(bone))
                {
                    (float twist, Vector3f swung) = Decompose(key.Rotation, axis[bone]);
                    swungSum[bone] += swung;
                    twistLow[bone] = Math.Min(twistLow[bone], twist);
                    twistHigh[bone] = Math.Max(twistHigh[bone], twist);
                    samples[bone]++;

                    // The TOTAL angle the clip turns this bone from its rest, which is the one quantity
                    // the solver actually bounds -- see RagdollBody.SolveDeviation. Measuring a swing
                    // and a twist separately and then taking the wider of the two was letting a joint
                    // that swings 60 and twists 60 deviate by 60 in a direction that is both, which is
                    // how a spine ended up bent double.
                    Quaternion32 q = key.Rotation.Normalized();
                    float half = MathF.Abs(q.W);
                    widest[bone] = MathF.Max(widest[bone], 2f * MathF.Acos(Math.Clamp(half, -1f, 1f)));
                }
            }
        }

        // A second pass, because the cone's half-angle is measured against the average direction and the
        // average is not known until every sample has been seen.
        Vector3f[] centre = new Vector3f[count];
        float[] swing = new float[count];
        bool[] measured = new bool[count];

        for (int bone = 0; bone < count; bone++)
        {
            measured[bone] = samples[bone] > 0 && swungSum[bone].LengthSquared > 0f;
            centre[bone] = measured[bone] ? swungSum[bone].Normalized() : axis[bone];
        }

        foreach (BoneAnimation clip in set.Animations)
        {
            int shared = Math.Min(clip.BoneCount, count);
            for (int bone = 0; bone < shared; bone++)
            {
                if (!measured[bone])
                {
                    continue;
                }

                foreach (BoneRotationKey key in clip.Rotations(bone))
                {
                    (_, Vector3f swung) = Decompose(key.Rotation, axis[bone]);
                    float angle = MathF.Acos(Math.Clamp(Vector3f.Dot(centre[bone], swung), -1f, 1f));
                    swing[bone] = MathF.Max(swing[bone], angle);
                }
            }
        }

        RagdollJointRange[] ranges = new RagdollJointRange[count];
        for (int bone = 0; bone < count; bone++)
        {
            ranges[bone] = new RagdollJointRange(
                centre[bone],
                Math.Clamp(widest[bone] * Margin, MinimumSwing, MaximumSwing),
                Math.Clamp(twistLow[bone] * Margin, -MaximumTwist, 0f),
                Math.Clamp(twistHigh[bone] * Margin, 0f, MaximumTwist));
        }

        return new RagdollLimits(ranges, measured);
    }

    /// <summary>Splits a local rotation into how far it twists about an axis and where it puts that axis.</summary>
    /// <param name="rotation">The bone's local rotation.</param>
    /// <param name="axis">Its own axis, in its own frame, unit length.</param>
    /// <returns>The twist angle in radians, and where the axis ends up.</returns>
    /// <remarks>
    /// The swing-twist decomposition: the part of the rotation's vector component along the axis is the
    /// twist, and what remains carries the axis somewhere else. Only the twist ANGLE and the swung
    /// DIRECTION are wanted here, and both come straight out without needing the swing quaternion itself.
    /// </remarks>
    public static (float Twist, Vector3f Swung) Decompose(Quaternion32 rotation, Vector3f axis)
    {
        Quaternion32 q = rotation.Normalized();
        float along = (q.X * axis.X) + (q.Y * axis.Y) + (q.Z * axis.Z);

        // atan2 rather than 2*asin: the angle has to keep its sign and stay meaningful past a quarter
        // turn, and the projection alone loses both.
        float twist = 2f * MathF.Atan2(along, q.W);
        if (twist > MathF.PI)
        {
            twist -= 2f * MathF.PI;
        }
        else if (twist < -MathF.PI)
        {
            twist += 2f * MathF.PI;
        }

        Matrix3f m = BonePose.ToMatrix(q);
        Vector3f swung = new(
            (m.M11 * axis.X) + (m.M12 * axis.Y) + (m.M13 * axis.Z),
            (m.M21 * axis.X) + (m.M22 * axis.Y) + (m.M23 * axis.Z),
            (m.M31 * axis.X) + (m.M32 * axis.Y) + (m.M33 * axis.Z));

        return (twist, swung.LengthSquared > 0f ? swung.Normalized() : axis);
    }
}
