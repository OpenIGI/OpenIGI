using OpenIGI.Core.Numerics;

namespace OpenIGI.Formats.Animation;

/// <summary>
/// One clip playing on a bone object — a clock over a <see cref="BoneAnimation"/>, and the rotations it
/// yields for every bone that inherits it.
/// </summary>
/// <remarks>
/// <para>
/// The original's clip instance is built by <c>0x4D47D0</c>, which registers a five-field record
/// <c>{ advance, sample, tick, callback, context }</c> — <c>0x4D4840</c>, <c>0x4D5180</c>,
/// <c>0x4D4B60</c> and <c>0x4D5080</c>. This is those first three; the fourth, the clip's completion
/// callback, is not read.
/// </para>
/// <para>
/// The important part is which track a bone reads. <c>0x4D5180</c> indexes the animation's rotation
/// tracks by <b>the asking node's own bone index</b>, not by the bone the clip was started on — which is
/// why one clip started on the root drives a whole skeleton. See <see cref="SampleRotation"/>.
/// </para>
/// </remarks>
public sealed class BoneClip
{
    private readonly BoneAnimation _animation;

    /// <summary>Initializes a new instance of the <see cref="BoneClip"/> class.</summary>
    /// <param name="animation">The clip to play.</param>
    /// <param name="bone">The bone the clip was started on.</param>
    /// <param name="loops">Whether the clip wraps rather than stopping at its duration.</param>
    /// <param name="backwards">Whether the clip runs to zero rather than to its duration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="animation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is negative.</exception>
    public BoneClip(BoneAnimation animation, int bone, bool loops = false, bool backwards = false)
    {
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentOutOfRangeException.ThrowIfNegative(bone);

        _animation = animation;
        Bone = bone;
        Loops = loops;
        Backwards = backwards;
        Rate = backwards ? -BoneAnimation.FrameStep : BoneAnimation.FrameStep;
        Time = backwards ? animation.Duration : 0f;
    }

    /// <summary>Gets the animation being played.</summary>
    public BoneAnimation Animation => _animation;

    /// <summary>Gets the bone the clip was started on.</summary>
    /// <remarks>
    /// It selects nothing by itself — every inheriting bone samples its own track. The original keeps it
    /// so the sampler can raise the "propagate this source" flag when the asking node <i>is</i> that bone.
    /// </remarks>
    public int Bone { get; }

    /// <summary>Gets whether the clip wraps at its duration — clip flag bit 0.</summary>
    public bool Loops { get; }

    /// <summary>Gets whether the clip plays backwards — clip flag bit 1.</summary>
    public bool Backwards { get; }

    /// <summary>Gets or sets the time added per tick.</summary>
    /// <remarks>
    /// One authoring frame step, negated when <see cref="Backwards"/>. The original stores the rate
    /// signed and uses the flag only to decide which end counts as the end, which is what this keeps.
    /// </remarks>
    public float Rate { get; set; }

    /// <summary>Gets where the clock has reached.</summary>
    public float Time { get; private set; }

    /// <summary>Gets whether the clip has reached its end at least once.</summary>
    /// <remarks>
    /// Clip flag bit 8 is set on the tick the end is crossed; a looping clip sets it too and keeps
    /// running, which is what lets a caller notice a cycle boundary without stopping the clip.
    /// </remarks>
    public bool Ended { get; private set; }

    /// <summary>Gets whether the last <see cref="Tick"/> crossed the end of the clip.</summary>
    public bool EndedThisTick { get; private set; }

    /// <summary>How far the clip's root-motion track moved the body on the last <see cref="Tick"/>.</summary>
    /// <remarks>
    /// The engine's clip instance holds the sampled root at <c>+16</c> and this delta at <c>+28</c>
    /// (<c>animation.md</c> §5). <b>The delta is what moves a human</b>; the sampled value is an absolute
    /// position along the track.
    /// </remarks>
    public Vector3f RootDelta { get; private set; }

    private float _eventIntervalStart;
    private float _eventIntervalEnd;
    private bool _eventIntervalBackwards;
    private bool _eventIntervalWrapped;
    private bool _hasEventInterval;

    /// <summary>Advances the clock by one tick.</summary>
    /// <remarks>
    /// <para>Transcribed from <c>0x4D4B60</c>:</para>
    /// <code>
    /// time += rate;
    /// if (time >= duration || (backwards &amp;&amp; time &lt;= 0)) {
    ///     ended = true;
    ///     if (looping) time += backwards ? duration : -duration;
    ///     else time = backwards ? 0 : duration;
    /// }
    /// </code>
    /// <para>
    /// A looping clip adjusts by one duration rather than taking a remainder, so a rate longer than the
    /// clip can leave the clock beyond the other end — the original does not guard that and neither does
    /// this. <see cref="CrossedEvent"/> exposes the same interval-based event crossing used by the retail
    /// ladder state machine.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        float duration = _animation.Duration;
        float beforeTime = Time;
        Vector3f before = SampleRoot(beforeTime);
        bool playingBackwards = Rate < 0f || (Rate == 0f && Backwards);

        Time += Rate;
        EndedThisTick = false;
        bool wrapped = false;

        if ((!playingBackwards && Time >= duration) || (playingBackwards && Time <= 0f))
        {
            Ended = true;
            EndedThisTick = true;
            if (Loops)
            {
                Time += playingBackwards ? duration : -duration;
                wrapped = true;
            }
            else
            {
                Time = playingBackwards ? 0f : duration;
            }
        }

        _eventIntervalStart = beforeTime;
        _eventIntervalEnd = Time;
        _eventIntervalBackwards = playingBackwards;
        _eventIntervalWrapped = wrapped;
        _hasEventInterval = Rate != 0f;

        // The engine keeps BOTH the sampled root (+16) and the per-tick delta (+28) on a clip instance, and
        // it is the DELTA that moves the body -- the sampled value is an absolute position along the clip's
        // own root track. Treating the absolute value as a step makes a walk cycle play on the spot, because
        // a walk's root starts at nearly zero.
        Vector3f after = SampleRoot(Time);
        RootDelta = Subtract(after, before);

        if (wrapped)
        {
            // Across a loop seam the sampled root jumps between the two ends. Add the tail and head of
            // the interval instead of treating that jump as motion. This also keeps the sign correct when
            // the ladder clip reverses at its time-zero boundary.
            Vector3f start = SampleRoot(0f);
            Vector3f end = SampleRoot(duration);
            RootDelta = playingBackwards
                ? Add(Subtract(start, before), Subtract(after, end))
                : Add(Subtract(end, before), Subtract(after, start));
        }

    }

    /// <summary>Tests whether the last tick crossed an authored event of the requested type.</summary>
    /// <param name="eventId">The event id stored in the animation.</param>
    /// <returns><see langword="true"/> when the event lies in the interval advanced last tick.</returns>
    /// <remarks>
    /// The interval is open at the old clock and closed at the new one. Consequently a ladder clip
    /// held at its time-zero boundary does not repeatedly fire that boundary, while wrapping through
    /// zero does fire it once. Reverse playback uses the mirrored rule.
    /// </remarks>
    public bool CrossedEvent(int eventId)
    {
        if (!_hasEventInterval)
        {
            return false;
        }

        foreach (BoneAnimationEvent animationEvent in _animation.Events)
        {
            if (animationEvent.EventId != eventId)
            {
                continue;
            }

            float eventTime = animationEvent.Time;
            bool crossed = _eventIntervalBackwards
                ? _eventIntervalWrapped
                    ? eventTime < _eventIntervalStart || eventTime >= _eventIntervalEnd
                    : eventTime < _eventIntervalStart && eventTime >= _eventIntervalEnd
                : _eventIntervalWrapped
                    ? eventTime > _eventIntervalStart || eventTime <= _eventIntervalEnd
                    : eventTime > _eventIntervalStart && eventTime <= _eventIntervalEnd;

            if (crossed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Moves the clock, without advancing it.</summary>
    /// <param name="time">Where to start.</param>
    /// <remarks>
    /// <c>sub_4D65B0</c>, which writes the clip's <c>+44</c> — the clock <see cref="Tick"/> steps and
    /// compares against the duration. The rate lives at <c>+40</c> and a different function sets it, so
    /// this changes <i>where</i> a clip plays from and never <i>how fast</i>. Its one retail caller is
    /// the timed fire action, which starts each shot's animation at a random point.
    /// </remarks>
    public void SkipTo(float time) => Time = time;

    /// <summary>Samples a bone's rotation at the current time.</summary>
    /// <param name="bone">The bone asking — <b>not</b> <see cref="Bone"/>.</param>
    /// <param name="spline">Whether to use SQUAD rather than a plain slerp.</param>
    /// <returns>The bone's local orientation, or the identity when it has no track.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this clip.</exception>
    /// <remarks>
    /// The result is <b>local</b> — relative to the parent. <c>0x4D41C0</c>, the default way two
    /// orientation results are combined, treats <c>0</c> as local and anything else as already in world
    /// space; a clip reports the former, which is why <see cref="BonePose"/> composes parent-then-child.
    /// </remarks>
    public Quaternion32 SampleRotation(int bone, bool spline = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _animation.BoneCount);

        return BoneRotationSampler.Sample(
            _animation.Rotations(bone),
            Time,
            _animation.Duration,
            _animation.IsCyclic,
            spline);
    }

    /// <summary>Samples the root-motion translation track at the current time.</summary>
    /// <returns>The root's position, or zero when the clip has no translation track.</returns>
    public Vector3f SampleRoot() => SampleRoot(Time);

    private Vector3f SampleRoot(float time) =>
        _animation.Translation.Length == 0
            ? default
            : BoneTrackSampler.Sample(_animation.Translation, time);

    private static Vector3f Add(Vector3f left, Vector3f right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static Vector3f Subtract(Vector3f left, Vector3f right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
}
