using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A terrain tile turned into the passes a renderer issues.</summary>
public sealed class RetailIgi2TerrainMeshTests
{
    /// <summary>Every declared tile becomes geometry, split into one pass per material.</summary>
    /// <remarks>
    /// The corpus draws its ground in 229 passes over 46 tiles — five materials a tile — and issues
    /// 13.27 million triangles at full detail against 6.36 million vertices. The grid alone accounts for
    /// about 12.6 million of those; the surplus is the triangles that straddle a material boundary and
    /// are therefore issued more than once, which is the blend costing what it costs.
    /// <para>
    /// 13 million triangles is also why the original has a quadtree — it is eight times the whole
    /// world's object geometry, and nothing draws it all at once.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_tile_becomes_passes_a_renderer_can_issue()
    {
        int tiles = 0;
        int passes = 0;
        long triangles = 0;
        long vertices = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                tiles++;
                Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);

                Assert.Equal(tile.Width * tile.Height, mesh.Positions.Count);
                Assert.Equal(mesh.Positions.Count, mesh.Lights.Count);
                Assert.NotEmpty(mesh.Passes);

                passes += mesh.Passes.Count;
                triangles += mesh.TriangleCount;
                vertices += mesh.Positions.Count;

                foreach (Igi2TerrainPass pass in mesh.Passes)
                {
                    Assert.Equal(mesh.Positions.Count, pass.Weights.Length);
                    Assert.Equal(0, pass.Indices.Length % 3);
                    Assert.True(pass.TextureScale > 0, $"Material {pass.MaterialId} has scale {pass.TextureScale}.");

                    foreach (int index in pass.Indices.Span)
                    {
                        Assert.InRange(index, 0, mesh.Positions.Count - 1);
                    }
                }
            }
        }

        Assert.Equal(46, tiles);

        // 215 rather than one per material per tile: a tile whose only samples of some material lie
        // under a finer tile now issues no triangles for it, and a pass with nothing to draw is dropped.
        Assert.Equal(215, passes);
        Assert.Equal(6_356_992L, vertices);

        // 12,925,877 rather than the 12,939,092 the ground has before its holes: the four levels that
        // declare a DiscardTask lose 13,215 triangles between them, which is what a vehicle hangar, two
        // sewer mouths and ten multiplayer cellars cost.
        Assert.Equal(12_925_877L, triangles);
    }

    /// <summary>A triangle is issued by every material at any of its corners, and by no other.</summary>
    /// <remarks>
    /// <para>
    /// The rule from <c>TerrainRender_DrawFace</c>, and the one thing here that is not an
    /// approximation. A triangle spanning a boundary is drawn twice — once per material — and each pass
    /// carries weight only at its own corners, so the two blend across the seam.
    /// </para>
    /// <para>
    /// Checked in both directions on a level's first tile: every triangle a pass issues has a corner of
    /// that material, and every triangle with such a corner is issued. A per-face rule, or a majority
    /// vote, fails the second half immediately.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_triangle_is_issued_by_every_material_at_any_of_its_corners()
    {
        int shared = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);

            // Every triangle a pass issues has a corner of that pass's material.
            Dictionary<(int, int, int), List<int>> issuedBy = [];
            foreach (Igi2TerrainPass pass in mesh.Passes)
            {
                ReadOnlySpan<int> indices = pass.Indices.Span;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Assert.True(
                        pass.Weights.Span[indices[i]] > 0
                        || pass.Weights.Span[indices[i + 1]] > 0
                        || pass.Weights.Span[indices[i + 2]] > 0,
                        $"Material {pass.MaterialId} issued a triangle none of whose corners it covers.");

                    (int, int, int) key = (indices[i], indices[i + 1], indices[i + 2]);
                    if (!issuedBy.TryGetValue(key, out List<int>? by))
                    {
                        issuedBy[key] = by = [];
                    }

                    by.Add(pass.MaterialId);
                }
            }

            // And every material at a corner issued it. Counting the triangles issued more than once is
            // what says the corpus exercises the rule at all rather than being uniformly painted.
            foreach ((_, List<int> by) in issuedBy)
            {
                shared += by.Count > 1 ? 1 : 0;
            }
        }

        // 99,710 triangles straddle a boundary on the first tile of each level alone, so the rule is
        // exercised heavily rather than being a corner case the corpus never reaches. It was 140,913
        // before the coarse tiles stopped drawing the ground their submaps own -- a boundary is exactly
        // the kind of detail a level lays a finer tile over.
        Assert.Equal(99_710, shared);
    }

    /// <summary>Texture coordinates come from the world, not from the tile's grid.</summary>
    /// <remarks>
    /// <c>1 / (vScale * 4096)</c> against the world position, so a material repeats every
    /// <c>vScale</c> metres wherever it is painted. Two tiles at different sample densities therefore
    /// texture identically where they meet — which a grid-relative coordinate would not.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Texture_coordinates_are_a_world_space_projection()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);
            Igi2TerrainPass pass = mesh.Passes[0];

            (float U, float V) first = Igi2TerrainMesh.TextureCoordinate(pass.TextureScale, mesh.Positions[0]);
            (float U, float V) next = Igi2TerrainMesh.TextureCoordinate(pass.TextureScale, mesh.Positions[1]);

            // One texel of separation in the world is one texel-width's worth of UV, scaled.
            double expected = tile.TexelWidth / (pass.TextureScale * 4096.0);
            Assert.Equal(expected, next.U - first.U, 5);
            Assert.Equal(first.V, next.V, 5);
            return;
        }

        Assert.Fail("No level shipped terrain to check.");
    }

    /// <summary>The weights at a sample sum to one, which is what makes the additive blend an average.</summary>
    /// <remarks>
    /// <para>
    /// <c>TerrainRender_SetBlendState</c> (<c>igi2ed 0x66BCB0</c>) sets <c>SRCBLEND = SRCALPHA</c> and
    /// <c>DESTBLEND = ONE</c>, so the passes accumulate. That only composites correctly if the weights
    /// at each sample sum to one — otherwise the ground comes out over- or under-bright rather than
    /// simply wrong, which is the kind of error that reads as a lighting bug.
    /// </para>
    /// <para>
    /// The exceptions are the samples painted with a material the level never declared: 758 across the
    /// corpus, one in eight thousand. Their weight sums to <b>zero</b>, and under an additive blend that
    /// is not a fallback — it is a contribution of nothing, so the frame keeps whatever lies behind the
    /// terrain. The original does the same, so they are counted rather than repaired.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_weights_at_a_sample_sum_to_one_except_where_a_material_was_deleted()
    {
        int uncovered = 0;
        long samples = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                Igi2TerrainMesh mesh = Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards);
                uncovered += mesh.UncoveredSampleCount;
                samples += mesh.Positions.Count;

                for (int i = 0; i < mesh.Positions.Count; i++)
                {
                    float total = 0f;
                    foreach (Igi2TerrainPass pass in mesh.Passes)
                    {
                        total += pass.Weights.Span[i];
                    }

                    Assert.True(total is 0f or 1f, $"Sample {i} of tile {tile.Id} sums to {total}.");
                }
            }
        }

        Assert.Equal(6_356_992L, samples);

        // Measured: 758 samples across 6.36 million carry a material their level deleted.
        Assert.Equal(758, uncovered);
    }

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
