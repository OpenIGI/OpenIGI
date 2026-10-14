using System.Runtime.InteropServices;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
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
/// <see cref="Igi2TerrainPresenter(IGraphicsDevice, Igi2TerrainSurface, Igi2TerrainLodSettings, Func{string, TextureHandle}, IOpenIgiLogger?)">
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

    /// <summary>How many chunk pools take turns, one per frame.</summary>
    /// <remarks>
    /// <para>
    /// <b>A chunk cannot be rewritten while a frame in flight is still reading it.</b> A storage mesh's
    /// buffers are host-visible and <c>UploadMeshRange</c> writes them directly, with no staging copy
    /// and no fence: the contract is that callers <em>append</em>, so the bytes a submitted frame reads
    /// are never touched again. The tessellated ground is the one caller that does not append — it
    /// refills the same chunks from offset zero every frame — so with two frames in flight the geometry
    /// the GPU was drawing was being overwritten underneath it, and triangles came out built from two
    /// frames' vertices at once: flat sheets and black wedges hanging off the ground, appearing only
    /// while the camera moved, because a still camera rewrites the same bytes.
    /// </para>
    /// <para>
    /// One more than <c>VulkanFrameLoop.MaxFramesInFlight</c>, which is the same ring
    /// <c>TerrainShadowCaster</c> takes for the same reason: a pool written now was last submitted three
    /// frames ago, and those have retired. It costs two more copies of the chunks actually used — about
    /// half a megabyte each — rather than a stall.
    /// </para>
    /// </remarks>
    public const int ChunkPoolSlots = 3;

    private readonly IGraphicsDevice _device;
    private readonly List<Chunk> _chunks = [];
    private readonly List<Tile> _tiles = [];
    private readonly List<MeshHandle>[] _pools = [.. Enumerable.Range(0, ChunkPoolSlots).Select(_ => new List<MeshHandle>())];
    private readonly Igi2TerrainLodSettings _settings;
    private readonly bool _tessellates;

    private readonly Dictionary<int, ushort> _remap = [];
    private readonly List<Vertex3D> _vertices = [];
    private readonly List<ushort> _compact = [];

    private readonly List<(Tile Tile, Igi2TerrainTessellation Tessellation, Matrix4f ModelView)> _ready = [];
    private int _poolSlot;
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
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2TerrainTile tile in surface.Tiles)
        {
            Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);
            foreach (Igi2TerrainPass pass in mesh.Passes)
            {
                if (!surface.Materials.TryGetValue(pass.MaterialId, out TerrainMaterialTask? material))
                {
                    continue;
                }

                Upload(mesh, pass, tile.Centre, textures(material.Texture), textures(material.DetailTexture));
            }

            // And the tile's bake, over the lot: the original's mode 3, drawn once for every triangle
            // the material passes established.
            TextureHandle lightmap = CreateLightmap(device, tile);
            if (lightmap.IsValid)
            {
                UploadLightmap(mesh, tile, lightmap);
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
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        _settings = settings;
        _tessellates = true;
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2TerrainTile tile in surface.Tiles)
        {
            Dictionary<int, (TextureHandle Diffuse, TextureHandle Detail)> resolved = [];
            foreach ((int id, TerrainMaterialTask material) in surface.Materials)
            {
                resolved[id] = (textures(material.Texture), textures(material.DetailTexture));
            }

            _tiles.Add(new Tile(
                new Igi2TerrainLod(tile, surface.Materials, surface.Discards),
                tile.Centre,
                CreateLightmap(device, tile),
                tile.LightmapProjection(),
                resolved));
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

        // NO ALPHA TEST. TerrainRender_SetGenericState (igi2ed 0x66BAD0) writes
        // D3DRS_ALPHATESTENABLE = 0 for the ground and nothing turns it back on, and the ground is the
        // one surface in the game that cannot afford one: alpha here is not a cut-out, it is the
        // material's SHARE of the pixel. The passes blend SRCALPHA onto ONE and their weights sum to
        // exactly one at every vertex, so the ground is fully painted only if every pass draws its
        // share however small.
        //
        // Left at the port's default of 128 the test threw away every share under a half. A pixel is
        // then black wherever no single material owns most of it -- which is every place three
        // materials meet, and every two-material blend near even -- and it moved with the camera,
        // because the quadtree morphs the weights as it subdivides. It read as holes torn in the
        // ground, one to two metres across, in the shape of the boundaries between grass, rock and
        // scree.
        // The second texture is the material's DETAIL map, multiplied into the ground's colour at its
        // own tiling rate -- stage 1's MODULATE at TEXCOORDINDEX 1 (TerrainRender_SetTextureState,
        // igi2ed 0x66B810), bound by TerrainRender_SetMaterialTextures (0x667330) beside the diffuse.
        // The forward path multiplies its lightmap slot into the colour already, so binding it there is
        // the whole of it; the deferred path keeps that slot for an object's bake, and this is what
        // tells it this one is a colour instead.
        _device.SetAlphaReference(0);
        _device.SetLightmapModulates(true);
        try
        {
            DrawGround(frame);
        }
        finally
        {
            _device.SetFogTarget(FogTarget.Scene);
            _device.SetLightmapModulates(false);
            _device.SetAlphaReference(IGraphicsDevice.DefaultAlphaReference);
        }
    }

    /// <summary>The colour a ground pass fogs towards, as the original's state block for its mode writes it.</summary>
    /// <remarks>
    /// <c>TerrainRender_SetBlendMode</c> (<c>igi2ed 0x666B90</c>): modes 1 and 2 — the base and the
    /// overlays — set <c>D3DRS_FOGCOLOR</c> to the fog colour times 127; mode 3 — the lightmap — sets it
    /// to <c>0xFFFFFFFF</c>. The lightmap pass doubles what is under it, so at distance the three land
    /// on <c>2 × (fog / 2) × 1</c>, the fog; nearer in the bake is fogged to the identity before the
    /// texture is fogged to the haze, which is what keeps far rock textured. Fogging every pass to the
    /// scene's colour instead multiplied the fog into itself, and the far hills went to fog squared —
    /// a third the brightness the original draws them at.
    /// </remarks>
    private static FogTarget FogTargetOf(ChunkPass pass) =>
        pass == ChunkPass.Lightmap ? FogTarget.White : FogTarget.MaterialWeightedHalfScene;

    private void DrawGround(in TerrainFrame frame)
    {
        if (!_tessellates)
        {
            // The original's three, in its order: every base, then every overlay, then every lightmap.
            // Globally rather than per chunk -- an opaque base written after a neighbouring pass had
            // already added its share would erase it, and a lightmap multiplied in before the shares
            // were finished would be multiplied by half a ground.
            for (int phase = 0; phase < 3; phase++)
            {
                ChunkPass which = (ChunkPass)phase;
                _device.SetDepthState(which == ChunkPass.Base ? DepthState.TestAndWrite : DepthState.TestOnly);
                _device.SetFogTarget(FogTargetOf(which));
                foreach (Chunk chunk in _chunks)
                {
                    if (chunk.Pass != which || chunk.IndexCount == 0)
                    {
                        continue;
                    }

                    // Stage 0 is the material, stage 1 the detail map modulated onto it -- the two
                    // stages TerrainRender_SetTextureState configures. The lightmap pass carries the
                    // tile's bake in stage 0 instead and no second texture at all.
                    _device.DrawMesh(
                        chunk.Mesh,
                        0,
                        chunk.IndexCount,
                        frame.Camera.CreateModelView(WorldScene.ToMetres(chunk.Origin), Matrix3f.Identity),
                        chunk.Diffuse,
                        chunk.Detail,
                        which switch
                        {
                            ChunkPass.Base => BlendMode.Opaque,
                            ChunkPass.Overlay => BlendMode.Additive,
                            _ => BlendMode.Modulate2X,
                        });
                }
            }

            _device.SetDepthState(DepthState.TestAndWrite);
            return;
        }

        // The quadtree runs in engine units, because that is the space its thresholds are authored in:
        // the level's Detail and Adaption scale a squared edge length measured against the eye, and the
        // original measures it in the same units the height field is stored in. So the eye is converted
        // rather than the terrain -- which is what Project I.G.I.'s octree traversal does too.
        Igi2TerrainView view = Igi2TerrainView.FromFrame(
            frame with { Camera = new Camera3D(WorldScene.ToUnits(frame.Camera.Position), frame.Camera.Orientation) });

        // Onto the pool the frames still in flight are not reading -- see ChunkPoolSlots.
        _poolSlot = (_poolSlot + 1) % ChunkPoolSlots;
        _poolUsed = 0;

        int triangles = 0;
        int drawn = 0;
        int culled = 0;

        _ready.Clear();
        foreach (Tile tile in _tiles)
        {
            Igi2TerrainTessellation tessellation = tile.Lod.Tessellate(view, _settings);
            drawn += tessellation.SquaresDrawn;
            culled += tessellation.SquaresCulled;
            _ready.Add((tile, tessellation, frame.Camera.CreateModelView(WorldScene.ToMetres(tile.Centre), Matrix3f.Identity)));
        }

        // The original's three passes, in its order: every tile's bases, then every tile's overlays,
        // then every tile's lightmap. Globally rather than per tile -- an opaque base written after a
        // neighbour had already added its share would erase it, and a lightmap multiplied in before the
        // shares were finished would be multiplied by half a ground.
        for (int phase = 0; phase < 3; phase++)
        {
            ChunkPass which = (ChunkPass)phase;
            _device.SetDepthState(which == ChunkPass.Base ? DepthState.TestAndWrite : DepthState.TestOnly);
            _device.SetFogTarget(FogTargetOf(which));
            foreach ((Tile tile, Igi2TerrainTessellation tessellation, Matrix4f modelView) in _ready)
            {
                if (which == ChunkPass.Lightmap)
                {
                    if (tile.Lightmap.IsValid)
                    {
                        triangles += StreamLightmap(tessellation.Mesh, tile, modelView);
                    }

                    continue;
                }

                foreach (Igi2TerrainPass pass in tessellation.Mesh.Passes)
                {
                    if (!tile.Textures.TryGetValue(pass.MaterialId, out (TextureHandle Diffuse, TextureHandle Detail) texture))
                    {
                        continue;
                    }

                    bool bases = which == ChunkPass.Base;
                    triangles += Stream(
                        tessellation.Mesh,
                        pass,
                        bases ? pass.BaseIndices : pass.OverlayIndices,
                        bases ? BlendMode.Opaque : BlendMode.Additive,
                        tile.Centre,
                        texture,
                        modelView);
                }
            }
        }

        _device.SetDepthState(DepthState.TestAndWrite);
        _ready.Clear();

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

        foreach (List<MeshHandle> pool in _pools)
        {
            foreach (MeshHandle mesh in pool)
            {
                _device.DestroyMesh(mesh);
            }

            pool.Clear();
        }

        _chunks.Clear();
        _tiles.Clear();
    }

    /// <summary>Uploads the full-detail lightmap pass: every triangle once, at the tile's projection.</summary>
    private void UploadLightmap(Igi2TerrainMesh mesh, Igi2TerrainTile tile, TextureHandle lightmap)
    {
        (float ScaleX, float ScaleY, float OffsetX, float OffsetY) projection = tile.LightmapProjection();
        Dictionary<int, ushort> remap = [];
        List<Vertex3D> vertices = [];
        List<ushort> compact = [];

        // The terrain map's own pass, which the original issues for every face it did not discard --
        // materials or none. Walking the material passes instead dropped the faces no material covers.
        {
            ReadOnlySpan<int> indices = mesh.GroundIndices.Span;
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (compact.Count + 3 > MaximumIndicesPerChunk)
                {
                    Store(vertices, compact, ChunkPass.Lightmap, tile.Centre, lightmap, TextureHandle.None);
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
                        vertices.Add(BuildLightmapVertex(mesh, index, tile.Centre, projection));
                    }

                    compact.Add(mapped);
                }
            }
        }

        Store(vertices, compact, ChunkPass.Lightmap, tile.Centre, lightmap, TextureHandle.None);
    }

    /// <summary>Uploads one tile's baked lightmap as a texture.</summary>
    /// <remarks>
    /// Clamped, because the original's lightmap pass sets <c>ADDRESSU</c> and <c>ADDRESSV</c> to
    /// <c>D3DTADDRESS_CLAMP</c> (<c>TerrainRender_SetBlendMode</c> case 3, <c>igi2ed 0x666B90</c>): the
    /// quadtree's root reaches half a texel past the map on two sides, and wrapping there would fetch
    /// the far edge's bake and draw a bright seam along the tile.
    /// </remarks>
    private static TextureHandle CreateLightmap(IGraphicsDevice device, Igi2TerrainTile tile)
    {
        if (tile.Lightmap is not { } lightmap)
        {
            return TextureHandle.None;
        }

        int width = lightmap.Width;
        int height = lightmap.Height;
        Bgra32[] pixels = new Bgra32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = lightmap.GetLight(x, y);
            }
        }

        return device.CreateTexture(new TextureDescription(width, height), pixels);
    }

    private static Vertex3D Build(Igi2TerrainMesh mesh, Igi2TerrainPass pass, int index, Vector3d centre)
    {
        Vector3d world = mesh.Positions[index];
        (float U, float V) diffuseUv = Igi2TerrainMesh.TextureCoordinate(pass.TextureScale, world);
        (float U, float V) detailUv = Igi2TerrainMesh.TextureCoordinate(pass.DetailTextureScale, world);
        Vector3f normal = mesh.Normals[index];

        // THE WEIGHT IS FOLDED INTO THE COLOUR, not left in the alpha for a blend factor to apply.
        //
        // The original keeps this weight in a vertex stream of its own -- stream 1, four bytes a vertex,
        // one buffer per material (TerrainRender_DrawRenderChunk_, igi2ed 0x667400) -- and applies it
        // through the blender: TerrainRender_SetBlendMode (0x666B90) sets SRCBLEND = SRCALPHA for both
        // of the ground's modes, with DESTBLEND ZERO for the base pass and ONE for the ones that add.
        // So in the original every terrain pass is scaled by its share whatever else it does.
        //
        // The port cannot lean on that, because its blend modes do not all carry a source-alpha factor:
        // the deferred G-buffer's additive is ONE/ONE and every path's opaque is ONE/ZERO. Left in the
        // alpha the share was therefore applied on the vanilla path and DROPPED on the deferred one,
        // where each of a tile's five to seven materials then added its texture at full strength. That
        // is what made the remastered ground a pale wash with no material in it: not a lighting fault,
        // an unweighted sum of every texture the tile paints.
        //
        // Multiplied into the light instead, it survives every blend the port uses and comes out as the
        // original's own product -- texture * light * share -- on all four combinations.
        float weight = Math.Clamp(pass.Weights.Span[index], 0f, 1f);

        // THE BAKE IS NOT HERE. It is a texture over the whole ground, drawn as its own pass — see
        // LightmapPass and Igi2TerrainTile.LightmapProjection. Reading it per sample, which is what this
        // vertex used to carry, threw nearly all of it away: the quadtree's vertices are the tile's
        // samples only where the camera is standing, and hundreds of metres apart at the far end of a
        // level, so a bake authored every three metres arrived as one value per hillside.

        return new Vertex3D(
            new Vector3f(
                (float)((world.X - centre.X) / WorldScene.UnitsPerMetre),
                (float)((world.Y - centre.Y) / WorldScene.UnitsPerMetre),
                (float)((world.Z - centre.Z) / WorldScene.UnitsPerMetre)),

            // The original's ground has no normal at all: its vertex is sixteen bytes of x, y, z and a
            // fog factor (TerrainRender_CalculateRenderVertex, igi2ed 0x66A740), and it is lit by the
            // tile's lightmap rather than by a light. The deferred path needs one anyway -- see
            // Igi2TerrainTile.NormalAt.
            normal,
            diffuseUv.U,
            diffuseUv.V,
            detailUv.U,
            detailUv.V,
            new Bgra32(
                Share(weight),
                Share(weight),
                Share(weight),
                255));
    }

    private static byte Share(float weight) => (byte)Math.Clamp((int)MathF.Round(weight * 255f), 0, 255);

    /// <summary>Builds a vertex of the lightmap pass: the projection in place of the material's uv.</summary>
    private static Vertex3D BuildLightmapVertex(
        Igi2TerrainMesh mesh,
        int index,
        Vector3d centre,
        (float ScaleX, float ScaleY, float OffsetX, float OffsetY) projection)
    {
        Vector3d world = mesh.Positions[index];

        return new Vertex3D(
            new Vector3f(
                (float)((world.X - centre.X) / WorldScene.UnitsPerMetre),
                (float)((world.Y - centre.Y) / WorldScene.UnitsPerMetre),
                (float)((world.Z - centre.Z) / WorldScene.UnitsPerMetre)),
            mesh.Normals[index],
            (float)((world.X * projection.ScaleX) + projection.OffsetX),
            (float)((world.Y * projection.ScaleY) + projection.OffsetY),
            0f,
            0f,

            // White: this pass carries no share and no colour of its own. What it multiplies in is the
            // texture, and the doubling is the blend's.
            new Bgra32(255, 255, 255, 255));
    }

    private int Stream(
        Igi2TerrainMesh mesh,
        Igi2TerrainPass pass,
        ReadOnlySpan<int> indices,
        BlendMode blend,
        Vector3d centre,
        (TextureHandle Diffuse, TextureHandle Detail) texture,
        in Matrix4f modelView)
    {
        int triangles = 0;

        _remap.Clear();
        _vertices.Clear();
        _compact.Clear();

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (_compact.Count + 3 > MaximumIndicesPerChunk)
            {
                triangles += Flush(texture, blend, modelView);
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

        return triangles + Flush(texture, blend, modelView);
    }

    /// <summary>Streams one tile's lightmap pass: every leaf triangle once, multiplied over the ground.</summary>
    /// <remarks>
    /// The geometry is the union of the passes' BASE triangles, which is every triangle exactly once —
    /// each is base to the one pass that established it. Taking the passes' whole index lists instead
    /// would multiply the lightmap in again for every material a triangle straddles.
    /// </remarks>
    private int StreamLightmap(Igi2TerrainMesh mesh, Tile tile, in Matrix4f modelView)
    {
        int triangles = 0;
        (TextureHandle Diffuse, TextureHandle Detail) texture = (tile.Lightmap, TextureHandle.None);

        _remap.Clear();
        _vertices.Clear();
        _compact.Clear();

        // As above: the bake covers every face the ground draws, not only those a material claimed.
        {
            ReadOnlySpan<int> indices = mesh.GroundIndices.Span;
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (_compact.Count + 3 > MaximumIndicesPerChunk)
                {
                    triangles += Flush(texture, BlendMode.Modulate2X, modelView);
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int index = indices[i + corner];
                    if (!_remap.TryGetValue(index, out ushort mapped))
                    {
                        mapped = (ushort)_vertices.Count;
                        _remap[index] = mapped;
                        _vertices.Add(BuildLightmapVertex(mesh, index, tile.Centre, tile.Projection));
                    }

                    _compact.Add(mapped);
                }
            }
        }

        return triangles + Flush(texture, BlendMode.Modulate2X, modelView);
    }

    private int Flush(
        (TextureHandle Diffuse, TextureHandle Detail) texture,
        BlendMode blend,
        in Matrix4f modelView)
    {
        if (_compact.Count == 0)
        {
            return 0;
        }

        List<MeshHandle> pool = _pools[_poolSlot];
        if (_poolUsed == pool.Count)
        {
            pool.Add(_device.CreateMeshStorage(MaximumIndicesPerChunk, MaximumIndicesPerChunk));
        }

        MeshHandle mesh = pool[_poolUsed++];
        _device.UploadMeshRange(
            mesh,
            0,
            CollectionsMarshal.AsSpan(_vertices),
            0,
            CollectionsMarshal.AsSpan(_compact));

        _device.DrawMesh(
            mesh,
            0,
            _compact.Count,
            modelView,
            texture.Diffuse,
            texture.Detail,
            blend);

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
        Upload(mesh, pass, pass.BaseIndices, ChunkPass.Base, centre, diffuse, detail);
        Upload(mesh, pass, pass.OverlayIndices, ChunkPass.Overlay, centre, diffuse, detail);
    }

    private void Upload(
        Igi2TerrainMesh mesh,
        Igi2TerrainPass pass,
        ReadOnlySpan<int> indices,
        ChunkPass which,
        Vector3d centre,
        TextureHandle diffuse,
        TextureHandle detail)
    {
        // Only the vertices this chunk's own triangles touch, compacted, so the alpha can carry this
        // material's weight and the indices stay 16-bit.
        Dictionary<int, ushort> remap = [];
        List<Vertex3D> vertices = [];
        List<ushort> compact = [];

        for (int i = 0; i < indices.Length; i += 3)
        {
            if (compact.Count + 3 > MaximumIndicesPerChunk)
            {
                Store(vertices, compact, which, centre, diffuse, detail);
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

        Store(vertices, compact, which, centre, diffuse, detail);
    }

    private void Store(
        List<Vertex3D> vertices,
        List<ushort> indices,
        ChunkPass which,
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
            which,
            centre,
            diffuse,
            detail));
    }

    /// <summary>Which of the ground's three passes a chunk belongs to.</summary>
    /// <remarks>
    /// The original's own three, and its own order: <c>TerrainRender_DrawFace</c>
    /// (<c>igi2ed 0x66AAB0</c>) files a face under blend mode 1 for the first material that claims it,
    /// mode 2 for every other material that claims it, and mode 3 once for the tile's lightmap.
    /// </remarks>
    private enum ChunkPass
    {
        /// <summary>Mode 1: the material that establishes the pixel, opaque and writing depth.</summary>
        Base,

        /// <summary>Mode 2: another material adding its share over it.</summary>
        Overlay,

        /// <summary>Mode 3: the tile's lightmap, multiplied over the lot.</summary>
        Lightmap,
    }

    private readonly record struct Chunk(
        MeshHandle Mesh,
        int IndexCount,
        ChunkPass Pass,
        Vector3d Origin,
        TextureHandle Diffuse,
        TextureHandle Detail);

    private readonly record struct Tile(
        Igi2TerrainLod Lod,
        Vector3d Centre,
        TextureHandle Lightmap,
        (float ScaleX, float ScaleY, float OffsetX, float OffsetY) Projection,
        Dictionary<int, (TextureHandle Diffuse, TextureHandle Detail)> Textures);
}
