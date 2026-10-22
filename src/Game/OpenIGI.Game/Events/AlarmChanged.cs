using OpenIGI.Engine.Events;

namespace OpenIGI.Game.Events;

/// <summary>The level's alarm has gone off, or been silenced.</summary>
/// <param name="On">Whether any alarm is sounding now.</param>
public readonly record struct AlarmChanged(bool On) : IGameEvent;
