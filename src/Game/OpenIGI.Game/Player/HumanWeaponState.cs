namespace OpenIGI.Game.Player;

/// <summary>
/// The thirteen states of the weapon-action machine at <c>Human+3320</c>.
/// </summary>
/// <remarks>
/// <para>
/// The second of the two machines <c>HumanPlayer</c>'s constructor registers through <c>0x4EC190</c>;
/// the first is the 49-state locomotion machine (<see cref="HumanLocomotionStates"/>). Its function
/// table is <c>off_538678</c> and its prologue <c>0x410700</c>, and it registers <b>no triggers</b> —
/// there are no double-tap gestures on the weapon side.
/// </para>
/// <para>
/// <b>Thirteen, not more.</b> The table runs from <c>0x538678</c> to <c>0x5386AB</c> and then meets the
/// locomotion machine's trigger step arrays at <c>0x5386B0</c>, exactly as the locomotion table's own
/// boundary fixed its count. Entry 13 onward decode as <c>{polarity, tolerance, mask}</c> step records
/// rather than as code addresses.
/// </para>
/// </remarks>
public enum HumanWeaponState
{
    /// <summary>
    /// Suspended — plays <see cref="Weapons.WeaponAction.None"/> and waits for <c>Human+3396</c> to
    /// clear.
    /// </summary>
    /// <remarks><c>0x40E960</c> / <c>0x40E980</c>.</remarks>
    Suspended = 0,

    /// <summary>
    /// Ready — the hub. Every other state is entered from here and returns here.
    /// </summary>
    /// <remarks><c>0x40E9B0</c> / <c>0x40E9C0</c>. The initial state the constructor registers.</remarks>
    Ready = 1,

    /// <summary>Firing an ordinary gun.</summary>
    /// <remarks>
    /// <c>0x40EF60</c> / <c>0x40EF70</c>. Every weapon category the dispatch does not name explicitly
    /// lands here, which in the retail roster is every firearm.
    /// </remarks>
    Firing = 2,

    /// <summary>Reloading.</summary>
    /// <remarks><c>0x40F170</c> / <c>0x40F180</c>.</remarks>
    Reloading = 3,

    /// <summary>Changing weapon, from the pending id at <c>Human+3404</c>.</summary>
    /// <remarks>
    /// <c>0x40F230</c> / <c>0x40F280</c>. Its per-tick is what writes <c>Human+1220</c>, the array
    /// every consumer reads the held weapon out of.
    /// </remarks>
    ChangingWeapon = 4,

    /// <summary>Leaning out of cover; also pushes the body into locomotion state 44.</summary>
    /// <remarks><c>0x40F370</c> / <c>0x40F3F0</c>.</remarks>
    Peeking = 5,

    /// <summary>Alternate fire — in the retail roster, only the M16A2's M203 launcher.</summary>
    /// <remarks><c>0x40F4A0</c> / <c>0x40FD70</c>.</remarks>
    AlternateFire = 6,

    /// <summary>Throwing: the grenade's charge, held while the button is down, then the release.</summary>
    /// <remarks><c>0x40F4F0</c> / <c>0x40F590</c>.</remarks>
    Throwing = 7,

    /// <summary>Placing a proximity mine.</summary>
    /// <remarks><c>0x40F930</c> / <c>0x40F950</c>.</remarks>
    Placing = 8,

    /// <summary>Striking — the knife, and any other weapon of category 3.</summary>
    /// <remarks><c>0x40FA60</c> / <c>0x40FA80</c>.</remarks>
    Striking = 9,

    /// <summary>Using the medipack.</summary>
    /// <remarks><c>0x40FB30</c> / <c>0x40FB90</c>.</remarks>
    Medipack = 10,

    /// <summary>Reading the map computer; also pushes the body into locomotion state 45.</summary>
    /// <remarks><c>0x40FDA0</c> / <c>0x40FDF0</c>.</remarks>
    MapComputer = 11,

    /// <summary>Dry fire.</summary>
    /// <remarks>
    /// <c>0x40F0D0</c> / <c>0x40F100</c>. Named from its own entry, which plays the sound
    /// <c>guns_dry_1</c> — not inferred.
    /// </remarks>
    DryFire = 12,
}
