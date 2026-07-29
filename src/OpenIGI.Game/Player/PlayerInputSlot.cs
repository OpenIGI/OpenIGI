namespace OpenIGI.Game.Player;

/// <summary>
/// The slots of a <see cref="PlayerInputFrame"/>, in the order <c>0x45E060</c> fills them.
/// </summary>
/// <remarks>
/// <para>
/// Most slots are one bit of the engine's action bitmask (<c>dword_BC210C</c>). The bit each slot
/// carries is recorded below, because the sampler writes the slots out of bit order and the mapping is
/// the only thing tying a slot back to a binding. <b>What the bits mean is not yet resolved</b> — the
/// binding table that names them has not been read — so the action slots are numbered rather than
/// given invented names. The named exceptions are the ones a consumer identifies outright: the debug
/// chords, and <see cref="Jump"/>, which locomotion state 0 tests by name.
/// </para>
/// </remarks>
public enum PlayerInputSlot
{
    /// <summary>Horizontal look delta, scaled by the clamped mouse sensitivity.</summary>
    LookX = 0,

    /// <summary>Vertical look delta, scaled and negated when the profile inverts the look.</summary>
    LookY = 1,

    /// <summary>Action bit <c>0x1</c>.</summary>
    Action2 = 2,

    /// <summary>Action bit <c>0x2</c>.</summary>
    Action3 = 3,

    /// <summary>Action bit <c>0x4</c>.</summary>
    Action4 = 4,

    /// <summary>Action bit <c>0x8</c>.</summary>
    Action5 = 5,

    /// <summary>Action bit <c>0x100</c>.</summary>
    Action6 = 6,

    /// <summary>Action bit <c>0x4000</c>.</summary>
    Action7 = 7,

    /// <summary>Action bit <c>0x200</c>.</summary>
    Action8 = 8,

    /// <summary>Action bit <c>0x400</c>.</summary>
    Action9 = 9,

    /// <summary>Action bit <c>0x800</c>.</summary>
    Action10 = 10,

    /// <summary>
    /// Action bit <c>0x1000</c> — <b>jump</b>. Named from locomotion state 0's per-tick function
    /// (<c>0x4087B0</c>), which tests this slot's rising edge against the previous frame and moves to
    /// state 28, the state that plays <c>"jump_1"</c> and applies the jump speeds.
    /// </summary>
    Jump = 11,

    /// <summary>Action bit <c>0x8000</c>.</summary>
    Action12 = 12,

    /// <summary>Action bit <c>0x80000000</c>, the mask's sign bit.</summary>
    Action13 = 13,

    /// <summary>Action bit <c>0x100000</c>.</summary>
    Action14 = 14,

    /// <summary>Action bit <c>0x200000</c>.</summary>
    Action15 = 15,

    /// <summary>Developer chord, numpad <c>+</c> held (DIK <c>78</c>).</summary>
    DebugNumpadPlusHeld = 16,

    /// <summary>Developer chord, numpad <c>-</c> held (DIK <c>74</c>).</summary>
    DebugNumpadMinusHeld = 17,

    /// <summary>Developer chord, numpad Enter pressed (DIK <c>156</c>).</summary>
    DebugNumpadEnterPressed = 18,

    /// <summary>Developer chord, numpad <c>*</c> pressed (DIK <c>55</c>).</summary>
    DebugNumpadStarPressed = 19,

    /// <summary>
    /// Action bit <c>0x40000</c>. Its rising edge toggles the byte at <c>HumanPlayer+3397</c>.
    /// </summary>
    Action20 = 20,

    /// <summary>Action bit <c>0x2000</c>.</summary>
    Action21 = 21,

    /// <summary>Action bit <c>0x80000</c>.</summary>
    Action22 = 22,

    /// <summary>Action bit <c>0x400000</c>.</summary>
    Action23 = 23,

    /// <summary>Action bit <c>0x800000</c>.</summary>
    Action24 = 24,

    /// <summary>Action bit <c>0x1000000</c>.</summary>
    Action25 = 25,

    /// <summary>Action bit <c>0x2000000</c>.</summary>
    Action26 = 26,

    /// <summary>Action bit <c>0x4000000</c>.</summary>
    Action27 = 27,

    /// <summary>Action bit <c>0x8000000</c>.</summary>
    Action28 = 28,

    /// <summary>Action bit <c>0x10000000</c>.</summary>
    Action29 = 29,

    /// <summary>Action bit <c>0x20000000</c>.</summary>
    Action30 = 30,

    /// <summary>Action bit <c>0x40000000</c>.</summary>
    Action31 = 31,

    /// <summary>Action bit <c>0x20000</c>.</summary>
    Action32 = 32,

    /// <summary>
    /// Developer chord, <c>F</c> pressed (DIK <c>33</c>). Its rising edge enters and leaves locomotion
    /// state 48, the developer state.
    /// </summary>
    DebugFlyPressed = 33,

    /// <summary>Action bit <c>0x10000</c>.</summary>
    Action34 = 34,

    /// <summary>Unassigned; the sampler zeroes the frame and never writes this slot.</summary>
    Unused35 = 35,

    /// <summary>Unassigned.</summary>
    Unused36 = 36,

    /// <summary>Unassigned.</summary>
    Unused37 = 37,

    /// <summary>Unassigned.</summary>
    Unused38 = 38,

    /// <summary>Unassigned.</summary>
    Unused39 = 39,
}
