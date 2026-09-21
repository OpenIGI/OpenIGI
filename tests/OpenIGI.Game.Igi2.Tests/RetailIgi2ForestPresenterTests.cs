using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The forests, uploaded, culled and drawn.</summary>
public sealed class RetailIgi2ForestPresenterTests
{
    /// <summary>How many headings each stand is looked at from.</summary>
    private const int Headings = 8;

    /// <summary>The most trees any of those 680 views leaves standing, out of 54,537.</summary>
    private const int WorstVisible = 1_267;

    /// <summary>The draws that worst view issues, trunks and foliage together.</summary>
    private const int WorstDraws = 1_798;

    /// <summary>A frame draws a small fraction of the corpus, where before it drew all of it.</summary>
    /// <remarks>
    /// <para>
    /// The camera is put in the middle of a stand and turned through the compass, which is the cull's
    /// worst case: the trees are all around it and none of them is behind a hill, because nothing here
    /// occludes. Over all 85 stands and eight headings each, <b>the mean frame finds 214 trees and issues
    /// 341 draws, and the worst finds 1,267 and issues 1,798</b> — against the 54,537 draws every frame
    /// carried before, whatever it was looking at.
    /// </para>
    /// <para>
    /// Two things do the work and both come out of the binary. <c>Forest_DrawTrees1_Optimized</c>
    /// (<c>igi2ed 0x64AE60</c>) rejects whole subtrees against the stand's clip planes, and the last two
    /// of those planes cut the stand off at its own <c>View cutoff</c> — which the shipped levels set as
    /// low as 18 metres and never above 2,000. A cull without the cut-off would still be walking most of
    /// a 2-kilometre stand.
    /// </para>
    /// <para>
    /// The winding is asserted for the reason it always was. <c>ForestRender_SetGenericState_Trunk</c>
    /// (<c>0x57C5A0</c>) sets <c>CULLMODE = CCW</c> where the terrain's state sets <c>CW</c>, so a frame
    /// that set it once would draw one of the two inside out, and from above neither looks wrong.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_frame_draws_far_fewer_trees_than_the_level_plants()
    {
        int levels = 0;
        int stands = 0;
        int trees = 0;
        int views = 0;
        long visible = 0;
        long draws = 0;
        int worstVisible = 0;
        int worstDraws = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            if (forest.IsEmpty)
            {
                continue;
            }

            levels++;
            RecordingGraphicsDevice device = new();
            using Igi2ForestPresenter presenter = new(
                device,
                forest,
                name => Resolve(level, name),
                (_, _) => TextureHandle.None);

            stands += presenter.StandCount;
            trees += presenter.TreeCount;

            foreach (Igi2ForestStand stand in forest.Stands)
            {
                for (int heading = 0; heading < Headings; heading++)
                {
                    device.MeshDraws.Clear();
                    presenter.Draw(CameraIn(stand, heading));

                    views++;
                    visible += presenter.VisibleTreeCount;
                    draws += device.MeshDraws.Count;
                    worstVisible = Math.Max(worstVisible, presenter.VisibleTreeCount);
                    worstDraws = Math.Max(worstDraws, device.MeshDraws.Count);

                    Assert.Equal(CullMode.CounterClockwise, device.CullMode);
                    Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.Opaque, draw.Blend));
                }
            }
        }

        Assert.Equal(22, levels);
        Assert.Equal(85, stands);
        Assert.Equal(54_537, trees);
        Assert.Equal(85 * Headings, views);

        Assert.Equal(214, visible / views);
        Assert.Equal(341, draws / views);
        Assert.Equal(WorstVisible, worstVisible);
        Assert.Equal(WorstDraws, worstDraws);
    }

    /// <summary>The trunk and the foliage are two passes over the same visible trees.</summary>
    /// <remarks>
    /// <para>
    /// <c>ForestRender_Draw</c> (<c>igi2ed 0x57CA90</c>) compares each face group's index against the one
    /// <c>ForestRender_CreateBSPModel</c> (<c>0x57D160</c>) flagged and sends it to
    /// <c>ForestRender_DrawFoliageFaceGroup</c> or <c>ForestRender_DrawTrunkFaceGroup</c> accordingly, so
    /// the split is a bit in the model and not a guess about the texture. Grouping by pass rather than by
    /// tree is this port's own choice — the states are the original's, the order they are issued in is
    /// not.
    /// </para>
    /// <para>
    /// The check is that a two-group model issues each visible tree once per pass and that the blended
    /// pass, when it is turned on, comes after every opaque draw rather than among them. A one-group
    /// model is all foliage and issues no trunk at all, which is why the passes are counted against the
    /// model rather than assumed to both be there.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_trunk_and_the_foliage_are_issued_as_separate_passes()
    {
        int counted = 0;
        int sorted = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            if (forest.IsEmpty)
            {
                continue;
            }

            RecordingGraphicsDevice device = new();
            using Igi2ForestPresenter presenter = new(
                device,
                forest,
                name => Resolve(level, name),
                (_, _) => TextureHandle.None);

            foreach (Igi2ForestStand stand in forest.Stands)
            {
                device.MeshDraws.Clear();
                presenter.Draw(CameraIn(stand, 0));
                if (presenter.VisibleTreeCount == 0)
                {
                    continue;
                }

                MefModel model = Assert.IsType<MefModel>(Resolve(level, stand.Model));
                int leafGroups = Igi2ForestPresenter.FoliageFaceGroupOf(model) < 0 ? 0 : 1;
                int trunkGroups = model.DrawRecords.Count - leafGroups;
                int unsorted = device.MeshDraws.Count;

                // One draw per face group per visible tree. Only a level whose stands all draw the same
                // model can be counted this way — the camera is in one stand but the frame culls all of
                // them, and a level that mixes a one-group model with a two-group one has no single
                // multiplier.
                if (forest.Stands.All(s => s.Model == stand.Model))
                {
                    counted++;
                    Assert.Equal(presenter.VisibleTreeCount * model.DrawRecords.Count, unsorted);

                    // The passes do not interleave: the trunk's index ranges all come before the
                    // foliage's, and no range belongs to both.
                    List<int> offsets = [.. device.MeshDraws.Select(static d => d.IndexOffset)];
                    HashSet<int> trunkRanges = [.. offsets.Take(presenter.VisibleTreeCount * trunkGroups)];
                    HashSet<int> leafRanges = [.. offsets.Skip(presenter.VisibleTreeCount * trunkGroups)];

                    Assert.Equal(trunkGroups, trunkRanges.Count);
                    Assert.Equal(leafGroups, leafRanges.Count);
                    Assert.Empty(trunkRanges.Intersect(leafRanges));
                }

                // Sorted foliage moves the leaves to the end and blends them, which is the third state
                // block. The same draws come out; the foliage is simply in a different pass.
                presenter.SortedFoliage = true;
                device.MeshDraws.Clear();
                presenter.Draw(CameraIn(stand, 0));
                presenter.SortedFoliage = false;

                Assert.Equal(unsorted, device.MeshDraws.Count);

                int firstBlend = device.MeshDraws.FindIndex(static d => d.Blend == BlendMode.AlphaBlend);
                if (firstBlend < 0)
                {
                    // Every visible tree came from a model with no flagged foliage group.
                    continue;
                }

                sorted++;
                Assert.All(
                    device.MeshDraws.Take(firstBlend),
                    static d => Assert.Equal(BlendMode.Opaque, d.Blend));
                Assert.All(
                    device.MeshDraws.Skip(firstBlend),
                    static d => Assert.Equal(BlendMode.AlphaBlend, d.Blend));
            }
        }

        // Five of the 22 levels plant one stand and no more, which is what makes their frames countable
        // against a single model; the rest mix two to nine stands and several models.
        Assert.Equal(5, counted);
        Assert.True(sorted >= 50, $"Only {sorted} stands drew a blended foliage pass.");
    }

    /// <summary>The foliage pass lowers the cut-out to 32 and puts it back afterwards.</summary>
    /// <remarks>
    /// <para>
    /// <c>ForestRender_SetStates_Foliage</c> (<c>igi2ed 0x57BDF0</c>) sets <c>D3DRS_ALPHAREF</c> to 32,
    /// well below the 128 this port cuts out at everywhere else. It matters because I.G.I. 2's leaf
    /// sprites do not carry a strictly nought-or-255 alpha the way I.G.I. 1's do: theirs ramps, and
    /// testing it at a half erodes the sprite to a fraction of its authored shape.
    /// </para>
    /// <para>
    /// The restore is half the claim. The reference is device state that spans a run of draws, so a
    /// pass that lowered it and walked away would leave every later cut-out in the frame — fencing,
    /// railings, window glass — testing at the foliage's threshold and keeping texels the original cuts.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_foliage_pass_lowers_the_cut_out_and_restores_it()
    {
        int checkedStands = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            if (forest.IsEmpty)
            {
                continue;
            }

            RecordingGraphicsDevice device = new();
            using Igi2ForestPresenter presenter = new(
                device,
                forest,
                name => Resolve(level, name),
                (_, _) => TextureHandle.None);

            foreach (Igi2ForestStand stand in forest.Stands)
            {
                device.AlphaReferences.Clear();
                presenter.Draw(CameraIn(stand, 0));
                if (presenter.VisibleTreeCount == 0)
                {
                    continue;
                }

                checkedStands++;

                // Lowered once, restored once, in that order — and the device is left where it started.
                Assert.Equal(
                    [Igi2ForestPresenter.FoliageAlphaReference, IGraphicsDevice.DefaultAlphaReference],
                    device.AlphaReferences);
                Assert.Equal(IGraphicsDevice.DefaultAlphaReference, device.AlphaReference);
            }
        }

        Assert.True(checkedStands > 0, "no stand drew a tree, so the cut-out was never exercised.");
    }

    /// <summary>Every tree model but one flags the face group that carries its leaves.</summary>
    /// <remarks>
    /// <para>
    /// <b>84 of the 85 stands</b> draw a model with a flagged group. A two-group model always flags the
    /// second, which is the arrangement the split assumes: group 0 is the trunk and group 1 the canopy. A
    /// one-group model flags its only group, so the whole thing is foliage — those are the undergrowth
    /// models, and they have no trunk to speak of.
    /// </para>
    /// <para>
    /// The exception is <c>timberland</c>'s <c>935_01_1</c>, whose single group carries no flag at all and
    /// is therefore drawn entirely as trunk. It is the one model in the corpus outside the <c>900_</c> and
    /// <c>940_</c> families the other stands draw from, so a stand of bare stumps rather than a stand of
    /// trees is exactly what the flag is saying.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_tree_model_but_one_flags_its_foliage()
    {
        int flagged = 0;
        List<string> unflagged = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2ForestStand stand in Forest(level).Stands)
            {
                MefModel model = Assert.IsType<MefModel>(Resolve(level, stand.Model));
                int foliage = Igi2ForestPresenter.FoliageFaceGroupOf(model);

                if (foliage < 0)
                {
                    unflagged.Add(stand.Model);
                    continue;
                }

                flagged++;

                // The last group, which for a two-group model is the second: the trunk comes first.
                Assert.Equal(model.DrawRecords.Count - 1, foliage);
            }
        }

        Assert.Equal(84, flagged);
        Assert.Equal(["935_01_1"], unflagged);
    }

    /// <summary>A tilted tree is drawn tilted, and a scaled one scaled.</summary>
    /// <remarks>
    /// A quarter of the corpus's trees carry non-zero angles — the bake leans them to the ground — and
    /// every one carries a per-axis scale. Both ride on the one matrix a draw takes, so a presenter that
    /// dropped either would still draw a forest, of identical upright clones.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_tilted_tree_is_drawn_tilted_and_a_scaled_one_scaled()
    {
        int tilted = 0;
        int scaled = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            foreach (Igi2ForestStand stand in forest.Stands)
            {
                foreach (Igi2Tree tree in stand.Trees)
                {
                    Matrix3f orientation = Igi2ForestPresenter.OrientationOf(tree);

                    // The first row's length is the X scale, whatever the rotation.
                    float length = MathF.Sqrt(
                        (orientation.M11 * orientation.M11)
                        + (orientation.M12 * orientation.M12)
                        + (orientation.M13 * orientation.M13));

                    // Relative, because the scale rides through a rotation and back out of a square
                    // root; an absolute comparison lands on a rounding boundary for one tree in tens of
                    // thousands and says nothing useful when it does.
                    Assert.True(
                        Math.Abs(length - tree.Scale.X) <= 1e-4f * Math.Max(1f, tree.Scale.X),
                        $"A tree scaled {tree.Scale.X} came back as {length}.");

                    tilted += Math.Abs(tree.Angles.X) > 1e-4f || Math.Abs(tree.Angles.Y) > 1e-4f ? 1 : 0;
                    scaled += Math.Abs(tree.Scale.X - 1f) > 1e-4f ? 1 : 0;
                }
            }
        }

        Assert.True(tilted > 5_000, $"Only {tilted} trees were tilted, which is too few to be the bake.");
        Assert.True(scaled > 5_000, $"Only {scaled} trees were scaled.");
    }

    /// <summary>Stood among the stand's own trees, at head height, on one of eight headings.</summary>
    /// <remarks>
    /// The stand's <em>centre</em> is not a place to stand: a tree's Z is local to the task and carries
    /// the whole offset from the task down to the ground, which in mission 1 is 1.9 million units. A
    /// camera at the task's own Z would be four hundred metres over the wood and see none of it.
    /// </remarks>
    /// <summary>An eye standing among a stand's trees, head height above the middle one.</summary>
    /// <remarks>
    /// <b>In metres</b>, which is what a frame carries: every camera downstream of <c>WorldScene</c> is,
    /// and the host hands the forest the same one it hands the ground. The presenter converts it into the
    /// cull's own space itself. This used to build the camera in engine units to match what the presenter
    /// then did with it, which was self-consistent and agreed with nothing else -- the host's real camera
    /// arrived in metres and every tree in every level was four thousand times too far away to draw.
    /// </remarks>
    private static Camera3D CameraIn(Igi2ForestStand stand, int heading)
    {
        Igi2Tree middle = stand.Trees[stand.Trees.Count / 2];
        return new Camera3D(
            WorldScene.ToMetres(new Vector3d(middle.Position.X, middle.Position.Y, middle.Position.Z + 7000)),
            Camera3D.LookRotation(heading * MathF.Tau / Headings, 0f));
    }

    private static MefModel? Resolve(Igi2Level level, string name)
    {
        if (!level.Table.TryResolveMesh(name, out string? mesh))
        {
            mesh = name;
        }

        return level.TryReadModel(mesh, out MefModel? model) ? model : null;
    }

    private static Igi2Forest Forest(Igi2Level level)
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
