using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>A wire's model, stretched down the span it is strung across.</summary>
/// <param name="ScaleY">The factor the model's local Y axis is scaled by — the span over WORLD_METER.</param>
/// <remarks>
/// <c>Wire_UpdateInternalDataHandler</c> (<c>igi2ed 0x49B720</c>) scales only that one axis, and both
/// games run the same handler, so the arithmetic lives in the shared <see cref="WireTransform"/> and this
/// is only the placement's end of it.
/// </remarks>
public sealed record Igi2WireGeometry(float ScaleY) : IIgi2PlacementGeometry
{
    /// <inheritdoc/>
    public MeshGeometry? Build(MefModel model) => WireTransform.Stretch(model, ScaleY);
}
