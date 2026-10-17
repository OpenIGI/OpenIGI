using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The index a stand is culled through, and the level of detail one tree draws at.</summary>
public sealed class RetailIgi2ForestBspTests
{
    /// <summary>The deepest node of the deepest stand in the corpus.</summary>
    /// <remarks>
    /// <c>forestraid</c>'s task 3525 plants 9,181 trees, and a median split reaches
    /// <c>floor(log2(9181)) = 13</c>. The number is worth pinning because the original allocates its
    /// traversal stack as <c>24 * (depth + 1)</c> bytes from exactly this figure — a build that split
    /// somewhere other than the median would show up here before it showed up anywhere else.
    /// </remarks>
    private const int DeepestStand = 13;

    /// <summary>Every tree is indexed, and the index is a k-d tree alternating X and Y with depth.</summary>
    /// <remarks>
    /// <para>
    /// <c>Forest_RemoveOutsiders</c> (<c>igi2ed 0x64C6D0</c>) drops trees beyond <c>AreaSize * 2048</c>
    /// before the build, and <b>it drops none of the retail corpus</b> — all 54,537 trees survive into
    /// all 54,537 nodes. That is the same fact the placement tests measure from the other side, arrived at
    /// through the engine's own predicate rather than through a bounds check written for the occasion.
    /// </para>
    /// <para>
    /// The ordering assertion is what says the build is the engine's. Every tree below a node is on the
    /// low side of that node's split coordinate and every tree above it on the high side, with the axis
    /// alternating — which is exactly what <c>Forest_BuildBSPTree</c> (<c>0x64C610</c>) produces by
    /// sorting the sub-range with the X or Y comparator (<c>0x649180</c> / <c>0x6491C0</c>) picked by
    /// <c>depth &amp; 1</c> and taking the element at <c>count &gt;&gt; 1</c>.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_stand_indexes_every_one_of_its_trees()
    {
        int stands = 0;
        int trees = 0;
        int nodes = 0;
        int deepest = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2ForestStand stand in ForestOf(level).Stands)
            {
                Igi2ForestBsp bsp = Igi2ForestBsp.Build(stand.Trees, stand.Centre, stand.HalfExtent, 0f);

                stands++;
                trees += stand.Trees.Count;
                nodes += bsp.Count;
                deepest = Math.Max(deepest, bsp.Depth);

                Assert.Equal(stand.Trees.Count, bsp.Count);
                AssertOrdered(bsp, bsp.Root, 0);
            }
        }

        Assert.Equal(85, stands);
        Assert.Equal(54_537, trees);
        Assert.Equal(54_537, nodes);
        Assert.Equal(DeepestStand, deepest);
    }

    /// <summary>The walk keeps exactly the trees a test of every tree against the same planes keeps.</summary>
    /// <remarks>
    /// The whole point of the index is that it answers a question it never asks of most of the data, so
    /// the only assertion worth making about it is that the answer is unchanged. Every stand is walked
    /// from eight headings and the result compared, tree for tree, against
    /// <c>Forest_IsAABBOutOfSight</c> run over each tree's own box in turn: an over-tight node box, a
    /// margin that is too small, or a near/far child mixed up all show as a tree the walk lost.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_walk_keeps_exactly_what_testing_every_tree_keeps()
    {
        int compared = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2ForestStand stand in ForestOf(level).Stands)
            {
                // A radius and a height in the right order of magnitude for a tree: three metres across
                // and thirty tall. The exact model does not matter here — both sides use the same one.
                const float Radius = 3f * (float)WorldScene.UnitsPerMetre;
                const float Height = 30f * (float)WorldScene.UnitsPerMetre;

                Igi2ForestBsp bsp = Igi2ForestBsp.Build(stand.Trees, stand.Centre, stand.HalfExtent, Height);
                float margin = Igi2ForestBsp.MarginOf(Radius, stand.RandomScaleRangeX, stand.RandomScaleRangeY);
                Igi2Tree middle = stand.Trees[stand.Trees.Count / 2];

                for (int step = 0; step < 8; step++)
                {
                    Camera3D camera = new(
                        new Vector3d(middle.Position.X, middle.Position.Y, middle.Position.Z + 7000),
                        Camera3D.LookRotation(step * MathF.Tau / 8f, 0f));

                    Igi2ForestClipPlanes planes = Igi2ForestClipPlanes.Create(
                        camera,
                        Igi2ForestPresenter.DefaultHorizontalHalfTangent,
                        Igi2ForestPresenter.DefaultVerticalHalfTangent,
                        (float)(stand.ViewCutoff * WorldScene.UnitsPerMetre));

                    Vector3f offset = new(
                        (float)(stand.Centre.X - camera.Position.X),
                        (float)(stand.Centre.Y - camera.Position.Y),
                        (float)(stand.Centre.Z - camera.Position.Z));

                    HashSet<int> walked = [];
                    bsp.Cull(planes, offset, margin, Radius, Height, tree => walked.Add(tree));

                    HashSet<int> expected = [];
                    for (int i = 0; i < bsp.Count; i++)
                    {
                        int tree = bsp.TreeOf(i);
                        Vector3f local = bsp.Positions[bsp.Nodes[i].Tree];
                        Vector3f scale = stand.Trees[tree].Scale;
                        float rx = scale.X * Radius;
                        float ry = scale.Y * Radius;

                        if (!planes.IsBoxOutOfSight(
                                local.X + rx,
                                local.X - rx,
                                local.Y + ry,
                                local.Y - ry,
                                local.Z,
                                local.Z + (scale.Z * Height),
                                offset,
                                0f,
                                Igi2ForestClipPlanes.NoPlanesInside,
                                out _))
                        {
                            expected.Add(tree);
                        }
                    }

                    Assert.Equal(expected.Count, walked.Count);
                    Assert.True(
                        expected.SetEquals(walked),
                        $"Forest {stand.TaskId} from heading {step} walked {walked.Count} trees where "
                            + $"testing every tree kept {expected.Count}.");
                    compared++;
                }
            }
        }

        Assert.Equal(85 * 8, compared);
    }

    /// <summary>Past its stand's view cut-off a tree is not drawn at all, and inside it it is.</summary>
    /// <remarks>
    /// <c>ForestRender_GetLOD</c> (<c>igi2ed 0x57D680</c>) returns −1 when the biased distance reaches the
    /// last entry of the model's distance table, and <c>ForestRender_Draw</c> (<c>0x57CA90</c>) returns
    /// without drawing when it does. With one level the whole function is that comparison, which is what
    /// makes it testable without the multi-level distance table this port cannot read.
    /// </remarks>
    [Fact]
    public void A_tree_past_the_cut_off_is_not_drawn()
    {
        float[] cutoff = [100f];

        Assert.Equal(0, Igi2ForestLod.Select(99f, 1f, cutoff, 1, 1f).Level);
        Assert.Equal(Igi2ForestLod.NotDrawn, Igi2ForestLod.Select(100f, 1f, cutoff, 1, 1f).Level);
        Assert.Equal(Igi2ForestLod.NotDrawn, Igi2ForestLod.Select(101f, 1f, cutoff, 1, 1f).Level);

        // Every threshold is scaled by the tree's own largest scale first, so a tree the bake grew stays
        // drawn proportionally further out.
        Assert.Equal(0, Igi2ForestLod.Select(150f, 2f, cutoff, 1, 1f).Level);
        Assert.Equal(Igi2ForestLod.NotDrawn, Igi2ForestLod.Select(150f, 0.5f, cutoff, 1, 1f).Level);

        // And the bias divides the distance before the comparison, so raising it keeps trees longer.
        Assert.Equal(Igi2ForestLod.NotDrawn, Igi2ForestLod.Select(120f, 1f, cutoff, 1, 1f).Level);
        Assert.Equal(0, Igi2ForestLod.Select(120f, 1f, cutoff, 1, 0.5f).Level);
    }

    /// <summary>A multi-level model steps down through its table and morphs across the band.</summary>
    /// <remarks>
    /// The band is <c>thresholds[0] * 0.5</c> whichever level is chosen — the original hoists it out of
    /// the loop rather than taking each level's own, which is why level 1's morph here starts at 150 and
    /// not at 100.
    /// </remarks>
    [Fact]
    public void A_level_morphs_into_the_next_across_the_band()
    {
        float[] table = [100f, 200f, 400f];

        Igi2ForestLodChoice near = Igi2ForestLod.Select(10f, 1f, table, 3, 1f);
        Assert.Equal(0, near.Level);
        Assert.Equal(Igi2ForestLod.NotDrawn, near.MorphLevel);
        Assert.Equal(0f, near.MorphFactor);

        // The band under the first threshold is [50, 100).
        Igi2ForestLodChoice morphing = Igi2ForestLod.Select(75f, 1f, table, 3, 1f);
        Assert.Equal(0, morphing.Level);
        Assert.Equal(1, morphing.MorphLevel);
        Assert.Equal(0.5f, morphing.MorphFactor, 5);

        Igi2ForestLodChoice second = Igi2ForestLod.Select(120f, 1f, table, 3, 1f);
        Assert.Equal(1, second.Level);
        Assert.Equal(Igi2ForestLod.NotDrawn, second.MorphLevel);

        // Past every threshold: the coarsest level, fading over the last gap's half, then gone.
        Igi2ForestLodChoice last = Igi2ForestLod.Select(250f, 1f, table, 3, 1f);
        Assert.Equal(2, last.Level);
        Assert.Equal(0f, last.MorphFactor);
        Assert.Equal(Igi2ForestLod.NotDrawn, Igi2ForestLod.Select(400f, 1f, table, 3, 1f).Level);
    }

    /// <summary>A stand with no trees indexes nothing and walks nothing.</summary>
    [Fact]
    public void An_empty_stand_has_no_root()
    {
        Igi2ForestBsp bsp = Igi2ForestBsp.Build([], default, 1000d, 0f);

        Assert.Equal(Igi2ForestBsp.NoNode, bsp.Root);
        Assert.Equal(0, bsp.Count);
        Assert.Equal(
            0,
            bsp.Cull(default, default, 0f, 0f, 0f, static _ => Assert.Fail("An empty stand walked a tree.")));
    }

    private static void AssertOrdered(Igi2ForestBsp bsp, int node, int depth)
    {
        if (node == Igi2ForestBsp.NoNode)
        {
            return;
        }

        Igi2ForestBspNode self = bsp.Nodes[node];
        Vector3f position = bsp.Positions[self.Tree];
        float split = (depth & 1) == 0 ? position.X : position.Y;

        // The axis the split was taken on stays fixed all the way down the subtree, whatever the axes
        // those deeper levels go on to split.
        AssertSide(bsp, self.Low, depth & 1, split, below: true);
        AssertSide(bsp, self.High, depth & 1, split, below: false);
        AssertOrdered(bsp, self.Low, depth + 1);
        AssertOrdered(bsp, self.High, depth + 1);
    }

    private static void AssertSide(Igi2ForestBsp bsp, int node, int axis, float split, bool below)
    {
        if (node == Igi2ForestBsp.NoNode)
        {
            return;
        }

        Igi2ForestBspNode self = bsp.Nodes[node];
        Vector3f position = bsp.Positions[self.Tree];
        float coordinate = axis == 0 ? position.X : position.Y;

        if (below)
        {
            Assert.True(coordinate <= split, $"A tree at {coordinate} sits below a split at {split}.");
        }
        else
        {
            Assert.True(coordinate >= split, $"A tree at {coordinate} sits above a split at {split}.");
        }

        AssertSide(bsp, self.Low, axis, split, below);
        AssertSide(bsp, self.High, axis, split, below);
    }

    private static Igi2Forest ForestOf(Igi2Level level)
    {
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        files.SetAlias(
            VirtualFileSystem.MissionAlias,
            "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");

        return Igi2Forest.Build(level.Root, new ResourceRegistry(files));
    }
}
