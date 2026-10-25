using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Lines against mission 1's stands of trees, <c>Forest_GetNextZHandler</c> and <c>Forest_TraceLinesHandler</c>.</summary>
public sealed class RetailIgi2ForestColliderTests
{
    private static readonly Lazy<(Igi2CollisionWorld World, Igi2Forest Forest)> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        files.SetAlias(
            VirtualFileSystem.MissionAlias,
            "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace(Path.DirectorySeparatorChar, '/') + "/");
        Igi2Forest forest = Igi2Forest.Build(level.Root, new ResourceRegistry(files));
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain", forest);
        return (world, forest);
    });

    /// <summary>Every stand whose tree model has a collision mesh becomes a collider, with a tree radius.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_stands_become_colliders()
    {
        (Igi2CollisionWorld world, Igi2Forest forest) = Mission1.Value;

        Assert.NotEmpty(world.Forests);
        Assert.True(world.Forests.Count <= forest.Stands.Count);
        Assert.All(world.Forests, static stand => Assert.True(stand.TreeRadius > 0f));
    }

    /// <summary>A line down onto a tree strikes it before the ground under it, and the hit is the stand's.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_down_onto_a_tree_strikes_the_stand()
    {
        (Igi2CollisionWorld world, Igi2Forest forest) = Mission1.Value;
        int struck = 0;
        foreach (Igi2Tree tree in Trees(forest).Take(40))
        {
            WorldLineTrace trace = new(1);
            trace.Starts[0] = new Vector3d(tree.Position.X, tree.Position.Y, tree.Position.Z + 200000.0);
            world.NextZ(trace, [-400000.0], Igi2CollisionWorld.ForestGroup | Igi2CollisionWorld.TerrainGroup);

            WorldIntersection hit = trace.Intersections[0];
            if (hit.IsHit && hit.HitObject is Igi2ForestCollider)
            {
                Assert.True(hit.Position.Z > tree.Position.Z, "the tree was struck below its own foot.");
                struck++;
            }
        }

        Assert.True(struck > 10, $"only {struck} trees were struck from above.");
    }

    /// <summary>A line level with the ground, run into a trunk, stops at it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_into_a_trunk_stops_at_it()
    {
        (Igi2CollisionWorld world, Igi2Forest forest) = Mission1.Value;
        int struck = 0;
        foreach (Igi2Tree tree in Trees(forest).Take(40))
        {
            double z = tree.Position.Z + 2048.0;
            WorldLineTrace trace = new(1);
            trace.Starts[0] = new Vector3d(tree.Position.X - 4096.0, tree.Position.Y, z);
            trace.Ends[0] = new Vector3d(tree.Position.X + 4096.0, tree.Position.Y, z);
            world.TraceLines(trace, Igi2CollisionWorld.ForestGroup);

            WorldIntersection hit = trace.Intersections[0];
            if (hit.IsHit)
            {
                Assert.IsType<Igi2ForestCollider>(hit.HitObject);
                Assert.True(hit.Position.X < tree.Position.X + 1.0, "the trunk was struck past its own middle.");
                struck++;
            }
        }

        Assert.True(struck > 10, $"only {struck} trunks stopped a line.");
    }

    /// <summary>The stand's trees, ordinary-sized ones first.</summary>
    private static IEnumerable<Igi2Tree> Trees(Igi2Forest forest) =>
        forest.Stands.SelectMany(static stand => stand.Trees).Where(static t => t.Scale.X > 0.5f && t.Scale.Z > 0.5f);
}
