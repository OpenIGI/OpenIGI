using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// One rotation key: an orientation, its time, and the two tangents that interpolate it.
/// </summary>
/// <param name="Rotation">The orientation. Measured to be a unit quaternion in all retail data.</param>
/// <param name="Time">The key's time, in the set's abstract units.</param>
/// <param name="OutTangent">
/// The tangent of the segment <em>starting</em> at this key; equal to <paramref name="Rotation"/> in a
/// one-key track.
/// </param>
/// <param name="InTangent">The tangent of the segment <em>ending</em> at this key; likewise.</param>
/// <remarks>
/// The on-disk record is <see cref="SizeInBytes"/> bytes, but a track with a single key is only read
/// for its first <see cref="ShortSizeInBytes"/> — the value and the time — because there is nothing to
/// interpolate.1 and §4.1.
/// </remarks>
public readonly record struct BoneRotationKey(
    Quaternion32 Rotation,
    float Time,
    Quaternion32 OutTangent,
    Quaternion32 InTangent)
{
    /// <summary>The full record size, and the stride of a multi-key track.</summary>
    public const int SizeInBytes = 52;

    /// <summary>How much of the record the loader reads when the track has one key.</summary>
    public const int ShortSizeInBytes = 20;
}

/// <summary>
/// One translation key: a position, its time, and the two tangents that interpolate it.
/// </summary>
/// <param name="Position">The position, in engine units.</param>
/// <param name="Time">The key's time, in the set's abstract units.</param>
/// <param name="OutTangent">
/// The tangent of the segment <em>starting</em> at this key — the <c>m0</c> of the Hermite basis. Zero
/// in a one-key track.
/// </param>
/// <param name="InTangent">
/// The tangent of the segment <em>ending</em> at this key — the <c>m1</c>. Likewise zero.
/// </param>
/// <remarks>
/// <para>
/// The on-disk record is <see cref="SizeInBytes"/> bytes; a one-key track is read only for its first
/// <see cref="ShortSizeInBytes"/>.
/// </para>
/// <para>
/// Which stored tangent is which comes from the sampler (<c>0x4D4840</c>), which takes <c>m0</c> from
/// the previous key's <c>+16</c> and <c>m1</c> from this key's <c>+28</c>, both scaled by the segment's
/// duration.2 and §5.4.
/// </para>
/// </remarks>
public readonly record struct BoneTranslationKey(
    Vector3f Position,
    float Time,
    Vector3f OutTangent,
    Vector3f InTangent)
{
    /// <summary>The full record size, and the stride of a multi-key track.</summary>
    public const int SizeInBytes = 40;

    /// <summary>How much of the record the loader reads when the track has one key.</summary>
    public const int ShortSizeInBytes = 16;
}

/// <summary>
/// One animation event: an id, a time and a place.
/// </summary>
/// <param name="EventId">The trigger id, resolved through <c>LOCAL:animtrigger</c>.</param>
/// <param name="BoneIndex">The bone the event hangs off, or <see cref="NoBone"/>.</param>
/// <param name="Time">When the event fires, in the set's abstract units.</param>
/// <param name="Position">Where it fires, in engine units.</param>
/// <remarks>
/// <para>
/// <b>The bone reading is inferred, not read out of the binary.</b> Across the 490 retail events the
/// field takes only <c>-1</c> and values that are valid bone indices for their own skeleton, and the
/// commonest event (<c>1010</c>, 234 occurrences) splits between bones 22 and 27 — a left/right pair,
/// which is what a footstep looks like. A sentinel of <c>-1</c> also rules out a count or a bitfield.
/// It is the best-supported reading, but no consumer of the field has been decompiled yet.
/// </para>
/// </remarks>
public readonly record struct BoneAnimationEvent(int EventId, int BoneIndex, float Time, Vector3f Position)
{
    /// <summary>The record size.</summary>
    public const int SizeInBytes = 24;

    /// <summary>The <see cref="BoneIndex"/> value that names no bone.</summary>
    public const int NoBone = -1;
}
