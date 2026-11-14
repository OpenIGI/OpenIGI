namespace OpenIGI.Game.World;

/// <summary>Says which placements a title's own level has taken out of the world, so they are left out of the frame.</summary>
/// <remarks>
/// The sibling of <see cref="IPlacementDestruction"/> for a placement that is not there at all rather than drawn as something
/// else: the title owns the tasks that put its objects in the world and take them out -- a container whose condition stops
/// holding takes everything under it out -- and the renderer asks which are in.
/// </remarks>
public interface IPlacementPresence
{
    /// <summary>Gets a value indicating whether a placement is in the world now.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <returns><see langword="false"/> while the task that placed it is out of the world.</returns>
    bool IsInWorld(WorldPlacement placement);
}
