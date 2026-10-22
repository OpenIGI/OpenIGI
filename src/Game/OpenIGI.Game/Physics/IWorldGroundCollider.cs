namespace OpenIGI.Game.Physics;

/// <summary>The ground as the collision sees it: what vertical lines stand on, and what any line meets.</summary>
/// <remarks>
/// A world asks the ground last, under its terrain group. Each game's ground is its own terrain, which answers the world's
/// queries as an object does: the vertical one (<c>World_GetNextZ</c>) and lines in any direction.
/// </remarks>
public interface IWorldGroundCollider : IWorldCollider
{
    /// <summary>Traces lines in any direction against the ground, keeping the nearer of each line's results.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void TraceLines(WorldLineTrace trace);
}
