using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Engine.Tasks;
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
/// <param name="CastsIntoCascades">
/// Whether the sun's shadow cascades will be filled, so the ground is gathered for them to draw through
/// <see cref="ITerrainPresenter.DrawShadowCaster"/>.
/// </param>
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
    bool OcclusionCulling,
    bool CastsIntoCascades = false);

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

    /// <summary>Draws the ground into the sun's shadow cascade being filled.</summary>
    /// <param name="lightCamera">The light's camera: the eye's position, looking along the sun.</param>
    /// <remarks>
    /// What the last <see cref="Draw"/> built for casting, when its frame asked for
    /// <see cref="TerrainFrame.CastsIntoCascades"/>. Defaulted to nothing, for a ground that casts no shadow.
    /// </remarks>
    void DrawShadowCaster(in Camera3D lightCamera)
    {
        _ = lightCamera;
    }

    /// <summary>Re-reads the tile atlas, after the override folder has changed.</summary>
    /// <param name="tiles">The replacement atlas, or null to fall back to the installation's own.</param>
    /// <remarks>Defaulted to nothing, for a ground that is not built from a tile atlas.</remarks>
    void ReloadTiles(TexAtlasFile? tiles)
    {
        _ = tiles;
    }

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
    IOpenIgiLogger Logger,
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
/// It grows one method per pass as each pass stops being constructed by the host: terrain, then the
/// world, then the first-person and projectile passes. The HUD, the map computer and the cameras are
/// what is left, plus the two that are not getting interfaces at all — see the census on
/// <c>TitleContentTests</c> for which and why.
/// </para>
/// </remarks>
/// <summary>What a title needs in order to read and build its own ground.</summary>
/// <param name="Device">The graphics device to upload through.</param>
/// <param name="LevelRoot">The level's task tree, which is what says where the ground is and what it is made of.</param>
/// <param name="Resources">The registry the level's files are read from.</param>
/// <param name="Logger">Where load diagnostics go.</param>
/// <remarks>
/// Nothing here is title-shaped, which is the point: a title is handed the level and reads whatever its
/// own ground happens to be, rather than being handed a ground somebody else decided the shape of.
/// </remarks>
public readonly record struct GroundPresentationRequest(
    IGraphicsDevice Device,
    GameTask LevelRoot,
    ResourceRegistry Resources,
    IOpenIgiLogger Logger);

/// <remarks>
/// <para>
/// <b>Every factory may return null, and the default is null.</b> That is the same rule
/// <see cref="ITitlePresentation"/> already states for the passes themselves: a title under
/// construction presents what it has and skips the rest. The interface was written when one title
/// existed, so returning non-null everywhere cost nothing; a second title makes the difference real —
/// I.G.I. 2 can draw its world today and has no view model, no HUD and no map computer, because those
/// are milestones that have not happened yet.
/// </para>
/// <para>
/// A default of null rather than an abstract member is deliberate: it means a title implements the
/// passes it has and stays silent about the others, instead of carrying thirteen overrides that all
/// say "not yet". The host's own presenter fields are already nullable, so nothing downstream changes.
/// </para>
/// </remarks>
public interface ITitlePresentationProvider
{
    /// <summary>Gets the presenters this title draws with: its ground, world, sky, view model, HUD, computers and effects.</summary>
    /// <remarks>Each may be absent, and the host draws nothing in its place; see <see cref="PresenterRegistry"/>.</remarks>
    PresenterRegistry Presenters { get; }

    /// <summary>Reads the air this title's levels are played in, if it authors one.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The atmosphere, or nothing when this game does not author one.</returns>
    /// <remarks>
    /// <para>
    /// <b>A title seam, and deliberately empty by default.</b> I.G.I. 2 lights its levels with a
    /// <c>GlobalLight</c> task carrying a fog colour, a fog density and a flat ambient, and applies that
    /// fog to every surface it draws. Project I.G.I. has no such task and no such pass, and its picture
    /// is a regression gate — so it answers nothing here and the host applies nothing, which is what
    /// keeps a change to the sequel's air from reaching it at all rather than merely not being noticed.
    /// </para>
    /// </remarks>
    SceneAtmosphere? ReadAtmosphere(GameTask levelRoot) => null;

    /// <summary>Reads how this title composites its placed objects, if it departs from the default.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>The shading, or nothing when this game draws objects the way the port always has.</returns>
    /// <remarks>
    /// <para>
    /// <b>A second seam rather than a wider first one.</b> <see cref="ReadAtmosphere"/> is about the air
    /// and is baked into every pipeline; this is about how a lightmap is applied and what lights an
    /// object without one, and is applied per draw. I.G.I. 2's lightmap stage is <c>MODULATE2X</c> and
    /// its unlightmapped objects are lit by Direct3D's own vertex pipeline from the level's sun
    /// (<see cref="ObjectShading"/> cites the state blocks); Project I.G.I. answers nothing here and is
    /// composited exactly as before.
    /// </para>
    /// </remarks>
    ObjectShading? ReadObjectShading(GameTask levelRoot) => null;

    /// <summary>Reads how this title's level drives the dynamic renderer, if it authors that itself.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <returns>
    /// The lighting, or nothing when this game's sun is a position in the sky and the host should go on
    /// deriving the picture's darkness and its fill from where that position is.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A third seam, and the one that turns the sky model off.</b> The other two are about what a
    /// surface is drawn with; this is about what lights it in the dynamic path. Answering it says three
    /// things at once: use my sun's direction as authored rather than sweeping it around an arc, fill
    /// with this ambient rather than the one you would have blended towards, and take this darkness
    /// rather than measuring my sun's height. <see cref="DeferredLighting"/> gives the measurements
    /// behind that, over all of I.G.I. 2's levels.
    /// </para>
    /// <para>
    /// Project I.G.I. answers nothing for a daylit level and keeps the sweep, the moon and the
    /// elevation-derived exposure untouched — which its regression captures require, since every one of
    /// them is a dynamic frame. It answers only for the six missions whose sky dome it paints as night,
    /// which its <c>Dirlight</c> cannot say: every level authors that sun at the same noon.
    /// </para>
    /// </remarks>
    DeferredLighting? ReadDeferredLighting(GameTask levelRoot) => null;
}
