namespace OpenIGI.Game.Igi2.Collision;

/// <summary>Something lines can strike: an object's type and the vertical line handler it answers with.</summary>
/// <remarks>
/// A hit records the collider it struck, as <c>QIntersection_s.ptHitObj</c> records the object, so a caller can put
/// a second question to that one object alone. <c>Human_GroundCollision</c> (<c>igi2 0x4FD130</c>) does exactly that
/// to read the material under a body's feet.
/// </remarks>
public interface IIgi2Collider
{
    /// <summary>Gets what the collider stands for in the level: the placing task, or the terrain.</summary>
    object Owner { get; }

    /// <summary>Traces vertical lines against this collider alone, the object's handler for the ground-query event.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void NextZ(Igi2LineTrace trace);
}
