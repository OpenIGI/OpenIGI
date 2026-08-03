using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

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
    private readonly List<ChunkKey> _evictions = [];
    private readonly TextureHandle[] _tiles;
    private long _frame;
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
    /// <exception cref="ObjectDisposedException">The renderer has been disposed.</exception>
    public void Draw(
        in Camera3D camera,
        in Matrix4f projection,
        float verticalFieldOfView,
        float aspectRatio,
        int viewDistance = 0)
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
        TerrainTraversalResult result = TerrainTraversal.Collect(_terrain.Octree, view, _chunks);

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
        bool fading = false;

        int triangles = 0;
        int passes = 0;
        foreach (TerrainChunk chunk in _chunks)
        {
            if (!TryGetMesh(chunk, out Entry? entry) || !entry.Mesh.IsValid)
            {
                continue;
            }

            Matrix4f modelView = camera.CreateModelView(new Vector3d(
                chunk.Position.X / WorldScene.UnitsPerMetre,
                chunk.Position.Y / WorldScene.UnitsPerMetre,
                chunk.Position.Z / WorldScene.UnitsPerMetre));

            foreach (TerrainChunkLayer layer in entry.Layers)
            {
                // Every terrain pass morphs its seams with the LOD fade; the detail passes additionally
                // fade their alpha in with proximity. The fade scale is per chunk because the window is
                // measured in the chunk's own morph units.
                _device.SetDetailFade(
                    (float)(0.375 * WorldScene.UnitsPerMetre / (1 << (25 - chunk.LodExponent))),
                    fadeOffset,
                    fadeSlope,
                    layer.IsDetail);
                fading = true;

                // The base pass fills the chunk and owns the depth; the rest fade in over it, which is
                // what makes one ground type meet another without a seam. They must not write depth —
                // an overlay is transparent over most of its own chunk, and a transparent fragment that
                // writes depth hides whatever is behind it.
                _device.SetDepthState(layer.IsBase ? DepthState.TestAndWrite : DepthState.TestOnly);
                _device.DrawMesh(
                    entry.Mesh,
                    layer.IndexOffset,
                    layer.IndexCount,
                    modelView,
                    TileTexture(layer.TileIndex),
                    TextureHandle.None,
                    layer.IsBase ? BlendMode.Opaque : BlendMode.AlphaBlend);

                triangles += layer.TriangleCount;
                passes++;
            }
        }

        if (fading)
        {
            _device.ClearDetailFade();
        }

        LastChunkCount = result.Emitted;
        LastPassCount = passes;
        LastTriangleCount = triangles;
        LastVisitedCount = result.Visited;

        if (result.Dropped > 0)
        {
            _logger.Warning(LogCategory, $"{result.Dropped} chunk(s) did not fit the visible list.");
        }

        Trim();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Entry entry in _cache.Values)
        {
            _device.DestroyMesh(entry.Mesh);
        }

        foreach (TextureHandle tile in _tiles)
        {
            _device.DestroyTexture(tile);
        }

        _cache.Clear();
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

    private bool TryGetMesh(in TerrainChunk chunk, [NotNullWhen(true)] out Entry? entry)
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

        MeshHandle mesh = MeshHandle.None;
        try
        {
            mesh = _device.CreateMesh(geometry.Vertices, geometry.Indices);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _logger.Warning(LogCategory, $"A chunk at {chunk.Position} did not upload: {ex.Message}");
            return false;
        }

        entry = new Entry(mesh, geometry.Layers) { LastUsedFrame = _frame };
        _cache[key] = entry;
        return true;
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

        foreach (ChunkKey key in _evictions)
        {
            _device.DestroyMesh(_cache[key].Mesh);
            _cache.Remove(key);
        }
    }

    private readonly record struct ChunkKey(long X, long Y, long Z, int Lod);

    private sealed class Entry(MeshHandle mesh, IReadOnlyList<TerrainChunkLayer> layers)
    {
        public MeshHandle Mesh { get; } = mesh;

        public IReadOnlyList<TerrainChunkLayer> Layers { get; } = layers;

        public long LastUsedFrame { get; set; }
    }
}
