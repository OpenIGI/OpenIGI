using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Game.Igi2;

/// <summary>Builds the renderers I.G.I. 2 draws with.</summary>
/// <remarks>
/// <para>
/// <b>One member, and that is the honest surface.</b> The sequel can draw its placed world — every
/// milestone up to here was about getting it to the point where it could — and it has no first-person
/// arms, no held weapon, no HUD and no map computer, because none of those systems exist for it yet.
/// Every other factory is left at the interface's default of null, which the host reads as "this title
/// does not present that pass" and skips.
/// </para>
/// <para>
/// The world presenter itself is <c>WorldRenderer</c>, shared with Project I.G.I. It named no task type,
/// no model id and no asset path, so it moved into the presentation layer and both titles draw through
/// it; what differs is only how a level becomes placements, which is
/// <see cref="World.Igi2DrawableScene"/>'s job.
/// </para>
/// <para>
/// The ground arrives through <see cref="CreateGround"/> rather than
/// <see cref="ITitlePresentationProvider.CreateTerrain"/>, because the latter carries a
/// <c>TerrainSurface</c> built from Project I.G.I.'s chunked octree and the sequel's flat grid of
/// <c>.thm</c> tiles produces no such thing. Given the level and a registry, this reads its own.
/// </para>
/// </remarks>
public sealed class Igi2PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// The level's <c>TerrainMap</c> tasks say which tiles it has; <c>heightmaps.res</c> beside them
    /// holds the three maps of each. A level that declares no terrain gets no presenter rather than an
    /// empty one, so the host skips the pass entirely instead of issuing nothing every frame.
    /// </remarks>
    public ITerrainPresenter? CreateGround(in GroundPresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);

        IresArchive? heightmaps = null;
        if (request.Resources.TryLoad(HeightmapArchive, out ReadOnlyMemory<byte> bytes))
        {
            heightmaps = IresArchive.Read(bytes);
        }

        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(request.LevelRoot, heightmaps, request.Logger);
        if (surface.IsEmpty)
        {
            return null;
        }

        Igi2TextureLoader textures = new(request.Device, request.Resources, request.Logger);
        return new Igi2TerrainPresenter(request.Device, surface, textures.Resolve, request.Logger);
    }

    /// <summary>The archive a level keeps its terrain maps in, under the mission alias.</summary>
    public const string HeightmapArchive = "MISSION:heightmaps/heightmaps.res";
}
