namespace OpenIGI.Game.Physics;

/// <summary>Something the world's lines can strike: an object's type and the vertical line handler it answers with.</summary>
/// <remarks>
/// A hit records the collider it struck, as <c>QIntersection_s.ptHitObj</c> records the object, so a caller can put
/// a second question to that one object alone. <c>Human_GroundCollision</c> (<c>igi2 0x4FD130</c>) does exactly that
/// to read the material under a body's feet. An object of either game's world is one of these: the world sends its queries to
/// the objects of its trees, and each answers with its type's handlers.
/// </remarks>
public interface IWorldCollider
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
    void NextZ(WorldLineTrace trace);
}
