using OpenIGI.Game.Igi1.World;
using OpenIGI.Presentation;

namespace OpenIGI.Game.Igi1;

/// <summary>Builds the renderers Project I.G.I. draws with.</summary>
/// <remarks>
/// One method for now, because terrain is the one pass the host no longer constructs by hand. It grows
/// as the rest of <c>OpenIGI.Presentation/Igi1</c> follows the terrain renderer into this project; see
/// that folder's README for what is holding each of the others.
/// </remarks>
public sealed class Igi1PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi1PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public ITerrainPresenter CreateTerrain(in TerrainPresentationRequest request) =>
        new TerrainRenderer(
            request.Device,
            request.Surface,
            request.Logger,
            request.DetailMaps,
            request.DerivedDetail);
}
