using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace OpenIGI.Presentation.Igi1;

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
public sealed class TerrainRenderer : IDisposable
{
    private const string LogCategory = "Terrain";
    private const int BatchCellMetres = 2048;
    private const int MaximumBatchVertices = ushort.MaxValue + 1;
    private const int SparseVisibleQueryInterval = 16;
    private const int ProductiveVisibleQueryInterval = 4;
    private const int ProductiveOccludedChunkThreshold = 64;

    /// <summary>How many frames the view must hold still before a visibility verdict is trusted again.</summary>
    /// <remarks>
    /// A verdict is not measured against the frame that acts on it. The box is drawn, the result is read
    /// once the slot's fence comes round, and the renderer skips the page a frame or two after the depth
    /// that judged it was written. Standing still that is exactly right. Turning it is a statement about
    /// where the camera was looking, not where it is: a page that has just come out from behind a hill is
    /// still carrying the verdict it earned while hidden, so it is skipped -- the hole -- until its retest
    /// lands and it is drawn again. Long enough to cover the deepest frame queue the backends run.
    /// </remarks>
    private const int OcclusionSettleFrames = 3;

    /// <summary>How far the camera may drift per frame and still count as holding still, in metres.</summary>
    private const double OcclusionSettlePosition = 0.05;

    /// <summary>How far any orientation element may move per frame and still count as holding still.</summary>
    /// <remarks>About a fifth of a degree: below what a hand on a mouse holds, above sensor noise.</remarks>
    private const float OcclusionSettleOrientation = 0.003f;

    /// <summary>How many frames the cumulative-movement window spans.</summary>
    /// <remarks>
    /// The per-frame <see cref="OcclusionSettlePosition"/> is what a single frame's step is judged by, and
    /// it is fooled by a HIGH FRAME RATE and by a SLOW CINEMATIC PAN: sustained travel whose every step is
    /// under the per-frame threshold reads as "still" frame after frame, so occlusion re-engages while the
    /// camera is in fact moving, and terrain emerging from behind a hill is culled on the stale "occluded"
    /// verdict it earned while hidden -- the black flash on a mission switch and during fast flight. Judging
    /// the camera against where it was a whole window ago catches that travel however it is paced, because
    /// occlusion depends on camera POSITION (a hill either hides a point or it does not), never on where the
    /// camera looks -- so a windowed position test is both sufficient and exact.
    /// </remarks>
    private const int OcclusionWindowFrames = 12;

    /// <summary>Cumulative metres over the window above which the camera counts as moving.</summary>
    private const double OcclusionWindowDistance = 0.1;

    // A ring of the last OcclusionWindowFrames camera positions, oldest at _positionWindowNext once filled.
    private readonly Vector3d[] _positionWindow = new Vector3d[OcclusionWindowFrames];
    private int _positionWindowNext;
    private bool _positionWindowFilled;

    /// <summary>The default <see cref="CacheLimit"/>: how many uploaded chunks are kept.</summary>
    /// <remarks>
    /// The original bounds the same cache at this figure and aborts with "Out of terrain renderchunks".
    /// It is the retail bound and the floor here, not the ceiling: retail draws a fraction of the ground
    /// this does, and <see cref="CacheLimit"/> is what the level of detail setting raises.
    /// </remarks>
    public const int CacheCapacity = 4096;

    /// <summary>How far under <see cref="CacheCapacity"/> an over-capacity trim drops the cache.</summary>
    /// <remarks>
    /// Without it a trim that evicts exactly the excess leaves the cache one chunk under its bound, so the
    /// next chunk to stream in puts it back over and the next frame trims again — a page rebuild every
    /// frame for as long as the player keeps moving. The margin buys a few hundred chunks of quiet.
    /// </remarks>
    private const int EvictionMargin = 256;

    /// <summary>How many frames an unused chunk is kept for before it becomes a candidate for eviction.</summary>
    /// <remarks>
    /// Ten seconds at the simulation rate. Long enough that turning round and back again, or stepping
    /// behind a hill, costs nothing.
    /// </remarks>
    public const int RetentionFrames = 300;

    private readonly IGraphicsDevice _device;
    private readonly TerrainSurface _surface;
    private readonly TerrainData _terrain;
    private readonly IIgiLogger _logger;
    private readonly List<TerrainChunk> _chunks = [];

    // What the ray-traced world traces instead of the pages: this frame's chunks, morphed as drawn.
    private readonly TerrainShadowCaster _caster;
    private readonly Dictionary<ChunkKey, Entry> _cache = [];

    // The arrays evicted chunks give up, for the chunks coming into view to build into.
    private readonly TerrainGeometryPool _geometryPool = new();
    private readonly Dictionary<BatchKey, List<BatchPage>> _batchPages = [];
    private readonly List<BatchPage> _drawPages = [];
    private readonly List<BatchPage> _queryPages = [];
    private readonly List<ChunkKey> _evictions = [];

    // The cache ordered by the frame that last drew it, for the over-capacity path below.
    private readonly List<(ChunkKey Key, long LastUsed)> _evictionOrder = [];
    private readonly HashSet<BatchPage> _affectedPages = [];

    /// <summary>Pages emptied by eviction, kept with their meshes for the next page streaming opens.</summary>
    /// <remarks>
    /// A page's mesh is two device buffers, and the driver charges real time to create or free either.
    /// The set of pages in use changes constantly while the camera turns -- chunks behind it are
    /// evicted, chunks ahead of it stream in -- but its SIZE barely does, so a page that just emptied
    /// is exactly the page about to be needed. Bounded so a level's worth of pages emptied at once
    /// does not stay resident for good.
    /// </remarks>
    private readonly Stack<BatchPage> _sparePages = [];

    /// <summary>The most emptied pages kept for re-use before the rest go back to the device.</summary>
    private const int MaximumSparePages = 512;
    private readonly List<BatchKey> _emptyBatchKeys = [];
    private TextureHandle[] _tiles;
    private long _frame;
    private int _pageMeshCreates;
    private int _pageMeshDestroys;
    private int _evictedChunks;
    private int _rebuiltPages;
    private int _trims;
    private int _buildsThisFrame;
    private int _cacheLimit = CacheCapacity;
    private Camera3D _previousCamera;
    private bool _hasPreviousCamera;
    private int _occlusionSettle;
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
        IIgiLogger? logger = null,
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
        _tiles = UploadTiles(surface.Terrain.Tiles, derivedDetail);
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
    public int TileCount => _tiles.Length;

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
    /// together while the camera turns is the eviction rebuild <see cref="Trim"/> describes.
    /// </remarks>
    public int PageMeshesCreated => _pageMeshCreates;

    /// <summary>Gets how many page meshes have been destroyed since the level loaded.</summary>
    public int PageMeshesDestroyed => _pageMeshDestroys;

    /// <summary>Gets how many trims have evicted chunks since the level loaded.</summary>
    public int TrimCount => _trims;

    /// <summary>Gets how many chunks eviction has dropped since the level loaded.</summary>
    public int EvictedChunkCount => _evictedChunks;

    /// <summary>Gets how many pages eviction has compacted since the level loaded.</summary>
    public int CompactedPageCount => _rebuiltPages;

    /// <summary>Gets how many spare pages are held for re-use.</summary>
    public int SparePageCount => _sparePages.Count;

    /// <summary>Gets the live batch pages, the vertices they hold and the vertices they have room for.</summary>
    /// <remarks>
    /// The page sizing in one reading: held against capacity is what the floor in
    /// <c>BatchPage.MinimumVertexCapacity</c> costs, and a capacity many times the vertices held means
    /// the batch keys are splitting the ground finer than the pages can fill. Walked on demand, for the
    /// inspector panel; nothing in a frame reads it.
    /// </remarks>
    public (int Pages, long Vertices, long Capacity) PageOccupancy
    {
        get
        {
            int pages = 0;
            long vertices = 0;
            long capacity = 0;
            foreach (List<BatchPage> list in _batchPages.Values)
            {
                foreach (BatchPage page in list)
                {
                    pages++;
                    vertices += page.VertexCount;
                    capacity += page.VertexCapacity;
                }
            }

            return (pages, vertices, capacity);
        }
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
        Camera3D engineCamera = new(
            new Vector3d(
                camera.Position.X * WorldScene.UnitsPerMetre,
                camera.Position.Y * WorldScene.UnitsPerMetre,
                camera.Position.Z * WorldScene.UnitsPerMetre),
            camera.Orientation);

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

        // The detail cross-fade's window, from sub_50FE60: the offset is truncated to an integer there,
        // and 2.6666667 is the constant as the original spells it. The scale converts the shader's
        // metres back to the engine units the window is expressed in, per chunk below.
        float fadeOffset = (long)((view.ViewDistance + 64) * 0.375);
        float fadeSlope = 2.6666667f / (view.ViewDistance - 64);

        if (!batching)
        {
            DrawDirect(camera, result, fadeOffset, fadeSlope);
            Trim();
            return;
        }

        _drawPages.Clear();
        _queryPages.Clear();
        foreach (List<BatchPage> pages in _batchPages.Values)
        {
            foreach (BatchPage page in pages)
            {
                page.BeginFrame();
            }
        }

        bool bypassOcclusion = !occlusionCulling || !OcclusionVerdictsUsable(camera);
        int visibleQueryInterval = _previousOccludedChunks >= ProductiveOccludedChunkThreshold
            ? ProductiveVisibleQueryInterval
            : SparseVisibleQueryInterval;
        int triangles = 0;
        int passes = 0;
        int occludedChunks = 0;
        foreach (TerrainChunk chunk in _chunks)
        {
            if (!TryGetEntry(chunk, out Entry? entry))
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

            foreach (BatchedLayer layer in entry.Layers)
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
        _drawPages.Sort(BatchPage.DrawOrder);
        _queryPages.Sort(BatchPage.DrawOrder);

        int batches = 0;
        int queries = 0;
        foreach (BatchPage page in _drawPages)
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
        foreach (BatchPage page in _queryPages)
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

        foreach (BatchPage page in _drawPages)
        {
            if (!page.Key.IsBase
                && DrawBatchPage(page, camera.CreateModelView(page.OriginMetres), fadeOffset, fadeSlope))
            {
                batches++;
            }
        }
        _device.ClearDetailFade();
        SubmitShadowCaster(camera, fadeOffset, fadeSlope);

        LastChunkCount = result.Emitted - occludedChunks;
        LastPassCount = passes;
        LastBatchCount = batches;
        LastOcclusionQueryCount = queries;
        LastOccludedChunkCount = occludedChunks;
        LastTriangleCount = triangles;
        LastVisitedCount = result.Visited;
        LastDroppedChunkCount = result.Dropped;
        _previousCamera = camera;
        _hasPreviousCamera = true;
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

        Trim();
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
            if (!TryGetEntry(chunk, out Entry? entry) || !EnsureDirectMesh(entry))
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
                    TileTexture(layer.TileIndex),
                    TextureHandle.None,
                    // The original's ONE, ONE: every pass after the first adds its weighted share of
                    // the ground (see TerrainChunkMesh), and the shares sum to one.
                    layer.IsBase ? BlendMode.Opaque : BlendMode.Additive);
                triangles += layer.TriangleCount;
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

    private bool EnsureDirectMesh(Entry entry)
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
        BatchPage page,
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
            TileTexture(page.Key.TileIndex),
            TextureHandle.None,
            // The original's ONE, ONE: every pass after the first adds its weighted share of the ground
            // (see TerrainChunkMesh), and the shares sum to one.
            page.Key.IsBase ? BlendMode.Opaque : BlendMode.Additive);
        return true;
    }

    /// <summary>Rejects stale query decisions after a teleport or abrupt camera cut.</summary>
    /// <summary>Gets whether visibility verdicts may be acted on: the view has held still long enough.</summary>
    /// <remarks>
    /// Not a test of this frame alone. A view that has just stopped still has verdicts in flight that were
    /// measured while it moved, so the count runs on for <see cref="OcclusionSettleFrames"/> frames after
    /// the last movement and only then lets the culler skip anything again.
    /// </remarks>
    private bool OcclusionVerdictsUsable(in Camera3D camera)
    {
        // WindowMoved is evaluated unconditionally -- it maintains the position ring -- so it must run even
        // when ViewMoved has already decided the camera moved.
        bool windowMoved = WindowMoved(camera.Position);
        if (ViewMoved(camera) || windowMoved)
        {
            _occlusionSettle = OcclusionSettleFrames;
        }
        else if (_occlusionSettle > 0)
        {
            _occlusionSettle--;
        }

        return _occlusionSettle == 0;
    }

    /// <summary>Whether the camera has travelled more than the window allows over the last several frames.</summary>
    /// <param name="position">This frame's camera position, in metres.</param>
    /// <returns><see langword="true"/> when cumulative movement over the window counts as moving.</returns>
    /// <remarks>Catches sustained travel whose per-frame step slips under <see cref="OcclusionSettlePosition"/>.</remarks>
    private bool WindowMoved(Vector3d position)
    {
        // The entry about to be overwritten is the oldest -- OcclusionWindowFrames ago -- once the ring has
        // filled. Before then there is no window to compare against and the per-frame test carries the load.
        bool moved = _positionWindowFilled
            && (position - _positionWindow[_positionWindowNext]).Length > OcclusionWindowDistance;

        _positionWindow[_positionWindowNext] = position;
        _positionWindowNext++;
        if (_positionWindowNext >= _positionWindow.Length)
        {
            _positionWindowNext = 0;
            _positionWindowFilled = true;
        }

        return moved;
    }

    private bool ViewMoved(in Camera3D camera)
    {
        if (!_hasPreviousCamera)
        {
            return true;
        }

        if ((camera.Position - _previousCamera.Position).Length > OcclusionSettlePosition)
        {
            return true;
        }

        Matrix3f a = camera.Orientation;
        Matrix3f b = _previousCamera.Orientation;
        float largest = 0f;
        largest = Math.Max(largest, Math.Abs(a.M11 - b.M11));
        largest = Math.Max(largest, Math.Abs(a.M12 - b.M12));
        largest = Math.Max(largest, Math.Abs(a.M13 - b.M13));
        largest = Math.Max(largest, Math.Abs(a.M21 - b.M21));
        largest = Math.Max(largest, Math.Abs(a.M22 - b.M22));
        largest = Math.Max(largest, Math.Abs(a.M23 - b.M23));
        largest = Math.Max(largest, Math.Abs(a.M31 - b.M31));
        largest = Math.Max(largest, Math.Abs(a.M32 - b.M32));
        largest = Math.Max(largest, Math.Abs(a.M33 - b.M33));
        return largest > OcclusionSettleOrientation;
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
        foreach (BatchPage spare in _sparePages)
        {
            spare.Destroy(_device);
        }

        _sparePages.Clear();
        foreach (List<BatchPage> pages in _batchPages.Values)
        {
            foreach (BatchPage page in pages)
            {
                page.Destroy(_device);
            }
        }

        foreach (Entry entry in _cache.Values)
        {
            if (entry.DirectMesh.IsValid)
            {
                _device.DestroyMesh(entry.DirectMesh);
            }
        }

        foreach (TextureHandle tile in _tiles)
        {
            _device.DestroyTexture(tile);
        }

        _cache.Clear();
        _geometryPool.Clear();
        _batchPages.Clear();
        _drawPages.Clear();
        _queryPages.Clear();
        _chunks.Clear();
    }

    /// <summary>Uploads the level's ground tiles once.</summary>
    /// <remarks>
    /// Eighteen 128×128 tiles is a third of a megabyte, and a chunk can name any of them, so there is
    /// nothing to gain from doing this on demand. They repeat across the ground, hence the wrapping.
    /// </remarks>
    private TextureHandle[] UploadTiles(TexAtlasFile? tiles, IReadOnlyList<Bgra32[]>? derivedDetail)
    {
        if (tiles is not { } atlas || atlas.Tiles.Count == 0)
        {
            _logger.Warning(LogCategory, "The level ships no ground texture atlas; the terrain draws untextured.");
            return [];
        }

        TextureHandle[] handles = new TextureHandle[atlas.Tiles.Count];
        int uploadedWidth = atlas.TileWidth;
        int uploadedHeight = atlas.TileHeight;
        for (int i = 0; i < handles.Length; i++)
        {
            TextureImage tile = atlas.Tiles[i].WithMipChain();
            uploadedWidth = tile.Width;
            uploadedHeight = tile.Height;
            try
            {
                handles[i] = _device.CreateTexture(
                    new TextureDescription(
                        tile.Width,
                        tile.Height,
                        tile.MipLevels,
                        TextureFilter.Linear,
                        TextureAddressMode.Repeat),
                    tile.Pixels);

                // A caller that has already done the arithmetic off this thread hands the result in; the
                // menu's backdrop loader does, because deriving a tile measured at tens of milliseconds
                // and a level's worth is most of a second on the thread holding the menu up.
                if (derivedDetail is not null && i < derivedDetail.Count && derivedDetail[i].Length > 0)
                {
                    DetailMaps?.AttachPacked(handles[i], derivedDetail[i], tile.Width, tile.Height);
                }
                else
                {
                    // Derived in process from whatever the tile turned out to be. A ground tile has no
                    // name to look a map up by -- it is an index into an atlas -- and it does not need
                    // one: when the overrides folder replaces the atlas, this derives from the
                    // replacement, which is the picture actually on the ground.
                    DetailMaps?.AttachDerived(
                        handles[i], tile.GetLevel(0), tile.Width, tile.Height);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Ground tile {i} did not upload: {ex.Message}");
            }
        }

        // The UPLOADED size, not the atlas's declared one. Those are the same number until the overrides
        // folder replaces the atlas, and then reporting the header's is actively misleading -- it would
        // say 128x128 for ground that went up at 1024.
        _logger.Information(
            LogCategory,
            $"Ground tiles: {handles.Length} of {uploadedWidth}x{uploadedHeight}"
                + (uploadedWidth != atlas.TileWidth ? $" (atlas declares {atlas.TileWidth}x{atlas.TileHeight})." : "."));
        return handles;
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
    public void ReloadTiles(TexAtlasFile? tiles)
    {
        foreach (TextureHandle tile in _tiles)
        {
            if (tile.IsValid)
            {
                _device.DestroyTexture(tile);
            }
        }

        _tiles = UploadTiles(tiles, derivedDetail: null);
    }

    private TextureHandle TileTexture(int index) =>
        (uint)index < (uint)_tiles.Length ? _tiles[index] : TextureHandle.None;

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
    private bool TryGetEntry(in TerrainChunk chunk, [NotNullWhen(true)] out Entry? entry)
    {
        // The key is the chunk's integer position and its level of detail — the geometry a record holds
        // is only the same geometry when it is drawn in the same place at the same detail.
        ChunkKey key = new(
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
            _geometryPool);

        if (geometry is null)
        {
            return false;
        }

        _buildsThisFrame++;
        entry = new Entry(chunk, geometry) { LastUsedFrame = _frame };
        entry.Layers = AddToBatches(chunk, geometry);
        entry.OcclusionPage = entry.Layers[0].Page;
        _cache[key] = entry;
        return true;
    }

    /// <summary>Adds one chunk's material layers to spatially local, state-compatible mesh pages.</summary>
    private BatchedLayer[] AddToBatches(in TerrainChunk chunk, TerrainChunkGeometry geometry)
    {
        int vertexCount = geometry.Vertices.Length / geometry.Layers.Length;
        BatchedLayer[] result = new BatchedLayer[geometry.Layers.Length];

        for (int slot = 0; slot < geometry.Layers.Length; slot++)
        {
            result[slot] = AppendLayer(chunk, geometry, slot, vertexCount);
        }

        return result;
    }

    /// <summary>Appends one of a chunk's material layers to the open page for its batch key.</summary>
    private BatchedLayer AppendLayer(
        in TerrainChunk chunk,
        TerrainChunkGeometry geometry,
        int slot,
        int vertexCount)
    {
        TerrainChunkLayer layer = geometry.Layers[slot];
        BatchKey key = BatchKey.For(chunk, layer, slot);
        if (!_batchPages.TryGetValue(key, out List<BatchPage>? pages))
        {
            pages = [];
            _batchPages.Add(key, pages);
        }

        // The first page with room rather than the last page opened: compaction empties out an earlier
        // page's holes, and its room is only ever used again if the search can reach it.
        BatchPage? page = null;
        foreach (BatchPage candidate in pages)
        {
            if (candidate.CanFit(vertexCount))
            {
                page = candidate;
                break;
            }
        }

        if (page is null)
        {
            page = AcquirePage(key);
            pages.Add(page);
        }

        int sourceVertex = slot * vertexCount;
        Vector3d chunkMetres = new(
            chunk.Position.X / WorldScene.UnitsPerMetre,
            chunk.Position.Y / WorldScene.UnitsPerMetre,
            chunk.Position.Z / WorldScene.UnitsPerMetre);
        Vector3d offset = chunkMetres - page.OriginMetres;
        MeshDrawRange range = page.Append(
            geometry.Vertices.AsSpan(sourceVertex, vertexCount),
            geometry.Indices.AsSpan(layer.IndexOffset, layer.IndexCount),
            sourceVertex,
            new Vector3f((float)offset.X, (float)offset.Y, (float)offset.Z));
        return new BatchedLayer(page, range, layer.TriangleCount, vertexCount);
    }

    /// <summary>A page for a key: a spare re-keyed if there is one, otherwise a new one.</summary>
    private BatchPage AcquirePage(BatchKey key)
    {
        if (_sparePages.TryPop(out BatchPage? spare))
        {
            spare.Rekey(key, _device);
            return spare;
        }

        return new BatchPage(key, _device, this);
    }

    /// <summary>Puts an emptied page aside for re-use, or gives it up once enough are aside.</summary>
    private void RetirePage(BatchPage page)
    {
        if (_sparePages.Count < MaximumSparePages)
        {
            page.Reset();
            _sparePages.Push(page);
            return;
        }

        page.Destroy(_device);
    }

    /// <summary>Hands the ray-traced world this frame's terrain, morphed exactly as the shader drew it.</summary>
    /// <remarks>
    /// Every chunk the traversal emitted, whether or not the occlusion queries let it be drawn: an
    /// occluded chunk still shadows the ground in front of whatever hides it.
    /// </remarks>
    private void SubmitShadowCaster(in Camera3D camera, float fadeOffset, float fadeSlope)
    {
        _caster.Begin(camera.Position, fadeOffset, fadeSlope);
        foreach (TerrainChunk chunk in _chunks)
        {
            if (TryGetEntry(chunk, out Entry? entry))
            {
                _caster.Add(chunk, entry.Geometry);
            }
        }

        _caster.Submit(camera);
        LastCasterTriangleCount = _caster.LastTriangleCount;
    }

    /// <summary>Gets how many triangles the last frame's shadow caster held.</summary>
    public int LastCasterTriangleCount { get; private set; }

    /// <summary>Drops the chunks no recent frame drew, once the cache is over its bound.</summary>
    /// <remarks>
    /// "Recent" is <see cref="RetentionFrames"/>, not "this frame". Dropping everything the current view
    /// misses is what makes turning round and back again rebuild the whole world twice: a chunk behind
    /// the camera has not gone anywhere, and uploading it again costs a buffer allocation each time.
    /// </remarks>
    private void Trim()
    {
        if (_cache.Count <= CacheLimit)
        {
            return;
        }

        _evictions.Clear();
        foreach ((ChunkKey key, Entry entry) in _cache)
        {
            if (_frame - entry.LastUsedFrame > RetentionFrames)
            {
                _evictions.Add(key);
            }
        }

        // Nothing has aged out yet and the cache is still over its bound, so the oldest go instead —
        // otherwise it would grow without limit while the camera sweeps.
        //
        // The oldest, and only as many as the bound is over by. This used to drop every chunk the current
        // frame did not draw, which is the thing the remark above warns against rather than the thing it
        // describes: on any one frame a sweep leaves most of the cache undrawn, so turning evicted nearly
        // the whole world, rebuilt it as the camera came back, and evicted it again on the way past --
        // page destroys and re-appends every frame, which is where the stutter and the missing ground came
        // from. A margin comes with them so the next frame is not straight back here.
        if (_evictions.Count == 0)
        {
            int excess = _cache.Count - CacheLimit + EvictionMargin;

            _evictionOrder.Clear();
            _evictionOrder.EnsureCapacity(_cache.Count);
            foreach ((ChunkKey key, Entry entry) in _cache)
            {
                // Never what the current frame is drawing: it is on screen now, whatever its age.
                if (entry.LastUsedFrame != _frame)
                {
                    _evictionOrder.Add((key, entry.LastUsedFrame));
                }
            }

            _evictionOrder.Sort(static (left, right) => left.LastUsed.CompareTo(right.LastUsed));

            int take = Math.Min(excess, _evictionOrder.Count);
            for (int i = 0; i < take; i++)
            {
                _evictions.Add(_evictionOrder[i].Key);
            }
        }

        if (_evictions.Count == 0)
        {
            return;
        }

        // Pages are append-only, so evicting a chunk means rebuilding the pages that held its layers —
        // and only those. The old approach destroyed every page and re-appended the whole cache, which
        // re-uploaded tens of megabytes of geometry on one frame and was the single largest freeze:
        // eviction happens exactly when the player is moving fast, which is when a spike shows most.
        // Evicting a chunk retires its ranges in place: the page keeps its mesh and its other chunks and
        // only notes how many of its vertices are now dead. Pages are append-only, so a dead range is a
        // hole until the page is compacted -- and a page is compacted only once half of it is holes, at
        // which point its survivors are appended again and the page re-uploaded into the SAME mesh.
        //
        // This used to destroy every page an evicted chunk touched and rebuild it from nothing. At the
        // two chunks a page the batch keys work out to, that was a mesh destroyed and another created for
        // most chunks evicted: fifty Vulkan buffer creates and destroys a frame while the camera turned,
        // and the frame time climbing the longer it turned, once the cache had filled and every few
        // frames evicted a batch.
        _affectedPages.Clear();
        foreach (ChunkKey key in _evictions)
        {
            Entry entry = _cache[key];
            foreach (BatchedLayer layer in entry.Layers)
            {
                layer.Page.Release(layer.VertexCount);
                if (layer.Page.NeedsCompaction)
                {
                    _affectedPages.Add(layer.Page);
                }
            }

            if (entry.DirectMesh.IsValid)
            {
                _device.DestroyMesh(entry.DirectMesh);
            }
            _cache.Remove(key);

            // Nothing refers to this chunk's geometry once it leaves the cache: its vertices were copied
            // into a batch page and uploaded to the device when it arrived, and the rebuild below only
            // reads the geometry of chunks that survived. So the arrays go back for the next chunk in.
            _geometryPool.Return(entry.Geometry);
        }

        _trims++;
        _evictedChunks += _evictions.Count;
        _rebuiltPages += _affectedPages.Count;
        foreach (BatchPage page in _affectedPages)
        {
            page.Reset();
        }

        if (_affectedPages.Count == 0)
        {
            return;
        }

        // Surviving chunks that shared a compacted page get just those layers appended again; their
        // layers in untouched pages keep their ranges, because untouched pages only ever grow.
        foreach (Entry entry in _cache.Values)
        {
            bool touched = false;
            foreach (BatchedLayer layer in entry.Layers)
            {
                if (_affectedPages.Contains(layer.Page))
                {
                    touched = true;
                    break;
                }
            }

            if (!touched)
            {
                continue;
            }

            int vertexCount = entry.Geometry.Vertices.Length / entry.Geometry.Layers.Length;
            for (int slot = 0; slot < entry.Layers.Length; slot++)
            {
                if (_affectedPages.Contains(entry.Layers[slot].Page))
                {
                    entry.Layers[slot] = AppendLayer(entry.Chunk, entry.Geometry, slot, vertexCount);
                }
            }

            entry.OcclusionPage = entry.Layers[0].Page;
        }

        // A compacted page no survivor came back to is empty. It goes to the spares rather than to the
        // device, so the next page the streaming opens takes its mesh instead of creating one.
        _emptyBatchKeys.Clear();
        foreach ((BatchKey key, List<BatchPage> pages) in _batchPages)
        {
            for (int i = pages.Count - 1; i >= 0; i--)
            {
                if (pages[i].VertexCount == 0)
                {
                    RetirePage(pages[i]);
                    pages.RemoveAt(i);
                }
            }

            if (pages.Count == 0)
            {
                _emptyBatchKeys.Add(key);
            }
        }

        foreach (BatchKey key in _emptyBatchKeys)
        {
            _batchPages.Remove(key);
        }
    }

    private readonly record struct ChunkKey(long X, long Y, long Z, int Lod);

    private readonly record struct BatchKey(
        int LodExponent,
        int TileIndex,
        bool IsBase,
        bool IsDetail,
        int LayerSlot,
        long CellX,
        long CellY,
        long CellZ)
    {
        public static BatchKey For(in TerrainChunk chunk, in TerrainChunkLayer layer, int layerSlot)
        {
            double unitsPerCell = BatchCellMetres * WorldScene.UnitsPerMetre;
            return new(
                chunk.LodExponent,
                layer.TileIndex,
                layer.IsBase,
                layer.IsDetail,
                layerSlot,
                (long)Math.Floor(chunk.Position.X / unitsPerCell),
                (long)Math.Floor(chunk.Position.Y / unitsPerCell),
                (long)Math.Floor(chunk.Position.Z / unitsPerCell));
        }
    }

    private readonly record struct BatchedLayer(BatchPage Page, MeshDrawRange Range, int TriangleCount, int VertexCount);

    private sealed class Entry(TerrainChunk chunk, TerrainChunkGeometry geometry)
    {
        public TerrainChunk Chunk { get; } = chunk;

        public TerrainChunkGeometry Geometry { get; } = geometry;

        public BatchedLayer[] Layers { get; set; } = [];

        public MeshHandle DirectMesh { get; set; }

        public BatchPage OcclusionPage { get; set; } = null!;

        public long LastUsedFrame { get; set; }
    }

    /// <summary>A 16-bit indexed mesh page sharing every state needed by one multi-draw.</summary>
    private sealed class BatchPage
    {
        /// <summary>The smallest vertex capacity a page's mesh is created with.</summary>
        /// <remarks>
        /// <para>
        /// Headroom, so that appending a chunk to a page does not recreate its mesh every time; the
        /// capacity doubles from here as the page fills, so a page reaching a thousand vertices is
        /// recreated four times over its whole life rather than once per chunk.
        /// </para>
        /// <para>
        /// It is small because pages are: the batch key separates every level of detail, ground tile,
        /// layer slot and base-or-detail pass, and there are only so many chunks in a 2048 m cell that
        /// share all four. Measured over a level 1 panorama, the median page holds ELEVEN vertices and
        /// three quarters of them hold under twenty; the top percent reach a thousand or so. A floor of
        /// 4096 was charging 192 KB of device memory for a median page holding 528 bytes of geometry --
        /// 2306 live pages, 105 412 vertices held against 9 445 376 allocated, some 486 MB of buffers
        /// for 5 MB of ground. The doubling is what handles the rare large page; the floor only has to
        /// cover the common tiny one.
        /// </para>
        /// </remarks>
        private const int MinimumVertexCapacity = 64;

        /// <summary>The smallest index capacity a page's mesh is created with.</summary>
        /// <remarks>Three per vertex, which is what the ground's triangles measure at (2.72 over a panorama).</remarks>
        private const int MinimumIndexCapacity = MinimumVertexCapacity * 3;

        private readonly List<Vertex3D> _vertices = [];

        private readonly List<ushort> _indices = [];
        private readonly List<VisibleDraw> _visible = [];
        private readonly List<MeshDrawRange> _visibleRanges = [];
        private Vector3f _minimum = new(float.MaxValue);
        private Vector3f _maximum = new(float.MinValue);
        private int _uploadedVertices;
        private int _uploadedIndices;
        private int _vertexCapacity;
        private int _indexCapacity;
        private int _deadVertices;
        private bool _dirty;
        private bool _queryRequested;
        private readonly TerrainRenderer _owner;

        public BatchPage(BatchKey key, IGraphicsDevice device, TerrainRenderer owner)
        {
            Key = key;
            _owner = owner;
            OriginMetres = new Vector3d(
                key.CellX * BatchCellMetres,
                key.CellY * BatchCellMetres,
                key.CellZ * BatchCellMetres);
            OcclusionQuery = key.IsBase ? device.CreateOcclusionQuery() : OcclusionQueryHandle.None;
        }

        public static Comparison<BatchPage> DrawOrder { get; } = (left, right) =>
        {
            int layer = left.Key.LayerSlot.CompareTo(right.Key.LayerSlot);
            return layer != 0 ? layer : left.MinimumDistance.CompareTo(right.MinimumDistance);
        };

        public BatchKey Key { get; private set; }

        public Vector3d OriginMetres { get; private set; }

        public MeshHandle Mesh { get; private set; }

        public OcclusionQueryHandle OcclusionQuery { get; private set; }

        public OcclusionBounds Bounds => new(_minimum, _maximum);

        public bool HasVisibleRanges => _visible.Count > 0;

        /// <summary>Gets how many vertices the page holds, dead ranges included.</summary>
        public int VertexCount => _vertices.Count;

        /// <summary>Gets how many vertices the page's mesh was created with room for.</summary>
        public int VertexCapacity => _vertexCapacity;

        /// <summary>Gets how many of those vertices belong to evicted chunks.</summary>
        public int DeadVertexCount => _deadVertices;

        /// <summary>Gets whether enough of the page is holes that it is worth rebuilding from its survivors.</summary>
        public bool NeedsCompaction => _deadVertices > 0 && _deadVertices * 2 >= _vertices.Count;

        /// <summary>Notes that one appended range's chunk has been evicted.</summary>
        public void Release(int vertexCount) => _deadVertices += vertexCount;

        /// <summary>Forgets every appended range, keeping the mesh and its capacity for what comes next.</summary>
        public void Reset()
        {
            _vertices.Clear();
            _indices.Clear();
            _visible.Clear();
            _visibleRanges.Clear();
            _minimum = new Vector3f(float.MaxValue);
            _maximum = new Vector3f(float.MinValue);
            _deadVertices = 0;
            _uploadedVertices = 0;
            _uploadedIndices = 0;
            _dirty = true;
            _queryRequested = false;
            MinimumDistance = double.MaxValue;
        }

        /// <summary>Makes a spare page the page for another key.</summary>
        public void Rekey(BatchKey key, IGraphicsDevice device)
        {
            Key = key;
            OriginMetres = new Vector3d(
                key.CellX * BatchCellMetres,
                key.CellY * BatchCellMetres,
                key.CellZ * BatchCellMetres);

            // Only base pages are occlusion-queried, so the query follows the key -- and a recycled page
            // never inherits one. A query handle carries the last verdict the device recorded for it, and
            // that verdict was measured against the geometry and the position of the page this one used
            // to be: a spare that emptied while hidden behind a hill comes back as a page somewhere else
            // entirely, and would be culled on the old page's "hidden" until its retest landed several
            // frames later. A fresh query is conservatively visible, which is the only safe answer for a
            // page nothing has probed yet.
            if (OcclusionQuery.IsValid)
            {
                device.DestroyOcclusionQuery(OcclusionQuery);
                OcclusionQuery = OcclusionQueryHandle.None;
            }

            if (key.IsBase)
            {
                OcclusionQuery = device.CreateOcclusionQuery();
            }

            Reset();
        }

        /// <summary>Gets whether a camera position sits inside this page's occlusion bounds.</summary>
        /// <param name="cameraMetres">The camera position, in metres.</param>
        /// <param name="margin">Extra reach in metres, keeping the near plane clear of the box.</param>
        public bool ContainsCamera(in Vector3d cameraMetres, double margin)
        {
            double x = cameraMetres.X - OriginMetres.X;
            double y = cameraMetres.Y - OriginMetres.Y;
            double z = cameraMetres.Z - OriginMetres.Z;
            return x >= _minimum.X - margin && x <= _maximum.X + margin
                && y >= _minimum.Y - margin && y <= _maximum.Y + margin
                && z >= _minimum.Z - margin && z <= _maximum.Z + margin;
        }

        public bool QueryRequested => _queryRequested;

        public double MinimumDistance { get; private set; }

        public ReadOnlySpan<MeshDrawRange> VisibleRanges
        {
            get
            {
                if (_visible.Count > 1)
                {
                    _visible.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
                }

                _visibleRanges.Clear();
                foreach (VisibleDraw draw in _visible)
                {
                    _visibleRanges.Add(draw.Range);
                }
                return CollectionsMarshal.AsSpan(_visibleRanges);
            }
        }

        public bool CanFit(int vertices) => _vertices.Count + vertices <= MaximumBatchVertices;

        public MeshDrawRange Append(
            ReadOnlySpan<Vertex3D> vertices,
            ReadOnlySpan<ushort> indices,
            int sourceVertex,
            Vector3f translation)
        {
            int vertexBase = _vertices.Count;
            int indexBase = _indices.Count;

            // Grown to what this append needs before the loops, so a page filling up over many appends
            // does not shed a trail of half-size backing arrays behind each doubling.
            _vertices.EnsureCapacity(vertexBase + vertices.Length);
            _indices.EnsureCapacity(indexBase + indices.Length);

            foreach (Vertex3D vertex in vertices)
            {
                Vector3f position = new(
                    vertex.Position.X + translation.X,
                    vertex.Position.Y + translation.Y,
                    vertex.Position.Z + translation.Z);
                Vector3f morphed = new(
                    position.X + vertex.Normal.X,
                    position.Y + vertex.Normal.Y,
                    position.Z + vertex.Normal.Z);
                ExpandBounds(position);
                ExpandBounds(morphed);
                _vertices.Add(new Vertex3D(
                    position,
                    vertex.Normal,
                    vertex.DiffuseU,
                    vertex.DiffuseV,
                    vertex.LightmapU,
                    vertex.LightmapV,
                    vertex.Colour,
                    vertex.SurfaceNormal));
            }

            foreach (ushort index in indices)
            {
                _indices.Add(checked((ushort)(vertexBase + index - sourceVertex)));
            }

            _dirty = true;
            return new MeshDrawRange(indexBase, indices.Length);
        }

        public void BeginFrame()
        {
            _visible.Clear();
            _visibleRanges.Clear();
            MinimumDistance = double.MaxValue;
            _queryRequested = false;
        }

        /// <returns><see langword="true"/> when this is the first visible range queued.</returns>
        public bool Queue(MeshDrawRange range, double distance)
        {
            bool first = _visible.Count == 0;
            _visible.Add(new VisibleDraw(range, distance));
            MinimumDistance = Math.Min(MinimumDistance, distance);
            return first;
        }

        /// <returns><see langword="true"/> when this page needs adding to the frame's query order.</returns>
        public bool RequestQuery(double distance)
        {
            MinimumDistance = Math.Min(MinimumDistance, distance);
            if (_queryRequested || !OcclusionQuery.IsValid)
            {
                return false;
            }

            _queryRequested = true;
            return true;
        }

        /// <summary>Brings the GPU copy up to date with what has been appended.</summary>
        /// <remarks>
        /// Pages grow every time terrain streams in, and re-uploading a whole page per appended chunk
        /// was the frame-spike this renderer was profiled to. The mesh is allocated with headroom and
        /// each append uploads only its own range; the full re-upload survives only for the rare
        /// doubling where the headroom runs out.
        /// </remarks>
        public void EnsureUploaded(IGraphicsDevice device)
        {
            if (!_dirty && Mesh.IsValid)
            {
                return;
            }

            if (Mesh.IsValid && _vertices.Count <= _vertexCapacity && _indices.Count <= _indexCapacity)
            {
                device.UploadMeshRange(
                    Mesh,
                    _uploadedVertices,
                    CollectionsMarshal.AsSpan(_vertices)[_uploadedVertices..],
                    _uploadedIndices,
                    CollectionsMarshal.AsSpan(_indices)[_uploadedIndices..]);
            }
            else
            {
                if (Mesh.IsValid)
                {
                    device.DestroyMesh(Mesh);
                    Mesh = MeshHandle.None;
                    _owner._pageMeshDestroys++;
                }

                _owner._pageMeshCreates++;
                _vertexCapacity = Math.Min(
                    MaximumBatchVertices,
                    (int)Math.Max(MinimumVertexCapacity, System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_vertices.Count)));
                _indexCapacity = (int)Math.Max(
                    MinimumIndexCapacity,
                    System.Numerics.BitOperations.RoundUpToPowerOf2((uint)_indices.Count));
                Mesh = device.CreateMeshStorage(_vertexCapacity, _indexCapacity);
                device.UploadMeshRange(
                    Mesh,
                    0,
                    CollectionsMarshal.AsSpan(_vertices),
                    0,
                    CollectionsMarshal.AsSpan(_indices));
            }

            _uploadedVertices = _vertices.Count;
            _uploadedIndices = _indices.Count;
            _dirty = false;
        }

        public void Destroy(IGraphicsDevice device)
        {
            if (Mesh.IsValid)
            {
                device.DestroyMesh(Mesh);
                Mesh = MeshHandle.None;
                _owner._pageMeshDestroys++;
            }

            if (OcclusionQuery.IsValid)
            {
                device.DestroyOcclusionQuery(OcclusionQuery);
                OcclusionQuery = OcclusionQueryHandle.None;
            }

            _uploadedVertices = 0;
            _uploadedIndices = 0;
            _vertexCapacity = 0;
            _indexCapacity = 0;
        }

        private void ExpandBounds(Vector3f point)
        {
            _minimum = new Vector3f(
                Math.Min(_minimum.X, point.X),
                Math.Min(_minimum.Y, point.Y),
                Math.Min(_minimum.Z, point.Z));
            _maximum = new Vector3f(
                Math.Max(_maximum.X, point.X),
                Math.Max(_maximum.Y, point.Y),
                Math.Max(_maximum.Z, point.Z));
        }

        private readonly record struct VisibleDraw(MeshDrawRange Range, double Distance);
    }
}
