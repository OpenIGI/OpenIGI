using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>
/// Plays a human's locomotion animation and hands back the ground it covered this tick.
/// </summary>
/// <remarks>
/// <para>
/// This is what replaces a walking-speed constant. A locomotion state names an animation
/// (<see cref="HumanLocomotionStates"/>), the animation carries a <b>root-motion</b> translation track
/// (<c>animation.md</c> §4 — its last key sits one frame step past the duration precisely so a looping
/// cycle yields the whole cycle's travel), and the difference between two samples of that track is how
/// far the body moved.
/// </para>
/// <para>
/// So the speed of a walk is a property of its animation. Sampling the retail set bears that out: state
/// 1's clip travels +Y, state 4's −Y, state 7's +X and state 8's −X — forward, back, right and left —
/// which is how <see cref="HumanMotionState"/>'s four movement bits were named.
/// </para>
/// </remarks>
public sealed class HumanLocomotionAnimator
{
    /// <summary>How far the animation clock advances each tick.</summary>
    /// <remarks>
    /// One authoring frame — <see cref="BoneAnimation.FrameStep"/>, 160 — per simulation tick. That is
    /// what makes a clip's duration in frames its duration in ticks.
    /// </remarks>
    public const float TimePerTick = BoneAnimation.FrameStep;

    private readonly BoneAnimationSet _set;
    private float _intervalStart;
    private float _intervalEnd;
    private bool _intervalWrapped;
    private bool _hasInterval;

    /// <summary>Initializes a new instance of the <see cref="HumanLocomotionAnimator"/> class.</summary>
    /// <param name="set">The animation set the human's skeleton uses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    public HumanLocomotionAnimator(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _set = set;
    }

    /// <summary>Gets the locomotion state being played.</summary>
    public int State { get; private set; }

    /// <summary>Gets the animation the state names, or <see cref="HumanLocomotionStates.NoAnimation"/>.</summary>
    public int AnimationId { get; private set; }

    /// <summary>Gets where the clock has reached within the animation.</summary>
    public float Time { get; private set; }

    /// <summary>Gets whether the current animation loops.</summary>
    /// <remarks>
    /// A state's mode of 1 is the looping one; the movement states all carry it, which is what lets a
    /// walk cycle repeat and keep covering ground.
    /// </remarks>
    public bool Loops { get; private set; }

    /// <summary>Switches to a state, restarting its animation when the state changes.</summary>
    /// <param name="state">The locomotion state.</param>
    /// <remarks>
    /// Re-entering the state already playing is a no-op, so holding a direction does not restart the
    /// walk every tick.
    /// </remarks>
    public void Enter(int state)
    {
        if (state == State && AnimationId != HumanLocomotionStates.NoAnimation)
        {
            return;
        }

        State = state;
        HumanLocomotionState binding = HumanLocomotionStates.For(state);
        AnimationId = binding.AnimationId;
        Loops = binding.Mode == 1;
        Time = 0f;
    }

    /// <summary>Advances the animation one tick and returns the ground it covered.</summary>
    /// <returns>
    /// The delta translation in the body's own frame — X sideways, Y forward — <b>before</b> the
    /// <see cref="HumanMotion.DefaultDeltaTranslationScale"/> is applied.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The delta is the difference between the track sampled at the new time and at the old one, which
    /// is what makes it independent of where in the cycle the animation happens to be.
    /// </para>
    /// <para>
    /// A looping clip wraps, and the wrap is where the extra key past the duration earns its keep: the
    /// travel across the seam is the end key minus the start key, so a loop does not lose the last
    /// frame's ground or jump backwards when it repeats.
    /// </para>
    /// </remarks>
    public Vector3f Advance()
    {
        if (AnimationId == HumanLocomotionStates.NoAnimation || !_set.TryGet(AnimationId, out BoneAnimation animation))
        {
            return default;
        }

        if (animation.Translation.Count == 0)
        {
            Time += TimePerTick;
            return default;
        }

        float previous = Time;
        float next = Time + TimePerTick;
        float duration = animation.Duration;
        _intervalStart = previous;
        _intervalEnd = next;
        _intervalWrapped = false;
        _hasInterval = true;

        Vector3f before = BoneTrackSampler.Sample(animation.Translation, previous);

        if (Loops && duration > 0f && next >= duration)
        {
            // Across the seam: what is left of this cycle, then what the next one has already covered.
            Vector3f atEnd = BoneTrackSampler.Sample(animation.Translation, duration);
            float carried = next - duration;
            Vector3f atStart = BoneTrackSampler.Sample(animation.Translation, 0f);
            Vector3f afterWrap = BoneTrackSampler.Sample(animation.Translation, carried);

            Time = carried;
            _intervalEnd = carried;
            _intervalWrapped = true;

            return new Vector3f(
                (atEnd.X - before.X) + (afterWrap.X - atStart.X),
                (atEnd.Y - before.Y) + (afterWrap.Y - atStart.Y),
                (atEnd.Z - before.Z) + (afterWrap.Z - atStart.Z));
        }

        if (!Loops && duration > 0f && next > duration)
        {
            next = duration;
            _intervalEnd = next;
        }

        Time = next;
        Vector3f after = BoneTrackSampler.Sample(animation.Translation, next);

        return new Vector3f(after.X - before.X, after.Y - before.Y, after.Z - before.Z);
    }

    /// <summary>Whether the last <see cref="Advance"/> stepped over an animation event.</summary>
    /// <param name="eventId">The event id, such as <see cref="World.FootstepSounds.EventId"/>.</param>
    /// <returns><see langword="true"/> when the tick just advanced crossed it.</returns>
    /// <remarks>
    /// The same rule <c>BoneClip</c> applies, and for the same reason: an event is dispatched over the
    /// <b>interval a tick covers</b>, not at a point, so a clip running fast enough to step past a
    /// footstep still fires it. The interval is open at the old clock and closed at the new one, and a
    /// looping wrap is tested as two pieces so the seam neither drops an event nor fires it twice.
    /// This rig only ever plays forwards, so the backwards arm <c>BoneClip</c> carries is not needed.
    /// </remarks>
    public bool CrossedEvent(int eventId)
    {
        if (!_hasInterval
            || AnimationId == HumanLocomotionStates.NoAnimation
            || !_set.TryGet(AnimationId, out BoneAnimation animation))
        {
            return false;
        }

        foreach (BoneAnimationEvent animationEvent in animation.Events)
        {
            if (animationEvent.EventId != eventId)
            {
                continue;
            }

            float time = animationEvent.Time;
            bool crossed = _intervalWrapped
                ? time > _intervalStart || time <= _intervalEnd
                : time > _intervalStart && time <= _intervalEnd;

            if (crossed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a one-shot animation has played out.</summary>
    /// <remarks>A looping state never finishes, which is what the caller uses to decide when to leave one.</remarks>
    public bool HasFinished =>
        !Loops
        && AnimationId != HumanLocomotionStates.NoAnimation
        && _set.TryGet(AnimationId, out BoneAnimation animation)
        && Time >= animation.Duration;
}
