namespace OpenIGI.Engine.Tasks;

/// <summary>The level's <c>Static</c> container: what it holds is placed once and never run.</summary>
/// <remarks>
/// The original registers it with a run handler that does nothing (Project I.G.I. beside <c>Level</c> and <c>Dynamic</c>;
/// I.G.I. 2 <c>Level_StaticRunHandler</c>), so the tick stops at it and level geometry costs nothing a tick. A class of its
/// own so a level's run tells it by what it is, not by the spelling of its type's name.
/// </remarks>
public sealed class StaticTask : GameTask;
