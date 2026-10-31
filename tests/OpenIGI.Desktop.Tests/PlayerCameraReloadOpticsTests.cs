using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A reload comes out of the sight: the ready hub hands over to the reload through <c>0x4659E0</c>, which clears the
/// Dragunov's scope and puts both tangents home, and while the weapon is not ready the MP5's sight (<c>0x473AF0</c>)
/// stays home and the Dragunov's scope (<c>0x472E50</c>) cannot be raised.
/// </summary>
public sealed class PlayerCameraReloadOpticsTests
{
    private const int AmmoId = 1;

    [RequiresGameDataFact]
    public void A_reload_closes_a_zoomed_dragunov_scope_and_it_stays_shut_until_the_reload_ends()
    {
        PlayerCamera camera = ArmedWith("WEAPON_ID_DRAGUNOV");
        InputState input = new();
        TickUntilReady(camera, input);

        Press(camera, input, KeyCode.Backspace);
        for (int tick = 0; tick < 30; tick++)
        {
            Press(camera, input, KeyCode.PageUp);
        }

        Assert.True(camera.IsSniperScoped);
        Assert.True(camera.View!.TanX < HumanView.DefaultTanX);

        StartReload(camera, input);

        Assert.False(camera.IsSniperScoped);
        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, camera.View.ViewModelTanX);

        // Alternate fire mid-reload does not raise it.
        Press(camera, input, KeyCode.Backspace);
        Assert.Equal(HumanWeaponState.Reloading, camera.Weapons!.State);
        Assert.False(camera.IsSniperScoped);
        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
    }

    [RequiresGameDataFact]
    public void A_reload_closes_a_zoomed_mp5_sight_and_it_stays_shut_until_the_reload_ends()
    {
        PlayerCamera camera = ArmedWith("WEAPON_ID_MP5SD");
        InputState input = new();
        TickUntilReady(camera, input);

        Press(camera, input, KeyCode.Backspace);
        for (int tick = 0; tick < 30; tick++)
        {
            Press(camera, input, key: null);
        }

        Assert.True(camera.View!.TanX < HumanView.DefaultTanX);

        StartReload(camera, input);

        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
        Assert.Equal(HumanView.DefaultViewModelTanX, camera.View.ViewModelTanX);

        Press(camera, input, KeyCode.Backspace);
        for (int tick = 0; tick < 5; tick++)
        {
            Press(camera, input, key: null);
        }

        Assert.Equal(HumanWeaponState.Reloading, camera.Weapons!.State);
        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);

        // Once the reload is over, the sight is closed: it takes a new press to zoom again.
        for (int tick = 0; tick < 300 && camera.Weapons.State != HumanWeaponState.Ready; tick++)
        {
            Press(camera, input, key: null);
        }

        Assert.Equal(HumanWeaponState.Ready, camera.Weapons.State);
        for (int tick = 0; tick < 5; tick++)
        {
            Press(camera, input, key: null);
        }

        Assert.Equal(HumanView.DefaultTanX, camera.View.TanX);
    }

    private static void StartReload(PlayerCamera camera, InputState input)
    {
        HeldGun gun = camera.Weapons!.InHand!.Value.Gun;
        gun.State.RoundsInClip = 1;
        camera.Weapons.Ammunition.Add(AmmoId, 50);

        // The press raises the reload bit; the ready hub hands over to the reload on the tick it reads it.
        Press(camera, input, KeyCode.Return);
        for (int tick = 0; tick < 3 && camera.Weapons.State != HumanWeaponState.Reloading; tick++)
        {
            Press(camera, input, key: null);
        }

        Assert.Equal(HumanWeaponState.Reloading, camera.Weapons.State);
    }

    private static PlayerCamera ArmedWith(string scriptId)
    {
        WeaponType weapon = LoadRoster().Weapons.Single(candidate => candidate.ScriptId == scriptId);
        PlayerCamera camera = new();
        camera.UseWeaponCycle([weapon.WeaponId]);
        camera.Carry(weapon, new AmmoType { AmmoId = AmmoId, CalibreId = weapon.CalibreId });
        camera.UseViewRig(LoadViewSet());
        camera.UseWalls(ObjectCollisionIndex.Build([]));
        return camera;
    }

    private static void Press(PlayerCamera camera, InputState input, KeyCode? key)
    {
        input.BeginFrame();
        foreach (KeyCode held in new[] { KeyCode.Backspace, KeyCode.PageUp, KeyCode.Return })
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
