using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Mounted guns, as stationarygun.c runs them and humanplayer.c works them.</summary>
public sealed class RetailIgi2StationaryGunTests
{
    /// <summary>
    /// Mission 14's mounted guns are armed with their weapon and mounted on their holders; unworked, the barrel droops towards nine
    /// tenths of its lowest tilt.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_unworked_gun_droops()
    {
        (Igi2Level level, Igi2Session session, Igi2StationaryGunTask gun) = Gun();
        Assert.NotNull(gun.Firing);
        Assert.Equal(gun.WeaponId, gun.Firing!.Inventory.Current?.WeaponType.WeaponId);
        Assert.NotNull(gun.BarrelModel);
        Assert.NotEqual(gun.Position, gun.Mount);

        Flow.Igi2LevelRun run = Bare();
        for (int tick = 0; tick < 30; tick++)
        {
            gun.Run(run);
        }

        float rest = (float)(gun.MaxDownAngle * 0.8999999761581421 * Igi2StationaryGunTask.DegreesToRadians);
        Assert.Equal(rest, gun.FinalTilt, 4);
        GC.KeepAlive(session);
    }

    /// <summary>
    /// Activated from behind, the gun is worked from the gun state: the player's view is seated behind the barrel, the look turns it
    /// within its limits, the fire key fires its weapon; pressed afresh, the activate key lets it go and the view is the player's own.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_works_a_gun()
    {
        (Igi2Level level, Igi2Session session, Igi2StationaryGunTask gun) = Gun();
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Vector3d stand = gun.ActivatePosition(out _);
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, stand, 0f, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        Flow.Igi2LevelRun run = Bare();

        player.Human.ScriptActivate(gun);
        Assert.Equal(Igi2PlayerState.ActivateStationaryGun, player.Human.Locomotion.CurrentState);
        player.Tick(RetailIgi2Mission1.Idle);
        gun.Run(run);
        Assert.Same(player.Human, gun.Operator);
        Assert.True(gun.IsActivated);
        Assert.True(player.Human.View.IsOverride);

        for (int tick = 0; tick < 300; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { MouseY = 1f });
            gun.Run(run);
        }

        Assert.Equal((float)(gun.MaxUpAngle * Igi2StationaryGunTask.DegreesToRadians), gun.Tilt, 4);
        Assert.Equal(Igi2StationaryGunTask.TiltLimited, gun.LimitFlags & Igi2StationaryGunTask.TiltLimited);

        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Fire });
            gun.Run(run);
        }

        Assert.True(gun.Firing!.Inventory.TotalBulletsFired > 0);

        player.Tick(RetailIgi2Mission1.Idle);
        gun.Run(run);
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate });
        gun.Run(run);
        Assert.NotEqual(Igi2PlayerState.ActivateStationaryGun, player.Human.Locomotion.CurrentState);
        Assert.False(gun.IsActivated);
        Assert.Null(gun.Operator);
        Assert.False(player.Human.View.IsOverride);
    }

    private static (Igi2Level Level, Igi2Session Session, Igi2StationaryGunTask Gun) Gun()
    {
        Igi2Level level = Igi2Level.Find("location1", "level4");
        Igi2Session session = Igi2Session.Start();
        Igi2StationaryGunTask gun = All(level.Root).OfType<Igi2StationaryGunTask>().First();
        gun.Prepare(session.Require<Igi2HumanWeapons>(), level.ModelFor);
        return (level, session, gun);
    }

    // A level with no flow of its own, so that the gun counts its ticks itself.
    private static Flow.Igi2LevelRun Bare()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        GameTask root = tree.New(null, types.Register<ContainerTask>("Container"));
        return new(root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());
    }

    private static IEnumerable<GameTask> All(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask inner in All(child))
            {
                yield return inner;
            }
        }
    }
}
