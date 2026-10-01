namespace OpenIGI.Game.Weapons;

/// <summary>
/// The <c>WEAPONTYPE_*</c> constants, as <c>0x413DF0</c> registers them.
/// </summary>
/// <remarks>
/// These are real values read from the executable's registration block, not an invented numbering:
/// the weapon module publishes all eight by name at start-up, alongside a <c>WEAPONTYPE_.*</c>
/// pattern that catches any it missed.
/// </remarks>
public enum WeaponCategory
{
    /// <summary>A rifle, sub-machine gun or emplacement gun — anything that fires from a magazine.</summary>
    Gun = 0,

    /// <summary>A side-arm.</summary>
    Pistol = 1,

    /// <summary>A shotgun, which fires more than one projectile per round.</summary>
    Shotgun = 2,

    /// <summary>The knife.</summary>
    Knife = 3,

    /// <summary>A thrown grenade.</summary>
    Grenade = 4,

    /// <summary>The binoculars.</summary>
    Binocular = 5,

    /// <summary>The medipack.</summary>
    Medipack = 6,

    /// <summary>A placed proximity mine.</summary>
    ProximityMine = 7,
}
