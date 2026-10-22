using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>The player has taken damage this tick.</summary>
/// <param name="Damage">How much was taken, in the game's own damage units.</param>
/// <param name="RemainingFraction">How much health is left, from 0 to 1.</param>
public readonly record struct PlayerDamaged(float Damage, float RemainingFraction) : IGameEvent;
