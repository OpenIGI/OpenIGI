using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A multiplayer player carries one weapon in each place and the counted things up to their limits.</summary>
public sealed class CarrySlotsTests
{
    private static readonly WeaponType Knife = new() { WeaponId = 1, ScriptId = "WEAPON_ID_KNIFE", Category = WeaponCategory.Knife };
    private static readonly WeaponType Glock = new() { WeaponId = 2, ScriptId = "WEAPON_ID_GLOCK", Category = WeaponCategory.Pistol };
    private static readonly WeaponType Uzi = new() { WeaponId = 3, ScriptId = "WEAPON_ID_UZI", Category = WeaponCategory.Gun };
    private static readonly WeaponType Ak47 = new() { WeaponId = 4, ScriptId = "WEAPON_ID_AK47", Category = WeaponCategory.Gun };
    private static readonly WeaponType Spas = new() { WeaponId = 5, ScriptId = "WEAPON_ID_SPAS12", Category = WeaponCategory.Shotgun };
    private static readonly WeaponType Grenade = new() { WeaponId = 6, ScriptId = "WEAPON_ID_GRENADE", Category = WeaponCategory.Grenade };
    private static readonly AmmoType Grenades = new() { AmmoId = 60 };

    [Fact]
    public void Rifles_and_shotguns_are_the_main_gun_pistols_and_sub_machine_guns_the_second()
    {
        Assert.Equal(
            [CarrySlot.Knife, CarrySlot.Secondary, CarrySlot.Secondary, CarrySlot.Primary, CarrySlot.Primary, CarrySlot.Counted],
            new[] { Knife, Glock, Uzi, Ak47, Spas, Grenade }.Select(CarrySlots.Of));
    }

    [Fact]
    public void A_gun_whose_place_is_taken_by_another_is_left_and_one_already_carried_or_in_a_free_place_is_taken()
    {
        PlayerWeapons weapons = new([1, 2, 3, 4, 5]);
        weapons.Carry(Knife);
        weapons.Carry(Glock);
        weapons.Carry(Ak47);

        Assert.False(CarrySlots.Admits(weapons, Uzi, null));
        Assert.Equal(Glock.WeaponId, CarrySlots.InTheWay(weapons, Uzi)!.Value.Weapon.WeaponId);
        Assert.False(CarrySlots.Admits(weapons, Spas, null));
        Assert.True(CarrySlots.Admits(weapons, Ak47, null));
        Assert.True(CarrySlots.Admits(weapons, Glock, null));

        PlayerWeapons light = new([1, 2]);
        light.Carry(Knife);
        Assert.True(CarrySlots.Admits(light, Spas, null));
        Assert.True(CarrySlots.Admits(light, Uzi, null));
    }

    [Fact]
    public void A_counted_thing_is_taken_until_its_limit()
    {
        PlayerWeapons weapons = new([6]);
        weapons.Ammunition.SetLimit(Grenades.AmmoId, 3);
        weapons.Ammunition.Add(Grenades.AmmoId, 2);
        Assert.True(CarrySlots.Admits(weapons, Grenade, Grenades));
        weapons.Ammunition.Add(Grenades.AmmoId, 1);
        Assert.False(CarrySlots.Admits(weapons, Grenade, Grenades));
    }
}
