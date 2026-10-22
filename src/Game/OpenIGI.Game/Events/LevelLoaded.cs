using OpenIGI.Engine.Events;
using OpenIGI.Core.Titles;

namespace OpenIGI.Game.Events;

/// <summary>A level has been brought up and is about to run its first tick.</summary>
/// <param name="Title">The game the level is of.</param>
/// <param name="MissionId">The mission's id in its game's catalogue.</param>
public readonly record struct LevelLoaded(GameTitle Title, int MissionId) : IGameEvent;
