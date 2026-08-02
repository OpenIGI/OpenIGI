namespace OpenIGI.Input;

/// <summary>A button (or a digital direction) on a game controller.</summary>
/// <remarks>
/// The names follow the common cross-vendor layout: <see cref="South"/>/<see cref="East"/>/<see cref="West"/>/
/// <see cref="North"/> are the four face buttons by position — A/B/X/Y on an Xbox pad, ✕/○/□/△ on a
/// PlayStation one — named by where they sit rather than by a vendor's letter, so a binding does not
/// change meaning with the hardware. Values are contiguous from zero so they index a bit in
/// <see cref="GamepadState"/>.
/// </remarks>
public enum GamepadButton
{
    /// <summary>The bottom face button — A / ✕. The conventional "accept".</summary>
    South = 0,

    /// <summary>The right face button — B / ○. The conventional "back".</summary>
    East = 1,

    /// <summary>The left face button — X / □.</summary>
    West = 2,

    /// <summary>The top face button — Y / △.</summary>
    North = 3,

    /// <summary>The left shoulder bumper.</summary>
    LeftShoulder = 4,

    /// <summary>The right shoulder bumper.</summary>
    RightShoulder = 5,

    /// <summary>The left stick pressed in.</summary>
    LeftStick = 6,

    /// <summary>The right stick pressed in.</summary>
    RightStick = 7,

    /// <summary>The start / menu button.</summary>
    Start = 8,

    /// <summary>The back / view / select button.</summary>
    Back = 9,

    /// <summary>The directional pad up.</summary>
    DpadUp = 10,

    /// <summary>The directional pad down.</summary>
    DpadDown = 11,

    /// <summary>The directional pad left.</summary>
    DpadLeft = 12,

    /// <summary>The directional pad right.</summary>
    DpadRight = 13,
}
