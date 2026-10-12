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

    /// <summary>Gets or sets what the human's model gives its body: bone names, collision, damage zones.</summary>
    public Igi2BodyModel? Model { get; set; }

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
        if (_isPoseStale)
        {
            _pose = null;
            if (Animation is Igi2HumanAnimation { Rig: { } rig } animation)
            {
                if (_bones is null || _bones.BoneCount != rig.BoneCount)
                {
                    _bones = new Igi2BoneBuffer(rig.BoneCount) { Root = RootTranslation };
                }

                animation.Pose(_bones, BoneCallbacks);
                RootTranslation = _bones.Root;
                (Igi2AnimationClip? clip, int time) = animation.AttachSource;
                _pose = new Igi2BodyPose(_bones.ToPose(rig.Parents), clip, time);
            }

            _isPoseStale = false;
        }

        return _pose;
    }

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
    internal void TouchPose() => _isPoseStale = true;
}
