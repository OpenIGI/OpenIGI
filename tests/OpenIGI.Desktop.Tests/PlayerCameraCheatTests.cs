using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Igi1;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>The all-weapons cheat granting into a player that is already armed.</summary>
public sealed class PlayerCameraCheatTests
{
    [Fact]
    public void AllWeaponsAddsOnlyWhatIsNotAlreadyCarried()
    {
        WeaponType starting = new() { WeaponId = 10, CalibreId = -1 };
        WeaponType granted = new() { WeaponId = 20, CalibreId = -1 };
        PlayerCamera camera = new();
        camera.UseWeaponCycle([starting.WeaponId]);
        camera.Carry(starting);

        int added = camera.EnsureAllWeapons([starting, granted], new AmmoTypeRegistry());

        Assert.Equal(1, added);
        Assert.Equal(2, camera.Weapons!.Carried.Count);
        Assert.True(camera.Weapons.HasWeapon(granted.WeaponId));

        // Every tick asks again; a second pass must be a no-op rather than a second copy.
        Assert.Equal(0, camera.EnsureAllWeapons([starting, granted], new AmmoTypeRegistry()));
        Assert.Equal(2, camera.Weapons.Carried.Count);
    }

    [Fact]
    public void AllWeaponsNeedsAWeaponMachineToGrantInto()
    {
        WeaponType granted = new() { WeaponId = 20, CalibreId = -1 };

        Assert.Equal(0, new PlayerCamera().EnsureAllWeapons([granted], new AmmoTypeRegistry()));
    }

    [Fact]
    public void ACameraAppliesCheatsToItsBodyAndWeapons()
    {
        PlayerCamera camera = new();
        camera.UseWeaponCycle([]);
        PlayerCheatOptions cheats = PlayerCheatOptions.Retail with { InfiniteHealth = true, NoRecoil = true };

        camera.ApplyCheats(cheats);

        Assert.True(camera.Body.Health.Invulnerable);
        Assert.Equal(cheats, camera.Weapons!.Cheats);
    }
}
