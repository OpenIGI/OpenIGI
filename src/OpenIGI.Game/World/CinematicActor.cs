using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Classifies humans staged by an authored cutscene rather than gameplay AI.</summary>
public static class CinematicActor
{
    /// <summary>Returns whether a task belongs to an authored intro/outro cutscene subtree.</summary>
    public static bool IsStaged(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        for (GameTask? ancestor = task.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is CutSceneTask)
            {
                return true;
            }

            if (ancestor is ConditionalContainerTask container && IsCutsceneContainer(container.Label))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCutsceneContainer(string label) =>
        label.Contains("cutscene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut scene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut-scene", StringComparison.OrdinalIgnoreCase);
}
