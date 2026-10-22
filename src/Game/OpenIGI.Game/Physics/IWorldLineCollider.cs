namespace OpenIGI.Game.Physics;

/// <summary>
/// Something lines and sight meet beside a world's own kinds, answering both itself: another game's human, among them.
/// </summary>
/// <remarks>Like a boned body, it answers line traces and sight and not the ground query: nothing stands on it.</remarks>
public interface IWorldLineCollider : IWorldCollider
{
    /// <summary>Gets the query group its type is in; queries name the groups they reach.</summary>
    uint Group { get; }

    /// <summary>Traces lines against it, keeping the nearer of each line's results.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void TraceLines(WorldLineTrace trace);

    /// <summary>Weighs a line of sight through it.</summary>
    /// <param name="sight">The line, which it blocks or lets through.</param>
    void LineOfSight(WorldLineOfSight sight);
}
