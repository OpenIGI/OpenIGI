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
/// The controllers sit under three blenders as <c>HumanAnim_Init</c> wires them: the main blend between the two main
/// controllers, the upper body's swap between its two, and over both the mix of the upper body into the main clip, the one
/// the body's pose is taken from (<see cref="Pose"/>). The main controllers animate every bone; the upper body's, and its swap,
/// the <c>lower body</c> branch, which in these rigs holds the spine, arms and head; the main blend and the mix the
/// <c>center</c> branch. Both main and swap blends take a finished blend's old clip away.
/// </para>
/// <para>
/// The clip's root translation does not move the body — only the states' own movement and
/// <see cref="Igi2Human.DeltaTranslation"/> do; it places the root bone.
/// </para>
/// </remarks>
public sealed class Igi2HumanAnimation : IIgi2HumanAnimation
{
    /// <summary>How many ticks a second of blend is, the float <c>4800</c>.</summary>
    public const float BlendTicksPerSecond = 4800f;

    /// <summary>The clip a body holding no weapon plays.</summary>
    public const string UnarmedClip = "stand";

    /// <summary>The state a named clip stands for when no state plays it, <c>2</c>.</summary>
    public const int NamedStateFallback = 2;

    private readonly Igi2AnimationLibrary? _library;
    private readonly Igi2BoneHierarchy? _rig;
    private readonly IReadOnlyList<int> _stateAnimations;
    private readonly Func<IReadOnlyList<Igi2AnimationClip?>?> _weaponClips;
    private readonly Igi2AnimController _first = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _second = new() { Flags = Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _third = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimController _fourth = new() { Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip };
    private readonly Igi2AnimBlend _mainBlend = new() { DropsOldSource = true };
    private readonly Igi2AnimBlend _upperSwapBlend = new() { DropsOldSource = true };
    private readonly Igi2AnimBlend _upperBlend = new();

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

        // HumanAnim_Init (igi2 0x58F650): the second main controller is the active one, and every blend starts finished.
        Main = _second;
        _mainBlend.Create(_first, _second);
        _upperSwapBlend.Create(_third, _fourth);
        _upperBlend.Create(_upperSwapBlend, _mainBlend);
    }

    /// <summary>
    /// Marks the branches the controllers and blenders animate, as <c>HumanAnim_Init</c> (<c>igi2 0x58F650</c>) builds the rig's
    /// animated info: the <c>center</c> branch for the main blend and the mix, the <c>lower body</c> branch for the upper body.
    /// </summary>
    /// <param name="boneNames">The model's bone names, in bone order.</param>
    public void SetBranches(IReadOnlyList<string> boneNames)
    {
        ArgumentNullException.ThrowIfNull(boneNames);
        if (_rig is null)
        {
            return;
        }

        bool[] all = Igi2BoneBranches.Mark(_rig, Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.CenterBone), include: true);
        bool[] upper = Igi2BoneBranches.Mark(_rig, Igi2BoneBranches.IndexOf(boneNames, Igi2BoneBranches.LowerBodyBone), include: true);
        _mainBlend.Mask = all;
        _upperBlend.Mask = all;
        _upperSwapBlend.Mask = upper;
        _third.Mask = upper;
        _fourth.Mask = upper;
    }

    /// <summary>
    /// The body's pose: the mix's interpolation into the bones (<c>BoneDynCubeObj_eInterpolateQTaskEvent</c>), then the
    /// hierarchy composed with the bones' callbacks (<c>BoneDynCubeObj_UpdateHierarchy</c>, <c>igi2 0x564B50</c>).
    /// </summary>
    /// <param name="bones">The body's bones, written in place; their root translation is the body's.</param>
    /// <param name="callbacks">The bones' callbacks, or <see langword="null"/> for none.</param>
    public void Pose(Igi2BoneBuffer bones, IIgi2BoneCallbacks? callbacks)
    {
        ArgumentNullException.ThrowIfNull(bones);
        if (_rig is null)
        {
            return;
        }

        _upperBlend.Interpolate(bones, _upperBlend.Mask);
        Igi2BoneMath.UpdateHierarchy(_rig, bones, callbacks);
    }

    /// <summary>Gets the clip and time the body's attach objects hang from: the upper body's while it plays, else the main one's.</summary>
    public (Igi2AnimationClip? Clip, int Time) AttachSource =>
        UpperBody is { Clip: { } upper } upperController ? (upper, upperController.Time) : (Main.Clip, Main.Time);

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
    public void SetMainFlags(Igi2AnimControllerBits raise, Igi2AnimControllerBits clear) => Main.Flags = (Main.Flags & ~clear) | raise;

    /// <inheritdoc/>
    public void StepMain() => Main.Step(Main.Time + Main.TimeStep);

    /// <inheritdoc/>
    public void PlayClip(Igi2AnimationClip? clip, int time, float blendSeconds, int state)
    {
        SetAnimation(clip, time, blendSeconds);
        LastState = state;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The state is the first whose base animation is the first of the held weapon's whose clip has the same name
    /// (<c>WeaponTypeAnim_GetWeaponTypeAnimFromName</c>, <c>igi2 0x4FA060</c>, which falls back to the first base); with no
    /// weapon's table, or no state playing it, it is state 2.
    /// </remarks>
    public void PlayNamed(string name, int time, float blendSeconds)
    {
        Igi2AnimationClip? clip = _rig is null ? null : _library?.Find(_rig, name);
        SetAnimation(clip, time, blendSeconds);
        LastState = NamedStateFallback;
        if (clip is null || _weaponClips() is not { } clips)
        {
            return;
        }

        int animation = 0;
        for (int i = 0; i < clips.Count && i < Igi2WeaponTypeAnims.Count; i++)
        {
            if (clips[i] is { } candidate && string.Equals(candidate.Name, clip.Name, StringComparison.Ordinal))
            {
                animation = i;
                break;
            }
        }

        for (int state = 0; state < _stateAnimations.Count; state++)
        {
            if (_stateAnimations[state] == animation)
            {
                LastState = state;
                return;
            }
        }
    }

    /// <inheritdoc/>
    public bool UpperBodyEnded => UpperBody?.IsEnded ?? false;

    /// <inheritdoc/>
    public int? UpperBodyTotalTime => UpperBody is { } upper ? upper.Clip?.TotalTime ?? 0 : null;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>HumanAnim_IsBlendFinished</c> (<c>igi2 0x58FD40</c>): the main blend, the upper body's swap and its mix over the main
    /// clip have all finished.
    /// </remarks>
    public bool IsBlendFinished => _mainBlend.IsFinished && _upperSwapBlend.IsFinished && _upperBlend.IsFinished;

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
            _upperBlend.Blend(UpperBodyBlendTicks, _mainBlend, _upperSwapBlend);
            UpperBody = null;
        }
    }

    /// <inheritdoc/>
    public void SetUpperBody(Igi2AnimationClip? clip, int time, float blendSeconds)
    {
        if (ReferenceEquals(UpperBody, _third) || ReferenceEquals(UpperBody, _fourth))
        {
            Igi2AnimController old = UpperBody;
            Igi2AnimController next = ReferenceEquals(old, _third) ? _fourth : _third;
            old.Flags |= Igi2AnimControllerBits.SuppressTriggers;
            next.SetAnimationAndTime(clip, time);
            UpperBody = next;
            UpperBodyBlendTicks = BlendTicksFor(blendSeconds);
            _upperSwapBlend.Blend(UpperBodyBlendTicks, next, old);
        }
        else
        {
            // HumanAnim_SetUpperBodyAnimationFromHeader (igi2 0x58F300): from no upper body the swap is made at once and the
            // upper body is blended in over the main clip.
            _third.SetAnimationAndTime(clip, time);
            UpperBody = _third;
            UpperBodyBlendTicks = BlendTicksFor(blendSeconds);
            _upperSwapBlend.Blend(0, _third, _fourth);
            _upperBlend.Blend(UpperBodyBlendTicks, _upperSwapBlend, _mainBlend);
        }

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
    /// <remarks>
    /// In the order the body's children run: the mix, then its sources each before their own sources, the upper body's swap
    /// before the main blend (<c>AnimBlender_RunHandler</c>, <c>igi2 0x5D9630</c>, runs itself and then its children, which
    /// <c>AnimBlender_New</c>, <c>0x5D9E80</c>, appends old source first).
    /// </remarks>
    public void AutoStep()
    {
        _upperBlend.Run();
        _upperSwapBlend.Run();
        _third.AutoStep();
        _fourth.AutoStep();
        _mainBlend.Run();
        _first.AutoStep();
        _second.AutoStep();
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

    /// <inheritdoc/>
    /// <remarks>Unlike <see cref="PlayState"/>, a lowered weapon's clip plays forward, from its end.</remarks>
    public void PlayStateAtEnd(int state, float blendSeconds)
    {
        int animation = (uint)state < (uint)_stateAnimations.Count ? _stateAnimations[state] : 0;
        Igi2AnimationClip? clip = _weaponClips() is { } clips
            ? ((uint)animation < (uint)clips.Count ? clips[animation] : null)
            : (_rig is null ? null : _library?.Find(_rig, UnarmedClip));
        SetAnimation(clip, clip?.TotalTime ?? 0, blendSeconds);
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
        _mainBlend.Blend(BlendTicks, next, Main);
        Main = next;
        Main.Flags &= ~Igi2AnimControllerBits.AutoStep;
    }

    /// <summary>A blend time in ticks, <c>(int)(seconds × 4800)</c> truncated.</summary>
    private static int BlendTicksFor(float blendSeconds) => Fpu.ToInt32((Fpu)blendSeconds * BlendTicksPerSecond);
}
