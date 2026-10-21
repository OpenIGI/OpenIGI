using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Moves placements a title's own objects carry: a door swung or slid open, and what it has attached.</summary>
/// <remarks>
/// The sibling of <see cref="IPlacementSpin"/> for a placement that leaves its spot as well as turning. The title owns
/// the object and its state; the renderer asks it once a frame where each of its placements now stands.
/// </remarks>
public interface IPlacementMotion
{
    /// <summary>Gets where a moved placement is drawn this frame.</summary>
    /// <param name="placement">The placement as the walk produced it, at its authored pose.</param>
    /// <param name="tickFraction">How far the frame is between the last tick and the next, 0 to 1.</param>
    /// <param name="positionMetres">Receives the position, in metres.</param>
    /// <param name="orientation">Receives the orientation.</param>
    /// <returns><see langword="false"/> when the placement does not move, which is nearly all of them.</returns>
    bool TryGetTransform(WorldPlacement placement, float tickFraction, out Vector3d positionMetres, out Matrix3f orientation);
}
