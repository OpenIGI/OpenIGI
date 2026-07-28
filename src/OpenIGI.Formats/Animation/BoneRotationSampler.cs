using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// Samples a bone's rotation track at a time, reproducing <c>0x4D5180</c>.
/// </summary>
/// <remarks>
/// <para>
/// Linear mode is a plain <b>slerp</b> between the two bracketing keys. Spline mode is <b>SQUAD</b> —
/// the key slerp and the tangent slerp, blended by <c>2s(1-s)</c> — with the keys' stored tangent
/// quaternions as the quadrangle control points.
/// </para>
/// <para>
/// The original reads each segment's angle and <c>1/sin(angle)</c> out of a table baked onto the track
/// record, and encodes "negate the target quaternion" by storing the angle plus a full turn. Nothing
/// read so far builds that table, so this computes the same quantities from the two quaternions; the
/// branch structure and the thresholds are the original's.
/// </para>
/// </remarks>
public static class BoneRotationSampler
{
    /// <summary>
    /// The angle below which the original abandons slerp for a lerp, guarding the reciprocal sine.
    /// </summary>
    public const float MinimumSlerpAngle = 0.0099999998f;

    /// <summary>The blend weight at or below which <c>0x4D4500</c> returns its second argument whole.</summary>
    public const float BlendLowerCutoff = 0.001f;

    /// <summary>The blend weight at or above which it returns its first argument whole.</summary>
    public const float BlendUpperCutoff = 0.99900001f;

    /// <summary>Samples a rotation track.</summary>
    /// <param name="keys">The track's keys, in time order.</param>
    /// <param name="time">The time to sample at.</param>
    /// <param name="duration">The clip's duration, used only when <paramref name="cyclic"/> wraps.</param>
    /// <param name="cyclic">
    /// The clip's <c>BOAH</c> flag bit 31. When set, a time past the last key interpolates back to
    /// key 0 over the time remaining before <paramref name="duration"/>; when clear the last key holds.
    /// </param>
    /// <param name="spline"><see langword="true"/> for SQUAD, <see langword="false"/> for a plain slerp.</param>
    /// <returns>The orientation, or the identity for an empty track.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    public static Quaternion32 Sample(
        IReadOnlyList<BoneRotationKey> keys,
        float time,
        float duration,
        bool cyclic = false,
        bool spline = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return new Quaternion32(0f, 0f, 0f, 1f);
        }

        if (keys.Count == 1)
        {
            return keys[0].Rotation;
        }

        int index = 0;
        while (index < keys.Count && keys[index].Time < time)
        {
            index++;
        }

        if (index < keys.Count && keys[index].Time == time)
        {
            return keys[index].Rotation;
        }

        int from;
        int to;
        float s;

        if (index == keys.Count)
        {
            float lastTime = keys[^1].Time;
            if (!cyclic || duration <= lastTime)
            {
                return keys[^1].Rotation;
            }

            // A cyclic clip bridges the gap between its last key and its first, over whatever time is
            // left before the duration. That is why the two need not be equal in the data.
            from = keys.Count - 1;
            to = 0;
            s = (time - lastTime) / (duration - lastTime);
        }
        else if (index == 0)
        {
            return keys[0].Rotation;
        }
        else
        {
            from = index - 1;
            to = index;
            float span = keys[to].Time - keys[from].Time;
            if (span <= 0f)
            {
                return keys[to].Rotation;
            }

            s = 1f - ((keys[to].Time - time) / span);
        }

        Quaternion32 rotation = Slerp(keys[from].Rotation, keys[to].Rotation, s);
        if (!spline)
        {
            return rotation;
        }

        Quaternion32 tangent = Slerp(keys[from].OutTangent, keys[to].InTangent, s);
        return Blend(rotation, tangent, 1f - (2f * s * (1f - s)));
    }

    /// <summary>Spherical linear interpolation, with the original's near-zero-angle fallback.</summary>
    /// <param name="from">The orientation at <c>s = 0</c>.</param>
    /// <param name="to">The orientation at <c>s = 1</c>.</param>
    /// <param name="s">The parameter.</param>
    /// <returns>The interpolated orientation.</returns>
    public static Quaternion32 Slerp(Quaternion32 from, Quaternion32 to, float s)
    {
        float dot = (from.X * to.X) + (from.Y * to.Y) + (from.Z * to.Z) + (from.W * to.W);

        // The baked table signals this case by storing the angle plus 2*PI; computed here, it is just
        // the sign of the dot product picking the shorter arc.
        if (dot < 0f)
        {
            to = new Quaternion32(-to.X, -to.Y, -to.Z, -to.W);
            dot = -dot;
        }

        float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
        if (angle == 0f)
        {
            return from;
        }

        float fromWeight;
        float toWeight;
        if (angle >= MinimumSlerpAngle)
        {
            float inverseSine = 1f / MathF.Sin(angle);
            fromWeight = MathF.Sin(angle - (angle * s)) * inverseSine;
            toWeight = MathF.Sin(angle * s) * inverseSine;
        }
        else
        {
            fromWeight = 1f - s;
            toWeight = s;
        }

        return new Quaternion32(
            (from.X * fromWeight) + (to.X * toWeight),
            (from.Y * fromWeight) + (to.Y * toWeight),
            (from.Z * fromWeight) + (to.Z * toWeight),
            (from.W * fromWeight) + (to.W * toWeight));
    }

    /// <summary>SQUAD's outer blend — <c>0x4D4500</c>.</summary>
    /// <param name="first">Returned whole when <paramref name="weight"/> is at or above the upper cutoff.</param>
    /// <param name="second">Returned whole when <paramref name="weight"/> is at or below the lower cutoff.</param>
    /// <param name="weight">The blend weight; the slerp runs at <c>1 - weight</c>.</param>
    /// <returns>The blended orientation.</returns>
    /// <remarks>
    /// The two cutoffs are the original's and are not cosmetic: they are what stops the endpoints of a
    /// SQUAD segment paying for a slerp whose parameter is numerically zero or one.
    /// </remarks>
    public static Quaternion32 Blend(Quaternion32 first, Quaternion32 second, float weight)
    {
        if (weight <= BlendLowerCutoff)
        {
            return second;
        }

        if (weight >= BlendUpperCutoff)
        {
            return first;
        }

        return Slerp(first, second, 1f - weight);
    }
}
