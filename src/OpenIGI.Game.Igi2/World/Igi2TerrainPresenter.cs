using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Draws I.G.I. 2's ground: one additive pass per material, over the tiles a level declares.
/// </summary>
/// <remarks>
/// <para>
/// The state this reproduces is the original's, read from <c>igi2ed.exe</c> rather than chosen:
/// <c>TerrainRender_SetBlendState</c> (<c>0x66BCB0</c>) gives <c>SRCBLEND = SRCALPHA</c> with
/// <c>DESTBLEND = ONE</c>, so the passes accumulate; <c>TerrainRender_SetTextureState</c>
/// (<c>0x66B810</c>) modulates the material texture by the vertex diffuse and the detail texture onto
/// that; and the vertex diffuse carries the baked terrain light in RGB with the material weight in
/// alpha. <see cref="BlendMode.Additive"/> and <see cref="IGraphicsDevice.SetTextureDetail"/> are the
/// port's names for the same two things.
/// </para>
/// <para>
/// <b>A vertex buffer per material, which is also the original's shape.</b> A vertex's alpha is its
/// weight <em>for one material</em>, so it cannot be shared between passes — and
/// <c>TerrainRender_AllocateMaterialVertexBuffers</c> / <c>...UnlockMaterialVertexBuffers</c> say the
/// original reached the same conclusion. Each chunk therefore keeps only the vertices its own triangles
/// touch, compacted, so the alpha is free to mean one material's weight.
/// </para>
/// <para>
/// Positions are stored relative to their tile's centre and drawn through
/// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>, which folds the camera in at double
/// precision. I.G.I. 2's coordinates reach millions of units, and the original solves the same problem
/// the same way — <c>TerrainRender_SetVertexShaderConstants</c> builds its world matrix about the
/// viewer.
/// </para>
/// <para>
/// <b>Two modes, and the constructor chooses.</b> Given
/// <see cref="Igi2TerrainPresenter(IGraphicsDevice, Igi2TerrainSurface, Igi2TerrainLodSettings, Func{string, TextureHandle}, IIgiLogger?)">
/// a level's detail controls</see> it runs <see cref="Igi2TerrainLod"/> every frame and streams the
/// result, which is what the original does — <c>TerrainRender_Draw</c> locks its vertex and index
/// buffers at the top of the frame and refills them from the quadtree. Without them it uploads every
/// tile at full detail once and draws all of it forever, which is correct and slow: 13,271,214
/// triangles across the corpus.
/// </para>
/// <para>
/// The full-detail path is kept rather than deleted because it is the reference the tessellated one is
/// measured against — a quadtree that drops geometry is a bug that only shows as a comparison.
/// </para>
/// </remarks>
public sealed class Igi2TerrainPresenter : ITerrainPresenter
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.terrain";

    /// <summary>The most indices one chunk holds before the next is started.</summary>
    /// <remarks>
    /// The original's own budget. <c>TerrainRender_DrawFace</c> (<c>igi2ed 0x66AAB0</c>) keeps appending
    /// to a render chunk while its index count is <c>&lt;= 9997</c> and calls
    /// <c>TerrainRender_AllocateRenderChunk</c> when it is not, so a chunk holds at most 10,000 indices.
    /// Rounded down to a multiple of three here, which the original gets for free by appending three at
    /// a time.
    /// <para>
    /// It also settles a problem the device would otherwise have: indices are 16-bit, and a 512-sample
    /// tile drawn as one pass would want 262,144 vertices — four times what an index can name. Chunking
    /// at the original's budget keeps a chunk to 9,999 vertices at the very worst.
    /// </para>
    /// </remarks>
    public const int MaximumIndicesPerChunk = 9999;

    private readonly IGraphicsDevice _device;
    private readonly List<Chunk> _chunks = [];
    private readonly List<Tile> _tiles = [];
    private readonly List<MeshHandle> _pool = [];
    private readonly Igi2TerrainLodSettings _settings;
    private readonly bool _tessellates;

    private readonly Dictionary<int, ushort> _remap = [];
    private readonly List<Vertex3D> _vertices = [];
    private readonly List<ushort> _compact = [];

    private int _poolUsed;
    private bool _disposed;

    /// <summary>Builds the ground of one level at full detail, with no quadtree.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="surface">The level's tiles and materials.</param>
    /// <param name="textures">
    /// Resolves a terrain material's texture name to a handle, returning
    /// <see cref="TextureHandle.None"/> for one the level does not ship.
    /// </param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The textures arrive through a delegate rather than an archive, because which archive a name lives
    /// in is the mission loader's business and resolving it here would make this type need one.
    /// </remarks>
    public Igi2TerrainPresenter(
        IGraphicsDevice device,
        Igi2TerrainSurface surface,
        Func<string, TextureHandle> textures,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        IIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2TerrainTile tile in surface.Tiles)
        {
            Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials);
            foreach (Igi2TerrainPass pass in mesh.Passes)
            {
                if (!surface.Materials.TryGetValue(pass.MaterialId, out TerrainMaterialTask? material))
                {
                    continue;
                }

                Upload(mesh, pass, tile.Centre, textures(material.Texture), textures(material.DetailTexture));
            }
        }

        TriangleCount = _chunks.Sum(static c => c.IndexCount) / 3;
        ChunkCount = _chunks.Count;
        log.Information(LogCategory, $"Terrain: {_chunks.Count} chunk(s), {TriangleCount} triangles.");
    }

    /// <summary>Builds the ground of one level and tessellates it per frame.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="surface">The level's tiles and materials.</param>
    /// <param name="settings">
    /// The level's <c>Detail</c> and <c>Adaption</c>, from <see cref="Igi2TerrainLodSettings.Read"/>.
    /// </param>
    /// <param name="textures">Resolves a terrain material's texture name to a handle.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Nothing is uploaded here beyond the height pyramids the culling test needs. The geometry is built
    /// by <see cref="Draw"/> into storage meshes taken from a pool that only ever grows, which is the
    /// port's shape of <c>TerrainRender_tRenderChunkPool</c> — fifty chunks of 10,000 indices that the
    /// original flushes and reuses within a frame.
    /// </remarks>
    public Igi2TerrainPresenter(
        IGraphicsDevice device,
        Igi2TerrainSurface surface,
        Igi2TerrainLodSettings settings,
        Func<string, TextureHandle> textures,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        _settings = settings;
        _tessellates = true;
        IIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2TerrainTile tile in surface.Tiles)
        {
            Dictionary<int, (TextureHandle Diffuse, TextureHandle Detail)> resolved = [];
            foreach ((int id, TerrainMaterialTask material) in surface.Materials)
            {
                resolved[id] = (textures(material.Texture), textures(material.DetailTexture));
            }

            _tiles.Add(new Tile(new Igi2TerrainLod(tile, surface.Materials), tile.Centre, resolved));
        }

        log.Information(
            LogCategory,
            $"Terrain: {_tiles.Count} tile(s) tessellated per frame, detail {settings.Detail}, adaption {settings.Adaption}.");
    }

    /// <summary>Gets how many triangles the ground draws.</summary>
    /// <remarks>
    /// For the full-detail path this is fixed at construction. For the tessellated one it is what the
    /// last <see cref="Draw"/> issued, and it moves with the camera.
    /// </remarks>
    public int TriangleCount { get; private set; }

    /// <summary>Gets how many chunks the ground was drawn as.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Gets how many quadtree leaves the last frame emitted, or zero at full detail.</summary>
    public int SquaresDrawn { get; private set; }

    /// <summary>Gets how many quadtree squares the clipping planes rejected last frame.</summary>
    public int SquaresCulled { get; private set; }

    /// <summary>Gets a value indicating whether the ground is tessellated per frame.</summary>
    public bool IsTessellated => _tessellates;

    /// <inheritdoc/>
    public string Summary => _tessellates
        ? $"{ChunkCount} chunk(s), {TriangleCount} triangles, {SquaresDrawn} squares"
        : $"{_chunks.Count} chunk(s), {TriangleCount} triangles";

    /// <inheritdoc/>
    /// <remarks>
    /// The tessellated path reads the frame's camera and projection, because that is what the quadtree
    /// is a function of — <c>TerrainRender_Draw</c> measures every edge against the eye and
    /// <c>TerrainRender_SetupClippingPlanes</c> builds its four half-spaces out of the projection's half
    /// angles. The full-detail path ignores both rather than pretending to a selectivity it does not
    /// have.
    /// </remarks>
    public void Draw(in TerrainFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.SetCullMode(CullMode.Clockwise);
        if (!_tessellates)
        {
            foreach (Chunk chunk in _chunks)
            {
                // Stage 0 is the material, stage 1 the detail map modulated onto it -- the two stages
                // TerrainRender_SetTextureState configures.
                _device.SetTextureDetail(chunk.Diffuse, chunk.Detail);
                _device.DrawMesh(
                    chunk.Mesh,
                    0,
                    chunk.IndexCount,
                    frame.Camera.CreateModelView(chunk.Origin, Matrix3f.Identity),
                    chunk.Diffuse,
                    TextureHandle.None,
                    BlendMode.Additive);
            }

            return;
        }

        Igi2TerrainView view = Igi2TerrainView.FromFrame(frame);
        _poolUsed = 0;

        int triangles = 0;
        int drawn = 0;
        int culled = 0;

        foreach (Tile tile in _tiles)
        {
            Igi2TerrainTessellation tessellation = tile.Lod.Tessellate(view, _settings);
            drawn += tessellation.SquaresDrawn;
            culled += tessellation.SquaresCulled;

            Matrix4f modelView = frame.Camera.CreateModelView(tile.Centre, Matrix3f.Identity);
            foreach (Igi2TerrainPass pass in tessellation.Mesh.Passes)
            {
                if (!tile.Textures.TryGetValue(pass.MaterialId, out (TextureHandle Diffuse, TextureHandle Detail) texture))
                {
                    continue;
                }

                triangles += Stream(tessellation.Mesh, pass, tile.Centre, texture, modelView);
            }
        }

        TriangleCount = triangles;
        ChunkCount = _poolUsed;
        SquaresDrawn = drawn;
        SquaresCulled = culled;
    }

    /// <inheritdoc/>
    public void Describe(ICollection<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Add($"igi2 terrain: {ChunkCount} chunk(s)");
        into.Add(_tessellates
            ? $"igi2 terrain: {TriangleCount} triangles, {SquaresDrawn} squares, {SquaresCulled} culled"
            : $"igi2 terrain: {TriangleCount} triangles, drawn without level of detail");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Chunk chunk in _chunks)
        {
            _device.DestroyMesh(chunk.Mesh);
        }

        foreach (MeshHandle mesh in _pool)
        {
            _device.DestroyMesh(mesh);
        }

        _chunks.Clear();
        _pool.Clear();
        _tiles.Clear();
    }

    private static Vertex3D Build(Igi2TerrainMesh mesh, Igi2TerrainPass pass, int index, Vector3d centre)
    {
        Vector3d world = mesh.Positions[index];
        (float U, float V) diffuseUv = Igi2TerrainMesh.TextureCoordinate(pass.TextureScale, world);
        (float U, float V) detailUv = Igi2TerrainMesh.TextureCoordinate(pass.DetailTextureScale, world);
        Bgra32 light = mesh.Lights[index];

        return new Vertex3D(
            new Vector3f(
                (float)(world.X - centre.X),
                (float)(world.Y - centre.Y),
                (float)(world.Z - centre.Z)),
            Vector3f.UnitZ,
            diffuseUv.U,
            diffuseUv.V,
            detailUv.U,
            detailUv.V,
            // RGB is the baked terrain light; alpha is this material's weight, which is what the
            // additive blend scales the pass by. The quadtree makes that weight fractional: a vertex
            // half-morphed between two levels carries a share of each material its parents had.
            new Bgra32(
                light.B,
                light.G,
                light.R,
                (byte)Math.Clamp((int)MathF.Round(pass.Weights.Span[index] * 255f), 0, 255)));
    }

    private int Stream(
        Igi2TerrainMesh mesh,
        Igi2TerrainPass pass,
        Vector3d centre,
        (TextureHandle Diffuse, TextureHandle Detail) texture,
        in Matrix4f modelView)
    {
        ReadOnlySpan<int> indices = pass.Indices.Span;
        int triangles = 0;

        _remap.Clear();
        _vertices.Clear();
        _compact.Clear();

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (_compact.Count + 3 > MaximumIndicesPerChunk)
            {
                triangles += Flush(texture, modelView);
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int index = indices[i + corner];
                if (!_remap.TryGetValue(index, out ushort mapped))
                {
                    mapped = (ushort)_vertices.Count;
                    _remap[index] = mapped;
                    _vertices.Add(Build(mesh, pass, index, centre));
                }

                _compact.Add(mapped);
            }
        }

        return triangles + Flush(texture, modelView);
    }

    private int Flush((TextureHandle Diffuse, TextureHandle Detail) texture, in Matrix4f modelView)
    {
        if (_compact.Count == 0)
        {
            return 0;
        }

        if (_poolUsed == _pool.Count)
        {
            _pool.Add(_device.CreateMeshStorage(MaximumIndicesPerChunk, MaximumIndicesPerChunk));
        }

        MeshHandle mesh = _pool[_poolUsed++];
        _device.UploadMeshRange(
            mesh,
            0,
            CollectionsMarshal.AsSpan(_vertices),
            0,
            CollectionsMarshal.AsSpan(_compact));

        _device.SetTextureDetail(texture.Diffuse, texture.Detail);
        _device.DrawMesh(
            mesh,
            0,
            _compact.Count,
            modelView,
            texture.Diffuse,
            TextureHandle.None,
            BlendMode.Additive);

        int triangles = _compact.Count / 3;
        _remap.Clear();
        _vertices.Clear();
        _compact.Clear();
        return triangles;
    }

    private void Upload(
        Igi2TerrainMesh mesh,
        Igi2TerrainPass pass,
        Vector3d centre,
        TextureHandle diffuse,
        TextureHandle detail)
    {
        ReadOnlySpan<int> indices = pass.Indices.Span;

        // Only the vertices this chunk's own triangles touch, compacted, so the alpha can carry this
        // material's weight and the indices stay 16-bit.
        Dictionary<int, ushort> remap = [];
        List<Vertex3D> vertices = [];
        List<ushort> compact = [];

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (compact.Count + 3 > MaximumIndicesPerChunk)
            {
                Store(vertices, compact, centre, diffuse, detail);
                remap.Clear();
                vertices.Clear();
                compact.Clear();
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int index = indices[i + corner];
                if (!remap.TryGetValue(index, out ushort mapped))
                {
                    mapped = (ushort)vertices.Count;
                    remap[index] = mapped;
                    vertices.Add(Build(mesh, pass, index, centre));
                }

                compact.Add(mapped);
            }
        }

        Store(vertices, compact, centre, diffuse, detail);
    }

    private void Store(
        List<Vertex3D> vertices,
        List<ushort> indices,
        Vector3d centre,
        TextureHandle diffuse,
        TextureHandle detail)
    {
        if (indices.Count == 0)
        {
            return;
        }

        _chunks.Add(new Chunk(
            _device.CreateMesh(vertices.ToArray(), indices.ToArray()),
            indices.Count,
            centre,
            diffuse,
            detail));
    }

    private readonly record struct Chunk(
        MeshHandle Mesh,
        int IndexCount,
        Vector3d Origin,
        TextureHandle Diffuse,
        TextureHandle Detail);

    private readonly record struct Tile(
        Igi2TerrainLod Lod,
        Vector3d Centre,
        Dictionary<int, (TextureHandle Diffuse, TextureHandle Detail)> Textures);
}
