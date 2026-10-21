using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>An event a squad was told of, <c>AISquadEventContext_s</c>: who heard it, about what, where and from which way.</summary>
public sealed class Igi2AiSquadEvent
{
    /// <summary>Gets or sets its kind, an <c>AIEVENT_*</c>.</summary>
    public int Kind { get; set; }

    /// <summary>Gets or sets its rank; a higher one is held nearer the front.</summary>
    public int Priority { get; set; }

    /// <summary>Gets the members who heard it, up to eight.</summary>
    public List<Igi2HumanAi> Receivers { get; } = [];

    /// <summary>Gets or sets what it is about, or <see langword="null"/>.</summary>
    public object? Reference { get; set; }

    /// <summary>Gets or sets where it happened.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the way it came from.</summary>
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets its kind-specific integer, <c>uSpecificData</c>.</summary>
    public int SpecificInt { get; set; }
}
