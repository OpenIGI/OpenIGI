using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The quadtree that decides how finely a tile is tessellated, over the shipped ground.</summary>
public sealed class RetailIgi2TerrainLodTests
{
    // A player's eye, two metres over the sample the camera is standing on. The detail test is a ratio
    // of edge length to distance, so where the camera is matters more than anything else in this suite.
    private const double EyeHeight = 2.0 * OpenIGI.Game.World.WorldScene.UnitsPerMetre;

    /// <summary>The morph is a raised cosine, flat at both ends.</summary>
    /// <remarks>
    /// <c>TerrainRender_CalculateRenderVertex</c> (<c>igi2ed 0x66A76D</c>) blends a split vertex toward
    /// the midpoint of its two parents by <c>(1 - cos(m * pi)) * 0.5</c>. The endpoints alone would be
    /// satisfied by a straight lerp; what the cosine adds is a zero derivative at each end, so a vertex
    /// arrives and leaves without the ground appearing to twitch. That is the property worth pinning,
    /// because a lerp would pass every other test in this file.
    /// </remarks>
    [Fact]
    public void The_morph_is_a_raised_cosine()
    {
        Assert.Equal(0f, Igi2TerrainLodSettings.MorphFraction(0f));
        Assert.Equal(1f, Igi2TerrainLodSettings.MorphFraction(1f));
        Assert.Equal(0.5f, Igi2TerrainLodSettings.MorphFraction(0.5f), 5);

        // Flat at both ends: the first and last hundredth move a fortieth of what the middle one does,
        // where a straight lerp would move exactly as much.
        float first = Igi2TerrainLodSettings.MorphFraction(0.01f) - Igi2TerrainLodSettings.MorphFraction(0f);
        float last = Igi2TerrainLodSettings.MorphFraction(1f) - Igi2TerrainLodSettings.MorphFraction(0.99f);
        float middle = Igi2TerrainLodSettings.MorphFraction(0.505f) - Igi2TerrainLodSettings.MorphFraction(0.495f);
        Assert.True(first * 40f < middle, $"The curve starts at {first} per hundredth, not flat.");
        Assert.True(last * 40f < middle, $"The curve ends at {last} per hundredth, not flat.");

        // Monotone, so a vertex never falls back toward its parents while its square keeps approaching.
        for (int i = 1; i <= 100; i++)
        {
            Assert.True(
                Igi2TerrainLodSettings.MorphFraction(i / 100f) > Igi2TerrainLodSettings.MorphFraction((i - 1) / 100f),
                $"The curve is not monotone at {i / 100f}.");
        }
    }

    /// <summary>Every level with ground authors the two numbers the quadtree is a function of.</summary>
    /// <remarks>
    /// <c>Detail</c> and <c>Adaption</c> reach the renderer as <c>TerrainContext_t::vDetail</c> and
    /// <c>::vAdapt</c> and are read in one place, <c>igi2ed 0x6691D3</c>. Recording their range is the
    /// only way to know whether the port's arithmetic is being exercised in the regime the game shipped
    /// in — a threshold two orders of magnitude off would still produce a plausible-looking mesh.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_level_with_ground_authors_a_detail_and_an_adaption()
    {
        List<float> detail = [];
        List<float> adaption = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            if (Surface(level).IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Assert.NotNull(Igi2TerrainLodSettings.Read(level.Root));
            detail.Add(settings.Detail);
            adaption.Add(settings.Adaption);
        }

        Assert.Equal(22, detail.Count);
        // Nine distinct detail thresholds between 0.12 and 0.35, and eight adaptions between 3.92 and
        // 50. So the shipped ground splits an edge that subtends more than about seven to nineteen
        // degrees on flat terrain, and roughness can pull that threshold down by more than an order of
        // magnitude where the height field is broken up. Both are ratios, which is why they are
        // dimensionless and why the same numbers work on a 128-sample tile and a 512-sample one.
        Assert.Equal(9, detail.Distinct().Count());
        Assert.Equal(8, adaption.Distinct().Count());
        Assert.Equal(0.12f, detail.Min(), 4);
        Assert.Equal(0.35f, detail.Max(), 4);
        Assert.Equal(3.916667f, adaption.Min(), 4);
        Assert.Equal(50f, adaption.Max(), 4);
    }

    /// <summary>The leaves tile their square exactly: no gap, no overlap, at any camera.</summary>
    /// <remarks>
    /// The traversal only ever divides a square into four equal quadrants, so the emitted leaves' areas
    /// adding up to the root's is a proof that the partition is complete. This is the property a
    /// level-of-detail mesh loses first and shows worst — a hole in the ground with the sky behind it —
    /// and it is checked at four very different camera positions per level because a gap that only
    /// appears at one detail boundary is exactly the kind that ships.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_leaves_tile_their_square_exactly()
    {
        int checks = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                Igi2TerrainLod lod = new(tile, surface.Materials);
                long area = (long)lod.RootSize * lod.RootSize;

                foreach (Igi2TerrainView view in Views(tile))
                {
                    Igi2TerrainTessellation tessellation = lod.Tessellate(view, settings);

                    // Culled subtrees are the one legitimate way to leave a hole, so this checks the
                    // uncullable view of each tile as well: with no planes at all nothing is rejected
                    // and the leaves must account for every last sample.
                    if (view.PlaneNormals.IsEmpty)
                    {
                        Assert.Equal(area, tessellation.CoveredArea);
                    }
                    else
                    {
                        Assert.True(
                            tessellation.CoveredArea <= area,
                            $"Tile {tile.Id} covered {tessellation.CoveredArea} of {area} samples.");
                    }

                    checks++;
                }
            }
        }

        Assert.Equal(184, checks);
    }

    /// <summary>A tile far enough away collapses to its root: two triangles for the whole thing.</summary>
    /// <remarks>
    /// The detail test is <c>|edge|² &gt; D²·distance²</c>, so pushing the eye far enough out makes the
    /// right-hand side win for every edge of the root square and the traversal draws a leaf immediately.
    /// It is the cheap end of the scale and it should be reachable, because a quadtree whose coarsest
    /// answer is still thousands of triangles has not really got a coarsest answer.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_distant_tile_collapses_to_two_triangles()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainLod lod = new(tile, surface.Materials);

            // A hundred kilometres out, along every axis at once, and looking at nothing in particular:
            // no clipping planes, so the collapse is the detail test's doing and not the cull's.
            double far = 100_000.0 * OpenIGI.Game.World.WorldScene.UnitsPerMetre;
            Igi2TerrainView view = new(
                new Vector3d(tile.Centre.X + far, tile.Centre.Y + far, tile.Centre.Z + far),
                []);

            Igi2TerrainTessellation tessellation = lod.Tessellate(view, settings);
            Assert.Equal(1, tessellation.SquaresDrawn);

            // One leaf is two triangles, issued once per material with any weight at a corner -- so the
            // count is two times however many of the four corners' materials differ.
            Assert.True(
                tessellation.Mesh.TriangleCount % 2 == 0 && tessellation.Mesh.TriangleCount >= 2,
                $"A collapsed tile issued {tessellation.Mesh.TriangleCount} triangles.");
            return;
        }

        Assert.Fail("No level shipped terrain to draw.");
    }

    /// <summary>Standing on the ground, the quadtree issues a small fraction of the full-detail mesh.</summary>
    /// <remarks>
    /// <para>
    /// The whole point of the exercise, measured rather than asserted loosely. The full-detail figure is
    /// the one <c>RetailIgi2TerrainMeshTests</c> pins; the tessellated one is what the same corpus costs
    /// with the camera two metres above the middle sample of each tile, looking along <c>+Y</c> with a
    /// sixty-degree vertical field of view — which is a player standing in the level rather than a
    /// worst case chosen to flatter the number.
    /// </para>
    /// <para>
    /// The counts are pinned exactly because every input to them is: the split rule, the morph lattice,
    /// the clipping planes and the camera are all fixed here, so a change in the answer is a change in
    /// the algorithm and worth seeing.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Standing_on_the_ground_costs_a_fraction_of_full_detail()
    {
        long full = 0;
        long tessellated = 0;
        int squares = 0;
        int culled = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                full += Igi2TerrainMesh.Build(tile, surface.Materials).TriangleCount;

                Igi2TerrainLod lod = new(tile, surface.Materials);
                Igi2TerrainTessellation tessellation = lod.Tessellate(StandingOn(tile), settings);
                tessellated += tessellation.Mesh.TriangleCount;
                squares += tessellation.SquaresDrawn;
                culled += tessellation.SquaresCulled;
            }
        }

        // 13,271,214 triangles at full detail against 230,483 tessellated -- 1.74 per cent, a factor of
        // 57.6. 82,541 quadtree leaves produce those triangles and 8,771 squares never got that far,
        // rejected outright by the four clipping planes with their subtrees.
        Assert.Equal(13_271_214L, full);
        Assert.Equal(230_483L, tessellated);
        Assert.Equal(82_541, squares);
        Assert.Equal(8_771, culled);
    }

    /// <summary>The clipping planes reject what is behind the camera.</summary>
    /// <remarks>
    /// <c>TerrainRender_IsSquareOutOfSight</c> (<c>igi2ed 0x66AE50</c>) builds a box from the square's
    /// footprint and the height range in its min/max pyramid and rejects it when all eight corners fall
    /// outside one of the four side planes. Turning the camera around should therefore cost a great deal
    /// less than facing the ground, and the difference is what says the box test is wired to the
    /// traversal rather than merely present.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Turning_around_costs_less_than_facing_the_ground()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainLod lod = new(tile, surface.Materials);

            Igi2TerrainTessellation facing = lod.Tessellate(StandingOn(tile), settings);
            Igi2TerrainTessellation nothing = lod.Tessellate(LookingAt(tile, MathF.PI, -1.4f), settings);

            Assert.True(facing.SquaresCulled > 0, "Nothing was culled while facing the ground.");
            Assert.True(
                nothing.SquaresDrawn < facing.SquaresDrawn,
                $"Looking up and away drew {nothing.SquaresDrawn} squares against {facing.SquaresDrawn}.");
            return;
        }

        Assert.Fail("No level shipped terrain to draw.");
    }

    /// <summary>A vertex's weights are a partition of one, which is what makes the additive blend work.</summary>
    /// <remarks>
    /// The passes are <c>SRCALPHA</c>/<c>ONE</c>, so the accumulation is only a weighted average while
    /// the weights at a sample sum to one. At full detail that is trivial — one material, weight one.
    /// The quadtree makes it non-trivial: <c>TerrainRender_CalculateRenderVertex</c> gives a split vertex
    /// <c>w</c> of its own material and <c>(1 - w)</c> shared out of its parents' means, and the sum
    /// survives only because the parents' own sums were one. A vertex painted with a material the level
    /// never declared is the documented exception and sums to zero.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_vertexs_material_weights_sum_to_one()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = Surface(level);
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainLod lod = new(tile, surface.Materials);
            Igi2TerrainTessellation tessellation = lod.Tessellate(StandingOn(tile), settings);

            int blended = 0;
            for (int i = 0; i < tessellation.Mesh.Positions.Count; i++)
            {
                float sum = 0f;
                int carrying = 0;
                foreach (Igi2TerrainPass pass in tessellation.Mesh.Passes)
                {
                    float weight = pass.Weights.Span[i];
                    sum += weight;
                    carrying += weight > 0f ? 1 : 0;
                }

                Assert.True(
                    Math.Abs(sum - 1f) < 1e-4f || sum == 0f,
                    $"Vertex {i} carries weights summing to {sum}.");

                blended += carrying > 1 ? 1 : 0;
            }

            // And some vertex really is split between two materials, or the sum above would be proving
            // nothing the full-detail path did not already satisfy.
            Assert.True(blended > 0, "No vertex was blended between two materials.");
            return;
        }

        Assert.Fail("No level shipped terrain to draw.");
    }

    private static Igi2TerrainView StandingOn(Igi2TerrainTile tile) => LookingAt(tile, 0f, -0.2f);

    private static Igi2TerrainView LookingAt(Igi2TerrainTile tile, float yaw, float pitch)
    {
        Vector3d ground = tile.PositionOf(tile.Width / 2, tile.Height / 2);
        Camera3D camera = new(
            new Vector3d(ground.X, ground.Y, ground.Z + EyeHeight),
            Camera3D.LookRotation(yaw, pitch));

        return Igi2TerrainView.FromFrame(new TerrainFrame(
            camera,
            Matrix4f.Identity,
            MathF.PI / 3f,
            1.6f,
            ViewDistance: 0,
            Batching: false,
            OcclusionCulling: false));
    }

    private static IEnumerable<Igi2TerrainView> Views(Igi2TerrainTile tile)
    {
        // No planes at all, so nothing may be culled and the partition has to be complete.
        yield return new Igi2TerrainView(
            new Vector3d(tile.Centre.X, tile.Centre.Y, tile.Centre.Z + EyeHeight),
            []);

        yield return StandingOn(tile);
        yield return LookingAt(tile, MathF.PI / 2f, 0f);
        yield return LookingAt(tile, MathF.PI, 0.5f);
    }

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
