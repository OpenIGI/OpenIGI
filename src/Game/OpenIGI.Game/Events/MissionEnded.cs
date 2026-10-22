using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>The mission is decided: completed, or failed once the original's grace period ran out.</summary>
/// <param name="MissionId">The mission's id in its game's catalogue.</param>
/// <param name="Completed">Whether it was completed rather than failed.</param>
public readonly record struct MissionEnded(int MissionId, bool Completed) : IGameEvent;
