using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
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
            if (level.Terrain().IsEmpty)
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
        long superseded = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
                long area = (long)lod.RootSize * lod.RootSize;

                foreach (Igi2TerrainView view in Views(tile))
                {
                    Igi2TerrainTessellation tessellation = lod.Tessellate(view, settings);

                    // Culled subtrees are the one legitimate way to leave a hole, so this checks the
                    // uncullable view of each tile as well: with no planes at all nothing is rejected
                    // and the leaves must account for every last sample.
                    // Ground a finer tile owns is the other legitimate way, and it is not a view
                    // decision: the same squares are left alone from every camera.
                    long accounted = tessellation.CoveredArea + tessellation.SupersededArea;
                    if (view.PlaneNormals.IsEmpty)
                    {
                        Assert.Equal(area, accounted);
                    }
                    else
                    {
                        Assert.True(
                            accounted <= area,
                            $"Tile {tile.Id} accounted for {accounted} of {area} samples.");
                    }

                    superseded += tessellation.SupersededArea;

                    checks++;
                }
            }
        }

        Assert.Equal(184, checks);

        // And some of it was left alone, so the branch above is not vacuous: I.G.I. 2's tiles nest, and
        // a coarse tile that drew under a finer one would put a second ground in the frame.
        Assert.True(superseded > 0, "no tile left any ground to a finer one, so nesting is untested here.");
    }

    /// <summary>A tile and the finer tile inside it meet along one line, and neither draws the other's ground.</summary>
    /// <remarks>
    /// <para>
    /// A level's tiles nest, and they are separate height maps of the same ground. The quadtree used to keep the coarser tile
    /// drawing a sample into the finer one so the join had no holes, and under the additive passes that second ground showed
    /// wherever it stood proud of the finer tile's surface: mission 4's far hillside had a pale square on it, one coarse sample
    /// across and lit by the coarse tile's bake, there from the ridge above the base and gone from nearer by.
    /// </para>
    /// <para>
    /// So the coarser tile now draws none of the finer tile's ground, and the two are held to one line instead. The coarser
    /// tile has a vertex at every one of its samples round the finer tile, each at its true height, from any distance; and
    /// every vertex of the finer tile's edge stands on the straight lines between them.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Nested_tiles_meet_along_one_line()
    {
        int joins = 0;
        int held = 0;
        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                bool hosts = tile.Submaps is { Count: > 0 };
                bool hosted = tile.Hosts is { Count: > 0 };
                if (!hosts && !hosted)
                {
                    continue;
                }

                Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
                Igi2TerrainExtent own = tile.Extent;
                foreach (double height in (double[])[EyeHeight, 409600.0, 40960000.0])
                {
                    Igi2TerrainView view = new(new Vector3d(tile.Centre.X, tile.Centre.Y, tile.Centre.Z + height), []);
                    Igi2TerrainTessellation tessellation = lod.Tessellate(view, settings);
                    Assert.Equal(0, tessellation.LargestLeafOverSubmap);
                    Assert.Equal((long)lod.RootSize * lod.RootSize, tessellation.CoveredArea + tessellation.SupersededArea);

                    IReadOnlyList<Vector3d> positions = tessellation.Mesh.Positions;
                    int[] onJoin = new int[tile.Submaps?.Count ?? 0];
                    foreach (Vector3d at in positions)
                    {
                        // This tile's own edge, in a coarser tile: on that tile's line.
                        if (hosted && own.IsOnEdge(at.X, at.Y))
                        {
                            bool alongX = Math.Abs(at.Y - own.Top) <= Igi2TerrainExtent.Tolerance
                                || Math.Abs(at.Y - own.Bottom) <= Igi2TerrainExtent.Tolerance;
                            if (tile.SeamHeight(at.X, at.Y, alongX) is { } line)
                            {
                                Assert.Equal(line, at.Z, 0.01);
                                held++;
                                continue;
                            }
                        }

                        // The edge of a finer tile's ground: this tile's own sample, at its true height.
                        for (int i = 0; i < onJoin.Length; i++)
                        {
                            if (tile.Submaps![i].IsOnEdge(at.X, at.Y))
                            {
                                int column = (int)Math.Round((at.X - tile.SampleX(0)) / tile.TexelWidth);
                                int row = (int)Math.Round((at.Y - tile.SampleY(0)) / tile.TexelHeight);
                                Assert.Equal(tile.DrawnHeightAt(column, row), at.Z, 0.01);
                                onJoin[i]++;
                            }
                        }
                    }

                    // And a vertex at every sample round each finer tile that lies whole inside this one.
                    for (int i = 0; i < onJoin.Length; i++)
                    {
                        Igi2TerrainExtent inner = tile.Submaps![i];
                        // One that another finer tile lies across has part of its edge inside that tile's ground, which is
                        // not this tile's to draw: location2/LEVEL5 lays two across each other.
                        if (!own.Contains(inner.Left, inner.Top, inner.Right, inner.Bottom)
                            || tile.Submaps.Where((_, other) => other != i).Any(other => other.Touches(inner.Left, inner.Top, inner.Right, inner.Bottom)))
                        {
                            continue;
                        }

                        int across = (int)Math.Round((inner.Right - inner.Left) / tile.TexelWidth);
                        int down = (int)Math.Round((inner.Bottom - inner.Top) / tile.TexelHeight);
                        Assert.Equal(2 * (across + down), onJoin[i]);
                        joins++;
                    }
                }
            }
        }

        Assert.True(joins > 0, "no tile has a finer one inside it, so the join is untested here.");
        Assert.True(held > 0, "no finer tile's edge was held to a coarser tile's line.");
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
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;

            // A tile that neither holds a finer one nor lies in a coarser: one that does keeps its side of the join divided
            // from any distance (Nested_tiles_meet_along_one_line), so its coarsest answer is the join's outline.
            if (!surface.Tiles.Any(static t => t.Submaps is not { Count: > 0 } && t.Hosts is not { Count: > 0 }))
            {
                continue;
            }

            Igi2TerrainTile tile = surface.Tiles.First(static t => t.Submaps is not { Count: > 0 } && t.Hosts is not { Count: > 0 });
            Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);

            // A hundred kilometres out, along every axis at once, and looking at nothing in particular:
            // no clipping planes, so the collapse is the detail test's doing and not the cull's.
            double far = 100_000.0 * OpenIGI.Game.World.WorldScene.UnitsPerMetre;
            Igi2TerrainView view = new(
                new Vector3d(tile.Centre.X + far, tile.Centre.Y + far, tile.Centre.Z + far),
                []);

            Igi2TerrainTessellation tessellation = lod.Tessellate(view, settings);
            Assert.Equal(1, tessellation.SquaresDrawn);

            // One leaf is two triangles, each issued once per material with any weight at one of its own three corners -- so
            // the count is at least the two, and no more than two for every material the tile paints.
            Assert.True(
                tessellation.Mesh.TriangleCount >= 2 && tessellation.Mesh.TriangleCount <= 2 * (lod.PassCount + 1),
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
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            foreach (Igi2TerrainTile tile in surface.Tiles)
            {
                full += Igi2TerrainMesh.Build(tile, surface.Materials, surface.Discards).TriangleCount;

                Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
                Igi2TerrainTessellation tessellation = lod.Tessellate(StandingOn(tile), settings);
                tessellated += tessellation.Mesh.TriangleCount;
                squares += tessellation.SquaresDrawn;
                culled += tessellation.SquaresCulled;
            }
        }

        // 12,938,075 triangles at full detail against 210,752 tessellated -- 1.63 per cent, a factor
        // of 61.4. 76,394 quadtree leaves produce those triangles, and the clipping planes reject the
        // rest outright with their subtrees. Both counts are of the ground the levels actually draw:
        // the four levels with a DiscardTask have their holes cut out of each.
        Assert.Equal(12_938_075L, full);
        Assert.Equal(210_752L, tessellated);
        Assert.Equal(76_394, squares);
        Assert.Equal(7_717, culled);
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
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);

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
            Igi2TerrainSurface surface = level.Terrain();
            if (surface.IsEmpty)
            {
                continue;
            }

            Igi2TerrainLodSettings settings = Igi2TerrainLodSettings.Read(level.Root)!.Value;
            Igi2TerrainTile tile = surface.Tiles[0];
            Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
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

        return Igi2TerrainView.FromCamera(camera, MathF.PI / 3f, 1.6f);
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
}
