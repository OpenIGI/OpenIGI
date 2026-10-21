namespace OpenIGI.Game.World;

/// <summary>What a title's level run decides for a <see cref="ConditionalContainerTask"/>: the parts of its run handler the two games do differently.</summary>
/// <remarks>
/// A level run that does not implement this runs a conditional container as a plain container.
/// </remarks>
public interface IConditionalContainerRules
{
    /// <summary>
    /// Gets a value indicating whether the children run once more on the tick the condition stops holding: Project I.G.I.'s
    /// handler (<c>0x4874E0</c>) runs them while the container runs or ran last tick, I.G.I. 2's (<c>igi2 0x520EF0</c>) only
    /// while it runs.
    /// </summary>
    bool RunsChildrenTheTickItStops { get; }

    /// <summary>Reads a container's condition this tick.</summary>
    /// <param name="container">The container.</param>
    /// <returns>Whether it holds, or <see langword="null"/> to leave <c>isRun</c> as it is, as an empty condition does.</returns>
    bool? Condition(ConditionalContainerTask container);

    /// <summary>Runs one of a container's scripts, <c>Run at start</c> or <c>Run at stop</c>.</summary>
    /// <param name="container">The container.</param>
    /// <param name="script">The script's text; nothing runs when it is empty.</param>
    void RunScript(ConditionalContainerTask container, string script);

    /// <summary>Brings a container's children back into the world (<c>ConditionalContainer_RegisterChildren</c>).</summary>
    /// <param name="container">The container.</param>
    void PutChildrenInWorld(ConditionalContainerTask container);

    /// <summary>Takes a container's children out of the world (<c>ConditionalContainer_RemoveChildren</c>).</summary>
    /// <param name="container">The container.</param>
    void TakeChildrenOutOfWorld(ConditionalContainerTask container);
}
