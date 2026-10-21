using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Player;
using static OpenIGI.Game.Tests.PlayerWeaponRecords;
using static OpenIGI.Game.Tests.PlayerWeaponsDriver;

namespace OpenIGI.Game.Tests;

/// <summary>
/// What the player carries and how it comes to hand: gun pickups and the next-weapon cycle.
/// </summary>
public sealed class PlayerWeaponCycleTests
{
    [Fact]
    public void TheFirstEntryOfTheCycleThePlayerCarriesEndsUpInHand()
    {
        // Human+3400 starts at -1, so the first tick advances to cycle index 0. The shipped cycle
        // starts with the knife, which is why level 1 opens with a blade.
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Knife);

        Assert.Null(weapons.InHand);

        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void GunPickupAddsANewWeaponWithoutChangingWhatIsHeld()
    {
        PlayerWeapons weapons = new([20, 1, 7]);
        weapons.Carry(Knife);
        Equip(weapons);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        Assert.True(result.AddedWeapon);
        Assert.Equal(0, result.AddedReserveRounds);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
        CarriedWeapon pickedUp = Assert.Single(weapons.Carried, carried => carried.Weapon.WeaponId == Glock.WeaponId);
        Assert.Equal(Glock.RoundsPerClip, pickedUp.Gun.RoundsInClip);
    }

    [Fact]
    public void GunPickupTurnsADuplicateWeaponIntoOneFullMagazineOfReserveAmmo()
    {
        PlayerWeapons weapons = new([1]);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Ammunition.Add(NineMillimetre.AmmoId, 2);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        Assert.False(result.AddedWeapon);
        Assert.Equal(Glock.RoundsPerClip, result.AddedReserveRounds);
        Assert.Single(weapons.Carried);
        Assert.Equal(2 + Glock.RoundsPerClip, weapons.Ammunition.Count(NineMillimetre.AmmoId));
    }

    [Fact]
    public void RuntimeAndAuthoredGunPickupsUseTheSameFullMagazinePayload()
    {
        PlayerWeapons weapons = new([1]);

        WeaponPickupResult result = weapons.PickUp(Glock, NineMillimetre, Glock.RoundsPerClip);

        CarriedWeapon gun = Assert.Single(weapons.Carried);
        Assert.True(result.AddedWeapon);
        Assert.Equal(Glock.RoundsPerClip, gun.Gun.RoundsInClip);
    }

    [Fact]
    public void TheCycleSkipsWhatIsNotCarried()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Mp5Sd, NineMillimetre);

        Equip(weapons);

        // Entries 0 and 1 -- knife and Glock -- are not carried, so it steps on to the MP5SD.
        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void NextWeaponStepsThroughTheCycle()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Mp5Sd, NineMillimetre);

        Equip(weapons);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);

        PlayerInputState input = new();
        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);

        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);

        // And it wraps.
        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_KNIFE", weapons.InHand?.Weapon.ScriptId);
    }

    [Fact]
    public void NextWeaponContinuesFromARecentlySelectedCategory()
    {
        PlayerWeapons weapons = new(Cycle);
        weapons.UseCategories(
        [
            new HumanPlayerWeaponCategory([20]),
            new HumanPlayerWeaponCategory([1]),
            new HumanPlayerWeaponCategory([7]),
        ]);
        weapons.Carry(Knife);
        weapons.Carry(Glock, NineMillimetre);
        weapons.Carry(Mp5Sd, NineMillimetre);

        Equip(weapons);
        PlayerInputState input = new();
        Press(weapons, input, PlayerInputSlot.WeaponCategory2);
        Assert.Equal("WEAPON_ID_GLOCK", weapons.InHand?.Weapon.ScriptId);

        Press(weapons, input, PlayerInputSlot.NextWeapon);
        Assert.Equal("WEAPON_ID_MP5SD", weapons.InHand?.Weapon.ScriptId);
    }
}
