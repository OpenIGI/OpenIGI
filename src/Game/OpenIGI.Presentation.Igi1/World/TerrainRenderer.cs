using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using System.Diagnostics.CodeAnalysis;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// Draws a level's terrain: descends the octree from the camera, then draws each chunk the descent
/// picked, uploading its geometry the first time it is needed.
/// </summary>
/// <remarks>
/// <para>
/// Chunks are cached by position and level of detail rather than by octree record, because the octree is
/// a DAG: one record is reused at several places and orientations, so its record index does not identify
/// the geometry. The original caches on the same key, hashing the chunk's integer coordinates.
/// </para>
/// <para>
/// The ground texture is real data all the way down: the level's <c>TextureModifier</c> tasks pick a
/// material per vertex through the <c>terrain.bit</c> masks, the material's band for the chunk's level of
/// detail names a tile map, and the tile map indexes the level's <c>terrain.tex</c>. Where two materials
/// meet, the second is blended over the first with the coverage in the vertex alpha.
/// </para>
/// <para>
/// Terrain culls back faces. The winding was settled by measurement: the <c>.cmd</c> triples are
/// uniformly wound within every block, and only a mirrored orientation flips a chunk's facing.
/// </para>
/// </remarks>
public sealed class TerrainRenderer : ITerrainPresenter
{
    private const string LogCategory = "Terrain";
    private const int SparseVisibleQueryInterval = 16;
    private const int ProductiveVisibleQueryInterval = 4;
    private const int ProductiveOccludedChunkThreshold = 64;


    /// <summary>The default <see cref="CacheLimit"/>: how many uploaded chunks are kept.</summary>
    /// <remarks>
    /// The original bounds the same cache at this figure and aborts with "Out of terrain renderchunks".
    /// It is the retail bound and the floor here, not the ceiling: retail draws a fraction of the ground
    /// this does, and <see cref="CacheLimit"/> is what the level of detail setting raises.
    /// </remarks>
    public const int CacheCapacity = 4096;

    /// <summary>How many frames an unused chunk is kept for before it becomes a candidate for eviction.</summary>
    /// <remarks>
    /// Ten seconds at the simulation rate. Long enough that turning round and back again, or stepping
    /// behind a hill, costs nothing.
    /// </remarks>
    public const int RetentionFrames = 300;

    private readonly IGraphicsDevice _device;
    private readonly TerrainSurface _surface;
    private readonly TerrainData _terrain;
    private readonly IOpenIgiLogger _logger;
    private readonly List<TerrainChunk> _chunks = [];

    // The ground the traced sun is shadowed by: the same descent with no frustum. See SubmitShadowCaster.
    private readonly List<TerrainChunk> _casterChunks = [];

    // What the ray-traced world traces instead of the pages: this frame's chunks, morphed as drawn.
    private readonly TerrainShadowCaster _caster;

    // Whether the last frame asked for the caster to be built for the sun's cascades as well as for the
    // traced world. See TerrainFrame.CastsIntoCascades.
    private bool _castsIntoCascades;
    private readonly Dictionary<TerrainChunkKey, TerrainChunkEntry> _cache = [];

    // The arrays evicted chunks give up, for the chunks coming into view to build into.
    private readonly TerrainGeometryPool _geometryPool = new();

    // The batch pages the cached chunks' layers live in, and the eviction that compacts them.
    private readonly TerrainBatcher _batcher;
    private readonly List<TerrainBatchPage> _drawPages = [];
    private readonly List<TerrainBatchPage> _queryPages = [];
    private readonly TerrainTileTextures _tiles;

    // The level's terrain lightmap pages, uploaded through its light ramp the first time a chunk names one.
    private readonly Dictionary<int, TextureHandle> _lightPages = [];
    private long _frame;
    private int _buildsThisFrame;
    private int _cacheLimit = CacheCapacity;
    private readonly OcclusionSettle _occlusionSettle = new(windowed: true);
    private int _previousOccludedChunks;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="TerrainRenderer"/> class.</summary>
    /// <param name="device">The device to upload to and draw with.</param>
    /// <param name="surface">
    /// The level's terrain and the tile layers that texture it; its terrain must be
    /// <see cref="TerrainData.IsUsable"/>.
    /// </param>
    /// <param name="logger">Where to report upload problems.</param>
    /// <param name="detailMaps">
    /// The derived surface-detail maps, or <see langword="null"/> for none. A constructor argument
    /// rather than a settable property, unlike the other renderers': the ground tiles are uploaded
    /// here and now, so a property assigned by an object initialiser would be set after the only
    /// moment it could have been read.
    /// </param>
    /// <param name="derivedDetail">
    /// Detail maps already derived for the ground tiles, one per tile and in tile order, or
    /// <see langword="null"/> to derive them here. Deriving one measured at tens of milliseconds, so a
    /// level's worth of them is most of a second; the menu's backdrop loader does the arithmetic on a
    /// worker and passes the results in, leaving this constructor with only the upload.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The terrain is not drawable.</exception>
    public TerrainRenderer(
        IGraphicsDevice device,
        TerrainSurface surface,
        IOpenIgiLogger? logger = null,
        MaterialDetailPack? detailMaps = null,
        IReadOnlyList<Bgra32[]>? derivedDetail = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.Terrain.IsUsable)
        {
            throw new ArgumentException(
                $"The terrain is not drawable: {string.Join("; ", surface.Terrain.Defects)}",
                nameof(surface));
        }

        _device = device;
        _surface = surface;
        _terrain = surface.Terrain;
        _logger = logger ?? NullLogger.Instance;
        DetailMaps = detailMaps;
        _batcher = new TerrainBatcher(device, _cache, _geometryPool);
        _tiles = new TerrainTileTextures(device, _logger, detailMaps, surface.Terrain.Tiles, derivedDetail);

        // On the GPU now; the level's terrain data need not hold the pictures as well.
        surface.Terrain.Tiles?.ReleasePixels();
        _caster = new TerrainShadowCaster(device);
    }

    /// <summary>Gets how many chunks the last <see cref="Draw"/> selected.</summary>
    public int LastChunkCount { get; private set; }

    /// <summary>Gets how many triangles the last <see cref="Draw"/> submitted.</summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Gets how many material passes the last <see cref="Draw"/> submitted.</summary>
    /// <remarks>One per chunk where a single ground material covers it, more where two or more meet.</remarks>
    public int LastPassCount { get; private set; }

    /// <summary>Gets how many multi-draw submissions replaced the last frame's material passes.</summary>
    public int LastBatchCount { get; private set; }

    /// <summary>Gets how many terrain batch bounds were queried in the last frame.</summary>
    public int LastOcclusionQueryCount { get; private set; }

    /// <summary>Gets how many emitted chunks were conservatively rejected by prior query results.</summary>
    public int LastOccludedChunkCount { get; private set; }

    /// <summary>Gets how many ground tiles were uploaded from the level's atlas.</summary>
    public int TileCount => _tiles.Count;

    /// <summary>Gets the derived surface-detail maps, or <see langword="null"/> for none.</summary>
    /// <remarks>Owned by the shell: one pack serves every renderer that loads a texture.</remarks>
    public MaterialDetailPack? DetailMaps { get; }

    /// <summary>Gets how many octree nodes the last descent entered.</summary>
    public int LastVisitedCount { get; private set; }

    /// <summary>Gets how many chunks the last descent found and could not fit in its visible list.</summary>
    /// <remarks>
    /// Every one of them is ground the frame does not draw — a hole, not a saving. It should be zero at
    /// every detail setting the presets offer; a non-zero figure means the list is bounded below what
    /// <c>r_terrain_detail</c> asks for, which is what <see cref="CacheLimit"/> is passed to the descent
    /// to prevent.
    /// </remarks>
    public int LastDroppedChunkCount { get; private set; }

    /// <summary>Gets how many uploaded chunks are held.</summary>
    public int CachedCount => _cache.Count;

    /// <summary>Gets how many chunks the last <see cref="Draw"/> had to build before it could draw them.</summary>
    /// <remarks>
    /// The streaming signal: it spikes when the view sweeps over ground the cache does not hold and falls
    /// to zero the moment the view stops finding new ground. A steady non-zero figure while the camera
    /// turns and turns back means the cache is evicting chunks the camera still wants -- see
    /// <see cref="CacheLimit"/> -- and every one of those is a chunk rebuilt for nothing.
    /// </remarks>
    public int LastBuildCount => _buildsThisFrame;

    /// <summary>Gets or sets how many uploaded chunks are kept before the oldest are evicted.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="CacheCapacity"/> is the default and the floor of what is sensible; the level of detail
    /// setting raises it, because how much ground is in view is exactly what decides how much of it has
    /// to be held. A cache smaller than one frame's descent could ask for would evict chunks the very
    /// next frame wants, so the setter will not go below <see cref="TerrainTraversal.MaximumChunks"/>.
    /// </para>
    /// <para>
    /// Sized so that turning right round and back does not evict: a frustum holds a quarter or so of the
    /// panorama, so a cache that only fits the frustum drops the ground behind the player and rebuilds
    /// it as they turn back -- measured at a hundred chunks rebuilt every frame, for ever, on level 1 at
    /// the ultra view distance. <see cref="LastBuildCount"/> is what that looks like from outside: a
    /// steady non-zero figure while the camera sweeps ground it has already seen.
    /// </para>
    /// </remarks>
    public int CacheLimit
    {
        get => _cacheLimit;
        set => _cacheLimit = Math.Max(TerrainTraversal.MaximumChunks, value);
    }

    /// <summary>Gets how many page meshes have been created since the level loaded.</summary>
    /// <remarks>
    /// With <see cref="PageMeshesDestroyed"/>, the streaming churn in one pair of numbers: both should
    /// settle within seconds of a level loading and then barely move, whatever the camera does. Growing
    /// together while the camera turns is the eviction rebuild <see cref="TerrainBatcher.Trim"/> describes.
    /// </remarks>
    public int PageMeshesCreated => _batcher.PageMeshesCreated;

    /// <summary>Gets how many page meshes have been destroyed since the level loaded.</summary>
    public int PageMeshesDestroyed => _batcher.PageMeshesDestroyed;

    /// <summary>Gets how many trims have evicted chunks since the level loaded.</summary>
    public int TrimCount => _batcher.TrimCount;

    /// <summary>Gets how many chunks eviction has dropped since the level loaded.</summary>
    public int EvictedChunkCount => _batcher.EvictedChunkCount;

    /// <summary>Gets how many pages eviction has compacted since the level loaded.</summary>
    public int CompactedPageCount => _batcher.CompactedPageCount;

    /// <summary>Gets how many spare pages are held for re-use.</summary>
    public int SparePageCount => _batcher.SparePageCount;

    /// <summary>Gets the live batch pages, the vertices they hold and the vertices they have room for.</summary>
    /// <remarks>
    /// The page sizing in one reading: held against capacity is what the floor in
    /// <c>TerrainBatchPage.MinimumVertexCapacity</c> costs, and a capacity many times the vertices held means
    /// the batch keys are splitting the ground finer than the pages can fill. Walked on demand, for the
    /// inspector panel; nothing in a frame reads it.
    /// </remarks>
    public (int Pages, long Vertices, long Capacity) PageOccupancy => _batcher.PageOccupancy;

    /// <inheritdoc/>
    public string Summary => FormattableString.Invariant($"{LastChunkCount} chunk(s) drawn");

    /// <inheritdoc/>
    /// <remarks>
    /// The counters this reports are facts about a streamed octree -- chunks, octree visits, batch pages
    /// -- so they are formatted here rather than named on an interface a flat-grid terrain could not
    /// fill. The overlay that shows them wants lines, and lines are all it is promised.
    /// </remarks>
    public void Describe(ICollection<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Add(FormattableString.Invariant($"tiles {TileCount}   chunks drawn {LastChunkCount}"));
        into.Add(FormattableString.Invariant(
            $"triangles {LastTriangleCount}   passes {LastPassCount}   batches {LastBatchCount}"));
        into.Add(FormattableString.Invariant(
            $"occlusion queries {LastOcclusionQueryCount}   occluded {LastOccludedChunkCount}"));
        into.Add(FormattableString.Invariant($"cached {CachedCount}   built this frame {LastBuildCount}"));
        into.Add(FormattableString.Invariant(
            $"page meshes created {PageMeshesCreated}   destroyed {PageMeshesDestroyed}   spare {SparePageCount}"));

        (int pages, long pageVertices, long pageCapacity) = PageOccupancy;
        into.Add(FormattableString.Invariant(
            $"pages {pages}   vertices {pageVertices} of {pageCapacity} ({pageCapacity * 48 / (1024 * 1024)} MB)"));
        into.Add(FormattableString.Invariant(
            $"trims {TrimCount}   evicted {EvictedChunkCount}   compacted pages {CompactedPageCount}"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The cache limit is read here, per frame, rather than set by the host before the call. It is this
    /// renderer's own setting -- a flat-grid terrain streams nothing and has no cache to bound -- and
    /// reading it per frame is what makes changing it from the console take effect on the next frame,
    /// which is how every other render setting behaves.
    /// </remarks>
    void ITerrainPresenter.Draw(in TerrainFrame frame)
    {
        CacheLimit = GeometryCVars.TerrainCache.Value;
        _castsIntoCascades = frame.CastsIntoCascades;

        Draw(
            frame.Camera,
            frame.Projection,
            frame.VerticalFieldOfView,
            frame.AspectRatio,
            frame.ViewDistance,
            frame.Batching,
            frame.OcclusionCulling);
    }

    /// <summary>Descends the octree from a camera and draws what it selects.</summary>
    /// <param name="camera">The camera, positioned in metres.</param>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <param name="verticalFieldOfView">The projection's full vertical field of view in radians.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="viewDistance">Overrides the derived level-of-detail distance; zero keeps the derived one.</param>
    /// <param name="batching">Whether compatible material draws may use spatial multi-draw pages.</param>
    /// <param name="occlusionCulling">Whether prior asynchronous visibility results may reject batch pages.</param>
    /// <exception cref="ObjectDisposedException">The renderer has been disposed.</exception>
    public void Draw(
        in Camera3D camera,
        in Matrix4f projection,
        float verticalFieldOfView,
        float aspectRatio,
        int viewDistance = 0,
        bool batching = true,
        bool occlusionCulling = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // The traversal works in engine units, which is the octree's own space; the camera arrives in
        // metres because that is what everything downstream of WorldScene uses.
        Camera3D engineCamera = new(WorldScene.ToUnits(camera.Position), camera.Orientation);

        TerrainView view = TerrainView.FromPerspective(
            engineCamera,
            verticalFieldOfView,
            aspectRatio,
            viewDistance > 0 ? viewDistance : null);
        TerrainTraversalResult result = TerrainTraversal.Collect(
            _terrain.Octree,
            view,
            _chunks,
            _surface.Discards,

            // The visible list is bounded by what the cache can hold, not by retail's 4096: the detail
            // setting is what decides how many chunks one frustum asks for, and the same setting sizes
            // the cache. Left at 4096 a wide frustum at the ultra distance drops thousands of chunks a
            // frame, and every dropped chunk is a piece of ground that is not there. Vanilla's cache is
            // retail's 4096, so vanilla keeps retail's bound exactly.
            CacheLimit);

        _frame++;
        _buildsThisFrame = 0;
        _device.SetProjection(projection);

        // Terrain culls back faces: the .cmd triples are uniformly wound (measured — see
        // TerrainChunkMesh.ReadTriangles) and mirrored chunks are emitted swapped, so the whole ground
        // faces one way. Culled against the double-sided picture from straight above: pixel-identical,
        // while the opposite face removes every triangle.
        _device.SetCullMode(CullMode.CounterClockwise);

        // NO ALPHA TEST. Where two materials meet the second is drawn over the first weighted by the
        // per-vertex alpha, so alpha here is a material's SHARE of the pixel and not a cut-out. The
        // deferred G-buffer tests diffuse.a * vColor.a against the port's default reference of 128 and
        // discards below it, which throws away every overlay vertex covering less than half -- that is,
        // exactly the fade that makes one material become another. What survives is the inner core of
        // each overlay with a hard edge where the gradient used to be.
        //
        // The forward path never had this: it tests the TEXTURE alpha alone and only for opaque draws,
        // and an overlay is additive. So the fault is the deferred presets only, and vanilla is the
        // picture it should have been matching all along. The sequel's ground says the same thing for
        // the same reason -- see Igi2TerrainPresenter.
        _device.SetAlphaReference(0);

        // The detail cross-fade's window, from sub_50FE60: the offset is truncated to an integer there,
        // and 2.6666667 is the constant as the original spells it. The scale converts the shader's
        // metres back to the engine units the window is expressed in, per chunk below.
        float fadeOffset = (long)((view.ViewDistance + 64) * 0.375);
        float fadeSlope = 2.6666667f / (view.ViewDistance - 64);

        if (!batching)
        {
            DrawDirect(camera, result, fadeOffset, fadeSlope);

            // Put back like the batched path below does. Missing here, the vanilla preset -- the only one
            // that draws unbatched -- left every later cut-out untested: the forest after the ground in
            // the same frame, and the world's fences and grilles from the next frame on, each drawing
            // its holes as the black the texture keeps in them.
            _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
            _batcher.Trim(_frame, CacheLimit);
            return;
        }

        _drawPages.Clear();
        _queryPages.Clear();
        _batcher.BeginFrame();

        bool bypassOcclusion = !occlusionCulling || !_occlusionSettle.VerdictsUsable(camera);
        int visibleQueryInterval = _previousOccludedChunks >= ProductiveOccludedChunkThreshold
            ? ProductiveVisibleQueryInterval
            : SparseVisibleQueryInterval;
        int triangles = 0;
        int passes = 0;
        int occludedChunks = 0;
        foreach (TerrainChunk chunk in _chunks)
        {
            if (!TryGetEntry(chunk, out TerrainChunkEntry? entry))
            {
                continue;
            }

            Vector3d centreMetres = new(
                chunk.Position.X / WorldScene.UnitsPerMetre,
                chunk.Position.Y / WorldScene.UnitsPerMetre,
                chunk.Position.Z / WorldScene.UnitsPerMetre);
            double distance = (centreMetres - camera.Position).Length;

            // A camera inside a page's bounds — always true of the ground underfoot — sees the query
            // box only from within, where its faces sit behind the page's own terrain and every
            // sample fails. Querying from in there flickers the floor, so the containing page is
            // force-visible and left unqueried until the camera is clear of it.
            bool cameraInsidePage = entry.OcclusionPage.ContainsCamera(camera.Position, 0.5);
            bool pageVisible = bypassOcclusion
                || cameraInsidePage
                || _device.OcclusionVisible(entry.OcclusionPage.OcclusionQuery);
            bool refreshQuery = !pageVisible
                || ((_frame + entry.OcclusionPage.OcclusionQuery.Id) % visibleQueryInterval) == 0;
            if (occlusionCulling && !cameraInsidePage && refreshQuery && entry.OcclusionPage.RequestQuery(distance))
            {
                _queryPages.Add(entry.OcclusionPage);
            }

            if (!pageVisible)
            {
                occludedChunks++;
                continue;
            }

            foreach (TerrainBatchedLayer layer in entry.Layers)
            {
                if (layer.Page.Queue(layer.Range, distance))
                {
                    _drawPages.Add(layer.Page);
                }
                triangles += layer.TriangleCount;
                passes++;
            }
        }

        // Layer slot is the hard ordering constraint: every chunk's base must exist before its first
        // blend, and material blends keep their authored order. Within a slot, nearer pages go first so
        // ordinary hardware early-Z rejects hills and ground hidden behind them.
        _drawPages.Sort(TerrainBatchPage.DrawOrder);
        _queryPages.Sort(TerrainBatchPage.DrawOrder);

        int batches = 0;
        int queries = 0;
        foreach (TerrainBatchPage page in _drawPages)
        {
            if (!page.Key.IsBase)
            {
                continue;
            }

            Matrix4f pageModelView = camera.CreateModelView(page.OriginMetres);
            if (page.QueryRequested)
            {
                _device.SetDepthState(DepthState.TestAndWrite);
                _device.IssueOcclusionQuery(page.OcclusionQuery, pageModelView, page.Bounds);
                queries++;
            }
            if (DrawBatchPage(page, pageModelView, fadeOffset, fadeSlope))
            {
                batches++;
            }
        }

        // Hidden pages have no draw entry. Retest them after visible terrain has populated depth, so
        // they can recover immediately when the camera moves while still benefiting from occluders.
        foreach (TerrainBatchPage page in _queryPages)
        {
            if (page.HasVisibleRanges)
            {
                continue;
            }

            _device.SetDepthState(DepthState.TestAndWrite);
            _device.IssueOcclusionQuery(
                page.OcclusionQuery,
                camera.CreateModelView(page.OriginMetres),
                page.Bounds);
            queries++;
        }

        foreach (TerrainBatchPage page in _drawPages)
        {
            if (!page.Key.IsBase
                && DrawBatchPage(page, camera.CreateModelView(page.OriginMetres), fadeOffset, fadeSlope))
            {
                batches++;
            }
        }
        _device.ClearDetailFade();
        _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);

        // Only where something will trace it or the cascades will draw it: the caster is a walk of the
        // whole octree from every direction, and a frame that uses neither was paying for that.
        if (_device.AcceptsShadowCasters || _castsIntoCascades)
        {
            SubmitShadowCaster(camera, view, fadeOffset, fadeSlope);
        }
        else
        {
            LastCasterTriangleCount = 0;
        }

        LastChunkCount = result.Emitted - occludedChunks;
        LastPassCount = passes;
        LastBatchCount = batches;
        LastOcclusionQueryCount = queries;
        LastOccludedChunkCount = occludedChunks;
        LastTriangleCount = triangles;
        LastVisitedCount = result.Visited;
        LastDroppedChunkCount = result.Dropped;
        _occlusionSettle.Observed(camera);
        _previousOccludedChunks = occludedChunks;

        if (result.Dropped > 0)
        {
            // Every one of these is ground the descent chose and the frame will not draw, so it says
            // which setting to raise rather than only that it happened.
            _logger.Warning(
                LogCategory,
                $"{result.Dropped} chunk(s) did not fit the visible list of {CacheLimit}; "
                    + "the ground has holes in it. Raise r_terrain_cache to match r_terrain_detail.");
        }

        _batcher.Trim(_frame, CacheLimit);
    }

    /// <summary>Keeps Vanilla's original chunk-local transforms and submission order byte-identical.</summary>
    private void DrawDirect(
        in Camera3D camera,
        in TerrainTraversalResult result,
        float fadeOffset,
        float fadeSlope)
    {
        int chunks = 0;
        int triangles = 0;
        int passes = 0;
        foreach (TerrainChunk chunk in _chunks)
        {
            if (!TryGetEntry(chunk, out TerrainChunkEntry? entry) || !EnsureDirectMesh(entry))
            {
                continue;
            }

            Matrix4f modelView = camera.CreateModelView(new Vector3d(
                chunk.Position.X / WorldScene.UnitsPerMetre,
                chunk.Position.Y / WorldScene.UnitsPerMetre,
                chunk.Position.Z / WorldScene.UnitsPerMetre));
            foreach (TerrainChunkLayer layer in entry.Geometry.Layers)
            {
                _device.SetDetailFade(
                    (float)(0.375 * WorldScene.UnitsPerMetre / (1 << (25 - chunk.LodExponent))),
                    fadeOffset,
                    fadeSlope,
                    layer.IsDetail);
                _device.SetDepthState(layer.IsBase ? DepthState.TestAndWrite : DepthState.TestOnly);
                _device.DrawMesh(
                    entry.DirectMesh,
                    layer.IndexOffset,
                    layer.IndexCount,
                    modelView,
                    _tiles[layer.TileIndex],
                    TextureHandle.None,
                    // The original's ONE, ONE: every pass after the first adds its weighted share of
                    // the ground (see TerrainChunkMesh), and the shares sum to one.
                    layer.IsBase ? BlendMode.Opaque : BlendMode.Additive);
                triangles += layer.TriangleCount;
                passes++;
            }

            // The ground's light, after its material passes (sub_5144E0 mode 2): the page multiplies the
            // frame -- ZERO, SRCCOLOR -- tested against the depth the base pass wrote and writing none,
            // and fogs towards white, so that at full fog it leaves the fogged ground as it found it. A
            // chunk no cell encloses has none and keeps its tiles' own colour, as in the original.
            if (entry.Geometry.Light is { } light && LightPage(light.Page) is { IsValid: true } page)
            {
                _device.SetDetailFadeUnweighted(
                    (float)(0.375 * WorldScene.UnitsPerMetre / (1 << (25 - chunk.LodExponent))),
                    fadeOffset,
                    fadeSlope);
                _device.SetDepthState(DepthState.TestOnly);
                _device.SetFogTarget(FogTarget.White);
                _device.DrawMesh(
                    entry.DirectMesh,
                    light.IndexOffset,
                    light.IndexCount,
                    modelView,
                    page,
                    TextureHandle.None,
                    BlendMode.Modulate);
                _device.SetFogTarget(FogTarget.Scene);
                triangles += light.TriangleCount;
                passes++;
            }

            chunks++;
        }

        if (passes > 0)
        {
            _device.ClearDetailFade();
        }

        LastChunkCount = chunks;
        LastPassCount = passes;
        LastBatchCount = 0;
        LastOcclusionQueryCount = 0;
        LastOccludedChunkCount = 0;
        LastTriangleCount = triangles;
        LastVisitedCount = result.Visited;
    }

    /// <summary>Gets a terrain lightmap page as a texture, uploading it through the level's ramp on first use.</summary>
    /// <param name="index">The page's index in <c>terrain.lmp</c>.</param>
    /// <returns>The texture, or an invalid handle when the page cannot be drawn.</returns>
    /// <remarks>
    /// Each texel is an index into the ramp <see cref="TerrainLightBindings.Ramp"/> holds -- the sun over the
    /// terrain's ambient -- expanded as <c>sub_517FD0</c> expands it. Linear and clamped, one level: the
    /// original's stage 0 filters linearly and clamps, and whether it built mips depends on a global this
    /// port does not reproduce, so the page is drawn at the one resolution it ships at.
    /// </remarks>
    private TextureHandle LightPage(int index)
    {
        if (_lightPages.TryGetValue(index, out TextureHandle cached))
        {
            return cached;
        }

        TextureHandle handle = default;
        if (_terrain.Lightmaps is { } pages && index >= 0 && index < pages.Pages.Count && !pages.Pages[index].IsEmpty)
        {
            TerrainLightmapPage page = pages.Pages[index];
            IReadOnlyList<Bgra32> ramp = _surface.Lights.Ramp;
            ReadOnlySpan<byte> texels = page.Texels.Span;
            var pixels = new Bgra32[page.Size * page.Size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = ramp[texels[i]];
            }

            try
            {
                handle = _device.CreateTexture(
                    new TextureDescription(page.Size, page.Size, 1, TextureFilter.Linear, TextureAddressMode.Clamp),
                    pixels);
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Terrain lightmap page {index} did not upload: {ex.Message}");
            }
        }

        _lightPages[index] = handle;
        return handle;
    }

    private bool EnsureDirectMesh(TerrainChunkEntry entry)
    {
        if (entry.DirectMesh.IsValid)
        {
            return true;
        }

        try
        {
            entry.DirectMesh = _device.CreateMesh(entry.Geometry.Vertices, entry.Geometry.Indices);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Terrain chunk did not upload: {ex.Message}");
            return false;
        }
    }

    private bool DrawBatchPage(
        TerrainBatchPage page,
        in Matrix4f modelView,
        float fadeOffset,
        float fadeSlope)
    {
        try
        {
            page.EnsureUploaded(_device);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"Terrain batch {page.Key} did not upload: {ex.Message}");
            return false;
        }

        _device.SetDetailFade(
            (float)(0.375 * WorldScene.UnitsPerMetre / (1 << (25 - page.Key.LodExponent))),
            fadeOffset,
            fadeSlope,
            page.Key.IsDetail);
        _device.SetDepthState(page.Key.IsBase ? DepthState.TestAndWrite : DepthState.TestOnly);
        _device.DrawMeshRanges(
            page.Mesh,
            page.VisibleRanges,
            modelView,
            _tiles[page.Key.TileIndex],
            TextureHandle.None,
            // The original's ONE, ONE: every pass after the first adds its weighted share of the ground
            // (see TerrainChunkMesh), and the shares sum to one.
            page.Key.IsBase ? BlendMode.Opaque : BlendMode.Additive);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _caster.Dispose();
        _batcher.DestroyPages();
        foreach (TerrainChunkEntry entry in _cache.Values)
        {
            if (entry.DirectMesh.IsValid)
            {
                _device.DestroyMesh(entry.DirectMesh);
            }
        }

        _tiles.Destroy();
        foreach (TextureHandle page in _lightPages.Values)
        {
            if (page.IsValid)
            {
                _device.DestroyTexture(page);
            }
        }

        _lightPages.Clear();
        _cache.Clear();
        _geometryPool.Clear();
        _batcher.Clear();
        _drawPages.Clear();
        _queryPages.Clear();
        _chunks.Clear();
    }

    /// <summary>Re-uploads the ground from a freshly-read atlas.</summary>
    /// <param name="tiles">The atlas to draw the ground from, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// <para>
    /// The terrain's half of taking the overrides folder in and out of the frame. The geometry is
    /// untouched -- the ground does not change shape when its pictures do -- so this destroys the tile
    /// textures and puts the new ones up in their place, and the chunk cache, which names tiles by index
    /// rather than by handle, never notices.
    /// </para>
    /// <para>
    /// The derived surface maps come with them. A ground tile has no name to look a map up by, so its
    /// maps are derived from whatever the tile turned out to be -- which means an atlas swapped under it
    /// must re-derive, or the ground would wear the replacement's colour with the original's normals.
    /// That is the expensive part of this and the reason it is a keypress rather than something done
    /// every frame.
    /// </para>
    /// </remarks>
    public void ReloadTiles(TexAtlasFile? tiles) => _tiles.Reload(tiles);

    /// <summary>The record for a chunk the descent emitted, building and uploading it if it is new.</summary>
    /// <remarks>
    /// Every chunk the descent emitted is built on the frame it is emitted, however many that is. There
    /// was briefly a per-frame ceiling here, on the reasoning that a frame's streaming cost should not
    /// grow with how far the view had swept; it was measured to withhold hundreds of chunks a frame on
    /// an ordinary mouse sweep -- a fifth to a half of the visible ground, blinking out and back as the
    /// budget came round -- and it went. A chunk the descent emitted is a chunk the player is looking
    /// at, so the only honest answer is to build it: a hole in the ground is not a cheaper frame, it is
    /// a wrong one. What the ceiling was really rationing was the rebuild of chunks the cache had
    /// evicted while the camera still wanted them; that is a cache-size question, and
    /// <see cref="LastBuildCount"/> is the counter that shows it.
    /// </remarks>
    private bool TryGetEntry(in TerrainChunk chunk, [NotNullWhen(true)] out TerrainChunkEntry? entry)
    {
        // The key is the chunk's integer position and its level of detail — the geometry a record holds
        // is only the same geometry when it is drawn in the same place at the same detail.
        TerrainChunkKey key = new(
            (long)chunk.Position.X,
            (long)chunk.Position.Y,
            (long)chunk.Position.Z,
            chunk.LodExponent);

        if (_cache.TryGetValue(key, out entry))
        {
            entry.LastUsedFrame = _frame;
            return true;
        }

        if (!_terrain.TryGetBlock(chunk, out TerrainCommandBlock block))
        {
            return false;
        }

        TerrainChunkGeometry? geometry = TerrainChunkMesh.Build(
            chunk,
            block,
            _surface,
            (float)(1.0 / WorldScene.UnitsPerMetre),
            _surface.Heights.Resolve(chunk),
            _geometryPool,
            _surface.Lights.Resolve(chunk));

        if (geometry is null)
        {
            return false;
        }

        _buildsThisFrame++;
        entry = new TerrainChunkEntry(chunk, geometry) { LastUsedFrame = _frame };
        entry.Layers = _batcher.AddToBatches(chunk, geometry);
        entry.OcclusionPage = entry.Layers[0].Page;
        _cache[key] = entry;
        return true;
    }

    /// <summary>Hands the ray-traced world this frame's terrain, morphed exactly as the shader drew it.</summary>
    /// <remarks>
    /// Every chunk the traversal emitted, whether or not the occlusion queries let it be drawn: an
    /// occluded chunk still shadows the ground in front of whatever hides it.
    /// </remarks>
    /// <remarks>
    /// From every direction, not the frustum: a hill behind the camera shadows the valley in front of it at
    /// a low sun, and built from what was drawn the caster lost the hill whenever the player looked away --
    /// the valley flicked between shadow and sunlight as the view turned. The same distance rule decides
    /// the level of detail, so the part of it on screen is still exactly the drawn surface.
    /// </remarks>
    private void SubmitShadowCaster(in Camera3D camera, in TerrainView view, float fadeOffset, float fadeSlope)
    {
        TerrainTraversal.Collect(
            _terrain.Octree,
            view with { Panoramic = true },
            _casterChunks,
            _surface.Discards,
            CacheLimit);

        _caster.Begin(camera.Position, fadeOffset, fadeSlope);
        foreach (TerrainChunk chunk in _casterChunks)
        {
            if (TryGetEntry(chunk, out TerrainChunkEntry? entry))
            {
                _caster.Add(chunk, entry.Geometry);
            }
        }

        _caster.Commit();
        if (_device.AcceptsShadowCasters)
        {
            _caster.Submit(camera);
        }

        LastCasterTriangleCount = _caster.LastTriangleCount;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The caster the last frame built: the cascades are filled before this frame's ground is drawn. A
    /// frame's worth of camera travel moves where the levels of detail meet by less than a cascade texel,
    /// and the ring of meshes keeps that one alive past the frames in flight.
    /// </remarks>
    public void DrawShadowCaster(in Camera3D lightCamera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_castsIntoCascades)
        {
            _caster.DrawCascade(lightCamera);
        }
    }

    /// <summary>Gets how many triangles the last frame's shadow caster held.</summary>
    public int LastCasterTriangleCount { get; private set; }
}
