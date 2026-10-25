using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>A character other than the player has died.</summary>
/// <param name="TaskId">The character's task id in the level.</param>
/// <param name="Team">The side it was on.</param>
public readonly record struct HumanKilled(int TaskId, int Team) : IGameEvent;
