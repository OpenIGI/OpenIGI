using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>
/// The terrain's batch pages: appends each cached chunk's material layers to spatially local,
/// state-compatible pages, keeps emptied pages for re-use, and evicts the chunks no recent frame drew.
/// </summary>
/// <remarks>
/// The chunk cache itself belongs to <see cref="TerrainRenderer"/>, which builds the entries; this reads
/// and evicts from the same dictionary, because evicting a chunk is what releases its layers' ranges in
/// the pages and decides which pages are compacted.
/// </remarks>
internal sealed class TerrainBatcher
{
    /// <summary>How far under <see cref="TerrainRenderer.CacheCapacity"/> an over-capacity trim drops the cache.</summary>
    /// <remarks>
    /// Without it a trim that evicts exactly the excess leaves the cache one chunk under its bound, so the
    /// next chunk to stream in puts it back over and the next frame trims again — a page rebuild every
    /// frame for as long as the player keeps moving. The margin buys a few hundred chunks of quiet.
    /// </remarks>
    private const int EvictionMargin = 256;

    /// <summary>The most emptied pages kept for re-use before the rest go back to the device.</summary>
    private const int MaximumSparePages = 512;

    private readonly IGraphicsDevice _device;
    private readonly Dictionary<TerrainChunkKey, TerrainChunkEntry> _cache;

    // The arrays evicted chunks give up, for the chunks coming into view to build into.
    private readonly TerrainGeometryPool _geometryPool;
    private readonly Dictionary<TerrainBatchKey, List<TerrainBatchPage>> _batchPages = [];
    private readonly List<TerrainChunkKey> _evictions = [];

    // The cache ordered by the frame that last drew it, for the over-capacity path below.
    private readonly List<(TerrainChunkKey Key, long LastUsed)> _evictionOrder = [];
    private readonly HashSet<TerrainBatchPage> _affectedPages = [];

    /// <summary>Pages emptied by eviction, kept with their meshes for the next page streaming opens.</summary>
    /// <remarks>
    /// A page's mesh is two device buffers, and the driver charges real time to create or free either.
    /// The set of pages in use changes constantly while the camera turns -- chunks behind it are
    /// evicted, chunks ahead of it stream in -- but its SIZE barely does, so a page that just emptied
    /// is exactly the page about to be needed. Bounded so a level's worth of pages emptied at once
    /// does not stay resident for good.
    /// </remarks>
    private readonly Stack<TerrainBatchPage> _sparePages = [];
    private readonly List<TerrainBatchKey> _emptyBatchKeys = [];
    private int _pageMeshCreates;
    private int _pageMeshDestroys;
    private int _evictedChunks;
    private int _rebuiltPages;
    private int _trims;

    /// <summary>Initializes a new instance of the <see cref="TerrainBatcher"/> class.</summary>
    /// <param name="device">The device the pages' meshes live on.</param>
    /// <param name="cache">The renderer's chunk cache, which <see cref="Trim"/> evicts from.</param>
    /// <param name="geometryPool">Where evicted chunks' geometry arrays go back to.</param>
    public TerrainBatcher(
        IGraphicsDevice device,
        Dictionary<TerrainChunkKey, TerrainChunkEntry> cache,
        TerrainGeometryPool geometryPool)
    {
        _device = device;
        _cache = cache;
        _geometryPool = geometryPool;
    }

    /// <summary>Gets how many page meshes have been created since the level loaded.</summary>
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
    public (int Pages, long Vertices, long Capacity) PageOccupancy
    {
        get
        {
            int pages = 0;
            long vertices = 0;
            long capacity = 0;
            foreach (List<TerrainBatchPage> list in _batchPages.Values)
            {
                foreach (TerrainBatchPage page in list)
                {
                    pages++;
                    vertices += page.VertexCount;
                    capacity += page.VertexCapacity;
                }
            }

            return (pages, vertices, capacity);
        }
    }

    /// <summary>Counts a page mesh created; called by the page that created it.</summary>
    public void NotePageMeshCreated() => _pageMeshCreates++;

    /// <summary>Counts a page mesh destroyed; called by the page that destroyed it.</summary>
    public void NotePageMeshDestroyed() => _pageMeshDestroys++;

    /// <summary>Clears every live page's queued draws and query request for a new frame.</summary>
    public void BeginFrame()
    {
        foreach (List<TerrainBatchPage> pages in _batchPages.Values)
        {
            foreach (TerrainBatchPage page in pages)
            {
                page.BeginFrame();
            }
        }
    }

    /// <summary>Adds one chunk's material layers to spatially local, state-compatible mesh pages.</summary>
    public TerrainBatchedLayer[] AddToBatches(in TerrainChunk chunk, TerrainChunkGeometry geometry)
    {
        int vertexCount = geometry.VerticesPerPass;
        TerrainBatchedLayer[] result = new TerrainBatchedLayer[geometry.Layers.Length];

        for (int slot = 0; slot < geometry.Layers.Length; slot++)
        {
            result[slot] = AppendLayer(chunk, geometry, slot, vertexCount);
        }

        return result;
    }

    /// <summary>Appends one of a chunk's material layers to the open page for its batch key.</summary>
    private TerrainBatchedLayer AppendLayer(
        in TerrainChunk chunk,
        TerrainChunkGeometry geometry,
        int slot,
        int vertexCount)
    {
        TerrainChunkLayer layer = geometry.Layers[slot];
        TerrainBatchKey key = TerrainBatchKey.For(chunk, layer, slot);
        if (!_batchPages.TryGetValue(key, out List<TerrainBatchPage>? pages))
        {
            pages = [];
            _batchPages.Add(key, pages);
        }

        // The first page with room rather than the last page opened: compaction empties out an earlier
        // page's holes, and its room is only ever used again if the search can reach it.
        TerrainBatchPage? page = null;
        foreach (TerrainBatchPage candidate in pages)
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
        return new TerrainBatchedLayer(page, range, layer.TriangleCount, vertexCount);
    }

    /// <summary>A page for a key: a spare re-keyed if there is one, otherwise a new one.</summary>
    private TerrainBatchPage AcquirePage(TerrainBatchKey key)
    {
        if (_sparePages.TryPop(out TerrainBatchPage? spare))
        {
            spare.Rekey(key, _device);
            return spare;
        }

        return new TerrainBatchPage(key, _device, this);
    }

    /// <summary>Puts an emptied page aside for re-use, or gives it up once enough are aside.</summary>
    private void RetirePage(TerrainBatchPage page)
    {
        if (_sparePages.Count < MaximumSparePages)
        {
            page.Reset();
            _sparePages.Push(page);
            return;
        }

        page.Destroy(_device);
    }

    /// <summary>Drops the chunks no recent frame drew, once the cache is over its bound.</summary>
    /// <param name="frame">The frame being drawn.</param>
    /// <param name="cacheLimit">How many chunks the cache may hold.</param>
    /// <remarks>
    /// "Recent" is <see cref="TerrainRenderer.RetentionFrames"/>, not "this frame". Dropping everything the
    /// current view misses is what makes turning round and back again rebuild the whole world twice: a
    /// chunk behind the camera has not gone anywhere, and uploading it again costs a buffer allocation
    /// each time.
    /// </remarks>
    public void Trim(long frame, int cacheLimit)
    {
        if (_cache.Count <= cacheLimit)
        {
            return;
        }

        _evictions.Clear();
        foreach ((TerrainChunkKey key, TerrainChunkEntry entry) in _cache)
        {
            if (frame - entry.LastUsedFrame > TerrainRenderer.RetentionFrames)
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
            int excess = _cache.Count - cacheLimit + EvictionMargin;

            _evictionOrder.Clear();
            _evictionOrder.EnsureCapacity(_cache.Count);
            foreach ((TerrainChunkKey key, TerrainChunkEntry entry) in _cache)
            {
                // Never what the current frame is drawing: it is on screen now, whatever its age.
                if (entry.LastUsedFrame != frame)
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
        foreach (TerrainChunkKey key in _evictions)
        {
            TerrainChunkEntry entry = _cache[key];
            foreach (TerrainBatchedLayer layer in entry.Layers)
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
        foreach (TerrainBatchPage page in _affectedPages)
        {
            page.Reset();
        }

        if (_affectedPages.Count == 0)
        {
            return;
        }

        // Surviving chunks that shared a compacted page get just those layers appended again; their
        // layers in untouched pages keep their ranges, because untouched pages only ever grow.
        foreach (TerrainChunkEntry entry in _cache.Values)
        {
            bool touched = false;
            foreach (TerrainBatchedLayer layer in entry.Layers)
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

            int vertexCount = entry.Geometry.VerticesPerPass;
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
        foreach ((TerrainBatchKey key, List<TerrainBatchPage> pages) in _batchPages)
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

        foreach (TerrainBatchKey key in _emptyBatchKeys)
        {
            _batchPages.Remove(key);
        }
    }

    /// <summary>Gives every spare and live page's mesh and query back to the device, spares first.</summary>
    public void DestroyPages()
    {
        foreach (TerrainBatchPage spare in _sparePages)
        {
            spare.Destroy(_device);
        }

        _sparePages.Clear();
        foreach (List<TerrainBatchPage> pages in _batchPages.Values)
        {
            foreach (TerrainBatchPage page in pages)
            {
                page.Destroy(_device);
            }
        }
    }

    /// <summary>Forgets every live page without touching the device; see <see cref="DestroyPages"/>.</summary>
    public void Clear() => _batchPages.Clear();
}
