using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Lines of sight through mission 1, <c>World_LineOfSight</c> (<c>igi2 0x551F60</c>) and the handlers it calls.</summary>
public sealed class RetailIgi2LineOfSightTests
{
    private static readonly Lazy<(Igi2Level Level, Igi2CollisionWorld World, Igi2Forest Forest, Igi2GameMaterials Materials)> Mission1 = new(static () =>
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
        return (level, world, forest, Igi2Session.Start().Require<Igi2GameMaterials>());
    });

    /// <summary>High over the level a sight crosses nothing: it is clear, all of it gets through, and it has no material.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_sight_high_over_the_level_is_clear()
    {
        (Igi2Level level, Igi2CollisionWorld world, _, Igi2GameMaterials materials) = Mission1.Value;
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Vector3d start = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 2000000.0);
        Igi2LineOfSight sight = new(start, new Vector3d(start.X + 40960.0, start.Y, start.Z), materials);

        Assert.True(world.LineOfSight(sight, uint.MaxValue));
        Assert.Equal(1f, sight.Transparency);
        Assert.Null(sight.Material);
    }

    /// <summary>
    /// The ground blocks a sight run down through it when the ground is asked for, and only then: the object groups
    /// alone do not reach it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_ground_blocks_a_sight_only_when_asked_for()
    {
        (Igi2Level level, Igi2CollisionWorld world, _, Igi2GameMaterials materials) = Mission1.Value;
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Vector3d over = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 8192.0);
        Vector3d under = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z - 81920.0);

        Igi2LineOfSight ground = new(over, under, materials);
        Assert.False(world.LineOfSight(ground, Igi2CollisionWorld.TerrainGroup));
        Assert.Equal(0f, ground.Transparency);

        Igi2LineOfSight objectsOnly = new(over, under, materials);
        _ = world.LineOfSight(objectsOnly, Igi2CollisionWorld.ForestGroup);
        Assert.NotEqual(0f, objectsOnly.Transparency);
    }

    /// <summary>
    /// A solid a line trace strikes from above also stops a sight along the same line: every face a trace can strike
    /// is one a sight crosses, and mission 1's walls let nothing through. A face with no material row blocks it too,
    /// naming no material (<c>RigidDynCubeObj_LineOfSightHandler</c>, <c>igi2 0x58DA34</c>).
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_solid_struck_from_above_blocks_the_sight()
    {
        (_, Igi2CollisionWorld world, _, Igi2GameMaterials materials) = Mission1.Value;
        int blocked = 0;
        foreach (Igi2RigidObject solid in world.Objects.Where(static o => o.Owner is Igi2CollisionOwner { TypeName: not "GunPickup" }).Take(60))
        {
            Vector3d p = solid.Position;
            double r = solid.BoundingRadius;
            Vector3d over = new(p.X, p.Y, p.Z + r), under = new(p.X, p.Y, p.Z - r);
            Igi2LineTrace trace = new(1);
            trace.Starts[0] = over;
            trace.Ends[0] = under;
            world.TraceLines(trace, Igi2RigidObject.RigidGroup);
            Igi2Intersection hit = trace.Intersections[0];
            if (!hit.IsHit || (hit.EnterMaterial ?? hit.LeaveMaterial) is not { } row || materials[row]?.QMaterial.Transparency != 0f)
            {
                continue;
            }

            Igi2LineOfSight sight = new(over, under, materials);
            Assert.False(world.LineOfSight(sight, Igi2RigidObject.RigidGroup));
            Assert.Equal(0f, sight.Transparency);
            Assert.InRange(sight.T, 0f, 1f);
            blocked++;
        }

        Assert.True(blocked > 10, $"only {blocked} solids were struck from above.");
    }

    /// <summary>A line into a trunk that a trace stops at blocks a sight too, and the tree that crossed it first ends the walk.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_trunk_blocks_the_sight()
    {
        (_, Igi2CollisionWorld world, Igi2Forest forest, Igi2GameMaterials materials) = Mission1.Value;
        int blocked = 0;
        foreach (Igi2Tree tree in forest.Stands.SelectMany(static s => s.Trees).Where(static t => t.Scale.X > 0.5f && t.Scale.Z > 0.5f).Take(40))
        {
            double z = tree.Position.Z + 2048.0;
            Vector3d from = new(tree.Position.X - 4096.0, tree.Position.Y, z), to = new(tree.Position.X + 4096.0, tree.Position.Y, z);
            Igi2LineTrace trace = new(1);
            trace.Starts[0] = from;
            trace.Ends[0] = to;
            world.TraceLines(trace, Igi2CollisionWorld.ForestGroup);
            if (!trace.Intersections[0].IsHit)
            {
                continue;
            }

            Igi2LineOfSight sight = new(from, to, materials);
            Assert.False(world.LineOfSight(sight, Igi2CollisionWorld.ForestGroup));
            Assert.Equal(0f, sight.Transparency);
            blocked++;
        }

        Assert.True(blocked > 10, $"only {blocked} trunks blocked a sight.");
    }

    /// <summary>
    /// A solid placed as a <c>GunPickup</c> is one its own handler would cross, but the type answers sight with nothing,
    /// so the world lets a sight through it (<c>GunPickup_Open</c>, <c>igi2 0x46C780</c>); one of its attachments would
    /// not.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Sight_passes_through_a_gun_pickup()
    {
        (_, Igi2CollisionWorld world, _, Igi2GameMaterials materials) = Mission1.Value;
        foreach (Igi2RigidObject solid in world.Objects)
        {
            Vector3d p = solid.Position;
            double r = solid.BoundingRadius;
            Vector3d over = new(p.X, p.Y, p.Z + r), under = new(p.X, p.Y, p.Z - r);
            Igi2LineOfSight direct = new(over, under, materials);
            Igi2RigidLineOfSight.Meet(solid, direct);
            if (!direct.IsHit)
            {
                continue;
            }

            Assert.True(new Igi2CollisionWorld([As(solid, depth: 0)], null).LineOfSight(new(over, under, materials), uint.MaxValue));
            Assert.False(new Igi2CollisionWorld([As(solid, depth: 1)], null).LineOfSight(new(over, under, materials), uint.MaxValue));
            return;
        }

        Assert.Fail("no solid crosses a line down through it.");

        static Igi2RigidObject As(Igi2RigidObject solid, int depth)
        {
            Igi2RigidObject pickup = new(
                new Igi2CollisionOwner("GunPickup", 1, "pickup", depth),
                [.. Enumerable.Range(0, MefCollisionMesh.MeshLevels).Select(solid.Mesh)],
                solid.Radius,
                solid.IsBuildingMesh)
            {
                Position = solid.Position,
                Scale = solid.Scale,
            };
            solid.Orientation.CopyTo(pickup.Orientation, 0);
            return pickup;
        }
    }
}
