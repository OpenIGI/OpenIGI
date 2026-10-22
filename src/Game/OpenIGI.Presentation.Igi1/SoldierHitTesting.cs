using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Meets rays with the soldiers: the player's rounds, the developer pick, and the caches both read.</summary>
/// <remarks>
/// Every cast is at the body the crosshair was drawn over: with smoothing on, the presented transform and the
/// pair of skinned poses the device is blending, rather than the newer simulation state.
/// </remarks>
/// <param name="groups">The groups the renderer draws; shared, not copied.</param>
/// <param name="sim">The soldiers' simulation, which grounds their feet.</param>
/// <param name="frames">What says whether this frame is drawn between ticks.</param>
/// <param name="logger">Where an unusable collision mesh is reported.</param>
internal sealed class SoldierHitTesting(
    List<SoldierGroup> groups,
    SoldierSimulation sim,
    SoldierFrameInterpolator frames,
    IOpenIgiLogger logger)
{
    private readonly List<SoldierGroup> _groups = groups;
    private readonly SoldierSimulation _sim = sim;
    private readonly SoldierFrameInterpolator _frames = frames;
    private readonly IOpenIgiLogger _logger = logger;

    /// <summary>Primes immutable hit-test and corpse-rig data before the first round can arrive.</summary>
    /// <remarks>
    /// Collision geometry and ragdoll rigs are shared by every member of a group. They used to be
    /// created by the first shot/death that touched a group, which made a normal first kill look like a
    /// render stall. A malformed optional collision chunk still falls back to the cylinder path; it must
    /// not make mission loading fail.
    /// </remarks>
    public void WarmHitTestCaches()
    {
        int collisionMeshes = 0;
        int ragdollRigs = 0;
        foreach (SoldierGroup group in _groups)
        {
            try
            {
                if (group.Body.CollisionMesh is not null)
                {
                    collisionMeshes++;
                }
            }
            catch (PackFormatException ex)
            {
                _logger.Warning(SoldierRenderer.LogCategory, $"A soldier collision mesh was not usable: {ex.Message}");
            }

            if (!GameplayCVars.Ragdoll.Value || group.Body.Model is null || group.Soldiers.Count == 0)
            {
                continue;
            }

            if (_sim.RagdollRigFor(group.Soldiers[0].Agent.View.Bones.Set, group.Body.Model) is not null)
            {
                ragdollRigs++;
            }
        }

        _logger.Information(
            SoldierRenderer.LogCategory,
            $"Prepared {collisionMeshes} soldier collision mesh(es) and {ragdollRigs} corpse rig(s) before play.");
    }

    /// <summary>Finds the nearest soldier a ray from the eye meets, for the developer pick.</summary>
    /// <param name="originUnits">Where the ray starts, in world units.</param>
    /// <param name="direction">Which way it points; need not be unit length.</param>
    /// <param name="reachUnits">How far it reaches.</param>
    /// <param name="name">Receives the soldier's model name.</param>
    /// <param name="distanceUnits">Receives how far along the ray it was met.</param>
    /// <param name="feetUnits">Receives where its feet stand.</param>
    /// <param name="zone">Receives the hit zone the ray met.</param>
    /// <returns><see langword="true"/> when a soldier was met.</returns>
    public bool TryPick(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        out string name,
        out double distanceUnits,
        out Vector3d feetUnits,
        out int zone)
    {
        name = string.Empty;
        distanceUnits = double.MaxValue;
        feetUnits = default;
        zone = -1;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        Vector3d end = new(
            originUnits.X + (dir.X * reachUnits),
            originUnits.Y + (dir.Y * reachUnits),
            originUnits.Z + (dir.Z * reachUnits));
        bool found = false;

        foreach (SoldierGroup group in _groups)
        {
            if (group.Body.CollisionMesh is not { } mesh)
            {
                continue;
            }

            foreach (SoldierMember member in group.Soldiers)
            {
                SoldierView view = member.Agent.View;
                if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
                {
                    continue;
                }
                double snappedZ = _sim.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                Vector3d feet = new(view.Position.X, view.Position.Y, snappedZ);

                // Broad phase: a generous body sphere, so a ray nowhere near a soldier never touches its mesh.
                Vector3d centre = new(feet.X, feet.Y, feet.Z + SoldierSimulation.PickChestUnits);
                if (!SoldierSimulation.RaySphere(originUnits, dir, centre, SoldierSimulation.PickCullRadiusUnits, reachUnits, out _))
                {
                    continue;
                }

                Matrix3f m = view.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                if (HumanHitTest.TryCastMesh(
                        originUnits, end, feet, facing, mesh, view.Pose(), view.RootSample.Z, out HumanHit h)
                    && h.Distance < distanceUnits)
                {
                    distanceUnits = h.Distance;
                    name = group.Instance?.Name ?? "soldier";
                    feetUnits = feet;
                    zone = h.Zone;
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Every living, active soldier, as the shots find it.</summary>
    /// <returns>The bodies, each made the first time one looks.</returns>
    public IEnumerable<IShootableBody> Bodies()
    {
        foreach (SoldierGroup group in _groups)
        {
            foreach (SoldierMember member in group.Soldiers)
            {
                SoldierView soldier = member.Agent.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier) || _sim.HealthOf(soldier).IsDead)
                {
                    continue;
                }

                yield return member.Body ??= new SoldierMemberBody(this, group, member);
            }
        }
    }

    // One soldier's colliders as the player's round meets them, in the pose it was drawn in.
    internal bool TryCastMember(SoldierGroup group, SoldierMember member, Vector3d origin, Vector3d rayEnd, out HumanHit candidate)
    {
        SoldierView soldier = member.Agent.View;
        Vector3d ray = new(rayEnd.X - origin.X, rayEnd.Y - origin.Y, rayEnd.Z - origin.Z);
        double reach = Math.Sqrt((ray.X * ray.X) + (ray.Y * ray.Y) + (ray.Z * ray.Z));
        Vector3d direction = reach > 0.0
            ? new Vector3d(ray.X / reach, ray.Y / reach, ray.Z / reach)
            : default;

        // The player fires against the body the crosshair was drawn over. With r_smooth that is
        // the last presentation transform, not the newer simulation state the next tick has just
        // published. AI fire stays on its own fixed-tick path below: this is only the player-shot
        // delegate supplied by Shooter().
        bool presented = _frames.FrameSmooth && member.HasFrameState;
        Vector3d feet;
        Matrix3f m;
        if (presented)
        {
            Vector3d root = member.DrawnWhere * WorldScene.UnitsPerMetre;
            feet = new Vector3d(root.X, root.Y, root.Z - soldier.RootSample.Z);
            m = member.DrawnOrientation;
        }
        else
        {
            // r_smooth off is the vanilla simulation transform verbatim. The feet the round is
            // cast at must be the SAME feet the body is drawn on — grounded onto the floor — and
            // the bones sit a root height above them, exactly as the draw lifts the pelvis.
            double snappedZ = _sim.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
            feet = new Vector3d(soldier.Position.X, soldier.Position.Y, snappedZ);
            m = soldier.Orientation;
        }
        Vector3f facing = new(m.M12, m.M22, m.M32);

        // The faithful path: trace the round at the soldier's skinned collision mesh and score it by
        // the bone the round actually went through (BoneDynCubeObj_TraceLinesHandler). Skinning every
        // collision vertex for every guard is far costlier than the ray itself, so reject bodies the
        // segment cannot reach with the same generous body sphere TryPick uses. Starting inside the
        // sphere still reaches the mesh, which preserves point-blank hits even when the round ends
        // before it leaves the broad-phase sphere. Only if the model ships no collision mesh does it
        // fall back to the cylinder-and-nearest-bone stand-in.
        bool met;
        if (group.Body.CollisionMesh is { } collision)
        {
            Vector3d centre = new(feet.X, feet.Y, feet.Z + SoldierSimulation.PickChestUnits);
            double offsetX = origin.X - centre.X;
            double offsetY = origin.Y - centre.Y;
            double offsetZ = origin.Z - centre.Z;
            bool startsInside = (offsetX * offsetX) + (offsetY * offsetY) + (offsetZ * offsetZ)
                <= SoldierSimulation.PickCullRadiusUnits * SoldierSimulation.PickCullRadiusUnits;
            if (reach <= 0.0 || (!startsInside
                && !SoldierSimulation.RaySphere(origin, direction, centre, SoldierSimulation.PickCullRadiusUnits, reach, out _)))
            {
                candidate = default;
                return false;
            }

            if (presented
                && member.SkinnedPose is { } currentPose
                && member.PreviousSkinnedPose is { } previousPose)
            {
                // The renderer already owns both poses used by the motion-blended draw. Reuse
                // them directly; do not re-evaluate the animation for a candidate that has
                // already survived the broad phase.
                met = HumanHitTest.TryCastMeshBlended(
                    origin,
                    rayEnd,
                    member.DrawnWhere * WorldScene.UnitsPerMetre,
                    facing,
                    collision,
                    currentPose,
                    previousPose,
                    member.DrawnOrientation,
                    member.PoseBlend,
                    out candidate);
            }
            else
            {
                // Pose evaluation (especially a presented-orientation re-evaluation) skins the
                // complete rig. Keep it behind the ray/sphere rejection so off-ray soldiers do
                // not pay that cost on every scoped shot.
                BonePose pose = presented
                    ? SoldierSimulation.PoseAtPresentationOrientation(soldier, m)
                    : soldier.Pose();
                met = HumanHitTest.TryCastMesh(
                    origin, rayEnd, feet, facing, collision, pose, soldier.RootSample.Z, out candidate);
            }
        }
        else
        {
            // The cylinder fallback still needs a pose for bone-zone refinement, but only after
            // there is no collision mesh broad phase to skip.
            BonePose pose = presented
                ? SoldierSimulation.PoseAtPresentationOrientation(soldier, m)
                : soldier.Pose();
            met = HumanHitTest.TryCast(
                origin, rayEnd, feet, facing, pose, out candidate, soldier.RootSample.Z);
        }

        return met;
    }
}
