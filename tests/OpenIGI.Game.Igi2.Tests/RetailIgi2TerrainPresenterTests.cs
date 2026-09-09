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

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
