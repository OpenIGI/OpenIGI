using OpenIGI.Engine.Events;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Events;

/// <summary>One of the mission's objectives has completed or failed, or gone back to neither.</summary>
/// <param name="Slot">The objective's place in the mission's list, from 0.</param>
/// <param name="State">What it is now.</param>
/// <param name="Text">Its text, in the player's language.</param>
public readonly record struct ObjectiveChanged(int Slot, MissionObjectiveState State, string Text) : IGameEvent;
