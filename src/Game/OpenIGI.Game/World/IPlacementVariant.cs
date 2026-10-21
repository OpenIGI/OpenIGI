namespace OpenIGI.Game.World;

/// <summary>Says which of its models a placement wears now, for an object that changes model with its state.</summary>
/// <remarks>
/// The sibling of <see cref="IPlacementDestruction"/> for a placement with more than one model to wear: the walk bound each
/// (<see cref="WorldPlacement.Variants"/>), the title owns the object and its state, and the renderer asks which to draw.
/// A switch is one: on, on and pressed, off, off and pressed, destroyed.
/// </remarks>
public interface IPlacementVariant
{
    /// <summary>Gets which of a placement's variants to draw.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <returns>The index into <see cref="WorldPlacement.Variants"/>, or -1 for the model the walk placed.</returns>
    int VariantOf(WorldPlacement placement);
}
