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
        return Find(root, taskId);

        static Vector3d Find(GameTask task, int id)
        {
            if (task.Id == id)
            {
                if (task is DeclaredTask declared)
                {
                    if (declared.TryGetValue("Graph position", out object? graphPos) && graphPos is Vector3d gp)
                    {
                        return gp;
                    }

                    if (declared.TryGetValue("Position", out object? pos) && pos is Vector3d p)
                    {
                        return p;
                    }
                }

                return default;
            }

            foreach (GameTask child in task.Children)
            {
                Vector3d found = Find(child, id);
                if (found != default)
                {
                    return found;
                }
            }

            return default;
        }
    }
}
