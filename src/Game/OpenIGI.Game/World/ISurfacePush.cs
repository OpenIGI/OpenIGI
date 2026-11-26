using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// What a level's surfaces push whoever stands on them with, each tick: a conveyor belt's moving faces carry a human along.
/// </summary>
/// <remarks>
/// A title whose own humans ask their footing for this answers it for another game's humans standing in its level, which walk
/// the level's object collision (<see cref="ObjectCollisionIndex"/>) rather than its own.
/// </remarks>
public interface ISurfacePush
{
    /// <summary>The push a face of a placement gives what stands on it this tick.</summary>
    /// <param name="placement">What is stood on.</param>
    /// <param name="material">The game-material row of the face stood on.</param>
    /// <returns>The push, in world units a tick; <see langword="null"/> where the face pushes nothing.</returns>
    Vector3f? PushOn(WorldPlacement placement, int material);
}
