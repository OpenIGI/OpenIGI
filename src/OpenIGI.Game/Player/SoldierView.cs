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
    /// <summary>
    /// The animation a soldier stands in when its task declares none (<c>-1</c>) or declares an id the
    /// set does not hold — the literal <c>2</c> in <c>0x456E50</c>.
    /// </summary>
    /// <remarks>Every shipped set starts its ids at 2, so the fallback resolves in all six.</remarks>
    public const int FallbackStandAnimation = 2;

    private readonly BoneObject _bones;
    private BonePose? _pose;

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

        // 0x456E50: -1 means "no stand declared" and plays 2; a declared id that is not in the set
        // prints "Animation #%d not found" and also plays 2. Both go to bone 0 with flags 0 - the
        // immediate arm, loop bit clear.
        int declared = soldier.StandAnimation;
        DeclaredStandAnimationFound = declared >= 0 && set.TryGet(declared, out _);
        StandAnimation = declared >= 0 && DeclaredStandAnimationFound ? declared : FallbackStandAnimation;
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
        Orientation = orientation;
        _pose = null;
    }

    /// <summary>Switches the rig to a walk cycle, or back to standing.</summary>
    /// <param name="walking">Whether the soldier is moving.</param>
    /// <param name="walkAnimation">The walk clip's id, from the locomotion table's forward state.</param>
    /// <returns><see langword="true"/> when the requested clip resolved in this soldier's set.</returns>
    public bool SetMoving(bool walking, int walkAnimation)
    {
        if (IsDead)
        {
            return false;
        }

        int wanted = walking ? walkAnimation : StandAnimation;
        if (wanted == CurrentAnimation)
        {
            return true;
        }

        if (!_bones.Set.TryGet(wanted, out _))
        {
            return false;
        }

        CurrentAnimation = wanted;
        _bones.Play(0, wanted, loops: true);
        _pose = null;
        return true;
    }

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
        _pose = null;
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

        IsDead = true;
        if (deathAnimation >= 0 && _bones.Set.TryGet(deathAnimation, out _))
        {
            CurrentAnimation = deathAnimation;
            _bones.Play(0, deathAnimation, loops: false);
            _pose = null;
        }
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
    public BonePose UnrotatedPose() => _bones.Pose();
}
