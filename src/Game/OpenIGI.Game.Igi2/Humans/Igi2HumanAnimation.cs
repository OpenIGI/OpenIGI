using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>
/// A human's main animation: the clip its state plays, through the weapon it holds, <c>HumanAnim_t</c>.
/// </summary>
/// <remarks>
/// <para>
/// A body has two main controllers and plays on one of them. Setting a clip (<c>HumanAnim_SetAnimation</c>,
/// <c>igi2 0x58F110</c>) puts it on the other controller, blends across over the blend time in ticks
/// (<c>4800</c> a second, truncated), and makes that one the active controller; the one handed over from stops
/// triggering its events.
/// </para>
/// <para>
/// A state picks its clip through its row of the state-animation table and the held weapon's clips for the body's
/// rig (<c>HumanAnim_SetAnimationAndTimeFromState</c>, <c>0x58FAA0</c>). The two weapon-raise rows that lower the
/// weapon start from the clip's end less the time asked for and step backwards a frame a tick. A body holding no
/// weapon plays its rig's <c>stand</c> clip whatever the state.
/// </para>
/// <para>
/// Not yet run: the upper-body controller, the blend itself and the pose it makes. The clip's root translation does
/// not move the body — only the states' own movement and <see cref="Igi2Human.DeltaTranslation"/> do.
/// </para>
/// </remarks>
public sealed class Igi2HumanAnimation : IIgi2HumanAnimation
{
    /// <summary>How many ticks a second of blend is, the float <c>4800</c>.</summary>
    public const float BlendTicksPerSecond = 4800f;

    /// <summary>The clip a body holding no weapon plays.</summary>
    public const string UnarmedClip = "stand";

    private readonly Igi2AnimationLibrary? _library;
    private readonly Igi2BoneHierarchy? _rig;
    private readonly IReadOnlyList<int> _stateAnimations;
    private readonly Func<IReadOnlyList<Igi2AnimationClip?>?> _weaponClips;
    private readonly Igi2AnimController _first = new();
    private readonly Igi2AnimController _second = new();

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanAnimation"/> class.</summary>
    /// <param name="library">The level's clips, or <see langword="null"/> for none.</param>
    /// <param name="rig">The body's rig, or <see langword="null"/> for a model without bones.</param>
    /// <param name="stateAnimations">The base animation each state plays, <c>ptStateAnimData</c>.</param>
    /// <param name="weaponClips">The held weapon's clips for the body's rig, or <see langword="null"/> when none is held.</param>
    public Igi2HumanAnimation(
        Igi2AnimationLibrary? library,
        Igi2BoneHierarchy? rig,
        IReadOnlyList<int> stateAnimations,
        Func<IReadOnlyList<Igi2AnimationClip?>?> weaponClips)
    {
        _library = library;
        _rig = rig;
        _stateAnimations = stateAnimations ?? throw new ArgumentNullException(nameof(stateAnimations));
        _weaponClips = weaponClips ?? throw new ArgumentNullException(nameof(weaponClips));
        Main = _first;
    }

    /// <summary>Gets the active main controller.</summary>
    public Igi2AnimController Main { get; private set; }

    /// <summary>Gets the state the main clip was last set from, <c>iStateLastUsedToSetMainAnimation</c>.</summary>
    public int LastState { get; private set; } = -1;

    /// <summary>Gets the blend the last clip was set with, in ticks.</summary>
    public int BlendTicks { get; private set; }

    /// <inheritdoc/>
    public int MainTime => Main.Time;

    /// <inheritdoc/>
    public int MainTotalTime => Main.Clip?.TotalTime ?? 0;

    /// <inheritdoc/>
    public int MainTimeStep
    {
        get => Main.TimeStep;
        set => Main.TimeStep = value;
    }

    /// <inheritdoc/>
    public bool MainEnded => Main.IsEnded;

    /// <inheritdoc/>
    public void LoopMain() => Main.Flags |= Igi2AnimControllerBits.Loop;

    /// <inheritdoc/>
    public void StepMain() => Main.Step(Main.Time + Main.TimeStep);

    /// <inheritdoc/>
    /// <remarks>The upper-body controller is not run yet, so there is nothing to clear.</remarks>
    public void ClearUpperBody(float blendSeconds)
    {
    }

    /// <inheritdoc/>
    public void PlayState(int state, int time, float blendSeconds)
    {
        int animation = (uint)state < (uint)_stateAnimations.Count ? _stateAnimations[state] : 0;
        if (_weaponClips() is { } clips)
        {
            Igi2AnimationClip? clip = (uint)animation < (uint)clips.Count ? clips[animation] : null;
            if (animation is Igi2WeaponTypeAnims.LowerWeapon or Igi2WeaponTypeAnims.LowerWeaponLyingFlat)
            {
                SetAnimation(clip, (clip?.TotalTime ?? 0) - time, blendSeconds);
                Main.TimeStep = -Igi2AnimController.TicksPerFrame;
                LastState = state;
                return;
            }

            SetAnimation(clip, time, blendSeconds);
        }
        else
        {
            SetAnimation(_rig is null ? null : _library?.Find(_rig, UnarmedClip), time, blendSeconds);
        }

        LastState = state;
    }

    /// <summary>Sets a clip on the controller not playing, blends across to it and makes it the one playing.</summary>
    private void SetAnimation(Igi2AnimationClip? clip, int time, float blendSeconds)
    {
        Igi2AnimController next = ReferenceEquals(Main, _first) ? _second : _first;
        Main.Flags |= Igi2AnimControllerBits.Handover | Igi2AnimControllerBits.SuppressTriggers;
        next.Flags &= ~Igi2AnimControllerBits.SuppressTriggers;
        next.SetAnimationAndTime(clip, time);
        BlendTicks = Fpu.ToInt32((Fpu)blendSeconds * BlendTicksPerSecond);
        Main = next;
        Main.Flags &= ~Igi2AnimControllerBits.Handover;
    }
}
