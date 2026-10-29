namespace OpenIGI.Game.Player;

/// <summary>
/// The player's body state machine for standing, moving, crouching, jumping and falling: states 0 to 29 of the 49 in
/// <c>gBodyStates</c> (<c>0x5385B4</c>), each state's per-tick and what its entry does.
/// </summary>
/// <remarks>
/// <para>
/// <c>StateMachine_Update</c> (<c>0x4EC1E0</c>) builds the input word and runs the state's per-tick. A per-tick that moves on
/// calls the target's entry, and the entry only installs the target's own per-tick (<c>0x408770</c> and its siblings): so a
/// tick makes at most one transition, and a diagonal pressed from a standstill is reached a tick after the straight move that
/// leads to it. The tests below are the per-ticks' own, in their order — <see cref="HumanLocomotionGraph"/> lists the same
/// targets, extracted from the code.
/// </para>
/// <para>
/// The states the other systems own — the uses, the ladder, the wire, the zipline, lean, the computer, hit and death — are
/// left to them: from any of those this machine reads the body as state 0 would.
/// </para>
/// </remarks>
public static class HumanBodyStateMachine
{
    /// <summary>The stand, state 0.</summary>
    public const int Stand = 0;

    /// <summary>The crouch, state 19.</summary>
    public const int Crouch = 19;

    /// <summary>The jump, state 28.</summary>
    public const int Jump = HumanLocomotionGraph.Jump;

    /// <summary>The fall, state 29.</summary>
    public const int Fall = HumanLocomotionGraph.Falling;

    /// <summary>The speed the jump's clip plays at, <c>Anim_SetSpeed(0.4)</c> in its entry (<c>0x40BA70</c>).</summary>
    public const float JumpClipSpeed = 0.4f;

    /// <summary>The fade most entries play their clip with, <c>Human_PlayAnimSlot(.., 0.2)</c>.</summary>
    public const float BlendSeconds = 0.2f;

    private const HumanMotionState Ground = HumanMotionState.OnGround;

    // The two strafe bits by value, as the per-ticks test them: 0x40 is MoveRight, 0x20 MoveLeft.
    private const HumanMotionState Strafe40 = HumanMotionState.MoveRight;
    private const HumanMotionState Strafe20 = HumanMotionState.MoveLeft;
    private const HumanMotionState Forward = HumanMotionState.MoveForward;
    private const HumanMotionState Back = HumanMotionState.MoveBackward;
    private const HumanMotionState CrouchKey = HumanMotionState.Crouch;
    private const HumanMotionState Walking = HumanMotionState.Walking;

    // A crouched body stays down while the key is held or a roof is low over it (0x2010).
    private const HumanMotionState StaysDown = HumanMotionState.Crouch | HumanMotionState.UnderRoof;

    /// <summary>The state this tick's per-tick moves the body to, or the one it is in.</summary>
    /// <param name="state">The state the body is in.</param>
    /// <param name="input">The input word, <c>0x4105C0</c>'s.</param>
    /// <param name="jumpPressed">Whether jump went down this tick, the per-ticks' edge on input slot 11.</param>
    /// <returns>The state after this tick's per-tick.</returns>
    public static int Next(int state, HumanMotionState input, bool jumpPressed)
    {
        bool has(HumanMotionState bit) => (input & bit) != 0;

        // Losing the ground comes first in every state but the two airborne ones, which wait for it (0x4087B0 and on). The two
        // death bits that follow it in the original are never raised here: the player's death is the death system's.
        if (!has(Ground) && state != Jump)
        {
            return Fall;
        }

        switch (state)
        {
            // 0x40BBD0 and 0x40BE40: nothing until the ground is under the feet, then into the gait the keys hold.
            case Jump:
                return !has(Ground) ? Jump : Landing(input);
            case Fall:
                return Stand;
            case >= 1 and <= 8:
                return Gait(state, input, jumpPressed, walking: false);
            case >= 9 and <= 16:
                return Gait(state, input, jumpPressed, walking: true);
            case Crouch:
                return has(Strafe40) ? 26
                    : has(Strafe20) ? 27
                    : !has(StaysDown) ? Stand
                    : has(Forward) ? 20
                    : has(Back) ? 23
                    : Crouch;
            case >= 20 and <= 27:
                return CrouchMove(state, input);
            default:
                return FromStand(input, jumpPressed);
        }
    }

    /// <summary>Whether a state's entry sets the crouch byte (<c>Human+829</c>): the crouched ones, 19 to 27.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> for a crouched state.</returns>
    public static bool Crouches(int state) => state is >= Crouch and <= 27;

    /// <summary>How long a state's entry fades its clip in, given the state it came from.</summary>
    /// <param name="state">The state entered.</param>
    /// <param name="previous">The state left.</param>
    /// <returns>Seconds of fade.</returns>
    /// <remarks>
    /// The jump and the fall cut (<c>0x40BA70</c>, <c>0x40BDB0</c>). The four straight running entries cut too when they follow
    /// the jump, so the landing stride is not mixed with the airborne pose (<c>0x408A40</c>'s test of the previous state).
    /// </remarks>
    public static float BlendFor(int state, int previous) => state switch
    {
        Jump or Fall => 0f,
        1 or 4 or 7 or 8 when previous == Jump => 0f,
        _ => BlendSeconds,
    };

    /// <summary>Whether a state's entry keeps the outgoing clip's phase, by the state it came from.</summary>
    /// <param name="state">The state entered.</param>
    /// <param name="previous">The state left.</param>
    /// <returns><see langword="true"/> when the new clip starts as far through as the old one was.</returns>
    /// <remarks>
    /// Each entry names its own predecessors, and not symmetrically: state 3 keeps the step it came from (<c>0x408DF0</c>)
    /// but its mirror, state 2, never does (<c>0x408C30</c>).
    /// </remarks>
    public static bool KeepsPhase(int state, int previous) => state switch
    {
        3 => previous is 1 or 11 or 22,
        4 => previous is 5 or 6 or 12 or 23,
        5 => previous is 4 or 13 or 24,
        6 => previous is 4 or 14 or 25,
        9 => previous is 1 or 10 or 11 or 20,
        10 => previous is 2 or 9 or 21,
        11 => previous is 3 or 9 or 22,
        12 => previous is 4 or 13 or 14 or 23,
        13 => previous is 5 or 12 or 24,
        14 => previous is 6 or 12 or 25,
        15 => previous == 26,
        16 => previous == 27,
        20 => previous == 1,
        21 => previous is 2 or 20 or 26,
        22 => previous is 3 or 20 or 27,
        23 => previous == 12,
        24 => previous is 5 or 13 or 23,
        25 => previous is 6 or 14 or 23,
        26 => previous == 7,
        27 => previous == 8,
        _ => false,
    };

    // 0x4087B0: the walking gait first, then the running one, then the jump, then the crouch.
    private static int FromStand(HumanMotionState input, bool jumpPressed)
    {
        bool has(HumanMotionState bit) => (input & bit) != 0;
        bool walking = has(Walking);
        return has(Forward) ? (walking ? 9 : 1)
            : has(Back) ? (walking ? 12 : 4)
            : has(Strafe40) ? (walking ? 15 : 7)
            : has(Strafe20) ? (walking ? 16 : 8)
            : jumpPressed ? Jump
            : has(CrouchKey) ? Crouch
            : Stand;
    }

    // The sixteen standing gait states, 0x408AA0 to 0x40A870: the toggle swaps running and walking, the jump edge leaves, and
    // each then follows its own keys. A walking diagonal is not always reached from walking: 9 hands a strafe to the RUNNING
    // diagonals and 13 hands a dropped back key to the running strafe -- the original's tables, kept.
    private static int Gait(int state, HumanMotionState input, bool jumpPressed, bool walking)
    {
        bool has(HumanMotionState bit) => (input & bit) != 0;
        if (has(Walking) != walking)
        {
            return walking ? state - 8 : state + 8;
        }

        if (jumpPressed)
        {
            return Jump;
        }

        return state switch
        {
            1 => has(Strafe40) ? 2 : has(Strafe20) ? 3 : !has(Forward) ? Stand : has(CrouchKey) ? 20 : 1,
            2 => !has(Forward) ? 7 : !has(Strafe40) ? 1 : has(CrouchKey) ? 21 : 2,
            3 => !has(Forward) ? 8 : !has(Strafe20) ? 1 : has(CrouchKey) ? 22 : 3,
            4 => has(Strafe40) ? 5 : has(Strafe20) ? 6 : !has(Back) ? Stand : has(CrouchKey) ? 23 : 4,
            5 => !has(Back) ? 7 : !has(Strafe40) ? 4 : has(CrouchKey) ? 24 : 5,
            6 => !has(Back) ? 8 : !has(Strafe20) ? 4 : has(CrouchKey) ? 25 : 6,
            7 => !has(Strafe40) ? Stand : has(Forward) ? 2 : has(Back) ? 5 : has(CrouchKey) ? 26 : 7,
            8 => !has(Strafe20) ? Stand : has(Forward) ? 3 : has(Back) ? 6 : has(CrouchKey) ? 27 : 8,
            9 => has(Strafe40) ? 2 : has(Strafe20) ? 3 : !has(Forward) ? Stand : has(CrouchKey) ? 20 : 9,
            10 => !has(Forward) ? 15 : !has(Strafe40) ? 9 : has(CrouchKey) ? 21 : 10,
            11 => !has(Forward) ? 16 : !has(Strafe20) ? 9 : has(CrouchKey) ? 22 : 11,
            12 => has(Strafe40) ? 13 : has(Strafe20) ? 14 : !has(Back) ? Stand : has(CrouchKey) ? 23 : 12,
            13 => !has(Back) ? 7 : !has(Strafe40) ? 12 : has(CrouchKey) ? 23 : 13,
            14 => !has(Back) ? 15 : !has(Strafe20) ? 12 : has(CrouchKey) ? 23 : 14,
            15 => !has(Strafe40) ? Stand : has(Forward) ? 10 : has(Back) ? 13 : has(CrouchKey) ? 26 : 15,
            _ => !has(Strafe20) ? Stand : has(Forward) ? 11 : has(Back) ? 14 : has(CrouchKey) ? 27 : 16,
        };
    }

    // The crouched moves, 0x40AD20 and 0x40AEF0 to 0x40B920. Each stands back up into the gait the toggle picks once the key is
    // up and no roof holds it down; the forward one checks that before its keys, the others after.
    private static int CrouchMove(int state, HumanMotionState input)
    {
        bool has(HumanMotionState bit) => (input & bit) != 0;
        int up(int run) => has(StaysDown) ? state : has(Walking) ? run + 8 : run;

        return state switch
        {
            20 => has(Strafe40) ? 21 : has(Strafe20) ? 22 : !has(Forward) ? Crouch : !has(StaysDown) ? (has(Walking) ? 9 : 1) : 20,
            21 => !has(Strafe40) ? 20 : !has(Forward) ? 26 : up(2),
            22 => !has(Strafe20) ? 20 : !has(Forward) ? 27 : up(3),
            23 => has(Strafe40) ? 24 : has(Strafe20) ? 25 : !has(Back) ? Crouch : up(4),
            24 => !has(Back) ? 26 : !has(Strafe40) ? 23 : up(5),
            25 => !has(Back) ? 27 : !has(Strafe20) ? 23 : up(6),
            26 => !has(Strafe40) ? Crouch : has(Forward) ? 21 : has(Back) ? 24 : up(7),
            _ => !has(Strafe20) ? Crouch : has(Forward) ? 22 : has(Back) ? 25 : up(8),
        };
    }

    // 0x40BBD0's landing: the walking or running stride the keys hold, or the stand.
    private static int Landing(HumanMotionState input)
    {
        bool has(HumanMotionState bit) => (input & bit) != 0;
        bool walking = has(Walking);
        return has(Forward) ? (walking ? 9 : 1)
            : has(Back) ? (walking ? 12 : 4)
            : has(Strafe40) ? (walking ? 15 : 7)
            : has(Strafe20) ? (walking ? 16 : 8)
            : Stand;
    }
}
