using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>The bone debug view: where every nearby soldier's bones are drawn, and what a ragdoll of him collides with.</summary>
public sealed partial class SoldierRenderer
{
    /// <inheritdoc/>
    public void CollectBoneMarks(Vector3d eye, double range, bool capsules, List<BoneDebugMark> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                if (!member.HasFrameState
                    || member.DrawnPose is not { } pose
                    || Vector3d.Distance(member.DrawnWhere, eye) > range)
                {
                    continue;
                }

                BoneSkeleton skeleton = member.Agent.View.Bones.Set.Skeleton;
                if (pose.BoneCount != skeleton.BoneCount)
                {
                    continue;
                }

                bool human = HumanRagdollProfile.Matches(skeleton);
                for (int bone = 0; bone < pose.BoneCount; bone++)
                {
                    int parent = skeleton.Parents[bone];
                    Vector3d at = Placed(member, pose.Positions[bone]);
                    into.Add(new BoneDebugMark(
                        at,
                        parent == BoneSkeleton.NoParent ? at : Placed(member, pose.Positions[parent]),
                        0.0,
                        human ? $"{bone} {HumanRagdollProfile.NameOf(bone)}" : $"{bone}"));
                }

                if (capsules)
                {
                    AddCapsules(group, member, pose, into);
                }
            }
        }
    }

    // A corpse's capsules where the solver has them; a living soldier's where the rig would put them for
    // the pose he is drawn in, so a rig's shapes can be checked against its skin before anyone dies.
    private void AddCapsules(SoldierGroup group, SoldierMember member, BonePose pose, List<BoneDebugMark> into)
    {
        const double Metre = WorldScene.UnitsPerMetre;
        if (member.Ragdoll is { } corpse)
        {
            int bodies = corpse.BodyCount + (corpse.HasAttachment ? 1 : 0);
            for (int body = 0; body < bodies; body++)
            {
                corpse.CapsuleOf(body, out Vector3d from, out Vector3d to, out double radius);
                into.Add(new BoneDebugMark(from / Metre, to / Metre, radius / Metre, null));
            }

            return;
        }

        if (_sim.RagdollRigFor(member.Agent.View.Bones.Set, group.Body.Model) is not { } rig)
        {
            return;
        }

        for (int body = 0; body < rig.BodyCount; body++)
        {
            RagdollBone shape = rig.BodyAt(body);
            int bone = rig.BoneOf(body);
            Matrix3f turn = pose.Rotations[bone];
            Vector3f middle = pose.Positions[bone] + RagdollSolver.Rotate(turn, shape.Centre);
            Vector3f along = RagdollSolver.Rotate(turn, shape.Aim) * shape.HalfLength;
            into.Add(new BoneDebugMark(
                Placed(member, middle - along),
                Placed(member, middle + along),
                shape.Radius / Metre,
                null));
        }
    }

    // A point in a soldier's model space, in world metres: where his body is drawn.
    private static Vector3d Placed(SoldierMember member, Vector3f modelUnits)
    {
        Vector3f turned = RagdollSolver.Rotate(member.DrawnOrientation, modelUnits);
        double scale = 1.0 / WorldScene.UnitsPerMetre;
        return member.DrawnWhere + new Vector3d(turned.X * scale, turned.Y * scale, turned.Z * scale);
    }
}
