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
/// <b>No level of detail.</b> The original draws through a quadtree that retessellates with distance
/// and morphs vertices across the transition. This draws every tile at full detail, which is correct
/// and slow — 13 million triangles across the corpus — and is the next thing to fix rather than
/// something the geometry gets wrong.
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
    private bool _disposed;

    /// <summary>Builds the ground of one level.</summary>
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
        log.Information(LogCategory, $"Terrain: {_chunks.Count} chunk(s), {TriangleCount} triangles.");
    }

    /// <summary>Gets how many triangles the ground draws, before any culling.</summary>
    public int TriangleCount { get; }

    /// <summary>Gets how many chunks the ground was uploaded as.</summary>
    public int ChunkCount => _chunks.Count;

    /// <inheritdoc/>
    public string Summary => $"{_chunks.Count} chunk(s), {TriangleCount} triangles";

    /// <inheritdoc/>
    /// <remarks>
    /// Every chunk, every frame. The frame's view distance and culling flags are read by the original's
    /// quadtree, which this does not have; honouring them here would mean pretending to a selectivity it
    /// does not implement.
    /// </remarks>
    public void Draw(in TerrainFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _device.SetCullMode(CullMode.Clockwise);
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
    }

    /// <inheritdoc/>
    public void Describe(ICollection<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Add($"igi2 terrain: {_chunks.Count} chunk(s)");
        into.Add($"igi2 terrain: {TriangleCount} triangles, drawn without level of detail");
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

        _chunks.Clear();
    }

    private void Upload(
        Igi2TerrainMesh mesh,
        Igi2TerrainPass pass,
        Vector3d centre,
        TextureHandle diffuse,
        TextureHandle detail)
    {
        ReadOnlySpan<int> indices = pass.Indices.Span;
        ReadOnlySpan<float> weights = pass.Weights.Span;

        // Only the vertices this chunk's own triangles touch, compacted, so the alpha can carry this
        // material's weight and the indices stay 16-bit.
        Dictionary<int, ushort> remap = [];
        List<Vertex3D> vertices = [];
        List<ushort> compact = [];

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (compact.Count + 3 > MaximumIndicesPerChunk)
            {
                Flush(vertices, compact, centre, diffuse, detail);
                remap.Clear();
                vertices.Clear();
                compact.Clear();
            }

            for (int corner = 0; corner < 3; corner++)
            {
                int index = indices[i + corner];
                if (!remap.TryGetValue(index, out ushort mapped))
                {
                    Vector3d world = mesh.Positions[index];
                    (float U, float V) diffuseUv = Igi2TerrainMesh.TextureCoordinate(pass.TextureScale, world);
                    (float U, float V) detailUv = Igi2TerrainMesh.TextureCoordinate(pass.DetailTextureScale, world);
                    Bgra32 light = mesh.Lights[index];

                    mapped = (ushort)vertices.Count;
                    remap[index] = mapped;
                    vertices.Add(new Vertex3D(
                        new Vector3f(
                            (float)(world.X - centre.X),
                            (float)(world.Y - centre.Y),
                            (float)(world.Z - centre.Z)),
                        Vector3f.UnitZ,
                        diffuseUv.U,
                        diffuseUv.V,
                        detailUv.U,
                        detailUv.V,
                        // RGB is the baked terrain light; alpha is this material's weight, which is what
                        // the additive blend scales the pass by.
                        new Bgra32(light.B, light.G, light.R, (byte)(weights[index] * 255f))));
                }

                compact.Add(mapped);
            }
        }

        Flush(vertices, compact, centre, diffuse, detail);
    }

    private void Flush(
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
}
