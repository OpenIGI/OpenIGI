namespace OpenIGI.Game.Igi2.Player;

/// <summary>The slots of a human's 63-float channel block (<c>Human_s.avChannel</c>) the player fills.</summary>
/// <remarks>
/// <para>
/// The player and the AI drive a human through the same block: the player's input task fills it from the
/// keyboard and mouse (<c>HumanPlayerInput_ReadChannels</c>, <c>igi2 0x5296C0</c>), an AI fills it from its
/// action stack. Named here for the player's use; slots the single-player reader always writes zero are
/// left out.
/// </para>
/// </remarks>
public enum Igi2HumanChannel
{
    /// <summary>Horizontal look delta: mouse X × the clamped sensitivity, ±0.05 for the turn keys.</summary>
    LookX = 0,

    /// <summary>Vertical look delta: mouse Y × the clamped sensitivity, negated when the look is inverted.</summary>
    LookY = 1,

    /// <summary><see cref="Igi2PlayerAction.MoveLeft"/>.</summary>
    MoveLeft = 2,

    /// <summary><see cref="Igi2PlayerAction.MoveRight"/>.</summary>
    MoveRight = 3,

    /// <summary><see cref="Igi2PlayerAction.MoveForwards"/>.</summary>
    MoveForwards = 4,

    /// <summary><see cref="Igi2PlayerAction.MoveBackwards"/>.</summary>
    MoveBackwards = 5,

    /// <summary><see cref="Igi2PlayerAction.Fire"/>.</summary>
    Fire = 6,

    /// <summary><see cref="Igi2PlayerAction.AlternateFire"/>.</summary>
    AlternateFire = 7,

    /// <summary><see cref="Igi2PlayerAction.Reload"/>.</summary>
    Reload = 8,

    /// <summary><see cref="Igi2PlayerAction.Activate"/>.</summary>
    Activate = 9,

    /// <summary><see cref="Igi2PlayerAction.Crouch"/>.</summary>
    Crouch = 10,

    /// <summary><see cref="Igi2PlayerAction.Jump"/>.</summary>
    Jump = 11,

    /// <summary><see cref="Igi2PlayerAction.Peek"/>.</summary>
    Peek = 12,

    /// <summary>Always −1 from the player's reader.</summary>
    Unused13 = 13,

    /// <summary><see cref="Igi2PlayerAction.ZoomIn"/>.</summary>
    ZoomIn = 14,

    /// <summary><see cref="Igi2PlayerAction.ZoomOut"/>.</summary>
    ZoomOut = 15,

    /// <summary><see cref="Igi2PlayerAction.DropWeapon"/>.</summary>
    DropWeapon = 16,

    /// <summary>Low button bit <c>0x40000000</c>, which no single-player action binds.</summary>
    Button30 = 17,

    /// <summary>Low button bit <c>0x80000000</c>, which no single-player action binds.</summary>
    Button31 = 18,

    /// <summary><see cref="Igi2PlayerAction.ToggleWalkRun"/>.</summary>
    ToggleWalkRun = 23,

    /// <summary><see cref="Igi2PlayerAction.NextWeapon"/>.</summary>
    NextWeapon = 24,

    /// <summary><see cref="Igi2PlayerAction.PrevWeapon"/>.</summary>
    PrevWeapon = 25,

    /// <summary>Weapon category 1 pressed this frame; categories 2–7 follow, then 9 at 34.</summary>
    WeaponCategory1 = 26,

    /// <summary>Weapon category 9 pressed this frame.</summary>
    WeaponCategory9 = 34,

    /// <summary><see cref="Igi2PlayerAction.ComputerMap"/>.</summary>
    ComputerMap = 35,

    /// <summary><see cref="Igi2PlayerAction.Binoculars"/>.</summary>
    Binoculars = 37,

    /// <summary><see cref="Igi2PlayerAction.LowerStance"/>.</summary>
    LowerStance = 38,

    /// <summary><see cref="Igi2PlayerAction.RaiseStance"/>.</summary>
    RaiseStance = 39,

    /// <summary><see cref="Igi2PlayerAction.ModifyWalkRun"/>.</summary>
    ModifyWalkRun = 40,

    /// <summary><see cref="Igi2PlayerAction.Thermal"/>.</summary>
    Thermal = 43,

    /// <summary><see cref="Igi2PlayerAction.ComputerLog"/>.</summary>
    ComputerLog = 44,

    /// <summary><see cref="Igi2PlayerAction.ComputerObjectives"/>.</summary>
    ComputerObjectives = 45,

    /// <summary><see cref="Igi2PlayerAction.ComputerIgiHq"/>.</summary>
    ComputerIgiHq = 46,

    /// <summary><see cref="Igi2PlayerAction.Inventory"/> pressed this frame.</summary>
    Inventory = 47,

    /// <summary>High button bit <c>0x20000</c> (bit 49), which no single-player action binds.</summary>
    Button49 = 48,

    /// <summary><see cref="Igi2PlayerAction.ToggleStandLie"/>.</summary>
    ToggleStandLie = 49,
}
