namespace OpenIGI.Game.Player;

/// <summary>
/// The slots of a <see cref="PlayerInputFrame"/>, in the order <c>0x45E060</c> fills them.
/// </summary>
/// <remarks>
/// <para>
/// Most slots are one bit of the engine's action bitmask (<c>dword_BC210C</c>), and the bit each slot
/// carries is recorded below because the sampler writes them out of bit order. Joining that to the
/// binding table at <c>0x536558</c> — 27 records of <c>{ char name[32]; u32 mask; }</c>, read in
/// <see cref="PlayerAction"/> — names every one of them.
/// </para>
/// <para>
/// <b>The join is checked, not assumed.</b> Three consumers agree with it independently: locomotion
/// state 0 tests the four movement bits in an order that matches the directions measured
/// off the animations; <c>0x411000</c> reads slots 23–31 as a nine-way weapon-category
/// selector; and <c>0x40E9C0</c> raises the binoculars on slot 34. <see cref="Jump"/> was named from a
/// consumer before the table was found and the table agrees.
/// </para>
/// </remarks>
public enum PlayerInputSlot
{
    /// <summary>Horizontal look delta, scaled by the clamped mouse sensitivity.</summary>
    LookX = 0,

    /// <summary>Vertical look delta, scaled and negated when the profile inverts the look.</summary>
    LookY = 1,

    /// <summary>Action bit <c>0x1</c> — <see cref="PlayerAction.MoveLeft"/>.</summary>
    MoveLeft = 2,

    /// <summary>Action bit <c>0x2</c> — <see cref="PlayerAction.MoveRight"/>.</summary>
    MoveRight = 3,

    /// <summary>Action bit <c>0x4</c> — <see cref="PlayerAction.MoveUp"/>, forward.</summary>
    MoveUp = 4,

    /// <summary>Action bit <c>0x8</c> — <see cref="PlayerAction.MoveDown"/>, backward.</summary>
    MoveDown = 5,

    /// <summary>
    /// Action bit <c>0x100</c> — <b><see cref="PlayerAction.Fire"/></b>.
    /// </summary>
    /// <remarks>
    /// The weapon machine's prologue maps this to flag <see cref="HumanWeaponFlagWord.Fire"/> and
    /// <c>0x40E9C0</c> tests that flag as a <b>level</b>, with no edge, which is what lets an automatic
    /// weapon keep firing while it is held.
    /// </remarks>
    Fire = 6,

    /// <summary>Action bit <c>0x4000</c> — <see cref="PlayerAction.AlternateFire"/>.</summary>
    AlternateFire = 7,

    /// <summary>Action bit <c>0x200</c> — <see cref="PlayerAction.Reload"/>.</summary>
    Reload = 8,

    /// <summary>Action bit <c>0x400</c> — <see cref="PlayerAction.Activate"/>.</summary>
    Activate = 9,

    /// <summary>Action bit <c>0x800</c> — <see cref="PlayerAction.Crouch"/>.</summary>
    /// <remarks>
    /// Locomotion state 0's ninth branch tests this slot's flag (<c>0x2000</c>) and moves to state 19.
    /// It is <i>not</i> what selects locomotion states 9–16; that is <see cref="ToggleWalkRun"/>.
    /// </remarks>
    Crouch = 10,

    /// <summary>
    /// Action bit <c>0x1000</c> — <see cref="PlayerAction.Jump"/>.
    /// </summary>
    /// <remarks>
    /// Named from locomotion state 0's per-tick (<c>0x4087B0</c>) before the binding table was read: it
    /// tests this slot's rising edge and moves to state 28, which plays <c>"jump_1"</c>. The table
    /// gives Jump the same <c>0x1000</c>, so the two routes agree.
    /// </remarks>
    Jump = 11,

    /// <summary>Action bit <c>0x8000</c> — <see cref="PlayerAction.Peek"/>.</summary>
    Peek = 12,

    /// <summary>
    /// <b>Not an input.</b> The sampler writes the mask's sign bit here, and the binding table binds no
    /// action to <c>0x80000000</c>.
    /// </summary>
    /// <remarks>
    /// <c>0x411000</c>, the weapon-category selector, overwrites this slot with the <i>weapon id</i> it
    /// picked, and <c>sub_40F760</c> reads it back one call later in the same update. It is a one-tick
    /// scratch register living inside the input frame.
    /// </remarks>
    SelectedWeaponScratch = 13,

    /// <summary>Action bit <c>0x100000</c> — <see cref="PlayerAction.ZoomIn"/>.</summary>
    ZoomIn = 14,

    /// <summary>Action bit <c>0x200000</c> — <see cref="PlayerAction.ZoomOut"/>.</summary>
    ZoomOut = 15,

    /// <summary>Developer chord, numpad <c>+</c> held (DIK <c>78</c>).</summary>
    DebugNumpadPlusHeld = 16,

    /// <summary>Developer chord, numpad <c>-</c> held (DIK <c>74</c>).</summary>
    DebugNumpadMinusHeld = 17,

    /// <summary>Developer chord, numpad Enter pressed (DIK <c>156</c>).</summary>
    DebugNumpadEnterPressed = 18,

    /// <summary>Developer chord, numpad <c>*</c> pressed (DIK <c>55</c>).</summary>
    DebugNumpadStarPressed = 19,

    /// <summary>
    /// Action bit <c>0x40000</c> — <see cref="PlayerAction.ToggleWalkRun"/>.
    /// </summary>
    /// <remarks>
    /// Its rising edge toggles the byte at <c>HumanPlayer+3397</c>, which is the locomotion flag word's
    /// <c>0x40000</c> — the bit that selects locomotion states 9–16 over 1–8. An earlier pass read that
    /// bit as crouch; it is walk against run.
    /// </remarks>
    ToggleWalkRun = 20,

    /// <summary>Action bit <c>0x2000</c> — <see cref="PlayerAction.NextWeapon"/>.</summary>
    NextWeapon = 21,

    /// <summary>Action bit <c>0x80000</c> — <see cref="PlayerAction.PreviousWeapon"/>.</summary>
    PreviousWeapon = 22,

    /// <summary>Action bit <c>0x400000</c> — <see cref="PlayerAction.WeaponCategory1"/>.</summary>
    WeaponCategory1 = 23,

    /// <summary>Action bit <c>0x800000</c> — <see cref="PlayerAction.WeaponCategory2"/>.</summary>
    WeaponCategory2 = 24,

    /// <summary>Action bit <c>0x1000000</c> — <see cref="PlayerAction.WeaponCategory3"/>.</summary>
    WeaponCategory3 = 25,

    /// <summary>Action bit <c>0x2000000</c> — <see cref="PlayerAction.WeaponCategory4"/>.</summary>
    WeaponCategory4 = 26,

    /// <summary>Action bit <c>0x4000000</c> — <see cref="PlayerAction.WeaponCategory5"/>.</summary>
    WeaponCategory5 = 27,

    /// <summary>Action bit <c>0x8000000</c> — <see cref="PlayerAction.WeaponCategory6"/>.</summary>
    WeaponCategory6 = 28,

    /// <summary>Action bit <c>0x10000000</c> — <see cref="PlayerAction.WeaponCategory7"/>.</summary>
    WeaponCategory7 = 29,

    /// <summary>Action bit <c>0x20000000</c> — <see cref="PlayerAction.WeaponCategory8"/>.</summary>
    WeaponCategory8 = 30,

    /// <summary>Action bit <c>0x40000000</c> — <see cref="PlayerAction.WeaponCategory9"/>.</summary>
    WeaponCategory9 = 31,

    /// <summary>Action bit <c>0x20000</c> — <see cref="PlayerAction.MapComputer"/>.</summary>
    MapComputer = 32,

    /// <summary>
    /// Developer chord, <c>F</c> pressed (DIK <c>33</c>). Its rising edge enters and leaves locomotion
    /// state 48, the developer state.
    /// </summary>
    DebugFlyPressed = 33,

    /// <summary>Action bit <c>0x10000</c> — <see cref="PlayerAction.Binoculars"/>.</summary>
    Binoculars = 34,

    /// <summary>Action bit <c>0x10</c> — <see cref="PlayerAction.Flashlight"/>.</summary>
    /// <remarks>
    /// Not retail: the original's sampler zeroes the frame and never writes this slot, so the remaster's
    /// flashlight takes it without moving anything the original reads.
    /// </remarks>
    Flashlight = 35,

    /// <summary>Action bit <c>0x20</c> — <see cref="PlayerAction.WeaponWheel"/>.</summary>
    /// <remarks>Not retail, as <see cref="Flashlight"/>: a slot the original's sampler never writes.</remarks>
    WeaponWheel = 36,

    /// <summary>Action bit <c>0x40</c> — <see cref="PlayerAction.Scoreboard"/>.</summary>
    /// <remarks>Not retail, as <see cref="Flashlight"/>: a slot the original's sampler never writes.</remarks>
    Scoreboard = 37,

    /// <summary>Unassigned.</summary>
    Unused38 = 38,

    /// <summary>Unassigned.</summary>
    Unused39 = 39,
}

/// <summary>The join between an input slot and the action its bit binds.</summary>
/// <remarks>
/// Kept as data rather than only in doc comments so a test can check it against
/// <see cref="PlayerAction"/> — every named action must appear exactly once, and every slot's mask
/// must be an action the binding table declares.
/// </remarks>
public static class PlayerInputBindings
{
    /// <summary>Every slot that carries a bound action, paired with that action.</summary>
    /// <remarks>
    /// Twenty-seven entries, one per row of the binding table, and two more for the remaster's
    /// <see cref="PlayerAction.Flashlight"/> and <see cref="PlayerAction.WeaponWheel"/>. <see cref="PlayerInputSlot.LookX"/>,
    /// <see cref="PlayerInputSlot.LookY"/>, the four developer chords,
    /// <see cref="PlayerInputSlot.SelectedWeaponScratch"/> and the three unassigned slots are absent —
    /// none of them is an action a key can be bound to.
    /// </remarks>
    public static IReadOnlyList<(PlayerInputSlot Slot, PlayerAction Action)> All { get; } =
    [
        (PlayerInputSlot.MoveLeft, PlayerAction.MoveLeft),
        (PlayerInputSlot.MoveRight, PlayerAction.MoveRight),
        (PlayerInputSlot.MoveUp, PlayerAction.MoveUp),
        (PlayerInputSlot.MoveDown, PlayerAction.MoveDown),
        (PlayerInputSlot.Fire, PlayerAction.Fire),
        (PlayerInputSlot.AlternateFire, PlayerAction.AlternateFire),
        (PlayerInputSlot.Reload, PlayerAction.Reload),
        (PlayerInputSlot.Activate, PlayerAction.Activate),
        (PlayerInputSlot.Crouch, PlayerAction.Crouch),
        (PlayerInputSlot.Jump, PlayerAction.Jump),
        (PlayerInputSlot.Peek, PlayerAction.Peek),
        (PlayerInputSlot.ZoomIn, PlayerAction.ZoomIn),
        (PlayerInputSlot.ZoomOut, PlayerAction.ZoomOut),
        (PlayerInputSlot.ToggleWalkRun, PlayerAction.ToggleWalkRun),
        (PlayerInputSlot.NextWeapon, PlayerAction.NextWeapon),
        (PlayerInputSlot.PreviousWeapon, PlayerAction.PreviousWeapon),
        (PlayerInputSlot.WeaponCategory1, PlayerAction.WeaponCategory1),
        (PlayerInputSlot.WeaponCategory2, PlayerAction.WeaponCategory2),
        (PlayerInputSlot.WeaponCategory3, PlayerAction.WeaponCategory3),
        (PlayerInputSlot.WeaponCategory4, PlayerAction.WeaponCategory4),
        (PlayerInputSlot.WeaponCategory5, PlayerAction.WeaponCategory5),
        (PlayerInputSlot.WeaponCategory6, PlayerAction.WeaponCategory6),
        (PlayerInputSlot.WeaponCategory7, PlayerAction.WeaponCategory7),
        (PlayerInputSlot.WeaponCategory8, PlayerAction.WeaponCategory8),
        (PlayerInputSlot.WeaponCategory9, PlayerAction.WeaponCategory9),
        (PlayerInputSlot.MapComputer, PlayerAction.MapComputer),
        (PlayerInputSlot.Binoculars, PlayerAction.Binoculars),
        (PlayerInputSlot.Flashlight, PlayerAction.Flashlight),
        (PlayerInputSlot.WeaponWheel, PlayerAction.WeaponWheel),
        (PlayerInputSlot.Scoreboard, PlayerAction.Scoreboard),
    ];

    /// <summary>Gets the action name used by the persisted retail configuration.</summary>
    public static string ConfigName(PlayerAction action) =>
        action == PlayerAction.PreviousWeapon ? "PrevWeapon" : action.ToString();

    /// <summary>Finds the action a slot carries.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="action">The action, when the slot binds one.</param>
    /// <returns><see langword="false"/> for the look axes, the chords and the scratch slot.</returns>
    public static bool TryGetAction(PlayerInputSlot slot, out PlayerAction action)
    {
        foreach ((PlayerInputSlot candidate, PlayerAction bound) in All)
        {
            if (candidate == slot)
            {
                action = bound;
                return true;
            }
        }

        action = default;
        return false;
    }

    /// <summary>Finds the slot an action is sampled into.</summary>
    /// <param name="action">The action.</param>
    /// <param name="slot">The slot the sampler writes it to.</param>
    /// <returns><see langword="false"/> when nothing samples that action.</returns>
    public static bool TryGetSlot(PlayerAction action, out PlayerInputSlot slot)
    {
        foreach ((PlayerInputSlot candidate, PlayerAction bound) in All)
        {
            if (bound == action)
            {
                slot = candidate;
                return true;
            }
        }

        slot = default;
        return false;
    }
}
