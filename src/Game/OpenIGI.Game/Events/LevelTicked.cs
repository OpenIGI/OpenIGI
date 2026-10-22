using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>The level has run a tick: raised after every other event of that tick.</summary>
/// <param name="Tick">How many ticks the level has run, this one counted.</param>
public readonly record struct LevelTicked(int Tick) : IGameEvent;
