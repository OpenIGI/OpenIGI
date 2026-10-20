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

    /// <summary>
    /// Gets the task that answers the events sent to what a line struck, as a weapon's hit is, or <see langword="null"/> when
    /// <see cref="Owner"/> answers them itself.
    /// </summary>
    object? Handler => null;

    /// <summary>Traces vertical lines against this collider alone, the object's handler for the ground-query event.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void NextZ(Igi2LineTrace trace);
}

/// <summary>The ground as the collision sees it: what vertical lines stand on, and what any line meets.</summary>
/// <remarks>
/// The collision world asks the ground last, under <see cref="Igi2CollisionWorld.TerrainGroup"/>. This game's ground is its
/// terrain's tiles (<see cref="Igi2TerrainCollider"/>); another game's ground is answered by its own collider.
/// </remarks>
public interface IIgi2GroundCollider : IIgi2Collider
{
    /// <summary>Traces lines in any direction against the ground, keeping the nearer of each line's results.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void TraceLines(Igi2LineTrace trace);
}
