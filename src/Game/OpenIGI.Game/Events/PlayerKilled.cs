using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>The player has died.</summary>
public readonly record struct PlayerKilled() : IGameEvent;
