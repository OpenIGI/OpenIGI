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
    /// <summary>Every level's ground uploads and draws: an opaque base, then the shares added over it.</summary>
    /// <remarks>
    /// <para>
    /// The blend is the original's: <c>SRCBLEND = SRCALPHA</c> with <c>DESTBLEND = ONE</c> is
    /// <see cref="BlendMode.Additive"/>, and that is what a material pass adds its share of the ground
    /// with. But a ground built only from those can never be opaque, and the frame draws the world's
    /// objects before the terrain — so each triangle is also written ONCE, opaquely and with depth, by
    /// the lowest-numbered pass that covers it. Both kinds of draw must be present, and every draw must
    /// be one or the other.
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
            Assert.All(
                device.MeshDraws,
                draw => Assert.True(
                    draw.Blend is BlendMode.Opaque or BlendMode.Additive or BlendMode.Modulate2X,
                    $"The ground drew with {draw.Blend}."));
            Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Opaque);
            Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Additive);
            Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Modulate2X);

            // And in that order, with no interleaving: an opaque base landing after a neighbouring pass
            // had added its share would erase it, and a lightmap multiplied in before the shares were
            // finished would be multiplied by half a ground.
            int lastBase = device.MeshDraws.FindLastIndex(draw => draw.Blend == BlendMode.Opaque);
            int firstOverlay = device.MeshDraws.FindIndex(draw => draw.Blend == BlendMode.Additive);
            int lastOverlay = device.MeshDraws.FindLastIndex(draw => draw.Blend == BlendMode.Additive);
            int firstLightmap = device.MeshDraws.FindIndex(draw => draw.Blend == BlendMode.Modulate2X);
            Assert.True(lastBase < firstOverlay, "An opaque base was drawn after an additive overlay.");
            Assert.True(lastOverlay < firstLightmap, "An overlay was drawn after the lightmap pass.");
            Assert.Equal(CullMode.Clockwise, device.CullMode);
        }

        Assert.Equal(22, levels);

        // 7,849 rather than the 4,006 an additive-only ground uploaded. A pass whose triangles are
        // split between the base it writes and the shares it adds needs one chunk run for each, which
        // took it to 4,115; the tile's lightmap pass then draws every triangle once more, and since
        // most of a tile is a single material -- only the boundaries straddle -- that is nearly as
        // many triangles again.
        Assert.Equal(7_849, chunks);

        // Every triangle the mesh builds is uploaded -- the 12,939,092 RetailIgi2TerrainMeshTests
        // counts -- and then 12,365,624 more for the lightmap pass, which draws each DISTINCT triangle
        // once. The gap between those two numbers is the whole of the material straddling in the
        // corpus: 573,468 triangles, four and a half per cent, are claimed by more than one material.
        //
        // An earlier attempt kept one chunk per pass and dropped the 32 that overran a 16-bit index,
        // which lost whole materials from whole levels and did it quietly. Chunking at the original's
        // own 10,000-index budget makes the question go away rather than trading it off.
        Assert.Equal(25_304_716L, triangles);
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

    /// <summary>A vertex carries the baked light already scaled by its material's share.</summary>
    /// <remarks>
    /// <para>
    /// What stage 0 consumes is <c>COLOROP = MODULATE(TEXTURE, DIFFUSE)</c>, and the share arrives
    /// separately: the original keeps it in a vertex stream of its own — stream 1, four bytes a vertex,
    /// one buffer per material (<c>TerrainRender_DrawRenderChunk_</c>, <c>igi2ed 0x667400</c>) — and
    /// applies it through the blender, <c>SRCBLEND = SRCALPHA</c> in both of the ground's modes
    /// (<c>TerrainRender_SetBlendMode</c>, <c>0x666B90</c>).
    /// </para>
    /// <para>
    /// The port folds it into the colour instead, because not every blend mode it draws the ground with
    /// carries a source-alpha factor — the deferred G-buffer's additive is ONE/ONE and every path's
    /// opaque is ONE/ZERO. Left in the alpha the share was applied on the vanilla path and dropped on
    /// the deferred one, where a tile's five to seven materials each added at full strength. So the
    /// contract asserted here is the product: a vertex whose material this pass is keeps its bake, and
    /// one borrowed from a neighbour comes through black and adds nothing.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_vertex_carries_the_bake_scaled_by_its_materials_share()
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

            int carried = 0;
            int borrowed = 0;
            foreach (Vertex3D vertex in device.Meshes.Values.First().Vertices)
            {
                // The share is one or zero at full detail, so a vertex is either its own bake or black.
                // The alpha is no longer the share and must not gate anything: it is always opaque.
                Assert.Equal(255, vertex.Colour.A);

                bool lit = vertex.Colour.R != 0 || vertex.Colour.G != 0 || vertex.Colour.B != 0;
                carried += lit ? 1 : 0;
                borrowed += lit ? 0 : 1;

                // And the normal is the ground's own slope rather than a flat up: the deferred path
                // lights the ground with it, and a plate normal is what made a hillside read as one.
                Assert.InRange(vertex.Normal.Z, 0.001f, 1.001f);
                float length = MathF.Sqrt(
                    (vertex.Normal.X * vertex.Normal.X)
                    + (vertex.Normal.Y * vertex.Normal.Y)
                    + (vertex.Normal.Z * vertex.Normal.Z));
                Assert.Equal(1f, length, 3);
            }

            // Both kinds are present: a pass keeps the vertices its triangles touch, and a triangle
            // straddling a boundary brings in corners belonging to another material.
            Assert.True(carried > 0, "No vertex carried this material.");
            Assert.True(borrowed > 0, "No vertex was borrowed from a neighbouring material.");

            // Not every normal is flat, or the slope is not being read at all.
            Assert.Contains(device.Meshes.Values.First().Vertices, v => v.Normal.Z < 0.999f);
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
            Assert.All(
                device.MeshDraws,
                draw => Assert.True(
                    draw.Blend is BlendMode.Opaque or BlendMode.Additive or BlendMode.Modulate2X,
                    $"The ground drew with {draw.Blend}."));
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
        // 266,495 triangles across the corpus against 12,939,092 at full detail, from 54,940 quadtree
        // leaves. 156,615 of those are the material passes; the other 109,880 are the lightmap pass,
        // which is exactly two per leaf -- every leaf triangle once, and no more, because it is drawn
        // from the passes' BASE lists.
        Assert.Equal(266_495L, triangles);
        Assert.Equal(54_940, squares);
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
