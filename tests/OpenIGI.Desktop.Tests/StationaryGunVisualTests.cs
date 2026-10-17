using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Igi1;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Desktop.Tests;

/// <summary>Visibility of the authored stationary-gun model by ownership kind.</summary>
public sealed class StationaryGunVisualTests
{
    [Fact]
    public void PlayerOwnershipKeepsTheStaticGunWhileAiOwnershipSuppressesItsDuplicate()
    {
        AiStationaryGun gun = new(taskId: 1, position: default, orientation: Matrix3f.Identity);
        object owner = new();
        Assert.True(gun.TryMount(owner));

        Assert.True(SoldierRenderer.ShouldDrawStaticStationaryGun(gun, playerUsing: true));
        Assert.False(SoldierRenderer.ShouldDrawStaticStationaryGun(gun, playerUsing: false));

        gun.Dismount(owner);

        Assert.True(SoldierRenderer.ShouldDrawStaticStationaryGun(gun, playerUsing: false));
    }

    [Fact]
    public void MountedGunnerDeathDropsTheCarriedWeaponNotTheRenderedStationaryGun()
    {
        WeaponType carried = new() { ScriptId = "WEAPON_ID_MP5SD" };
        WeaponType stationary = new() { ScriptId = "WEAPON_ID_M2HB" };

        Assert.Same(carried, SoldierSimulation.WeaponForDrop(carried, stationary));
    }
}
