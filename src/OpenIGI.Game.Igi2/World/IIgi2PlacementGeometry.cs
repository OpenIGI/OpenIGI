using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>How a placement's geometry is derived, when it is not the model as it was authored.</summary>
/// <remarks>
/// <para>
/// Most of the sequel's objects are a model placed at a position under an orientation, and the renderer
/// draws the shipped mesh unchanged. A few are not: a <c>Wire</c> stretches its model down the span it
/// is strung across, and a <c>SplineObjSegment</c> bends its model along a curve. Both deform the
/// vertices before they reach the vertex buffer — the original does it every frame in
/// <c>Direct3DRender_ProcessVerticesSplineRigid</c> and its siblings — and both therefore need geometry
/// of their own rather than the model's.
/// </para>
/// <para>
/// This is the seam that keeps that out of the world walk. A placement says how its geometry is made;
/// the drawable scene, which is the only stage holding a <see cref="MefModel"/>, asks it to make one.
/// </para>
/// </remarks>
public interface IIgi2PlacementGeometry
{
    /// <summary>Builds the deformed geometry for a placement of this model.</summary>
    /// <param name="model">The model the placement names.</param>
    /// <returns>The geometry, in metres, or <see langword="null"/> when the model has none to deform.</returns>
    MeshGeometry? Build(MefModel model);
}
