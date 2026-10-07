namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's first-person view, <c>HumanView_t</c>: so far, the kick a landing gives it.</summary>
/// <remarks>Grows with the view's port (<c>HumanView</c>, <c>igi2 0x4D5C50</c>).</remarks>
public sealed class Igi2HumanView
{
    /// <summary>Gets or sets how far a landing pushed the view down, <c>vImpactOffset</c>.</summary>
    public float ImpactOffset { get; set; }

    /// <summary>Gets or sets the impact's angle, <c>vImpactAngle</c>; a landing zeroes it.</summary>
    public float ImpactAngle { get; set; }
}
