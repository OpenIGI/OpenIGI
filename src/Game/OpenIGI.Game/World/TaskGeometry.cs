using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>
/// Reads placement geometry out of a loaded task tree, for the callers that need where a task sits rather
/// than what it does.
/// </summary>
public static class TaskGeometry
{
    /// <summary>Finds a declared task by id and reads one of its named parameters.</summary>
    /// <param name="root">The tree to search.</param>
    /// <param name="taskId">The task's id.</param>
    /// <param name="name">The parameter's declared name.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when the task exists and declares the parameter.</returns>
    public static bool TryFindValue(GameTask root, int taskId, string name, out object? value)
    {
        if (root.Id == taskId && root is DeclaredTask declared && declared.TryGetValue(name, out value))
        {
            return true;
        }

        foreach (GameTask child in root.Children)
        {
            if (TryFindValue(child, taskId, name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Finds a task by id and returns its graph or world position, or the origin when not found.</summary>
    /// <param name="root">The task tree to search.</param>
    /// <param name="taskId">The id of the task whose position is wanted.</param>
    /// <returns>The task's "Graph position" or "Position", or <see langword="default"/> when neither is present.</returns>
    public static Vector3d FindPosition(GameTask root, int taskId)
    {
        return TryFindPosition(root, taskId, out Vector3d position) ? position : default;
    }

    /// <summary>Finds a task's authored position without treating the world origin as “not found”.</summary>
    /// <param name="root">The task tree to search.</param>
    /// <param name="taskId">The id of the task whose position is wanted.</param>
    /// <param name="position">Receives the task's "Graph position" or "Position".</param>
    /// <returns><see langword="true"/> when the task exists and declares either.</returns>
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
