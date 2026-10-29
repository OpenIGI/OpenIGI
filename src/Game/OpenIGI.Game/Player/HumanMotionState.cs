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
/// <remarks>
/// <para>
/// <b>The four movement flags are the action mask's own bits shifted up five.</b> The binding table
/// (<see cref="PlayerAction"/>) gives <c>MoveLeft</c> 0x1, <c>MoveRight</c> 0x2, <c>MoveUp</c> 0x4 and
/// <c>MoveDown</c> 0x8; here they are 0x20, 0x40, 0x80 and 0x100, in the same order.
/// </para>
/// <para>
/// Which is which was settled by the animations rather than by the names: state 0's per-tick
/// (<c>0x4087B0</c>) sends 0x80 to state 1, 0x100 to state 4, 0x40 to state 7 and 0x20 to state 8, and
/// sampling those states' root-motion tracks shows them travelling +Y, −Y, +X and −X — forward,
/// backward, right and left.
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

    /// <summary>
    /// A ceiling within 1.8 m of the feet, the roof bit the ground probe raises (<c>0x462390</c>): a crouched body cannot
    /// stand up under it.
    /// </summary>
    UnderRoof = 0x10,

    /// <summary>Strafing left — the action mask's <c>MoveLeft</c>, shifted up five.</summary>
    MoveLeft = 0x20,

    /// <summary>Strafing right.</summary>
    MoveRight = 0x40,

    /// <summary>Moving forward.</summary>
    MoveForward = 0x80,

    /// <summary>Moving backward.</summary>
    MoveBackward = 0x100,

    /// <summary>
    /// Drives idle → the stationary crouch state, 19, as the lowest-priority transition out of idle.
    /// </summary>
    Crouch = 0x2000,

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
    /// Selects the walking variant of every standing movement transition. Its input action is
    /// <see cref="PlayerAction.ToggleWalkRun"/>: set selects states 9–16, clear selects states 1–8.
    /// Crouching instead takes the separate state 19–27 path.
    /// </summary>
    Walking = 0x40000,
}
