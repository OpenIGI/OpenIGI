using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Project I.G.I.'s soldiers stand, think and are struck in I.G.I. 2's mission one, in a soldier's stead.</summary>
public sealed class RetailIgi1GuestLevelTests(ITestOutputHelper output)
{
    [RequiresTitleDataFact("igi1", "igi2")]
    public void A_project_igi_soldier_stands_for_a_mission_one_soldier_thinks_on_its_graph_and_is_struck_where_it_stands()
    {
        string model = Igi1SoldierModel();
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2HumanSoldierTask claimed = m.Tree.OfType<Igi2HumanSoldierTask>().First(static t => t.Ai is { GraphId: >= 0 });
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        ResourceRegistry resources = new(files);
        IReadOnlyDictionary<int, GraphFile> graphs = Igi2AiGraphs.Build(
            m.Tree[0], resources, m.Run.Natives, m.Services!.Require<Igi2ScriptFunctions>()).ToGraphFiles(resources);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            m.Collision!, Igi2Session.Start().Require<Igi2PlayerTuning>().General, spawn.Position, spawn.Gamma, AiDifficulty.Normal, null,
            m.Level.ModelFor(spawn.Model)!.BoneHierarchy);

        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), [model]);
        Igi1GuestLevel level = Igi1GuestLevel.Open(content);
        HumanSoldierTask soldier = level.AddSoldier(
            new Igi1GuestSoldier(model, "AITYPE_PATROL_AK", claimed.Ai!.GraphId, claimed.Position, claimed.Gamma, claimed.Team)
            {
                Weapons = ["WEAPON_ID_AK47"],
                PreferredId = claimed.Ai.Id,
            });
        Igi1HostedLevel hosted = level.Start(new Igi1GuestLevelStart(m.Collision!, player.Human, new IgiRandom(), AiDifficulty.Normal)
        {
            Graphs = graphs,
        });
        hosted.StandFor(claimed, soldier);

        Assert.Equal(1, hosted.Thinking);
        Assert.Equal("WEAPON_ID_AK47", Assert.Single(hosted.Groups).Roster.Agents[0].WeaponType?.ScriptId);
        Assert.Same(hosted.StandIns.Single(), claimed.StandIn);

        for (int tick = 0; tick < 90; tick++)
        {
            hosted.Tick();
        }

        Vector3d feet = ((IAiHuman)soldier).Position;
        output.WriteLine($"placed {claimed.Position}, stands {feet} after {hosted.Ticks} ticks");
        Assert.True(Vector3d.Distance(new Vector3d(feet.X, feet.Y, 0), new Vector3d(claimed.Position.X, claimed.Position.Y, 0)) < 4096 * 30);
        Assert.False(soldier.IsDead);

        // A round of the level's, cast at the soldier through the level's world, meets its stand-in and takes it down.
        Vector3d chest = soldier.OriginalViewPosition - new Vector3d(0, 0, 1024);
        Vector3d from = chest + new Vector3d(4096 * 3, 0, 0);
        WorldLineTrace line = new(1);
        line.Starts[0] = from;
        line.Ends[0] = chest - new Vector3d(4096, 0, 0);
        ((IHostCollisionWorld)m.Collision!).TraceLines(line);
        WorldIntersection met = line.Intersections[0];
        Assert.True(met.IsHit);
        IWeaponHitTarget target = Assert.IsAssignableFrom<IWeaponHitTarget>(met.HitObject!.Handler);
        Assert.Same(claimed.StandIn, target);
        for (int round = 0; round < 10 && !soldier.IsDead; round++)
        {
            target.WeaponHit(new WeaponHit(met.Position, met.Normal, 1000f, new Vector3f(-1f, 0f, 0f), player.Human, null));
            hosted.Tick();
        }

        Assert.True(soldier.IsDead);
        hosted.Leave();
        Assert.DoesNotContain(((IHostCollisionWorld)m.Collision!) is Collision.Igi2CollisionWorld world ? world.OtherColliders : [], c => ReferenceEquals(c.Owner, soldier));
    }

    // The body Project I.G.I.'s first mission dresses its first soldier in.
    private static string Igi1SoldierModel()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        return root.Descendants().OfType<HumanSoldierTask>().First().Model;
    }
}
