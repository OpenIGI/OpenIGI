using OpenIGI.Game.Player;

namespace OpenIGI.Game.Weapons;

/// <summary>Which of a multiplayer player's places a weapon takes.</summary>
public enum CarrySlot
{
    /// <summary>The main gun: a rifle, a sniper rifle, a shotgun, a machine gun.</summary>
    Primary,

    /// <summary>The second gun: a pistol or a sub-machine gun.</summary>
    Secondary,

    /// <summary>The knife.</summary>
    Knife,

    /// <summary>A thing carried by the count, up to its limit: grenades, flashbangs, mines, the medipack.</summary>
    Counted,

    /// <summary>Anything else, carried freely: binoculars, the bomb.</summary>
    Free,
}

/// <summary>
/// What a multiplayer player may carry: one weapon in each place -- a main gun, a pistol or sub-machine gun, the knife -- and the
/// thrown and used things up to their counts, as I.G.I. 2's multiplayer has it, rather than every weapon walked over as the
/// campaigns let the player gather.
/// </summary>
/// <remarks>
/// I.G.I. 2 keeps one weapon of each category: its pickup (<c>HumanPlayer</c>'s pickup handler) takes a gun whose category is free,
/// takes only the rounds of one already carried, and leaves one whose category is taken for a swap; its shop drops the gun a
/// bought one replaces (<c>NetManager_DropWeapon</c>). I.G.I. files its sub-machine guns with its rifles; here they go with the
/// pistols.
/// </remarks>
public static class CarrySlots
{
    private static readonly HashSet<string> SubMachineGuns = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEAPON_ID_UZI", "WEAPON_ID_UZIX2", "WEAPON_ID_MP5SD", "WEAPON_ID_MP5", "WEAPON_ID_MAC10", "WEAPON_ID_SMG2", "WEAPON_ID_TYPE64SMG",
    };

    /// <summary>Gets the place a weapon takes.</summary>
    /// <param name="weapon">The weapon.</param>
    /// <returns>Its place.</returns>
    public static CarrySlot Of(WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.Category switch
        {
            WeaponCategory.Knife => CarrySlot.Knife,
            WeaponCategory.Pistol => CarrySlot.Secondary,
            WeaponCategory.Gun => SubMachineGuns.Contains(weapon.ScriptId) ? CarrySlot.Secondary : CarrySlot.Primary,
            WeaponCategory.Shotgun => CarrySlot.Primary,
            WeaponCategory.Grenade or WeaponCategory.ProximityMine or WeaponCategory.Medipack => CarrySlot.Counted,
            _ => CarrySlot.Free,
        };
    }

    /// <summary>Gets the weapon carried in the place another would take, when it is a different one.</summary>
    /// <param name="weapons">What the player carries.</param>
    /// <param name="weapon">The weapon it would take.</param>
    /// <returns>The weapon in its way, or <see langword="null"/> when the place is free, holds this one, or is not a single place.</returns>
    public static CarriedWeapon? InTheWay(PlayerWeapons weapons, WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        CarrySlot slot = Of(weapon);
        if (slot is CarrySlot.Counted or CarrySlot.Free)
        {
            return null;
        }

        foreach (CarriedWeapon carried in weapons.Carried)
        {
            if (carried.Weapon.WeaponId != weapon.WeaponId && Of(carried.Weapon) == slot)
            {
                return carried;
            }
        }

        return null;
    }

    /// <summary>Gets whether a weapon walked over may be taken: its place free or holding it already, or its count not full.</summary>
    /// <param name="weapons">What the player carries.</param>
    /// <param name="weapon">The weapon.</param>
    /// <param name="ammo">Its ammunition, whose carry limit a counted thing is held to.</param>
    /// <returns><see langword="true"/> when it may be taken.</returns>
    public static bool Admits(PlayerWeapons weapons, WeaponType weapon, AmmoType? ammo)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(weapon);
        if (Of(weapon) == CarrySlot.Counted)
        {
            int limit = ammo is null ? -1 : weapons.Ammunition.Limit(ammo.AmmoId);
            return limit < 0 || weapons.Ammunition.Count(ammo!.AmmoId) < limit;
        }

        return InTheWay(weapons, weapon) is null;
    }
}
