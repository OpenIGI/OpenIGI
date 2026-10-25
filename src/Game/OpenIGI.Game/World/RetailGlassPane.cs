using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>A pane of glass a model attaches, as a body's ray finds it: what <c>Glass_Touch</c> (<c>0x449CB0</c>) answers.</summary>
/// <param name="owner">The task that placed the model it hangs in, whose container decides whether it is there.</param>
/// <param name="positionUnits">Where it hangs, in world units.</param>
public sealed class RetailGlassPane(GameTask owner, Vector3d positionUnits)
{
    /// <summary>Gets the task that placed it.</summary>
    public GameTask Owner { get; } = owner;

    /// <summary>Gets where it hangs, in world units.</summary>
    public Vector3d PositionUnits { get; } = positionUnits;

    /// <summary>Gets or sets whether it has broken, after which no ray finds it.</summary>
    public bool IsBroken { get; set; }
}
