using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>The body's bones as its clips pose them, the collision that follows them, and the zone a hit finds on them.</content>
public abstract partial class Igi2Human : IIgi2BonedBody
{
    /// <summary>How far below which a hit by the groin's mark is to the groin, <c>614.4</c>, a double constant.</summary>
    public const double GroinReach = 614.4;

    private Igi2BodyPose? _pose;
    private Igi2BoneBuffer? _bones;
    private bool _isPoseStale = true;

    // The picture's own take of the pose while the simulation's is stale, and which staleness it was taken in.
    private Igi2BoneBuffer? _presentedBones;
    private Igi2BodyPose? _presentedPose;
    private int _poseVersion;
    private int _presentedVersion = -1;

    private Igi2BodyModel? _model;

    /// <summary>Gets or sets what the human's model gives its body: bone names, collision, damage zones.</summary>
    /// <remarks>
    /// A boned model given to any human marks the branches its clips animate, as <c>Human_UpdateInternalDataHandler</c>
    /// (<c>igi2 0x4FF2F0</c>) calls <c>HumanAnim_Init</c> for every human once its model is set: the main clip over the whole
    /// body, the upper body's from the lower back up. Left unmarked, an upper-body clip posed every bone: a player's body, which
    /// always holds its weapon's clip, stood in that clip's legs whatever its gait played beneath, and slid over the ground.
    /// </remarks>
    public Igi2BodyModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            if (value is { BoneNames.Count: > 0 } && Animation is Igi2HumanAnimation layer)
            {
                layer.SetBranches(value.BoneNames);
            }

            TouchPose();
        }
    }

    /// <summary>Gets or sets the human's collider in the level's collision world, or <see langword="null"/> when not placed in one.</summary>
    public Igi2BoneCollider? Collider { get; set; }

    /// <summary>
    /// Where a bone stands in the world, <c>BoneDynCubeObj_GetBonePosition</c> (<c>igi2 0x568190</c>): its place as the bones now stand,
    /// turned by the body's rows and added to its position; the body's position with no bones or no such bone.
    /// </summary>
    /// <param name="bone">The bone's name.</param>
    /// <returns>The position.</returns>
    public Vector3d BonePosition(string bone) =>
        Model is { } model && Igi2BoneBranches.IndexOf(model.BoneNames, bone) is >= 0 and var index ? BonePosition(index) : Position;

    /// <summary>Where a bone stands in the world, by its index in the hierarchy (<c>BoneDynCubeObj_GetBonePosition</c>).</summary>
    /// <param name="index">The bone's index.</param>
    /// <returns>The position, or the body's position with no bones or no such bone.</returns>
    public Vector3d BonePosition(int index)
    {
        if (index < 0 || BodyPose() is not { } pose || index >= pose.Pose.BoneCount)
        {
            return Position;
        }

        Vector3f b = pose.Pose.Positions[index];
        Matrix3f o = Orientation;
        Fpu z = (((Fpu)o.M33 * b.Z) + ((Fpu)o.M32 * b.Y)) + ((Fpu)o.M31 * b.X);
        float rz = z.F;
        Fpu x = (((Fpu)o.M13 * b.Z) + ((Fpu)o.M12 * b.Y)) + ((Fpu)o.M11 * b.X);
        Fpu y = (((Fpu)o.M23 * b.Z) + ((Fpu)o.M22 * b.Y)) + ((Fpu)o.M21 * b.X);
        return new Vector3d((x + Position.X).Value, (y + Position.Y).Value, ((Fpu)rz + Position.Z).Value);
    }

    /// <summary>Gets what turns the body's bones toward its aim as the hierarchy reaches them, its type's bone callbacks.</summary>
    protected virtual IIgi2BoneCallbacks? BoneCallbacks => null;

    /// <summary>
    /// Gets the pose the body stands in, which its collision is traced against and which it is drawn in: the bones brought up to
    /// date when the clips have moved since (<c>BoneDynCubeObj_UpdateHierarchy</c>, <c>igi2 0x564B50</c>, while <c>isTouched</c>).
    /// </summary>
    /// <returns>The pose, or <see langword="null"/> when the body has no rig.</returns>
    /// <remarks>
    /// The bones persist from one update to the next, as <c>patBone</c> does. The animation writes the root translation too, so the
    /// body's <see cref="RootTranslation"/> follows it.
    /// </remarks>
    public Igi2BodyPose? BodyPose()
    {
        // Read as the frame is drawn -- a trace for the laser's dot or a marker, the body's own draw -- it takes nothing.
        if (OpenIGI.Game.World.SimulationPhase.IsPresenting)
        {
            return PresentedPose();
        }

        if (_isPoseStale)
        {
            _pose = null;
            if (Animation is Igi2HumanAnimation { Rig: { } rig } animation)
            {
                if (_bones is null || _bones.BoneCount != rig.BoneCount)
                {
                    _bones = new Igi2BoneBuffer(rig.BoneCount) { Root = RootTranslation };
                }

                EvaluatingForSimulation = true;
                try
                {
                    animation.Pose(_bones, BoneCallbacks);
                }
                finally
                {
                    EvaluatingForSimulation = false;
                }

                RootTranslation = _bones.Root;
                (Igi2AnimationClip? clip, int time) = animation.AttachSource;
                _pose = new Igi2BodyPose(_bones.ToPose(rig.Parents), clip, time);
            }

            _isPoseStale = false;
        }

        return _pose;
    }

    /// <summary>
    /// Gets the pose as the picture draws it, leaving the simulation's alone: its own pose where the simulation has taken it since
    /// the clips last stepped, else the same evaluation into a buffer of the picture's own -- which writes no root translation,
    /// keeps nothing and lets the bones' callbacks change nothing.
    /// </summary>
    /// <returns>The pose, or <see langword="null"/> when the body has no rig.</returns>
    /// <remarks>
    /// The original takes a body's pose whenever anything asks for it after its clips stepped -- a round's trace, a bone's place,
    /// the render -- and a callback of the player's eases its shoulders on every take. The render is the simulation's business
    /// here, once a tick (<see cref="TakeTickPose"/>); a frame drawn here, any number of times, must take nothing, or the level
    /// would play by how often it was drawn.
    /// </remarks>
    public Igi2BodyPose? PresentedPose()
    {
        if (!_isPoseStale)
        {
            return _pose;
        }

        if (_presentedVersion == _poseVersion)
        {
            return _presentedPose;
        }

        _presentedVersion = _poseVersion;
        _presentedPose = null;
        if (Animation is Igi2HumanAnimation { Rig: { } rig } animation)
        {
            if (_presentedBones is null || _presentedBones.BoneCount != rig.BoneCount)
            {
                _presentedBones = new Igi2BoneBuffer(rig.BoneCount);
            }

            if (_bones is { } bones && bones.BoneCount == rig.BoneCount)
            {
                _presentedBones.CopyFrom(bones);
            }
            else
            {
                _presentedBones.Root = RootTranslation;
            }

            animation.Pose(_presentedBones, BoneCallbacks);
            (Igi2AnimationClip? clip, int time) = animation.AttachSource;
            _presentedPose = new Igi2BodyPose(_presentedBones.ToPose(rig.Parents), clip, time);
        }

        return _presentedPose;
    }

    /// <summary>
    /// Takes the body's pose as the original's render of the tick does, once its update is done: what the bones' callbacks keep
    /// from one take to the next -- the player's shoulders' pitch -- steps once a tick, however the level is drawn, or whether it is.
    /// </summary>
    public void TakeTickPose() => _ = BodyPose();

    /// <summary>Gets a value indicating whether the pose is being taken for the simulation: the only take a callback may change state on.</summary>
    protected bool EvaluatingForSimulation { get; private set; }

    /// <inheritdoc/>
    public BonePose? CurrentPose() => BodyPose()?.Pose;

    /// <inheritdoc/>
    public Igi2BoneMesh? BoneMesh(int level) => Model?.Mesh(level);

    /// <summary>
    /// The zone a hit lands on, <c>HumanDamage_GetDamagePartFromWeaponHit</c> (<c>igi2 0x469980</c>): the struck bone's zone; a hit
    /// to the upper body is to the back or the chest when the shot came mostly from behind or the front; and any hit taken from
    /// the front within <see cref="GroinReach"/> of the groin's mark is to the groin.
    /// </summary>
    /// <param name="hit">The hit.</param>
    /// <returns>The zone.</returns>
    public Igi2DamagePart DamagePartOf(in Igi2WeaponHit hit)
    {
        Matrix3f o = Orientation;
        Vector3f shot = hit.ShotVector;
        Fpu bx = -(Fpu)shot.X, by = -(Fpu)shot.Y, bz = -(Fpu)shot.Z;
        float across = (((bz * o.M31) + (by * o.M21)) + (bx * o.M11)).F;
        float ahead = (((bz * o.M32) + (by * o.M22)) + (bx * o.M12)).F;

        int bone = hit.Intersection.Sector;
        IReadOnlyList<Igi2DamagePart>? parts = Model?.DamageParts;
        Igi2DamagePart part = parts is not null && (uint)bone < (uint)parts.Count ? parts[bone] : Igi2DamagePart.RightFoot;
        if (part == Igi2DamagePart.UpperBody)
        {
            if (ahead <= 0f && Math.Abs(ahead) > Math.Abs(across))
            {
                part = Igi2DamagePart.Back;
            }

            if (ahead < 0f)
            {
                return part;
            }

            if (Math.Abs(ahead) > Math.Abs(across))
            {
                part = Igi2DamagePart.Chest;
            }
        }

        if (ahead < 0f || Model is not { } model)
        {
            return part;
        }

        Vector3d mark = model.GroinPosition(CurrentPose(), o, Position);
        Vector3d struck = hit.Intersection.Position;
        Fpu dx = (Fpu)struck.X - mark.X, dy = (Fpu)struck.Y - mark.Y, dz = (Fpu)struck.Z - mark.Z;
        Fpu reach = Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
        return reach.Value < GroinReach ? Igi2DamagePart.Groin : part;
    }

    /// <summary>The body's pose is to be taken again: its clips have stepped or changed.</summary>
    internal void TouchPose()
    {
        _isPoseStale = true;
        _poseVersion++;
    }
}
