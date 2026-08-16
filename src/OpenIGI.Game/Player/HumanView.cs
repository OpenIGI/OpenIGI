using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Player;

/// <summary>
/// The <c>HumanView</c> a human carries at <c>+1260</c> — the first-person rig, and what the held weapon
/// actually hangs off.
/// </summary>
/// <remarks>
/// <para>
/// The original registers this as a task class of its own: <c>HumanView</c>, 808 bytes
/// (<c>0x464260</c>). Every <c>Human</c> creates one in its constructor (<c>0x45EB30</c>) and
/// <c>0x464660</c> initialises it with <b>skeleton 5</b> and the model <b><c>005_01_1</c></b> — both
/// literals in the executable. That is the 56-bone set, the only one of the six that is not the 33-bone
/// body rig, and <c>005_01_1</c> is one of exactly two shipped models whose bone indices reach past 32.
/// </para>
/// <para>
/// It is not a camera with a gun pasted in front of it. The eye is a transform on this object
/// (<c>+224</c> position, <c>+248</c> orientation, both filled by <c>0x464BD0</c> from the human's aim
/// angles and a wall probe), and the weapon is a model on a <b>bone of this object's skeleton</b>. That
/// is why the port needed a bone runtime before it could draw a knife.
/// </para>
/// <para>
/// Two things about the transform are worth stating because they are what make the attachment come out
/// of the data rather than out of a tuned number:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>0x4D38C0</c> builds the palette with a <b>zero root position</b> and the object's own 3×3 as the
/// rotation above the root, so the pose is in the object's space and <see cref="Position"/> is added at
/// use — which is exactly what <c>0x4600C0</c> does.
/// </item>
/// <item>
/// The palette is rebuilt <b>lazily</b>: <c>0x464BD0</c> raises a dirty byte at <c>+218</c> when it
/// writes the orientation, and <c>0x465700</c> — called first thing in the render handler — rebuilds and
/// clears it. <see cref="Pose"/> reproduces that, so posing twice in a frame costs one evaluation.
/// </item>
/// </list>
/// <para>See the original and the original.</para>
/// </remarks>
public sealed class HumanView
{
    /// <summary>The skeleton id <c>0x464660</c> passes as a literal.</summary>
    public const int SkeletonId = 5;

    /// <summary>The model <c>0x464660</c> resolves by name.</summary>
    public const string ModelName = "005_01_1";

    /// <summary>The horizontal field-of-view half-tangent the constructor sets — <c>tan(45°)</c>.</summary>
    public const float DefaultTanX = 1f;

    /// <summary>
    /// The view model's field-of-view half-tangent the constructor sets — <c>tan(30°)</c>, as
    /// <c>0x4643A0</c> stores it into <c>+488</c>.
    /// </summary>
    public const float DefaultViewModelTanX = 0.57735026f;

    private readonly BoneObject _bones;
    private Matrix3f _orientation = Matrix3f.Identity;
    private BonePose? _pose;

    /// <summary>Initializes a new instance of the <see cref="HumanView"/> class.</summary>
    /// <param name="set">The animation set for skeleton 5, read from <c>common/anims/005.iff</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The set is not skeleton 5.</exception>
    /// <remarks>
    /// The skeleton id is checked rather than trusted: the whole attachment reading rests on bone 3 of
    /// the 56-bone rig, and handing this the body's 33-bone set would silently produce an attachment at
    /// a bone that means something else entirely.
    /// </remarks>
    public HumanView(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Skeleton.Id != SkeletonId)
        {
            throw new ArgumentException(
                $"The view rig is skeleton {SkeletonId}, but this set is skeleton {set.Skeleton.Id}.",
                nameof(set));
        }

        _bones = new BoneObject(set);
    }

    /// <summary>Gets the bone object the weapon animations play on.</summary>
    public BoneObject Bones => _bones;

    /// <summary>
    /// Gets the object's world position, in engine units — the original's <c>+32</c>, and
    /// <b>not the eye</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>0x464BD0</c> commits it as <c>eye + orientation · rootMotion + bob</c> (through
    /// <c>0x4C6940</c>), and the root-motion term is the one that makes a first-person weapon look
    /// right. Each weapon's standing clip carries a translation track holding <b>that weapon's own
    /// hold offset</b> — the knife's is <c>(233, −837, −751)</c>, 6 cm right, 20 cm back and 18 cm down
    /// — so the rig, and everything hung off it, sits below and behind the eye rather than in the
    /// middle of the screen. The track also oscillates by 17.5 units over its cycle, which is the idle
    /// breathing.
    /// </para>
    /// <para>
    /// The final term is <see cref="WallPullbackInUnits"/>: a 0.5 m forward probe pulls the rig back
    /// when it meets a wall, taking the arms and held weapon with it while the eye remains fixed.
    /// </para>
    /// </remarks>
    public Vector3d Position
    {
        get
        {
            Vector3f root = _bones.SampleRootMotion();
            Vector3f offset = Rotate(_orientation, root);
            Vector3f forward = Forward(EyeOrientation);
            return new Vector3d(
                Eye.X + offset.X + (forward.X * WallPullbackInUnits),
                Eye.Y + offset.Y + (forward.Y * WallPullbackInUnits),
                Eye.Z + offset.Z + (forward.Z * WallPullbackInUnits));
        }
    }

    /// <summary>Gets the hold offset the playing clip's root-motion track carries, in engine units.</summary>
    public Vector3f RootMotion => _bones.SampleRootMotion();

    /// <summary>Gets or sets the object's world 3×3 — the original's <c>+120</c>.</summary>
    /// <remarks>
    /// <c>0x464BD0</c> composes it from the eye orientation and the weapon sway, and marks the palette
    /// dirty on every write. Setting it here does the same.
    /// </remarks>
    public Matrix3f Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            _pose = null;
        }
    }

    /// <summary>Gets or sets the eye's world position, in engine units — the original's <c>+224</c>.</summary>
    /// <remarks>
    /// The camera takes this, not the human's own position (<c>0x45FCD0</c> via <c>0x465800</c>). It is
    /// not derived from the skeleton: <c>0x464BD0</c> computes it from the human's position
    /// (<c>0x465820</c> adds the standing or crouched eye height to <c>Human+32</c>), applies the lean
    /// offsets, and then pushes it out of walls with a multi-ray cast. Everything else on this object,
    /// <see cref="Position"/> included, hangs off it.
    /// </remarks>
    public Vector3d Eye { get; set; }

    /// <summary>Gets or sets the eye's orientation — the original's <c>+248</c>.</summary>
    public Matrix3f EyeOrientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets how far collision has pulled the first-person rig back from the eye.</summary>
    /// <remarks>
    /// This is <c>HumanView+804</c>. It is zero with 0.5 m of clear space and otherwise non-positive,
    /// so the arms and weapon move opposite the eye's sight vector without shifting the camera.
    /// </remarks>
    public double WallPullbackInUnits { get; private set; }

    /// <summary>Gets or sets the horizontal field-of-view half-tangent — the original's <c>+484</c>.</summary>
    public float TanX { get; set; } = DefaultTanX;

    /// <summary>
    /// Gets or sets the <b>view model's own</b> field-of-view half-tangent — the original's <c>+488</c>,
    /// <c>tan(30°)</c>.
    /// </summary>
    /// <remarks>
    /// Not a zoom. <c>0x45FCD0</c> copies it to <c>HumanCamera+220</c>, <c>0x482700</c> publishes that
    /// as <c>dword_B81700</c>, and <c>0x49F700</c> feeds it through the <b>standard</b> view-block build
    /// (<c>sub_497D70</c>'s own algebra, inlined) while drawing a bone model with no LOD chain — which
    /// the first-person rig is. The world renders with <see cref="TanX"/> = <c>tan(45°)</c>, so the arms
    /// and the held weapon are drawn <c>tan 45° / tan 30° ≈ 1.732×</c> magnified about the screen
    /// centre — which is what holds them low in the frame.
    /// </remarks>
    public float ViewModelTanX { get; set; } = DefaultViewModelTanX;

    /// <summary>Plays a weapon animation on the whole rig.</summary>
    /// <param name="animationId">The animation id out of the weapon's own record.</param>
    /// <param name="loops">Whether the clip repeats.</param>
    /// <param name="blendSeconds">
    /// How long to cross-fade from the clip already playing into this one. Zero snaps; the standing and
    /// moving actions ease over the weapon table's default 0.4 s, which is what keeps a weapon from
    /// teleporting back to its idle pose after a shot, a reload, or an injection.
    /// </param>
    /// <returns>The clip, or <see langword="null"/> when skeleton 5's set does not define that id.</returns>
    /// <remarks>
    /// <c>0x489F20</c> is one line — <c>BoneObj_Play(human-&gt;view-&gt;boneObj, bone 0, id, loop, blend)</c>
    /// — and bone 0 is what makes it drive the whole rig. The blend argument was dropped in an earlier
    /// port; forwarding it is what restores the eased return to the stand pose. This is where a weapon's
    /// <b>full-body</b> animation ids go; its <c>UpperBody*</c> ids go to bone 1 of the human's own
    /// skeleton-0 rig (<c>0x489ED0</c>), which is why the two halves resolve in different sets.
    /// </remarks>
    public BoneClip? PlayWeaponAnimation(int animationId, bool loops, float blendSeconds = 0f)
    {
        BoneClip? clip = _bones.Play(0, animationId, loops, backwards: false, blendSeconds);
        _pose = null;
        return clip;
    }

    /// <summary>
    /// Gets whether bone 0's clip reached its end on the last tick — the node flag <c>0x100</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This one bit is the whole clock of the weapon state machine. <c>sub_4D4B60</c> clears it at the
    /// top of every advance and re-raises it when the clip crosses its end, so on a <b>looping</b> clip
    /// it marks the wrap tick and on a <b>one-shot</b> clip it latches — the clock is clamped at the
    /// duration and every following advance meets the same condition again.
    /// </para>
    /// <para>
    /// <c>0x410700</c> reads it into the machine's flag <c>0x1</c> and <c>0x489F50</c> reads it directly
    /// as "the animation has finished", which is how a reload knows when to move the rounds.
    /// </para>
    /// </remarks>
    public bool ClipEnded => _bones.SourceOf(0)?.EndedThisTick ?? false;

    /// <summary>Gets the normalized progress of the active whole-rig weapon clip.</summary>
    /// <remarks>
    /// The weapon state machine consumes the medipack at the same time as the first-person syringe
    /// animation advances. Reading the clip before <see cref="BoneObject.Tick"/> preserves the native
    /// update order: the health accumulator sees the frame that was visibly displayed during the
    /// previous tick, rather than jumping to the end of the injection on its first update.
    /// </remarks>
    public float ClipProgress
    {
        get
        {
            BoneClip? clip = _bones.SourceOf(0);
            if (clip is null || clip.Animation.Duration <= 0f)
            {
                return 0f;
            }

            return Math.Clamp(clip.Time / clip.Animation.Duration, 0f, 1f);
        }
    }

    /// <summary>Gets whether the first-person rig crossed an authored event on its last tick.</summary>
    /// <param name="eventId">The event id stored in the active weapon animation.</param>
    /// <returns><see langword="true"/> when the active whole-rig clip crossed the event this tick.</returns>
    /// <remarks>
    /// Weapon reload sounds are authored as events in this clip rather than as fields on the weapon
    /// record. Exposing the clip's interval test here keeps callers from reaching through the view
    /// object's animation implementation just to dispatch those events.
    /// </remarks>
    public bool CrossedEvent(int eventId) => _bones.SourceOf(0)?.CrossedEvent(eventId) ?? false;

    /// <summary>Skips a clip forward, as <c>sub_4D65B0</c> does by writing the clip's <c>+44</c>.</summary>
    /// <param name="time">Where to start, in the clip's own time.</param>
    /// <remarks>
    /// Used by <see cref="Weapons.WeaponAction.FireTimed"/>, which starts each shot's animation at a
    /// random point so that two shots do not look identical. Does nothing when bone 0 has no clip.
    /// </remarks>
    public void SkipTo(float time)
    {
        if (_bones.SourceOf(0) is { } clip)
        {
            clip.SkipTo(time);
        }

        _pose = null;
    }

    /// <summary>Advances the rig by one tick.</summary>
    public void Tick()
    {
        _bones.Tick();
        _pose = null;
    }

    /// <summary>Updates the retail forward wall-clearance probe for one fixed simulation tick.</summary>
    /// <param name="walls">The ordinary world-object and terrain trace.</param>
    /// <exception cref="ArgumentNullException"><paramref name="walls"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>0x464BD0</c> casts from the final eye to eye + forward × 2,048 before committing the rig
    /// transform. It does not test the player movement hull or move the camera: it changes only this
    /// object's placement, so every first-person bone attachment follows the weapon back together.
    /// </remarks>
    public void TickWallPullback(WorldRayCast walls)
    {
        ArgumentNullException.ThrowIfNull(walls);

        Vector3f forward = Forward(EyeOrientation);
        Vector3d end = new(
            Eye.X + (forward.X * HumanViewWallPullback.ProbeDistanceInUnits),
            Eye.Y + (forward.Y * HumanViewWallPullback.ProbeDistanceInUnits),
            Eye.Z + (forward.Z * HumanViewWallPullback.ProbeDistanceInUnits));

        double target = walls(Eye, end, out WorldRayHit hit)
            ? HumanViewWallPullback.TargetForHit(hit.Distance)
            : 0.0;
        WallPullbackInUnits = HumanViewWallPullback.Advance(WallPullbackInUnits, target);
    }

    /// <summary>Gets the current pose, rebuilding it only when something has changed.</summary>
    /// <returns>The posed rig, in the object's own space.</returns>
    public BonePose Pose() => _pose ??= _bones.Pose(_orientation);

    /// <summary>Resolves one of the rig's attachment points.</summary>
    /// <param name="bone">The bone, from <see cref="HumanAttachPoints"/>.</param>
    /// <returns>Where a model hung off that bone sits, in engine units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of the rig.</exception>
    public BoneAttachment Attachment(int bone) => Resolve(Pose(), Position, bone);

    /// <summary>Gets where the held weapon's model goes while the player is looking through this rig.</summary>
    /// <remarks>
    /// Bone 3, from <c>0x4600C0</c>'s first-person arm. No offset is applied and none exists: the
    /// original adds the object's position to the bone's and copies the bone's rotation whole.
    /// </remarks>
    public BoneAttachment HeldWeapon => Attachment(HumanAttachPoints.FirstPersonWeapon);

    /// <summary>Resolves an attachment out of any posed rig.</summary>
    /// <param name="pose">The posed skeleton, in its object's space.</param>
    /// <param name="origin">The object's world position, in engine units.</param>
    /// <param name="bone">The bone index.</param>
    /// <returns>The attachment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pose"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of the pose.</exception>
    /// <remarks>
    /// Shared by both arms of <c>0x4600C0</c> deliberately: the first-person and third-person cases
    /// differ only in which rig and which bone, not in the arithmetic. The third-person attachment is
    /// <see cref="HumanAttachPoints.ThirdPersonWeapon"/> on the human's own skeleton-0 pose.
    /// </remarks>
    public static BoneAttachment Resolve(BonePose pose, Vector3d origin, int bone)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, pose.BoneCount);

        Vector3f local = pose.Positions[bone];
        return new BoneAttachment(
            new Vector3d(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z),
            pose.Rotations[bone]);
    }

    // The palette applies its 3x3 as M * v, dotting the ROWS -- the same convention BonePose places
    // bones with, and the one 0x464BD0 uses on the root motion.
    private static Vector3f Rotate(Matrix3f m, Vector3f v) => new(
        (m.M11 * v.X) + (m.M12 * v.Y) + (m.M13 * v.Z),
        (m.M21 * v.X) + (m.M22 * v.Y) + (m.M23 * v.Z),
        (m.M31 * v.X) + (m.M32 * v.Y) + (m.M33 * v.Z));

    // The second matrix column is the engine's +Y axis. Both 0x464BD0's probe and the projectile
    // path use it as the eye's forward vector; see PlayerCamera.ResolveFire for the same convention.
    private static Vector3f Forward(Matrix3f m) => new(m.M12, m.M22, m.M32);
}
