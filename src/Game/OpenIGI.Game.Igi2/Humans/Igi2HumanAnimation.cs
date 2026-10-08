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
/// The upper body has its own pair of controllers (<c>HumanAnim_Init</c>, <c>0x58F650</c>), set the same way
/// (<c>HumanAnim_SetUpperBodyAnimationFromHeader</c>, <c>0x58F300</c>) or cleared to leave the main clip on the whole
/// body (<c>HumanAnim_ClearUpperBodyAnimation</c>, <c>0x58F5E0</c>). An upper-body clip that is the main clip too does
/// not trigger its events twice. A controller that steps itself does so in <see cref="AutoStep"/>: the main one handed
/// over from, while it blends out, and both upper-body ones always.
/// </para>
/// <para>
/// Not yet run: the blend itself and the pose it makes. The clip's root translation does not move the body — only
/// the states' own movement and <see cref="Igi2Human.DeltaTranslation"/> do.
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
    private readonly Igi2AnimController _first = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _second = new() { Flags = Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _third = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _fourth = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };

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

    /// <summary>Sets what the body's clips fire their anim triggers on, on all four controllers.</summary>
    public IIgi2AnimTriggerTarget? TriggerTarget
    {
        set
        {
            _first.Target = value;
            _second.Target = value;
            _third.Target = value;
            _fourth.Target = value;
        }
    }

    /// <inheritdoc/>
    public Igi2BoneHierarchy? Rig => _rig;

    /// <summary>Gets the active upper-body controller, or <see langword="null"/> when the main clip plays on the whole body.</summary>
    public Igi2AnimController? UpperBody { get; private set; }

    /// <summary>Gets the state the main clip was last set from, <c>iStateLastUsedToSetMainAnimation</c>.</summary>
    public int LastState { get; private set; } = -1;

    /// <inheritdoc/>
    public int LastUpperBodyState { get; set; } = -1;

    /// <summary>Gets the blend the last upper-body clip was set or cleared with, in ticks.</summary>
    public int UpperBodyBlendTicks { get; private set; }

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
    public void PlayClip(Igi2AnimationClip? clip, int time, float blendSeconds, int state)
    {
        SetAnimation(clip, time, blendSeconds);
        LastState = state;
    }

    /// <inheritdoc/>
    public bool UpperBodyEnded => UpperBody?.IsEnded ?? false;

    /// <inheritdoc/>
    public int UpperBodyTimeStep
    {
        get => UpperBody?.TimeStep ?? 0;
        set
        {
            if (UpperBody is { } upper)
            {
                upper.TimeStep = value;
            }
        }
    }

    /// <inheritdoc/>
    public void ClearUpperBody(float blendSeconds)
    {
        if (UpperBody is not null)
        {
            UpperBodyBlendTicks = BlendTicksFor(blendSeconds);
            UpperBody = null;
        }
    }

    /// <inheritdoc/>
    public void SetUpperBody(Igi2AnimationClip? clip, int time, float blendSeconds)
    {
        if (ReferenceEquals(UpperBody, _third) || ReferenceEquals(UpperBody, _fourth))
        {
            Igi2AnimController next = ReferenceEquals(UpperBody, _third) ? _fourth : _third;
            UpperBody.Flags |= Igi2AnimControllerBits.SuppressTriggers;
            next.SetAnimationAndTime(clip, time);
            UpperBody = next;
        }
        else
        {
            _third.SetAnimationAndTime(clip, time);
            UpperBody = _third;
        }

        UpperBodyBlendTicks = BlendTicksFor(blendSeconds);
        UpperBody.Flags = ReferenceEquals(Main.Clip, clip)
            ? UpperBody.Flags | Igi2AnimControllerBits.SuppressTriggers
            : UpperBody.Flags & ~Igi2AnimControllerBits.SuppressTriggers;
    }

    /// <inheritdoc/>
    public void PlayUpperBodyState(int state, int time, float blendSeconds)
    {
        int animation = (uint)state < (uint)_stateAnimations.Count ? _stateAnimations[state] : 0;
        if (_weaponClips() is { } clips)
        {
            Igi2AnimationClip? clip = (uint)animation < (uint)clips.Count ? clips[animation] : null;
            if (animation is Igi2WeaponTypeAnims.LowerWeapon or Igi2WeaponTypeAnims.LowerWeaponLyingFlat)
            {
                SetUpperBody(clip, (clip?.TotalTime ?? 0) - time, blendSeconds);
                UpperBodyTimeStep = -Igi2AnimController.TicksPerFrame;
                LastUpperBodyState = state;
                return;
            }

            SetUpperBody(clip, time, blendSeconds);
        }
        else
        {
            SetUpperBody(_rig is null ? null : _library?.Find(_rig, UnarmedClip), time, blendSeconds);
        }

        LastUpperBodyState = state;
    }

    /// <inheritdoc/>
    /// <remarks>In the order the controllers were made, which is the order the body's children run in.</remarks>
    public void AutoStep()
    {
        _first.AutoStep();
        _second.AutoStep();
        _third.AutoStep();
        _fourth.AutoStep();
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
        Main.Flags |= Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.SuppressTriggers;
        next.Flags &= ~Igi2AnimControllerBits.SuppressTriggers;
        next.SetAnimationAndTime(clip, time);
        BlendTicks = BlendTicksFor(blendSeconds);
        Main = next;
        Main.Flags &= ~Igi2AnimControllerBits.AutoStep;
    }

    /// <summary>A blend time in ticks, <c>(int)(seconds × 4800)</c> truncated.</summary>
    private static int BlendTicksFor(float blendSeconds) => Fpu.ToInt32((Fpu)blendSeconds * BlendTicksPerSecond);
}
