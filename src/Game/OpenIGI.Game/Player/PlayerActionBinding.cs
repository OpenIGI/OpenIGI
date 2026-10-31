namespace OpenIGI.Game.Player;

/// <summary>
/// The actions a key can be bound to, and the bit each sets in the input mask.
/// </summary>
/// <remarks>
/// <para>
/// Read from the binding table at <c>0x536558</c> — <b>27</b> records of 36 bytes, each a name and
/// its mask, which <c>0x402FF0</c> walks. This had been unresolved: the action slots
/// were numbered rather than named because the table that names them had not been found.
/// </para>
/// <para>
/// <b>It cross-checks the one independent identification.</b> Jump was named from locomotion state
/// zero's per-tick, which tests it by name and moves to the state that plays <c>jump_1</c>; the table
/// gives Jump the same <c>0x1000</c>. Two unrelated routes to the same bit.
/// </para>
/// <para>
/// The movement pair is <b>MoveUp</b> and <b>MoveDown</b> rather than forward and back — named for the
/// screen rather than the world, as the era's key-binding dialogs were.
/// </para>
/// </remarks>
public enum PlayerAction
{
    /// <summary>Strafe left.</summary>
    MoveLeft = 0x1,

    /// <summary>Strafe right.</summary>
    MoveRight = 0x2,

    /// <summary>Move forward — up the screen.</summary>
    MoveUp = 0x4,

    /// <summary>Move backward — down the screen.</summary>
    MoveDown = 0x8,

    /// <summary>Switch the flashlight on or off.</summary>
    /// <remarks>
    /// <b>Not retail.</b> The original binds nothing to <c>0x10</c> — its table jumps from the movement
    /// nibble to <c>0x100</c> — so the remaster's one extension action takes the first bit the binding
    /// table leaves free, and no retail row can ever collide with it.
    /// </remarks>
    Flashlight = 0x10,

    /// <summary>Hold the weapon wheel open; let go to take the weapon pointed at.</summary>
    /// <remarks><b>Not retail</b>, as <see cref="Flashlight"/>: the remaster's wheel (<see cref="WeaponWheel"/>) takes the next free bit.</remarks>
    WeaponWheel = 0x20,

    /// <summary>Hold a multiplayer session's scoreboard open.</summary>
    /// <remarks><b>Not retail</b>, as <see cref="Flashlight"/>: the next free bit. The host reads it; the player's update does not.</remarks>
    Scoreboard = 0x40,

    /// <summary>Fire the held weapon.</summary>
    Fire = 0x100,

    /// <summary>Reload.</summary>
    Reload = 0x200,

    /// <summary>Use whatever is in front.</summary>
    Activate = 0x400,

    /// <summary>Crouch.</summary>
    Crouch = 0x800,

    /// <summary>Jump.</summary>
    /// <remarks>The bit identified from the state machine, confirmed by the table.</remarks>
    Jump = 0x1000,

    /// <summary>Cycle to the next weapon.</summary>
    NextWeapon = 0x2000,

    /// <summary>The weapon's alternate fire.</summary>
    AlternateFire = 0x4000,

    /// <summary>Lean out from cover.</summary>
    Peek = 0x8000,

    /// <summary>Raise the binoculars.</summary>
    Binoculars = 0x10000,

    /// <summary>Open the map computer.</summary>
    MapComputer = 0x20000,

    /// <summary>Swap between walking and running.</summary>
    ToggleWalkRun = 0x40000,

    /// <summary>Cycle to the previous weapon.</summary>
    PreviousWeapon = 0x80000,

    /// <summary>Zoom in.</summary>
    ZoomIn = 0x100000,

    /// <summary>Zoom out.</summary>
    ZoomOut = 0x200000,

    /// <summary>Select the first weapon category.</summary>
    WeaponCategory1 = 0x400000,

    /// <summary>Select the second weapon category.</summary>
    WeaponCategory2 = 0x800000,

    /// <summary>Select the third weapon category.</summary>
    WeaponCategory3 = 0x1000000,

    /// <summary>Select the fourth weapon category.</summary>
    WeaponCategory4 = 0x2000000,

    /// <summary>Select the fifth weapon category.</summary>
    WeaponCategory5 = 0x4000000,

    /// <summary>Select the sixth weapon category.</summary>
    WeaponCategory6 = 0x8000000,

    /// <summary>Select the seventh weapon category.</summary>
    WeaponCategory7 = 0x10000000,

    /// <summary>Select the eighth weapon category.</summary>
    WeaponCategory8 = 0x20000000,

    /// <summary>Select the ninth weapon category.</summary>
    /// <remarks>
    /// There are <b>nine</b>, not eight — the table's last named row. Worth checking rather than
    /// assuming the run stopped at a round number.
    /// </remarks>
    WeaponCategory9 = 0x40000000,
}
