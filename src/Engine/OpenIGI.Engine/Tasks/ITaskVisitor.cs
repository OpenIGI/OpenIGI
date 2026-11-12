namespace OpenIGI.Engine.Tasks;

/// <summary>What a walk of the task tree does at each task it reaches.</summary>
/// <remarks>A struct, so a walk costs no delegate and no allocation; a <see langword="ref struct"/> may carry an event's context by reference.</remarks>
public interface ITaskVisitor
{
    /// <summary>Visits one task.</summary>
    /// <param name="task">The task.</param>
    void Visit(GameTask task);
}
