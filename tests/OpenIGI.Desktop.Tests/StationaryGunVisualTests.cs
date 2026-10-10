using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Visibility of the authored stationary-gun model by ownership kind.</summary>
public sealed class StationaryGunVisualTests
{
    [Fact]
    public void MountedGunnerDeathDropsTheCarriedWeaponNotTheRenderedStationaryGun()
    {
        WeaponType carried = new() { ScriptId = "WEAPON_ID_MP5SD" };
        WeaponType stationary = new() { ScriptId = "WEAPON_ID_M2HB" };

        Assert.Same(carried, SoldierRenderer.WeaponForDrop(carried, stationary));
    }
}
