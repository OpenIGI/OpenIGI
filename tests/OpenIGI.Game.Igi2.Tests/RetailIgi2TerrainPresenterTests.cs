using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The ground, uploaded and drawn.</summary>
public sealed class RetailIgi2TerrainPresenterTests
{
    /// <summary>Every level's ground uploads and draws, one additive pass per material.</summary>
    /// <remarks>
    /// <para>
    /// The state is the original's, not a choice: <c>SRCBLEND = SRCALPHA</c> with
    /// <c>DESTBLEND = ONE</c> is <see cref="BlendMode.Additive"/>, and every chunk must use it — one
    /// opaque pass would overwrite the materials drawn before it instead of accumulating with them.
    /// </para>
    /// <para>
    /// Culling is clockwise because <c>TerrainRender_SetGenericState</c> sets <c>CULLMODE = CW</c>. That
    /// is worth asserting because the wrong winding is invisible from above and turns the ground inside
    /// out from below.
    /// </para>
    /// <para>
    /// The chunk count is the other half: 4,113 of them for 13.27 million triangles, which is the
    /// original's 10,000-index budget doing the dividing.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_levels_ground_uploads_and_draws_additively()
    {
        int levels = 0;
        int chunks = 0;
        long triangles = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            levels++;
            RecordingGraphicsDevice device = new();
            using Igi2TerrainPresenter presenter = new(device, surface, _ => TextureHandle.None);

            chunks += presenter.ChunkCount;
            triangles += presenter.TriangleCount;

            presenter.Draw(new TerrainFrame(
                default,
                Matrix4f.Identity,
                1f,
                1.6f,
                ViewDistance: 0,
                Batching: false,
                OcclusionCulling: false));

            Assert.Equal(presenter.ChunkCount, device.MeshDraws.Count);
            Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.Additive, draw.Blend));
            Assert.Equal(CullMode.Clockwise, device.CullMode);
        }

        Assert.Equal(22, levels);
        Assert.Equal(4_113, chunks);

        // Every triangle the mesh builds is uploaded -- the same 13,271,214 RetailIgi2TerrainMeshTests
        // counts. An earlier attempt kept one chunk per pass and dropped the 32 that overran a 16-bit
        // index, which lost whole materials from whole levels and did it quietly. Chunking at the
        // original's own 10,000-index budget makes the question go away rather than trading it off.
        Assert.Equal(13_271_214L, triangles);
    }

    /// <summary>A tile is drawn about its own centre, so a distant level keeps its precision.</summary>
    /// <remarks>
    /// I.G.I. 2's coordinates reach millions of units, and a float carries about seven digits — so a
    /// vertex stored in world space quantises to something like a decimetre before the camera ever moves.
    /// The original transforms about the viewer for the same reason; this stores tile-local positions and
    /// lets <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/> fold the camera in at double
    /// precision.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Tile_geometry_is_stored_about_its_own_centre()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainTile tile = surface.Tiles[0];
            RecordingGraphicsDevice device = new();
            using Igi2TerrainPresenter presenter = new(device, surface, _ => TextureHandle.None);

            double halfWidth = tile.Width * tile.TexelWidth * 0.5;
            double halfHeight = tile.Height * tile.TexelHeight * 0.5;

            // Every uploaded position sits inside its own tile's footprint, which it could not if the
            // vertices were world-space or the origin were the level's.
            foreach (Vertex3D vertex in device.Meshes.Values.First().Vertices)
            {
                Assert.InRange(vertex.Position.X, -(float)halfWidth, (float)halfWidth);
                Assert.InRange(vertex.Position.Y, -(float)halfHeight, (float)halfHeight);
            }

            return;
        }

        Assert.Fail("No level shipped terrain to draw.");
    }

    /// <summary>A vertex carries the baked light in colour and its material's weight in alpha.</summary>
    /// <remarks>
    /// What stage 0 consumes: <c>COLOROP = MODULATE(TEXTURE, DIFFUSE)</c> and
    /// <c>ALPHAOP = SELECTARG2(DIFFUSE)</c>. So the diffuse channel does two jobs at once, and getting
    /// either wrong is a lighting fault rather than a geometry one — the ground still draws, in the
    /// wrong colour or with the wrong material showing through.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_vertex_carries_the_bake_in_colour_and_the_weight_in_alpha()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            RecordingGraphicsDevice device = new();
            using Igi2TerrainPresenter presenter = new(device, surface, _ => TextureHandle.None);

            int opaque = 0;
            int transparent = 0;
            foreach (Vertex3D vertex in device.Meshes.Values.First().Vertices)
            {
                // The weight is one or zero, so the alpha is 255 or 0 and nothing between.
                Assert.True(vertex.Colour.A is 0 or 255, $"A vertex carries alpha {vertex.Colour.A}.");
                opaque += vertex.Colour.A == 255 ? 1 : 0;
                transparent += vertex.Colour.A == 0 ? 1 : 0;
            }

            // Both kinds are present: a pass keeps the vertices its triangles touch, and a triangle
            // straddling a boundary brings in corners belonging to another material.
            Assert.True(opaque > 0, "No vertex carried this material.");
            Assert.True(transparent > 0, "No vertex was borrowed from a neighbouring material.");
            return;
        }

        Assert.Fail("No level shipped terrain to draw.");
    }

    /// <summary>With the quadtree, a frame issues a small fraction of the full-detail ground.</summary>
    /// <remarks>
    /// <para>
    /// The same 22 levels, drawn from a camera two metres above the middle sample of each level's first
    /// tile. Every draw is still additive and clockwise-culled, because the quadtree changes how many
    /// triangles there are and nothing about how they combine.
    /// </para>
    /// <para>
    /// <b>The chunks are storage meshes from a pool, not a fresh upload.</b>
    /// <c>TerrainRender_Draw</c> locks one vertex and one index buffer at the top of the frame and
    /// refills them from the traversal, handing out 10,000-index chunks from
    /// <c>TerrainRender_tRenderChunkPool</c> as it goes; this does the same with
    /// <see cref="IGraphicsDevice.CreateMeshStorage"/>. Drawing a second frame must therefore create no
    /// mesh at all, and that is asserted — a per-frame quadtree that reallocated its buffers would be
    /// slower than the full-detail path it replaced.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Tessellated_ground_draws_a_fraction_of_full_detail()
    {
        int levels = 0;
        int chunks = 0;
        long triangles = 0;
        int squares = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            levels++;
            RecordingGraphicsDevice device = new();
            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            using Igi2TerrainPresenter presenter = new(device, surface, settings, _ => TextureHandle.None);

            Assert.True(presenter.IsTessellated);

            TerrainFrame frame = StandingOn(surface.Tiles[0]);
            presenter.Draw(frame);

            chunks += presenter.ChunkCount;
            triangles += presenter.TriangleCount;
            squares += presenter.SquaresDrawn;

            Assert.Equal(presenter.ChunkCount, device.MeshDraws.Count);
            Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.Additive, draw.Blend));
            Assert.Equal(CullMode.Clockwise, device.CullMode);

            // The second frame reuses every chunk the first one took out of the pool: the same draws
            // again, and not one new mesh.
            int allocated = device.Meshes.Count;
            int issued = device.MeshDraws.Count;
            presenter.Draw(frame);
            Assert.Equal(allocated, device.Meshes.Count);
            Assert.Equal(issued * 2, device.MeshDraws.Count);
        }

        Assert.Equal(22, levels);

        // 187,834 triangles across the corpus against 13,271,214 at full detail -- 1.42 per cent, a
        // factor of 70.7 -- from 64,896 quadtree leaves, in 186 chunks rather than 4,113. Lower than
        // RetailIgi2TerrainLodTests' 230,483 because that suite stands the camera on each tile in turn
        // while a frame has one camera: a level's second and third tiles are further away and coarser.
        Assert.Equal(187_834L, triangles);
        Assert.Equal(64_896, squares);
        Assert.Equal(186, chunks);
    }

    /// <summary>A frame from an eye standing two metres above the middle of a tile.</summary>
    /// <remarks>
    /// <b>In metres</b>, which is what a <see cref="TerrainFrame"/> carries: every camera downstream of
    /// <c>WorldScene</c> is, and the host hands the presenter the same one it hands the world pass. The
    /// presenter converts it into the quadtree's own space itself. This used to build the camera in
    /// engine units to match what the presenter then did with it, which was self-consistent and agreed
    /// with nothing else -- the host's real camera arrived in metres and the ground was tessellated for
    /// an eye four thousand times further away.
    /// </remarks>
    private static TerrainFrame StandingOn(Igi2TerrainTile tile)
    {
        Vector3d ground = tile.PositionOf(tile.Width / 2, tile.Height / 2);
        Camera3D camera = new(
            OpenIGI.Game.World.WorldScene.ToMetres(ground) + new Vector3d(0.0, 0.0, 2.0),
            Camera3D.LookRotation(0f, -0.2f));

        return new TerrainFrame(
            camera,
            Matrix4f.Identity,
            MathF.PI / 3f,
            1.6f,
            ViewDistance: 0,
            Batching: false,
            OcclusionCulling: false);
    }

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
