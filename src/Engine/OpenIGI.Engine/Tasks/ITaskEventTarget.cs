namespace OpenIGI.Engine.Tasks;

/// <summary>A task whose type handles an event.</summary>
/// <typeparam name="TEvent">The event, by its context.</typeparam>
/// <remarks>
/// The original links a handler to a type and copies a parent type's handlers into each type declared under it, so a type
/// answers every event its parent does unless it links its own (<c>0x401900</c>, <c>0x401400</c>). A class that implements
/// this is a type that linked a handler; a derived class inherits it and overrides it, by the same rule.
/// </remarks>
public interface ITaskEventTarget<TEvent>
    where TEvent : struct, ITaskEvent
{
    /// <summary>Handles the event.</summary>
    /// <param name="context">The event's context, which the handler may answer through.</param>
    void Handle(ref TEvent context);
}
