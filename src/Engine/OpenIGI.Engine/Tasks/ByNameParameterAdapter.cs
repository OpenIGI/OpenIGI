namespace OpenIGI.Engine.Tasks;

/// <summary>Sets a task class's parameters from a level's by name: the same parameters in another order, some added or dropped.</summary>
/// <remarks>
/// <para>
/// Two versions of the engine declare many a type with the same parameters but in another order, a field or two added
/// between them -- I.G.I. 2's <c>MenuScreen</c> puts its logo before Project I.G.I.'s frame -- so one class can make the
/// type in both if it is set by name. Every value the level gives a name the class declares, of the same type, is set; the
/// rest of the class's parameters keep their defaults, and a name the class does not declare is dropped.
/// </para>
/// <para>
/// A mod that makes one game's tasks out of another game's class, where the two name a parameter alike, uses it too.
/// </para>
/// </remarks>
public sealed class ByNameParameterAdapter : ITaskParameterAdapter
{
    /// <summary>Gets the adapter; it keeps no state.</summary>
    public static ByNameParameterAdapter Instance { get; } = new();

    /// <inheritdoc/>
    public void Apply(TaskParameterValues values, GameTask task)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(task);
        for (int i = 0; i < values.Count; i++)
        {
            task.TrySetScriptValue(values.Parameters[i].Name, values[i]);
        }
    }
}
