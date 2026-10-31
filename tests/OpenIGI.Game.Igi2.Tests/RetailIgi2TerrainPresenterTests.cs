using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;
using OpenIGI.Rendering.Headless;

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
            Igi2TerrainSurface surface = level.Terrain();
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

        // 7,840 rather than the 4,006 an additive-only ground uploaded. A pass whose triangles are
        // split between the base it writes and the shares it adds needs one chunk run for each, which
        // took it to 4,115; the tile's lightmap pass then draws every triangle once more, and since
        // most of a tile is a single material -- only the boundaries straddle -- that is nearly as
        // many triangles again. Nine fewer than before the DiscardTasks were honoured: a hole can take
        // the last triangles a pass had, and five more than before a tile kept one sample of its
        // submaps' ground back -- see Igi2TerrainTile.IsSuperseded.
        Assert.Equal(7_845, chunks);

        // Every triangle the mesh builds is uploaded -- the 12,938,075 RetailIgi2TerrainMeshTests
        // counts, the ground with its holes already cut -- and then 12,364,578 more for the lightmap
        // pass, which draws each DISTINCT triangle once, whatever material covers it. The gap between
        // those two numbers is the material straddling in the corpus less the faces no material claims
        // at all: 574,269 triangles are claimed by more than one material, four and a half per cent,
        // and 772 are claimed by none and drawn by the terrain map's pass alone.
        //
        // An earlier attempt kept one chunk per pass and dropped the 32 that overran a 16-bit index,
        // which lost whole materials from whole levels and did it quietly. Chunking at the original's
        // own 10,000-index budget makes the question go away rather than trading it off.
        Assert.Equal(25_302_653L, triangles);
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
            Igi2TerrainSurface surface = level.Terrain();
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
            Igi2TerrainSurface surface = level.Terrain();
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
            Igi2TerrainSurface surface = level.Terrain();
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

            // A chunk is written by the host straight into a buffer the GPU may still be reading, so
            // every frame draws from its own pool and only the ChunkPoolSlots'th frame after one may
            // reuse its chunks -- see Igi2TerrainPresenter.ChunkPoolSlots. The pools therefore fill for
            // as many frames as there are slots and the ring closes: nothing is allocated after that,
            // and every frame issues the same draws from the same camera.
            int perFrameDraws = device.MeshDraws.Count;
            int perFrameMeshes = device.Meshes.Count;

            for (int f = 1; f <= Igi2TerrainPresenter.ChunkPoolSlots; f++)
            {
                presenter.Draw(frame);
                Assert.Equal(perFrameDraws * (f + 1), device.MeshDraws.Count);
                Assert.Equal(
                    perFrameMeshes * Math.Min(f + 1, Igi2TerrainPresenter.ChunkPoolSlots),
                    device.Meshes.Count);
            }
        }

        Assert.Equal(22, levels);

        // 267,891 triangles across the corpus against 12,938,075 at full detail, from 55,355 quadtree
        // leaves. Part of that is the material passes; the rest is the lightmap pass, which draws every
        // leaf triangle once and no more, because it is drawn from the passes' BASE lists -- fewer than
        // two per leaf, because a leaf triangle that fell wholly inside a DiscardTask was never issued
        // into any pass at all. The leaf count itself is not reduced by a hole: the quadtree still
        // descends through one, it just emits nothing there.
        Assert.Equal(267_891L, triangles);
        Assert.Equal(55_355, squares);
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
}
