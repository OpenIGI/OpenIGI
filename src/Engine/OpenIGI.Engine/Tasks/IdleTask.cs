namespace OpenIGI.Engine.Tasks;

/// <summary>The level's <c>Idle</c> container: what a level parks beneath it is neither run nor in the world.</summary>
/// <remarks>
/// Registered beside <c>Static</c> with a run handler that does nothing, so the tick stops at it. A class of its own so a
/// level's run tells it by what it is, not by the spelling of its type's name.
/// </remarks>
public sealed class IdleTask : GameTask;
