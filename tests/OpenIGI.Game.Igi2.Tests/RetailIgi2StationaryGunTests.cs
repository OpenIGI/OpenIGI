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
        (_, Igi2Session session, Igi2StationaryGunTask gun) = Gun();
        Assert.NotNull(gun.Firing);
        Assert.Equal(gun.WeaponId, gun.Firing.Inventory.Current?.WeaponType.WeaponId);
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
    public void A_co_op_clients_copy_of_a_gun_its_own_player_works_turns_at_once()
    {
        (Igi2Level level, Igi2Session session, Igi2StationaryGunTask gun) = Gun();
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, gun.ActivatePosition(out _), 0f, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        Flow.Igi2LevelRun run = Bare();

        // A client's copy, held to the server's aim; then its own player works it.
        gun.Hold(new Igi2GunAim(0f, 0f, 0));
        player.Human.ScriptActivate(gun);
        player.Tick(RetailIgi2Mission1.Idle);
        gun.Run(run);
        Assert.Same(player.Human, gun.Operator);

        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { MouseX = 1f });
            gun.Run(run);

            // The server's word, a round trip behind, does not pull it back while the player works it.
            gun.Hold(new Igi2GunAim(0f, 0f, 0));
        }

        Assert.NotEqual(0f, gun.Turn);
    }

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
        Assert.Same(gun, player.Human.AttachParent);

        // The gunner rides the gun (Human_Attach): as the holder turns, the body turns and is carried round with it.
        for (int tick = 0; tick < 30; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { MouseX = 1f });
            gun.Run(run);
        }

        Assert.NotEqual(0f, gun.Turn);
        player.Tick(RetailIgi2Mission1.Idle);
        Assert.Equal(World.Igi2Pose.Angles(gun.ModelFrame).Gamma, player.Human.Gamma, 3);

        for (int tick = 0; tick < 300; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { MouseY = -1f });
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
        Assert.Null(player.Human.AttachParent);
        Assert.False(player.Human.View.IsOverride);
    }

    /// <summary>
    /// Mission 17's automatic gun carries its head on its holder's attach point and its barrel on the head; an enemy it sees, the head
    /// and barrel turn onto at their speeds, and on target it fires, its barrel spinning.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_turret_turns_onto_what_it_sees_and_fires()
    {
        Igi2Level level = Igi2Level.Find("location1", "level7");
        Igi2Session session = Igi2Session.Start();
        Igi2AiStationaryGunHolderTask turret = All(level.Root).OfType<Igi2AiStationaryGunHolderTask>().Single(static t => t.Id == 3783);
        Detection.Igi2DetectionWorld detection = new(null, session.Require<World.Igi2GameMaterials>());
        turret.Prepare(session.Require<Igi2HumanWeapons>(), level.ModelFor, session.Require<World.Igi2MagicObjTypes>(), detection);
        Assert.Equal("720_06_1", turret.Gun!.HeadModel);

        Vector3d target = turret.Gun.BarrelPosition + new Vector3d(30.0 * 4096.0, 10.0 * 4096.0, -2.0 * 4096.0);
        Target enemy = new(target);
        turret.Detection!.Track(enemy).FullVisible[0] = true;
        turret.OnExpression = string.Empty;
        Flow.Igi2LevelRun run = Bare();
        for (int tick = 0; tick < 120; tick++)
        {
            turret.Run(run);
        }

        Assert.True(turret.Gun.HasTarget);
        Vector3d to = target - turret.Gun.BarrelPosition;
        double length = to.Length;
        IReadOnlyList<float> f = turret.Gun.BarrelFrame;
        double cosine = ((f[1] * to.X) + (f[4] * to.Y) + (f[7] * to.Z)) / length;
        Assert.True(cosine > Math.Cos(10.0 * Math.PI / 180.0), $"barrel off by {Math.Acos(cosine) * 180.0 / Math.PI} degrees");
        Assert.True(turret.Gun.IsOnTarget);
        Assert.True(turret.Gun.Firing.Inventory.TotalBulletsFired > 0);
        Assert.True(turret.Gun.Spin > 0f);
        GC.KeepAlive(session);
    }

    /// <summary>
    /// An automatic gun's barrel is drawn as an object of its gun's own: on the first magic vertex of the head its holder's attach point
    /// carries, walked after the head, where the gun places it at rest.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_turrets_barrel_hangs_on_its_head()
    {
        Igi2Level level = Igi2Level.Find("location1", "level7");
        Igi2Session session = Igi2Session.Start();
        World.Igi2MagicObjTypes magic = session.Require<World.Igi2MagicObjTypes>();
        Igi2AiStationaryGunHolderTask turret = All(level.Root).OfType<Igi2AiStationaryGunHolderTask>().Single(static t => t.Id == 3783);
        turret.Prepare(session.Require<Igi2HumanWeapons>(), level.ModelFor, magic, null);

        (World.Igi2DrawableSceneResult built, _) = Igi2LevelScene.Build(level, magic);
        List<WorldPlacement> own = [.. built.Scene.Placements.Where(static p => p.LevelId == 3783)];
        int head = own.FindIndex(static p => p.Instance.Name == "720_06_1");
        int barrel = own.FindIndex(static p => p.Instance.Name == "720_07_1");
        Assert.True(head >= 0 && barrel > head, $"head {head}, barrel {barrel}");
        Vector3d at = WorldScene.ToUnits(own[barrel].Position);
        Assert.True((at - turret.Gun!.BarrelPosition).Length < 1.0, $"barrel at {at}, gun puts it at {turret.Gun.BarrelPosition}");
        GC.KeepAlive(session);
    }

    // Something seen, standing still.
    private sealed class Target(Vector3d at) : IAiDetectable
    {
        public Vector3d DetectablePosition => at;

        public float DetectableRadius => 4096f;

        public float? VisibilityFactor => null;

        public bool IsGone => false;

        public AiDetectionInfo DetectionInfo() =>
            new(AiDetectableKind.Human, at, at, at, 0, IsAlive: true, IsActive: true);
    }

    /// <summary>
    /// Zaleb's Stronghold (<c>location2/level5</c>) opens with the player locked onto the door gun of the helicopter a spline guide
    /// flies (<c>HumanPlayer_Activate(4021), HumanPlayer_LockActivate(TRUE)</c>). The gun is held to the helicopter
    /// (<c>StationaryGun_UpdateAttachmentHandler</c>): as it flies and turns, the holder, the barrel's mount and the gunner go with it,
    /// and the locked activate key does not let the gunner off.
    /// </summary>
    /// <remarks>Held to nothing, the gun stayed where the level placed it while the helicopter flew off with the player.</remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_helicopters_door_gun_rides_it_and_carries_its_gunner()
    {
        Igi2Level level = Igi2Level.Find("location2", "level5");
        Igi2Session session = Igi2Session.Start();
        Interaction.Igi2EditRigidObjTask helicopter = All(level.Root).OfType<Interaction.Igi2EditRigidObjTask>().Single(static t => t.Id == 151);
        Igi2StationaryGunTask gun = helicopter.Children.OfType<Igi2StationaryGunTask>().Single();
        Assert.Equal(4021, gun.Id);

        // As the build does it: the carrier holds what is beneath it, then the gun is armed.
        World.Igi2Attachment.AttachChildren(helicopter, helicopter);
        gun.Prepare(session.Require<Igi2HumanWeapons>(), level.ModelFor);
        Assert.NotNull(gun.BarrelModel);

        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, gun.ActivatePosition(out _), 0f, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        Flow.Igi2LevelRun run = Bare();

        player.Human.ScriptActivate(gun);
        player.Human.LockActivate(1);
        player.Tick(RetailIgi2Mission1.Idle);
        gun.Run(run);
        Assert.Same(gun, player.Human.AttachParent);

        Vector3d heliWas = helicopter.CurrentPosition;
        float[] heliFrame = [.. helicopter.CurrentOrientation];
        Vector3d mount = Local(heliFrame, Offset(gun.Mount, heliWas));
        Vector3d place = Local(heliFrame, Offset(gun.CurrentPosition, heliWas));
        Vector3d gunner = Local(heliFrame, Offset(player.Human.Position, heliWas));
        Vector3d gunWas = gun.CurrentPosition;

        // Flown 100 m on and 20 m up, without turning: the gun, its mount and its gunner go as far.
        Vector3d flown = new(heliWas.X + 409600.0, heliWas.Y, heliWas.Z + 81920.0);
        helicopter.Ride(flown, heliFrame, tick: 1);
        AssertNear(place, Local(heliFrame, Offset(gun.CurrentPosition, flown)));
        AssertNear(mount, Local(heliFrame, Offset(gun.Mount, flown)));
        AssertNear(gunWas, gun.LastPosition);

        // The locked activate key, pressed, does not let the gunner off; carried, the gunner stands where it stood on the gun.
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate });
        gun.Run(run);
        Assert.Same(gun, player.Human.AttachParent);
        Assert.True(gun.IsActivated);
        Assert.True(player.Human.View.IsOverride);
        AssertNear(gunner, Local(heliFrame, Offset(player.Human.Position, flown)));

        // Turned a quarter round where it hovers: the gun and its mount sit where they sat against it.
        float[] turned = World.Igi2Pose.RotateGamma(heliFrame, MathF.PI / 2f);
        helicopter.Ride(flown, turned, tick: 2);
        AssertNear(place, Local(turned, Offset(gun.CurrentPosition, flown)));
        AssertNear(mount, Local(turned, Offset(gun.Mount, flown)));
        GC.KeepAlive(session);
    }

    private static Vector3d Offset(Vector3d of, Vector3d from) => new(of.X - from.X, of.Y - from.Y, of.Z - from.Z);

    // An offset in the world, in a frame's own axes, as ModelObj_CalcRelativePosAndOrientation takes it.
    private static Vector3d Local(float[] f, Vector3d d) => new(
        (f[0] * d.X) + (f[3] * d.Y) + (f[6] * d.Z),
        (f[1] * d.X) + (f[4] * d.Y) + (f[7] * d.Z),
        (f[2] * d.X) + (f[5] * d.Y) + (f[8] * d.Z));

    private static void AssertNear(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, 2.0);
        Assert.Equal(expected.Y, actual.Y, 2.0);
        Assert.Equal(expected.Z, actual.Z, 2.0);
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
