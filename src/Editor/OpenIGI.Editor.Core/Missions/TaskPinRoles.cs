namespace OpenIGI.Editor.Core.Missions;

/// <summary>What a task's <c>VarString</c> parameter holds, which decides the pin it gets in a graph.</summary>
/// <remarks>
/// Level data declares every one of these as a plain <c>VarString</c>; the game decides what it means by how the task reads
/// it. Most are conditions the game evaluates each tick, but some run once for their effects, some give a number, and some
/// are not expressions at all.
/// </remarks>
public enum TextRole
{
    /// <summary>A level expression read as true or false each tick: a bool input pin.</summary>
    Condition,

    /// <summary>A level expression run for what its calls do (run at start, a soldier's weapons): an exec pin.</summary>
    Action,

    /// <summary>A level expression read as a number: a real input pin.</summary>
    Number,

    /// <summary>The id of a text in the language file, such as <c>M1_MSG_4</c>.</summary>
    TextResource,

    /// <summary>A set of <c>CRITERIA_*</c> names, which things an area reacts to.</summary>
    Criteria,

    /// <summary>Plain text the task reads as a name or list, not an expression.</summary>
    Word,
}

/// <summary>The role of every <c>VarString</c> parameter of every task type, in both games.</summary>
/// <remarks>
/// Both games give a task type and parameter name the same meaning, so one table serves both. It was taken from what every
/// retail level writes in each parameter: a parameter not listed is a condition, which is what almost all of them are.
/// </remarks>
public static class TaskPinRoles
{
    private static readonly Dictionary<(string Type, string Parameter), TextRole> Exceptions = new()
    {
        [("ConditionalContainer", "Run at start")] = TextRole.Action,
        [("ConditionalContainer", "Run at stop")] = TextRole.Action,
        [("HumanPlayer", "Weapon")] = TextRole.Action,
        [("HumanSoldier", "Weapon")] = TextRole.Action,
        [("SequenceCommand", "Run Expression")] = TextRole.Action,
        [("SequenceCommand", "Next Expression")] = TextRole.Action,
        [("AIStationaryGunHolder", "Team expression")] = TextRole.Number,
        [("Smoke", "Generate factor")] = TextRole.Number,
        [("Water", "Environmental map index")] = TextRole.Number,
        [("StatusMessage", "Text")] = TextRole.TextResource,
        [("AreaActivate", "Criteria")] = TextRole.Criteria,
        [("EditBoneObj", "Animation list")] = TextRole.Word,
        [("EditBoneObj", "WeaponModel Expression")] = TextRole.Word,
    };

    /// <summary>The role of a <c>VarString</c> parameter.</summary>
    /// <param name="taskType">The task type's name.</param>
    /// <param name="parameter">The parameter's name.</param>
    /// <returns>Its role.</returns>
    public static TextRole RoleOf(string taskType, string parameter)
    {
        ArgumentNullException.ThrowIfNull(taskType);
        ArgumentNullException.ThrowIfNull(parameter);
        return Exceptions.TryGetValue((taskType, parameter), out TextRole role) ? role : TextRole.Condition;
    }

    /// <summary>Whether a role's text is a level expression the game parses.</summary>
    /// <param name="role">The role.</param>
    /// <returns><see langword="true"/> for conditions, actions and numbers.</returns>
    public static bool IsExpression(TextRole role) => role is TextRole.Condition or TextRole.Action or TextRole.Number;
}
