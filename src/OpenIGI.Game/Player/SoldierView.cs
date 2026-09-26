using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>
/// A placed soldier's drawable state: the declared skeleton posed by the declared stand animation,
/// facing the declared heading.
/// </summary>
/// <remarks>
/// <para>
/// This is the soldier init handler (<c>0x456E50</c>) and the tail of <c>Human</c>'s (<c>0x45F890</c>)
/// without the scene graph — see the original. The original builds the bone
/// object from the task's <c>Bone Heirachy</c> id (<c>0x4D9620</c>), plays its <c>Stand Animation</c>
/// on bone 0 of the rig, resolves the ordinary <c>Model</c> parameter by name, and writes the facing
/// as a pure Z rotation of <c>Gamma</c> into the object's own 3×3 (<c>0x4B3BE0</c>).
/// </para>
/// <para>
/// The pose is in the object's space with the facing baked in (<c>0x4D38C0</c> builds the palette with
/// a zero root position under the object's 3×3), so the world position is added by whoever draws it —
/// the same convention as <see cref="HumanView"/>.
/// </para>
/// </remarks>
public sealed class SoldierView
{
    private BoneObject _bones;
    private BonePose? _pose;
    private BonePose? _unrotatedPose;

    /// <summary>Initializes a new instance of the <see cref="SoldierView"/> class.</summary>
    /// <param name="soldier">The placed task, already bound from the level script.</param>
    /// <param name="set">The animation set whose skeleton the task's <c>Bone Heirachy</c> names.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The set is not the declared skeleton.</exception>
    public SoldierView(HumanSoldierTask soldier, BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        ArgumentNullException.ThrowIfNull(set);
        if (set.Skeleton.Id != soldier.BoneHierarchy)
        {
            throw new ArgumentException(
                $"'{soldier.Name}' declares skeleton {soldier.BoneHierarchy}, but this set is skeleton {set.Skeleton.Id}.",
                nameof(set));
        }

        Soldier = soldier;
        _bones = new BoneObject(set);

        // 0x456E50: -1 means "no stand declared" and plays the class's fallback (2; 6 for a female
        // soldier's 0x454F10); a declared id that is not in the set prints "Animation #%d not found" and
        // plays the fallback too. Both go to bone 0 with flags 0 - the immediate arm, loop bit clear.
        int declared = soldier.StandAnimation;
        DeclaredStandAnimationFound = declared >= 0 && set.TryGet(declared, out _);
        StandAnimation = declared >= 0 && DeclaredStandAnimationFound
            ? declared
            : soldier.Class.FallbackStandAnimation;
        CurrentAnimation = StandAnimation;

        // The stand animation LOOPS. 0x456E50 passes flags 0, whose bit 0 is the loop bit -- but a stand
        // clip that runs out and holds its last frame leaves a soldier frozen, which is not what the game
        // does. The idle-animation countdown in HumanAI's per-tick (ai.md 3.1) re-triggers it, so the
        // observable behaviour is a continuously standing guard either way; looping here is the same
        // result without needing the AI attached.
        _bones.Play(0, StandAnimation, loops: true);

        // 0x45F890's last line: 0x4B3BE0(human + 120, gamma), the plain Z rotation - which is
        // Z(gamma)*Y(0)*X(0) of the same composition 0x4B38E0 builds. The declared facing, no mirror.
        Position = soldier.Position;
        Orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma);
    }

    /// <summary>Gets the placed task this view draws.</summary>
    public HumanSoldierTask Soldier { get; }

    /// <summary>Gets the bone object the stand animation plays on.</summary>
    public BoneObject Bones => _bones;

    /// <summary>Gets the animation actually standing in — the declared id, or the fallback.</summary>
    public int StandAnimation { get; }

    /// <summary>
    /// Gets a value indicating whether the declared <c>Stand Animation</c> resolved in the set. When the
    /// task declared one and this is <see langword="false"/>, the original prints
    /// <c>"Animation #%d not found"</c> — a caller that wants that diagnostic checks here.
    /// </summary>
    public bool DeclaredStandAnimationFound { get; }

    /// <summary>Gets the world position, in engine units. The declared <c>Position</c> until an AI moves it.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets the world 3×3 — the original's <c>Human+120</c>, a pure Z rotation of the heading.</summary>
    public Matrix3f Orientation { get; private set; }

    /// <summary>Moves and turns the body, which is what the AI's steering does each tick.</summary>
    /// <param name="position">The new world position, in engine units.</param>
    /// <param name="orientation">The new world 3×3.</param>
    public void PlaceAt(Vector3d position, Matrix3f orientation)
    {
        Position = position;
        if (Orientation != orientation)
        {
            Orientation = orientation;
            _pose = null;
        }
    }

    /// <summary>Switches the rig to a walk cycle, or back to standing.</summary>
    /// <param name="walking">Whether the soldier is moving.</param>
    /// <param name="walkAnimation">The walk clip's id, from the locomotion table's forward state.</param>
    /// <returns><see langword="true"/> when the requested clip resolved in this soldier's set.</returns>
    public bool SetMoving(bool walking, int walkAnimation)
    {
        int wanted = walking ? walkAnimation : StandAnimation;
        return PlayStateAnimation(wanted, loops: true);
    }

    /// <summary>Plays the clip selected by a HumanSoldier state with that table entry's loop mode.</summary>
    /// <param name="animation">The animation id from <c>0x53ED10</c>.</param>
    /// <param name="loops"><see langword="true"/> when the table's mode is one.</param>
    /// <returns><see langword="true"/> when the animation exists on this soldier's rig.</returns>
    /// <remarks>
    /// This is separate from <see cref="SetMoving"/> because most soldier states are one-shots. Treating
    /// rack, recover, reload, roll and stance-transition clips as walk cycles made their completion event
    /// unreachable and left the body machine permanently in the wrong state.
    /// </remarks>
    public bool PlayStateAnimation(int animation, bool loops)
    {
        if (IsDead)
        {
            return false;
        }

        if (animation == CurrentAnimation
            && _bones.SourceOf(0) is { } current
            && current.Loops == loops)
        {
            return true;
        }

        if (!_bones.Set.TryGet(animation, out _))
        {
            return false;
        }

        CurrentAnimation = animation;
        _bones.Play(0, animation, loops);
        _pose = null;
        _unrotatedPose = null;
        return true;
    }

    /// <summary>Plays a weapon's <c>UpperBody*</c> clip on the torso, leaving the legs alone.</summary>
    /// <param name="animation">The upper-body animation id out of the weapon record.</param>
    /// <param name="loops">Whether the clip repeats.</param>
    /// <param name="blendSeconds">How long to cross-fade into it.</param>
    /// <returns><see langword="true"/> when the clip resolved in this rig's set.</returns>
    /// <remarks>
    /// <para>
    /// <b>Bone 1, which is the whole of the split.</b> <c>0x4137E0</c> picks one full-body and one
    /// upper-body id per weapon action; the full-body id goes to bone 0 of the first-person rig
    /// (<c>0x489F20</c>) and the upper-body id to <b>bone 1 of the human's own skeleton-0 rig</b>
    /// (<c>0x489ED0</c> → <c>0x4D6130</c>, with bone 1 written as a literal). So retail plays these on
    /// the body every time the player fires or reloads, invisibly — the data and the dispatch were
    /// always there, and the third-person view is only what makes them visible.
    /// </para>
    /// <para>
    /// The split works because of how the rig is shaped (<c>weapons.md</c> §26.1): bones 0–3 are the
    /// pelvis and spine, the head and both arms hang off the spine top, the weapon chain 20–22 hangs off
    /// <b>bone 1</b>, and the two legs hang off the root. So bone 1's subtree is exactly torso, head,
    /// arms and weapon, and bone 0's clip keeps the legs.
    /// </para>
    /// </remarks>
    public bool PlayUpperBodyAnimation(int animation, bool loops, float blendSeconds = 0f)
    {
        if (IsDead || animation < 0 || !_bones.Set.TryGet(animation, out _))
        {
            return false;
        }

        if (animation == UpperBodyAnimation
            && _bones.SourceOf(HumanUpperBody.Bone) is { } current
            && current.Loops == loops)
        {
            return true;
        }

        UpperBodyAnimation = animation;
        _bones.Play(HumanUpperBody.Bone, animation, loops, backwards: false, blendSeconds);
        _pose = null;
        _unrotatedPose = null;
        return true;
    }

    /// <summary>Gets the upper-body clip playing on bone 1, or <c>-1</c> when the torso is unclaimed.</summary>
    public int UpperBodyAnimation { get; private set; } = -1;

    /// <summary>Hands the torso back to the full-body clip on bone 0.</summary>
    /// <remarks>
    /// <c>0x489F00</c>, which <c>0x4137E0</c> calls when the action is zero — the human is holding
    /// nothing and the upper body has no weapon clip to be in.
    /// </remarks>
    public void StopUpperBodyAnimation()
    {
        if (UpperBodyAnimation < 0)
        {
            return;
        }

        UpperBodyAnimation = -1;
        _bones.Stop(HumanUpperBody.Bone);
        _pose = null;
        _unrotatedPose = null;
    }

    /// <summary>Gives the whole rig back to whatever is playing on bone 0.</summary>
    /// <remarks>
    /// <para>
    /// <b>An authored full-body clip owns the whole body.</b> A ladder, a wire, a keypad and a death are
    /// each one clip that animates the character from the boots up, and they play on bone 0 — so a
    /// weapon stance still sitting on bone 1, or a spine still bent to an aim, replaces the half of that
    /// clip above the waist and leaves a man climbing a ladder in a rifle pose. Every one of those
    /// entries comes through here first.
    /// </para>
    /// <para>
    /// A port decision, not a retail one, and it exists only because the port draws this body. Retail's
    /// bone 1 keeps its weapon clip through a climb, which costs it nothing: nobody ever sees the
    /// player's own body, and its <em>own</em> upper-body track is what the first-person arms are.
    /// </para>
    /// </remarks>
    public void ReleaseUpperBody()
    {
        StopUpperBodyAnimation();
        AimAlong(0f);
    }

    /// <summary>Bends the torso and head to look along an aim pitch.</summary>
    /// <param name="pitchRadians">Where the human is looking, positive up; zero stands the rig level.</param>
    /// <remarks>
    /// <para>
    /// <b>A remaster addition, and not a transcription.</b> IGI1 has one orientation source in the whole
    /// engine — the node <c>0x4D6130</c> builds for the upper body — and its quaternion
    /// (<c>Human+1616</c>) is written exactly once, by the human's constructor at <c>0x410424</c>, from
    /// the rig's own bone 0 after the palette build. Nothing else in <c>.text</c> writes it, and
    /// <c>0x4D5790</c> copies it <i>by value</i> into the node when a clip starts, so even a hypothetical
    /// writer could not steer it live. Retail's body does not follow the eye, because retail never draws
    /// it. See <c>REMASTER.md</c> §R15.
    /// </para>
    /// <para>
    /// The split follows the rig rather than taste. Bone 1 carries the arms, the head and the weapon
    /// chain, so bending it is what actually points the gun; bone 4 is the neck, so bending it is what
    /// moves the head alone. Each is capped well short of the player's own ±89° pitch limit, because a
    /// spine folded to 89° is a bow rather than an aim, and the remainder is simply not expressed.
    /// </para>
    /// </remarks>
    public void AimAlong(float pitchRadians)
    {
        float chest = Math.Clamp(
            pitchRadians * HumanUpperBody.ChestShare,
            -HumanUpperBody.ChestLimitRadians,
            HumanUpperBody.ChestLimitRadians);
        float head = Math.Clamp(
            pitchRadians * HumanUpperBody.HeadShare,
            -HumanUpperBody.HeadLimitRadians,
            HumanUpperBody.HeadLimitRadians);

        if (chest == _chestBend && head == _headBend)
        {
            return;
        }

        _chestBend = chest;
        _headBend = head;

        // About X, which is the rig's own right: the weapon chain's rest translations run +Y out of the
        // chest (weapons.md section 26.1 measures 1.08 m forward), so +Y is forward and a pitch is a
        // turn about X. Positive leans back and looks up, matching the camera's own pitch sign.
        _bones.Bend(HumanUpperBody.Bone, Pitch(chest));
        _bones.Bend(HumanUpperBody.NeckBone, Pitch(head));
        _pose = null;
        _unrotatedPose = null;
    }

    private float _chestBend;
    private float _headBend;

    private static Quaternion32? Pitch(float radians) => radians == 0f
        ? null
        : Quaternion32.FromAxisAngle(new Vector3f(1f, 0f, 0f), radians);

    /// <summary>Starts one of the three authored ladder animations.</summary>
    /// <param name="animation">The state's animation id: 168, 169 or 170 in the player set.</param>
    /// <param name="loops">Whether the state loops its clip.</param>
    /// <param name="backwards">Whether it starts at the end and plays toward zero.</param>
    /// <param name="paused">Whether its time scale starts at zero.</param>
    /// <returns><see langword="true"/> when the clip resolved in this set.</returns>
    /// <remarks>
    /// State 37 starts animation 168 looped and pauses at authored event 8; state 38 plays animation
    /// 170 backwards once; state 39 plays it forwards once; state 40 loops animation 169 while falling.
    /// </remarks>
    public bool StartLadderAnimation(int animation, bool loops, bool backwards = false, bool paused = false)
    {
        if (IsDead || !_bones.Set.TryGet(animation, out _))
        {
            return false;
        }

        // The clip is a whole body; see ReleaseUpperBody for why it has to be handed one.
        ReleaseUpperBody();

        CurrentAnimation = animation;
        BoneClip? clip = _bones.Play(0, animation, loops, backwards);
        if (paused && clip is not null)
        {
            clip.Rate = 0f;
        }

        _pose = null;
        _unrotatedPose = null;
        return true;
    }

    /// <summary>Sets animation 168's time scale to −1, 0 or +1 authoring frame per tick.</summary>
    /// <param name="direction">The requested sign, clamped to −1, 0 or +1.</param>
    public void SetLadderPlaybackDirection(int direction)
    {
        if (_bones.SourceOf(0) is { } clip)
        {
            clip.Rate = Math.Sign(direction) * BoneAnimation.FrameStep;
        }
    }

    /// <summary>Gets whether the last tick crossed animation 168's authored rung boundary.</summary>
    public bool CrossedLadderBoundary => _bones.SourceOf(0)?.CrossedEvent(8) ?? false;

    /// <summary>Gets how far through the clip on bone 0 the rig is, 0 at the start of the rung and 1 at its end.</summary>
    /// <remarks>Counts up whichever way the clip plays, so a backward (down) rung still runs 0 → 1.</remarks>
    public float ClipProgress
    {
        get
        {
            if (_bones.SourceOf(0) is not { } clip)
            {
                return 1f;
            }

            float duration = clip.Animation.Duration;
            if (duration <= 0f)
            {
                return 1f;
            }

            float forward = Math.Clamp(clip.Time / duration, 0f, 1f);
            return clip.Rate < 0f || (clip.Rate == 0f && clip.Backwards) ? 1f - forward : forward;
        }
    }

    /// <summary>Gets whether the clip on bone 0 finished on the last <see cref="Tick"/> — the rung completed.</summary>
    public bool ClipEndedThisTick => _bones.SourceOf(0)?.EndedThisTick ?? false;

    /// <summary>How far the playing clip's own root-motion track moves the body, in engine units.</summary>
    /// <remarks>
    /// The engine moves a human by its clip's root motion and nothing else (M8), so this is the soldier's
    /// speed — no constant is chosen anywhere.
    /// </remarks>
    public Vector3f RootMotion() => _bones.RootMotionDelta();

    /// <summary>Gets a value indicating whether this soldier has been killed.</summary>
    public bool IsDead { get; private set; }

    /// <summary>Gets the animation currently playing on bone 0.</summary>
    public int CurrentAnimation { get; private set; }

    /// <summary>Advances the rig by one tick.</summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing called this before, and the pose was cached with <c>??=</c></b> — so every soldier in the
    /// game stood frozen on the first frame its stand animation resolved to, which reads as a default A-pose.
    /// The clip has to be advanced and the cached pose dropped, exactly as the player's rig is.
    /// </para>
    /// <para>
    /// A non-looping clip that has run out holds its last frame, which is what a death animation wants and
    /// what <see cref="BoneClip"/> already does.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        _bones.Tick();
        if (_bones.ChangedThisTick)
        {
            _pose = null;
            _unrotatedPose = null;
        }
    }

    /// <summary>
    /// The two literal death animations, from the soldier class's <b>two special</b> death states —
    /// <c>0x45C5A0</c> plays 24 and <c>0x45C650</c> plays 25.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These are not the ordinary death clips, and using them was the floating-corpse bug.</b> The
    /// soldier class has <em>three</em> death states — entries 32, 33 and 34 of its 38-state machine — and
    /// the third (<c>0x45C440</c>) is the general one, which works its id out from the killing weapon, the
    /// zone struck and which way the round came. <see cref="SoldierDeath.Choose"/> is that function.
    /// </para>
    /// <para>
    /// Measured over the retail sets: on skeleton 1, clip 24 starts from a crouch (root Z 2219 → 1020) and
    /// clip 25 is a 15-tick twitch already on the floor (462 → 523), while the clips the general state
    /// names — 22, 23, 28, 30, 71 — all run from the standing pelvis height of 3877 down to 524…772. So
    /// playing 24 or 25 on a guard who was standing left the body at standing height with a lying pose,
    /// which is exactly what a floating corpse looks like. Kept because the two literals are real; no
    /// longer what kills a guard.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> LiteralDeathAnimations { get; } = [24, 25];

    /// <summary>
    /// Gets where the playing clip's own root-motion track currently puts the skeleton root, in engine
    /// units, in the body's model space — the original's <c>Human+1300</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The soldier update calls <c>sub_4D2FB0(human + 1300, human + 1312, human + 104)</c> every tick, so
    /// <c>+1300</c> is this and <c>+1312</c> is the per-tick delta the mover consumes. <c>0x48A340</c> —
    /// which the AI's eye, the wall sweep and the ground query all locate a bone with — adds <c>+1300</c>
    /// to the bone's rotated position, so a bone's true offset from the body's origin includes it.
    /// </para>
    /// <para>
    /// It matters because a skeleton's rest translation for bone 0 is <b>zero</b> in every retail set, so
    /// the pose puts the pelvis at the object origin and the feet almost a metre below it. The root
    /// track's Z is what bridges that: 3877 units (0.95 m) standing, and 524…772 once a death clip has
    /// run. Only the Z is used here — the horizontal part is already carried by the body's own position,
    /// which the mover advances by the same track's delta, and a Z-only heading rotation leaves Z alone
    /// anyway.
    /// </para>
    /// </remarks>
    public Vector3f RootSample => _bones.SampleRootMotion();

    /// <summary>Kills the soldier and starts its death animation.</summary>
    /// <param name="deathAnimation">
    /// The animation id to play — what <see cref="SoldierDeath.Choose"/> named. <c>-1</c> keeps the
    /// current clip, which is what the original's own switch does when no arm names one.
    /// </param>
    /// <remarks>
    /// The soldier keeps being drawn and keeps ticking — a dead guard in the original falls and stays on the
    /// ground. Vanishing was the port having nothing to play; the clip holds its last frame once it runs out,
    /// so the body settles and stays.
    /// </remarks>
    public void Kill(int deathAnimation)
    {
        if (IsDead)
        {
            return;
        }

        // Before IsDead, which the release's own guards test: a fall is an authored full-body clip like
        // a climb, and a corpse must not go down still holding a rifle stance in its shoulders.
        ReleaseUpperBody();

        IsDead = true;
        if (deathAnimation >= 0 && _bones.Set.TryGet(deathAnimation, out _))
        {
            CurrentAnimation = deathAnimation;
            _bones.Play(0, deathAnimation, loops: false);
            _pose = null;
            _unrotatedPose = null;
        }
    }

    /// <summary>Rebuilds this body at its authored spawn, as a GuardGenerator clone does.</summary>
    /// <remarks>
    /// The native generator recursively clones the stored task template rather than reviving the corpse
    /// object in place. Replacing the bone object clears every animation channel and event cursor while
    /// retaining this managed view's identity, which is important because renderer registries key on it.
    /// </remarks>
    public void Respawn()
    {
        BoneAnimationSet set = _bones.Set;
        _bones = new BoneObject(set);
        _bones.Play(0, StandAnimation, loops: true);

        IsDead = false;
        CurrentAnimation = StandAnimation;
        Position = Soldier.Position;
        Orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, Soldier.Gamma);
        _pose = null;
        _unrotatedPose = null;
    }

    /// <summary>Gets the current pose, in the object's own space with the facing baked in.</summary>
    /// <returns>The posed rig.</returns>
    public BonePose Pose() => _pose ??= _bones.Pose(Orientation);

    /// <summary>Gets the pose built with an identity root, for a renderer that applies the facing itself.</summary>
    /// <returns>The posed rig, unrotated.</returns>
    /// <remarks>
    /// Exactly equivalent to <see cref="Pose"/> under <see cref="Orientation"/>, because the root
    /// rotation only ever left-multiplies every bone — the trade the port's skinned draw already makes
    /// so that a shared pose skins once and turns per soldier (see <c>ViewModelRenderer</c>).
    /// </remarks>
    public BonePose UnrotatedPose() => _unrotatedPose ??= _bones.Pose();

}
