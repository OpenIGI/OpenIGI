using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What one frame needs in order to draw a title's ground.</summary>
/// <param name="Camera">The camera, positioned in metres.</param>
/// <param name="Projection">The view-to-clip transform.</param>
/// <param name="VerticalFieldOfView">The projection's full vertical field of view, in radians.</param>
/// <param name="AspectRatio">The viewport's width divided by its height.</param>
/// <param name="ViewDistance">
/// Overrides the level-of-detail distance the presenter would derive; zero keeps the derived one. The
/// host resolves this from <c>-viewdist</c> or the render settings before the pass runs, because which
/// of the two wins is a host policy rather than a title one.
/// </param>
/// <param name="Batching">Whether compatible draws may be merged.</param>
/// <param name="OcclusionCulling">Whether prior visibility results may reject geometry.</param>
/// <remarks>
/// Grouped into one value rather than passed as seven arguments because it is one thing — the frame —
/// and because a title that ignores half of it should be able to say so by ignoring fields rather than
/// by carrying parameters it never reads.
/// </remarks>
public readonly record struct TerrainFrame(
    Camera3D Camera,
    Matrix4f Projection,
    float VerticalFieldOfView,
    float AspectRatio,
    int ViewDistance,
    bool Batching,
    bool OcclusionCulling);

/// <summary>Draws a title's ground.</summary>
/// <remarks>
/// <para>
/// Terrain is the first pass to get an interface of its own because it is where the two games diverge
/// hardest. I.G.I. 1's ground is a chunked octree streamed by level of detail out of <c>.ctr</c> and
/// <c>.cmd</c>; I.G.I. 2's is a flat grid of tiles, each a <c>.thm</c> height field with a material and
/// a lightmap beside it. Almost nothing about one describes the other, which is exactly the condition
/// that makes an interface worth extracting rather than a shared base class.
/// </para>
/// <para>
/// The counters are behind <see cref="Describe"/> rather than on properties for the same reason. A
/// chunk count, an octree visit count and a batch-page occupancy are facts about I.G.I. 1's
/// implementation, and a developer overlay that named them would be an overlay only I.G.I. 1 could
/// fill. Lines of text are what the overlay actually wants and all it should be promised.
/// </para>
/// </remarks>
public interface ITerrainPresenter : IDisposable
{
    /// <summary>Gets a one-line summary for an entity list, such as how much was drawn last frame.</summary>
    string Summary { get; }

    /// <summary>Draws the ground.</summary>
    /// <param name="frame">The frame to draw for.</param>
    void Draw(in TerrainFrame frame);

    /// <summary>Appends the presenter's per-frame counters, one line each.</summary>
    /// <param name="into">Receives the lines, already formatted.</param>
    void Describe(ICollection<string> into);
}

/// <summary>What a title needs in order to build an <see cref="ITerrainPresenter"/>.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="Surface">The level's ground, already read and built.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <param name="DetailMaps">The detail textures the ground is shaded with, or null for none.</param>
/// <param name="DerivedDetail">
/// Per-tile detail already derived on a worker, or null to derive it here. The menu's backdrop loader
/// passes it in because deriving one measures in tens of milliseconds and a level's worth is most of a
/// second, which is not something to spend on the render thread.
/// </param>
/// <remarks>
/// <see cref="Surface"/> is the shared <see cref="TerrainSurface"/> rather than anything title-shaped,
/// which is what lets this request be common. When I.G.I. 2's ground arrives it will need its own
/// carrier, and the honest move then is a second factory method rather than widening this one with
/// fields one title always leaves null.
/// </remarks>
public readonly record struct TerrainPresentationRequest(
    IGraphicsDevice Device,
    TerrainSurface Surface,
    IIgiLogger Logger,
    MaterialDetailPack? DetailMaps,
    IReadOnlyList<Core.Graphics.Bgra32[]>? DerivedDetail);

/// <summary>Builds the renderers one title draws with.</summary>
/// <remarks>
/// <para>
/// A separate seam from <see cref="OpenIGI.Engine.Titles.IGameTitleRuntime"/> because the reference
/// graph forbids joining them: <c>OpenIGI.Presentation</c> references <c>OpenIGI.Engine</c>, so
/// <c>Engine</c> cannot name a presenter. The composition root resolves both from the same title and is
/// the only place that names either implementation, which is the property that actually matters.
/// </para>
/// <para>
/// It grows one method per pass as each pass stops being constructed by the host. Terrain came first and
/// the world followed; the characters, the view model, the HUD and the menus are what is left. The
/// characters are the hard one — see the README beside I.G.I. 1's remaining renderers.
/// </para>
/// </remarks>
public interface ITitlePresentationProvider
{
    /// <summary>Builds the presenter for this title's ground.</summary>
    /// <param name="request">What the presenter needs.</param>
    /// <returns>The presenter.</returns>
    ITerrainPresenter CreateTerrain(in TerrainPresentationRequest request);

    /// <summary>Builds the presenter for this title's placed world objects.</summary>
    /// <param name="request">What the presenter needs.</param>
    /// <returns>The presenter.</returns>
    IWorldPresenter CreateWorld(in WorldPresentationRequest request);
}
