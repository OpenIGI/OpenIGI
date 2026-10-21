using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// Samples an <see cref="Igi2AnimationClip"/> the way the sequel's animation controller does.
/// </summary>
/// <remarks>
/// <para>
/// <c>AnimController_Step</c> (<c>igi2 0x5549A0</c>) makes each key active once the controller's time
/// reaches it, and <c>AnimController_InterpolateHandler</c> (<c>0x5540F0</c>) then blends every active
/// key toward the key its <c>next</c> link names, at
/// <c>t = (time − key.time) / (next.time − key.time)</c>. A key with no <c>next</c> holds. So a track
/// sampled before its first key reads nothing; the controller never asks, because every shipped track
/// starts at time zero.
/// </para>
/// <para>
/// The result is local: a root translation and one rotation per bone, which is what
/// <see cref="BonePose.Evaluate"/> composes into a posed skeleton — the same composition
/// <c>BoneDynCubeObj_UpdateHierarchy</c> (<c>0x564B50</c>) performs, so both games share it.
/// </para>
/// </remarks>
public static class Igi2ClipSampler
{
    /// <summary>The dot product past which <c>Quat32_SLERP</c> stops being spherical.</summary>
    public const double LinearDotThreshold = 0.99998999;

    /// <summary>Samples the root translation.</summary>
    /// <param name="track">The clip's translation track.</param>
    /// <param name="time">The controller time, in the clip's units.</param>
    /// <returns>The root's position, or zero for an empty track.</returns>
    /// <remarks>Linear between keys: the translation key has no tangents.</remarks>
    public static Vector3f SampleTranslation(IReadOnlyList<Igi2TranslationKey> track, int time)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!TryFindSegment(track, time, static k => k.Time, out int index, out double t))
        {
            return default;
        }

        Vector3f from = track[index].Translation;
        if (index + 1 >= track.Count)
        {
            return from;
        }

        Vector3f to = track[index + 1].Translation;
        return new Vector3f(
            (float)(((to.X - from.X) * t) + from.X),
            (float)(((to.Y - from.Y) * t) + from.Y),
            (float)(((to.Z - from.Z) * t) + from.Z));
    }

    /// <summary>Samples one bone's rotation.</summary>
    /// <param name="track">The bone's rotation track.</param>
    /// <param name="time">The controller time, in the clip's units.</param>
    /// <param name="squad">
    /// The controller's spline flag (bit 3 of <c>nFlags</c>): SQUAD through the key tangents when set,
    /// a plain slerp when clear.
    /// </param>
    /// <returns>The bone's local orientation, or the identity for an empty track.</returns>
    public static Quaternion32 SampleRotation(IReadOnlyList<Igi2RotationKey> track, int time, bool squad)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!TryFindSegment(track, time, static k => k.Time, out int index, out double t))
        {
            return new Quaternion32(0f, 0f, 0f, 1f);
        }

        Igi2RotationKey from = track[index];
        if (index + 1 >= track.Count)
        {
            return from.Rotation;
        }

        Igi2RotationKey to = track[index + 1];
        Quaternion32 rotation = Slerp(from.Rotation, to.Rotation, t);
        if (!squad)
        {
            return rotation;
        }

        // The tangent pair runs from this key's outgoing tangent to the next key's incoming one, and the
        // two curves are blended by 2t(1 - t): zero at both keys, a half at the middle.
        Quaternion32 tangent = Slerp(from.OutgoingTangent, to.IncomingTangent, t);
        return Slerp(rotation, tangent, 2.0 * t * (1.0 - t));
    }

    /// <summary>Samples every bone of a clip.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="time">The controller time, in the clip's units.</param>
    /// <param name="squad">Whether rotations are SQUAD-interpolated.</param>
    /// <param name="rotations">Receives one local rotation per bone.</param>
    /// <returns>The root translation.</returns>
    /// <exception cref="ArgumentException"><paramref name="rotations"/> is not one per bone.</exception>
    public static Vector3f Sample(Igi2AnimationClip clip, int time, bool squad, Span<Quaternion32> rotations)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (rotations.Length != clip.BoneCount)
        {
            throw new ArgumentException(
                $"The clip has {clip.BoneCount} bones but {rotations.Length} rotations were given room.",
                nameof(rotations));
        }

        for (int bone = 0; bone < clip.BoneCount; bone++)
        {
            rotations[bone] = SampleRotation(clip.Rotations(bone), time, squad);
        }

        return SampleTranslation(clip.Translation, time);
    }

    /// <summary>The sequel's quaternion slerp, <c>Quat32_SLERP</c> (<c>igi2 0x5635D0</c>).</summary>
    /// <param name="from">The orientation at <c>t = 0</c>.</param>
    /// <param name="to">The orientation at <c>t = 1</c>.</param>
    /// <param name="t">The parameter.</param>
    /// <returns>The interpolated orientation.</returns>
    /// <remarks>
    /// <para>
    /// Not I.G.I. 1's slerp, and the differences are behaviour rather than style:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The angle is <c>√(1 − d)·(((−0.0187293·d + 0.0742610)·d − 0.2121144)·d + 1.5707288)</c> — the
    /// polynomial arc-cosine, not a library one.
    /// </description></item>
    /// <item><description>
    /// The cut-over to a plain lerp is on the <b>dot product</b>, above <see cref="LinearDotThreshold"/>,
    /// and the lerp is not renormalised.
    /// </description></item>
    /// <item><description>
    /// The shorter arc is taken by negating the <em>start</em> weight when the dot is negative; below
    /// <c>−0.99999</c> it lerps toward the negated end instead.
    /// </description></item>
    /// </list>
    /// <para>
    /// The original passes the end in <c>ecx</c> and the start in <c>edx</c>; this takes them in reading
    /// order. Arithmetic is done in double precision, as the x87 stack the original runs on does it.
    /// </para>
    /// </remarks>
    public static Quaternion32 Slerp(Quaternion32 from, Quaternion32 to, double t)
    {
        double dot = (from.X * (double)to.X) + (from.Z * (double)to.Z) + (from.Y * (double)to.Y) + (to.W * (double)from.W);
        float d = (float)dot;
        double fromWeight;
        double toWeight;

        // The first test is made on the full-precision dot, the rest on the value stored to a float.
        if (dot > LinearDotThreshold)
        {
            fromWeight = 1.0 - t;
            toWeight = t;
        }
        else if (d < -LinearDotThreshold)
        {
            fromWeight = 1.0 - t;
            toWeight = -t;
        }
        else
        {
            double magnitude = Math.Abs(d);
            double angle = Math.Sqrt(1.0 - magnitude)
                * ((((magnitude * -0.018729299) + 0.074261002) * magnitude - 0.21211439) * magnitude + 1.5707288);
            double inverseSine = 1.0 / Math.Sin(angle);
            fromWeight = Math.Sin((1.0 - t) * angle) * inverseSine * (d >= 0f ? 1.0 : -1.0);
            toWeight = Math.Sin(angle * t) * inverseSine;
        }

        return new Quaternion32(
            (float)((fromWeight * from.X) + (toWeight * to.X)),
            (float)((fromWeight * from.Y) + (toWeight * to.Y)),
            (float)((fromWeight * from.Z) + (toWeight * to.Z)),
            (float)((toWeight * to.W) + (fromWeight * from.W)));
    }

    private static bool TryFindSegment<TKey>(
        IReadOnlyList<TKey> track,
        int time,
        Func<TKey, int> timeOf,
        out int index,
        out double t)
    {
        // The active key is the last whose time the controller has reached; the stepper scans with <=.
        index = -1;
        for (int i = 0; i < track.Count && timeOf(track[i]) <= time; i++)
        {
            index = i;
        }

        t = 0.0;
        if (index < 0)
        {
            return false;
        }

        if (index + 1 < track.Count)
        {
            int start = timeOf(track[index]);
            t = (double)(time - start) / (timeOf(track[index + 1]) - start);
        }

        return true;
    }
}
