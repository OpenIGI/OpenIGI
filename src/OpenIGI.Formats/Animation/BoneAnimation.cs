namespace OpenIGI.Formats.Animation;

/// <summary>
/// One clip: a root-motion translation track, one rotation track per bone, and its events.
/// </summary>
public sealed class BoneAnimation
{
    /// <summary>
    /// The authoring step the durations are quantised to. Every retail duration is a multiple of it,
    /// though individual key times need not be.
    /// </summary>
    public const float FrameStep = 160f;

    private readonly BoneTranslationKey[] _translation;
    private readonly BoneRotationKey[][] _rotations;
    private readonly BoneAnimationEvent[] _events;

    internal BoneAnimation(
        int id,
        float duration,
        uint flags,
        BoneTranslationKey[] translation,
        BoneRotationKey[][] rotations,
        BoneAnimationEvent[] events)
    {
        Id = id;
        Duration = duration;
        Flags = flags;
        _translation = translation;
        _rotations = rotations;
        _events = events;
    }

    /// <summary>Gets the animation id, which is how scripts and the AI address it.</summary>
    public int Id { get; }

    /// <summary>Gets the duration. No rotation key in any retail set exceeds it.</summary>
    public float Duration { get; }

    /// <summary>Gets the header's flag word.</summary>
    /// <remarks>
    /// Only <c>0</c> and <c>0x80000000</c> occur. Its meaning is <b>unresolved</b>: it is measurably
    /// not a loop flag and not a marker for a static root-motion track. See
    /// the original.
    /// </remarks>
    public uint Flags { get; }

    /// <summary>Gets the root-motion translation track.</summary>
    /// <remarks>
    /// In 96 of the 376 retail animations its last key sits exactly one <see cref="FrameStep"/> past
    /// <see cref="Duration"/> — the displacement at the end of the cycle, so a loop can take the whole
    /// cycle's travel.
    /// </remarks>
    public IReadOnlyList<BoneTranslationKey> Translation => _translation;

    /// <summary>Gets the events, in file order.</summary>
    public IReadOnlyList<BoneAnimationEvent> Events => _events;

    /// <summary>Gets the number of bones this clip carries a rotation track for.</summary>
    public int BoneCount => _rotations.Length;

    /// <summary>Gets a bone's rotation track.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The keys, in time order. Never empty for retail data, but may be.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public IReadOnlyList<BoneRotationKey> Rotations(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _rotations.Length);
        return _rotations[bone];
    }
}
