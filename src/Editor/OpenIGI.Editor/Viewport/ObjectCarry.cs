using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// What moves with an object when it is moved or turned: the objects placed under it in the mission, and the loose objects
/// inside it or standing on it, such as the furniture in a building or the guard on a water tower.
/// </summary>
/// <remarks>
/// The game keeps every placed object's position in world space, a building's own children included, so a building moved
/// alone leaves its contents where they stood. Moving them with it is what the person moving it means; the
/// <c>ed_move_contents</c> setting turns it off.
/// </remarks>
internal static class ObjectCarry
{
    // How far outside an object's box a point still counts as on it: a guard's feet stand on the roof, not inside it.
    private const double ReachMetres = 0.5;
    private const double StandMetres = 2.0;

    /// <summary>Gets the objects that move with some others.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="live">The drawn world, for the objects' boxes.</param>
    /// <param name="moving">The objects being moved.</param>
    /// <returns>The objects carried along, none of them among <paramref name="moving"/>.</returns>
    public static IReadOnlyList<MissionTask> Carried(MissionDocument document, LiveWorldLink live, IReadOnlyCollection<MissionTask> moving)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(moving);
        if (!EditorCVars.MoveContents.Value || moving.Count == 0)
        {
            return [];
        }

        HashSet<MissionTask> taken = new(moving, ReferenceEqualityComparer.Instance);
        HashSet<MissionTask> above = new(ReferenceEqualityComparer.Instance);
        List<MissionTask> carried = [];
        List<BoxGizmo.Box> boxes = [];
        double largest = 0.0;
        foreach (MissionTask task in moving)
        {
            for (MissionTask? parent = task.Parent; parent is not null; parent = parent.Parent)
            {
                above.Add(parent);
            }

            foreach (MissionTask child in task.SelfAndSubtree().Skip(1))
            {
                if (TaskPresentation.Position(child) is not null && !MissionGraphs.IsGraph(child) && taken.Add(child))
                {
                    carried.Add(child);
                }
            }

            foreach (BoxGizmo.Box box in live.Boxes(task))
            {
                boxes.Add(box);
                largest = Math.Max(largest, box.Half.X * box.Half.Y);
            }
        }

        if (boxes.Count == 0)
        {
            return carried;
        }

        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (taken.Contains(task) || above.Contains(task) || MissionGraphs.IsGraph(task)
                || TaskPresentation.PositionMetres(task) is not { } at || !boxes.Any(box => Holds(box, at)))
            {
                continue;
            }

            // Only what is smaller than the mover: a building moved on a hill does not take the hill.
            if (live.Boxes(task).Any(own => own.Half.X * own.Half.Y >= largest))
            {
                continue;
            }

            // And only what is not as much another's: a copy made where its original stands leaves the original's contents.
            if (HeldByAnother(document, live, task, at, taken, largest))
            {
                continue;
            }

            taken.Add(task);
            carried.Add(task);
            foreach (MissionTask child in task.SelfAndSubtree().Skip(1))
            {
                if (TaskPresentation.Position(child) is not null && taken.Add(child))
                {
                    carried.Add(child);
                }
            }
        }

        return carried;
    }

    /// <summary>Records where carried objects stand and how they face, before a turn starts.</summary>
    /// <param name="carried">The carried objects.</param>
    /// <returns>Each one's position in world metres and orientation, where it has them.</returns>
    public static IReadOnlyList<(MissionTask Task, Vector3d? Position, Matrix3f? Orientation)> Snapshot(IEnumerable<MissionTask> carried) =>
        [.. carried.Select(static t => (t, TaskPresentation.PositionMetres(t), TaskPresentation.Orientation(t) is null ? (Matrix3f?)null : TaskPresentation.OrientationMatrix(t)))];

    /// <summary>Makes the steps that turn carried objects with a turn of the object carrying them.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="snapshot">The carried objects as they were when the turn started.</param>
    /// <param name="pivot">The point the turn is about, in world metres.</param>
    /// <param name="turn">The world-space turn, from the start.</param>
    /// <returns>One position step for each, and one orientation step for each that has an orientation.</returns>
    public static IEnumerable<IEditorCommand> Turn(
        MissionDocument document,
        IEnumerable<(MissionTask Task, Vector3d? Position, Matrix3f? Orientation)> snapshot,
        Vector3d pivot,
        Matrix3f turn)
    {
        foreach ((MissionTask task, Vector3d? start, Matrix3f? facing) in snapshot)
        {
            if (TaskPresentation.Position(task) is { } position && start is { } at)
            {
                Vector3d to = pivot + turn.Transform(at - pivot);
                yield return SetParameterCommand.Numbers(document, task, position.Name, [WorldUnits.FromMetres(to.X), WorldUnits.FromMetres(to.Y), WorldUnits.FromMetres(to.Z)]);
            }

            if (TaskPresentation.Orientation(task) is { } orientation && facing is { } was)
            {
                Vector3f angles = (was * turn.Transposed()).ToEngineEulerAngles();
                yield return TaskPresentation.TurnsAboutUpOnly(task)
                    ? SetParameterCommand.Numbers(document, task, orientation.Name, [angles.Z])
                    : SetParameterCommand.Numbers(document, task, orientation.Name, [angles.X, angles.Y, angles.Z]);
            }
        }
    }

    // Whether an object that is not moving, and is at least as large as the mover, also holds the point.
    private static bool HeldByAnother(MissionDocument document, LiveWorldLink live, MissionTask candidate, Vector3d at, HashSet<MissionTask> moving, double largest)
    {
        foreach (MissionTask other in document.EnumerateTasks())
        {
            if (ReferenceEquals(other, candidate) || moving.Contains(other) || !live.IsLive(other))
            {
                continue;
            }

            foreach (BoxGizmo.Box box in live.Boxes(other))
            {
                if (box.Half.X * box.Half.Y >= largest * 0.9 && Holds(box, at))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a point is inside a box, or on top of it.
    private static bool Holds(BoxGizmo.Box box, Vector3d point)
    {
        Vector3d local = box.Orientation.Transform(point - box.Centre);
        return Math.Abs(local.X) <= box.Half.X + ReachMetres
            && Math.Abs(local.Y) <= box.Half.Y + ReachMetres
            && local.Z >= -box.Half.Z - ReachMetres
            && local.Z <= box.Half.Z + StandMetres;
    }
}
