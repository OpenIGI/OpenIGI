using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// What a change of squad action carries, <c>AISquadActionChangeContext_s</c>: the action coming and going, whether the members'
/// stacks go with it, and what the coming action needs (the outgoing action's DeInit fills it from the current event).
/// </summary>
public sealed class Igi2AiSquadChange
{
    /// <summary>Gets or sets the action coming.</summary>
    public int NextAction { get; set; }

    /// <summary>Gets or sets the action going, which DeInit records.</summary>
    public int PreviousAction { get; set; }

    /// <summary>Gets or sets a value indicating whether the members' stacks are popped.</summary>
    public bool PopMembers { get; set; }

    /// <summary>Gets or sets a value indicating whether a save is being restored (no lines then).</summary>
    public bool Deserializing { get; set; }

    /// <summary>Gets or sets the position the coming action works at.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the direction it works in.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets the member who heard the event, for an inquiry.</summary>
    public Igi2HumanAi? Receiver { get; set; }

    /// <summary>Gets or sets the enemy a leapfrog advances on.</summary>
    public IAiDetectable? Target { get; set; }

    /// <summary>Gets or sets the patrol to walk.</summary>
    public int PatrolPathId { get; set; }
}
