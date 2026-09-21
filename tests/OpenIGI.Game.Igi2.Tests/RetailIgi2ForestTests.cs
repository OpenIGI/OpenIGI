using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The forests a level plants, placed in the world.</summary>
public sealed class RetailIgi2ForestTests
{
    /// <summary>Every stand's tree list loads, and its trees fall inside the stand.</summary>
    /// <remarks>
    /// <para>
    /// The bound is the claim. <c>Forest_DrawTrees</c> (<c>igi2ed 0x64AC80</c>) culls a stand against a
    /// square of <c>AreaSize * 4096</c> centred on the task and walks its BSP over
    /// <c>±AreaSize * 2048</c>, so a tree outside that square would be one the original never draws —
    /// which is what a world-space reading of the stored position would produce for every tree at once.
    /// </para>
    /// <para>
    /// A little tolerance, because a tree is a volume rather than a point and the bake plants them up to
    /// their own radius past the edge. Not enough tolerance to survive being wrong about the origin.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_tree_stands_inside_the_stand_that_planted_it()
    {
        int levels = 0;
        int stands = 0;
        int trees = 0;
        int outside = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            if (forest.IsEmpty)
            {
                continue;
            }

            levels++;
            stands += forest.Stands.Count;

            foreach (Igi2ForestStand stand in forest.Stands)
            {
                Assert.False(string.IsNullOrWhiteSpace(stand.Model), $"Stand {stand.TaskId} names no model.");
                Assert.True(stand.AreaSize > 0, $"Stand {stand.TaskId} has area {stand.AreaSize}.");

                double limit = stand.HalfExtent * 1.25;
                foreach (Igi2Tree tree in stand.Trees)
                {
                    trees++;
                    if (Math.Abs(tree.Position.X - stand.Centre.X) > limit
                        || Math.Abs(tree.Position.Y - stand.Centre.Y) > limit)
                    {
                        outside++;
                    }
                }
            }
        }

        Assert.Equal(22, levels);
        Assert.Equal(85, stands);
        Assert.Equal(54_537, trees);

        // Not one tree falls outside the square its stand is culled against, which is what says the
        // stored position is local. Read as world coordinates, every one of the 54,537 would.
        Assert.Equal(0, outside);
    }

    /// <summary>The trees stand on the ground the terrain describes.</summary>
    /// <remarks>
    /// <para>
    /// The check that settles the vertical reading. A tree's stored Z carries a large constant bias —
    /// the shipped files run to about −1.9 million units — which on its own could mean either a local
    /// offset from the stand or an absolute height. Adding the stand's own Z and comparing against the
    /// terrain underneath is what distinguishes them: a wrong reading puts the whole forest hundreds of
    /// metres above or below the ground it was planted on.
    /// </para>
    /// <para>
    /// Compared as a median, because a stand may overhang water or a cliff and because the ground is
    /// sampled at the nearest texel rather than interpolated.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_trees_stand_on_the_ground()
    {
        List<double> offsets = [];

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2Forest forest = Forest(level);
            Igi2TerrainSurface surface = Surface(level);
            if (forest.IsEmpty || surface.IsEmpty)
            {
                continue;
            }

            foreach (Igi2ForestStand stand in forest.Stands)
            {
                foreach (Igi2Tree tree in stand.Trees)
                {
                    foreach (Igi2TerrainTile tile in surface.Tiles)
                    {
                        double left = tile.Centre.X - (tile.Width * tile.TexelWidth * 0.5);
                        double top = tile.Centre.Y - (tile.Height * tile.TexelHeight * 0.5);
                        int x = (int)((tree.Position.X - left) / tile.TexelWidth);
                        int y = (int)((tree.Position.Y - top) / tile.TexelHeight);
                        if (x < 0 || y < 0 || x >= tile.Width || y >= tile.Height)
                        {
                            continue;
                        }

                        offsets.Add(tree.Position.Z - tile.HeightAt(x, y));
                        break;
                    }
                }
            }
        }

        Assert.True(offsets.Count > 20_000, $"Only {offsets.Count} trees stood over a tile.");

        offsets.Sort();
        double median = offsets[offsets.Count / 2];

        // Within a couple of metres of the ground. A world-space reading of the stored Z would put this
        // in the hundreds of metres, which is the failure this exists to catch.
        Assert.InRange(median, -2.0 * 4096.0, 2.0 * 4096.0);
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

    private static Igi2TerrainSurface Surface(Igi2Level level)
    {
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        return Igi2TerrainSurface.Build(level.Root, archive);
    }
}
