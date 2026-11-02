using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi1.Physics;

/// <content>
/// Where the body comes from and what it draws as: seeding it from the pose it died in, reading every
/// bone's place back out of it, and building the pose the renderer skins.
/// </content>
public sealed partial class RagdollBody
{
    /// <summary>Gets where a bone's joint sits in the world, in engine units.</summary>
    /// <param name="bone">The bone index, simulated or carried.</param>
    /// <returns>The joint position — the bone's own origin, not its centre of mass.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public Vector3d PositionOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _boneCount);

        int body = _rig.BodyOf(bone);
        Vector3d joint = _position[body] - Turned(body, _centre[body]);
        if (_rig.BoneOf(body) == bone)
        {
            return joint;
        }

        // A carried bone hangs off the body by the rest translations down to it, each turned by the bone
        // above it -- the same walk BonePose.Evaluate makes.
        Vector3d at = PositionOf(_rig.Skeleton.Parents[bone]);
        Vector3f step = RagdollSolver.Rotate(
            BonePose.ToMatrix(WorldOf(_rig.Skeleton.Parents[bone])), _rig.Skeleton.RestTranslations[bone]);
        return at + new Vector3d(step.X, step.Y, step.Z);
    }

    /// <summary>Gets which way a bone is facing, in world space.</summary>
    /// <param name="bone">The bone index, simulated or carried.</param>
    /// <returns>The bone's world orientation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bone"/> is not a bone of this rig.</exception>
    public Quaternion32 OrientationOf(int bone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bone);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bone, _boneCount);
        return WorldOf(bone);
    }

    /// <summary>Gets one body's capsule in the world.</summary>
    /// <param name="body">The body index; <see cref="BodyCount"/> is the carried body when there is one.</param>
    /// <param name="from">One end of the capsule's straight section, in engine units.</param>
    /// <param name="to">The other.</param>
    /// <param name="radius">Its radius, in engine units.</param>
    public void CapsuleOf(int body, out Vector3d from, out Vector3d to, out double radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(body);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(body, _position.Length);
        Vector3d along = Turned(body, _aim[body]) * _halfLength[body];
        from = _position[body] - along;
        to = _position[body] + along;
        radius = _radius[body];
    }

    /// <summary>Finds the body whose capsule passes nearest a point.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <param name="centre">Receives that body's centre of mass.</param>
    /// <returns>The body index; never the carried body.</returns>
    public int NearestBody(Vector3d point, out Vector3d centre)
    {
        int best = 0;
        double nearest = double.MaxValue;
        for (int body = 0; body < _bodyCount; body++)
        {
            CapsuleOf(body, out Vector3d from, out Vector3d to, out double radius);
            double gap = Vector3d.Distance(Vector3d.ClosestOnSegment(from, to, point), point) - radius;
            if (gap < nearest)
            {
                nearest = gap;
                best = body;
            }
        }

        centre = _position[best];
        return best;
    }

    /// <summary>Starts a ragdoll from the pose a body was drawn in when it died.</summary>
    /// <param name="rig">The prepared rig.</param>
    /// <param name="pose">
    /// The last animated pose, in the body's own model space — what <c>SoldierView.UnrotatedPose()</c>
    /// yields.
    /// </param>
    /// <param name="origin">Where that model space sits in the world, in engine units.</param>
    /// <param name="orientation">The body's heading, applied to the pose to reach world space.</param>
    /// <param name="attachment">
    /// Something the body is carrying — a weapon — as a rigid body of its own, or <see langword="null"/>
    /// for empty hands. It is placed at <paramref name="host"/>'s transform and jointed to the body that
    /// moves it.
    /// </param>
    /// <param name="host">Which bone holds the attachment.</param>
    /// <returns>The seeded body, with no velocity.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The pose does not match the rig.</exception>
    /// <remarks>
    /// <para>
    /// Seeding from the drawn pose rather than from a bind pose is what makes the takeover invisible: the
    /// first simulated frame is the frame that was already on screen.
    /// </para>
    /// <para>
    /// It is also where each joint's real anchor is found. A body can carry a bone between itself and the
    /// body below it — the abdomen carries the upper spine the chest hangs from — and where that joint
    /// sits depends on how the carried bone was turned, which only the pose knows.
    /// </para>
    /// </remarks>
    public static RagdollBody Seed(
        RagdollSkeleton rig,
        BonePose pose,
        Vector3d origin,
        Matrix3f orientation,
        RagdollBone? attachment = null,
        int host = 0)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(pose);
        if (pose.BoneCount != rig.BoneCount)
        {
            throw new ArgumentException(
                $"The rig has {rig.BoneCount} bones but the pose has {pose.BoneCount}.",
                nameof(pose));
        }

        if (attachment is not null && (host < 0 || host >= rig.BoneCount))
        {
            attachment = null;
        }

        BoneSkeleton skeleton = rig.Skeleton;
        int bones = rig.BoneCount;
        Matrix3f[] turned = new Matrix3f[bones];
        Vector3f[] placed = new Vector3f[bones];
        Quaternion32[] boneLocal = new Quaternion32[bones];
        for (int bone = 0; bone < bones; bone++)
        {
            turned[bone] = orientation * pose.Rotations[bone];
            placed[bone] = RagdollSolver.Rotate(orientation, pose.Positions[bone]);

            int parent = skeleton.Parents[bone];
            boneLocal[bone] = parent == BoneSkeleton.NoParent
                ? RagdollPose.QuaternionOf(turned[bone]).Normalized()
                : RagdollPose.QuaternionOf(turned[parent].Transposed() * turned[bone]).Normalized();
        }

        int count = rig.BodyCount + (attachment is null ? 0 : 1);
        Vector3d[] position = new Vector3d[count];
        Quaternion32[] rotation = new Quaternion32[count];
        for (int body = 0; body < rig.BodyCount; body++)
        {
            int bone = rig.BoneOf(body);
            rotation[body] = RagdollPose.QuaternionOf(turned[bone]).Normalized();
            Vector3f centre = RagdollSolver.Rotate(turned[bone], rig.BodyAt(body).Centre);
            position[body] = origin + new Vector3d(
                placed[bone].X + centre.X, placed[bone].Y + centre.Y, placed[bone].Z + centre.Z);
        }

        List<RagdollJoint> links = new(rig.Joints.Count + 1);
        List<Quaternion32> offsets = new(rig.Joints.Count + 1);
        foreach (RagdollJoint joint in rig.Joints)
        {
            int child = rig.BoneOf(joint.Child);
            (RagdollJoint seeded, Quaternion32 offset) = Anchor(
                rig, joint, joint.Parent, skeleton.Parents[child], placed[child], turned, placed);
            links.Add(seeded);
            offsets.Add(offset);
        }

        if (attachment is { } carried)
        {
            // A weapon is drawn AT its hand bone's transform, so that is where its body starts -- offset
            // only by its own centre of mass, which a bone's origin is never at.
            int slot = rig.BodyCount;
            rotation[slot] = RagdollPose.QuaternionOf(turned[host]).Normalized();
            Vector3f middle = RagdollSolver.Rotate(turned[host], carried.Centre);
            position[slot] = origin + new Vector3d(
                placed[host].X + middle.X, placed[host].Y + middle.Y, placed[host].Z + middle.Z);

            RagdollJoint grip = new(
                rig.BodyOf(host),
                slot,
                Vector3f.Zero,
                -carried.Centre,
                carried.Aim,
                RagdollWeapon.GripAllowance,
                -RagdollWeapon.GripAllowance,
                RagdollWeapon.GripAllowance,
                RagdollJointKind.Ball,
                carried.Aim);
            (RagdollJoint seeded, Quaternion32 offset) = Anchor(
                rig, grip, grip.Parent, host, placed[host], turned, placed);
            links.Add(seeded);
            offsets.Add(offset);
        }

        return new RagdollBody(
            rig, origin, position, rotation, boneLocal, [.. links], [.. offsets], attachment);
    }

    // A joint's parent anchor and joint frame, from the pose: where the joint sits in the parent body's
    // frame, and how the bone the child actually hangs from is turned relative to that body.
    private static (RagdollJoint Joint, Quaternion32 Offset) Anchor(
        RagdollSkeleton rig,
        RagdollJoint joint,
        int parentBody,
        int frameBone,
        Vector3f at,
        Matrix3f[] turned,
        Vector3f[] placed)
    {
        int parentBone = rig.BoneOf(parentBody);
        Matrix3f back = turned[parentBone].Transposed();
        Vector3f anchor = RagdollSolver.Rotate(back, at - placed[parentBone]) - rig.BodyAt(parentBody).Centre;
        Quaternion32 offset = frameBone == parentBone
            ? Quaternion32.Identity
            : RagdollPose.QuaternionOf(back * turned[frameBone]).Normalized();
        return (joint with { ParentAnchor = anchor }, offset);
    }

    /// <summary>Builds the pose the renderer should draw.</summary>
    /// <returns>The pose, relative to <see cref="Origin"/> with no heading applied.</returns>
    /// <remarks>
    /// <b>A direct read, with no fitting and no approximation.</b> Every simulated bone carries its own
    /// orientation, and every carried bone the turn it died with, so the drawn pose is what the solver
    /// produced. Because the pose is rebuilt from rotations and the rig's own bone lengths, a limb can
    /// never be drawn stretched, whatever the joints are doing.
    /// </remarks>
    public BonePose Pose()
    {
        Array.Clear(_worldDone);
        for (int bone = 0; bone < _boneCount; bone++)
        {
            int parent = _rig.Skeleton.Parents[bone];
            Quaternion32 above = parent == BoneSkeleton.NoParent ? Quaternion32.Identity : WorldOf(parent);
            _local[bone] = (above.Conjugate() * WorldOf(bone)).Normalized();
        }

        Vector3d root = PositionOf(0) - Origin;
        return BonePose.Evaluate(
            _rig.Skeleton,
            _local,
            new Vector3f((float)root.X, (float)root.Y, (float)root.Z));
    }

    // A bone's world orientation: its body's when it is simulated, its parent's turned by the local it
    // died with when it is carried. Memoized per pose, because a carried chain is walked once per bone.
    private Quaternion32 WorldOf(int bone)
    {
        int body = _rig.BodyOf(bone);
        if (_rig.BoneOf(body) == bone)
        {
            return _orientation[body];
        }

        if (_worldDone[bone])
        {
            return _world[bone];
        }

        Quaternion32 world = (WorldOf(_rig.Skeleton.Parents[bone]) * _boneLocal[bone]).Normalized();
        _world[bone] = world;
        _worldDone[bone] = true;
        return world;
    }
}
