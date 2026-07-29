namespace OpenIGI.Game.Player;

/// <summary>
/// The bits of the locomotion state machine's flag word (<c>Human+756</c>) that the state functions
/// branch on.
/// </summary>
/// <remarks>
/// <para>
/// Only the bits actually tested by a state function are named here. They are read from the branches
/// themselves rather than from any table, so each one is as certain as the transition it drives — and
/// where a bit's <em>meaning</em> is inferred rather than read, the member says so.
/// </para>
/// </remarks>
[Flags]
public enum HumanMotionState : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>
    /// The human is standing on something. Named from <c>HumanPlayer_Update</c>'s <c>"Ground!"</c>
    /// debug line and used by the ground query and the movement integrator alike.
    /// </summary>
    OnGround = 0x4,

    /// <summary>A movement direction. Drives idle → state 8, or state 16 when crouched.</summary>
    Move20 = 0x20,

    /// <summary>A movement direction. Drives idle → state 7, or state 15 when crouched.</summary>
    Move40 = 0x40,

    /// <summary>A movement direction. Drives idle → state 1, or state 9 when crouched.</summary>
    Move80 = 0x80,

    /// <summary>A movement direction. Drives idle → state 4, or state 12 when crouched.</summary>
    Move100 = 0x100,

    /// <summary>Drives idle → state 19, the lowest-priority transition out of idle.</summary>
    Turn = 0x2000,

    /// <summary>
    /// Cleared at the top of every <c>HumanPlayer_Update</c>. Drives idle → state 46, one of the two
    /// self-driving states the animation table leaves at id 0.
    /// </summary>
    Death46 = 0x10000,

    /// <summary>
    /// Drives idle → state 47 — the state whose entry function names <c>"player_death_"</c>, which is
    /// what identifies this pair as the death flags.
    /// </summary>
    Death47 = 0x20000,

    /// <summary>
    /// Selects the crouched variant of every movement transition. Inferred: it is the one bit that
    /// re-targets all four directions at once, and the states it selects carry the crouched animations.
    /// </summary>
    Crouched = 0x40000,
}
