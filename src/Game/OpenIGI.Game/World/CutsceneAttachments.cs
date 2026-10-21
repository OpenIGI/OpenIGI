using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using static OpenIGI.Game.World.CutsceneTaskValues;
using static OpenIGI.Game.World.CutscenePoses;

namespace OpenIGI.Game.World;

/// <summary>Parents tasks to the moving vehicles and platforms they ride, and poses them on their parents.</summary>
/// <remarks>
/// A cinematic Human or a vehicle's crew is welded to its parent the way <c>Human_SetParent</c> (<c>0x463310</c>) welds
/// it: the parent's frame at the moment of parenting is kept, and every read rebuilds the rider from the parent's
/// pose at the rider's own slot in the level walk.
/// </remarks>
internal sealed class CutsceneAttachments(CutsceneContext context)
{
    private readonly CutsceneContext _context = context;
    private readonly Dictionary<GameTask, CutsceneAttachmentState> _attachments = [];

    /// <summary>Gets what each attached task rides, by task.</summary>
    public IReadOnlyDictionary<GameTask, CutsceneAttachmentState> All => _attachments;

    /// <summary>Finds what a task rides, if anything.</summary>
    public bool TryGet(GameTask task, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CutsceneAttachmentState? attachment) =>
        _attachments.TryGetValue(task, out attachment);

    /// <summary>Resolves a cinematic actor staged inside a moving vehicle.</summary>
    public bool TryGetAttachedDrawTransform(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (!TryEvaluateAttachment(task, out Vector3d attachedPosition, out orientation))
        {
            position = default;
            return false;
        }

        position = attachedPosition / WorldScene.UnitsPerMetre;
        return true;
    }

    /// <summary>Parents a cinematic Human to the moving vehicle surface it has just landed on.</summary>
    /// <param name="task">The Human task whose live transform is being parented.</param>
    /// <param name="parent">The exact placed vehicle task returned by the ground collision.</param>
    /// <param name="position">The Human's current world position, in engine units.</param>
    /// <param name="orientation">The Human's current world orientation.</param>
    /// <returns><see langword="true"/> when the parent has a live cutscene transform.</returns>
    /// <remarks>
    /// Retail's Human ground pass tests the struck collision task's moving-platform flag and parents
    /// the Human to that exact task. The aligned native helper at <c>0x463310</c> writes the parent
    /// pointer and the Human's local position/orientation frame; its real one-block boundary is
    /// confirmed in both Ghidra and r2.
    /// The paired detach helper at <c>0x463550</c> clears <c>Human+0x76c</c> when the ground pass has
    /// no valid moving contact. The task-tree ancestry of the Human is unrelated: Mission 1 actor
    /// 1666 is a sibling of train car 1888, not its child.
    /// </remarks>
    public bool TryAttachActorToTask(
        GameTask task,
        GameTask parent,
        Vector3d position,
        Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(parent);

        if (!(IsVehicle(parent) || CinematicActor.IsMovingPlatform(parent))
            || !_context.TaskTransforms.TryGetValue(parent, out CutsceneTransformState? parentTransform))
        {
            return false;
        }

        if (_attachments.TryGetValue(task, out CutsceneAttachmentState? existing)
            && ReferenceEquals(existing.Parent, parent))
        {
            return true;
        }

        if (!_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state))
        {
            state = new CutsceneTransformState(position, orientation);
            _context.Remember(task, state);
        }
        else
        {
            state.Position = position;
            state.Orientation = orientation;
        }

        // A Human ahead of its car in the tree runs its ground pass before the car moves, so it is parented to
        // the car where it stood: mission 12's gondola rider is attached (0x463310) against the station frame,
        // a degree round, before the car's first step turns it to the cable's two -- and so he turns with it.
        // The host has already stepped the lifts by now, so that is the pose before this tick's step.
        bool beforeCar = string.Equals(parent.Type.Name, "Elevator", StringComparison.Ordinal)
            && _context.TaskOrder.TryGetValue(task, out int humanOrder)
            && humanOrder < parentTransform.LastSimulationOrder;
        return CaptureAttachment(task, state, parent, parentTransform, beforeCar);
    }

    /// <summary>Holds a platform this runtime does not move -- an <c>Elevator</c> -- where its own registry put it.</summary>
    /// <param name="platform">The platform task.</param>
    /// <param name="position">Where it is, in engine units.</param>
    /// <param name="orientation">How it is turned.</param>
    /// <remarks>
    /// A staged Human standing in a car is parented to it like one standing on a train (<c>Human_SetParent</c>,
    /// <c>0x463310</c>, from the ground pass): mission 12's intro sets a soldier in the cutscene gondola at load
    /// and the original carries him up the cable with it. The car's own mover is the elevator registry's, so its
    /// pose is handed in here each tick, last tick's kept as the previous one, for the attachment to read.
    /// </remarks>
    public void SetPlatformPose(GameTask platform, Vector3d position, Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(platform);
        int order = _context.TaskOrder.TryGetValue(platform, out int placed) ? placed : int.MaxValue;
        if (!_context.TaskTransforms.TryGetValue(platform, out CutsceneTransformState? state))
        {
            _context.Remember(platform, new CutsceneTransformState(position, orientation) { LastSimulationOrder = order });
            return;
        }

        SnapshotDynamicPose(state, order);
        state.Position = position;
        state.Orientation = orientation;
    }

    /// <summary>Reads the moving parent a cinematic Human is currently attached to, if any.</summary>
    /// <remarks>
    /// Observation only: it never selects or changes the parent. Regressions use it to assert
    /// the live contact selection without supplying an expected owner first.
    /// </remarks>
    public bool TryGetAttachmentParent(GameTask task, out GameTask? parent)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_attachments.TryGetValue(task, out CutsceneAttachmentState? attachment))
        {
            parent = attachment.ParentTask;
            return true;
        }

        parent = null;
        return false;
    }

    /// <summary>
    /// Clears a cinematic Human's moving-parent slot when its contacted owner has been unregistered.
    /// </summary>
    /// <remarks>
    /// Native <c>Human_GroundCollision</c> calls the aligned detach helper at <c>0x463550</c> when a
    /// candidate owner is no longer active; an empty query only clears support fields and deliberately
    /// retains an existing parent. The latter distinction matters during the Level 1 helicopter-to-train
    /// handoff, so this method checks only the parent's registration state and never guesses a replacement.
    /// </remarks>
    public bool TryDetachInactiveAttachment(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!_attachments.TryGetValue(task, out CutsceneAttachmentState? attachment)
            || ConditionalContainerGate.IsTaskActive(attachment.ParentTask))
        {
            return false;
        }

        _attachments.Remove(task);
        return true;
    }

    /// <summary>Consumes one Human root-motion delta while the Human is standing on a moving parent.</summary>
    /// <remarks>
    /// Every retail Human state first rotates <c>Human+1312</c> through the Human orientation (for
    /// example <c>0x4551A4</c>). <c>Human_AttachedMovement</c> (<c>0x462CF0</c>) then converts that
    /// world-space delta into the parent's local frame and accumulates it at <c>Human+1904</c>.
    /// Keeping both transforms in this order is what makes an actor follow its own facing while staying
    /// welded to a moving vehicle.
    /// </remarks>
    public bool AdvanceAttachedActor(GameTask task, Vector3f rootMotion)
    {
        if (!_attachments.TryGetValue(task, out CutsceneAttachmentState? attachment))
        {
            return false;
        }

        if (rootMotion.X == 0f && rootMotion.Y == 0f && rootMotion.Z == 0f)
        {
            return true;
        }

        TryEvaluateAttachment(task, out _, out Matrix3f humanOrientation);
        EvaluateAttachmentParentPose(task, attachment, out _, out Matrix3f parentOrientation);
        // Human's state tick (IGI.exe 0x4550B0) first turns the clip delta through Human+120,
        // producing Human+1312 in world axes. Human_AttachedMovement (0x462CF0, named Human_Attach
        // in the IGI2 symbols) then turns it through the parent's Human+112 frame into Human+1904.
        Vector3d worldDelta = humanOrientation.Transposed().Transform(new Vector3d(
            rootMotion.X, rootMotion.Y, rootMotion.Z));
        Vector3d localDelta = parentOrientation.Transform(worldDelta);
        attachment.RelativePosition += localDelta;
        return true;
    }

    /// <summary>Poses an attached task on its parent as this slot of the walk sees the parent, and stores the result.</summary>
    internal bool TryEvaluateAttachment(GameTask task, out Vector3d position, out Matrix3f orientation)
    {
        if (!_attachments.TryGetValue(task, out CutsceneAttachmentState? attachment))
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        EvaluateAttachmentParentPose(
            task,
            attachment,
            out Vector3d parentPosition,
            out Matrix3f parentOrientation);
        position = parentPosition
            + parentOrientation.Transposed().Transform(attachment.RelativePosition);
        // Human_SetParent (0x463310) keeps L = H0 * P0^T at Human+1928 -- the Human's frame times the
        // TRANSPOSED parent frame, in that order -- and every tick 0x4610C0 rebuilds the Human as P * L.
        // That is a conjugation, not a relative frame: a Human attached to a pitched parent leans at once.
        // Mission 1's 3031, attached to wagon 607_01_1 pitched 10.6 degrees down the hill, leans 21 degrees
        // there and 10.6 on the flat, and his climb-down clip's root steps are turned through that lean.
        orientation = parentOrientation
            * attachment.Orientation
            * attachment.ParentOriginOrientation.Transposed();
        if (_context.TaskTransforms.TryGetValue(task, out CutsceneTransformState? state))
        {
            state.Position = position;
            state.Orientation = orientation;
        }
        return true;
    }

    private void EvaluateAttachmentParentPose(
        GameTask task,
        CutsceneAttachmentState attachment,
        out Vector3d position,
        out Matrix3f orientation)
    {
        // Human_AttachedMovement reads its parent's pose at the Human's own task-tree slot. Our grouped
        // scheduler advances every AnimTask first, so reconstruct that slot: a Human preceding the
        // controlling AnimTask sees the saved pre-update pose, while one following it sees current.
        // Mission 1 contains both source-of-truth cases: wagon Human 2021 precedes train-1999's AnimTask;
        // helicopter Human 1505 follows heli-1502's AnimTask.
        CutsceneTransformState dynamicParent = attachment.Parent.TrainRoot ?? attachment.Parent;
        bool usePreviousPose = task is HumanSoldierTask
            && DynamicRevision(attachment.Parent) > attachment.ParentRevisionAtCapture
            && _context.TaskOrder.TryGetValue(task, out int humanOrder)
            && humanOrder < dynamicParent.LastSimulationOrder;
        EvaluateDynamicPose(attachment.Parent, usePreviousPose, out position, out orientation);
    }

    /// <summary>Parents a task to the nearest vehicle among its ancestors, when one has a transform.</summary>
    internal bool CaptureAttachment(GameTask task, CutsceneTransformState state)
    {
        for (GameTask? parent = task.Parent; parent is not null; parent = parent.Parent)
        {
            if (IsVehicle(parent)
                && _context.TaskTransforms.TryGetValue(parent, out CutsceneTransformState? parentTransform))
            {
                return CaptureAttachment(task, state, parent, parentTransform);
            }
        }
        return false;
    }

    /// <summary>Parents a task to a given parent in the frame the parent stands in now, or stood in before its step.</summary>
    internal bool CaptureAttachment(
        GameTask task,
        CutsceneTransformState state,
        GameTask parent,
        CutsceneTransformState parentTransform,
        bool beforeParentStep = false)
    {
        UpdateDerivedPose(parentTransform);
        CutsceneAttachmentState attachment = new(
            parent,
            parentTransform,
            DynamicRevision(parentTransform) - (beforeParentStep ? 1 : 0),
            beforeParentStep ? parentTransform.PreviousPosition : parentTransform.Position,
            beforeParentStep ? parentTransform.PreviousOrientation : parentTransform.Orientation,
            state.Position,
            state.Orientation);
        _attachments[task] = attachment;
        if (task is DeclaredTask attached
            && _context.VehiclePhysics?.TryGetRotor(
                Text(attached, WorldScene.ModelParameter), out RotorPhysicsDefinition rotor) == true)
        {
            if (rotor.ProducesLift)
            {
                parentTransform.RotorAxialCoefficient += 2.5f;
            }
            if (rotor.IsTailRotor)
            {
                parentTransform.RotorAxialCoefficient -= 2.5f;
            }
        }
        return true;
    }

    private static bool IsVehicle(GameTask task) => CinematicActor.IsVehicle(task);
}
