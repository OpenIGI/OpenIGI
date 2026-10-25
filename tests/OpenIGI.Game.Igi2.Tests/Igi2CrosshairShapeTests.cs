using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>I.G.I. 2's weapons aimed with the remastered crosshair of their kind, by the sight their types declare.</summary>
public sealed class Igi2CrosshairShapeTests
{
    [Theory]
    [InlineData(0, 1, CrosshairShape.Pistol)]
    [InlineData(0, 2, CrosshairShape.Shotgun)]
    [InlineData(0, 3, CrosshairShape.Smg)]
    [InlineData(0, 4, CrosshairShape.Rifle)]
    [InlineData(0, 5, CrosshairShape.MachineGun)]
    [InlineData(0, 6, CrosshairShape.Launcher)]
    [InlineData(4, 7, CrosshairShape.Grenade)]
    [InlineData(0, 8, CrosshairShape.Sniper)]
    [InlineData(5, 0, CrosshairShape.None)]
    [InlineData(3, 0, CrosshairShape.Melee)]
    public void Each_sight_is_its_kinds_crosshair(int weaponType, int sight, CrosshairShape shape) =>
        Assert.Equal(shape, Igi2TitlePlayer.ShapeOf(new Igi2WeaponTypeTask { WeaponType = weaponType, SightDisplayType = sight }));
}
