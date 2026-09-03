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

            // QVM parents cabin occupants under the airframe (M4 Heli 356 minds 1234–1238,
            // Heli 800 minds 540–544). That ancestry is the ride contract, not a label.
            if (IsVehicle(ancestor))
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

    /// <summary>Vehicles whose child Humans inherit the ride (Heli / Plane / Train / Car).</summary>
    public static bool IsVehicle(GameTask task) =>
        task is DeclaredTask declared && declared.Type?.Name is "Train" or "Heli" or "Plane" or "Car";

    private static bool IsCutsceneContainer(string label) =>
        label.Contains("cutscene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut scene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut-scene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Friendly Forces", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Guys running for heli", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Harrison & Men in Heli", StringComparison.OrdinalIgnoreCase);
}
