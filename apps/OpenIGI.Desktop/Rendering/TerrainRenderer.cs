using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using System.Runtime.InteropServices;

namespace OpenIGI.Desktop;

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

    /// <summary>How many uploaded chunks to keep before dropping the ones no recent frame used.</summary>
    /// <remarks>The original bounds the same cache and aborts with "Out of terrain renderchunks".</remarks>
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
    private readonly IIgiLogger _logger;
    private readonly List<TerrainChunk> _chunks = [];
    private readonly Dictionary<ChunkKey, Entry> _cache = [];
    private readonly Dictionary<BatchKey, List<BatchPage>> _batchPages = [];
    private readonly List<BatchPage> _drawPages = [];
    private readonly List<BatchPage> _queryPages = [];
    private readonly List<ChunkKey> _evictions = [];
    private readonly HashSet<BatchPage> _affectedPages = [];
    private readonly List<BatchKey> _emptyBatchKeys = [];
    private readonly TextureHandle[] _tiles;
    private long _frame;
    private Camera3D _previousCamera;
    private bool _hasPreviousCamera;
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
        _tiles = UploadTiles(derivedDetail);
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

    /// <summary>Gets how many uploaded chunks are held.</summary>
    public int CachedCount => _cache.Count;

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
            _surface.Discards);

        _frame++;
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

        bool bypassOcclusion = !occlusionCulling || IsCameraCut(camera);
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

        LastChunkCount = result.Emitted - occludedChunks;
        LastPassCount = passes;
        LastBatchCount = batches;
        LastOcclusionQueryCount = queries;
        LastOccludedChunkCount = occludedChunks;
        LastTriangleCount = triangles;
        LastVisitedCount = result.Visited;
        _previousCamera = camera;
        _hasPreviousCamera = true;
        _previousOccludedChunks = occludedChunks;

        if (result.Dropped > 0)
        {
            _logger.Warning(LogCategory, $"{result.Dropped} chunk(s) did not fit the visible list.");
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
                    layer.IsBase ? BlendMode.Opaque : BlendMode.AlphaBlend);
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
            page.Key.IsBase ? BlendMode.Opaque : BlendMode.AlphaBlend);
        return true;
    }

    /// <summary>Rejects stale query decisions after a teleport or abrupt camera cut.</summary>
    private bool IsCameraCut(in Camera3D camera)
    {
        if (!_hasPreviousCamera || (camera.Position - _previousCamera.Position).Length > 4.0)
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
        return largest > 0.15f;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
    private TextureHandle[] UploadTiles(IReadOnlyList<Bgra32[]>? derivedDetail)
    {
        if (_terrain.Tiles is not { } atlas || atlas.Tiles.Count == 0)
        {
            _logger.Warning(LogCategory, "The level ships no ground texture atlas; the terrain draws untextured.");
            return [];
        }

        TextureHandle[] handles = new TextureHandle[atlas.Tiles.Count];
        for (int i = 0; i < handles.Length; i++)
        {
            TextureImage tile = atlas.Tiles[i];
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

                // Derived here rather than read from the pack, because a ground tile arrives as an
                // index into an atlas and has no name to look one up with. There are a handful of them
                // and they cover more of the screen than anything else, so this is both the cheapest
                // case to derive in process and the one it would be most visible to skip. A caller that
                // has already done the arithmetic off this thread hands the result in instead.
                if (derivedDetail is not null && i < derivedDetail.Count && derivedDetail[i].Length > 0)
                {
                    DetailMaps?.AttachPacked(handles[i], derivedDetail[i], tile.Width, tile.Height);
                }
                else
                {
                    DetailMaps?.AttachDerived(
                        handles[i], tile.GetLevel(0), tile.Width, tile.Height);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
            {
                _logger.Warning(LogCategory, $"Ground tile {i} did not upload: {ex.Message}");
            }
        }

        _logger.Information(LogCategory, $"Ground tiles: {handles.Length} of {atlas.TileWidth}x{atlas.TileHeight}.");
        return handles;
    }

    private TextureHandle TileTexture(int index) =>
        (uint)index < (uint)_tiles.Length ? _tiles[index] : TextureHandle.None;

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
            (float)(1.0 / WorldScene.UnitsPerMetre));

        if (geometry is null)
        {
            return false;
        }

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

        BatchPage? page = pages.Count > 0 && pages[^1].CanFit(vertexCount)
            ? pages[^1]
            : null;
        if (page is null)
        {
            page = new BatchPage(key, _device);
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
        return new BatchedLayer(page, range, layer.TriangleCount);
    }

    /// <summary>Drops the chunks no recent frame drew, once the cache is over its bound.</summary>
    /// <remarks>
    /// "Recent" is <see cref="RetentionFrames"/>, not "this frame". Dropping everything the current view
    /// misses is what makes turning round and back again rebuild the whole world twice: a chunk behind
    /// the camera has not gone anywhere, and uploading it again costs a buffer allocation each time.
    /// </remarks>
    private void Trim()
    {
        if (_cache.Count <= CacheCapacity)
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
        if (_evictions.Count == 0)
        {
            foreach ((ChunkKey key, Entry entry) in _cache)
            {
                if (entry.LastUsedFrame != _frame)
                {
                    _evictions.Add(key);
                }
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
        _affectedPages.Clear();
        foreach (ChunkKey key in _evictions)
        {
            Entry entry = _cache[key];
            foreach (BatchedLayer layer in entry.Layers)
            {
                _affectedPages.Add(layer.Page);
            }

            if (entry.DirectMesh.IsValid)
            {
                _device.DestroyMesh(entry.DirectMesh);
            }
            _cache.Remove(key);
        }

        foreach (BatchPage page in _affectedPages)
        {
            page.Destroy(_device);
        }

        _emptyBatchKeys.Clear();
        foreach ((BatchKey key, List<BatchPage> pages) in _batchPages)
        {
            pages.RemoveAll(_affectedPages.Contains);
            if (pages.Count == 0)
            {
                _emptyBatchKeys.Add(key);
            }
        }

        foreach (BatchKey key in _emptyBatchKeys)
        {
            _batchPages.Remove(key);
        }

        // Surviving chunks that shared a destroyed page get just those layers appended again; their
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

    private readonly record struct BatchedLayer(BatchPage Page, MeshDrawRange Range, int TriangleCount);

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
        private const int MinimumVertexCapacity = 4096;

        /// <summary>The smallest index capacity a page's mesh is created with.</summary>
        private const int MinimumIndexCapacity = 12288;

        private readonly List<Vertex3D> _vertices = [];

        // Reused across appends so streaming terrain does not leave a trail of one-chunk arrays.
        private Vector3f[] _chunkNormals = new Vector3f[256];
        private readonly List<ushort> _indices = [];
        private readonly List<VisibleDraw> _visible = [];
        private readonly List<MeshDrawRange> _visibleRanges = [];
        private Vector3f _minimum = new(float.MaxValue);
        private Vector3f _maximum = new(float.MinValue);
        private int _uploadedVertices;
        private int _uploadedIndices;
        private int _vertexCapacity;
        private int _indexCapacity;
        private bool _dirty;
        private bool _queryRequested;

        public BatchPage(BatchKey key, IGraphicsDevice device)
        {
            Key = key;
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

        public BatchKey Key { get; }

        public Vector3d OriginMetres { get; }

        public MeshHandle Mesh { get; private set; }

        public OcclusionQueryHandle OcclusionQuery { get; private set; }

        public OcclusionBounds Bounds => new(_minimum, _maximum);

        public bool HasVisibleRanges => _visible.Count > 0;

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

        /// <summary>Scratch space for one chunk's accumulated normals, grown once and reused.</summary>
        private Span<Vector3f> ChunkNormals(int count)
        {
            if (_chunkNormals.Length < count)
            {
                _chunkNormals = new Vector3f[Math.Max(count, _chunkNormals.Length * 2)];
            }

            Span<Vector3f> normals = _chunkNormals.AsSpan(0, count);
            normals.Clear();
            return normals;
        }

        /// <summary>Works out a smooth normal for every vertex of a chunk from the triangles that meet there.</summary>
        /// <param name="vertices">The chunk's vertices.</param>
        /// <param name="indices">Its triangles, addressing <paramref name="sourceVertex"/>-relative slots.</param>
        /// <param name="sourceVertex">The slot the chunk's indices are relative to.</param>
        /// <param name="normals">Filled with one unnormalised normal per vertex.</param>
        /// <remarks>
        /// <para>
        /// The terrain has no shading normal of its own — the channel a mesh would keep one in carries the
        /// offset each vertex slides along as the ground morphs between levels of detail. Lighting the
        /// ground with that offset is what made distant terrain read as a chequerboard of lit and unlit
        /// patches: a morph target swings smoothly from pointing up to pointing sideways across a chunk,
        /// and a surface lit by <c>max(dot(n, sun), 0)</c> turns that smooth swing into a hard edge.
        /// </para>
        /// <para>
        /// So the normal is taken from the surface itself, once, here — the sum of the facing of every
        /// triangle meeting a vertex, weighted by area because the cross product's length is twice the
        /// triangle's. That is a smooth per-vertex normal rather than the flat per-triangle one a pixel
        /// shader's derivatives would give, and a flat one over triangles this size would keep the patches.
        /// </para>
        /// <para>
        /// The ground is a heightfield, so every normal is turned to face upwards. That costs nothing where
        /// the winding was already consistent and saves the case where it was not, which for this port's
        /// geometry is not a hypothetical.
        /// </para>
        /// </remarks>
        private static void AccumulateNormals(
            ReadOnlySpan<Vertex3D> vertices,
            ReadOnlySpan<ushort> indices,
            int sourceVertex,
            Span<Vector3f> normals)
        {
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int a = indices[i] - sourceVertex;
                int b = indices[i + 1] - sourceVertex;
                int c = indices[i + 2] - sourceVertex;

                if ((uint)a >= (uint)vertices.Length
                    || (uint)b >= (uint)vertices.Length
                    || (uint)c >= (uint)vertices.Length)
                {
                    continue;
                }

                Vector3f pa = vertices[a].Position;
                Vector3f pb = vertices[b].Position;
                Vector3f pc = vertices[c].Position;

                float ux = pb.X - pa.X, uy = pb.Y - pa.Y, uz = pb.Z - pa.Z;
                float vx = pc.X - pa.X, vy = pc.Y - pa.Y, vz = pc.Z - pa.Z;

                float nx = (uy * vz) - (uz * vy);
                float ny = (uz * vx) - (ux * vz);
                float nz = (ux * vy) - (uy * vx);

                if (nz < 0f)
                {
                    nx = -nx;
                    ny = -ny;
                    nz = -nz;
                }

                normals[a] = new Vector3f(normals[a].X + nx, normals[a].Y + ny, normals[a].Z + nz);
                normals[b] = new Vector3f(normals[b].X + nx, normals[b].Y + ny, normals[b].Z + nz);
                normals[c] = new Vector3f(normals[c].X + nx, normals[c].Y + ny, normals[c].Z + nz);
            }
        }

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

            Span<Vector3f> surfaceNormals = ChunkNormals(vertices.Length);
            AccumulateNormals(vertices, indices, sourceVertex, surfaceNormals);

            for (int v = 0; v < vertices.Length; v++)
            {
                Vertex3D vertex = vertices[v];
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
                    Vertex3D.PackSurfaceNormal(surfaceNormals[v])));
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
                }

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
