using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerCameraLauncherTests
{
    [RequiresGameDataFact]
    public void SaturatedM203LaunchRefundsOneRoundAfterTheCameraEnumeratesTheShot()
    {
        WeaponType m16 = LoadRoster().Weapons.Single(weapon => weapon.ScriptId == "WEAPON_ID_M16A2");
        AmmoType m203Ammo = new() { AmmoId = 10, ScriptId = "AMMO_ID_M203", CalibreId = 10002 };
        PlayerCamera camera = CreateCamera(m16, m16.WeaponId, new AmmoType { AmmoId = 0, CalibreId = m16.CalibreId });
        camera.Weapons!.SetM203Ammunition(m203Ammo);
        camera.Weapons.Ammunition.Add(m203Ammo.AmmoId, 1);
        WaitUntilReady(camera);

        for (int i = 0; i < M203Simulation.MaximumLive; i++)
        {
            Assert.True(camera.M203.Spawn(new M203Projectile(
                Vector3d.Zero,
                new Vector3f(0f, 1f, 0f),
                270f,
                5f,
                1f)));
        }

        InputState input = new();
        input.BeginFrame();
        input.Mouse.SetButton(MouseButton.Right, down: true);

        camera.Tick(input, terrain: null);

        Assert.Equal(1, camera.Weapons.Ammunition.Count(m203Ammo.AmmoId));
        Assert.Empty(camera.Weapons.M203Shots);
        Assert.Equal(M203Simulation.MaximumLive, camera.M203.Projectiles.Count);
    }

    [RequiresGameDataFact]
    public void SaturatedMissileLaunchRefundsOneRoundAfterTheCameraEnumeratesTheShot()
    {
        WeaponType launcher = LoadRoster().Weapons.First(
            weapon => weapon.Projectile.TypeName == "TASKTYPE_MISSILE");
        AmmoType ammo = new() { AmmoId = 0, CalibreId = launcher.CalibreId };
        PlayerCamera camera = CreateCamera(launcher, launcher.WeaponId, ammo);
        WaitUntilReady(camera);
        int roundsBefore = camera.Weapons!.InHand!.Value.Gun.RoundsInClip;

        for (int i = 0; i < MissileSimulation.MaximumLive; i++)
        {
            Assert.True(camera.Missiles.Spawn(
                Vector3d.Zero,
                new Vector3d(4096, 0, 0),
                MissileFlight.TypeForLauncher(launcher),
                MissileFlight.LaunchSpeedMetresPerSecond(launcher),
                new Vector3f(1f, 0f, 0f)));
        }

        InputState input = new();
        input.BeginFrame();
        input.Mouse.SetButton(MouseButton.Left, down: true);

        camera.Tick(input, terrain: null);

        Assert.Equal(roundsBefore, camera.Weapons.InHand!.Value.Gun.RoundsInClip);
        Assert.Empty(camera.Weapons.Shots);
        Assert.Equal(MissileSimulation.MaximumLive, camera.Missiles.Missiles.Count);
    }

    private static PlayerCamera CreateCamera(WeaponType weapon, int weaponId, AmmoType ammo)
    {
        PlayerCamera camera = new();
        camera.UseHealth(new HumanHealth(HumanHitZoneTable.Player));
        camera.UseWeaponCycle([weaponId]);
        camera.Carry(weapon, ammo);
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        return camera;
    }

    private static void WaitUntilReady(PlayerCamera camera)
    {
        InputState input = new();
        for (int tick = 0; tick < 60
            && (camera.Weapons?.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        Assert.Equal(HumanWeaponState.Ready, camera.Weapons!.State);
    }

    private static BoneAnimationSet LoadViewSet() => BoneAnimationSet.Read(File.ReadAllBytes(
        Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));

    private static WeaponTypeRegistry LoadRoster()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        return roster;
    }
}
