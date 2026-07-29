namespace OpenIGI.Game.Weapons;

/// <summary>
/// The <c>MISSILE_TYPE_*</c> constants, as <c>0x427FF0</c> registers them.
/// </summary>
/// <remarks>
/// Real values read from the registration block, and they are <b>not</b> in the order the strings sit
/// in the binary — the helicopter takes 1, between the two missile kinds.
/// </remarks>
public enum MissileType
{
    /// <summary>Flies straight at what it was aimed at.</summary>
    MissileDirect = 0,

    /// <summary>The Hind's own weapon.</summary>
    HindHelicopter = 1,

    /// <summary>Flies an arc.</summary>
    MissileBallistic = 2,

    /// <summary>Seeks armour.</summary>
    MissileAntiTank = 3,

    /// <summary>Steered from the keyboard.</summary>
    MissileKeyboard = 4,

    /// <summary>A dropped bomb.</summary>
    Bomb = 5,
}

/// <summary>The <c>MISSILE_SMOKE_*</c> constants, as <c>0x427FF0</c> registers them.</summary>
public enum MissileSmoke
{
    /// <summary>No trail.</summary>
    None = 0,

    /// <summary>A smoke trail.</summary>
    Normal = 1,
}
