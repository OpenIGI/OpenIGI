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
