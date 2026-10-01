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

    /// <summary>Builds a clip directly, for tools and tests.</summary>
    /// <param name="id">The animation id.</param>
    /// <param name="duration">The duration, in the authoring unit.</param>
    /// <param name="rotations">One rotation track per bone.</param>
    /// <param name="translation">The root-motion track, or empty.</param>
    /// <param name="events">The events, or empty.</param>
    /// <param name="flags">The header's flag word; see <see cref="CyclicFlag"/>.</param>
    /// <returns>The clip.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rotations"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A rotation track is <see langword="null"/>.</exception>
    public static BoneAnimation Create(
        int id,
        float duration,
        IReadOnlyList<IReadOnlyList<BoneRotationKey>> rotations,
        IReadOnlyList<BoneTranslationKey>? translation = null,
        IReadOnlyList<BoneAnimationEvent>? events = null,
        uint flags = 0)
    {
        ArgumentNullException.ThrowIfNull(rotations);

        BoneRotationKey[][] tracks = new BoneRotationKey[rotations.Count][];
        for (int bone = 0; bone < rotations.Count; bone++)
        {
            tracks[bone] = rotations[bone] is { } track
                ? [.. track]
                : throw new ArgumentException($"Bone {bone} has no rotation track.", nameof(rotations));
        }

        return new BoneAnimation(
            id,
            duration,
            flags,
            translation is null ? [] : [.. translation],
            tracks,
            events is null ? [] : [.. events]);
    }

    /// <summary>Gets the animation id, which is how scripts and the AI address it.</summary>
    public int Id { get; }

    /// <summary>Gets the duration. No rotation key in any retail set exceeds it.</summary>
    public float Duration { get; }

    /// <summary>The <see cref="Flags"/> bit that makes a rotation track wrap round to its first key.</summary>
    public const uint CyclicFlag = 0x80000000u;

    /// <summary>Gets the header's flag word.</summary>
    /// <remarks>
    /// Only <c>0</c> and <see cref="CyclicFlag"/> occur, and the rotation sampler (<c>0x4D5295</c>)
    /// tests exactly that bit — see <see cref="IsCyclic"/> and the original.
    /// </remarks>
    public uint Flags { get; }

    /// <summary>
    /// Gets whether a rotation track past its last key interpolates back to key 0 rather than holding.
    /// </summary>
    /// <remarks>
    /// <c>BOAH+4</c> bit 31, tested at <c>0x4D5295</c>. It is what lets a seamless loop's last key differ
    /// from its first: the sampler bridges the two over the time left before <see cref="Duration"/>.
    /// </remarks>
    public bool IsCyclic => (Flags & CyclicFlag) != 0;

    /// <summary>Gets the root-motion translation track.</summary>
    /// <remarks>
    /// In 96 of the 376 retail animations its last key sits exactly one <see cref="FrameStep"/> past
    /// <see cref="Duration"/> — the displacement at the end of the cycle, so a loop can take the whole
    /// cycle's travel.
    /// </remarks>
    public ReadOnlySpan<BoneTranslationKey> Translation => _translation;

    /// <summary>Gets the events, in file order.</summary>
    public IReadOnlyList<BoneAnimationEvent> Events => _events;

    /// <summary>Gets the number of bones this clip carries a rotation track for.</summary>
    public int BoneCount => _rotations.Length;

    /// <summary>Gets a bone's rotation track.</summary>
    /// <param name="bone">The bone index.</param>
    /// <returns>The keys, in time order. Never empty for retail data, but may be.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone.</exception>
    public ReadOnlySpan<BoneRotationKey> Rotations(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _rotations.Length);
        return _rotations[bone];
    }
}
