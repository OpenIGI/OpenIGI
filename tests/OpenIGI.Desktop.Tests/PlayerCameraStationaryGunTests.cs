using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Mission 4's M2HB (task 775), mounted by the player: the look turns the gun, the trigger fires the
/// gun's own rounds from its own muzzle, a target down the barrel dies, and a fresh Use lets go.
/// </summary>
public sealed class PlayerCameraStationaryGunTests
{
    [RequiresGameDataFact]
    public void TheMissionFourM2hbIsAimedFiredFromItsMuzzleAndLeft()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables, WeaponTypeRegistry weapons) =
            LoadMissionFour();
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        WorldInteraction interaction = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.StationaryGun && item.Task.Id == 775);
        AiStationaryGun gun = Assert.IsType<AiStationaryGun>(interaction.StationaryGun);

        // The stand's +264 as read from the running original: rows (-0.297, 0.954, 0), (-0.954, -0.297, 0).
        Assert.Equal(-0.297f, gun.Orientation.M11, 2);
        Assert.Equal(0.954f, gun.Orientation.M12, 2);
        Assert.Equal(-0.954f, gun.Orientation.M21, 2);
        Assert.Equal(-0.297f, gun.Orientation.M22, 2);
        Assert.True(weapons.TryGetByScriptId(gun.WeaponId, out WeaponType m2hb));
        gun.BindWeapon(m2hb);
        if (mission.TryGetModel(m2hb.GunModel, out MefModel? model)
            && model is not null
            && model.TryGetMagicVertex(2, out Vector3f muzzle))
        {
            gun.MuzzleVertex = muzzle;
        }

        PlayerCamera camera = new();
        camera.UseInteractions(interactions);
        PlaceAt(camera, interaction, gun, behind: true);
        InputState input = new();

        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);

        Assert.True(camera.IsMountedStationaryGun);
        Assert.Same(gun, camera.MountedStationaryGun);
        Assert.Equal("WEAPON_ID_M2HB", camera.HeldWeaponType!.ScriptId);
        Assert.True(gun.IsOccupied);

        // 0x40C970 stands the body 2867.2 units along the stand's barrel axis (local Y), never beside it;
        // retail's was measured at 2867 units behind task 775.
        Matrix3f stand = gun.Orientation;
        Vector3d offset = camera.Body.Position - gun.Position;
        double along = (offset.X * stand.M12) + (offset.Y * stand.M22);
        double across = (offset.X * stand.M11) + (offset.Y * stand.M21);
        Assert.Equal(2867.2, along, 1);
        Assert.Equal(0.0, across, 1);

        // Mode 4: the gun's own sight, not the player's eye.
        AssertClose(gun.SightPosition / PlayerCamera.UnitsPerMetre, camera.Camera.Position);

        // The look turns the gun, and 0x4748B0 holds it at the authored 25 degrees.
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: false);
        camera.Look(input, seconds: 1f / 60f);
        input.BeginFrame();
        input.Mouse.AddMotion(0f, -100000f);
        camera.Look(input, seconds: 1f / 60f);
        Assert.Equal(25f * (MathF.PI / 180f), MathF.Abs(gun.AimPitch), 4);

        // The body faces down the barrel, so the player steps off looking where the gun was.
        Matrix3f turned = gun.GunOrientation;
        Assert.Equal(MathF.Atan2(turned.M12, turned.M22), camera.Yaw, 4);

        // Level the barrel again, then put a soldier four metres down it.
        gun.Steer(-gun.AimPitch, 0f);
        camera.Tick(input, terrain: null);
        Matrix3f aim = gun.GunOrientation;
        Vector3f barrel = new(aim.M12, aim.M22, aim.M32);
        Vector3d muzzleUnits = gun.MuzzlePosition;
        Vector3d targetFeet = new(
            muzzleUnits.X + (barrel.X * 4.0 * PlayerCamera.UnitsPerMetre),
            muzzleUnits.Y + (barrel.Y * 4.0 * PlayerCamera.UnitsPerMetre),
            muzzleUnits.Z + (barrel.Z * 4.0 * PlayerCamera.UnitsPerMetre) - (HumanHitTest.BodyHeightInUnits * 0.5));
        HumanHealth target = new(HumanHitZoneTable.Soldier);
        Vector3d? firstOrigin = null;
        camera.Humans = (Vector3d origin, Vector3d end, float damageFactor, WeaponCategory _, double _, bool _, out HumanHit hit) =>
        {
            firstOrigin ??= origin;
            if (target.IsDead)
            {
                hit = default;
                return false;
            }

            bool met = HumanHitTest.TryCast(
                origin, end, targetFeet, new Vector3f(-barrel.X, -barrel.Y, 0f), pose: null, out hit);
            if (met)
            {
                target.Apply(hit.Zone, damageFactor);
            }

            return met;
        };

        bool fired = false;
        for (int tick = 0; tick < 300 && !target.IsDead; tick++)
        {
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: true);
            camera.Tick(input, terrain: null);
            fired |= camera.FiredThisTick;
        }

        // 0x474360 passes the gun no origin, so 0x478BA0 fires from the gun's own muzzle.
        Assert.True(fired);
        Assert.True(firstOrigin.HasValue);
        AssertClose(muzzleUnits, firstOrigin!.Value);
        Assert.NotEqual(camera.EyeUnits, firstOrigin.Value);
        Assert.True(target.IsDead);
        Assert.Equal(-1, gun.RemainingAmmunition);

        input.BeginFrame();
        input.Mouse.SetButton(MouseButton.Left, down: false);
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);

        Assert.False(camera.IsInWorldInteraction);
        Assert.False(camera.IsMountedStationaryGun);
        Assert.False(camera.FiredThisTick);
        Assert.False(gun.IsOccupied);
    }

    /// <summary>
    /// StationaryGun::GetActivate (0x474E30) offers the gun only from behind it; from the barrel side
    /// retail's Use does nothing (measured on the running original, mission 4 task 775).
    /// </summary>
    [RequiresGameDataFact]
    public void TheMissionFourM2hbIsNotOfferedFromInFrontOfItsBarrel()
    {
        (LevelTask root, LoadedMission mission, ScriptVarRegistry variables, WeaponTypeRegistry weapons) =
            LoadMissionFour();
        WorldInteractionRegistry interactions = WorldInteractionRegistry.Build(
            root, mission, DoorRegistry.Build(root), variables);
        WorldInteraction interaction = Assert.Single(
            interactions.Items,
            static item => item.Type == InteractionActivateType.StationaryGun && item.Task.Id == 775);
        AiStationaryGun gun = Assert.IsType<AiStationaryGun>(interaction.StationaryGun);
        Assert.True(weapons.TryGetByScriptId(gun.WeaponId, out WeaponType m2hb));
        gun.BindWeapon(m2hb);

        PlayerCamera camera = new();
        camera.UseInteractions(interactions);
        PlaceAt(camera, interaction, gun, behind: false);
        InputState input = new();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.E, down: true);
        camera.Tick(input, terrain: null);

        Assert.False(camera.IsInWorldInteraction);
        Assert.False(gun.IsOccupied);
    }

    // Stands the player a metre from the gun on its +Y side (behind, away from the barrel) or its -Y
    // side (in front of the barrel), looking at it.
    private static void PlaceAt(PlayerCamera camera, WorldInteraction interaction, AiStationaryGun gun, bool behind)
    {
        Vector3d back = new(gun.Orientation.M12, gun.Orientation.M22, 0.0);
        Vector3d eye = interaction.Anchor + (back * (behind ? 4096.0 : -4096.0));
        Vector3d toward = interaction.Anchor - eye;
        camera.Body.Position = new Vector3d(eye.X, eye.Y, eye.Z - camera.Body.EyeHeight);
        camera.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
        camera.Body.Pitch = (float)Math.Atan2(
            toward.Z,
            Math.Sqrt((toward.X * toward.X) + (toward.Y * toward.Y)));
    }

    private static void AssertClose(Vector3d expected, Vector3d actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    private static (LevelTask Root, LoadedMission Mission, ScriptVarRegistry Variables, WeaponTypeRegistry Weapons)
        LoadMissionFour()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);

        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            missions,
            fileSystem,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(4);

        WeaponTypeRegistry weapons = new();
        weapons.Register(loop.Natives);
        Assert.True(weapons.Load(fileSystem, resources, loop.Natives) > 0);
        return (root, mission, loop.Variables, weapons);
    }
}
