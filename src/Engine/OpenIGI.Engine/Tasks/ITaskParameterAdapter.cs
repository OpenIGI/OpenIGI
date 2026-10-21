namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Sets a task class's members from the parameters level data writes for the type it was made to replace, when the class's own
/// parameters are not those.
/// </summary>
/// <remarks>
/// <para>
/// A type replaced with an adapter (<see cref="TaskTypeRegistry.Replace(string, Func{GameTask}, ITaskParameterAdapter)"/>)
/// keeps the parameters level data declares for it: they are what <c>Task_New</c> reads, in that order, and the values are
/// handed here with the new task instead of to the class's own binder. So one title's soldier can be made by another
/// title's class from the first title's data.
/// </para>
/// <para>
/// It runs where the class's own binder would, before the task is exported and before it receives
/// <see cref="TaskMessage.Parsed"/>.
/// </para>
/// </remarks>
public interface ITaskParameterAdapter
{
    /// <summary>Sets the new task from the values level data gave the type it replaces.</summary>
    /// <param name="values">The values, by the names and in the order the level declares them.</param>
    /// <param name="task">The task the replacing class made.</param>
    void Apply(TaskParameterValues values, GameTask task);
}
