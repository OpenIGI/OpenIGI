namespace OpenIGI.Game.World;

/// <summary>Says which placements a title's own objects have destroyed, so their destroyed model is drawn instead.</summary>
/// <remarks>
/// The sibling of <see cref="IPlacementMotion"/> for a placement's model rather than its pose: the title owns the object and
/// its state, the walk already bound the destroyed model (<see cref="WorldPlacement.DestroyedReplacement"/>), and the renderer
/// asks which to draw.
/// </remarks>
public interface IPlacementDestruction
{
    /// <summary>Gets a value indicating whether a placement is to be drawn as its destroyed model.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <returns><see langword="true"/> once the object that placed it is destroyed.</returns>
    bool IsDestroyed(WorldPlacement placement);
}
