namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's first-person view, <c>HumanView_t</c>: so far, what landing and dying do to it.</summary>
/// <remarks>Grows with the view's port (<c>HumanView</c>, <c>igi2 0x4D5C50</c>).</remarks>
public sealed class Igi2HumanView
{
    /// <summary>Gets or sets how far a landing pushed the view down, <c>vImpactOffset</c>.</summary>
    public float ImpactOffset { get; set; }

    /// <summary>Gets or sets a value indicating whether the view's orientation is frozen, <c>isLockOrientation</c>.</summary>
    public bool IsLockOrientation { get; set; }

    /// <summary>Gets or sets the impact's angle, <c>vImpactAngle</c>; a landing zeroes it.</summary>
    public float ImpactAngle { get; set; }
}
