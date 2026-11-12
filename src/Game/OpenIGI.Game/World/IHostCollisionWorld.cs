using OpenIGI.Game.Physics;

namespace OpenIGI.Game.World;

/// <summary>
/// A level's collision as another game's soldiers move, look and shoot through it: lines through all it holds, and the colliders
/// those soldiers stand in it as.
/// </summary>
/// <remarks>
/// The level's own game answers in its own terms -- its objects, its ground, its humans -- and a contact names the collider it
/// met, whose <see cref="IWorldCollider.Handler"/> takes what struck it. The other game's bodies join it as
/// <see cref="IWorldLineCollider"/>s, so the level's own rounds and sight meet them as they meet its own.
/// </remarks>
public interface IHostCollisionWorld : ICollisionWorld
{
    /// <summary>Traces lines through every object, human and the ground, and keeps each line's nearest contact.</summary>
    /// <param name="trace">The lines and their running results.</param>
    void TraceLines(WorldLineTrace trace);

    /// <summary>Tests a line of sight through every object, human and the ground.</summary>
    /// <param name="sight">The line, what it may meet, and its running results.</param>
    /// <returns><see langword="true"/> when the line crossed no face.</returns>
    bool LineOfSight(WorldLineOfSight sight);

    /// <summary>Casts one line as a world trace asks it (<see cref="WorldTraceCast"/>).</summary>
    /// <param name="request">The line, the kinds of collision it meets, and the owners it passes through.</param>
    /// <param name="hit">Receives the nearest contact; its material is the level's own row.</param>
    /// <returns><see langword="true"/> when the line met something before its end.</returns>
    bool Trace(in WorldTraceRequest request, out WorldTraceHit hit);

    /// <summary>Puts a collider of the other game's in the level.</summary>
    /// <param name="collider">It.</param>
    void AddCollider(IWorldLineCollider collider);

    /// <summary>Takes a collider of the other game's out of the level.</summary>
    /// <param name="collider">It.</param>
    void RemoveCollider(IWorldLineCollider collider);
}
