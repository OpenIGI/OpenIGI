using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The soldiers a cutscene stages: the moving ground each rides (<c>Human_GroundCollision</c>'s <c>Human_SetParent</c>),
/// and the pose an EditCamera link or target reads off a human task.
/// </summary>
/// <remarks>
/// The attachment itself is the cutscene runtime's; this decides when a soldier asks for one -- on its graph's carrier,
/// on the platform under its feet -- and when an attachment is left alone.
/// </remarks>
/// <param name="context">The soldiers, the collision index and the cutscene runtime.</param>
internal sealed class SoldierCutsceneStaging(SoldierContext context)
{
    /// <summary>
    /// Retail IGI.exe Human_GroundCollision raises a standing human's task position by 1638.4
    /// engine units before subtracting the animated root height and querying the supporting surface.
    /// </summary>
    private const double HumanGroundCheckLiftInUnits = 1638.4;

    /// <summary>
    /// Retail Human_GroundCollision supplies -40960.0 as the first distance to
    /// World_GetNextZSpecificMesh. This is deliberately deeper than the normal two-metre
    /// placement probe so a staged cutscene actor can acquire a vehicle below its task origin.
    /// </summary>
    private const double HumanGroundProbeDownInUnits = 40960.0;

    private readonly SoldierContext _context = context;

    /// <summary>Asks the moving ground under a staged actor to carry it, as <c>Human_GroundCollision</c> does.</summary>
    /// <param name="view">The actor.</param>
    /// <param name="probePosition">Where its feet are, in world units.</param>
    /// <param name="attachmentPosition">The origin the first <c>Human_SetParent</c> carries, when it differs.</param>
    /// <param name="mask">What the ground probe may meet.</param>
    /// <param name="includeInactiveOwners">Whether a dormant container's object may be met.</param>
    public void AttachCutsceneActorToMovingGround(
        SoldierView view,
        Vector3d probePosition,
        Vector3d? attachmentPosition = null,
        WorldTraceMask mask = WorldTraceMask.DynamicObjects,
        bool includeInactiveOwners = false)
    {
        if (_context.Solids is not { } solids || _context.Cutscenes is not { } cutscenes)
        {
            return;
        }

        // A moving owner that leaves its conditional container is the native
        // Human_GroundCollision inactive-owner branch (0x463550): clear the parent slot first. An
        // empty query is intentionally different and leaves an existing parent in place, so this
        // check is based only on the owner's live registration state.
        if (cutscenes.TryDetachInactiveAttachment(view.Soldier))
        {
            return;
        }

        // An AI-driven Human never casts for its ground: 0x462390's AI branch reads the node under it from
        // its graph and, when that graph is authored in a placed model's frame (AIGraph +100) and the Human
        // has no parent yet, calls Human_SetParent (0x463310) with the graph's owner -- registered or not.
        // Mission 1's 1666 stands on wagon 1888's graph from load and rides it through the landing scene;
        // the ray below could not see that wagon, whose collision is still static until its train moves.
        if (GraphCarrierOf(view) is { } carrier)
        {
            if (!cutscenes.TryGetAttachedDrawTransform(view.Soldier, out _, out _))
            {
                cutscenes.TryAttachActorToTask(view.Soldier, carrier, view.Soldier.Position, view.Orientation);
            }

            return;
        }

        if (cutscenes.TryGetAttachedDrawTransform(view.Soldier, out _, out _))
        {
            // Retail 0x4626E8 only calls Human_SetParent when Human+1900 is null. Once attached,
            // the parent owns movement until a separate detach path clears it; another ground ray
            // must not silently recapture or switch the local frame.
            return;
        }

        // Retail IGI.exe 0x4623C3 samples BoneDynCubeObj's animated root, then 0x462403 forms the
        // standing ground-check Z as Human.Position.Z + 1638.4 - RootSample.Z. Querying from the task origin
        // instead hits geometry above the feet (Mission 1's helicopter underside) and parents Jones
        // to the wrong vehicle. DynamicObject membership alone is NOT the moving-platform flag
        // tested at 0x4626E1: a hovering airframe is dynamic but never captures (its riders are
        // direct QVM children); only a platform contact reaches Human_SetParent at 0x463310.
        double checkZ = probePosition.Z + HumanGroundCheckLiftInUnits - view.RootSample.Z;
        WorldTraceRequest request = new(
            new Vector3d(probePosition.X, probePosition.Y, checkZ),
            new Vector3d(probePosition.X, probePosition.Y, checkZ - HumanGroundProbeDownInUnits),
            mask,
            IgnoredOwner: view.Soldier,
            IncludeInactiveOwners: includeInactiveOwners);
        if (solids.TryTrace(request, out WorldTraceHit hit)
            && hit.Owner is { } parent
            && CinematicActor.IsMovingPlatform(parent))
        {
            // The retail probe selects the supporting task from the live foot query, but the first
            // Human_SetParent packet still contains the staged Human+32. Our actor mover may have
            // consumed a root delta while the vehicle's conditional container was dormant, so keep
            // those two inputs distinct instead of baking that scheduling error into the local frame.
            cutscenes.TryAttachActorToTask(
                view.Soldier,
                parent,
                attachmentPosition ?? probePosition,
                view.Orientation);
        }
    }

    /// <summary>Un-brained cutscene actors ride their vehicles, as the brained ones do when they update.</summary>
    public void AttachStagedActorsToVehicles()
    {
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (member.IsCutsceneActor && !_context.Brains.ContainsKey(member.View))
                {
                    AttachStagedActor(member);
                }
            }
        }
    }

    /// <summary>Retries a live cutscene actor's attachment to the moving ground under it.</summary>
    public void AttachStagedActor(SoldierAgent member)
    {
        if (_context.Solids is null
            || _context.Cutscenes is null
            || !ConditionalContainerGate.IsTaskActive(member.View.Soldier))
        {
            return;
        }

        AttachCutsceneActorToMovingGround(
            member.View,
            member.Position * WorldScene.UnitsPerMetre,
            attachmentPosition: null,
            mask: WorldTraceMask.DynamicObjects,
            includeInactiveOwners: false);
    }

    /// <summary>Resolves a human task's live pose for an EditCamera link or target.</summary>
    public CutsceneTargetPose? CutscenePose(int taskId)
    {
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                if (member.View.Soldier.Id == taskId && ConditionalContainerGate.IsTaskActive(member.View.Soldier))
                {
                    if (_context.Cutscenes?.TryGetAttachedDrawTransform(
                        member.View.Soldier, out Vector3d attached, out Matrix3f attachedOrientation) == true)
                    {
                        return new CutsceneTargetPose(
                            attached * WorldScene.UnitsPerMetre,
                            attachedOrientation);
                    }

                    if (member.IsCutsceneActor)
                    {
                        // Grounded on its graph, as TickPlacementOf draws it.
                        if (!member.HasCutsceneGameplayMovement
                            && _context.Brains.TryGetValue(member.View, out Igi1HumanAi? stagedBrain) && stagedBrain.HasGraph && stagedBrain.HasGroundQuery
                            && !stagedBrain.GraphInParentFrame && stagedBrain.StoodOnGraph)
                        {
                            return new CutsceneTargetPose(
                                member.View.Position + new Vector3d(0d, 0d, member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneGameplayMovement)
                        {
                            double groundedZ = _context.SnapToGround(
                                member.View.Position.X,
                                member.View.Position.Y,
                                member.View.Position.Z);
                            return new CutsceneTargetPose(
                                new Vector3d(
                                    member.View.Position.X,
                                    member.View.Position.Y,
                                    groundedZ + member.View.RootSample.Z),
                                member.View.Orientation);
                        }

                        if (member.HasCutsceneRootMotion)
                        {
                            return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                        }

                        // A stationary staged task retains its authored model origin. Native
                        // 0x462390 derives feet by subtracting the bone/root offset from that origin;
                        // it does not treat the authored origin itself as a floor height.
                        HumanSoldierTask staged = member.View.Soldier;
                        return new CutsceneTargetPose(
                            staged.Position,
                            Matrix3f.FromEngineEulerAngles(0f, 0f, staged.Gamma));
                    }
                    return new CutsceneTargetPose(member.View.Position, member.View.Orientation);
                }
            }
        }

        return null;
    }

    /// <summary>The vehicle an AI actor's own graph is authored on, which the original parents it to.</summary>
    private GameTask? GraphCarrierOf(SoldierView view) =>
        _context.Brains.TryGetValue(view, out Igi1HumanAi? brain)
        && brain.GraphInParentFrame
        && SoldierContext.FindMind(view) is { GraphId: > 0 } mind
        && view.Soldier.Tree.FromId(mind.GraphId)?.Parent is { } owner
        && CinematicActor.IsVehicle(owner)
            ? owner
            : null;
}
