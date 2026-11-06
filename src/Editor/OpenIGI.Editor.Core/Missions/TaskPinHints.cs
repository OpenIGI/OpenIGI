using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Editor.Core.Missions;

/// <summary>The numeric task parameters that are really references: to a task, a task of one type, or an AI graph node.</summary>
/// <remarks>
/// Level data declares these as plain <c>Int32</c>; what they hold was taken from every retail level of both games, by
/// checking each value against the ids of the tasks in the same mission. A reference is typed only when every retail value
/// names the same task type (a <c>Car ID</c> is always a <c>Car</c>); one that names many types (a camera's target) takes any
/// task. Both games give these parameters the same meaning, so one table serves both.
/// </remarks>
public static class TaskPinHints
{
    private static readonly Dictionary<(string Type, string Parameter), ScriptType> Hints = new()
    {
        [("AnimTask", "TargetID")] = ScriptType.TaskRef,
        [("EditCamera", "Link task ID")] = ScriptType.TaskRef,
        [("EditCamera", "Target task ID")] = ScriptType.TaskRef,
        [("PlaceExplosiveTBA", "Task ID to place")] = ScriptType.TaskRef,
        [("MineField", "Explode close to task ID")] = ScriptType.TaskRef,
        [("CarAI", "Car ID")] = ScriptType.TaskOf("Car"),
        [("CarAI", "Route ID")] = ScriptType.TaskOf("PatrolPath"),
        [("CarAI", "Graph ID")] = ScriptType.TaskOf("AIGraph"),
        [("HumanAI", "Graph ID")] = ScriptType.TaskOf("AIGraph"),
        [("PatrolPath", "Graph ID")] = ScriptType.TaskOf("AIGraph"),
        [("Elevator", "SplineObj taskid")] = ScriptType.TaskOf("SplineObj"),
        [("Train", "RailroadQTaskID")] = ScriptType.TaskOf("SplineObj"),
        [("AISquad", "AlarmControlID")] = ScriptType.TaskOf("AlarmControl"),
        [("AISquad", "AlarmTriggerID")] = ScriptType.TaskRef,
        [("AISquad", "StationaryGunID")] = ScriptType.TaskOf("StationaryGun"),
        [("Lensflare", "Dirlight QTask ID")] = ScriptType.TaskOf("Dirlight"),
        [("Lift", "Start floor task ID")] = ScriptType.TaskOf("Floor"),
        [("SplinePathGuideQTask", "Path ID")] = ScriptType.TaskOf("SplinePathDynCubeObj"),
        [("SplinePathGuideQTask", "RigidObject ID")] = ScriptType.TaskOf("EditRigidObj"),
        [("PatrolPathCommand", "eNodeId")] = ScriptType.NodeRef,
    };

    /// <summary>The parameters whose numbered names (<c>Objective 3 Link To Position</c>) are task references.</summary>
    private static readonly (string Type, string Prefix, string Suffix)[] Numbered =
    [
        ("DefineComputerObjective", "Objective ", " Link To Position"),
    ];

    /// <summary>Gets every hinted parameter with its type.</summary>
    public static IReadOnlyDictionary<(string Type, string Parameter), ScriptType> All => Hints;

    /// <summary>The reference type a numeric parameter holds, if it is a reference.</summary>
    /// <param name="taskType">The task type's name.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="type">Receives the reference type.</param>
    /// <returns><see langword="true"/> when the parameter is a reference.</returns>
    public static bool TryGetReference(string taskType, string parameter, out ScriptType type)
    {
        ArgumentNullException.ThrowIfNull(taskType);
        ArgumentNullException.ThrowIfNull(parameter);
        if (Hints.TryGetValue((taskType, parameter), out type))
        {
            return true;
        }

        // A type the census did not see still names its references the way level data always does.
        if (parameter is "TargetID" or "Target ID"
            || parameter.EndsWith("task ID", StringComparison.OrdinalIgnoreCase)
            || parameter.EndsWith("taskid", StringComparison.OrdinalIgnoreCase)
            || parameter.EndsWith("QTaskID", StringComparison.Ordinal)
            || parameter.EndsWith("QTask ID", StringComparison.Ordinal))
        {
            type = ScriptType.TaskRef;
            return true;
        }

        foreach ((string type0, string prefix, string suffix) in Numbered)
        {
            if (string.Equals(taskType, type0, StringComparison.Ordinal)
                && parameter.StartsWith(prefix, StringComparison.Ordinal)
                && parameter.EndsWith(suffix, StringComparison.Ordinal))
            {
                type = ScriptType.TaskRef;
                return true;
            }
        }

        type = default;
        return false;
    }
}
