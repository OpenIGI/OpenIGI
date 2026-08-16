using OpenIGI.Formats.Animation;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Desktop;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class PlayerCameraHealthTests
{
    [Fact]
    public void CameraUsesTheRuntimeHealthObjectForItsBody()
    {
        PlayerCamera camera = new();
        HumanHealth runtimeHealth = new(HumanHitZoneTable.Player, maximumHealth: 3f);

        camera.UseHealth(runtimeHealth);

        Assert.Same(runtimeHealth, camera.Body.Health);
    }

    [RequiresGameDataFact]
    public void FullHealthMedipackKeepsItsRetailEntryActionInsteadOfBeingReplacedByStand()
    {
        WeaponType medipack = LoadRoster().Weapons.First(
            weapon => weapon.Category == WeaponCategory.Medipack);
        PlayerCamera camera = new();
        camera.UseHealth(new HumanHealth(HumanHitZoneTable.Player));
        camera.UseWeaponCycle([medipack.WeaponId]);
        camera.Carry(medipack, new AmmoType { AmmoId = PlayerWeapons.MedipackAmmoId, CalibreId = 10005 });
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));

        InputState input = new();
        for (int tick = 0; tick < 40
            && (camera.Weapons?.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        Assert.Equal(medipack, camera.Weapons!.InHand!.Value.Weapon);
        input.BeginFrame();
        camera.Tick(input, terrain: null);
        input.BeginFrame();
        input.Mouse.SetButton(MouseButton.Left, down: true);
        Assert.True(PlayerCamera.SampleFrame(input, camera.InputProfile).IsSet(PlayerInputSlot.Fire));
        camera.Tick(input, terrain: null);

        Assert.Equal(HumanWeaponState.Medipack, camera.Weapons.State);
        BoneClip clip = Assert.IsType<BoneClip>(camera.View!.Bones.SourceOf(0));
        int retailEntry = WeaponAnimationTable.Select(medipack, WeaponAction.Fire).FullBody;
        Assert.Equal(retailEntry, clip.Animation.Id);
        Assert.False(clip.Loops);
    }

    [RequiresGameDataFact]
    public void HeldRetailMedipackKeepsItsInjectionClipLoopingAcrossMultipleDoses()
    {
        WeaponType medipack = LoadRoster().Weapons.First(
            weapon => weapon.Category == WeaponCategory.Medipack);
        PlayerCamera camera = new();
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.5f);
        camera.UseHealth(health);
        camera.UseWeaponCycle([medipack.WeaponId]);
        camera.Carry(medipack, new AmmoType { AmmoId = PlayerWeapons.MedipackAmmoId, CalibreId = 10005 });
        camera.Weapons!.Ammunition.Add(PlayerWeapons.MedipackAmmoId, 4);
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));

        InputState input = new();
        for (int tick = 0; tick < 60
            && (camera.Weapons.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        int doses = 0;
        int medipackStateFrames = 0;
        int retailLoopAnimation = WeaponAnimationTable.Select(medipack, WeaponAction.MedipackLoop).FullBody;
        for (int tick = 0; tick < 180 && doses < 2; tick++)
        {
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: true);
            camera.Tick(input, terrain: null);

            if (camera.Weapons.MedipackUsedThisTick)
            {
                doses++;
            }

            if (camera.Weapons.State == HumanWeaponState.Medipack)
            {
                medipackStateFrames++;
                if (retailLoopAnimation >= 0)
                {
                    BoneClip clip = Assert.IsType<BoneClip>(camera.View?.Bones.SourceOf(0));
                    Assert.True(clip.Loops);
                    Assert.Equal(retailLoopAnimation, clip.Animation.Id);
                }
            }
        }

        Assert.True(doses >= 2, "Holding Fire must apply more than one retail medipack dose.");
        Assert.True(medipackStateFrames > 1, "The medipack state must remain active between doses.");
        Assert.True(health.RemainingFraction > 0.5f);
    }

    [RequiresGameDataFact]
    public void MissingRetailMedipackLoopSlotPlaysNothingSoTheEntryClipHoldsItsFinalFrame()
    {
        WeaponType medipack = LoadRoster().Weapons.First(
            weapon => weapon.Category == WeaponCategory.Medipack);
        int retailLoopAnimation = WeaponAnimationTable.Select(
            medipack,
            WeaponAction.MedipackLoop).FullBody;
        if (retailLoopAnimation >= 0)
        {
            return;
        }

        PlayerCamera camera = new();
        camera.UseViewRig(LoadViewSet());

        // The shipped medipack's FireAnimation2 slot is -1. 0x40FB90 forwards that to 0x4D61D0, which
        // leaves the view untouched -- so the sustained loop plays no clip and the one-shot entry
        // ("insert syringe") holds its last frame. Substituting a looping FireAnimation1 replayed that
        // "insert" animation over and over while Fire was held, which is not what the injection does.
        Assert.False(camera.PlayWeaponAction(medipack, WeaponAction.MedipackLoop));
    }

    [RequiresGameDataFact]
    public void RebuildingTheViewRigRestoresOpticsAndInputStateForTheNextMission()
    {
        PlayerCamera camera = new();
        BoneAnimationSet viewSet = LoadViewSet();
        camera.UseViewRig(viewSet);

        camera.View!.TanX = 0.1f;
        camera.View.ViewModelTanX = 0.1f;

        camera.UseViewRig(viewSet);

        Assert.Equal(HumanView.DefaultTanX, camera.View!.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, camera.View.ViewModelTanX);
    }

    [RequiresGameDataFact]
    public void RetailMedipackWithoutAThirdClipReturnsToReadyWithoutReloading()
    {
        WeaponType medipack = LoadRoster().Weapons.First(
            weapon => weapon.Category == WeaponCategory.Medipack);
        PlayerCamera camera = new();
        HumanHealth health = new(HumanHitZoneTable.Player);
        health.ApplyDirect(0, 0.5f);
        camera.UseHealth(health);
        camera.UseWeaponCycle([medipack.WeaponId]);
        camera.Carry(medipack, new AmmoType { AmmoId = PlayerWeapons.MedipackAmmoId, CalibreId = 10005 });
        camera.Weapons!.Ammunition.Add(PlayerWeapons.MedipackAmmoId, 1);
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));

        InputState input = new();
        for (int tick = 0; tick < 60
            && (camera.Weapons.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        bool used = false;
        for (int tick = 0; tick < 120 && !used; tick++)
        {
            input.BeginFrame();
            input.Mouse.SetButton(MouseButton.Left, down: true);
            camera.Tick(input, terrain: null);
            used = camera.Weapons.MedipackUsedThisTick;
        }

        Assert.True(used);
        input.BeginFrame();
        camera.Tick(input, terrain: null);

        Assert.Equal(HumanWeaponState.Ready, camera.Weapons.State);
        Assert.NotEqual(WeaponAction.Reload, camera.Weapons.CurrentAction);
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
