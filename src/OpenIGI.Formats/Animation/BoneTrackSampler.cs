using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// Samples an animation's root-motion track at a time, reproducing <c>0x4D4840</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original picks between a cubic Hermite spline and a straight lerp on a global toggle
/// (<c>dword_A54678</c>), so both arms are here and <see cref="Sample"/> takes the choice as an
/// argument rather than deciding for the caller.
/// </para>
/// </remarks>
public static class BoneTrackSampler
{
    /// <summary>Samples a translation track.</summary>
    /// <param name="keys">The track's keys, in time order.</param>
    /// <param name="time">The time to sample at.</param>
    /// <param name="spline">
    /// <see langword="true"/> for the Hermite arm, <see langword="false"/> for the linear one.
    /// </param>
    /// <returns>The position, or the zero vector for an empty track.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    public static Vector3f Sample(IReadOnlyList<BoneTranslationKey> keys, float time, bool spline = true)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return default;
        }

        // The original scans linearly for the first key at or after the time, and leaves the loop
        // variable at the count when every key is earlier. Both of those edges matter below.
        int index = 0;
        while (index < keys.Count && keys[index].Time < time)
        {
            index++;
        }

        if (index == keys.Count)
        {
            // Past the last key: hold it. This is also what a one-key track always does.
            return keys[^1].Position;
        }

        if (index == 0 || keys[index].Time == time)
        {
            // An exact hit takes the key untouched, with no interpolation at all.
            return keys[index].Position;
        }

        BoneTranslationKey previous = keys[index - 1];
        BoneTranslationKey next = keys[index];
        float duration = next.Time - previous.Time;
        if (duration <= 0f)
        {
            return next.Position;
        }

        float s = 1f - ((next.Time - time) / duration);

        if (!spline)
        {
            return Lerp(previous.Position, next.Position, s);
        }

        // The standard cubic Hermite basis. The tangents are stored per unit time, so each is scaled
        // by the segment's duration before use — which is why a long segment does not overshoot.
        float s2 = s * s;
        float s3 = s2 * s;
        float h00 = (2f * s3) - (3f * s2) + 1f;
        float h01 = (3f * s2) - (2f * s3);
        float h10 = s - (2f * s2) + s3;
        float h11 = s2 - s3;

        return Add(
            Add(Scale(previous.Position, h00), Scale(next.Position, h01)),
            Add(Scale(previous.OutTangent, duration * h10), Scale(next.InTangent, duration * h11)));
    }

    private static Vector3f Lerp(Vector3f from, Vector3f to, float s) => new(
        from.X + ((to.X - from.X) * s),
        from.Y + ((to.Y - from.Y) * s),
        from.Z + ((to.Z - from.Z) * s));

    private static Vector3f Scale(Vector3f value, float factor) =>
        new(value.X * factor, value.Y * factor, value.Z * factor);

    private static Vector3f Add(Vector3f left, Vector3f right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
}
