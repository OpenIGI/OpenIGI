using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Desktop;

/// <summary>
/// Reads placement geometry out of a loaded task tree, for the callers that need where a task sits rather
/// than what it does.
/// </summary>
internal static class TaskGeometry
{
    /// <summary>Finds a task by id and returns its graph or world position, or the origin when not found.</summary>
    /// <param name="root">The task tree to search.</param>
    /// <param name="taskId">The id of the task whose position is wanted.</param>
    /// <returns>The task's "Graph position" or "Position", or <see langword="default"/> when neither is present.</returns>
    public static Vector3d FindPosition(GameTask root, int taskId)
    {
        return TryFindPosition(root, taskId, out Vector3d position) ? position : default;
    }

    /// <summary>Finds a task's authored position without treating the world origin as “not found”.</summary>
    public static bool TryFindPosition(GameTask root, int taskId, out Vector3d position)
    {
        return Find(root, taskId, out position);

        static bool Find(GameTask task, int id, out Vector3d found)
        {
            if (task.Id == id)
            {
                if (task is DeclaredTask declared)
                {
                    if (declared.TryGetValue("Graph position", out object? graphPos) && graphPos is Vector3d gp)
                    {
                        found = gp;
                        return true;
                    }

                    if (declared.TryGetValue("Position", out object? pos) && pos is Vector3d p)
                    {
                        found = p;
                        return true;
                    }
                }

                found = default;
                return false;
            }

            foreach (GameTask child in task.Children)
            {
                if (Find(child, id, out found))
                {
                    return true;
                }
            }

            found = default;
            return false;
        }
    }
}
