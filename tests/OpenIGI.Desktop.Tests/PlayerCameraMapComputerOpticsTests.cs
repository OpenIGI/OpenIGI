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

/// <summary>
/// Issue #115: opening the map computer closes the optics, as retail's Ready state does through
/// <c>0x4659E0</c> before it enters weapon state 11.
/// </summary>
public sealed class PlayerCameraMapComputerOpticsTests
{
    [RequiresGameDataFact]
    public void OpeningTheMapClosesAZoomedDragunovScope()
    {
        WeaponTypeRegistry roster = LoadRoster();
        WeaponType dragunov = roster.Weapons.Single(weapon => weapon.ScriptId == "WEAPON_ID_DRAGUNOV");
        PlayerCamera camera = Arm(dragunov);
        InputState input = new();
        TickUntilReady(camera, input);

        Press(camera, input, KeyCode.Backspace);
        for (int tick = 0; tick < 30; tick++)
        {
            Press(camera, input, KeyCode.PageUp);
        }

        Assert.True(camera.IsSniperScoped);
        Assert.True(camera.View!.TanX < HumanView.DefaultTanX);

        OpenAndCloseMap(camera, input);

        Assert.False(camera.IsSniperScoped);
        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, camera.View.ViewModelTanX);
    }

    [RequiresGameDataFact]
    public void OpeningTheMapClosesZoomedBinoculars()
    {
        WeaponTypeRegistry roster = LoadRoster();
        WeaponType binoculars = roster.Weapons.Single(weapon => weapon.Category == WeaponCategory.Binocular);
        // The binocular key raises them from another weapon; pressed while they are already in hand it
        // puts them away again, so the player starts with the knife.
        WeaponType knife = roster.Weapons.Single(weapon => weapon.ScriptId == "WEAPON_ID_KNIFE");
        PlayerCamera camera = Arm(knife, binoculars);
        InputState input = new();
        TickUntilReady(camera, input);

        Press(camera, input, KeyCode.B);
        for (int tick = 0; tick < 60 && !camera.IsBinocularsOpen; tick++)
        {
            Press(camera, input, key: null);
        }

        for (int tick = 0; tick < 30; tick++)
        {
            Press(camera, input, KeyCode.PageUp);
        }

        Assert.True(camera.IsBinocularsOpen);
        Assert.True(camera.View!.TanX < HumanView.DefaultTanX);

        OpenAndCloseMap(camera, input);

        Assert.False(camera.IsBinocularsOpen);
        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, camera.View.ViewModelTanX);
    }

    private static PlayerCamera Arm(params WeaponType[] weapons)
    {
        PlayerCamera camera = new();
        camera.UseWeaponCycle([.. weapons.Select(weapon => weapon.WeaponId)]);
        foreach (WeaponType weapon in weapons)
        {
            camera.Carry(weapon, new AmmoType { AmmoId = 1, CalibreId = weapon.CalibreId });
        }

        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        return camera;
    }

    private static void OpenAndCloseMap(PlayerCamera camera, InputState input)
    {
        camera.SetMapComputerOpen(true);
        for (int tick = 0; tick < 5; tick++)
        {
            camera.TickMapComputer();
        }

        camera.SetMapComputerOpen(false);
        Press(camera, input, key: null);
    }

    private static void Press(PlayerCamera camera, InputState input, KeyCode? key)
    {
        input.BeginFrame();
        foreach (KeyCode held in new[] { KeyCode.Backspace, KeyCode.PageUp, KeyCode.B })
        {
            input.Keyboard.SetKey(held, down: held == key);
        }

        camera.Tick(input, terrain: null);
    }

    private static void TickUntilReady(PlayerCamera camera, InputState input)
    {
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
