namespace OpenIGI.Game.Weapons;

/// <summary>
/// The <c>AMMODISPLAYTYPE_*</c> constants, as <c>0x413DF0</c> registers them.
/// </summary>
/// <remarks>How the HUD counts what the weapon consumes.</remarks>
public enum AmmoDisplayType
{
    /// <summary>Nothing is counted.</summary>
    None = 0,

    /// <summary>A plain round count.</summary>
    Normal = 1,

    /// <summary>Rounds in the magazine, and magazines held.</summary>
    Clip = 2,

    /// <summary>A barrel gauge.</summary>
    Barrel = 3,

    /// <summary>Shotgun shells.</summary>
    Shell = 4,

    /// <summary>Grenades.</summary>
    Grenade = 5,
}
