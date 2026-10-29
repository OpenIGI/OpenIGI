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
/// (its last key sits one frame step past the duration precisely so a looping
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

    // The body's own bone object, track 0 of which the locomotion plays on: 0x4D61D0's cross-fade, the clip's clock and its
    // root track are the shared ones every human uses, so the player's pose blends exactly as a soldier's does.
    private readonly BoneObject _bones;

    /// <summary>Initializes a new instance of the <see cref="HumanLocomotionAnimator"/> class.</summary>
    /// <param name="set">The animation set the human's skeleton uses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    public HumanLocomotionAnimator(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _bones = new BoneObject(set);
    }

    /// <summary>Gets the locomotion state being played.</summary>
    public int State { get; private set; }

    /// <summary>Gets the animation the state names, or <see cref="HumanLocomotionStates.NoAnimation"/>.</summary>
    public int AnimationId { get; private set; } = HumanLocomotionStates.NoAnimation;

    /// <summary>Gets where the clock has reached within the animation.</summary>
    public float Time => Clip?.Time ?? 0f;

    /// <summary>Gets whether the current animation loops.</summary>
    /// <remarks>
    /// A state's mode of 1 is the looping one; the movement states all carry it, which is what lets a
    /// walk cycle repeat and keep covering ground.
    /// </remarks>
    public bool Loops { get; private set; } = true;

    /// <summary>Gets whether a state has been played since the animator was made.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>Gets how far through its clip the animation is, <c>Anim_GetPhase</c> (<c>0x4D6520</c>): time over length.</summary>
    public float Phase => Clip is { } clip && clip.Animation.Duration > 0f ? clip.Time / clip.Animation.Duration : 0f;

    /// <summary>Gets the height of the root bone in the pose as blended now, in world units.</summary>
    /// <remarks>
    /// Bone 0's place, which the ground probe (<c>0x462390</c>) stands the origin on: 3877.2 units over the feet in the stand,
    /// 3812 running, 1753.8 crouched still and 2669 crouch-walking. While a state's clip fades in, it is the two clips' roots
    /// mixed by the fade, so the body sinks into a crouch over the six ticks of its blend rather than dropping.
    /// </remarks>
    public float RootHeight => _bones.BlendedRootPosition().Z;

    /// <summary>Gets whether a clip is playing: the state names one and the set holds it.</summary>
    public bool HasClip => Clip is not null;

    private BoneClip? Clip => _bones.SourceOf(0);

    /// <summary>Switches to a state, restarting its animation when the state changes.</summary>
    /// <param name="state">The locomotion state.</param>
    /// <remarks>
    /// Re-entering the state already playing is a no-op, so holding a direction does not restart the
    /// walk every tick.
    /// </remarks>
    public void Enter(int state)
    {
        if (HasStarted && state == State && AnimationId != HumanLocomotionStates.NoAnimation)
        {
            return;
        }

        Play(state, 0f, keepPhase: false);
    }

    /// <summary>Plays a state's clip as a state entry does, <c>Human_PlayAnimSlot</c> (<c>0x489E70</c>).</summary>
    /// <param name="state">The locomotion state.</param>
    /// <param name="blendSeconds">How long the new clip fades in over the old: 0.2 s for most entries, none after a jump.</param>
    /// <param name="keepPhase">
    /// Whether the new clip starts as far through itself as the old one was, the entries' <c>Anim_SetTime(length · phase)</c>,
    /// so a turn between two gaits keeps the feet in step.
    /// </param>
    public void Play(int state, float blendSeconds, bool keepPhase)
    {
        float phase = Phase;
        State = state;
        HasStarted = true;
        HumanLocomotionState binding = HumanLocomotionStates.For(state);
        AnimationId = binding.AnimationId;
        Loops = binding.Mode == 1;

        if (AnimationId == HumanLocomotionStates.NoAnimation
            || _bones.Play(0, AnimationId, Loops, blendSeconds: blendSeconds) is not { } clip)
        {
            _bones.Stop(0);
            return;
        }

        // A clip does not advance on the tick it is started: the entry runs inside the state machine, after this tick's
        // animation step has been taken. Held a step back, it stands at its start -- or the kept phase -- once the tick is done,
        // its first step covers nothing, and an event authored at that time fires on the entry tick.
        float start = keepPhase ? clip.Animation.Duration * phase : 0f;
        clip.SetTime(start - clip.Rate);
    }

    /// <summary>Sets the clip's speed as a multiple of its authored rate, <c>Anim_SetSpeed</c> (<c>0x4D6580</c>).</summary>
    /// <param name="speed">The multiple: 0.4 for the jump.</param>
    public void SetSpeed(float speed)
    {
        if (Clip is { } clip)
        {
            clip.Rate = BoneAnimation.FrameStep * speed;
        }
    }

    /// <summary>Advances the animation one tick and returns the ground it covered.</summary>
    /// <returns>
    /// The delta translation in the body's own frame — X sideways, Y forward — <b>before</b> the
    /// <see cref="HumanMotion.DefaultDeltaTranslationScale"/> is applied.
    /// </returns>
    /// <remarks>
    /// The root's step (<c>0x4D2FB0</c>): while a clip fades in, its step and the outgoing clip's mixed by the fade's weight.
    /// A loop's step across its seam is the tail of one cycle plus the head of the next, as <see cref="BoneClip.Tick"/> keeps it.
    /// </remarks>
    public Vector3f Advance()
    {
        _bones.Tick();
        return _bones.BlendedRootMotionDelta();
    }

    /// <summary>Whether the last <see cref="Advance"/> stepped over an animation event.</summary>
    /// <param name="eventId">The event id, such as <see cref="World.FootstepSounds.EventId"/>.</param>
    /// <returns><see langword="true"/> when the tick just advanced crossed it.</returns>
    /// <remarks>
    /// An event is dispatched over the <b>interval a tick covers</b>, not at a point, so a clip running fast enough to step past
    /// a footstep still fires it (<see cref="BoneClip.CrossedEvent"/>).
    /// </remarks>
    public bool CrossedEvent(int eventId) => Clip?.CrossedEvent(eventId) ?? false;

    /// <summary>Whether a one-shot animation has played out.</summary>
    /// <remarks>A looping state never finishes, which is what the caller uses to decide when to leave one.</remarks>
    public bool HasFinished => !Loops && Clip is { } clip && clip.Time >= clip.Animation.Duration;
}
