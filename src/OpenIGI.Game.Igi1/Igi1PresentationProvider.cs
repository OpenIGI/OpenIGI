using OpenIGI.Game.Igi1.World;
using OpenIGI.Presentation;

namespace OpenIGI.Game.Igi1;

/// <summary>Builds the renderers Project I.G.I. draws with.</summary>
/// <remarks>
/// Two passes so far: the ground and the placed world. Each arrived when the host stopped naming its
/// renderer, which is what lets the renderer live here; see the README beside the ones still in
/// <c>OpenIGI.Presentation/Igi1</c> for what is holding the rest.
/// </remarks>
public sealed class Igi1PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi1PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    public ITerrainPresenter CreateTerrain(in TerrainPresentationRequest request) =>
        new TerrainRenderer(
            request.Device,
            request.Surface,
            request.Logger,
            request.DetailMaps,
            request.DerivedDetail);
}
