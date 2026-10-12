namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's locomotion states, by their slot in <c>HumanPlayer_apfState</c>.</summary>
/// <remarks>
/// <para>
/// Slot numbers are the original's (<c>HumanPlayer_InitStateTable</c>, <c>igi2 0x49EE00</c>) because the
/// states transition by number and the animation table is indexed by it. Slots the table leaves empty are
/// not named.
/// </para>
/// </remarks>
public static class Igi2PlayerState
{
    /// <summary>The number of slots in the table.</summary>
    public const int Count = 109;

    /// <summary>Standing still.</summary>
    public const int Stand = 0;

    /// <summary>Turning on the spot.</summary>
    public const int StandTurn = 1;

    /// <summary>Running forward.</summary>
    public const int RunForward = 3;

    /// <summary>Running forward and right.</summary>
    public const int RunForwardRight = 4;

    /// <summary>Running forward and left.</summary>
    public const int RunForwardLeft = 5;

    /// <summary>Running backward.</summary>
    public const int RunBackward = 6;

    /// <summary>Running backward and right.</summary>
    public const int RunBackwardRight = 7;

    /// <summary>Running backward and left.</summary>
    public const int RunBackwardLeft = 8;

    /// <summary>Running right.</summary>
    public const int RunRight = 9;

    /// <summary>Running left.</summary>
    public const int RunLeft = 10;

    /// <summary>Walking forward.</summary>
    public const int WalkForward = 11;

    /// <summary>Walking forward and right.</summary>
    public const int WalkForwardRight = 12;

    /// <summary>Walking forward and left.</summary>
    public const int WalkForwardLeft = 13;

    /// <summary>Walking backward.</summary>
    public const int WalkBackward = 14;

    /// <summary>Walking backward and right.</summary>
    public const int WalkBackwardRight = 15;

    /// <summary>Walking backward and left.</summary>
    public const int WalkBackwardLeft = 16;

    /// <summary>Walking right.</summary>
    public const int WalkRight = 17;

    /// <summary>Walking left.</summary>
    public const int WalkLeft = 18;

    /// <summary>Swimming forward.</summary>
    public const int SwimForward = 19;

    /// <summary>Swimming backward.</summary>
    public const int SwimBackward = 20;

    /// <summary>Treading water; the three swim states share one entry.</summary>
    public const int TreadWater = 21;

    /// <summary>Dying, a second slot with the same entry as <see cref="Die"/>.</summary>
    public const int DieAlternate = 22;

    /// <summary>Dying.</summary>
    public const int Die = 55;

    /// <summary>On a ladder, stepping up and down it (<c>HumanPlayer_InitStateClimbLadder</c>).</summary>
    public const int ClimbLadder = 45;

    /// <summary>Getting on a ladder at its top (<c>HumanPlayer_InitStateClimbLadderGetOnTop</c>).</summary>
    public const int ClimbLadderGetOnTop = 46;

    /// <summary>Getting off a ladder at its top (<c>HumanPlayer_InitStateClimbLadderGetOffTop</c>).</summary>
    public const int ClimbLadderGetOffTop = 47;

    /// <summary>Sliding down a ladder (<c>HumanPlayer_InitStateClimbLadderSlideDown</c>).</summary>
    public const int ClimbLadderSlideDown = 48;

    /// <summary>Climbing over a fence (<c>HumanPlayer_InitStateClimbFence</c>).</summary>
    public const int ClimbFence = 49;

    /// <summary>Waiting for the network, the multiplayer starting state; the body is not moved in it.</summary>
    public const int NetworkWait = 56;

    /// <summary>The debug fly, <c>HumanPlayer_InitStateFly</c> (<c>igi2 0x4B29D0</c>); the body is not moved in it.</summary>
    public const int Fly = 57;

    /// <summary>Crouching still.</summary>
    public const int Crouch = 23;

    /// <summary>Turning crouched.</summary>
    public const int CrouchTurn = 24;

    /// <summary>Walking forward crouched.</summary>
    public const int CrouchWalkForward = 26;

    /// <summary>Walking forward and right crouched.</summary>
    public const int CrouchWalkForwardRight = 27;

    /// <summary>Walking forward and left crouched.</summary>
    public const int CrouchWalkForwardLeft = 28;

    /// <summary>Walking backward crouched.</summary>
    public const int CrouchWalkBackward = 29;

    /// <summary>Walking backward and right crouched.</summary>
    public const int CrouchWalkBackwardRight = 30;

    /// <summary>Walking backward and left crouched.</summary>
    public const int CrouchWalkBackwardLeft = 31;

    /// <summary>Walking right crouched.</summary>
    public const int CrouchWalkRight = 32;

    /// <summary>Walking left crouched.</summary>
    public const int CrouchWalkLeft = 33;

    /// <summary>Jumping.</summary>
    public const int Jump = 36;

    /// <summary>Falling.</summary>
    public const int Fall = 37;

    /// <summary>Picking a door's lock, kneeling at it until the lock gives or the key is let go.</summary>
    public const int ActivateDoor = 38;

    /// <summary>Hacking a terminal, kneeling at it until it is hacked or the key is let go.</summary>
    public const int ActivateTerminal = 40;

    /// <summary>Working at a timed action, a code pad or a padlock, until it finishes or the key is let go.</summary>
    public const int ActivateGenericTba = 42;

    /// <summary>Flinching from a hit.</summary>
    public const int Hit = 54;

    /// <summary>Planting a charge at a C4 target (<c>HumanPlayer_InitStateActivateC4BombTBA</c>, <c>igi2 0x4AEC80</c>).</summary>
    public const int ActivateC4BombTba = 43;

    /// <summary>Lying flat.</summary>
    public const int Flat = 75;

    /// <summary>Crawling.</summary>
    public const int FlatCrawl = 80;

    /// <summary>
    /// The animation slot standing still plays (anim 61): <c>HumanPlayer_InitStateStand</c> plays slot 2, not the
    /// stand state's own slot 0 (anim 60).
    /// </summary>
    public const int StandAnimation = 2;

    /// <summary>The animation slot crouching still plays (anim 101): <c>HumanPlayer_InitStateCrouch</c> plays slot 25.</summary>
    public const int CrouchAnimation = 25;

    /// <summary>The animation slot a jump from a crouch plays (anim 117), <c>HumanPlayer_InitStateJump</c>.</summary>
    public const int CrouchJumpAnimation = 68;
}
