namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>The kind of primary weapon a tactical loadout carries one of (<see cref="Igi2Loadout.Tactical"/>).</summary>
public enum Igi2PrimaryRole
{
    /// <summary>Not a kind of its own: a category other than the primary, or a loadout that carries one primary.</summary>
    None,

    /// <summary>A rifle, sniper rifle, shotgun or machine gun.</summary>
    LongGun,

    /// <summary>A submachine gun, <c>SIGHTDISPLAYTYPE_SMG</c>.</summary>
    Smg,

    /// <summary>A launcher, <c>SIGHTDISPLAYTYPE_RPG</c>.</summary>
    Launcher,
}

/// <summary>Where a carried weapon goes: the player carries one weapon of each slot, and swaps another of it for that one.</summary>
/// <param name="Category">The weapon's <c>WEAPONCATEGORY_*</c> value.</param>
/// <param name="Role">Which of the primary weapons it is, under a tactical loadout.</param>
/// <remarks>
/// The original's slot is the category alone (<c>HumanPlayer_Pickup</c>, <c>igi2 0x49F8C0</c>). A tactical loadout splits the
/// primary category by the sight each weapon is defined with, so the weapon scripts, a mod's among them, say which kind a weapon is.
/// </remarks>
public readonly record struct Igi2CarrySlot(int Category, Igi2PrimaryRole Role)
{
    /// <summary>Gets the slot a weapon goes in.</summary>
    /// <param name="weaponType">The weapon.</param>
    /// <param name="loadout">What the player may carry.</param>
    /// <returns>The slot.</returns>
    public static Igi2CarrySlot Of(Igi2WeaponTypeTask weaponType, Igi2Loadout loadout)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        int category = weaponType.WeaponCategory;
        if (loadout != Igi2Loadout.Tactical || category != Igi2WeaponCategory.Primary)
        {
            return new Igi2CarrySlot(category, Igi2PrimaryRole.None);
        }

        return new Igi2CarrySlot(category, weaponType.SightDisplayType switch
        {
            Igi2SightDisplayType.Smg => Igi2PrimaryRole.Smg,
            Igi2SightDisplayType.Rpg => Igi2PrimaryRole.Launcher,
            _ => Igi2PrimaryRole.LongGun,
        });
    }
}

/// <summary>The <c>SIGHTDISPLAYTYPE_*</c> values the carry slots test (<see cref="Igi2WeaponConstants"/>).</summary>
internal static class Igi2SightDisplayType
{
    /// <summary><c>SIGHTDISPLAYTYPE_SMG</c>.</summary>
    public const int Smg = 3;

    /// <summary><c>SIGHTDISPLAYTYPE_RPG</c>.</summary>
    public const int Rpg = 6;
}
