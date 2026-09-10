using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The forests, uploaded and drawn.</summary>
public sealed class RetailIgi2ForestPresenterTests
{
    /// <summary>Every stand uploads its model and draws one instance per tree.</summary>
    /// <remarks>
    /// <para>
    /// The winding is the assertion worth having. <c>ForestRender_SetGenericState_Trunk</c>
    /// (<c>igi2ed 0x57C5A0</c>) sets <c>CULLMODE = CCW</c> where the terrain's state sets <c>CW</c>, so
    /// the two passes disagree and the forest's has to be set per pass. A frame that set it once would
    /// draw one of the two inside out, and from above neither looks wrong.
    /// </para>
    /// <para>
    /// One draw per tree is what this does and not what the original does — it walks a BSP per stand and
    /// picks a level of detail per tree. The count is pinned so that when the cull arrives the change is
    /// visible rather than silent.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_stand_draws_one_instance_per_tree()
    {
        int levels = 0;
        int stands = 0;
        int trees = 0;

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
                _ => TextureHandle.None);

            stands += presenter.StandCount;
            trees += presenter.TreeCount;

            presenter.Draw(default);

            Assert.Equal(presenter.TreeCount, device.MeshDraws.Count);
            Assert.Equal(CullMode.CounterClockwise, device.CullMode);
            Assert.All(device.MeshDraws, draw => Assert.Equal(BlendMode.Opaque, draw.Blend));
        }

        Assert.Equal(22, levels);
        Assert.Equal(85, stands);
        Assert.Equal(54_537, trees);
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
