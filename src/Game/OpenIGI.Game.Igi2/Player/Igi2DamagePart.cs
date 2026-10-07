namespace OpenIGI.Game.Igi2.Player;

/// <summary>A human's seventeen damage zones, <c>DamagePart_e</c>, in the original's order.</summary>
/// <remarks>
/// The order is the one <c>HumanPlayerConfigItem</c> declares its multipliers in and the index into
/// <c>HumanDamage_t.atDamagePart</c>; a fall lands on <see cref="Chest"/>, which
/// <c>HumanPlayer_InAirControl</c> (<c>igi2 0x49FCC0</c>) writes as 13.
/// </remarks>
public enum Igi2DamagePart
{
    /// <summary>Right foot.</summary>
    RightFoot = 0,

    /// <summary>Right lower leg.</summary>
    RightLowerLeg = 1,

    /// <summary>Right upper leg.</summary>
    RightUpperLeg = 2,

    /// <summary>Right hand.</summary>
    RightHand = 3,

    /// <summary>Right lower arm.</summary>
    RightLowerArm = 4,

    /// <summary>Right upper arm.</summary>
    RightUpperArm = 5,

    /// <summary>Left foot.</summary>
    LeftFoot = 6,

    /// <summary>Left lower leg.</summary>
    LeftLowerLeg = 7,

    /// <summary>Left upper leg.</summary>
    LeftUpperLeg = 8,

    /// <summary>Left hand.</summary>
    LeftHand = 9,

    /// <summary>Left lower arm.</summary>
    LeftLowerArm = 10,

    /// <summary>Left upper arm.</summary>
    LeftUpperArm = 11,

    /// <summary>The upper body.</summary>
    UpperBody = 12,

    /// <summary>Chest.</summary>
    Chest = 13,

    /// <summary>Back.</summary>
    Back = 14,

    /// <summary>Groin.</summary>
    Groin = 15,

    /// <summary>Head.</summary>
    Head = 16,
}
